using BepInEx.Configuration;
using UnityEngine;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// Estado de combate do companheiro (Fase 2.4a): modo atual, configuração e a memória curta de
    /// "quem nos agrediu" (usada pelo modo Defensive para revidar).
    ///
    /// <para>
    /// Este tipo é <b>estático e sem estado de cena</b>: ele só guarda o modo/parâmetros e alimenta
    /// a <see cref="CompanionAI"/> com eles. Quem de fato escolhe alvo, persegue e ataca é a IA da
    /// instância (que roda apenas no servidor/dono do ZDO — o modelo autoritativo do mod é
    /// preservado).
    /// </para>
    ///
    /// <para>
    /// A memória de agressão é alimentada de dois lugares: (1) <c>CompanionAI.OnDamaged</c> quando o
    /// próprio companheiro leva dano; (2) o patch Harmony em <c>Character.Damage</c>
    /// (<see cref="CompanionCombatPatches"/>), que cobre tanto o dono agredido quanto o dono
    /// atacando alguém.
    /// </para>
    /// </summary>
    internal static class CompanionCombat
    {
        /// <summary>Quanto tempo (s) a memória de agressão sobrevive sem um novo estímulo.</summary>
        private const float AttackerMemorySeconds = 15f;

        /// <summary>
        /// Tempo (s) mínimo com um inimigo em <c>InAttack()</c> antes de erguer o escudo. É o
        /// <c>_enemySwingTimer &gt;= 0.2f</c> da referência (item #6): dá tempo de o golpe "carregar"
        /// antes de bloquear, em vez de bloquear a cada tique.
        /// </summary>
        internal const float BlockDelay = 0.2f;

        private static ConfigEntry<CompanionMode> _mode;
        private static ConfigEntry<KeyCode> _modeKey;
        private static ConfigEntry<float> _aggressiveRadius;
        private static ConfigEntry<float> _defensiveRadius;
        private static ConfigEntry<float> _leashDistance;
        private static ConfigEntry<bool> _attackNeutralAnimals;
        private static ConfigEntry<float> _scanInterval;
        private static ConfigEntry<bool> _blockAttacks;
        private static ConfigEntry<float> _blockRadius;
        private static ConfigEntry<float> _blockHold;

        /// <summary>Quem agrediu (companheiro/jogador) ou quem o jogador atacou, e quando.</summary>
        private static Character _recentAttacker;
        private static float _recentAttackerTime = -9999f;

        internal static CompanionMode Mode => _mode != null ? _mode.Value : CompanionMode.Defensive;

        internal static KeyCode ModeKey => _modeKey != null ? _modeKey.Value : KeyCode.F3;

        /// <summary>Raio (m) da varredura do modo Aggressive, medido a partir do companheiro.</summary>
        internal static float AggressiveRadius => _aggressiveRadius != null ? _aggressiveRadius.Value : 30f;

        /// <summary>Raio (m) em que o modo Defensive procura quem está caçando o dono/companheiro.</summary>
        internal static float DefensiveRadius => _defensiveRadius != null ? _defensiveRadius.Value : 25f;

        /// <summary>Distância (m) máxima do alvo até o jogador seguido (a "coleira").</summary>
        internal static float LeashDistance => _leashDistance != null ? _leashDistance.Value : 50f;

        /// <summary>Se true, o companheiro também caça a fauna neutra (fação <c>AnimalsVeg</c>).</summary>
        internal static bool AttackNeutralAnimals => _attackNeutralAnimals != null && _attackNeutralAnimals.Value;

        /// <summary>
        /// Se true (padrão), o companheiro ergue o escudo quando um inimigo próximo ataca (#6). Só
        /// tem efeito prático se ele estiver com um escudo equipado.
        /// </summary>
        internal static bool BlockAttacks => _blockAttacks == null || _blockAttacks.Value;

        /// <summary>Raio (m) em que procuramos um inimigo atacando para bloquear (#6).</summary>
        internal static float BlockRadius => _blockRadius != null ? Mathf.Clamp(_blockRadius.Value, 2f, 20f) : 8f;

        /// <summary>Tempo (s) que o bloqueio é mantido depois de um golpe inimigo (#6).</summary>
        internal static float BlockHold => _blockHold != null ? Mathf.Clamp(_blockHold.Value, 0.05f, 1f) : 0.2f;

        /// <summary>Intervalo (s) entre varreduras de alvo — nunca por frame.</summary>
        internal static float ScanInterval
        {
            get
            {
                float value = _scanInterval != null ? _scanInterval.Value : 0.5f;
                return Mathf.Clamp(value, 0.1f, 2f);
            }
        }

        internal static void Configure(
            ConfigEntry<CompanionMode> mode,
            ConfigEntry<KeyCode> modeKey,
            ConfigEntry<float> aggressiveRadius,
            ConfigEntry<float> defensiveRadius,
            ConfigEntry<float> leashDistance,
            ConfigEntry<bool> attackNeutralAnimals,
            ConfigEntry<float> scanInterval,
            ConfigEntry<bool> blockAttacks,
            ConfigEntry<float> blockRadius,
            ConfigEntry<float> blockHold)
        {
            _mode = mode;
            _modeKey = modeKey;
            _aggressiveRadius = aggressiveRadius;
            _defensiveRadius = defensiveRadius;
            _leashDistance = leashDistance;
            _attackNeutralAnimals = attackNeutralAnimals;
            _scanInterval = scanInterval;
            _blockAttacks = blockAttacks;
            _blockRadius = blockRadius;
            _blockHold = blockHold;

            Plugin.Log.LogInfo(
                $"[ModIA] Combate: modo = {Mode} | tecla = {ModeKey} | " +
                $"raio agressivo = {AggressiveRadius} m | raio defensivo = {DefensiveRadius} m | " +
                $"coleira = {LeashDistance} m | animais neutros = {AttackNeutralAnimals} | " +
                $"varredura = {ScanInterval} s | bloquear = {BlockAttacks} " +
                $"(raio {BlockRadius} m, tempo {BlockHold} s).");
        }

        /// <summary>Solta o estado estático (unload do plugin / fim do mundo).</summary>
        internal static void Shutdown()
        {
            _recentAttacker = null;
            _recentAttackerTime = -9999f;
        }

        /// <summary>Tecla de ciclar o modo (chamada pelo <see cref="Plugin"/> a cada frame).</summary>
        internal static void Tick()
        {
            if (_modeKey == null || CompanionManager.InputBlocked())
            {
                return;
            }

            if (ZInput.GetKeyDown(_modeKey.Value, false))
            {
                CycleMode($"tecla {_modeKey.Value}");
            }
        }

        /// <summary>Cicla Passive → Defensive → Aggressive.</summary>
        internal static void CycleMode(string source)
        {
            SetMode(Mode.Next(), source);
        }

        /// <summary>
        /// Aplica um modo novo: grava a config (persiste no <c>.cfg</c>), limpa a memória de
        /// agressão, avisa a instância ativa e dá feedback no HUD/log.
        /// </summary>
        internal static void SetMode(CompanionMode mode, string source)
        {
            CompanionMode previous = Mode;

            if (_mode != null)
            {
                _mode.Value = mode;
            }

            _recentAttacker = null;
            _recentAttackerTime = -9999f;

            Plugin.Log.LogInfo(
                $"[ModIA] Modo do companheiro: {previous} -> {mode} ({source}).");

            CompanionAI companion = CompanionManager.ActiveCompanion;
            if (companion != null)
            {
                companion.ApplyMode(mode);
            }
            else
            {
                Plugin.Log.LogInfo(
                    "[ModIA] Nenhum companheiro no mundo; o modo vale para o próximo spawn.");
            }

            ShowMessage($"Companheiro: modo {mode.ToDisplayName()}");
        }

        /// <summary>
        /// Registra quem agrediu o companheiro/jogador (ou quem o jogador atacou). Vale por
        /// <see cref="AttackerMemorySeconds"/> e é ignorado no modo Passive.
        /// </summary>
        internal static void NotifyAttacker(Character attacker)
        {
            if (attacker == null || Mode == CompanionMode.Passive)
            {
                return;
            }

            _recentAttacker = attacker;
            _recentAttackerTime = Time.time;
        }

        /// <summary>Atacante recente ainda "fresco" (null se venceu ou foi destruído).</summary>
        internal static Character GetRecentAttacker()
        {
            if (_recentAttacker == null)
            {
                return null;
            }

            if (Time.time - _recentAttackerTime > AttackerMemorySeconds)
            {
                _recentAttacker = null;
                return null;
            }

            return _recentAttacker;
        }

        private static void ShowMessage(string text)
        {
            MessageHud hud = MessageHud.instance;
            if (hud != null)
            {
                hud.ShowMessage(MessageHud.MessageType.TopLeft, text);
            }
        }
    }
}
