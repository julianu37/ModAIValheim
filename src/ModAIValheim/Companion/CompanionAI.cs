using System.Collections.Generic;
using UnityEngine;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// IA do companheiro do ModIA.
    ///
    /// Deriva de <see cref="MonsterAI"/> (e não de <c>BaseAI</c>) porque no Valheim 1.0 o estado de
    /// "seguir" (<c>m_follow</c>) e os campos de alvo/agressividade vivem em <c>MonsterAI</c> —
    /// ver <c>docs/re/api-diff.md</c>, seções B1–B3.
    ///
    /// <para>
    /// <b>Fase 2.4a (combate).</b> A abordagem é <i>usar o mecanismo nativo para executar</i> e
    /// <i>assumir a escolha do alvo</i>:
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///   <see cref="UpdateAI"/> chama <c>base.UpdateAI</c> (o <c>MonsterAI.UpdateAI</c>), que faz a
    ///   perseguição com pathfinding (<c>MoveTo</c>), o <c>LookAt</c>, o <c>EquipBestWeapon</c>, o
    ///   <c>DoAttack</c> → <c>Character.StartAttack(target, charge)</c> (animação, hitbox e dano
    ///   corretos) e o <c>Follow(m_follow)</c> quando não há alvo. Nada disso é reimplementado.
    ///   </description></item>
    ///   <item><description>
    ///   Quem escolhe o alvo é <see cref="UpdateTargetSelection"/>, a cada
    ///   <c>CompanionCombat.ScanInterval</c> (0,5 s por padrão, nunca por frame), respeitando o
    ///   <see cref="CompanionMode"/>. Para que o <c>MonsterAI</c> não escolha alvo sozinho
    ///   (<c>FindEnemy</c> a cada 2–6 s, que agride tudo que ele "vê"), o timer privado
    ///   <c>m_updateTargetTimer</c> fica travado em <see cref="TargetUpdateFreeze"/>.
    ///   </description></item>
    ///   <item><description>
    ///   O alvo é escrito direto em <c>m_targetCreature</c> (+ memória de perseguição) — campos
    ///   <c>private</c> do <c>MonsterAI</c>, acessíveis por compilarmos contra a
    ///   <c>assembly_valheim</c> publicized (ver <c>api-diff.md</c>, B3). Todo esse acesso
    ///   "não-público" está isolado nesta região do arquivo, para poder ser trocado depois.
    ///   </description></item>
    /// </list>
    ///
    /// <para>
    /// <b>Nunca</b> atacamos: o jogador, criaturas <c>tamed</c>, membros da mesma facção/grupo, e
    /// construções (<c>m_attackPlayerObjects = false</c> — e o <c>MonsterAI</c> já pula o alvo
    /// estático quando <c>IsTamed()</c>). Além disso só consideramos <i>hostil</i> quem pertence a
    /// uma facção de monstro (ver <see cref="IsHostileFaction"/>).
    /// </para>
    /// </summary>
    public class CompanionAI : MonsterAI
    {
        /// <summary>Intervalo (s) entre tentativas de reaquisição do alvo de follow.</summary>
        private const float ReacquireInterval = 1f;

        /// <summary>
        /// Valor gravado em <c>m_updateTargetTimer</c> para "congelar" a re-aquisição automática do
        /// <c>MonsterAI</c>: em float32, subtrair <c>dt</c> de 1e9 não altera o valor, então o timer
        /// nunca expira e o <c>FindEnemy</c> do jogo deixa de rodar.
        /// </summary>
        private const float TargetUpdateFreeze = 1e9f;

        /// <summary>Raio de visão mínimo da IA (o default do <c>BaseAI</c> é 50 m).</summary>
        private const float MinimumViewRange = 50f;

        /// <summary>Ângulo máximo (graus) de desvio tolerado antes de golpear um recurso.</summary>
        private const float MaxStrikeAngle = 60f;

        /// <summary>
        /// Comprimento (m) do "ponto à frente" usado para reconstruir a direção do passo do follow
        /// nativo na guarda de porta/barranco (a direção vem de <c>Character.m_moveDir</c>).
        /// </summary>
        private const float FollowProbeAhead = 3f;

        /// <summary>Alcance (m) usado quando a ferramenta não informa um (default do jogo).</summary>
        private const float DefaultHarvestReach = 1.5f;

        private GameObject _followTarget;
        private Character _ownerCharacter;
        private float _reacquireTimer = ReacquireInterval;
        private float _scanTimer;

        /// <summary>Buffer reaproveitado pelas varreduras de alvo (nenhuma lista alocada em jogo).</summary>
        private readonly List<Character> _scanBuffer = new List<Character>();

        /// <summary>Buffer reaproveitado das ferramentas de golpe do companheiro (coleta).</summary>
        private readonly List<HarvestToolProfile> _harvestTools = new List<HarvestToolProfile>();

        // --- Fase 2.4b: coleta de recursos -----------------------------------
        /// <summary>True enquanto a coleta esteve ativa no frame anterior (para saber quando restaurar o follow).</summary>
        private bool _harvestWasActive;
        private float _harvestScanTimer;
        private float _harvestStrikeTimer;
        private float _harvestInteractTimer;
        private int _harvestEmptyScans;

        /// <summary>Prefab da ferramenta escolhida para o nó atual (null = mão livre).</summary>
        private string _harvestTool;

        // --- Fase 2.9: estado de coleta de drops (#1/#9) e "último nó conhecido" (#3/#16) -------
        /// <summary>True enquanto o companheiro está no estado dedicado de coleta de drops.</summary>
        private bool _collectingDrops;

        /// <summary>Drop que o companheiro está indo pegar agora (null = varrendo o ponto).</summary>
        private GameObject _dropTarget;

        /// <summary>Ponto onde o último nó foi destruído (centro da varredura de drops).</summary>
        private Vector3 _lastDestroyPos;

        private float _dropTimer;
        private float _dropScanTimer;
        private float _dropScanDelayTimer;
        private int _dropEmptyScans;

        /// <summary>Posição do último nó em trabalho (para achar a cópia "rachada" e os drops).</summary>
        private Vector3 _lastNodePosition;

        // --- Fase 2.12: catar os drops de criatura que o próprio companheiro abateu --------------
        /// <summary>True enquanto a camada de loot esteve no comando no frame anterior.</summary>
        private bool _lootWasActive;

        /// <summary>Item do abate que o companheiro está indo pegar (null = varrendo/esperando).</summary>
        private ItemDrop _lootTarget;

        private float _lootScanTimer;

        /// <summary>Falhas seguidas ao pegar (inventário cheio/peso) antes de desistir do abate.</summary>
        private int _lootPickupFailures;

        /// <summary>Intervalo (s) entre tentativas de pegar um item do abate (independente da coleta).</summary>
        private float _lootInteractTimer;

        /// <summary>Momento em que o item-alvo do loot foi escolhido (para desistir se for inalcançável).</summary>
        private float _lootTargetSince;

        /// <summary>Tempo (s) tentando alcançar um item do abate antes de desistir dele.</summary>
        private const float LootTargetTimeout = 12f;

        /// <summary>Falhas seguidas de <c>Pickup</c> que fazem desistir do abate atual.</summary>
        private const int LootPickupFailuresBeforeGiveUp = 3;

        /// <summary>Tipo do último nó em trabalho (None = não há nó para "seguir o rastro").</summary>
        private HarvestNodeKind _lastNodeKind = HarvestNodeKind.None;

        /// <summary>Varreduras seguidas sem achar nada antes de encerrar a coleta.</summary>
        private const int HarvestEmptyScansBeforeStop = 3;

        /// <summary>Intervalo (s) entre interações (colher/pegar item).</summary>
        private const float HarvestInteractInterval = 0.3f;

        // --- Fase 2.13: comida, resistência (stamina) e habilidades ---
        /// <summary>Gestor de comida do companheiro (bônus de vida/stamina + regeneração).</summary>
        private CompanionFood _food;

        /// <summary>Resistência própria do companheiro (correr/atacar/bloquear gastam).</summary>
        private CompanionStamina _stamina;

        // --- Fase 2.14: peso, depósito em baú e tool keeper ---
        /// <summary>
        /// True quando a coleta está <b>pausada por falta de espaço/carga</b> (cheio e sem baú
        /// disponível). A tarefa continua viva: ela volta sozinha quando houver espaço ou baú.
        /// </summary>
        private bool _packFullPaused;

        /// <summary>Espera (s) entre tentativas de resolver "cheio" (procurar baú de novo).</summary>
        private float _packRetryTimer;

        /// <summary>Intervalo (s) entre tentativas de resolver a carga cheia.</summary>
        private const float PackRetryInterval = 3f;

        /// <summary>Intervalo (s) da verificação de peso/espaço (nunca por frame).</summary>
        private const float PackCheckInterval = 0.5f;

        /// <summary>Tempo (s) até a próxima verificação de peso/espaço do inventário.</summary>
        private float _packCheckTimer;

        /// <summary>Resultado da última verificação: o companheiro está no teto de peso?</summary>
        private bool _packOverWeight;

        /// <summary>Resultado da última verificação: o inventário ainda tem slot livre?</summary>
        private bool _packHasSpace;

        /// <summary>
        /// A camada de depósito dirigiu o companheiro no frame anterior (para restaurar o follow
        /// quando a viagem acabar).
        /// </summary>
        private bool _depositWasActive;

        // --- Fase 2.10: dono do movimento por frame (#2/#5) e bloqueio/parry (#6) ---
        /// <summary>Quem dirige o movimento neste frame (evita <c>MoveTo</c> concorrendo).</summary>
        private MovementOwner _movementOwner;

        /// <summary>Tempo (s) que o companheiro segura o bloqueio depois de um golpe inimigo.</summary>
        private float _blockHoldTimer;

        /// <summary>Tempo (s) acumulado com um inimigo em <c>InAttack()</c> antes de erguer o escudo.</summary>
        private float _enemySwingTimer;

        /// <summary>Nosso espelho de <c>Character.m_blocking</c> (para não escrever o campo à toa).</summary>
        private bool _blocking;

        // --- Fase 2.9: coleta de drops, rocha "rachada" e tora -----------------

        /// <summary>
        /// Espera (s) antes de varrer os drops: eles levam alguns frames para nascer depois de o nó
        /// ser destruído (é o <c>_dropScanDelayTimer</c> da referência, item #1 da auditoria).
        /// </summary>
        private const float DropScanDelay = 0.5f;

        /// <summary>Teto (s) do estado de coleta de drops: nunca deixa o companheiro vagando (item #1).</summary>
        private const float DropTimeout = 5f;

        /// <summary>Varreduras vazias consecutivas de drops antes de voltar a procurar nós (item #1/#9).</summary>
        private const int DropEmptyScansBeforeStop = 5;

        /// <summary>Raio (m) da varredura de drops em volta do ponto de destruição (item #9).</summary>
        private const float DropScanRadius = 8f;

        /// <summary>Raio (m) em que procuramos a tora da árvore que acabou de cair (item #16).</summary>
        private const float TreeLogFollowRadius = 12f;

        /// <summary>Raio (m) em que procuramos a cópia "rachada" de uma rocha (item #3).</summary>
        private const float CrackedRockRadius = 2.5f;

        /// <summary>Alvo seguido atualmente (normalmente o jogador local).</summary>
        public GameObject FollowTarget => _followTarget;

        /// <summary>
        /// True quando este cliente é dono do ZDO do companheiro. Só o dono roda a IA (e portanto a
        /// coleta) — os demais clientes apenas veem o resultado replicado.
        /// </summary>
        internal bool IsLocallyOwned => m_nview != null && m_nview.IsValid() && m_nview.IsOwner();

        /// <summary>
        /// Ajustes de instância que não dependem do ZDO.
        /// Chamado depois de <see cref="MonsterAI.Awake"/> (que já criou <c>m_nview</c>,
        /// <c>m_character</c>, <c>m_animator</c> e registrou os RPCs na <c>ZNetView</c>).
        ///
        /// <c>public override</c> (e não <c>protected</c>): a base do jogo é
        /// <c>protected override void Awake()</c>, mas compilamos contra a assembly publicized,
        /// então o compilador enxerga o membro como público. O runtime aceita normalmente
        /// (ampliar a visibilidade no override é legal pelo CLR).
        /// </summary>
        public override void Awake()
        {
            base.Awake();
            ConfigureAsCompanion();
        }

        /// <summary>
        /// Define o alvo que o companheiro deve seguir.
        /// Além de guardar a referência para o nosso loop, sincroniza o campo do
        /// <c>MonsterAI</c> via <c>base.SetFollowTarget</c> (a API pública que escreve
        /// <c>m_follow</c>), para que o restante do motor de IA do jogo veja o mesmo alvo.
        /// </summary>
        public new void SetFollowTarget(GameObject go)
        {
            _followTarget = go;
            _ownerCharacter = go != null ? go.GetComponent<Character>() : null;
            base.SetFollowTarget(go);

            if (go == null)
            {
                StopMoving();
            }
        }

        /// <summary>
        /// Loop de IA do companheiro (chamado pelo <c>MonoUpdaters</c> a ~20 Hz, dt fixo de 0.05).
        /// Só o dono do ZDO age; sem alocação por frame e com guardas de null.
        /// </summary>
        public override bool UpdateAI(float dt)
        {
            if (m_character == null || m_character.IsDead())
            {
                return false;
            }

            // Sem autoridade sobre o ZDO não mexemos em movimento (o dono replica por ZSyncTransform).
            if (m_nview == null || !m_nview.IsValid() || !m_nview.IsOwner())
            {
                return false;
            }

            if (_followTarget == null)
            {
                _reacquireTimer -= dt;
                if (_reacquireTimer <= 0f)
                {
                    _reacquireTimer = ReacquireInterval;
                    TryReacquireLocalPlayer();
                }
            }

            ApplyCombatTuning();

            // Fase 2.13: comida, resistência (stamina) e habilidades. Roda antes do motor nativo
            // para que a vida máxima (bônus de comida) e a stamina deste tick já valham para o
            // combate/coleta do mesmo frame. Só no dono do ZDO (já garantido acima).
            UpdateVitals(dt);

            // Fase 2.10 (#6): baixa o escudo ANTES do motor nativo para que a lógica de ataque do
            // MonsterAI (que roda em base.UpdateAI) não fique presa no bloqueio; a camada
            // UpdateBlocking volta a erguê-lo logo depois, valendo pelo resto do frame (é quando o
            // dano do inimigo chega e o BlockAttack do jogo consulta m_blocking).
            LowerShieldForAttack();

            // Trava a re-aquisição automática do MonsterAI: quem escolhe o alvo é o nosso modo.
            m_updateTargetTimer = TargetUpdateFreeze;

            _scanTimer -= dt;
            if (_scanTimer <= 0f)
            {
                _scanTimer = CompanionCombat.ScanInterval;
                UpdateTargetSelection();
            }

            // Perseguição, ataque (animação/hit detection/dano), regen e follow nativos do jogo.
            bool result = base.UpdateAI(dt);

            // Fase 2.10: o motor nativo é o dono do movimento por padrão (follow/perseguição).
            // Cada camada abaixo reivindica o frame quando de fato dirige o companheiro — assim
            // bloqueio, coleta e guardas de porta/barranco nunca chamam MoveTo ao mesmo tempo.
            _movementOwner = MovementOwner.Native;

            // Fase 2.10 (#6): bloqueio/parry — só ergue o escudo e mira; não dirige movimento.
            UpdateBlocking(dt);

            // Fase 2.14: depósito em baú / tool keeper. Quando existe uma viagem em andamento é ela
            // quem manda no movimento (o dono do frame passa a ser MovementOwner.Deposit) — a coleta
            // e o loot só voltam quando a viagem terminar.
            UpdateDeposit(dt);

            // Fase 2.4b: a coleta roda DEPOIS do motor nativo. Quando ela está ativa é ela quem
            // manda no movimento/mira/ataque (a última escrita em SetMoveDir/SetLookDir vence) e
            // quem decide o que o MonsterAI segue.
            UpdateHarvest(dt);

            // Fase 2.12 (#4): catar o que o próprio companheiro abateu. Roda depois da coleta e só
            // quando ela NÃO está ativa (prioridade combate > coleta > loot/follow).
            UpdateLoot(dt);

            // Fase 2.10 (#2/#5): guarda o passo do follow nativo (porta/barranco) — só quando a
            // coleta não reivindicou o frame.
            UpdateFollowSafety(dt);

            // Fase 2.10 (#5): fecha as portas que abrimos, depois de passar por elas.
            CompanionMovement.TickOpenedDoors(m_character);

            return result;
        }

        /// <summary>
        /// Reaplica a identidade do companheiro (facção + domado). Idempotente; é usado depois
        /// do spawn para não depender da ordem de <c>Awake</c> dos componentes.
        /// </summary>
        public void EnsureConfigured()
        {
            ConfigureAsCompanion();
        }

        /// <summary>
        /// Camada de <b>comida</b>, <b>resistência</b> e <b>habilidades</b> do companheiro
        /// (Fase 2.13). Chamada a cada <see cref="UpdateAI"/> (≈20 Hz), sempre <b>depois</b> das
        /// guardas de dono do ZDO e <b>antes</b> do motor nativo.
        ///
        /// <para>
        /// Os três componentes são resolvidos uma única vez (guardados em campo) — nada de
        /// <c>GetComponent</c> por frame. Quem faz o trabalho pesado com intervalo são eles:
        /// a comida varre o inventário só a cada <c>Comida/IntervaloVerificacao</c> (5 s) e a
        /// stamina/queima só usam contas por tick.
        /// </para>
        /// </summary>
        private void UpdateVitals(float dt)
        {
            if (_food == null)
            {
                _food = GetComponent<CompanionFood>();
            }

            if (_stamina == null)
            {
                _stamina = GetComponent<CompanionStamina>();
            }

            if (_food != null)
            {
                _food.Tick(dt);
            }

            if (_stamina != null)
            {
                _stamina.Tick(dt);
            }
        }

        /// <summary>
        /// Reação imediata à troca de modo (chamada por <see cref="CompanionCombat.SetMode"/>):
        /// em Passive larga o alvo na hora; nos outros modos força uma nova varredura já no
        /// próximo <see cref="UpdateAI"/>.
        /// </summary>
        internal void ApplyMode(CompanionMode mode)
        {
            _scanTimer = 0f;
            ClearBlock();

            if (mode == CompanionMode.Passive)
            {
                ClearTarget("modo passivo");
                if (IsAlerted())
                {
                    SetAlerted(false);
                }
            }
        }

        /// <summary>
        /// Larga o alvo de combate atual e força uma nova varredura (usado pelo <c>modia_stop</c>:
        /// "para o que estiver fazendo e volta a me seguir").
        /// </summary>
        internal void ClearCombatTarget()
        {
            ClearTarget("comando do jogador");
            ClearBlock();
            _scanTimer = 0f;
        }

        // ---------------------------------------------------------------------
        // Reações a dano (alimenta o "revide" do modo Defensive)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Chamado pelo <c>Character.m_onDamaged</c> (registrado no <c>BaseAI.Awake</c>) quando o
        /// companheiro leva dano. No Passive desfazemos o "acordar" que o <c>MonsterAI.OnDamaged</c>
        /// faz; nos demais modos marcamos o agressor como prioridade.
        ///
        /// <c>public override</c>: a base é <c>protected</c> no jogo, mas a assembly publicized
        /// expõe como pública (mesma observação do <see cref="Awake"/>).
        /// </summary>
        public override void OnDamaged(float damage, Character attacker)
        {
            base.OnDamaged(damage, attacker);

            if (CompanionCombat.Mode == CompanionMode.Passive)
            {
                m_targetCreature = null;
                if (IsAlerted())
                {
                    SetAlerted(false);
                }

                return;
            }

            CompanionCombat.NotifyAttacker(attacker);
            _scanTimer = 0f;
        }

        /// <summary>
        /// O <c>MonsterAI</c> acorda e adota como alvo quem dispara um projétil que passa perto
        /// (é um RPC). No Passive isso não pode acontecer; nos outros modos só antecipamos a
        /// varredura.
        /// </summary>
        public override void RPC_OnNearProjectileHit(
            long sender, Vector3 center, float range, ZDOID attackerID)
        {
            if (CompanionCombat.Mode == CompanionMode.Passive)
            {
                return;
            }

            base.RPC_OnNearProjectileHit(sender, center, range, attackerID);
            _scanTimer = 0f;
        }

        // ---------------------------------------------------------------------
        // Seleção de alvo
        // ---------------------------------------------------------------------

        private void UpdateTargetSelection()
        {
            CompanionMode mode = CompanionCombat.Mode;
            Character current = GetTargetCreature();

            if (mode == CompanionMode.Passive)
            {
                ClearTarget("modo passivo");
                if (IsAlerted())
                {
                    SetAlerted(false);
                }

                return;
            }

            Character next = SelectTarget(mode, current);

            if (next == null)
            {
                ClearTarget("nenhum inimigo válido no alcance");
                return;
            }

            if (next != current)
            {
                SetCurrentTarget(next, mode);
            }
        }

        private Character SelectTarget(CompanionMode mode, Character current)
        {
            // Defensive: prioridade absoluta para quem acabou de agredir o companheiro ou o dono.
            if (mode == CompanionMode.Defensive)
            {
                Character attacker = CompanionCombat.GetRecentAttacker();
                if (attacker != null && IsTargetValid(attacker, mode, retaliation: true))
                {
                    return attacker;
                }
            }

            // O alvo atual continua valendo? Mantém (evita trocar de alvo a cada varredura).
            if (current != null && IsTargetValid(current, mode, retaliation: false))
            {
                return current;
            }

            return mode == CompanionMode.Aggressive ? FindNearestHostile() : FindHostileHuntingUs();
        }

        /// <summary>Aggressive: criatura hostil mais próxima dentro do raio configurado.</summary>
        private Character FindNearestHostile()
        {
            float radius = CompanionCombat.AggressiveRadius;
            Character best = null;
            float bestDistance = radius;

            FillScanBuffer(radius);
            for (int i = 0; i < _scanBuffer.Count; i++)
            {
                Character candidate = _scanBuffer[i];
                if (!IsHostileTarget(candidate))
                {
                    continue;
                }

                float distance = Vector3.Distance(candidate.transform.position, transform.position);
                if (distance > radius || (best != null && distance >= bestDistance))
                {
                    continue;
                }

                best = candidate;
                bestDistance = distance;
            }

            return best;
        }

        /// <summary>
        /// Defensive: criatura hostil que está <b>caçando</b> o jogador dono ou o companheiro
        /// (o <c>MonsterAI</c> de cada criatura expõe o alvo atual via
        /// <c>BaseAI.GetTargetCreature()</c>).
        /// </summary>
        private Character FindHostileHuntingUs()
        {
            float radius = CompanionCombat.DefensiveRadius;
            Character owner = _ownerCharacter;
            Character best = null;
            float bestDistance = radius;

            FillScanBuffer(radius);
            for (int i = 0; i < _scanBuffer.Count; i++)
            {
                Character candidate = _scanBuffer[i];
                if (!IsHostileTarget(candidate))
                {
                    continue;
                }

                float distance = Vector3.Distance(candidate.transform.position, transform.position);
                if (distance > radius || (best != null && distance >= bestDistance))
                {
                    continue;
                }

                if (!IsHunting(candidate, owner))
                {
                    continue;
                }

                best = candidate;
                bestDistance = distance;
            }

            return best;
        }

        /// <summary>
        /// Preenche o buffer reaproveitado com os personagens dentro do raio (o próprio jogo varre
        /// a lista estática <c>Character.s_characters</c> por nós — nada de alocar por varredura).
        /// </summary>
        private void FillScanBuffer(float radius)
        {
            _scanBuffer.Clear();
            Character.GetCharactersInRange(transform.position, radius, _scanBuffer);
        }

        private bool IsHunting(Character candidate, Character owner)
        {
            BaseAI ai = candidate.GetBaseAI();
            if (ai == null)
            {
                return false;
            }

            Character aiTarget = ai.GetTargetCreature();
            if (aiTarget == null)
            {
                return false;
            }

            return aiTarget == m_character || (owner != null && aiTarget == owner);
        }

        private bool IsTargetValid(Character target, CompanionMode mode, bool retaliation)
        {
            if (!IsHostileTarget(target))
            {
                return false;
            }

            float radius;
            if (retaliation)
            {
                // Quem acabou de nos agredir pode estar um pouco mais longe (ataques à distância).
                radius = CompanionCombat.LeashDistance;
            }
            else if (mode == CompanionMode.Aggressive)
            {
                radius = CompanionCombat.AggressiveRadius;
            }
            else
            {
                radius = CompanionCombat.DefensiveRadius;
            }

            if (Vector3.Distance(target.transform.position, transform.position) > radius)
            {
                return false;
            }

            // "Coleira": não perseguir quem se afastou demais do jogador seguido.
            if (_followTarget != null &&
                Vector3.Distance(target.transform.position, _followTarget.transform.position) >
                CompanionCombat.LeashDistance)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Filtro de alvo: só criaturas <b>hostis</b>. Exclui explicitamente o jogador, criaturas
        /// domadas, o próprio dono, quem o jogo marcou para ignorar (<c>m_aiSkipTarget</c>) e a
        /// fauna neutra; e delega ao próprio jogo o filtro de facção/grupo (<c>IsEnemy</c>).
        /// </summary>
        private bool IsHostileTarget(Character target)
        {
            if (target == null || target == m_character)
            {
                return false;
            }

            if (target.IsDead())
            {
                return false;
            }

            if (target.IsPlayer())
            {
                return false;
            }

            if (target.IsTamed())
            {
                return false;
            }

            if (target.m_aiSkipTarget)
            {
                return false;
            }

            if (_followTarget != null && target.gameObject == _followTarget)
            {
                return false;
            }

            // Fase 2.10 (#14): fora do combate, ignora a fauna passiva/neutra (AnimalAI, ou
            // criaturas que só revidam / fogem / vivem na água). Só vale quando o jogador NÃO
            // pediu para atacar neutros; quem já está engajado (alertado/com alvo) continua válido,
            // preservando o revide do modo Defensive.
            if (!CompanionCombat.AttackNeutralAnimals && IsPassiveCreature(target))
            {
                return false;
            }

            if (!IsHostileFaction(target.GetFaction()))
            {
                return false;
            }

            // IsEnemy (do BaseAI) cuida de grupo/mesma facção/domado com as regras do jogo.
            return IsEnemy(target);
        }

        /// <summary>
        /// Criatura passiva/neutra <b>fora de combate</b> (item #14): <c>AnimalAI</c> (cervo, lebre,
        /// galinha), ou <c>MonsterAI</c>/<c>BaseAI</c> que só revida (<c>m_passiveAggresive</c>),
        /// evita terra (<c>m_avoidLand</c>) ou foge enquanto não é alertada
        /// (<c>m_fleeIfNotAlerted</c>). Quem já tem alvo ou está alertado não conta como passivo —
        /// aí é uma luta de verdade.
        /// </summary>
        private static bool IsPassiveCreature(Character target)
        {
            BaseAI ai = target.GetBaseAI();
            if (ai == null)
            {
                return false;
            }

            if (ai.GetTargetCreature() != null || ai.IsAlerted())
            {
                return false;
            }

            if (ai is AnimalAI || ai.m_passiveAggresive)
            {
                return true;
            }

            MonsterAI monster = ai as MonsterAI;
            return monster != null && (monster.m_avoidLand || monster.m_fleeIfNotAlerted);
        }

        /// <summary>
        /// Facções que o companheiro considera hostis. A fauna neutra (<c>AnimalsVeg</c>) só entra
        /// se a config mandar; jogadores, dverger, invocações de jogador e bonecos de treino nunca.
        /// </summary>
        private static bool IsHostileFaction(Character.Faction faction)
        {
            switch (faction)
            {
                case Character.Faction.ForestMonsters:
                case Character.Faction.Undead:
                case Character.Faction.Demon:
                case Character.Faction.MountainMonsters:
                case Character.Faction.SeaMonsters:
                case Character.Faction.PlainsMonsters:
                case Character.Faction.MistlandsMonsters:
                case Character.Faction.DeepNorth:
                case Character.Faction.Boss:
                    return true;
                case Character.Faction.AnimalsVeg:
                    return CompanionCombat.AttackNeutralAnimals;
                default:
                    return false;
            }
        }

        // ---------------------------------------------------------------------
        // Alvo: escrita (campos privados do MonsterAI — assembly publicized)
        // ---------------------------------------------------------------------

        private void SetCurrentTarget(Character target, CompanionMode mode)
        {
            m_targetCreature = target;
            m_targetStatic = null;
            m_lastKnownTargetPos = target.transform.position;
            m_beenAtLastPos = false;
            m_timeSinceSensedTargetCreature = 0f;
            m_timeSinceAttacking = 0f;

            // O MonsterAI só chega a DoAttack com IsAlerted() == true.
            SetAlerted(true);

            Plugin.Log.LogInfo(
                $"[ModIA] Companheiro: alvo adquirido '{target.m_name}' " +
                $"({target.GetFaction()}, HP {target.GetHealthPercentage() * 100f:0}%) a " +
                $"{Vector3.Distance(target.transform.position, transform.position):0.#} m " +
                $"[modo {mode}].");

            WarnIfUnarmed();
        }

        /// <summary>
        /// Sem arma o <c>MonsterAI</c> cai no ramo "sem itemData" e volta a seguir o jogador em vez
        /// de atacar — vale avisar no log (o loadout padrão dá um <c>Club</c>).
        /// </summary>
        private void WarnIfUnarmed()
        {
            Humanoid humanoid = m_character as Humanoid;
            if (humanoid != null && humanoid.GetCurrentWeapon() == null)
            {
                Plugin.Log.LogWarning(
                    "[ModIA] Companheiro está sem arma: ele persegue o alvo mas não consegue " +
                    "atacar. Equipe algo (modia_equip <prefab>) ou configure WeaponPrefab.");
            }
        }

        private void ClearTarget(string reason)
        {
            Character lost = m_targetCreature;
            if (lost == null)
            {
                return;
            }

            m_targetCreature = null;
            m_targetStatic = null;
            m_timeSinceAttacking = 0f;
            m_timeSinceSensedTargetCreature = 0f;
            SetAlerted(false);

            Plugin.Log.LogInfo($"[ModIA] Companheiro: alvo perdido '{lost.m_name}' ({reason}).");
        }

        // ---------------------------------------------------------------------
        // Fase 2.14: depósito em baú (peso/inventário cheio) e tool keeper
        // ---------------------------------------------------------------------

        /// <summary>
        /// Camada de <b>depósito em baú</b> (Fase 2.14, item #7 da auditoria comparativa vs
        /// <i>Offline Companions Reborn</i>, MIT — ver <c>THIRD_PARTY_NOTICES.md</c>).
        ///
        /// <para>
        /// Enquanto existe uma viagem em andamento (<see cref="CompanionDeposit.TripActive"/>) é ela
        /// quem dirige o companheiro: anda até o baú, faz o trabalho (depositar a carga, buscar a
        /// ferramenta que falta ou devolver a emprestada) e encerra a viagem — a coleta continua
        /// <b>viva</b> e retoma sozinha no frame seguinte.
        /// </para>
        ///
        /// <para>
        /// <b>Prioridade:</b> o combate vence (com alvo hostil o companheiro não anda até o baú, mas a
        /// viagem continua viva e tem prazo próprio). Como o dono do movimento por frame passa a ser
        /// <see cref="MovementOwner.Deposit"/>, a coleta e o loot não brigam pelo passo.
        /// </para>
        /// </summary>
        private void UpdateDeposit(float dt)
        {
            if (!CompanionDeposit.TripActive)
            {
                if (_depositWasActive)
                {
                    _depositWasActive = false;

                    // Só devolve o follow quando a coleta não está no comando (ela usa o nó como
                    // alvo do motor nativo e o restaura sozinha).
                    if (!CompanionHarvest.IsActive)
                    {
                        RestoreFollowTarget();
                    }
                }

                return;
            }

            _depositWasActive = true;

            if (GetTargetCreature() != null)
            {
                StopMoving();
                return;
            }

            Humanoid humanoid = m_character as Humanoid;
            Inventory inventory = humanoid != null ? humanoid.GetInventory() : null;
            if (humanoid == null || inventory == null)
            {
                CompanionDeposit.Cancel("companheiro sem inventário");
                return;
            }

            Container chest = CompanionDeposit.Chest;
            if (chest == null)
            {
                CompanionDeposit.Cancel("o baú sumiu");
                return;
            }

            if (CompanionDeposit.IsStuck())
            {
                CompanionDeposit.Cancel("não consegui chegar ao baú");
                return;
            }

            Vector3 point = chest.transform.position;
            float distance = Vector3.Distance(point, transform.position);
            CompanionDeposit.ReportDistance(distance);

            if (distance > CompanionDeposit.UseReach)
            {
                ApproachAndWork(dt, point, CompanionDeposit.UseReach, MovementOwner.Deposit);
                return;
            }

            StopMoving();
            LookAt(point);

            switch (CompanionDeposit.TripKind)
            {
                case ChestTripKind.Deposit:
                    WorkAtChestDeposit(inventory);
                    break;

                case ChestTripKind.FetchTool:
                    WorkAtChestFetchTool(inventory);
                    break;

                case ChestTripKind.ReturnTools:
                    WorkAtChestReturnTools(inventory, chest);
                    break;
            }
        }

        /// <summary>No baú: guarda os materiais e encerra a viagem (a coleta retoma).</summary>
        private void WorkAtChestDeposit(Inventory inventory)
        {
            int moved = CompanionDeposit.DepositResources(inventory);
            CompanionDeposit.RegisterMoved(moved);

            if (moved > 0)
            {
                CompanionHarvest.ShowMessage(
                    $"Carga guardada no baú ({moved} item(ns))", MessageHud.MessageType.TopLeft);
            }

            _packFullPaused = false;
            _packOverWeight = false;
            _packCheckTimer = 0f; // reavalia peso/espaço já no próximo tick (a carga acabou de mudar)
            CompanionDeposit.End(moved > 0
                ? $"guardei {moved} item(ns)"
                : "não havia nada para guardar");
        }

        /// <summary>No baú: pega a ferramenta que faltava e encerra a viagem (tool keeper).</summary>
        private void WorkAtChestFetchTool(Inventory inventory)
        {
            string prefab = CompanionDeposit.Prefab;
            Container chest = CompanionDeposit.Chest;

            bool taken = CompanionDeposit.TakeItemFromChest(inventory, prefab);
            if (taken)
            {
                CompanionToolKeeper.NoteBorrowed(chest, prefab);
                CompanionDeposit.RegisterMoved(1);
                CompanionHarvest.ShowMessage(
                    $"Ferramenta emprestada: {prefab}", MessageHud.MessageType.TopLeft);
            }

            CompanionDeposit.End(taken ? $"peguei '{prefab}'" : $"não achei '{prefab}' no baú");
        }

        /// <summary>No baú: devolve as ferramentas emprestadas que não usa mais (tool keeper).</summary>
        private void WorkAtChestReturnTools(Inventory inventory, Container chest)
        {
            int moved = CompanionDeposit.ReturnBorrowedTools(inventory, chest);
            CompanionDeposit.RegisterMoved(moved);
            CompanionDeposit.End(moved > 0 ? $"devolvi {moved} ferramenta(s)" : "nada para devolver");
        }

        /// <summary>
        /// True quando a coleta deve <b>esperar</b> neste frame por causa de carga/inventário cheio:
        /// ou existe uma viagem ao baú em andamento, ou o companheiro está pausado (cheio e sem baú).
        ///
        /// <para>
        /// A verificação de peso roda no máximo a cada <see cref="PackCheckInterval"/> (nada de somar
        /// o peso do inventário por frame) e a varredura de baú tem espera própria. Nada aqui encerra
        /// a tarefa: ela volta sozinha quando houver espaço (ou um baú).
        /// </para>
        /// </summary>
        private bool UpdatePackFull(float dt)
        {
            if (CompanionDeposit.TripActive)
            {
                return true;
            }

            _packCheckTimer -= dt;
            if (_packCheckTimer <= 0f)
            {
                _packCheckTimer = PackCheckInterval;

                Humanoid humanoid = m_character as Humanoid;
                Inventory inventory = humanoid != null ? humanoid.GetInventory() : null;
                _packOverWeight = CompanionInventory.IsOverWeight(inventory);
                _packHasSpace = inventory != null && inventory.HaveEmptySlot();

                if (_packFullPaused && !_packOverWeight && _packHasSpace)
                {
                    _packFullPaused = false;
                    Plugin.Log.LogInfo("[ModIA] Coleta: carga e espaço liberados; retomando a coleta.");
                }
            }

            if (!_packFullPaused && !_packOverWeight)
            {
                return false;
            }

            if (!CompanionDeposit.Enabled)
            {
                // Depósito desligado: comportamento antigo (o pickup avisa/falha).
                return false;
            }

            _packRetryTimer -= dt;
            if (_packFullPaused && _packRetryTimer > 0f)
            {
                CompanionHarvest.NotifyBusy();
                RestoreFollowTarget();
                return true;
            }

            _packRetryTimer = PackRetryInterval;

            Humanoid body = m_character as Humanoid;
            Inventory bodyInventory = body != null ? body.GetInventory() : null;
            if (CompanionDeposit.TryStartDepositTrip(this, bodyInventory))
            {
                _packFullPaused = false;
                return true;
            }

            if (!_packFullPaused)
            {
                _packFullPaused = true;
                CompanionHarvest.NotifyPackFull(
                    _packOverWeight ? "carga acima do limite" : "inventário sem espaço");
            }

            CompanionHarvest.NotifyBusy();
            RestoreFollowTarget();
            return true;
        }

        /// <summary>
        /// Não caberia (espaço ou peso): tenta levar a carga ao baú; sem baú, <b>pausa</b> a coleta e
        /// avisa no HUD — a tarefa continua viva (nunca mais o "não consegui pegar o item" encerra a
        /// coleta, como acontecia antes da Fase 2.14).
        /// </summary>
        private void HandlePackFull(Inventory inventory, ItemDrop.ItemData item)
        {
            if (CompanionDeposit.Enabled && CompanionDeposit.TryStartDepositTrip(this, inventory))
            {
                return;
            }

            _packFullPaused = true;
            _packRetryTimer = PackRetryInterval;
            CompanionHarvest.NotifyBusy();
            CompanionHarvest.NotifyPackFull(item != null && item.m_dropPrefab != null
                ? $"não cabe '{Utils.GetPrefabName(item.m_dropPrefab)}'"
                : "inventário sem espaço/carga");
        }

        // ---------------------------------------------------------------------
        // Coleta de recursos (Fase 2.4b)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Camada de coleta do companheiro. É chamada a cada <see cref="UpdateAI"/> (≈20 Hz) e só
        /// faz algo quando existe tarefa ativa (<see cref="CompanionHarvest.IsActive"/>).
        ///
        /// <para>
        /// <b>Como anda e ataca:</b> o nó é colocado no campo de follow do <c>MonsterAI</c>
        /// (<c>base.SetFollowTarget</c>), então o próprio motor nativo faz a perseguição com
        /// pathfinding (<c>Follow</c>). Por cima disso apertamos o raio de parada com
        /// <c>MoveTo</c> (o <c>Follow</c> nativo para em 3 m, longe demais para a hitbox do
        /// machado) e disparamos o golpe com <c>Character.StartAttack(null, false)</c> — a
        /// direção/hitbox/dano são resolvidos pelo ataque equipado, exatamente como quando o
        /// jogador bate numa árvore.
        /// </para>
        ///
        /// <para>
        /// <b>Como colhe e pega:</b> <c>Pickable.Interact</c> (o RPC interno gera os drops) e
        /// <c>Humanoid.Pickup</c> para os itens no chão. Nada aqui atravessa a main thread com
        /// espera: tudo é uma chamada curta por tick, com intervalos e buffers reaproveitados.
        /// </para>
        /// </summary>
        private void UpdateHarvest(float dt)
        {
            if (!CompanionHarvest.IsActive)
            {
                if (_harvestWasActive)
                {
                    ResetHarvestState();
                }

                return;
            }

            _harvestWasActive = true;
            _harvestScanTimer -= dt;
            _harvestStrikeTimer -= dt;
            _harvestInteractTimer -= dt;

            // 1) Combate x coleta: em Defensive/Aggressive o alvo hostil tem prioridade e a coleta
            //    é pausada (a tarefa continua viva e volta quando o combate acabar). No Passive não
            //    existe alvo, então a coleta segue em frente.
            if (CompanionHarvest.PauseOnCombat && GetTargetCreature() != null)
            {
                CompanionHarvest.PauseForCombat();
                ForgetNode();
                RestoreFollowTarget();
                return;
            }

            CompanionHarvest.ResumeFromCombat();

            // 1b) Fase 2.14: carga/inventário cheio — leva a carga ao baú e VOLTA (a tarefa nunca é
            //     encerrada por isso). Sem baú por perto ele PAUSA e avisa no HUD.
            if (UpdatePackFull(dt))
            {
                return;
            }

            // 2) Estado dedicado de COLETA DE DROPS (Fase 2.9, itens #1/#9). Depois de destruir um
            //    nó os drops levam alguns frames para nascer: em vez de contar uma varredura vazia na
            //    hora (e encerrar a tarefa em ~1,5 s), esperamos e varremos o ponto de destruição.
            //    Enquanto ele está no comando, não procuramos nós novos.
            if (_collectingDrops && UpdateDropCollection(dt))
            {
                return;
            }

            // 3) O nó atual ainda existe/serve? (destruído, colhido, tipo errado, longe do jogador)
            GameObject node = CompanionHarvest.Node;
            if (node != null && !CompanionHarvestScanner.IsStillValidNode(node, CompanionHarvest.ResourceName))
            {
                CompanionHarvest.ClearNode("nó destruído ou já esgotado");
                node = null;
                _harvestScanTimer = 0f;
            }

            // 4) O nó que estávamos trabalhando sumiu: pode ter sido destruído (os drops vêm a
            //    caminho), trocado por uma cópia "rachada" no mesmo ponto (#3) ou derrubado (a tora
            //    entrou no lugar, #16). Resolve isso antes de sair varrendo de novo.
            if (node == null && _lastNodeKind != HarvestNodeKind.None)
            {
                node = ResolveGoneNode();
                if (node == null)
                {
                    // Nada para continuar ali: a coleta de drops (ou a próxima varredura) assume.
                    RestoreFollowTarget();
                    return;
                }
            }

            if (node != null && _followTarget != null &&
                Vector3.Distance(node.transform.position, _followTarget.transform.position) >
                CompanionHarvest.MaxNodeDistanceFromFollowTarget)
            {
                CompanionHarvest.ClearNode("jogador se afastou do nó");
                ForgetNode();
                node = null;

                // A "coleira" venceu: o companheiro volta a seguir o jogador AGORA e só procura nó
                // novo no próximo ciclo de varredura (o timer > 0 faz o passo 6 devolver o follow).
                //
                // Zerar o timer aqui (como era antes) re-escolhia o MESMO nó no mesmo tick — a
                // guarda largava de novo no tick seguinte e a coleta entrava em ciclo: o companheiro
                // ficava preso batendo/largando longe do jogador, sem nunca voltar a segui-lo, e o
                // log repetia a mesma linha a cada tick (bug do loop da coleta, v0.3.4).
                _harvestScanTimer = CompanionHarvest.ScanInterval;
                RestoreFollowTarget();
            }

            // 5) Travou? (não chega, bate e não sai dano, construção no caminho…)
            if (node != null && CompanionHarvest.ShouldGiveUpNode(out string stuckReason))
            {
                CompanionHarvest.BlacklistNode(node, stuckReason);
                ForgetNode();
                node = null;
                _harvestScanTimer = 0f;
                _harvestTool = null;
            }

            // 6) Sem nó: segue o jogador enquanto espera a próxima varredura.
            if (node == null)
            {
                if (_harvestScanTimer > 0f)
                {
                    RestoreFollowTarget();
                    return;
                }

                _harvestScanTimer = CompanionHarvest.ScanInterval;
                node = AcquireHarvestNode();
                if (node == null)
                {
                    RestoreFollowTarget();
                    return;
                }
            }

            if (!CompanionHarvestScanner.TryGetInfo(node, out HarvestPrefabInfo info))
            {
                CompanionHarvest.ClearNode("prefab não classificado");
                ForgetNode();
                _harvestScanTimer = 0f;
                RestoreFollowTarget();
                return;
            }

            Vector3 nodePosition = node.transform.position;

            // "Último nó conhecido": se ele sumir (destruído ou trocado), é daqui que procuramos a
            // cópia "rachada" (#3), a tora da árvore (#16) e os drops (#1/#9).
            _lastNodeKind = info.Kind;
            _lastNodePosition = nodePosition;

            float distance = Vector3.Distance(nodePosition, transform.position);

            if (info.Kind == HarvestNodeKind.ItemDrop)
            {
                CompanionHarvest.ReportDistance(node, distance);
                ApproachAndWork(dt, nodePosition + Vector3.up * 0.6f, CompanionHarvest.InteractReach, MovementOwner.Harvest);
                if (distance <= CompanionHarvest.InteractReach)
                {
                    PickupDropNode(node);
                }

                return;
            }

            if (info.Kind == HarvestNodeKind.Pickable)
            {
                CompanionHarvest.ReportDistance(node, distance);
                ApproachAndWork(dt, nodePosition + Vector3.up * 0.6f, CompanionHarvest.InteractReach, MovementOwner.Harvest);
                if (distance <= CompanionHarvest.InteractReach)
                {
                    HarvestPickableNode(node);
                }

                return;
            }

            // Fonte (árvore/tora/pedra/entulho): mira na superfície mais próxima do nó — a raiz de
            // uma rocha grande pode estar no centro, longe da hitbox do golpe. O progresso passa a
            // ser medido pela distância à superfície (a distância à raiz não cai ao chegar nela).
            Vector3 aim = CompanionHarvestScanner.GetAimPoint(node, info, transform.position);
            float aimDistance = Vector3.Distance(aim, transform.position);
            CompanionHarvest.ReportDistance(node, aimDistance);

            float reach = GetHarvestReach();
            ApproachAndWork(dt, aim, reach, MovementOwner.Harvest);
            if (aimDistance <= reach)
            {
                StrikeNode(node, aim);
            }
        }

        /// <summary>
        /// Zera o estado da camada de coleta quando a tarefa deixa de estar ativa (ou é reiniciada).
        /// </summary>
        private void ResetHarvestState()
        {
            _harvestWasActive = false;
            _harvestScanTimer = 0f;
            _harvestStrikeTimer = 0f;
            _harvestInteractTimer = 0f;
            _harvestEmptyScans = 0;
            _harvestTool = null;
            _collectingDrops = false;
            _dropTarget = null;
            _dropTimer = 0f;
            _dropScanTimer = 0f;
            _dropScanDelayTimer = 0f;
            _dropEmptyScans = 0;
            ForgetNode();
            RestoreFollowTarget();

            // Fase 2.14: a tarefa acabou — zera o estado de "cheio" e devolve ao baú a ferramenta
            // emprestada que ele não usa mais (tool keeper). A viagem é dirigida pela camada de
            // depósito, então nada aqui segura o companheiro.
            _packFullPaused = false;
            _packRetryTimer = 0f;
            _packCheckTimer = 0f;
            _packOverWeight = false;

            Humanoid humanoid = m_character as Humanoid;
            Inventory inventory = humanoid != null ? humanoid.GetInventory() : null;
            CompanionDeposit.TryStartReturnTrip(this, inventory);
        }

        /// <summary>
        /// Esquece o "último nó conhecido". Usado quando o nó é largado por <b>decisão nossa</b>
        /// (combate, jogador se afastou, travamento, nó consumido) — e não porque foi destruído:
        /// nesse caso o rastro (<see cref="ResolveGoneNode"/>) é justamente o que interessa.
        /// </summary>
        private void ForgetNode()
        {
            _lastNodeKind = HarvestNodeKind.None;
            _lastNodePosition = Vector3.zero;
        }

        /// <summary>
        /// O nó que o companheiro estava trabalhando sumiu (destruído, colhido ou trocado pelo
        /// jogo). Tenta continuar no <b>mesmo lugar</b>, na ordem:
        /// <list type="number">
        ///   <item><description><b>#3 — rocha "rachada"</b>: no 1º golpe o jogo troca muitos nós por
        ///   uma cópia no mesmo ponto (<c>Destructible.m_spawnWhenDamaged</c>). Sem isto o companheiro
        ///   lia "destruído", saía varrendo e dava <i>um golpe por pedra</i>.</description></item>
        ///   <item><description><b>#16 — tora da árvore</b>: a árvore derrubada gera a tora onde caiu
        ///   e é nela que a madeira está; a tora partida gera as toras menores. Mirar a mais próxima
        ///   evita andar a esmo e preserva a cascata <c>TreeBase → TreeLog → m_subLogPrefab</c>.</description></item>
        ///   <item><description><b>#1/#9 — drops</b>: sem nada para continuar, entra no estado de
        ///   coleta de drops, que espera eles nascerem antes de contar varredura vazia.</description></item>
        /// </list>
        /// </summary>
        /// <returns>Nó substituto, ou <c>null</c> quando a coleta de drops (ou a próxima varredura) assume.</returns>
        private GameObject ResolveGoneNode()
        {
            HarvestNodeKind kind = _lastNodeKind;
            Vector3 position = _lastNodePosition;
            _lastNodeKind = HarvestNodeKind.None;

            // #3 — rocha/entulho "rachado" no mesmo ponto.
            if (kind == HarvestNodeKind.MineRock ||
                kind == HarvestNodeKind.MineRock5 ||
                kind == HarvestNodeKind.Destructible)
            {
                GameObject cracked = CompanionHarvestScanner.FindCrackedNode(
                    position, CompanionHarvest.ResourceName, CrackedRockRadius);
                if (cracked != null)
                {
                    CompanionHarvest.LogNodeEvent(
                        CompanionHarvest.MakeNodeLogKey("trace", cracked, kind.ToString()),
                        $"[ModIA] Coleta: '{kind}' trocado por uma cópia no mesmo ponto; " +
                        $"continuando em '{cracked.name}'.");
                    CompanionHarvest.SetNode(cracked);
                    return cracked;
                }
            }

            // #16 — árvore derrubada: mira a tora mais próxima. Vale também para a tora que foi
            // partida (o jogo gera as toras menores — `TreeLog.m_subLogPrefab`), preservando a
            // cascata TreeBase -> TreeLog -> toras menores, que é o que faz a árvore render madeira.
            if (kind == HarvestNodeKind.TreeBase || kind == HarvestNodeKind.TreeLog)
            {
                GameObject log = CompanionHarvestScanner.FindNearestTreeLog(
                    transform.position, CompanionHarvest.ResourceName, TreeLogFollowRadius);
                if (log != null)
                {
                    CompanionHarvest.LogNodeEvent(
                        CompanionHarvest.MakeNodeLogKey("trace", log, kind.ToString()),
                        $"[ModIA] Coleta: '{kind}' partido; mirando a tora mais próxima '{log.name}'.");
                    CompanionHarvest.SetNode(log);
                    return log;
                }
            }

            // #1/#9 — nada para continuar: os drops levam alguns frames para nascer.
            if (CompanionHarvest.CollectDrops && kind != HarvestNodeKind.ItemDrop)
            {
                BeginDropCollection(position);
            }

            return null;
        }

        /// <summary>Entra no estado dedicado de coleta de drops em volta do ponto de destruição.</summary>
        private void BeginDropCollection(Vector3 position)
        {
            _collectingDrops = true;
            _dropTarget = null;
            _lastDestroyPos = position;
            _dropTimer = 0f;
            _dropScanTimer = 0f;
            _dropScanDelayTimer = DropScanDelay;
            _dropEmptyScans = 0;
        }

        /// <summary>
        /// Sai do estado de coleta de drops e devolve o comando à busca de nós (com o contador de
        /// varreduras vazias zerado: a espera pelos drops não pode contar como "acabou o recurso").
        /// </summary>
        private void EndDropCollection(string reason)
        {
            if (!_collectingDrops)
            {
                return;
            }

            _collectingDrops = false;
            _dropTarget = null;
            _dropEmptyScans = 0;
            _harvestEmptyScans = 0;
            _harvestScanTimer = 0f;
            Plugin.Log.LogInfo(
                $"[ModIA] Coleta: fim da coleta de drops ({reason}); voltando a procurar nós.");
        }

        /// <summary>
        /// Estado de <b>coleta de drops</b> (itens #1/#9). Espera <see cref="DropScanDelay"/> (os
        /// drops ainda estão nascendo), varre o ponto de destruição em <see cref="DropScanRadius"/>
        /// atrás de <c>ItemDrop</c> do recurso pedido e vai pegando um por um. Termina por
        /// <see cref="DropEmptyScansBeforeStop"/> varreduras vazias consecutivas <b>ou</b> por
        /// <see cref="DropTimeout"/> segundos — sempre com limite, para não vagar para sempre.
        /// </summary>
        /// <returns><c>true</c> enquanto a coleta de drops está no comando do companheiro.</returns>
        private bool UpdateDropCollection(float dt)
        {
            _dropTimer += dt;

            if (_dropScanDelayTimer > 0f)
            {
                _dropScanDelayTimer -= dt;
                RestoreFollowTarget();
                return true;
            }

            if (_dropTarget == null || !IsPickableDrop(_dropTarget))
            {
                if (_dropTarget != null)
                {
                    // Alvo inválido (item preso no piche, sumiu): veta para não varrer de novo.
                    CompanionHarvestScanner.Blacklist(_dropTarget, CompanionHarvest.BlacklistSeconds);
                    _dropTarget = null;
                }

                _dropScanTimer -= dt;
                if (_dropScanTimer > 0f)
                {
                    RestoreFollowTarget();
                    return true;
                }

                _dropScanTimer = CompanionHarvest.ScanInterval;
                _dropTarget = CompanionHarvestScanner.FindNearestDrop(
                    _lastDestroyPos, CompanionHarvest.ResourceName, DropScanRadius);

                if (_dropTarget == null)
                {
                    _dropEmptyScans++;
                    if (_dropEmptyScans < DropEmptyScansBeforeStop && _dropTimer < DropTimeout)
                    {
                        RestoreFollowTarget();
                        return true;
                    }

                    EndDropCollection($"{_dropEmptyScans} varredura(s) vazia(s) em {_dropTimer:0.#} s");
                    return false;
                }

                _dropEmptyScans = 0;

                // O drop entra no follow do motor nativo (mesmo caminho do nó da varredura): é ele
                // que faz o pathfinding até o item; o ApproachAndWork aperta o raio de parada.
                base.SetFollowTarget(_dropTarget);
            }

            Vector3 point = _dropTarget.transform.position;
            float distance = Vector3.Distance(point, transform.position);
            ApproachAndWork(dt, point + Vector3.up * 0.6f, CompanionHarvest.InteractReach, MovementOwner.Harvest);
            if (distance <= CompanionHarvest.InteractReach)
            {
                PickupDropNode(_dropTarget);
            }

            return true;
        }

        /// <summary>Item no chão ainda pegável (existe e não está preso no piche).</summary>
        private static bool IsPickableDrop(GameObject drop)
        {
            if (drop == null)
            {
                return false;
            }

            ItemDrop item = drop.GetComponent<ItemDrop>();
            return item != null && !item.InTar();
        }

        // ---------------------------------------------------------------------
        // Fase 2.12 (#4): catar os drops de criatura que o companheiro abateu
        // ---------------------------------------------------------------------

        /// <summary>
        /// Camada de <b>loot de criatura</b> (item #4 da auditoria): o companheiro volta ao ponto
        /// onde <b>ele mesmo</b> abateu a criatura e cata os itens que aquele abate gerou
        /// (<see cref="CompanionLoot"/>). É o fluxo que faltava: matar um cervo e deixar o couro no
        /// chão.
        ///
        /// <para>
        /// <b>Prioridade (combate &gt; coleta &gt; loot/follow):</b> com um alvo hostil ou com uma
        /// <b>tarefa de coleta ativa</b> o loot apenas espera na fila (nada se perde — os itens
        /// continuam no chão). Fora disso ele toma o frame: é o "slot" do follow.
        /// </para>
        ///
        /// <para>
        /// <b>Como anda e pega:</b> o item entra no follow do motor nativo
        /// (<c>base.SetFollowTarget</c>) para o pathfinding e o <see cref="ApproachAndWork"/> aperta
        /// o raio de parada; ao chegar, pega com <c>Humanoid.Pickup</c> (o mesmo caminho do jogador,
        /// incluindo o <c>RequestOwn</c> quando não somos donos do item). Sem item à vista ele vai ao
        /// <b>ponto do abate</b> e <b>espera</b>: os itens só saem do corpo quando o ragdoll
        /// desaparece (<c>Ragdoll.DestroyNow → SpawnLoot</c>).
        /// </para>
        /// </summary>
        private void UpdateLoot(float dt)
        {
            if (!CompanionLoot.Enabled)
            {
                if (_lootWasActive)
                {
                    ResetLootDrive();
                }

                return;
            }

            // Prioridade: combate > coleta > loot/follow. Quem assumiu o companheiro (um alvo hostil
            // ou uma tarefa de coleta ativa) devolve o abate em andamento para a FRENTE da fila — o
            // relógio dele para junto, então ele não "expira" parado esperando a vez.
            if (CompanionHarvest.IsActive || GetTargetCreature() != null)
            {
                if (_lootWasActive)
                {
                    CompanionLoot.Pause(
                        GetTargetCreature() != null ? "combate" : "coleta em andamento");
                    ResetLootDrive();
                }

                return;
            }

            if (!CompanionLoot.HasWork)
            {
                if (_lootWasActive)
                {
                    ResetLootDrive();
                }

                return;
            }

            // Fase 2.14: uma viagem ao baú está no comando — o loot espera (nada se perde no chão).
            // O abate volta para a frente da fila com o relógio parado, como na pausa por combate.
            if (CompanionDeposit.TripActive)
            {
                if (_lootWasActive)
                {
                    CompanionLoot.Pause("levando a carga ao baú");
                    ResetLootDrive();
                }

                return;
            }

            _lootWasActive = true;
            _lootInteractTimer -= dt;

            if (!CompanionLoot.TryBeginNext())
            {
                ResetLootDrive();
                return;
            }

            LootKill kill = CompanionLoot.Current;
            Humanoid humanoid = m_character as Humanoid;
            Inventory inventory = humanoid != null ? humanoid.GetInventory() : null;
            if (kill == null || inventory == null)
            {
                CompanionLoot.Finish("companheiro sem inventário");
                ResetLootDrive();
                return;
            }

            // Fase 2.14: carga cheia? Leva ao baú e volta — o abate volta para a frente da fila (com
            // o relógio parado) e é retomado depois do depósito.
            if (CompanionInventory.IsOverWeight(inventory) &&
                CompanionDeposit.TryStartDepositTrip(this, inventory))
            {
                CompanionLoot.Pause("carga cheia (levando ao baú)");
                ResetLootDrive();
                return;
            }

            if (CompanionLoot.ShouldGiveUp(this, kill, inventory, out string giveUp))
            {
                CompanionLoot.Finish(giveUp);
                ResetLootDrive();
                return;
            }

            // O corpo pode escorregar ribanceira abaixo, e saber se ele ainda existe é o que permite
            // encerrar cedo quando não caiu nada. Sondagem no máximo 1×/s (nunca por frame).
            CompanionLoot.TrackBody(kill);

            if (_lootTarget != null && !CompanionLoot.IsStillValidTarget(_lootTarget))
            {
                _lootTarget = null;
            }

            if (_lootTarget == null)
            {
                _lootScanTimer -= dt;
                if (_lootScanTimer <= 0f)
                {
                    _lootScanTimer = CompanionLoot.ScanInterval;
                    _lootTarget = CompanionLoot.FindDrop(transform.position, kill, inventory);
                    if (_lootTarget != null)
                    {
                        kill.SawDrop = true;
                        kill.LastDrop = Time.time;

                        // O item entra no follow do motor nativo: é ele que faz o pathfinding.
                        base.SetFollowTarget(_lootTarget.gameObject);
                        _lootTargetSince = Time.time;
                    }
                }
            }

            if (_lootTarget != null)
            {
                Vector3 point = _lootTarget.transform.position;
                ApproachAndWork(dt, point, CompanionLoot.PickupReach, MovementOwner.Loot);
                if (CompanionLoot.IsWithinReach(transform.position, point))
                {
                    PickupLootTarget(humanoid, inventory);
                }
                else if (Time.time - _lootTargetSince > LootTargetTimeout)
                {
                    // Item que não dá para alcançar (água funda, dentro de uma construção): veta e
                    // segue para o próximo, em vez de empurrar a parede até estourar o teto de tempo.
                    DropLootTarget("não consegui chegar ao item", permanent: true);
                }

                return;
            }

            // Nada para pegar agora: vai ao ponto do abate e espera os itens saírem do corpo.
            Vector3 spot = kill.Position;
            if (CompanionLoot.IsAtSpot(transform.position, spot))
            {
                kill.MarkArrived();
                StopMoving();
            }
            else
            {
                CompanionLoot.ReportDistance(kill, Vector3.Distance(transform.position, spot));
                if (CompanionLoot.IsStuck(kill))
                {
                    CompanionLoot.Finish($"não consegui chegar ao corpo (a {kill.LastDistance:0.#} m)");
                    ResetLootDrive();
                    return;
                }

                ApproachAndWork(dt, spot, CompanionLoot.ArriveStop, MovementOwner.Loot);
            }

            if (CompanionLoot.ShouldFinish(kill, out string done))
            {
                CompanionLoot.Finish(done);
                ResetLootDrive();
            }
        }

        /// <summary>Pega o item do abate ao alcance da mão (mesmo fluxo do jogador).</summary>
        private void PickupLootTarget(Humanoid humanoid, Inventory inventory)
        {
            if (_lootInteractTimer > 0f || _lootTarget == null)
            {
                return;
            }

            ItemDrop drop = _lootTarget;
            GameObject go = drop.gameObject;

            if (drop.InTar())
            {
                DropLootTarget("item preso no piche", permanent: true);
                return;
            }

            // Sem autoridade sobre o item não dá para carregá-lo: pede e tenta de novo no próximo
            // tick (é o mesmo fluxo do jogador e o que a coleta já faz).
            ZNetView netView = go.GetComponent<ZNetView>();
            if (netView != null && netView.IsValid() && !netView.IsOwner())
            {
                drop.RequestOwn();
                StopMoving();
                return;
            }

            // Lê o ZDO antes de julgar espaço/peso (o stack real do item está lá).
            drop.Load();
            ItemDrop.ItemData item = drop.m_itemData;
            if (item == null || item.m_shared == null)
            {
                DropLootTarget("item sem dados", permanent: true);
                return;
            }

            if (!inventory.CanAddItem(item))
            {
                DropLootTarget("inventário sem espaço", permanent: false);
                return;
            }

            if (CompanionLoot.OverWeight(inventory, item))
            {
                DropLootTarget("carga acima do limite", permanent: false);
                return;
            }

            // Lido ANTES do Pickup: ele destrói o ItemDrop do mundo.
            int stack = Mathf.Max(1, item.m_stack);
            string prefabName = item.m_dropPrefab != null
                ? Utils.GetPrefabName(item.m_dropPrefab)
                : Utils.GetPrefabName(go.name);

            _lootInteractTimer = HarvestInteractInterval;
            _lootPickupFailures = 0;

            // Humanoid.Pickup: valida, coloca no inventário, toca o efeito e destrói o ItemDrop do
            // mundo. autoequip falso para o companheiro não trocar de arma ao catar um machado.
            if (!humanoid.Pickup(go, autoequip: false, autoPickupDelay: false))
            {
                DropLootTarget("o jogo recusou pegar o item", permanent: false);
                return;
            }

            CompanionLoot.NotifyPickedUp(prefabName, stack);
            _lootTarget = null;
            _lootScanTimer = 0f;
        }

        /// <summary>
        /// Solta o item-alvo do loot. <paramref name="permanent"/> marca o descarte definitivo
        /// (piche/item sem dados); as falhas que podem passar (espaço/peso) só contam para desistir
        /// do abate depois de <see cref="LootPickupFailuresBeforeGiveUp"/> tentativas — assim o
        /// companheiro não fica parado em frente a um item que não cabe.
        /// </summary>
        private void DropLootTarget(string reason, bool permanent)
        {
            ItemDrop drop = _lootTarget;
            _lootTarget = null;
            _lootScanTimer = 0f;

            if (drop == null)
            {
                return;
            }

            if (permanent)
            {
                CompanionLoot.MarkSkipped(drop);
                return;
            }

            _lootPickupFailures++;
            if (_lootPickupFailures < LootPickupFailuresBeforeGiveUp)
            {
                return;
            }

            LootKill kill = CompanionLoot.Current;
            string victim = kill != null ? kill.VictimName : "?";
            Plugin.Log.LogWarning(
                $"[ModIA] Loot: não consegui pegar o que '{victim}' deixou ({reason}).");
            CompanionLoot.Finish($"não consegui pegar ({reason})");
            ResetLootDrive();
        }

        /// <summary>Sai da camada de loot e devolve o follow do motor nativo ao jogador.</summary>
        private void ResetLootDrive()
        {
            _lootWasActive = false;
            _lootTarget = null;
            _lootScanTimer = 0f;
            _lootInteractTimer = 0f;
            _lootTargetSince = 0f;
            _lootPickupFailures = 0;
            RestoreFollowTarget();
        }

        // ---------------------------------------------------------------------
        // Fase 2.10 (#6): bloqueio/parry
        // ---------------------------------------------------------------------

        /// <summary>
        /// Bloqueio do companheiro (item #6). Quando um inimigo próximo (&lt; <c>Combate/RaioBloqueio</c>)
        /// está em <c>InAttack()</c>, o companheiro vira para ele e ergue o escudo
        /// (<c>Character.m_blocking</c>, o mesmo campo que <c>Character.SetBlocking</c> usaria) por
        /// <c>Combate/TempoBloqueio</c> s; fora disso o bloqueio é desfeito para ele revidar.
        ///
        /// <para>
        /// Como não existe mais <c>Character.SetBlocking</c> na 1.0.17 (só o campo privado
        /// <c>m_blocking</c> e o <c>Humanoid.m_blockTimer</c>), escrevemos os dois direto — possível
        /// porque compilamos contra a <c>assembly_valheim</c> publicized. Não dirigimos movimento
        /// aqui (só <c>LookAt</c>), então não disputamos o "dono do movimento".
        /// </para>
        /// </summary>
        private void UpdateBlocking(float dt)
        {
            if (!CompanionCombat.BlockAttacks || m_character == null)
            {
                ClearBlock();
                return;
            }

            if (CompanionCombat.Mode == CompanionMode.Passive ||
                GetTargetCreature() == null ||
                m_character.InAttack() ||
                m_character.IsStaggering())
            {
                ClearBlock();
                return;
            }

            float radius = CompanionCombat.BlockRadius;
            Character threat = FindSwingingEnemy(radius);
            if (threat != null)
            {
                _enemySwingTimer += dt;
                if (_enemySwingTimer >= CompanionCombat.BlockDelay)
                {
                    LookAt(threat.GetTopPoint());
                    SetBlocking(true);
                    _blockHoldTimer = CompanionCombat.BlockHold;
                }

                return;
            }

            if (_blockHoldTimer > 0f)
            {
                // Ainda segurando o escudo um instante depois do golpe (parry).
                _blockHoldTimer -= dt;
                _enemySwingTimer = 0f;
                SetBlocking(true);
                return;
            }

            ClearBlock();
        }

        /// <summary>Inimigo hostil dentro do raio que está desferindo um ataque agora.</summary>
        private Character FindSwingingEnemy(float radius)
        {
            FillScanBuffer(radius);
            for (int i = 0; i < _scanBuffer.Count; i++)
            {
                Character candidate = _scanBuffer[i];
                if (candidate == null || candidate == m_character || candidate.IsDead() ||
                    !candidate.InAttack())
                {
                    continue;
                }

                if (Vector3.Distance(candidate.transform.position, transform.position) > radius)
                {
                    continue;
                }

                if (!IsHostileTarget(candidate))
                {
                    continue;
                }

                return candidate;
            }

            return null;
        }

        /// <summary>Escreve o estado de bloqueio (campo do <c>Character</c> + timer do <c>Humanoid</c>).</summary>
        private void SetBlocking(bool value)
        {
            if (m_character == null || _blocking == value)
            {
                return;
            }

            _blocking = value;
            m_character.m_blocking = value;

            if (value)
            {
                Humanoid humanoid = m_character as Humanoid;
                if (humanoid != null)
                {
                    humanoid.m_blockTimer = 0f;
                }
            }
        }

        /// <summary>Desfaz o bloqueio e zera os timers da camada de defesa.</summary>
        private void ClearBlock()
        {
            _enemySwingTimer = 0f;
            _blockHoldTimer = 0f;
            SetBlocking(false);
        }

        /// <summary>
        /// Baixa o escudo sem tocar nos timers: usado só no começo do tick, antes do
        /// <c>base.UpdateAI</c>, para o motor nativo poder atacar (a camada de bloqueio volta a
        /// levantá-lo no fim do mesmo tick).
        /// </summary>
        private void LowerShieldForAttack()
        {
            if (_blocking)
            {
                SetBlocking(false);
            }
        }

        // ---------------------------------------------------------------------
        // Fase 2.10 (#2/#5): guarda do passo do follow nativo
        // ---------------------------------------------------------------------

        /// <summary>
        /// Guarda o passo do <b>follow/perseguição nativos</b>: só roda quando a coleta não
        /// reivindicou o frame (<see cref="MovementOwner.Native"/>). Diante de uma porta fechada à
        /// frente abre e para (#5); diante de um barranco desvia para o lado seguro ou para (#2).
        ///
        /// <para>
        /// Usa a <b>direção de passo real</b> que o motor nativo escolheu (<c>Character.m_moveDir</c>,
        /// escrita pelo <c>MoveTo</c>/<c>Follow</c> do jogo) — e não a reta até o alvo — para não
        /// confundir "seguir por uma trilha que contorna o precipício" com "andar para o precipício".
        /// Parado (<c>m_moveDir</c> zerado) não há passo para guardar.
        /// </para>
        /// </summary>
        private void UpdateFollowSafety(float dt)
        {
            if (_movementOwner != MovementOwner.Native)
            {
                return;
            }

            if (!CompanionMovement.LedgeProtection && !CompanionMovement.OpenDoors)
            {
                return;
            }

            if (m_character == null || m_character.IsDead() ||
                !m_character.IsOnGround() || m_character.IsSwimming())
            {
                return;
            }

            Vector3 stepDir = m_character.m_moveDir;
            stepDir.y = 0f;
            if (stepDir.sqrMagnitude < 0.0001f)
            {
                return; // parado: não há passo em andamento para guardar
            }

            stepDir.Normalize();

            Vector3 probeTarget = transform.position + stepDir * FollowProbeAhead;
            CompanionMovement.StepAction action =
                CompanionMovement.PrepareStep(m_character, probeTarget, out Vector3 adjusted);

            if (action != CompanionMovement.StepAction.Move)
            {
                StopMoving();
                return;
            }

            if (adjusted != probeTarget)
            {
                // Barranco contornado: segue pelo lado seguro neste frame, mantendo o trote nativo.
                Vector3 side = adjusted - transform.position;
                side.y = 0f;
                if (side.sqrMagnitude > 0.0001f)
                {
                    MoveTowards(side.normalized, m_character.m_run);
                }
                else
                {
                    StopMoving();
                }
            }
        }

        /// <summary>
        /// Aproxima (ou para) e mira no ponto. <paramref name="point"/> é o alvo da mira e do
        /// deslocamento; <paramref name="stopAt"/> é a distância em que paramos (alcance da
        /// ferramenta para golpe, distância de interação para itens/colhíveis).
        ///
        /// <para>
        /// Reivindica o movimento do frame (<paramref name="owner"/>): enquanto a coleta (ou o loot)
        /// está no comando, a guarda de follow (<see cref="UpdateFollowSafety"/>) não mexe no passo.
        /// Antes de andar, <see cref="CompanionMovement.PrepareStep"/> abre uma porta fechada à frente
        /// (#5) ou desvia/aborta o passo diante de um barranco (#2).
        /// </para>
        /// </summary>
        private void ApproachAndWork(float dt, Vector3 point, float stopAt, MovementOwner owner)
        {
            _movementOwner = owner;
            LookAt(point);

            float distance = Vector3.Distance(point, transform.position);
            if (distance <= stopAt)
            {
                StopMoving();
                return;
            }

            float stop = Mathf.Max(0.5f, stopAt - 0.8f);
            CompanionMovement.StepAction action =
                CompanionMovement.PrepareStep(m_character, point, out Vector3 adjusted);
            if (action != CompanionMovement.StepAction.Move)
            {
                // Porta abrindo ou sem caminho seguro: para neste frame. Se o barranco persistir, o
                // nó acaba vetado pela detecção de travamento (CompanionHarvest.ShouldGiveUpNode).
                StopMoving();
                return;
            }

            // Aproxima-se até um pouco antes do ponto de parada; corre se estiver longe.
            MoveTo(dt, adjusted, stop, distance > 12f);
        }

        /// <summary>
        /// Procura o melhor nó para o recurso pedido (varredura com intervalo, buffer reaproveitado),
        /// equipa a ferramenta adequada e passa o nó para o follow do motor nativo.
        /// Depois de <see cref="HarvestEmptyScansBeforeStop"/> varreduras vazias a coleta termina
        /// (foi o contrato do comando: "até não haver mais nó no raio").
        /// </summary>
        private GameObject AcquireHarvestNode()
        {
            // Garante uma ferramenta de golpe no inventário (config Coleta/FornecerFerramentas):
            // sem machado/picareta, pedra e minério nunca entram na conta — era o sintoma de
            // "pedra não funciona" com o loadout padrão (só Club).
            HarvestResourceCategory category;
            bool mayNeedTool =
                !CompanionHarvestResources.TryGetCategory(CompanionHarvest.ResourceName, out category) ||
                category != HarvestResourceCategory.Pickable;
            if (mayNeedTool)
            {
                CompanionInventory.EnsureHarvestTools(this, CompanionHarvest.ResourceName);

                // Fase 2.14 (tool keeper): criar não resolveu (a lista configurada não tem
                // ferramenta capaz ou FornecerFerramentas está desligado)? Procura num baú próximo
                // e vai buscar. A viagem é dirigida pela camada de depósito; a varredura de nós
                // repete depois dela (sem contar varredura vazia).
                if (!CompanionInventory.HasHarvestTools(this) &&
                    CompanionToolKeeper.TryStartFetchForMissingTool(this))
                {
                    _harvestScanTimer = CompanionHarvest.ScanInterval;
                    return null;
                }
            }

            CompanionInventory.GetHarvestToolProfiles(this, _harvestTools);

            // A varredura usa a MESMA "coleira" da guarda de distância (passo 4 do UpdateHarvest):
            // assim ela nunca escolhe um nó que a guarda largaria no tick seguinte. É o que impede
            // o ciclo escolher/largar quando o jogador já se afastou.
            Vector3 anchor = _followTarget != null ? _followTarget.transform.position : Vector3.zero;
            float anchorMaxDistance = _followTarget != null
                ? CompanionHarvest.MaxNodeDistanceFromFollowTarget
                : 0f;

            HarvestPick pick = CompanionHarvestScanner.FindBestNode(
                transform.position,
                CompanionHarvest.ResourceName,
                CompanionHarvest.ScanRadius,
                CompanionHarvest.CollectDrops,
                _harvestTools,
                anchor,
                anchorMaxDistance);

            // Item #8: há nó do recurso no raio, mas ele exige uma ferramenta de tier maior do que o
            // companheiro tem (ex.: prata/obsidiana com a picareta de chifre). Fornece a MELHOR
            // ferramenta da lista configurada que atenda ao nó e repete a varredura UMA vez.
            if (pick.Node == null && pick.NeedsBetterTool && pick.RequiredTier >= 0)
            {
                bool provided = CompanionInventory.EnsureHarvestToolForNode(
                    this, CompanionHarvest.ResourceName, pick.RequiredModifiers, pick.RequiredTier);

                if (provided)
                {
                    CompanionInventory.GetHarvestToolProfiles(this, _harvestTools);
                    pick = CompanionHarvestScanner.FindBestNode(
                        transform.position,
                        CompanionHarvest.ResourceName,
                        CompanionHarvest.ScanRadius,
                        CompanionHarvest.CollectDrops,
                        _harvestTools,
                        anchor,
                        anchorMaxDistance);
                }
                else if (CompanionToolKeeper.TryStartFetchForNode(
                             this, pick.RequiredTier, pick.RequiredModifiers))
                {
                    // Fase 2.14 (tool keeper): criar não resolveu, mas um baú próximo tem a
                    // ferramenta do tier — a camada de depósito vai buscá-la e a varredura repete
                    // depois dela.
                    _harvestScanTimer = CompanionHarvest.ScanInterval;
                    return null;
                }
            }

            GameObject node = pick.Node;
            if (node == null)
            {
                if (pick.NeedsBetterTool)
                {
                    // Avisa no HUD em vez de deixar o companheiro vagando: a razão mais comum de
                    // "não coleta X" é não ter a ferramenta do tier certo.
                    CompanionHarvest.NotifyMissingTool(CompanionHarvest.ResourceName);
                }

                // Existe nó do recurso, mas todos fora da "coleira" do jogador: a coleta fica
                // PENDENTE (não conta como "acabou o recurso") e o companheiro segue o jogador —
                // quando ele voltar para perto de nós, a próxima varredura retoma o trabalho.
                if (pick.LeashRejected > 0)
                {
                    CompanionHarvest.LogNodeEvent(
                        "leash|" + CompanionHarvest.ResourceName,
                        $"[ModIA] Coleta: {pick.LeashRejected} nó(s) de " +
                        $"'{CompanionHarvest.ResourceName}' fora da coleira do jogador; seguindo o " +
                        "jogador até ele voltar para perto.");
                    return null;
                }

                _harvestEmptyScans++;
                if (_harvestEmptyScans >= HarvestEmptyScansBeforeStop)
                {
                    CompanionHarvest.Stop($"acabou: {pick.Reason}");
                }
                else
                {
                    CompanionHarvest.LogNodeEvent(
                        "empty|" + CompanionHarvest.ResourceName,
                        $"[ModIA] Coleta: {pick.Reason}; procurando de novo.");
                }

                return null;
            }

            _harvestEmptyScans = 0;
            CompanionHarvest.SetNode(node);

            HarvestPrefabInfo info = pick.Info;
            _harvestTool = null;
            if (CompanionHarvestScanner.NeedsTool(info.Kind))
            {
                if (CompanionInventory.TryEquipBestHarvestTool(
                        this, info.Modifiers, info.MinToolTier, CompanionHarvest.ResourceName, out string tool))
                {
                    _harvestTool = tool;
                }
            }

            // O nó entra no follow do motor nativo: é ele que faz o pathfinding até lá.
            base.SetFollowTarget(node);

            string cost = pick.Hits > 0
                ? $"~{pick.Hits} golpe(s)"
                : "interação direta";

            CompanionHarvest.LogNodeEvent(
                CompanionHarvest.MakeNodeLogKey("pick", node, info.Kind.ToString()),
                $"[ModIA] Coleta: alvo '{node.name}' ({info.Kind}{(pick.Primary ? ", fonte" : ", direto")}), " +
                $"rende ~{pick.Yield:0.#} '{CompanionHarvest.ResourceName}' em {cost}, " +
                $"ferramenta '{_harvestTool ?? "atual"}' — {pick.Reason}, " +
                $"a {pick.Distance:0.#} m, score {pick.Score:0.#}.");
            return node;
        }

        /// <summary>Bate no nó (árvore/pedra/entulho) com a ferramenta equipada, mirando no ponto dado.</summary>
        private void StrikeNode(GameObject node, Vector3 aim)
        {
            if (_harvestStrikeTimer > 0f || m_character.InAttack())
            {
                return;
            }

            // Garante a ferramenta escolhida (o combate pode ter trocado a arma no meio).
            if (!EnsureHarvestToolEquipped())
            {
                return;
            }

            if (!IsLookingAt(aim, MaxStrikeAngle))
            {
                // Ainda girando para o alvo: o próximo tick tenta de novo.
                return;
            }

            // Construção do jogador no caminho do golpe? Não batemos (a hitbox poderia acertar a
            // parede do jogador). Sem golpe não há progresso e o nó acaba vetado sozinho.
            if (CompanionHarvestScanner.HasPlayerBuildInFront(m_character, aim))
            {
                return;
            }

            _harvestStrikeTimer = CompanionHarvest.StrikeInterval;

            // Fase 2.13: o golpe custa resistência. O `Attack.DoAttack` do jogo recusa o ataque
            // sem stamina (`HaveStamina(custo + 0.1)`) e aí nenhum dano sai — o que faria a coleta
            // vetar o nó por "sem progresso em 7 s". Aqui isso é tratado como ESPERA (o relógio de
            // progresso é renovado), não como travamento.
            if (!HasStaminaToStrike())
            {
                CompanionHarvest.NotifyWaiting("sem resistência para golpear");
                return;
            }

            // StartAttack(null, false): o alvo é decidido pela hitbox do ataque (o mesmo caminho do
            // jogador batendo em recurso) — o parâmetro Character nem é usado pelo Humanoid.
            if (m_character.StartAttack(null, false))
            {
                CompanionHarvest.RegisterStrike(node);
            }
        }

        /// <summary>
        /// O companheiro tem resistência para o golpe que a ferramenta equipada vai desferir?
        /// Reproduz a conta do <c>Attack.GetAttackStamina</c> (custo da arma reduzido pela perícia
        /// dela) e usa a mesma margem de 0,1 do <c>Attack.DoAttack</c>. Sem ferramenta de ataque
        /// (ou sem resistência ligada) devolve <c>true</c> — nunca bloqueia por engano.
        /// </summary>
        private bool HasStaminaToStrike()
        {
            if (_stamina == null || !CompanionStamina.Enabled)
            {
                return true;
            }

            Humanoid humanoid = m_character as Humanoid;
            ItemDrop.ItemData weapon = humanoid != null ? humanoid.GetCurrentWeapon() : null;
            Attack attack = weapon != null && weapon.m_shared != null ? weapon.m_shared.m_attack : null;
            if (attack == null || attack.m_attackStamina <= 0f)
            {
                return true;
            }

            float cost = attack.m_attackStamina;
            cost -= cost * 0.33f * m_character.GetSkillFactor(weapon.m_shared.m_skillType);
            return _stamina.Stamina > cost + 0.1f;
        }

        /// <summary>Reequipa a ferramenta de coleta se algo (combate, comando) trocou a arma atual.</summary>
        private bool EnsureHarvestToolEquipped()
        {
            if (string.IsNullOrEmpty(_harvestTool))
            {
                return true;
            }

            Humanoid humanoid = m_character as Humanoid;
            if (humanoid == null)
            {
                return false;
            }

            ItemDrop.ItemData current = humanoid.GetCurrentWeapon();
            if (current != null && current.m_dropPrefab != null &&
                Utils.GetPrefabName(current.m_dropPrefab) == _harvestTool)
            {
                return true;
            }

            return CompanionInventory.EquipWeapon(this, _harvestTool);
        }

        /// <summary>Colhe um <c>Pickable</c> (fruta, cogumelo, cardo…).</summary>
        private void HarvestPickableNode(GameObject node)
        {
            if (_harvestInteractTimer > 0f)
            {
                return;
            }

            Pickable pickable = node.GetComponent<Pickable>();
            Humanoid humanoid = m_character as Humanoid;
            if (pickable == null || humanoid == null)
            {
                CompanionHarvest.ClearNode("sem Pickable/Humanoid");
                return;
            }

            // Fase 2.14: a planta SEMPRE solta os itens no chão (Pickable.RPC_Pick → Drop), então
            // colher sem espaço/carga só desperdiçaria o arbusto: trata como "cheio" (deposita ou
            // pausa) e volta depois.
            Inventory inventory = humanoid.GetInventory();
            if (inventory != null &&
                (CompanionInventory.IsOverWeight(inventory) || !inventory.HaveEmptySlot()))
            {
                _harvestInteractTimer = HarvestInteractInterval;
                HandlePackFull(inventory, null);
                return;
            }

            _harvestInteractTimer = HarvestInteractInterval;

            // Mesma sequência do jogador colhendo: vira para o alvo, toca a animação de interação e
            // chama o Interact (que dispara o RPC interno que gera os drops).
            LookAt(node.transform.position + Vector3.up * 0.5f);
            if (m_character.m_zanim != null)
            {
                m_character.m_zanim.SetTrigger("interact");
            }

            pickable.Interact(humanoid, false, false);
            Plugin.Log.LogInfo($"[ModIA] Coleta: colhendo '{node.name}'.");
        }

        /// <summary>Pega um item caído (<c>ItemDrop</c>) que casa com o recurso pedido.</summary>
        private void PickupDropNode(GameObject node)
        {
            if (_harvestInteractTimer > 0f)
            {
                return;
            }

            ItemDrop drop = node.GetComponent<ItemDrop>();
            Humanoid humanoid = m_character as Humanoid;
            if (drop == null || humanoid == null)
            {
                ReleaseDropOrNode(node, "sem ItemDrop/Humanoid");
                return;
            }

            if (drop.InTar())
            {
                ReleaseDropOrNode(node, "item preso no piche");
                return;
            }

            // Sem autoridade sobre o item não dá para carregá-lo para o inventário: pede e tenta
            // de novo no próximo tick (é o mesmo fluxo do jogador).
            ZNetView netView = node.GetComponent<ZNetView>();
            if (netView != null && netView.IsValid() && !netView.IsOwner())
            {
                drop.RequestOwn();
                return;
            }

            // Lê o ZDO antes de julgar espaço/peso (o stack real do item está lá). O Humanoid.Pickup
            // faz o mesmo Load internamente, então ler aqui não muda o que o jogo vê.
            drop.Load();
            ItemDrop.ItemData item = drop.m_itemData;
            if (item == null || item.m_shared == null)
            {
                ReleaseDropOrNode(node, "item sem dados");
                return;
            }

            Inventory inventory = humanoid.GetInventory();
            if (inventory == null)
            {
                ReleaseDropOrNode(node, "companheiro sem inventário");
                return;
            }

            // Fase 2.14: espaço e peso ANTES de pegar. Não cabendo, a tarefa NÃO encerra (era o
            // "não consegui pegar o item (inventário cheio?)" que matava a coleta): o companheiro
            // leva a carga ao baú e volta, ou pausa (se não houver baú).
            if (!inventory.CanAddItem(item, item.m_stack) ||
                CompanionInventory.WouldExceedWeight(inventory, item))
            {
                _harvestInteractTimer = HarvestInteractInterval;
                HandlePackFull(inventory, item);
                return;
            }

            int stack = Mathf.Max(1, item.m_stack);
            string prefabName = item.m_dropPrefab != null
                ? Utils.GetPrefabName(item.m_dropPrefab)
                : Utils.GetPrefabName(node.name);

            _harvestInteractTimer = HarvestInteractInterval;

            // Humanoid.Pickup: valida, coloca no inventário, toca o efeito e destrói o ItemDrop do
            // mundo (o Humanoid cuida do Container/ZDO). autoequip falso para não trocar a
            // ferramenta de coleta no meio do trabalho.
            if (!humanoid.Pickup(node, autoequip: false, autoPickupDelay: false))
            {
                HandlePackFull(inventory, item);
                return;
            }

            ReleaseDropOrNode(node, null);
            CompanionHarvest.NotifyCollected(stack, prefabName);
        }

        /// <summary>
        /// O drop foi consumido (ou não serve mais): solta o alvo do <b>estado de drops</b> e/ou o
        /// nó da <b>busca de nós</b>, sem mexer no outro. Alvo descartado com motivo vai para a
        /// lista negra (senão a varredura o acharia de novo até o estado expirar).
        /// </summary>
        private void ReleaseDropOrNode(GameObject node, string reason)
        {
            if (node == _dropTarget)
            {
                if (reason != null)
                {
                    CompanionHarvestScanner.Blacklist(node, CompanionHarvest.BlacklistSeconds);
                }

                _dropTarget = null;
                _dropScanTimer = 0f; // o próximo drop do monte pode estar a um passo: varre já
            }

            if (node == CompanionHarvest.Node)
            {
                CompanionHarvest.ClearNode(reason);
                _harvestScanTimer = 0f;
                ForgetNode();
            }
        }

        /// <summary>Alcance (m) em que consideramos o nó ao alcance da ferramenta equipada.</summary>
        private float GetHarvestReach()
        {
            float range = DefaultHarvestReach;
            Humanoid humanoid = m_character as Humanoid;
            ItemDrop.ItemData weapon = humanoid != null ? humanoid.GetCurrentWeapon() : null;
            if (weapon != null && weapon.m_shared.m_attack != null)
            {
                range = weapon.m_shared.m_attack.m_attackRange;
            }

            // Margem porque a origem do golpe é a mão/arma (não o centro do personagem).
            return Mathf.Clamp(range + 0.6f, 1.8f, 3f);
        }

        /// <summary>Devolve o follow do motor nativo ao jogador (a coleta usava o nó como follow).</summary>
        private void RestoreFollowTarget()
        {
            if (_followTarget == null)
            {
                return;
            }

            base.SetFollowTarget(_followTarget);
        }

        // ---------------------------------------------------------------------
        // Configuração de identidade / ajustes
        // ---------------------------------------------------------------------

        /// <summary>
        /// Companheiro nasce aliado: sem caçar jogador e sem atacar construções.
        ///
        /// <para>
        /// <c>m_canBeAlerted = true</c> é <b>obrigatório</b> para o combate: o
        /// <c>MonsterAI.UpdateAI</c> só executa <c>DoAttack</c> quando <c>IsAlerted()</c> é true (e,
        /// com alerta, o teste de ângulo de visão é dispensado). Quem liga/desliga o alerta é o
        /// nosso código, na aquisição/perda de alvo — nunca a "visão" genérica do monstro.
        /// </para>
        /// </summary>
        private void ConfigureAsCompanion()
        {
            m_canBeAlerted = true;
            m_aggravatable = false;
            m_attackPlayerObjects = false;
            m_enableHuntPlayer = false;
            m_fleeIfLowHealth = 0f;
            m_fleeIfNotAlerted = false;
            m_fleeIfHurtWhenTargetCantBeReached = false;
            m_fleeInLava = true;

            SetHuntPlayer(false);
            ApplyCombatTuning();

            if (m_character == null)
            {
                return;
            }

            // O corpo vem do prefab 'Player' (que não tem IA): se o campo serializado tivesse
            // vindo true, o MonsterAI.UpdateTarget sairia logo no início e o companheiro nunca
            // "veria" o alvo — logo, nunca atacaria.
            m_character.m_aiCannotTargetOthers = false;

            // Facção "Players" + domado: o motor de IA deixa de tratar o companheiro como
            // monstro selvagem (ver BaseAI.IsEnemy) e ele nunca vira inimigo do jogador.
            m_character.m_faction = Character.Faction.Players;
            m_character.SetTamed(true);
        }

        /// <summary>
        /// Raios do <c>MonsterAI</c> que precisam acompanhar a config:
        /// <c>m_alertRange</c> é a "coleira" nativa para criaturas domadas (o alvo é descartado se
        /// ficar mais longe que isso do alvo de follow) e <c>m_viewRange</c> é o alcance em que o
        /// alvo é considerado visível (precisa cobrir o maior raio de varredura).
        /// </summary>
        private void ApplyCombatTuning()
        {
            m_alertRange = CompanionCombat.LeashDistance;

            float viewRange = Mathf.Max(MinimumViewRange, CompanionCombat.AggressiveRadius + 10f);
            if (!Mathf.Approximately(m_viewRange, viewRange))
            {
                m_viewRange = viewRange;
            }
        }

        private void TryReacquireLocalPlayer()
        {
            Player localPlayer = Player.m_localPlayer;
            if (localPlayer == null)
            {
                return;
            }

            SetFollowTarget(localPlayer.gameObject);
            Plugin.Log.LogInfo("[ModIA] Companheiro sem alvo de follow: re-adotou o jogador local.");
        }
    }
}
