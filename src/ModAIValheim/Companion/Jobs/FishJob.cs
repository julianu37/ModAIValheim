using System.Collections.Generic;
using UnityEngine;

namespace ModAIValheim.Companion.Jobs
{
    /// <summary>
    /// <b>TRABALHO: PESCAR</b> (Fase 2.16a) — o companheiro vai a uma margem próxima, equipa a
    /// <b>vara</b> e a <b>isca</b> do inventário, arremessa no peixe, espera a fisgada, puxa e guarda
    /// o peixe; e repete até a quantidade pedida ou até não haver mais peixe no raio.
    ///
    /// <para>
    /// <b>Pesca de verdade:</b> quem pesca é o <b>flutuador do próprio jogo</b>
    /// (<c>FishingFloat</c>) — o peixe (<c>Fish</c>) nada até ele, morde, é fisgado, luta, gasta
    /// stamina e pode arrebentar a linha, com as regras do vanilla (a perícia de pesca inclusive:
    /// puxar fica mais rápido e mais barato conforme ele treina). O trabalho só decide
    /// <b>onde</b> pescar, <b>o que</b> arremessar e <b>quando</b> puxar (a "mão" no mouse do
    /// jogador é o bloqueio, como no <c>FishController</c> da referência).
    /// </para>
    ///
    /// <para>
    /// <b>O que o vanilla não fazia:</b> o <c>FishingFloat</c> só conhece jogadores (procura o dono
    /// na lista de jogadores pelo <i>user id</i>), então para o companheiro ele era destruído na
    /// hora. Os patches de <see cref="CompanionFishPatches"/> dão ao flutuador do companheiro uma
    /// chave própria no ZDO, resolvem o dono por ela e devolvem a isca a ele quando a linha volta
    /// sem peixe.
    /// </para>
    ///
    /// <para>
    /// Adaptado (lógica) de <c>src/Companions/Jobs/FishController.cs</c> do <i>Offline Companions
    /// Reborn</i> (MIT — ver <c>THIRD_PARTY_NOTICES.md</c>): os mesmos estados
    /// (mover → arremessar → esperar → puxar), a mesma escolha de isca pela chance
    /// (<c>Fish.m_baits</c>), o mesmo "descansa antes de arremessar" por stamina, a mesma força de
    /// arremesso aprendida de onde o flutuador caiu (<c>_castScale</c>) e o mesmo puxar
    /// alternado (larga quando o peixe foge ou a linha tensiona, puxa quando ele cansa). A
    /// implementação aqui é própria, adaptada ao modelo de camadas do <see cref="CompanionAI"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Fora do núcleo (2.16b):</b> o mapeamento de <i>todos</i> os pontos de margem da região
    /// (<c>MapShore</c>, uma grade de raios em volta do local) e o "caminhar para casa" com a carga
    /// cheia. Aqui a margem é procurada <b>em volta do peixe escolhido</b>, com linha de visão e
    /// espaço para ficar de pé — que é o que o núcleo funcional precisa; o depósito da carga
    /// continua sendo o da Fase 2.14 (o companheiro cheio deposita e volta, sem o trabalho precisar
    /// saber disso).
    /// </para>
    /// </summary>
    internal sealed class FishJob : CompanionJob
    {
        private enum FishState
        {
            /// <summary>Procurando um peixe (e uma margem) para arremessar.</summary>
            Scanning,

            /// <summary>Andando até o ponto de pesca.</summary>
            Moving,

            /// <summary>No ponto: equipa a vara/isca, descansa e arremessa.</summary>
            Casting,

            /// <summary>Flutuador na água, esperando a fisgada.</summary>
            Waiting,

            /// <summary>Peixe fisgado (ou recolhendo a linha).</summary>
            Reeling,
        }

        /// <summary>Tempo (s) máximo andando até o ponto de pesca.</summary>
        private const float MoveTimeout = 60f;

        /// <summary>Sem reduzir a distância ao ponto por este tempo, desiste dele.</summary>
        private const float MoveStuckSeconds = 12f;

        /// <summary>Teto (s) do puxão de um peixe (nunca fica preso na linha).</summary>
        private const float ReelTimeout = 90f;

        /// <summary>Varreduras vazias seguidas antes de considerar a pesca encerrada.</summary>
        private const int EmptyScansBeforeStop = 3;

        /// <summary>Tensão (m) da linha acima da qual ele larga o puxão (o peixe está fugindo).</summary>
        private const float MaxTension = 2.5f;

        private static WaterVolume s_waterVolume;
        private static int s_solidMask = -1;
        private static int s_standMask = -1;

        private FishState _state = FishState.Scanning;

        private float _scanTimer;
        private float _stateSince;

        private Fish _fish;
        private string _fishName = string.Empty;
        private Vector3 _stand;
        private string _bait;

        private float _castAt;
        private int _castTries;
        private bool _settled;
        private bool _retrieving;
        private float _castScale = 24f;
        private float _hookTry;

        private Fish _hooked;
        private string _hookedName;
        private string _hookedHover;
        private int _haveBefore;

        private float _bestDistance;
        private float _noProgress;
        private int _emptyScans;

        private Transform _rodTop;

        /// <summary>Presa largada (id → momento em que volta a valer).</summary>
        private readonly Dictionary<int, float> _skip = new Dictionary<int, float>();

        internal override JobKind Kind => JobKind.Fish;

        internal override string DisplayName => "Pescar";

        internal override string CommandName => "fish";

        protected override bool OnStart(CompanionAI companion, out string error)
        {
            error = null;

            if (companion.JobHumanoid == null)
            {
                error = "[ModIA] O companheiro esta sem corpo/inventario; pescar cancelado.";
                return false;
            }

            if (!CompanionInventory.EnsureFishingGear(
                    companion,
                    CompanionJobsConfig.RodPrefab,
                    CompanionJobsConfig.BaitPrefab,
                    CompanionJobsConfig.BaitAmount,
                    CompanionJobsConfig.ProvideRod,
                    CompanionJobsConfig.ProvideBait,
                    out string missing))
            {
                error =
                    $"[ModIA] Nao da para pescar: {missing}. De a ele uma vara/isca " +
                    "(modia_equip FishingRod) ou ligue Jobs/FornecerVara e Jobs/FornecerIsca.";
                return false;
            }

            _state = FishState.Scanning;
            _scanTimer = 0f;
            _stateSince = Time.time;
            _fish = null;
            _fishName = string.Empty;
            _bait = null;
            _castTries = 0;
            _castScale = 24f;
            _hooked = null;
            _hookedName = null;
            _skip.Clear();
            _rodTop = null;

            SetStatus("procurando peixe");
            return true;
        }

        protected override void OnTick(CompanionAI companion, float dt)
        {
            // Prioridade: o combate e o loot vencem o trabalho. Um inimigo atacando no meio da pesca
            // manda ele largar a linha (o flutuador se desfaz sozinho quando ele saca a vara).
            if (companion.JobHasCombatTarget || CompanionLoot.HasWork)
            {
                if (_state != FishState.Scanning)
                {
                    LogEvent(
                        "fish|pause",
                        "[ModIA] Pescar: pausando a pesca (combate/loot no comando); a vara volta " +
                        "para as costas e ele retoma depois.");
                }

                Pause(companion, companion.JobHasCombatTarget ? "combate" : "loot");
                SetStatus(companion.JobHasCombatTarget ? "combate" : "catando o abate");
                return;
            }

            switch (_state)
            {
                case FishState.Moving:
                    TickMoving(companion, dt);
                    break;

                case FishState.Casting:
                    TickCasting(companion, dt);
                    break;

                case FishState.Waiting:
                    TickWaiting(companion);
                    break;

                case FishState.Reeling:
                    TickReeling(companion);
                    break;

                default:
                    TickScanning(companion, dt);
                    break;
            }
        }

        protected override void OnStop(CompanionAI companion, string reason)
        {
            if (companion != null)
            {
                ReelOff(companion);
                RestoreGear(companion);
            }

            _fish = null;
            _hooked = null;
            _hookedName = null;
            _skip.Clear();
            CompanionFishingFloat.LastBait = null;
            SetStatus("encerrado");
        }

        // ---------------------------------------------------------------------
        // Procurando peixe
        // ---------------------------------------------------------------------

        private void TickScanning(CompanionAI companion, float dt)
        {
            _scanTimer -= dt;
            if (_scanTimer > 0f)
            {
                SetStatus("procurando peixe");
                return;
            }

            _scanTimer = CompanionJobsConfig.ScanInterval;

            // Vara/isca: a isca é consumida a cada arremesso, então isto repõe quando acabar.
            if (!CompanionInventory.EnsureFishingGear(
                    companion,
                    CompanionJobsConfig.RodPrefab,
                    CompanionJobsConfig.BaitPrefab,
                    CompanionJobsConfig.BaitAmount,
                    CompanionJobsConfig.ProvideRod,
                    CompanionJobsConfig.ProvideBait,
                    out string missing))
            {
                Finish(companion, missing ?? "sem vara/isca");
                return;
            }

            if (!PickTarget(companion, out string why))
            {
                _emptyScans++;
                LogEvent(
                    "fish|empty|" + why,
                    $"[ModIA] Pescar: nada para pescar ({why}); varredura {_emptyScans}/{EmptyScansBeforeStop}.");

                if (_emptyScans >= EmptyScansBeforeStop)
                {
                    Finish(companion, $"acabou a pesca: {why}");
                    return;
                }

                SetStatus("procurando peixe");
                return;
            }

            _emptyScans = 0;
            _castTries = 0;
            _bestDistance = float.MaxValue;
            _noProgress = 0f;
            _state = FishState.Moving;
            _stateSince = Time.time;
            SetStatus($"indo pescar {_fishName}");

            LogEvent(
                "fish|target|" + _fishName,
                $"[ModIA] Pescar: indo atras de '{_fishName}' a " +
                $"{Vector3.Distance(_fish.transform.position, companion.JobPosition):0.#} m " +
                $"(isca '{_bait}', margem a {Flat(companion.JobPosition, _stand):0.#} m).");
        }

        /// <summary>
        /// Escolhe o peixe e o ponto de pesca: o peixe não fisgado mais "barato" dentro do raio que
        /// tenha isca no inventário e uma margem de onde ele fique ao alcance do arremesso e à vista.
        /// </summary>
        private bool PickTarget(CompanionAI companion, out string why)
        {
            why = "nenhum peixe no raio";
            float radius = CompanionJobsConfig.FishRadius;
            Vector3 me = companion.JobPosition;
            Inventory inventory = companion.JobInventory;

            List<IMonoUpdater> instances = Fish.Instances;
            Fish bestFish = null;
            Vector3 bestStand = Vector3.zero;
            string bestBait = null;
            float best = float.MaxValue;

            int seen = 0;
            int noBait = 0;
            int noStand = 0;
            string firstNoBait = null;
            string firstNoStand = null;

            for (int i = 0; i < instances.Count; i++)
            {
                Fish fish = instances[i] as Fish;
                if (fish == null || fish.IsHooked() || IsSkipped(fish))
                {
                    continue;
                }

                Vector3 position = fish.transform.position;
                if (Flat(position, me) > radius)
                {
                    continue;
                }

                float water = WaterLevel(position);
                if (water < -1000f)
                {
                    continue; // fora da água (não dá para pescar)
                }

                seen++;

                string bait = BestBait(fish, inventory);
                if (bait == null)
                {
                    noBait++;
                    if (firstNoBait == null)
                    {
                        firstNoBait = $"{fish.m_name} (aceita {BaitList(fish)})";
                    }

                    continue;
                }

                if (!TryFindStand(companion, position, water, out Vector3 stand, out float cost))
                {
                    noStand++;
                    if (firstNoStand == null)
                    {
                        firstNoStand = $"{fish.m_name} em {position}";
                    }

                    continue;
                }

                if (cost >= best)
                {
                    continue;
                }

                best = cost;
                bestFish = fish;
                bestStand = stand;
                bestBait = bait;
            }

            if (bestFish == null)
            {
                why = seen == 0
                    ? $"nenhum peixe a menos de {radius:0} m"
                    : noBait > 0 && noStand == 0
                        ? $"sem isca para {firstNoBait}"
                        : $"sem margem para pescar (ex.: {firstNoStand ?? "?"})";
                return false;
            }

            _fish = bestFish;
            _fishName = PrefabName(bestFish.gameObject);
            _stand = bestStand;
            _bait = bestBait;
            return true;
        }

        /// <summary>Do lugar onde ele está agora: o peixe mais perto ao alcance do arremesso.</summary>
        private bool PickFromHere(CompanionAI companion)
        {
            Inventory inventory = companion.JobInventory;
            Vector3 me = companion.JobPosition;
            float range = CompanionJobsConfig.CastRange;

            List<IMonoUpdater> instances = Fish.Instances;
            Fish best = null;
            string bestBait = null;
            float bestDistance = range;

            for (int i = 0; i < instances.Count; i++)
            {
                Fish fish = instances[i] as Fish;
                if (fish == null || fish.IsHooked() || IsSkipped(fish))
                {
                    continue;
                }

                Vector3 position = fish.transform.position;
                float distance = Flat(me, position);
                if (distance >= bestDistance || distance < 2f)
                {
                    continue;
                }

                string bait = BestBait(fish, inventory);
                if (bait == null)
                {
                    continue;
                }

                float water = WaterLevel(position);
                if (water < -1000f)
                {
                    continue;
                }

                if (Physics.Linecast(
                        me + Vector3.up * 1.6f,
                        new Vector3(position.x, water + 0.3f, position.z),
                        SolidMask,
                        QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                bestDistance = distance;
                best = fish;
                bestBait = bait;
            }

            if (best == null)
            {
                return false;
            }

            _fish = best;
            _fishName = PrefabName(best.gameObject);
            _bait = bestBait;
            _stand = me;
            return true;
        }

        /// <summary>
        /// A isca do inventário que este peixe aceita melhor (<c>Fish.m_baits</c> com a chance de
        /// cada uma). Null quando ele não tem nenhuma que sirva.
        /// </summary>
        private static string BestBait(Fish fish, Inventory inventory)
        {
            if (fish.m_baits == null || inventory == null)
            {
                return null;
            }

            string best = null;
            float bestChance = -1f;

            for (int i = 0; i < fish.m_baits.Count; i++)
            {
                Fish.BaitSetting setting = fish.m_baits[i];
                if (setting == null || setting.m_bait == null)
                {
                    continue;
                }

                string name = setting.m_bait.gameObject.name;
                if (setting.m_chance > bestChance && CountIn(inventory, name) > 0)
                {
                    bestChance = setting.m_chance;
                    best = name;
                }
            }

            return best;
        }

        /// <summary>Texto com as iscas que o peixe aceita (para o log/HUD quando falta isca).</summary>
        private static string BaitList(Fish fish)
        {
            if (fish.m_baits == null || fish.m_baits.Count == 0)
            {
                return "nenhuma isca conhecida";
            }

            string text = null;
            for (int i = 0; i < fish.m_baits.Count; i++)
            {
                Fish.BaitSetting setting = fish.m_baits[i];
                if (setting == null || setting.m_bait == null)
                {
                    continue;
                }

                string name = setting.m_bait.gameObject.name;
                text = text == null ? name : text + ", " + name;
            }

            return text ?? "nenhuma isca conhecida";
        }

        // ---------------------------------------------------------------------
        // Margem de onde pescar
        // ---------------------------------------------------------------------

        /// <summary>
        /// Ponto de onde dá para arremessar neste peixe: o lugar onde ele está (se já serve) ou a
        /// margem seca mais próxima em volta do peixe, com espaço para ficar de pé e linha de visão
        /// até a água. O "custo" é o caminho até lá mais 1,5× o arremesso (prefere andar pouco e
        /// arremessar pouco, como na referência).
        /// </summary>
        private bool TryFindStand(CompanionAI companion, Vector3 fishPosition, float water, out Vector3 stand, out float cost)
        {
            stand = Vector3.zero;
            cost = float.MaxValue;

            Vector3 target = new Vector3(fishPosition.x, water, fishPosition.z);
            Vector3 me = companion.JobPosition;

            if (GoodStand(me, target, water))
            {
                stand = me;
                cost = 0f;
                return true;
            }

            Vector3 toMe = me - fishPosition;
            toMe.y = 0f;
            float baseAngle = toMe.sqrMagnitude > 0.01f ? Mathf.Atan2(toMe.x, toMe.z) * Mathf.Rad2Deg : 0f;

            bool found = false;
            float range = CompanionJobsConfig.CastRange;

            // Em volta do peixe, começando pela direção de onde ele está: 12 ângulos, andando para
            // fora de 1 em 1 m até o alcance do arremesso.
            for (int i = 0; i < 12; i++)
            {
                float angle = baseAngle + (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * 30f;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;

                for (float distance = 2f; distance <= range; distance += 1f)
                {
                    Vector3 point = fishPosition + direction * distance;
                    if (!TryTop(point, water, out Vector3 top))
                    {
                        continue;
                    }

                    if (top.y < water + 0.1f)
                    {
                        continue; // ainda é água (ou o fundo dela)
                    }

                    if (!GoodStand(top, target, water))
                    {
                        break; // a primeira margem seca deste lado não serve: tenta outro lado
                    }

                    float candidate = Flat(me, top) + Flat(top, target) * 1.5f;
                    if (candidate < cost)
                    {
                        cost = candidate;
                        stand = top;
                        found = true;
                    }

                    break;
                }
            }

            return found;
        }

        /// <summary>Chão (terreno, doca, muro) visto de cima da água, com inclinação para ficar de pé.</summary>
        private static bool TryTop(Vector3 point, float water, out Vector3 top)
        {
            top = point;

            if (!Physics.Raycast(
                    new Vector3(point.x, water + 30f, point.z),
                    Vector3.down,
                    out RaycastHit hit,
                    60f,
                    StandMask,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            if (hit.normal.y < 0.6f)
            {
                return false;
            }

            top = hit.point;
            return true;
        }

        /// <summary>
        /// O ponto serve para pescar? Seco, ao alcance do arremesso (nem colado nem longe demais),
        /// com espaço para ficar de pé e linha de visão livre até a água sobre o peixe.
        /// </summary>
        private static bool GoodStand(Vector3 point, Vector3 target, float water)
        {
            float distance = Flat(point, target);
            if (distance < 2f || distance > CompanionJobsConfig.CastRange)
            {
                return false;
            }

            if (point.y < water + 0.1f || point.y > water + 10f)
            {
                return false;
            }

            if (Physics.CheckCapsule(
                    point + Vector3.up * 0.6f,
                    point + Vector3.up * 1.6f,
                    0.3f,
                    StandMask,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            return !Physics.Linecast(
                point + Vector3.up * 1.6f,
                target + Vector3.up * 0.3f,
                SolidMask,
                QueryTriggerInteraction.Ignore);
        }

        // ---------------------------------------------------------------------
        // Andando até o ponto de pesca
        // ---------------------------------------------------------------------

        private void TickMoving(CompanionAI companion, float dt)
        {
            if (_fish == null || _fish.IsHooked())
            {
                BackToScan(companion, "o peixe sumiu (ou outro pescou)");
                return;
            }

            Vector3 me = companion.JobPosition;
            if (Flat(me, _stand) <= 1.2f && Mathf.Abs(me.y - _stand.y) < 1.5f)
            {
                companion.JobStopMoving();
                _state = FishState.Casting;
                _stateSince = Time.time;
                SetStatus($"pescando {_fishName}");
                return;
            }

            if (Time.time - _stateSince > MoveTimeout || _noProgress > MoveStuckSeconds)
            {
                Skip(_fish, "não consegui chegar à margem");
                BackToScan(companion, "não consegui chegar à margem");
                return;
            }

            companion.JobApproach(dt, _stand, 0.8f, MovementOwner.Fish);

            float distance = Vector3.Distance(companion.JobPosition, _stand);
            if (distance < _bestDistance - 0.5f)
            {
                _bestDistance = distance;
                _noProgress = 0f;
            }
            else
            {
                _noProgress += dt;
            }

            SetStatus($"indo pescar {_fishName}");
        }

        // ---------------------------------------------------------------------
        // Arremessando
        // ---------------------------------------------------------------------

        private void TickCasting(CompanionAI companion, float dt)
        {
            companion.JobStopMoving();

            // Escorregou para a água (ou o ponto mudou de baixo dele): volta para a margem.
            if (companion.JobIsSwimming ||
                Flat(companion.JobPosition, _stand) > 2.5f ||
                companion.JobPosition.y < _stand.y - 1.2f)
            {
                Fell(companion, "antes de arremessar");
                return;
            }

            // Puxar um peixe cansa: descansa até ter stamina de sobra antes de arremessar (o
            // FishController da referência faz o mesmo com 90%).
            float minimum = CompanionJobsConfig.MinCastStamina;
            float maxStamina = companion.JobMaxStamina;
            if (minimum > 0f && maxStamina > 0f && companion.JobStamina < maxStamina * minimum)
            {
                companion.JobLookAt(AimPoint(companion));
                _stateSince = Time.time; // esperar stamina não é travamento
                SetStatus("descansando (stamina)");
                LogEvent(
                    "fish|rest",
                    $"[ModIA] Pescar: descansando antes de arremessar " +
                    $"({companion.JobStamina:0}/{maxStamina:0} de resistencia).");
                return;
            }

            Humanoid humanoid = companion.JobHumanoid;
            Inventory inventory = humanoid != null ? humanoid.GetInventory() : null;
            if (humanoid == null || inventory == null)
            {
                Finish(companion, "companheiro sem inventario");
                return;
            }

            ItemDrop.ItemData rod = CompanionInventory.FindFishingRod(inventory);
            ItemDrop.ItemData bait = FindBait(inventory, _bait);
            if (rod == null)
            {
                Finish(companion, "sem vara de pesca");
                return;
            }

            if (bait == null)
            {
                // A isca acabou (a última foi consumida no arremesso): repõe e volta a escolher.
                BackToScan(companion, $"a isca '{_bait}' acabou");
                return;
            }

            // A vara e a isca na mão (a isca vai no slot de munição, como no jogador).
            if (humanoid.GetCurrentWeapon() != rod)
            {
                humanoid.EquipItem(rod);
                return;
            }

            if (humanoid.GetAmmoItem() != bait)
            {
                humanoid.EquipItem(bait);
            }

            _rodTop = null;

            if (_fish != null &&
                Flat(companion.JobPosition, _fish.transform.position) > CompanionJobsConfig.CastRange + 6f)
            {
                BackToScan(companion, "o peixe nadou para longe");
                return;
            }

            Vector3 aim = AimPoint(companion);
            companion.JobLookAt(aim);

            if (companion.JobInAttack || !companion.JobIsLookingAt(aim, 8f) ||
                Time.time - _stateSince < 0.5f)
            {
                if (Time.time - _stateSince > 10f)
                {
                    BackToScan(companion, "não consegui arremessar");
                }

                return;
            }

            // Força do arremesso: aprendida de onde o último flutuador caiu (um arremesso cheio vai
            // a ~24 m, então arremessar sempre com tudo jogaria a linha longe demais).
            float want = Flat(companion.JobPosition, aim);
            float percent = Mathf.Clamp01(want / Mathf.Max(4f, _castScale));
            Attack attack = rod.m_shared.m_attack;
            float full = attack.m_drawDurationMin;
            float skill = companion.JobCharacter.GetSkillFactor(rod.m_shared.m_skillType);
            float draw = Mathf.Lerp(full, full * 0.2f, skill);

            humanoid.m_attackDrawTime = draw > 0f ? Mathf.Max(0.01f, percent * draw) : 0f;
            bool cast = companion.JobAttack();
            humanoid.m_attackDrawTime = 0f;

            if (!cast)
            {
                // O jogo recusou (stamina/isca/ataque em andamento): tenta no próximo tick.
                return;
            }

            CompanionFishingFloat.LastBait = _bait;
            _castAt = Time.time;
            _castTries++;
            _settled = false;
            _retrieving = false;
            _hooked = null;
            _hookedName = null;
            _state = FishState.Waiting;
            _stateSince = Time.time;
            SetStatus("esperando o peixe");

            LogEvent(
                "fish|cast",
                $"[ModIA] Pescar: arremessei em '{_fishName}' ({want:0.#} m, " +
                $"{percent * 100f:0}% de forca, isca '{_bait}').");
        }

        /// <summary>Ponto da água onde a linha deve cair: no peixe, mas nunca além do arremesso.</summary>
        private Vector3 AimPoint(CompanionAI companion)
        {
            Vector3 me = companion.JobPosition;
            Vector3 point = _fish != null
                ? _fish.transform.position
                : me + companion.JobCharacter.transform.forward * 8f;

            Vector3 direction = point - me;
            direction.y = 0f;
            float range = CompanionJobsConfig.CastRange;
            if (direction.magnitude > range)
            {
                point = me + direction.normalized * range;
            }

            float water = WaterLevel(point);
            point.y = water > -1000f ? water : me.y - 1f;
            return point;
        }

        // ---------------------------------------------------------------------
        // Esperando a fisgada
        // ---------------------------------------------------------------------

        private void TickWaiting(CompanionAI companion)
        {
            companion.JobStopMoving();

            FishingFloat floatInstance = CompanionFishingFloat.Of(companion.JobCharacter);
            if (floatInstance == null)
            {
                if (Time.time - _castAt > 4f)
                {
                    if (_castTries >= 3)
                    {
                        Skip(_fish, "o arremesso não saiu");
                        BackToScan(companion, "o arremesso não saiu");
                    }
                    else
                    {
                        _state = FishState.Casting;
                        _stateSince = Time.time;
                    }
                }

                return;
            }

            companion.JobLookAt(floatInstance.transform.position);

            if (!_settled && Time.time - _castAt > 2.5f)
            {
                _settled = true;

                float landed = Flat(companion.JobPosition, floatInstance.transform.position);
                float want = Flat(companion.JobPosition, AimPoint(companion));
                float percent = Mathf.Clamp01(want / Mathf.Max(4f, _castScale));
                if (percent > 0.05f && floatInstance.IsInWater())
                {
                    _castScale = Mathf.Clamp(Mathf.Lerp(_castScale, landed / percent, 0.5f), 4f, 40f);
                }

                if (!floatInstance.IsInWater())
                {
                    LogEvent("fish|dry", "[ModIA] Pescar: o flutuador caiu em terra; recolhendo.");
                    Retrieve(companion);
                    return;
                }
            }

            // Fisgada: o peixe mordeu agora (o jogo marca o nibbler e o instante da mordida) —
            // puxa na hora, como um jogador olhando o flutuador.
            Fish nibbler = floatInstance.m_nibbler;
            if (nibbler != null && Time.time - floatInstance.m_nibbleTime < 0.4f)
            {
                _hookTry = Time.time;
                ReelOn(companion);
                _state = FishState.Reeling;
                _stateSince = Time.time;
                SetStatus("fisgou!");
                LogEvent("fish|hook", "[ModIA] Pescar: fisgou — puxando.");
                return;
            }

            if (Time.time - _castAt > CompanionJobsConfig.CastWait)
            {
                LogEvent("fish|nobite", "[ModIA] Pescar: nenhuma fisgada; recolhendo para tentar de novo.");
                Retrieve(companion);
            }
        }

        private void Retrieve(CompanionAI companion)
        {
            _retrieving = true;
            ReelOn(companion);
            _state = FishState.Reeling;
            _stateSince = Time.time;
            SetStatus("recolhendo");
        }

        // ---------------------------------------------------------------------
        // Puxando
        // ---------------------------------------------------------------------

        private void TickReeling(CompanionAI companion)
        {
            companion.JobStopMoving();

            if (companion.JobIsSwimming)
            {
                if (_hooked != null)
                {
                    LogEvent(
                        "fish|fell",
                        $"[ModIA] Pescar: caiu na agua com '{_hookedName}' na linha — perdi o peixe.");
                }

                _hooked = null;
                _hookedName = null;
                Fell(companion, "puxando");
                return;
            }

            FishingFloat floatInstance = CompanionFishingFloat.Of(companion.JobCharacter);
            if (floatInstance == null)
            {
                // O flutuador saiu (peixe na mão ou linha arrebentada): confere o inventário.
                ReelOff(companion);
                Landed(companion);
                return;
            }

            companion.JobLookAt(floatInstance.transform.position);

            Fish fish = floatInstance.GetCatch();
            if (fish == null)
            {
                if (_hooked != null)
                {
                    // Estava fisgado e soltou (luta, stamina curta ou linha frouxa).
                    LogEvent("fish|escape", $"[ModIA] Pescar: o '{_hookedName}' escapou.");
                    _hooked = null;
                    Retrieve(companion);
                    return;
                }

                if (!_retrieving && Time.time - _hookTry > 1f)
                {
                    // Fisgou no vazio: deixa o flutuador assentar e espera de novo.
                    ReelOff(companion);
                    _state = FishState.Waiting;
                    _stateSince = Time.time;
                    SetStatus("esperando o peixe");
                }
                else
                {
                    ReelOn(companion);
                }
            }
            else
            {
                if (_hooked == null)
                {
                    _hooked = fish;
                    _hookedName = PrefabName(fish.gameObject);
                    _hookedHover = fish.GetHoverName();
                    _haveBefore = CountIn(companion.JobInventory, _hookedName, _hookedHover);
                    LogEvent("fish|reel", $"[ModIA] Pescar: '{_hookedName}' na linha — puxando.");
                }

                // Como um jogador: larga quando o peixe foge (ou a linha tensiona) e puxa quando
                // ele cansa.
                float tension = Tension(companion, floatInstance);
                if (fish.IsEscaping() || tension > MaxTension)
                {
                    ReelOff(companion);
                }
                else
                {
                    ReelOn(companion);
                }
            }

            if (Time.time - _stateSince > ReelTimeout)
            {
                LogEvent("fish|reeltimeout", "[ModIA] Pescar: puxar demorou demais; largando a linha.");
                DropLine(companion, "puxar demorou demais");
            }
        }

        /// <summary>
        /// Tensão da linha (m): a distância da ponta da vara ao flutuador menos o comprimento da
        /// linha. Positivo grande = linha esticada (o peixe está fugindo).
        /// </summary>
        private float Tension(CompanionAI companion, FishingFloat floatInstance)
        {
            if (_rodTop == null)
            {
                _rodTop = Utils.FindChild(companion.JobCharacter.transform, "_RodTop");
            }

            if (_rodTop == null)
            {
                return 0f;
            }

            return Vector3.Distance(_rodTop.position, floatInstance.transform.position) -
                   floatInstance.m_lineLength;
        }

        /// <summary>Fecha a conta de um puxão: o peixe entrou no inventário ou a linha arrebentou.</summary>
        private void Landed(CompanionAI companion)
        {
            if (_hookedName != null)
            {
                int now = CountIn(companion.JobInventory, _hookedName, _hookedHover);
                if (now > _haveBefore)
                {
                    AddProgress();
                    Plugin.Log.LogInfo(
                        $"[ModIA] Pescar: peguei '{_hookedName}' " +
                        $"(peixe {Progress}{(Amount > 0 ? $"/{Amount}" : "")}).");
                    ShowHud($"Pesquei: {_hookedName} ({Progress}{(Amount > 0 ? $"/{Amount}" : "")})");
                }
                else
                {
                    LogEvent(
                        "fish|linebroke",
                        $"[ModIA] Pescar: o '{_hookedName}' escapou (a linha arrebentou).");
                }
            }

            _hooked = null;
            _hookedName = null;
            _retrieving = false;

            if (AmountReached)
            {
                Finish(companion, "meta atingida");
                return;
            }

            // Do mesmo lugar, no peixe mais perto que ele ainda quer (sem andar de novo).
            if (PickFromHere(companion))
            {
                _castTries = 0;
                _state = FishState.Casting;
                _stateSince = Time.time;
                SetStatus($"pescando {_fishName}");
                return;
            }

            BackToScan(companion, null);
        }

        // ---------------------------------------------------------------------
        // Apoio
        // ---------------------------------------------------------------------

        /// <summary>Volta a procurar peixe (e solta a vara se a linha ainda estiver fora).</summary>
        private void BackToScan(CompanionAI companion, string why)
        {
            if (why != null)
            {
                LogEvent("fish|back|" + why, $"[ModIA] Pescar: {why}.");
            }

            DropLine(companion, "procurando outro peixe");
            _fish = null;
            _fishName = string.Empty;
            _bait = null;
            _state = FishState.Scanning;
            _scanTimer = 0f;
            SetStatus("procurando peixe");
        }

        /// <summary>Larga a linha e guarda a vara (o flutuador se desfaz sozinho sem a ponta da vara).</summary>
        private void DropLine(CompanionAI companion, string reason)
        {
            if (companion == null)
            {
                return;
            }

            ReelOff(companion);
            RestoreGear(companion);
            _hooked = null;
            _hookedName = null;
            _retrieving = false;
            _rodTop = null;
            _ = reason;
        }

        /// <summary>Escorregou do ponto de pesca: volta a andar até ele (ou escolhe outro).</summary>
        private void Fell(CompanionAI companion, string when)
        {
            ReelOff(companion);
            LogEvent("fish|fell|" + when, $"[ModIA] Pescar: saiu do ponto de pesca ({when}); voltando.");
            _hooked = null;
            _hookedName = null;
            _retrieving = false;
            _state = FishState.Moving;
            _stateSince = Time.time;
            _bestDistance = float.MaxValue;
            _noProgress = 0f;
        }

        /// <summary>Pausa por prioridade (combate/loot): guarda a vara e larga o que estava fazendo.</summary>
        private void Pause(CompanionAI companion, string reason)
        {
            if (_state == FishState.Scanning)
            {
                return;
            }

            _ = reason;
            DropLine(companion, "pausa");
            _state = FishState.Scanning;
            _scanTimer = 0f;
        }

        /// <summary>Vara de pesca: puxar é o mesmo "bloqueio" que o jogador segura no mouse.</summary>
        private static void ReelOn(CompanionAI companion)
        {
            companion.JobSetBlocking(true);
        }

        private static void ReelOff(CompanionAI companion)
        {
            companion.JobSetBlocking(false);
        }

        /// <summary>Guarda a vara (e a isca) quando a pesca para, como no <c>RestoreGear</c> da referência.</summary>
        private static void RestoreGear(CompanionAI companion)
        {
            Humanoid humanoid = companion.JobHumanoid;
            if (humanoid == null)
            {
                return;
            }

            ItemDrop.ItemData weapon = humanoid.GetCurrentWeapon();
            if (weapon != null && weapon.m_shared != null &&
                weapon.m_shared.m_animationState == ItemDrop.ItemData.AnimationState.FishingRod)
            {
                humanoid.UnequipItem(weapon);
            }
        }

        private static ItemDrop.ItemData FindBait(Inventory inventory, string prefabName)
        {
            if (inventory == null || string.IsNullOrEmpty(prefabName))
            {
                return null;
            }

            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (item != null && item.m_dropPrefab != null &&
                    item.m_dropPrefab.name == prefabName)
                {
                    return item;
                }
            }

            return null;
        }

        private static int CountIn(Inventory inventory, string prefabName, string hoverName = null)
        {
            if (inventory == null || string.IsNullOrEmpty(prefabName))
            {
                return 0;
            }

            int count = 0;
            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (item == null)
                {
                    continue;
                }

                // O item guarda o prefab do ObjectDB ("Fish1"); o peixe no mundo é o mesmo nome de
                // prefab. Quando o ObjectDB não tem o item (prefab só de mundo), cai no nome
                // localizado do item — assim a contagem do peixe nunca depende de um detalhe do
                // prefab.
                string itemPrefab = item.m_dropPrefab != null
                    ? Utils.GetPrefabName(item.m_dropPrefab.name)
                    : null;

                bool match = itemPrefab != null && itemPrefab == prefabName;
                if (!match && !string.IsNullOrEmpty(hoverName) && item.m_shared != null &&
                    item.m_shared.m_name == hoverName)
                {
                    match = true;
                }

                if (match)
                {
                    count += item.m_stack;
                }
            }

            return count;
        }

        /// <summary>Nome do prefab do objeto de rede (o nome do GameObject pode ter "(Clone)").</summary>
        private static string PrefabName(GameObject go)
        {
            ZNetView netView = go != null ? go.GetComponent<ZNetView>() : null;
            ZDO zdo = netView != null && netView.IsValid() ? netView.GetZDO() : null;
            GameObject prefab = zdo != null && ZNetScene.instance != null
                ? ZNetScene.instance.GetPrefab(zdo.GetPrefab())
                : null;

            return prefab != null ? prefab.name : Utils.GetPrefabName(go != null ? go.name : "?");
        }

        private bool IsSkipped(Fish fish)
        {
            if (_skip.Count == 0 || fish == null)
            {
                return false;
            }

            int id = fish.GetInstanceID();
            if (!_skip.TryGetValue(id, out float until))
            {
                return false;
            }

            if (Time.time >= until)
            {
                _skip.Remove(id);
                return false;
            }

            return true;
        }

        private void Skip(Fish fish, string reason)
        {
            if (fish == null)
            {
                return;
            }

            if (_skip.Count > 50)
            {
                _skip.Clear();
            }

            _skip[fish.GetInstanceID()] = Time.time + 120f;
            LogEvent(
                "fish|skip|" + fish.GetInstanceID(),
                $"[ModIA] Pescar: deixando '{_fishName}' de lado por 2 min ({reason}).");
        }

        /// <summary>Distância no plano (o Y do peixe não interessa para o arremesso).</summary>
        private static float Flat(Vector3 a, Vector3 b)
        {
            return new Vector2(a.x - b.x, a.z - b.z).magnitude;
        }

        /// <summary>Altura da água no ponto (<c>-10000</c> = não há água ali).</summary>
        private static float WaterLevel(Vector3 point)
        {
            return Floating.GetWaterLevel(point, ref s_waterVolume);
        }

        // ---------------------------------------------------------------------
        // Máscaras (construídas uma vez)
        // ---------------------------------------------------------------------

        /// <summary>Superfícies sólidas (terreno, peças, pedras, veículos) para visão e água.</summary>
        private static int SolidMask
        {
            get
            {
                if (s_solidMask == -1)
                {
                    s_solidMask = LayerMask.GetMask(
                        "Default", "static_solid", "Default_small", "piece", "piece_nonsolid",
                        "terrain", "vehicle");
                }

                return s_solidMask;
            }
        }

        /// <summary>Onde dá para ficar de pé (mesmo conjunto, sem os veículos).</summary>
        private static int StandMask
        {
            get
            {
                if (s_standMask == -1)
                {
                    s_standMask = LayerMask.GetMask(
                        "Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain");
                }

                return s_standMask;
            }
        }
    }
}
