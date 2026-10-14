using BepInEx.Configuration;
using UnityEngine;

namespace ModAIValheim.Companion.Jobs
{
    /// <summary>
    /// Configuração da seção <c>[Jobs]</c> do <c>.cfg</c> (Fase 2.16 — trabalhos: caçar, pescar,
    /// cultivar, cozinhar e fundir).
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
    ///
    /// <para>
    /// A <b>zona de fazenda</b> (<c>Jobs/FarmZona</c>) é o único valor que o mod <b>escreve</b>: o
    /// comando <c>modia_farm set</c> grava ali (e salva o arquivo), então a zona sobrevive a
    /// reinícios sem precisar de um arquivo de estado próprio.
    /// </para>
    /// </summary>
    internal static class CompanionJobsConfig
    {
        /// <summary>Prefab vanilla da vara de pesca.</summary>
        internal const string DefaultRodPrefab = "FishingRod";

        /// <summary>Prefab vanilla da isca de pesca.</summary>
        internal const string DefaultBaitPrefab = "FishingBait";

        // ------------------------------------------------------------------
        // Gerais
        // ------------------------------------------------------------------

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<int> _defaultAmount;
        private static ConfigEntry<float> _maxSeconds;
        private static ConfigEntry<float> _scanInterval;

        // ------------------------------------------------------------------
        // Caçar / pescar (Fase 2.16a)
        // ------------------------------------------------------------------

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

        // ------------------------------------------------------------------
        // Cultivar (Fase 2.16b)
        // ------------------------------------------------------------------

        private static ConfigEntry<string> _farmZone;
        private static ConfigEntry<float> _farmRadius;
        private static ConfigEntry<bool> _farmPlant;
        private static ConfigEntry<bool> _farmHarvest;
        private static ConfigEntry<float> _farmSpacing;
        private static ConfigEntry<int> _farmStoreEvery;
        private static ConfigEntry<int> _farmKeepSeeds;

        // ------------------------------------------------------------------
        // Cozinhar / bebidas (Fase 2.16b)
        // ------------------------------------------------------------------

        private static ConfigEntry<float> _cookRadius;
        private static ConfigEntry<bool> _cookFuel;
        private static ConfigEntry<float> _cookFuelLevel;
        private static ConfigEntry<bool> _cookTakeDone;
        private static ConfigEntry<bool> _cookCauldron;
        private static ConfigEntry<float> _cookWaitSeconds;

        // ------------------------------------------------------------------
        // Fundições (Fase 2.16b)
        // ------------------------------------------------------------------

        private static ConfigEntry<float> _smeltRadius;
        private static ConfigEntry<bool> _smeltFuel;
        private static ConfigEntry<bool> _smeltTakeOutput;
        private static ConfigEntry<int> _smeltMaxInsert;

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

        /// <summary>
        /// A zona de fazenda gravada na config ("" = nenhuma). O <c>modia_farm set</c> é quem a
        /// escreve; o <see cref="FarmJob"/> só lê.
        /// </summary>
        internal static FarmZone FarmZone
        {
            get
            {
                // Cache: a propriedade é lida a cada tick do trabalho e o parse faz Split (alocação).
                // O texto da config só muda quando o comando modia_farm set escreve nele.
                string raw = _farmZone != null ? _farmZone.Value : null;
                if (!string.Equals(raw, s_zoneRaw, System.StringComparison.Ordinal))
                {
                    s_zoneRaw = raw;
                    s_zoneParsed = ModAIValheim.Companion.Jobs.FarmZone.TryParse(raw, out FarmZone zone)
                        ? zone
                        : default;
                }

                return s_zoneParsed;
            }
        }

        private static string s_zoneRaw;
        private static FarmZone s_zoneParsed;

        /// <summary>Grava a zona de fazenda na config e salva o arquivo (comando <c>modia_farm set</c>).</summary>
        internal static void SetFarmZone(FarmZone zone)
        {
            if (_farmZone == null)
            {
                return;
            }

            _farmZone.Value = zone.IsValid ? zone.Serialize() : string.Empty;
            _farmZone.ConfigFile.Save();
        }

        /// <summary>Raio (m) padrão da zona quando o jogador não diz um (<c>modia_farm set</c>).</summary>
        internal static float FarmRadius => _farmRadius != null ? Mathf.Clamp(_farmRadius.Value, 2f, 60f) : 8f;

        /// <summary>O trabalho de cultivar <b>planta</b> nas vagas da zona?</summary>
        internal static bool FarmPlant => _farmPlant == null || _farmPlant.Value;

        /// <summary>O trabalho de cultivar <b>colhe</b> as plantas prontas da zona?</summary>
        internal static bool FarmHarvest => _farmHarvest == null || _farmHarvest.Value;

        /// <summary>Espaçamento (m) mínimo entre plantas (nunca menor que o raio de crescimento).</summary>
        internal static float FarmSpacing => _farmSpacing != null ? Mathf.Clamp(_farmSpacing.Value, 0.5f, 10f) : 2f;

        /// <summary>
        /// A cada quantos itens colhidos ele leva a colheita ao baú (0 = só quando a carga encher).
        /// É o "guardar o resultado" reaproveitando o depósito da Fase 2.14.
        /// </summary>
        internal static int FarmStoreEvery =>
            _farmStoreEvery != null ? Mathf.Clamp(_farmStoreEvery.Value, 0, 500) : 20;

        /// <summary>
        /// Quantas unidades de <b>cada semente</b> (e de cada vegetal que serve para produzir
        /// semente) ele mantém no inventário ao levar a colheita ao baú — o resto é guardado. É o
        /// "Keep" da referência: sem isso, a primeira viagem ao baú levaria justamente o que ele
        /// precisa para replantar.
        /// </summary>
        internal static int FarmKeepSeeds =>
            _farmKeepSeeds != null ? Mathf.Clamp(_farmKeepSeeds.Value, 0, 200) : 20;

        /// <summary>Raio (m) em que o cozinheiro procura estações (fogueira, espeto, barril, caldeirão).</summary>
        internal static float CookRadius => _cookRadius != null ? Mathf.Clamp(_cookRadius.Value, 3f, 100f) : 20f;

        /// <summary>O cozinheiro reabastece o combustível das estações (fogueira/espeto/forno)?</summary>
        internal static bool CookFuel => _cookFuel == null || _cookFuel.Value;

        /// <summary>Fração (0..1) do combustível máximo que ele mantém nas estações.</summary>
        internal static float CookFuelLevel =>
            _cookFuelLevel != null ? Mathf.Clamp(_cookFuelLevel.Value, 0.1f, 1f) : 0.5f;

        /// <summary>O cozinheiro retira o prato/bebida pronto (antes de queimar)?</summary>
        internal static bool CookTakeDone => _cookTakeDone == null || _cookTakeDone.Value;

        /// <summary>Cozinhar receitas no caldeirão/mesa de preparo (o "craft" da estação de fogo)?</summary>
        internal static bool CookCauldron => _cookCauldron == null || _cookCauldron.Value;

        /// <summary>Espera (s) máxima pelo preparo antes de desistir da estação e encerrar o trabalho.</summary>
        internal static float CookWaitSeconds =>
            _cookWaitSeconds != null ? Mathf.Clamp(_cookWaitSeconds.Value, 10f, 1800f) : 240f;

        /// <summary>Raio (m) em que o fundidor procura fornos/fornalhas/carvoeiras.</summary>
        internal static float SmeltRadius => _smeltRadius != null ? Mathf.Clamp(_smeltRadius.Value, 3f, 100f) : 20f;

        /// <summary>O fundidor reabastece o combustível dos fornos?</summary>
        internal static bool SmeltFuel => _smeltFuel == null || _smeltFuel.Value;

        /// <summary>O fundidor retira o produto (barra/carvão) que a estação larga?</summary>
        internal static bool SmeltTakeOutput => _smeltTakeOutput == null || _smeltTakeOutput.Value;

        /// <summary>Quantas unidades ele põe (minério/combustível) por viagem até a estação.</summary>
        internal static int SmeltMaxInsert => _smeltMaxInsert != null ? Mathf.Clamp(_smeltMaxInsert.Value, 1, 200) : 30;

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
            ConfigEntry<int> baitAmount,
            ConfigEntry<string> farmZone,
            ConfigEntry<float> farmRadius,
            ConfigEntry<bool> farmPlant,
            ConfigEntry<bool> farmHarvest,
            ConfigEntry<float> farmSpacing,
            ConfigEntry<int> farmStoreEvery,
            ConfigEntry<int> farmKeepSeeds,
            ConfigEntry<float> cookRadius,
            ConfigEntry<bool> cookFuel,
            ConfigEntry<float> cookFuelLevel,
            ConfigEntry<bool> cookTakeDone,
            ConfigEntry<bool> cookCauldron,
            ConfigEntry<float> cookWaitSeconds,
            ConfigEntry<float> smeltRadius,
            ConfigEntry<bool> smeltFuel,
            ConfigEntry<bool> smeltTakeOutput,
            ConfigEntry<int> smeltMaxInsert)
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
            _farmZone = farmZone;
            _farmRadius = farmRadius;
            _farmPlant = farmPlant;
            _farmHarvest = farmHarvest;
            _farmSpacing = farmSpacing;
            _farmStoreEvery = farmStoreEvery;
            _farmKeepSeeds = farmKeepSeeds;
            _cookRadius = cookRadius;
            _cookFuel = cookFuel;
            _cookFuelLevel = cookFuelLevel;
            _cookTakeDone = cookTakeDone;
            _cookCauldron = cookCauldron;
            _cookWaitSeconds = cookWaitSeconds;
            _smeltRadius = smeltRadius;
            _smeltFuel = smeltFuel;
            _smeltTakeOutput = smeltTakeOutput;
            _smeltMaxInsert = smeltMaxInsert;

            Plugin.Log.LogInfo(
                $"[ModIA] Trabalhos (jobs): ligados = {Enabled} | quantidade padrao = " +
                $"{(DefaultAmount > 0 ? DefaultAmount.ToString() : "sem limite")} | " +
                $"tempo maximo = {(MaxSeconds > 0f ? $"{MaxSeconds / 60f:0.#} min" : "sem limite")} | " +
                $"varredura = {ScanInterval} s | raio cacar = {HuntRadius} m | raio pescar = {FishRadius} m | " +
                $"arremesso = {CastRange} m (espera {CastWait} s, stamina minima {MinCastStamina * 100f:0}%) | " +
                $"vara = {ProvideRod} ('{RodPrefab}') | isca = {ProvideBait} ('{BaitPrefab}' x{BaitAmount}).");
            Plugin.Log.LogInfo(
                $"[ModIA] Trabalhos (2.16b): fazenda = {FarmZone.Describe()} (raio padrao {FarmRadius} m, " +
                $"plantar = {FarmPlant}, colher = {FarmHarvest}, espacamento = {FarmSpacing} m, " +
                $"depositar a cada {FarmStoreEvery}, manter {FarmKeepSeeds} semente(s)) | cozinha = raio {CookRadius} m " +
                $"(combustivel = {CookFuel} ate {CookFuelLevel * 100f:0}%, retirar pronto = {CookTakeDone}, " +
                $"caldeirao = {CookCauldron}, espera {CookWaitSeconds} s) | fundicao = raio {SmeltRadius} m " +
                $"(combustivel = {SmeltFuel}, retirar produto = {SmeltTakeOutput}, " +
                $"por viagem = {SmeltMaxInsert}).");
        }
    }
}
