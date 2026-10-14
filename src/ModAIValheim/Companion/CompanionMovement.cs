using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// Quem dirige o movimento do companheiro no frame atual. É o "dono único" que evita que
    /// follow nativo, coleta e as guardas de porta/barranco chamem <c>MoveTo</c>/<c>MoveTowards</c>
    /// ao mesmo tempo (a última escrita venceria de forma imprevisível).
    /// Prioridade: <see cref="Harvest"/> (coleta ativa) &gt; <see cref="Loot"/> (catar abate) &gt;
    /// <see cref="Native"/> (follow/combate do motor do jogo); o combate vence a coleta pausando-a
    /// antes de chegar aqui.
    /// </summary>
    internal enum MovementOwner
    {
        /// <summary>Ninguém reivindicou o frame (estado inicial/transitório).</summary>
        None,

        /// <summary>O motor nativo do jogo (follow/perseguição do <c>MonsterAI</c>).</summary>
        Native,

        /// <summary>A camada de coleta (aproximação do nó/drop).</summary>
        Harvest,

        /// <summary>
        /// A camada de <b>loot de criatura</b> (aproximação do corpo/itens de um abate do próprio
        /// companheiro). Só existe quando não há tarefa de coleta ativa.
        /// </summary>
        Loot,

        /// <summary>
        /// A camada de <b>depósito em baú</b> (Fase 2.14): o companheiro está indo/voltando de um
        /// baú para levar a carga (peso/inventário cheio) ou buscar/devolver uma ferramenta
        /// (tool keeper). Vence a coleta enquanto está ativa (a tarefa continua viva e volta depois).
        /// </summary>
        Deposit,

        /// <summary>
        /// O <b>trabalho de caçar</b> (Fase 2.16): quem persegue a presa é o motor nativo do jogo
        /// (o alvo foi entregue por <c>CompanionAI.JobEngage</c>), mas a camada do trabalho é a dona
        /// do frame enquanto ele roda — é o que impede a guarda de follow de mexer no passo.
        /// </summary>
        Hunt,

        /// <summary>
        /// O <b>trabalho de pescar</b> (Fase 2.16): o companheiro anda até o ponto de pesca e para
        /// lá (arremessar/puxar não dirigem movimento, mas o frame continua sendo dele).
        /// </summary>
        Fish,

        /// <summary>
        /// O <b>trabalho de cultivar</b> (Fase 2.16b): andar até o canteiro (colher) ou até o ponto
        /// livre da zona de fazenda (plantar).
        /// </summary>
        Farm,

        /// <summary>
        /// O <b>trabalho de cozinhar</b> (Fase 2.16b): andar até a estação (fogueira, espeto,
        /// caldeirão, barril) para abastecer, esperar o preparo e retirar o prato/bebida.
        /// </summary>
        Cook,

        /// <summary>
        /// O <b>trabalho de fundir</b> (Fase 2.16b): andar até o forno/fornalha/carvoeira para
        /// abastecer e retirar o produto.
        /// </summary>
        Smelt,
    }

    /// <summary>
    /// Guardas de <b>movimento</b> do companheiro (Fase 2.10, itens #2 e #5 da auditoria
    /// comparativa vs Offline Companions Reborn, MIT).
    ///
    /// <para>
    /// Este tipo concentra as duas checagens que acontecem <i>antes de dar um passo</i> — seja na
    /// coleta (<c>ApproachAndWork</c>) ou no follow nativo — e devolve uma única decisão
    /// (<see cref="StepAction"/>) para quem estiver dirigindo o companheiro naquele frame:
    /// </para>
    /// <list type="number">
    ///   <item><description><b>#5 — portas:</b> se houver uma porta <b>fechada e utilizável</b> logo
    ///   à frente (raycast curto por <see cref="Door"/>), o companheiro vira para ela e chama
    ///   <c>Door.Interact(humanoid, hold: false, alt: false)</c>; depois de passar, fecha de novo
    ///   (<see cref="TickOpenedDoors"/>). Portas com <c>m_keyItem</c> e portas em
    ///   <see cref="PrivateArea"/> sem acesso <b>nunca</b> são abertas (mesmo filtro do
    ///   <c>DoorHandler.IsValidClosedDoor</c> da referência).</description></item>
    ///   <item><description><b>#2 — proteção contra queda:</b> um <c>SphereCast</c> curto na direção
    ///   do passo (com <c>ZoneSystem.GetSolidHeight</c>/<c>GetGroundHeight</c> como reserva) mede
    ///   quanto o chão cai à frente; caindo mais que <c>Movimento/QuedaMaxima</c> o passo é
    ///   <b>desviado</b> para um lado seguro ou <b>abortado</b>. Água funda abaixo não conta como
    ///   barranco (cair n'água é seguro).</description></item>
    /// </list>
    ///
    /// <para>
    /// <b>Sem alocação por frame:</b> todos os buffers de colliders são estáticos e reaproveitados,
    /// as sondagens são <c>Physics.*NonAlloc</c>/<c>SphereCast</c> e os desvios usam só aritmética de
    /// structs. As sondagens rodam no tick da IA (~20 Hz), nunca em <c>Update</c> de GUI.
    /// </para>
    /// </summary>
    internal static class CompanionMovement
    {
        /// <summary>O que fazer com o passo que está sendo dado agora.</summary>
        internal enum StepAction
        {
            /// <summary>Segue em frente (no ponto original ou num ponto desviado).</summary>
            Move,

            /// <summary>Interagimos com algo (porta): parar neste frame.</summary>
            Halted,

            /// <summary>Sem caminho seguro (barranco e nenhum desvio): parar.</summary>
            Blocked,
        }

        /// <summary>Distância (m) do raycast que procura uma porta à frente.</summary>
        private const float DoorReach = 2.5f;

        /// <summary>Comprimento (m) do passo lateral usado para contornar um barranco.</summary>
        private const float SafeStepLength = 2.5f;

        /// <summary>Tempo mínimo (s) depois de abrir uma porta antes de poder fechá-la de novo.</summary>
        private const float DoorCloseDelay = 1.5f;

        /// <summary>Distância (m) da porta a partir da qual fechamos a que abrimos (já passamos).</summary>
        private const float DoorCloseDistance = 3.5f;

        /// <summary>Não fechamos a porta se o jogador ainda estiver a menos disso dela.</summary>
        private const float DoorKeepOpenPlayerDistance = 2.5f;

        /// <summary>Veto (s) a uma porta que acabou de ser fechada/recusada (evita reabrir em loop).</summary>
        private const float DoorCooldown = 1.5f;

        /// <summary>Intervalo (s) mínimo entre tentativas de abrir porta.</summary>
        private const float DoorAttemptInterval = 0.25f;

        /// <summary>Queda (m) usada quando não há chão nenhum sob o ponto sondado (penhasco).</summary>
        private const float MissingGroundDrop = 60f;

        /// <summary>Ângulos (graus) testados para desviar de um barranco.</summary>
        private static readonly float[] s_sideAngles = { 35f, -35f, 70f, -70f, 100f, -100f };

        private static readonly Collider[] s_characterBuffer = new Collider[8];
        private static readonly Dictionary<int, float> s_doorCooldown = new Dictionary<int, float>();
        private static readonly List<OpenedDoor> s_openedDoors = new List<OpenedDoor>(4);
        private static WaterVolume s_waterVolume;
        private static float s_nextDoorAttempt;

        private static int s_solidMask = -1;
        private static int s_doorMask = -1;
        private static int s_characterMask = -1;

        // --- Configuração -----------------------------------------------------
        private static ConfigEntry<bool> _ledgeProtection;
        private static ConfigEntry<float> _maxDrop;
        private static ConfigEntry<float> _probeDistance;
        private static ConfigEntry<bool> _openDoors;

        /// <summary>Se o companheiro deve evitar barrancos/penhascos (#2).</summary>
        internal static bool LedgeProtection => _ledgeProtection == null || _ledgeProtection.Value;

        /// <summary>Queda máxima (m) tolerada num passo antes de considerar barranco.</summary>
        internal static float MaxDrop => _maxDrop != null ? Mathf.Clamp(_maxDrop.Value, 0.4f, 5f) : 1.2f;

        /// <summary>Distância (m) à frente onde o chão é sondado.</summary>
        internal static float ProbeDistance =>
            _probeDistance != null ? Mathf.Clamp(_probeDistance.Value, 0.5f, 3f) : 1.2f;

        /// <summary>Se o companheiro abre (e fecha) portas no caminho (#5).</summary>
        internal static bool OpenDoors => _openDoors == null || _openDoors.Value;

        internal static void Configure(
            ConfigEntry<bool> ledgeProtection,
            ConfigEntry<float> maxDrop,
            ConfigEntry<float> probeDistance,
            ConfigEntry<bool> openDoors)
        {
            _ledgeProtection = ledgeProtection;
            _maxDrop = maxDrop;
            _probeDistance = probeDistance;
            _openDoors = openDoors;

            Plugin.Log.LogInfo(
                $"[ModIA] Movimento: protecao contra queda = {LedgeProtection} " +
                $"(queda maxima {MaxDrop:0.#} m, sondagem {ProbeDistance:0.#} m) | " +
                $"abrir portas = {OpenDoors}.");
        }

        /// <summary>Solta o estado estático (unload do plugin / fim do mundo).</summary>
        internal static void Shutdown()
        {
            s_openedDoors.Clear();
            s_doorCooldown.Clear();
            s_nextDoorAttempt = 0f;
            s_waterVolume = null;
        }

        // ---------------------------------------------------------------------
        // Decisão do passo (porta + barranco)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Prepara o passo em direção a <paramref name="target"/>: abre a porta fechada à frente
        /// (quando há) ou desvia/aborta o passo se o chão cair demais à frente. Em
        /// <see cref="StepAction.Move"/> <paramref name="adjusted"/> pode ser o próprio
        /// <paramref name="target"/> (caminho livre) ou um ponto lateral seguro (desvio).
        /// </summary>
        internal static StepAction PrepareStep(Character character, Vector3 target, out Vector3 adjusted)
        {
            adjusted = target;
            if (character == null)
            {
                return StepAction.Move;
            }

            Vector3 position = character.transform.position;
            Vector3 direction = target - position;
            direction.y = 0f;
            float distance = direction.magnitude;
            if (distance < 0.05f)
            {
                return StepAction.Move;
            }

            direction /= distance;

            // #5 — porta fechada e utilizável logo à frente: abre e para o passo neste frame.
            if (OpenDoors && TryOpenDoorAhead(character, position, direction, distance))
            {
                return StepAction.Halted;
            }

            // #2 — barranco/penhasco à frente: contorna ou para.
            if (LedgeProtection && character.IsOnGround() && !character.IsSwimming() &&
                LedgeAhead(position, direction, out _))
            {
                if (TryFindSafeDirection(position, direction, out Vector3 safe))
                {
                    adjusted = position + safe * Mathf.Min(distance, SafeStepLength);
                    return StepAction.Move;
                }

                return StepAction.Blocked;
            }

            return StepAction.Move;
        }

        /// <summary>
        /// Fecha as portas que o companheiro abriu no caminho, quando ele (e o jogador) já passaram
        /// e ninguém está na soleira. É o "fechando ao passar" do item #5. Barato: a lista só tem as
        /// portas abertas por nós (nunca mais que um punhado).
        /// </summary>
        internal static void TickOpenedDoors(Character character)
        {
            if (s_openedDoors.Count == 0 || character == null)
            {
                return;
            }

            Vector3 position = character.transform.position;
            for (int i = s_openedDoors.Count - 1; i >= 0; i--)
            {
                OpenedDoor entry = s_openedDoors[i];
                Door door = entry.Door;

                if (door == null)
                {
                    s_openedDoors.RemoveAt(i);
                    continue;
                }

                if (Time.time - entry.At > 60f)
                {
                    s_openedDoors.RemoveAt(i);
                    continue;
                }

                if (!IsOpenDoor(door))
                {
                    // Alguém (ou o jogo) já a fechou: nada a fazer.
                    s_openedDoors.RemoveAt(i);
                    continue;
                }

                if (Time.time - entry.At < DoorCloseDelay)
                {
                    continue;
                }

                Vector3 doorPosition = door.transform.position;
                if (FlatDistance(position, doorPosition) < DoorCloseDistance)
                {
                    continue; // ainda estamos passando por ela
                }

                Player player = Player.m_localPlayer;
                if (player != null &&
                    Vector3.Distance(player.transform.position, doorPosition) < DoorKeepOpenPlayerDistance)
                {
                    continue;
                }

                if (AnyCharacterIn(doorPosition, 1.2f))
                {
                    continue;
                }

                FaceTo(character, doorPosition);
                Humanoid humanoid = character as Humanoid;
                bool closed = false;
                try
                {
                    closed = humanoid != null && door.Interact(humanoid, false, false);
                }
                catch
                {
                    closed = false;
                }

                if (closed)
                {
                    s_doorCooldown[door.GetInstanceID()] = Time.time + DoorCooldown;
                    Plugin.Log.LogInfo($"[ModIA] Movimento: fechei a porta '{door.m_name}' depois de passar.");
                }

                s_openedDoors.RemoveAt(i);
            }
        }

        // ---------------------------------------------------------------------
        // #2 — proteção contra queda
        // ---------------------------------------------------------------------

        /// <summary>
        /// O chão a um passo à frente está faltando ou caiu mais que <see cref="MaxDrop"/>? Combina
        /// um <c>SphereCast</c> curto (colliders do mundo: terreno, peças, pedras) com a altura do
        /// <c>ZoneSystem</c> (heightmap) como reserva. Água com 1 m ou mais de profundidade abaixo
        /// não conta como barranco: cair n'água é seguro.
        /// </summary>
        internal static bool LedgeAhead(Vector3 position, Vector3 direction, out float drop)
        {
            drop = 0f;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            direction.Normalize();

            float maxDrop = MaxDrop;
            Vector3 probe = position + direction * ProbeDistance;
            Vector3 origin = probe + Vector3.up;

            if (Physics.SphereCast(
                    origin, 0.25f, Vector3.down, out RaycastHit hit, 1f + maxDrop, SolidMask,
                    QueryTriggerInteraction.Ignore))
            {
                drop = position.y - hit.point.y;
                if (drop <= maxDrop)
                {
                    return false;
                }

                return !IsDeepWater(probe, hit.point.y);
            }

            if (TryTerrainHeight(probe, out float ground))
            {
                drop = position.y - ground;
                if (drop <= maxDrop)
                {
                    return false;
                }

                return !IsDeepWater(probe, ground);
            }

            drop = MissingGroundDrop;
            return true;
        }

        /// <summary>
        /// Direção lateral (girada em alguns ângulos) cujo chão à frente é seguro. Devolve
        /// <c>false</c> quando nenhum lado serve (aí o passo é abortado).
        /// </summary>
        private static bool TryFindSafeDirection(Vector3 position, Vector3 direction, out Vector3 safe)
        {
            safe = Vector3.zero;
            for (int i = 0; i < s_sideAngles.Length; i++)
            {
                Vector3 candidate = Quaternion.Euler(0f, s_sideAngles[i], 0f) * direction;
                candidate.y = 0f;
                if (candidate.sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                candidate.Normalize();
                if (!LedgeAhead(position, candidate, out _))
                {
                    safe = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Altura do terreno (heightmap) no ponto, quando o <c>ZoneSystem</c> a conhece.</summary>
        private static bool TryTerrainHeight(Vector3 point, out float height)
        {
            height = 0f;
            ZoneSystem zone = ZoneSystem.instance;
            if (zone == null)
            {
                return false;
            }

            float solid = zone.GetSolidHeight(point);
            if (IsValidHeight(solid))
            {
                height = solid;
                return true;
            }

            float ground = zone.GetGroundHeight(point);
            if (IsValidHeight(ground))
            {
                height = ground;
                return true;
            }

            return false;
        }

        private static bool IsValidHeight(float height)
        {
            return !float.IsNaN(height) && height > -1000f;
        }

        /// <summary>Água com pelo menos 1 m de profundidade sobre <paramref name="groundY"/>.</summary>
        private static bool IsDeepWater(Vector3 point, float groundY)
        {
            float waterLevel = Floating.GetWaterLevel(point, ref s_waterVolume);
            return waterLevel > -10000f && waterLevel - groundY >= 1f;
        }

        // ---------------------------------------------------------------------
        // #5 — portas
        // ---------------------------------------------------------------------

        /// <summary>
        /// Abre a porta fechada e utilizável entre o companheiro e o alvo (raycast curto na direção
        /// do passo). Devolve <c>true</c> quando abriu (o passo deve parar neste frame para não
        /// atravessar a folha antes da animação).
        /// </summary>
        private static bool TryOpenDoorAhead(Character character, Vector3 position, Vector3 direction, float distance)
        {
            if (Time.time < s_nextDoorAttempt)
            {
                return false;
            }

            float reach = Mathf.Min(distance, DoorReach);
            if (reach < 0.3f)
            {
                return false;
            }

            Vector3 from = position + Vector3.up;
            if (!Physics.Raycast(from, direction, out RaycastHit hit, reach, DoorMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            Door door = hit.collider != null ? hit.collider.GetComponentInParent<Door>() : null;
            if (!IsUsableClosedDoor(door))
            {
                return false;
            }

            s_nextDoorAttempt = Time.time + DoorAttemptInterval;
            FaceTo(character, door.transform.position);

            Humanoid humanoid = character as Humanoid;
            bool opened = false;
            try
            {
                opened = humanoid != null && door.Interact(humanoid, false, false);
            }
            catch
            {
                opened = false;
            }

            if (!opened)
            {
                // Não deu (dono, distância, trava): evita insistir na mesma porta a cada tick.
                s_doorCooldown[door.GetInstanceID()] = Time.time + DoorCooldown;
                return false;
            }

            RegisterOpened(door);
            Plugin.Log.LogInfo($"[ModIA] Movimento: abri a porta '{door.m_name}' no caminho.");
            return true;
        }

        /// <summary>
        /// Porta fechada que o companheiro pode abrir com segurança: existe, é uma <see cref="Door"/>
        /// válida, está fechada no ZDO, <b>não</b> exige item de chave e não está numa área privada
        /// sem acesso. Espelha a lógica de <c>DoorHandler.IsValidClosedDoor</c> da referência.
        /// </summary>
        private static bool IsUsableClosedDoor(Door door)
        {
            if (door == null || !IsClosedDoor(door))
            {
                return false;
            }

            if (door.m_keyItem != null)
            {
                return false;
            }

            if (door.m_checkGuardStone &&
                !PrivateArea.CheckAccess(door.transform.position, 0f, false, false))
            {
                return false;
            }

            return !IsOnCooldown(door);
        }

        private static bool IsClosedDoor(Door door)
        {
            return TryGetDoorState(door, out int state) && state == 0;
        }

        private static bool IsOpenDoor(Door door)
        {
            return TryGetDoorState(door, out int state) && state != 0;
        }

        private static bool TryGetDoorState(Door door, out int state)
        {
            state = 0;
            if (door == null)
            {
                return false;
            }

            ZNetView netView = door.GetComponent<ZNetView>();
            if (netView == null || !netView.IsValid())
            {
                return false;
            }

            ZDO zdo = netView.GetZDO();
            if (zdo == null)
            {
                return false;
            }

            state = zdo.GetInt(ZDOVars.s_state, 0);
            return true;
        }

        private static bool IsOnCooldown(Door door)
        {
            if (s_doorCooldown.Count == 0)
            {
                return false;
            }

            int id = door.GetInstanceID();
            if (!s_doorCooldown.TryGetValue(id, out float until))
            {
                return false;
            }

            if (Time.time >= until)
            {
                s_doorCooldown.Remove(id);
                return false;
            }

            return true;
        }

        private static void RegisterOpened(Door door)
        {
            if (s_openedDoors.Count >= 8)
            {
                s_openedDoors.RemoveAt(0);
            }

            s_openedDoors.Add(new OpenedDoor { Door = door, At = Time.time });
        }

        private static void FaceTo(Character character, Vector3 point)
        {
            Vector3 direction = point - character.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
            {
                character.SetLookDir(direction.normalized, 0f);
            }
        }

        private static bool AnyCharacterIn(Vector3 point, float radius)
        {
            int count = Physics.OverlapSphereNonAlloc(
                point, radius, s_characterBuffer, CharacterMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider collider = s_characterBuffer[i];
                if (collider != null && collider.GetComponentInParent<Character>() != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        // ---------------------------------------------------------------------
        // Máscaras (construídas uma vez)
        // ---------------------------------------------------------------------

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

        private static int DoorMask
        {
            get
            {
                if (s_doorMask == -1)
                {
                    s_doorMask = LayerMask.GetMask("piece", "piece_nonsolid", "Default");
                }

                return s_doorMask;
            }
        }

        private static int CharacterMask
        {
            get
            {
                if (s_characterMask == -1)
                {
                    s_characterMask = LayerMask.GetMask("character", "character_net", "character_ghost");
                }

                return s_characterMask;
            }
        }

        /// <summary>Porta que abrimos no caminho, para fechar de novo depois de passar (#5).</summary>
        private struct OpenedDoor
        {
            public Door Door;
            public float At;
        }
    }
}
