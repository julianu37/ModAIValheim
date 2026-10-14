using HarmonyLib;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// Perícias do companheiro (Fase 2.13) — <b>revisão do patch que só silenciava</b> o aviso
    /// <c>"$modia_companion_name is tamed but missing tameable or monster AI script!"</c>
    /// (criado na Fase 2.11).
    ///
    /// <para>
    /// <b>O que o patch antigo fazia:</b> um prefix em <c>Character.RaiseSkill</c> devolvia
    /// <c>false</c> para o companheiro, pulando o corpo do jogo. Isso calava o aviso, mas também
    /// <b>matava qualquer evolução de perícia</b> — o companheiro nunca subia skill nenhuma.
    /// </para>
    ///
    /// <para>
    /// <b>Por que o aviso existia (decompilado de <c>assembly_valheim</c> 1.0.17):</b>
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
    /// <para>
    /// O companheiro é um <see cref="Humanoid"/> domado (<c>SetTamed(true)</c>, ver
    /// <see cref="CompanionAI"/>) cujo <c>Tameable</c> foi removido de propósito do prefab
    /// (<see cref="CompanionPrefab.StripVanillaBehaviour"/>). Logo <c>m_tameable</c> é nulo e o
    /// método caía no ramo do aviso. Quem chama: o golpe equipado (<c>MeleeAttack</c> ao acertar),
    /// o bloqueio (<c>Humanoid.BlockAttack</c> → <c>RaiseSkill(Blocking)</c>) e a corrida/nado
    /// (no <c>Player</c>; no NPC quem chama é o <see cref="CompanionStamina"/>).
    /// </para>
    ///
    /// <para>
    /// <b>Como resolvemos agora (as duas coisas ao mesmo tempo).</b> O prefix continua devolvendo
    /// <c>false</c> para o companheiro — então o corpo do jogo <b>não roda</b> e o aviso <b>não pode
    /// voltar</b> —, mas antes disso ele <b>entrega o ganho para a perícia do companheiro</b>
    /// (<see cref="CompanionSkills.RaiseSkill"/>, que usa a mesma matemática do
    /// <c>Skills.Skill.Raise</c> do jogo). E o prefix de <c>GetSkillFactor</c> faz o <b>nível</b>
    /// chegar onde o jogo consome: custo de stamina do ataque
    /// (<c>Attack.GetAttackStamina</c>), poder de bloqueio (<c>Humanoid.BlockAttack</c>) e as contas
    /// de ferramenta.
    /// </para>
    ///
    /// <para>
    /// <b>O que NÃO patchamos (de propósito):</b> o <c>Character.GetRandomSkillFactor</c>. Ele é o
    /// <b>multiplicador de dano</b> do golpe (<c>MeleeAttack</c> usa o retorno como
    /// <c>skillDamageFactor</c>) e a curva do jogador
    /// (<c>Lerp(0.4, 1, fator) ± 0.15</c>) faria um companheiro de perícia <b>0</b> bater com ~40%
    /// do dano, enquanto o NPC sem perícias usa o fator base do jogo (~0,87 a 1,0). Ou seja: aplicar
    /// a curva do jogador <b>enfraqueceria</b> o companheiro em relação a antes da Fase 2.13 até ele
    /// treinar muito — uma regressão, não uma melhoria. O ganho de perícia continua valendo de
    /// verdade (custo de ataque, bloqueio e ferramenta) e o dano fica como estava.
    /// </para>
    ///
    /// <para>
    /// Os patches saem na primeira comparação para todo personagem que <b>não</b> é o companheiro
    /// ativo (<see cref="CompanionManager.IsActiveCompanion"/> é só comparação de referências), então
    /// jogador, criaturas e bosses seguem 100% pelo caminho vanilla — inclusive o <c>Player</c>, que
    /// sobrescreve <c>RaiseSkill</c>/<c>GetSkillFactor</c> e nem chega a passar por estes prefixes.
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
            /// Entrega o ganho para o companheiro e pula o corpo do jogo (que, para ele, só saberia
            /// logar o aviso de <c>Tameable</c> ausente). Para todos os outros personagens devolve
            /// <c>true</c> e nada muda.
            /// </summary>
            private static bool Prefix(Character __instance, Skills.SkillType skill, float value)
            {
                if (!CompanionManager.IsActiveCompanion(__instance))
                {
                    return true;
                }

                CompanionSkills skills = __instance.GetComponent<CompanionSkills>();
                if (skills != null)
                {
                    skills.RaiseSkill(skill, value);
                }

                // Nunca roda o original: é o que mantém o log limpo (sem o aviso de Tameable).
                return false;
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.GetSkillFactor))]
        internal static class Character_GetSkillFactor_Patch
        {
            /// <summary>
            /// O nível do companheiro passa a valer para o custo de stamina do ataque
            /// (<c>Attack.GetAttackStamina</c>), para o poder de bloqueio
            /// (<c>Humanoid.BlockAttack</c>) e para as contas de ferramenta — o <c>Humanoid</c> não
            /// sobrescreve <c>GetSkillFactor</c>, então a base é a que roda.
            /// </summary>
            private static bool Prefix(Character __instance, Skills.SkillType skill, ref float __result)
            {
                if (!CompanionManager.IsActiveCompanion(__instance))
                {
                    return true;
                }

                CompanionSkills skills = __instance.GetComponent<CompanionSkills>();
                if (skills == null)
                {
                    return true;
                }

                __result = skills.GetSkillFactor(skill);
                return false;
            }
        }
    }
}
