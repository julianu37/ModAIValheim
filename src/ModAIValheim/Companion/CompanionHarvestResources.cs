using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// Categoria de um recurso de coleta (organiza a lista do menu e a ajuda do comando).
    /// </summary>
    internal enum HarvestResourceCategory
    {
        /// <summary>Madeira e derivados (árvores, toras, tocos) — machado.</summary>
        Wood = 0,

        /// <summary>Pedra e pedra "decorativa" (sílex, mármore, grausten) — picareta.</summary>
        Stone = 1,

        /// <summary>Minérios e metais (cobre, estanho, ferro, prata…) — picareta.</summary>
        Ore = 2,

        /// <summary>Colhíveis com a mão (frutas, cogumelos, ervas, grãos).</summary>
        Pickable = 3,
    }

    /// <summary>
    /// Um recurso conhecido de coleta: o <b>nome canônico do prefab do item</b> no
    /// <see cref="ObjectDB"/> (é o que o scanner procura nos drops), um rótulo PT-BR de fallback
    /// (usado só se a localização do jogo não tiver o item) e os apelidos que o jogador pode digitar.
    /// </summary>
    internal struct HarvestResourceInfo
    {
        /// <summary>Nome do prefab do item (chave no <c>ObjectDB</c>), ex.: <c>Stone</c>.</summary>
        public string Prefab;

        /// <summary>Nome em PT-BR usado quando a localização do jogo não resolve o item.</summary>
        public string PtName;

        public HarvestResourceCategory Category;

        /// <summary>Apelidos (já dobrados/minúsculos, sem acento) além do próprio nome do prefab.</summary>
        public string[] Aliases;
    }

    /// <summary>
    /// Lista <b>curada</b> dos materiais que o companheiro sabe coletar (Fase 2.8).
    ///
    /// <para>
    /// Serve a três propósitos:
    /// </para>
    /// <list type="number">
    ///   <item><description><b>Resolver o pedido</b> (<c>modia_gather pedra</c>, <c>stone</c>,
    ///   <c>Stone</c>) para o nome canônico do item — junto com o fallback genérico do
    ///   <see cref="CompanionHarvest"/> (qualquer nome de item/prefab continua valendo).</description></item>
    ///   <item><description><b>Rótulos do menu</b>: o dropdown de recursos usa o nome
    ///   <i>localizado</i> do item (<c>ObjectDB</c> + <see cref="Localization"/>), com o nome PT-BR
    ///   daqui como reserva.</description></item>
    ///   <item><description><b>Ajuda do comando</b> (<c>modia_gather</c> sem argumento).</description></item>
    /// </list>
    ///
    /// <para>
    /// <b>O que entra na lista:</b> só o que cai de <i>nó de coleta</i> (árvore/tora, pedra/minério,
    /// planta colhível) — ver <c>docs/re/harvesting-notes.md</c>. Material que só cai de
    /// <b>criatura</b> (penas, couro, pele de cervo, olho de Greydwarf…), de <b>baú</b> (âmbar,
    /// rubi…) ou de <b>produção do jogador</b> (carvão na fornalha) fica de fora: não há nó para o
    /// companheiro procurar. Ver a tabela do <c>README</c>.
    /// </para>
    ///
    /// <para>
    /// Nada aqui aloca por frame: a lista é estática e o índice de apelidos é montado uma vez no
    /// carregamento da classe.
    /// </para>
    /// </summary>
    internal static class CompanionHarvestResources
    {
        // ---------------------------------------------------------------------
        // Lista curada
        // ---------------------------------------------------------------------

        /// <summary>
        /// Recursos conhecidos, na ordem em que aparecem no menu (madeira → pedra → minério →
        /// colhíveis). Os nomes de prefab foram confirmados no Valheim 1.0 (ver o README).
        /// </summary>
        internal static readonly HarvestResourceInfo[] All =
        {
            // --- Madeira ---------------------------------------------------
            Info("Wood", "Madeira", HarvestResourceCategory.Wood,
                "madeira", "lenha", "tronco", "wood"),
            Info("FineWood", "Madeira fina", HarvestResourceCategory.Wood,
                "madeirafina", "finewood", "fina"),
            Info("RoundLog", "Tora", HarvestResourceCategory.Wood,
                "tora", "toras", "roundlog", "log"),
            Info("ElderBark", "Casca de ancião", HarvestResourceCategory.Wood,
                "cascadeanciao", "casca", "elderbark"),
            Info("YggdrasilWood", "Madeira de Yggdrasil", HarvestResourceCategory.Wood,
                "madeiradeyggdrasil", "yggdrasil", "yggdrasilwood"),
            Info("Blackwood", "Madeira negra", HarvestResourceCategory.Wood,
                "madeiranegra", "blackwood"),
            Info("Resin", "Resina", HarvestResourceCategory.Wood,
                "resina", "resin"),

            // --- Pedra -----------------------------------------------------
            Info("Stone", "Pedra", HarvestResourceCategory.Stone,
                "pedra", "pedras", "rocha", "rochas", "stone"),
            Info("Flint", "Sílex", HarvestResourceCategory.Stone,
                "silex", "flint", "pedradefogo"),
            Info("Obsidian", "Obsidiana", HarvestResourceCategory.Stone,
                "obsidiana", "obsidian"),
            Info("BlackMarble", "Mármore negro", HarvestResourceCategory.Stone,
                "marmorenegro", "marmore", "blackmarble", "marble"),
            Info("Grausten", "Grausten", HarvestResourceCategory.Stone,
                "grausten"),

            // --- Minérios --------------------------------------------------
            Info("CopperOre", "Minério de cobre", HarvestResourceCategory.Ore,
                "cobre", "mineriodecobre", "copper", "copperore"),
            Info("TinOre", "Minério de estanho", HarvestResourceCategory.Ore,
                "estanho", "mineriodeestanho", "tin", "tinore"),
            Info("IronScrap", "Sucata de ferro", HarvestResourceCategory.Ore,
                "ferro", "sucatadeferro", "iron", "ironscrap"),
            Info("SilverOre", "Minério de prata", HarvestResourceCategory.Ore,
                "prata", "mineriodeprata", "silver", "silverore"),
            Info("BlackMetalScrap", "Sucata de metal negro", HarvestResourceCategory.Ore,
                "metalnegro", "sucatademetalnegro", "blackmetal", "blackmetalscrap"),
            Info("CopperScrap", "Sucata de cobre", HarvestResourceCategory.Ore,
                "sucatadecobre", "copperscrap"),
            Info("FlametalOreNew", "Minério de flametal", HarvestResourceCategory.Ore,
                "flametal", "minerioflametal", "flametalore"),
            Info("Chitin", "Quitina", HarvestResourceCategory.Ore,
                "quitina", "chitin"),
            Info("Crystal", "Cristal", HarvestResourceCategory.Ore,
                "cristal", "crystal"),

            // --- Colhíveis -------------------------------------------------
            Info("Raspberry", "Framboesa", HarvestResourceCategory.Pickable,
                "framboesa", "amora", "raspberry"),
            Info("Blueberries", "Mirtilos", HarvestResourceCategory.Pickable,
                "mirtilo", "mirtilos", "blueberry", "blueberries"),
            Info("Cloudberry", "Amora-branca", HarvestResourceCategory.Pickable,
                "amorabranca", "cloudberry"),
            Info("Mushroom", "Cogumelo", HarvestResourceCategory.Pickable,
                "cogumelo", "mushroom"),
            Info("MushroomYellow", "Cogumelo amarelo", HarvestResourceCategory.Pickable,
                "cogumeloamarelo", "mushroomyellow"),
            Info("MushroomBlue", "Cogumelo azul", HarvestResourceCategory.Pickable,
                "cogumeloazul", "mushroomblue"),
            Info("MushroomMagecap", "Cogumelo mágico", HarvestResourceCategory.Pickable,
                "cogumelomagico", "magecap", "mushroommagecap"),
            Info("MushroomJotunPuffs", "Cogumelo Jotun", HarvestResourceCategory.Pickable,
                "cogumelojotun", "jotunpuffs", "mushroomjotunpuffs"),
            Info("MushroomSmokePuff", "Cogumelo de fumaça", HarvestResourceCategory.Pickable,
                "cogumelodefumaca", "smokepuff", "mushroomsmokepuff"),
            Info("Thistle", "Cardo", HarvestResourceCategory.Pickable,
                "cardo", "thistle"),
            Info("Dandelion", "Dente-de-leão", HarvestResourceCategory.Pickable,
                "dentedeleao", "dandelion"),
            Info("Carrot", "Cenoura", HarvestResourceCategory.Pickable,
                "cenoura", "carrot"),
            Info("Turnip", "Nabo", HarvestResourceCategory.Pickable,
                "nabo", "turnip"),
            Info("Onion", "Cebola", HarvestResourceCategory.Pickable,
                "cebola", "onion"),
            Info("Barley", "Cevada", HarvestResourceCategory.Pickable,
                "cevada", "barley"),
            Info("Flax", "Linho", HarvestResourceCategory.Pickable,
                "linho", "flax"),
            Info("RoyalJelly", "Geleia real", HarvestResourceCategory.Pickable,
                "geleia real", "geleiaReal", "geleia", "royaljelly"),
            Info("Honey", "Mel", HarvestResourceCategory.Pickable,
                "mel", "honey"),
        };

        /// <summary>Prefab que representa "sem recurso" no menu (o jogador vai digitar outro).</summary>
        internal const string OtherValue = "__other__";

        private static readonly Dictionary<string, string> s_aliasIndex = BuildAliasIndex();

        // ---------------------------------------------------------------------
        // Resolução
        // ---------------------------------------------------------------------

        /// <summary>
        /// Traduz um texto digitado (apelido PT/EN, nome do prefab ou nome PT-BR da lista) para o
        /// nome canônico do item. Devolve <c>false</c> quando não está na lista curada — quem chama
        /// ainda pode tentar o fallback genérico do <see cref="CompanionHarvest"/>.
        /// </summary>
        internal static bool TryResolve(string input, out string prefab)
        {
            prefab = null;
            if (string.IsNullOrEmpty(input))
            {
                return false;
            }

            string folded = CompanionHarvest.Fold(input);
            if (folded.Length == 0)
            {
                return false;
            }

            if (s_aliasIndex.TryGetValue(folded, out prefab))
            {
                return true;
            }

            // Nome do prefab ou nome PT-BR exato da lista.
            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i].Prefab, input.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(CompanionHarvest.Fold(All[i].PtName), folded, StringComparison.Ordinal))
                {
                    prefab = All[i].Prefab;
                    return true;
                }
            }

            prefab = null;
            return false;
        }

        /// <summary>Nome PT-BR de reserva do prefab (ou <c>null</c> se não estiver na lista curada).</summary>
        internal static string PtNameFor(string prefab)
        {
            if (string.IsNullOrEmpty(prefab))
            {
                return null;
            }

            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i].Prefab, prefab, StringComparison.OrdinalIgnoreCase))
                {
                    return All[i].PtName;
                }
            }

            return null;
        }

        /// <summary>
        /// Categoria curada do recurso. <c>false</c> quando o nome não está na lista (recurso fora da
        /// lista: tratamos como se precisasse de ferramenta, o lado conservador).
        /// </summary>
        internal static bool TryGetCategory(string prefab, out HarvestResourceCategory category)
        {
            category = HarvestResourceCategory.Wood;
            if (string.IsNullOrEmpty(prefab))
            {
                return false;
            }

            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i].Prefab, prefab, StringComparison.OrdinalIgnoreCase))
                {
                    category = All[i].Category;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Nome de exibição de um recurso: o nome <b>localizado</b> do item no idioma do jogo
        /// (<c>ObjectDB</c> → <see cref="ItemDrop.ItemData.ItemShared.m_name"/> →
        /// <see cref="Localization"/>), caindo para o nome PT-BR da lista e, por último, para o
        /// próprio nome do prefab.
        /// </summary>
        internal static string DisplayName(string prefab)
        {
            if (string.IsNullOrEmpty(prefab))
            {
                return "-";
            }

            ObjectDB db = ObjectDB.instance;
            GameObject go = db != null ? db.GetItemPrefab(prefab) : null;
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            if (drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null)
            {
                Localization localization = Localization.instance;
                string sharedName = drop.m_itemData.m_shared.m_name;
                if (localization != null && !string.IsNullOrEmpty(sharedName))
                {
                    string localized = localization.Localize(sharedName);
                    if (!string.IsNullOrEmpty(localized) && localized[0] != '[' && localized[0] != '$')
                    {
                        return localized;
                    }
                }
            }

            return PtNameFor(prefab) ?? prefab;
        }

        /// <summary>Rótulo PT-BR da categoria (usado na ajuda do comando).</summary>
        internal static string CategoryLabel(HarvestResourceCategory category)
        {
            switch (category)
            {
                case HarvestResourceCategory.Wood:
                    return "madeira";
                case HarvestResourceCategory.Stone:
                    return "pedra";
                case HarvestResourceCategory.Ore:
                    return "minério";
                default:
                    return "colhível";
            }
        }

        /// <summary>
        /// Linhas da lista curada para a ajuda do console, agrupadas por categoria e usando o nome
        /// localizado (quando o mundo já estiver carregado). Aloca só sob demanda (comando).
        /// </summary>
        internal static List<string> DescribeForHelp()
        {
            List<string> lines = new List<string>();
            HarvestResourceCategory current = (HarvestResourceCategory)(-1);
            System.Text.StringBuilder builder = new System.Text.StringBuilder(96);

            for (int i = 0; i < All.Length; i++)
            {
                HarvestResourceInfo info = All[i];
                if (info.Category != current)
                {
                    if (builder.Length > 0)
                    {
                        lines.Add(builder.ToString());
                        builder.Length = 0;
                    }

                    current = info.Category;
                    builder.Append("[ModIA] ").Append(CategoryLabel(current)).Append(": ");
                }
                else
                {
                    builder.Append(", ");
                }

                builder.Append(DisplayName(info.Prefab));
            }

            if (builder.Length > 0)
            {
                lines.Add(builder.ToString());
            }

            return lines;
        }

        // ---------------------------------------------------------------------
        // Índice de apelidos
        // ---------------------------------------------------------------------

        private static Dictionary<string, string> BuildAliasIndex()
        {
            Dictionary<string, string> index = new Dictionary<string, string>(StringComparer.Ordinal);

            for (int i = 0; i < All.Length; i++)
            {
                HarvestResourceInfo info = All[i];

                // O próprio nome do prefab e o nome PT-BR sempre resolvem.
                AddAlias(index, info.Prefab, info.Prefab);
                AddAlias(index, info.PtName, info.Prefab);

                string[] aliases = info.Aliases;
                if (aliases == null)
                {
                    continue;
                }

                for (int a = 0; a < aliases.Length; a++)
                {
                    AddAlias(index, aliases[a], info.Prefab);
                }
            }

            return index;
        }

        private static void AddAlias(Dictionary<string, string> index, string alias, string prefab)
        {
            string folded = CompanionHarvest.Fold(alias);
            if (folded.Length == 0 || index.ContainsKey(folded))
            {
                return;
            }

            index[folded] = prefab;
        }

        private static HarvestResourceInfo Info(
            string prefab, string ptName, HarvestResourceCategory category, params string[] aliases)
        {
            HarvestResourceInfo info = default;
            info.Prefab = prefab;
            info.PtName = ptName;
            info.Category = category;
            info.Aliases = aliases;
            return info;
        }
    }
}
