using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// Menu do mod dentro do jogo (Fase 2.6b): um <b>painel de madeira</b> no estilo do Valheim com
    /// as seções de <i>status</i>, <i>aparência</i>, <i>combate</i>, <i>coleta</i> e <i>teclas</i>.
    ///
    /// <para>
    /// <b>Abertura.</b> A tecla configurada (<c>Teclas/MenuKey</c>, padrão <c>Y</c>) alterna o painel
    /// quando o jogador está <b>a até <see cref="OpenDistance"/> m do companheiro</b> (mesmo espírito
    /// do hover da Fase 2.6a). Fecha com a mesma tecla, com <c>ESC</c> ou com o botão "Fechar".
    /// </para>
    /// <para>
    /// <b>Input.</b> Com o painel aberto chamamos <c>GUIManager.BlockInput(true)</c> (libera o mouse e
    /// congela o jogador); ao fechar, <c>BlockInput(false)</c> — o Jotunn não libera sozinho, então o
    /// par é sempre 1:1 (guardado por <see cref="_inputBlocked"/>). O <c>ESC</c> não abre o menu do
    /// jogo nesse estado porque o <c>BlockInput</c> do Jotunn faz <c>TextInput.IsVisible()</c> devolver
    /// true, o que desarma o <c>Menu.Update</c>.
    /// </para>
    /// <para>
    /// <b>Performance.</b> A árvore de controles é montada <b>uma única vez</b> (na primeira abertura)
    /// e depois só é ligada/desligada; o status é reescrito no máximo ~2x/s. Nada é recriado por frame
    /// e não há varredura de cena em loop.
    /// </para>
    /// <para>
    /// O painel é recriado quando a cena muda (o <c>GUIManager</c> reconstrói o CustomGUIFront): o
    /// evento <c>OnCustomGUIAvailable</c> só limpa as referências, e a próxima abertura remonta tudo.
    /// </para>
    /// </summary>
    internal static class CompanionMenu
    {
        /// <summary>Distância (m) máxima ao companheiro para o menu poder ser aberto.</summary>
        internal const float OpenDistance = 5f;

        /// <summary>Fechar sozinho se o jogador se afastar além disto (m).</summary>
        private const float AutoCloseDistance = 8f;

        /// <summary>Intervalo (s) de atualização do texto de status com o painel aberto.</summary>
        private const float StatusRefreshInterval = 0.5f;

        // --- Dimensões do painel e grade de layout (pixels do canvas) -------
        private const float PanelWidth = 780f;
        private const float PanelHeight = 560f;

        private static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        private static readonly Vector2 CenterAnchor = new Vector2(0.5f, 0.5f);

        // Idiomas do jogo. O Jotunn sempre registra o inglês e depois o idioma do jogador;
        // gravamos os dois com o texto em PT-BR (idioma do mod), como na Fase 2.6a.
        private const string LanguageEnglish = "English";
        private const string LanguagePortugueseBrazilian = "Portuguese_Brazilian";

        // ---------------------------------------------------------------------
        // Tokens de localização (chave com '$'; fallback PT-BR embutido)
        // ---------------------------------------------------------------------

        private const string TokenTitle = "$modia_menu_title";
        private const string TokenClose = "$modia_menu_close";

        private const string TokenStatus = "$modia_menu_status";
        private const string TokenHealth = "$modia_menu_health";
        private const string TokenMode = "$modia_menu_mode";
        private const string TokenHarvestState = "$modia_menu_harvest_state";

        private const string TokenAppearance = "$modia_menu_appearance";
        private const string TokenAppearanceEnabled = "$modia_menu_appearance_enabled";
        private const string TokenSkinColor = "$modia_menu_skin_color";
        private const string TokenHairColor = "$modia_menu_hair_color";
        private const string TokenHair = "$modia_menu_hair";
        private const string TokenBeard = "$modia_menu_beard";
        private const string TokenStyleDefault = "$modia_menu_style_default";
        private const string TokenStyleNone = "$modia_menu_style_none";

        private const string TokenCombat = "$modia_menu_combat";
        private const string TokenModePassive = "$modia_menu_mode_passive";
        private const string TokenModeDefensive = "$modia_menu_mode_defensive";
        private const string TokenModeAggressive = "$modia_menu_mode_aggressive";

        private const string TokenHarvest = "$modia_menu_harvest";
        private const string TokenHarvestResource = "$modia_menu_harvest_resource";
        private const string TokenHarvestOther = "$modia_menu_harvest_other";
        private const string TokenHarvestCustom = "$modia_menu_harvest_custom";
        private const string TokenHarvestAmount = "$modia_menu_harvest_amount";
        private const string TokenHarvestStart = "$modia_menu_harvest_start";
        private const string TokenHarvestStop = "$modia_menu_harvest_stop";
        private const string TokenHarvestActive = "$modia_menu_harvest_active";
        private const string TokenHarvestInactive = "$modia_menu_harvest_inactive";
        private const string TokenNoLimit = "$modia_menu_no_limit";

        private const string TokenKeys = "$modia_menu_keys";
        private const string TokenKeySpawn = "$modia_menu_key_spawn";
        private const string TokenKeyDismiss = "$modia_menu_key_dismiss";
        private const string TokenKeyMode = "$modia_menu_key_mode";
        private const string TokenKeyHarvest = "$modia_menu_key_harvest";
        private const string TokenKeyMenu = "$modia_menu_key_menu";

        private const string TokenNoCompanion = "$modia_menu_no_companion";
        private const string TokenTooFar = "$modia_menu_too_far";

        /// <summary>Comida ativa e resistência (stamina) do companheiro (Fase 2.13).</summary>
        private const string TokenFood = "$modia_menu_food";
        private const string TokenStamina = "$modia_menu_stamina";

        /// <summary>Token → texto em PT-BR (é também o fallback usado em código).</summary>
        private static readonly Dictionary<string, string> s_tokens = new Dictionary<string, string>
        {
            { TokenTitle, "Companheiro" },
            { TokenClose, "Fechar" },

            { TokenStatus, "Status" },
            { TokenHealth, "Vida" },
            { TokenMode, "Modo" },
            { TokenHarvestState, "Coleta" },
            { TokenFood, "Comida" },
            { TokenStamina, "Resistência" },

            { TokenAppearance, "Aparência" },
            { TokenAppearanceEnabled, "Aparência personalizada" },
            { TokenSkinColor, "Cor da pele" },
            { TokenHairColor, "Cor do cabelo" },
            { TokenHair, "Cabelo" },
            { TokenBeard, "Barba" },
            { TokenStyleDefault, "Padrão do jogo" },
            { TokenStyleNone, "Nenhum" },

            { TokenCombat, "Combate" },
            { TokenModePassive, "Passivo" },
            { TokenModeDefensive, "Defensivo" },
            { TokenModeAggressive, "Agressivo" },

            { TokenHarvest, "Coleta" },
            { TokenHarvestResource, "Recurso" },
            { TokenHarvestOther, "Outro…" },
            { TokenHarvestCustom, "Outro (digite)" },
            { TokenHarvestAmount, "Quantidade" },
            { TokenHarvestStart, "Iniciar" },
            { TokenHarvestStop, "Parar" },
            { TokenHarvestActive, "Coletando" },
            { TokenHarvestInactive, "Parada" },
            { TokenNoLimit, "0 = sem limite" },

            { TokenKeys, "Teclas" },
            { TokenKeySpawn, "Invocar" },
            { TokenKeyDismiss, "Dispensar" },
            { TokenKeyMode, "Modo de combate" },
            { TokenKeyHarvest, "Coleta" },
            { TokenKeyMenu, "Menu" },

            { TokenNoCompanion, "Nenhum companheiro por perto." },
            { TokenTooFar, "Chegue mais perto do companheiro para abrir o menu." },
        };

        // ---------------------------------------------------------------------
        // Estado
        // ---------------------------------------------------------------------

        private static ConfigEntry<KeyCode> _menuKey;
        private static bool _localizationRegistered;

        private static bool _open;
        private static bool _inputBlocked;
        private static float _statusTimer;

        /// <summary>Guarda contra callbacks ao reescrever os valores dos controles.</summary>
        private static bool _refreshing;

        private static GameObject _panel;

        private static Text _statusText;
        private static Text _harvestStatusText;
        private static Text _keysText;

        private static Toggle _appearanceToggle;
        private static Slider _skinR;
        private static Slider _skinG;
        private static Slider _skinB;
        private static Slider _hairR;
        private static Slider _hairG;
        private static Slider _hairB;
        private static Image _skinSwatch;
        private static Image _hairSwatch;

        private static Dropdown _modeDropdown;
        private static Dropdown _hairDropdown;
        private static Dropdown _beardDropdown;

        private static InputField _amountField;

        /// <summary>Dropdown dos recursos conhecidos (o último item é "Outro…", que revela o campo livre).</summary>
        private static Dropdown _resourceDropdown;

        /// <summary>Prefab por índice do dropdown (<see cref="CompanionHarvestResources.OtherValue"/> no fim).</summary>
        private static List<string> _resourceValues;

        /// <summary>Campo livre de recurso, mostrado só quando "Outro…" está escolhido.</summary>
        private static InputField _customResourceField;
        private static GameObject _customResourceLabel;

        /// <summary>Valor de config por índice dos dropdowns de cabelo/barba (0 = "" e 1 = "none").</summary>
        private static List<string> _hairValues;
        private static List<string> _beardValues;

        /// <summary>Painel aberto (o menu do mod responde ao input).</summary>
        internal static bool IsOpen => _open;

        /// <summary>Tecla que abre/fecha o menu (config <c>Teclas/MenuKey</c>).</summary>
        internal static KeyCode MenuKey => _menuKey != null ? _menuKey.Value : KeyCode.Y;

        internal static void Configure(ConfigEntry<KeyCode> menuKey)
        {
            _menuKey = menuKey;
            Plugin.Log.LogInfo($"[ModIA] Menu do companheiro: tecla = {MenuKey} (abre a até {OpenDistance} m).");
        }

        /// <summary>
        /// Assina o evento do Jotunn que avisa que a GUI da cena foi (re)criada. Não montamos nada
        /// aqui: só invalidamos o estado, porque os objetos da cena anterior foram destruídos.
        /// </summary>
        internal static void Init()
        {
            GUIManager.OnCustomGUIAvailable -= OnCustomGuiAvailable;
            GUIManager.OnCustomGUIAvailable += OnCustomGuiAvailable;
        }

        /// <summary>Solta o estado estático (unload do plugin / fim do mundo).</summary>
        internal static void Shutdown()
        {
            GUIManager.OnCustomGUIAvailable -= OnCustomGuiAvailable;
            ClosePanel();
            ClearReferences();
        }

        private static void OnCustomGuiAvailable()
        {
            // A GUI da cena antiga morreu: fecha o painel (liberando o input) e descarta as
            // referências; a próxima abertura remonta tudo do zero.
            ClosePanel();
            ClearReferences();
        }

        // ---------------------------------------------------------------------
        // Tick (chamado pelo Plugin a cada frame)
        // ---------------------------------------------------------------------

        internal static void Tick()
        {
            if (_menuKey == null)
            {
                return;
            }

            if (_open)
            {
                UpdateOpen();
                return;
            }

            if (CompanionManager.GameUiCapturesInput())
            {
                return;
            }

            if (ZInput.GetKeyDown(_menuKey.Value, false))
            {
                TryOpen();
            }
        }

        /// <summary>
        /// Loop do painel aberto: fecha por tecla/ESC, fecha se o companheiro sumir ou o jogador se
        /// afastar, e atualiza o status a cada <see cref="StatusRefreshInterval"/>.
        /// </summary>
        private static void UpdateOpen()
        {
            _statusTimer -= Time.unscaledDeltaTime;
            if (_statusTimer <= 0f)
            {
                _statusTimer = StatusRefreshInterval;
                RefreshStatus();
                RefreshHarvestStatus();
            }

            Player player = Player.m_localPlayer;
            CompanionAI companion = CompanionManager.ActiveCompanion;
            Character body = Body(companion);
            if (player == null || player.IsDead() || companion == null || body == null ||
                body.IsDead() ||
                Vector3.Distance(player.transform.position, companion.transform.position) >
                AutoCloseDistance)
            {
                // Nunca deixar o jogador travado: o painel fecha e o input é liberado.
                ClosePanel();
                return;
            }

            // Rede de segurança: se alguma UI do jogo capturar o input (menu de pausa, console),
            // soltamos o painel para não competir por ele.
            if (Menu.IsVisible() || Console.IsVisible())
            {
                ClosePanel();
                return;
            }

            // Enquanto o jogador digita em um campo, as teclas são do campo (digitar "y" no
            // recurso não pode fechar o menu).
            if (TypingInField())
            {
                return;
            }

            if (ZInput.GetKeyDown(_menuKey.Value, false) || ZInput.GetKeyDown(KeyCode.Escape, false))
            {
                ClosePanel();
            }
        }

        private static bool TypingInField()
        {
            return (_customResourceField != null && _customResourceField.isFocused) ||
                   (_amountField != null && _amountField.isFocused);
        }

        private static void TryOpen()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return;
            }

            CompanionAI companion = CompanionManager.ActiveCompanion;
            Character body = Body(companion);
            if (companion == null || body == null || body.IsDead())
            {
                CompanionHarvest.ShowMessage(
                    Text(TokenNoCompanion), MessageHud.MessageType.TopLeft);
                return;
            }

            if (Vector3.Distance(player.transform.position, companion.transform.position) > OpenDistance)
            {
                CompanionHarvest.ShowMessage(Text(TokenTooFar), MessageHud.MessageType.TopLeft);
                return;
            }

            if (!EnsureBuilt())
            {
                Plugin.Log.LogWarning("[ModIA] Menu: GUI do Jotunn indisponível; não abri o painel.");
                return;
            }

            RefreshAll();
            _panel.SetActive(true);
            _open = true;
            _statusTimer = StatusRefreshInterval;
            AcquireInput();

            Plugin.Log.LogInfo("[ModIA] Menu do companheiro aberto.");
        }

        private static void ClosePanel()
        {
            if (_open)
            {
                Plugin.Log.LogInfo("[ModIA] Menu do companheiro fechado.");
            }

            _open = false;

            if (_panel != null)
            {
                _panel.SetActive(false);
            }

            ReleaseInput();
        }

        private static void AcquireInput()
        {
            if (_inputBlocked)
            {
                return;
            }

            _inputBlocked = true;
            GUIManager.BlockInput(true);
        }

        private static void ReleaseInput()
        {
            if (!_inputBlocked)
            {
                return;
            }

            _inputBlocked = false;
            GUIManager.BlockInput(false);
        }

        // ---------------------------------------------------------------------
        // Construção da UI (uma vez por cena)
        // ---------------------------------------------------------------------

        private static bool EnsureBuilt()
        {
            if (_panel != null)
            {
                return true;
            }

            GUIManager gui = GUIManager.Instance;
            if (gui == null || GUIManager.CustomGUIFront == null)
            {
                return false;
            }

            try
            {
                Build(gui, GUIManager.CustomGUIFront.transform);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[ModIA] Falha ao montar o menu do companheiro: {e}");
                ClearReferences();
                return false;
            }

            return _panel != null;
        }

        private static void Build(GUIManager gui, Transform parent)
        {
            _panel = gui.CreateWoodpanel(
                parent: parent,
                anchorMin: CenterAnchor,
                anchorMax: CenterAnchor,
                position: Vector2.zero,
                width: PanelWidth,
                height: PanelHeight,
                draggable: true);
            _panel.name = "ModIA_CompanionMenu";

            // Título + fechar.
            MakeLabel(gui, Text(TokenTitle), 20f, 14f, 480f, 30f, 24, gui.ValheimOrange);

            GameObject close = gui.CreateButton(
                Text(TokenClose), _panel.transform, TopLeft, TopLeft,
                Center(PanelWidth - 20f - 110f, 14f, 110f, 30f), 110f, 30f);
            close.GetComponent<Button>().onClick.AddListener(ClosePanel);

            BuildStatus(gui);
            BuildAppearance(gui);
            BuildCombat(gui);
            BuildHarvest(gui);
            BuildKeys(gui);

            _panel.SetActive(false);
            Plugin.Log.LogInfo("[ModIA] Menu do companheiro montado (780x560).");
        }

        private static void BuildStatus(GUIManager gui)
        {
            MakeHeader(gui, TokenStatus, 20f, 54f, 360f);

            _statusText = MakeLabel(gui, string.Empty, 30f, 82f, 350f, 62f, 15,
                Color.white).GetComponent<Text>();
        }

        private static void BuildAppearance(GUIManager gui)
        {
            MakeHeader(gui, TokenAppearance, 20f, 148f, 360f);

            // Liga/desliga a aparência personalizada.
            GameObject toggleGo = gui.CreateToggle(_panel.transform, 28f, 28f);
            toggleGo.name = "ModIA_AppearanceToggle";
            toggleGo.transform.SetParent(_panel.transform, false);
            Place(toggleGo, 30f, 178f, 28f, 28f);
            _appearanceToggle = toggleGo.GetComponent<Toggle>();
            _appearanceToggle.onValueChanged.AddListener(OnAppearanceToggled);

            MakeLabel(gui, Text(TokenAppearanceEnabled), 66f, 178f, 300f, 28f, 15, Color.white);

            // Cor da pele (3 sliders + amostra).
            MakeLabel(gui, Text(TokenSkinColor), 30f, 214f, 340f, 20f, 15, gui.ValheimOrange);
            _skinR = ColorSlider(gui, "R", 238f, OnSkinColorChanged);
            _skinG = ColorSlider(gui, "G", 260f, OnSkinColorChanged);
            _skinB = ColorSlider(gui, "B", 282f, OnSkinColorChanged);
            _skinSwatch = ColorSwatch(350f, 258f);

            // Cor do cabelo/barba.
            MakeLabel(gui, Text(TokenHairColor), 30f, 312f, 340f, 20f, 15, gui.ValheimOrange);
            _hairR = ColorSlider(gui, "R", 336f, OnHairColorChanged);
            _hairG = ColorSlider(gui, "G", 358f, OnHairColorChanged);
            _hairB = ColorSlider(gui, "B", 380f, OnHairColorChanged);
            _hairSwatch = ColorSwatch(350f, 356f);

            // Cabelo e barba.
            MakeLabel(gui, Text(TokenHair), 30f, 410f, 340f, 20f, 15, gui.ValheimOrange);
            _hairDropdown = MenuDropdown(gui, 30f, 432f, 348f, OnHairChanged);
            _hairValues = BuildAppearanceOptions(CompanionAppearance.HairPrefix, _hairDropdown);

            MakeLabel(gui, Text(TokenBeard), 30f, 470f, 340f, 20f, 15, gui.ValheimOrange);
            _beardDropdown = MenuDropdown(gui, 30f, 492f, 348f, OnBeardChanged);
            _beardValues = BuildAppearanceOptions(CompanionAppearance.BeardPrefix, _beardDropdown);
        }

        private static void BuildCombat(GUIManager gui)
        {
            MakeHeader(gui, TokenCombat, 440f, 54f, 320f);

            MakeLabel(gui, Text(TokenMode), 448f, 84f, 90f, 26f, 15, Color.white);
            _modeDropdown = MenuDropdown(gui, 544f, 84f, 216f, OnModeChanged);

            _modeDropdown.AddOptions(new List<Dropdown.OptionData>
            {
                new Dropdown.OptionData(Text(TokenModePassive)),
                new Dropdown.OptionData(Text(TokenModeDefensive)),
                new Dropdown.OptionData(Text(TokenModeAggressive)),
            });
            _modeDropdown.RefreshShownValue();
        }

        private static void BuildHarvest(GUIManager gui)
        {
            MakeHeader(gui, TokenHarvest, 440f, 122f, 320f);

            // Recurso: dropdown com a lista curada (nomes localizados) + "Outro…".
            MakeLabel(gui, Text(TokenHarvestResource), 448f, 152f, 90f, 26f, 15, Color.white);
            _resourceDropdown = MenuDropdown(gui, 544f, 152f, 216f, OnResourceChanged);
            BuildResourceOptions(_resourceDropdown);

            // Campo livre, visível só quando "Outro…" está escolhido.
            _customResourceLabel = MakeLabel(
                gui, Text(TokenHarvestCustom), 448f, 186f, 90f, 26f, 15, Color.white);
            _customResourceField = gui.CreateInputField(
                parent: _panel.transform,
                anchorMin: TopLeft,
                anchorMax: TopLeft,
                position: Center(544f, 186f, 216f, 26f),
                contentType: InputField.ContentType.Standard,
                placeholderText: "Wood",
                fontSize: 15,
                width: 216f,
                height: 26f).GetComponent<InputField>();
            SetCustomResourceVisible(false);

            MakeLabel(gui, Text(TokenHarvestAmount), 448f, 220f, 90f, 26f, 15, Color.white);
            _amountField = gui.CreateInputField(
                parent: _panel.transform,
                anchorMin: TopLeft,
                anchorMax: TopLeft,
                position: Center(544f, 220f, 216f, 26f),
                contentType: InputField.ContentType.IntegerNumber,
                placeholderText: "0",
                fontSize: 15,
                width: 216f,
                height: 26f).GetComponent<InputField>();

            GameObject start = gui.CreateButton(
                Text(TokenHarvestStart), _panel.transform, TopLeft, TopLeft,
                Center(448f, 254f, 148f, 30f), 148f, 30f);
            start.GetComponent<Button>().onClick.AddListener(OnHarvestStart);

            GameObject stop = gui.CreateButton(
                Text(TokenHarvestStop), _panel.transform, TopLeft, TopLeft,
                Center(612f, 254f, 148f, 30f), 148f, 30f);
            stop.GetComponent<Button>().onClick.AddListener(OnHarvestStop);

            _harvestStatusText = MakeLabel(gui, string.Empty, 448f, 290f, 312f, 44f, 14,
                Color.white).GetComponent<Text>();
        }

        /// <summary>
        /// Preenche o dropdown de recursos com a lista curada (rótulo = nome localizado do item) e
        /// fecha com a opção "Outro…", que revela o campo livre.
        /// </summary>
        private static void BuildResourceOptions(Dropdown dropdown)
        {
            if (dropdown == null)
            {
                return;
            }

            _resourceValues = new List<string>();
            List<Dropdown.OptionData> options = new List<Dropdown.OptionData>();

            HarvestResourceInfo[] all = CompanionHarvestResources.All;
            for (int i = 0; i < all.Length; i++)
            {
                _resourceValues.Add(all[i].Prefab);
                options.Add(new Dropdown.OptionData(CompanionHarvestResources.DisplayName(all[i].Prefab)));
            }

            _resourceValues.Add(CompanionHarvestResources.OtherValue);
            options.Add(new Dropdown.OptionData(Text(TokenHarvestOther)));

            dropdown.ClearOptions();
            dropdown.AddOptions(options);
            dropdown.RefreshShownValue();
        }

        private static void SetCustomResourceVisible(bool visible)
        {
            if (_customResourceLabel != null)
            {
                _customResourceLabel.SetActive(visible);
            }

            if (_customResourceField != null)
            {
                _customResourceField.gameObject.SetActive(visible);
            }
        }

        private static void BuildKeys(GUIManager gui)
        {
            MakeHeader(gui, TokenKeys, 440f, 340f, 320f);

            _keysText = MakeLabel(gui, string.Empty, 448f, 366f, 312f, 130f, 14,
                Color.white).GetComponent<Text>();
        }

        // ---------------------------------------------------------------------
        // Controles (wrappers do GUIManager)
        // ---------------------------------------------------------------------

        private static void MakeHeader(GUIManager gui, string token, float left, float top, float width)
        {
            MakeLabel(gui, Text(token), left, top, width, 24f, 18, gui.ValheimYellow);
        }

        private static GameObject MakeLabel(
            GUIManager gui, string text, float left, float top,
            float width, float height, int fontSize, Color color)
        {
            return gui.CreateText(
                text: text,
                parent: _panel.transform,
                anchorMin: TopLeft,
                anchorMax: TopLeft,
                position: Center(left, top, width, height),
                font: gui.AveriaSerifBold,
                fontSize: fontSize,
                color: color,
                outline: true,
                outlineColor: Color.black,
                width: width,
                height: height,
                addContentSizeFitter: false);
        }

        /// <summary>
        /// Um slider 0..1 do estilo Valheim (o GUIManager não tem <c>CreateSlider</c> em 2.30.2:
        /// criamos pelo <see cref="DefaultControls"/> e aplicamos <c>ApplySliderStyle</c>).
        /// </summary>
        private static Slider ColorSlider(
            GUIManager gui, string label, float top, UnityEngine.Events.UnityAction<float> onChanged)
        {
            const float labelLeft = 30f;
            const float sliderLeft = 50f;
            const float sliderWidth = 296f;
            const float sliderHeight = 20f;

            MakeLabel(gui, label, labelLeft, top, 16f, sliderHeight, 14, Color.white);

            GameObject go = DefaultControls.CreateSlider(gui.ValheimControlResources);
            go.name = "ModIA_Slider";
            go.transform.SetParent(_panel.transform, false);
            Place(go, sliderLeft, top, sliderWidth, sliderHeight);

            Slider slider = go.GetComponent<Slider>();
            gui.ApplySliderStyle(slider, new Vector2(17f, 17f));
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.onValueChanged.AddListener(onChanged);
            return slider;
        }

        private static Image ColorSwatch(float left, float top)
        {
            GameObject go = new GameObject("ModIA_Swatch", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_panel.transform, false);
            Place(go, left, top, 28f, 28f);

            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private static Dropdown MenuDropdown(
            GUIManager gui, float left, float top, float width,
            UnityEngine.Events.UnityAction<int> onChanged)
        {
            GameObject go = gui.CreateDropDown(
                parent: _panel.transform,
                anchorMin: TopLeft,
                anchorMax: TopLeft,
                position: Center(left, top, width, 28f),
                fontSize: 15,
                width: width,
                height: 28f);

            Dropdown dropdown = go.GetComponent<Dropdown>();
            dropdown.onValueChanged.AddListener(onChanged);
            return dropdown;
        }

        /// <summary>Posiciona/redimensiona um controle criado fora dos helpers do GUIManager.</summary>
        private static void Place(GameObject go, float left, float top, float width, float height)
        {
            RectTransform rect = go.transform as RectTransform;
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = TopLeft;
            rect.anchorMax = TopLeft;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Center(left, top, width, height);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }

        /// <summary>Posição (pivô central) de uma caixa a partir do canto superior esquerdo.</summary>
        private static Vector2 Center(float left, float top, float width, float height)
        {
            return new Vector2(left + width * 0.5f, -(top + height * 0.5f));
        }

        // ---------------------------------------------------------------------
        // Dados dos dropdowns de aparência
        // ---------------------------------------------------------------------

        /// <summary>
        /// Preenche um dropdown de cabelo/barba: índice 0 = "Padrão do jogo" (config vazia), 1 =
        /// "Nenhum" (hash 0) e, a partir de 2, os prefabs encontrados no ObjectDB. Devolve a lista
        /// de valores de config por índice.
        /// </summary>
        private static List<string> BuildAppearanceOptions(string prefix, Dropdown dropdown)
        {
            List<string> values = new List<string>
            {
                CompanionAppearance.AutoValue,
                CompanionAppearance.NoneValue,
            };

            List<Dropdown.OptionData> options = new List<Dropdown.OptionData>
            {
                new Dropdown.OptionData(Text(TokenStyleDefault)),
                new Dropdown.OptionData(Text(TokenStyleNone)),
            };

            List<string> names = CompanionAppearance.GetCustomizationNames(prefix);
            for (int i = 0; i < names.Count; i++)
            {
                values.Add(names[i]);
                options.Add(new Dropdown.OptionData(names[i]));
            }

            dropdown.AddOptions(options);
            dropdown.RefreshShownValue();
            return values;
        }

        private static int IndexOfAppearanceValue(List<string> values, string current)
        {
            if (values == null)
            {
                return 0;
            }

            if (string.IsNullOrEmpty(current))
            {
                return 0;
            }

            if (CompanionAppearance.IsDisabledValue(current))
            {
                return 1;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], current.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return 0;
        }

        // ---------------------------------------------------------------------
        // Sincronização estado -> controles
        // ---------------------------------------------------------------------

        private static void RefreshAll()
        {
            _refreshing = true;
            try
            {
                RefreshStatus();
                RefreshAppearance();
                RefreshCombat();
                RefreshHarvest();
                RefreshKeys();
            }
            finally
            {
                _refreshing = false;
            }
        }

        private static void RefreshStatus()
        {
            if (_statusText == null)
            {
                return;
            }

            CompanionAI companion = CompanionManager.ActiveCompanion;
            Character body = Body(companion);
            if (companion == null || body == null)
            {
                _statusText.text = Text(TokenNoCompanion);
                return;
            }

            string name = CompanionHover.GetName();
            string health = string.Format(
                "{0}: {1:0} / {2:0}",
                Text(TokenHealth),
                body.GetHealth(),
                body.GetMaxHealth());
            string mode = string.Format("{0}: {1}", Text(TokenMode), ModeDisplayName(CompanionCombat.Mode));

            // Fase 2.13: comida ativa e resistência substituem a linha de coleta — o estado da
            // coleta já tem o painel próprio logo abaixo ("Coleta"), com muito mais detalhe.
            CompanionFood food = companion.GetComponent<CompanionFood>();
            CompanionStamina stamina = companion.GetComponent<CompanionStamina>();

            string foodText = string.Format(
                "{0}: {1}/{2}",
                Text(TokenFood),
                food != null ? food.ActiveCount : 0,
                CompanionFood.MaxSlots);
            string staminaText = string.Format(
                "{0}: {1}",
                Text(TokenStamina),
                stamina != null ? stamina.Describe() : "-");

            _statusText.text = name + "\n" + health + "   |   " + mode + "\n" +
                               foodText + "   |   " + staminaText;
        }

        private static void RefreshAppearance()
        {
            if (_appearanceToggle != null)
            {
                _appearanceToggle.isOn = CompanionAppearance.Enabled;
            }

            SetSlider(_skinR, CompanionAppearance.SkinColor.x);
            SetSlider(_skinG, CompanionAppearance.SkinColor.y);
            SetSlider(_skinB, CompanionAppearance.SkinColor.z);
            SetSlider(_hairR, CompanionAppearance.HairColor.x);
            SetSlider(_hairG, CompanionAppearance.HairColor.y);
            SetSlider(_hairB, CompanionAppearance.HairColor.z);

            if (_skinSwatch != null)
            {
                _skinSwatch.color = ToColor(CompanionAppearance.SkinColor);
            }

            if (_hairSwatch != null)
            {
                _hairSwatch.color = ToColor(CompanionAppearance.HairColor);
            }

            if (_hairDropdown != null && _hairValues != null)
            {
                _hairDropdown.value = IndexOfAppearanceValue(_hairValues, CompanionAppearance.HairPrefabName);
                _hairDropdown.RefreshShownValue();
            }

            if (_beardDropdown != null && _beardValues != null)
            {
                _beardDropdown.value = IndexOfAppearanceValue(_beardValues, CompanionAppearance.BeardPrefabName);
                _beardDropdown.RefreshShownValue();
            }
        }

        private static void SetSlider(Slider slider, float value)
        {
            if (slider != null)
            {
                slider.SetValueWithoutNotify(Mathf.Clamp01(value));
            }
        }

        private static void RefreshCombat()
        {
            if (_modeDropdown != null)
            {
                _modeDropdown.value = (int)CompanionCombat.Mode;
                _modeDropdown.RefreshShownValue();
            }
        }

        private static void RefreshHarvest()
        {
            string resource = CompanionHarvest.IsActive
                ? CompanionHarvest.ResourceName
                : CompanionHarvest.DefaultResource;

            if (_resourceDropdown != null && _resourceValues != null && _resourceValues.Count > 0)
            {
                int index = _resourceValues.IndexOf(resource ?? string.Empty);
                if (index < 0)
                {
                    // Recurso fora da lista curada: cai em "Outro…" e o nome vai para o campo livre.
                    index = _resourceValues.Count - 1;
                    if (_customResourceField != null)
                    {
                        _customResourceField.SetTextWithoutNotify(resource ?? string.Empty);
                    }
                }

                _resourceDropdown.SetValueWithoutNotify(index);
                _resourceDropdown.RefreshShownValue();
                SetCustomResourceVisible(_resourceValues[index] == CompanionHarvestResources.OtherValue);
            }

            if (_amountField != null)
            {
                int amount = CompanionHarvest.IsActive
                    ? (CompanionHarvest.HasLimit ? CompanionHarvest.Remaining : 0)
                    : CompanionHarvest.DefaultAmount;
                _amountField.SetTextWithoutNotify(amount.ToString());
            }

            RefreshHarvestStatus();
        }

        private static void RefreshHarvestStatus()
        {
            if (_harvestStatusText == null)
            {
                return;
            }

            _harvestStatusText.text = HarvestStateText() +
                (CompanionHarvest.IsActive ? string.Empty : "\n" + Text(TokenNoLimit));
        }

        private static void RefreshKeys()
        {
            if (_keysText == null)
            {
                return;
            }

            _keysText.text =
                Key(CompanionManager.SpawnKey) + " " + Text(TokenKeySpawn) + "\n" +
                Key(CompanionManager.DismissKey) + " " + Text(TokenKeyDismiss) + "\n" +
                Key(CompanionCombat.ModeKey) + " " + Text(TokenKeyMode) + "\n" +
                Key(CompanionHarvest.HarvestKey) + " " + Text(TokenKeyHarvest) + "\n" +
                Key(MenuKey) + " " + Text(TokenKeyMenu);
        }

        // ---------------------------------------------------------------------
        // Callbacks dos controles
        // ---------------------------------------------------------------------

        private static void OnAppearanceToggled(bool value)
        {
            if (_refreshing)
            {
                return;
            }

            CompanionAppearance.SetEnabled(value);

            // Reescreve os controles sem deixar os setters dispararem os callbacks de novo.
            _refreshing = true;
            try
            {
                RefreshAppearance();
            }
            finally
            {
                _refreshing = false;
            }
        }

        private static void OnSkinColorChanged(float _)
        {
            if (_refreshing)
            {
                return;
            }

            Vector3 color = new Vector3(
                _skinR != null ? _skinR.value : 0f,
                _skinG != null ? _skinG.value : 0f,
                _skinB != null ? _skinB.value : 0f);

            CompanionAppearance.SetSkinColor(color);

            if (_skinSwatch != null)
            {
                _skinSwatch.color = ToColor(color);
            }
        }

        private static void OnHairColorChanged(float _)
        {
            if (_refreshing)
            {
                return;
            }

            Vector3 color = new Vector3(
                _hairR != null ? _hairR.value : 0f,
                _hairG != null ? _hairG.value : 0f,
                _hairB != null ? _hairB.value : 0f);

            CompanionAppearance.SetHairColor(color);

            if (_hairSwatch != null)
            {
                _hairSwatch.color = ToColor(color);
            }
        }

        private static void OnHairChanged(int index)
        {
            if (_refreshing || _hairValues == null || index < 0 || index >= _hairValues.Count)
            {
                return;
            }

            CompanionAppearance.SetHairPrefab(_hairValues[index]);
        }

        private static void OnBeardChanged(int index)
        {
            if (_refreshing || _beardValues == null || index < 0 || index >= _beardValues.Count)
            {
                return;
            }

            CompanionAppearance.SetBeardPrefab(_beardValues[index]);
        }

        private static void OnModeChanged(int index)
        {
            if (_refreshing)
            {
                return;
            }

            CompanionCombat.SetMode((CompanionMode)Mathf.Clamp(index, 0, 2), "menu");
        }

        /// <summary>
        /// Troca do recurso no dropdown: mostra o campo livre só quando "Outro…" foi escolhido.
        /// </summary>
        private static void OnResourceChanged(int index)
        {
            if (_refreshing || _resourceValues == null || index < 0 || index >= _resourceValues.Count)
            {
                return;
            }

            bool other = _resourceValues[index] == CompanionHarvestResources.OtherValue;
            SetCustomResourceVisible(other);

            if (other && _customResourceField != null && !_customResourceField.isFocused)
            {
                _customResourceField.ActivateInputField();
            }
        }

        /// <summary>
        /// Recurso escolhido no menu: o prefab do dropdown ou, em "Outro…", o texto digitado no
        /// campo livre (que aceita o mesmo que o <c>modia_gather</c>: nome do item, apelido PT/EN).
        /// </summary>
        private static string SelectedResource()
        {
            if (_resourceDropdown != null && _resourceValues != null &&
                _resourceDropdown.value >= 0 && _resourceDropdown.value < _resourceValues.Count)
            {
                string value = _resourceValues[_resourceDropdown.value];
                if (value != CompanionHarvestResources.OtherValue)
                {
                    return value;
                }
            }

            return _customResourceField != null ? _customResourceField.text : string.Empty;
        }

        private static void OnHarvestStart()
        {
            string resource = SelectedResource();
            int amount = 0;
            if (_amountField != null)
            {
                int.TryParse(_amountField.text, out amount);
            }

            if (!CompanionHarvest.Start(
                    CompanionManager.ActiveCompanion, resource, amount, "menu", out string error) &&
                error != null)
            {
                CompanionHarvest.ShowMessage(error, MessageHud.MessageType.Center);
            }

            RefreshHarvestStatus();
        }

        private static void OnHarvestStop()
        {
            CompanionHarvest.Stop("menu");
            RefreshHarvestStatus();
        }

        // ---------------------------------------------------------------------
        // Texto auxiliar
        // ---------------------------------------------------------------------

        private static string HarvestStateText()
        {
            if (!CompanionHarvest.IsActive)
            {
                return string.Format(
                    "{0} ({1})",
                    Text(TokenHarvestInactive),
                    ResourceName(CompanionHarvest.DefaultResource));
            }

            string progress = CompanionHarvest.HasLimit
                ? string.Format("{0}/{1}", CompanionHarvest.Collected, CompanionHarvest.Collected + CompanionHarvest.Remaining)
                : CompanionHarvest.Collected.ToString();

            return string.Format(
                "{0} {1} ({2})",
                Text(TokenHarvestActive),
                ResourceName(CompanionHarvest.ResourceName),
                progress);
        }

        private static string ModeDisplayName(CompanionMode mode)
        {
            switch (mode)
            {
                case CompanionMode.Passive:
                    return Text(TokenModePassive);
                case CompanionMode.Aggressive:
                    return Text(TokenModeAggressive);
                default:
                    return Text(TokenModeDefensive);
            }
        }

        /// <summary>
        /// Nome de exibição do recurso (ex.: <c>Wood</c> → "Madeira"). Fonte única:
        /// <see cref="CompanionHarvestResources.DisplayName"/> (nome localizado do item com reserva
        /// PT-BR e, por último, o próprio nome do prefab).
        /// </summary>
        private static string ResourceName(string prefabName)
        {
            return CompanionHarvestResources.DisplayName(prefabName);
        }

        private static string Key(KeyCode key)
        {
            return "[<color=yellow><b>" + key + "</b></color>]";
        }

        /// <summary>
        /// O corpo (<see cref="Character"/>) do companheiro. A IA (<see cref="CompanionAI"/>) é um
        /// <c>BaseAI</c> à parte: vida/morte ficam no <c>Character</c> do mesmo GameObject.
        /// </summary>
        private static Character Body(CompanionAI companion)
        {
            return companion != null ? companion.GetComponent<Character>() : null;
        }

        /// <summary>Cor do swatch (o jogo guarda a cor como <see cref="Vector3"/> RGB 0..1).</summary>
        private static Color ToColor(Vector3 rgb)
        {
            return new Color(rgb.x, rgb.y, rgb.z, 1f);
        }

        private static string Text(string token)
        {
            Localization localization = Localization.instance;
            if (localization != null)
            {
                string value = localization.Localize(token);
                if (!string.IsNullOrEmpty(value) && value[0] != '[')
                {
                    return value;
                }
            }

            return s_tokens.TryGetValue(token, out string fallback) ? fallback : token;
        }

        // ---------------------------------------------------------------------
        // Localização
        // ---------------------------------------------------------------------

        /// <summary>
        /// Registra os tokens do menu no <see cref="LocalizationManager"/> (chamado no
        /// <c>Plugin.Awake</c>, junto com os do hover). Inglês e PT-BR recebem o mesmo texto
        /// (idioma do mod); o fallback em código cobre o caso de a loc ainda não estar pronta.
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
                foreach (KeyValuePair<string, string> pair in s_tokens)
                {
                    localization.AddTranslation(LanguageEnglish, pair.Key, pair.Value);
                    localization.AddTranslation(LanguagePortugueseBrazilian, pair.Key, pair.Value);
                }

                Plugin.Log.LogInfo(
                    $"[ModIA] Localização do menu registrada ({LanguagePortugueseBrazilian}/" +
                    $"{LanguageEnglish}): {s_tokens.Count} tokens.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"[ModIA] Não foi possível registrar a localização do menu ({e.Message}); " +
                    "o menu usa os textos padrão em PT-BR.");
            }
        }

        // ---------------------------------------------------------------------
        // Limpeza
        // ---------------------------------------------------------------------

        private static void ClearReferences()
        {
            _panel = null;
            _statusText = null;
            _harvestStatusText = null;
            _keysText = null;
            _appearanceToggle = null;
            _skinR = null;
            _skinG = null;
            _skinB = null;
            _hairR = null;
            _hairG = null;
            _hairB = null;
            _skinSwatch = null;
            _hairSwatch = null;
            _modeDropdown = null;
            _hairDropdown = null;
            _beardDropdown = null;
            _resourceDropdown = null;
            _resourceValues = null;
            _customResourceField = null;
            _customResourceLabel = null;
            _amountField = null;
            _hairValues = null;
            _beardValues = null;
            _statusTimer = 0f;
        }
    }
}
