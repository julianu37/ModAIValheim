using BepInEx.Configuration;
using ModAIValheim.Companion.Jobs;
using UnityEngine;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// <b>Seguir pelo portal</b> (Fase 2.18, seção <c>[Portal]</c> do <c>.cfg</c>).
    ///
    /// <para>
    /// <b>O problema.</b> No Valheim 1.0.17 quem atravessa um portal é o <c>Player</c>:
    /// <c>TeleportWorld.Interact</c> chama <c>TeleportWorld.Teleport(player)</c> →
    /// <c>Player.TeleportTo(pos, rot, distantTeleport)</c>, que liga o estado de teleporte
    /// (<c>Player.IsTeleporting</c>), move o corpo depois de ~2 s e só "termina" quando a área de
    /// destino está pronta (<c>Player.UpdateTeleport</c> + <c>ZNetScene.IsAreaReady</c> +
    /// <c>ZoneSystem.FindFloor</c>). O companheiro é um <c>Humanoid</c>, não um <c>Player</c>: ele
    /// não tem como entrar no portal (e <c>Character.TeleportTo</c> é vazio na base do jogo), então
    /// sem esta camada ele <b>fica para trás</b>.
    /// </para>
    ///
    /// <para>
    /// <b>O que fazemos.</b> Observamos o <b>estado de teleporte do jogador</b> (API pública,
    /// <c>Player.IsTeleporting</c> — sem patch Harmony nenhum):
    /// </para>
    /// <list type="number">
    ///   <item><description>quando o teleporte <b>começa</b>, guardamos o <b>portal</b> por perto
    ///   (o <c>TeleportWorld</c> mais próximo do jogador, se houver) — é para lá que o companheiro
    ///   vai <b>a pé</b>, como um jogador entraria;</description></item>
    ///   <item><description>enquanto o jogador não chega do outro lado, o companheiro <b>anda até o
    ///   portal</b> (<see cref="MovementOwner.Portal"/>) e para ali;</description></item>
    ///   <item><description>quando o teleporte do jogador <b>termina</b>, o companheiro é levado para
    ///   <b>junto dele</b> (<see cref="CompanionAI.JobWarp"/>) — o "atravessar junto" do pedido.</description></item>
    /// </list>
    ///
    /// <para>
    /// <b>Não entra em loop:</b> só existe <b>uma</b> viagem por teleporte (o estado é limpo ao
    /// levar o companheiro), ele <b>não</b> é levado quando já está perto do jogador (portais curtos,
    /// o mesmo lado da base) e há um <b>teto de tempo</b> (<c>Portal/TempoMaximo</c>) para desistir.
    /// Também <b>não</b> age quando ele tem algo mais importante no comando (trabalho, coleta,
    /// viagem ao baú, loot, combate ou carroça engatada): aí ele fica onde está, e o
    /// <c>Casa/VoltarSePerdido</c> (Fase 2.17) continua sendo o caminho de volta para casa.
    /// </para>
    ///
    /// <para>
    /// Adaptado (lógica) de <c>src/Companions/Patches/PortalPatches.cs</c> do
    /// <i>Offline Companions Reborn</i> (MIT — ver <c>THIRD_PARTY_NOTICES.md</c>): lá o mod patcheia
    /// <c>Player.TeleportTo</c>/<c>Player.UpdateTeleport</c> por Harmony, enfileira os companheiros
    /// que estão em follow e os "warpa" para o ponto de chegada (<c>WarpCompanion</c>: posição no
    /// ZDO + <c>transform</c> + <c>Rigidbody</c> + <c>ZoneSystem.FindFloor</c>). Aqui a detecção é
    /// por <b>observação</b> do estado público do jogador (sem patch), o companheiro <b>anda até o
    /// portal</b> antes de ser levado e o alvo é sempre o jogador local — a mesma ideia, sem cópia de
    /// arquivo.
    /// </para>
    /// </summary>
    internal static class CompanionPortalFollow
    {
        /// <summary>Teto (s) da viagem: o jogador não chegou e desistimos.</summary>
        private const float DefaultTimeout = 30f;

        /// <summary>Distância (m) do jogador em que não vale a pena "atravessar" (ele já está junto).</summary>
        private const float SkipWarpDistance = 8f;

        /// <summary>Quantos colliders a varredura do portal examina por vez.</summary>
        private const int ScanBufferSize = 16;

        private static readonly Collider[] s_colliderBuffer = new Collider[ScanBufferSize];

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<float> _portalRadius;
        private static ConfigEntry<float> _portalReach;
        private static ConfigEntry<float> _timeout;

        /// <summary>Um teleporte do jogador está em andamento e o companheiro está indo atrás.</summary>
        private static bool _pending;

        /// <summary>Estado de teleporte observado no tick anterior (para pegar o início).</summary>
        private static bool _playerWasTeleporting;

        /// <summary>Instante (s) em que o teleporte começou.</summary>
        private static float _pendingSince;

        /// <summary>Posição do portal que o jogador usou (quando havia um por perto).</summary>
        private static Vector3 _portalPosition;
        private static bool _hasPortal;

        private static int s_pieceMask = -1;

        internal static void Configure(
            ConfigEntry<bool> enabled,
            ConfigEntry<float> portalRadius,
            ConfigEntry<float> portalReach,
            ConfigEntry<float> timeout)
        {
            _enabled = enabled;
            _portalRadius = portalRadius;
            _portalReach = portalReach;
            _timeout = timeout;

            Plugin.Log.LogInfo(
                $"[ModIA] Portal: seguir pelo portal = {Enabled} | portal a ate {PortalRadius:0} m | " +
                $"entra nele a {PortalReach:0.#} m | desiste em {Timeout:0} s.");
        }

        /// <summary>Ligado na config (<c>Portal/Ligado</c>).</summary>
        internal static bool Enabled => _enabled == null || _enabled.Value;

        /// <summary>Raio (m) em que procuramos o portal que o jogador usou.</summary>
        private static float PortalRadius =>
            _portalRadius != null ? Mathf.Clamp(_portalRadius.Value, 2f, 40f) : 12f;

        /// <summary>Distância (m) do portal em que o companheiro considera que "entrou" nele.</summary>
        private static float PortalReach =>
            _portalReach != null ? Mathf.Clamp(_portalReach.Value, 1f, 10f) : 2.5f;

        /// <summary>Teto (s) para o jogador chegar do outro lado.</summary>
        private static float Timeout =>
            _timeout != null ? Mathf.Clamp(_timeout.Value, 5f, 120f) : DefaultTimeout;

        /// <summary>Solta o estado estático (unload do plugin / fim do mundo).</summary>
        internal static void Shutdown()
        {
            Reset();
        }

        /// <summary>
        /// Um passo da camada. Roda a cada tick da IA (≈20 Hz) e é barato fora de um teleporte: só lê
        /// o estado do jogador local.
        /// </summary>
        internal static void Tick(CompanionAI companion, float dt)
        {
            if (!Enabled || companion == null)
            {
                Reset();
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null)
            {
                Reset();
                return;
            }

            bool teleporting = player.IsTeleporting();

            // Início de um teleporte do jogador (portal, morte, comando de console...).
            if (teleporting && !_playerWasTeleporting)
            {
                Begin(companion, player);
            }

            _playerWasTeleporting = teleporting;

            if (!_pending)
            {
                return;
            }

            if (Time.time - _pendingSince > Timeout)
            {
                Plugin.Log.LogInfo("[ModIA] Portal: o teleporte demorou demais; desisti de seguir.");
                Reset();
                return;
            }

            if (teleporting)
            {
                // O jogador ainda está no caminho: o companheiro vai até o portal e espera ali.
                if (_hasPortal)
                {
                    float distance = Vector3.Distance(companion.transform.position, _portalPosition);
                    if (distance > PortalReach)
                    {
                        companion.JobApproach(dt, _portalPosition, PortalReach, MovementOwner.Portal);
                        return;
                    }
                }

                companion.JobHold(MovementOwner.Portal);
                return;
            }

            WarpToPlayer(companion, player);
        }

        /// <summary>
        /// O teleporte do jogador começou: guarda o portal (quando há um perto) e decide se vale
        /// seguir. Companheiro ocupado (trabalho/coleta/baú/loot/combate) ou puxando a carroça fica
        /// onde está — ele é um membro da equipe, não um passageiro.
        /// </summary>
        private static void Begin(CompanionAI companion, Player player)
        {
            if (CompanionJobQueue.HasJob ||
                CompanionHarvest.IsActive ||
                CompanionDeposit.TripActive ||
                CompanionLoot.HasWork ||
                CompanionCart.Busy ||
                companion.GetTargetCreature() != null)
            {
                Plugin.Log.LogInfo(
                    "[ModIA] Portal: voce atravessou um portal, mas o companheiro esta ocupado " +
                    "(trabalho/coleta/carga/combate); ele fica onde esta.");
                return;
            }

            _pending = true;
            _pendingSince = Time.time;
            _portalPosition = FindPortalNear(player.transform.position, PortalRadius);
            _hasPortal = _portalPosition != Vector3.zero;

            Plugin.Log.LogInfo(_hasPortal
                ? "[ModIA] Portal: voce esta atravessando um portal; o companheiro vai atras."
                : "[ModIA] Portal: teleporte detectado (sem portal por perto); o companheiro vai junto " +
                  "quando voce chegar.");
        }

        /// <summary>
        /// O jogador chegou do outro lado: leva o companheiro para junto dele — a menos que ele já
        /// esteja perto (portais curtos, o mesmo lado da base), que é o que evita o vaivém.
        /// </summary>
        private static void WarpToPlayer(CompanionAI companion, Player player)
        {
            Vector3 destination = FindArrivalPosition(player.transform.position);

            if (Vector3.Distance(companion.transform.position, destination) < SkipWarpDistance)
            {
                Reset();
                return;
            }

            // Descanso/sono não sobrevivem a uma viagem (a cama e a fogueira ficaram do outro lado).
            CompanionRest.Stop(companion, "seguindo pelo portal");

            companion.JobWarp(destination, player.transform.rotation);

            Plugin.Log.LogInfo(
                $"[ModIA] Portal: o companheiro atravessou com voce e chegou em " +
                $"{destination.x:0.#}, {destination.z:0.#}.");
            CompanionJob.ShowHud("Companheiro atravessou o portal com voce");

            Reset();
        }

        /// <summary>
        /// Um ponto de chegada perto do jogador: tenta alguns deslocamentos curtos e assenta no chão
        /// do <c>ZoneSystem</c> (<c>FindFloor</c>), como o <c>WarpCompanion</c> da referência. Sem
        /// chão conhecido, chega um pouco acima do jogador e cai.
        /// </summary>
        private static Vector3 FindArrivalPosition(Vector3 origin)
        {
            ZoneSystem zone = ZoneSystem.instance;

            for (int i = 0; i < s_offsets.Length; i++)
            {
                Vector3 candidate = origin + s_offsets[i];
                if (zone != null && zone.FindFloor(candidate, out float height))
                {
                    candidate.y = height + 0.5f;
                    return candidate;
                }
            }

            return origin + s_offsets[0];
        }

        /// <summary>Deslocamentos curtos testados em volta do ponto de chegada (sem alocar por tick).</summary>
        private static readonly Vector3[] s_offsets =
        {
            new Vector3(2f, 0.5f, 0f),
            new Vector3(-2f, 0.5f, 0f),
            new Vector3(0f, 0.5f, 2f),
            new Vector3(0f, 0.5f, -2f),
            new Vector3(1.5f, 0.5f, 1.5f),
            new Vector3(-1.5f, 0.5f, -1.5f),
        };

        /// <summary>
        /// Posição do <c>TeleportWorld</c> mais próximo (zero quando não há). É o portal que o jogador
        /// acabou de usar — a varredura roda uma vez por teleporte, nunca por frame.
        /// </summary>
        private static Vector3 FindPortalNear(Vector3 position, float radius)
        {
            int count = Physics.OverlapSphereNonAlloc(
                position, radius, s_colliderBuffer, PieceMask, QueryTriggerInteraction.Collide);

            Vector3 best = Vector3.zero;
            float bestDistance = radius;

            for (int i = 0; i < count; i++)
            {
                Collider collider = s_colliderBuffer[i];
                if (collider == null)
                {
                    continue;
                }

                TeleportWorld portal = collider.GetComponentInParent<TeleportWorld>();
                if (portal == null)
                {
                    continue;
                }

                float distance = Vector3.Distance(portal.transform.position, position);
                if (distance < bestDistance)
                {
                    best = portal.transform.position;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static int PieceMask
        {
            get
            {
                if (s_pieceMask == -1)
                {
                    s_pieceMask = LayerMask.GetMask(
                        "piece", "piece_nonsolid", "Default", "static_solid");
                }

                return s_pieceMask;
            }
        }

        private static void Reset()
        {
            _pending = false;
            _hasPortal = false;
            _playerWasTeleporting = false;
        }

        /// <summary>Linha de estado para o <c>modia_stats</c>.</summary>
        internal static string Describe()
        {
            if (!Enabled)
            {
                return "desligado (Portal/Ligado = false)";
            }

            if (!_pending)
            {
                return "ligado (ele atravessa com voce)";
            }

            return _hasPortal ? "seguindo voce por um portal" : "esperando voce chegar do teleporte";
        }
    }
}
