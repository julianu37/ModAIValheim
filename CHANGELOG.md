# Changelog

Todas as mudanças relevantes do **ModIA**. Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/)
e versionamento semântico.

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
