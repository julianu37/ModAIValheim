using HarmonyLib;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// Stamina do companheiro (Fase 2.13).
    ///
    /// <para>
    /// No Valheim 1.0.17 <c>Character.UseStamina</c>, <c>Character.HaveStamina</c> e
    /// <c>Character.AddStamina</c> são <c>virtual</c> e <b>vazios</b> na base; só o <c>Player</c> os
    /// implementa (com <c>m_stamina</c> no ZDO e <c>RPC_UseStamina</c>). Como o companheiro é um
    /// <see cref="Humanoid"/> (não um <c>Player</c>), <b>ele não tem stamina nenhuma</b>: hoje correr,
    /// atacar, bloquear e pular são de graça. Estes prefixes redirecionam essas três chamadas para o
    /// <see cref="CompanionStamina"/> — o que automaticamente faz o jogo <b>cobrar</b> e <b>recusar</b>
    /// as ações caras:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><c>Attack.DoAttack</c> → <c>m_character.UseStamina(GetAttackStamina())</c>
    ///   (custo já reduzido pela perícia da arma, porque <c>GetAttackStamina</c> usa
    ///   <c>GetSkillFactor</c>) e, antes disso, <c>HaveStamina(attackStamina + 0.1f)</c> — que agora
    ///   recusa o golpe sem stamina.</description></item>
    ///   <item><description><c>Humanoid.BlockAttack</c> → <c>UseStamina</c> e <c>HaveStamina</c>:
    ///   bloquear/parry também cansa.</description></item>
    ///   <item><description><c>Character.OnJump</c> → <c>UseStamina(m_jumpStaminaUsage …)</c>.</description></item>
    ///   <item><description><c>SE_Stats.UpdateStatusEffect</c> → <c>AddStamina</c> (hidromel/poções de
    ///   stamina e efeitos de "stamina ao longo do tempo" passam a funcionar no NPC).</description></item>
    ///   <item><description><c>Character.CheckRun</c> (postfix) → com a stamina zerada o
    ///   <c>CheckRun</c> devolve <c>false</c> e o companheiro <b>para de correr</b> (anda) até
    ///   recuperar — é o "não fica preso sem stamina" da fase.</description></item>
    /// </list>
    ///
    /// <para>
    /// Todos os prefixes saem na primeira comparação (<see cref="CompanionManager.IsActiveCompanion"/>
    /// é só comparação de referências), então o resto do mundo continua vanilla. Nada roda por frame:
    /// as chamadas acontecem por golpe/bloqueio/pulo e o <c>CheckRun</c> só paga um <c>GetComponent</c>
    /// depois da guarda de referência.
    /// </para>
    /// </summary>
    internal static class CompanionStaminaPatches
    {
        [HarmonyPatch(typeof(Character), nameof(Character.UseStamina))]
        internal static class Character_UseStamina_Patch
        {
            /// <summary>
            /// Gasta a stamina do companheiro e pula o corpo base (que é vazio para ele). Aplica
            /// <c>Game.m_staminaRate</c> como o <c>Player.UseStamina</c> faz.
            /// </summary>
            private static bool Prefix(Character __instance, float stamina)
            {
                if (!CompanionManager.IsActiveCompanion(__instance))
                {
                    return true;
                }

                CompanionStamina component = __instance.GetComponent<CompanionStamina>();
                if (component == null)
                {
                    return true;
                }

                component.Drain(stamina * Game.m_staminaRate);
                return false;
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.HaveStamina))]
        internal static class Character_HaveStamina_Patch
        {
            /// <summary>
            /// "Tem stamina para isto?" — a mesma regra do <c>Player.HaveStamina</c>
            /// (<c>stamina &gt; amount</c>, e não <c>&gt;=</c>). É o que o <c>Attack.DoAttack</c>
            /// consulta antes de gastar.
            /// </summary>
            private static bool Prefix(Character __instance, float amount, ref bool __result)
            {
                if (!CompanionManager.IsActiveCompanion(__instance))
                {
                    return true;
                }

                CompanionStamina component = __instance.GetComponent<CompanionStamina>();
                if (component == null)
                {
                    return true;
                }

                __result = component.Stamina > amount;
                return false;
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.AddStamina))]
        internal static class Character_AddStamina_Patch
        {
            /// <summary>Devolve stamina (efeitos de status, hidromel, "stamina over time").</summary>
            private static bool Prefix(Character __instance, float v)
            {
                if (!CompanionManager.IsActiveCompanion(__instance))
                {
                    return true;
                }

                CompanionStamina component = __instance.GetComponent<CompanionStamina>();
                if (component == null)
                {
                    return true;
                }

                component.Restore(v);
                return false;
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.CheckRun))]
        internal static class Character_CheckRun_Patch
        {
            /// <summary>
            /// O <c>Player.CheckRun</c> devolve <c>false</c> quando a stamina acabou (é o que faz o
            /// jogador andar em vez de correr). O <c>Humanoid</c> não tem essa regra — a colocamos
            /// aqui, pelo <c>base.CheckRun</c> que o <c>Humanoid.CheckRun</c> sempre chama.
            /// </summary>
            private static void Postfix(Character __instance, ref bool __result)
            {
                if (!__result)
                {
                    return;
                }

                if (!CompanionManager.IsActiveCompanion(__instance))
                {
                    return;
                }

                CompanionStamina component = __instance.GetComponent<CompanionStamina>();
                if (component != null && component.Stamina <= 0f)
                {
                    __result = false;
                }
            }
        }
    }
}
