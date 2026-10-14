using System.Collections.Generic;
using Jotunn.Entities;
using Jotunn.Managers;
using ModAIValheim.Companion.Jobs;
using UnityEngine;

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
    ///   <item><description><c>modia_gather &lt;recurso&gt; [qtd]</c> — coleta de recursos (Fase 2.4b).</description></item>
    ///   <item><description><c>modia_stop</c> — para coleta, trabalho e combate (volta a seguir você).</description></item>
    ///   <item><description><c>modia_job &lt;hunt|fish|farm|cook|smelt|upkeep|stop&gt; [qtd]</c> — trabalhos: caçar, pescar, cultivar, cozinhar, fundir e cuidar da base (Fase 2.16/2.17).</description></item>
    ///   <item><description><c>modia_farm &lt;set|clear|info&gt; [raio]</c> — zona de fazenda do trabalho de cultivar (Fase 2.16b).</description></item>
    ///   <item><description><c>modia_home &lt;set|clear|info&gt; [raio]</c> — zona da casa/base do companheiro (Fase 2.17).</description></item>
    ///   <item><description><c>modia_basecare [on|off]</c> — modo contínuo do cuidado da base (Fase 2.17).</description></item>
    ///   <item><description><c>modia_rest [info|sit|sleep|stop]</c> — descanso/sono do companheiro (Fase 2.18).</description></item>
    ///   <item><description><c>modia_cart [info|pull|release]</c> — puxar/soltar a carroça do jogador (Fase 2.18).</description></item>
    ///   <item><description><c>modia_stats</c> — comidas ativas, resistência (stamina) e perícias do companheiro (Fase 2.13).</description></item>
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
            manager.AddConsoleCommand(new StateCommand());
            manager.AddConsoleCommand(new StatsCommand());
            manager.AddConsoleCommand(new DepositCommand());
            manager.AddConsoleCommand(new JobCommand());
            manager.AddConsoleCommand(new FarmCommand());
            manager.AddConsoleCommand(new HomeCommand());
            manager.AddConsoleCommand(new BaseCareCommand());
            manager.AddConsoleCommand(new RestCommand());
            manager.AddConsoleCommand(new CartCommand());

            Plugin.Log.LogInfo(
                "[ModIA] Comandos de console registrados: modia_inv, modia_equip, modia_drop, " +
                "modia_mode, modia_gather, modia_stop, modia_state, modia_stats, modia_deposit, " +
                "modia_job, modia_farm, modia_home, modia_basecare, modia_rest, modia_cart.");
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
                "Para a coleta de recursos e o trabalho (cacar/pescar) do companheiro (ele volta a " +
                "seguir o jogador) e larga qualquer alvo de combate.";

            public override void Run(string[] args, Terminal context)
            {
                bool wasHarvesting = CompanionHarvest.IsActive;
                int collected = CompanionHarvest.Collected;
                bool hadJob = CompanionJobQueue.HasJob;
                string jobName = CompanionJobQueue.CurrentName;

                // Fase 2.17: "parar" tem de parar de verdade — o modo contínuo do cuidado da base
                // sai junto (senão ele voltaria a cuidar da casa sozinho segundos depois).
                bool stoppedContinuous = CompanionBaseCare.SuspendForStopCommand("comando modia_stop");

                // Fase 2.18: "parar" também levanta o companheiro do descanso/sono e solta a
                // carroça (senão ele continuaria dormindo ou puxando a carroça depois do comando).
                bool wasResting = Current != null && CompanionRest.Stop(Current, "comando modia_stop");
                bool wasPulling = Current != null && CompanionCart.Release(Current, "comando modia_stop");

                CompanionHarvest.Stop("comando modia_stop");
                CompanionJobQueue.Cancel(Current, "comando modia_stop");

                if (Current != null)
                {
                    Current.ClearCombatTarget();
                }

                if (stoppedContinuous)
                {
                    Report(
                        context,
                        "[ModIA] Modo continuo do cuidado da base DESLIGADO nesta sessao " +
                        "(modia_basecare on para religar).");
                }

                if (wasResting)
                {
                    Report(context, "[ModIA] Ele levantou do descanso/da cama.");
                }

                if (wasPulling)
                {
                    Report(context, "[ModIA] Ele soltou a carroca.");
                }

                if (wasHarvesting)
                {
                    Report(
                        context,
                        $"[ModIA] Coleta parada ({collected} item(ns) coletado(s)); " +
                        "o companheiro voltou a seguir o jogador.");
                    return;
                }

                if (hadJob)
                {
                    Report(
                        context,
                        $"[ModIA] Trabalho '{jobName}' parado; o companheiro voltou a seguir o jogador.");
                    return;
                }

                Report(context, "[ModIA] O companheiro nao estava trabalhando; alvo de combate limpo.");
            }

            public override void Run(string[] args)
            {
                Run(args, null);
            }
        }

        // ---------------------------------------------------------------------
        // modia_state [clear]
        // ---------------------------------------------------------------------

        private sealed class StateCommand : ConsoleCommand
        {
            public override string Name => "modia_state";

            public override string Help =>
                "Mostra o snapshot de persistência do companheiro (Fase 2.7: o que ele tinha ao ser " +
                "dispensado/morrer). Uso: modia_state | modia_state clear. " +
                "'clear' esquece o companheiro guardado (memória + arquivo): o próximo F1 volta a " +
                "usar o loadout padrão da config.";

            public override void Run(string[] args, Terminal context)
            {
                if (args != null && args.Length > 0 &&
                    string.Equals(args[0]?.Trim(), "clear", System.StringComparison.OrdinalIgnoreCase))
                {
                    CompanionStateStore.Clear("comando modia_state clear");
                    Report(
                        context,
                        "[ModIA] Companheiro guardado esquecido; o próximo spawn (F1) usa o loadout padrão.");
                    return;
                }

                List<string> lines = CompanionStateStore.Describe();
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
        // modia_stats
        // ---------------------------------------------------------------------

        private sealed class StatsCommand : ConsoleCommand
        {
            public override string Name => "modia_stats";

            public override string Help =>
                "Mostra o estado de 'membro da equipe' do companheiro (Fase 2.13): comidas ativas " +
                "(com o tempo de queima que falta), resistencia (stamina) e as pericias que ele ja " +
                "tem. Serve para ver em jogo que ele come, cansa e evolui skill.";

            public override void Run(string[] args, Terminal context)
            {
                if (!RequireCompanion(context))
                {
                    return;
                }

                CompanionAI companion = Current;
                Character body = companion.GetComponent<Character>();

                if (body != null)
                {
                    Report(
                        context,
                        $"[ModIA] Vida: {body.GetHealth():0} / {body.GetMaxHealth():0} " +
                        $"(base sem comida {body.GetMaxHealthBase():0}).");
                }

                CompanionFood food = companion.GetComponent<CompanionFood>();
                if (food != null)
                {
                    Report(
                        context,
                        $"[ModIA] Comida ({food.ActiveCount}/{CompanionFood.MaxSlots}): {food.Describe()} " +
                        $"— bonus +{food.TotalHealthBonus:0} vida / +{food.TotalStaminaBonus:0} stamina.");
                }

                CompanionStamina stamina = companion.GetComponent<CompanionStamina>();
                if (stamina != null)
                {
                    Report(
                        context,
                        $"[ModIA] Resistencia: {stamina.Describe()} " +
                        $"({stamina.GetStaminaPercentage() * 100f:0}%).");
                }

                CompanionSkills skills = companion.GetComponent<CompanionSkills>();
                if (skills != null)
                {
                    string top = skills.DescribeTop(6);
                    Report(
                        context,
                        top.Length > 0
                            ? $"[ModIA] Pericias: {top}."
                            : "[ModIA] Pericias: nenhuma ainda (ele sobe cortando, minerando, " +
                              "lutando, bloqueando e correndo).");
                }

                // Fase 2.14: carga (peso), depósito em baú e tool keeper.
                Humanoid humanoid = companion.GetComponent<Humanoid>();
                Inventory inventory = humanoid != null ? humanoid.GetInventory() : null;
                if (inventory != null)
                {
                    string limit = CompanionInventory.MaxWeight > 0f
                        ? $"{CompanionInventory.MaxWeight:0} kg"
                        : "sem limite";
                    Report(
                        context,
                        $"[ModIA] Carga: {CompanionInventory.GetWeight(inventory):0.#} kg " +
                        $"(limite {limit}) | {inventory.NrOfItems()} item(ns), " +
                        $"{inventory.GetEmptySlots()} slot(s) livre(s).");
                }

                Report(
                    context,
                    $"[ModIA] Deposito: {CompanionDeposit.Describe()} | " +
                    $"Tool keeper: {CompanionToolKeeper.Describe()}.");

                // Fase 2.16: trabalhos (caçar/pescar/cultivar/cozinhar/fundir) — a fila única.
                Report(
                    context,
                    $"[ModIA] Trabalho: {CompanionJobQueue.Describe()} " +
                    "(prioridade: combate > coleta > deposito > loot > trabalho > follow).");

                // Fase 2.17: a casa e o cuidado da base.
                Report(context, $"[ModIA] Casa: {CompanionHome.Describe()}.");
                Report(context, $"[ModIA] Cuidado da base: {CompanionBaseCare.Describe()}.");

                // Fase 2.18: qualidade de vida.
                Report(context, $"[ModIA] Descanso: {CompanionRest.Describe()}.");
                Report(context, $"[ModIA] Portal: {CompanionPortalFollow.Describe()}.");
                Report(context, $"[ModIA] Carroca: {CompanionCart.Describe()}.");
            }

            public override void Run(string[] args)
            {
                Run(args, null);
            }
        }

        // ---------------------------------------------------------------------
        // modia_job <hunt|fish|stop> [qtd]
        // ---------------------------------------------------------------------

        private sealed class JobCommand : ConsoleCommand
        {
            public override string Name => "modia_job";

            public override string Help =>
                "Manda o companheiro executar um trabalho (um por vez, ate ele acabar). " +
                "Uso: modia_job <hunt|fish|farm|cook|smelt|upkeep|stop> [quantidade]. " +
                "Ex.: modia_job hunt 5 | modia_job pescar | modia_job farm | modia_job upkeep | modia_job stop. " +
                "hunt = cacar fauna por perto, abater e catar o que caiu; " +
                "fish = pescar com vara numa margem proxima; " +
                "farm = cultivar na zona de fazenda (modia_farm set): plantar, colher e guardar; " +
                "cook = cozinhar/preparar bebidas (abastecer, esperar e retirar o pronto); " +
                "smelt = operar fundicoes (forno/alto-forno/carvoeira); " +
                "upkeep = cuidar da base (modia_home set): reparar construcoes, abastecer fogueiras, " +
                "esvaziar colmeias e ordenar baus; " +
                "stop = parar o trabalho. Sem quantidade vale Jobs/QuantidadePadrao " +
                "(0 = sem limite: trabalha enquanto houver o que fazer no raio).";

            public override void Run(string[] args, Terminal context)
            {
                if (args == null || args.Length == 0)
                {
                    Report(context, Help);
                    Report(
                        context,
                        $"[ModIA] Trabalho atual: {CompanionJobQueue.Describe()}.");
                    return;
                }

                if (!RequireCompanion(context))
                {
                    return;
                }

                string requested = args[0] != null ? args[0].Trim().ToLowerInvariant() : string.Empty;

                if (requested == "stop" || requested == "parar" || requested == "none")
                {
                    // Fase 2.17: parar o trabalho também desliga o modo contínuo do cuidado da base.
                    bool stoppedContinuous = CompanionBaseCare.SuspendForStopCommand("comando modia_job stop");

                    if (!CompanionJobQueue.HasJob)
                    {
                        Report(context, stoppedContinuous
                            ? "[ModIA] O companheiro nao esta num trabalho; o modo continuo do " +
                              "cuidado da base foi DESLIGADO nesta sessao (modia_basecare on para religar)."
                            : "[ModIA] O companheiro nao esta num trabalho.");
                        return;
                    }

                    string name = CompanionJobQueue.CurrentName;
                    CompanionJobQueue.Cancel(Current, "comando modia_job stop");
                    Report(
                        context,
                        $"[ModIA] Trabalho '{name}' parado; ele voltou a seguir o jogador." +
                        (stoppedContinuous
                            ? " O modo continuo do cuidado da base foi DESLIGADO nesta sessao " +
                              "(modia_basecare on para religar)."
                            : string.Empty));
                    return;
                }

                int amount = CompanionJobsConfig.DefaultAmount;
                if (args.Length >= 2 && !int.TryParse(args[1], out amount))
                {
                    Report(context, $"[ModIA] Quantidade invalida: '{args[1]}'.");
                    return;
                }

                if (amount < 0)
                {
                    Report(context, "[ModIA] A quantidade nao pode ser negativa.");
                    return;
                }

                CompanionJob job = CreateJob(requested);
                if (job == null)
                {
                    Report(
                        context,
                        $"[ModIA] Trabalho '{args[0]}' desconhecido. Use: hunt (cacar), fish (pescar), " +
                        "farm (cultivar), cook (cozinhar), smelt (fundir), upkeep (cuidar da base) ou stop.");
                    return;
                }

                // Um trabalho novo e o pedido do momento: a coleta anterior para (como o
                // modia_gather faz com a coleta anterior) para nao disputar o companheiro.
                CompanionHarvest.Stop("novo trabalho (modia_job)");

                if (CompanionJobQueue.Request(job, Current, amount, "comando modia_job", out string error))
                {
                    string limit = amount > 0 ? $"{amount}x" : "sem limite";
                    Report(context, $"[ModIA] Trabalho iniciado: {job.DisplayName} ({limit}).");
                    return;
                }

                Report(context, error);
            }

            public override void Run(string[] args)
            {
                Run(args, null);
            }

            /// <summary>Resolve o nome do trabalho (aceita PT e EN, como os outros comandos).</summary>
            private static CompanionJob CreateJob(string requested)
            {
                switch (requested)
                {
                    case "hunt":
                    case "cacar":
                    case "caçar":
                        return new HuntJob();

                    case "fish":
                    case "pescar":
                    case "pesca":
                        return new FishJob();

                    case "farm":
                    case "fazenda":
                    case "cultivar":
                        return new FarmJob();

                    case "cook":
                    case "cozinhar":
                    case "cozinha":
                    case "bebida":
                        return new CookJob();

                    case "smelt":
                    case "fundir":
                    case "fundicao":
                    case "fundição":
                        return new SmeltJob();

                    case "upkeep":
                    case "base":
                    case "casa":
                    case "cuidar":
                        return new UpkeepJob();

                    default:
                        return null;
                }
            }
        }

        // ---------------------------------------------------------------------
        // modia_farm <set|clear|info> [raio]
        // ---------------------------------------------------------------------

        private sealed class FarmCommand : ConsoleCommand
        {
            public override string Name => "modia_farm";

            public override string Help =>
                "Define a ZONA DE FAZENDA do trabalho de cultivar (Fase 2.16b). " +
                "Uso: modia_farm <set|clear|info> [raio]. " +
                "set = define a zona no ponto onde VOCE esta (raio opcional em metros, padrao " +
                "Jobs/FarmRaio); clear = apaga a zona; info = mostra a zona atual e as sementes que " +
                "ele sabe plantar. A zona fica gravada no .cfg (Jobs/FarmZona) e sobrevive a " +
                "reinicios. Depois: modia_job farm.";

            public override void Run(string[] args, Terminal context)
            {
                string action = args != null && args.Length > 0 && args[0] != null
                    ? args[0].Trim().ToLowerInvariant()
                    : "info";

                switch (action)
                {
                    case "set":
                    case "definir":
                        Set(context, args);
                        return;

                    case "clear":
                    case "apagar":
                    case "limpar":
                        CompanionJobsConfig.SetFarmZone(default);
                        Report(context, "[ModIA] Zona de fazenda apagada (o trabalho de cultivar nao tem onde trabalhar).");
                        return;

                    default:
                        Info(context);
                        return;
                }
            }

            public override void Run(string[] args)
            {
                Run(args, null);
            }

            private static void Set(Terminal context, string[] args)
            {
                Player player = Player.m_localPlayer;
                if (player == null)
                {
                    Report(context, "[ModIA] Sem jogador local: nao sei onde definir a zona.");
                    return;
                }

                float radius = CompanionJobsConfig.FarmRadius;
                if (args != null && args.Length >= 2 && !float.TryParse(
                        args[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out radius))
                {
                    Report(context, $"[ModIA] Raio invalido: '{args[1]}'.");
                    return;
                }

                radius = Mathf.Clamp(radius, 2f, 60f);

                Vector3 center = player.transform.position;
                FarmZone zone = new FarmZone { Center = center, Radius = radius, Crop = string.Empty };
                CompanionJobsConfig.SetFarmZone(zone);

                Report(
                    context,
                    $"[ModIA] Zona de fazenda definida em {center.x:0.#}, {center.z:0.#} com raio " +
                    $"{radius:0.#} m (gravada em Jobs/FarmZona). Agora: modia_job farm.");
            }

            private static void Info(Terminal context)
            {
                FarmZone zone = CompanionJobsConfig.FarmZone;
                Report(context, $"[ModIA] Zona de fazenda: {zone.Describe()}.");

                List<string> seeds = FarmCatalog.Describe();
                if (seeds.Count == 0)
                {
                    Report(context, "[ModIA] Culturas: nenhuma encontrada nos prefabs do jogo.");
                    return;
                }

                Report(
                    context,
                    $"[ModIA] Culturas plantaveis ({seeds.Count}): {string.Join(", ", seeds.ToArray())}.");
                Report(
                    context,
                    "[ModIA] Ele planta so o que tiver de semente no inventario (e so em terreno " +
                    "cultivado, no raio da zona).");
            }
        }

        // ---------------------------------------------------------------------
        // modia_deposit
        // ---------------------------------------------------------------------

        private sealed class DepositCommand : ConsoleCommand
        {
            public override string Name => "modia_deposit";

            public override string Help =>
                "Manda o companheiro levar a carga ao bau mais proximo AGORA (o mesmo caminho de " +
                "quando ele enche sozinho): ele deposita os materiais e volta a seguir voce. " +
                "Uso: modia_deposit. Serve para testar o deposito sem esperar o peso estourar.";

            public override void Run(string[] args, Terminal context)
            {
                if (!RequireCompanion(context))
                {
                    return;
                }

                CompanionAI companion = Current;
                Humanoid humanoid = companion.GetComponent<Humanoid>();
                Inventory inventory = humanoid != null ? humanoid.GetInventory() : null;
                if (inventory == null)
                {
                    Report(context, "[ModIA] O companheiro esta sem inventario.");
                    return;
                }

                if (!CompanionInventory.HasStorableResource(inventory))
                {
                    Report(
                        context,
                        "[ModIA] Nao ha materiais para guardar (equipamento, ferramenta, arma, comida " +
                        "e municao ficam com o companheiro).");
                    return;
                }

                if (CompanionDeposit.TripActive)
                {
                    Report(context, "[ModIA] O companheiro ja esta numa viagem ao bau.");
                    return;
                }

                if (CompanionDeposit.TryStartDepositTrip(companion, inventory))
                {
                    Report(context, "[ModIA] Deposito iniciado: ele esta indo ao bau (acompanhe o log).");
                    return;
                }

                Report(
                    context,
                    $"[ModIA] Nao consegui iniciar: deposito desligado (Coleta/DepositarAoEncher) ou " +
                    $"nenhum bau utilizavel num raio de {CompanionDeposit.ChestRadius:0} m.");
            }

            public override void Run(string[] args)
            {
                Run(args, null);
            }
        }

        // ---------------------------------------------------------------------
        // modia_home <set|clear|info> [raio]
        // ---------------------------------------------------------------------

        private sealed class HomeCommand : ConsoleCommand
        {
            public override string Name => "modia_home";

            public override string Help =>
                "Define a CASA/base do companheiro (Fase 2.17): o ponto de referencia para depositar " +
                "(o bau DENTRO da casa tem preferencia), para o trabalho de cuidar da base " +
                "(modia_job upkeep) e para ele voltar quando ficar perdido. " +
                "Uso: modia_home <set|clear|info> [raio]. " +
                "set = define a casa no ponto onde VOCE esta (raio opcional em metros, padrao " +
                "Casa/HomeRaio); clear = apaga a casa; info = mostra a casa atual. " +
                "A casa fica gravada no .cfg (Casa/HomeZona) e sobrevive a reinicios.";

            public override void Run(string[] args, Terminal context)
            {
                string action = args != null && args.Length > 0 && args[0] != null
                    ? args[0].Trim().ToLowerInvariant()
                    : "info";

                switch (action)
                {
                    case "set":
                    case "definir":
                        Set(context, args);
                        return;

                    case "clear":
                    case "apagar":
                    case "limpar":
                        CompanionHome.ClearHome();
                        Report(
                            context,
                            "[ModIA] Casa apagada (o cuidado da base passa a usar um circulo em volta " +
                            "de onde o trabalho comecar).");
                        return;

                    default:
                        Info(context);
                        return;
                }
            }

            public override void Run(string[] args)
            {
                Run(args, null);
            }

            private static void Set(Terminal context, string[] args)
            {
                Player player = Player.m_localPlayer;
                if (player == null)
                {
                    Report(context, "[ModIA] Sem jogador local: nao sei onde definir a casa.");
                    return;
                }

                float radius = CompanionHome.DefaultRadius;
                if (args != null && args.Length >= 2 && !float.TryParse(
                        args[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out radius))
                {
                    Report(context, $"[ModIA] Raio invalido: '{args[1]}'.");
                    return;
                }

                Vector3 center = player.transform.position;
                CompanionHome.SetHome(center, radius);

                Report(
                    context,
                    $"[ModIA] Casa definida em {center.x:0.#}, {center.z:0.#} com raio " +
                    $"{Mathf.Clamp(radius, 4f, 120f):0.#} m (gravada em Casa/HomeZona). " +
                    "Agora: modia_job upkeep (ou modia_basecare on).");
            }

            private static void Info(Terminal context)
            {
                Report(context, $"[ModIA] Casa: {CompanionHome.Describe()}.");
                Report(context, $"[ModIA] Cuidado da base: {CompanionBaseCare.Describe()}.");

                HomeZone zone = CompanionHome.Zone;
                if (!zone.IsValid)
                {
                    Report(
                        context,
                        "[ModIA] Sem casa definida: o cuidado da base trabalha num circulo em volta de " +
                        "onde o trabalho comecar.");
                    return;
                }

                Report(
                    context,
                    "[ModIA] Dentro da casa ele: deposita no bau da casa (se preferirBauDaCasa), " +
                    "repara construcoes, abastece fogueiras, esvazia colmeias e ordena os baus.");
            }
        }

        // ---------------------------------------------------------------------
        // modia_basecare [on|off]
        // ---------------------------------------------------------------------

        private sealed class BaseCareCommand : ConsoleCommand
        {
            public override string Name => "modia_basecare";

            public override string Help =>
                "Liga/desliga o MODO CONTINUO do cuidado da base (Fase 2.17): com ele ligado, o " +
                "companheiro RETOMA o cuidado da casa sozinho sempre que fica ocioso (sem trabalho, " +
                "coleta, deposito ou combate). Precisa de uma casa definida (modia_home set). " +
                "Uso: modia_basecare [on|off]. Sem argumento mostra o estado. A escolha vale para " +
                "ESTA sessao; o padrao do .cfg e Base/Continuo. O trabalho em si continua sendo " +
                "pedido com modia_job upkeep (que roda uma passada e encerra).";

            public override void Run(string[] args, Terminal context)
            {
                string action = args != null && args.Length > 0 && args[0] != null
                    ? args[0].Trim().ToLowerInvariant()
                    : "status";

                switch (action)
                {
                    case "on":
                    case "ligar":
                    case "true":
                        On(context);
                        return;

                    case "off":
                    case "desligar":
                    case "false":
                        Off(context);
                        return;

                    default:
                        Status(context);
                        return;
                }
            }

            public override void Run(string[] args)
            {
                Run(args, null);
            }

            private static void On(Terminal context)
            {
                CompanionBaseCare.SetContinuous(true, "comando modia_basecare on");

                if (!CompanionHome.IsSet)
                {
                    Report(
                        context,
                        "[ModIA] Modo continuo ligado, mas NAO ha casa definida: use modia_home set " +
                        "onde voce quer que ele cuide.");
                    return;
                }

                if (!CompanionBaseCare.Enabled)
                {
                    Report(context, "[ModIA] Base/Ligado = false no .cfg; ligue para ele cuidar da casa.");
                    return;
                }

                if (!CompanionJobsConfig.Enabled)
                {
                    Report(context, "[ModIA] Jobs/Ligado = false no .cfg; o cuidado da base e um trabalho.");
                    return;
                }

                CompanionAI companion = Current;
                if (companion == null)
                {
                    Report(
                        context,
                        "[ModIA] Modo continuo ligado; ele comeca assim que o companheiro estiver no " +
                        "mundo (F1).");
                    return;
                }

                if (CompanionJobQueue.HasJob)
                {
                    Report(
                        context,
                        $"[ModIA] Modo continuo ligado; ele retoma o cuidado da casa quando o trabalho " +
                        $"atual ('{CompanionJobQueue.CurrentName}') acabar.");
                    return;
                }

                if (CompanionBaseCare.TryAutoStart(companion))
                {
                    Report(context, "[ModIA] Modo continuo ligado: ele ja esta cuidando da base.");
                    return;
                }

                Report(
                    context,
                    "[ModIA] Modo continuo ligado; ele comeca assim que estiver livre (sem combate, " +
                    "coleta ou deposito).");
            }

            private static void Off(Terminal context)
            {
                CompanionBaseCare.SetContinuous(false, "comando modia_basecare off");

                if (CompanionBaseCare.CancelContinuous(Current, "modo continuo desligado"))
                {
                    Report(context, "[ModIA] Modo continuo desligado; ele voltou a seguir voce.");
                    return;
                }

                Report(context, "[ModIA] Modo continuo desligado (um trabalho pedido a mao nao e mexido).");
            }

            private static void Status(Terminal context)
            {
                Report(context, $"[ModIA] Cuidado da base: {CompanionBaseCare.Describe()}.");
                Report(context, $"[ModIA] Casa: {CompanionHome.Describe()}.");

                if (CompanionBaseCare.Continuous && !CompanionHome.IsSet)
                {
                    Report(
                        context,
                        "[ModIA] O modo continuo precisa de uma casa: use modia_home set.");
                }
            }
        }

        // ---------------------------------------------------------------------
        // modia_rest [info|sit|sleep|stop]
        // ---------------------------------------------------------------------

        private sealed class RestCommand : ConsoleCommand
        {
            public override string Name => "modia_rest";

            public override string Help =>
                "Descanso e sono do companheiro (Fase 2.18). Uso: modia_rest <info|sit|sleep|stop>. " +
                "sit = senta e descansa na fogueira acesa mais proxima (ou onde esta, dentro da casa); " +
                "sleep = vai dormir na cama LIVRE mais proxima (a sua cama, que e o seu ponto de " +
                "spawn, nao conta: construa uma segunda); stop = levanta e volta a seguir voce. " +
                "Sem argumento mostra o estado. Sozinho ele ja faz isso quando fica ocioso " +
                "(secao [Descanso] do .cfg).";

            public override void Run(string[] args, Terminal context)
            {
                string action = args != null && args.Length > 0 && args[0] != null
                    ? args[0].Trim().ToLowerInvariant()
                    : "info";

                switch (action)
                {
                    case "sit":
                    case "sentar":
                    case "descansar":
                        Sit(context);
                        return;

                    case "sleep":
                    case "dormir":
                    case "cama":
                        Sleep(context);
                        return;

                    case "stop":
                    case "levantar":
                    case "parar":
                        Stop(context);
                        return;

                    default:
                        Info(context);
                        return;
                }
            }

            public override void Run(string[] args)
            {
                Run(args, null);
            }

            private static void Sit(Terminal context)
            {
                if (!RequireCompanion(context))
                {
                    return;
                }

                if (CompanionRest.DirectSit(Current, out string error))
                {
                    Report(context, "[ModIA] Descanso: ele vai sentar e descansar.");
                    return;
                }

                Report(context, error);
            }

            private static void Sleep(Terminal context)
            {
                if (!RequireCompanion(context))
                {
                    return;
                }

                if (CompanionRest.DirectSleep(Current, out string error))
                {
                    Report(context, "[ModIA] Descanso: ele vai dormir na cama livre mais proxima.");
                    return;
                }

                Report(context, error);
            }

            private static void Stop(Terminal context)
            {
                if (Current != null && CompanionRest.Stop(Current, "comando modia_rest stop"))
                {
                    Report(context, "[ModIA] Descanso: ele levantou e voltou a seguir voce.");
                    return;
                }

                Report(context, "[ModIA] O companheiro nao estava descansando.");
            }

            private static void Info(Terminal context)
            {
                Report(context, $"[ModIA] Descanso: {CompanionRest.Describe()}.");
                Report(context, $"[ModIA] Portal: {CompanionPortalFollow.Describe()}.");
            }
        }

        // ---------------------------------------------------------------------
        // modia_cart [info|pull|release]
        // ---------------------------------------------------------------------

        private sealed class CartCommand : ConsoleCommand
        {
            public override string Name => "modia_cart";

            public override string Help =>
                "Carroca do companheiro (Fase 2.18). Uso: modia_cart <info|pull|release>. " +
                "pull = ele anda ate o engate da carroca (Vagon) livre mais proxima e se prende nela " +
                "pelo caminho do jogo (a carroca vem atras dele enquanto ele te segue, como quando " +
                "VOCE puxa); release = solta a carroca; info = mostra o estado. Ele nunca engata " +
                "sozinho, e a carroca NAO atravessa portal com voce.";

            public override void Run(string[] args, Terminal context)
            {
                string action = args != null && args.Length > 0 && args[0] != null
                    ? args[0].Trim().ToLowerInvariant()
                    : "info";

                switch (action)
                {
                    case "pull":
                    case "puxar":
                    case "engatar":
                        Pull(context);
                        return;

                    case "release":
                    case "soltar":
                    case "largar":
                        Release(context);
                        return;

                    default:
                        Report(context, $"[ModIA] Carroca: {CompanionCart.Describe()}.");
                        return;
                }
            }

            public override void Run(string[] args)
            {
                Run(args, null);
            }

            private static void Pull(Terminal context)
            {
                if (!RequireCompanion(context))
                {
                    return;
                }

                if (CompanionCart.Pull(Current, out string error))
                {
                    Report(context, "[ModIA] Carroca: ele esta indo puxar a carroca.");
                    return;
                }

                Report(context, error);
            }

            private static void Release(Terminal context)
            {
                if (Current != null && CompanionCart.Release(Current, "comando modia_cart release"))
                {
                    Report(context, "[ModIA] Carroca: solta; ele voltou a seguir voce.");
                    return;
                }

                Report(context, "[ModIA] O companheiro nao estava puxando carroca nenhuma.");
            }
        }
    }
}