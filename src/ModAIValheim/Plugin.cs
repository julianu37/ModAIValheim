using System;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ModAIValheim.Companion;
using UnityEngine;

namespace ModAIValheim
{
    /// <summary>
    /// Plugin principal do ModIA.
    /// Fase 2.1: invoca/dispensa o companheiro NPC (que segue o jogador) por tecla.
    /// Fase 2.3b: o companheiro usa o corpo do jogador ('Player'), então armadura de
    /// peito/pernas renderiza e o NPC parece um viking.
    /// Fase 2.4a: modo de combate (Passive/Defensive/Aggressive) com seleção de alvo e revide.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "modia.companion";
        public const string PluginName = "ModIA Companion";
        public const string PluginVersion = "0.2.5";

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        private ConfigEntry<bool> _logDetalhado;
        private ConfigEntry<KeyCode> _spawnKey;
        private ConfigEntry<KeyCode> _dismissKey;
        private ConfigEntry<bool> _equipLoadoutOnSpawn;
        private ConfigEntry<string> _weaponPrefab;
        private ConfigEntry<string> _armorPrefabs;
        private ConfigEntry<bool> _appearanceEnabled;
        private ConfigEntry<string> _hairPrefab;
        private ConfigEntry<string> _beardPrefab;
        private ConfigEntry<string> _skinColor;
        private ConfigEntry<string> _hairColor;
        private ConfigEntry<CompanionMode> _combatMode;
        private ConfigEntry<KeyCode> _combatModeKey;
        private ConfigEntry<float> _aggressiveRadius;
        private ConfigEntry<float> _defensiveRadius;
        private ConfigEntry<float> _combatLeashDistance;
        private ConfigEntry<bool> _attackNeutralAnimals;
        private ConfigEntry<float> _combatScanInterval;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            _logDetalhado = Config.Bind(
                "Geral",
                "LogDetalhado",
                false,
                "Ativa logs detalhados do ModIA.");

            _spawnKey = Config.Bind(
                "Teclas",
                "SpawnKey",
                KeyCode.F1,
                "Tecla para invocar o companheiro (ignorada se ele já estiver no mundo). " +
                "Padrao F1 (a tecla G e usada pelo menu radial do jogo).");

            _dismissKey = Config.Bind(
                "Teclas",
                "DismissKey",
                KeyCode.H,
                "Tecla para dispensar (despawnar) o companheiro.");

            _equipLoadoutOnSpawn = Config.Bind(
                "Companheiro",
                "EquiparLoadoutNoSpawn",
                true,
                "Equipa o loadout padrao (arma + armaduras) assim que o companheiro e invocado.");

            _weaponPrefab = Config.Bind(
                "Companheiro",
                "WeaponPrefab",
                "Club",
                "Prefab vanilla da arma do companheiro (ex.: Club, AxeStone, SwordBronze, BowFineWood). " +
                "Deixe vazio para spawnar sem arma. Prefab inexistente so gera aviso no log.");

            _armorPrefabs = Config.Bind(
                "Companheiro",
                "ArmorSetPrefabs",
                "ArmorLeatherChest,ArmorLeatherLegs",
                "Prefabs de armadura equipados no spawn, separados por virgula " +
                "(ex.: ArmorLeatherChest,ArmorLeatherLegs,HelmetLeather). " +
                "O slot e detectado pelo tipo do item. Vazio desliga.");

            // Aparencia padrão do corpo de viking (Fase 2.3b).
            _appearanceEnabled = Config.Bind(
                "Aparencia",
                "AparenciaPadrao",
                true,
                "Aplica cabelo/barba/cor de pele padrao no companheiro ao spawnar " +
                "(corpo base e o do jogador, 'Player').");

            _hairPrefab = Config.Bind(
                "Aparencia",
                "CabeloPrefab",
                "",
                "Prefab de cabelo do companheiro (ex.: Hair1 ate Hair38). " +
                "Vazio = primeiro cabelo disponivel no jogo; 'none' = careca.");

            _beardPrefab = Config.Bind(
                "Aparencia",
                "BarbaPrefab",
                "",
                "Prefab de barba do companheiro (ex.: Beard1 ate Beard26). " +
                "Vazio = primeira barba disponivel no jogo; 'none' = sem barba.");

            _skinColor = Config.Bind(
                "Aparencia",
                "CorPele",
                "0.92;0.82;0.74",
                "Cor de pele em r;g;b de 0 a 1, ponto decimal (ex.: 0.92;0.82;0.74). " +
                "1;1;1 = sem tingimento (o tom mais claro que o jogo usa quando nao ha dado).");

            _hairColor = Config.Bind(
                "Aparencia",
                "CorCabelo",
                "0.62;0.45;0.30",
                "Cor do cabelo/barba em r;g;b de 0 a 1, ponto decimal (ex.: 0.62;0.45;0.30).");

            // Combate (Fase 2.4a).
            _combatMode = Config.Bind(
                "Combate",
                "Modo",
                CompanionMode.Defensive,
                "Modo de combate do companheiro. " +
                "Passive = nunca ataca nem revida; " +
                "Defensive = revida quem agredir o companheiro ou o jogador dono e ataca quem estiver " +
                "cacando um dos dois; " +
                "Aggressive = ataca qualquer criatura hostil dentro do RaioAgressivo. " +
                "Cicla em jogo pela tecla de combate; tambem aceita modia_mode.");

            _combatModeKey = Config.Bind(
                "Teclas",
                "CombatModeKey",
                KeyCode.F3,
                "Tecla que cicla o modo de combate: Passive -> Defensive -> Aggressive.");

            _aggressiveRadius = Config.Bind(
                "Combate",
                "RaioAgressivo",
                30f,
                "Raio em metros (a partir do companheiro) em que o modo Aggressive procura a " +
                "criatura hostil mais proxima.");

            _defensiveRadius = Config.Bind(
                "Combate",
                "RaioDefensivo",
                25f,
                "Raio em metros (a partir do companheiro) em que o modo Defensive procura criaturas " +
                "que estejam cacando o companheiro ou o jogador dono.");

            _combatLeashDistance = Config.Bind(
                "Combate",
                "Coleira",
                50f,
                "Distancia maxima (m) entre o alvo de combate e o jogador seguido. " +
                "Passando disso o companheiro larga o alvo e volta a seguir o jogador.");

            _attackNeutralAnimals = Config.Bind(
                "Combate",
                "AtacarAnimaisNeutros",
                false,
                "Se true, o companheiro tambem caca a fauna neutra (javali, cervo, passaro...) - " +
                "a faccao AnimalsVeg do jogo. Por padrao ele so ataca criaturas hostis.");

            _combatScanInterval = Config.Bind(
                "Combate",
                "IntervaloVarredura",
                0.5f,
                "Intervalo em segundos entre as varreduras de alvo (0.1 a 2.0). " +
                "A varredura nunca roda por frame.");

            Log.LogInfo($"{PluginName} v{PluginVersion} carregado (GUID: {PluginGuid}).");
            Log.LogInfo($"Unity: {Application.version} | plataforma: {Application.platform}");

            var jotunn = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Jotunn");
            Log.LogInfo($"Jotunn: {(jotunn != null ? "detectado" : "AUSENTE")}");

            var valheim = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "assembly_valheim");
            Log.LogInfo($"assembly_valheim: {(valheim != null ? "carregada" : "AUSENTE")}");

            Log.LogInfo($"[ModIA] Teclas: spawn = {_spawnKey.Value}, dismiss = {_dismissKey.Value}.");
            Log.LogInfo(
                $"[ModIA] Loadout: equipar no spawn = {_equipLoadoutOnSpawn.Value} | " +
                $"arma = '{_weaponPrefab.Value}' | armaduras = '{_armorPrefabs.Value}'.");
            Log.LogInfo(
                $"[ModIA] Aparencia: padrao = {_appearanceEnabled.Value} | " +
                $"cabelo = '{_hairPrefab.Value}' | barba = '{_beardPrefab.Value}' | " +
                $"pele = '{_skinColor.Value}' | cabelo/cor = '{_hairColor.Value}'.");

            // Combate: modo, tecla de ciclo e raios (Fase 2.4a).
            CompanionCombat.Configure(
                _combatMode,
                _combatModeKey,
                _aggressiveRadius,
                _defensiveRadius,
                _combatLeashDistance,
                _attackNeutralAnimals,
                _combatScanInterval);

            // Prefab do companheiro: clonado dos prefabs vanilla assim que eles estiverem
            // disponíveis (o Jotunn dispara o evento a cada carga de ObjectDB).
            CompanionPrefab.Hook();
            CompanionManager.Configure(_spawnKey, _dismissKey);
            CompanionInventory.Configure(_equipLoadoutOnSpawn, _weaponPrefab, _armorPrefabs);
            CompanionAppearance.Configure(
                _appearanceEnabled, _hairPrefab, _beardPrefab, _skinColor, _hairColor);
            CompanionCommands.Register();
            ApplyHarmonyPatches();

            if (_logDetalhado.Value)
            {
                Log.LogWarning("Log detalhado ligado - ModIA pronto para o desenvolvimento.");
            }
        }

        /// <summary>
        /// Aplica os patches Harmony do mod (observadores de dano do combate). Fica isolado num
        /// try/catch: um patch que não casa (jogo atualizado) não pode derrubar o plugin inteiro.
        /// </summary>
        private void ApplyHarmonyPatches()
        {
            try
            {
                _harmony = new Harmony(PluginGuid);
                _harmony.PatchAll(typeof(Plugin).Assembly);
                Log.LogInfo("[ModIA] Patches Harmony aplicados.");
            }
            catch (Exception e)
            {
                Log.LogError($"[ModIA] Falha ao aplicar patches Harmony: {e}");
            }
        }

        /// <summary>Teclas de spawn/dismiss e de modo de combate (o Tick sai rápido fora do mundo).</summary>
        private void Update()
        {
            CompanionManager.Tick();
            CompanionCombat.Tick();
        }

        private void OnDestroy()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }

            CompanionPrefab.Unhook();
            CompanionManager.Shutdown();
        }
    }
}
