using System;
using HarmonyLib;
using UnityEngine;

namespace ModAIValheim.Companion.Jobs
{
    /// <summary>
    /// Acesso ao <b>flutuador de pesca</b> (<c>FishingFloat</c>) quando ele é do companheiro
    /// (Fase 2.16a).
    ///
    /// <para>
    /// O <c>FishingFloat</c> do jogo só conhece <b>jogadores</b>: ele guarda no ZDO o
    /// <c>ZDOVars.s_rodOwner</c> (o <i>user id</i> de quem arremessou) e procura o dono na lista de
    /// jogadores (<c>ZNet.GetPlayerList()</c>). Para um companheiro esse id não corresponde a nenhum
    /// jogador — e, pior, em single-player ele pode casar com o <b>próprio jogador</b>, fazendo o
    /// flutuador procurar a vara na sua mão e se destruir (ou destruir o SEU flutuador no
    /// <c>FindFloat</c> do <c>Setup</c>). Por isso o flutuador do companheiro ganha uma chave
    /// própria no ZDO (<see cref="OwnerKey"/>) e o dono é resolvido por ela
    /// (<see cref="OwnerOf"/>), com o <c>s_rodOwner</c> apontando para um id impossível
    /// (<see cref="ForeignUserID"/>) para nunca casar com um jogador de verdade.
    /// </para>
    ///
    /// <para>
    /// Adaptado (lógica) de <c>src/Companions/Patches/MHR_FishingPatches.cs</c> do <i>Offline
    /// Companions Reborn</i> (MIT — ver <c>THIRD_PARTY_NOTICES.md</c>): a referência usa os mesmos
    /// três patches (<c>Setup</c>, <c>GetOwner</c>, <c>ReturnBait</c>) e a mesma chave de ZDO. Aqui
    /// o acesso aos campos privados do <c>FishingFloat</c> é direto (compilamos contra a
    /// <c>assembly_valheim</c> publicized), sem <c>AccessTools</c>.
    /// </para>
    /// </summary>
    internal static class CompanionFishingFloat
    {
        /// <summary>Chave do ZDO com o <c>ZDOID</c> do companheiro que arremessou.</summary>
        internal const string OwnerKey = "ModIA_FloatOwner";

        /// <summary>
        /// Valor gravado em <c>ZDOVars.s_rodOwner</c> para o flutuador do companheiro: um id que
        /// nunca pertence a um jogador (assim o código vanilla nunca o confunde com um).
        /// </summary>
        internal const long ForeignUserID = long.MinValue;

        /// <summary>Última isca equipada pelo trabalho (usada se o arremesso vier sem munição).</summary>
        internal static string LastBait;

        /// <summary>Flutuador que <paramref name="character"/> arremessou agora (null se não há).</summary>
        internal static FishingFloat Of(Character character)
        {
            if (character == null)
            {
                return null;
            }

            ZDOID me = character.GetZDOID();
            System.Collections.Generic.List<FishingFloat> instances = FishingFloat.GetAllInstances();
            for (int i = 0; i < instances.Count; i++)
            {
                FishingFloat instance = instances[i];
                if (instance == null)
                {
                    continue;
                }

                ZNetView netView = instance.m_nview;
                if (netView == null || !netView.IsValid())
                {
                    continue;
                }

                if (netView.GetZDO().GetZDOID(OwnerKey) == me)
                {
                    return instance;
                }
            }

            return null;
        }

        /// <summary>Dono (companheiro) do flutuador, quando ele é nosso; null caso contrário.</summary>
        internal static Character OwnerOf(FishingFloat instance)
        {
            if (instance == null)
            {
                return null;
            }

            ZNetView netView = instance.m_nview;
            if (netView == null || !netView.IsValid())
            {
                return null;
            }

            ZDOID id = netView.GetZDO().GetZDOID(OwnerKey);
            if (id.IsNone() || ZNetScene.instance == null)
            {
                return null;
            }

            GameObject go = ZNetScene.instance.FindInstance(id);
            return go != null ? go.GetComponent<Character>() : null;
        }
    }

    /// <summary>
    /// Patches do <b>flutuador de pesca</b> para o companheiro poder pescar de verdade (Fase 2.16a).
    ///
    /// <para>
    /// Os três patches saem na primeira comparação quando o dono não é o companheiro
    /// (<see cref="CompanionManager.IsActiveCompanion"/> é só comparação de referências), então o
    /// jogador continua pescando exatamente como no vanilla. Nenhum deles roda por frame: dois
    /// acontecem uma vez por arremesso e o outro só quando o flutuador é recolhido sem peixe.
    /// </para>
    /// </summary>
    internal static class CompanionFishPatches
    {
        /// <summary>
        /// <c>FishingFloat.Setup</c>: monta o flutuador do companheiro no lugar do código vanilla
        /// (que grava um <i>user id</i> de jogador e explode quando o arremesso chega sem munição —
        /// <c>ammo.m_dropPrefab</c> com <c>ammo == null</c>).
        /// </summary>
        [HarmonyPatch(typeof(FishingFloat), "Setup")]
        internal static class FishingFloat_Setup_Patch
        {
            private static bool Prefix(
                FishingFloat __instance,
                Character owner,
                ItemDrop.ItemData ammo)
            {
                if (!CompanionManager.IsActiveCompanion(owner))
                {
                    return true;
                }

                try
                {
                    ZNetView netView = __instance.m_nview;
                    if (netView == null || !netView.IsValid())
                    {
                        return false;
                    }

                    // Um flutuador por dono (como no vanilla): o antigo sai antes de o novo entrar.
                    FishingFloat previous = CompanionFishingFloat.Of(owner);
                    if (previous != null && previous != __instance && ZNetScene.instance != null)
                    {
                        ZNetScene.instance.Destroy(previous.gameObject);
                    }

                    ZDO zdo = netView.GetZDO();
                    zdo.Set(CompanionFishingFloat.OwnerKey, owner.GetZDOID());
                    zdo.Set(ZDOVars.s_rodOwner, CompanionFishingFloat.ForeignUserID);

                    string bait = BaitOf(owner, ammo);
                    if (bait != null)
                    {
                        zdo.Set(ZDOVars.s_bait, bait);
                    }

                    Transform tip = Utils.FindChild(owner.transform, "_RodTop");
                    if (tip == null)
                    {
                        Plugin.Log.LogWarning(
                            "[ModIA] Pescar: o companheiro nao tem a ponta da vara (_RodTop); " +
                            "o flutuador nao pode ser usado (a vara esta equipada?).");
                        netView.Destroy();
                        return false;
                    }

                    if (__instance.m_rodLine != null)
                    {
                        __instance.m_rodLine.SetPeer(owner.GetZDOID());
                    }

                    __instance.m_lineLength = Vector3.Distance(tip.position, __instance.transform.position);

                    Plugin.Log.LogInfo(
                        $"[ModIA] Pescar: linha fora {__instance.m_lineLength:0} m " +
                        $"(isca '{bait ?? "?"}'{(ammo == null ? ", o arremesso veio sem municao" : "")}).");
                    return false;
                }
                catch (Exception e)
                {
                    // Um patch no caminho do arremesso não pode derrubar nada: avisa e desfaz.
                    Plugin.Log.LogWarning($"[ModIA] Pescar: falha ao montar o flutuador: {e.Message}");
                    try
                    {
                        ZNetView netView = __instance.m_nview;
                        if (netView != null && netView.IsValid())
                        {
                            netView.Destroy();
                        }
                    }
                    catch
                    {
                        // ignorado de propósito: já estamos no caminho de erro
                    }

                    return false;
                }
            }

            /// <summary>Nome da isca: a munição do arremesso, a equipada ou a última escolhida.</summary>
            private static string BaitOf(Character owner, ItemDrop.ItemData ammo)
            {
                if (ammo != null && ammo.m_dropPrefab != null)
                {
                    return ammo.m_dropPrefab.name;
                }

                Humanoid humanoid = owner as Humanoid;
                ItemDrop.ItemData equipped = humanoid != null ? humanoid.GetAmmoItem() : null;
                if (equipped != null && equipped.m_dropPrefab != null)
                {
                    return equipped.m_dropPrefab.name;
                }

                return CompanionFishingFloat.LastBait;
            }
        }

        /// <summary>
        /// <c>FishingFloat.GetOwner</c>: devolve o companheiro (o vanilla só procura jogadores e
        /// destruiria o flutuador no <c>FixedUpdate</c> por "não achar a vara").
        /// </summary>
        [HarmonyPatch(typeof(FishingFloat), "GetOwner")]
        internal static class FishingFloat_GetOwner_Patch
        {
            private static bool Prefix(FishingFloat __instance, ref Character __result)
            {
                try
                {
                    ZNetView netView = __instance.m_nview;
                    if (netView == null || !netView.IsValid() ||
                        netView.GetZDO().GetZDOID(CompanionFishingFloat.OwnerKey).IsNone())
                    {
                        return true; // flutuador de jogador: comportamento vanilla
                    }

                    __result = CompanionFishingFloat.OwnerOf(__instance);
                    return false;
                }
                catch
                {
                    return true;
                }
            }
        }

        /// <summary>
        /// <c>FishingFloat.ReturnBait</c>: o vanilla só devolve a isca a um <c>Player</c> — o
        /// companheiro recebe a dele de volta quando recolhe sem peixe.
        /// </summary>
        [HarmonyPatch(typeof(FishingFloat), "ReturnBait")]
        internal static class FishingFloat_ReturnBait_Patch
        {
            private static bool Prefix(FishingFloat __instance, bool ___m_baitConsumed)
            {
                try
                {
                    Character owner = CompanionFishingFloat.OwnerOf(__instance);
                    if (owner == null)
                    {
                        return true;
                    }

                    if (!___m_baitConsumed)
                    {
                        Humanoid humanoid = owner as Humanoid;
                        string bait = __instance.GetBait();
                        GameObject prefab = bait != null && ZNetScene.instance != null
                            ? ZNetScene.instance.GetPrefab(bait)
                            : null;

                        if (humanoid != null && prefab != null)
                        {
                            humanoid.GetInventory().AddItem(prefab, 1);
                        }
                    }

                    return false;
                }
                catch
                {
                    return true;
                }
            }
        }
    }
}
