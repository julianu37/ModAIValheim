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
| 2.4b | Coleta de recursos (madeira/pedra/itens) | ✅ |
| 2.4b-fix | Coleta de madeira: derrubar árvores (seguir a cascata tora→toras menores) + pontuação por rendimento real | ✅ |
| **2.6** | **Interface** | 🔄 |
| 2.6a | Hover do companheiro: nome + ações com as teclas (PT-BR, tokens de localização) | ✅ |
| 2.6b | Menu do companheiro (painel Jotunn `GUIManager`): status, aparência ao vivo, combate, coleta e teclas | ✅ |
| 2.7 | Persistência do companheiro: ao re-invocar (F1) volta o mesmo NPC (itens, equipamento, aparência e modo), com snapshot em memória + JSON opcional | ✅ |
| 2.8 | Coleta ampliada: lista curada de materiais (pedra/minérios/colhíveis) + dropdown no menu + mira na rocha + ferramenta fornecida | ✅ |
| 2.9 | Coleta: correções da auditoria (espera pelos drops, rocha "rachada", tora mais próxima, ferramenta por tier) | ✅ |
| 2.10 | Movimento/combate: proteção contra queda, portas no caminho, bloqueio/parry e filtro de alvo passivo | ✅ |
| 2.11 | Coleta: fim do loop de escolher/largar nó (seleção respeita a "coleira" do jogador) + log só na mudança | ✅ |
| 2.12 | Loot: catar os drops das criaturas abatidas pelo companheiro (patch em `Character.OnDeath` + estado de loot) | ✅ |
| 2.13 | **Membro da equipe**: comida (come sozinho do inventário), resistência (stamina) e habilidades que evoluem | ✅ |
| 2.14 | **Peso, depósito em baú e tool keeper**: teto de peso, ao encher leva a carga ao baú e retoma (ou pausa), ferramenta emprestada/devolvida do baú | ✅ |
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
- **Fase 2.0–2.4b** — companheiro viking com spawn/dismiss, follow, inventário/equip, aparência,
  combate e coleta de recursos (`modia_gather`/`modia_stop`, tecla F4).
- **Fase 2.4b (fix 0.2.7)** — coleta de madeira passou a **derrubar árvores**: a classificação
  segue a cadeia `TreeBase.m_logPrefab` → `TreeLog.m_subLogPrefab` (a madeira está nas toras, não na
  árvore) e a pontuação virou valor × esforço × distância, com diagnóstico por categoria em
  `Geral/LogDetalhado`.
- **Fase 2.6a (0.2.8)** — **hover do companheiro**: patch em `Character.GetHoverText`/
  `GetHoverName` (só para o NPC do mod) mostrando o nome e o mapa de teclas, com os rótulos em
  tokens do `LocalizationManager` do Jotunn e o texto cacheado por mudança de estado.
- **Fase 2.6b (0.2.9)** — **menu do companheiro**: painel de madeira do `GUIManager` (Jotunn) aberto
  pela tecla `Y` com o NPC a até 5 m, com status, aparência **ao vivo** (`VisEquipment`),
  modo de combate, coleta e a lista de teclas. Input bloqueado enquanto aberto (`BlockInput`),
  controles montados uma vez e fechamento automático se o companheiro sumir/afastar.
- **Fase 2.7 (0.3.0)** — **persistência do companheiro**: ao dispensar (H), ao morrer e no fim do
  mundo/fechar o jogo o NPC é fotografado (`CompanionStateStore`) e o próximo F1 devolve o mesmo
  companheiro (itens, equipamento, aparência e modo). Estado em memória sempre + JSON opcional em
  `BepInEx\config\modia.companion.state.json` (`Companheiro/PersistirEntreSessoes`), com versão de
  formato e itens revalidados contra o `ObjectDB`; `modia_state`/`modia_state clear` no console.
- **Fase 2.8 (0.3.1)** — **coleta ampliada**: lista curada de 39 materiais (`CompanionHarvestResources`)
  com apelidos PT/EN (madeira, pedra, minérios e colhíveis), **dropdown de recurso** no menu (com
  campo livre "Outro…"), **mira na superfície** de rochas (`GetAimPoint`) para os golpes conectarem
  e **fornecimento automático de machado/picareta** quando falta (`Coleta/FornecerFerramentas`) —
  pedra e minério passam a funcionar de primeira. Tabela de materiais no `README`.
- **Fase 2.9 (0.3.2)** — **correções de coleta da auditoria comparativa** (vs Offline Companions
  Reborn, MIT): **estado dedicado de coleta de drops** (espera 0,5 s os drops nascerem e varre o
  ponto de destruição; termina por 5 varreduras vazias ou 5 s — corrige o encerramento precoce em
  ~1,5 s), **rocha "rachada"** (segue a cópia no mesmo ponto — fim do "um golpe por pedra"),
  **tora mais próxima** (árvore derrubada → `TreeLog` imediata, preservando a cascata) e
  **ferramenta por tier** (`PickaxeIron` no padrão + melhor ferramenta da lista que atenda ao nó,
  o que destrava prata/obsidiana). Ver `docs/audit-vs-offline-companions.md`.
- **Fase 2.10 (0.3.3)** — **correções de movimento e combate da auditoria comparativa**: **proteção
  contra queda** (#2, sonda o chão à frente e desvia/aborta se cair mais que 1,2 m — configurável),
  **portas no caminho** (#5, abre/fecha `Door` sem abrir portas com chave nem em área privada),
  **bloqueio/parry** (#6, `Character.m_blocking` + `Humanoid.m_blockTimer` quando um inimigo próximo
  ataca) e **filtro de alvo passivo/neutro** (#14, ignora `AnimalAI` e criaturas que só revidam/fogem
  salvo `AtacarAnimaisNeutros = true`). Introduz o **dono do movimento por frame**
  (`CompanionMovement`/`MovementOwner`) para coleta, follow e guardas não disputarem o `MoveTo`.
  Ver `docs/audit-vs-offline-companions.md`.
- **Fase 2.11 (0.3.4)** — **fim do loop infinito da coleta** (bug crítico reportado em jogo): a
  varredura de nó passou a respeitar a **mesma "coleira" do jogador** que a guarda de distância do
  `CompanionAI` (antes ela escolhia um nó que a guarda largava no tick seguinte), o nó largado por
  "jogador se afastou" **não é re-escolhido no mesmo tick** (o companheiro **volta a seguir** na hora
  e só varre no próximo ciclo) e o log de escolha/largada **só sai quando o evento muda** (10 s de
  janela por nó+motivo). Também silencia o aviso de `Character.RaiseSkill` para o companheiro domado
  sem `Tameable` (`CompanionSkillPatches`), que era benigno mas poluía o log.
- **Fase 2.12 (0.4.0)** — **loot de criatura** (item #4 da auditoria): o companheiro **cata o que ele
  mesmo abateu**. Patch Harmony **observador** em `Character.OnDeath` lê o `m_lastHit` do falecido
  (o último golpe — o mesmo campo que o jogo usa para creditar o abate) e enfileira o ponto do abate;
  a camada `CompanionLoot`/`UpdateLoot` vai ao corpo, **espera os itens saírem do ragdoll** (eles só
  nascem quando o corpo desaparece), **segue o corpo** se ele escorregar e cata só os `ItemDrop` que
  **aquele** abate gerou (foto do "antes" ignora o que já estava no chão), respeitando raio, distância
  máxima, peso, fila de 6 e tetos de tempo. Prioridade preservada (**combate > coleta > loot/follow**,
  com o abate voltando à frente da fila e o relógio pausado) e novo `MovementOwner.Loot` no dono único
  do movimento por frame. Ver `docs/audit-vs-offline-companions.md`.
- **Fase 2.13 (0.4.1)** — **membro da equipe: comida, resistência e habilidades**. O companheiro
  **come sozinho** a comida do próprio inventário (slots próprios com a queima e a curva de bônus do
  jogo, `Character.SetMaxHealth(base + bônus)`, regeneração de `Σ m_foodRegen` a cada 10 s com
  `SEMan.ModifyHealthRegen`, efeito de status da comida e a animação de comer) — necessário porque os
  slots de comida são **privados do `Player`** e `Humanoid.ConsumeItem` é um **no-op** na base;
  **resistência própria** que importa (patches em `Character.UseStamina`/`HaveStamina`/`AddStamina` e
  postfix em `Character.CheckRun`, com gasto por corrida/nado no tick do `CompanionAI`) — sem ela,
  correr/atacar/bloquear/pular eram de graça para o NPC, porque a base é `virtual` e vazia; e
  **habilidades que evoluem** (`CompanionSkills` com a matemática do `Skills.Skill.Raise` e as
  `SkillDef` copiadas do prefab do jogador), com o `CompanionSkillPatches` **revisado**: o mesmo
  prefix que silenciava o aviso de `Tameable` agora **entrega o ganho** ao companheiro e o
  `GetSkillFactor` faz o nível valer no custo de stamina do ataque e no poder de bloqueio (o
  `GetRandomSkillFactor`, que é o multiplicador de **dano**, ficou de fora de propósito: a curva do
  jogador enfraqueceria o companheiro até ele treinar muito). Comida, stamina e perícias entram no **snapshot** (campos aditivos) e o console ganhou
  `modia_stats`. A coleta ganhou `CompanionHarvest.NotifyWaiting`: esperar stamina **não** é
  travamento (o nó não é vetado).
- **Fase 2.14 (0.4.2)** — **peso, depósito em baú e tool keeper** (itens #7 e #11 da auditoria). O
  companheiro ganhou **teto de peso** (`Companheiro/PesoMaximo`, padrão 300 kg, `0` = sem limite):
  antes de catar/coletar ele soma o peso do item (`ItemData.GetWeight`) ao que já carrega
  (`Inventory.GetTotalWeight`) e **nunca** passa do teto — vale para os drops do nó, os colhíveis e o
  **loot de criatura** (que passou a respeitar também o teto geral). Ao encher, a coleta **não
  encerra mais** (`não consegui pegar o item (inventário cheio?)` deixou de existir): o novo
  `CompanionDeposit` procura o **baú utilizável mais próximo** (`Piece.GetAllPiecesInRadius`,
  `Coleta/RaioBuscaBau` 30 m) com as regras do jogo (Container de `Piece`, `ZNetView` válida, não
  estar em uso, `PrivateArea.CheckAccess` como nas portas, privado só se for do jogador),
  **anda até ele** (`MovementOwner.Deposit`), **deposita** os materiais (`Inventory.MoveItemToThis`;
  equipamento/ferramenta/arma/comida/munição ficam com ele) e **volta** — a tarefa continua viva e o
  relógio do nó é renovado durante a viagem (ir ao baú é trabalho, não travamento). **Sem baú** ele
  **PAUSA** e avisa no HUD (a tarefa não é perdida) e **retoma sozinho** quando houver espaço.
  Prioridade **combate > coleta > depósito > loot > follow** (o abate volta para a frente da fila com
  o relógio parado). O **tool keeper** (`CompanionToolKeeper`) busca num baú a ferramenta que
  **falta** (só o tipo que falta) ou a que atende ao **tier do nó** (tier ≥ mínimo e dano > 0 contra
  as resistências, pontuação por tier/qualidade/dano/durabilidade, quebrada descartada) e **devolve**
  a emprestada quando a coleta termina (`Coleta/GuardarFerramentaNaoUsada`) — ferramenta **criada**
  nunca é devolvida. Console ganhou `modia_deposit` e o `modia_stats` mostra carga/depósito/empréstimos.
