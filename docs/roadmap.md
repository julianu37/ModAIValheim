# Roadmap — ModIA

Recriação, para o **Valheim 1.0 atual**, de um mod de companheiro NPC com IA (inspirado no "Thrall").

Legenda: ✅ concluído · 🔄 em andamento · ⏳ pendente

---

## Trilha A — Equipe de desenvolvimento (agentes + skills)

| Item | Status |
|---|---|
| 11 agentes (1 líder + 10 especialistas) em `.opencode/agents/` | ✅ |
| 6 skills de dependências (`bepinex`, `jotunn`, `thunderstore-packaging`, `valheim-reverse-engineering`, `valheim-ai-backend`, `valheim-assets`) | ✅ |
| `AGENTS.md`, `opencode.jsonc`, references (BepInEx, Jotunn) | ✅ |
| MCPs (`alvin` = Visual Studio, `context7`, `learn`) | ✅ |
| Repositório no GitHub | ✅ |

## Trilha B — O mod

| Fase | Descrição | Status |
|---|---|---|
| **0** | Setup: toolchain, solução net472, plugin "hello world" | ✅ |
| **1** | Reconhecimento: decompilar Thrall + jogo 1.0, mapa de features e diff de API (`docs/re/`) | ✅ |
| **2** | **Núcleo do NPC** | 🔄 |
| 2.0 | Publicizer das assemblies do jogo | ✅ |
| 2.1 | Spawn/dismiss (F1/H) + seguir o jogador | ✅ |
| 2.2 | `CompanionAI : MonsterAI` (herança correta do motor de IA) | ✅ |
| 2.3 | Inventário/equip + aparência hash-based do `VisEquipment` | ✅ |
| 2.3b | Corpo base viking (clone do `Player`) + aparência padrão | ✅ |
| 2.4a | Combate: Passive/Defensive/Aggressive, seleção de alvo, revidar | ✅ |
| 2.4b | Coleta de recursos (madeira/pedra/itens) | ⏳ |
| 2.5 | Multiplayer (ZNetView/ZDO, testes com cliente/servidor) | ⏳ |
| **3** | IA e voz: comandos por voz/chat, STT, TTS, personalidade | ⏳ |
| **4** | Backend híbrido (LLM local + API), endpoints do "cérebro" | ⏳ |
| **5** | Empacotamento (manifest, README, ícone) + revisão final | ⏳ |

---

## Riscos conhecidos (do `docs/re/api-diff.md`)

1. 🔴 `VisEquipment` migrou para **hashes** de item (`int itemHash`) — *já tratado na 2.3*.
2. 🔴 `ZNet.PlayerInfo.m_host` removido → identidade por `PlatformUserID` — **afeta o `player_id` do backend** (Fase 3/4).
3. 🔴 Campos viraram `private` → **resolvido com o publicizer** (2.0).
4. 🔴 Bases viraram `protected virtual` → overrides ajustados.
5. 🟠 `m_follow` saiu de `BaseAI` para `MonsterAI` → `CompanionAI : MonsterAI` (2.2).

---

## Histórico de fases concluídas

- **Fase 0** — BepInEx 5.4.23.5 + Jotunn 2.30.2 instalados; plugin carregando no Valheim 1.0.17.
- **Fase 1** — 2 DLLs do Thrall decompilados; 4 documentos de análise em `docs/re/`.
- **Fase 2.0–2.4a** — companheiro viking com spawn/dismiss, follow, inventário/equip, aparência e combate.
