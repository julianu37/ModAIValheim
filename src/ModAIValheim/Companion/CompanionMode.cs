using System;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// Modo de combate do companheiro (Fase 2.4a), no espírito do <c>NPCMode</c> do Thrall
    /// (ver <c>docs/re/thrall-features.md</c>, seção COMBATE).
    ///
    /// <list type="bullet">
    ///   <item><description><see cref="Passive"/> — nunca ataca nem revida (nem se for agredido).</description></item>
    ///   <item><description><see cref="Defensive"/> — revida quem agredir o companheiro ou o jogador
    ///   dono, e ataca criaturas hostis que estejam caçando um dos dois.</description></item>
    ///   <item><description><see cref="Aggressive"/> — ataca a criatura hostil mais próxima dentro de
    ///   um raio configurado (<c>RaioAgressivo</c>).</description></item>
    /// </list>
    ///
    /// O valor é persistido como texto no <c>.cfg</c> (o BepInEx converte enums por
    /// <c>Enum.Parse</c>), então a ordem dos membros importa apenas para o ciclo da tecla.
    /// </summary>
    public enum CompanionMode
    {
        Passive = 0,
        Defensive = 1,
        Aggressive = 2,
    }

    /// <summary>Utilitários de exibição/parse do <see cref="CompanionMode"/>.</summary>
    internal static class CompanionModeExtensions
    {
        /// <summary>Nome em português usado nas mensagens de HUD/console/log.</summary>
        internal static string ToDisplayName(this CompanionMode mode)
        {
            switch (mode)
            {
                case CompanionMode.Passive:
                    return "Passivo";
                case CompanionMode.Aggressive:
                    return "Agressivo";
                default:
                    return "Defensivo";
            }
        }

        /// <summary>Próximo modo do ciclo Passive → Defensive → Aggressive → Passive.</summary>
        internal static CompanionMode Next(this CompanionMode mode)
        {
            int next = ((int)mode + 1) % 3;
            return (CompanionMode)next;
        }

        /// <summary>Aceita "passive"/"defensive"/"aggressive" (e os nomes em português).</summary>
        internal static bool TryParse(string text, out CompanionMode mode)
        {
            mode = CompanionMode.Defensive;

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            switch (text.Trim().ToLowerInvariant())
            {
                case "passive":
                case "passivo":
                    mode = CompanionMode.Passive;
                    return true;
                case "defensive":
                case "defensivo":
                    mode = CompanionMode.Defensive;
                    return true;
                case "aggressive":
                case "agressivo":
                    mode = CompanionMode.Aggressive;
                    return true;
                default:
                    // Última tentativa: o próprio parser do enum (aceita "Passive", "Passive, ...").
                    return Enum.TryParse(text.Trim(), true, out mode) &&
                           Enum.IsDefined(typeof(CompanionMode), mode);
            }
        }
    }
}
