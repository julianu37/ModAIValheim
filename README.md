# ModIA — Companheiro NPC com IA para Valheim

Mod de **companheiro NPC com inteligência artificial** para **Valheim 1.0** (inspirado no "Thrall",
recriado do zero para a versão atual). Projeto **pessoal de estudo** — ver [`NOTICE.md`](NOTICE.md).

> **Status:** 🚧 em desenvolvimento — Fase 2 (núcleo do NPC). O companheiro já **spawna, segue,
> equipa itens, tem aparência de viking e combate**. Ainda faltam **coleta de recursos**,
> **multiplayer**, **voz/IA** e **backend**.

---

## Versões alvo

| Item | Versão |
|---|---|
| Valheim | 1.0.17 (Unity **6000.0.75f1** / Unity 6) |
| BepInExPack_Valheim | 5.4.2351 (BepInEx 5.4.23.5) |
| Jotunn | 2.30.2 |
| Target do plugin | .NET Framework 4.7.2 (net472) |
| Runtime do "cérebro" (futuro) | Python 3.12 |

---

## 🎮 Controles

| Tecla / comando | Ação |
|---|---|
| **F1** | Invocar o companheiro (spawn) |
| **H** | Dispensar (despawn) |
| **F3** | Ciclar modo de combate: Passive → Defensive → Aggressive |
| `modia_inv` | (console, F5) mostra o inventário/itens equipados do companheiro |
| `modia_equip <prefab> [slot]` | equipa arma/armadura |
| `modia_drop <prefab> [qtd]` | dropa item do companheiro |
| `modia_mode [passive\|defensive\|aggressive]` | define/consulta o modo de combate |

Todas as teclas são configuráveis em `BepInEx\config\modia.companion.cfg`.

### Configuração (seções do `.cfg`)

- **Geral** — `LogDetalhado`
- **Teclas** — `SpawnKey` (F1), `DismissKey` (H), `CombatModeKey` (F3)
- **Companheiro** — `EquiparLoadoutNoSpawn`, `WeaponPrefab`, `ArmorSetPrefabs`
- **Aparencia** — `AparenciaPadrao`, `CabeloPrefab`, `BarbaPrefab`, `CorPele`, `CorCabelo`
- **Combate** — `Modo`, `RaioAgressivo`, `RaioDefensivo`, `Coleira`, `AtacarAnimaisNeutros`, `IntervaloVarredura`

---

## Estrutura do projeto

```
ModIA/
├─ AGENTS.md                  # regras/convenções para os agentes (stack, build, docs)
├─ README.md                  # este arquivo
├─ CHANGELOG.md               # histórico de versões
├─ NOTICE.md                  # aviso de projeto de estudo
├─ Environment.props          # caminhos locais (gitignored)
├─ ModAIValheim.slnx          # solução (VS 2026)
├─ docs/
│  ├─ roadmap.md              # fases e status
│  └─ re/                     # reverse engineering (Fase 1)
├─ src/ModAIValheim/          # código do mod (net472)
│  ├─ Plugin.cs
│  └─ Companion/              # CompanionAI, Prefab, Manager, Inventory, Appearance, Combat...
└─ .opencode/                 # agentes + skills (equipe de desenvolvimento)
```

---

## Como compilar e testar

```powershell
# Build (copia a DLL para BepInEx\plugins automaticamente)
dotnet build src/ModAIValheim/ModAIValheim.csproj -c Debug

# Teste: iniciar o jogo e conferir o log
#   C:\Program Files (x86)\Steam\steamapps\common\Valheim\BepInEx\LogOutput.log
```

Requisitos: .NET SDK 8+, Valheim 1.0 com BepInEx 5.4.23+ e Jotunn 2.30.2, Visual Studio 2026 (opcional).

---

## Documentação

- [`docs/roadmap.md`](docs/roadmap.md) — fases do projeto e status.
- [`CHANGELOG.md`](CHANGELOG.md) — histórico de versões.
- [`docs/re/`](docs/re/) — **reverse engineering**:
  - [`thrall-features.md`](docs/re/thrall-features.md) — o que o Thrall fazia, por área.
  - [`game-api-usage.md`](docs/re/game-api-usage.md) — APIs do jogo usadas e status na 1.0.17.
  - [`api-diff.md`](docs/re/api-diff.md) — o que quebrou no Valheim 1.0.
  - [`backend-contract.md`](docs/re/backend-contract.md) — endpoints do "cérebro" (LLM/STT/TTS).
- [`AGENTS.md`](AGENTS.md) — como a equipe de agentes trabalha neste repositório.

---

## Licença / uso

Projeto **pessoal de estudo**. Não afiliado à Iron Gate, Coffee Stain ou ego.ai. Nenhum binário ou
código de terceiros é redistribuído. Ver [`NOTICE.md`](NOTICE.md).
