using System.Collections.Generic;
using UnityEngine;

namespace ModAIValheim.Companion.Jobs
{
    /// <summary>
    /// <b>TRABALHO: OPERAR FUNDIÇÕES</b> (Fase 2.16b) — o companheiro opera os fornos por perto:
    /// <list type="bullet">
    ///   <item><description><b>forno</b> (<c>Smelter</c> — funde minério em barra, exige
    ///   combustível);</description></item>
    ///   <item><description><b>alto-forno</b> (<c>BlastFurnace</c> — mesmo <c>Smelter</c>, com
    ///   carvão);</description></item>
    ///   <item><description><b>carvoeira</b> (<c>CharcoalKiln</c> — <c>Smelter</c> com
    ///   <c>m_maxFuel == 0</c>: madeira vira carvão, sem combustível).</description></item>
    /// </list>
    ///
    /// <para>
    /// <b>O que ele faz:</b> abastece <b>combustível</b> e <b>minério</b> do próprio inventário,
    /// deixa a fornalha trabalhar e <b>retira o produto</b> (barra/carvão) que o jogo larga no chão
    /// no ponto de saída — o <c>Smelter</c> chama <c>SpawnProcessed</c> sozinho quando a fila de
    /// minério esvazia ou o combustível acaba, e o companheiro só cata o que apareceu.
    /// </para>
    ///
    /// <para>
    /// <b>Como fala com a fornalha (API da 1.0.17):</b> pelo RPC do <c>ZNetView</c>, que é o que o
    /// <c>Smelter</c> usa por baixo dos seus <c>Switch</c>: <c>RPC_AddOre(nome, cheated)</c> e
    /// <c>RPC_AddFuel</c>. O estado vem do ZDO: <c>fuel</c> (float), <c>queued</c> (fila de minério)
    /// e <c>SpawnAmount</c> (produto já processado). O <c>RPC_AddOre</c> só aceita item que esteja
    /// em <c>m_conversion</c> (a "receita" do forno), então o minério é escolhido por ali — e é o
    /// que faz o mesmo código servir para forno, alto-forno e carvoeira.
    /// </para>
    ///
    /// <para>
    /// Adaptado (lógica) de <c>src/Companions/Jobs/SmeltController.cs</c> (fases varrer/levar/
    /// inserir/coletar, <c>ScanNearbySmelters</c>, separação carvoeira × forno por
    /// <c>m_maxFuel == 0</c>, <c>TryPlanRefill</c>/<c>TryPlanFuelRefill</c>, inserção de uma unidade
    /// por vez com <c>RPC_AddFuel</c>/<c>RPC_AddOre</c> conferindo o ZDO antes, e
    /// <c>CountGroundDropsNearSmelter</c>) do <i>Offline Companions Reborn</i> (MIT — ver
    /// <c>THIRD_PARTY_NOTICES.md</c>). A implementação aqui é própria (uma tarefa por vez na máquina
    /// de estados do <see cref="CompanionJob"/>, movimento pelo <see cref="MovementOwner.Smelt"/> e
    /// buffers reaproveitados), sem cópia de arquivo.
    /// </para>
    ///
    /// <para>
    /// <b>Fora do núcleo:</b> <b>buscar minério/combustível em baús</b> (a referência usa o
    /// tool keeper e as pilhas de recurso — aqui, como pede a tarefa, os insumos vêm do inventário);
    /// a <b>ordenação entre vários fornos</b> por fila/percentual e o <b>modo de espera "tudo
    /// abastecido"</b> da referência; e <b>pilhas de recurso no chão</b> (<c>MHR_PileTaker</c>).
    /// </para>
    /// </summary>
    internal sealed class SmeltJob : CompanionJob
    {
        private enum SmeltState
        {
            /// <summary>Varrendo os fornos por perto.</summary>
            Scanning,

            /// <summary>Andando até o forno (ou até o ponto de saída, para catar).</summary>
            Moving,

            /// <summary>No forno: abastecendo.</summary>
            Working,

            /// <summary>Catando o produto que a fornalha largou.</summary>
            Collecting,
        }

        private enum SmeltTaskKind
        {
            None,

            /// <summary>Catar o produto (barra/carvão) largado no chão.</summary>
            Collect,

            /// <summary>Abastecer o combustível.</summary>
            Fuel,

            /// <summary>Pôr minério na fila.</summary>
            Ore,
        }

        private struct SmeltTask
        {
            internal SmeltTaskKind Kind;
            internal Smelter Station;
            internal string Item;
            internal int Count;
            internal Vector3 Spot;
            internal Vector3 Output;
        }

        /// <summary>Espera (s) antes de agir ao chegar no forno (animação de interação).</summary>
        private const float WorkDelay = 0.4f;

        /// <summary>Espera (s) depois de largar o produto antes de catar.</summary>
        private const float CollectDelay = 0.6f;

        /// <summary>Varreduras seguidas sem tarefa (e sem forno trabalhando) antes de encerrar.</summary>
        private const int EmptyScansBeforeStop = 3;

        /// <summary>Espera (s) máxima vendo os fornos trabalharem antes de encerrar o trabalho.</summary>
        private const float WaitLimitSeconds = 300f;

        /// <summary>Tempo (s) sem chegar mais perto que faz desistir do forno.</summary>
        private const float NoProgressSeconds = 8f;

        private const int PieceBufferSize = 256;

        private static readonly List<Piece> s_pieces = new List<Piece>(PieceBufferSize);

        private SmeltState _state = SmeltState.Scanning;
        private SmeltTask _task;
        private float _scanTimer;
        private float _workTimer;
        private float _waitSeconds;
        private int _emptyScans;
        private float _bestDistance = float.MaxValue;
        private float _noProgress;

        internal override JobKind Kind => JobKind.Smelt;

        internal override string DisplayName => "Fundir";

        internal override string CommandName => "smelt";

        protected override bool OnStart(CompanionAI companion, out string error)
        {
            error = null;

            if (companion.JobHumanoid == null || companion.JobInventory == null)
            {
                error = "[ModIA] O companheiro esta sem corpo/inventario; fundir cancelado.";
                return false;
            }

            _state = SmeltState.Scanning;
            _task = default;
            _scanTimer = 0f;
            _workTimer = 0f;
            _waitSeconds = 0f;
            _emptyScans = 0;
            _bestDistance = float.MaxValue;
            _noProgress = 0f;

            SetStatus("procurando fornos");
            LogEvent(
                "smelt|start",
                $"[ModIA] Fundir: procurando fornos num raio de {CompanionJobsConfig.SmeltRadius:0} m " +
                $"(combustivel = {CompanionJobsConfig.SmeltFuel}, retirar produto = " +
                $"{CompanionJobsConfig.SmeltTakeOutput}, por viagem = {CompanionJobsConfig.SmeltMaxInsert}).");
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
                case SmeltState.Moving:
                    TickMoving(companion, dt);
                    break;

                case SmeltState.Working:
                    TickWorking(companion, dt);
                    break;

                case SmeltState.Collecting:
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
            // Meta de quantidade atingida (modia_job smelt 20): o trabalho acabou.
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

            if (PickTask(companion, out SmeltTask task, out bool busy))
            {
                _task = task;
                _bestDistance = float.MaxValue;
                _noProgress = 0f;
                _waitSeconds = 0f;
                _state = SmeltState.Moving;
                SetStatus(Describe(task));
                LogEvent(
                    "smelt|task|" + task.Kind + "|" + task.Item,
                    $"[ModIA] Fundir: {Describe(task)} ({Vector3.Distance(companion.JobPosition, task.Spot):0.#} m).");
                return;
            }

            // Nada a fazer agora, mas tem forno trabalhando: ele fica por perto (e retira o produto
            // assim que sair).
            if (busy)
            {
                _emptyScans = 0;
                _waitSeconds += CompanionJobsConfig.ScanInterval;
                if (_waitSeconds >= WaitLimitSeconds)
                {
                    Finish(companion, "os fornos pararam sem produto");
                    return;
                }

                SetStatus("esperando o forno");
                companion.JobStopMoving();
                return;
            }

            _emptyScans++;
            LogEvent(
                "smelt|empty",
                $"[ModIA] Fundir: nenhum forno com o que fazer num raio de " +
                $"{CompanionJobsConfig.SmeltRadius:0} m ({_emptyScans}/{EmptyScansBeforeStop}).");

            if (_emptyScans >= EmptyScansBeforeStop)
            {
                Finish(companion, "nada para fazer nas fundicoes");
                return;
            }

            SetStatus("procurando fornos");
            companion.JobStopMoving();
        }

        /// <summary>
        /// Uma passada nos fornos por perto escolhendo <b>uma</b> tarefa: catar o produto (vence,
        /// senão ele se acumula no chão), abastecer o combustível e pôr minério.
        /// </summary>
        private bool PickTask(CompanionAI companion, out SmeltTask best, out bool busy)
        {
            best = default;
            busy = false;

            Inventory inventory = companion.JobInventory;
            if (inventory == null)
            {
                return false;
            }

            Vector3 me = companion.JobPosition;
            int maxInsert = CompanionJobsConfig.SmeltMaxInsert;

            s_pieces.Clear();
            Piece.GetAllPiecesInRadius(me, CompanionJobsConfig.SmeltRadius, s_pieces);

            int bestTier = int.MaxValue;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < s_pieces.Count; i++)
            {
                Piece piece = s_pieces[i];
                Smelter smelter = piece != null ? piece.GetComponent<Smelter>() : null;
                if (smelter == null)
                {
                    continue;
                }

                ZNetView netView = smelter.GetComponent<ZNetView>();
                if (netView == null || !netView.IsValid() || netView.GetZDO() == null)
                {
                    continue;
                }

                ZDO zdo = netView.GetZDO();
                float fuel = zdo.GetFloat(ZDOVars.s_fuel);
                int queued = zdo.GetInt(ZDOVars.s_queued);
                int processed = zdo.GetInt(ZDOVars.s_spawnAmount);

                Vector3 output = smelter.m_outputPoint != null
                    ? smelter.m_outputPoint.position
                    : smelter.transform.position;
                Vector3 spot = smelter.transform.position;

                if (smelter.IsActive() || queued > 0 || processed > 0)
                {
                    busy = true;
                }

                // 1) Produto no chão: cata antes de tudo (o forno não guarda barra).
                if (CompanionJobsConfig.SmeltTakeOutput &&
                    JobStations.HasDropNear(output, JobStations.DropRadius))
                {
                    Offer(ref best, ref bestTier, ref bestDistance, me, output, new SmeltTask
                    {
                        Kind = SmeltTaskKind.Collect,
                        Station = smelter,
                        Count = 1,
                        Spot = output,
                        Output = output,
                    }, 0);
                    continue;
                }

                // 2) Combustível (forno/alto-forno; a carvoeira não tem — m_maxFuel == 0).
                if (CompanionJobsConfig.SmeltFuel && smelter.m_maxFuel > 0 && smelter.m_fuelItem != null &&
                    fuel < smelter.m_maxFuel - 0.5f)
                {
                    string fuelPrefab = smelter.m_fuelItem.gameObject.name;
                    int have = CountPrefab(inventory, fuelPrefab);
                    if (have > 0)
                    {
                        int need = Mathf.CeilToInt(smelter.m_maxFuel - fuel);
                        Offer(ref best, ref bestTier, ref bestDistance, me, spot, new SmeltTask
                        {
                            Kind = SmeltTaskKind.Fuel,
                            Station = smelter,
                            Item = fuelPrefab,
                            Count = Mathf.Min(Mathf.Min(need, have), maxInsert),
                            Spot = spot,
                            Output = output,
                        }, 1);
                        continue;
                    }
                }

                // 3) Minério na fila.
                if (smelter.m_maxOre > 0 && queued < smelter.m_maxOre)
                {
                    string ore = FindOre(smelter, inventory);
                    if (ore != null)
                    {
                        int have = CountPrefab(inventory, ore);
                        int need = smelter.m_maxOre - queued;
                        Offer(ref best, ref bestTier, ref bestDistance, me, spot, new SmeltTask
                        {
                            Kind = SmeltTaskKind.Ore,
                            Station = smelter,
                            Item = ore,
                            Count = Mathf.Min(Mathf.Min(need, have), maxInsert),
                            Spot = spot,
                            Output = output,
                        }, 2);
                    }
                }
            }

            return best.Kind != SmeltTaskKind.None;
        }

        /// <summary>
        /// Guarda a tarefa quando ela é melhor que a atual: <b>primeiro por prioridade</b>
        /// (<paramref name="tier"/> menor vence) e, empatada, pelo forno mais perto.
        /// </summary>
        private static void Offer(
            ref SmeltTask best,
            ref int bestTier,
            ref float bestDistance,
            Vector3 me,
            Vector3 spot,
            SmeltTask candidate,
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
        // Andando até o forno
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

            float reach = _task.Kind == SmeltTaskKind.Collect ? JobStations.DropRadius : JobStations.UseReach;
            float distance = Vector3.Distance(_task.Spot, companion.JobPosition);
            if (distance <= reach)
            {
                if (_task.Kind == SmeltTaskKind.Collect)
                {
                    _state = SmeltState.Collecting;
                    _workTimer = CollectDelay;
                }
                else
                {
                    _state = SmeltState.Working;
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
                        "smelt|stuck",
                        $"[ModIA] Fundir: nao consegui chegar no forno ({Describe(_task)}); " +
                        "procurando outro.");
                    BackToScanning();
                    return;
                }
            }

            companion.JobApproach(dt, _task.Spot, reach, MovementOwner.Smelt);
        }

        // ---------------------------------------------------------------------
        // No forno: a ação
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
            Smelter smelter = _task.Station;
            ZNetView netView = smelter != null ? smelter.GetComponent<ZNetView>() : null;
            if (inventory == null || !JobStations.ReadyForRpc(netView))
            {
                BackToScanning();
                return;
            }

            ZDO zdo = netView.GetZDO();
            if (_task.Kind == SmeltTaskKind.Fuel)
            {
                int added = 0;
                while (added < _task.Count &&
                       zdo.GetFloat(ZDOVars.s_fuel) + added < smelter.m_maxFuel - 0.5f &&
                       ConsumeOne(inventory, _task.Item))
                {
                    netView.InvokeRPC("RPC_AddFuel");
                    added++;
                }

                InteractAnimation(companion);

                if (added > 0)
                {
                    SetStatus($"abasteci o forno (+{added} {_task.Item})");
                    LogEvent(
                        "smelt|fuel|" + _task.Item,
                        $"[ModIA] Fundir: pus {added}x '{_task.Item}' de combustivel no forno.");
                }
                else
                {
                    SetStatus("sem combustivel");
                    LogEvent(
                        "smelt|nofuel|" + _task.Item,
                        $"[ModIA] Fundir: sem '{_task.Item}' no inventario para abastecer o forno.");
                }
            }
            else if (_task.Kind == SmeltTaskKind.Ore)
            {
                int put = 0;
                while (put < _task.Count &&
                       zdo.GetInt(ZDOVars.s_queued) + put < smelter.m_maxOre &&
                       ConsumeOne(inventory, _task.Item))
                {
                    netView.InvokeRPC("RPC_AddOre", _task.Item, false);
                    put++;
                }

                InteractAnimation(companion);

                if (put > 0)
                {
                    SetStatus($"pus {put}x {_task.Item} no forno");
                    LogEvent(
                        "smelt|ore|" + _task.Item,
                        $"[ModIA] Fundir: pus {put}x '{_task.Item}' na fila do forno.");
                }
            }

            BackToScanning();
        }

        // ---------------------------------------------------------------------
        // Catando o produto
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
                companion, _task.Output, JobStations.DropRadius, null, 20);

            if (picked > 0)
            {
                AddProgress(picked);
                ShowProgress("Fundi", $"{picked} produto(s)");
                LogEvent("smelt|gathered", $"[ModIA] Fundir: catei {picked} produto(s) da fornalha.");
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
            _state = SmeltState.Scanning;
            _scanTimer = 0f;
            _task = default;
            _bestDistance = float.MaxValue;
            _noProgress = 0f;
        }

        private static void InteractAnimation(CompanionAI companion)
        {
            Character character = companion != null ? companion.JobCharacter : null;
            if (character != null && character.m_zanim != null)
            {
                character.m_zanim.SetTrigger("interact");
            }
        }

        private static string Describe(SmeltTask task)
        {
            switch (task.Kind)
            {
                case SmeltTaskKind.Collect:
                    return "catando o produto do forno";

                case SmeltTaskKind.Fuel:
                    return $"abastecendo o forno com {task.Item}";

                case SmeltTaskKind.Ore:
                    return $"pondo {task.Item} no forno";

                default:
                    return "procurando fornos";
            }
        }

        /// <summary>
        /// O minério (ou a madeira, na carvoeira) que esta fornalha aceita e que ele tem: vem das
        /// próprias conversões do <c>Smelter</c> (<c>m_conversion</c>), o que faz o mesmo código
        /// servir para forno, alto-forno e carvoeira.
        /// </summary>
        private static string FindOre(Smelter smelter, Inventory inventory)
        {
            if (smelter.m_conversion == null)
            {
                return null;
            }

            for (int i = 0; i < smelter.m_conversion.Count; i++)
            {
                Smelter.ItemConversion conversion = smelter.m_conversion[i];
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
    }
}
