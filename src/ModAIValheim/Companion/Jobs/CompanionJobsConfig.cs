using BepInEx.Configuration;
using UnityEngine;

namespace ModAIValheim.Companion.Jobs
{
    /// <summary>
    /// Configuração da seção <c>[Jobs]</c> do <c>.cfg</c> (Fase 2.16 — trabalhos).
    ///
    /// <para>
    /// Como o <c>CompanionCombat</c>/<c>CompanionMovement</c>, este tipo é <b>estático e sem estado
    /// de cena</b>: só guarda os <see cref="ConfigEntry"/> e expõe os valores já saneados
    /// (<c>Clamp</c>) para os controladores de trabalho. Quem lê é o <see cref="Plugin"/>, que
    /// registra tudo em <see cref="Configure"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Ligado</b> (padrão <c>true</c>) é a chave geral: desligada, <c>modia_job</c> recusa o
    /// pedido. Ele <b>não</b> faz o companheiro trabalhar sozinho — nenhum trabalho começa sem um
    /// comando explícito do jogador ("desligado até o jogador mandar"): a chave existe para quem
    /// quer o companheiro estritamente no follow/coleta.
    /// </para>
    /// </summary>
    internal static class CompanionJobsConfig
    {
        /// <summary>Prefab vanilla da vara de pesca.</summary>
        internal const string DefaultRodPrefab = "FishingRod";

        /// <summary>Prefab vanilla da isca de pesca.</summary>
        internal const string DefaultBaitPrefab = "FishingBait";

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<int> _defaultAmount;
        private static ConfigEntry<float> _maxSeconds;
        private static ConfigEntry<float> _scanInterval;
        private static ConfigEntry<float> _huntRadius;
        private static ConfigEntry<float> _fishRadius;
        private static ConfigEntry<float> _castRange;
        private static ConfigEntry<float> _castWait;
        private static ConfigEntry<float> _minCastStamina;
        private static ConfigEntry<bool> _provideRod;
        private static ConfigEntry<string> _rodPrefab;
        private static ConfigEntry<bool> _provideBait;
        private static ConfigEntry<string> _baitPrefab;
        private static ConfigEntry<int> _baitAmount;

        /// <summary>Trabalhos ligados (config <c>Jobs/Ligado</c>). Nunca começam sozinhos.</summary>
        internal static bool Enabled => _enabled == null || _enabled.Value;

        /// <summary>Quantidade padrão do <c>modia_job</c> sem número (0 = sem limite).</summary>
        internal static int DefaultAmount => _defaultAmount != null ? Mathf.Max(0, _defaultAmount.Value) : 0;

        /// <summary>
        /// Teto de tempo (s) de um trabalho — a rede de segurança contra um trabalho que trava
        /// (0 = sem limite).
        /// </summary>
        internal static float MaxSeconds => _maxSeconds != null ? Mathf.Max(0f, _maxSeconds.Value) : 1200f;

        /// <summary>Intervalo (s) entre varreduras de alvo dos trabalhos (nunca por frame).</summary>
        internal static float ScanInterval =>
            _scanInterval != null ? Mathf.Clamp(_scanInterval.Value, 0.5f, 10f) : 2f;

        /// <summary>Raio (m) em que o caçador procura fauna (a partir do companheiro).</summary>
        internal static float HuntRadius => _huntRadius != null ? Mathf.Clamp(_huntRadius.Value, 5f, 150f) : 40f;

        /// <summary>Raio (m) em que o pescador procura peixes (a partir do companheiro).</summary>
        internal static float FishRadius => _fishRadius != null ? Mathf.Clamp(_fishRadius.Value, 5f, 150f) : 30f;

        /// <summary>Distância (m) máxima de um arremesso (o jogo arrebenta a linha além disso).</summary>
        internal static float CastRange => _castRange != null ? Mathf.Clamp(_castRange.Value, 6f, 28f) : 20f;

        /// <summary>Espera (s) máxima por uma fisgada antes de recolher e tentar de novo.</summary>
        internal static float CastWait => _castWait != null ? Mathf.Clamp(_castWait.Value, 5f, 300f) : 60f;

        /// <summary>
        /// Fração (0..1) da stamina que ele exige para arremessar: puxar um peixe cansa, então ele
        /// descansa antes (como no <c>FishController</c> da referência).
        /// </summary>
        internal static float MinCastStamina =>
            _minCastStamina != null ? Mathf.Clamp(_minCastStamina.Value, 0f, 1f) : 0.6f;

        /// <summary>Se true, o mod cria uma vara quando o companheiro não tem nenhuma.</summary>
        internal static bool ProvideRod => _provideRod == null || _provideRod.Value;

        /// <summary>Prefab da vara fornecida (config <c>Jobs/VaraPrefab</c>).</summary>
        internal static string RodPrefab =>
            _rodPrefab != null && !string.IsNullOrEmpty(_rodPrefab.Value)
                ? _rodPrefab.Value.Trim()
                : DefaultRodPrefab;

        /// <summary>Se true, o mod cria iscas quando o companheiro não tem nenhuma.</summary>
        internal static bool ProvideBait => _provideBait == null || _provideBait.Value;

        /// <summary>Prefab da isca fornecida (config <c>Jobs/IscaPrefab</c>).</summary>
        internal static string BaitPrefab =>
            _baitPrefab != null && !string.IsNullOrEmpty(_baitPrefab.Value)
                ? _baitPrefab.Value.Trim()
                : DefaultBaitPrefab;

        /// <summary>Quantas iscas o mod cria de uma vez (config <c>Jobs/IscaQuantidade</c>).</summary>
        internal static int BaitAmount => _baitAmount != null ? Mathf.Clamp(_baitAmount.Value, 1, 200) : 20;

        internal static void Configure(
            ConfigEntry<bool> enabled,
            ConfigEntry<int> defaultAmount,
            ConfigEntry<float> maxSeconds,
            ConfigEntry<float> scanInterval,
            ConfigEntry<float> huntRadius,
            ConfigEntry<float> fishRadius,
            ConfigEntry<float> castRange,
            ConfigEntry<float> castWait,
            ConfigEntry<float> minCastStamina,
            ConfigEntry<bool> provideRod,
            ConfigEntry<string> rodPrefab,
            ConfigEntry<bool> provideBait,
            ConfigEntry<string> baitPrefab,
            ConfigEntry<int> baitAmount)
        {
            _enabled = enabled;
            _defaultAmount = defaultAmount;
            _maxSeconds = maxSeconds;
            _scanInterval = scanInterval;
            _huntRadius = huntRadius;
            _fishRadius = fishRadius;
            _castRange = castRange;
            _castWait = castWait;
            _minCastStamina = minCastStamina;
            _provideRod = provideRod;
            _rodPrefab = rodPrefab;
            _provideBait = provideBait;
            _baitPrefab = baitPrefab;
            _baitAmount = baitAmount;

            Plugin.Log.LogInfo(
                $"[ModIA] Trabalhos (jobs): ligados = {Enabled} | quantidade padrao = " +
                $"{(DefaultAmount > 0 ? DefaultAmount.ToString() : "sem limite")} | " +
                $"tempo maximo = {(MaxSeconds > 0f ? $"{MaxSeconds / 60f:0.#} min" : "sem limite")} | " +
                $"varredura = {ScanInterval} s | raio cacar = {HuntRadius} m | raio pescar = {FishRadius} m | " +
                $"arremesso = {CastRange} m (espera {CastWait} s, stamina minima {MinCastStamina * 100f:0}%) | " +
                $"vara = {ProvideRod} ('{RodPrefab}') | isca = {ProvideBait} ('{BaitPrefab}' x{BaitAmount}).");
        }
    }
}
