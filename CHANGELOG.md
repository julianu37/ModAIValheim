# Changelog

Todas as mudanças relevantes do **ModIA**. Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/)
e versionamento semântico.

## [0.3.2] — 2026-10-13

### Corrigido
- **Coleta: encerramento precoce ao destruir um nó** (Fase 2.9, item #1 da
  [auditoria](docs/audit-vs-offline-companions.md)): ao quebrar árvore/pedra os drops levam alguns
  frames para nascer e a varredura imediata podia não achar nada — a tarefa encerrava em ~1,5 s
  (3 varreduras vazias). Agora existe um **estado dedicado de coleta de drops**
  (`CollectingDrops`): depois de destruir o nó o companheiro espera **0,5 s** e varre o **ponto de
  destruição** (raio de 8 m) atrás do `ItemDrop` do recurso pedido, pegando um por um. O estado
  termina por **5 varreduras vazias consecutivas OU 5 s** — sempre com limite, sem "vagar para
  sempre" — e só então a busca de nós volta, com o contador de varreduras vazias **zerado** (a
  espera pelos drops não pode contar como "acabou o recurso").
- **Coleta: rocha "rachada"** (item #3): no primeiro golpe o jogo troca muitos nós por uma cópia no
  mesmo ponto (`Destructible.m_spawnWhenDamaged`); o nó original sumia e o companheiro lia
  "destruído" — era o sintoma de *um golpe por pedra*. Agora ele guarda a **posição do último nó** e,
  na próxima varredura, procura um `MineRock`/`MineRock5`/`Destructible` **no mesmo ponto** (raio
  2,5 m) e continua a minerar.
- **Coleta: tora mais próxima** (item #16): ao derrubar uma árvore (`TreeBase`) — ou partir uma tora
  (`TreeLog`) — o companheiro mira **imediatamente a `TreeLog` mais próxima** (raio 12 m), em vez de
  sair varrendo a esmo. A cascata `TreeBase → TreeLog → m_subLogPrefab` (é o que faz a árvore render
  madeira) fica **intacta**.
- **Coleta: ferramenta por tier** (item #8): o padrão de `Coleta/FerramentasFornecidas` passa a ser
  **`AxeStone,PickaxeAntler,PickaxeIron`** e, quando o nó exige um tier maior do que a ferramenta
  disponível (prata/obsidiana com a picareta de chifre), o mod **fornece a melhor ferramenta da lista
  que atenda** (tier ≥ mínimo do nó e dano > 0 contra as resistências dele) e repete a varredura uma
  vez — prata/obsidiana passam a ser caváveis de primeira.

### Alterado
- `CompanionHarvestScanner`: `FindBestNode` passou a reportar o **tier mínimo exigido** e as
  resistências do nó que a ferramenta disponível não arranha (`HarvestPick.RequiredTier` /
  `RequiredModifiers`); ganhou as sondagens pontuais `FindNearestDrop`, `FindCrackedNode` e
  `FindNearestTreeLog`, com buffers reaproveitados (nada alocado por chamada).
- `CompanionInventory.EnsureHarvestToolForNode`: escolhe a melhor ferramenta da lista configurada
  para o tier exigido pelo nó (não cria nada se já houver uma capaz e nunca duplica).
- `CompanionAI`: a camada de coleta ganhou o "último nó conhecido" (posição + tipo) para resolver o
  nó que sumiu sem depender do frame em que o `GameObject` é destruído, e a limpeza do estado da
  coleta foi para `ResetHarvestState`.
- Versão do plugin → **0.3.2**.

### Notas
- **Referência (MIT, uso livre):** as correções #1/#9 (espera pelos drops + estado dedicado),
  #3 (seguir a cópia "rachada") e #16 (tora mais próxima) espelham a *lógica* de
  `HarvestController.UpdateCollectingDrops`/`TryFollowCrackedRock` e
  `MHR_GatherRules.NearestLog` do [Offline Companions Reborn](https://github.com/MaddhattrRocks/Offline-Companions-Reborn)
  (ver `THIRD_PARTY_NOTICES.md`). A implementação é nossa (adaptada ao nosso modelo de estados e
  aos nossos buffers), não é cópia de arquivo.
- **Performance:** a coleta de drops usa uma varredura com buffer pré-alocado no intervalo normal
  (0,5 s), nunca por frame; as sondagens pontuais compartilham o mesmo buffer.
- A espera pelos drops é **pulada** quando o combate vence (a coleta pausa e retoma depois) e
  desligável junto com `Coleta/CatarDrops = false` (aí não há drops para esperar).

## [0.3.1] — 2026-10-13

### Adicionado
- **Lista curada de materiais de coleta** (Fase 2.8, `CompanionHarvestResources`): 39 recursos
  conhecidos com o **nome canônico do item**, rótulo PT-BR e **apelidos PT/EN** — madeira
  (`Wood`, `FineWood`, `RoundLog`, `ElderBark`, `YggdrasilWood`, `Blackwood`, `Resin`), pedra
  (`Stone`, `Flint`, `Obsidian`, `BlackMarble`, `Grausten`), minérios (`CopperOre`, `TinOre`,
  `IronScrap`, `SilverOre`, `BlackMetalScrap`, `CopperScrap`, `FlametalOreNew`, `Chitin`,
  `Crystal`) e colhíveis (`Raspberry`, `Blueberries`, `Cloudberry`, `Mushroom*`, `Thistle`,
  `Dandelion`, `Carrot`, `Turnip`, `Onion`, `Barley`, `Flax`, `RoyalJelly`, `Honey`). Agora
  `modia_gather pedra`, `stone` e `Stone` caem todos no mesmo item; a resolução genérica (qualquer
  nome de prefab ou nome localizado) continua valendo.
- **Dropdown de recurso no menu** (seção **Coleta**): substitui o campo de texto livre por uma lista
  com os materiais conhecidos (rótulos no **nome localizado** do item, ex.: "Madeira", "Pedra",
  "Sílex", "Minério de cobre"), mantendo um campo livre **Outro…** para digitar qualquer recurso
  fora da lista.
- **Mira na superfície da rocha** (`CompanionHarvestScanner.GetAimPoint`): para
  `MineRock`/`MineRock5`/`Destructible` o companheiro mira na **área de golpe ativa mais próxima**
  (em vez do centro da raiz) e o progresso passa a ser medido pela distância à superfície — é o que
  faz os golpes conectarem em rochas grandes e reposiciona quando uma área some.
- **Fornecimento automático da ferramenta de coleta** (`CompanionInventory.EnsureHarvestTools` +
  config `Coleta/FornecerFerramentas` / `Coleta/FerramentasFornecidas`): sem machado/picareta no
  inventário, o mod cria a primeira ferramenta compatível da lista configurada (padrão
  `AxeStone,PickaxeAntler`) e a coloca no inventário — é o que faz **pedra e minério funcionarem de
  primeira** (o loadout padrão só tem `Club`, que não arranha rocha). Desligável.
- **Aviso de ferramenta ausente** no HUD (rate-limited) e **descarte explícito** no diagnóstico da
  varredura (`sem ferramenta=N`): nó que o companheiro não consegue arranhar deixa de virar alvo —
  antes ele caminhava até a rocha, batia sem dano, travava 7 s e a vetava.
- Ajuda do `modia_gather` agora lista os recursos conhecidos por categoria (nomes localizados).
- Seção **Materiais suportados** no `README` (tabela do que funciona e do que está fora do escopo)
  e receita de teste in-game.

### Alterado
- `CompanionHarvest.TryResolveResource`: resolve primeiro pela lista curada e só depois cai no
  fallback genérico (nome de prefab → nome localizado do item).
- Nome de exibição do recurso (`CompanionMenu`/`CompanionHover`) passa a ter **fonte única** em
  `CompanionHarvestResources.DisplayName`.
- `CompanionInventory.IsHarvestTool` descarta **arcos/bestas e munição** (a besta lista dano de
  corte, mas o ataque é projétil e não quebra nó nenhum).
- Menu: seção **Coleta** reorganizada (dropdown em 152, campo livre em 186, quantidade em 220,
  botões em 254, status em 290) e a seção **Teclas** desceu para 340 para abrir espaço.
- Versão do plugin → **0.3.1**.

### Notas
- O que cai de **criatura** (penas, couro, pele de cervo, fragmentos de osso, olho de Greydwarf,
  gosma), de **baú/tesouro** (âmbar, rubi, colar de prata) ou de **produção do jogador** (carvão) não
  é coletável: não há nó para o companheiro procurar. Ver a tabela do `README`.
- `Chitin` (Leviatã) e `Crystal` (cristais da montanha) entram na lista curada; o scanner os resolve
  pelo componente do nó, como qualquer outro material.

## [0.3.0] — 2026-10-13

### Adicionado
- **Persistência do companheiro** (Fase 2.7): ao **dispensar (H)**, ao **morrer** e no **fim do
  mundo/ao fechar o jogo** o NPC é fotografado e a próxima invocação (**F1**) devolve **o mesmo
  companheiro** — mesmos itens no inventário, mesmos itens equipados, mesma aparência
  (cabelo/barba/cores) e mesmo modo de combate — em vez de nascer vazio com o loadout padrão.
- `CompanionState`/`CompanionItemState`: o snapshot (por item: prefab, pilha, qualidade,
  durabilidade, `worldLevel`, variante e se estava **equipado**) + aparência + modo de combate +
  vida (informativa) + origem/data da captura.
- `CompanionStateStore`: estado em **memória estática** (sempre ativo — sobrevive ao dismiss e a
  trocar de mundo dentro da sessão) e, opcionalmente, em **arquivo JSON** em
  `BepInEx\config\modia.companion.state.json` (config `Companheiro/PersistirEntreSessoes`, padrão
  `true`) para sobreviver a **fechar e abrir o jogo**. O arquivo tem `Version` de formato e os itens
  são revalidados contra o `ObjectDB` na restauração: prefab que não existe mais é **pulado com log**.
- `CompanionInventory.CaptureItems`/`RestoreItems`/`EquipItemData` (e o `CreateItem` compartilhado
  com o loadout padrão): a restauração reusa o caminho normal do mod — item criado do `ObjectDB` →
  `Inventory.AddItem` → `Humanoid.EquipItem` → `VisEquipment` (o mesmo do `modia_equip`).
- `CompanionAppearance.ApplySnapshot`: aplica no spawn a aparência do snapshot (sem escrever na
  config — o snapshot é histórico, não preferência; a preferência continua sendo o `.cfg`).
- `CompanionStatePatches`: prefix Harmony **somente observador** em `ZNetScene.Shutdown` (o ponto por
  onde o jogo sai do mundo e fecha), para guardar o companheiro de quem estava vivo e não foi
  dispensado.
- Comandos de console **`modia_state`** (mostra o companheiro guardado) e **`modia_state clear`**
  (esquece o snapshot: o próximo F1 volta a usar o loadout padrão da config).
- A mensagem de morte agora avisa que os itens do companheiro ficaram guardados.
- Referência a `UnityEngine.JSONSerializeModule` (`JsonUtility`) no `.csproj`.

### Notas
- **Vida:** o companheiro volta com a vida **cheia** (a vida do snapshot é só informativa).
  Dispensar é um ato deliberado e seguro — devolver o NPC com 3 % de vida porque ele foi guardado no
  meio de uma briga seria uma armadilha; e na morte o snapshot é justamente o "renascer" dele.
- **Morte:** o `Character.OnDeath` dispara o `m_onDeath` **antes** de destruir o GameObject, então o
  snapshot é tirado com o inventário ainda inteiro. O `CharacterDrop` é removido do prefab, logo o
  corpo não dropa nada — não há duplicação de itens.
- **Sem duplicação:** o companheiro nasce vazio e a restauração só **adiciona** o que estava no
  snapshot; se o inventário já tiver itens, a restauração é **recusada** e logada.
- O arquivo de estado é **por instalação** (não por mundo): o companheiro guardado volta igual em
  qualquer mundo — é isso que preserva os itens de quem fecha o jogo com o NPC vivo (o prefab é
  não-persistente: nada dele vai para o save do mundo).
- O snapshot **substitui** o loadout padrão enquanto existir: para voltar ao comportamento antigo,
  `modia_state clear` (ou `Companheiro/PersistirEntreSessoes = false`).

## [0.2.9] — 2026-10-13

### Adicionado
- **Menu do companheiro dentro do jogo** (Fase 2.6b): um **painel de madeira** no estilo Valheim
  (Jotunn `GUIManager`) aberto pela tecla configurável **`Y`** quando o jogador está a até **5 m** do
  companheiro. Fecha com a mesma tecla, com **ESC** ou pelo botão **Fechar**. Todo o texto é PT-BR.
  - **Status** — nome (token localizado), **vida** atual/máxima, **modo de combate** e **estado da
    coleta** (recurso + progresso), atualizado 2×/s com o painel aberto.
  - **Aparência** — ligar/desligar a aparência personalizada, **cor de pele** e **cor de
    cabelo/barba** (3 sliders R/G/B cada, com amostra da cor) e dropdowns de **cabelo** e **barba**
    (lista montada dos itens `Customization` do `ObjectDB` + "Padrão do jogo"/"Nenhum"). As mudanças
    **aplicam em tempo real** no companheiro (`VisEquipment.SetSkinColor`/`SetHairColor`/
    `SetHairItem`/`SetBeardItem`) e são gravadas no `.cfg`.
  - **Combate** — dropdown **Passivo / Defensivo / Agressivo** ligado ao `CompanionCombat`.
  - **Coleta** — campo de **recurso**, campo de **quantidade** (0 = sem limite) e botões
    **Iniciar**/**Parar** ligados ao `CompanionHarvest`.
  - **Teclas** — lista **somente leitura** das teclas atuais (invocar, dispensar, modo, coleta e menu).
- Config `[Teclas] MenuKey` (`KeyCode`, padrão `Y`).
- `CompanionMenu`: painel + controles (montados **uma vez** e só mostrados/escondidos depois),
  bloqueio do input do jogo com o painel aberto (`GUIManager.BlockInput`) e fechamento automático se o
  companheiro morrer/desaparecer ou o jogador se afastar (nunca deixa o jogador travado).
- API ao vivo em `CompanionAppearance` (`Enabled`, `SkinColor`, `HairColor`, `SetSkinColor`,
  `SetHairColor`, `SetHairPrefab`, `SetBeardPrefab`, `SetEnabled`, `GetCustomizationNames`) e
  `CompanionAppearance.IsDisabledValue`; `"none"` agora **desanexa** cabelo/barba (hash 0) em vez de
  só ignorar.
- `CompanionManager.GameUiCapturesInput()` (guardas de input sem considerar o próprio menu).
- Tokens de localização do menu (34) no `LocalizationManager` do Jotunn, em `Portuguese_Brazilian` e
  `English`, com fallback PT-BR em código.
- O hover (Fase 2.6a) agora também mostra a linha `[Y] Menu`.

### Alterado
- `CompanionManager.InputBlocked()` passa a devolver `true` com o menu do mod aberto — as teclas de
  spawn/dismiss/modo/coleta ficam suspensas enquanto o painel está aberto.
- `Plugin.Update` lê o menu **antes** das demais teclas do mod.

### Notas
- O `ESC` não abre o menu de pausa do jogo com o painel aberto: o `BlockInput(true)` do Jotunn faz
  `TextInput.IsVisible()` devolver `true`, o que desarma o `Menu.Update` do jogo.
- Com o campo de recurso/quantidade em foco, as teclas (inclusive a do menu) são do campo — digitar
  "y" no recurso não fecha o painel.
- Como no resto do mod, a mudança de aparência só **replica** para os demais clientes a partir do dono
  do ZDO (multiplayer completo é a Fase 2.5).
- Ficou de fora da v1 (ver `docs/roadmap.md`): renomear o companheiro (campo somente leitura), nomes
  localizados dos penteados (o dropdown usa o nome do prefab, `Hair1`…), e abrir o menu por **mira**
  (a abertura usa a distância, como o alcance do hover).

## [0.2.8] — 2026-10-13

### Adicionado
- **Hover do companheiro** (Fase 2.6a): com a mira sobre ele, o tooltip do jogo mostra o **nome**
  e a **lista de ações com as teclas reais** do mod, em PT-BR:
  `[F3] Modo de combate: Defensivo`, `[F4] Coleta: Madeira (desligada)` e
  `[F1] Invocar / [H] Dispensar`.
- Config `[Interface] MostrarDicasNoHover` (`bool`, padrão `true`): desligada, o hover mostra só o
  nome do companheiro.
- `CompanionHover` (estado + texto/cache do hover) e `CompanionHoverPatches` (Harmony): prefix em
  `Character.GetHoverText` e `Character.GetHoverName`, **só** para o nosso companheiro.
- Tokens de localização no `LocalizationManager` do Jotunn (`$modia_companion_name` e os rótulos das
  ações), registrados em `Portuguese_Brazilian` e `English`, com fallback PT-BR em código.
- Nome do companheiro no corpo (`m_name`) agora é o token de localização — o `EnemyHud`, as
  mensagens de interação e qualquer UI que use `GetHoverName` passam a mostrar "Companheiro"
  (antes o corpo herdava o nome do prefab `Player`).

### Notas
- O `Character.GetHoverText()` do jogo devolve string vazia para personagens sem `Tameable` (caso do
  companheiro), por isso o texto vem de patch; personagens de outros mods/criaturas não são afetados
  (o prefix só age quando o alvo tem `CompanionAI`, com fallback pelo nome do prefab via `ZNetView`).
- O texto é montado apenas quando o estado muda (modo, coleta, teclas, config) e cacheado — o
  `Hud.UpdateCrosshair` chama o hover por frame.
- A tecla de menu do companheiro (Fase 2.6b) e as teclas de conversa/voz (Fase 3) entram no guia
  quando existirem.

## [0.2.7] — 2026-10-13

### Corrigido
- **Coleta de madeira: o companheiro agora derruba árvores** (Fase 2.4b). Causa raiz: a
  classificação lia só `TreeBase.m_dropWhenDestroyed`, mas a madeira de uma árvore **não está nessa
  tabela** — ela está nas toras que a árvore gera ao cair. Sem seguir
  `TreeBase.m_logPrefab` → `TreeLog.m_dropWhenDestroyed` → toras menores (`m_subLogPrefab`), uma
  Beech1 **nunca entrava** no conjunto de candidatos de `wood`: o log real só mostrava galhos
  (`Pickable_Branch`) e itens avulsos (`Wood`/`ItemDrop`).
- Pontuação da escolha de nó refeita (`CompanionHarvestScanner.CalculateScore`): agora é
  **valor × esforço × distância**, com os três termos na mesma escala (0–100) —
  valor = unidades do recurso pedido que o nó rende (50 %), esforço = golpes estimados pela melhor
  ferramenta disponível (20 %), distância (30 %). A fórmula herdada (`(mín+máx)/2×10×0,3` +
  `100/vida×0,3` + `100/(1+dist)×0,4`) dava ~100 pontos de graça a qualquer galho (vida 1) e
  afundava árvores/pedras (vida alta) mesmo rendendo 20× mais; somava ainda a quantidade de **todos**
  os drops do nó, não só do recurso pedido.
- `MineRock`/`MineRock5`: rendimento e esforço contam as **áreas de golpe** (o jogo dropa a tabela
  uma vez por área destruída) — rochas grandes deixam de perder para pedrinhas do chão.
- Nó cujo rendimento estimado do recurso pedido é **zero** (tabela sem o item, árvore sem tora
  conhecida) não vira mais alvo: antes o companheiro podia atravessar o raio atrás de um nó que não
  renderia nada.
- Nó cuja ferramenta disponível não arranha (tier mínimo acima do disponível ou dano 0 contra as
  resistências) leva penalidade forte (×0,1) e, quando escolhido, o "sem progresso" continua vetando
  o nó por 90 s — o log agora diz explicitamente `SEM ferramenta capaz`.

### Adicionado
- Diagnóstico da varredura de coleta com `Geral/LogDetalhado = true`: contadores por categoria
  (`TreeBase`, `TreeLog`, `MineRock`, `MineRock5`, `Pickable`, `ItemDrop`, `Destructible`), quantos
  nós casam com o recurso pedido, motivos de descarte (veto, drop desligado, sem o recurso, sem
  rendimento, esgotado, submerso, perto de obra, longe, buffer saturado) e o alvo escolhido com
  camada, tier de ferramenta, rendimento, golpes e score.
- Log do alvo agora mostra o **rendimento estimado** e o **custo em golpes** (`rende ~20 'Wood' em
  ~2 golpe(s)`), além da ferramenta equipada para o nó.
- `CompanionInventory.GetHarvestToolProfiles`: entrega as ferramentas de golpe (tier + dano por
  tipo) para a busca de nós; `CompanionHarvestScanner.EffectiveDamage`/`ModifierValue` passam a ser
  a **fonte única** da tabela de resistências (a escolha da ferramenta usa a mesma conta).

### Notas
- Verificado no `assembly_valheim.dll` 1.0.17: `TreeBase.m_logPrefab` (usado por `SpawnLog`),
  `TreeLog.m_subLogPrefab`/`m_subLogPoints`, `DropTable.m_dropMin/m_dropMax/m_dropChance`
  (sorteio de `GetDropList`), `MineRock5`/`MineRock` dropando por área e
  `HitData.CheckToolTier` como trava de tier.
- Pintura de madeira por galho/`ItemDrop` continua valendo quando não há árvore no raio (o pedido
  é atendido, só que pela fonte certa primeiro).

## [0.2.6] — 2026-10-13

### Adicionado
- **Coleta de recursos** do companheiro (Fase 2.4b, `CompanionHarvest` + `CompanionHarvestScanner`):
  busca de nós por varredura física (`OverlapSphereNonAlloc`, buffer reaproveitado), pontuação de
  "facilidade" (quantidade × vida × distância), escolha da ferramenta pelas resistências do nó
  (machado/picareta), golpe com a hitbox do ataque equipado, colheita de `Pickable` e coleta dos
  `ItemDrop` caídos.
- Comandos de console **`modia_gather <recurso> [qtd]`** e **`modia_stop`**; tecla **F4**
  (config `Teclas/HarvestKey`) inicia/para a coleta do recurso padrão.
- Config `[Coleta]`: `RecursoPadrao`, `QuantidadePadrao`, `RaioVarredura`, `IntervaloVarredura`,
  `PausarEmCombate`, `CatarDrops`, `Camadas`.
- `CompanionInventory`: `GetBestAvailableToolTier` e `TryEquipBestHarvestTool` (ferramenta ideal por
  tabela de modificadores de dano).
- Referência a `assembly_guiutils` no `.csproj` (nome localizado dos itens).

### Notas
- Combate x coleta: em Defensive/Aggressive um alvo hostil **pausa** a coleta (retomada depois); em
  Passive a coleta continua.
- Nunca ataca construções do jogador nem coleta dentro d'água/perto de base; nós impossíveis
  (ferramenta fraca, parede no caminho) são vetados por 90 s.

## [0.2.5] — 2026-10-13

### Alterado
- Tecla de ciclo do **modo de combate**: `F2` → **`F3`** (config `Teclas/CombatModeKey`).

### Adicionado
- Documentação do projeto: `README.md`, `CHANGELOG.md`, `docs/roadmap.md`.

## [0.2.4] — 2026-10-13

### Adicionado
- **Combate** do companheiro com modos **Passive / Defensive / Aggressive** (`CompanionMode`).
- Tecla **F2** para ciclar o modo e comando de console `modia_mode`.
- Patch Harmony observador em `Character.Damage` (detecta o dono sendo agredido/atacando).
- Config `[Combate]`: `Modo`, `RaioAgressivo`, `RaioDefensivo`, `Coleira`, `AtacarAnimaisNeutros`, `IntervaloVarredura`.

### Notas
- Perseguição/ataque usam o motor nativo do `MonsterAI`; nós escolhemos o alvo (varredura 0,5 s, sem alocação por frame).
- Nunca ataca jogador, tamed, mesma facção ou construções.

## [0.2.3] — 2026-10-13

### Alterado
- **Corpo base do companheiro**: `Draugr` → **`Player` (viking)** — `Player` removido e corpo convertido para `Humanoid`; armadura de peito/pernas agora renderiza.

### Adicionado
- `CompanionAppearance`: cabelo, barba, cor de pele e cor de cabelo (config `[Aparencia]`).

## [0.2.2] — 2026-10-13

### Adicionado
- **Inventário/equipamento** do companheiro (`CompanionInventory`), com `VisEquipment` no novo esquema **hash-based** do 1.0.
- Loadout padrão configurável (`WeaponPrefab`, `ArmorSetPrefabs`).
- Comandos de console: `modia_inv`, `modia_equip`, `modia_drop`.

## [0.2.1] — 2026-10-13

### Alterado
- Tecla de spawn padrão: `G` → **`F1`** (a `G` passou a abrir o menu radial no Valheim 1.0).

## [0.2.0] — 2026-10-13

### Adicionado
- **Companheiro NPC** (`CompanionAI : MonsterAI`): spawn/dismiss por tecla e comportamento de **seguir** o jogador.
- Prefab `ModIA_Companion` registrado via `PrefabManager` (Jotunn), server-authoritative.
- `Plugin`: `ConfigEntry<KeyCode>` para as teclas; Jotunn como dependência dura.

## [0.1.0] — 2026-10-13

### Adicionado
- Solução C# `net472` + plugin "hello world" (`modia.companion`) validado no Valheim 1.0.17 com BepInEx + Jotunn.
- Publicizer das assemblies do jogo (`BepInEx.AssemblyPublicizer.MSBuild`).
