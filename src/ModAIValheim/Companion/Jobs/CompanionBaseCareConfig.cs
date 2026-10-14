using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace ModAIValheim.Companion.Jobs
{
    /// <summary>
    /// Configuração da seção <c>[Base]</c> do <c>.cfg</c> (Fase 2.17 — cuidado da base: reparar
    /// construções, reabastecer fogueiras, cuidar de colmeias e ordenar baús).
    ///
    /// <para>
    /// Como o <see cref="CompanionJobsConfig"/>, este tipo é <b>estático e sem estado de cena</b>:
    /// só guarda os <see cref="ConfigEntry"/> e expõe os valores já saneados (<c>Clamp</c>) para o
    /// controlador de trabalho. Quem lê é o <see cref="Plugin"/>, que registra tudo em
    /// <see cref="Configure"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Ligado</b> é a chave geral do cuidado da base: desligada, o trabalho <c>upkeep</c> recusa
    /// o pedido. Ele <b>não</b> faz o companheiro trabalhar sozinho — como os demais trabalhos, nada
    /// começa sem um comando explícito (<c>modia_job upkeep</c>) ou sem o <b>modo contínuo</b>
    /// (<c>Continuo</c>, ligado por <c>modia_basecare on</c>).
    /// </para>
    ///
    /// <para>
    /// O cuidado da base é um <b>trabalho</b>: ele também respeita <c>Jobs/Ligado</c> (a chave geral
    /// dos trabalhos) e a fila única (<see cref="CompanionJobQueue"/>).
    /// </para>
    ///
    /// <para>
    /// Adaptado (lógica) das chaves de <c>src/Companions/ModConfig.cs</c> do <i>Offline Companions
    /// Reborn</i> (MIT — ver <c>THIRD_PARTY_NOTICES.md</c>): <c>UpkeepRepair</c>,
    /// <c>UpkeepRepairBelow</c>, <c>UpkeepRestock</c>, <c>UpkeepFuelBelow</c>, <c>UpkeepHoney</c>,
    /// <c>UpkeepHoneyAt</c>, <c>UpkeepSort</c>, <c>UpkeepSortItems</c>, <c>UpkeepAwayRadius</c>,
    /// <c>UpkeepScanInterval</c> — aqui em PT-BR e com os nomes do nosso mod.
    /// </para>
    /// </summary>
    internal static class CompanionBaseCareConfig
    {
        /// <summary>Prefab vanilla do martelo de construção (o que o jogo exige para reparar).</summary>
        internal const string DefaultHammerPrefab = "Hammer";

        // ------------------------------------------------------------------
        // Gerais
        // ------------------------------------------------------------------

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<bool> _continuous;
        private static ConfigEntry<float> _workRadius;
        private static ConfigEntry<float> _scanInterval;

        // ------------------------------------------------------------------
        // Reparar construções
        // ------------------------------------------------------------------

        private static ConfigEntry<bool> _repair;
        private static ConfigEntry<float> _repairBelow;
        private static ConfigEntry<bool> _provideHammer;
        private static ConfigEntry<string> _hammerPrefab;

        // ------------------------------------------------------------------
        // Abastecer fogueiras
        // ------------------------------------------------------------------

        private static ConfigEntry<bool> _refuel;
        private static ConfigEntry<float> _fuelLevel;

        // ------------------------------------------------------------------
        // Colmeias
        // ------------------------------------------------------------------

        private static ConfigEntry<bool> _hives;
        private static ConfigEntry<float> _honeyMin;

        // ------------------------------------------------------------------
        // Ordenar baús
        // ------------------------------------------------------------------

        private static ConfigEntry<bool> _sortChests;
        private static ConfigEntry<string> _sortItems;
        private static ConfigEntry<int> _sortPerTrip;
        private static ConfigEntry<bool> _mergeStacks;

        /// <summary>Cuidado da base ligado (config <c>Base/Ligado</c>). Nunca começa sozinho.</summary>
        internal static bool Enabled => _enabled == null || _enabled.Value;

        /// <summary>Modo contínuo (config <c>Base/Continuo</c>): ele mantém o cuidado da base sozinho.</summary>
        internal static bool Continuous => _continuous != null && _continuous.Value;

        /// <summary>
        /// Raio (m) de trabalho quando <b>não há casa definida</b>: a base passa a ser um círculo em
        /// volta de onde o trabalho foi começado. Com casa definida vale o raio dela.
        /// </summary>
        internal static float WorkRadius =>
            _workRadius != null ? Mathf.Clamp(_workRadius.Value, 5f, 120f) : 30f;

        /// <summary>Intervalo (s) entre varreduras da base (nunca por frame).</summary>
        internal static float ScanInterval =>
            _scanInterval != null ? Mathf.Clamp(_scanInterval.Value, 1f, 30f) : 3f;

        /// <summary>Reparar construções danificadas (<c>Base/Reparar</c>).</summary>
        internal static bool Repair => _repair == null || _repair.Value;

        /// <summary>
        /// Fração (0..1) da vida da peça abaixo da qual ele repara. <c>1</c> = repara qualquer
        /// arranhão; o padrão <c>0.9</c> deixa passar o desgaste leve de chuva.
        /// </summary>
        internal static float RepairBelow =>
            _repairBelow != null ? Mathf.Clamp(_repairBelow.Value, 0.1f, 1f) : 0.9f;

        /// <summary>Criar um martelo quando o companheiro não tem nenhum (config <c>Base/FornecerMartelo</c>).</summary>
        internal static bool ProvideHammer => _provideHammer == null || _provideHammer.Value;

        /// <summary>Prefab do martelo usado/fomecido (config <c>Base/MarteloPrefab</c>).</summary>
        internal static string HammerPrefab =>
            _hammerPrefab != null && !string.IsNullOrEmpty(_hammerPrefab.Value)
                ? _hammerPrefab.Value.Trim()
                : DefaultHammerPrefab;

        /// <summary>Reabastecer fogueiras/tochas/braseiros com o combustível do inventário.</summary>
        internal static bool Refuel => _refuel == null || _refuel.Value;

        /// <summary>
        /// Fração (0..1) do combustível máximo que ele mantém nas fogueiras. Não precisa encher até
        /// o teto: o padrão <c>0.5</c> é o suficiente para a fogueira não apagar.
        /// </summary>
        internal static float FuelLevel =>
            _fuelLevel != null ? Mathf.Clamp(_fuelLevel.Value, 0.1f, 1f) : 0.5f;

        /// <summary>Esvaziar colmeias com mel (<c>Base/Colmeias</c>).</summary>
        internal static bool Hives => _hives == null || _hives.Value;

        /// <summary>Fração (0..1) do mel máximo a partir da qual ele esvazia a colmeia.</summary>
        internal static float HoneyMin =>
            _honeyMin != null ? Mathf.Clamp(_honeyMin.Value, 0.1f, 1f) : 0.5f;

        /// <summary>Mover itens repetidos para o baú que já tem mais deles (<c>Base/OrdenarBaus</c>).</summary>
        internal static bool SortChests => _sortChests == null || _sortChests.Value;

        /// <summary>
        /// Itens que ele <b>pode</b> mover entre baús (config <c>Base/OrdenarItens</c>):
        /// <c>*</c> = todos os empilháveis (o padrão), ou uma lista de prefabs separada por vírgula.
        /// Itens de equipamento/ferramenta nunca entram (pilha máxima 1) — só se estiverem na lista.
        /// </summary>
        internal static string SortItems =>
            _sortItems != null && !string.IsNullOrEmpty(_sortItems.Value) ? _sortItems.Value : "*";

        /// <summary>Quantas unidades ele leva por viagem ao arrumar os baús (config <c>Base/OrdenarPorViagem</c>).</summary>
        internal static int SortPerTrip =>
            _sortPerTrip != null ? Mathf.Clamp(_sortPerTrip.Value, 1, 500) : 50;

        /// <summary>Juntar pilhas parciais dentro do mesmo baú (<c>Base/JuntarPilhas</c>).</summary>
        internal static bool MergeStacks => _mergeStacks == null || _mergeStacks.Value;

        /// <summary>Existe alguma tarefa de cuidado ligada? (sem nenhuma, o trabalho recusa começar).</summary>
        internal static bool AnyTask => Repair || Refuel || Hives || SortChests || MergeStacks;

        /// <summary>Texto de <c>OrdenarItens</c> de onde a lista em cache foi lida (evita Split por item).</summary>
        private static string s_itemsRaw;
        private static HashSet<string> s_itemsParsed;

        /// <summary>
        /// O item <paramref name="prefabName"/> pode ser movido entre baús?
        /// <c>*</c> (o padrão) libera todos os itens empilháveis; qualquer outra coisa é uma lista de
        /// prefabs separada por vírgula. O parse é cacheado pelo texto da config — a varredura de
        /// baús chama isto para cada item, então nada de <c>Split</c> por item.
        /// </summary>
        internal static bool AllowsSort(string prefabName)
        {
            string raw = SortItems;
            if (!string.Equals(raw, s_itemsRaw, StringComparison.Ordinal))
            {
                s_itemsRaw = raw;
                s_itemsParsed = null;

                if (raw != "*" && raw.Length > 0)
                {
                    s_itemsParsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    string[] names = raw.Split(',');
                    for (int i = 0; i < names.Length; i++)
                    {
                        string name = names[i].Trim();
                        if (name.Length > 0)
                        {
                            s_itemsParsed.Add(name);
                        }
                    }
                }
            }

            if (s_itemsParsed == null)
            {
                return raw == "*" || raw.Length == 0;
            }

            return !string.IsNullOrEmpty(prefabName) && s_itemsParsed.Contains(prefabName);
        }

        internal static void Configure(
            ConfigEntry<bool> enabled,
            ConfigEntry<bool> continuous,
            ConfigEntry<float> workRadius,
            ConfigEntry<float> scanInterval,
            ConfigEntry<bool> repair,
            ConfigEntry<float> repairBelow,
            ConfigEntry<bool> provideHammer,
            ConfigEntry<string> hammerPrefab,
            ConfigEntry<bool> refuel,
            ConfigEntry<float> fuelLevel,
            ConfigEntry<bool> hives,
            ConfigEntry<float> honeyMin,
            ConfigEntry<bool> sortChests,
            ConfigEntry<string> sortItems,
            ConfigEntry<int> sortPerTrip,
            ConfigEntry<bool> mergeStacks)
        {
            _enabled = enabled;
            _continuous = continuous;
            _workRadius = workRadius;
            _scanInterval = scanInterval;
            _repair = repair;
            _repairBelow = repairBelow;
            _provideHammer = provideHammer;
            _hammerPrefab = hammerPrefab;
            _refuel = refuel;
            _fuelLevel = fuelLevel;
            _hives = hives;
            _honeyMin = honeyMin;
            _sortChests = sortChests;
            _sortItems = sortItems;
            _sortPerTrip = sortPerTrip;
            _mergeStacks = mergeStacks;

            Plugin.Log.LogInfo(
                $"[ModIA] Base (cuidado): ligado = {Enabled} | continuo = {Continuous} | " +
                $"raio = {WorkRadius:0} m (sem casa) | varredura = {ScanInterval} s.");
            Plugin.Log.LogInfo(
                $"[ModIA] Base (tarefas): reparar = {Repair} abaixo de {RepairBelow * 100f:0}% " +
                $"(fornecer martelo = {ProvideHammer}, '{HammerPrefab}') | abastecer fogueiras = " +
                $"{Refuel} ate {FuelLevel * 100f:0}% | colmeias = {Hives} a partir de " +
                $"{HoneyMin * 100f:0}% do mel | ordenar baus = {SortChests} ('{SortItems}', " +
                $"{SortPerTrip} por viagem, juntar pilhas = {MergeStacks}).");
        }
    }
}
