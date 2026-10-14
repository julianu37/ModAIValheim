using System.Collections.Generic;
using BepInEx.Configuration;
using ModAIValheim.Companion.Jobs;
using UnityEngine;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// <b>Descanso e sono do companheiro</b> (Fase 2.18, seção <c>[Descanso]</c> do <c>.cfg</c>).
    ///
    /// <para>
    /// Quando o companheiro está <b>ocioso</b> (sem trabalho, coleta, viagem ao baú, loot ou alvo de
    /// combate — <see cref="CompanionAI.JobIsIdle"/>) e você está por perto, ele faz o que um
    /// companheiro humano faria:
    /// </para>
    /// <list type="number">
    ///   <item><description><b>senta e descansa</b> perto de uma <b>fogueira acesa</b>
    ///   (<c>Fireplace.IsBurning</c>) — ou dentro da casa (<c>modia_home set</c>) —, recuperando
    ///   vida e resistência mais rápido e acumulando o <b>descanso</b> do jogo;</description></item>
    ///   <item><description><b>dorme numa cama livre</b> (<c>Bed</c>) de noite
    ///   (<c>DormirDeNoite</c>) ou quando <b>você se deita</b> (<c>DormirQuandoVoceDorme</c>),
    ///   ficando deitado até amanhecer, até você chamá-lo, até aparecer inimigo ou até a cama
    ///   sumir.</description></item>
    /// </list>
    ///
    /// <para>
    /// <b>A cama do jogador é do jogador:</b> ele nunca usa a cama que é o <b>ponto de spawn atual</b>
    /// de um jogador (<c>Bed.IsCurrent</c>) nem uma cama de outro jogador
    /// (<c>Bed.GetOwner</c>) — precisa de uma cama <b>livre</b> (sem dono) ou de uma cama sua que
    /// <b>não</b> seja o seu ponto de spawn. Sem cama livre, ele apenas senta no fogo.
    /// </para>
    ///
    /// <para>
    /// <b>Como fala com o jogo (1.0.17).</b> A animação sai pelo <c>ZSyncAnimation</c> do corpo (o
    /// mesmo componente que o <c>Player</c> usa): <c>SetBool("emote_sit", true)</c> para sentar e
    /// <c>SetBool("attach_bed", true)</c> para deitar — os dois <b>sincronizados pelo ZDO</b>, porque
    /// <c>ZSyncAnimation.SetBool</c> grava o estado (o parâmetro só é usado quando existe no
    /// <c>Animator</c>, conferido com <c>HasParameter</c>). O sono segue o caminho do
    /// <c>Player.AttachStart</c>/<c>AttachStop</c> do jogo (que é <b>vazio</b> para um NPC):
    /// posição da cama (<c>Bed.m_spawnPoint</c>), gravidade desligada, colisão ignorada com os
    /// colliders da cama, <c>ZDOVars.s_inBed</c> no ZDO e <c>attach_bed</c> na animação — desfeito
    /// inteiro ao acordar. O <b>descanso</b> lê os números do <c>SE_Rested</c> vanilla
    /// (<c>ObjectDB.GetStatusEffect</c>: <c>m_baseTTL</c>, <c>m_TTLPerComfortLevel</c>,
    /// <c>m_healthRegenMultiplier</c>, <c>m_staminaRegenMultiplier</c>,
    /// <c>m_raiseSkillModifier</c>) e o <b>conforto</b> pelo caminho do jogo
    /// (<c>Cover.GetCoverForPoint</c> + <c>SE_Rested.CalculateComfortLevel</c>).
    /// </para>
    ///
    /// <para>
    /// Adaptado (lógica) de <c>src/Companions/Companion/CompanionRest.cs</c> (as máquinas de
    /// <c>DirectSit</c>/<c>DirectSleep</c>/<c>BeginSleep</c>/<c>FinalizeSleep</c>/<c>StopSleeping</c>/
    /// <c>FindNearbyFire</c>/<c>TryClaimBedIfNeeded</c>/<c>MHR_BedOwnerOk</c>/<c>MHR_BedIsMine</c> e o
    /// <c>PinToBed</c>) e de <c>src/Companions/Companion/CompanionRestedBuff.cs</c> (o descanso com
    /// aquecimento, os bônus lidos do <c>SE_Rested</c> e a duração por nível de conforto) do
    /// <i>Offline Companions Reborn</i> (MIT — ver <c>THIRD_PARTY_NOTICES.md</c>): lá o estado vive
    /// num <c>MonoBehaviour</c> por companheiro, com um sistema próprio de <c>StatusEffect</c> e a
    /// cama "reivindicada" por um campo no ZDO da cama; aqui tudo é uma máquina de estados estática
    /// amarrada ao <see cref="CompanionAI"/>, o descanso é um estado do próprio mod (sem criar
    /// <c>StatusEffect</c>) e a cama é escolhida a cada vez — sem cópia de arquivo.
    /// </para>
    ///
    /// <para>
    /// <b>Nada por frame:</b> a procura de fogueira/cama e de inimigos tem intervalo
    /// (<c>Descanso/IntervaloVarredura</c>), os buffers de collider/personagens são reaproveitados e
    /// o resto do estado é aritmética por tick.
    /// </para>
    /// </summary>
    internal static class CompanionRest
    {
        /// <summary>Por que o companheiro foi dormir (decide quando ele acorda).</summary>
        private enum SleepKind
        {
            /// <summary>Foi dormir porque é noite (<c>DormirDeNoite</c>): acorda de manhã.</summary>
            Night,

            /// <summary>Foi dormir porque você se deitou: acorda quando você levanta.</summary>
            Player,

            /// <summary>Sono pedido a mão (<c>modia_rest sleep</c>): acorda no tempo do pedido.</summary>
            Directed,
        }

        /// <summary>Estado do companheiro na camada de descanso.</summary>
        private enum RestState
        {
            /// <summary>Não está descansando (nem indo descansar).</summary>
            Idle,

            /// <summary>Andando até a fogueira/casa para sentar.</summary>
            WalkToFire,

            /// <summary>Sentado, descansando.</summary>
            Sitting,

            /// <summary>Andando até a cama para dormir.</summary>
            WalkToBed,

            /// <summary>Deitado, dormindo.</summary>
            Sleeping,
        }

        /// <summary>Intervalo (s) entre as decisões de "hora de descansar?" (nunca por frame).</summary>
        private const float ScanInterval = 2f;

        /// <summary>Raio (m) da varredura de inimigos que interrompe o descanso.</summary>
        private const float EnemyRadius = 15f;

        /// <summary>Intervalo (s) entre varreduras de inimigo enquanto ele descansa.</summary>
        private const float EnemyCheckInterval = 0.5f;

        /// <summary>Distância (m) da cama a partir da qual o companheiro já se deita.</summary>
        private const float BedReach = 3f;

        /// <summary>Distância (m) da fogueira em que ele considera que chegou para sentar.</summary>
        private const float FireReach = 2f;

        /// <summary>Teto (s) para chegar à fogueira/cama antes de desistir.</summary>
        private const float NavTimeout = 25f;

        /// <summary>Teto (s) de um descanso <b>pedido a mão</b> (<c>modia_rest</c>).</summary>
        private const float DirectedTimeout = 300f;

        /// <summary>Quantos colliders a varredura de fogueira/cama examina por vez.</summary>
        private const int ScanBufferSize = 32;

        /// <summary>Valores de reserva do <c>SE_Rested</c> quando o jogo não os dá (nomes do vanilla).</summary>
        private const float FallbackBaseTtl = 300f;
        private const float FallbackTtlPerComfort = 60f;
        private const float FallbackHealthRegen = 1.5f;
        private const float FallbackStaminaRegen = 2f;
        private const float FallbackSkillModifier = 0.5f;

        private static readonly Collider[] s_colliderBuffer = new Collider[ScanBufferSize];
        private static readonly List<Character> s_characterBuffer = new List<Character>(16);

        // --- Configuração -----------------------------------------------------
        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<bool> _sitToRest;
        private static ConfigEntry<float> _fireRadius;
        private static ConfigEntry<float> _playerRadius;
        private static ConfigEntry<bool> _sleepInBed;
        private static ConfigEntry<bool> _sleepAtNight;
        private static ConfigEntry<bool> _sleepWhenPlayerSleeps;
        private static ConfigEntry<float> _bedRadius;
        private static ConfigEntry<float> _wakeDistance;
        private static ConfigEntry<float> _staminaMultiplier;
        private static ConfigEntry<float> _healRate;
        private static ConfigEntry<float> _warmupSeconds;
        private static ConfigEntry<float> _skillBonus;

        // --- Estado -----------------------------------------------------------
        private static RestState _state = RestState.Idle;
        private static GameObject _fireTarget;
        private static Bed _bedTarget;
        private static Vector3 _bedSpot;
        private static Quaternion _bedSpotRotation;
        private static Collider[] _bedColliders;
        private static ZSyncAnimation _zanim;
        private static Character _character;
        private static CompanionStamina _stamina;

        private static float _scanTimer;
        private static float _navDeadline;
        private static float _healTimer;
        private static float _restTimer;
        private static bool _restedGranted;
        private static bool _directed;
        private static float _directedDeadline;
        private static SleepKind _sleepKind;
        private static float _sleepDeadline;
        private static float _enemyCheckUntil;
        private static bool _enemyNearby;
        private static bool _loggedSitAnim;
        private static bool _loggedBedAnim;

        /// <summary>Instante (s) até quando o companheiro está <b>descansado</b>.</summary>
        private static float _restedUntil = -999f;

        /// <summary>Nível de conforto do último descanso (para o log/HUD).</summary>
        private static int _restedComfort = 1;

        /// <summary>Números do <c>SE_Rested</c> do jogo (lidos uma vez).</summary>
        private static bool _bonusesCached;
        private static float _baseTtl = FallbackBaseTtl;
        private static float _ttlPerComfort = FallbackTtlPerComfort;
        private static float _healthRegenMultiplier = FallbackHealthRegen;
        private static float _staminaRegenMultiplier = FallbackStaminaRegen;
        private static float _skillModifier = FallbackSkillModifier;

        private static int s_pieceMask = -1;

        // ---------------------------------------------------------------------
        // Leitura (stamina/skills)
        // ---------------------------------------------------------------------

        /// <summary>Descanso ligado (config <c>Descanso/Ligado</c>).</summary>
        internal static bool Enabled => _enabled == null || _enabled.Value;

        /// <summary>O companheiro está <b>sentado ou dormindo</b> (ou indo para um dos dois).</summary>
        internal static bool IsResting => _state != RestState.Idle;

        /// <summary>O companheiro está <b>dormindo</b> (deitado na cama).</summary>
        internal static bool IsSleeping => _state == RestState.Sleeping;

        /// <summary>O companheiro está com o <b>descanso</b> ativo (ganhou o bônus do jogo).</summary>
        internal static bool IsRested => Time.time < _restedUntil;

        /// <summary>Segundos que faltam do descanso (0 quando não há).</summary>
        internal static float RestedRemaining => Mathf.Max(0f, _restedUntil - Time.time);

        /// <summary>
        /// Multiplicador da <b>regeneração de resistência</b> enquanto o companheiro descansa
        /// (<c>Descanso/MultiplicadorStamina</c>, padrão 2×) ou está descansado
        /// (<c>m_staminaRegenMultiplier</c> do <c>SE_Rested</c> do jogo). Lido pelo
        /// <see cref="CompanionStamina"/>.
        /// </summary>
        internal static float StaminaRegenMultiplier
        {
            get
            {
                if (_state == RestState.Sitting || _state == RestState.Sleeping)
                {
                    return _staminaMultiplier != null
                        ? Mathf.Clamp(_staminaMultiplier.Value, 1f, 10f)
                        : 2f;
                }

                return IsRested ? Mathf.Max(1f, _staminaRegenMultiplier) : 1f;
            }
        }

        /// <summary>
        /// Multiplicador do <b>ganho de perícia</b> enquanto o companheiro está descansado
        /// (<c>m_raiseSkillModifier</c> do <c>SE_Rested</c> + <c>Descanso/BonusSkill</c>). Lido pelo
        /// <see cref="CompanionSkills"/>.
        /// </summary>
        internal static float SkillGainMultiplier
        {
            get
            {
                if (!IsRested)
                {
                    return 1f;
                }

                float bonus = _skillBonus != null ? Mathf.Max(0f, _skillBonus.Value) : FallbackSkillModifier;
                return 1f + bonus;
            }
        }

        internal static void Configure(
            ConfigEntry<bool> enabled,
            ConfigEntry<bool> sitToRest,
            ConfigEntry<float> fireRadius,
            ConfigEntry<float> playerRadius,
            ConfigEntry<bool> sleepInBed,
            ConfigEntry<bool> sleepAtNight,
            ConfigEntry<bool> sleepWhenPlayerSleeps,
            ConfigEntry<float> bedRadius,
            ConfigEntry<float> wakeDistance,
            ConfigEntry<float> staminaMultiplier,
            ConfigEntry<float> healRate,
            ConfigEntry<float> warmupSeconds,
            ConfigEntry<float> skillBonus)
        {
            _enabled = enabled;
            _sitToRest = sitToRest;
            _fireRadius = fireRadius;
            _playerRadius = playerRadius;
            _sleepInBed = sleepInBed;
            _sleepAtNight = sleepAtNight;
            _sleepWhenPlayerSleeps = sleepWhenPlayerSleeps;
            _bedRadius = bedRadius;
            _wakeDistance = wakeDistance;
            _staminaMultiplier = staminaMultiplier;
            _healRate = healRate;
            _warmupSeconds = warmupSeconds;
            _skillBonus = skillBonus;

            Plugin.Log.LogInfo(
                $"[ModIA] Descanso: ligado = {Enabled} | sentar = {SitToRest} " +
                $"(fogueira {FireRadius:0} m, voce a ate {PlayerRadius:0} m) | " +
                $"dormir = {SleepInBed} (cama {BedRadius:0} m, de noite = {SleepAtNight}, " +
                $"quando voce dorme = {SleepWhenPlayerSleeps}) | acorda a {WakeDistance:0} m | " +
                $"stamina x{StaminaMultiplier:0.#}, cura {HealRate:0.#}/s, aquecimento " +
                $"{WarmupSeconds:0} s, skill +{SkillBonus:0.##}.");
        }

        /// <summary>Solta o estado estático (unload do plugin / fim do mundo).</summary>
        internal static void Shutdown()
        {
            _state = RestState.Idle;
            _fireTarget = null;
            _bedTarget = null;
            _bedColliders = null;
            _zanim = null;
            _character = null;
            _stamina = null;
            _scanTimer = 0f;
            _restTimer = 0f;
            _restedGranted = false;
            _directed = false;
            _restedUntil = -999f;
            _bonusesCached = false;
            _loggedSitAnim = false;
            _loggedBedAnim = false;
        }

        // ---------------------------------------------------------------------
        // Propriedades de config (com defaults)
        // ---------------------------------------------------------------------

        private static bool SitToRest => _sitToRest == null || _sitToRest.Value;

        private static bool SleepInBed => _sleepInBed == null || _sleepInBed.Value;

        private static bool SleepAtNight => _sleepAtNight == null || _sleepAtNight.Value;

        private static bool SleepWhenPlayerSleeps => _sleepWhenPlayerSleeps == null || _sleepWhenPlayerSleeps.Value;

        private static float FireRadius => _fireRadius != null ? Mathf.Clamp(_fireRadius.Value, 2f, 40f) : 10f;

        private static float PlayerRadius => _playerRadius != null ? Mathf.Clamp(_playerRadius.Value, 3f, 60f) : 12f;

        private static float BedRadius => _bedRadius != null ? Mathf.Clamp(_bedRadius.Value, 5f, 80f) : 25f;

        private static float WakeDistance => _wakeDistance != null ? Mathf.Clamp(_wakeDistance.Value, 5f, 200f) : 30f;

        private static float StaminaMultiplier =>
            _staminaMultiplier != null ? Mathf.Clamp(_staminaMultiplier.Value, 1f, 10f) : 2f;

        private static float HealRate => _healRate != null ? Mathf.Clamp(_healRate.Value, 0f, 50f) : 3f;

        private static float WarmupSeconds =>
            _warmupSeconds != null ? Mathf.Clamp(_warmupSeconds.Value, 1f, 300f) : 20f;

        private static float SkillBonus =>
            _skillBonus != null ? Mathf.Clamp(_skillBonus.Value, 0f, 2f) : FallbackSkillModifier;

        // ---------------------------------------------------------------------
        // Ciclo (chamado pelo CompanionAI, só no dono do ZDO)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Um passo da camada de descanso. Decide se é hora de sentar/dormir quando ele está livre e
        /// mantém (ou encerra) o descanso em andamento. Nunca dirige o movimento do follow: quando
        /// ele anda ou fica parado, reivindica o frame
        /// (<see cref="MovementOwner.Rest"/>/<see cref="MovementOwner.Sleep"/>).
        /// </summary>
        internal static void Tick(CompanionAI companion, float dt)
        {
            if (companion == null)
            {
                ResetState();
                return;
            }

            if (!Enabled)
            {
                if (_state != RestState.Idle)
                {
                    Stop(companion, "descanso desligado na config");
                }

                return;
            }

            ResolveReferences(companion);

            // A carroça vence o descanso: ele está trabalhando (puxando) por você — inclusive
            // enquanto anda até o engate.
            bool idle = companion.JobIsIdle && !CompanionCart.Busy;

            switch (_state)
            {
                case RestState.Sleeping:
                    UpdateSleeping(companion, dt, idle);
                    return;

                case RestState.Sitting:
                    UpdateSitting(companion, dt, idle);
                    return;

                case RestState.WalkToFire:
                case RestState.WalkToBed:
                    UpdateNavigation(companion, dt, idle);
                    return;
            }

            _scanTimer -= dt;
            if (_scanTimer > 0f)
            {
                return;
            }

            _scanTimer = ScanInterval;
            TryStart(companion, idle);
        }

        private static void ResolveReferences(CompanionAI companion)
        {
            if (_character == null)
            {
                _character = companion.JobCharacter;
            }

            if (_stamina == null)
            {
                _stamina = companion.GetComponent<CompanionStamina>();
            }

            if (_zanim == null)
            {
                _zanim = companion.GetComponent<ZSyncAnimation>();
            }
        }

        // ---------------------------------------------------------------------
        // Decisão
        // ---------------------------------------------------------------------

        /// <summary>
        /// Decide entre sentar e dormir (ou nada) quando o companheiro está livre. O descanso só
        /// começa com você por perto (<c>Descanso/RaioJogador</c>) e sem inimigo a
        /// <see cref="EnemyRadius"/> m — ninguém senta no fogo com um troll do lado.
        /// </summary>
        private static void TryStart(CompanionAI companion, bool idle)
        {
            if (!idle || _directed)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return;
            }

            float toPlayer = FlatDistance(companion.transform.position, player.transform.position);
            if (toPlayer > PlayerRadius)
            {
                return;
            }

            if (EnemyNearby(companion))
            {
                return;
            }

            if (SleepInBed && TryStartSleep(companion, player))
            {
                return;
            }

            if (SitToRest && TryStartSit(companion))
            {
                return;
            }
        }

        /// <summary>De noite (ou com você na cama) e com uma cama livre perto: vai dormir.</summary>
        private static bool TryStartSleep(CompanionAI companion, Player player)
        {
            bool playerInBed = SleepWhenPlayerSleeps && player.InBed();
            bool night = SleepAtNight && EnvMan.IsNight();

            if (!playerInBed && !night)
            {
                return false;
            }

            Bed bed = FindAvailableBed(companion.transform.position, BedRadius);
            if (bed == null)
            {
                return false;
            }

            BeginWalkToBed(
                companion,
                bed,
                playerInBed ? SleepKind.Player : SleepKind.Night,
                playerInBed ? "voce se deitou" : "e noite e ha uma cama livre");

            return true;
        }

        /// <summary>Perto de uma fogueira acesa (ou dentro da casa): senta e descansa.</summary>
        private static bool TryStartSit(CompanionAI companion)
        {
            GameObject fire = FindBurningFire(companion.transform.position, FireRadius);
            if (fire == null && !CompanionHome.Zone.Contains(companion.transform.position))
            {
                return false;
            }

            BeginSit(companion, fire, "ocioso perto do fogo");
            return true;
        }

        // ---------------------------------------------------------------------
        // Descanso pedido a mão (modia_rest)
        // ---------------------------------------------------------------------

        /// <summary>
        /// <c>modia_rest sit</c>: manda o companheiro <b>sentar e descansar</b> agora. Procura uma
        /// fogueira acesa num raio maior (<c>Descanso/RaioFogueira</c> × 2,5) e, sem fogueira, senta
        /// onde está desde que esteja dentro da casa.
        /// </summary>
        internal static bool DirectSit(CompanionAI companion, out string error)
        {
            error = null;

            if (!Enabled)
            {
                error = "[ModIA] O descanso esta desligado (Descanso/Ligado = false no .cfg).";
                return false;
            }

            if (companion == null)
            {
                error = "[ModIA] Nenhum companheiro no mundo.";
                return false;
            }

            if (!companion.JobIsIdle)
            {
                error = "[ModIA] O companheiro esta ocupado (trabalho, coleta, deposito ou combate); " +
                        "use modia_stop antes.";
                return false;
            }

            Stop(companion, "novo descanso pedido");

            GameObject fire = FindBurningFire(companion.transform.position, FireRadius * 2.5f);
            bool inHome = CompanionHome.Zone.Contains(companion.transform.position);
            if (fire == null && !inHome)
            {
                error = "[ModIA] Nao achei fogueira acesa por perto nem uma casa definida " +
                        "(modia_home set) para ele sentar.";
                return false;
            }

            _directed = true;
            _directedDeadline = Time.time + DirectedTimeout;
            BeginSit(companion, fire, "pedido do jogador (modia_rest sit)");
            return true;
        }

        /// <summary>
        /// <c>modia_rest sleep</c>: manda o companheiro <b>dormir</b> na cama livre mais próxima
        /// agora (mesmo de dia). Nunca usa a cama que é o seu ponto de spawn atual.
        /// </summary>
        internal static bool DirectSleep(CompanionAI companion, out string error)
        {
            error = null;

            if (!Enabled)
            {
                error = "[ModIA] O descanso esta desligado (Descanso/Ligado = false no .cfg).";
                return false;
            }

            if (companion == null)
            {
                error = "[ModIA] Nenhum companheiro no mundo.";
                return false;
            }

            if (!companion.JobIsIdle)
            {
                error = "[ModIA] O companheiro esta ocupado (trabalho, coleta, deposito ou combate); " +
                        "use modia_stop antes.";
                return false;
            }

            Bed bed = FindAvailableBed(companion.transform.position, BedRadius * 2f);
            if (bed == null)
            {
                error = "[ModIA] Nao ha cama LIVRE por perto (a sua cama, a que e o seu ponto de " +
                        "spawn, nao conta: construa uma segunda).";
                return false;
            }

            Stop(companion, "novo descanso pedido");

            _directed = true;
            _directedDeadline = Time.time + DirectedTimeout;
            BeginWalkToBed(companion, bed, SleepKind.Directed, "pedido do jogador (modia_rest sleep)");
            return true;
        }

        /// <summary>
        /// Encerra o descanso em andamento (o companheiro levanta e volta a seguir você). É o
        /// <c>modia_rest stop</c>, o <c>modia_stop</c>, o dismiss/morte e o seguir pelo portal.
        /// Devolve <c>true</c> quando havia algo para encerrar.
        /// </summary>
        internal static bool Stop(CompanionAI companion, string reason)
        {
            if (_state == RestState.Idle)
            {
                _directed = false;
                return false;
            }

            switch (_state)
            {
                case RestState.Sleeping:
                    StopSleeping(companion, reason);
                    return true;

                case RestState.Sitting:
                    StopSitting(companion, reason);
                    return true;

                default:
                    Plugin.Log.LogInfo($"[ModIA] Descanso: indo descansar cancelado ({reason}).");
                    ResetState();
                    return true;
            }
        }

        /// <summary>
        /// O companheiro saiu do mundo (dismiss/morte): desfaz o sono <b>sem</b> tentar mexer no
        /// corpo (ele está indo embora) e solta o estado.
        /// </summary>
        internal static void Forget(string reason)
        {
            if (_state == RestState.Sleeping)
            {
                RestoreBedCollisions();
                ClearInBedFlag();
                Plugin.Log.LogInfo($"[ModIA] Descanso: sono encerrado ({reason}).");
            }

            ResetState();
        }

        /// <summary>Limpa o campo "na cama" do ZDO usando o corpo já resolvido (dismiss/morte).</summary>
        private static void ClearInBedFlag()
        {
            ZNetView netView = _character != null ? _character.GetComponent<ZNetView>() : null;
            if (netView == null || !netView.IsValid() || !netView.IsOwner())
            {
                return;
            }

            ZDO zdo = netView.GetZDO();
            if (zdo != null)
            {
                zdo.Set(ZDOVars.s_inBed, false);
            }
        }

        // ---------------------------------------------------------------------
        // Sentar
        // ---------------------------------------------------------------------

        private static void BeginSit(CompanionAI companion, GameObject fire, string reason)
        {
            _fireTarget = fire;

            if (fire == null)
            {
                Sit(companion, reason);
                return;
            }

            float distance = Vector3.Distance(companion.transform.position, fire.transform.position);
            if (distance <= FireReach)
            {
                Sit(companion, reason);
                return;
            }

            _state = RestState.WalkToFire;
            _navDeadline = Time.time + NavTimeout;
            Plugin.Log.LogInfo(
                $"[ModIA] Descanso: indo sentar perto de '{fire.name}' ({reason}, " +
                $"{distance:0.#} m).");
        }

        private static void Sit(CompanionAI companion, string reason)
        {
            _state = RestState.Sitting;
            _restTimer = 0f;
            _restedGranted = false;
            _healTimer = 0f;
            _directedDeadline = _directed ? Time.time + DirectedTimeout : 0f;

            companion.JobHold(MovementOwner.Rest);

            if (_fireTarget != null)
            {
                Vector3 direction = _fireTarget.transform.position - companion.transform.position;
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.0001f)
                {
                    companion.JobLookAt(companion.transform.position + direction.normalized);
                }
            }

            SetSittingAnimation(true);

            if (_stamina != null)
            {
                _stamina.IsResting = true;
            }

            Plugin.Log.LogInfo($"[ModIA] Descanso: o companheiro sentou para descansar ({reason}).");
            CompanionJob.ShowHud("Companheiro descansando");
        }

        private static void UpdateSitting(CompanionAI companion, float dt, bool idle)
        {
            if (!StillValid(companion, idle, out string reason))
            {
                StopSitting(companion, reason);
                return;
            }

            if (_directed && Time.time > _directedDeadline)
            {
                StopSitting(companion, "o descanso pedido acabou");
                return;
            }

            companion.JobHold(MovementOwner.Rest);
            TickResting(dt);
        }

        private static void StopSitting(CompanionAI companion, string reason)
        {
            SetSittingAnimation(false);

            if (_stamina != null)
            {
                _stamina.IsResting = false;
            }

            Plugin.Log.LogInfo($"[ModIA] Descanso: o companheiro levantou ({reason}).");
            ResetState();
        }

        // ---------------------------------------------------------------------
        // Dormir
        // ---------------------------------------------------------------------

        private static void BeginWalkToBed(CompanionAI companion, Bed bed, SleepKind kind, string reason)
        {
            _bedTarget = bed;
            _sleepKind = kind;
            _bedSpot = bed.m_spawnPoint != null ? bed.m_spawnPoint.position : bed.transform.position;
            _bedSpotRotation = bed.m_spawnPoint != null ? bed.m_spawnPoint.rotation : bed.transform.rotation;

            float distance = Vector3.Distance(companion.transform.position, _bedSpot);
            if (distance <= BedReach)
            {
                BeginSleep(companion, reason);
                return;
            }

            _state = RestState.WalkToBed;
            _navDeadline = Time.time + NavTimeout;
            Plugin.Log.LogInfo(
                $"[ModIA] Descanso: indo dormir na cama '{bed.name}' ({reason}, {distance:0.#} m).");
        }

        private static void BeginSleep(CompanionAI companion, string reason)
        {
            _state = RestState.Sleeping;
            _restTimer = 0f;
            _restedGranted = false;
            _healTimer = 0f;
            _sleepDeadline = _sleepKind == SleepKind.Directed
                ? Time.time + DirectedTimeout
                : 0f;

            companion.JobTeleport(_bedSpot, _bedSpotRotation, MovementOwner.Sleep);
            IgnoreBedCollisions(companion);
            SetBedAnimation(true);

            if (_stamina != null)
            {
                _stamina.IsResting = true;
            }

            Plugin.Log.LogInfo(
                $"[ModIA] Descanso: o companheiro se deitou na cama " +
                $"'{BedName()}' ({reason}).");
            CompanionJob.ShowHud("Companheiro dormindo");
        }

        private static string BedName()
        {
            return _bedTarget != null ? _bedTarget.name : "?";
        }

        private static void UpdateSleeping(CompanionAI companion, float dt, bool idle)
        {
            if (!StillValid(companion, idle, out string reason))
            {
                StopSleeping(companion, reason);
                return;
            }

            switch (_sleepKind)
            {
                case SleepKind.Night:
                    // O jogo pula a noite (ZNetScene.UpdateSleeping) quando todos estão na cama:
                    // ao amanhecer ele levanta sozinho.
                    if (!EnvMan.IsNight())
                    {
                        StopSleeping(companion, "amanheceu");
                        return;
                    }

                    break;

                case SleepKind.Player:
                    Player player = Player.m_localPlayer;
                    if (player == null || !player.InBed())
                    {
                        StopSleeping(companion, "voce saiu da cama");
                        return;
                    }

                    break;

                default:
                    if (Time.time > _sleepDeadline)
                    {
                        StopSleeping(companion, "o sono pedido acabou");
                        return;
                    }

                    break;
            }

            // Deitado (gravidade desligada): reafirma a posição e trava o passo por tick.
            companion.JobPin(_bedSpot, _bedSpotRotation);

            TickResting(dt);
        }

        private static void StopSleeping(CompanionAI companion, string reason)
        {
            RestoreBedCollisions();

            if (companion != null)
            {
                companion.JobTeleport(StandUpSpot(), _bedSpotRotation, MovementOwner.Sleep);
            }

            SetBedAnimation(false);

            if (_stamina != null)
            {
                _stamina.IsResting = false;
            }

            // Acordar de uma noite de sono dá o descanso, como no jogo (o buff entra ao levantar).
            ApplyRestedBuff();

            Plugin.Log.LogInfo(
                $"[ModIA] Descanso: o companheiro acordou ({reason})." +
                (IsRested ? $" Descansado por {RestedRemaining:0} s (conforto {_restedComfort})." : string.Empty));
            CompanionJob.ShowHud(IsRested
                ? $"Companheiro descansado ({RestedRemaining:0}s)"
                : "Companheiro acordou");

            ResetState();
        }

        /// <summary>
        /// Onde o companheiro fica ao levantar: ao lado da cama (não em cima dela), como o jogador
        /// faria. Sem a cama (destruída), fica onde estava.
        /// </summary>
        private static Vector3 StandUpSpot()
        {
            if (_bedTarget == null)
            {
                return _bedSpot;
            }

            Vector3 side = _bedTarget.transform.right;
            side.y = 0f;
            if (side.sqrMagnitude < 0.0001f)
            {
                return _bedSpot;
            }

            return _bedSpot + side.normalized * 1.2f + Vector3.up * 0.3f;
        }

        // ---------------------------------------------------------------------
        // Caminhada (fogueira ou cama)
        // ---------------------------------------------------------------------

        private static void UpdateNavigation(CompanionAI companion, float dt, bool idle)
        {
            if (!StillValid(companion, idle, out string reason))
            {
                Stop(companion, reason);
                return;
            }

            if (Time.time > _navDeadline)
            {
                Stop(companion, "nao consegui chegar (tempo esgotado)");
                return;
            }

            if (_state == RestState.WalkToFire)
            {
                if (_fireTarget == null)
                {
                    // Sem fogueira o alvo é "onde ele está" (dentro da casa): senta aqui.
                    Sit(companion, "dentro da casa");
                    return;
                }

                Vector3 point = _fireTarget.transform.position;
                if (Vector3.Distance(companion.transform.position, point) <= FireReach)
                {
                    Sit(companion, "chegou na fogueira");
                    return;
                }

                companion.JobApproach(dt, point, FireReach, MovementOwner.Rest);
                return;
            }

            if (_bedTarget == null)
            {
                Stop(companion, "a cama sumiu");
                return;
            }

            if (Vector3.Distance(companion.transform.position, _bedSpot) <= BedReach)
            {
                BeginSleep(companion, "chegou na cama");
                return;
            }

            companion.JobApproach(dt, _bedSpot, BedReach, MovementOwner.Sleep);
        }

        // ---------------------------------------------------------------------
        // Validade do descanso
        // ---------------------------------------------------------------------

        /// <summary>
        /// O descanso ainda faz sentido? (livre, jogador por perto, sem inimigo, fogueira acesa e
        /// cama viva). Devolve <c>false</c> com o motivo para o chamador levantar o companheiro.
        /// </summary>
        private static bool StillValid(CompanionAI companion, bool idle, out string reason)
        {
            reason = null;

            if (!idle)
            {
                reason = "ele voltou a ter trabalho";
                return false;
            }

            if (companion.JobCharacter == null || companion.JobCharacter.IsDead())
            {
                reason = "o companheiro morreu";
                return false;
            }

            Player player = Player.m_localPlayer;
            if (player == null)
            {
                reason = "sem jogador no mundo";
                return false;
            }

            // Sentado, o "raio de descanso" é o mesmo que o de começar a sentar (um pouco mais
            // folgado): descanso é para você estar por perto, não para ele ficar parado enquanto
            // você vai embora. Dormindo vale Descanso/DistanciaAcordar (você não quer que ele
            // levante da cama só porque andou até a outra ponta da casa).
            float tolerance = _state == RestState.Sleeping || _state == RestState.WalkToBed
                ? WakeDistance
                : PlayerRadius * 1.5f;

            if (FlatDistance(companion.transform.position, player.transform.position) > tolerance)
            {
                reason = "voce se afastou";
                return false;
            }

            if (EnemyNearby(companion))
            {
                reason = "inimigo por perto";
                return false;
            }

            if (_state == RestState.Sitting || _state == RestState.WalkToFire)
            {
                if (_fireTarget == null)
                {
                    // Sem fogueira ele só está descansando por estar dentro da casa: se saiu de lá,
                    // não faz mais sentido continuar sentado.
                    if (!CompanionHome.Zone.Contains(companion.transform.position))
                    {
                        reason = "saiu da casa";
                        return false;
                    }
                }
                else
                {
                    Fireplace fireplace = _fireTarget.GetComponent<Fireplace>();
                    if (fireplace == null || !fireplace.IsBurning())
                    {
                        reason = "a fogueira apagou";
                        return false;
                    }
                }
            }

            if (_state == RestState.Sleeping || _state == RestState.WalkToBed)
            {
                if (_bedTarget == null)
                {
                    reason = "a cama sumiu";
                    return false;
                }
            }

            return true;
        }

        // ---------------------------------------------------------------------
        // Cura, resistência e o "descanso" do jogo
        // ---------------------------------------------------------------------

        /// <summary>
        /// O trabalho de descansar em si: cura por segundo (<c>Descanso/TaxaCura</c>), acumula o
        /// tempo de descanso e, passado o aquecimento (<c>Descanso/TempoParaDescansar</c>), dá o
        /// <b>descanso</b> — o equivalente ao <c>SE_Rested</c> do jogador, com os números do próprio
        /// <c>SE_Rested</c> e a duração pelo <b>nível de conforto</b> do lugar.
        /// </summary>
        private static void TickResting(float dt)
        {
            if (_character != null && !_character.IsDead() && HealRate > 0f)
            {
                _healTimer -= dt;
                if (_healTimer <= 0f)
                {
                    _healTimer = 0.5f;
                    if (_character.GetHealth() < _character.GetMaxHealth())
                    {
                        _character.Heal(HealRate * 0.5f, false);
                    }
                }
            }

            if (_restedGranted)
            {
                return;
            }

            _restTimer += dt;
            if (_restTimer >= WarmupSeconds)
            {
                ApplyRestedBuff();
            }
        }

        /// <summary>
        /// Dá (ou renova) o <b>descanso</b>: duração = <c>m_baseTTL + (conforto - 1) ×
        /// m_TTLPerComfortLevel</c>, exatamente a conta do <c>SE_Rested</c> do jogo, com o conforto
        /// calculado como para o jogador (<c>Cover.GetCoverForPoint</c> +
        /// <c>SE_Rested.CalculateComfortLevel</c>).
        /// </summary>
        private static void ApplyRestedBuff()
        {
            if (_character == null || _character.IsDead())
            {
                return;
            }

            CacheVanillaBonuses();

            _restedComfort = CalculateComfort(_character.transform.position);
            float duration = _baseTtl + (_restedComfort - 1) * _ttlPerComfort;

            if (IsRested && duration <= RestedRemaining)
            {
                // Já existe um descanso maior em andamento: não renova (e não deixa o chamador
                // tentando de novo a cada tick — o conforto custa uma varredura de peças).
                _restedGranted = true;
                return;
            }

            _restedUntil = Time.time + duration;
            _restedGranted = true;

            Plugin.Log.LogInfo(
                $"[ModIA] Descanso: o companheiro esta DESCANSADO por {duration:0} s " +
                $"(conforto {_restedComfort}): stamina x{_staminaRegenMultiplier:0.#}, " +
                $"cura x{_healthRegenMultiplier:0.#}, skill +{_skillModifier:0.##}.");
        }

        /// <summary>Nível de conforto do ponto, como o jogo calcula para o jogador.</summary>
        private static int CalculateComfort(Vector3 position)
        {
            Cover.GetCoverForPoint(position, out float cover, out bool underRoof);
            bool inShelter = cover >= 0.8f && underRoof;
            return SE_Rested.CalculateComfortLevel(inShelter, position);
        }

        /// <summary>
        /// Lê os números do <c>SE_Rested</c> vanilla uma vez (é a única fonte do tempo de descanso e
        /// dos multiplicadores do jogo). Sem o efeito no <c>ObjectDB</c>, valem os valores de reserva.
        /// </summary>
        private static void CacheVanillaBonuses()
        {
            if (_bonusesCached)
            {
                return;
            }

            ObjectDB database = ObjectDB.instance;
            if (database == null)
            {
                return; // tenta de novo no próximo descanso
            }

            _bonusesCached = true;
            StatusEffect effect = database.GetStatusEffect(SEMan.s_statusEffectRested);
            if (effect == null)
            {
                Plugin.Log.LogWarning(
                    "[ModIA] Descanso: o jogo nao tem o efeito SE_Rested; usando os valores de reserva.");
                return;
            }

            if (effect is SE_Rested rested)
            {
                _baseTtl = rested.m_baseTTL;
                _ttlPerComfort = rested.m_TTLPerComfortLevel;
                _healthRegenMultiplier = rested.m_healthRegenMultiplier;
                _staminaRegenMultiplier = rested.m_staminaRegenMultiplier;
                _skillModifier = rested.m_raiseSkillModifier;
                return;
            }

            if (effect is SE_Stats stats)
            {
                _healthRegenMultiplier = stats.m_healthRegenMultiplier;
                _staminaRegenMultiplier = stats.m_staminaRegenMultiplier;
                _skillModifier = stats.m_raiseSkillModifier;
                return;
            }

            Plugin.Log.LogWarning(
                $"[ModIA] Descanso: o SE_Rested do jogo e um '{effect.GetType().Name}' (nao um " +
                "SE_Stats); usando os valores de reserva.");
        }

        // ---------------------------------------------------------------------
        // Animação (emote_sit / attach_bed) e cama
        // ---------------------------------------------------------------------

        private static void SetSittingAnimation(bool sitting)
        {
            if (_zanim == null)
            {
                return;
            }

            if (!_zanim.HasParameter("emote_sit", AnimatorControllerParameterType.Bool))
            {
                if (!_loggedSitAnim)
                {
                    _loggedSitAnim = true;
                    Plugin.Log.LogWarning(
                        "[ModIA] Descanso: o Animator do companheiro nao tem o parametro " +
                        "'emote_sit'; ele descansa sem a animacao de sentar.");
                }

                return;
            }

            _zanim.SetBool("emote_sit", sitting);
            if (!sitting)
            {
                _zanim.SetTrigger("emote_stop");
            }
        }

        private static void SetBedAnimation(bool inBed)
        {
            if (_zanim == null)
            {
                return;
            }

            if (!_zanim.HasParameter("attach_bed", AnimatorControllerParameterType.Bool))
            {
                if (!_loggedBedAnim)
                {
                    _loggedBedAnim = true;
                    Plugin.Log.LogWarning(
                        "[ModIA] Descanso: o Animator do companheiro nao tem o parametro " +
                        "'attach_bed'; ele dorme sem a animacao de deitar.");
                }

                return;
            }

            _zanim.SetBool("attach_bed", inBed);
        }

        /// <summary>
        /// Deita o companheiro: ZDO marcado como "na cama" (<c>ZDOVars.s_inBed</c>, o mesmo campo que
        /// o <c>Player.AttachStart</c> do jogo escreve — é o que faz a noite passar mais rápido
        /// quando todos estão dormindo) e gravidade desligada, para ele não escorregar da cama
        /// enquanto o tick reafirma a posição.
        /// </summary>
        private static void IgnoreBedCollisions(CompanionAI companion)
        {
            SetInBedFlag(companion, true);

            Character character = companion.JobCharacter;
            if (character == null || _bedTarget == null)
            {
                return;
            }

            Collider capsule = character.GetComponent<Collider>();
            _bedColliders = _bedTarget.GetComponentsInChildren<Collider>();
            if (capsule == null || _bedColliders == null)
            {
                return;
            }

            for (int i = 0; i < _bedColliders.Length; i++)
            {
                if (_bedColliders[i] != null)
                {
                    Physics.IgnoreCollision(capsule, _bedColliders[i], true);
                }
            }

            if (character.m_body != null)
            {
                character.m_body.useGravity = false;
            }
        }

        /// <summary>Levanta o companheiro da cama: colisões de volta, gravidade de volta e ZDO limpo.</summary>
        private static void RestoreBedCollisions()
        {
            Character character = _character;
            if (character != null)
            {
                if (character.m_body != null)
                {
                    character.m_body.useGravity = true;
                }

                Collider capsule = character.GetComponent<Collider>();
                if (capsule != null && _bedColliders != null)
                {
                    for (int i = 0; i < _bedColliders.Length; i++)
                    {
                        if (_bedColliders[i] != null)
                        {
                            Physics.IgnoreCollision(capsule, _bedColliders[i], false);
                        }
                    }
                }
            }

            _bedColliders = null;
        }

        private static void SetInBedFlag(CompanionAI companion, bool value)
        {
            ZNetView netView = companion.GetComponent<ZNetView>();
            if (netView == null || !netView.IsValid() || !netView.IsOwner())
            {
                return;
            }

            ZDO zdo = netView.GetZDO();
            if (zdo != null)
            {
                zdo.Set(ZDOVars.s_inBed, value);
            }
        }

        // ---------------------------------------------------------------------
        // Varreduras (fogueira, cama, inimigo) — com buffer reaproveitado
        // ---------------------------------------------------------------------

        /// <summary>Fogueira <b>acesa</b> mais próxima dentro do raio (null quando não há).</summary>
        private static GameObject FindBurningFire(Vector3 position, float radius)
        {
            int count = Physics.OverlapSphereNonAlloc(
                position, radius, s_colliderBuffer, PieceMask, QueryTriggerInteraction.Collide);

            GameObject best = null;
            float bestDistance = radius;

            for (int i = 0; i < count; i++)
            {
                Collider collider = s_colliderBuffer[i];
                if (collider == null)
                {
                    continue;
                }

                Fireplace fireplace = collider.GetComponentInParent<Fireplace>();
                if (fireplace == null || !fireplace.IsBurning())
                {
                    continue;
                }

                float distance = Vector3.Distance(fireplace.transform.position, position);
                if (distance < bestDistance)
                {
                    best = fireplace.gameObject;
                    bestDistance = distance;
                }
            }

            return best;
        }

        /// <summary>
        /// Cama <b>livre</b> mais próxima dentro do raio. "Livre" = sem dono ou de um jogador local
        /// que <b>não</b> a usa como ponto de spawn (<c>Bed.IsCurrent</c>) — a cama do jogador é do
        /// jogador.
        /// </summary>
        private static Bed FindAvailableBed(Vector3 position, float radius)
        {
            int count = Physics.OverlapSphereNonAlloc(
                position, radius, s_colliderBuffer, PieceMask, QueryTriggerInteraction.Collide);

            Bed best = null;
            float bestDistance = radius;

            for (int i = 0; i < count; i++)
            {
                Collider collider = s_colliderBuffer[i];
                if (collider == null)
                {
                    continue;
                }

                Bed bed = collider.GetComponentInParent<Bed>();
                if (bed == null || !IsBedAvailable(bed))
                {
                    continue;
                }

                float distance = Vector3.Distance(bed.transform.position, position);
                if (distance < bestDistance)
                {
                    best = bed;
                    bestDistance = distance;
                }
            }

            return best;
        }

        /// <summary>
        /// A cama pode ser usada pelo companheiro? Cama sem dono pode; cama de outro jogador nunca;
        /// cama do jogador local só quando <b>não</b> é o ponto de spawn atual dele
        /// (mesma regra do <c>MHR_BedOwnerOk</c> da referência).
        /// </summary>
        private static bool IsBedAvailable(Bed bed)
        {
            if (bed == null)
            {
                return false;
            }

            try
            {
                long owner = bed.GetOwner();
                if (owner == 0L)
                {
                    return true;
                }

                Player player = Player.m_localPlayer;
                if (player == null || player.GetPlayerID() != owner)
                {
                    return false;
                }

                return !bed.IsCurrent();
            }
            catch (System.Exception e)
            {
                // Sem perfil de jogador (menu/transição) a checagem de dono pode estourar: nesse caso
                // é mais seguro NÃO usar a cama.
                Plugin.Log.LogWarning($"[ModIA] Descanso: nao consegui checar a cama '{bed.name}': {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Há criatura <b>hostil</b> perto? Usa o filtro do próprio jogo
        /// (<c>BaseAI.IsEnemy</c>), ignorando jogadores, domados e o próprio companheiro. A resposta
        /// é cacheada por <see cref="EnemyCheckInterval"/> s: a checagem roda no tick da IA (≈20 Hz)
        /// e não precisa ser mais fina que isso para decidir se ele levanta.
        /// </summary>
        private static bool EnemyNearby(CompanionAI companion)
        {
            if (Time.time < _enemyCheckUntil)
            {
                return _enemyNearby;
            }

            _enemyCheckUntil = Time.time + EnemyCheckInterval;
            _enemyNearby = ScanEnemyNearby(companion);
            return _enemyNearby;
        }

        private static bool ScanEnemyNearby(CompanionAI companion)
        {
            Character me = companion.JobCharacter;
            if (me == null)
            {
                return false;
            }

            s_characterBuffer.Clear();
            Character.GetCharactersInRange(companion.transform.position, EnemyRadius, s_characterBuffer);

            for (int i = 0; i < s_characterBuffer.Count; i++)
            {
                Character candidate = s_characterBuffer[i];
                if (candidate == null || candidate == me || candidate.IsDead() ||
                    candidate.IsPlayer() || candidate.IsTamed())
                {
                    continue;
                }

                if (BaseAI.IsEnemy(me, candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private static int PieceMask
        {
            get
            {
                if (s_pieceMask == -1)
                {
                    s_pieceMask = LayerMask.GetMask(
                        "piece", "piece_nonsolid", "Default", "static_solid");
                }

                return s_pieceMask;
            }
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static void ResetState()
        {
            _state = RestState.Idle;
            _fireTarget = null;
            _bedTarget = null;
            _bedColliders = null;
            _restTimer = 0f;
            _restedGranted = false;
            _directed = false;
            _directedDeadline = 0f;
            _sleepDeadline = 0f;
        }

        // ---------------------------------------------------------------------
        // Leitura (console/menu)
        // ---------------------------------------------------------------------

        /// <summary>Linha de estado para o <c>modia_stats</c>/<c>modia_rest</c>.</summary>
        internal static string Describe()
        {
            string rested = IsRested ? $"descansado ({RestedRemaining:0}s)" : "sem descanso";

            switch (_state)
            {
                case RestState.Sitting:
                    return $"sentado descansando" + (_directed ? " (pedido)" : string.Empty) + $", {rested}";

                case RestState.Sleeping:
                    return $"dormindo na cama" + (_directed ? " (pedido)" : string.Empty) + $", {rested}";

                case RestState.WalkToFire:
                    return "indo sentar perto do fogo";

                case RestState.WalkToBed:
                    return "indo para a cama";

                default:
                    return $"ocioso, {rested}";
            }
        }
    }
}
