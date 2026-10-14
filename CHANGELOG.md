# Changelog

Todas as mudanças relevantes do **ModIA**. Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/)
e versionamento semântico.

## [0.4.1] — 2026-10-14

### Adicionado
- **O companheiro virou MEMBRO DA EQUIPE** (Fase 2.13): agora ele **come**, **cansa** e **evolui
  habilidades** — o que faltava para ele deixar de ser decorativo.
  - **Comida (`Companion/CompanionFood.cs`).** Ele come sozinho a comida do **próprio inventário**:
    consome o item, ganha os bônus do jogo (vida máxima, stamina máxima e regeneração de vida) e
    respeita os **tempos de queima**. No 1.0.17 os slots de comida (`m_foods`, `m_foodUpdateTimer`,
    `GetTotalFoodValue`, `EatFood`, `UpdateFood`) são **privados do `Player`**, e
    `Humanoid.ConsumeItem` é `virtual` com corpo **vazio** na base (quem come é
    `Player.ConsumeItem` → `EatFood`, que ainda escreve no **perfil do jogador** e nos campos
    `m_baseHP`/`m_maxStamina` que só o jogador tem) — ou seja, **não havia caminho pronto para um
    NPC**. Reproduzimos as contas do `Player.UpdateFood`: queima de 1 s a cada
    `1 / Game.m_foodRate`, bônus decrescente `base × pow(clamp01(restante/total), 0.3)`,
    `Character.SetMaxHealth(base + bônus)`, regeneração de `Σ m_foodRegen` a cada 10 s passando por
    `SEMan.ModifyHealthRegen`, efeito de status da comida
    (`SEMan.AddStatusEffect(m_consumeStatusEffect)`) e o feedback visual do próprio jogo
    (`m_zanim.SetTrigger("eat")`, `Humanoid.m_consumeItemEffects`, `SetUseHandVisual`).
  - **Sem desperdício:** ele come com um slot livre (o jogo permite 3 comidas simultâneas) ou, com
    os 3 cheios, **só** se a vida (`Comida/LimiarHP`, 50%) ou a stamina (`Comida/LimiarStamina`, 30%)
    estiverem abaixo do limiar **e** alguma comida já tiver passado de metade da queima (a regra
    `CanEatAgain` do jogo). Na escolha, prefere a comida que cobre a necessidade (mais vida quando a
    vida está baixa, mais stamina quando a stamina está baixa) e, sem necessidade nenhuma, a **mais
    barata** — o banquete bom fica guardado. Comida cujo efeito de status já está ativo é recusada.
  - **Resistência (`Companion/CompanionStamina.cs` + `CompanionStaminaPatches.cs`).** No 1.0.17
    `Character.UseStamina`/`HaveStamina`/`AddStamina` são `virtual` e **vazios** na base (só o
    `Player` os implementa), então o companheiro não tinha stamina nenhuma: correr, atacar, bloquear
    e pular eram de graça. Patches Harmony redirecionam as três chamadas para a stamina dele — o que
    faz o **próprio jogo** cobrar e recusar as ações caras: `Attack.Start` (custo reduzido pela
    perícia da arma, via `GetAttackStamina` → `GetSkillFactor`), `Humanoid.BlockAttack`,
    `Character.OnJump` e `SE_Stats` (hidromel/poções). O gasto por correr/nadar e a regeneração
    (mesma forma do jogador: taxa + bônus proporcional ao que falta, com `SEMan.ModifyStaminaRegen`)
    rodam no tick do `CompanionAI`, e um postfix em `Character.CheckRun` faz ele **parar de correr**
    com a stamina zerada em vez de continuar em disparada.
  - **Sem travar a coleta:** golpear árvore/pedra também custa stamina, e sem stamina o
    `Attack.Start` recusa o golpe — o que faria a coleta **vetar o nó** por "sem progresso em 7 s".
    O `StrikeNode` agora checa a resistência **antes** de bater e, quando falta, marca o nó como
    *esperando* (`CompanionHarvest.NotifyWaiting`), renovando o relógio de progresso: ele recupera e
    volta a bater.
  - **Habilidades (`Companion/CompanionSkills.cs`).** O companheiro não tem o componente `Skills`
    (removido do prefab de propósito, porque ele guarda uma referência ao `Player`), então
    `GetSkills()`/`GetSkillFactor()` devolviam `null`/`0` e ele bateria sempre com "skill 0".
    Guardamos nível + acumulador de cada perícia e reproduzimos a matemática do `Skills.Skill.Raise`
    (`m_increseStep × fator × Game.m_skillGainRate`; próximo nível
    `pow(floor(nível+1), 1.5) × 0.5 + 0.5`), com as definições (`SkillDef`: ícone e passo de ganho)
    copiadas do prefab do jogador. O nível vale **de verdade**: custo de stamina do ataque
    (a perícia da arma corta até 33% do gasto), poder de bloqueio e as contas de ferramenta. O
    multiplicador de **dano** (`GetRandomSkillFactor`) ficou **de fora de propósito** — a curva do
    jogador faria um companheiro de perícia 0 bater com ~40% do dano, ou seja, mais fraco do que
    antes desta fase; o dano fica como estava. Ele ganha perícia cortando
    (`WoodCutting`), minerando (`Pickaxes`), com a arma equipada, bloqueando (`Blocking`) e correndo
    (`Run`), com aviso opcional no HUD e perda por morte (25%, como o jogo).
  - **`CompanionSkillPatches` revisado.** O patch da 2.11 silenciava o aviso
    `"… is tamed but missing tameable or monster AI script!"` devolvendo `false` em
    `Character.RaiseSkill` — o que também **matava qualquer evolução de perícia**. Agora o mesmo
    `false` (o corpo do jogo **não roda** para o NPC, logo o aviso **não pode voltar**) vem depois de
    **entregar o ganho** ao `CompanionSkills`, e dois prefixes novos
    (`GetSkillFactor`/`GetRandomSkillFactor`) fazem o nível chegar onde o jogo consome.
  - **Snapshot (Fase 2.7) ampliado:** comidas ativas (com o tempo de queima que faltava), stamina e
    perícias entram no `CompanionState` — dismiss (H) → invocar (F1) devolve o companheiro
    **alimentado, com a resistência e as habilidades que ele tinha**. Os campos são **aditivos**:
    arquivo antigo (formato v1 sem eles) carrega normalmente, só sem esses dados.
  - **`modia_stats`** (novo comando de console): mostra vida (com a base sem comida), comidas ativas
    com o tempo que falta, resistência e as perícias já treinadas.
  - **Menu (Y):** a linha de estado ganhou `Comida: n/3 | Resistência: x/y` (o estado da coleta já
    tem painel próprio logo abaixo).
- Config em três seções novas (todas em PT-BR): **`[Comida]`** (`ComerAutomaticamente`, `LimiarHP`,
  `LimiarStamina`, `IntervaloVerificacao`), **`[Resistencia]`** (`StaminaLigada`, `StaminaBase`,
  `GastoCorrida`, `GastoNado`, `TaxaRegen`, `AtrasoRegen`) e **`[Skills]`** (`EvoluirSkills`,
  `AvisarSubidaDeNivel`, `PerderSkillsAoMorrer`, `FatorPerdaAoMorrer`).
- Seção **"Comida, resistência e habilidades"** no `README` com a receita de teste in-game.

### Alterado
- `CompanionAI.UpdateAI` ganhou a camada `UpdateVitals(dt)` (comida + resistência), que roda
  **depois** das guardas de dono do ZDO e **antes** do motor nativo; `CompanionAI.StrikeNode` passou
  a consultar a resistência antes de golpear.
- `CompanionPrefab` adiciona os três componentes novos ao prefab (`CompanionFood`,
  `CompanionStamina`, `CompanionSkills`) e copia as definições de perícia do prefab do jogador.
- `CompanionManager` aplica a **perda de perícia por morte** e encerra os efeitos de comida **antes**
  do snapshot de morte.
- `CompanionHarvest` ganhou `NotifyWaiting(reason)` (espera não é travamento: o nó não é vetado).
- Versão do plugin → **0.4.1**.

> **Referência (MIT):** a lógica de comida/stamina/perícias segue
> [Offline Companions Reborn](https://github.com/MaddhattrRocks/Offline-Companions-Reborn)
> (`src/Companions/Companion/CompanionFood.cs`, `CompanionStamina.cs`, `CompanionSkills.cs`,
> `src/Companions/Patches/StaminaPatches.cs`, `SkillPatches.cs`), com atribuição em
> [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md). A implementação aqui é própria (adaptada ao
> modelo de estados do `CompanionAI`, ao snapshot JSON do `CompanionStateStore` e às seções de
> config em PT-BR), sem cópia de arquivo.

---

## [0.4.0] — 2026-10-14

### Adicionado
- **Catar os drops das criaturas que o companheiro mesmo abateu** (Fase 2.12, item #4 da
  [auditoria](docs/audit-vs-offline-companions.md)). Antes, matar um cervo/javali deixava o couro,
  as penas e os ossos no chão para sempre — agora ele **volta ao corpo e cata** o que aquele abate
  gerou:
  - **Detecção (patch observador):** prefix Harmony em `Character.OnDeath` lê o `Character.m_lastHit`
    do falecido — o **último golpe** que ele levou (o mesmo campo que o próprio jogo usa para
    creditar o abate: `m_lastHit?.GetAttacker()`) — e, se o atacante tem um `CompanionAI`, enfileira
    o **ponto do abate**. O `Attack.DoAttack` preenche `HitData.m_attacker` com o ZDOID de quem
    golpeou (`hitData.SetAttacker(m_character)`), então `GetAttacker()` resolve o companheiro sem
    precisarmos manter nenhum "último atacante" nosso. Patch **barato** (sai na primeira comparação
    na esmagadora maioria das mortes), **`try/catch`** (nunca lança) e só age no **dono do ZDO**
    (`IsLocallyOwned`).
  - **Só o que aquele abate gerou:** no instante da morte é fotografado o conjunto de `ItemDrop` que
    **já estava no chão** em volta (`Before`); a varredura só considera itens **fora** dessa foto —
    nada de varrer a tralha que o jogador deixou ali.
  - **Espera os itens saírem do corpo:** os drops de criatura não nascem na morte — o
    `Character.OnDeath` cria um **ragdoll** com a lista de drops salva no ZDO e é o
    `Ragdoll.DestroyNow → SpawnLoot` que instancia os itens alguns segundos depois. O companheiro vai
    ao ponto do abate, **segue o corpo** (ragdoll que escorrega ribanceira abaixo, sondagem 1×/s) e
    **espera** (`Loot/TempoEspera`, padrão 15 s) antes de declarar "nada caiu".
  - **Como pega:** o item entra no follow do motor nativo (pathfinding) e o companheiro usa o mesmo
    caminho do jogador: `ItemDrop.RequestOwn()` quando não é dono do item e
    `Humanoid.Pickup(go, autoequip: false, autoPickupDelay: false)`. Só itens com
    `ItemDrop.m_autoPickup` (o mesmo filtro do auto-pickup do jogador) — item dropado à mão pelo
    jogador **não** é roubado.
  - **Limites:** raio (`Loot/RaioCatar`, 8 m), distância máxima do abate ao companheiro **e** ao
    jogador (`Loot/DistanciaMaxima`, 30 m), fila (`Loot/LimiteFila`, 6 — ao encher, o abate mais
    antigo é abandonado), espera (`Loot/TempoEspera`, 15 s), teto de tempo por abate
    (`Loot/TempoMaximo`, 45 s), peso opcional (`Loot/PesoMaximo`, 0 = sem limite) e guardas de
    travamento (não chegou ao corpo em 6 s; item inalcançável em 12 s). Itens presos no piche ou
    submersos são descartados.
  - **Prioridade preservada:** **combate > coleta > loot/follow**. Com alvo hostil ou com tarefa de
    coleta ativa o abate volta para a **frente da fila** (`CompanionLoot.Pause`) — o relógio dele
    para junto, então ele não "expira" esperando a vez — e o loot toma o frame só quando o
    companheiro está livre. Novo `MovementOwner.Loot` garante o **dono único do movimento por frame**
    (as guardas de porta/barranco não brigam com ele).
  - **Log deduplicado** (mesma política da 0.3.4): `Loot: o companheiro abateu 'Javali' (3 item(ns)
    já estavam no chão ali); 1 abate(s) para catar.` → `Loot: indo catar o que 'Javali' deixou a
    14 m` → `Loot: +1x 'LeatherScraps'.` → `Loot: fim do abate de 'Javali' (peguei tudo que caiu)`.
- Config `[Loot]` (7 opções, todas em PT-BR): `CatarDropsDeCriatura`, `RaioCatar`, `DistanciaMaxima`,
  `LimiteFila`, `TempoEspera`, `TempoMaximo`, `PesoMaximo`.
- Seção **"Loot de criatura"** no `README` com a receita de teste (javali/cervo e Greydwarf).

### Alterado
- `CompanionAI.UpdateAI` ganhou a camada `UpdateLoot(dt)`, que roda **depois** de `UpdateHarvest`;
  `ApproachAndWork` passou a receber o **dono do movimento** do frame (`MovementOwner.Harvest` na
  coleta, `MovementOwner.Loot` no loot) em vez de fixar `Harvest`.
- `CompanionMovement.MovementOwner` ganhou o valor `Loot` (prioridade coleta > loot > follow nativo).
- `CompanionManager` limpa o estado do loot ao **dispensar**, na **morte** do companheiro e no
  **unload** do plugin (`CompanionLoot.Shutdown()`), para a fila não sobreviver ao NPC.
- Versão do plugin → **0.4.0**.

### Notas
- **Referência (MIT, uso livre):** a feature espelha a *lógica* de `src/Companions/Jobs/MHR_Loot.cs`
  (`MHR_Loot.OnKill`/`Drive`, `MHR_Loot_OnDeath_Patch`) do
  [Offline Companions Reborn](https://github.com/MaddhattrRocks/Offline-Companions-Reborn) (ver
  `THIRD_PARTY_NOTICES.md`): fila limitada, foto do "antes" para ignorar o que já estava no chão,
  seguir o corpo e respeitar peso/distância. A implementação no ModIA é própria (adaptada ao nosso
  modelo de estados, ao dono do movimento por frame e aos buffers reaproveitados), sem cópia de
  arquivo.
- **API confirmada no `assembly_valheim.dll` 1.0.17:** `Character.OnDeath()` é `public virtual`
  (patchável), `Character.m_lastHit` é `protected HitData` (acessível pelo publicizer),
  `HitData.GetAttacker()` resolve por ZDOID, `Ragdoll.m_dropItems`/`GetAverageBodyPosition()`,
  `ItemDrop.m_autoPickup`/`CanPickup()`/`RequestOwn()`/`InTar()`, `Humanoid.Pickup(GameObject,
  bool, bool)`, `Inventory.CanAddItem(ItemData, int)`/`GetTotalWeight()` e `ItemData.GetWeight()`.
- **Sem duplicação:** o `CharacterDrop` do **alvo** (criatura normal do jogo) continua dropando
  normalmente — o companheiro apenas **cata** o que caiu. O corpo do **companheiro** continua sem
  `CharacterDrop` (o prefab o remove desde a 0.3.0).
- **Performance:** o patch de `OnDeath` é uma checagem de referência por morte; a varredura de loot
  roda a cada **0,4 s** com buffer pré-alocado (`OverlapSphereNonAlloc`, 128 colliders), a sondagem
  de corpo no máximo **1×/s** e para assim que algum item é visto; nenhuma alocação por frame e
  nenhum `Find*`/LINQ por frame. O `MovementOwner.Loot` impede `MoveTo` concorrente.
- **Fora do escopo:** multiplayer completo (o loot roda no dono do ZDO, como o resto da IA) e
  depósito em baú (item #7 da auditoria).
- **Configs novas:** se o seu `modia.companion.cfg` não tiver a seção `[Loot]`, o jogo a cria com os
  defaults ao carregar. Nenhum default existente mudou.

## [0.3.4] — 2026-10-14

### Corrigido
- **Loop infinito da coleta (bug crítico, Fase 2.11).** Com o jogador já afastado, o companheiro
  ficava preso num ciclo de **escolher/largar o mesmo nó** (~300 linhas iguais no log, a cada tick)
  e **nunca voltava a seguir o jogador**. Eram duas causas somadas:
  - a **seleção** de nó (`CompanionHarvestScanner.FindBestNode`) só olhava o raio a partir do
    **companheiro**, enquanto a **guarda** do `CompanionAI` largava o nó pela distância ao
    **jogador** (`CompanionHarvest.MaxNodeDistanceFromFollowTarget` = `Coleta/RaioVarredura` + 15 m):
    a varredura escolhia um nó que a guarda rejeitava no tick seguinte. Agora a varredura recebe o
    **âncora** (posição do jogador) + a **mesma** "coleira" e descarta esses nós (contados no
    diagnóstico como `longe do jogador`), então seleção e guarda **nunca discordam**;
  - ao largar por `jogador se afastou do nó`, o código **zerava o timer de varredura** e re-varria no
    **mesmo tick** — re-escolhendo o mesmo nó. Agora ele **volta a seguir o jogador na hora**
    (`RestoreFollowTarget`) e só procura nó novo no **próximo ciclo** de varredura;
  - se há nó do recurso, mas **todos fora da coleira**, a coleta fica **pendente** (a varredura não
    conta "varredura vazia", então a tarefa **não** é encerrada por isso) e o companheiro **segue o
    jogador** até voltar para perto de nós (`HarvestPick.LeashRejected`);
  - sem nó válido (incluindo `prefab não classificado`) o follow do jogador é sempre restaurado.
- **Spam de log da coleta.** A escolha/largada de nó agora é logada **só quando muda** (mesmo nó +
  mesmo motivo não se repetem dentro de 10 s — `CompanionHarvest.LogNodeEvent`); antes uma única
  linha repetia a cada tick.

### Alterado
- `CompanionHarvestScanner.FindBestNode` ganhou os parâmetros `anchor` (alvo do follow) e
  `anchorMaxDistance` (a "coleira"; `<= 0` desliga), aplicados na avaliação dos candidatos — e o
  `HarvestPick` ganhou `LeashRejected` (quantos candidatos ficaram fora da coleira), usado pela IA
  para pausar em vez de encerrar a tarefa.
- `CompanionAI.AcquireHarvestNode` monta a âncora a partir de `_followTarget` e usa
  `CompanionHarvest.MaxNodeDistanceFromFollowTarget` nas duas varreduras (inclusive na repetição por
  ferramenta de tier maior).
- Novo helper `CompanionHarvest.LogNodeEvent`/`MakeNodeLogKey` (escolha, largada e rastro).
- **Silenciado o aviso** `"<nome> is tamed but missing tameable or monster AI script!"`: novo patch
  `CompanionSkillPatches` (prefixo em `Character.RaiseSkill` que pula **só** o corpo do companheiro).
  Causa confirmada no decompilado: o companheiro é domado e **não tem** `Tameable` (removido de
  propósito no prefab), então `Character.RaiseSkill` — chamado pelo golpe equipado a cada machadada
  (`MeleeAttack` → `m_character.RaiseSkill`) e pelo bloqueio — caía no ramo do aviso. Para um
  personagem domado sem `Tameable` esse método base é um **no-op** (só loga), então pular o corpo do
  companheiro não muda nenhum estado do jogo: o aviso é que some. `Player` sobrescreve o método e
  não passa pelo patch; os demais personagens seguem intactos.
- Versão do plugin → **0.3.4**.

### Notas
- **Nada mudou nos comandos, configs ou defaults** — a correção é interna (seleção/estado/log).
- **Performance:** o filtro novo é um `Vector3.Distance` a mais por candidato (dentro da varredura que
  já roda a cada 0,5 s, com buffers reaproveitados); o patch de `RaiseSkill` é uma comparação de
  referências. Nada alocado por frame.

## [0.3.3] — 2026-10-13

### Adicionado
- **Proteção contra queda** (Fase 2.10, item #2 da
  [auditoria](docs/audit-vs-offline-companions.md)): antes de dar um passo — na coleta
  (`CompanionAI.ApproachAndWork`) e no follow nativo — o companheiro sonda o chão à frente
  (`Physics.SphereCast` curto, com `ZoneSystem.GetSolidHeight`/`GetGroundHeight` de reserva) e, se
  ele **cair mais que `Movimento/QuedaMaxima`** (padrão **1,2 m**), **desvia** para um lado seguro
  ou **aborta** o passo. Água funda abaixo não conta como barranco. Novas configs
  `Movimento/ProtecaoContraQueda`, `Movimento/QuedaMaxima` e `Movimento/DistanciaSondagem`.
- **Portas no caminho** (item #5): o companheiro detecta uma porta **fechada e utilizável** à frente
  (raycast curto por `Door`), abre com `Door.Interact(humanoid, hold: false, alt: false)` e **fecha
  de novo depois de passar**. Nunca abre portas com `m_keyItem` nem portas em `PrivateArea` sem
  acesso (mesmo filtro do `DoorHandler.IsValidClosedDoor` da referência). Config
  `Movimento/AbrirPortas` (padrão `true`).
- **Bloqueio/parry** (item #6): quando um inimigo hostil a menos de `Combate/RaioBloqueio`
  (padrão **8 m**) está em `InAttack()`, o companheiro vira para ele e ergue o escudo
  (`Character.m_blocking` + `Humanoid.m_blockTimer`) por `Combate/TempoBloqueio` (padrão **0,2 s**);
  depois baixa o escudo para **revidar**. Configs `Combate/BloquearAtaques`, `Combate/RaioBloqueio`
  e `Combate/TempoBloqueio`.
- **Filtro de alvo passivo/neutro** (item #14): fora do combate o companheiro ignora `AnimalAI`
  (cervo, lebre, galinha) e criaturas que só revidam/fogem/vivem na água (`m_passiveAggresive`,
  `m_fleeIfNotAlerted`, `m_avoidLand`); quem já está alertado/com alvo continua válido (o revide do
  modo Defensive não muda). Vale só com `Combate/AtacarAnimaisNeutros = false` (o padrão); ligue-o
  para caçar essa fauna.

### Alterado
- **Um dono do movimento por frame**: a coleta reivindica o frame quando dirige
  (`MovementOwner.Harvest`) e as guardas de porta/barranco do follow só agem quando o dono é o motor
  nativo (`MovementOwner.Native`). Prioridade: **combate vence a coleta** (pausando-a, como antes) e,
  entre os que andam, **coleta > follow nativo**. Novo tipo `CompanionMovement` (portas + barranco)
  e o enum `MovementOwner`.
- `CompanionAI`: `ApproachAndWork` passa por `CompanionMovement.PrepareStep` antes de `MoveTo`;
  `UpdateBlocking`/`UpdateFollowSafety` entram no tick, logo após o motor nativo; `IsHostileTarget`
  ganhou o filtro de passivos (`IsPassiveCreature`).
- `CompanionCombat.Configure` passou a receber as três configs de bloqueio.
- Versão do plugin → **0.3.3**.

### Notas
- **Referência (MIT, uso livre):** as correções #2 (queda), #5 (portas) e #6 (bloqueio) espelham a
  *lógica* de `MHR_Mover.LedgeAhead`/`DropAhead`/`DoorAhead`/`OpenDoor`/`CloseDoor`,
  `DoorHandler.IsValidClosedDoor` e `CompanionCombatAI.UpdateBlocking` do
  [Offline Companions Reborn](https://github.com/MaddhattrRocks/Offline-Companions-Reborn)
  (ver `THIRD_PARTY_NOTICES.md`). A implementação é nossa (adaptada ao modelo de dono-do-movimento e
  aos buffers reaproveitados), não é cópia de arquivo.
- **API:** o `Character.SetBlocking(bool)` citado na auditoria **não existe mais** na 1.0.17 — só o
  campo privado `Character.m_blocking` e o `Humanoid.m_blockTimer`, que escrevemos direto por
  compilarmos contra a `assembly_valheim` publicized.
- **Performance:** as sondagens (barranco/porta/bloqueio) rodam no tick da IA (~20 Hz), com
  `Physics.*NonAlloc`/`SphereCast` e buffers estáticos; nada é alocado por frame. As portas abertas
  ficam numa lista curta e são fechadas com um teste de distância, não por varredura contínua.
- **Configs novas** (nenhum default existente mudou): se o seu `modia.companion.cfg` não tiver a
  seção `[Movimento]`, o jogo a cria com os defaults ao carregar.

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
