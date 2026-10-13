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

        private GameObject _followTarget;
        private Character _ownerCharacter;
        private float _reacquireTimer = ReacquireInterval;
        private float _scanTimer;

        /// <summary>Buffer reaproveitado pelas varreduras de alvo (nenhuma lista alocada em jogo).</summary>
        private readonly List<Character> _scanBuffer = new List<Character>();

        /// <summary>Alvo seguido atualmente (normalmente o jogador local).</summary>
        public GameObject FollowTarget => _followTarget;

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
            return base.UpdateAI(dt);
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
