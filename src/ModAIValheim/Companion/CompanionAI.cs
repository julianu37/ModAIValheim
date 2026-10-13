using UnityEngine;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// IA do companheiro do ModIA.
    ///
    /// Deriva de <see cref="MonsterAI"/> (e não de <c>BaseAI</c>) porque no Valheim 1.0 o estado de
    /// "seguir" (<c>m_follow</c>) e os campos de alvo/agressividade vivem em <c>MonsterAI</c> —
    /// ver <c>docs/re/api-diff.md</c>, seção B1.
    ///
    /// Fase 2.1: o único comportamento é SEGUIR o jogador. Por isso <see cref="UpdateAI"/> NÃO
    /// chama <c>base.UpdateAI</c> (que fecharia alvo, agravaria e atacaria). Combate, coleta e
    /// patrulha entram nas próximas fases.
    /// </summary>
    public class CompanionAI : MonsterAI
    {
        /// <summary>Intervalo (s) entre tentativas de reaquisição do alvo de follow.</summary>
        private const float ReacquireInterval = 1f;

        private GameObject _followTarget;
        private float _reacquireTimer = ReacquireInterval;

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

            GameObject target = _followTarget;
            if (target == null)
            {
                StopMoving();
                return true;
            }

            // Mecanismo de follow do próprio jogo (BaseAI.Follow):
            // para quando está a menos de 3 m e corre quando passa de 10 m.
            Follow(target, dt);
            return true;
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
        /// Companheiro nasce passivo: sem agressividade própria, sem caçar jogador e sem atacar
        /// construções. O comportamento de combate (Fase 3+) vai religar isso com regras nossas.
        /// </summary>
        private void ConfigureAsCompanion()
        {
            m_canBeAlerted = false;
            m_aggravatable = false;
            m_attackPlayerObjects = false;
            m_enableHuntPlayer = false;
            m_fleeIfLowHealth = 0f;
            m_fleeIfNotAlerted = false;
            m_fleeIfHurtWhenTargetCantBeReached = false;

            if (m_character == null)
            {
                return;
            }

            // Facção "Players" + domado: o motor de IA deixa de tratar o companheiro como
            // monstro selvagem (ver BaseAI.IsEnemy) e ele nunca vira inimigo do jogador.
            m_character.m_faction = Character.Faction.Players;
            m_character.SetTamed(true);
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
