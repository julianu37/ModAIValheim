using System;
using BepInEx.Configuration;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// Guia de ações do companheiro no <b>hover do jogo</b> (Fase 2.6a).
    ///
    /// <para>
    /// O Valheim só mostra texto na mira para objetos <see cref="Hoverable"/> — e, no caso dos
    /// personagens, o <see cref="Character"/> só devolve algo em <c>GetHoverText()</c> quando existe
    /// um <see cref="Tameable"/> no corpo (<c>Character.GetHoverText</c> delega para o
    /// <c>Tameable</c>; sem ele devolve string vazia). O companheiro não é <c>Tameable</c>
    /// (o componente é removido em <see cref="CompanionPrefab"/>), então o caminho é substituir o
    /// texto via Harmony — ver <see cref="CompanionHoverPatches"/>.
    /// </para>
    ///
    /// <para>
    /// O texto sai no mesmo formato que o jogo usa (<c>[&lt;color=yellow&gt;&lt;b&gt;tecla&lt;/b&gt;&lt;/color&gt;] rótulo</c>,
    /// como em <c>Tameable.GetHoverText</c>), com as <b>teclas lidas das ConfigEntry</b> — nunca
    /// hardcoded. As linhas são montadas uma vez por mudança de estado (modo, coleta, teclas) e
    /// cacheadas: o <c>Hud.UpdateCrosshair</c> chama o hover <b>por frame</b> e não pode alocar à toa.
    /// </para>
    ///
    /// <para>
    /// Os rótulos são tokens de localização (<see cref="LocalizationManager"/> do Jotunn), com
    /// fallback em código para PT-BR: se o Jotunn/loc ainda não estiver pronto, o hover continua
    /// legível em vez de mostrar <c>[modia_hover_coleta]</c>.
    /// </para>
    /// </summary>
    internal static class CompanionHover
    {
        /// <summary>
        /// Token do nome do companheiro. É também o <c>m_name</c> do corpo (ver
        /// <see cref="CompanionPrefab"/>), então todo lugar do jogo que mostra o nome do personagem
        /// (<c>Character.GetHoverName</c>, <c>EnemyHud</c>, mensagens de interação) já sai localizado.
        /// </summary>
        internal const string NameToken = "$modia_companion_name";

        /// <summary>Nome usado se a localização ainda não estiver carregada.</summary>
        internal const string NameFallback = "Companheiro";

        private const string TokenCombatMode = "$modia_hover_combat_mode";
        private const string TokenHarvest = "$modia_hover_harvest";
        private const string TokenHarvestOn = "$modia_hover_harvest_on";
        private const string TokenHarvestOff = "$modia_hover_harvest_off";
        private const string TokenInvoke = "$modia_hover_invoke";
        private const string TokenDismiss = "$modia_hover_dismiss";

        // Idiomas do jogo (constantes de Localization.LocalizationConstants no assembly 1.0.17).
        // O Jotunn SEMPRE carrega o inglês e, depois, o idioma do jogador (LocalizationManager.
        // AddTranslations): registramos os dois com o texto em PT-BR, que é o idioma do mod.
        private const string LanguageEnglish = "English";
        private const string LanguagePortugueseBrazilian = "Portuguese_Brazilian";

        private static ConfigEntry<bool> _showHints;
        private static bool _localizationRegistered;

        /// <summary>Último texto montado e a "assinatura" do estado que o gerou (cache do hover).</summary>
        private static string _cachedText;
        private static int _cachedStamp = int.MinValue;

        /// <summary>Dicas ligadas (config <c>Interface/MostrarDicasNoHover</c>).</summary>
        internal static bool ShowHints => _showHints == null || _showHints.Value;

        internal static void Configure(ConfigEntry<bool> showHints)
        {
            _showHints = showHints;

            // A troca de idioma recria o hover localizado: invalida o cache (sem isso o texto
            // ficaria no idioma antigo até o próximo companheiro).
            Localization.OnLanguageChange -= InvalidateCache;
            Localization.OnLanguageChange += InvalidateCache;

            Plugin.Log.LogInfo($"[ModIA] Interface: dicas no hover do companheiro = {ShowHints}.");
        }

        /// <summary>Solta o estado estático (unload do plugin / fim do mundo).</summary>
        internal static void Shutdown()
        {
            Localization.OnLanguageChange -= InvalidateCache;
            InvalidateCache();
        }

        private static void InvalidateCache()
        {
            _cachedText = null;
            _cachedStamp = int.MinValue;
        }

        // ---------------------------------------------------------------------
        // Localização (tokens do Jotunn)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Registra os tokens do hover no <see cref="LocalizationManager"/> do Jotunn (chamado no
        /// <c>Plugin.Awake</c>, como pede a documentação do framework).
        /// </summary>
        internal static void RegisterLocalization()
        {
            if (_localizationRegistered)
            {
                return;
            }

            _localizationRegistered = true;

            try
            {
                CustomLocalization localization = LocalizationManager.Instance.GetLocalization();
                Add(localization, LanguageEnglish);
                Add(localization, LanguagePortugueseBrazilian);

                Plugin.Log.LogInfo(
                    $"[ModIA] Localização do hover registrada ({LanguagePortugueseBrazilian}/" +
                    $"{LanguageEnglish}): '{NameToken}' e labels de ação.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"[ModIA] Não foi possível registrar a localização do hover ({e.Message}); " +
                    "o hover usa os textos padrão em PT-BR.");
            }
        }

        private static void Add(CustomLocalization localization, string language)
        {
            localization.AddTranslation(language, NameToken, NameFallback);
            localization.AddTranslation(language, TokenCombatMode, "Modo de combate");
            localization.AddTranslation(language, TokenHarvest, "Coleta");
            localization.AddTranslation(language, TokenHarvestOn, "ligada");
            localization.AddTranslation(language, TokenHarvestOff, "desligada");
            localization.AddTranslation(language, TokenInvoke, "Invocar");
            localization.AddTranslation(language, TokenDismiss, "Dispensar");
        }

        /// <summary>
        /// Traduz um token, caindo para o texto em PT-BR quando a localização não estiver
        /// disponível (jogo carregando) ou o token não existir (retorno <c>[token]</c>).
        /// </summary>
        private static string Text(string token, string fallback)
        {
            Localization localization = Localization.instance;
            if (localization == null)
            {
                return fallback;
            }

            string value = localization.Localize(token);
            return string.IsNullOrEmpty(value) || value[0] == '[' ? fallback : value;
        }

        // ---------------------------------------------------------------------
        // Identificação do companheiro
        // ---------------------------------------------------------------------

        /// <summary>
        /// True quando <paramref name="character"/> é o corpo do companheiro do ModIA.
        ///
        /// <para>
        /// O caminho principal é o componente <see cref="CompanionAI"/> (é ele que define o corpo
        /// como companheiro). O fallback compara o <b>nome do prefab</b>
        /// (<c>Utils.GetPrefabName</c>, que já tira o sufixo <c>(Clone)</c>) em objetos de rede
        /// (<see cref="ZNetView"/> válida) — assim o hover continua funcionando se o componente for
        /// trocado por outro mod, sem nunca casar com uma string solta.
        /// </para>
        /// </summary>
        internal static bool IsCompanion(Character character)
        {
            if (character == null)
            {
                return false;
            }

            if (character.GetComponent<CompanionAI>() != null)
            {
                return true;
            }

            ZNetView netView = character.GetComponent<ZNetView>();
            if (netView == null || !netView.IsValid())
            {
                return false;
            }

            return string.Equals(
                Utils.GetPrefabName(character.gameObject),
                CompanionPrefab.PrefabName,
                StringComparison.Ordinal);
        }

        // ---------------------------------------------------------------------
        // Texto do hover
        // ---------------------------------------------------------------------

        /// <summary>
        /// Texto mostrado na mira quando ela está sobre o companheiro. Sempre traz o nome; a lista de
        /// ações só aparece com <c>Interface/MostrarDicasNoHover = true</c>. Só monta a string quando
        /// algo visível muda (o jogo chama isto por frame).
        /// </summary>
        internal static string GetHoverText(Character companion)
        {
            int stamp = ComputeStamp(companion);
            if (_cachedText != null && stamp == _cachedStamp)
            {
                return _cachedText;
            }

            _cachedStamp = stamp;
            _cachedText = BuildHoverText(companion);
            return _cachedText;
        }

        /// <summary>Nome do companheiro (localizado).</summary>
        internal static string GetName()
        {
            return Text(NameToken, NameFallback);
        }

        private static string BuildHoverText(Character companion)
        {
            string text = GetName();

            // Só o nome quando as dicas estão desligadas ou o corpo está morto (aí as teclas não
            // valem nada: o companheiro já saiu do mundo).
            if (!ShowHints || (companion != null && companion.IsDead()))
            {
                return text;
            }

            text += "\n" + CombatHint();
            text += "\n" + HarvestHint();

            // Invocar enquanto ele já está no mundo é no-op (o spawn recusa duplicata), mas a linha
            // fica no guia como no mod de referência: é o mapa de teclas do companheiro.
            text += "\n" +
                    Key(CompanionManager.SpawnKey) + " " + Text(TokenInvoke, "Invocar") + " / " +
                    Key(CompanionManager.DismissKey) + " " + Text(TokenDismiss, "Dispensar");

            return text;
        }

        private static string CombatHint()
        {
            // O nome do modo vem de CompanionModeExtensions.ToDisplayName() — é a MESMA fonte usada
            // nas mensagens de HUD/console/log do mod (evita duas traduções divergindo).
            return Key(CompanionCombat.ModeKey) + " " +
                   Text(TokenCombatMode, "Modo de combate") + ": " +
                   CompanionCombat.Mode.ToDisplayName();
        }

        private static string HarvestHint()
        {
            string state;
            string resource;

            if (CompanionHarvest.IsActive)
            {
                resource = ResourceDisplayName(CompanionHarvest.ResourceName);
                int collected = CompanionHarvest.Collected;
                state = CompanionHarvest.HasLimit
                    ? Text(TokenHarvestOn, "ligada") + $", {collected}/{collected + CompanionHarvest.Remaining}"
                    : Text(TokenHarvestOn, "ligada") + $", {collected}";
            }
            else
            {
                // Parada: mostra o recurso que a tecla vai coletar (config Coleta/RecursoPadrao).
                resource = ResourceDisplayName(CompanionHarvest.DefaultResource);
                state = Text(TokenHarvestOff, "desligada");
            }

            return Key(CompanionHarvest.HarvestKey) + " " +
                   Text(TokenHarvest, "Coleta") + ": " + resource + " (" + state + ")";
        }

        /// <summary>Tecla no mesmo formato do jogo (amarelo/negrito), sem depender de bindings vanilla.</summary>
        private static string Key(KeyCode key)
        {
            return "[<color=yellow><b>" + key + "</b></color>]";
        }

        /// <summary>
        /// Nome de exibição do recurso de coleta: o nome localizado do item (ex.: <c>Wood</c> →
        /// "Madeira") quando ele existe no <see cref="ObjectDB"/>; senão o próprio nome do prefab.
        /// </summary>
        private static string ResourceDisplayName(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
            {
                return "-";
            }

            ObjectDB db = ObjectDB.instance;
            GameObject prefab = db != null ? db.GetItemPrefab(prefabName) : null;
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null)
            {
                Localization localization = Localization.instance;
                string sharedName = drop.m_itemData.m_shared.m_name;
                if (localization != null && !string.IsNullOrEmpty(sharedName))
                {
                    string localized = localization.Localize(sharedName);
                    if (!string.IsNullOrEmpty(localized))
                    {
                        return localized;
                    }
                }
            }

            return prefabName;
        }

        /// <summary>
        /// "Assinatura" barata do estado que o hover mostra: só quando ela muda a string é
        /// reconstruída. Inclui as teclas configuradas, o modo, o estado da coleta e o nome do
        /// corpo (sem alocações além do hash das strings).
        /// </summary>
        private static int ComputeStamp(Character companion)
        {
            int stamp = ShowHints ? 1 : 0;
            stamp = (stamp * 397) ^ (int)CompanionCombat.Mode;
            stamp = (stamp * 397) ^ (int)CompanionCombat.ModeKey;
            stamp = (stamp * 397) ^ (companion != null && companion.IsDead() ? 1 : 0);
            stamp = (stamp * 397) ^ (int)CompanionManager.SpawnKey;
            stamp = (stamp * 397) ^ (int)CompanionManager.DismissKey;
            stamp = (stamp * 397) ^ (int)CompanionHarvest.HarvestKey;
            stamp = (stamp * 397) ^ (CompanionHarvest.IsActive ? 1 : 0);
            stamp = (stamp * 397) ^ (CompanionHarvest.HasLimit ? 1 : 0);
            stamp = (stamp * 397) ^ CompanionHarvest.Collected;
            stamp = (stamp * 397) ^ CompanionHarvest.Remaining;

            string resource = CompanionHarvest.IsActive
                ? CompanionHarvest.ResourceName
                : CompanionHarvest.DefaultResource;
            stamp = (stamp * 397) ^ (resource != null ? resource.GetHashCode() : 0);

            string name = companion != null ? companion.m_name : null;
            stamp = (stamp * 397) ^ (name != null ? name.GetHashCode() : 0);

            return stamp;
        }
    }
}
