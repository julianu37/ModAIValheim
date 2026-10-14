using System.Collections.Generic;
using UnityEngine;

namespace ModAIValheim.Companion.Jobs
{
    /// <summary>
    /// <b>TRABALHO: COZINHAR / PREPARAR BEBIDAS</b> (Fase 2.16b) — o companheiro opera as estações
    /// de cozinha por perto (fogueira/espeto/forno de cozinha, barril de fermentação e o
    /// caldeirão/mesa de preparo):
    /// <list type="number">
    ///   <item><description><b>abastece</b> com combustível e comida do próprio inventário;</description></item>
    ///   <item><description><b>espera</b> o preparo (mantendo o fogo aceso);</description></item>
    ///   <item><description><b>retira</b> o prato/bebida pronto (antes de queimar) e cata o que a
    ///   estação larga no chão.</description></item>
    /// </list>
    ///
    /// <para>
    /// <b>Como fala com as estações (API da 1.0.17):</b> nada de <c>Interact</c> — o
    /// <c>CookingStation.Interact</c> e o <c>Fireplace.Interact</c> mexem em perícia/estatística do
    /// <c>Player.m_localPlayer</c> e no <c>InventoryGui</c>, o que não é do companheiro. O caminho
    /// certo é o <b>RPC do ZNetView</b>, que é o que o próprio jogo chama por baixo do Interact:
    /// <c>RPC_AddItem(nome, cheated)</c> põe comida no espeto, <c>RPC_AddFuel</c> põe combustível,
    /// <c>RPC_RemoveDoneItem(pos, 1)</c> tira o que ficou pronto e <c>RPC_Tap</c> tira a bebida do
    /// barril. O estado é lido direto do ZDO (<c>slotN</c>/<c>slotstatusN</c> no espeto,
    /// <c>Content</c>/<c>StartTime</c> no barril), com as chaves já em <c>int</c> (sem alocar
    /// string por tick).
    /// </para>
    ///
    /// <para>
    /// <b>Prioridade das tarefas</b> (a mesma da referência): retirar o que está pronto/queimando →
    /// tornear o barril pronto → cozinhar no caldeirão → reabastecer o fogo → pôr comida no espeto →
    /// encher o barril vazio. Assim ele nunca deixa a comida queimar no pé nem o fogo apagar com o
    /// espeto cheio.
    /// </para>
    ///
    /// <para>
    /// Adaptado (lógica) de <c>src/Companions/Jobs/CookController.cs</c> (fases de cozinhar/fermentar/
    /// catar, prioridade "pronto antes de tudo", <c>AddToCooker</c> com
    /// <c>ConsumeOneFromInventory</c> + <c>RPC_AddItem</c>, <c>UpdateCooking</c> com
    /// <c>RPC_RemoveDoneItem</c> e <c>PickUpDrops</c>, <c>UpdateFermenting</c> com
    /// <c>RPC_AddItem(hash)</c>/<c>RPC_Tap</c>, e <c>UpdateCrafting</c> para o caldeirão) e de
    /// <c>src/Companions/Jobs/CookFuel.cs</c> (manter o combustível das fogueiras/espetos em uma
    /// fração do máximo — <c>FuelTarget</c>, <c>HeatSources</c>, <c>Stoke</c>) do <i>Offline
    /// Companions Reborn</i> (MIT — ver <c>THIRD_PARTY_NOTICES.md</c>). A implementação aqui é
    /// própria (uma tarefa por vez na máquina de estados do <see cref="CompanionJob"/>, com o
    /// movimento pelo <see cref="MovementOwner.Cook"/> e buffers reaproveitados), sem cópia de
    /// arquivo.
    /// </para>
    ///
    /// <para>
    /// <b>Fora do núcleo:</b> a <b>lista curada de receitas</b> da referência (<c>CookCatalog</c>,
    /// com foco por chefe e prioridades) — aqui o caldeirão escolhe a primeira receita habilitada
    /// da estação para a qual o companheiro tem todos os ingredientes; <b>colmeias</b>
    /// (<c>Beehive</c>/<c>UpkeepHoney</c>) e a <b>manutenção da base</b>, que são da Fase 2.17;
    /// e <b>buscar combustível/comida em baús</b> — como pede a tarefa, os insumos vêm do
    /// inventário do companheiro.
    /// </para>
    /// </summary>
    internal sealed class CookJob : CompanionJob
    {
        private enum CookState
        {
            /// <summary>Varrendo as estações por perto.</summary>
            Scanning,

            /// <summary>Andando até a estação escolhida.</summary>
            Moving,

            /// <summary>Na estação: abastecendo, retirando ou torneirando.</summary>
            Working,

            /// <summary>Catando o que a estação largou no chão.</summary>
            Collecting,
        }

        private enum CookTaskKind
        {
            None,

            /// <summary>Tirar o prato/bebida pronto (ou queimando) do espeto/forno.</summary>
            Take,

            /// <summary>Tornear o barril de fermentação pronto.</summary>
            Tap,

            /// <summary>Cozinhar uma receita no caldeirão/mesa de preparo.</summary>
            Craft,

            /// <summary>Reabastecer o combustível (do espeto/forno ou da fogueira embaixo dele).</summary>
            Stoke,

            /// <summary>Pôr comida no espeto/forno.</summary>
            FillCooker,

            /// <summary>Pôr a base de bebida no barril vazio.</summary>
            FillFermenter,

            /// <summary>Catar o que a estação largou e ficou no chão (rede de segurança).</summary>
            Gather,
        }

        /// <summary>A tarefa escolhida na varredura (uma por vez).</summary>
        private struct CookTask
        {
            internal CookTaskKind Kind;
            internal Component Station;
            internal string Item;
            internal int Count;
            internal Vector3 Spot;
            internal Vector3 Output;
        }

        /// <summary>Espera (s) antes de agir ao chegar na estação (animação de interação).</summary>
        private const float WorkDelay = 0.4f;

        /// <summary>Espera (s) depois de largar/retirar item antes de catar (o RPC é assíncrono).</summary>
        private const float CollectDelay = 0.6f;

        /// <summary>
        /// Espera (s) depois de <b>tornear o barril</b>: o jogo só larga as garrafas
        /// <c>m_tapDelay</c> (1,5 s) depois do <c>RPC_Tap</c> — catar antes disso não acharia nada.
        /// </summary>
        private const float TapCollectDelay = 2f;

        /// <summary>Varreduras seguidas sem nada para fazer (e nada preparando) antes de encerrar.</summary>
        private const int EmptyScansBeforeStop = 3;

        /// <summary>Tempo (s) sem chegar mais perto que faz desistir da estação.</summary>
        private const float NoProgressSeconds = 8f;

        /// <summary>Raio (m) em que procuramos a fogueira que esquenta o espeto.</summary>
        private const float HeatRadius = 2.5f;

        /// <summary>Slots lidos do ZDO de um espeto/forno (as chaves já vêm prontas em int).</summary>
        private const int SlotKeyCount = 16;

        private const int PieceBufferSize = 256;
        private const int HeatBufferSize = 32;

        private static readonly List<Piece> s_pieces = new List<Piece>(PieceBufferSize);
        private static readonly List<Piece> s_heatPieces = new List<Piece>(HeatBufferSize);
        private static readonly int[] s_slotKeys = BuildSlotKeys("slot");
        private static readonly int[] s_statusKeys = BuildSlotKeys("slotstatus");

        private CookState _state = CookState.Scanning;
        private CookTask _task;
        private float _scanTimer;
        private float _workTimer;
        private float _waitSeconds;
        private int _emptyScans;
        private float _bestDistance = float.MaxValue;
        private float _noProgress;

        internal override JobKind Kind => JobKind.Cook;

        internal override string DisplayName => "Cozinhar";

        internal override string CommandName => "cook";

        protected override bool OnStart(CompanionAI companion, out string error)
        {
            error = null;

            if (companion.JobHumanoid == null || companion.JobInventory == null)
            {
                error = "[ModIA] O companheiro esta sem corpo/inventario; cozinhar cancelado.";
                return false;
            }

            if (!CompanionJobsConfig.CookTakeDone && !CompanionJobsConfig.CookFuel &&
                !CompanionJobsConfig.CookCauldron)
            {
                error = "[ModIA] Cozinhar esta desligado no .cfg (Jobs/Cozinha* = false).";
                return false;
            }

            _state = CookState.Scanning;
            _task = default;
            _scanTimer = 0f;
            _workTimer = 0f;
            _waitSeconds = 0f;
            _emptyScans = 0;
            _bestDistance = float.MaxValue;
            _noProgress = 0f;

            SetStatus("procurando estacao de cozinha");
            LogEvent(
                "cook|start",
                $"[ModIA] Cozinhar: procurando estacoes num raio de {CompanionJobsConfig.CookRadius:0} m " +
                $"(retirar pronto = {CompanionJobsConfig.CookTakeDone}, combustivel = " +
                $"{CompanionJobsConfig.CookFuel} ate {CompanionJobsConfig.CookFuelLevel * 100f:0}%, " +
                $"caldeirao = {CompanionJobsConfig.CookCauldron}).");
            return true;
        }

        protected override void OnStop(CompanionAI companion, string reason)
        {
            if (companion != null)
            {
                companion.JobStopMoving();
            }

            _task = default;
            SetStatus("encerrado");
        }

        protected override void OnTick(CompanionAI companion, float dt)
        {
            switch (_state)
            {
                case CookState.Moving:
                    TickMoving(companion, dt);
                    break;

                case CookState.Working:
                    TickWorking(companion, dt);
                    break;

                case CookState.Collecting:
                    TickCollecting(companion, dt);
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
            // Meta de quantidade atingida (modia_job cook 10): o trabalho acabou.
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

            _scanTimer = CompanionJobsConfig.ScanInterval;

            if (PickTask(companion, out CookTask task, out bool busy))
            {
                _task = task;
                _bestDistance = float.MaxValue;
                _noProgress = 0f;
                _state = CookState.Moving;
                SetStatus(Describe(task));
                LogEvent(
                    "cook|task|" + task.Kind + "|" + task.Item,
                    $"[ModIA] Cozinhar: {Describe(task)} " +
                    $"({Vector3.Distance(companion.JobPosition, task.Spot):0.#} m).");
                return;
            }

            // Nada a fazer agora, mas tem coisa preparando: ele fica por perto cuidando do fogo —
            // é o "esperar o preparo e retirar quando ficar pronto" da tarefa.
            if (busy)
            {
                _emptyScans = 0;
                _waitSeconds += CompanionJobsConfig.ScanInterval;
                if (_waitSeconds >= CompanionJobsConfig.CookWaitSeconds)
                {
                    Finish(companion, "o preparo nao terminou a tempo");
                    return;
                }

                SetStatus("esperando o preparo");
                companion.JobStopMoving();
                return;
            }

            _emptyScans++;
            LogEvent(
                "cook|empty",
                $"[ModIA] Cozinhar: nenhuma estacao com o que fazer num raio de " +
                $"{CompanionJobsConfig.CookRadius:0} m ({_emptyScans}/{EmptyScansBeforeStop}).");

            if (_emptyScans >= EmptyScansBeforeStop)
            {
                Finish(companion, "nada para fazer nas estacoes de cozinha");
                return;
            }

            SetStatus("procurando estacao de cozinha");
            companion.JobStopMoving();
        }

        /// <summary>
        /// Uma passada nas peças por perto escolhendo <b>uma</b> tarefa, na ordem de prioridade.
        /// Devolve <c>false</c> quando não há tarefa; <paramref name="busy"/> diz se alguma estação
        /// está com coisa preparando (para ele esperar em vez de encerrar).
        /// </summary>
        private bool PickTask(CompanionAI companion, out CookTask best, out bool busy)
        {
            best = default;
            busy = false;

            Inventory inventory = companion.JobInventory;
            if (inventory == null)
            {
                return false;
            }

            Vector3 me = companion.JobPosition;
            float radius = CompanionJobsConfig.CookRadius;

            s_pieces.Clear();
            Piece.GetAllPiecesInRadius(me, radius, s_pieces);

            int bestTier = int.MaxValue;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < s_pieces.Count; i++)
            {
                Piece piece = s_pieces[i];
                if (piece == null || piece.GetComponent<ZNetView>() == null ||
                    piece.GetComponent<ZNetView>().GetZDO() == null)
                {
                    continue;
                }

                CookingStation cooker = piece.GetComponent<CookingStation>();
                if (cooker != null)
                {
                    ConsiderCooker(inventory, cooker, me, ref best, ref bestTier, ref bestDistance, ref busy);
                    continue;
                }

                Fermenter fermenter = piece.GetComponent<Fermenter>();
                if (fermenter != null)
                {
                    ConsiderFermenter(inventory, fermenter, me, ref best, ref bestTier, ref bestDistance, ref busy);
                    continue;
                }

                CraftingStation station = piece.GetComponent<CraftingStation>();
                if (station != null && CompanionJobsConfig.CookCauldron)
                {
                    ConsiderCauldron(inventory, station, me, ref best, ref bestTier, ref bestDistance);
                }
            }

            return best.Kind != CookTaskKind.None;
        }

        /// <summary>Espeto/forno de cozinha: retirar o pronto, reabastecer e pôr comida.</summary>
        private void ConsiderCooker(
            Inventory inventory,
            CookingStation cooker,
            Vector3 me,
            ref CookTask best,
            ref int bestTier,
            ref float bestDistance,
            ref bool busy)
        {
            ZNetView netView = cooker.GetComponent<ZNetView>();
            ZDO zdo = netView.GetZDO();
            int slots = Mathf.Min(cooker.m_slots != null ? cooker.m_slots.Length : 0, SlotKeyCount);

            int done = 0;
            int cooking = 0;
            int free = 0;
            for (int s = 0; s < slots; s++)
            {
                if (string.IsNullOrEmpty(zdo.GetString(s_slotKeys[s])))
                {
                    free++;
                    continue;
                }

                if (zdo.GetInt(s_statusKeys[s]) != 0)
                {
                    done++;
                }
                else
                {
                    cooking++;
                }
            }

            if (done > 0 || cooking > 0)
            {
                busy = true;
            }

            Vector3 spot = JobStations.StandPoint(piece: cooker.GetComponent<Piece>(), interactPoint: cooker.transform);
            Vector3 output = cooker.m_spawnPoint != null ? cooker.m_spawnPoint.position : cooker.transform.position;

            // 0) Sobrou comida no chão perto da estação (o depósito estava cheio, a coleta foi
            //    interrompida…): catar é barato e é a prioridade mais baixa das tarefas de comida.
            if (CompanionJobsConfig.CookTakeDone &&
                JobStations.HasDropNear(output, JobStations.DropRadius))
            {
                Offer(ref best, ref bestTier, ref bestDistance, me, output, new CookTask
                {
                    Kind = CookTaskKind.Gather,
                    Station = cooker,
                    Count = 1,
                    Spot = output,
                    Output = output,
                }, 6);
            }

            // 1) Pronto (ou queimando): tira antes de tudo.
            if (done > 0 && CompanionJobsConfig.CookTakeDone)
            {
                Offer(ref best, ref bestTier, ref bestDistance, me, spot, new CookTask
                {
                    Kind = CookTaskKind.Take,
                    Station = cooker,
                    Count = 1,
                    Spot = spot,
                    Output = output,
                }, 0);
                return;
            }

            // 2) Fogo apagado (espeto que exige fogueira): reabastece a fogueira embaixo dele.
            bool needsFire = cooker.m_requireFire && !cooker.IsFireLit();
            if (needsFire)
            {
                if (CompanionJobsConfig.CookFuel &&
                    TryPlanHeatRefill(inventory, cooker.transform.position, me, ref best, ref bestTier, ref bestDistance))
                {
                    return;
                }

                // Sem como acender: o espeto não cozinha — não é "tarefa", e ele não deve esperar
                // para sempre por uma estação que nunca vai preparar nada.
                busy = cooking > 0;
                return;
            }

            // 3) Combustível próprio da estação (forno de cozinha/estação de ferro).
            if (cooker.m_useFuel && cooker.m_fuelItem != null && CompanionJobsConfig.CookFuel)
            {
                float fuel = zdo.GetFloat(ZDOVars.s_fuel);
                int target = FuelTarget(cooker.m_maxFuel);
                if (fuel < target - 0.5f)
                {
                    string fuelPrefab = cooker.m_fuelItem.gameObject.name;
                    int have = CountPrefab(inventory, fuelPrefab);
                    if (have > 0)
                    {
                        Offer(ref best, ref bestTier, ref bestDistance, me, spot, new CookTask
                        {
                            Kind = CookTaskKind.Stoke,
                            Station = cooker,
                            Item = fuelPrefab,
                            Count = Mathf.Min(Mathf.CeilToInt(target - fuel), have),
                            Spot = spot,
                            Output = output,
                        }, 3);
                        return;
                    }
                }
            }

            // 4) Pôr comida (só com vaga livre e o fogo aceso).
            if (free > 0)
            {
                string cookable = FindCookable(cooker, inventory);
                if (cookable != null)
                {
                    Offer(ref best, ref bestTier, ref bestDistance, me, spot, new CookTask
                    {
                        Kind = CookTaskKind.FillCooker,
                        Station = cooker,
                        Item = cookable,
                        Count = free,
                        Spot = spot,
                        Output = output,
                    }, 4);
                }
            }
        }

        /// <summary>Barril de fermentação: tornear o pronto e encher o vazio.</summary>
        private void ConsiderFermenter(
            Inventory inventory,
            Fermenter fermenter,
            Vector3 me,
            ref CookTask best,
            ref int bestTier,
            ref float bestDistance,
            ref bool busy)
        {
            int status = FermenterStatus(fermenter);
            if (status == FermenterFermenting)
            {
                busy = true;
            }

            Vector3 spot = fermenter.m_outputPoint != null
                ? fermenter.m_outputPoint.position
                : fermenter.transform.position;

            // 0) Sobrou garrafa no chão perto do barril: catar (prioridade mais baixa).
            if (CompanionJobsConfig.CookTakeDone &&
                JobStations.HasDropNear(spot, JobStations.DropRadius))
            {
                Offer(ref best, ref bestTier, ref bestDistance, me, spot, new CookTask
                {
                    Kind = CookTaskKind.Gather,
                    Station = fermenter,
                    Count = 1,
                    Spot = spot,
                    Output = spot,
                }, 6);
            }

            // 1) Pronto: torneira (o jogo larga as garrafas no ponto de saída).
            if (status == FermenterReady && CompanionJobsConfig.CookTakeDone)
            {
                Offer(ref best, ref bestTier, ref bestDistance, me, spot, new CookTask
                {
                    Kind = CookTaskKind.Tap,
                    Station = fermenter,
                    Count = 1,
                    Spot = spot,
                    Output = spot,
                }, 1);
                return;
            }

            // 2) Vazio: põe uma base de bebida que ele tenha.
            if (status == FermenterEmpty)
            {
                string baseItem = FindFermentable(fermenter, inventory);
                if (baseItem != null)
                {
                    Offer(ref best, ref bestTier, ref bestDistance, me, spot, new CookTask
                    {
                        Kind = CookTaskKind.FillFermenter,
                        Station = fermenter,
                        Item = baseItem,
                        Count = 1,
                        Spot = spot,
                        Output = spot,
                    }, 5);
                }
            }
        }

        /// <summary>Caldeirão/mesa de preparo: cozinhar uma receita que ele tenha os ingredientes.</summary>
        private void ConsiderCauldron(
            Inventory inventory,
            CraftingStation station,
            Vector3 me,
            ref CookTask best,
            ref int bestTier,
            ref float bestDistance)
        {
            // Só estações de fogo (caldeirão/mesa de preparo) e com o fogo aceso: sem isso a receita
            // não sai como para o jogador.
            if (!station.m_craftRequireFire || !station.m_haveFire)
            {
                return;
            }

            ObjectDB db = ObjectDB.instance;
            if (db == null || db.m_recipes == null)
            {
                return;
            }

            int level = station.GetLevel();

            for (int i = 0; i < db.m_recipes.Count; i++)
            {
                Recipe recipe = db.m_recipes[i];
                if (recipe == null || !recipe.m_enabled || recipe.m_item == null ||
                    recipe.m_craftingStation == null || recipe.m_requireOnlyOneIngredient)
                {
                    continue;
                }

                if (recipe.m_craftingStation.m_name != station.m_name ||
                    recipe.GetRequiredStationLevel(1) > level)
                {
                    continue;
                }

                if (!HasResources(inventory, recipe))
                {
                    continue;
                }

                Vector3 spot = station.transform.position;
                Offer(ref best, ref bestTier, ref bestDistance, me, spot, new CookTask
                {
                    Kind = CookTaskKind.Craft,
                    Station = station,
                    Item = recipe.m_item.gameObject.name,
                    Count = Mathf.Max(1, recipe.m_amount),
                    Spot = spot,
                    Output = station.transform.position,
                }, 2);
                return;
            }
        }

        /// <summary>
        /// Guarda a tarefa quando ela é melhor que a atual: <b>primeiro por prioridade</b>
        /// (<paramref name="tier"/> menor vence) e, empatada, pela estação mais perto.
        /// </summary>
        private static void Offer(
            ref CookTask best,
            ref int bestTier,
            ref float bestDistance,
            Vector3 me,
            Vector3 spot,
            CookTask candidate,
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

        /// <summary>
        /// Planeja reabastecer a <b>fogueira</b> que esquenta a estação (o espeto exige fogo aceso).
        /// Procura uma fogueira recarregável a menos de <see cref="HeatRadius"/> e, se o companheiro
        /// tiver o combustível dela, propõe a tarefa.
        /// </summary>
        private bool TryPlanHeatRefill(
            Inventory inventory,
            Vector3 at,
            Vector3 me,
            ref CookTask best,
            ref int bestTier,
            ref float bestDistance)
        {
            s_heatPieces.Clear();
            Piece.GetAllPiecesInRadius(at, HeatRadius, s_heatPieces);

            for (int i = 0; i < s_heatPieces.Count; i++)
            {
                Piece piece = s_heatPieces[i];
                Fireplace fire = piece != null ? piece.GetComponent<Fireplace>() : null;
                if (fire == null || !fire.m_canRefill || fire.m_infiniteFuel || fire.m_fuelItem == null ||
                    fire.m_maxFuel <= 0f)
                {
                    continue;
                }

                ZNetView netView = fire.GetComponent<ZNetView>();
                if (netView == null || !netView.IsValid() || netView.GetZDO() == null)
                {
                    continue;
                }

                float fuel = netView.GetZDO().GetFloat(ZDOVars.s_fuel);
                int target = FuelTarget(fire.m_maxFuel);
                if (fuel >= target - 0.5f)
                {
                    continue;
                }

                string fuelPrefab = fire.m_fuelItem.gameObject.name;
                int have = CountPrefab(inventory, fuelPrefab);
                if (have <= 0)
                {
                    continue;
                }

                Vector3 spot = fire.transform.position;
                Offer(ref best, ref bestTier, ref bestDistance, me, spot, new CookTask
                {
                    Kind = CookTaskKind.Stoke,
                    Station = fire,
                    Item = fuelPrefab,
                    Count = Mathf.Min(Mathf.CeilToInt(target - fuel), have),
                    Spot = spot,
                    Output = spot,
                }, 3);
                return true;
            }

            return false;
        }

        // ---------------------------------------------------------------------
        // Andando até a estação
        // ---------------------------------------------------------------------

        private void TickMoving(CompanionAI companion, float dt)
        {
            if (companion.JobHasCombatTarget)
            {
                SetStatus("combate");
                companion.JobStopMoving();
                return;
            }

            if (_task.Station == null)
            {
                BackToScanning();
                return;
            }

            float distance = Vector3.Distance(_task.Spot, companion.JobPosition);
            if (distance <= JobStations.UseReach)
            {
                if (_task.Kind == CookTaskKind.Gather)
                {
                    _state = CookState.Collecting;
                    _workTimer = CollectDelay;
                }
                else
                {
                    _state = CookState.Working;
                    _workTimer = WorkDelay;
                }

                companion.JobStopMoving();
                return;
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
                    LogEvent(
                        "cook|stuck",
                        $"[ModIA] Cozinhar: nao consegui chegar na estacao ({Describe(_task)}); " +
                        "procurando outra.");
                    BackToScanning();
                    return;
                }
            }

            companion.JobApproach(dt, _task.Spot, JobStations.UseReach, MovementOwner.Cook);
        }

        // ---------------------------------------------------------------------
        // Na estação: a ação
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
                case CookTaskKind.Take:
                    TakeDone(companion);
                    break;

                case CookTaskKind.Tap:
                    Tap(companion);
                    break;

                case CookTaskKind.Stoke:
                    Stoke(companion, inventory);
                    break;

                case CookTaskKind.FillCooker:
                    FillCooker(companion, inventory);
                    break;

                case CookTaskKind.FillFermenter:
                    FillFermenter(companion, inventory);
                    break;

                case CookTaskKind.Craft:
                    Craft(companion, inventory);
                    break;

                case CookTaskKind.Gather:
                    // Já está no ponto de saída: o próximo tick cata (não há ação na estação).
                    _state = CookState.Collecting;
                    _workTimer = CollectDelay;
                    break;

                default:
                    BackToScanning();
                    break;
            }
        }

        /// <summary>Tira o que ficou pronto do espeto/forno (o jogo larga o item perto da estação).</summary>
        private void TakeDone(CompanionAI companion)
        {
            CookingStation cooker = _task.Station as CookingStation;
            ZNetView netView = cooker != null ? cooker.GetComponent<ZNetView>() : null;
            if (!JobStations.ReadyForRpc(netView))
            {
                BackToScanning();
                return;
            }

            InteractAnimation(companion);
            // O ponto de referência é o do companheiro: o jogo usa ele para jogar o item para fora.
            netView.InvokeRPC("RPC_RemoveDoneItem", companion.JobPosition, 1);

            _state = CookState.Collecting;
            _workTimer = CollectDelay;
            SetStatus("retirando o que ficou pronto");
            LogEvent("cook|take", "[ModIA] Cozinhar: retirei o que ficou pronto da estacao.");
        }

        /// <summary>Torneira do barril pronto (o jogo larga as garrafas no ponto de saída).</summary>
        private void Tap(CompanionAI companion)
        {
            Fermenter fermenter = _task.Station as Fermenter;
            ZNetView netView = fermenter != null ? fermenter.GetComponent<ZNetView>() : null;
            if (!JobStations.ReadyForRpc(netView))
            {
                BackToScanning();
                return;
            }

            InteractAnimation(companion);
            netView.InvokeRPC("RPC_Tap");

            _state = CookState.Collecting;
            _workTimer = TapCollectDelay;
            SetStatus("torneirando o barril");
            LogEvent("cook|tap", "[ModIA] Cozinhar: torneirei o barril de fermentacao.");
        }

        /// <summary>
        /// Reabastece o combustível: na fogueira (<see cref="Fireplace"/>) ou na própria estação,
        /// uma unidade por vez (como o jogador) até a fração configurada do máximo.
        /// </summary>
        private void Stoke(CompanionAI companion, Inventory inventory)
        {
            Component station = _task.Station;
            ZNetView netView = station != null ? station.GetComponent<ZNetView>() : null;
            if (!JobStations.ReadyForRpc(netView))
            {
                BackToScanning();
                return;
            }

            float max = station is Fireplace fire ? fire.m_maxFuel : ((CookingStation)station).m_maxFuel;
            int target = FuelTarget(max);
            int added = 0;
            ZDO zdo = netView.GetZDO();
            while (zdo.GetFloat(ZDOVars.s_fuel) + added < target && added < _task.Count &&
                   ConsumeOne(inventory, _task.Item))
            {
                netView.InvokeRPC("RPC_AddFuel");
                added++;
            }

            InteractAnimation(companion);

            if (added > 0)
            {
                SetStatus($"abasteci o fogo (+{added})");
                LogEvent(
                    "cook|fuel|" + _task.Item,
                    $"[ModIA] Cozinhar: pus {added}x '{_task.Item}' no fogo (mantido em " +
                    $"{CompanionJobsConfig.CookFuelLevel * 100f:0}% do maximo).");
            }
            else
            {
                SetStatus("sem combustivel");
                LogEvent(
                    "cook|nofuel|" + _task.Item,
                    $"[ModIA] Cozinhar: sem '{_task.Item}' no inventario para abastecer o fogo.");
            }

            BackToScanning();
        }

        /// <summary>Põe comida no espeto/forno (uma por vez, como o jogador).</summary>
        private void FillCooker(CompanionAI companion, Inventory inventory)
        {
            CookingStation cooker = _task.Station as CookingStation;
            ZNetView netView = cooker != null ? cooker.GetComponent<ZNetView>() : null;
            if (!JobStations.ReadyForRpc(netView))
            {
                BackToScanning();
                return;
            }

            int put = 0;
            while (put < _task.Count && ConsumeOne(inventory, _task.Item))
            {
                netView.InvokeRPC("RPC_AddItem", _task.Item, false);
                put++;
            }

            InteractAnimation(companion);

            if (put > 0)
            {
                SetStatus($"pus {put}x {_task.Item} para cozinhar");
                LogEvent(
                    "cook|cook|" + _task.Item,
                    $"[ModIA] Cozinhar: pus {put}x '{_task.Item}' na estacao; agora e esperar ficar pronto.");
            }

            BackToScanning();
        }

        /// <summary>Põe a base de bebida no barril vazio (o RPC espera o <b>hash</b> do prefab).</summary>
        private void FillFermenter(CompanionAI companion, Inventory inventory)
        {
            Fermenter fermenter = _task.Station as Fermenter;
            ZNetView netView = fermenter != null ? fermenter.GetComponent<ZNetView>() : null;
            if (!JobStations.ReadyForRpc(netView) || FermenterStatus(fermenter) != FermenterEmpty)
            {
                BackToScanning();
                return;
            }

            if (!ConsumeOne(inventory, _task.Item))
            {
                BackToScanning();
                return;
            }

            InteractAnimation(companion);
            netView.InvokeRPC("RPC_AddItem", _task.Item.GetStableHashCode(), false);

            SetStatus($"barril com {_task.Item}");
            LogEvent(
                "cook|ferment|" + _task.Item,
                $"[ModIA] Cozinhar: pus '{_task.Item}' no barril de fermentacao.");
            BackToScanning();
        }

        /// <summary>
        /// Cozinha uma receita no caldeirão: consome os ingredientes do inventário e entrega o
        /// prato (o mesmo atalho da referência — o jogo só tem o caminho da GUI de craft, que é do
        /// jogador). A estação de fogo e o nível dela já foram conferidos na varredura.
        /// </summary>
        private void Craft(CompanionAI companion, Inventory inventory)
        {
            CraftingStation station = _task.Station as CraftingStation;
            if (station == null || !HasResources(inventory, _task.Item))
            {
                BackToScanning();
                return;
            }

            ObjectDB db = ObjectDB.instance;
            if (db == null || db.m_recipes == null)
            {
                BackToScanning();
                return;
            }

            for (int i = 0; i < db.m_recipes.Count; i++)
            {
                Recipe recipe = db.m_recipes[i];
                if (recipe == null || recipe.m_item == null ||
                    recipe.m_item.gameObject.name != _task.Item ||
                    recipe.m_craftingStation == null ||
                    recipe.m_craftingStation.m_name != station.m_name ||
                    !HasResources(inventory, recipe))
                {
                    continue;
                }

                int amount = Mathf.Max(1, recipe.m_amount);
                if (!ConsumeResources(inventory, recipe))
                {
                    BackToScanning();
                    return;
                }

                InteractAnimation(companion);
                station.PokeInUse();

                if (inventory.AddItem(_task.Item, amount, 1, 0, 0L, string.Empty, false) == null)
                {
                    // Sem vaga: o prato vai para o chão (como a referência faz) e ele cata depois.
                    GameObject prefab = db.GetItemPrefab(_task.Item);
                    if (prefab != null)
                    {
                        GameObject dropped = Object.Instantiate(
                            prefab, station.transform.position + Vector3.up * 0.5f, Quaternion.identity);
                        ItemDrop drop = dropped != null ? dropped.GetComponent<ItemDrop>() : null;
                        if (drop != null)
                        {
                            drop.m_itemData.m_stack = amount;
                        }
                    }

                    LogEvent("cook|drop", $"[ModIA] Cozinhar: mochila sem vaga; deixei '{_task.Item}' no chao.");
                }

                station.m_craftItemDoneEffects.Create(station.transform.position, Quaternion.identity);

                AddProgress(amount);
                SetStatus($"fiz {amount}x {_task.Item}");
                ShowProgress("Cozinhei", _task.Item);
                LogEvent(
                    "cook|craft|" + _task.Item,
                    $"[ModIA] Cozinhar: fiz {amount}x '{_task.Item}' no caldeirao.");
                BackToScanning();
                _scanTimer = 1f;
                return;
            }

            BackToScanning();
        }

        // ---------------------------------------------------------------------
        // Catando o que a estação largou
        // ---------------------------------------------------------------------

        private void TickCollecting(CompanionAI companion, float dt)
        {
            _workTimer -= dt;
            if (_workTimer > 0f)
            {
                companion.JobStopMoving();
                return;
            }

            int picked = JobStations.PickupDrops(
                companion, _task.Output, JobStations.DropRadius, null, 12);

            if (picked > 0)
            {
                AddProgress(picked);
                ShowProgress("Peguei", $"{picked} item(ns)");
                LogEvent("cook|gathered", $"[ModIA] Cozinhar: catei {picked} item(ns) da estacao.");
            }
            else if (companion.JobIsPackFull)
            {
                ReportPackFull(companion, dt);
            }

            BackToScanning();
        }

        // ---------------------------------------------------------------------
        // Apoio
        // ---------------------------------------------------------------------

        private void BackToScanning()
        {
            _state = CookState.Scanning;
            _scanTimer = 0f;
            _task = default;
            _bestDistance = float.MaxValue;
            _noProgress = 0f;
        }

        /// <summary>Mantém o fogo em uma fração do máximo (nunca menos de uma unidade).</summary>
        private static int FuelTarget(float max)
        {
            return Mathf.Max(1, Mathf.CeilToInt(max * CompanionJobsConfig.CookFuelLevel));
        }

        private static void InteractAnimation(CompanionAI companion)
        {
            Character character = companion != null ? companion.JobCharacter : null;
            if (character != null && character.m_zanim != null)
            {
                character.m_zanim.SetTrigger("interact");
            }
        }

        private static string Describe(CookTask task)
        {
            switch (task.Kind)
            {
                case CookTaskKind.Take:
                    return "retirando o que ficou pronto";

                case CookTaskKind.Tap:
                    return "torneirando o barril";

                case CookTaskKind.Craft:
                    return $"cozinhando {task.Item} no caldeirao";

                case CookTaskKind.Stoke:
                    return $"abastecendo o fogo com {task.Item}";

                case CookTaskKind.FillCooker:
                    return $"pondo {task.Item} na estacao";

                case CookTaskKind.FillFermenter:
                    return $"pondo {task.Item} no barril";

                case CookTaskKind.Gather:
                    return "catando o que a estacao largou";

                default:
                    return "procurando estacao de cozinha";
            }
        }

        /// <summary>Estado do barril (mesma conta do <c>Fermenter.GetStatus</c> do jogo).</summary>
        private const int FermenterEmpty = 0;
        private const int FermenterFermenting = 1;
        private const int FermenterReady = 2;

        private static int FermenterStatus(Fermenter fermenter)
        {
            if (fermenter == null)
            {
                return FermenterEmpty;
            }

            ZNetView netView = fermenter.GetComponent<ZNetView>();
            if (netView == null || !netView.IsValid() || netView.GetZDO() == null)
            {
                return FermenterEmpty;
            }

            ZDO zdo = netView.GetZDO();
            if (zdo.GetInt(ZDOVars.s_content) == 0)
            {
                return FermenterEmpty;
            }

            long ticks = zdo.GetLong(ZDOVars.s_startTime, 0L);
            if (ticks == 0L)
            {
                return FermenterFermenting;
            }

            double seconds = (ZNet.instance.GetTime() - new System.DateTime(ticks)).TotalSeconds;
            return seconds > fermenter.m_fermentationDuration ? FermenterReady : FermenterFermenting;
        }

        /// <summary>Primeira conversão da estação cujo item de entrada ele tem no inventário.</summary>
        private static string FindCookable(CookingStation cooker, Inventory inventory)
        {
            if (cooker.m_conversion == null)
            {
                return null;
            }

            for (int i = 0; i < cooker.m_conversion.Count; i++)
            {
                CookingStation.ItemConversion conversion = cooker.m_conversion[i];
                if (conversion == null || conversion.m_from == null)
                {
                    continue;
                }

                string prefab = conversion.m_from.gameObject.name;
                if (CountPrefab(inventory, prefab) > 0)
                {
                    return prefab;
                }
            }

            return null;
        }

        /// <summary>Primeira conversão do barril cujo item de entrada ele tem no inventário.</summary>
        private static string FindFermentable(Fermenter fermenter, Inventory inventory)
        {
            if (fermenter.m_conversion == null)
            {
                return null;
            }

            for (int i = 0; i < fermenter.m_conversion.Count; i++)
            {
                Fermenter.ItemConversion conversion = fermenter.m_conversion[i];
                if (conversion == null || conversion.m_from == null)
                {
                    continue;
                }

                string prefab = conversion.m_from.gameObject.name;
                if (CountPrefab(inventory, prefab) > 0)
                {
                    return prefab;
                }
            }

            return null;
        }

        /// <summary>Ele tem todos os ingredientes desta receita?</summary>
        private static bool HasResources(Inventory inventory, Recipe recipe)
        {
            Piece.Requirement[] resources = recipe.m_resources;
            if (resources == null || resources.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < resources.Length; i++)
            {
                Piece.Requirement requirement = resources[i];
                if (requirement == null || requirement.m_resItem == null)
                {
                    continue;
                }

                if (CountPrefab(inventory, requirement.m_resItem.gameObject.name) <
                    requirement.GetAmount(1))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Ele tem os ingredientes do prato já escolhido (usado antes de repetir a conta)?</summary>
        private static bool HasResources(Inventory inventory, string outputPrefab)
        {
            ObjectDB db = ObjectDB.instance;
            if (db == null || db.m_recipes == null)
            {
                return false;
            }

            for (int i = 0; i < db.m_recipes.Count; i++)
            {
                Recipe recipe = db.m_recipes[i];
                if (recipe != null && recipe.m_item != null &&
                    recipe.m_item.gameObject.name == outputPrefab)
                {
                    return HasResources(inventory, recipe);
                }
            }

            return false;
        }

        /// <summary>Consome os ingredientes da receita (uma unidade por vez, como o jogo).</summary>
        private static bool ConsumeResources(Inventory inventory, Recipe recipe)
        {
            Piece.Requirement[] resources = recipe.m_resources;
            for (int i = 0; i < resources.Length; i++)
            {
                Piece.Requirement requirement = resources[i];
                if (requirement == null || requirement.m_resItem == null)
                {
                    continue;
                }

                int amount = requirement.GetAmount(1);
                for (int n = 0; n < amount; n++)
                {
                    if (!ConsumeOne(inventory, requirement.m_resItem.gameObject.name))
                    {
                        return false;
                    }
                }
            }

            return true;
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

        /// <summary>
        /// As chaves do ZDO dos slots do espeto (<c>slotN</c>/<c>slotstatusN</c>) já como
        /// <c>int</c>: o <c>ZDO.GetString</c> usa o hash estável, então isto evita montar string a
        /// cada varredura.
        /// </summary>
        private static int[] BuildSlotKeys(string prefix)
        {
            int[] keys = new int[SlotKeyCount];
            for (int i = 0; i < SlotKeyCount; i++)
            {
                keys[i] = (prefix + i).GetStableHashCode();
            }

            return keys;
        }
    }
}
