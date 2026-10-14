using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModAIValheim.Companion.Jobs
{
    /// <summary>
    /// <b>TRABALHO: CUIDAR DA BASE</b> (Fase 2.17) — o companheiro cuida da casa/base por perto,
    /// em ordem de prioridade:
    /// <list type="number">
    ///   <item><description><b>REPARAR</b> as construções danificadas (<see cref="WearNTear"/>),
    ///   com o <b>martelo na mão</b> e a estação de craft da peça no alcance — o mesmo caminho do
    ///   <c>Player.Repair</c> do 1.0.17 (sem custo de material; gasta stamina e a durabilidade do
    ///   martelo);</description></item>
    ///   <item><description><b>ABASTECER</b> as fogueiras/tochas/braseiros (<see cref="Fireplace"/>)
    ///   com o combustível do próprio inventário, mantendo uma fração do máximo;</description></item>
    ///   <item><description><b>ESVAZIAR AS COLMEIAS</b> (<see cref="Beehive"/>) com mel e catar o
    ///   mel que cai no chão;</description></item>
    ///   <item><description><b>ORDENAR OS BAÚS</b>: levar cada item repetido para o baú que já tem
    ///   mais dele (e juntar as pilhas parciais dentro do mesmo baú).</description></item>
    /// </list>
    ///
    /// <para>
    /// <b>Onde ele trabalha:</b> na <b>casa</b> (<see cref="CompanionHome"/>, definida com
    /// <c>modia_home set</c>) — centro + raio. Sem casa definida, ele cuida de um círculo em volta
    /// de onde o trabalho foi começado (<c>Base/RaioBase</c>). Quando não há nada a fazer e o
    /// trabalho é <b>contínuo</b> (<see cref="Continuous"/>, do <c>modia_basecare on</c>), ele
    /// <b>volta para o centro</b> e espera em vez de encerrar; num <c>modia_job upkeep</c> comum ele
    /// encerra depois de algumas varreduras vazias (como os outros trabalhos).
    /// </para>
    ///
    /// <para>
    /// <b>Como fala com as peças (API da 1.0.17):</b> reparo pelo <c>WearNTear.Repair()</c> (o jogo
    /// manda o <c>RPC_Repair</c> e zera o dano — não há custo de material, como para o jogador);
    /// combustível pelo <c>RPC_AddFuel</c> do <see cref="ZNetView"/> (uma unidade por vez, com o
    /// combustível lido do ZDO em <c>ZDOVars.s_fuel</c>) e mel pelo <c>RPC_Extract</c> (o nível da
    /// colmeia vive em <c>ZDOVars.s_level</c>). Os baús são mexidos <b>direto no inventário</b>
    /// (<c>Inventory.AddItem</c>/<c>RemoveItem</c>), como o depósito da Fase 2.14 — nada de abrir a
    /// GUI. Tudo com <c>ClaimOwnership</c> antes do RPC (<c>JobStations.ReadyForRpc</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Performance:</b> uma varredura a cada <c>Base/IntervaloVarredura</c> (nunca por frame),
    /// buffers reaproveitados (<c>s_pieces</c>/<c>s_chests</c>/<c>s_stock</c>) e o mapa de "pule
    /// isto por um tempo" evita insistir na peça/baú que não deu para alcançar.
    /// </para>
    ///
    /// <para>
    /// Adaptado (lógica) de <c>src/Companions/Jobs/UpkeepController.cs</c> do <i>Offline Companions
    /// Reborn</i> (MIT — ver <c>THIRD_PARTY_NOTICES.md</c>): as fases <c>Repair</c>/<c>Fuel</c>/
    /// <c>SortTake</c>/<c>SortPut</c>/<c>Tidy</c>/<c>Hive</c>/<c>HiveCollect</c>/<c>Home</c> e a
    /// escolha por prioridade (<c>PlanRepair</c> → <c>PlanFuel</c> → <c>PlanHoney</c> →
    /// <c>PlanSort</c>), o martelo na mão com a animação e o custo de stamina/durabilidade
    /// (<c>Hammer</c>/<c>PutHammerAway</c>/<c>SwingStamina</c>), o alvo de combustível por fração
    /// (<c>FuelTarget</c>/<c>Need</c>), a colmeia por <c>RPC_Extract</c> + catar o mel
    /// (<c>PlanHoney</c>/<c>UpdateHive</c>/<c>UpdateHiveCollect</c>), a consolidação "igual com
    /// igual" e a junção de pilhas parciais (<c>PlanSort</c>/<c>Merge</c>/<c>RoomIn</c>/<c>Key</c>)
    /// e o retorno ao centro (<c>UpdateHome</c>). A implementação aqui é própria (uma máquina de
    /// estados do <see cref="CompanionJob"/>, movimento pelo <see cref="MovementOwner.Upkeep"/>,
    /// buffers reaproveitados e as chaves de config em PT-BR), sem cópia de arquivo. Ficaram
    /// <b>fora</b>: as pilhas de recurso no chão (<c>UpkeepPiles</c>), buscar combustível em baús
    /// (<c>FuelFetch</c>), a lista de itens por tipo/categoria e o visual/painel da zona da casa.
    /// </para>
    /// </summary>
    internal sealed class UpkeepJob : CompanionJob
    {
        /// <summary>Em que ponto do cuidado o companheiro está.</summary>
        private enum UpkeepState
        {
            /// <summary>Varrendo a base (com intervalo).</summary>
            Scanning,

            /// <summary>Andando até o alvo da tarefa (peça/fogueira/colmeia/baú de origem).</summary>
            Moving,

            /// <summary>No alvo: reparando, abastecendo, esvaziando ou juntando pilhas.</summary>
            Working,

            /// <summary>Catando o mel que a colmeia largou no chão.</summary>
            Collecting,

            /// <summary>No baú de origem, tirando o item para levar ao baú certo.</summary>
            SortTake,

            /// <summary>Andando até o baú de destino (e pondo o item lá).</summary>
            SortPut,

            /// <summary>Voltando para o centro da base (modo contínuo, sem nada a fazer).</summary>
            Home,
        }

        /// <summary>O que a varredura escolheu fazer (uma por vez).</summary>
        private enum UpkeepTaskKind
        {
            None,

            /// <summary>Reparar uma construção danificada.</summary>
            Repair,

            /// <summary>Pôr combustível numa fogueira/tocha/braseiro.</summary>
            Fuel,

            /// <summary>Esvaziar uma colmeia com mel.</summary>
            Hive,

            /// <summary>Levar um item repetido para o baú que já tem mais dele.</summary>
            Sort,

            /// <summary>Juntar as pilhas parciais do mesmo item dentro de um baú.</summary>
            Merge,
        }

        /// <summary>A tarefa escolhida na varredura.</summary>
        private struct UpkeepTask
        {
            internal UpkeepTaskKind Kind;
            internal Component Target;
            internal Container From;
            internal Container To;
            internal string ItemKey;
            internal string ItemPrefab;
            internal int Count;
            internal Vector3 Spot;
            internal Vector3 Output;
        }

        /// <summary>Como terminou a tentativa de chegar ao ponto.</summary>
        private enum ApproachResult
        {
            /// <summary>Ainda andando.</summary>
            Walking,

            /// <summary>Chegou (dentro do alcance).</summary>
            Arrived,

            /// <summary>Não conseguiu chegar (sem progresso) — a tarefa deve ser pulada.</summary>
            GaveUp,
        }

        /// <summary>Espera (s) antes de agir ao chegar no alvo (animação de interação).</summary>
        private const float WorkDelay = 0.4f;

        /// <summary>Espera (s) depois de esvaziar a colmeia antes de catar o mel (o RPC é assíncrono).</summary>
        private const float CollectDelay = 0.8f;

        /// <summary>Tempo (s) sem chegar mais perto que faz desistir do alvo.</summary>
        private const float NoProgressSeconds = 8f;

        /// <summary>Varreduras seguidas sem nada para cuidar antes de encerrar (trabalho por comando).</summary>
        private const int EmptyScansBeforeStop = 3;

        /// <summary>Alcance (m, no plano) para reparar uma construção (o jogador alcança a 5 m).</summary>
        private const float RepairReach = 4f;

        /// <summary>Alcance (m) para usar fogueira/colmeia.</summary>
        private const float StationReach = 3f;

        /// <summary>Alcance (m) para mexer num baú.</summary>
        private const float ChestReach = 2.5f;

        /// <summary>Distância (m) do centro em que ele considera que "está em casa".</summary>
        private const float HomeArrive = 6f;

        /// <summary>Alvo de reparo que não deu para alcançar fica de fora por este tempo (s).</summary>
        private const float SkipTargetSeconds = 300f;

        /// <summary>Tentativas de equipar o martelo antes de desistir da peça (o jogo recusa em ataque/dodge).</summary>
        private const int MaxEquipAttempts = 4;

        /// <summary>Baú que não deu para usar fica de fora por este tempo (s).</summary>
        private const float SkipChestSeconds = 60f;

        /// <summary>Colmeia recém-esvaziada fica de fora por este tempo (s) — o mel leva ~10 s por unidade.</summary>
        private const float HiveSkipSeconds = 20f;

        private const int PieceBufferSize = 384;
        private const int ChestBufferSize = 64;
        private const int MaxSkipEntries = 128;
        private const int MergeGuard = 16;

        /// <summary>Buffer de peças da varredura da base (reaproveitado).</summary>
        private static readonly List<Piece> s_pieces = new List<Piece>(PieceBufferSize);

        /// <summary>Buffer dos baús utilizáveis da base (reaproveitado).</summary>
        private static readonly List<Container> s_chests = new List<Container>(ChestBufferSize);

        /// <summary>Estoque por item da varredura de ordenação (reaproveitado).</summary>
        private static readonly Dictionary<string, Stock> s_stock =
            new Dictionary<string, Stock>(StringComparer.Ordinal);

        /// <summary>Filtro do mel da colmeia na coleta (reaproveitado — evita alocar por tick).</summary>
        private static readonly HashSet<string> s_honey = new HashSet<string>(StringComparer.Ordinal);

        private UpkeepState _state = UpkeepState.Scanning;
        private UpkeepTask _task;
        private Vector3 _anchor;
        private float _scanTimer;
        private float _workTimer;
        private float _bestDistance = float.MaxValue;
        private float _noProgress;
        private int _emptyScans;
        private int _carried;
        private float _nextWaitLog;

        /// <summary>Martelo que o companheiro tinha equipado antes de pegar o de construção (para devolver).</summary>
        private ItemDrop.ItemData _previousWeapon;

        /// <summary>True enquanto ele está com o martelo de construção na mão.</summary>
        private bool _holdingHammer;

        /// <summary>Tentativas seguidas de equipar o martelo (zera quando ele entra na mão).</summary>
        private int _equipAttempts;

        private void ResetJobState()
        {
            _holdingHammer = false;
            _previousWeapon = null;
            _equipAttempts = 0;
        }

        /// <summary>Alvos/baús que falharam, com o instante até quando ficam de fora.</summary>
        private readonly Dictionary<int, float> _skip = new Dictionary<int, float>(32);

        /// <summary>
        /// Modo contínuo: quando ligado, o trabalho <b>não encerra</b> por falta de tarefas — ele
        /// volta para o centro da base e espera a próxima varredura. Quem liga é o
        /// <see cref="CompanionBaseCare"/> (<c>modia_basecare on</c> ou <c>Base/Continuo</c>).
        /// </summary>
        internal bool Continuous { get; set; }

        internal override JobKind Kind => JobKind.Upkeep;

        internal override string DisplayName => "Cuidar da base";

        internal override string CommandName => "upkeep";

        // ---------------------------------------------------------------------
        // Ciclo do trabalho
        // ---------------------------------------------------------------------

        protected override bool OnStart(CompanionAI companion, out string error)
        {
            error = null;

            if (companion.JobHumanoid == null || companion.JobInventory == null)
            {
                error = "[ModIA] O companheiro esta sem corpo/inventario; cuidar da base cancelado.";
                return false;
            }

            if (!CompanionBaseCareConfig.Enabled)
            {
                error = "[ModIA] O cuidado da base esta desligado no .cfg (Base/Ligado = false).";
                return false;
            }

            if (!CompanionBaseCareConfig.AnyTask)
            {
                error =
                    "[ModIA] Nenhuma tarefa de cuidado ligada (Base/Reparar, Base/AbastecerFogueiras, " +
                    "Base/Colmeias, Base/OrdenarBaus, Base/JuntarPilhas).";
                return false;
            }

            _state = UpkeepState.Scanning;
            _task = default;
            _scanTimer = 0f;
            _workTimer = 0f;
            _emptyScans = 0;
            _carried = 0;
            _bestDistance = float.MaxValue;
            _noProgress = 0f;
            _nextWaitLog = 0f;
            _skip.Clear();
            ResetJobState();

            HomeZone home = CompanionHome.Zone;
            _anchor = home.IsValid ? home.Center : companion.JobPosition;

            // O reparo é o único que exige ferramenta: o jogo só repara com o martelo na mão (é o
            // que o Player.Repair faz). Sem martelo nenhum e com Base/FornecerMartelo ligado, o mod
            // cria um (mesmo caminho das ferramentas de coleta).
            if (CompanionBaseCareConfig.Repair && CompanionBaseCareConfig.ProvideHammer &&
                FindHammer(companion.JobInventory) == null)
            {
                CompanionInventory.EnsureItem(companion, CompanionBaseCareConfig.HammerPrefab);
            }

            SetStatus("procurando o que cuidar");
            LogEvent(
                "upkeep|start",
                $"[ModIA] Base: cuidado iniciado em {DescribeAnchor(home)} (raio {WorkRadius():0} m, " +
                $"continuo = {Continuous}, varredura = {CompanionBaseCareConfig.ScanInterval} s).");
            return true;
        }

        protected override void OnStop(CompanionAI companion, string reason)
        {
            PutHammerAway(companion);

            if (companion != null)
            {
                companion.JobStopMoving();
            }

            _task = default;
            _carried = 0;
            _skip.Clear();
            ResetJobState();
            SetStatus("encerrado");
        }

        protected override void OnTick(CompanionAI companion, float dt)
        {
            switch (_state)
            {
                case UpkeepState.Moving:
                    TickMoving(companion, dt);
                    break;

                case UpkeepState.Working:
                    TickWorking(companion, dt);
                    break;

                case UpkeepState.Collecting:
                    TickCollecting(companion, dt);
                    break;

                case UpkeepState.SortTake:
                    TickSortTake(companion, dt);
                    break;

                case UpkeepState.SortPut:
                    TickSortPut(companion, dt);
                    break;

                case UpkeepState.Home:
                    TickHome(companion, dt);
                    break;

                default:
                    TickScanning(companion, dt);
                    break;
            }
        }

        // ---------------------------------------------------------------------
        // Varredura
        // ---------------------------------------------------------------------

        private void TickScanning(CompanionAI companion, float dt)
        {
            if (AmountReached)
            {
                Finish(companion, "meta atingida");
                return;
            }

            if (companion.JobHasCombatTarget)
            {
                companion.JobStopMoving();
                SetStatus("combate");
                return;
            }

            if (CompanionLoot.HasWork)
            {
                companion.JobStopMoving();
                SetStatus("catando o abate");
                return;
            }

            _scanTimer -= dt;
            if (_scanTimer > 0f)
            {
                companion.JobStopMoving();
                return;
            }

            _scanTimer = CompanionBaseCareConfig.ScanInterval;

            if (PickTask(companion, out UpkeepTask task))
            {
                _task = task;
                _bestDistance = float.MaxValue;
                _noProgress = 0f;
                _state = task.Kind == UpkeepTaskKind.Sort ? UpkeepState.SortTake : UpkeepState.Moving;
                SetStatus(Describe(task));
                LogEvent(
                    "upkeep|task|" + task.Kind,
                    $"[ModIA] Base: {Describe(task)} " +
                    $"({Vector3.Distance(companion.JobPosition, task.Spot):0.#} m).");
                return;
            }

            // Nada de reparo: guarda o martelo (ele não fica andando de martelo na mão).
            PutHammerAway(companion);

            if (Continuous)
            {
                // Modo contínuo: não encerra — volta para o centro e espera a próxima varredura.
                _emptyScans = 0;
                float toAnchor = Vector3.Distance(
                    new Vector3(_anchor.x, companion.JobPosition.y, _anchor.z), companion.JobPosition);
                if (toAnchor > HomeArrive)
                {
                    _state = UpkeepState.Home;
                    _bestDistance = float.MaxValue;
                    _noProgress = 0f;
                    SetStatus("voltando para o centro da base");
                    return;
                }

                SetStatus("base em ordem (esperando)");
                companion.JobStopMoving();
                return;
            }

            _emptyScans++;
            LogEvent(
                "upkeep|empty",
                $"[ModIA] Base: nada para cuidar num raio de {WorkRadius():0} m " +
                $"({_emptyScans}/{EmptyScansBeforeStop}).");

            if (_emptyScans >= EmptyScansBeforeStop)
            {
                Finish(companion, "nada para cuidar na base");
                return;
            }

            SetStatus("procurando o que cuidar");
            companion.JobStopMoving();
        }

        /// <summary>Raio de trabalho efetivo: o da casa quando ela existe, senão o da config.</summary>
        private static float WorkRadius()
        {
            HomeZone home = CompanionHome.Zone;
            return home.IsValid ? home.Radius : CompanionBaseCareConfig.WorkRadius;
        }

        /// <summary>
        /// Uma passada nas peças da base escolhendo <b>uma</b> tarefa, na ordem de prioridade
        /// (reparar → abastecer → colmeia → ordenar → juntar pilhas). Devolve <c>false</c> quando
        /// não há nada para fazer.
        /// </summary>
        private bool PickTask(CompanionAI companion, out UpkeepTask best)
        {
            best = default;

            Inventory inventory = companion.JobInventory;
            if (inventory == null)
            {
                return false;
            }

            Vector3 me = companion.JobPosition;
            float radius = WorkRadius();

            s_pieces.Clear();
            Piece.GetAllPiecesInRadius(_anchor, radius, s_pieces);

            int bestTier = int.MaxValue;
            float bestDistance = float.MaxValue;
            int noStation = 0;

            s_chests.Clear();

            for (int i = 0; i < s_pieces.Count; i++)
            {
                Piece piece = s_pieces[i];
                if (piece == null)
                {
                    continue;
                }

                ZNetView netView = piece.GetComponent<ZNetView>();
                if (netView == null || !netView.IsValid() || netView.GetZDO() == null)
                {
                    continue;
                }

                Container container = piece.GetComponentInChildren<Container>();
                if (container != null && !Skipped(container) && CompanionDeposit.IsUsableChest(container))
                {
                    s_chests.Add(container);
                }

                if (CompanionBaseCareConfig.Repair)
                {
                    ConsiderRepair(piece, me, ref best, ref bestTier, ref bestDistance, ref noStation);
                }

                if (CompanionBaseCareConfig.Refuel)
                {
                    ConsiderFire(piece, inventory, me, ref best, ref bestTier, ref bestDistance);
                }

                if (CompanionBaseCareConfig.Hives)
                {
                    ConsiderHive(piece, me, ref best, ref bestTier, ref bestDistance);
                }
            }

            if (CompanionBaseCareConfig.SortChests || CompanionBaseCareConfig.MergeStacks)
            {
                ConsiderSort(me, ref best, ref bestTier, ref bestDistance);
            }

            if (best.Kind == UpkeepTaskKind.None && noStation > 0 && Time.time >= _nextWaitLog)
            {
                _nextWaitLog = Time.time + 120f;
                LogEvent(
                    "upkeep|nostation",
                    $"[ModIA] Base: {noStation} construcao(oes) danificada(s) sem a estacao de craft " +
                    "por perto (um jogador tambem nao conseguiria reparar essas).");
            }

            return best.Kind != UpkeepTaskKind.None;
        }

        /// <summary>Considera reparar uma construção danificada (prioridade 0).</summary>
        private void ConsiderRepair(
            Piece piece,
            Vector3 me,
            ref UpkeepTask best,
            ref int bestTier,
            ref float bestDistance,
            ref int noStation)
        {
            if (!piece.IsPlacedByPlayer())
            {
                return;
            }

            WearNTear wear = piece.GetComponent<WearNTear>();
            if (wear == null || Skipped(wear) ||
                wear.GetHealthPercentage() >= Mathf.Min(CompanionBaseCareConfig.RepairBelow, 0.9999f))
            {
                return;
            }

            Vector3 pos = piece.transform.position;
            if (!PrivateArea.CheckAccess(pos, 0f, false, false))
            {
                return;
            }

            // Mesma regra do Player.CheckCanRemovePiece: a estação de craft da peça tem de estar no
            // alcance (a chave global NoWorkbench desliga isso no mundo, como no jogo).
            bool noBench = ZoneSystem.instance != null &&
                           ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench);
            if (!noBench && piece.m_craftingStation != null &&
                CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, pos) == null)
            {
                noStation++;
                return;
            }

            Vector3 spot = new Vector3(pos.x, me.y, pos.z);
            Offer(ref best, ref bestTier, ref bestDistance, me, spot, new UpkeepTask
            {
                Kind = UpkeepTaskKind.Repair,
                Target = wear,
                Count = 1,
                Spot = spot,
            }, 0);
        }

        /// <summary>Considera reabastecer uma fogueira/tocha/braseiro (prioridade 1).</summary>
        private void ConsiderFire(
            Piece piece,
            Inventory inventory,
            Vector3 me,
            ref UpkeepTask best,
            ref int bestTier,
            ref float bestDistance)
        {
            Fireplace fire = piece.GetComponent<Fireplace>();
            if (fire == null || !fire.m_canRefill || fire.m_infiniteFuel || fire.m_fuelItem == null ||
                fire.m_maxFuel <= 0f || Skipped(fire))
            {
                return;
            }

            ZNetView netView = fire.GetComponent<ZNetView>();
            if (netView == null || !netView.IsValid() || netView.GetZDO() == null)
            {
                return;
            }

            Vector3 pos = fire.transform.position;
            if (!PrivateArea.CheckAccess(pos, 0f, false, false))
            {
                return;
            }

            float fuel = netView.GetZDO().GetFloat(ZDOVars.s_fuel);
            int target = FuelTarget(fire.m_maxFuel);
            if (fuel >= target - 0.5f)
            {
                return;
            }

            string fuelPrefab = fire.m_fuelItem.gameObject.name;
            int have = CountPrefab(inventory, fuelPrefab);
            if (have <= 0)
            {
                return;
            }

            Vector3 spot = pos + Vector3.up * 0.3f;
            Offer(ref best, ref bestTier, ref bestDistance, me, spot, new UpkeepTask
            {
                Kind = UpkeepTaskKind.Fuel,
                Target = fire,
                ItemPrefab = fuelPrefab,
                Count = Mathf.Min(Mathf.CeilToInt(target - fuel), have),
                Spot = spot,
                Output = pos,
            }, 1);
        }

        /// <summary>Considera esvaziar uma colmeia com mel (prioridade 2).</summary>
        private void ConsiderHive(
            Piece piece,
            Vector3 me,
            ref UpkeepTask best,
            ref int bestTier,
            ref float bestDistance)
        {
            Beehive hive = piece.GetComponent<Beehive>();
            if (hive == null || hive.m_honeyItem == null || Skipped(hive))
            {
                return;
            }

            ZNetView netView = hive.GetComponent<ZNetView>();
            if (netView == null || !netView.IsValid() || netView.GetZDO() == null)
            {
                return;
            }

            Vector3 pos = hive.transform.position;
            if (!PrivateArea.CheckAccess(pos, 0f, false, false))
            {
                return;
            }

            int honey = netView.GetZDO().GetInt(ZDOVars.s_level);
            int minimum = Mathf.Max(1, Mathf.CeilToInt(hive.m_maxHoney * CompanionBaseCareConfig.HoneyMin));
            if (honey < minimum)
            {
                return;
            }

            Vector3 output = hive.m_spawnPoint != null ? hive.m_spawnPoint.position : pos;
            Offer(ref best, ref bestTier, ref bestDistance, me, pos, new UpkeepTask
            {
                Kind = UpkeepTaskKind.Hive,
                Target = hive,
                ItemPrefab = hive.m_honeyItem.gameObject.name,
                Count = 1,
                Spot = pos,
                Output = output,
            }, 2);
        }

        /// <summary>
        /// Considera <b>ordenar os baús</b> (prioridade 3) e <b>juntar pilhas parciais</b>
        /// (prioridade 4), com a mesma ideia da referência: "igual com igual" — cada item vai para o
        /// baú que já tem mais dele.
        /// </summary>
        private void ConsiderSort(
            Vector3 me,
            ref UpkeepTask best,
            ref int bestTier,
            ref float bestDistance)
        {
            if (s_chests.Count < 2)
            {
                return;
            }

            BuildStock();

            if (CompanionBaseCareConfig.SortChests)
            {
                foreach (KeyValuePair<string, Stock> pair in s_stock)
                {
                    if (pair.Value.Count.Count < 2)
                    {
                        continue;
                    }

                    Container to = BestDestination(pair.Value);
                    if (to == null || !RoomIn(to, pair.Key))
                    {
                        continue;
                    }

                    Container from = null;
                    float bestCost = float.MaxValue;
                    foreach (KeyValuePair<Container, int> candidate in pair.Value.Count)
                    {
                        if (candidate.Key == to || candidate.Value <= 0)
                        {
                            continue;
                        }

                        float cost = Vector3.Distance(me, candidate.Key.transform.position) +
                                     Vector3.Distance(candidate.Key.transform.position, to.transform.position);
                        if (cost < bestCost)
                        {
                            bestCost = cost;
                            from = candidate.Key;
                        }
                    }

                    if (from == null)
                    {
                        continue;
                    }

                    int amount = Mathf.Min(pair.Value.Count[from], CompanionBaseCareConfig.SortPerTrip);
                    Vector3 spot = from.transform.position;
                    Offer(ref best, ref bestTier, ref bestDistance, me, spot, new UpkeepTask
                    {
                        Kind = UpkeepTaskKind.Sort,
                        From = from,
                        To = to,
                        ItemKey = pair.Key,
                        Count = amount,
                        Spot = spot,
                    }, 3);
                }
            }

            if (!CompanionBaseCareConfig.MergeStacks)
            {
                return;
            }

            foreach (KeyValuePair<string, Stock> pair in s_stock)
            {
                foreach (KeyValuePair<Container, int> candidate in pair.Value.Partial)
                {
                    if (candidate.Value < 2 || Skipped(candidate.Key))
                    {
                        continue;
                    }

                    Vector3 spot = candidate.Key.transform.position;
                    Offer(ref best, ref bestTier, ref bestDistance, me, spot, new UpkeepTask
                    {
                        Kind = UpkeepTaskKind.Merge,
                        To = candidate.Key,
                        ItemKey = pair.Key,
                        Count = 1,
                        Spot = spot,
                    }, 4);
                    break;
                }
            }
        }

        /// <summary>
        /// Guarda a tarefa quando ela é melhor que a atual: <b>primeiro por prioridade</b>
        /// (<paramref name="tier"/> menor vence) e, empatada, pelo alvo mais perto.
        /// </summary>
        private static void Offer(
            ref UpkeepTask best,
            ref int bestTier,
            ref float bestDistance,
            Vector3 me,
            Vector3 spot,
            UpkeepTask candidate,
            int tier)
        {
            float distance = (spot - me).sqrMagnitude;
            if (tier > bestTier || (tier == bestTier && distance >= bestDistance))
            {
                return;
            }

            best = candidate;
            bestTier = tier;
            bestDistance = distance;
        }

        // ---------------------------------------------------------------------
        // Andando até o alvo
        // ---------------------------------------------------------------------

        private void TickMoving(CompanionAI companion, float dt)
        {
            if (companion.JobHasCombatTarget)
            {
                SetStatus("combate");
                companion.JobStopMoving();
                return;
            }

            if (!TargetAlive())
            {
                BackToScanning();
                return;
            }

            switch (Approach(companion, _task.Spot, Reach(_task.Kind), dt))
            {
                case ApproachResult.Arrived:
                    companion.JobStopMoving();
                    _state = UpkeepState.Working;
                    _workTimer = WorkDelay;
                    return;

                case ApproachResult.GaveUp:
                    Skip(_task.Target, SkipTargetSeconds, "nao consegui chegar");
                    BackToScanning();
                    return;
            }
        }

        /// <summary>
        /// Aproxima-se do ponto reivindicando o movimento do frame para o cuidado da base
        /// (<see cref="MovementOwner.Upkeep"/>) — passa pelas mesmas guardas de porta/barranco.
        /// </summary>
        private ApproachResult Approach(CompanionAI companion, Vector3 spot, float reach, float dt)
        {
            float distance = Vector3.Distance(spot, companion.JobPosition);
            if (distance <= reach)
            {
                return ApproachResult.Arrived;
            }

            if (distance < _bestDistance - 0.25f)
            {
                _bestDistance = distance;
                _noProgress = 0f;
            }
            else
            {
                _noProgress += dt;
                if (_noProgress >= NoProgressSeconds)
                {
                    return ApproachResult.GaveUp;
                }
            }

            companion.JobApproach(dt, spot, reach, MovementOwner.Upkeep);
            return ApproachResult.Walking;
        }

        /// <summary>Distância (m) em que o companheiro para do alvo, por tipo de tarefa.</summary>
        private static float Reach(UpkeepTaskKind kind)
        {
            switch (kind)
            {
                case UpkeepTaskKind.Repair:
                    return RepairReach;

                case UpkeepTaskKind.Sort:
                case UpkeepTaskKind.Merge:
                    return ChestReach;

                default:
                    return StationReach;
            }
        }

        /// <summary>O alvo da tarefa ainda existe?</summary>
        private bool TargetAlive()
        {
            switch (_task.Kind)
            {
                case UpkeepTaskKind.Repair:
                    WearNTear wear = _task.Target as WearNTear;
                    return wear != null && wear.GetComponent<ZNetView>() != null &&
                           wear.GetComponent<ZNetView>().IsValid();

                case UpkeepTaskKind.Fuel:
                    Fireplace fire = _task.Target as Fireplace;
                    return fire != null && fire.GetComponent<ZNetView>() != null &&
                           fire.GetComponent<ZNetView>().IsValid();

                case UpkeepTaskKind.Hive:
                    Beehive hive = _task.Target as Beehive;
                    return hive != null && hive.GetComponent<ZNetView>() != null &&
                           hive.GetComponent<ZNetView>().IsValid();

                case UpkeepTaskKind.Sort:
                    return _task.From != null && _task.From.GetInventory() != null &&
                           _task.To != null && _task.To.GetInventory() != null;

                case UpkeepTaskKind.Merge:
                    return _task.To != null && _task.To.GetInventory() != null;

                default:
                    return false;
            }
        }

        // ---------------------------------------------------------------------
        // Na base do alvo: a ação
        // ---------------------------------------------------------------------

        private void TickWorking(CompanionAI companion, float dt)
        {
            _workTimer -= dt;
            if (_workTimer > 0f)
            {
                companion.JobStopMoving();
                return;
            }

            Inventory inventory = companion.JobInventory;
            if (inventory == null)
            {
                BackToScanning();
                return;
            }

            switch (_task.Kind)
            {
                case UpkeepTaskKind.Repair:
                    Repair(companion, inventory);
                    break;

                case UpkeepTaskKind.Fuel:
                    Stoke(companion, inventory);
                    break;

                case UpkeepTaskKind.Hive:
                    ExtractHoney(companion);
                    break;

                case UpkeepTaskKind.Merge:
                    Merge(companion);
                    break;

                default:
                    BackToScanning();
                    break;
            }
        }

        /// <summary>
        /// Repara a peça pelo caminho do <c>Player.Repair</c> do 1.0.17: martelo na mão, a estação de
        /// craft da peça no alcance e acesso na área privada; o dano sai pelo <c>WearNTear.Repair()</c>
        /// (que dispara o <c>RPC_Repair</c>). <b>Não há custo de material</b> — o custo é stamina
        /// (<c>GetBuildStamina</c> do jogo, reduzido pela perícia) e a durabilidade do martelo.
        /// </summary>
        private void Repair(CompanionAI companion, Inventory inventory)
        {
            WearNTear wear = _task.Target as WearNTear;
            if (wear == null)
            {
                BackToScanning();
                return;
            }

            if (wear.GetHealthPercentage() >= Mathf.Min(CompanionBaseCareConfig.RepairBelow, 0.9999f))
            {
                BackToScanning();
                return;
            }

            Vector3 pos = wear.transform.position;
            Humanoid humanoid = companion.JobHumanoid;
            if (humanoid == null)
            {
                BackToScanning();
                return;
            }

            // As mesmas checagens do jogo no instante do golpe (o companheiro pode ter se afastado).
            Piece piece = wear.GetComponent<Piece>();
            bool noBench = ZoneSystem.instance != null &&
                           ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench);
            if (!noBench && piece != null && piece.m_craftingStation != null &&
                CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, companion.JobPosition) == null)
            {
                Skip(wear, SkipTargetSeconds, "sem a estacao de craft por perto");
                BackToScanning();
                return;
            }

            if (!PrivateArea.CheckAccess(pos, 0f, false, false))
            {
                Skip(wear, SkipTargetSeconds, "area privada sem acesso");
                BackToScanning();
                return;
            }

            ItemDrop.ItemData hammer = FindHammer(inventory);
            if (hammer == null)
            {
                Skip(wear, SkipTargetSeconds, "sem martelo");
                LogEvent(
                    "upkeep|nohammer",
                    "[ModIA] Base: sem martelo no inventario; nao consigo reparar (Base/FornecerMartelo).");
                BackToScanning();
                return;
            }

            if (!humanoid.IsItemEquiped(hammer))
            {
                if (!_holdingHammer)
                {
                    _previousWeapon = humanoid.GetCurrentWeapon();
                    _holdingHammer = true;
                }

                // O jogo recusa equipar durante ataque/dodge; sem este teto o companheiro ficaria
                // tentando pegar o martelo para sempre (a peça sai da fila por um tempo).
                _equipAttempts++;
                humanoid.EquipItem(hammer, triggerEquipEffects: false);

                if (_equipAttempts >= MaxEquipAttempts)
                {
                    _equipAttempts = 0;
                    Skip(wear, SkipTargetSeconds, "nao consegui equipar o martelo");
                    BackToScanning();
                    return;
                }

                _workTimer = 0.6f; // deixa a troca de item terminar antes do golpe
                return;
            }

            _equipAttempts = 0;

            // Stamina do golpe (a mesma conta do Player.GetBuildStamina, com a perícia da estação
            // de construção do martelo): sem fôlego ele espera em vez de bater no vazio.
            float cost = SwingStamina(companion, hammer);
            CompanionStamina stamina = companion.GetComponent<CompanionStamina>();
            if (stamina != null && stamina.Stamina < cost)
            {
                SetStatus("recuperando o folego");
                _workTimer = 1f;
                return;
            }

            companion.JobLookAt(pos);
            if (!companion.JobIsLookingAt(pos, 25f))
            {
                return;
            }

            if (!wear.Repair())
            {
                // O jogo tem 1 s de espera entre reparos (ou a peça já foi reparada por outro).
                _workTimer = 1f;
                return;
            }

            Character character = companion.JobCharacter;
            if (character != null && character.m_zanim != null && hammer.m_shared.m_attack != null &&
                !string.IsNullOrEmpty(hammer.m_shared.m_attack.m_attackAnimation))
            {
                character.m_zanim.SetTrigger(hammer.m_shared.m_attack.m_attackAnimation);
            }

            if (piece != null)
            {
                piece.m_placeEffect.Create(
                    pos, piece.transform.rotation, null, 1f, -1,
                    character != null ? character.GetZDOID() : ZDOID.None);
            }

            if (stamina != null)
            {
                stamina.Drain(cost);
            }

            if (hammer.m_shared.m_useDurability)
            {
                hammer.m_durability = Mathf.Max(
                    0f, hammer.m_durability - hammer.m_shared.m_useDurabilityDrain * Game.m_durabilityRate);
            }

            AddProgress();
            ShowProgress("Reparei", PieceName(piece));
            LogEvent("upkeep|repair", $"[ModIA] Base: reparei {PieceName(piece)} (stamina -{cost:0}).");
            BackToScanning();
        }

        /// <summary>
        /// Reabastece a fogueira com o combustível do inventário, uma unidade por vez (como o
        /// jogador), até a fração configurada do máximo.
        /// </summary>
        private void Stoke(CompanionAI companion, Inventory inventory)
        {
            Fireplace fire = _task.Target as Fireplace;
            ZNetView netView = fire != null ? fire.GetComponent<ZNetView>() : null;
            if (!JobStations.ReadyForRpc(netView))
            {
                BackToScanning();
                return;
            }

            int target = FuelTarget(fire.m_maxFuel);
            int added = 0;
            float fuel = netView.GetZDO().GetFloat(ZDOVars.s_fuel);
            while (added < _task.Count && fuel + added < target &&
                   ConsumeOne(inventory, _task.ItemPrefab))
            {
                netView.InvokeRPC("RPC_AddFuel");
                added++;
            }

            InteractAnimation(companion);

            if (added > 0)
            {
                AddProgress();
                ShowProgress("Abasteci", $"+{added} {_task.ItemPrefab}");
                LogEvent(
                    "upkeep|fuel|" + _task.ItemPrefab,
                    $"[ModIA] Base: pus {added}x '{_task.ItemPrefab}' em {PieceName(fire)} " +
                    $"(mantido em {CompanionBaseCareConfig.FuelLevel * 100f:0}% do maximo).");
            }
            else
            {
                LogEvent(
                    "upkeep|nofuel|" + _task.ItemPrefab,
                    $"[ModIA] Base: sem '{_task.ItemPrefab}' no inventario para abastecer {PieceName(fire)}.");
            }

            BackToScanning();
        }

        /// <summary>Esvazia a colmeia pelo <c>RPC_Extract</c> (o mel cai no chão perto dela).</summary>
        private void ExtractHoney(CompanionAI companion)
        {
            Beehive hive = _task.Target as Beehive;
            ZNetView netView = hive != null ? hive.GetComponent<ZNetView>() : null;
            if (!JobStations.ReadyForRpc(netView))
            {
                BackToScanning();
                return;
            }

            InteractAnimation(companion);
            netView.InvokeRPC("RPC_Extract");

            // A colmeia sai da fila por um tempo: se o RPC não tiver aplicado (sem posse, por
            // exemplo), sem isto o companheiro voltaria à MESMA colmeia a cada varredura. Sem log:
            // "esvaziei a colmeia" já foi dito, e a espera é normal (o mel leva ~10 s por unidade).
            Skip(hive, HiveSkipSeconds, "acabei de esvaziar", log: false);

            _state = UpkeepState.Collecting;
            _workTimer = CollectDelay;
            SetStatus("esvaziando a colmeia");
            LogEvent(
                "upkeep|hive",
                $"[ModIA] Base: esvaziei a colmeia (o mel caiu em {_task.Output.x:0.#}, {_task.Output.z:0.#}).");
        }

        /// <summary>Junta as pilhas parciais do mesmo item dentro do baú (tier mais baixo).</summary>
        private void Merge(CompanionAI companion)
        {
            Container chest = _task.To;
            Inventory chestInventory = chest != null ? chest.GetInventory() : null;
            if (chestInventory == null)
            {
                BackToScanning();
                return;
            }

            InteractAnimation(companion);
            int merged = MergePartials(chestInventory, _task.ItemKey);
            if (merged > 0)
            {
                AddProgress();
                ShowProgress("Juntei", $"{merged} pilha(s)");
                LogEvent(
                    "upkeep|merge",
                    $"[ModIA] Base: juntei {merged} pilha(s) em {CompanionDeposit.ChestName(chest)}.");
            }
            else
            {
                Skip(chest, SkipChestSeconds, "nada para juntar");
            }

            BackToScanning();
        }

        /// <summary>Cataloga o conteúdo dos baús da base por item (chave = nome + qualidade + nível).</summary>
        private static void BuildStock()
        {
            s_stock.Clear();

            for (int i = 0; i < s_chests.Count; i++)
            {
                Container chest = s_chests[i];
                Inventory chestInventory = chest != null ? chest.GetInventory() : null;
                if (chestInventory == null)
                {
                    continue;
                }

                List<ItemDrop.ItemData> items = chestInventory.GetAllItems();
                for (int k = 0; k < items.Count; k++)
                {
                    ItemDrop.ItemData item = items[k];
                    if (item == null || item.m_shared == null || !IsSortable(item))
                    {
                        continue;
                    }

                    string key = Key(item);
                    if (!s_stock.TryGetValue(key, out Stock stock))
                    {
                        stock = new Stock();
                        s_stock[key] = stock;
                    }

                    stock.Count.TryGetValue(chest, out int count);
                    stock.Count[chest] = count + item.m_stack;

                    if (item.m_shared.m_maxStackSize > 1 &&
                        item.m_stack < item.m_shared.m_maxStackSize)
                    {
                        stock.Partial.TryGetValue(chest, out int partial);
                        stock.Partial[chest] = partial + 1;
                    }
                }
            }
        }

        /// <summary>
        /// Baú de destino de um item: o que <b>já tem mais</b> dele (empate: o mais perto do centro
        /// da base, para a escolha nunca ficar alternando entre dois baús).
        /// </summary>
        private Container BestDestination(Stock stock)
        {
            Container best = null;
            int most = -1;
            float bestDistance = float.MaxValue;

            foreach (KeyValuePair<Container, int> candidate in stock.Count)
            {
                if (candidate.Key == null)
                {
                    continue;
                }

                float distance = Vector3.Distance(_anchor, candidate.Key.transform.position);
                if (candidate.Value > most || (candidate.Value == most && distance < bestDistance))
                {
                    most = candidate.Value;
                    best = candidate.Key;
                    bestDistance = distance;
                }
            }

            return best;
        }

        /// <summary>O baú aceita mais unidades deste item (vaga livre ou pilha parcial do mesmo)?</summary>
        private static bool RoomIn(Container chest, string key)
        {
            Inventory inventory = chest != null ? chest.GetInventory() : null;
            if (inventory == null)
            {
                return false;
            }

            if (inventory.GetEmptySlots() > 0)
            {
                return true;
            }

            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (item != null && Key(item) == key && item.m_stack < item.m_shared.m_maxStackSize)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// O item pode ser movido entre baús: empilhável e permitido por <c>Base/OrdenarItens</c>
        /// (equipamento/ferramenta só se estiverem na lista).
        /// </summary>
        private static bool IsSortable(ItemDrop.ItemData item)
        {
            if (item.m_dropPrefab == null || item.m_shared == null)
            {
                return false;
            }

            string prefab = Utils.GetPrefabName(item.m_dropPrefab.name);
            if (!CompanionBaseCareConfig.AllowsSort(prefab))
            {
                return false;
            }

            return item.m_shared.m_maxStackSize > 1 || CompanionBaseCareConfig.SortItems != "*";
        }

        /// <summary>Chave de pilha do item (mesma regra do <c>Inventory.FindFreeStackItem</c>).</summary>
        private static string Key(ItemDrop.ItemData item)
        {
            return item.m_shared.m_name + "|" + item.m_quality + "|" + item.m_worldLevel;
        }

        // ---------------------------------------------------------------------
        // Ordenação: pegar no baú de origem e pôr no de destino
        // ---------------------------------------------------------------------

        private void TickSortTake(CompanionAI companion, float dt)
        {
            if (companion.JobHasCombatTarget)
            {
                SetStatus("combate");
                companion.JobStopMoving();
                return;
            }

            if (!TargetAlive())
            {
                BackToScanning();
                return;
            }

            switch (Approach(companion, _task.Spot, ChestReach, dt))
            {
                case ApproachResult.Arrived:
                    TakeForSort(companion);
                    return;

                case ApproachResult.GaveUp:
                    Skip(_task.From, SkipChestSeconds, "nao consegui chegar no bau");
                    BackToScanning();
                    return;
            }
        }

        private void TakeForSort(CompanionAI companion)
        {
            Container from = _task.From;
            Inventory source = from != null ? from.GetInventory() : null;
            Inventory inventory = companion.JobInventory;
            if (source == null || inventory == null)
            {
                BackToScanning();
                return;
            }

            companion.JobStopMoving();
            int want = Mathf.Min(_task.Count, CountKey(source, _task.ItemKey));
            int taken = MoveItems(source, inventory, _task.ItemKey, want, true);
            _carried = taken;

            if (taken <= 0)
            {
                Skip(from, SkipChestSeconds, "nada para levar (mochila cheia?)");
                BackToScanning();
                return;
            }

            InteractAnimation(companion);
            LogEvent(
                "upkeep|sort|take",
                $"[ModIA] Base: tirei {taken} {ItemName(_task.ItemKey)} de " +
                $"{CompanionDeposit.ChestName(from)} para levar ao bau com mais deles " +
                $"({CompanionDeposit.ChestName(_task.To)}).");

            _state = UpkeepState.SortPut;
            _bestDistance = float.MaxValue;
            _noProgress = 0f;
            SetStatus($"levando {taken} item(ns) ao bau certo");
        }

        private void TickSortPut(CompanionAI companion, float dt)
        {
            if (companion.JobHasCombatTarget)
            {
                SetStatus("combate");
                companion.JobStopMoving();
                return;
            }

            Container to = _task.To;
            if (to == null || to.GetInventory() == null)
            {
                StowBack(companion, "o bau de destino sumiu");
                return;
            }

            Vector3 spot = to.transform.position;
            switch (Approach(companion, spot, ChestReach, dt))
            {
                case ApproachResult.Arrived:
                    PutForSort(companion, to);
                    return;

                case ApproachResult.GaveUp:
                    StowBack(companion, "nao consegui chegar no bau de destino");
                    return;
            }
        }

        private void PutForSort(CompanionAI companion, Container to)
        {
            Inventory inventory = companion.JobInventory;
            Inventory destination = to.GetInventory();
            if (inventory == null || destination == null)
            {
                StowBack(companion, "sem inventario");
                return;
            }

            companion.JobStopMoving();
            InteractAnimation(companion);

            int put = MoveItems(inventory, destination, _task.ItemKey, _carried, false);
            _carried -= put;

            int merged = CompanionBaseCareConfig.MergeStacks
                ? MergePartials(destination, _task.ItemKey)
                : 0;

            if (put > 0 || merged > 0)
            {
                AddProgress();
                ShowProgress("Arrumei", $"{put} item(ns)");
                LogEvent(
                    "upkeep|sort|put",
                    $"[ModIA] Base: pus {put} {ItemName(_task.ItemKey)} em " +
                    $"{CompanionDeposit.ChestName(to)}" +
                    (merged > 0 ? $" e juntei {merged} pilha(s)" : string.Empty) + ".");
            }

            if (put <= 0 && merged <= 0)
            {
                Skip(to, SkipChestSeconds, "o bau nao aceitou");
            }

            // O que não coube volta para a origem (o companheiro não fica carregando o baú alheio).
            if (_carried > 0)
            {
                StowBack(companion, "nao coube no bau de destino");
                return;
            }

            BackToScanning();
        }

        /// <summary>Devolve o que ficou na mochila para o baú de origem (ou avisa que ficou com ele).</summary>
        private void StowBack(CompanionAI companion, string reason)
        {
            Inventory inventory = companion.JobInventory;
            Container from = _task.From;
            Inventory source = from != null ? from.GetInventory() : null;

            if (_carried > 0 && inventory != null && source != null)
            {
                int back = MoveItems(inventory, source, _task.ItemKey, _carried, false);
                _carried -= back;
            }

            if (_carried > 0)
            {
                LogEvent(
                    "upkeep|sort|leftover",
                    $"[ModIA] Base: sobrou {_carried} item(ns) na mochila ({reason}); ele leva na " +
                    "proxima ida ao bau.");
            }

            _carried = 0;
            BackToScanning();
        }

        // ---------------------------------------------------------------------
        // Catando o mel da colmeia
        // ---------------------------------------------------------------------

        private void TickCollecting(CompanionAI companion, float dt)
        {
            _workTimer -= dt;
            if (_workTimer > 0f)
            {
                companion.JobStopMoving();
                return;
            }

            s_honey.Clear();
            if (!string.IsNullOrEmpty(_task.ItemPrefab))
            {
                s_honey.Add(_task.ItemPrefab);
            }

            int picked = JobStations.PickupDrops(
                companion, _task.Output, JobStations.DropRadius, s_honey, 16);

            if (picked > 0)
            {
                AddProgress();
                ShowProgress("Peguei", $"{picked} mel");
                LogEvent("upkeep|honey", $"[ModIA] Base: catei {picked} mel da colmeia.");
            }
            else if (companion.JobIsPackFull)
            {
                ReportPackFull(companion, dt);
            }

            BackToScanning();
        }

        // ---------------------------------------------------------------------
        // Voltando para o centro da base (modo contínuo)
        // ---------------------------------------------------------------------

        private void TickHome(CompanionAI companion, float dt)
        {
            if (companion.JobHasCombatTarget)
            {
                SetStatus("combate");
                companion.JobStopMoving();
                return;
            }

            Vector3 center = new Vector3(_anchor.x, companion.JobPosition.y, _anchor.z);
            switch (Approach(companion, center, HomeArrive, dt))
            {
                case ApproachResult.Arrived:
                case ApproachResult.GaveUp:
                    BackToScanning();
                    return;
            }
        }

        // ---------------------------------------------------------------------
        // Martelo de construção
        // ---------------------------------------------------------------------

        /// <summary>
        /// Martelo no inventário (o item com <c>m_buildPieces</c> cujo nome contém "Hammer"): é o
        /// mesmo critério da referência — o jogo não considera o martelo uma arma, então
        /// <c>GetCurrentWeapon</c> não serve para achá-lo.
        /// </summary>
        private static ItemDrop.ItemData FindHammer(Inventory inventory)
        {
            if (inventory == null)
            {
                return null;
            }

            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            ItemDrop.ItemData best = null;
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                ItemDrop.ItemData.SharedData shared = item != null ? item.m_shared : null;
                if (shared == null || shared.m_buildPieces == null)
                {
                    continue;
                }

                string table = shared.m_buildPieces.name;
                if (string.IsNullOrEmpty(table) ||
                    table.IndexOf("Hammer", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (shared.m_useDurability && item.m_durability <= 0f)
                {
                    continue;
                }

                if (best == null || item.m_durability > best.m_durability)
                {
                    best = item;
                }
            }

            return best;
        }

        /// <summary>
        /// Custo de stamina de um golpe de martelo: a mesma conta do <c>Player.GetBuildStamina</c>
        /// (custo de ataque da ferramenta reduzido pela perícia da estação de construção).
        /// </summary>
        private static float SwingStamina(CompanionAI companion, ItemDrop.ItemData hammer)
        {
            float cost = hammer.m_shared.m_attack != null ? hammer.m_shared.m_attack.m_attackStamina : 0f;

            PieceTable table = hammer.m_shared.m_buildPieces;
            Character character = companion != null ? companion.JobCharacter : null;
            if (table != null && table.m_skill != Skills.SkillType.None && character != null)
            {
                cost -= cost * 0.5f * character.GetSkillFactor(table.m_skill);
            }

            return Mathf.Max(0f, cost);
        }

        /// <summary>Guarda o martelo (e devolve a arma que ele usava) quando não há mais reparo.</summary>
        private void PutHammerAway(CompanionAI companion)
        {
            if (!_holdingHammer)
            {
                return;
            }

            _holdingHammer = false;

            Humanoid humanoid = companion != null ? companion.JobHumanoid : null;
            Inventory inventory = companion != null ? companion.JobInventory : null;

            ItemDrop.ItemData hammer = FindHammer(inventory);
            if (humanoid != null && hammer != null && humanoid.IsItemEquiped(hammer))
            {
                humanoid.UnequipItem(hammer, triggerEquipEffects: false);
            }

            ItemDrop.ItemData previous = _previousWeapon;
            _previousWeapon = null;

            if (humanoid != null && previous != null && inventory != null &&
                inventory.ContainsItem(previous) && !humanoid.IsItemEquiped(previous))
            {
                humanoid.EquipItem(previous, triggerEquipEffects: false);
            }
        }

        // ---------------------------------------------------------------------
        // Apoio (itens, baús e estado)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Move até <paramref name="max"/> unidades do item <paramref name="key"/> de um inventário
        /// para o outro, uma pilha por vez e só quando o destino aceita tudo
        /// (<c>CanAddItem</c>) — nada de deixar item no limbo. Com <paramref name="intoBag"/> a
        /// mochila também respeita o teto de peso (<c>Companheiro/PesoMaximo</c>).
        /// </summary>
        private static int MoveItems(Inventory from, Inventory to, string key, int max, bool intoBag)
        {
            if (from == null || to == null || max <= 0)
            {
                return 0;
            }

            int moved = 0;
            List<ItemDrop.ItemData> items = from.GetAllItems();
            for (int i = 0; i < items.Count && moved < max; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (item == null || item.m_shared == null || Key(item) != key)
                {
                    continue;
                }

                int amount = Mathf.Min(item.m_stack, max - moved);

                if (intoBag && CompanionInventory.MaxWeight > 0f && item.m_shared.m_weight > 0f)
                {
                    float room = CompanionInventory.MaxWeight - CompanionInventory.GetWeight(to);
                    amount = Mathf.Min(amount, Mathf.FloorToInt(room / item.m_shared.m_weight));
                }

                while (amount > 0 && !to.CanAddItem(item, amount))
                {
                    amount--;
                }

                if (amount <= 0)
                {
                    break;
                }

                ItemDrop.ItemData copy = item.Clone();
                copy.m_stack = amount;
                if (!to.AddItem(copy))
                {
                    break;
                }

                from.RemoveItem(item, amount);
                moved += amount;
            }

            return moved;
        }

        /// <summary>
        /// Junta as pilhas parciais do mesmo item dentro de um baú: tira a menor pilha e devolve
        /// (o <c>AddItem</c> do jogo completa as outras antes de abrir uma vaga nova). Devolve quantas
        /// pilhas foram juntadas.
        /// </summary>
        private static int MergePartials(Inventory inventory, string key)
        {
            if (inventory == null)
            {
                return 0;
            }

            int merged = 0;
            for (int guard = 0; guard < MergeGuard; guard++)
            {
                ItemDrop.ItemData smallest = null;
                int partials = 0;

                List<ItemDrop.ItemData> items = inventory.GetAllItems();
                for (int i = 0; i < items.Count; i++)
                {
                    ItemDrop.ItemData item = items[i];
                    if (item == null || item.m_shared == null || item.m_shared.m_maxStackSize <= 1 ||
                        item.m_stack >= item.m_shared.m_maxStackSize || Key(item) != key)
                    {
                        continue;
                    }

                    partials++;
                    if (smallest == null || item.m_stack < smallest.m_stack)
                    {
                        smallest = item;
                    }
                }

                if (partials < 2 || smallest == null)
                {
                    break;
                }

                // Nunca perder item por causa da arrumação: o clone entra primeiro e, se o jogo
                // recusar (não deveria: acabamos de liberar uma vaga), a pilha volta como estava.
                ItemDrop.ItemData copy = smallest.Clone();
                int stack = smallest.m_stack;
                inventory.RemoveItem(smallest);
                if (!inventory.AddItem(copy))
                {
                    Plugin.Log.LogWarning(
                        "[ModIA] Base: nao consegui juntar uma pilha; o item voltou para o bau.");
                    copy.m_stack = stack;
                    inventory.AddItem(copy);
                    break;
                }

                merged++;
            }

            return merged;
        }

        /// <summary>Quantas unidades deste item o inventário tem (pela chave de pilha).</summary>
        private static int CountKey(Inventory inventory, string key)
        {
            if (inventory == null)
            {
                return 0;
            }

            int count = 0;
            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (item != null && item.m_shared != null && Key(item) == key)
                {
                    count += item.m_stack;
                }
            }

            return count;
        }

        /// <summary>Quantas unidades deste prefab o inventário tem.</summary>
        private static int CountPrefab(Inventory inventory, string prefab)
        {
            if (inventory == null || string.IsNullOrEmpty(prefab))
            {
                return 0;
            }

            int count = 0;
            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (item != null && item.m_dropPrefab != null &&
                    Utils.GetPrefabName(item.m_dropPrefab.name) == prefab)
                {
                    count += item.m_stack;
                }
            }

            return count;
        }

        /// <summary>Consome uma unidade do item <paramref name="prefab"/> do inventário.</summary>
        private static bool ConsumeOne(Inventory inventory, string prefab)
        {
            if (inventory == null || string.IsNullOrEmpty(prefab))
            {
                return false;
            }

            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (item != null && item.m_dropPrefab != null &&
                    Utils.GetPrefabName(item.m_dropPrefab.name) == prefab)
                {
                    inventory.RemoveItem(item, 1);
                    return true;
                }
            }

            return false;
        }

        /// <summary>Combustível que ele mantém na fogueira (fração do máximo, nunca menos de uma unidade).</summary>
        private static int FuelTarget(float maxFuel)
        {
            return Mathf.Max(1, Mathf.CeilToInt(maxFuel * CompanionBaseCareConfig.FuelLevel));
        }

        /// <summary>Marca um alvo como "pule isto por um tempo" (não deu para chegar/usar).</summary>
        private void Skip(Component target, float seconds, string reason, bool log = true)
        {
            if (target == null)
            {
                return;
            }

            if (_skip.Count >= MaxSkipEntries)
            {
                _skip.Clear();
            }

            _skip[target.GetInstanceID()] = Time.time + seconds;

            if (log)
            {
                LogEvent(
                    "upkeep|skip|" + target.GetInstanceID(),
                    $"[ModIA] Base: pulando {PieceName(target)} por {seconds:0} s ({reason}).");
            }
        }

        /// <summary>O alvo está na lista de "pule por um tempo" (e ainda vale)?</summary>
        private bool Skipped(Component target)
        {
            if (target == null)
            {
                return true;
            }

            int id = target.GetInstanceID();
            if (!_skip.TryGetValue(id, out float until))
            {
                return false;
            }

            if (Time.time < until)
            {
                return true;
            }

            _skip.Remove(id);
            return false;
        }

        /// <summary>Volta para a varredura (fim de uma tarefa ou desistência dela).</summary>
        private void BackToScanning()
        {
            _state = UpkeepState.Scanning;
            _scanTimer = 0.2f;
            _task = default;
            _bestDistance = float.MaxValue;
            _noProgress = 0f;
        }

        /// <summary>Animação de interação (o mesmo gatilho que o jogo usa ao usar uma peça).</summary>
        private static void InteractAnimation(CompanionAI companion)
        {
            Character character = companion != null ? companion.JobCharacter : null;
            if (character != null && character.m_zanim != null)
            {
                character.m_zanim.SetTrigger("interact");
            }
        }

        /// <summary>Nome localizado da peça (ou do alvo) para o HUD/log.</summary>
        private static string PieceName(Component target)
        {
            if (target == null)
            {
                return "a peca";
            }

            Piece piece = target.GetComponent<Piece>();
            string name = piece != null ? piece.m_name : null;
            Localization localization = Localization.instance;
            if (!string.IsNullOrEmpty(name) && localization != null)
            {
                name = localization.Localize(name);
            }

            return string.IsNullOrEmpty(name) ? Utils.GetPrefabName(target.gameObject) : name;
        }

        /// <summary>
        /// Nome localizado do item de uma chave de pilha (<c>$item_wood|1|0</c> → <c>Madeira</c>).
        /// Só roda em log/HUD (deduplicado), então a <c>Substring</c> não é um problema.
        /// </summary>
        private static string ItemName(string key)
        {
            int bar = key != null ? key.IndexOf('|') : -1;
            string token = bar >= 0 ? key.Substring(0, bar) : key;

            Localization localization = Localization.instance;
            if (!string.IsNullOrEmpty(token) && localization != null)
            {
                token = localization.Localize(token);
            }

            return string.IsNullOrEmpty(token) ? "item" : token;
        }

        /// <summary>Onde ele está cuidando (casa definida ou um círculo em volta de onde começou).</summary>
        private string DescribeAnchor(HomeZone home)
        {
            return home.IsValid
                ? $"na casa ({home.Describe()})"
                : $"em volta de {_anchor.x:0.#}, {_anchor.z:0.#}";
        }

        /// <summary>Descrição curta da tarefa (HUD/console/log).</summary>
        private static string Describe(UpkeepTask task)
        {
            switch (task.Kind)
            {
                case UpkeepTaskKind.Repair:
                    return $"reparando {PieceName(task.Target)}";

                case UpkeepTaskKind.Fuel:
                    return $"abastecendo {PieceName(task.Target)} com {task.ItemPrefab}";

                case UpkeepTaskKind.Hive:
                    return "esvaziando a colmeia";

                case UpkeepTaskKind.Sort:
                    return $"levando {task.Count} item(ns) para o bau certo";

                case UpkeepTaskKind.Merge:
                    return "juntando pilhas num bau";

                default:
                    return "procurando o que cuidar";
            }
        }

        /// <summary>Estoque de um item entre os baús da base (uma linha da varredura de ordenação).</summary>
        private sealed class Stock
        {
            /// <summary>Quanto de cada baú (unidades).</summary>
            internal readonly Dictionary<Container, int> Count = new Dictionary<Container, int>();

            /// <summary>Quantas pilhas parciais de cada baú (para juntar).</summary>
            internal readonly Dictionary<Container, int> Partial = new Dictionary<Container, int>();
        }
    }
}
