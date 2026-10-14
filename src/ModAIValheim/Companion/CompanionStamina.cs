using BepInEx.Configuration;
using UnityEngine;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// Resistência (stamina) própria do companheiro (Fase 2.13).
    ///
    /// <para>
    /// <b>Por que precisamos de uma stamina nossa.</b> No Valheim 1.0.17 os campos/métodos de
    /// stamina vivem no <c>Player</c>: <c>Character.UseStamina</c>, <c>Character.HaveStamina</c> e
    /// <c>Character.AddStamina</c> são <c>virtual</c> e <b>vazios</b> na base — quem os implementa é
    /// o <c>Player</c> (<c>m_stamina</c>/<c>m_maxStamina</c> no ZDO, <c>RPC_UseStamina</c>). O nosso
    /// companheiro é um <see cref="Humanoid"/> (não um <c>Player</c>), então hoje <b>correr, atacar,
    /// bloquear e pular são de graça</b> para ele: <c>HaveStamina()</c> devolve sempre <c>true</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Como funciona.</b> Guardamos a stamina do companheiro aqui e
    /// <see cref="CompanionStaminaPatches"/> redireciona as chamadas do jogo:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><c>Character.UseStamina(v)</c> → <see cref="Drain"/> (é o caminho que o
    ///   jogo usa em <c>Attack.DoAttack</c> — <c>m_character.UseStamina(GetAttackStamina())</c> —,
    ///   em <c>Humanoid.BlockAttack</c> e em <c>Character.OnJump</c>); logo <b>atacar, bloquear e
    ///   pular gastam de verdade</b> e o custo já vem reduzido pela perícia da arma
    ///   (<c>Attack.GetAttackStamina</c> consulta <c>GetSkillFactor</c>, que é nosso).</description></item>
    ///   <item><description><c>Character.HaveStamina(amount)</c> → <c>Stamina &gt; amount</c> (o
    ///   próprio <c>Attack.DoAttack</c> usa isso para recusar o golpe sem stamina).</description></item>
    ///   <item><description><c>Character.AddStamina(v)</c> → <see cref="Restore"/> (efeitos de status
    ///   como hidromel/poções de stamina também funcionam no NPC).</description></item>
    ///   <item><description><c>Character.CheckRun</c> (postfix) → devolve <c>false</c> com a stamina
    ///   zerada: o companheiro <b>para de correr</b> em vez de continuar em disparada
    ///   (<c>MonsterAI.Follow</c> só corre a mais de 10 m).</description></item>
    /// </list>
    ///
    /// <para>
    /// <b>Gasto e regeneração</b> são calculados por <see cref="Tick"/> (chamado do
    /// <see cref="CompanionAI"/>, ~20 Hz): correr e nadar gastam por segundo
    /// (<c>Resistencia/GastoCorrida</c>, <c>GastoNado</c>) e a stamina volta depois de um atraso
    /// (<c>AtrasoRegen</c>), com a mesma forma do jogador (<c>TaxaRegen</c> + bônus proporcional ao
    /// que falta) e multiplicador dos efeitos de status (<c>SEMan.ModifyStaminaRegen</c> — é o que
    /// faz "Molhado/Congelado" atrapalharem o NPC como atrapalham você). O teto é
    /// <c>StaminaBase</c> + a stamina da comida que ele tem no corpo (<see cref="CompanionFood"/>).
    /// </para>
    ///
    /// <para>
    /// Com <c>Resistencia/StaminaLigada = false</c> a stamina fica sempre cheia e os patches deixam
    /// de bloquear qualquer ação — é o "modo antigo", sem cansaço.
    /// </para>
    /// </summary>
    internal sealed class CompanionStamina : MonoBehaviour
    {
        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<float> _baseStamina;
        private static ConfigEntry<float> _runDrain;
        private static ConfigEntry<float> _swimDrain;
        private static ConfigEntry<float> _regenRate;
        private static ConfigEntry<float> _regenDelay;

        private Character _character;
        private CompanionFood _food;
        private CompanionSkills _skills;

        private float _regenDelayTimer;
        private float _runSkillTimer;
        private bool _drainLogged;

        /// <summary>Stamina atual do companheiro.</summary>
        internal float Stamina { get; private set; }

        /// <summary>
        /// O companheiro está <b>descansando</b> (sentado no fogo ou dormindo na cama — Fase 2.18).
        /// Quem escreve é o <see cref="CompanionRest"/>; aqui isso vale regeneração acelerada e
        /// nenhum atraso de regeneração (ninguém cansa descansando).
        /// </summary>
        internal bool IsResting { get; set; }

        /// <summary>Stamina ligada (config <c>Resistencia/StaminaLigada</c>).</summary>
        internal static bool Enabled => _enabled == null || _enabled.Value;

        /// <summary>Teto de stamina: base + bônus da comida ativa (como no jogador).</summary>
        internal float MaxStamina
        {
            get
            {
                float baseValue = _baseStamina != null ? Mathf.Max(1f, _baseStamina.Value) : 100f;
                return baseValue + (_food != null ? _food.TotalStaminaBonus : 0f);
            }
        }

        /// <summary>Fração da stamina (0 a 1) — usada pela comida, pelo menu e pelo console.</summary>
        internal float GetStaminaPercentage()
        {
            float max = MaxStamina;
            return max > 0f ? Mathf.Clamp01(Stamina / max) : 1f;
        }

        internal static void Configure(
            ConfigEntry<bool> enabled,
            ConfigEntry<float> baseStamina,
            ConfigEntry<float> runDrain,
            ConfigEntry<float> swimDrain,
            ConfigEntry<float> regenRate,
            ConfigEntry<float> regenDelay)
        {
            _enabled = enabled;
            _baseStamina = baseStamina;
            _runDrain = runDrain;
            _swimDrain = swimDrain;
            _regenRate = regenRate;
            _regenDelay = regenDelay;

            Plugin.Log.LogInfo(
                $"[ModIA] Resistencia: ligada = {Enabled} | base = " +
                $"{(_baseStamina != null ? _baseStamina.Value : 100f)} | corrida = " +
                $"{(_runDrain != null ? _runDrain.Value : 10f)}/s | nado = " +
                $"{(_swimDrain != null ? _swimDrain.Value : 10f)}/s | regen = " +
                $"{(_regenRate != null ? _regenRate.Value : 6f)}/s (+{(_regenDelay != null ? _regenDelay.Value : 1f)}s de atraso).");
        }

        private void Awake()
        {
            _character = GetComponent<Character>();
            ResolveDependencies();
            Stamina = MaxStamina;
        }

        private void ResolveDependencies()
        {
            if (_character == null)
            {
                _character = GetComponent<Character>();
            }

            if (_food == null)
            {
                _food = GetComponent<CompanionFood>();
            }

            if (_skills == null)
            {
                _skills = GetComponent<CompanionSkills>();
            }
        }

        // ---------------------------------------------------------------------
        // API (usada pelos patches e pela comida)
        // ---------------------------------------------------------------------

        /// <summary>Gasta stamina (sem travar nada: quem decide é <c>HaveStamina</c>).</summary>
        internal void Drain(float amount)
        {
            if (!Enabled || float.IsNaN(amount) || amount <= 0f)
            {
                return;
            }

            float before = Stamina;
            Stamina = Mathf.Max(0f, Stamina - amount);
            _regenDelayTimer = RegenDelay;

            if (before > 0f && Stamina <= 0f && !_drainLogged)
            {
                _drainLogged = true;
                Plugin.Log.LogInfo(
                    "[ModIA] Resistencia: o companheiro ficou SEM stamina (ele para de correr e " +
                    "recupera em alguns segundos).");
            }
        }

        /// <summary>Devolve stamina (efeitos de status, hidromel, descanso).</summary>
        internal void Restore(float amount)
        {
            if (float.IsNaN(amount) || amount <= 0f)
            {
                return;
            }

            Stamina = Mathf.Min(MaxStamina, Stamina + amount);
            if (Stamina > 0f)
            {
                _drainLogged = false;
            }
        }

        /// <summary>Define a stamina (restauração do snapshot).</summary>
        internal void SetStamina(float value)
        {
            Stamina = Mathf.Clamp(float.IsNaN(value) ? 0f : value, 0f, MaxStamina);
            _drainLogged = Stamina <= 0f;
        }

        /// <summary>
        /// Reaplica o teto depois de a comida mudar (comer/expirar): a stamina atual é apenas
        /// aparada no novo teto — comer nunca "cura" stamina já gasta, como no jogador.
        /// </summary>
        internal void RefreshMax()
        {
            Stamina = Mathf.Clamp(Stamina, 0f, MaxStamina);
        }

        /// <summary>Esquece a comida (a comida sai do corpo ao dispensar/morrer).</summary>
        internal void ClearBonuses()
        {
            Stamina = Mathf.Clamp(Stamina, 0f, MaxStamina);
        }

        // ---------------------------------------------------------------------
        // Ciclo (chamado pelo CompanionAI, só no dono do ZDO)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Gasto por corrida/nado, regeneração e o ganho da perícia de corrida. Nunca roda por
        /// frame: é chamado do <see cref="CompanionAI.UpdateAI"/> (~20 Hz, dt fixo de 0.05).
        /// </summary>
        internal void Tick(float dt)
        {
            ResolveDependencies();

            if (_character == null || _character.IsDead())
            {
                return;
            }

            if (!Enabled)
            {
                // "Modo antigo": stamina sempre cheia, nenhuma ação é bloqueada.
                if (Stamina < MaxStamina)
                {
                    Stamina = MaxStamina;
                }

                return;
            }

            float max = MaxStamina;
            if (Stamina > max)
            {
                Stamina = max;
            }

            if (float.IsNaN(Stamina))
            {
                Stamina = 0f;
            }

            bool draining = ApplyMovementDrain(dt);
            if (!draining)
            {
                if (IsResting)
                {
                    // Descansando não há atraso: a stamina volta já no primeiro tick (Fase 2.18).
                    _regenDelayTimer = 0f;
                }
                else
                {
                    _regenDelayTimer = Mathf.Max(0f, _regenDelayTimer - dt);
                }

                if (_regenDelayTimer <= 0f && Stamina < max)
                {
                    Stamina = Mathf.Min(max, Stamina + RegenPerSecond() * dt);
                    if (Stamina > 0f)
                    {
                        _drainLogged = false;
                    }
                }
            }
        }

        /// <summary>
        /// Correr/nadar gasta por segundo (o jogador gasta em <c>Player.CheckRun</c>/
        /// <c>Player.OnSwimming</c>, que não existem no NPC) e, correndo, o companheiro ganha a
        /// perícia <c>Run</c> — um ponto por segundo, como no jogo.
        /// </summary>
        /// <returns><c>true</c> quando houve gasto neste tick.</returns>
        private bool ApplyMovementDrain(float dt)
        {
            float velocity = _character.GetVelocity().magnitude;

            if (_character.IsSwimming() && velocity > 0.25f)
            {
                float swim = _swimDrain != null ? Mathf.Max(0f, _swimDrain.Value) : 10f;
                Stamina = Mathf.Max(0f, Stamina - swim * dt * Game.m_moveStaminaRate);
                _regenDelayTimer = RegenDelay;
                return true;
            }

            if (!_character.IsRunning() || !_character.IsOnGround() || velocity <= 0.5f)
            {
                _runSkillTimer = 0f;
                return false;
            }

            // O jogador reduz o custo da corrida pela perícia (Lerp(1, 0.5, fator)) — mantemos isso
            // para a perícia Run ter efeito prático no companheiro.
            float skillFactor = _skills != null ? _skills.GetSkillFactor(Skills.SkillType.Run) : 0f;
            float run = _runDrain != null ? Mathf.Max(0f, _runDrain.Value) : 10f;
            run *= Mathf.Lerp(1f, 0.5f, skillFactor);

            Stamina = Mathf.Max(0f, Stamina - run * dt * Game.m_moveStaminaRate);
            _regenDelayTimer = RegenDelay;

            if (_skills != null)
            {
                _runSkillTimer += dt;
                if (_runSkillTimer >= 1f)
                {
                    _runSkillTimer = 0f;
                    _skills.RaiseSkill(Skills.SkillType.Run, 1f);
                }
            }

            return true;
        }

        private float RegenPerSecond()
        {
            float rate = _regenRate != null ? Mathf.Max(0f, _regenRate.Value) : 6f;

            // Mesma forma do jogador: quanto mais vazio, mais rápido enche.
            rate *= 1f + (1f - GetStaminaPercentage());

            // Fase 2.18: descansar (ou estar descansado) acelera a recuperação, como o SE_Rested
            // do jogador.
            rate *= CompanionRest.StaminaRegenMultiplier;

            SEMan seman = _character != null ? _character.GetSEMan() : null;
            if (seman != null)
            {
                float multiplier = 1f;
                seman.ModifyStaminaRegen(ref multiplier);
                rate *= multiplier;
            }

            return rate;
        }

        private static float RegenDelay =>
            _regenDelay != null ? Mathf.Clamp(_regenDelay.Value, 0f, 10f) : 1f;

        // ---------------------------------------------------------------------
        // Leitura (menu/console)
        // ---------------------------------------------------------------------

        /// <summary>Linha legível do estado (ex.: <c>62 / 100</c>).</summary>
        internal string Describe()
        {
            return string.Format("{0:0} / {1:0}", Stamina, MaxStamina);
        }
    }
}
