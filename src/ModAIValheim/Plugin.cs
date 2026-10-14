using System;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ModAIValheim.Companion;
using UnityEngine;

namespace ModAIValheim
{
    /// <summary>
    /// Plugin principal do ModIA.
    /// Fase 2.1: invoca/dispensa o companheiro NPC (que segue o jogador) por tecla.
    /// Fase 2.3b: o companheiro usa o corpo do jogador ('Player'), então armadura de
    /// peito/pernas renderiza e o NPC parece um viking.
    /// Fase 2.4a: modo de combate (Passive/Defensive/Aggressive) com seleção de alvo e revide.
    /// Fase 2.4b: coleta de recursos (madeira/pedra/itens) por comando e tecla.
    /// Fase 2.6a: hover do companheiro com o nome e as ações disponíveis (teclas reais da config).
    /// Fase 2.6b: menu do companheiro dentro do jogo (painel de madeira com status, aparência,
    /// combate, coleta e teclas), aberto por tecla quando o jogador está perto do companheiro.
    /// Fase 2.7: persistência do companheiro — ao dispensar (H), ao morrer e no fim do mundo o NPC
    /// é fotografado; a próxima invocação (F1) devolve o mesmo companheiro (itens, equipamento,
    /// aparência e modo), com opção de guardar o estado em disco entre sessões.
    /// Fase 2.8: coleta ampliada — lista curada de materiais (madeira, pedra, minérios, colhíveis),
    /// dropdown de recurso no menu, mira na superfície das rochas e fornecimento automático da
    /// ferramenta de coleta (machado/picareta) quando falta.
    /// Fase 2.9: correções de coleta — espera os drops nascerem antes de encerrar a tarefa (estado
    /// dedicado de coleta de drops), segue a cópia "rachada" de uma rocha no mesmo ponto, mira a
    /// tora da árvore que acabou de cair e fornece a ferramenta pelo tier exigido pelo nó.
    /// Fase 2.10: correções de movimento/combate da auditoria — proteção contra queda (#2), portas
    /// no caminho (#5), bloqueio/parry (#6) e filtro de alvo passivo/neutro (#14).
    /// Fase 2.11: correção do loop da coleta — a seleção de nó passou a respeitar a mesma "coleira"
    /// do jogador que a guarda de distância, o nó largado por "jogador se afastou" não é re-escolhido
    /// no mesmo tick (o companheiro volta a seguir) e o log de nó só sai quando o evento muda.
    /// Fase 2.12: o companheiro cata os drops das criaturas que ele mesmo abateu (couro, penas,
    /// ossos...) — patch observador em Character.OnDeath enfileira o ponto do abate e a camada de
    /// loot vai ao corpo, espera os itens saírem do ragdoll e pega só o que aquele abate gerou.
    /// Fase 2.13: o companheiro vira MEMBRO DA EQUIPE — come sozinho a comida do próprio inventário
    /// (vida máxima, regeneração e efeito de status, com os tempos de queima do jogo), tem
    /// resistência própria que importa (correr/nadar/atacar/bloquear gastam; sem stamina ele anda em
    /// vez de correr) e as habilidades evoluem com o uso (o patch de Character.RaiseSkill passou a
    /// entregar o ganho para o companheiro em vez de só silenciar o aviso do jogo).
    /// Fase 2.14: PESO, DEPÓSITO EM BAÚ e TOOL KEEPER — o companheiro respeita um teto de peso
    /// (Companheiro/PesoMaximo), e ao encher NÃO encerra a tarefa: leva a carga ao baú mais próximo,
    /// deposita e volta a coletar (ou pausa e avisa, se não houver baú). O tool keeper busca num baú
    /// a ferramenta que faltar (além de criar, como já fazia) e devolve a emprestada quando não usa
    /// mais.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "modia.companion";
        public const string PluginName = "ModIA Companion";
        public const string PluginVersion = "0.4.2";

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        /// <summary>
        /// <c>Geral/LogDetalhado</c>: liga os logs de desenvolvimento (hoje, o diagnóstico por
        /// categoria da varredura de coleta). Exposto como estático porque o scanner roda fora da
        /// instância do plugin.
        /// </summary>
        internal static bool LogDetalhado => _logDetalhado != null && _logDetalhado.Value;

        private static ConfigEntry<bool> _logDetalhado;
        private ConfigEntry<KeyCode> _spawnKey;
        private ConfigEntry<KeyCode> _dismissKey;
        private ConfigEntry<bool> _equipLoadoutOnSpawn;
        private ConfigEntry<string> _weaponPrefab;
        private ConfigEntry<string> _armorPrefabs;
        private ConfigEntry<bool> _appearanceEnabled;
        private ConfigEntry<string> _hairPrefab;
        private ConfigEntry<string> _beardPrefab;
        private ConfigEntry<string> _skinColor;
        private ConfigEntry<string> _hairColor;
        private ConfigEntry<CompanionMode> _combatMode;
        private ConfigEntry<KeyCode> _combatModeKey;
        private ConfigEntry<float> _aggressiveRadius;
        private ConfigEntry<float> _defensiveRadius;
        private ConfigEntry<float> _combatLeashDistance;
        private ConfigEntry<bool> _attackNeutralAnimals;
        private ConfigEntry<float> _combatScanInterval;
        private ConfigEntry<bool> _blockAttacks;
        private ConfigEntry<float> _blockRadius;
        private ConfigEntry<float> _blockHold;

        // Movimento do companheiro (Fase 2.10): protecao contra queda e portas.
        private ConfigEntry<bool> _ledgeProtection;
        private ConfigEntry<float> _maxDrop;
        private ConfigEntry<float> _ledgeProbeDistance;
        private ConfigEntry<bool> _openDoors;

        // Persistencia do companheiro entre sessoes (Fase 2.7).
        private ConfigEntry<bool> _persistBetweenSessions;

        // Coleta de recursos (Fase 2.4b).
        private ConfigEntry<KeyCode> _harvestKey;
        private ConfigEntry<string> _harvestDefaultResource;
        private ConfigEntry<int> _harvestDefaultAmount;
        private ConfigEntry<float> _harvestScanRadius;
        private ConfigEntry<float> _harvestScanInterval;
        private ConfigEntry<bool> _harvestPauseOnCombat;
        private ConfigEntry<bool> _harvestCollectDrops;
        private ConfigEntry<string> _harvestLayers;
        private ConfigEntry<bool> _harvestProvideTools;
        private ConfigEntry<string> _harvestProvidedTools;

        // Peso, depósito em baú e tool keeper (Fase 2.14).
        private ConfigEntry<float> _maxWeight;
        private ConfigEntry<bool> _depositWhenFull;
        private ConfigEntry<float> _chestSearchRadius;
        private ConfigEntry<bool> _storeUnusedTool;

        // Loot de criatura abatida pelo companheiro (Fase 2.12).
        private ConfigEntry<bool> _lootEnabled;
        private ConfigEntry<float> _lootRadius;
        private ConfigEntry<float> _lootMaxDistance;
        private ConfigEntry<int> _lootQueueLimit;
        private ConfigEntry<float> _lootWaitSeconds;
        private ConfigEntry<float> _lootMaxSeconds;
        private ConfigEntry<float> _lootMaxWeight;

        // Comida, resistencia (stamina) e habilidades do companheiro (Fase 2.13).
        private ConfigEntry<bool> _autoEat;
        private ConfigEntry<float> _eatHealthThreshold;
        private ConfigEntry<float> _eatStaminaThreshold;
        private ConfigEntry<float> _eatCheckInterval;
        private ConfigEntry<bool> _staminaEnabled;
        private ConfigEntry<float> _staminaBase;
        private ConfigEntry<float> _staminaRunDrain;
        private ConfigEntry<float> _staminaSwimDrain;
        private ConfigEntry<float> _staminaRegenRate;
        private ConfigEntry<float> _staminaRegenDelay;
        private ConfigEntry<bool> _skillsEnabled;
        private ConfigEntry<bool> _skillsAnnounce;
        private ConfigEntry<bool> _skillsLoseOnDeath;
        private ConfigEntry<float> _skillsDeathLossFactor;

        // Interface (Fase 2.6a).
        private ConfigEntry<bool> _hoverHints;

        // Menu do mod (Fase 2.6b).
        private ConfigEntry<KeyCode> _menuKey;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            _logDetalhado = Config.Bind(
                "Geral",
                "LogDetalhado",
                false,
                "Ativa logs detalhados do ModIA.");

            _spawnKey = Config.Bind(
                "Teclas",
                "SpawnKey",
                KeyCode.F1,
                "Tecla para invocar o companheiro (ignorada se ele já estiver no mundo). " +
                "Padrao F1 (a tecla G e usada pelo menu radial do jogo).");

            _dismissKey = Config.Bind(
                "Teclas",
                "DismissKey",
                KeyCode.H,
                "Tecla para dispensar (despawnar) o companheiro.");

            _equipLoadoutOnSpawn = Config.Bind(
                "Companheiro",
                "EquiparLoadoutNoSpawn",
                true,
                "Equipa o loadout padrao (arma + armaduras) assim que o companheiro e invocado.");

            _weaponPrefab = Config.Bind(
                "Companheiro",
                "WeaponPrefab",
                "Club",
                "Prefab vanilla da arma do companheiro (ex.: Club, AxeStone, SwordBronze, BowFineWood). " +
                "Deixe vazio para spawnar sem arma. Prefab inexistente so gera aviso no log.");

            _armorPrefabs = Config.Bind(
                "Companheiro",
                "ArmorSetPrefabs",
                "ArmorLeatherChest,ArmorLeatherLegs",
                "Prefabs de armadura equipados no spawn, separados por virgula " +
                "(ex.: ArmorLeatherChest,ArmorLeatherLegs,HelmetLeather). " +
                "O slot e detectado pelo tipo do item. Vazio desliga.");

            // Persistencia do companheiro (Fase 2.7).
            _persistBetweenSessions = Config.Bind(
                "Companheiro",
                "PersistirEntreSessoes",
                true,
                "Guarda o estado do companheiro (itens, equipamento, aparencia e modo) em " +
                "BepInEx/config/modia.companion.state.json, para ele voltar igual mesmo depois de " +
                "FECHAR E ABRIR O JOGO. O estado em memoria (dismiss H -> invocar F1) funciona " +
                "sempre, independente desta opcao. Desligada, cada sessao comeca com o loadout " +
                "padrao. Use 'modia_state clear' para esquecer o companheiro guardado.");

            _maxWeight = Config.Bind(
                "Companheiro",
                "PesoMaximo",
                300f,
                "Peso maximo em kg que o companheiro aceita carregar (0 = sem limite). Antes de " +
                "catar/coletar ele soma o peso do item ao que ja carrega e NUNCA passa do teto; ao " +
                "estourar, ele leva a carga ao bau mais proximo, deposita e volta a tarefa (ver " +
                "Coleta/DepositarAoEncher). O jogo nao tem limite de peso para personagens, entao " +
                "este teto e uma escolha de balanceamento do mod (padrao 300 kg, perto do que um " +
                "jogador carrega com a armadura de ferro).");

            // Aparencia padrão do corpo de viking (Fase 2.3b).
            _appearanceEnabled = Config.Bind(
                "Aparencia",
                "AparenciaPadrao",
                true,
                "Aplica cabelo/barba/cor de pele padrao no companheiro ao spawnar " +
                "(corpo base e o do jogador, 'Player').");

            _hairPrefab = Config.Bind(
                "Aparencia",
                "CabeloPrefab",
                "",
                "Prefab de cabelo do companheiro (ex.: Hair1 ate Hair38). " +
                "Vazio = primeiro cabelo disponivel no jogo; 'none' = careca.");

            _beardPrefab = Config.Bind(
                "Aparencia",
                "BarbaPrefab",
                "",
                "Prefab de barba do companheiro (ex.: Beard1 ate Beard26). " +
                "Vazio = primeira barba disponivel no jogo; 'none' = sem barba.");

            _skinColor = Config.Bind(
                "Aparencia",
                "CorPele",
                "0.92;0.82;0.74",
                "Cor de pele em r;g;b de 0 a 1, ponto decimal (ex.: 0.92;0.82;0.74). " +
                "1;1;1 = sem tingimento (o tom mais claro que o jogo usa quando nao ha dado).");

            _hairColor = Config.Bind(
                "Aparencia",
                "CorCabelo",
                "0.62;0.45;0.30",
                "Cor do cabelo/barba em r;g;b de 0 a 1, ponto decimal (ex.: 0.62;0.45;0.30).");

            // Combate (Fase 2.4a).
            _combatMode = Config.Bind(
                "Combate",
                "Modo",
                CompanionMode.Defensive,
                "Modo de combate do companheiro. " +
                "Passive = nunca ataca nem revida; " +
                "Defensive = revida quem agredir o companheiro ou o jogador dono e ataca quem estiver " +
                "cacando um dos dois; " +
                "Aggressive = ataca qualquer criatura hostil dentro do RaioAgressivo. " +
                "Cicla em jogo pela tecla de combate; tambem aceita modia_mode.");

            _combatModeKey = Config.Bind(
                "Teclas",
                "CombatModeKey",
                KeyCode.F3,
                "Tecla que cicla o modo de combate: Passive -> Defensive -> Aggressive.");

            _aggressiveRadius = Config.Bind(
                "Combate",
                "RaioAgressivo",
                30f,
                "Raio em metros (a partir do companheiro) em que o modo Aggressive procura a " +
                "criatura hostil mais proxima.");

            _defensiveRadius = Config.Bind(
                "Combate",
                "RaioDefensivo",
                25f,
                "Raio em metros (a partir do companheiro) em que o modo Defensive procura criaturas " +
                "que estejam cacando o companheiro ou o jogador dono.");

            _combatLeashDistance = Config.Bind(
                "Combate",
                "Coleira",
                50f,
                "Distancia maxima (m) entre o alvo de combate e o jogador seguido. " +
                "Passando disso o companheiro larga o alvo e volta a seguir o jogador.");

            _attackNeutralAnimals = Config.Bind(
                "Combate",
                "AtacarAnimaisNeutros",
                false,
                "Se true, o companheiro tambem caca a fauna neutra (javali, cervo, passaro...) - " +
                "a faccao AnimalsVeg do jogo. Por padrao ele so ataca criaturas hostis.");

            _combatScanInterval = Config.Bind(
                "Combate",
                "IntervaloVarredura",
                0.5f,
                "Intervalo em segundos entre as varreduras de alvo (0.1 a 2.0). " +
                "A varredura nunca roda por frame.");

            _blockAttacks = Config.Bind(
                "Combate",
                "BloquearAtaques",
                true,
                "Se true (padrao), o companheiro ergue o escudo quando um inimigo proximo esta " +
                "atacando (bloqueio/parry) e o baixa para revidar. So tem efeito pratico se ele " +
                "estiver com um escudo equipado.");

            _blockRadius = Config.Bind(
                "Combate",
                "RaioBloqueio",
                8f,
                "Raio em metros em que um inimigo atacando faz o companheiro bloquear (2 a 20).");

            _blockHold = Config.Bind(
                "Combate",
                "TempoBloqueio",
                0.2f,
                "Tempo em segundos que o companheiro mantem o escudo erguido depois de um golpe " +
                "inimigo (0.05 a 1.0).");

            // Movimento do companheiro (Fase 2.10, itens #2 e #5 da auditoria).
            _ledgeProtection = Config.Bind(
                "Movimento",
                "ProtecaoContraQueda",
                true,
                "Se true (padrao), o companheiro nao anda para barranco/penhasco: antes de cada " +
                "passo (coleta e follow) ele sonda o chao a frente e desvia ou para se o chao cair " +
                "demais. Desligue se ele estiver parando em rampas legitimas.");

            _maxDrop = Config.Bind(
                "Movimento",
                "QuedaMaxima",
                1.2f,
                "Queda maxima em metros tolerada num passo antes de o companheiro considerar que ha " +
                "um barranco (0.4 a 5.0). Valores maiores = menos conservador. Nao conta agua funda " +
                "(cair na agua e seguro).");

            _ledgeProbeDistance = Config.Bind(
                "Movimento",
                "DistanciaSondagem",
                1.2f,
                "Distancia em metros a frente onde o chao e sondado para detectar barranco " +
                "(0.5 a 3.0). Curta demais detecta tarde; longa demais para em rampas suaves.");

            _openDoors = Config.Bind(
                "Movimento",
                "AbrirPortas",
                true,
                "Se true (padrao), o companheiro abre portas fechadas no caminho e as fecha depois " +
                "de passar. Nunca abre portas com item de chave nem em area privada sem acesso.");

            // Coleta de recursos (Fase 2.4b).
            _harvestKey = Config.Bind(
                "Teclas",
                "HarvestKey",
                KeyCode.F4,
                "Tecla que inicia (e para) a coleta do recurso padrao. Padrao F4 " +
                "(F1 = spawn, H = dismiss e F3 = modo de combate ja estao em uso).");

            _harvestDefaultResource = Config.Bind(
                "Coleta",
                "RecursoPadrao",
                "Wood",
                "Recurso coletado pela tecla de coleta (modia_gather aceita qualquer um). " +
                "Aceita o nome do item (Wood, Stone, Raspberry, Resin...) ou apelidos em portugues " +
                "(madeira, pedra...).");

            _harvestDefaultAmount = Config.Bind(
                "Coleta",
                "QuantidadePadrao",
                0,
                "Quantidade alvo da coleta iniciada PELA TECLA (0 = sem limite: coleta enquanto " +
                "houver no no raio). O comando modia_gather sem quantidade tambem coleta sem limite.");

            _harvestScanRadius = Config.Bind(
                "Coleta",
                "RaioVarredura",
                40f,
                "Raio em metros (a partir do companheiro) onde ele procura nos de coleta. " +
                "Nos que a ferramenta disponivel nao consegue quebrar ficam no fim da fila.");

            _harvestScanInterval = Config.Bind(
                "Coleta",
                "IntervaloVarredura",
                0.5f,
                "Intervalo em segundos entre as varreduras de no de coleta (0.2 a 3.0). " +
                "A varredura usa buffer pre-alocado e nunca roda por frame.");

            _harvestPauseOnCombat = Config.Bind(
                "Coleta",
                "PausarEmCombate",
                true,
                "Se true (padrao), nos modos Defensive/Aggressive a coleta e PAUSADA enquanto " +
                "houver alvo hostil e retomada depois do combate. No modo Passive a coleta nunca " +
                "e pausada. Se false, o companheiro ignora os inimigos e continua coletando.");

            _harvestCollectDrops = Config.Bind(
                "Coleta",
                "CatarDrops",
                true,
                "Se true, o companheiro vai atras dos itens caidos (ItemDrop) do recurso pedido - " +
                "e o que conta a quantidade coletada. Desligado, ele so quebra arvores/rochas e " +
                "colhe plantas (a quantidade alvo nao fecha).");

            _harvestLayers = Config.Bind(
                "Coleta",
                "Camadas",
                CompanionHarvest.DefaultLayerNames,
                "Camadas varridas na busca de nos (separadas por virgula). Ajuste se algum " +
                "recurso nao for encontrado (ex.: inclua 'terrain' ou 'vehicle').");

            _harvestProvideTools = Config.Bind(
                "Coleta",
                "FornecerFerramentas",
                true,
                "Se true (padrao), quando o companheiro NAO tiver machado/picareta o mod cria uma " +
                "das ferramentas de 'FerramentasFornecidas' e coloca no inventario dele. E o que faz " +
                "pedra e minerio funcionarem de primeira (o loadout padrao so tem Club, que nao " +
                "arranha rocha). Desligado, a coleta avisa no HUD que falta ferramenta.");

            _harvestProvidedTools = Config.Bind(
                "Coleta",
                "FerramentasFornecidas",
                CompanionInventory.DefaultProvidedTools,
                "Prefabs de ferramenta que o mod pode fornecer (separados por virgula), do mais " +
                "fraco ao mais forte. Sem nenhuma, so a primeira de cada tipo (machado = dano de " +
                "corte, picareta = dano de picareta) que faltar e criada. Quando o no escolhido " +
                "exige um tier maior (prata/obsidiana), o mod cria a MELHOR da lista que atenda " +
                "(ex.: PickaxeIron). Padrao: AxeStone,PickaxeAntler,PickaxeIron.");

            // Peso, depósito em baú e tool keeper (Fase 2.14, item #7 da auditoria).
            _depositWhenFull = Config.Bind(
                "Coleta",
                "DepositarAoEncher",
                true,
                "Se true (padrao), ao ficar cheio (peso ou inventario sem espaco) o companheiro NAO " +
                "encerra a tarefa: procura o bau utilizavel mais proximo (respeitando area privada, " +
                "como faz com as portas), anda ate ele, deposita os materiais e VOLTA a coletar o " +
                "mesmo recurso/quantidade. Sem bau no raio ele PAUSA e avisa no HUD (a tarefa " +
                "continua viva). Desligado, o comportamento antigo volta: ele para de pegar o que " +
                "nao cabe e a coleta encerra por varredura vazia.");

            _chestSearchRadius = Config.Bind(
                "Coleta",
                "RaioBuscaBau",
                30f,
                "Raio em metros (a partir do companheiro) em que ele procura um bau para depositar " +
                "a carga ou pegar/devolver ferramenta (5 a 100). A varredura roda so ao iniciar uma " +
                "viagem e tem espera minima entre tentativas (nunca por frame).");

            _storeUnusedTool = Config.Bind(
                "Coleta",
                "GuardarFerramentaNaoUsada",
                true,
                "Liga o TOOL KEEPER (ferramenta no bau). Se true (padrao), a ferramenta que o " +
                "companheiro EMPRESTOU de um bau volta para o bau de origem (ou o mais proximo com " +
                "espaco) quando a coleta termina e ele nao precisa mais dela - e, quando ele nao tem " +
                "a ferramenta que o no exige e criar nao resolveu (Coleta/FornecerFerramentas " +
                "desligado ou lista sem ferramenta capaz), ele procura num bau proximo e vai busca-la. " +
                "Ferramenta CRIADA pelo mod nunca e devolvida - senao o mod a criaria de novo, e de " +
                "novo. Desligado, o companheiro nunca mexe em bau por ferramenta.");

            // Loot de criatura abatida pelo companheiro (Fase 2.12, item #4 da auditoria).
            _lootEnabled = Config.Bind(
                "Loot",
                "CatarDropsDeCriatura",
                true,
                "Se true (padrao), o companheiro vai atras dos drops das criaturas que ELE MESMO " +
                "abateu (couro, penas, ossos, hide...) e pega o que caiu. Só o que aquele abate " +
                "gerou: itens que ja estavam no chao sao ignorados. Roda fora do combate e fora de " +
                "uma tarefa de coleta (prioridade: combate > coleta > loot).");

            _lootRadius = Config.Bind(
                "Loot",
                "RaioCatar",
                8f,
                "Raio em metros, em volta do ponto do abate, onde o companheiro procura os itens " +
                "que cairam (2 a 30).");

            _lootMaxDistance = Config.Bind(
                "Loot",
                "DistanciaMaxima",
                30f,
                "Distancia maxima em metros do local do abate ate o companheiro E ate o jogador " +
                "seguido (5 a 200). Passando disso o companheiro desiste de catar e volta a seguir " +
                "voce - a caca e servico, nao fuga.");

            _lootQueueLimit = Config.Bind(
                "Loot",
                "LimiteFila",
                6,
                "Quantos abates ficam na fila para catar ao mesmo tempo (1 a 20). Ao encher, o " +
                "abate mais antigo e abandonado.");

            _lootWaitSeconds = Config.Bind(
                "Loot",
                "TempoEspera",
                15f,
                "Tempo maximo, em segundos, que o companheiro espera PARADO no local do abate pelos " +
                "itens sairem do corpo (3 a 60). Os drops de criatura so nascem quando o ragdoll " +
                "desaparece, alguns segundos depois da morte.");

            _lootMaxSeconds = Config.Bind(
                "Loot",
                "TempoMaximo",
                45f,
                "Teto em segundos do trabalho de UM abate (5 a 180): nunca deixa o companheiro " +
                "vagando atras de um corpo.");

            _lootMaxWeight = Config.Bind(
                "Loot",
                "PesoMaximo",
                0f,
                "Peso maximo em kg que o companheiro aceita carregar de loot (0 = sem limite, o " +
                "padrao - o jogo nao tem limite de peso para personagens). Com um valor > 0 ele " +
                "para de catar quando a carga passa disso e volta a seguir voce.");

            // Comida (Fase 2.13): o companheiro come sozinho a comida do proprio inventario.
            _autoEat = Config.Bind(
                "Comida",
                "ComerAutomaticamente",
                true,
                "Se true (padrao), o companheiro COME SOZINHO usando a comida do proprio " +
                "inventario: consome o item, ganha os bonus de vida/regen do jogo (vida maxima, " +
                "stamina maxima e regeneracao de vida) e respeita o tempo de queima de cada comida. " +
                "Ele so come quando faz sentido: com um slot livre (o jogo permite 3 comidas ao " +
                "mesmo tempo) ou, com os 3 cheios, apenas se a vida/stamina estiver abaixo do limiar " +
                "e a comida ja tiver passado de metade da queima. Desligado, ele nunca come (e volta " +
                "a ser um NPC sem fome).");

            _eatHealthThreshold = Config.Bind(
                "Comida",
                "LimiarHP",
                50f,
                "Vida (%) abaixo da qual o companheiro procura comer - mesmo com os 3 slots cheios, " +
                "ele recarrega a comida mais esgotada. Na hora de escolher, ele prefere a comida com " +
                "mais VIDA (0 a 100).");

            _eatStaminaThreshold = Config.Bind(
                "Comida",
                "LimiarStamina",
                30f,
                "Stamina (%) abaixo da qual o companheiro procura comer - na escolha, ele prefere a " +
                "comida com mais STAMINA (0 a 100).");

            _eatCheckInterval = Config.Bind(
                "Comida",
                "IntervaloVerificacao",
                5f,
                "Intervalo em segundos entre as verificacoes de 'esta na hora de comer?' (1 a 60). " +
                "A varredura do inventario so roda nesse intervalo, nunca por frame.");

            // Resistencia / stamina (Fase 2.13).
            _staminaEnabled = Config.Bind(
                "Resistencia",
                "StaminaLigada",
                true,
                "Se true (padrao), o companheiro tem stamina propria que IMPORTA: correr, nadar, " +
                "atacar, bloquear e pular gastam, e sem stamina ele PARA DE CORRER e nao consegue " +
                "atacar ate recuperar. Desligada, a stamina fica sempre cheia e nada e bloqueado " +
                "(comportamento antigo, sem cansaco).");

            _staminaBase = Config.Bind(
                "Resistencia",
                "StaminaBase",
                100f,
                "Stamina do companheiro SEM comida. O jogador tem 75 (mais a comida); 100 de base da " +
                "margem para o companheiro acompanhar voce sem ficar para tras a cada esquina. A " +
                "comida ativa soma o bonus dela ao teto, como no jogador (1 a 500).");

            _staminaRunDrain = Config.Bind(
                "Resistencia",
                "GastoCorrida",
                10f,
                "Stamina gasta por segundo CORRENDO (o jogador gasta 10/s, reduzido pela pericia de " +
                "corrida; o companheiro segue a mesma regra). O companheiro so corre a mais de 10 m " +
                "do alvo (0 a 30).");

            _staminaSwimDrain = Config.Bind(
                "Resistencia",
                "GastoNado",
                10f,
                "Stamina gasta por segundo NADANDO (0 a 30).");

            _staminaRegenRate = Config.Bind(
                "Resistencia",
                "TaxaRegen",
                6f,
                "Stamina recuperada por segundo depois do atraso, ja com o bonus proporcional ao " +
                "que falta (como no jogador: quanto mais vazio, mais rapido enche) e o multiplicador " +
                "dos efeitos de status (Molhado/Congelado atrapalham). 1 a 30.");

            _staminaRegenDelay = Config.Bind(
                "Resistencia",
                "AtrasoRegen",
                1f,
                "Segundos de espera, depois de gastar stamina, antes de ela comecar a voltar " +
                "(0.1 a 5.0).");

            // Habilidades / skills (Fase 2.13).
            _skillsEnabled = Config.Bind(
                "Skills",
                "EvoluirSkills",
                true,
                "Se true (padrao), as pericias do companheiro EVOLUEM com o uso, como no jogo: " +
                "cortar madeira sobe WoodCutting, picaretar sobe Pickaxes, a arma equipada sobe a " +
                "pericia dela, bloquear sobe Blocking, correr sobe Run. O nivel vale de verdade " +
                "(custo de stamina do ataque, poder de bloqueio e as contas de ferramenta) e as " +
                "pericias entram no snapshot (dismiss/invocar). Desligado, tudo fica em nivel 0.");

            _skillsAnnounce = Config.Bind(
                "Skills",
                "AvisarSubidaDeNivel",
                true,
                "Mostra no HUD (canto superior esquerdo) quando o companheiro sobe de nivel numa " +
                "pericia, com o nome e o icone da pericia.");

            _skillsLoseOnDeath = Config.Bind(
                "Skills",
                "PerderSkillsAoMorrer",
                true,
                "Se true (padrao, como no jogo), o companheiro perde uma fracao de cada pericia ao " +
                "morrer. A perda entra ANTES do snapshot, entao ele volta com o que sobrou.");

            _skillsDeathLossFactor = Config.Bind(
                "Skills",
                "FatorPerdaAoMorrer",
                0.25f,
                "Fracao perdida de cada pericia ao morrer (0.25 = 25%, como o m_DeathLowerFactor do " +
                "jogo; 0 = nao perde nada). 0 a 1.");

            // Interface (Fase 2.6a).
            _hoverHints = Config.Bind(
                "Interface",
                "MostrarDicasNoHover",
                true,
                "Mostra, quando a mira estiver sobre o companheiro, o nome dele e a lista de acoes " +
                "disponiveis com as teclas configuradas (modo de combate, coleta, invocar/dispensar). " +
                "Desligado, o hover mostra so o nome.");

            // Menu do mod dentro do jogo (Fase 2.6b).
            _menuKey = Config.Bind(
                "Teclas",
                "MenuKey",
                KeyCode.Y,
                "Tecla que abre e fecha o MENU do companheiro (painel de madeira com status, " +
                "aparencia, combate, coleta e teclas). Abre quando o jogador esta a ate 5 m do " +
                "companheiro; fecha com a mesma tecla, com ESC ou pelo botao Fechar.");

            Log.LogInfo($"{PluginName} v{PluginVersion} carregado (GUID: {PluginGuid}).");
            Log.LogInfo($"Unity: {Application.version} | plataforma: {Application.platform}");

            var jotunn = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Jotunn");
            Log.LogInfo($"Jotunn: {(jotunn != null ? "detectado" : "AUSENTE")}");

            var valheim = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "assembly_valheim");
            Log.LogInfo($"assembly_valheim: {(valheim != null ? "carregada" : "AUSENTE")}");

            Log.LogInfo($"[ModIA] Teclas: spawn = {_spawnKey.Value}, dismiss = {_dismissKey.Value}.");
            Log.LogInfo(
                $"[ModIA] Loadout: equipar no spawn = {_equipLoadoutOnSpawn.Value} | " +
                $"arma = '{_weaponPrefab.Value}' | armaduras = '{_armorPrefabs.Value}'.");
            Log.LogInfo(
                $"[ModIA] Aparencia: padrao = {_appearanceEnabled.Value} | " +
                $"cabelo = '{_hairPrefab.Value}' | barba = '{_beardPrefab.Value}' | " +
                $"pele = '{_skinColor.Value}' | cabelo/cor = '{_hairColor.Value}'.");
            Log.LogInfo(
                $"[ModIA] Persistencia do companheiro: entre sessoes = " +
                $"{_persistBetweenSessions.Value} (snapshot em memoria sempre ativo).");

            // Combate: modo, tecla de ciclo e raios (Fase 2.4a) + bloqueio/parry (Fase 2.10, #6).
            CompanionCombat.Configure(
                _combatMode,
                _combatModeKey,
                _aggressiveRadius,
                _defensiveRadius,
                _combatLeashDistance,
                _attackNeutralAnimals,
                _combatScanInterval,
                _blockAttacks,
                _blockRadius,
                _blockHold);

            // Movimento: protecao contra queda (#2) e portas (#5) (Fase 2.10).
            CompanionMovement.Configure(
                _ledgeProtection,
                _maxDrop,
                _ledgeProbeDistance,
                _openDoors);

            // Prefab do companheiro: clonado dos prefabs vanilla assim que eles estiverem
            // disponíveis (o Jotunn dispara o evento a cada carga de ObjectDB).
            CompanionPrefab.Hook();
            CompanionManager.Configure(_spawnKey, _dismissKey);
            CompanionInventory.Configure(_equipLoadoutOnSpawn, _weaponPrefab, _armorPrefabs);
            CompanionAppearance.Configure(
                _appearanceEnabled, _hairPrefab, _beardPrefab, _skinColor, _hairColor);

            // Persistencia do companheiro (Fase 2.7): snapshot em memoria + arquivo opcional.
            CompanionStateStore.Configure(_persistBetweenSessions);

            // Coleta de recursos: recurso/quantidade padrao, tecla, raio e convivencia com o
            // combate (Fase 2.4b).
            CompanionHarvest.Configure(
                _harvestKey,
                _harvestDefaultResource,
                _harvestDefaultAmount,
                _harvestScanRadius,
                _harvestScanInterval,
                _harvestPauseOnCombat,
                _harvestCollectDrops,
                _harvestLayers);

            // Ferramenta de coleta fornecida quando falta (Fase 2.8).
            CompanionInventory.ConfigureHarvestTools(_harvestProvideTools, _harvestProvidedTools);

            // Peso, depósito em baú e tool keeper (Fase 2.14, item #7 da auditoria).
            CompanionInventory.ConfigureWeight(_maxWeight);
            CompanionDeposit.Configure(_depositWhenFull, _chestSearchRadius);
            CompanionToolKeeper.Configure(_storeUnusedTool);

            // Loot de criatura abatida pelo companheiro (Fase 2.12, item #4 da auditoria).
            CompanionLoot.Configure(
                _lootEnabled,
                _lootRadius,
                _lootMaxDistance,
                _lootQueueLimit,
                _lootWaitSeconds,
                _lootMaxSeconds,
                _lootMaxWeight);

            // Comida, resistencia (stamina) e habilidades do companheiro (Fase 2.13).
            CompanionFood.Configure(
                _autoEat,
                _eatHealthThreshold,
                _eatStaminaThreshold,
                _eatCheckInterval);

            CompanionStamina.Configure(
                _staminaEnabled,
                _staminaBase,
                _staminaRunDrain,
                _staminaSwimDrain,
                _staminaRegenRate,
                _staminaRegenDelay);

            CompanionSkills.Configure(
                _skillsEnabled,
                _skillsAnnounce,
                _skillsLoseOnDeath,
                _skillsDeathLossFactor);

            CompanionCommands.Register();

            // Interface (Fase 2.6a): tokens de localização + dicas do hover do companheiro.
            CompanionHover.RegisterLocalization();
            CompanionHover.Configure(_hoverHints);

            // Menu do companheiro dentro do jogo (Fase 2.6b): tokens + tecla + hook da GUI.
            CompanionMenu.RegisterLocalization();
            CompanionMenu.Configure(_menuKey);
            CompanionMenu.Init();

            ApplyHarmonyPatches();
            if (_logDetalhado.Value)
            {
                Log.LogWarning("Log detalhado ligado - ModIA pronto para o desenvolvimento.");
            }        }

        /// <summary>
        /// Aplica os patches Harmony do mod (observadores de dano do combate). Fica isolado num
        /// try/catch: um patch que não casa (jogo atualizado) não pode derrubar o plugin inteiro.
        /// </summary>
        private void ApplyHarmonyPatches()
        {
            try
            {
                _harmony = new Harmony(PluginGuid);
                _harmony.PatchAll(typeof(Plugin).Assembly);
                Log.LogInfo("[ModIA] Patches Harmony aplicados.");
            }
            catch (Exception e)
            {
                Log.LogError($"[ModIA] Falha ao aplicar patches Harmony: {e}");
            }
        }

        /// <summary>
        /// Teclas de spawn/dismiss, de modo de combate, de coleta e do menu do companheiro (o Tick
        /// sai rápido fora do mundo).
        /// </summary>
        private void Update()
        {
            // O menu é lido primeiro: com ele aberto, as demais teclas do mod ficam suspensas
            // (CompanionManager.InputBlocked passa a devolver true).
            CompanionMenu.Tick();
            CompanionManager.Tick();
            CompanionCombat.Tick();
            CompanionHarvest.Tick();
        }

        private void OnDestroy()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }

            CompanionPrefab.Unhook();
            CompanionMenu.Shutdown();
            CompanionHover.Shutdown();
            CompanionManager.Shutdown();
        }
    }
}
