using System;
using System.Collections.Generic;
using System.Text;
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

        /// <summary>Destrutível (arbusto/entulho/coto) com drops próprios (<c>Destructible</c>).</summary>
        Destructible,
    }

    /// <summary>
    /// Uma entrada de <c>DropTable</c> já decodificada: item, faixa de stack e o que o sorteio do
    /// jogo faria com ela (<c>peso/total</c>, número de sorteios e chance da tabela). Guardar a
    /// tabela como <b>dados</b> — em vez de depender de um componente vivo — é o que permite estimar
    /// o rendimento de um nó (e o da cascata de toras que ele gera).
    /// </summary>
    internal struct HarvestDrop
    {
        public int ItemHash;
        public int StackMin;
        public int StackMax;
        public float Weight;
        public float WeightTotal;
        public float Rolls;
        public float Chance;
    }

    /// <summary>
    /// Ferramenta que o companheiro pode usar para golpear um nó: tier (a trava dura do jogo,
    /// <c>HitData.CheckToolTier</c>) e o dano por tipo (para cruzar com as resistências do nó).
    /// </summary>
    internal struct HarvestToolProfile
    {
        public int Tier;
        public HitData.DamageTypes Damage;
    }

    /// <summary>
    /// Dados de um <b>prefab</b> de nó (não da instância): tipo, drops, vida, resistências, tier
    /// mínimo de ferramenta e o que ele gera quando é destruído. É o equivalente local do
    /// <c>resourceDatabase</c> do Thrall, mas construído sob demanda (só para os prefabs que
    /// aparecem na varredura) em vez de varrer o mundo inteiro no início do mundo.
    /// </summary>
    internal struct HarvestPrefabInfo
    {
        public HarvestNodeKind Kind;

        /// <summary>Tabela de drops decodificada (base do rendimento estimado).</summary>
        public HarvestDrop[] Drops;

        public float Health;
        public int MinToolTier;
        public HitData.DamageModifiers Modifiers;

        /// <summary>
        /// Prefab gerado quando o nó é destruído (tronco de uma árvore, tora menor de uma tora) e
        /// quantas cópias o jogo instancia. É por aqui que o rendimento real aparece: uma Beech1 não
        /// dropa madeira nenhuma — ela <i>gera</i> as toras, e a madeira está na tabela delas.
        /// </summary>
        public GameObject ChildPrefab;
        public int ChildCount;

        /// <summary>Quantas partes destrutíveis o nó tem (áreas de golpe de pedra/minério).</summary>
        public int Parts;

        /// <summary>
        /// O recurso está na tabela deste prefab? <b>Atenção:</b> não é o mesmo que "o nó rende o
        /// recurso" — a árvore não lista madeira, quem lista são as toras que ela gera (ver
        /// <see cref="CompanionHarvestScanner.EstimateYield"/>). Serve para separar, no diagnóstico,
        /// "não é fonte deste item" de "é fonte, mas o sorteio rende zero".
        /// </summary>
        public bool DropsItem(int resourceHash)
        {
            if (Drops == null)
            {
                return false;
            }

            for (int i = 0; i < Drops.Length; i++)
            {
                if (Drops[i].ItemHash == resourceHash)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Nó escolhido por uma varredura, com tudo o que o log e a IA precisam: qual é, que tipo,
    /// quanto se espera tirar dele, quantos golpes custa e por que ele ganhou.
    /// </summary>
    internal struct HarvestPick
    {
        public GameObject Node;
        public HarvestPrefabInfo Info;

        /// <summary>Unidades do recurso pedido que o nó rende (incluindo a cascata de toras).</summary>
        public float Yield;

        /// <summary>Golpes estimados até quebrar o nó (0 quando a interação é direta).</summary>
        public int Hits;

        /// <summary>Maior tier de ferramenta do companheiro capaz de arranhar este nó (-1: nenhum).</summary>
        public int ToolTier;

        public float Distance;
        public float Score;

        /// <summary>Fonte (precisa de golpe) x direto (colher planta / pegar item do chão).</summary>
        public bool Primary;

        /// <summary>False quando o companheiro não tem ferramenta capaz de fazer dano no nó.</summary>
        public bool Breakable;

        /// <summary>
        /// True quando a varredura encontrou nó(s) do recurso que <b>renderiam</b> mas a ferramenta
        /// do companheiro não arranha (tier abaixo do mínimo / dano 0). Serve para explicar
        /// "pedra não funciona" sem picareta: a IA mostra o aviso em vez de vagar batendo à toa.
        /// </summary>
        public bool NeedsBetterTool;

        /// <summary>Descrição da varredura (candidatos, distância, score), montada pelo scanner.</summary>
        public string Reason;
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
    /// <b>Como escolhe:</b> compara <i>valor × esforço × distância</i> de forma comparável entre
    /// categorias:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><b>valor</b> (50 %): unidades do recurso pedido que o nó rende — a
    ///   tabela de drops do próprio nó <i>mais</i> a cascata do que ele gera ao ser destruído
    ///   (a árvore gera as toras; a tora gera as toras menores; cada área de uma rocha dropa a
    ///   tabela). É o que faz uma árvore (~20 madeiras) ganhar de um galho (1 madeira).</description></item>
    ///   <item><description><b>esforço</b> (20 %): golpes estimados (<c>vida × partes / dano efetivo
    ///   da melhor ferramenta disponível</c>) — o que a ferramenta não consegue arranhar perde
    ///   quase todo o valor.</description></item>
    ///   <item><description><b>distância</b> (30 %): <c>100/(1+d)</c>. É só desempate: perto do
    ///   companheiro sempre há galhos, e com peso alto (como na fórmula antiga) ele nunca saía do
    ///   lugar.</description></item>
    /// </list>
    ///
    /// <para>
    /// <b>O que nunca vira alvo:</b> construções do jogador (<c>WearNTear</c>/<c>Piece</c>), baús e
    /// contêineres (<c>Container</c>), personagens e qualquer coisa sem <see cref="ZNetView"/>
    /// (terreno, rochas do heightmap). Nós dentro d'água, encostados numa construção ou cujo
    /// rendimento estimado do recurso pedido é zero (a viagem não renderia nada) também ficam de
    /// fora.
    /// </para>
    /// </summary>
    internal static class CompanionHarvestScanner
    {
        /// <summary>Tamanho do buffer da varredura (reaproveitado; nunca realocado em jogo).</summary>
        private const int ColliderBufferSize = 512;

        /// <summary>Raio (m) de construção do jogador que desqualifica um nó.</summary>
        private const float PlayerBuildAvoidRadius = 2.5f;

        /// <summary>Categorias contadas na telemetria da varredura (índice = <see cref="HarvestNodeKind"/>).</summary>
        private const int KindCount = (int)HarvestNodeKind.Destructible + 1;

        /// <summary>Profundidade máxima da cascata (árvore → tora → tora menor → …).</summary>
        private const int MaxChainDepth = 3;

        /// <summary>Máximo de cópias de um filho contadas no rendimento (defensivo contra prefabs cíclicos).</summary>
        private const int MaxChildCopies = 4;

        /// <summary>Teto do rendimento estimado (o termo de valor já satura em 10 unidades).</summary>
        private const float MaxYield = 100f;

        /// <summary>Máximo de áreas de golpe contadas como multiplicador de rendimento/esforço.</summary>
        private const int MaxParts = 12;

        /// <summary>Pontos por unidade do recurso (10 unidades saturam o termo de valor).</summary>
        internal const float UnitValue = 10f;

        /// <summary>Teto de cada termo da pontuação (valor, esforço e proximidade entram em 0–100).</summary>
        internal const float TermCeiling = 100f;

        /// <summary>Golpes máximos considerados (evita esforço infinito virando divisão por zero).</summary>
        private const int MaxHits = 50;

        private const float ValueWeight = 0.5f;
        private const float EffortWeight = 0.2f;
        private const float ProximityWeight = 0.3f;

        /// <summary>Nó que a ferramenta disponível não arranha: fica na fila, mas no fim dela.</summary>
        private const float UnbreakablePenalty = 0.1f;

        private static readonly Collider[] s_colliders = new Collider[ColliderBufferSize];
        private static readonly Collider[] s_fallbackColliders = new Collider[ColliderBufferSize];
        private static readonly HashSet<int> s_seen = new HashSet<int>();
        private static readonly Dictionary<int, HarvestPrefabInfo> s_prefabs = new Dictionary<int, HarvestPrefabInfo>();
        private static readonly Dictionary<long, float> s_yields = new Dictionary<long, float>();
        private static readonly Dictionary<int, float> s_blacklist = new Dictionary<int, float>();
        private static readonly List<int> s_expired = new List<int>();
        private static readonly Collider[] s_buildBuffer = new Collider[16];
        private static readonly List<HarvestDrop> s_drops = new List<HarvestDrop>();
        private static readonly RaycastHit[] s_rayHits = new RaycastHit[8];
        private static readonly StringBuilder s_diag = new StringBuilder(512);

        // Telemetria da última varredura (por categoria e por motivo de descarte).
        private static readonly int[] s_kindSeen = new int[KindCount];
        private static readonly int[] s_kindMatched = new int[KindCount];
        private static readonly int[] s_layerSeen = new int[32];
        private static int s_scanColliders;
        private static int s_scanRoots;
        private static int s_scanClassified;
        private static int s_scanBlacklisted;
        private static int s_scanNoDrop;
        private static int s_scanNoResource;
        private static int s_scanNoYield;
        private static int s_scanNotHarvestable;
        private static int s_scanUnderwater;
        private static int s_scanBuild;
        private static int s_scanFar;
        private static int s_scanNoTool;
        private static int s_scanPrimary;
        private static int s_scanFallback;
        private static bool s_scanSaturated;

        private static int s_resourceMask;
        private static int s_buildMask;
        private static bool s_masksBuilt;
        private static bool s_overflowWarned;
        private static WaterVolume s_waterVolume;

        internal static void Shutdown()
        {
            s_prefabs.Clear();
            s_yields.Clear();
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
        /// Procura o melhor nó para o recurso pedido. <paramref name="tools"/> são as ferramentas de
        /// golpe do companheiro (tier + dano): servem para estimar golpes e para identificar o que
        /// ele não consegue arranhar. O <see cref="HarvestPick.Reason"/> sempre sai preenchido.
        /// </summary>
        internal static HarvestPick FindBestNode(
            Vector3 origin,
            string resource,
            float radius,
            bool includeDrops,
            List<HarvestToolProfile> tools)
        {
            HarvestPick pick = default;
            pick.ToolTier = -1;

            if (string.IsNullOrEmpty(resource))
            {
                pick.Reason = "nenhum recurso definido";
                return pick;
            }

            EnsureMasks();
            PurgeBlacklist(Time.time);
            ResetScanStats();

            int resourceHash = resource.GetStableHashCode();
            int count = Physics.OverlapSphereNonAlloc(
                origin, radius, s_colliders, s_resourceMask, QueryTriggerInteraction.Collide);

            s_scanColliders = count;
            s_scanSaturated = count >= s_colliders.Length;
            s_seen.Clear();

            if (count > 0)
            {
                EvaluateCandidates(
                    s_colliders, Mathf.Min(count, s_colliders.Length), origin, radius, resourceHash,
                    includeDrops, tools, ref pick);
            }

            // Buffer cheio: roda uma segunda passada num raio menor, para garantir que os nós
            // vizinhos (os que interessam) entrem na conta. O dedup evita reprocessar nada.
            if (s_scanSaturated)
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

                s_scanColliders += fallbackCount;
                EvaluateCandidates(
                    s_fallbackColliders, Mathf.Min(fallbackCount, s_fallbackColliders.Length), origin,
                    radius, resourceHash, includeDrops, tools, ref pick);
            }

            if (pick.Node == null && s_scanNoTool > 0)
            {
                pick.NeedsBetterTool = true;
            }

            pick.Reason = DescribeScan(resource, radius);

            if (Plugin.LogDetalhado)
            {
                LogDiagnostic(resource, radius, in pick);
            }

            return pick;
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
            List<HarvestToolProfile> tools,
            ref HarvestPick pick)
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

                s_scanRoots++;
                s_layerSeen[node.layer & 31]++;

                if (s_blacklist.ContainsKey(node.GetInstanceID()))
                {
                    s_scanBlacklisted++;
                    continue;
                }

                if (!TryGetInfo(node, out HarvestPrefabInfo info))
                {
                    continue;
                }

                s_scanClassified++;
                s_kindSeen[(int)info.Kind]++;

                if (info.Kind == HarvestNodeKind.ItemDrop && !includeDrops)
                {
                    s_scanNoDrop++;
                    continue;
                }

                // O rendimento do recurso pedido é o que dá sentido à viagem: um nó que não rende
                // nada (tabela sem o item, cascata desconhecida) não vira alvo. É esta conta — e não
                // a tabela do próprio nó — que decide, porque a madeira de uma árvore só aparece nas
                // toras que ela gera.
                float yield = EstimateYield(node, info, resourceHash);
                if (yield <= 0f)
                {
                    if (info.DropsItem(resourceHash))
                    {
                        s_scanNoYield++;
                    }
                    else
                    {
                        s_scanNoResource++;
                    }

                    continue;
                }

                if (!IsHarvestableNow(node, info.Kind))
                {
                    s_scanNotHarvestable++;
                    continue;
                }

                Vector3 position = node.transform.position;
                if (IsUnderwater(position))
                {
                    s_scanUnderwater++;
                    continue;
                }

                if (NeedsTool(info.Kind) && HasPlayerBuildNear(position))
                {
                    s_scanBuild++;
                    continue;
                }

                float distance = Vector3.Distance(position, origin);
                if (distance > radius)
                {
                    s_scanFar++;
                    continue;
                }

                s_kindMatched[(int)info.Kind]++;

                int toolTier = -1;
                float damage = NeedsTool(info.Kind)
                    ? BestToolDamage(info, tools, out toolTier)
                    : 0f;
                bool breakable = !NeedsTool(info.Kind) ||
                                 (damage > 0f && toolTier >= info.MinToolTier);

                // Nó que o companheiro NÃO consegue arranhar não vira alvo: sem isso ele caminha
                // até a rocha, bate sem fazer dano, trava por 7 s e a veta — o sintoma de "pedra
                // não funciona" quando falta picareta. Aqui ele é contado e a IA avisa o jogador.
                if (NeedsTool(info.Kind) && !breakable)
                {
                    s_scanNoTool++;
                    continue;
                }

                int hits = EstimateHits(info, damage, breakable);
                float score = CalculateScore(yield, hits, distance, breakable);

                if (NeedsTool(info.Kind) && breakable)
                {
                    s_scanPrimary++;
                }
                else
                {
                    s_scanFallback++;
                }

                if (pick.Node != null && score <= pick.Score)
                {
                    continue;
                }

                pick.Node = node;
                pick.Info = info;
                pick.Yield = yield;
                pick.Hits = hits;
                pick.ToolTier = toolTier;
                pick.Distance = distance;
                pick.Score = score;
                pick.Primary = NeedsTool(info.Kind);
                pick.Breakable = breakable;
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

            // Mesma pergunta da varredura: este nó ainda rende o recurso pedido? (o rendimento é
            // cacheado por prefab + recurso, então isto é um lookup).
            return EstimateYield(node, info, resource.GetStableHashCode()) > 0f &&
                   IsHarvestableNow(node, info.Kind);
        }

        /// <summary>Tamanho do stack de um <c>ItemDrop</c> no chão (1 se não der para ler).</summary>
        internal static int GetDropStack(GameObject node)
        {
            ItemDrop drop = node != null ? node.GetComponent<ItemDrop>() : null;
            return drop != null && drop.m_itemData != null ? Mathf.Max(1, drop.m_itemData.m_stack) : 1;
        }

        /// <summary>Buffer reaproveitado da busca de colliders de um nó (nunca alocado em jogo).</summary>
        private static readonly List<Collider> s_colliderBuffer = new List<Collider>(16);

        /// <summary>
        /// Ponto de mira/golpe do nó. Para <b>rochas e entulho</b> (<see cref="MineRock"/>,
        /// <see cref="MineRock5"/>, <see cref="Destructible"/>) é o ponto mais próximo na superfície
        /// de uma <b>área de golpe ativa</b>: a raiz de uma rocha grande fica no centro (longe da
        /// hitbox do golpe), então mirar em <c>transform.position + up</c> fazia o companheiro bater
        /// no ar — era um dos motivos de "pedra não funciona". Para os demais nós (árvore, tora,
        /// planta, item) é a posição do nó um pouco acima.
        /// </summary>
        internal static Vector3 GetAimPoint(GameObject node, in HarvestPrefabInfo info, Vector3 from)
        {
            if (node == null)
            {
                return from;
            }

            if (info.Kind == HarvestNodeKind.MineRock ||
                info.Kind == HarvestNodeKind.MineRock5 ||
                info.Kind == HarvestNodeKind.Destructible)
            {
                Collider closest = FindClosestActiveCollider(node, from);
                if (closest != null)
                {
                    Vector3 point = ClosestPoint(closest, from);
                    point.y = Mathf.Max(point.y, from.y + 0.2f);
                    return point;
                }
            }

            return node.transform.position + Vector3.up * 1f;
        }

        /// <summary>
        /// Collider ativo (não-trigger) do nó mais próximo de <paramref name="from"/>. Usa o buffer
        /// estático (o overload de <c>List</c> do <c>GetComponentsInChildren</c>): nada é alocado por
        /// tick. O nó é varrido a cada golpe porque as áreas de uma <see cref="MineRock5"/> são
        /// desativadas conforme são destruídas — a mira precisa acompanhar.
        /// </summary>
        private static Collider FindClosestActiveCollider(GameObject node, Vector3 from)
        {
            s_colliderBuffer.Clear();
            node.GetComponentsInChildren(s_colliderBuffer);

            Collider best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < s_colliderBuffer.Count; i++)
            {
                Collider collider = s_colliderBuffer[i];
                if (collider == null || collider.isTrigger || !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                float distance = Vector3.Distance(collider.bounds.center, from);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = collider;
                }
            }

            return best;
        }

        private static Vector3 ClosestPoint(Collider collider, Vector3 point)
        {
            // MeshCollider não-convexo não tem ClosestPoint confiável em runtime: usa os bounds.
            if (collider is MeshCollider mesh && !mesh.convex)
            {
                return collider.ClosestPointOnBounds(point);
            }

            return collider.ClosestPoint(point);
        }

        // ---------------------------------------------------------------------
        // Rendimento (o que o nó realmente entrega)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Unidades do recurso pedido que o nó entrega, <b>incluindo a cascata que ele gera</b>: a
        /// tabela do próprio nó, as cópias que ele instancia quando é destruído (tronco de árvore,
        /// toras menores) e as áreas de golpe (rochas/minérios). O resultado por prefab+recurso fica
        /// em cache — a varredura seguinte só faz um lookup.
        /// </summary>
        internal static float EstimateYield(GameObject node, HarvestPrefabInfo info, int resourceHash)
        {
            if (info.Kind == HarvestNodeKind.ItemDrop)
            {
                // Uma pilha no chão entrega o stack inteiro numa única interação.
                return GetDropStack(node);
            }

            string prefabName = node != null ? Utils.GetPrefabName(node.name) : string.Empty;
            long key = MakeYieldKey(prefabName.GetStableHashCode(), resourceHash);
            if (s_yields.TryGetValue(key, out float cached))
            {
                return cached;
            }

            float yield = EstimateChain(info, resourceHash, MaxChainDepth);
            s_yields[key] = yield;
            return yield;
        }

        private static float EstimateChain(HarvestPrefabInfo info, int resourceHash, int depth)
        {
            float total = TableYield(info, resourceHash);
            if (total > 0f && info.Parts > 1)
            {
                total *= info.Parts;
            }

            if (depth <= 0 || total >= MaxYield ||
                info.ChildPrefab == null || info.ChildCount <= 0)
            {
                return Mathf.Min(total, MaxYield);
            }

            if (TryGetInfo(info.ChildPrefab, out HarvestPrefabInfo child) &&
                child.Kind != HarvestNodeKind.None)
            {
                int copies = Mathf.Clamp(info.ChildCount, 1, MaxChildCopies);
                float childYield = EstimateChain(child, resourceHash, depth - 1);
                if (childYield > 0f)
                {
                    total += childYield * copies;
                }
            }

            return Mathf.Min(total, MaxYield);
        }

        /// <summary>
        /// Unidades esperadas do recurso a partir de uma tabela de drops, do jeito que o jogo
        /// sorteia (<c>DropTable.GetDropList</c>): <c>chance × sorteios × (peso/total) × stack</c>,
        /// multiplicado pelo <c>Game.m_resourceRate</c> do mundo (é o que o jogo faz).
        /// </summary>
        private static float TableYield(HarvestPrefabInfo info, int resourceHash)
        {
            if (info.Drops == null || info.Drops.Length == 0)
            {
                return 0f;
            }

            float rate = Game.m_resourceRate > 0f ? Game.m_resourceRate : 1f;
            float expected = 0f;

            for (int i = 0; i < info.Drops.Length; i++)
            {
                HarvestDrop drop = info.Drops[i];
                if (drop.ItemHash != resourceHash)
                {
                    continue;
                }

                float totalWeight = drop.WeightTotal > 0f ? drop.WeightTotal : 1f;
                float weight = drop.WeightTotal > 0f ? Mathf.Max(0f, drop.Weight) : 1f;
                int stackMin = Mathf.Max(1, drop.StackMin);
                int stackMax = Mathf.Max(stackMin, drop.StackMax);
                float stack = (stackMin + stackMax) * 0.5f;
                float rolls = drop.Rolls > 0f ? drop.Rolls : 1f;

                expected += (weight / totalWeight) * stack * rolls * Mathf.Clamp01(drop.Chance);
            }

            return expected * rate;
        }

        private static long MakeYieldKey(int prefabHash, int resourceHash)
        {
            return ((long)prefabHash << 32) | (uint)resourceHash;
        }

        // ---------------------------------------------------------------------
        // Ferramenta e pontuação
        // ---------------------------------------------------------------------

        /// <summary>
        /// Maior dano efetivo que o companheiro consegue aplicar neste nó com uma das ferramentas de
        /// <paramref name="tools"/> (0 = nenhuma serve). Ferramenta com tier abaixo do mínimo do nó é
        /// descartada: é a trava dura do jogo (<c>HitData.CheckToolTier</c>).
        /// </summary>
        private static float BestToolDamage(
            HarvestPrefabInfo info,
            List<HarvestToolProfile> tools,
            out int bestTier)
        {
            bestTier = -1;
            if (tools == null)
            {
                return 0f;
            }

            float best = 0f;
            for (int i = 0; i < tools.Count; i++)
            {
                HarvestToolProfile tool = tools[i];
                if (tool.Tier < info.MinToolTier)
                {
                    continue;
                }

                float damage = EffectiveDamage(tool.Damage, info.Modifiers);
                if (damage > best)
                {
                    best = damage;
                    bestTier = tool.Tier;
                }
            }

            return best;
        }

        /// <summary>
        /// Golpes estimados até quebrar o nó: vida total (vida × partes) dividida pelo dano efetivo
        /// por golpe. Sem ferramenta capaz o esforço vai ao teto e o nó perde quase todo o score.
        /// </summary>
        private static int EstimateHits(HarvestPrefabInfo info, float damage, bool breakable)
        {
            if (!NeedsTool(info.Kind))
            {
                return 0;
            }

            if (!breakable || damage <= 0f)
            {
                return MaxHits;
            }

            float health = Mathf.Max(1f, info.Health) * Mathf.Max(1, info.Parts);
            return Mathf.Clamp(Mathf.CeilToInt(health / damage), 1, MaxHits);
        }

        /// <summary>
        /// Pontuação comparável entre categorias: <b>valor</b> (unidades do recurso pedido, 50 %),
        /// <b>esforço</b> (golpes estimados, 20 %) e <b>proximidade</b> (30 %), os três na mesma
        /// escala (0–100). É o que corrige o vício da fórmula herdada do Thrall: lá
        /// <c>100/vida</c> valia 100 pontos de graça para qualquer galho (vida 1) e árvores/pedras
        /// (vida alta) perdiam mesmo rendendo 20× mais.
        /// </summary>
        internal static float CalculateScore(float expectedYield, int hits, float distance, bool breakable)
        {
            float value = Mathf.Min(expectedYield * UnitValue, TermCeiling);
            float effort = TermCeiling / (1f + Mathf.Max(0, hits));
            float proximity = TermCeiling / (1f + Mathf.Max(0f, distance));

            float score = value * ValueWeight + effort * EffortWeight + proximity * ProximityWeight;
            return breakable ? score : score * UnbreakablePenalty;
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

        /// <summary>
        /// Dano efetivo de um conjunto de dano contra as resistências do nó (a mesma tabela do
        /// <c>HitData.ApplyModifier</c>). Fonte única da conta: a escolha da ferramenta
        /// (<see cref="CompanionInventory.TryEquipBestHarvestTool"/>) usa esta função também.
        /// </summary>
        internal static float EffectiveDamage(HitData.DamageTypes damage, HitData.DamageModifiers modifiers)
        {
            float total = 0f;
            total += damage.m_blunt * ModifierValue(modifiers.m_blunt);
            total += damage.m_slash * ModifierValue(modifiers.m_slash);
            total += damage.m_pierce * ModifierValue(modifiers.m_pierce);
            total += damage.m_chop * ModifierValue(modifiers.m_chop);
            total += damage.m_pickaxe * ModifierValue(modifiers.m_pickaxe);
            return total;
        }

        internal static float ModifierValue(HitData.DamageModifier modifier)
        {
            switch (modifier)
            {
                case HitData.DamageModifier.SlightlyResistant:
                    return 0.75f;
                case HitData.DamageModifier.Resistant:
                    return 0.5f;
                case HitData.DamageModifier.VeryResistant:
                    return 0.25f;
                case HitData.DamageModifier.SlightlyWeak:
                    return 1.25f;
                case HitData.DamageModifier.Weak:
                    return 1.5f;
                case HitData.DamageModifier.VeryWeak:
                    return 2f;
                case HitData.DamageModifier.Immune:
                case HitData.DamageModifier.Ignore:
                    return 0f;
                default:
                    return 1f;
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
        /// Classifica um prefab pela ordem de prioridade das categorias e decodifica drops/vida/
        /// resistências/tier de ferramenta/cascata de destruição. O resultado é cacheado por nome de
        /// prefab (o cache é do prefab, não da instância — é o papel que o <c>resourceDatabase</c>
        /// do Thrall fazia, só que sob demanda em vez de varrer o mundo inteiro no início do mundo).
        /// </summary>
        private static HarvestPrefabInfo BuildInfo(GameObject node)
        {
            HarvestPrefabInfo info = default;
            s_drops.Clear();

            bool classified =
                TryClassifyPickable(node, ref info) ||
                TryClassifyTreeBase(node, ref info) ||
                TryClassifyTreeLog(node, ref info) ||
                TryClassifyMineRock(node, ref info) ||
                TryClassifyMineRock5(node, ref info) ||
                TryClassifyDestructible(node, ref info) ||
                TryClassifyItemDrop(node, ref info);

            if (!classified)
            {
                info.Kind = HarvestNodeKind.None;
            }

            info.Drops = s_drops.Count > 0 ? s_drops.ToArray() : null;

            s_drops.Clear();
            return info;
        }

        private static bool TryClassifyPickable(GameObject node, ref HarvestPrefabInfo info)
        {
            Pickable pickable = node.GetComponent<Pickable>();
            if (pickable == null)
            {
                return false;
            }

            info.Kind = HarvestNodeKind.Pickable;
            info.Health = 1f;
            info.Parts = 1;

            // Colher é garantido (não depende de sorteio): o item da planta entra como uma
            // "tabela" de 1 sorteio com chance 1; m_extraDrops entra com os parâmetros dele.
            int amount = Mathf.Max(1, pickable.m_amount);
            AddDrop(pickable.m_itemPrefab, amount, amount, 1f, 1f, 1f, 1f);
            AddDropTable(pickable.m_extraDrops);
            return true;
        }

        private static bool TryClassifyTreeBase(GameObject node, ref HarvestPrefabInfo info)
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
            info.Parts = 1;
            AddDropTable(tree.m_dropWhenDestroyed);

            // A madeira de uma árvore NÃO está na tabela dela: está nas toras que ela gera ao cair
            // (o jogo instancia m_logPrefab uma vez, em SpawnLog). Sem seguir esse filho, uma Beech1
            // não é candidata a "Wood" — e era exatamente esse o bug da coleta de madeira.
            info.ChildPrefab = tree.m_logPrefab;
            info.ChildCount = 1;
            return true;
        }

        private static bool TryClassifyTreeLog(GameObject node, ref HarvestPrefabInfo info)
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
            info.Parts = 1;
            AddDropTable(log.m_dropWhenDestroyed);

            // Tora partida gera as toras menores (uma por ponto), cada uma com a própria tabela.
            info.ChildPrefab = log.m_subLogPrefab;
            info.ChildCount = log.m_subLogPoints != null ? log.m_subLogPoints.Length : 0;
            return true;
        }

        private static bool TryClassifyMineRock(GameObject node, ref HarvestPrefabInfo info)
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
            info.Parts = CountParts(node);
            AddDropTable(rock.m_dropItems);
            return true;
        }

        private static bool TryClassifyMineRock5(GameObject node, ref HarvestPrefabInfo info)
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
            info.Parts = CountParts(node);
            AddDropTable(rock.m_dropItems);
            return true;
        }

        private static bool TryClassifyDestructible(GameObject node, ref HarvestPrefabInfo info)
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
            info.Parts = 1;

            // Os drops de um Destructible (coto, arbusto, entulho) moram num DropOnDestroyed ligado
            // ao m_onDestroyed dele. A busca inclui filhos inativos porque o mesmo caminho é usado no
            // carimbo de prefab do ZNetScene (que não passou por Awake/SetActive).
            DropOnDestroyed drops = FindDropOnDestroyed(node);
            if (drops != null)
            {
                AddDropTable(drops.m_dropWhenDestroyed);
            }

            return true;
        }

        private static bool TryClassifyItemDrop(GameObject node, ref HarvestPrefabInfo info)
        {
            ItemDrop item = node.GetComponent<ItemDrop>();
            if (item == null)
            {
                return false;
            }

            info.Kind = HarvestNodeKind.ItemDrop;
            info.Health = 1f;
            info.Parts = 1;

            // m_dropPrefab é preenchido no ItemDrop.Awake a partir do ObjectDB; sem ele cai para o
            // próprio nome do objeto (que é o nome do item, com "(Clone)" cortado pelo Utils).
            GameObject dropPrefab = item.m_itemData != null ? item.m_itemData.m_dropPrefab : null;
            if (dropPrefab == null)
            {
                dropPrefab = node;
            }

            AddDrop(dropPrefab, 1, 1, 1f, 1f, 1f, 1f);
            return true;
        }

        private static DropOnDestroyed FindDropOnDestroyed(GameObject node)
        {
            DropOnDestroyed direct = node.GetComponent<DropOnDestroyed>();
            if (direct != null)
            {
                return direct;
            }

            DropOnDestroyed[] children = node.GetComponentsInChildren<DropOnDestroyed>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] != null && children[i].m_dropWhenDestroyed != null)
                {
                    return children[i];
                }
            }

            return null;
        }

        /// <summary>Áreas de golpe do nó (uma rocha dropa a tabela uma vez por área destruída).</summary>
        private static int CountParts(GameObject node)
        {
            Collider[] colliders = node.GetComponentsInChildren<Collider>(true);
            int count = colliders != null ? colliders.Length : 1;
            return Mathf.Clamp(count, 1, MaxParts);
        }

        /// <summary>
        /// Decodifica uma <c>DropTable</c> e acrescenta as entradas à tabela do prefab, guardando os
        /// parâmetros de sorteio da tabela (<c>m_dropMin/m_dropMax/m_dropChance</c> e o peso total).
        /// </summary>
        private static void AddDropTable(DropTable table)
        {
            if (table == null || table.m_drops == null || table.m_drops.Count == 0)
            {
                return;
            }

            List<DropTable.DropData> drops = table.m_drops;
            float totalWeight = 0f;
            for (int i = 0; i < drops.Count; i++)
            {
                totalWeight += Mathf.Max(0f, drops[i].m_weight);
            }

            if (totalWeight <= 0f)
            {
                // Tabela com todos os pesos zerados não é sorteável: trata cada entrada como igual.
                totalWeight = drops.Count;
            }

            float rolls = (Mathf.Max(0, table.m_dropMin) + Mathf.Max(0, table.m_dropMax)) * 0.5f;
            float chance = Mathf.Clamp01(table.m_dropChance);

            for (int i = 0; i < drops.Count; i++)
            {
                DropTable.DropData drop = drops[i];
                AddDrop(drop.m_item, drop.m_stackMin, drop.m_stackMax, drop.m_weight, totalWeight, rolls, chance);
            }
        }

        private static void AddDrop(
            GameObject prefab,
            int stackMin,
            int stackMax,
            float weight,
            float weightTotal,
            float rolls,
            float chance)
        {
            if (prefab == null || stackMax <= 0)
            {
                return;
            }

            HarvestDrop entry = default;
            entry.ItemHash = Utils.GetPrefabName(prefab.name).GetStableHashCode();
            entry.StackMin = Mathf.Max(0, stackMin);
            entry.StackMax = Mathf.Max(0, stackMax);
            entry.Weight = weight;
            entry.WeightTotal = weightTotal;
            entry.Rolls = rolls;
            entry.Chance = chance;
            s_drops.Add(entry);
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
        // Diagnóstico (Coleta/LogDetalhado)
        // ---------------------------------------------------------------------

        private static void ResetScanStats()
        {
            s_scanColliders = 0;
            s_scanRoots = 0;
            s_scanClassified = 0;
            s_scanBlacklisted = 0;
            s_scanNoDrop = 0;
            s_scanNoResource = 0;
            s_scanNoYield = 0;
            s_scanNotHarvestable = 0;
            s_scanUnderwater = 0;
            s_scanBuild = 0;
            s_scanFar = 0;
            s_scanNoTool = 0;
            s_scanPrimary = 0;
            s_scanFallback = 0;
            s_scanSaturated = false;
            Array.Clear(s_kindSeen, 0, s_kindSeen.Length);
            Array.Clear(s_kindMatched, 0, s_kindMatched.Length);
            Array.Clear(s_layerSeen, 0, s_layerSeen.Length);
        }

        /// <summary>Resumo curto da varredura para a linha de log do alvo (candidatos do recurso).</summary>
        private static string DescribeScan(string resource, float radius)
        {
            int matched = s_scanPrimary + s_scanFallback;
            if (matched == 0)
            {
                if (s_scanNoTool > 0)
                {
                    return $"{s_scanNoTool} nó(s) de '{resource}' no raio de {radius:0} m, mas o " +
                           "companheiro não tem ferramenta capaz (precisa de machado/picareta de " +
                           "tier suficiente; veja modia_inv e modia_equip)";
                }

                return s_scanClassified == 0
                    ? $"nenhum nó de coleta no raio de {radius:0} m ({s_scanColliders} collider(s) " +
                      "varrido(s); confira Coleta/Camadas se o recurso existe por perto)"
                    : $"nenhum nó de '{resource}' utilizável no raio de {radius:0} m " +
                      $"({s_scanClassified} nó(s) classificados; " +
                      $"{s_scanNoResource} sem o recurso, {s_scanNoYield} sem rendimento, " +
                      $"{s_scanNotHarvestable} esgotado(s), {s_scanBuild} perto de obra, " +
                      $"{s_scanUnderwater} submerso(s), {s_scanBlacklisted} vetado(s))";
            }

            s_diag.Length = 0;
            s_diag.Append(matched).Append(" candidato(s)");
            if (s_scanPrimary > 0)
            {
                s_diag.Append(", ").Append(s_scanPrimary).Append(" fonte(s)");
            }

            if (s_kindMatched[(int)HarvestNodeKind.ItemDrop] > 0)
            {
                s_diag.Append(", ").Append(s_kindMatched[(int)HarvestNodeKind.ItemDrop]).Append(" no chão");
            }

            if (s_scanSaturated)
            {
                s_diag.Append(" [buffer cheio: raio reduzido nesta passada]");
            }

            return s_diag.ToString();
        }

        /// <summary>
        /// Linha de diagnóstico completa (só com <c>Geral/LogDetalhado = true</c>): quantos nós de cada
        /// categoria entraram na varredura, quantos casaram com o recurso pedido e por que os outros
        /// foram descartados. É o que responde "as árvores nem entram na conta?".
        /// </summary>
        private static void LogDiagnostic(string resource, float radius, in HarvestPick pick)
        {
            s_diag.Length = 0;
            s_diag.Append("[ModIA] Coleta[diag] '").Append(resource).Append("' raio ")
                .Append(radius.ToString("0")).Append(" m: colliders=").Append(s_scanColliders)
                .Append('/').Append(ColliderBufferSize)
                .Append(s_scanSaturated ? " (saturado)" : string.Empty)
                .Append(", nós=").Append(s_scanRoots)
                .Append(", classificados=").Append(s_scanClassified)
                .Append(" | vistos:");
            AppendKindBreakdown(s_kindSeen);
            s_diag.Append(" nas camadas:");
            AppendLayerBreakdown();
            s_diag.Append(" | casam com o recurso:")
                .Append(' ').Append(s_scanPrimary + s_scanFallback)
                .Append(" (fonte ").Append(s_scanPrimary)
                .Append(", sobra ").Append(s_scanFallback).Append(");");
            AppendKindBreakdown(s_kindMatched);
            s_diag.Append(" | descartes: veto=").Append(s_scanBlacklisted)
                .Append(", drop desligado=").Append(s_scanNoDrop)
                .Append(", sem o recurso=").Append(s_scanNoResource)
                .Append(", sem rendimento=").Append(s_scanNoYield)
                .Append(", esgotado=").Append(s_scanNotHarvestable)
                .Append(", sem ferramenta=").Append(s_scanNoTool)
                .Append(", submerso=").Append(s_scanUnderwater)
                .Append(", perto de obra=").Append(s_scanBuild)
                .Append(", longe=").Append(s_scanFar)
                .Append(" | escolhido: ");

            if (pick.Node == null)
            {
                s_diag.Append("nenhum");
            }
            else
            {
                s_diag.Append('\'').Append(pick.Node.name).Append("' (").Append(pick.Info.Kind)
                    .Append(", camada '").Append(LayerMask.LayerToName(pick.Node.layer)).Append('\'')
                    .Append(", tier ferramenta ").Append(pick.ToolTier)
                    .Append('/').Append(pick.Info.MinToolTier)
                    .Append(", ~").Append(pick.Yield.ToString("0.#")).Append(" unidade(s)")
                    .Append(", ").Append(pick.Hits).Append(" golpe(s)")
                    .Append(pick.Breakable ? string.Empty : ", SEM ferramenta capaz")
                    .Append(") score ").Append(pick.Score.ToString("0.#"));
            }

            Plugin.Log.LogInfo(s_diag.ToString());
        }

        private static void AppendKindBreakdown(int[] counts)
        {
            s_diag.Append(" { ");
            bool first = true;
            for (int i = 1; i < counts.Length; i++)
            {
                if (counts[i] <= 0)
                {
                    continue;
                }

                if (!first)
                {
                    s_diag.Append(", ");
                }

                s_diag.Append((HarvestNodeKind)i).Append('=').Append(counts[i]);
                first = false;
            }

            if (first)
            {
                s_diag.Append("—");
            }

            s_diag.Append(" }");
        }

        /// <summary>
        /// Camadas das raízes encontradas na varredura. É o que fecha a dúvida "o nó nem entra na
        /// conta?" quando a categoria aparece zerada: se a camada das árvores não está aqui, o
        /// problema é a config <c>Coleta/Camadas</c>, não a classificação.
        /// </summary>
        private static void AppendLayerBreakdown()
        {
            s_diag.Append(" { ");
            bool first = true;
            for (int i = 0; i < s_layerSeen.Length; i++)
            {
                if (s_layerSeen[i] <= 0)
                {
                    continue;
                }

                if (!first)
                {
                    s_diag.Append(", ");
                }

                string name = LayerMask.LayerToName(i);
                s_diag.Append(string.IsNullOrEmpty(name) ? i.ToString() : name)
                    .Append('=').Append(s_layerSeen[i]);
                first = false;
            }

            if (first)
            {
                s_diag.Append("—");
            }

            s_diag.Append(" }");
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
