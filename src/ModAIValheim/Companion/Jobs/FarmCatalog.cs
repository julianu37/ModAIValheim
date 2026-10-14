using System.Collections.Generic;
using UnityEngine;

namespace ModAIValheim.Companion.Jobs
{
    /// <summary>
    /// <b>Catálogo de cultivo</b> (Fase 2.16b): o que dá para plantar e o que é colhível, lido dos
    /// <b>próprios prefabs do jogo</b> (nada de lista fixa — culturas de mods entram sozinhas).
    ///
    /// <para>
    /// A regra é a mesma que o jogo usa para o Cultivator: um prefab plantável é aquele que tem
    /// <see cref="Plant"/> <b>e</b> <see cref="Piece"/>, e o "custo" da peça (<c>m_resources[0]</c>)
    /// é justamente a <b>semente</b> que o jogador gasta para colocá-la. É assim que se descobre, por
    /// exemplo, que <c>CarrotSeeds</c> planta o <c>sapling_carrot</c> e que <c>Carrot</c> (a própria
    /// cenoura) planta o <c>sapling_carrot_seed</c> — a "fonte de sementes" que faz a rotação
    /// fechar (plantar parte da colheita para repor sementes).
    /// </para>
    ///
    /// <para>
    /// Só entram culturas de <b>terreno cultivado</b> (<c>Plant.m_needCultivatedGround</c>): mudas de
    /// árvore (faias, pinheiros, carvalhos) ficam de fora — plantar floresta é outro trabalho e o
    /// catálogo ficaria cheio de saplings que o companheiro não deve espalhar pela fazenda.
    /// </para>
    ///
    /// <para>
    /// Adaptado (lógica) de <c>src/Companions/Jobs/FarmController.cs</c> (<c>BuildSeedMapping</c> —
    /// varredura de <c>ZNetScene.instance.m_prefabs</c> por <c>Plant</c> + <c>Piece.m_resources[0]</c>,
    /// com os <c>m_grownPrefabs</c> virando o conjunto de colhíveis) e de
    /// <c>src/Companions/Jobs/MHR_SeedRules.cs</c> (famílias semente/cultura e a checagem de terreno
    /// cultivado) do <i>Offline Companions Reborn</i> (MIT — ver <c>THIRD_PARTY_NOTICES.md</c>). A
    /// implementação aqui é própria (dicionários simples + <c>HashSet</c> de colhíveis, sem rotação de
    /// fileiras nem estoque em baú) e adaptada às seções de config em PT-BR.
    /// </para>
    /// </summary>
    internal static class FarmCatalog
    {
        /// <summary>O que uma semente se torna quando plantada.</summary>
        internal struct SeedPlantInfo
        {
            /// <summary>Prefab da semente/vegetal que é consumido ao plantar (ex.: <c>CarrotSeeds</c>).</summary>
            internal string SeedPrefabName;

            /// <summary>Prefab da planta (peça) que nasce (ex.: <c>sapling_carrot</c>).</summary>
            internal GameObject PlantPrefab;

            /// <summary>Raio de crescimento da planta (m) — define o espaçamento mínimo.</summary>
            internal float GrowRadius;

            /// <summary>Exige terreno cultivado (<c>Plant.m_needCultivatedGround</c>).</summary>
            internal bool NeedsCultivated;

            /// <summary>Bioma exigido (<c>Heightmap.Biome.None</c> = qualquer um).</summary>
            internal Heightmap.Biome Biome;
        }

        private static readonly Dictionary<string, SeedPlantInfo> s_seedToPlant =
            new Dictionary<string, SeedPlantInfo>();

        private static readonly HashSet<string> s_cropOutputs = new HashSet<string>();

        private static bool s_built;

        /// <summary>
        /// O <c>Heightmap.IsCultivated</c> mexeu no mundo (outro mod trocou o heightmap)? Quando
        /// sim, paramos de perguntar e o plantio passa a exigir só "sem obstáculo" (é o mesmo
        /// cinto de segurança da referência, que evita um log de exceção por tick).
        /// </summary>
        private static bool s_cultivatedBroken;

        /// <summary>O catálogo já foi montado e tem pelo menos uma semente?</summary>
        internal static bool Ready => s_built && s_seedToPlant.Count > 0;

        /// <summary>Quantas sementes plantáveis foram encontradas (para o log/console).</summary>
        internal static int SeedCount => s_seedToPlant.Count;

        /// <summary>O item é o fruto de uma cultura (o que a planta pronta larga ao ser colhida)?</summary>
        internal static bool IsCropOutput(string prefabName)
        {
            return !string.IsNullOrEmpty(prefabName) && s_cropOutputs.Contains(prefabName);
        }

        /// <summary>
        /// Os prefabs colhíveis (frutos das culturas), para filtrar o que a colheita pode catar do
        /// chão. O conjunto é <b>do catálogo</b>: quem usa não deve alterá-lo.
        /// </summary>
        internal static HashSet<string> CropOutputs => s_cropOutputs;

        /// <summary>Monta o catálogo uma vez por sessão/mundo (barato: só varre a lista de prefabs).</summary>
        internal static void Ensure()
        {
            if (s_built)
            {
                return;
            }

            s_built = true;
            s_seedToPlant.Clear();
            s_cropOutputs.Clear();

            ZNetScene scene = ZNetScene.instance;
            if (scene == null)
            {
                return;
            }

            List<GameObject> prefabs = scene.m_prefabs;
            for (int i = 0; i < prefabs.Count; i++)
            {
                GameObject prefab = prefabs[i];
                if (prefab == null)
                {
                    continue;
                }

                Plant plant = prefab.GetComponent<Plant>();
                if (plant == null || !plant.m_needCultivatedGround)
                {
                    continue;
                }

                Piece piece = prefab.GetComponent<Piece>();
                if (piece == null || piece.m_resources == null || piece.m_resources.Length == 0 ||
                    piece.m_resources[0].m_resItem == null)
                {
                    continue;
                }

                string seedName = piece.m_resources[0].m_resItem.gameObject.name;
                if (s_seedToPlant.ContainsKey(seedName))
                {
                    continue;
                }

                s_seedToPlant[seedName] = new SeedPlantInfo
                {
                    SeedPrefabName = seedName,
                    PlantPrefab = prefab,
                    GrowRadius = plant.m_growRadius,
                    NeedsCultivated = plant.m_needCultivatedGround,
                    Biome = plant.m_biome,
                };

                GameObject[] grown = plant.m_grownPrefabs;
                if (grown == null)
                {
                    continue;
                }

                for (int g = 0; g < grown.Length; g++)
                {
                    Pickable pickable = grown[g] != null ? grown[g].GetComponent<Pickable>() : null;
                    if (pickable != null && pickable.m_itemPrefab != null)
                    {
                        s_cropOutputs.Add(pickable.m_itemPrefab.name);
                    }
                }
            }

            Plugin.Log.LogInfo(
                $"[ModIA] Fazenda: {s_seedToPlant.Count} semente(s) plantavel(is) e " +
                $"{s_cropOutputs.Count} cultura(s) colhivel(is) mapeadas dos prefabs do jogo.");
        }

        /// <summary>O que esta semente se torna (false quando o item não é plantável).</summary>
        internal static bool TryGetSeed(string seedPrefab, out SeedPlantInfo info)
        {
            Ensure();
            if (string.IsNullOrEmpty(seedPrefab))
            {
                info = default;
                return false;
            }

            return s_seedToPlant.TryGetValue(seedPrefab, out info);
        }

        /// <summary>
        /// A semente que o companheiro vai plantar agora: a <b>primeira</b> semente plantável do
        /// inventário que casa com o filtro ("" = qualquer uma). Devolve <c>null</c> quando não há
        /// nada plantável.
        /// </summary>
        internal static string FindSeed(Inventory inventory, string cropFilter, out SeedPlantInfo info)
        {
            info = default;
            if (inventory == null)
            {
                return null;
            }

            Ensure();

            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (item == null || item.m_dropPrefab == null)
                {
                    continue;
                }

                string prefab = Utils.GetPrefabName(item.m_dropPrefab.name);
                if (!string.IsNullOrEmpty(cropFilter) && prefab != cropFilter)
                {
                    continue;
                }

                if (s_seedToPlant.TryGetValue(prefab, out info))
                {
                    return prefab;
                }
            }

            return null;
        }

        /// <summary>Quantas unidades desta semente o companheiro tem no inventário.</summary>
        internal static int CountSeed(Inventory inventory, string seedPrefab)
        {
            if (inventory == null || string.IsNullOrEmpty(seedPrefab))
            {
                return 0;
            }

            int count = 0;
            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (item != null && item.m_dropPrefab != null &&
                    Utils.GetPrefabName(item.m_dropPrefab.name) == seedPrefab)
                {
                    count += item.m_stack;
                }
            }

            return count;
        }

        /// <summary>
        /// O terreno deste ponto está <b>cultivado</b> (foi arado com o Cultivator)? É a checagem
        /// que o próprio <see cref="Plant"/> faz antes de crescer — plantar fora dela resultaria numa
        /// planta <c>NotCultivated</c> que nunca dá fruto.
        /// </summary>
        internal static bool IsCultivated(Vector3 pos)
        {
            if (s_cultivatedBroken)
            {
                // Sem como perguntar: o plantio passa a exigir só "sem obstáculo" (e o jogador
                // arou o terreno de qualquer jeito — foi ele quem definiu a zona).
                return true;
            }

            try
            {
                Heightmap heightmap = Heightmap.FindHeightmap(pos);
                return heightmap != null && heightmap.IsCultivated(pos);
            }
            catch (System.Exception e)
            {
                s_cultivatedBroken = true;
                Plugin.Log.LogWarning(
                    "[ModIA] Fazenda: nao consegui checar o terreno cultivado (outro mod pode ter " +
                    "trocado o heightmap); o plantio passa a exigir so espaco livre. " + e.Message);
                return true;
            }
        }

        /// <summary>Lista das sementes conhecidas (para o <c>modia_farm</c> sem argumento).</summary>
        internal static List<string> Describe()
        {
            Ensure();
            List<string> names = new List<string>(s_seedToPlant.Keys);
            names.Sort(System.StringComparer.OrdinalIgnoreCase);
            return names;
        }

        /// <summary>Solta o catálogo (fim do mundo / unload: os prefabs do mundo anterior morreram).</summary>
        internal static void Reset()
        {
            s_built = false;
            s_cultivatedBroken = false;
            s_seedToPlant.Clear();
            s_cropOutputs.Clear();
        }
    }
}
