using System.Collections.Generic;
using UnityEngine;

namespace ModAIValheim.Companion.Jobs
{
    /// <summary>
    /// Apoio comum aos trabalhos de <b>estação</b> (cozinhar e fundir, Fase 2.16b): achar as peças
    /// (<see cref="Piece"/>) num raio com buffer reaproveitado, o ponto de onde se usa cada uma, e
    /// catar os itens que as estações largam no chão.
    ///
    /// <para>
    /// <b>Nada alocado por frame:</b> as listas e os buffers de collider são estáticos e
    /// reaproveitados, e tudo roda no tick do trabalho (com intervalo de varredura), nunca em
    /// <c>Update</c> de GUI. Como só existe <b>um</b> trabalho ativo por vez
    /// (<see cref="CompanionJobQueue"/>), os buffers estáticos não têm concorrência.
    /// </para>
    ///
    /// <para>
    /// Adaptado (lógica) de <c>src/Companions/Jobs/CookController.cs</c> (<c>Scan</c> —
    /// <c>Piece.GetAllPiecesInRadius</c> + <c>GetComponent&lt;T&gt;</c> por tipo de estação, e
    /// <c>PickUpDrops</c> — catar os itens que a estação largou) e de
    /// <c>src/Companions/Jobs/SmeltController.cs</c> (<c>ScanNearbySmelters</c>,
    /// <c>CountGroundDropsNearSmelter</c>) do <i>Offline Companions Reborn</i> (MIT — ver
    /// <c>THIRD_PARTY_NOTICES.md</c>), sem cópia de arquivo.
    /// </para>
    /// </summary>
    internal static class JobStations
    {
        /// <summary>Distância (m) em que o companheiro para da peça para "usá-la" (como o jogador).</summary>
        internal const float UseReach = 2.4f;

        /// <summary>Raio (m) em que os itens largados no chão pela estação são catados.</summary>
        internal const float DropRadius = 3.5f;

        /// <summary>Quantos colliders cabem na varredura de itens (nunca aloca).</summary>
        private const int DropBufferSize = 96;

        /// <summary>Buffer de colliders da varredura de itens (reaproveitado).</summary>
        private static readonly Collider[] s_dropBuffer = new Collider[DropBufferSize];

        /// <summary>Máscara da camada de itens largados no chão.</summary>
        private static int s_itemMask = -1;

        /// <summary>
        /// Onde o companheiro deve ficar para usar a peça: o ponto de interação mais próximo (a peça
        /// costuma ser um objeto baixo, e o <c>UseReach</c> cobre a distância da hitbox).
        /// </summary>
        internal static Vector3 StandPoint(Piece piece, Transform interactPoint)
        {
            if (interactPoint != null)
            {
                return interactPoint.position;
            }

            return piece != null ? piece.transform.position : Vector3.zero;
        }

        /// <summary>
        /// Cata os itens largados no chão perto do ponto (o produto da estação). <paramref name="allowed"/>
        /// filtra por prefab (null = qualquer item). Devolve quantos entraram no inventário.
        ///
        /// <para>
        /// Se o inventário não couber, o item fica no chão e quem chamou decide (o trabalho pede a
        /// viagem ao baú e pausa). Nada aqui espera por rede: o <c>Humanoid.Pickup</c> é uma chamada
        /// curta e o item é destruído na hora.
        /// </para>
        /// </summary>
        internal static int PickupDrops(
            CompanionAI companion,
            Vector3 center,
            float radius,
            HashSet<string> allowed,
            int max)
        {
            if (companion == null || max <= 0)
            {
                return 0;
            }

            if (s_itemMask == -1)
            {
                s_itemMask = LayerMask.GetMask("item");
            }

            int hits = Physics.OverlapSphereNonAlloc(center, radius, s_dropBuffer, s_itemMask);
            int picked = 0;
            for (int i = 0; i < hits && picked < max; i++)
            {
                Collider collider = s_dropBuffer[i];
                if (collider == null)
                {
                    continue;
                }

                ItemDrop drop = collider.GetComponentInParent<ItemDrop>();
                if (drop == null)
                {
                    continue;
                }

                if (allowed != null && !allowed.Contains(Utils.GetPrefabName(drop.gameObject.name)))
                {
                    continue;
                }

                if (companion.JobPickup(drop.gameObject))
                {
                    picked++;
                }
            }

            return picked;
        }

        /// <summary>Existe algum item largado no chão perto do ponto? (usado para decidir a tarefa).</summary>
        internal static bool HasDropNear(Vector3 center, float radius)
        {
            if (s_itemMask == -1)
            {
                s_itemMask = LayerMask.GetMask("item");
            }

            int hits = Physics.OverlapSphereNonAlloc(center, radius, s_dropBuffer, s_itemMask);
            for (int i = 0; i < hits; i++)
            {
                Collider collider = s_dropBuffer[i];
                if (collider != null && collider.GetComponentInParent<ItemDrop>() != null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// O <c>ZNetView</c> da peça está pronto para receber RPC (existe e tem ZDO)? Quando ele não
        /// tem dono, assume a posse — é o que o jogo faz ao interagir (sem posse o RPC não grava no
        /// ZDO e a estação não muda de estado).
        /// </summary>
        internal static bool ReadyForRpc(ZNetView netView)
        {
            if (netView == null || !netView.IsValid() || netView.GetZDO() == null)
            {
                return false;
            }

            if (!netView.IsOwner())
            {
                netView.ClaimOwnership();
            }

            return true;
        }
    }
}
