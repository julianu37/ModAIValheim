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
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "modia.companion";
        public const string PluginName = "ModIA Companion";
        public const string PluginVersion = "0.3.1";

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
                "fraco ao mais forte. So a primeira de cada tipo (machado = dano de corte, picareta " +
                "= dano de picareta) que faltar e criada. Ex.: AxeStone,PickaxeAntler. Para minerar " +
                "prata/obsidiana (tier maior) inclua PickaxeIron.");

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

            // Combate: modo, tecla de ciclo e raios (Fase 2.4a).
            CompanionCombat.Configure(
                _combatMode,
                _combatModeKey,
                _aggressiveRadius,
                _defensiveRadius,
                _combatLeashDistance,
                _attackNeutralAnimals,
                _combatScanInterval);

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
