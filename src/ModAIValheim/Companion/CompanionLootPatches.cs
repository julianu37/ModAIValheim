using System;
using HarmonyLib;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// Patch Harmony do <b>loot de criatura</b> (Fase 2.12 — item #4 da
    /// <c>docs/audit-vs-offline-companions.md</c>).
    ///
    /// <para>
    /// Um único ponto de observação: <c>Character.OnDeath()</c>. É o funil por onde passa a morte de
    /// <b>todo</b> personagem do jogo, então o patch precisa ser <b>baratíssimo</b> e nunca lançar
    /// exceção (ver os riscos da auditoria, item 4). Ele sai na primeira comparação na esmagadora
    /// maioria das mortes (o atacante não é o companheiro) e só chama
    /// <see cref="CompanionLoot.RegisterKill"/> quando o último golpe foi dele.
    /// </para>
    ///
    /// <para>
    /// <b>Como sabemos quem matou (Valheim 1.0.17):</b> <c>Character.Damage(HitData)</c> guarda o
    /// golpe em <c>Character.m_lastHit</c> sempre que o personagem ainda estava vivo ao recebê-lo
    /// (<c>if (health &gt; 0f) m_lastHit = hit;</c>) — logo <c>m_lastHit</c> é o <b>último golpe</b>,
    /// exatamente o que o próprio <c>Character.OnDeath</c> usa para creditar o abate ao jogador
    /// (<c>m_lastHit?.GetAttacker()</c>). O <c>Attack.DoAttack</c> preenche o <c>HitData.m_attacker</c>
    /// com o ZDOID de quem golpeou (<c>hitData.SetAttacker(m_character)</c>), então
    /// <c>HitData.GetAttacker()</c> resolve o <see cref="Character"/> do companheiro — basta checar se
    /// ele carrega um <see cref="CompanionAI"/>. Não é preciso manter nenhum "último atacante" nosso
    /// por golpe (nada de estado por frame).
    /// </para>
    ///
    /// <para>
    /// O patch é <b>somente observador</b>: não altera argumentos nem o resultado de <c>OnDeath</c>.
    /// O <c>CharacterDrop</c> do alvo continua sendo um <c>CharacterDrop</c> normal — só <b>catamos</b>
    /// o que ele deixar no chão (nada é duplicado nem bloqueado).
    /// </para>
    /// </summary>
    internal static class CompanionLootPatches
    {
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        internal static class Character_OnDeath_Patch
        {
            private static void Prefix(Character __instance, HitData ___m_lastHit)
            {
                try
                {
                    if (__instance == null || ___m_lastHit == null)
                    {
                        return;
                    }

                    Character attacker = ___m_lastHit.GetAttacker();
                    if (attacker == null)
                    {
                        return;
                    }

                    CompanionAI companion = attacker.GetComponent<CompanionAI>();
                    if (companion == null)
                    {
                        return;
                    }

                    CompanionLoot.RegisterKill(companion, __instance);
                }
                catch (Exception e)
                {
                    // Um patch que roda para TODA morte do jogo não pode derrubar nada: falha vira
                    // aviso no log e a morte segue normalmente.
                    Plugin.Log.LogWarning($"[ModIA] Loot: falha ao registrar o abate: {e.Message}");
                }
            }
        }
    }
}
