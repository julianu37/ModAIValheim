using HarmonyLib;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// Patch de hover do companheiro (Fase 2.6a).
    ///
    /// <para>
    /// <c>Character.GetHoverText()</c> é quem o <c>Hud.UpdateCrosshair</c> chama quando a mira está
    /// sobre um personagem (o <c>Character</c> implementa <c>Hoverable</c>). A implementação base
    /// devolve <b>string vazia</b> para todo personagem sem <c>Tameable</c> — e é exatamente o caso
    /// do nosso companheiro — então o patch entra só para <b>preencher</b> esse texto; para
    /// qualquer outro <c>Character</c> devolve <c>true</c> e o jogo segue exatamente como antes.
    /// </para>
    ///
    /// <para>
    /// Patchar a declaração base é seguro aqui: <c>Player</c>, <c>Raven</c>, <c>Trader</c> e outros
    /// personagens que <b>sobrescrevem</b> o método têm o próprio alvo e não passam por este prefix
    /// (o Harmony só intercepta a implementação exata de <c>Character</c>).
    /// </para>
    /// </summary>
    internal static class CompanionHoverPatches
    {
        [HarmonyPatch(typeof(Character), nameof(Character.GetHoverText))]
        internal static class Character_GetHoverText_Patch
        {
            private static bool Prefix(Character __instance, ref string __result)
            {
                if (!CompanionHover.IsCompanion(__instance))
                {
                    return true;
                }

                __result = CompanionHover.GetHoverText(__instance);
                return false;
            }
        }

        /// <summary>
        /// O nome do companheiro também passa por aqui (é o que o <c>EnemyHud</c> e as mensagens de
        /// interação usam). A base devolveria <c>Localize(m_name)</c> — que fica feio
        /// (<c>[modia_companion_name]</c>) se a localização não tiver carregado; o patch garante o
        /// nome em PT-BR mesmo nesse caso.
        /// </summary>
        [HarmonyPatch(typeof(Character), nameof(Character.GetHoverName))]
        internal static class Character_GetHoverName_Patch
        {
            private static bool Prefix(Character __instance, ref string __result)
            {
                if (!CompanionHover.IsCompanion(__instance))
                {
                    return true;
                }

                __result = CompanionHover.GetName();
                return false;
            }
        }
    }
}
