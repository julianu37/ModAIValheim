using HarmonyLib;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// Patch do ganho de perícia do companheiro (Fase 2.11) — silencia o aviso
    /// <c>"$modia_companion_name is tamed but missing tameable or monster AI script!"</c>.
    ///
    /// <para>
    /// <b>Causa (confirmada no decompilado de <c>assembly_valheim</c> 1.0.17, método
    /// <c>Character.RaiseSkill</c>):</b>
    /// </para>
    /// <code>
    /// public virtual void RaiseSkill(Skills.SkillType skill, float value = 1f)
    /// {
    ///     if (!IsTamed()) return;
    ///     if (!m_tameable) { m_tameable = GetComponent&lt;Tameable&gt;();
    ///                        m_tameableMonsterAI = GetComponent&lt;MonsterAI&gt;(); }
    ///     if (!m_tameable || !m_tameableMonsterAI)
    ///         ZLog.LogWarning(m_name + " is tamed but missing tameable or monster AI script!");
    ///     else { /* repassa o ganho para a perícia do DONO (m_tameable.m_levelUpOwnerSkill) */ }
    /// }
    /// </code>
    ///
    /// <para>
    /// O companheiro é um <c>Humanoid</c> domado (<c>SetTamed(true)</c>, ver
    /// <c>CompanionAI.ConfigureAsCompanion</c>) cujo <c>Tameable</c> foi <b>removido de propósito</b>
    /// do prefab (<c>CompanionPrefab.StripVanillaBehaviour</c>): sem ele o hover é o nosso
    /// (<see cref="CompanionHoverPatches"/>) e o jogador não pode domesticar/renomear/domar o NPC.
    /// Logo <c>m_tameable</c> é sempre nulo e o método cai no ramo do aviso. Quem chama é o
    /// <b>golpe equipado</b> (<c>MeleeAttack</c> → <c>m_character.RaiseSkill(skillType, …)</c> ao
    /// acertar) e o bloqueio (<c>Humanoid.BlockAttack</c> → <c>RaiseSkill(Blocking)</c>) — daí o
    /// aviso aparecer a cada machadada na árvore.
    /// </para>
    ///
    /// <para>
    /// <b>É benigno:</b> para um personagem domado sem <c>Tameable</c> o método base é um
    /// <i>no-op</i> — ele só loga e sai (quem sobe perícia é o dono, via
    /// <c>m_tameable.m_levelUpOwnerSkill</c>, e isso não existe aqui). O <c>Player</c> sobrescreve
    /// <c>RaiseSkill</c> (usa <c>m_seman</c>/<c>m_skills</c>) e não passa por este patch, e para os
    /// demais personagens o prefix devolve <c>true</c> (o jogo segue igual). Só o corpo do
    /// companheiro é pulado — nenhum estado do jogo muda, o aviso é que some.
    /// </para>
    /// </summary>
    internal static class CompanionSkillPatches
    {
        // Tipos explícitos: a assinatura é RaiseSkill(Skills.SkillType, float) — nomear só o método
        // funciona hoje (só existe uma sobrecarga em Character), mas explicitar nunca fica ambíguo.
        [HarmonyPatch(
            typeof(Character),
            nameof(Character.RaiseSkill),
            new[] { typeof(Skills.SkillType), typeof(float) })]
        internal static class Character_RaiseSkill_Patch
        {
            /// <summary>
            /// Devolve <c>false</c> (pula o método base) <b>apenas</b> para o companheiro ativo:
            /// era ele quem gerava o aviso, e o método não faria mais nada além de logá-lo.
            /// </summary>
            private static bool Prefix(Character __instance)
            {
                return !CompanionManager.IsActiveCompanion(__instance);
            }
        }
    }
}
