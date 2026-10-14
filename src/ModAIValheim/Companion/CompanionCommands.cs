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
    ///   <item><description><c>modia_job &lt;hunt|fish|farm|cook|smelt|stop&gt; [qtd]</c> — trabalhos: caçar, pescar, cultivar, cozinhar e fundir (Fase 2.16).</description></item>
    ///   <item><description><c>modia_farm &lt;set|clear|info&gt; [raio]</c> — zona de fazenda do trabalho de cultivar (Fase 2.16b).</description></item>
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

            Plugin.Log.LogInfo(
                "[ModIA] Comandos de console registrados: modia_inv, modia_equip, modia_drop, " +
                "modia_mode, modia_gather, modia_stop, modia_state, modia_stats, modia_deposit, " +
                "modia_job, modia_farm.");
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

                CompanionHarvest.Stop("comando modia_stop");
                CompanionJobQueue.Cancel(Current, "comando modia_stop");

                if (Current != null)
                {
                    Current.ClearCombatTarget();
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
                "Uso: modia_job <hunt|fish|farm|cook|smelt|stop> [quantidade]. " +
                "Ex.: modia_job hunt 5 | modia_job pescar | modia_job farm | modia_job stop. " +
                "hunt = cacar fauna por perto, abater e catar o que caiu; " +
                "fish = pescar com vara numa margem proxima; " +
                "farm = cultivar na zona de fazenda (modia_farm set): plantar, colher e guardar; " +
                "cook = cozinhar/preparar bebidas (abastecer, esperar e retirar o pronto); " +
                "smelt = operar fundicoes (forno/alto-forno/carvoeira); " +
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
                    if (!CompanionJobQueue.HasJob)
                    {
                        Report(context, "[ModIA] O companheiro nao esta num trabalho.");
                        return;
                    }

                    string name = CompanionJobQueue.CurrentName;
                    CompanionJobQueue.Cancel(Current, "comando modia_job stop");
                    Report(context, $"[ModIA] Trabalho '{name}' parado; ele voltou a seguir o jogador.");
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
                        "farm (cultivar), cook (cozinhar), smelt (fundir) ou stop.");
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
    }
}