using System.Collections.Generic;
using Jotunn.Entities;
using Jotunn.Managers;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// Comandos de console do companheiro (via <see cref="CommandManager"/> do Jotunn).
    ///
    /// São ferramentas de teste in-game para o inventário/equipamento (Fase 2.3):
    /// <list type="bullet">
    ///   <item><description><c>modia_inv</c> — inventário + itens equipados do companheiro.</description></item>
    ///   <item><description><c>modia_equip &lt;prefab&gt; [slot]</c> — equipa arma ou armadura.</description></item>
    ///   <item><description><c>modia_drop &lt;prefab&gt; [qtd]</c> — dropa item do inventário do companheiro.</description></item>
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

            Plugin.Log.LogInfo(
                "[ModIA] Comandos de console registrados: modia_inv, modia_equip, modia_drop.");
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
    }
}
