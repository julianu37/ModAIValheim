using HarmonyLib;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// Patches Harmony mínimos do combate do companheiro (Fase 2.4a).
    ///
    /// <para>
    /// Um único ponto de observação alimenta a memória de agressão do
    /// <see cref="CompanionCombat"/> nos dois sentidos que o modo Defensive pede:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><b>O dono foi agredido</b> — quando o <see cref="Player"/> local é a
    ///   vítima, guardamos quem o atacou.</description></item>
    ///   <item><description><b>O dono está atacando</b> — quando o <see cref="Player"/> local é o
    ///   agressor, guardamos a vítima (o alvo que ele escolheu).</description></item>
    /// </list>
    ///
    /// <para>
    /// <c>Character.Damage(HitData)</c> é o funil por onde passa todo dano do jogo (é ele que
    /// dispara o <c>RPC_Damage</c>), então cobre os dois casos de uma vez sem patchear
    /// <c>Player.OnDamaged</c>/<c>Humanoid.StartAttack</c>. Isso importa porque o jogador chama
    /// <c>Humanoid.StartAttack(null, …)</c> (a hitbox é que decide quem foi atingido) — ou seja, o
    /// "alvo" do jogador só existe de fato no evento de dano.
    /// </para>
    ///
    /// <para>
    /// O patch é <b>somente observador</b>: não altera argumentos nem o resultado. Só age se o mod
    /// estiver carregado e se houver companheiro; fora disso é uma comparação de referências.
    /// </para>
    /// </summary>
    internal static class CompanionCombatPatches
    {
        [HarmonyPatch(typeof(Character), nameof(Character.Damage), new[] { typeof(HitData) })]
        internal static class Character_Damage_Patch
        {
            private static void Postfix(Character __instance, HitData hit)
            {
                if (__instance == null || hit == null)
                {
                    return;
                }

                Player localPlayer = Player.m_localPlayer;
                if (localPlayer == null)
                {
                    return;
                }

                if (__instance == localPlayer)
                {
                    // O dono levou dano: revide contra quem bateu.
                    CompanionCombat.NotifyAttacker(hit.GetAttacker());
                    return;
                }

                if (hit.GetAttacker() == localPlayer)
                {
                    // O dono atacou: o companheiro entra na briga do lado dele.
                    CompanionCombat.NotifyAttacker(__instance);
                }
            }
        }
    }
}
