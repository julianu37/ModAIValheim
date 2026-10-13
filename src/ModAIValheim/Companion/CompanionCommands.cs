using System.Collections.Generic;
using Jotunn.Entities;
using Jotunn.Managers;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// Comandos de console do companheiro (via <see cref="CommandManager"/> do Jotunn).
    ///
    /// São ferramentas de teste in-game para o inventário/equipamento (Fase 2.3) e para o modo de
    /// combate (Fase 2.4a):
    /// <list type="bullet">
    ///   <item><description><c>modia_inv</c> — inventário + itens equipados do companheiro.</description></item>
    ///   <item><description><c>modia_equip &lt;prefab&gt; [slot]</c> — equipa arma ou armadura.</description></item>
    ///   <item><description><c>modia_drop &lt;prefab&gt; [qtd]</c> — dropa item do inventário do companheiro.</description></item>
    ///   <item><description><c>modia_mode [passive|defensive|aggressive]</c> — mostra/define o modo de combate.</description></item>
    /// </list>
    ///
    /// O <see cref="CommandManager"/> só injeta os comandos no <c>Console.Awake</c>, então basta
    /// registrar uma vez no <c>Plugin.Awake</c>. Os nomes não podem ter espaço nem colidir com
    /// comandos vanilla/Jotunn.
    /// </summary>
    internal static class CompanionCommands
    {
        private static bool _registered;

        internal static void Register()
        {
            if (_registered)
            {
                return;
            }

            _registered = true;

            CommandManager manager = CommandManager.Instance;
            manager.AddConsoleCommand(new InventoryCommand());
            manager.AddConsoleCommand(new EquipCommand());
            manager.AddConsoleCommand(new DropCommand());
            manager.AddConsoleCommand(new ModeCommand());
            manager.AddConsoleCommand(new GatherCommand());
            manager.AddConsoleCommand(new StopCommand());

            Plugin.Log.LogInfo(
                "[ModIA] Comandos de console registrados: modia_inv, modia_equip, modia_drop, " +
                "modia_mode, modia_gather, modia_stop.");
        }

        /// <summary>Escreve a mesma linha no console do jogo e no log do BepInEx.</summary>
        private static void Report(Terminal context, string text)
        {
            if (context != null)
            {
                context.AddString(text);
            }
            else
            {
                Console gameConsole = Console.instance;
                if (gameConsole != null)
                {
                    gameConsole.Print(text);
                }
            }

            Plugin.Log.LogInfo(text);
        }

        private static CompanionAI Current => CompanionManager.ActiveCompanion;

        private static bool RequireCompanion(Terminal context)
        {
            if (Current != null)
            {
                return true;
            }

            Report(context, "[ModIA] Nenhum companheiro no mundo. Use a tecla de spawn (F1 por padrão).");
            return false;
        }

        // ---------------------------------------------------------------------
        // modia_inv
        // ---------------------------------------------------------------------

        private sealed class InventoryCommand : ConsoleCommand
        {
            public override string Name => "modia_inv";

            public override string Help =>
                "Mostra o inventário e os itens equipados do companheiro do ModIA.";

            public override void Run(string[] args, Terminal context)
            {
                if (!RequireCompanion(context))
                {
                    return;
                }

                List<string> lines = CompanionInventory.GetInventorySnapshot(Current);
                for (int i = 0; i < lines.Count; i++)
                {
                    Report(context, lines[i]);
                }
            }

            public override void Run(string[] args)
            {
                Run(args, null);
            }
        }

        // ---------------------------------------------------------------------
        // modia_equip <prefab> [slot]
        // ---------------------------------------------------------------------

        private sealed class EquipCommand : ConsoleCommand
        {
            public override string Name => "modia_equip";

            public override string Help =>
                "Equipa um item no companheiro. Uso: modia_equip <prefab> [slot]. " +
                "Ex.: modia_equip Club | modia_equip ArmorLeatherChest chest. " +
                "Slots: left, right, backleft, backright, chest, legs, helmet, shoulder, utility.";

            public override void Run(string[] args, Terminal context)
            {
                if (args == null || args.Length == 0)
                {
                    Report(context, "[ModIA] Uso: modia_equip <prefab> [slot]");
                    return;
                }

                if (!RequireCompanion(context))
                {
                    return;
                }

                string prefabName = args[0];

                if (args.Length >= 2 && TryParseSlot(args[1], out VisSlot slot))
                {
                    if (CompanionInventory.EquipArmor(Current, prefabName, slot))
                    {
                        Report(context, $"[ModIA] Companheiro equipou '{prefabName}' em {slot}.");
                        return;
                    }

                    Report(context, $"[ModIA] Falhou equipar '{prefabName}' em {slot} (veja o log).");
                    return;
                }

                if (args.Length >= 2)
                {
                    Report(context, $"[ModIA] Slot '{args[1]}' desconhecido; usando o slot automático.");
                }

                if (CompanionInventory.Equip(Current, prefabName))
                {
                    Report(context, $"[ModIA] Companheiro equipou '{prefabName}'.");
                    return;
                }

                Report(context, $"[ModIA] Falhou equipar '{prefabName}' (prefab inexistente ou inventário cheio).");
            }

            public override void Run(string[] args)
            {
                Run(args, null);
            }

            private static bool TryParseSlot(string text, out VisSlot slot)
            {
                switch (text?.Trim().ToLowerInvariant())
                {
                    case "left":
                        slot = VisSlot.HandLeft;
                        return true;
                    case "right":
                        slot = VisSlot.HandRight;
                        return true;
                    case "backleft":
                        slot = VisSlot.BackLeft;
                        return true;
                    case "backright":
                        slot = VisSlot.BackRight;
                        return true;
                    case "chest":
                        slot = VisSlot.Chest;
                        return true;
                    case "legs":
                        slot = VisSlot.Legs;
                        return true;
                    case "helmet":
                        slot = VisSlot.Helmet;
                        return true;
                    case "shoulder":
                        slot = VisSlot.Shoulder;
                        return true;
                    case "utility":
                        slot = VisSlot.Utility;
                        return true;
                    default:
                        slot = VisSlot.HandRight;
                        return false;
                }
            }
        }

        // ---------------------------------------------------------------------
        // modia_drop <prefab> [qtd]
        // ---------------------------------------------------------------------

        private sealed class DropCommand : ConsoleCommand
        {
            public override string Name => "modia_drop";

            public override string Help =>
                "Dropa um item do inventário do companheiro para o chão. " +
                "Uso: modia_drop <prefab> [qtd] (sem qtd dropa o stack inteiro). " +
                "Ex.: modia_drop Club | modia_drop Wood 10";

            public override void Run(string[] args, Terminal context)
            {
                if (args == null || args.Length == 0)
                {
                    Report(context, "[ModIA] Uso: modia_drop <prefab> [qtd]");
                    return;
                }

                if (!RequireCompanion(context))
                {
                    return;
                }

                int amount = 0;
                if (args.Length >= 2 && !int.TryParse(args[1], out amount))
                {
                    Report(context, $"[ModIA] Quantidade inválida: '{args[1]}'.");
                    return;
                }

                int dropped = CompanionInventory.DropItem(Current, args[0], amount);
                if (dropped > 0)
                {
                    Report(context, $"[ModIA] Companheiro dropou {dropped}x '{args[0]}'.");
                    return;
                }

                Report(context, $"[ModIA] Nada foi dropado ('{args[0]}' não está no inventário?).");
            }

            public override void Run(string[] args)
            {
                Run(args, null);
            }
        }

        // ---------------------------------------------------------------------
        // modia_mode [passive|defensive|aggressive]
        // ---------------------------------------------------------------------

        private sealed class ModeCommand : ConsoleCommand
        {
            public override string Name => "modia_mode";

            public override string Help =>
                "Mostra ou define o modo de combate do companheiro. " +
                "Uso: modia_mode [passive|defensive|aggressive]. " +
                "Sem argumento mostra o modo atual (também trocável pela tecla de combate, F3).";

            public override void Run(string[] args, Terminal context)
            {
                if (args == null || args.Length == 0)
                {
                    Report(
                        context,
                        $"[ModIA] Modo de combate atual: {CompanionCombat.Mode} " +
                        $"(use modia_mode passive|defensive|aggressive).");
                    return;
                }

                if (!CompanionModeExtensions.TryParse(args[0], out CompanionMode mode))
                {
                    Report(
                        context,
                        $"[ModIA] Modo '{args[0]}' desconhecido. " +
                        "Use passive, defensive ou aggressive.");
                    return;
                }

                CompanionCombat.SetMode(mode, "comando modia_mode");
                Report(context, $"[ModIA] Modo de combate do companheiro: {mode}.");
            }

            public override void Run(string[] args)
            {
                Run(args, null);
            }
        }
        // ---------------------------------------------------------------------
        // modia_gather <recurso> [qtd]
        // ---------------------------------------------------------------------

        private sealed class GatherCommand : ConsoleCommand
        {
            public override string Name => "modia_gather";

            public override string Help =>
                "Faz o companheiro coletar um recurso. Uso: modia_gather <recurso> [qtd]. " +
                "Ex.: modia_gather wood 20 | modia_gather pedra | modia_gather raspberry 30. " +
                "Sem a quantidade ele coleta enquanto houver nó no raio configurado. " +
                "Aceita o nome do item (Wood, Stone, Raspberry...) ou apelidos em português.";

            public override void Run(string[] args, Terminal context)
            {
                if (args == null || args.Length == 0)
                {
                    List<string> help = CompanionHarvest.GetHelp();
                    for (int i = 0; i < help.Count; i++)
                    {
                        Report(context, help[i]);
                    }

                    return;
                }

                if (!RequireCompanion(context))
                {
                    return;
                }

                int amount = 0;
                if (args.Length >= 2 && !int.TryParse(args[1], out amount))
                {
                    Report(context, $"[ModIA] Quantidade inválida: '{args[1]}'.");
                    return;
                }

                if (amount < 0)
                {
                    Report(context, "[ModIA] A quantidade não pode ser negativa.");
                    return;
                }

                CompanionHarvest.Stop("novo comando de coleta");

                if (CompanionHarvest.Start(Current, args[0], amount, "comando modia_gather", out string error))
                {
                    string limit = amount > 0 ? $"{amount}x" : "sem limite";
                    Report(context, $"[ModIA] Coleta iniciada: '{CompanionHarvest.ResourceName}' ({limit}).");
                    return;
                }

                Report(context, error);
            }

            public override void Run(string[] args)
            {
                Run(args, null);
            }
        }

        // ---------------------------------------------------------------------
        // modia_stop
        // ---------------------------------------------------------------------

        private sealed class StopCommand : ConsoleCommand
        {
            public override string Name => "modia_stop";

            public override string Help =>
                "Para a coleta de recursos do companheiro (ele volta a seguir o jogador) e " +
                "larga qualquer alvo de combate.";

            public override void Run(string[] args, Terminal context)
            {
                bool wasHarvesting = CompanionHarvest.IsActive;
                int collected = CompanionHarvest.Collected;

                CompanionHarvest.Stop("comando modia_stop");

                if (Current != null)
                {
                    Current.ClearCombatTarget();
                }

                Report(
                    context,
                    wasHarvesting
                        ? $"[ModIA] Coleta parada ({collected} item(ns) coletado(s)); " +
                          "o companheiro voltou a seguir o jogador."
                        : "[ModIA] O companheiro não estava coletando; alvo de combate limpo.");
            }

            public override void Run(string[] args)
            {
                Run(args, null);
            }
        }
    }
}
