using System;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using ModAIValheim.Companion;
using UnityEngine;

namespace ModAIValheim
{
    /// <summary>
    /// Plugin principal do ModIA.
    /// Fase 2.1: invoca/dispensa o companheiro NPC (que segue o jogador) por tecla.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "modia.companion";
        public const string PluginName = "ModIA Companion";
        public const string PluginVersion = "0.2.0";

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        private ConfigEntry<bool> _logDetalhado;
        private ConfigEntry<KeyCode> _spawnKey;
        private ConfigEntry<KeyCode> _dismissKey;

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
                KeyCode.G,
                "Tecla para invocar o companheiro (ignorada se ele já estiver no mundo).");

            _dismissKey = Config.Bind(
                "Teclas",
                "DismissKey",
                KeyCode.H,
                "Tecla para dispensar (despawnar) o companheiro.");

            Log.LogInfo($"{PluginName} v{PluginVersion} carregado (GUID: {PluginGuid}).");
            Log.LogInfo($"Unity: {Application.version} | plataforma: {Application.platform}");

            var jotunn = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Jotunn");
            Log.LogInfo($"Jotunn: {(jotunn != null ? "detectado" : "AUSENTE")}");

            var valheim = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "assembly_valheim");
            Log.LogInfo($"assembly_valheim: {(valheim != null ? "carregada" : "AUSENTE")}");

            Log.LogInfo($"[ModIA] Teclas: spawn = {_spawnKey.Value}, dismiss = {_dismissKey.Value}.");

            // Prefab do companheiro: clonado dos prefabs vanilla assim que eles estiverem
            // disponíveis (o Jotunn dispara o evento a cada carga de ObjectDB).
            CompanionPrefab.Hook();
            CompanionManager.Configure(_spawnKey, _dismissKey);

            if (_logDetalhado.Value)
            {
                Log.LogWarning("Log detalhado ligado - ModIA pronto para o desenvolvimento.");
            }
        }

        /// <summary>Teclas de spawn/dismiss do companheiro (o Tick sai rápido fora do mundo).</summary>
        private void Update()
        {
            CompanionManager.Tick();
        }

        private void OnDestroy()
        {
            CompanionPrefab.Unhook();
            CompanionManager.Shutdown();
        }
    }
}
