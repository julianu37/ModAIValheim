using System.Collections.Generic;
using UnityEngine;

namespace ModAIValheim.Companion.Jobs
{
    /// <summary>
    /// <b>TRABALHO: CULTIVAR</b> (Fase 2.16b) — numa <b>zona de fazenda</b> (centro + raio, definida
    /// pelo jogador com <c>modia_farm set</c>), o companheiro:
    /// <list type="number">
    ///   <item><description><b>colhe</b> as plantas prontas (o fruto que a planta larga);</description></item>
    ///   <item><description><b>planta</b> sementes do próprio inventário nas vagas livres do terreno
    ///   cultivado;</description></item>
    ///   <item><description><b>guarda</b> a colheita no baú (reaproveitando o depósito da Fase 2.14),
    ///   preservando um mínimo de sementes para continuar plantando.</description></item>
    /// </list>
    ///
    /// <para>
    /// <b>Como planta (API da 1.0.17):</b> o jogo não expõe um "plante isto" — quem planta é o
    /// <c>Player.PlacePiece</c>, que instancia o prefab da peça. Fazemos o mesmo caminho, que é o
    /// que a referência também usa: consome uma semente do inventário e
    /// <c>Object.Instantiate(plantPrefab, ponto, rotação)</c>, com
    /// <c>TerrainModifier.SetTriggerOnPlaced</c> em volta (para o terreno "assentar" antes da peça,
    /// como no jogo). O <see cref="Plant"/> do prefab cuida do resto (semente, tempo de plantio,
    /// crescimento e <c>m_grownPrefabs</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Como colhe:</b> a planta pronta é um <see cref="Pickable"/> no mundo (o fruto), e o
    /// companheiro usa <c>Pickable.Interact(humanoid, false, false)</c> — o RPC interno
    /// (<c>RPC_Pick</c>) larga os itens no chão, que ele então cata com <c>Humanoid.Pickup</c>
    /// (o mesmo caminho da coleta da Fase 2.4b). Nada de <c>Player</c>: o <c>Interact</c> só usa o
    /// <c>Player</c> para perícia/estatística, e o companheiro é um <c>Humanoid</c>.
    /// </para>
    ///
    /// <para>
    /// <b>O que é plantável/colhível</b> vem dos próprios prefabs do jogo
    /// (<see cref="FarmCatalog"/>): prefab com <see cref="Plant"/> + <see cref="Piece"/> cujo custo
    /// (<c>m_resources[0]</c>) é a semente, e os <c>m_grownPrefabs</c> (os <see cref="Pickable"/>)
    /// são as culturas colhíveis. Só entram culturas de <b>terreno cultivado</b>
    /// (<c>m_needCultivatedGround</c>), o que exclui mudas de árvore.
    /// </para>
    ///
    /// <para>
    /// Adaptado (lógica) de <c>src/Companions/Jobs/FarmController.cs</c> (fases
    /// colher/plantar/guardar, <c>FindRipePickable</c>, <c>FindPlantPositionInZone</c> com
    /// espaçamento por <c>m_growRadius</c>, checagem de <c>Heightmap.IsCultivated</c>/bioma/acesso,
    /// <c>PlantSeed</c> com <c>TerrainModifier.SetTriggerOnPlaced</c> + <c>Instantiate</c> e
    /// <c>StoreOutput</c>), <c>src/Companions/Jobs/FarmZone.cs</c>/<c>FarmZoneSerializer.cs</c>
    /// (a zona definida pelo jogador) e <c>src/Companions/Jobs/MHR_SeedRules.cs</c> (manter um
    /// mínimo de sementes no inventário ao guardar a colheita) do <i>Offline Companions Reborn</i>
    /// (MIT — ver <c>THIRD_PARTY_NOTICES.md</c>). A implementação aqui é própria (zona circular,
    /// máquina de estados do <see cref="CompanionJob"/>, buffers reaproveitados e o depósito da
    /// Fase 2.14 no lugar do baú próprio da referência), sem cópia de arquivo.
    /// </para>
    ///
    /// <para>
    /// <b>Fora do núcleo:</b> a <b>UI</b> de escolher a cultura e de posicionar a zona com o mouse
    /// (<c>UI/FarmZoneCropPicker.cs</c>/<c>UI/FarmZonePlacer.cs</c> da referência) — aqui a zona vem
    /// do comando e a cultura da config; a <b>rotação de culturas em fileiras</b> e o
    /// <b>estoque/retirada de sementes em baús</b> (<c>FindChestWithSeeds</c>/<c>TakeExtras</c>) —
    /// o companheiro usa só o que tem no inventário; e <b>mudas de árvore</b>, que ficam fora do
    /// catálogo (só culturas de terreno cultivado).
    /// </para>
    /// </summary>
    internal sealed class FarmJob : CompanionJob
    {
        private enum FarmState
        {
            /// <summary>Varrendo a zona (colher o que está pronto, plantar no que está livre).</summary>
            Scanning,

            /// <summary>Andando até o alvo (a planta pronta ou a vaga do canteiro).</summary>
            Moving,

            /// <summary>No alvo: colhendo ou plantando.</summary>
            Working,

            /// <summary>Catando o que a colheita largou no chão.</summary>
            Collecting,
        }

        /// <summary>Alcance (m) para colher uma planta (a hitbox do fruto é pequena).</summary>
        private const float HarvestReach = 2.2f;

        /// <summary>Alcance (m) para plantar (o jogador planta quase em cima do ponto).</summary>
        private const float PlantReach = 1.8f;

        /// <summary>Espera (s) antes de agir ao chegar no alvo (a animação de interação).</summary>
        private const float WorkDelay = 0.45f;

        /// <summary>Espera (s) depois de colher antes de catar (os drops nascem no RPC).</summary>
        private const float CollectDelay = 0.7f;

        /// <summary>Varreduras seguidas sem nada para fazer antes de encerrar o trabalho.</summary>
        private const int EmptyScansBeforeStop = 3;

        /// <summary>Alvos "encalhados" (não deu para chegar) guardados ao mesmo tempo.</summary>
        private const int MaxSkippedSpots = 24;

        /// <summary>Tempo (s) que um alvo encalhado fica fora da varredura.</summary>
        private const float SpotSkipSeconds = 90f;

        /// <summary>Espera (s) entre tentativas de levar a colheita ao baú.</summary>
        private const float StoreRetrySeconds = 5f;

        /// <summary>Tempo (s) sem chegar mais perto que faz desistir do alvo atual.</summary>
        private const float NoProgressSeconds = 8f;

        private const int ZoneBufferSize = 256;
        private const int SpaceBufferSize = 64;

        /// <summary>Quantos itens a coleta de drops pega por varredura.</summary>
        private const int MaxPicksPerCollect = 12;

        private static readonly Collider[] s_zoneBuffer = new Collider[ZoneBufferSize];
        private static readonly Collider[] s_spaceBuffer = new Collider[SpaceBufferSize];
        private static int s_zoneMask = -1;
        private static int s_spaceMask = -1;

        private FarmState _state = FarmState.Scanning;
        private float _scanTimer;
        private float _workTimer;
        private float _storeTimer;
        private int _emptyScans;
        private int _harvestedSinceStore;

        /// <summary>Posição do companheiro no tick (cacheada para a varredura da zona).</summary>
        private Vector3 _me;

        /// <summary>Alvo atual é uma colheita? (false = plantio).</summary>
        private bool _isHarvest;

        /// <summary>Planta pronta escolhida (colheita).</summary>
        private Pickable _pickable;

        /// <summary>Onde está o alvo (posição cacheada: a planta some ao ser colhida).</summary>
        private Vector3 _targetPos;

        /// <summary>Semente que será plantada (prefab) e o que ela se torna.</summary>
        private string _plantSeed;
        private FarmCatalog.SeedPlantInfo _plantInfo;

        /// <summary>Melhor distância já alcançada no alvo atual (deteção de encalhe).</summary>
        private float _bestDistance = float.MaxValue;
        private float _noProgress;

        private struct SkippedSpot
        {
            internal Vector3 Position;
            internal float Until;
        }

        private readonly List<SkippedSpot> _skipped = new List<SkippedSpot>(MaxSkippedSpots);

        internal override JobKind Kind => JobKind.Farm;

        internal override string DisplayName => "Cultivar";

        internal override string CommandName => "farm";

        protected override bool OnStart(CompanionAI companion, out string error)
        {
            error = null;

            FarmZone zone = CompanionJobsConfig.FarmZone;
            if (!zone.IsValid)
            {
                error =
                    "[ModIA] Nenhuma zona de fazenda definida. Fique no canteiro e use " +
                    "modia_farm set (ou modia_farm set 10) antes de mandar cultivar.";
                return false;
            }

            if (companion.JobHumanoid == null || companion.JobInventory == null)
            {
                error = "[ModIA] O companheiro esta sem corpo/inventario; cultivar cancelado.";
                return false;
            }

            FarmCatalog.Ensure();
            if (!FarmCatalog.Ready)
            {
                error = "[ModIA] Nao encontrei nenhuma cultura plantavel nos prefabs do jogo.";
                return false;
            }

            if (!CompanionJobsConfig.FarmPlant && !CompanionJobsConfig.FarmHarvest)
            {
                error =
                    "[ModIA] Cultivar esta desligado nos dois sentidos (Jobs/FarmPlantar e " +
                    "Jobs/FarmColher = false).";
                return false;
            }

            // O depósito da Fase 2.14 vai levar a colheita ao baú; estas sementes (e os vegetais
            // que servem para produzir semente) ficam no inventário até o mínimo configurado —
            // senão a primeira viagem ao baú levaria justamente o que ele precisa para replantar.
            CompanionDeposit.SetKeepCounts(BuildSeedKeepList());

            _state = FarmState.Scanning;
            _scanTimer = 0f;
            _workTimer = 0f;
            _storeTimer = 0f;
            _emptyScans = 0;
            _harvestedSinceStore = 0;
            _isHarvest = false;
            _pickable = null;
            _plantSeed = null;
            _plantInfo = default;
            _targetPos = Vector3.zero;
            _bestDistance = float.MaxValue;
            _noProgress = 0f;
            _skipped.Clear();

            SetStatus($"na fazenda ({zone.Radius:0} m de raio)");
            LogEvent(
                "farm|start",
                $"[ModIA] Cultivar: zona em {zone.Center.x:0.#}, {zone.Center.z:0.#} (raio {zone.Radius:0} m), " +
                $"plantar = {CompanionJobsConfig.FarmPlant}, colher = {CompanionJobsConfig.FarmHarvest}, " +
                $"sementes protegidas no minimo de {CompanionJobsConfig.FarmKeepSeeds}.");
            return true;
        }

        protected override void OnStop(CompanionAI companion, string reason)
        {
            // A proteção das sementes vale só enquanto o trabalho roda (o resto do mod deposita tudo).
            CompanionDeposit.ClearKeepCounts();

            if (companion != null)
            {
                companion.JobStopMoving();
            }

            _pickable = null;
            _plantSeed = null;
            _plantInfo = default;
            _skipped.Clear();
            SetStatus("encerrado");
        }

        protected override void OnTick(CompanionAI companion, float dt)
        {
            _me = companion.JobPosition;

            switch (_state)
            {
                case FarmState.Moving:
                    TickMoving(companion, dt);
                    break;

                case FarmState.Working:
                    TickWorking(companion, dt);
                    break;

                case FarmState.Collecting:
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
            // Meta de quantidade atingida (modia_job farm 20): o trabalho acabou.
            if (AmountReached)
            {
                Finish(companion, "meta atingida");
                return;
            }

            // O combate vence o trabalho (é o motor nativo quem luta): ele para e espera.
            if (companion.JobHasCombatTarget)
            {
                companion.JobStopMoving();
                SetStatus("combate");
                return;
            }

            // O loot manda antes do trabalho (prioridade ... > loot > trabalho).
            if (CompanionLoot.HasWork)
            {
                companion.JobStopMoving();
                SetStatus("catando o abate");
                return;
            }

            // Colheita acumulada? Leva ao baú (a viagem pausa o trabalho sozinha).
            TryStoreHarvest(companion, dt);

            FarmZone zone = CompanionJobsConfig.FarmZone;
            if (!zone.IsValid)
            {
                Finish(companion, "a zona de fazenda foi apagada (modia_farm clear)");
                return;
            }

            // Longe da zona: primeiro ele chega lá (senão ficaria parado olhando o horizonte).
            if (zone.FlatDistance(companion.JobPosition) > zone.Radius)
            {
                SetStatus("indo para a fazenda");
                companion.JobApproach(
                    dt,
                    GroundPoint(zone.Center),
                    Mathf.Max(1.5f, zone.Radius * 0.5f),
                    MovementOwner.Farm);
                return;
            }

            _scanTimer -= dt;
            if (_scanTimer > 0f)
            {
                companion.JobStopMoving();
                return;
            }

            _scanTimer = CompanionJobsConfig.ScanInterval;

            ScanZone(zone, out Pickable ripe, out bool growing);

            // 1) Colher o que já está pronto (prioridade: não deixa a fruta apodrecer no pé).
            if (CompanionJobsConfig.FarmHarvest && ripe != null)
            {
                _isHarvest = true;
                _pickable = ripe;
                _targetPos = ripe.transform.position;
                _bestDistance = float.MaxValue;
                _noProgress = 0f;
                _state = FarmState.Moving;
                SetStatus($"colhendo {PrefabNameOf(ripe)}");
                LogEvent(
                    "farm|harvest|" + ripe.GetInstanceID(),
                    $"[ModIA] Cultivar: indo colher '{PrefabNameOf(ripe)}' a " +
                    $"{Vector3.Distance(companion.JobPosition, _targetPos):0.#} m.");
                return;
            }

            // 2) Plantar onde há vaga (enquanto houver semente no inventário).
            if (CompanionJobsConfig.FarmPlant && TryPickPlantSpot(companion, zone))
            {
                return;
            }

            // 3) Nada a fazer agora, mas tem planta crescendo: ele espera na fazenda (não conta
            //    varredura vazia — é o "cuidar do canteiro" até a colheita ficar pronta).
            if (growing)
            {
                _emptyScans = 0;
                SetStatus("esperando as plantas crescerem");
                companion.JobStopMoving();
                return;
            }

            // 4) Nada plantado, nada pronto e nada para plantar: o trabalho acabou.
            _emptyScans++;
            LogEvent(
                "farm|empty",
                $"[ModIA] Cultivar: nada para plantar nem colher na zona " +
                $"({_emptyScans}/{EmptyScansBeforeStop}).");

            if (_emptyScans >= EmptyScansBeforeStop)
            {
                Finish(companion, "nada para plantar nem colher na zona");
                return;
            }

            SetStatus("procurando o que fazer");
            companion.JobStopMoving();
        }

        /// <summary>
        /// Uma passada na zona: a planta pronta mais perto (se houver) e se existe <b>planta
        /// crescendo</b> (para ele esperar em vez de encerrar). Usa buffer reaproveitado — roda só
        /// no intervalo de varredura, nunca por frame.
        /// </summary>
        private void ScanZone(FarmZone zone, out Pickable ripe, out bool growing)
        {
            ripe = null;
            growing = false;

            if (s_zoneMask == -1)
            {
                s_zoneMask = LayerMask.GetMask(
                    "Default", "static_solid", "Default_small", "piece", "piece_nonsolid");
            }

            Vector3 me = _me;
            float best = float.MaxValue;

            int hits = Physics.OverlapSphereNonAlloc(zone.Center, zone.Radius, s_zoneBuffer, s_zoneMask);
            for (int i = 0; i < hits; i++)
            {
                Collider collider = s_zoneBuffer[i];
                if (collider == null)
                {
                    continue;
                }

                Plant plant = collider.GetComponent<Plant>() ?? collider.GetComponentInParent<Plant>();
                if (plant != null && plant.m_needCultivatedGround)
                {
                    // Só cultura de canteiro conta como "está crescendo": uma muda de árvore (ou
                    // planta decorativa) dentro da zona não prende o companheiro esperando.
                    growing = true;
                }

                Pickable pickable =
                    collider.GetComponent<Pickable>() ?? collider.GetComponentInParent<Pickable>();
                if (pickable == null || pickable.GetPicked() || !pickable.CanBePicked())
                {
                    continue;
                }

                if (pickable.m_itemPrefab == null ||
                    !FarmCatalog.IsCropOutput(Utils.GetPrefabName(pickable.m_itemPrefab.name)))
                {
                    continue;
                }

                Vector3 position = pickable.transform.position;
                if (!zone.Contains(position) || IsSkipped(position))
                {
                    continue;
                }

                float distance = (position - me).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    ripe = pickable;
                }
            }
        }

        /// <summary>Escolhe a semente do inventário e uma vaga livre da zona para ela.</summary>
        private bool TryPickPlantSpot(CompanionAI companion, FarmZone zone)
        {
            Inventory inventory = companion.JobInventory;
            if (inventory == null)
            {
                return false;
            }

            string seed = FarmCatalog.FindSeed(inventory, zone.Crop, out FarmCatalog.SeedPlantInfo info);
            if (seed == null || info.PlantPrefab == null)
            {
                return false;
            }

            if (!TryFindSpot(zone, info, out Vector3 spot))
            {
                LogEvent(
                    "farm|nospot|" + seed,
                    $"[ModIA] Cultivar: nenhuma vaga livre na zona para '{seed}' " +
                    $"(terreno cultivado, espaco livre e acesso).");
                return false;
            }

            _isHarvest = false;
            _plantSeed = seed;
            _plantInfo = info;
            _targetPos = spot;
            _bestDistance = float.MaxValue;
            _noProgress = 0f;
            _state = FarmState.Moving;
            SetStatus($"plantando {seed}");
            LogEvent(
                "farm|plant|" + seed,
                $"[ModIA] Cultivar: indo plantar '{seed}' em {spot.x:0.#}, {spot.z:0.#} " +
                $"({Vector3.Distance(companion.JobPosition, spot):0.#} m).");
            return true;
        }

        /// <summary>
        /// Procura uma vaga de plantio na zona: grade de <c>max(espacamento, raio de crescimento)</c>
        /// a partir do centro, <b>em anéis do mais perto para o mais longe</b> (sai na primeira que
        /// serve). Uma vaga serve quando tem chão, bioma compatível, terreno cultivado (quando a
        /// cultura exige), nenhuma planta/peça/colhível por perto, acesso na área privada e não está
        /// na lista de encalhados.
        /// </summary>
        private bool TryFindSpot(FarmZone zone, FarmCatalog.SeedPlantInfo info, out Vector3 spot)
        {
            spot = Vector3.zero;

            float spacing = Mathf.Max(CompanionJobsConfig.FarmSpacing, info.GrowRadius);
            float radius = zone.Radius;
            int rings = Mathf.CeilToInt(radius / spacing);
            // O raio da checagem é o de crescimento da cultura: dentro dele o próprio Plant marcaria
            // a nova planta como "sem espaço" (NoSpace) e ela nunca daria fruto.
            float clearRadius = Mathf.Max(0.4f, info.GrowRadius);

            if (s_spaceMask == -1)
            {
                s_spaceMask = LayerMask.GetMask(
                    "Default", "static_solid", "Default_small", "piece", "piece_nonsolid");
            }

            for (int ring = 0; ring <= rings; ring++)
            {
                for (int ix = -ring; ix <= ring; ix++)
                {
                    for (int iz = -ring; iz <= ring; iz++)
                    {
                        // Só a borda do anel (o miolo já foi visto nos anéis anteriores).
                        if (Mathf.Max(Mathf.Abs(ix), Mathf.Abs(iz)) != ring)
                        {
                            continue;
                        }

                        float lx = ix * spacing;
                        float lz = iz * spacing;
                        if (lx * lx + lz * lz > radius * radius)
                        {
                            continue;
                        }

                        Vector3 candidate = new Vector3(
                            zone.Center.x + lx, zone.Center.y, zone.Center.z + lz);

                        if (!GroundPoint(candidate, out candidate))
                        {
                            continue;
                        }

                        if (info.Biome != Heightmap.Biome.None &&
                            (Heightmap.FindBiome(candidate) & info.Biome) == 0)
                        {
                            continue;
                        }

                        if (info.NeedsCultivated && !FarmCatalog.IsCultivated(candidate))
                        {
                            continue;
                        }

                        if (IsSkipped(candidate))
                        {
                            continue;
                        }

                        int hits = Physics.OverlapSphereNonAlloc(
                            candidate, clearRadius, s_spaceBuffer, s_spaceMask);
                        bool blocked = false;
                        for (int i = 0; i < hits && !blocked; i++)
                        {
                            Collider collider = s_spaceBuffer[i];
                            blocked = collider != null &&
                                      (collider.GetComponent<Plant>() != null ||
                                       collider.GetComponent<Pickable>() != null ||
                                       collider.GetComponent<Piece>() != null);
                        }

                        if (blocked)
                        {
                            continue;
                        }

                        // Plantar é construir: nada dentro de uma área privada que não deixa entrar.
                        if (!PrivateArea.CheckAccess(candidate, 0f, false))
                        {
                            continue;
                        }

                        spot = candidate;
                        return true;
                    }
                }
            }

            return false;
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

            if (_isHarvest && !IsStillRipe(_pickable))
            {
                // Alguém colheu antes (ou a planta sumiu): volta a varrer.
                BackToScanning();
                return;
            }

            float reach = _isHarvest ? HarvestReach : PlantReach;
            Vector3 goal = _targetPos + Vector3.up * (_isHarvest ? 0.5f : 0.2f);
            float distance = Vector3.Distance(goal, companion.JobPosition);

            if (distance <= reach)
            {
                _state = FarmState.Working;
                _workTimer = WorkDelay;
                companion.JobStopMoving();
                return;
            }

            // Encalhe: não chega mais perto (cerca, barranco, canteiro cercado…). Veta o alvo por
            // um tempo e varre de novo — sem isso o companheiro ficaria empurrando o ar até o teto
            // de tempo do trabalho.
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
                    SkipSpot(_targetPos, _isHarvest ? "nao deu para chegar na planta" : "nao deu para chegar na vaga");
                    BackToScanning();
                    return;
                }
            }

            SetStatus(_isHarvest ? "indo colher" : "indo plantar");
            companion.JobApproach(dt, goal, reach, MovementOwner.Farm);
        }

        // ---------------------------------------------------------------------
        // No alvo: colher ou plantar
        // ---------------------------------------------------------------------

        private void TickWorking(CompanionAI companion, float dt)
        {
            _workTimer -= dt;
            if (_workTimer > 0f)
            {
                companion.JobStopMoving();
                return;
            }

            if (_isHarvest)
            {
                Harvest(companion);
            }
            else
            {
                Plant(companion);
            }
        }

        /// <summary>Colhe a planta pronta: o <c>Interact</c> dispara o RPC que larga os itens.</summary>
        private void Harvest(CompanionAI companion)
        {
            Humanoid humanoid = companion.JobHumanoid;
            if (humanoid == null || !IsStillRipe(_pickable))
            {
                BackToScanning();
                return;
            }

            Vector3 position = _pickable.transform.position;
            string name = PrefabNameOf(_pickable);

            companion.JobLookAt(position + Vector3.up * 0.4f);
            if (companion.JobCharacter != null && companion.JobCharacter.m_zanim != null)
            {
                companion.JobCharacter.m_zanim.SetTrigger("interact");
            }

            _pickable.Interact(humanoid, false, false);

            // A posição vale mesmo depois de a planta sumir (o RPC pode destruí-la na hora).
            _targetPos = position;
            _state = FarmState.Collecting;
            _workTimer = CollectDelay;
            SetStatus($"colhendo {name}");
            LogEvent("farm|picked|" + name, $"[ModIA] Cultivar: colhi '{name}' em {position.x:0.#}, {position.z:0.#}.");
        }

        /// <summary>
        /// Planta uma semente: consome uma unidade do inventário e instancia o prefab da planta no
        /// ponto, com a mesma sequência do <c>Player.PlacePiece</c>
        /// (<c>TerrainModifier.SetTriggerOnPlaced</c> + <c>Instantiate</c> + efeito de colocação).
        /// </summary>
        private void Plant(CompanionAI companion)
        {
            Inventory inventory = companion.JobInventory;
            if (inventory == null || _plantInfo.PlantPrefab == null)
            {
                BackToScanning();
                return;
            }

            FarmZone zone = CompanionJobsConfig.FarmZone;
            if (!zone.IsValid || !zone.Contains(_targetPos))
            {
                BackToScanning();
                return;
            }

            // A vaga pode ter sido ocupada desde a varredura (outra planta nasceu ali): confere.
            int hits = Physics.OverlapSphereNonAlloc(
                _targetPos, Mathf.Max(0.4f, _plantInfo.GrowRadius), s_spaceBuffer, s_spaceMask);
            for (int i = 0; i < hits; i++)
            {
                Collider collider = s_spaceBuffer[i];
                if (collider != null &&
                    (collider.GetComponent<Plant>() != null ||
                     collider.GetComponent<Pickable>() != null ||
                     collider.GetComponent<Piece>() != null))
                {
                    SkipSpot(_targetPos, "a vaga foi ocupada");
                    BackToScanning();
                    return;
                }
            }

            if (!ConsumeOne(inventory, _plantSeed))
            {
                // Sem semente (outra tarefa consumiu?): volta a varrer e escolhe outra.
                BackToScanning();
                return;
            }

            companion.JobLookAt(_targetPos);
            if (companion.JobCharacter != null && companion.JobCharacter.m_zanim != null)
            {
                companion.JobCharacter.m_zanim.SetTrigger("interact");
            }

            Quaternion rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            TerrainModifier.SetTriggerOnPlaced(true);
            GameObject placed = Object.Instantiate(_plantInfo.PlantPrefab, _targetPos, rotation);
            TerrainModifier.SetTriggerOnPlaced(false);

            Piece piece = placed != null ? placed.GetComponent<Piece>() : null;
            if (piece != null)
            {
                piece.m_placeEffect.Create(
                    _targetPos, rotation, placed.transform, 1f, -1, ZDOID.None);
            }

            AddProgress();
            SetStatus($"plantei {_plantSeed}");
            ShowProgress("Plantei", _plantSeed);
            LogEvent(
                "farm|planted|" + _plantSeed,
                $"[ModIA] Cultivar: plantei '{_plantSeed}' em {_targetPos.x:0.#}, {_targetPos.z:0.#} " +
                $"({FarmCatalog.CountSeed(inventory, _plantSeed)} restantes).");

            // Continua plantando (a próxima varredura já acha outra vaga) — com uma pausa curta
            // para o RPC da peça assentar.
            BackToScanning();
            _scanTimer = 0.4f;
        }

        // ---------------------------------------------------------------------
        // Catando a colheita
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
                companion, _targetPos, JobStations.DropRadius, FarmCatalog.CropOutputs, MaxPicksPerCollect);

            if (picked > 0)
            {
                AddProgress(picked);
                _harvestedSinceStore += picked;
                ShowProgress("Colhi", $"{picked} item(ns)");
                LogEvent("farm|gathered", $"[ModIA] Cultivar: catei {picked} item(ns) da colheita.");
            }
            else if (companion.JobIsPackFull)
            {
                // Inventário cheio: pede a viagem ao baú (a camada de depósito assume e o trabalho
                // pausa sozinho). Sem baú, o HandlePackFull avisa no HUD.
                ReportPackFull(companion, dt);
            }

            BackToScanning();
        }

        // ---------------------------------------------------------------------
        // Guardar a colheita
        // ---------------------------------------------------------------------

        /// <summary>
        /// Leva a colheita ao baú (Fase 2.14) a cada <c>Jobs/FarmDepositarCada</c> itens catados.
        /// A viagem pausa o trabalho sozinha (o <see cref="CompanionAI"/> só chama o tick quando
        /// não há viagem em andamento) e ele volta a cultivar depois.
        /// </summary>
        private void TryStoreHarvest(CompanionAI companion, float dt)
        {
            int every = CompanionJobsConfig.FarmStoreEvery;
            if (every <= 0 || _harvestedSinceStore < every)
            {
                return;
            }

            _storeTimer -= dt;
            if (_storeTimer > 0f)
            {
                return;
            }

            _storeTimer = StoreRetrySeconds;

            if (CompanionDeposit.TryStartDepositTrip(companion, companion.JobInventory))
            {
                _harvestedSinceStore = 0;
                LogEvent("farm|store", "[ModIA] Cultivar: colheita na mochila; levando ao bau.");
            }
        }

        // ---------------------------------------------------------------------
        // Apoio
        // ---------------------------------------------------------------------

        /// <summary>Volta ao estado de varredura (o próximo tick decide o que fazer).</summary>
        private void BackToScanning()
        {
            _state = FarmState.Scanning;
            _scanTimer = 0f;
            _pickable = null;
            _plantSeed = null;
            _plantInfo = default;
            _bestDistance = float.MaxValue;
            _noProgress = 0f;
        }

        /// <summary>A planta pronta ainda está lá (não foi colhida por outro)?</summary>
        private static bool IsStillRipe(Pickable pickable)
        {
            return pickable != null && !pickable.GetPicked() && pickable.CanBePicked();
        }

        /// <summary>Nome do prefab do objeto de mundo (o nome do GameObject pode ter "(Clone)").</summary>
        private static string PrefabNameOf(Pickable pickable)
        {
            if (pickable == null)
            {
                return "?";
            }

            return pickable.m_itemPrefab != null
                ? Utils.GetPrefabName(pickable.m_itemPrefab.name)
                : Utils.GetPrefabName(pickable.gameObject.name);
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

        /// <summary>Altura do chão neste ponto (false quando não há chão — fora do mapa).</summary>
        private static bool GroundPoint(Vector3 point, out Vector3 grounded)
        {
            grounded = point;
            ZoneSystem zone = ZoneSystem.instance;
            if (zone == null || !zone.GetGroundHeight(point, out float height))
            {
                return false;
            }

            grounded.y = height;
            return true;
        }

        private static Vector3 GroundPoint(Vector3 point)
        {
            return GroundPoint(point, out Vector3 grounded) ? grounded : point;
        }

        /// <summary>O ponto está na lista de encalhados (dentro do prazo)?</summary>
        private bool IsSkipped(Vector3 position)
        {
            for (int i = _skipped.Count - 1; i >= 0; i--)
            {
                SkippedSpot spot = _skipped[i];
                if (Time.time >= spot.Until)
                {
                    _skipped.RemoveAt(i);
                    continue;
                }

                float dx = spot.Position.x - position.x;
                float dz = spot.Position.z - position.z;
                if (dx * dx + dz * dz <= 1f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Veta um alvo por um tempo (não dá para chegar / a vaga foi ocupada).</summary>
        private void SkipSpot(Vector3 position, string reason)
        {
            if (_skipped.Count >= MaxSkippedSpots)
            {
                _skipped.Clear();
            }

            _skipped.Add(new SkippedSpot { Position = position, Until = Time.time + SpotSkipSeconds });
            LogEvent(
                "farm|skip|" + Mathf.RoundToInt(position.x * 10f) + "|" + Mathf.RoundToInt(position.z * 10f),
                $"[ModIA] Cultivar: deixando {position.x:0.#}, {position.z:0.#} de lado por " +
                $"{SpotSkipSeconds:0} s ({reason}).");
        }

        /// <summary>Lista de "manter N" das sementes para o depósito (Fase 2.14).</summary>
        private static Dictionary<string, int> BuildSeedKeepList()
        {
            Dictionary<string, int> keep = new Dictionary<string, int>();
            int amount = CompanionJobsConfig.FarmKeepSeeds;
            if (amount <= 0)
            {
                return keep;
            }

            List<string> seeds = FarmCatalog.Describe();
            for (int i = 0; i < seeds.Count; i++)
            {
                keep[seeds[i]] = amount;
            }

            return keep;
        }
    }
}
