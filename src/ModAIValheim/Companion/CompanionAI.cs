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

        /// <summary>Varreduras seguidas sem achar nada antes de encerrar a coleta.</summary>
        private const int HarvestEmptyScansBeforeStop = 3;

        /// <summary>Intervalo (s) entre interações (colher/pegar item).</summary>
        private const float HarvestInteractInterval = 0.3f;

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

            // Fase 2.4b: a coleta roda DEPOIS do motor nativo. Quando ela está ativa é ela quem
            // manda no movimento/mira/ataque (a última escrita em SetMoveDir/SetLookDir vence) e
            // quem decide o que o MonsterAI segue.
            UpdateHarvest(dt);

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
        /// Reação imediata à troca de modo (chamada por <see cref="CompanionCombat.SetMode"/>):
        /// em Passive larga o alvo na hora; nos outros modos força uma nova varredura já no
        /// próximo <see cref="UpdateAI"/>.
        /// </summary>
        internal void ApplyMode(CompanionMode mode)
        {
            _scanTimer = 0f;

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

            if (!IsHostileFaction(target.GetFaction()))
            {
                return false;
            }

            // IsEnemy (do BaseAI) cuida de grupo/mesma facção/domado com as regras do jogo.
            return IsEnemy(target);
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
                    _harvestWasActive = false;
                    _harvestScanTimer = 0f;
                    _harvestStrikeTimer = 0f;
                    _harvestInteractTimer = 0f;
                    _harvestEmptyScans = 0;
                    _harvestTool = null;
                    RestoreFollowTarget();
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
                RestoreFollowTarget();
                return;
            }

            CompanionHarvest.ResumeFromCombat();

            // 2) O nó atual ainda existe/serve? (destruído, colhido, tipo errado, longe do jogador)
            GameObject node = CompanionHarvest.Node;
            if (node != null && !CompanionHarvestScanner.IsStillValidNode(node, CompanionHarvest.ResourceName))
            {
                CompanionHarvest.ClearNode("nó destruído ou já esgotado");
                node = null;
                _harvestScanTimer = 0f;
            }

            if (node != null && _followTarget != null &&
                Vector3.Distance(node.transform.position, _followTarget.transform.position) >
                CompanionHarvest.MaxNodeDistanceFromFollowTarget)
            {
                CompanionHarvest.ClearNode("jogador se afastou do nó");
                node = null;
                _harvestScanTimer = 0f;
            }

            // 3) Travou? (não chega, bate e não sai dano, construção no caminho…)
            if (node != null && CompanionHarvest.ShouldGiveUpNode(out string stuckReason))
            {
                CompanionHarvest.BlacklistNode(node, stuckReason);
                node = null;
                _harvestScanTimer = 0f;
                _harvestTool = null;
            }

            // 4) Sem nó: segue o jogador enquanto espera a próxima varredura.
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
                return;
            }

            Vector3 nodePosition = node.transform.position;
            float distance = Vector3.Distance(nodePosition, transform.position);
            CompanionHarvest.ReportDistance(node, distance);

            switch (info.Kind)
            {
                case HarvestNodeKind.ItemDrop:
                    ApproachAndWork(dt, nodePosition, CompanionHarvest.InteractReach);
                    if (distance <= CompanionHarvest.InteractReach)
                    {
                        PickupDropNode(node);
                    }

                    break;

                case HarvestNodeKind.Pickable:
                    ApproachAndWork(dt, nodePosition, CompanionHarvest.InteractReach);
                    if (distance <= CompanionHarvest.InteractReach)
                    {
                        HarvestPickableNode(node);
                    }

                    break;

                default:
                    float reach = GetHarvestReach();
                    ApproachAndWork(dt, nodePosition, reach);
                    if (distance <= reach)
                    {
                        StrikeNode(node, nodePosition);
                    }

                    break;
            }
        }

        /// <summary>
        /// Aproxima (ou para) e mira no nó. <paramref name="stopAt"/> é a distância em que paramos:
        /// para ferramentas é o alcance do golpe, para itens/colhíveis é a distância de interação.
        /// </summary>
        private void ApproachAndWork(float dt, Vector3 point, float stopAt)
        {
            Vector3 aim = point + Vector3.up * 1f;
            LookAt(aim);

            float distance = Vector3.Distance(point, transform.position);
            if (distance <= stopAt)
            {
                StopMoving();
                return;
            }

            // Aproxima-se até um pouco antes do ponto de parada; corre se estiver longe.
            float stop = Mathf.Max(0.5f, stopAt - 0.8f);
            MoveTo(dt, point, stop, distance > 12f);
        }

        /// <summary>
        /// Procura o melhor nó para o recurso pedido (varredura com intervalo, buffer reaproveitado),
        /// equipa a ferramenta adequada e passa o nó para o follow do motor nativo.
        /// Depois de <see cref="HarvestEmptyScansBeforeStop"/> varreduras vazias a coleta termina
        /// (foi o contrato do comando: "até não haver mais nó no raio").
        /// </summary>
        private GameObject AcquireHarvestNode()
        {
            CompanionInventory.GetHarvestToolProfiles(this, _harvestTools);
            HarvestPick pick = CompanionHarvestScanner.FindBestNode(
                transform.position,
                CompanionHarvest.ResourceName,
                CompanionHarvest.ScanRadius,
                CompanionHarvest.CollectDrops,
                _harvestTools);

            GameObject node = pick.Node;
            if (node == null)
            {
                _harvestEmptyScans++;
                if (_harvestEmptyScans >= HarvestEmptyScansBeforeStop)
                {
                    CompanionHarvest.Stop($"acabou: {pick.Reason}");
                }
                else
                {
                    Plugin.Log.LogInfo($"[ModIA] Coleta: {pick.Reason}; procurando de novo.");
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

            Plugin.Log.LogInfo(
                $"[ModIA] Coleta: alvo '{node.name}' ({info.Kind}{(pick.Primary ? ", fonte" : ", direto")}), " +
                $"rende ~{pick.Yield:0.#} '{CompanionHarvest.ResourceName}' em {cost}, " +
                $"ferramenta '{_harvestTool ?? "atual"}' — {pick.Reason}, " +
                $"a {pick.Distance:0.#} m, score {pick.Score:0.#}.");
            return node;
        }

        /// <summary>Bate no nó (árvore/pedra/entulho) com a ferramenta equipada.</summary>
        private void StrikeNode(GameObject node, Vector3 nodePosition)
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

            Vector3 aim = nodePosition + Vector3.up * 1f;
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

            // StartAttack(null, false): o alvo é decidido pela hitbox do ataque (o mesmo caminho do
            // jogador batendo em recurso) — o parâmetro Character nem é usado pelo Humanoid.
            if (m_character.StartAttack(null, false))
            {
                CompanionHarvest.RegisterStrike(node);
            }
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
                CompanionHarvest.ClearNode("sem ItemDrop/Humanoid");
                return;
            }

            if (drop.InTar())
            {
                CompanionHarvest.BlacklistNode(node, "item preso no piche");
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

            int stack = drop.m_itemData != null ? Mathf.Max(1, drop.m_itemData.m_stack) : 1;
            string prefabName = drop.m_itemData != null && drop.m_itemData.m_dropPrefab != null
                ? Utils.GetPrefabName(drop.m_itemData.m_dropPrefab)
                : Utils.GetPrefabName(node.name);

            _harvestInteractTimer = HarvestInteractInterval;

            // Humanoid.Pickup: valida, coloca no inventário, toca o efeito e destrói o ItemDrop do
            // mundo (o Humanoid cuida do Container/ZDO). autoequip falso para não trocar a
            // ferramenta de coleta no meio do trabalho.
            if (!humanoid.Pickup(node, autoequip: false, autoPickupDelay: false))
            {
                CompanionHarvest.Stop("não consegui pegar o item (inventário cheio?)");
                return;
            }

            CompanionHarvest.ClearNode(null);
            _harvestScanTimer = 0f; // o próximo drop do monte pode estar a um passo: varre já
            CompanionHarvest.NotifyCollected(stack, prefabName);
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
