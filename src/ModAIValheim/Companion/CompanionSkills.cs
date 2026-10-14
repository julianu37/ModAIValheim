using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// Perícias (skills) do companheiro (Fase 2.13).
    ///
    /// <para>
    /// <b>Por que o companheiro não tem skills do jogo.</b> No Valheim 1.0.17 as perícias vivem no
    /// componente <c>Skills</c> (um <c>MonoBehaviour</c>), que existe <b>só no prefab do jogador</b>
    /// e guarda uma referência ao <c>Player</c> (<c>Skills.Awake</c> → <c>GetComponent&lt;Player&gt;()</c>).
    /// O nosso prefab nasce de um clone do <c>Player</c> com o componente trocado por um
    /// <see cref="Humanoid"/> (<see cref="CompanionPrefab"/>), então o <c>Skills</c> foi removido de
    /// propósito e <c>Character.GetSkills()</c>/<c>GetSkillFactor()</c> devolvem <c>null</c>/<c>0</c>
    /// para o NPC. Sem isso o companheiro bateria sempre com "skill 0": custo de stamina cheio no
    /// ataque, bloqueio fraco e nenhuma evolução de perícia.
    /// </para>
    ///
    /// <para>
    /// <b>O que fazemos.</b> Guardamos o nível e o acumulador de cada perícia do companheiro aqui
    /// (mesma matemática do <c>Skills.Skill.Raise</c> do jogo: ganho =
    /// <c>m_increseStep × fator × Game.m_skillGainRate</c>, próximo nível =
    /// <c>pow(floor(nível + 1), 1.5) × 0.5 + 0.5</c>) e <b>redirecionamos</b> as chamadas do jogo
    /// para cá por Harmony (<see cref="CompanionSkillPatches"/>):
    /// </para>
    /// <list type="bullet">
    ///   <item><description><c>Character.RaiseSkill</c> → ganha perícia de verdade (e o aviso
    ///   <c>"… is tamed but missing tameable or monster AI script!"</c> deixa de existir, porque o
    ///   corpo do jogo — que só sabia logar o aviso e sair — <b>não roda mais</b> para o NPC).</description></item>
    ///   <item><description><c>Character.GetSkillFactor</c> → o custo de stamina do ataque
    ///   (<c>Attack.GetAttackStamina</c>), o poder do bloqueio (<c>Humanoid.BlockAttack</c>) e as
    ///   contas de ferramenta passam a usar o nível do companheiro, como no jogador. (O
    ///   <c>GetRandomSkillFactor</c>, que é o <b>multiplicador de dano</b>, fica <b>de fora de
    ///   propósito</b> — ver <see cref="CompanionSkillPatches"/>.)</description></item>
    /// </list>
    ///
    /// <para>
    /// As definições (<c>Skills.SkillDef</c>: ícone, descrição e <c>m_increseStep</c>) são copiadas
    /// <b>uma vez</b> do prefab do jogador (<see cref="CacheSkillDefs"/>) — é a única fonte do passo
    /// de ganho de cada perícia, que varia por skill.
    /// </para>
    ///
    /// <para>
    /// Nada aqui roda por frame: <see cref="RaiseSkill"/> só é chamado quando o jogo dá o ganho
    /// (golpe que acerta, bloqueio, corrida, nado) e o estado só é escrito no snapshot em
    /// dismiss/morte/fim de mundo.
    /// </para>
    /// </summary>
    internal sealed class CompanionSkills : MonoBehaviour
    {
        /// <summary>Teto de perícia do jogo (<c>Skills.c_MaxSkillLevel</c>).</summary>
        internal const float MaxLevel = 100f;

        /// <summary>Definições de perícia do jogo, copiadas do prefab do jogador (uma vez por sessão).</summary>
        private static readonly List<Skills.SkillDef> s_defs = new List<Skills.SkillDef>();

        private static bool s_defsReady;

        /// <summary>Momento do último aviso de "sem SkillDef" (rate-limited: a chamada pode ser por golpe).</summary>
        private static float s_lastMissingDefWarnAt = -999f;

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<bool> _announce;
        private static ConfigEntry<bool> _loseOnDeath;
        private static ConfigEntry<float> _deathLossFactor;

        /// <summary>Nível + acumulador de uma perícia (o "Skills.Skill" do companheiro).</summary>
        private sealed class SkillState
        {
            internal Skills.SkillDef Def;
            internal float Level;
            internal float Accumulator;
        }

        private readonly Dictionary<Skills.SkillType, SkillState> _skills =
            new Dictionary<Skills.SkillType, SkillState>();

        private Character _character;

        /// <summary>Evolução de perícia ligada (config <c>Skills/EvoluirSkills</c>).</summary>
        internal static bool Enabled => _enabled == null || _enabled.Value;

        /// <summary>Avisar no HUD quando o companheiro sobe de nível (config).</summary>
        private static bool Announce => _announce == null || _announce.Value;

        /// <summary>Perder perícia ao morrer (config <c>Skills/PerderSkillsAoMorrer</c>).</summary>
        private static bool LoseOnDeath => _loseOnDeath != null && _loseOnDeath.Value;

        /// <summary>Fração perdida ao morrer (config; o jogo usa <c>Skills.m_DeathLowerFactor = 0.25</c>).</summary>
        private static float DeathLossFactor =>
            _deathLossFactor != null ? Mathf.Clamp(_deathLossFactor.Value, 0f, 1f) : 0.25f;

        internal static void Configure(
            ConfigEntry<bool> enabled,
            ConfigEntry<bool> announce,
            ConfigEntry<bool> loseOnDeath,
            ConfigEntry<float> deathLossFactor)
        {
            _enabled = enabled;
            _announce = announce;
            _loseOnDeath = loseOnDeath;
            _deathLossFactor = deathLossFactor;

            Plugin.Log.LogInfo(
                $"[ModIA] Skills: evolucao = {Enabled} | avisar subida = {Announce} | " +
                $"perder ao morrer = {LoseOnDeath} (fator {DeathLossFactor:0.##}).");
        }

        /// <summary>
        /// Copia as <c>Skills.SkillDef</c> do prefab do jogador (nome/ícone/<c>m_increseStep</c>).
        /// Chamado no registro do prefab do companheiro (<see cref="CompanionPrefab"/>), quando o
        /// prefab do jogador ainda está intacto — inclusive o componente <c>Skills</c> que o clone
        /// do companheiro perde na conversão.
        /// </summary>
        internal static void CacheSkillDefs(GameObject playerPrefab)
        {
            if (s_defsReady)
            {
                return;
            }

            Skills skills = playerPrefab != null ? playerPrefab.GetComponent<Skills>() : null;
            if (skills != null && skills.m_skills != null && skills.m_skills.Count > 0)
            {
                s_defs.Clear();
                for (int i = 0; i < skills.m_skills.Count; i++)
                {
                    Skills.SkillDef def = skills.m_skills[i];
                    if (def != null)
                    {
                        s_defs.Add(def);
                    }
                }

                s_defsReady = true;
                Plugin.Log.LogInfo(
                    $"[ModIA] Perícias do jogo copiadas do prefab do jogador: {s_defs.Count}.");
                return;
            }

            // Rede de segurança: sem as definições do jogo o companheiro simplesmente não evoluiria
            // nada (falha silenciosa). Criamos definições mínimas (passo de ganho 1, sem ícone) para
            // todas as perícias do enum — o ganho continua, só perde o ícone e o passo específico.
            BuildFallbackDefs();
        }

        private static void BuildFallbackDefs()
        {
            System.Array values = System.Enum.GetValues(typeof(Skills.SkillType));
            s_defs.Clear();
            for (int i = 0; i < values.Length; i++)
            {
                Skills.SkillType type = (Skills.SkillType)values.GetValue(i);
                if (type == Skills.SkillType.None || type == Skills.SkillType.All)
                {
                    continue;
                }

                Skills.SkillDef def = new Skills.SkillDef();
                def.m_skill = type;
                def.m_increseStep = 1f;
                s_defs.Add(def);
            }

            s_defsReady = s_defs.Count > 0;
            Plugin.Log.LogWarning(
                $"[ModIA] Não foi possível ler as perícias do prefab do jogador; usando definições " +
                $"mínimas para {s_defs.Count} perícias (o companheiro evolui, mas sem ícone).");
        }

        private void Awake()
        {
            _character = GetComponent<Character>();
        }

        // ---------------------------------------------------------------------
        // Ganho de perícia
        // ---------------------------------------------------------------------

        /// <summary>
        /// Dá <paramref name="factor"/> de ganho na perícia <paramref name="type"/>, com a mesma
        /// conta do <c>Skills.Skill.Raise</c> do jogo (incluindo o modificador de efeitos de status
        /// via <c>SEMan.ModifyRaiseSkill</c>, que é o que faz a perícia subir mais rápido descansado).
        /// </summary>
        internal void RaiseSkill(Skills.SkillType type, float factor)
        {
            if (!Enabled || type == Skills.SkillType.None || factor <= 0f)
            {
                return;
            }

            SkillState state = GetOrCreate(type);
            if (state == null || state.Level >= MaxLevel)
            {
                return;
            }

            float multiplier = 1f;
            SEMan seman = _character != null ? _character.GetSEMan() : null;
            if (seman != null)
            {
                seman.ModifyRaiseSkill(type, ref multiplier);
            }

            // Fase 2.18: o descanso também acelera o ganho de perícia, como o m_raiseSkillModifier
            // do SE_Rested do jogador.
            multiplier *= CompanionRest.SkillGainMultiplier;

            float gain = state.Def.m_increseStep * factor * multiplier * Game.m_skillGainRate;
            state.Accumulator += gain;

            if (state.Accumulator < NextLevelRequirement(state.Level))
            {
                return;
            }

            state.Level = Mathf.Clamp(state.Level + 1f, 0f, MaxLevel);
            state.Accumulator = 0f;

            OnLevelUp(type, state);
        }

        /// <summary>Requisito do próximo nível — a mesma fórmula do <c>Skills.Skill</c>.</summary>
        private static float NextLevelRequirement(float level)
        {
            return Mathf.Pow(Mathf.Floor(level + 1f), 1.5f) * 0.5f + 0.5f;
        }

        /// <summary>
        /// Fator da perícia (0 a 1), no formato que o jogo consome em
        /// <c>Attack.GetAttackStamina</c> (custo de stamina do ataque), <c>Humanoid.BlockAttack</c>
        /// (poder de bloqueio) e nas contas de ferramenta.
        /// </summary>
        internal float GetSkillFactor(Skills.SkillType type)
        {
            return Mathf.Clamp01(GetLevel(type) / MaxLevel);
        }

        /// <summary>Nível (0 a 100) da perícia; 0 quando não há registro.</summary>
        internal float GetLevel(Skills.SkillType type)
        {
            if (_skills.TryGetValue(type, out SkillState state) && state != null)
            {
                return state.Level;
            }

            return 0f;
        }

        private SkillState GetOrCreate(Skills.SkillType type)
        {
            if (_skills.TryGetValue(type, out SkillState existing) && existing != null)
            {
                return existing;
            }

            Skills.SkillDef def = FindDef(type);
            if (def == null)
            {
                // Uma vez a cada 10 s: isto pode ser chamado a cada golpe.
                if (Time.time - s_lastMissingDefWarnAt >= 10f)
                {
                    s_lastMissingDefWarnAt = Time.time;
                    Plugin.Log.LogWarning(
                        $"[ModIA] Não há definição de perícia para '{type}'; o ganho é ignorado " +
                        "(as definições vêm do prefab do jogador).");
                }

                return null;
            }

            SkillState state = new SkillState();
            state.Def = def;
            _skills[type] = state;
            return state;
        }

        private static Skills.SkillDef FindDef(Skills.SkillType type)
        {
            for (int i = 0; i < s_defs.Count; i++)
            {
                if (s_defs[i] != null && s_defs[i].m_skill == type)
                {
                    return s_defs[i];
                }
            }

            return null;
        }

        private void OnLevelUp(Skills.SkillType type, SkillState state)
        {
            string name = CompanionHover.GetName();
            Plugin.Log.LogInfo(
                $"[ModIA] Skill: '{name}' subiu {type} para {(int)state.Level}.");

            // O efeito visual de subida de nível do jogo (m_skillLevelupEffects) é do Player, não
            // existe no nosso Humanoid — o aviso sai pelo HUD (abaixo) e pelo log.

            if (!Announce)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return;
            }

            string message = name + " — $msg_skillup $skill_" +
                             type.ToString().ToLowerInvariant() + ": " + (int)state.Level;
            player.Message(MessageHud.MessageType.TopLeft, message, 0, state.Def.m_icon);
        }

        // ---------------------------------------------------------------------
        // Morte
        // ---------------------------------------------------------------------

        /// <summary>
        /// Perda de perícia ao morrer, como no jogo (<c>Player.OnDeath</c> →
        /// <c>Skills.LowerAllSkills</c>). Chamado pelo <see cref="CompanionManager"/> <b>antes</b>
        /// do snapshot, para o companheiro guardado voltar com o que sobrou.
        /// </summary>
        internal void ApplyDeathPenalty()
        {
            if (!LoseOnDeath || _skills.Count == 0)
            {
                return;
            }

            float factor = DeathLossFactor * Game.m_skillReductionRate;
            foreach (KeyValuePair<Skills.SkillType, SkillState> pair in _skills)
            {
                SkillState state = pair.Value;
                if (state == null)
                {
                    continue;
                }

                state.Level = Mathf.Max(0f, state.Level - state.Level * factor);
                state.Accumulator = 0f;
            }

            Plugin.Log.LogInfo(
                $"[ModIA] Skill: o companheiro morreu e perdeu {factor * 100f:0}% de cada perícia " +
                "(config Skills/PerderSkillsAoMorrer).");
        }

        // ---------------------------------------------------------------------
        // Snapshot (Fase 2.7/2.13)
        // ---------------------------------------------------------------------

        /// <summary>Preenche a lista de perícias do snapshot (só o que tem nível ou acumulador).</summary>
        internal void CaptureState(List<CompanionSkillState> into)
        {
            if (into == null)
            {
                return;
            }

            into.Clear();
            foreach (KeyValuePair<Skills.SkillType, SkillState> pair in _skills)
            {
                SkillState state = pair.Value;
                if (state == null || (state.Level <= 0f && state.Accumulator <= 0f))
                {
                    continue;
                }

                CompanionSkillState record = new CompanionSkillState();
                record.Skill = (int)pair.Key;
                record.Level = state.Level;
                record.Accumulator = state.Accumulator;
                into.Add(record);
            }
        }

        /// <summary>Devolve as perícias guardadas (o companheiro volta com o que sabia).</summary>
        internal void RestoreState(List<CompanionSkillState> records)
        {
            if (records == null || records.Count == 0)
            {
                return;
            }

            int restored = 0;
            for (int i = 0; i < records.Count; i++)
            {
                CompanionSkillState record = records[i];
                if (record == null)
                {
                    continue;
                }

                Skills.SkillType type = (Skills.SkillType)record.Skill;
                Skills.SkillDef def = FindDef(type);
                if (def == null)
                {
                    Plugin.Log.LogWarning(
                        $"[ModIA] Perícia {record.Skill} do snapshot não existe mais no jogo; ignorada.");
                    continue;
                }

                SkillState state = new SkillState();
                state.Def = def;
                state.Level = Mathf.Clamp(record.Level, 0f, MaxLevel);
                state.Accumulator = Mathf.Max(0f, record.Accumulator);
                _skills[type] = state;
                restored++;
            }

            Plugin.Log.LogInfo($"[ModIA] Perícias restauradas do snapshot: {restored}.");
        }

        // ---------------------------------------------------------------------
        // Leitura (console/menu)
        // ---------------------------------------------------------------------

        /// <summary>
        /// As <paramref name="count"/> perícias mais altas em uma linha legível
        /// (ex.: <c>WoodCutting 12, Pickaxes 7</c>), para o console e o menu.
        /// </summary>
        internal string DescribeTop(int count)
        {
            if (_skills.Count == 0)
            {
                return string.Empty;
            }

            List<KeyValuePair<Skills.SkillType, SkillState>> ordered =
                new List<KeyValuePair<Skills.SkillType, SkillState>>(_skills);
            ordered.Sort((a, b) => b.Value.Level.CompareTo(a.Value.Level));

            StringBuilder builder = new StringBuilder();
            int written = 0;
            for (int i = 0; i < ordered.Count && written < count; i++)
            {
                SkillState state = ordered[i].Value;
                if (state == null || state.Level < 1f)
                {
                    continue;
                }

                if (written > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(DisplayName(ordered[i].Key));
                builder.Append(' ');
                builder.Append(((int)state.Level).ToString(CultureInfo.InvariantCulture));
                written++;
            }

            return builder.ToString();
        }

        /// <summary>Nome localizado da perícia (<c>$skill_woodcutting</c> → "Corte de madeira").</summary>
        internal static string DisplayName(Skills.SkillType type)
        {
            string token = "$skill_" + type.ToString().ToLowerInvariant();
            Localization localization = Localization.instance;
            return localization != null ? localization.Localize(token) : token;
        }
    }
}
