using System.Collections.Generic;
using UnityEngine;

namespace ModAIValheim.Companion
{
    /// <summary>Tipo do nó de coleta (espelha as categorias do banco de recursos do Thrall).</summary>
    internal enum HarvestNodeKind
    {
        None = 0,

        /// <summary>Item jogado no chão (<c>ItemDrop</c>) — o próprio recurso pronto para pegar.</summary>
        ItemDrop,

        /// <summary>Planta/fruto colhível com a mão (<c>Pickable</c>).</summary>
        Pickable,

        /// <summary>Árvore em pé (<c>TreeBase</c>) — precisa de machado.</summary>
        TreeBase,

        /// <summary>Tora no chão (<c>TreeLog</c>) — precisa de machado.</summary>
        TreeLog,

        /// <summary>Pedra pequena (<c>MineRock</c>) — precisa de picareta.</summary>
        MineRock,

        /// <summary>Rocha grande de vários golpes (<c>MineRock5</c>) — precisa de picareta.</summary>
        MineRock5,

        /// <summary>Destrutível (arbusto/entulho) com drops próprios (<c>Destructible</c>).</summary>
        Destructible,
    }

    /// <summary>
    /// Dados de um <b>prefab</b> de nó (não da instância): tipo, drops, vida, resistências e tier
    /// mínimo de ferramenta. É o equivalente local do <c>resourceDatabase</c> do Thrall, mas
    /// construído sob demanda (só para os prefabs que aparecem na varredura) em vez de varrer o
    /// mundo inteiro no início do mundo.
    /// </summary>
    internal struct HarvestPrefabInfo
    {
        public HarvestNodeKind Kind;

        /// <summary>Hashes (<c>GetStableHashCode</c>) dos nomes dos prefabs de item que este nó dropa.</summary>
        public int[] DropHashes;

        public int DropMin;
        public int DropMax;

        public float Health;
        public int MinToolTier;
        public HitData.DamageModifiers Modifiers;

        /// <summary>True quando o recurso pedido está entre os drops deste prefab.</summary>
        public bool DropsResource(int resourceHash)
        {
            if (DropHashes == null)
            {
                return false;
            }

            for (int i = 0; i < DropHashes.Length; i++)
            {
                if (DropHashes[i] == resourceHash)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Busca de nós de coleta (Fase 2.4b).
    ///
    /// <para>
    /// <b>Como procura:</b> uma varredura <c>Physics.OverlapSphereNonAlloc</c> com buffer
    /// pré-alocado (nada de alocar por varredura e nada de <c>Find*</c>/LINQ por frame), no
    /// intervalo configurado (0,5 s por padrão). De cada collider subimos até o <see cref="ZNetView"/>
    /// pai — que no Valheim é sempre a raiz do prefab de mundo — e classificamos a raiz pelos
    /// componentes (<see cref="Pickable"/>, <see cref="TreeBase"/>, <see cref="MineRock"/>…).
    /// O resultado por prefab fica num cache; objetos repetidos são descartados por instância.
    /// </para>
    ///
    /// <para>
    /// <b>Como escolhe:</b> mesma heurística de "facilidade" do Thrall
    /// (<c>Resource.CalculateEaseScore</c>), com bônus por categoria: quantidade (30 %),
    /// <c>100/vida</c> (30 %) e <c>100/(1+distância)</c> (40 %). Nós que a ferramenta disponível
    /// não consegue sequer arranhar levam penalidade (tier mínimo da ferramenta), e itens no chão /
    /// colhíveis ganham bônus — pegar o que já caiu é barato e é o que conta para a meta.
    /// </para>
    ///
    /// <para>
    /// <b>O que nunca vira alvo:</b> construções do jogador (<c>WearNTear</c>/<c>Piece</c>), baús e
    /// contêineres (<c>Container</c>), personagens e qualquer coisa sem <see cref="ZNetView"/>
    /// (terreno, rochas do heightmap). Nós dentro d'água ou dentro/nomeadamente perto de uma
    /// construção também são descartados.
    /// </para>
    /// </summary>
    internal static class CompanionHarvestScanner
    {
        /// <summary>Tamanho do buffer da varredura (reaproveitado; nunca realocado em jogo).</summary>
        private const int ColliderBufferSize = 512;

        /// <summary>Raio (m) de construção do jogador que desqualifica um nó.</summary>
        private const float PlayerBuildAvoidRadius = 2.5f;

        private static readonly Collider[] s_colliders = new Collider[ColliderBufferSize];
        private static readonly Collider[] s_fallbackColliders = new Collider[ColliderBufferSize];
        private static readonly HashSet<int> s_seen = new HashSet<int>();
        private static readonly Dictionary<int, HarvestPrefabInfo> s_prefabs = new Dictionary<int, HarvestPrefabInfo>();
        private static readonly Dictionary<int, float> s_blacklist = new Dictionary<int, float>();
        private static readonly List<int> s_expired = new List<int>();
        private static readonly Collider[] s_buildBuffer = new Collider[16];
        private static readonly List<int> s_dropHashes = new List<int>();
        private static readonly RaycastHit[] s_rayHits = new RaycastHit[8];

        private static int s_resourceMask;
        private static int s_buildMask;
        private static bool s_masksBuilt;
        private static bool s_overflowWarned;
        private static WaterVolume s_waterVolume;

        internal static void Shutdown()
        {
            s_prefabs.Clear();
            s_blacklist.Clear();
            s_seen.Clear();
            s_overflowWarned = false;
            s_waterVolume = null;
        }

        /// <summary>Esquece os vetos (nova tarefa: vale tentar de novo).</summary>
        internal static void ForgetBlacklist()
        {
            s_blacklist.Clear();
        }

        internal static void Blacklist(GameObject node, float seconds)
        {
            if (node == null)
            {
                return;
            }

            s_blacklist[node.GetInstanceID()] = Time.time + seconds;
        }

        // ---------------------------------------------------------------------
        // Busca
        // ---------------------------------------------------------------------

        /// <summary>
        /// Procura o melhor nó para o recurso pedido e devolve a descrição da escolha no log.
        /// <paramref name="availableToolTier"/> é o maior tier de ferramenta no inventário do
        /// companheiro (usado só para pontuar nós que ele não conseguiria quebrar).
        /// </summary>
        internal static GameObject FindBestNode(
            Vector3 origin,
            string resource,
            float radius,
            bool includeDrops,
            int availableToolTier,
            out string reason)
        {
            reason = null;

            if (string.IsNullOrEmpty(resource))
            {
                reason = "nenhum recurso definido";
                return null;
            }

            EnsureMasks();
            PurgeBlacklist(Time.time);

            int resourceHash = resource.GetStableHashCode();
            int count = Physics.OverlapSphereNonAlloc(
                origin, radius, s_colliders, s_resourceMask, QueryTriggerInteraction.Collide);

            s_seen.Clear();

            GameObject best = null;
            float bestScore = float.MinValue;
            int candidates = 0;

            if (count > 0)
            {
                EvaluateCandidates(
                    s_colliders, Mathf.Min(count, s_colliders.Length), origin, radius, resourceHash,
                    includeDrops, availableToolTier, ref best, ref bestScore, ref candidates);
            }

            // Buffer cheio: roda uma segunda passada num raio menor, para garantir que os nós
            // vizinhos (os que interessam) entrem na conta. O dedup evita reprocessar nada.
            if (count >= s_colliders.Length)
            {
                if (!s_overflowWarned)
                {
                    s_overflowWarned = true;
                    Plugin.Log.LogWarning(
                        $"[ModIA] Coleta: a varredura encheu o buffer de {ColliderBufferSize} colliders " +
                        "(área muito densa); reduzindo o raio nesta passada. Se persistir, reduza " +
                        "Coleta/RaioVarredura ou estreite Coleta/Camadas.");
                }

                float fallbackRadius = Mathf.Max(5f, radius * 0.5f);
                int fallbackCount = Physics.OverlapSphereNonAlloc(
                    origin, fallbackRadius, s_fallbackColliders, s_resourceMask, QueryTriggerInteraction.Collide);

                EvaluateCandidates(
                    s_fallbackColliders, Mathf.Min(fallbackCount, s_fallbackColliders.Length), origin,
                    radius, resourceHash, includeDrops, availableToolTier, ref best, ref bestScore,
                    ref candidates);
            }

            if (best == null)
            {
                reason = candidates == 0
                    ? $"nenhum nó de '{resource}' no raio de {radius:0} m " +
                      "(confira Coleta/Camadas se o recurso existe por perto)"
                    : $"nenhum nó de '{resource}' utilizável";
                return null;
            }

            TryGetInfo(best, out HarvestPrefabInfo bestInfo);
            float bestDistance = Vector3.Distance(best.transform.position, origin);
            reason =
                $"'{best.name}' ({bestInfo.Kind}, {candidates} candidato(s)), " +
                $"a {bestDistance:0.#} m, score {bestScore:0.#}";
            return best;
        }

        /// <summary>
        /// Avalia um lote de colliders da varredura e atualiza o melhor nó/score. Usado pela
        /// passada principal e pela passada de raio reduzido (quando o buffer satura).
        /// </summary>
        private static void EvaluateCandidates(
            Collider[] colliders,
            int count,
            Vector3 origin,
            float radius,
            int resourceHash,
            bool includeDrops,
            int availableToolTier,
            ref GameObject best,
            ref float bestScore,
            ref int candidates)
        {
            for (int i = 0; i < count; i++)
            {
                Collider collider = colliders[i];
                if (collider == null)
                {
                    continue;
                }

                GameObject node = ResolveNode(collider);
                if (node == null || !s_seen.Add(node.GetInstanceID()))
                {
                    continue;
                }

                if (s_blacklist.ContainsKey(node.GetInstanceID()))
                {
                    continue;
                }

                if (!TryGetInfo(node, out HarvestPrefabInfo info))
                {
                    continue;
                }

                if (info.Kind == HarvestNodeKind.ItemDrop && !includeDrops)
                {
                    continue;
                }

                if (!info.DropsResource(resourceHash))
                {
                    continue;
                }

                if (!IsHarvestableNow(node, info.Kind))
                {
                    continue;
                }

                Vector3 position = node.transform.position;
                if (IsUnderwater(position))
                {
                    continue;
                }

                if (NeedsTool(info.Kind) && HasPlayerBuildNear(position))
                {
                    continue;
                }

                float distance = Vector3.Distance(position, origin);
                if (distance > radius)
                {
                    continue;
                }

                candidates++;
                float score = CalculateEaseScore(info, distance, availableToolTier);

                if (info.Kind == HarvestNodeKind.ItemDrop)
                {
                    // O cache é por prefab, mas pilhas no chão têm tamanhos diferentes: aqui vale o
                    // stack da instância (pegar 20 de uma vez vale mais que pegar 1).
                    int stack = GetDropStack(node);
                    score += (stack - 1) * 10f * 0.3f;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = node;
                }
            }
        }

        /// <summary>O objeto virou alvo válido ainda? (usado a cada tick no nó já escolhido).</summary>
        internal static bool IsStillValidNode(GameObject node, string resource)
        {
            if (node == null || string.IsNullOrEmpty(resource))
            {
                return false;
            }

            if (!TryGetInfo(node, out HarvestPrefabInfo info))
            {
                return false;
            }

            return info.DropsResource(resource.GetStableHashCode()) && IsHarvestableNow(node, info.Kind);
        }

        /// <summary>Tamanho do stack de um <c>ItemDrop</c> no chão (1 se não der para ler).</summary>
        internal static int GetDropStack(GameObject node)
        {
            ItemDrop drop = node != null ? node.GetComponent<ItemDrop>() : null;
            return drop != null && drop.m_itemData != null ? Mathf.Max(1, drop.m_itemData.m_stack) : 1;
        }

        // ---------------------------------------------------------------------
        // Pontuação
        // ---------------------------------------------------------------------

        /// <summary>
        /// "Facilidade" de um nó — a mesma fórmula do <c>Resource.CalculateEaseScore</c> do Thrall
        /// (quantidade 30 %, vida 30 %, distância 40 %), com bônus por categoria.
        /// </summary>
        internal static float CalculateEaseScore(HarvestPrefabInfo info, float distance, int availableToolTier)
        {
            float averageAmount = (info.DropMin + info.DropMax) * 0.5f * 10f;
            float health = info.Health > 0f ? info.Health : 1f;
            float ease = 100f / health;
            float proximity = 100f / (1f + distance);

            float score = averageAmount * 0.3f + ease * 0.3f + proximity * 0.4f;

            // Ferramenta insuficiente (tier mínimo acima do disponível): deixa no fim da fila, mas
            // ainda escolhível se for o único nó — nesse caso a IA vai desistir e vetar depois.
            if (NeedsTool(info.Kind) && info.MinToolTier > availableToolTier)
            {
                score *= 0.2f;
            }

            return score * CategoryBonus(info.Kind);
        }

        private static float CategoryBonus(HarvestNodeKind kind)
        {
            switch (kind)
            {
                case HarvestNodeKind.ItemDrop:
                    // Já está no chão: pegar é barato e é o que conta para a quantidade pedida.
                    return 1.6f;
                case HarvestNodeKind.Pickable:
                    // Colher não gasta ferramenta.
                    return 1.3f;
                default:
                    return 1f;
            }
        }

        /// <summary>Categorias que exigem golpe (as outras são interação direta).</summary>
        internal static bool NeedsTool(HarvestNodeKind kind)
        {
            switch (kind)
            {
                case HarvestNodeKind.TreeBase:
                case HarvestNodeKind.TreeLog:
                case HarvestNodeKind.MineRock:
                case HarvestNodeKind.MineRock5:
                case HarvestNodeKind.Destructible:
                    return true;
                default:
                    return false;
            }
        }

        // ---------------------------------------------------------------------
        // Classificação
        // ---------------------------------------------------------------------

        /// <summary>
        /// Raiz do objeto de mundo a que o collider pertence. O <see cref="ZNetView"/> fica sempre na
        /// raiz do prefab, então isso também descarta terreno, construções não-rede e afins.
        /// </summary>
        private static GameObject ResolveNode(Collider collider)
        {
            ZNetView netView = collider.GetComponentInParent<ZNetView>();
            if (netView == null)
            {
                return null;
            }

            GameObject go = netView.gameObject;

            // Nunca mexer em construção do jogador (peças) nem em contêiner (baú, fornalha…),
            // mesmo que por algum motivo tenham um desse tipo de componente.
            if (go.GetComponent<WearNTear>() != null || go.GetComponent<Container>() != null)
            {
                return null;
            }

            return go;
        }

        internal static bool TryGetInfo(GameObject node, out HarvestPrefabInfo info)
        {
            info = default;

            if (node == null)
            {
                return false;
            }

            string prefabName = Utils.GetPrefabName(node.name);
            int hash = prefabName.GetStableHashCode();
            if (s_prefabs.TryGetValue(hash, out info))
            {
                return info.Kind != HarvestNodeKind.None;
            }

            info = BuildInfo(node);
            s_prefabs[hash] = info;
            return info.Kind != HarvestNodeKind.None;
        }

        /// <summary>
        /// Classifica um prefab pela ordem de prioridade das categorias e coleta drops/vida/
        /// resistências/tier de ferramenta. O resultado é cacheado por nome de prefab (o cache é do
        /// prefab, não da instância — é o papel que o <c>resourceDatabase</c> do Thrall fazia, só
        /// que sob demanda em vez de varrer o mundo inteiro no início do mundo).
        /// </summary>
        private static HarvestPrefabInfo BuildInfo(GameObject node)
        {
            HarvestPrefabInfo info = default;
            s_dropHashes.Clear();
            int dropMin = 0;
            int dropMax = 0;

            bool classified =
                TryClassifyPickable(node, ref info, ref dropMin, ref dropMax) ||
                TryClassifyTreeBase(node, ref info, ref dropMin, ref dropMax) ||
                TryClassifyTreeLog(node, ref info, ref dropMin, ref dropMax) ||
                TryClassifyMineRock(node, ref info, ref dropMin, ref dropMax) ||
                TryClassifyMineRock5(node, ref info, ref dropMin, ref dropMax) ||
                TryClassifyDestructible(node, ref info, ref dropMin, ref dropMax) ||
                TryClassifyItemDrop(node, ref info, ref dropMin, ref dropMax);

            if (!classified)
            {
                info.Kind = HarvestNodeKind.None;
            }

            info.DropHashes = s_dropHashes.Count > 0 ? s_dropHashes.ToArray() : null;
            info.DropMin = dropMin;
            info.DropMax = dropMax;
            s_dropHashes.Clear();
            return info;
        }

        private static bool TryClassifyPickable(GameObject node, ref HarvestPrefabInfo info, ref int dropMin, ref int dropMax)
        {
            Pickable pickable = node.GetComponent<Pickable>();
            if (pickable == null)
            {
                return false;
            }

            info.Kind = HarvestNodeKind.Pickable;
            info.Health = 1f;
            AddDrop(pickable.m_itemPrefab, pickable.m_amount, pickable.m_amount, ref dropMin, ref dropMax);
            AddDropTable(pickable.m_extraDrops, ref dropMin, ref dropMax);
            return true;
        }

        private static bool TryClassifyTreeBase(GameObject node, ref HarvestPrefabInfo info, ref int dropMin, ref int dropMax)
        {
            TreeBase tree = node.GetComponent<TreeBase>();
            if (tree == null)
            {
                return false;
            }

            info.Kind = HarvestNodeKind.TreeBase;
            info.Health = tree.m_health;
            info.MinToolTier = tree.m_minToolTier;
            info.Modifiers = tree.m_damageModifiers;
            AddDropTable(tree.m_dropWhenDestroyed, ref dropMin, ref dropMax);
            return true;
        }

        private static bool TryClassifyTreeLog(GameObject node, ref HarvestPrefabInfo info, ref int dropMin, ref int dropMax)
        {
            TreeLog log = node.GetComponent<TreeLog>();
            if (log == null)
            {
                return false;
            }

            info.Kind = HarvestNodeKind.TreeLog;
            info.Health = log.m_health;
            info.MinToolTier = log.m_minToolTier;
            info.Modifiers = log.m_damages;
            AddDropTable(log.m_dropWhenDestroyed, ref dropMin, ref dropMax);
            return true;
        }

        private static bool TryClassifyMineRock(GameObject node, ref HarvestPrefabInfo info, ref int dropMin, ref int dropMax)
        {
            MineRock rock = node.GetComponent<MineRock>();
            if (rock == null)
            {
                return false;
            }

            info.Kind = HarvestNodeKind.MineRock;
            info.Health = rock.m_health;
            info.MinToolTier = rock.m_minToolTier;
            info.Modifiers = rock.m_damageModifiers;
            AddDropTable(rock.m_dropItems, ref dropMin, ref dropMax);
            return true;
        }

        private static bool TryClassifyMineRock5(GameObject node, ref HarvestPrefabInfo info, ref int dropMin, ref int dropMax)
        {
            MineRock5 rock = node.GetComponent<MineRock5>();
            if (rock == null)
            {
                return false;
            }

            info.Kind = HarvestNodeKind.MineRock5;
            info.Health = rock.m_health;
            info.MinToolTier = rock.m_minToolTier;
            info.Modifiers = rock.m_damageModifiers;
            AddDropTable(rock.m_dropItems, ref dropMin, ref dropMax);
            return true;
        }

        private static bool TryClassifyDestructible(GameObject node, ref HarvestPrefabInfo info, ref int dropMin, ref int dropMax)
        {
            Destructible destructible = node.GetComponent<Destructible>();
            if (destructible == null)
            {
                return false;
            }

            info.Kind = HarvestNodeKind.Destructible;
            info.Health = destructible.m_health;
            info.MinToolTier = destructible.m_minToolTier;
            info.Modifiers = destructible.m_damages;

            // Os drops de um Destructible moram num DropOnDestroyed (normalmente filho) ligado ao
            // m_onDestroyed dele.
            DropOnDestroyed drops = node.GetComponentInChildren<DropOnDestroyed>();
            if (drops != null)
            {
                AddDropTable(drops.m_dropWhenDestroyed, ref dropMin, ref dropMax);
            }

            return true;
        }

        private static bool TryClassifyItemDrop(GameObject node, ref HarvestPrefabInfo info, ref int dropMin, ref int dropMax)
        {
            ItemDrop item = node.GetComponent<ItemDrop>();
            if (item == null)
            {
                return false;
            }

            info.Kind = HarvestNodeKind.ItemDrop;
            info.Health = 1f;

            // m_dropPrefab é preenchido no ItemDrop.Awake a partir do ObjectDB; sem ele cai para o
            // próprio nome do objeto (que é o nome do item, com "(Clone)" cortado pelo Utils).
            GameObject dropPrefab = item.m_itemData != null ? item.m_itemData.m_dropPrefab : null;
            if (dropPrefab == null)
            {
                dropPrefab = node;
            }

            AddDrop(dropPrefab, 1, 1, ref dropMin, ref dropMax);
            return true;
        }

        private static void AddDropTable(DropTable table, ref int dropMin, ref int dropMax)
        {
            if (table == null || table.m_drops == null)
            {
                return;
            }

            List<DropTable.DropData> drops = table.m_drops;
            for (int i = 0; i < drops.Count; i++)
            {
                DropTable.DropData drop = drops[i];
                AddDrop(drop.m_item, drop.m_stackMin, drop.m_stackMax, ref dropMin, ref dropMax);
            }
        }

        private static void AddDrop(GameObject prefab, int stackMin, int stackMax, ref int dropMin, ref int dropMax)
        {
            if (prefab == null)
            {
                return;
            }

            int hash = Utils.GetPrefabName(prefab.name).GetStableHashCode();
            if (!s_dropHashes.Contains(hash))
            {
                s_dropHashes.Add(hash);
            }

            dropMin += Mathf.Max(0, stackMin);
            dropMax += Mathf.Max(0, stackMax);
        }

        // ---------------------------------------------------------------------
        // Filtros de segurança / viabilidade
        // ---------------------------------------------------------------------

        private static bool IsHarvestableNow(GameObject node, HarvestNodeKind kind)
        {
            if (node == null || !node.activeInHierarchy)
            {
                return false;
            }

            if (kind == HarvestNodeKind.Pickable)
            {
                Pickable pickable = node.GetComponent<Pickable>();
                return pickable != null && pickable.CanBePicked();
            }

            if (kind == HarvestNodeKind.ItemDrop)
            {
                ItemDrop item = node.GetComponent<ItemDrop>();
                return item != null && !item.InTar();
            }

            float health = GetHealth(node);
            return health < 0f || health > 0f;
        }

        /// <summary>
        /// Vida do nó lida do ZDO (<c>ZDOVars.s_health</c>) — <c>-1</c> quando o tipo não guarda vida
        /// legível ali (ex.: <c>MineRock5</c> guarda um pacote codificado). Serve de telemetria; a
        /// detecção de progresso usa <see cref="GetRevision"/>.
        /// </summary>
        internal static float GetHealth(GameObject node)
        {
            ZDO zdo = GetZdo(node);
            if (zdo == null)
            {
                return -1f;
            }

            return zdo.GetFloat(ZDOVars.s_health, -1f);
        }

        /// <summary>
        /// Revisão de dados do ZDO (-1 se não houver): o jogo a incrementa a cada <c>Set</c>, então
        /// ela muda quando o nó leva dano — é o nosso sinal universal de "o golpe fez efeito".
        /// </summary>
        internal static long GetRevision(GameObject node)
        {
            ZDO zdo = GetZdo(node);
            return zdo != null ? zdo.DataRevision : -1L;
        }

        private static ZDO GetZdo(GameObject node)
        {
            if (node == null)
            {
                return null;
            }

            ZNetView netView = node.GetComponent<ZNetView>();
            if (netView == null || !netView.IsValid())
            {
                return null;
            }

            return netView.GetZDO();
        }

        /// <summary>
        /// Nó submerso? Usa o nível de água do próprio jogo (<c>Floating.GetWaterLevel</c>, público
        /// na 1.0.17) — o Thrall dependia de <c>Fish.GetWaterLevel</c>, que virou privado.
        /// </summary>
        internal static bool IsUnderwater(Vector3 position)
        {
            float waterLevel = Floating.GetWaterLevel(position, ref s_waterVolume);
            return waterLevel > -10000f && position.y < waterLevel - 0.1f;
        }

        /// <summary>
        /// Construção do jogador perto do nó? Evita que o companheiro fique batendo em cima de uma
        /// base (e que o golpe acerte a parede) — a "não quebrar construções" do modo combate.
        /// </summary>
        private static bool HasPlayerBuildNear(Vector3 position)
        {
            int count = Physics.OverlapSphereNonAlloc(
                position, PlayerBuildAvoidRadius, s_buildBuffer, s_buildMask, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                Collider collider = s_buildBuffer[i];
                if (collider == null)
                {
                    continue;
                }

                if (collider.GetComponentInParent<WearNTear>() != null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Há uma construção do jogador entre o companheiro e o ponto? Chamado antes de golpear: se
        /// houver, o golpe é abortado (a hitbox do machado poderia acertar a parede).
        /// </summary>
        internal static bool HasPlayerBuildInFront(Character character, Vector3 point)
        {
            if (character == null)
            {
                return false;
            }

            Vector3 origin = character.transform.position + Vector3.up * 1.2f;
            Vector3 direction = point - origin;
            float distance = direction.magnitude;
            if (distance < 0.01f)
            {
                return false;
            }

            direction /= distance;
            int count = Physics.SphereCastNonAlloc(
                origin, 0.35f, direction, s_rayHits, Mathf.Max(0.5f, distance - 0.4f),
                s_buildMask, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = s_rayHits[i];
                if (hit.collider == null)
                {
                    continue;
                }

                if (hit.collider.GetComponentInParent<WearNTear>() != null)
                {
                    return true;
                }
            }

            return false;
        }

        // ---------------------------------------------------------------------
        // Máscaras e lista negra
        // ---------------------------------------------------------------------

        private static void EnsureMasks()
        {
            if (s_masksBuilt)
            {
                return;
            }

            s_masksBuilt = true;
            s_resourceMask = BuildMask(CompanionHarvest.LayerNames, "Coleta/Camadas");
            s_buildMask = LayerMask.GetMask("piece", "piece_nonsolid");
        }

        private static int BuildMask(string layerNames, string configKey)
        {
            if (string.IsNullOrEmpty(layerNames))
            {
                Plugin.Log.LogWarning($"[ModIA] {configKey} vazio; varrendo todas as camadas.");
                return ~0;
            }

            string[] names = layerNames.Split(',');
            int mask = 0;
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i].Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                int layer = LayerMask.NameToLayer(name);
                if (layer < 0)
                {
                    Plugin.Log.LogWarning(
                        $"[ModIA] Camada '{name}' de {configKey} não existe neste jogo; ignorada.");
                    continue;
                }

                mask |= 1 << layer;
            }

            if (mask == 0)
            {
                Plugin.Log.LogWarning(
                    $"[ModIA] Nenhuma camada válida em {configKey}; varrendo todas as camadas.");
                return ~0;
            }

            return mask;
        }

        private static void PurgeBlacklist(float now)
        {
            if (s_blacklist.Count == 0)
            {
                return;
            }

            s_expired.Clear();
            foreach (KeyValuePair<int, float> entry in s_blacklist)
            {
                if (entry.Value <= now)
                {
                    s_expired.Add(entry.Key);
                }
            }

            for (int i = 0; i < s_expired.Count; i++)
            {
                s_blacklist.Remove(s_expired[i]);
            }

            s_expired.Clear();
        }
    }
}
