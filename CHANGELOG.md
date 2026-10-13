# Changelog

Todas as mudanças relevantes do **ModIA**. Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/)
e versionamento semântico.

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
