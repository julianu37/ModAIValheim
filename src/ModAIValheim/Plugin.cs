using System;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace ModAIValheim
{
    /// <summary>
    /// Plugin principal do ModIA. Nesta fase (Fase 0) apenas valida a toolchain:
    /// o plugin precisa carregar no Valheim 1.0 ao lado do BepInEx e do Jotunn.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "modia.companion";
        public const string PluginName = "ModIA Companion";
        public const string PluginVersion = "0.1.0";

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        private ConfigEntry<bool> _logDetalhado;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            _logDetalhado = Config.Bind(
                "Geral",
                "LogDetalhado",
                false,
                "Ativa logs detalhados do ModIA.");

            Log.LogInfo($"{PluginName} v{PluginVersion} carregado (GUID: {PluginGuid}).");
            Log.LogInfo($"Unity: {Application.version} | plataforma: {Application.platform}");

            var jotunn = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Jotunn");
            Log.LogInfo($"Jotunn: {(jotunn != null ? "detectado" : "AUSENTE")}");

            var valheim = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "assembly_valheim");
            Log.LogInfo($"assembly_valheim: {(valheim != null ? "carregada" : "AUSENTE")}");

            if (_logDetalhado.Value)
            {
                Log.LogWarning("Log detalhado ligado - ModIA pronto para o desenvolvimento.");
            }
        }
    }
}
