using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// Guarda e devolve o <see cref="CompanionState"/> do companheiro (Fase 2.7), para o NPC
    /// voltar "o mesmo" depois de dispensado.
    ///
    /// <para>
    /// <b>Onde o estado vive:</b>
    /// </para>
    /// <list type="number">
    ///   <item><description><b>Memória estática</b> (sempre): sobrevive ao dismiss (H) e a trocar de
    ///   mundo dentro da mesma sessão do jogo. É o que atende o pedido "H → F1 volta igual".</description></item>
    ///   <item><description><b>Disco</b> (opcional, <c>Companheiro/PersistirEntreSessoes</c>): um JSON em
    ///   <c>BepInEx\config\modia.companion.state.json</c>, para o companheiro voltar igual depois de
    ///   <b>fechar e abrir o jogo</b>. O arquivo tem <c>Version</c> (formato) e os itens são
    ///   revalidados contra o <c>ObjectDB</c> na restauração — item que não existe mais é
    ///   <b>ignorado com log</b>, nunca inventado.</description></item>
    /// </list>
    ///
    /// <para>
    /// <b>Quando captura:</b> ao dispensar (H), na morte do companheiro (o <c>Character.OnDeath</c>
    /// chama o <c>m_onDeath</c> <i>antes</i> de destruir o GameObject, então o inventário ainda está
    /// inteiro) e no fim do mundo/ao fechar o jogo (<c>ZNetScene.Shutdown</c>, prefix em
    /// <see cref="CompanionStatePatches"/>), para não perder o estado de quem estava vivo e não foi
    /// dispensado.
    /// </para>
    ///
    /// <para>
    /// Nada aqui roda por frame: só em spawn/dismiss/morte/fim de mundo e no comando de console
    /// <c>modia_state</c>. O único I/O é a leitura/escrita de um arquivo pequeno nessas ocasiões.
    /// </para>
    /// </summary>
    internal static class CompanionStateStore
    {
        /// <summary>Nome do arquivo de estado dentro de <c>BepInEx\config</c>.</summary>
        private const string FileName = "modia.companion.state.json";

        private static ConfigEntry<bool> _persistBetweenSessions;

        /// <summary>Último snapshot capturado (ou carregado do disco) nesta sessão.</summary>
        private static CompanionState _snapshot;

        /// <summary>True depois da primeira tentativa de ler o disco (não relê a cada spawn).</summary>
        private static bool _loaded;

        /// <summary>Persistir o snapshot no disco (config <c>Companheiro/PersistirEntreSessoes</c>).</summary>
        internal static bool PersistBetweenSessions =>
            _persistBetweenSessions != null && _persistBetweenSessions.Value;

        /// <summary>Caminho do arquivo de estado (em <c>BepInEx\config</c>).</summary>
        internal static string FilePath
        {
            get
            {
                try
                {
                    return Path.Combine(Paths.ConfigPath, FileName);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"[ModIA] Não foi possível resolver a pasta de config do BepInEx ({e.Message}); " +
                        $"usando '{FileName}'.");
                    return FileName;
                }
            }
        }

        /// <summary>Existe um snapshot para restaurar (na memória ou no disco).</summary>
        internal static bool HasSnapshot
        {
            get
            {
                EnsureLoaded();
                return _snapshot != null;
            }
        }

        /// <summary>Snapshot atual (null se não houver) — carrega o disco na primeira consulta.</summary>
        internal static CompanionState Snapshot
        {
            get
            {
                EnsureLoaded();
                return _snapshot;
            }
        }

        internal static void Configure(ConfigEntry<bool> persistBetweenSessions)
        {
            _persistBetweenSessions = persistBetweenSessions;
            Plugin.Log.LogInfo(
                $"[ModIA] Persistência do companheiro entre sessões: {PersistBetweenSessions} " +
                $"(arquivo: {FilePath}).");
        }

        /// <summary>
        /// Solta o estado estático (unload do plugin). Antes de soltar, faz uma última captura
        /// <b>best-effort</b> do companheiro vivo (se ele ainda existir), para o caso de o jogo
        /// estar fechando sem passar pelo <c>ZNetScene.Shutdown</c>.
        /// </summary>
        internal static void Shutdown()
        {
            try
            {
                CaptureActive("descarga do plugin");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[ModIA] Falha ao capturar o companheiro na descarga: {e.Message}");
            }

            _snapshot = null;
            _loaded = false;
        }

        // ---------------------------------------------------------------------
        // Captura
        // ---------------------------------------------------------------------

        /// <summary>Captura o companheiro ativo no mundo (se houver).</summary>
        internal static void CaptureActive(string source)
        {
            CompanionAI companion = CompanionManager.ActiveCompanion;
            if (companion == null)
            {
                return;
            }

            Capture(companion, source);
        }

        /// <summary>
        /// Tira o snapshot do companheiro e o guarda em memória (e no disco, se a persistência
        /// estiver ligada). Chamado no dismiss, na morte e no fim do mundo.
        /// </summary>
        internal static void Capture(CompanionAI companion, string source)
        {
            if (companion == null)
            {
                return;
            }

            CompanionState state = new CompanionState();

            // Inventário + equipamento (quem monta isso é o CompanionInventory, que é dono das
            // regras de item/prefab).
            CompanionInventory.CaptureItems(companion, state);

            // Aparência (a fonte viva é a config do CompanionAppearance, escrita pelo menu).
            state.AppearanceEnabled = CompanionAppearance.Enabled;
            state.Hair = CompanionAppearance.HairPrefabName;
            state.Beard = CompanionAppearance.BeardPrefabName;
            state.SkinColor = CompanionAppearance.SkinColor;
            state.HairColor = CompanionAppearance.HairColor;

            // Modo de combate.
            state.Mode = CompanionCombat.Mode.ToString();

            // Fase 2.13: comida, stamina e perícias (o que faz dele um membro da equipe).
            CompanionFood food = companion.GetComponent<CompanionFood>();
            if (food != null)
            {
                food.CaptureState(state.Foods);
            }

            CompanionStamina stamina = companion.GetComponent<CompanionStamina>();
            if (stamina != null)
            {
                state.Stamina = stamina.Stamina;
            }

            CompanionSkills skills = companion.GetComponent<CompanionSkills>();
            if (skills != null)
            {
                skills.CaptureState(state.Skills);
            }

            // Vida (informativa: a restauração devolve o companheiro cheio).
            Character character = companion.GetComponent<Character>();
            if (character != null)
            {
                state.MaxHealth = character.GetMaxHealth();
                state.Health = character.GetHealth();
            }

            state.Source = source ?? string.Empty;
            state.SavedAtUtc = DateTime.UtcNow.ToString("o");

            _snapshot = state;
            _loaded = true;

            Plugin.Log.LogInfo(
                $"[ModIA] Snapshot do companheiro ({source}): {state.Items.Count} item(ns), " +
                $"{state.EquippedCount} equipado(s), modo {state.Mode}, " +
                $"vida {state.Health:0}/{state.MaxHealth:0}, aparência " +
                (state.AppearanceEnabled ? "ligada" : "desligada") + ", " +
                $"{state.FoodCount} comida(s), stamina {state.Stamina:0}, " +
                $"{state.SkillCount} perícia(s).");

            Persist(state);
        }

        // ---------------------------------------------------------------------
        // Restauração
        // ---------------------------------------------------------------------

        /// <summary>
        /// Restaura o snapshot no companheiro recém-invocado: inventário, itens equipados,
        /// aparência e modo de combate. Devolve <c>false</c> quando <b>não há</b> snapshot (o
        /// chamador então aplica o loadout padrão da config).
        ///
        /// <para>
        /// O companheiro nasce vazio (o prefab não distribui itens), então a restauração só
        /// <b>adiciona</b> o que estava no snapshot — não há duplicação. Se, por qualquer motivo, o
        /// inventário já tiver itens, a restauração é recusada (e logada) em vez de duplicar.
        /// </para>
        ///
        /// <para>
        /// <b>Vida:</b> volta ao <b>máximo</b> (não usamos <c>state.Health</c>). Dismissar é um ato
        /// deliberado e seguro — devolver o companheiro com 3% de vida porque ele foi guardado no
        /// meio de uma briga seria uma armadilha; e, na morte, o snapshot é justamente o "renascer"
        /// dele, que já vem curado como qualquer invocação.
        /// </para>
        /// </summary>
        internal static bool TryRestore(CompanionAI companion)
        {
            EnsureLoaded();

            CompanionState state = _snapshot;
            if (state == null || companion == null)
            {
                return false;
            }

            bool restored = CompanionInventory.RestoreItems(companion, state);

            CompanionAppearance.ApplySnapshot(companion, state);
            RestoreMode(state.Mode);

            // Fase 2.13: comida, stamina e perícias voltam junto com os itens.
            RestoreVitals(companion, state);

            Plugin.Log.LogInfo(
                $"[ModIA] Companheiro restaurado do snapshot (origem '{state.Source}', " +
                $"{state.Items.Count} item(ns), {state.EquippedCount} equipado(s), modo {state.Mode}); " +
                (restored
                    ? "itens/aparência/modo reaplicados."
                    : "itens NÃO restaurados (veja os avisos acima)."));
            return true;
        }

        /// <summary>
        /// Devolve o estado de <b>comida</b>, <b>stamina</b> e <b>perícias</b> do snapshot
        /// (Fase 2.13). A comida é restaurada com o tempo de queima que faltava e a stamina com o
        /// valor guardado (aparado no teto que a comida restaurada definir) — o companheiro volta
        /// "alimentado", não com os efeitos reiniciados de graça.
        /// </summary>
        private static void RestoreVitals(CompanionAI companion, CompanionState state)
        {
            CompanionFood food = companion.GetComponent<CompanionFood>();
            if (food != null)
            {
                food.RestoreState(state.Foods);
            }

            CompanionStamina stamina = companion.GetComponent<CompanionStamina>();
            if (stamina != null)
            {
                // < 0 = arquivo antigo (formato sem stamina): volta cheio, como antes.
                stamina.SetStamina(state.Stamina >= 0f ? state.Stamina : stamina.MaxStamina);
            }

            CompanionSkills skills = companion.GetComponent<CompanionSkills>();
            if (skills != null)
            {
                skills.RestoreState(state.Skills);
            }
        }

        /// <summary>
        /// Apaga o snapshot (memória + disco) — é o "esqueça esse companheiro" (comando
        /// <c>modia_state clear</c>): o próximo F1 volta a usar o loadout padrão da config.
        /// </summary>
        internal static void Clear(string reason)
        {
            bool had = _snapshot != null || File.Exists(FilePath);

            _snapshot = null;
            _loaded = true;

            try
            {
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[ModIA] Não foi possível apagar '{FilePath}': {e.Message}");
            }

            Plugin.Log.LogInfo(
                $"[ModIA] Snapshot do companheiro apagado ({reason}). " +
                (had ? "O próximo spawn usa o loadout padrão." : "Não havia snapshot."));
        }

        /// <summary>Resumo legível do snapshot para o console/log (<c>modia_state</c>).</summary>
        internal static List<string> Describe()
        {
            EnsureLoaded();

            List<string> lines = new List<string>();
            CompanionState state = _snapshot;

            if (state == null)
            {
                lines.Add(
                    "[ModIA] Nenhum snapshot do companheiro: ao invocar (F1) ele nasce com o " +
                    "loadout padrão da config.");
            }
            else
            {
                lines.Add(
                    $"[ModIA] Snapshot do companheiro: {state.Items.Count} item(ns) " +
                    $"({state.EquippedCount} equipado(s)), modo {state.Mode}, vida " +
                    $"{state.Health:0}/{state.MaxHealth:0}, aparência " +
                    (state.AppearanceEnabled ? "ligada" : "desligada") + ".");
                lines.Add(
                    $"[ModIA]   Capturado em '{state.Source}' ({state.SavedAtUtc}), formato v{state.Version}.");

                if (state.FoodCount > 0 || state.SkillCount > 0 || state.Stamina >= 0f)
                {
                    lines.Add(
                        $"[ModIA]   Comida/stamina/perícias (Fase 2.13): {state.FoodCount} comida(s) " +
                        $"ativa(s), stamina {state.Stamina:0}, {state.SkillCount} perícia(s).");
                }

                if (state.Foods != null)
                {
                    for (int i = 0; i < state.Foods.Count; i++)
                    {
                        CompanionFoodState food = state.Foods[i];
                        if (food == null)
                        {
                            continue;
                        }

                        lines.Add(
                            $"[ModIA]   comida {i}: {food.Prefab} ({food.Remaining:0}/{food.Total:0}s, " +
                            $"+{food.Health:0} vida, +{food.Stamina:0} stamina)");
                    }
                }

                if (state.Skills != null)
                {
                    for (int i = 0; i < state.Skills.Count; i++)
                    {
                        CompanionSkillState skill = state.Skills[i];
                        if (skill == null)
                        {
                            continue;
                        }

                        lines.Add(
                            $"[ModIA]   perícia: {(Skills.SkillType)skill.Skill} " +
                            $"nível {(int)skill.Level}");
                    }
                }

                for (int i = 0; i < state.Items.Count; i++)
                {
                    CompanionItemState item = state.Items[i];
                    if (item == null)
                    {
                        continue;
                    }

                    lines.Add(
                        $"[ModIA]   {i}: {item.Prefab} x{item.Stack} q{item.Quality} " +
                        (item.Equipped ? "[equipado]" : string.Empty));
                }
            }

            lines.Add(
                $"[ModIA] Persistência entre sessões: {(PersistBetweenSessions ? "ligada" : "desligada")} " +
                $"— arquivo {FilePath} " +
                (File.Exists(FilePath) ? "(existe)" : "(ainda não existe)") + ".");
            return lines;
        }

        // ---------------------------------------------------------------------
        // Internos
        // ---------------------------------------------------------------------

        private static void RestoreMode(string modeText)
        {
            if (!CompanionModeExtensions.TryParse(modeText, out CompanionMode mode))
            {
                return;
            }

            // O modo vive na config: só mexemos (e avisamos) se o snapshot discordar do atual.
            if (mode != CompanionCombat.Mode)
            {
                CompanionCombat.SetMode(mode, "restauração do snapshot");
            }
        }

        /// <summary>Lê o disco uma única vez por sessão (ObjectDB/config já prontos quando o spawn chama).</summary>
        private static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            _snapshot = LoadFromDisk();
        }

        private static CompanionState LoadFromDisk()
        {
            if (!PersistBetweenSessions)
            {
                return null;
            }

            string path = FilePath;
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrEmpty(json))
                {
                    return null;
                }

                CompanionState state = JsonUtility.FromJson<CompanionState>(json);
                if (state == null)
                {
                    Plugin.Log.LogWarning(
                        $"[ModIA] '{path}' não pôde ser lido (JSON inválido); ignorando o snapshot.");
                    return null;
                }

                if (state.Version != CompanionState.CurrentVersion)
                {
                    Plugin.Log.LogWarning(
                        $"[ModIA] Snapshot '{path}' é do formato v{state.Version} (esperado " +
                        $"v{CompanionState.CurrentVersion}); ignorando (o próximo dismiss regrava).");
                    return null;
                }

                if (state.Items == null)
                {
                    state.Items = new List<CompanionItemState>();
                }

                // Fase 2.13: campos aditivos — arquivo antigo vem sem eles.
                if (state.Foods == null)
                {
                    state.Foods = new List<CompanionFoodState>();
                }

                if (state.Skills == null)
                {
                    state.Skills = new List<CompanionSkillState>();
                }

                Plugin.Log.LogInfo(
                    $"[ModIA] Snapshot carregado do disco: {state.Items.Count} item(ns) " +
                    $"({state.EquippedCount} equipado(s)), modo {state.Mode}, origem '{state.Source}', " +
                    $"{state.FoodCount} comida(s), {state.SkillCount} perícia(s).");
                return state;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[ModIA] Falha ao ler o snapshot '{path}': {e.Message}");
                return null;
            }
        }

        private static void Persist(CompanionState state)
        {
            if (!PersistBetweenSessions || state == null)
            {
                return;
            }

            string path = FilePath;
            try
            {
                string json = JsonUtility.ToJson(state, true);
                File.WriteAllText(path, json);
                Plugin.Log.LogInfo($"[ModIA] Snapshot do companheiro gravado em '{path}'.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[ModIA] Falha ao gravar o snapshot '{path}': {e.Message}");
            }
        }
    }
}
