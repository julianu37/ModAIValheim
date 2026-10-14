using BepInEx.Configuration;
using UnityEngine;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// Ciclo de vida do companheiro no mundo: spawn e dismiss por tecla (via <c>ZInput</c>),
    /// com uma única instância por vez.
    ///
    /// <para>
    /// <b>Fase 2.7 (persistência):</b> o dismiss (H) e a morte fotografam o companheiro
    /// (<see cref="CompanionStateStore"/>) e a invocação (F1) devolve o mesmo NPC — mesmos itens,
    /// equipamento, aparência e modo de combate — em vez de nascer vazio com o loadout padrão.
    /// </para>
    ///
    /// Fluxo autoritativo: em single-player (e no host de um servidor local) o objeto é criado
    /// localmente com <c>Object.Instantiate</c>, e a <c>ZNetView</c> do prefab cuida do ZDO —
    /// é o mesmo caminho que o próprio jogo usa (ver <c>SpawnSystem.Spawn</c> e
    /// <c>ZNetScene.RPC_SpawnObject</c>). Um cliente sem autoridade pede o spawn ao servidor
    /// por <c>ZNetScene.SpawnObject</c>.
    /// </summary>
    internal static class CompanionManager
    {
        /// <summary>Distância (m) à frente do jogador onde o companheiro nasce.</summary>
        private const float SpawnDistance = 2f;

        private static ConfigEntry<KeyCode> _spawnKey;
        private static ConfigEntry<KeyCode> _dismissKey;

        private static GameObject _companion;

        /// <summary>
        /// Companheiro ativo no mundo (null se não houver). Usado pelos comandos de console e
        /// pelas APIs de inventário/equipamento.
        /// </summary>
        internal static CompanionAI ActiveCompanion => GetCompanion();

        /// <summary>Tecla de invocar (config <c>Teclas/SpawnKey</c>), como as demais teclas do mod.</summary>
        internal static KeyCode SpawnKey => _spawnKey != null ? _spawnKey.Value : KeyCode.F1;

        /// <summary>Tecla de dispensar (config <c>Teclas/DismissKey</c>).</summary>
        internal static KeyCode DismissKey => _dismissKey != null ? _dismissKey.Value : KeyCode.H;

        /// <summary>
        /// True quando <paramref name="character"/> é o corpo do companheiro ativo. É só comparação
        /// de referências (nenhum <c>GetComponent</c>/varredura): existe para os patches Harmony de
        /// caminho quente, como o <c>Character.RaiseSkill</c> (chamado a cada golpe do jogo).
        /// </summary>
        internal static bool IsActiveCompanion(Character character)
        {
            return character != null && _companion != null && character.gameObject == _companion;
        }

        internal static void Configure(ConfigEntry<KeyCode> spawnKey, ConfigEntry<KeyCode> dismissKey)
        {
            _spawnKey = spawnKey;
            _dismissKey = dismissKey;

            if (spawnKey != null && dismissKey != null && spawnKey.Value == dismissKey.Value)
            {
                Plugin.Log.LogWarning(
                    $"[ModIA] SpawnKey e DismissKey estão na mesma tecla ({spawnKey.Value}).");
            }
        }

        /// <summary>
        /// True quando o input do mod deve ser ignorado: sem personagem no mundo, com menu/console
        /// abertos, com o chat em foco (digitar "f2" no chat não pode ciclar o modo de combate) ou
        /// com o <b>menu do mod</b> aberto (Fase 2.6b) — enquanto ele está aberto, as teclas de
        /// spawn/dismiss/modo/coleta ficam suspensas e quem responde é o próprio menu.
        /// </summary>
        internal static bool InputBlocked()
        {
            if (Player.m_localPlayer == null || Menu.IsVisible() || Console.IsVisible())
            {
                return true;
            }

            if (CompanionMenu.IsOpen)
            {
                return true;
            }

            Chat chat = Chat.instance;
            return chat != null && chat.HasFocus();
        }

        /// <summary>
        /// Guardas de input "de fora" do mod (sem considerar o menu do mod): personagem no mundo,
        /// sem menu/console do jogo abertos e sem o chat em foco. É o que o <see cref="CompanionMenu"/>
        /// usa para decidir se a tecla de menu pode ser lida.
        /// </summary>
        internal static bool GameUiCapturesInput()
        {
            if (Player.m_localPlayer == null || Menu.IsVisible() || Console.IsVisible())
            {
                return true;
            }

            Chat chat = Chat.instance;
            return chat != null && chat.HasFocus();
        }

        /// <summary>Chamado a cada frame pelo <see cref="Plugin"/>; sai rápido fora do mundo.</summary>
        internal static void Tick()
        {
            if (_spawnKey == null || _dismissKey == null)
            {
                return;
            }

            // Só faz sentido com um personagem vivo no mundo e sem UI de texto capturando input.
            if (InputBlocked())
            {
                return;
            }

            if (ZInput.GetKeyDown(_spawnKey.Value, false))
            {
                Spawn();
            }

            if (_dismissKey.Value != _spawnKey.Value && ZInput.GetKeyDown(_dismissKey.Value, false))
            {
                Dismiss();
            }
        }

        /// <summary>Solta o estado estático (unload do plugin / fim do mundo).</summary>
        internal static void Shutdown()
        {
            // Última chance de guardar o companheiro vivo (o ZNetScene.Shutdown já faz isso no fim
            // do mundo; aqui é o caso da descarga do plugin).
            CompanionStateStore.Shutdown();

            _companion = null;
            CompanionInventory.Shutdown();
            CompanionCombat.Shutdown();
            CompanionHarvest.Shutdown();
            CompanionLoot.Shutdown();
            CompanionMovement.Shutdown();
        }

        private static void Spawn()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return;
            }

            if (_companion != null)
            {
                Plugin.Log.LogInfo("[ModIA] Companheiro já está no mundo; spawn ignorado.");
                return;
            }

            // Nunca duplicar: se existir uma instância viva (ex.: recarregada com a área),
            // adotamos ela em vez de criar outra. Neste caminho não restauramos snapshot: o NPC
            // está vivo, com os itens dele (o snapshot só vale para um companheiro novo).
            CompanionAI existing = FindExistingCompanion();
            if (existing != null)
            {
                _companion = existing.gameObject;
                existing.SetFollowTarget(player.gameObject);
                CompanionAppearance.Apply(existing);
                Plugin.Log.LogInfo("[ModIA] Companheiro existente adotado e voltou a seguir o jogador.");
                return;
            }

            GameObject prefab = CompanionPrefab.GetPrefab();
            if (prefab == null)
            {
                Plugin.Log.LogError("[ModIA] Prefab do companheiro indisponível; spawn cancelado.");
                ShowMessage("Companheiro indisponível (veja o log do BepInEx).");
                return;
            }

            Vector3 position = ComputeSpawnPosition(player);
            Quaternion rotation = player.transform.rotation;

            if (!IsServer())
            {
                ZNetScene scene = ZNetScene.instance;
                if (scene == null)
                {
                    Plugin.Log.LogError("[ModIA] ZNetScene indisponível; spawn cancelado.");
                    return;
                }

                // Cliente sem autoridade: quem cria o ZDO tem de ser o servidor.
                // O comando de follow precisa viajar junto (é a parte de rede da fase seguinte).
                scene.SpawnObject(position, rotation, prefab);
                Plugin.Log.LogWarning(
                    "[ModIA] Spawn do companheiro solicitado ao servidor (cliente sem autoridade). " +
                    "O follow só será ativado quando o comando de rede existir.");
                return;
            }

            GameObject companion = UnityEngine.Object.Instantiate(prefab, position, rotation);

            ZNetView netView = companion.GetComponent<ZNetView>();
            if (netView == null || !netView.IsValid())
            {
                Plugin.Log.LogError("[ModIA] Companheiro instanciado sem ZNetView válida; desfazendo.");
                UnityEngine.Object.Destroy(companion);
                return;
            }

            CompanionAI ai = companion.GetComponent<CompanionAI>();
            if (ai == null)
            {
                Plugin.Log.LogError("[ModIA] Companheiro instanciado sem CompanionAI; desfazendo.");
                netView.Destroy();
                return;
            }

            ai.EnsureConfigured();
            ai.SetFollowTarget(player.gameObject);

            // Fase 2.7: se existe um snapshot (do último dismiss/morte/fim de mundo), o companheiro
            // volta "o mesmo" — mesmos itens/equipamento, aparência e modo. Sem snapshot, vale o
            // comportamento antigo: aparência padrão + loadout da config.
            bool restored = CompanionStateStore.TryRestore(ai);
            if (!restored)
            {
                CompanionAppearance.Apply(ai);
                CompanionInventory.ApplyDefaultLoadout(ai);
            }

            Character character = companion.GetComponent<Character>();
            if (character != null)
            {
                character.m_onDeath += OnCompanionDeath;
            }

            _companion = companion;
            Plugin.Log.LogInfo(
                $"[ModIA] Companheiro '{companion.name}' invocado em {position} " +
                $"(prefab '{CompanionPrefab.PrefabName}', owner local: {netView.IsOwner()}).");
            ShowMessage(restored
                ? "Companheiro invocado! (itens guardados devolvidos)"
                : "Companheiro invocado!");
        }

        private static void Dismiss()
        {
            CompanionAI companion = GetCompanion();
            if (companion == null)
            {
                Plugin.Log.LogInfo("[ModIA] Nenhum companheiro para dispensar.");
                return;
            }

            // Fase 2.7: fotografa o companheiro ANTES de destruí-lo (itens, equipamento,
            // aparência, modo) para o próximo F1 devolvê-lo igual.
            CompanionStateStore.Capture(companion, "dispensa");

            ZNetView netView = companion.GetComponent<ZNetView>();
            if (netView == null || !netView.IsValid())
            {
                Plugin.Log.LogWarning("[ModIA] Companheiro sem ZNetView válida; dispensando só a referência.");
                _companion = null;
                return;
            }

            if (!netView.IsOwner())
            {
                Plugin.Log.LogWarning(
                    "[ModIA] Sem autoridade sobre o companheiro (cliente); dismiss ignorado.");
                return;
            }

            // ZNetScene.Destroy: destrói o ZDO (somos o dono) e o GameObject.
            netView.Destroy();
            _companion = null;

            // Coleta em andamento não faz sentido sem companheiro.
            CompanionHarvest.Stop("companheiro dispensado");
            CompanionLoot.Shutdown();

            Plugin.Log.LogInfo("[ModIA] Companheiro dispensado.");
            ShowMessage("Companheiro dispensado.");
        }

        private static CompanionAI GetCompanion()
        {
            if (_companion != null)
            {
                CompanionAI cached = _companion.GetComponent<CompanionAI>();
                if (cached != null)
                {
                    return cached;
                }

                _companion = null;
            }

            CompanionAI found = FindExistingCompanion();
            if (found != null)
            {
                _companion = found.gameObject;
            }

            return found;
        }

        /// <summary>
        /// Varredura pontual (só em spawn/dismiss, nunca por frame) por instâncias vivas do
        /// companheiro que não estejam na nossa referência estática.
        /// </summary>
        private static CompanionAI FindExistingCompanion()
        {
            CompanionAI[] instances = UnityEngine.Object.FindObjectsByType<CompanionAI>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            for (int i = 0; i < instances.Length; i++)
            {
                CompanionAI candidate = instances[i];
                if (candidate == null)
                {
                    continue;
                }

                Character character = candidate.GetComponent<Character>();
                if (character != null && character.IsDead())
                {
                    continue;
                }

                return candidate;
            }

            return null;
        }

        private static Vector3 ComputeSpawnPosition(Player player)
        {
            Vector3 position = player.transform.position + player.transform.forward * SpawnDistance;

            // Só ajusta o Y se o terreno sólido estiver abaixo do ponto escolhido (nascer
            // dentro da terra) — se o jogador estiver em um piso sobrado, mantemos a altura dele.
            ZoneSystem zone = ZoneSystem.instance;
            if (zone != null)
            {
                float groundHeight = zone.GetSolidHeight(position);
                if (groundHeight > position.y)
                {
                    position.y = groundHeight + 0.1f;
                }
            }

            return position;
        }

        private static bool IsServer()
        {
            // Sem ZNet (não deveria acontecer no mundo) tratamos como execução local.
            ZNet znet = ZNet.instance;
            return znet == null || znet.IsServer();
        }

        private static void OnCompanionDeath()
        {
            // O jogo chama o m_onDeath ANTES de destruir o GameObject (Character.OnDeath), então o
            // inventário ainda está inteiro aqui: é a hora de fotografar (Fase 2.7). Sem isso, os
            // itens do companheiro morreriam com ele (o prefab é não-persistente).
            CompanionAI dying = GetCompanion();
            if (dying != null)
            {
                // Fase 2.13: a perda de perícia por morte entra ANTES do snapshot — o companheiro
                // guardado volta com o que sobrou, como um jogador que morreu.
                CompanionSkills skills = dying.GetComponent<CompanionSkills>();
                if (skills != null)
                {
                    skills.ApplyDeathPenalty();
                }

                // Os efeitos de comida morrem com ele (a vida máxima volta ao valor base antes de
                // o snapshot registrar a vida).
                CompanionFood food = dying.GetComponent<CompanionFood>();
                if (food != null)
                {
                    food.Clear("companheiro morreu");
                }

                CompanionStateStore.Capture(dying, "morte");
            }

            Plugin.Log.LogInfo("[ModIA] Companheiro morreu.");
            _companion = null;
            CompanionHarvest.Stop("companheiro morreu");
            CompanionLoot.Shutdown();
            ShowMessage("Seu companheiro morreu! Os itens dele ficaram guardados (F1 invoca de novo).");
        }

        private static void ShowMessage(string text)
        {
            MessageHud hud = MessageHud.instance;
            if (hud != null)
            {
                hud.ShowMessage(MessageHud.MessageType.Center, text);
            }
        }
    }
}
