using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// <b>Tool keeper</b> do companheiro (Fase 2.14 — item #11 da
    /// <c>docs/audit-vs-offline-companions.md</c>): manter a ferramenta certa disponível.
    ///
    /// <para>
    /// Espelha a <i>lógica</i> de <c>MHR_ToolKeeper.FindBestInChests</c>/<c>BestIn</c>/<c>Score</c>,
    /// <c>MHR_Chests.Near</c>/<c>Usable</c> e do ciclo de empréstimo/devolução do
    /// <i>Offline Companions Reborn</i> (MIT — ver <c>THIRD_PARTY_NOTICES.md</c>), sem copiar código.
    /// </para>
    ///
    /// <list type="number">
    ///   <item><description><b>Emprestar:</b> quando falta uma ferramenta capaz (e criar — config
    ///   <c>Coleta/FornecerFerramentas</c> — não resolveu), procura num baú próximo a <b>melhor</b>
    ///   ferramenta que atenda ao nó (<c>m_toolTier</c> ≥ mínimo do nó e dano &gt; 0 contra as
    ///   resistências dele) e vai buscá-la. A escolha é a mesma régua do
    ///   <c>Score</c> da referência: tier, qualidade, durabilidade e dano.</description></item>
    ///   <item><description><b>Devolver:</b> a ferramenta emprestada volta para o baú de origem (ou o
    ///   mais próximo com espaço) quando o companheiro não precisa mais dela — config
    ///   <c>Coleta/GuardarFerramentaNaoUsada</c>. Ferramenta <b>criada</b> pelo mod nunca é devolvida
    ///   (senão o mod a criaria de novo, e de novo).</description></item>
    /// </list>
    ///
    /// <para>
    /// O empréstimo só acontece quando a criação <b>não</b> resolveu: o comportamento antigo (criar a
    /// ferramenta do nada) continua idêntico, e o baú entra como plano B — nunca como substituição.
    /// </para>
    ///
    /// <para>
    /// <b>Performance:</b> a varredura de baús é a mesma do <see cref="CompanionDeposit"/>
    /// (<c>Piece.GetAllPiecesInRadius</c>, com espera mínima entre tentativas e buffers
    /// reaproveitados); nada roda por frame.
    /// </para>
    /// </summary>
    internal static class CompanionToolKeeper
    {
        /// <summary>Espera (s) mínima entre varreduras de baú por ferramenta.</summary>
        private const float SearchCooldown = 3f;

        /// <summary>Buffer reaproveitado da varredura de baús.</summary>
        private static readonly List<Piece> s_pieces = new List<Piece>(128);

        /// <summary>Instance IDs dos baús já avaliados na varredura atual.</summary>
        private static readonly HashSet<int> s_seen = new HashSet<int>();

        /// <summary>Buffer reaproveitado dos nomes das ferramentas emprestadas.</summary>
        private static readonly List<string> s_prefabBuffer = new List<string>(4);

        /// <summary>Uma ferramenta emprestada de um baú (prefab + onde estava).</summary>
        private sealed class BorrowedTool
        {
            internal string Prefab;
            internal Container Chest;
        }

        private static readonly List<BorrowedTool> s_borrowed = new List<BorrowedTool>(4);

        private static ConfigEntry<bool> _storeUnused;
        private static float _nextSearchAt = -999f;

        /// <summary>Devolver a ferramenta emprestada quando não precisar mais (config).</summary>
        internal static bool StoreUnused => _storeUnused != null && _storeUnused.Value;

        /// <summary>Existe ferramenta emprestada de baú com o companheiro?</summary>
        internal static bool HasBorrowed => s_borrowed.Count > 0;

        /// <summary>Quantas ferramentas emprestadas estão com o companheiro.</summary>
        internal static int BorrowedCount => s_borrowed.Count;

        internal static void Configure(ConfigEntry<bool> storeUnused)
        {
            _storeUnused = storeUnused;

            Plugin.Log.LogInfo(
                $"[ModIA] Tool keeper: guardar ferramenta não usada = {StoreUnused}.");
        }

        /// <summary>Solta o estado estático (unload do plugin, dismiss, morte do companheiro).</summary>
        internal static void Shutdown()
        {
            s_borrowed.Clear();
            s_pieces.Clear();
            s_seen.Clear();
            _nextSearchAt = -999f;
        }

        /// <summary>
        /// Esquece o empréstimo sem devolver a ferramenta (usado quando o companheiro é dispensado
        /// ou morre: o item está no inventário dele, que vai para o snapshot). Sem isso o mod tentaria
        /// devolver para um baú uma ferramenta que já é do companheiro.
        /// </summary>
        internal static void ForgetBorrowed(string reason)
        {
            if (s_borrowed.Count == 0)
            {
                return;
            }

            Plugin.Log.LogInfo(
                $"[ModIA] Tool keeper: {s_borrowed.Count} ferramenta(s) emprestada(s) ficaram com o " +
                $"companheiro ({reason}).");
            s_borrowed.Clear();
        }

        // ---------------------------------------------------------------------
        // Empréstimo
        // ---------------------------------------------------------------------

        /// <summary>
        /// Procura num baú próximo <b>qualquer</b> ferramenta de golpe (machado ou picareta) para o
        /// companheiro que não tem nenhuma, e inicia a viagem até o baú. Devolve <c>true</c> quando a
        /// viagem começou (a varredura de nós deve ser repetida depois dela).
        /// </summary>
        internal static bool TryStartFetchForMissingTool(CompanionAI companion)
        {
            if (companion == null || !StoreUnused)
            {
                return false;
            }

            // Só procura o tipo que REALMENTE falta (não adianta emprestar outro machado para quem
            // já tem machado e precisa de picareta).
            if (!CompanionInventory.GetMissingHarvestToolKinds(
                    companion, out bool needChop, out bool needPickaxe))
            {
                return false;
            }

            if (!needChop && !needPickaxe)
            {
                return false;
            }

            if (!FindToolInChests(
                    companion.transform.position,
                    CompanionDeposit.ChestRadius,
                    anyTool: true,
                    needChop: needChop,
                    needPickaxe: needPickaxe,
                    minTier: 0,
                    modifiers: default,
                    out Container chest,
                    out string prefab))
            {
                return false;
            }

            return CompanionDeposit.TryStartToolFetchTrip(companion, chest, prefab);
        }

        /// <summary>
        /// Procura num baú próximo a melhor ferramenta que <b>atende ao nó</b> (tier mínimo e dano
        /// contra as resistências dele) — o caso de prata/obsidiana com a picareta de chifre — e
        /// inicia a viagem até o baú.
        /// </summary>
        internal static bool TryStartFetchForNode(
            CompanionAI companion,
            int minToolTier,
            HitData.DamageModifiers modifiers)
        {
            if (companion == null || !StoreUnused || minToolTier < 0)
            {
                return false;
            }

            if (!FindToolInChests(
                    companion.transform.position,
                    CompanionDeposit.ChestRadius,
                    anyTool: false,
                    needChop: false,
                    needPickaxe: false,
                    minTier: minToolTier,
                    modifiers: modifiers,
                    out Container chest,
                    out string prefab))
            {
                return false;
            }

            return CompanionDeposit.TryStartToolFetchTrip(companion, chest, prefab);
        }

        /// <summary>
        /// Melhor ferramenta de coleta nos baús dentro do raio. Com <paramref name="anyTool"/> só
        /// interessam os tipos que faltam (<paramref name="needChop"/>/<paramref name="needPickaxe"/>);
        /// caso contrário exige tier ≥ <paramref name="minTier"/> e dano efetivo &gt; 0 contra
        /// <paramref name="modifiers"/> (a mesma conta da escolha de nó).
        /// </summary>
        private static bool FindToolInChests(
            Vector3 origin,
            float radius,
            bool anyTool,
            bool needChop,
            bool needPickaxe,
            int minTier,
            HitData.DamageModifiers modifiers,
            out Container chest,
            out string prefab)
        {
            chest = null;
            prefab = null;

            if (Time.time < _nextSearchAt)
            {
                return false;
            }

            _nextSearchAt = Time.time + SearchCooldown;

            s_pieces.Clear();
            Piece.GetAllPiecesInRadius(origin, radius, s_pieces);

            s_seen.Clear();
            float bestScore = float.MinValue;

            for (int i = 0; i < s_pieces.Count; i++)
            {
                Piece piece = s_pieces[i];
                if (piece == null)
                {
                    continue;
                }

                Container container = piece.GetComponentInChildren<Container>();
                if (container == null || !s_seen.Add(container.GetInstanceID()))
                {
                    continue;
                }

                if (!CompanionDeposit.IsUsableChest(container))
                {
                    continue;
                }

                Inventory inventory = container.GetInventory();
                if (inventory == null)
                {
                    continue;
                }

                float distance = Vector3.Distance(origin, container.transform.position);
                List<ItemDrop.ItemData> items = inventory.GetAllItems();
                for (int j = 0; j < items.Count; j++)
                {
                    ItemDrop.ItemData item = items[j];
                    if (!IsBorrowable(item, anyTool, needChop, needPickaxe, minTier, modifiers))
                    {
                        continue;
                    }

                    float score = Score(item, anyTool) - distance * 0.01f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        chest = container;
                        prefab = Utils.GetPrefabName(item.m_dropPrefab);
                    }
                }
            }

            if (chest != null)
            {
                Plugin.Log.LogInfo(
                    $"[ModIA] Tool keeper: encontrei '{prefab}' no baú " +
                    $"'{CompanionDeposit.ChestName(chest)}' " +
                    $"a {Vector3.Distance(origin, chest.transform.position):0.#} m.");
            }

            return chest != null;
        }

        /// <summary>Ferramenta de golpe do baú que atende ao que o companheiro precisa.</summary>
        private static bool IsBorrowable(
            ItemDrop.ItemData item,
            bool anyTool,
            bool needChop,
            bool needPickaxe,
            int minTier,
            HitData.DamageModifiers modifiers)
        {
            if (!CompanionInventory.IsHarvestTool(item) || item.m_dropPrefab == null)
            {
                return false;
            }

            // Ferramenta quebrada não serve para nada.
            if (item.m_shared.m_useDurability && item.m_durability <= 0f)
            {
                return false;
            }

            HitData.DamageTypes damage = item.m_shared.m_damages;
            if (anyTool)
            {
                return (needChop && damage.m_chop > 0f) || (needPickaxe && damage.m_pickaxe > 0f);
            }

            return item.m_shared.m_toolTier >= minTier &&
                   CompanionHarvestScanner.EffectiveDamage(damage, modifiers) > 0f;
        }

        /// <summary>
        /// Pontuação da ferramenta (mesma ideia do <c>Score</c> da referência): tier manda, depois
        /// qualidade, dano de golpe e durabilidade restante.
        /// </summary>
        private static float Score(ItemDrop.ItemData item, bool anyTool)
        {
            HitData.DamageTypes damage = item.m_shared.m_damages;
            float hit = anyTool
                ? Mathf.Max(damage.m_chop, damage.m_pickaxe)
                : damage.m_chop + damage.m_pickaxe;

            float durability = item.m_shared.m_useDurability
                ? item.m_durability / Mathf.Max(1f, item.m_shared.m_maxDurability)
                : 1f;

            return item.m_shared.m_toolTier * 10000f +
                   item.m_quality * 1000f +
                   hit +
                   durability;
        }

        // ---------------------------------------------------------------------
        // Registro e devolução
        // ---------------------------------------------------------------------

        /// <summary>Registra que a ferramenta <paramref name="prefab"/> foi emprestada do baú.</summary>
        internal static void NoteBorrowed(Container chest, string prefab)
        {
            if (string.IsNullOrEmpty(prefab))
            {
                return;
            }

            for (int i = 0; i < s_borrowed.Count; i++)
            {
                if (string.Equals(s_borrowed[i].Prefab, prefab, System.StringComparison.Ordinal))
                {
                    s_borrowed[i].Chest = chest;
                    return;
                }
            }

            s_borrowed.Add(new BorrowedTool { Prefab = prefab, Chest = chest });
        }

        /// <summary>Esquece o empréstimo de uma ferramenta que já voltou para o baú.</summary>
        internal static void Forget(string prefab)
        {
            for (int i = s_borrowed.Count - 1; i >= 0; i--)
            {
                if (string.Equals(s_borrowed[i].Prefab, prefab, System.StringComparison.Ordinal))
                {
                    s_borrowed.RemoveAt(i);
                }
            }
        }

        /// <summary>Esquece todos os empréstimos (dismiss/morte: a ferramenta é do companheiro agora).</summary>
        internal static void ClearBorrowed()
        {
            s_borrowed.Clear();
        }

        /// <summary>Nomes (prefab) das ferramentas emprestadas que ainda estão com o companheiro.</summary>
        internal static List<string> BorrowedPrefabs()
        {
            s_prefabBuffer.Clear();
            for (int i = 0; i < s_borrowed.Count; i++)
            {
                s_prefabBuffer.Add(s_borrowed[i].Prefab);
            }

            return s_prefabBuffer;
        }

        /// <summary>Primeira ferramenta emprestada que ainda está no inventário (null = nenhuma).</summary>
        internal static ItemDrop.ItemData FirstBorrowedItem(Inventory inventory)
        {
            if (inventory == null)
            {
                return null;
            }

            for (int i = 0; i < s_borrowed.Count; i++)
            {
                ItemDrop.ItemData item = CompanionInventory.FindItem(inventory, s_borrowed[i].Prefab);
                if (item != null)
                {
                    return item;
                }
            }

            return null;
        }

        /// <summary>
        /// Baú para devolver a ferramenta emprestada: o de <b>origem</b> quando ele ainda existe e
        /// tem espaço; senão o baú utilizável mais próximo com espaço.
        /// </summary>
        internal static Container ChestForReturn(Inventory inventory, ItemDrop.ItemData item)
        {
            if (item == null)
            {
                return null;
            }

            string prefab = Utils.GetPrefabName(item.m_dropPrefab);
            for (int i = 0; i < s_borrowed.Count; i++)
            {
                BorrowedTool entry = s_borrowed[i];
                if (!string.Equals(entry.Prefab, prefab, System.StringComparison.Ordinal))
                {
                    continue;
                }

                Container origin = entry.Chest;
                if (origin != null && CompanionDeposit.IsUsableChest(origin) &&
                    origin.GetInventory() != null &&
                    origin.GetInventory().CanAddItem(item, item.m_stack))
                {
                    return origin;
                }
            }

            CompanionAI companion = CompanionManager.ActiveCompanion;
            Vector3 origin2 = companion != null ? companion.transform.position : Vector3.zero;
            return CompanionDeposit.FindNearestChest(origin2, CompanionDeposit.ChestRadius, item);
        }

        /// <summary>Estado legível do tool keeper (comando <c>modia_stats</c>).</summary>
        internal static string Describe()
        {
            if (s_borrowed.Count == 0)
            {
                return StoreUnused ? "nenhuma ferramenta emprestada" : "desligado (guarda)";
            }

            List<string> prefabs = BorrowedPrefabs();
            return $"{prefabs.Count} emprestada(s): " + string.Join(", ", prefabs.ToArray());
        }
    }
}
