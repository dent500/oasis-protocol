using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Universal Tactical Pause, Settings & Quit Overlay that automatically attaches to any game scene.
/// Allows players to press Escape during gameplay to open the Pause Overlay (with Resume, Settings,
/// Restart Match, Main Menu, and Quit Game) without freezing or trapping the mouse cursor.
/// </summary>
public class InGamePauseManager : MonoBehaviour
{
    public static InGamePauseManager Instance { get; private set; }
    public bool IsPaused => _isPaused;

    private GameObject _pauseCanvas;
    private GameObject _mainPausePanel;
    private GameObject _settingsPanel;
    private bool _isPaused = false;
    private string _mainMenuSceneName = "MainMenu";

    // Settings UI references
    private Slider _volumeSlider;
    private Text _volumeValueText;
    private Slider _sensitivitySlider;
    private Text _sensitivityValueText;
    private readonly Image[] _qualityBtnBgs = new Image[3];
    private readonly Text[] _qualityBtnTexts = new Text[3];
    private Image _fullscreenBtnBg;
    private Text _fullscreenBtnText;
    private Image _cameraViewBtnBg;
    private Text _cameraViewBtnText;
    private Text _rebindStatusText;
    private readonly Dictionary<OasisAction, Image> _keybindBtnBgs = new Dictionary<OasisAction, Image>();
    private readonly Dictionary<OasisAction, Text> _keybindBtnTexts = new Dictionary<OasisAction, Text>();
    private OasisAction? _awaitingRebindAction = null;
    private int _selectedQualityTier = 2;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInitialize()
    {
        if (Instance == null)
        {
            GameObject host = new GameObject("InGamePauseManager_AutoHost");
            DontDestroyOnLoad(host);
            Instance = host.AddComponent<InGamePauseManager>();
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        _selectedQualityTier = PlayerPrefs.GetInt("OasisGraphicsTier", 2);
        SceneManager.sceneLoaded += OnSceneLoaded;
        BuildPauseUI();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ResumeGame();

        if (scene.name.Equals(_mainMenuSceneName, StringComparison.OrdinalIgnoreCase))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        GameObject esObj = new GameObject("Oasis_EventSystem");
        esObj.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
        esObj.AddComponent<InputSystemUIInputModule>();
#else
        esObj.AddComponent<StandaloneInputModule>();
#endif
    }

    private void Update()
    {
        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene.Equals(_mainMenuSceneName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Do not intercept ESC while the Pre-Match Character Customizer or Post-Match Summary screen is active
        if (OasisCharacterCustomizer.Instance != null && OasisCharacterCustomizer.Instance.isSelectionActive)
        {
            if (_isPaused) ResumeGame();
            return;
        }
        if (OasisShooterManager.Instance != null && OasisShooterManager.Instance.isMatchFinished)
        {
            if (_isPaused) ResumeGame();
            return;
        }

        // If listening for a keybind inside the Settings panel
        if (_isPaused && _settingsPanel != null && _settingsPanel.activeSelf && _awaitingRebindAction.HasValue)
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                _awaitingRebindAction = null;
                RefreshSettingsUI();
                return;
            }
            if (OasisKeybindManager.TryDetectAnyPressedKeyCode(out KeyCode detectedKey))
            {
                AssignReboundKey(_awaitingRebindAction.Value, detectedKey);
                return;
            }
#else
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                _awaitingRebindAction = null;
                RefreshSettingsUI();
                return;
            }
            foreach (KeyCode kc in Enum.GetValues(typeof(KeyCode)))
            {
                if (kc == KeyCode.None || kc == KeyCode.Escape || kc >= KeyCode.Mouse0) continue;
                if (Input.GetKeyDown(kc))
                {
                    AssignReboundKey(_awaitingRebindAction.Value, kc);
                    return;
                }
            }
#endif
            return;
        }

        bool escPressed = false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            escPressed = Keyboard.current.escapeKey.wasPressedThisFrame;
        }
#else
        escPressed = Input.GetKeyDown(KeyCode.Escape);
#endif

        if (escPressed)
        {
            // If Fullscreen Tactical Map is open, close it first on ESC
            if (!_isPaused && OasisMapSystem.Instance != null && OasisMapSystem.Instance.IsFullscreenOpen)
            {
                OasisMapSystem.Instance.SetFullscreenMap(false);
                return;
            }

            if (_isPaused && _settingsPanel != null && _settingsPanel.activeSelf)
            {
                CloseSettingsSubPanel();
                return;
            }

            TogglePause();
        }

        if (_isPaused)
        {
            // Continuously enforce unlocked, visible cursor while pause overlay is open
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
        }
    }

    public void TogglePause()
    {
        if (_isPaused)
        {
            ResumeGame();
        }
        else
        {
            PauseGame();
        }
    }

    public void PauseGame()
    {
        EnsureEventSystem();
        _isPaused = true;
        _awaitingRebindAction = null;

        // Only freeze Time.timeScale in offline/singleplayer so live network/relay sessions stay synchronized
        bool isOnlineSession = OasisNetworkManager.Instance != null &&
                               OasisNetworkManager.Instance.currentRole != OasisNetworkManager.NetworkRole.Offline;
        Time.timeScale = isOnlineSession ? 1f : 0f;

        var inputs = FindAnyObjectByType<StarterAssets.StarterAssetsInputs>();
        if (inputs != null)
        {
            inputs.SetCursorState(false);
            inputs.look = Vector2.zero;
            inputs.move = Vector2.zero;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (_pauseCanvas == null) BuildPauseUI();
        if (_pauseCanvas != null)
        {
            _pauseCanvas.SetActive(true);
            if (_mainPausePanel != null) _mainPausePanel.SetActive(true);
            if (_settingsPanel != null) _settingsPanel.SetActive(false);
        }
    }

    public void ResumeGame()
    {
        _isPaused = false;
        _awaitingRebindAction = null;
        Time.timeScale = 1f;

        if (_pauseCanvas != null)
        {
            _pauseCanvas.SetActive(false);
        }
        if (_settingsPanel != null)
        {
            _settingsPanel.SetActive(false);
        }

        string currentScene = SceneManager.GetActiveScene().name;
        if (!currentScene.Equals(_mainMenuSceneName, StringComparison.OrdinalIgnoreCase))
        {
            bool shouldUnlock =
                (OasisCharacterCustomizer.Instance != null && OasisCharacterCustomizer.Instance.isSelectionActive) ||
                (OasisShooterManager.Instance != null && (OasisShooterManager.Instance.isMatchFinished || OasisShooterManager.Instance.isSimulationMode)) ||
                (OasisMapSystem.Instance != null && OasisMapSystem.Instance.IsFullscreenOpen);

            if (!shouldUnlock)
            {
                var inputs = FindAnyObjectByType<StarterAssets.StarterAssetsInputs>();
                if (inputs != null)
                {
                    inputs.SetCursorState(true);
                }
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }

    public void OpenSettingsSubPanel()
    {
        _awaitingRebindAction = null;
        if (_mainPausePanel != null) _mainPausePanel.SetActive(false);
        if (_settingsPanel != null)
        {
            _settingsPanel.SetActive(true);
            RefreshSettingsUI();
        }
    }

    public void CloseSettingsSubPanel()
    {
        _awaitingRebindAction = null;
        PlayerPrefs.Save();
        if (_settingsPanel != null) _settingsPanel.SetActive(false);
        if (_mainPausePanel != null) _mainPausePanel.SetActive(true);
    }

    public void RestartCurrentScene()
    {
        Time.timeScale = 1f;
        _isPaused = false;
        if (_pauseCanvas != null) _pauseCanvas.SetActive(false);

        if (OasisShooterManager.Instance != null)
        {
            OasisShooterManager.Instance.RestartShooter();
        }
        else
        {
            Scene currentScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(currentScene.name);
        }
    }

    public void ReturnToOperativeSelection()
    {
        Time.timeScale = 1f;
        _isPaused = false;
        if (_pauseCanvas != null) _pauseCanvas.SetActive(false);
        Scene currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.name);
    }

    public void ReturnToMainMenu()
    {
        Time.timeScale = 1f;
        _isPaused = false;
        if (_pauseCanvas != null) _pauseCanvas.SetActive(false);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (SceneManager.sceneCountInBuildSettings > 0)
        {
            SceneManager.LoadScene(0);
        }
        else
        {
            SceneManager.LoadScene(_mainMenuSceneName);
        }
    }

    public void QuitApplication()
    {
        Time.timeScale = 1f;
        _isPaused = false;
        PlayerPrefs.Save();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // =========================================================================
    // PAUSE & SETTINGS OVERLAY UI BUILDER
    // =========================================================================

    private void BuildPauseUI()
    {
        if (_pauseCanvas != null) Destroy(_pauseCanvas);

        _pauseCanvas = new GameObject("Oasis_TacticalPause_Canvas");
        _pauseCanvas.transform.SetParent(transform, false);

        Canvas canvas = _pauseCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 2500; // Above all HUD, map, and scope overlays

        CanvasScaler scaler = _pauseCanvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        _pauseCanvas.AddComponent<GraphicRaycaster>();

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

        // Translucent dark backdrop
        GameObject bg = CreateAnchoredPanel(_pauseCanvas.transform, "DarkBackdrop", Vector2.zero, Vector2.one, new Color(0.02f, 0.04f, 0.08f, 0.84f));
        bg.GetComponent<Image>().raycastTarget = true;

        BuildMainPauseCard(bg.transform, font);
        BuildSettingsSubPanel(bg.transform, font);

        _pauseCanvas.SetActive(false);
    }

    private void BuildMainPauseCard(Transform parent, Font font)
    {
        GameObject cardBorder = CreateAnchoredPanel(parent, "Pause_Card_Border", new Vector2(0.33f, 0.16f), new Vector2(0.67f, 0.84f), new Color(0.20f, 0.85f, 1.0f, 0.88f));
        _mainPausePanel = cardBorder;

        GameObject card = new GameObject("Inner");
        card.transform.SetParent(cardBorder.transform, false);
        RectTransform cRT = card.AddComponent<RectTransform>();
        cRT.anchorMin = Vector2.zero;
        cRT.anchorMax = Vector2.one;
        cRT.offsetMin = new Vector2(2, 2);
        cRT.offsetMax = new Vector2(-2, -2);
        Image cardBg = card.AddComponent<Image>();
        cardBg.color = new Color(0.045f, 0.075f, 0.125f, 0.98f);

        CreateAnchoredPanel(card.transform, "TopAccentStripe", new Vector2(0f, 0.972f), new Vector2(1f, 1f), new Color(0.20f, 0.90f, 1.0f, 1f));

        CreateAnchoredText(card.transform, font, "Title",
            "OASIS PROTOCOL // TACTICAL PAUSE",
            24, FontStyle.Bold, Color.white,
            new Vector2(0.06f, 0.86f), new Vector2(0.94f, 0.95f), TextAnchor.MiddleCenter);

        CreateAnchoredText(card.transform, font, "Subtitle",
            "PRESS [ESC] TO RESUME COMBAT",
            13, FontStyle.Bold, new Color(0.25f, 0.88f, 1.0f),
            new Vector2(0.06f, 0.80f), new Vector2(0.94f, 0.86f), TextAnchor.MiddleCenter);

        CreateAnchoredPanel(card.transform, "HeaderDivider", new Vector2(0.08f, 0.785f), new Vector2(0.92f, 0.789f), new Color(0.20f, 0.85f, 1.0f, 0.35f));

        // 1. RESUME GAME
        CreateAnchoredButton(card.transform, font, "Btn_Resume",
            "▶   RESUME COMBAT  [ESC]",
            16, new Color(0.10f, 0.65f, 0.38f, 0.98f), Color.white,
            new Vector2(0.12f, 0.64f), new Vector2(0.88f, 0.74f), ResumeGame);

        // 2. SETTINGS (AUDIO, SENSITIVITY, DISPLAY & KEYBINDS)
        CreateAnchoredButton(card.transform, font, "Btn_Settings",
            "⚙   SETTINGS  (SENSITIVITY, AUDIO & KEYS)",
            15, new Color(0.12f, 0.44f, 0.72f, 0.98f), Color.white,
            new Vector2(0.12f, 0.51f), new Vector2(0.88f, 0.61f), OpenSettingsSubPanel);

        // 3. CHANGE OPERATIVE / RESTART MATCH
        CreateAnchoredButton(card.transform, font, "Btn_Restart",
            "↻   CHANGE OPERATIVE / RESTART MATCH",
            15, new Color(0.16f, 0.26f, 0.42f, 0.98f), new Color(0.88f, 0.95f, 1.0f),
            new Vector2(0.12f, 0.38f), new Vector2(0.88f, 0.48f), ReturnToOperativeSelection);

        // 4. RETURN TO MAIN MENU
        CreateAnchoredButton(card.transform, font, "Btn_MainMenu",
            "⌂   RETURN TO MAIN MENU",
            15, new Color(0.48f, 0.28f, 0.12f, 0.98f), new Color(1.0f, 0.92f, 0.78f),
            new Vector2(0.12f, 0.25f), new Vector2(0.88f, 0.35f), ReturnToMainMenu);

        // 5. QUIT TO DESKTOP
        CreateAnchoredButton(card.transform, font, "Btn_QuitGame",
            "✕   QUIT GAME TO DESKTOP",
            15, new Color(0.68f, 0.16f, 0.18f, 0.98f), Color.white,
            new Vector2(0.12f, 0.10f), new Vector2(0.88f, 0.20f), QuitApplication);
    }

    private void BuildSettingsSubPanel(Transform parent, Font font)
    {
        GameObject cardBorder = CreateAnchoredPanel(parent, "Pause_Settings_Border", new Vector2(0.10f, 0.08f), new Vector2(0.90f, 0.92f), new Color(0.20f, 0.85f, 1.0f, 0.9f));
        _settingsPanel = cardBorder;

        GameObject card = new GameObject("Inner");
        card.transform.SetParent(cardBorder.transform, false);
        RectTransform cRT = card.AddComponent<RectTransform>();
        cRT.anchorMin = Vector2.zero;
        cRT.anchorMax = Vector2.one;
        cRT.offsetMin = new Vector2(2, 2);
        cRT.offsetMax = new Vector2(-2, -2);
        Image cardBg = card.AddComponent<Image>();
        cardBg.color = new Color(0.045f, 0.07f, 0.115f, 0.98f);

        CreateAnchoredPanel(card.transform, "TopStripe", new Vector2(0f, 0.975f), new Vector2(1f, 1f), new Color(0.20f, 0.88f, 1.0f, 1f));

        CreateAnchoredText(card.transform, font, "Title",
            "IN-GAME SETTINGS // AIM SENSITIVITY, AUDIO, DISPLAY & KEYBINDS",
            21, FontStyle.Bold, Color.white,
            new Vector2(0.04f, 0.89f), new Vector2(0.96f, 0.965f), TextAnchor.MiddleCenter);

        CreateAnchoredPanel(card.transform, "Divider", new Vector2(0.03f, 0.875f), new Vector2(0.97f, 0.879f), new Color(0.2f, 0.85f, 1.0f, 0.35f));
        CreateAnchoredPanel(card.transform, "VerticalDivider", new Vector2(0.425f, 0.16f), new Vector2(0.428f, 0.855f), new Color(0.2f, 0.85f, 1.0f, 0.25f));

        // =====================================================================
        // LEFT COLUMN: SENSITIVITY, AUDIO, CAMERA & DISPLAY (0.04 .. 0.40)
        // =====================================================================
        CreateAnchoredText(card.transform, font, "Hdr_System",
            "AIM, AUDIO & DISPLAY",
            14, FontStyle.Bold, new Color(0.22f, 0.90f, 1.0f),
            new Vector2(0.04f, 0.80f), new Vector2(0.40f, 0.86f), TextAnchor.MiddleLeft);

        // 1. Mouse Look Sensitivity
        CreateAnchoredText(card.transform, font, "Lbl_Sensitivity",
            "MOUSE LOOK / AIM SENSITIVITY",
            12, FontStyle.Bold, new Color(0.82f, 0.90f, 0.98f),
            new Vector2(0.04f, 0.72f), new Vector2(0.31f, 0.78f), TextAnchor.MiddleLeft);

        _sensitivityValueText = CreateAnchoredText(card.transform, font, "Val_Sensitivity",
            "1.00x",
            13, FontStyle.Bold, new Color(0.22f, 0.92f, 1.0f),
            new Vector2(0.31f, 0.72f), new Vector2(0.40f, 0.78f), TextAnchor.MiddleRight);

        _sensitivitySlider = CreateStyledSlider(card.transform, "Slider_Sensitivity",
            new Vector2(0.04f, 0.66f), new Vector2(0.40f, 0.71f),
            0.2f, 3.0f, PlayerPrefs.GetFloat("OasisMouseSensitivity", 1.0f), OnSensitivityChanged);

        // 2. Master Audio Volume
        CreateAnchoredText(card.transform, font, "Lbl_Volume",
            "MASTER AUDIO VOLUME",
            12, FontStyle.Bold, new Color(0.82f, 0.90f, 0.98f),
            new Vector2(0.04f, 0.57f), new Vector2(0.31f, 0.63f), TextAnchor.MiddleLeft);

        _volumeValueText = CreateAnchoredText(card.transform, font, "Val_Volume",
            "85%",
            13, FontStyle.Bold, new Color(0.22f, 0.92f, 1.0f),
            new Vector2(0.31f, 0.57f), new Vector2(0.40f, 0.63f), TextAnchor.MiddleRight);

        _volumeSlider = CreateStyledSlider(card.transform, "Slider_Volume",
            new Vector2(0.04f, 0.51f), new Vector2(0.40f, 0.56f),
            0f, 1f, PlayerPrefs.GetFloat("OasisMasterVolume", 0.85f), OnVolumeChanged);

        // 3. Camera Perspective Toggle (First-Person vs Third-Person)
        CreateAnchoredText(card.transform, font, "Lbl_CamMode",
            "CAMERA PERSPECTIVE  [V KEY]",
            12, FontStyle.Bold, new Color(0.82f, 0.90f, 0.98f),
            new Vector2(0.04f, 0.42f), new Vector2(0.40f, 0.48f), TextAnchor.MiddleLeft);

        Button camBtn = CreateAnchoredButton(card.transform, font, "Btn_CamView",
            "FIRST-PERSON VIEW (DEFAULT)", 12, new Color(0.12f, 0.48f, 0.68f, 0.95f), Color.white,
            new Vector2(0.04f, 0.35f), new Vector2(0.40f, 0.415f), ToggleCameraPerspective);
        _cameraViewBtnBg = camBtn.GetComponent<Image>();
        _cameraViewBtnText = camBtn.GetComponentInChildren<Text>();

        // 4. Graphics Quality Preset
        CreateAnchoredText(card.transform, font, "Lbl_Quality",
            "GRAPHICS QUALITY PRESET",
            12, FontStyle.Bold, new Color(0.82f, 0.90f, 0.98f),
            new Vector2(0.04f, 0.275f), new Vector2(0.40f, 0.33f), TextAnchor.MiddleLeft);

        string[] qLabels = new string[] { "PERFORMANCE", "BALANCED", "ULTRA" };
        float[] qMinX = new float[] { 0.04f, 0.165f, 0.29f };
        float[] qMaxX = new float[] { 0.155f, 0.28f, 0.40f };
        for (int q = 0; q < 3; q++)
        {
            int tier = q;
            Button qBtn = CreateAnchoredButton(card.transform, font, $"Btn_Quality_{q}",
                qLabels[q], 11, new Color(0.08f, 0.12f, 0.18f, 0.9f), Color.white,
                new Vector2(qMinX[q], 0.21f), new Vector2(qMaxX[q], 0.27f), () => SetQualityTier(tier));
            _qualityBtnBgs[q] = qBtn.GetComponent<Image>();
            _qualityBtnTexts[q] = qBtn.GetComponentInChildren<Text>();
        }

        // 5. Fullscreen / Windowed Mode
        Button fsBtn = CreateAnchoredButton(card.transform, font, "Btn_Fullscreen",
            "FULLSCREEN MODE: ENABLED", 12, new Color(0.12f, 0.45f, 0.72f, 0.95f), Color.white,
            new Vector2(0.04f, 0.135f), new Vector2(0.40f, 0.195f), ToggleFullscreenMode);
        _fullscreenBtnBg = fsBtn.GetComponent<Image>();
        _fullscreenBtnText = fsBtn.GetComponentInChildren<Text>();

        // =====================================================================
        // RIGHT COLUMN: KEYBIND SELECTION GRID (0.45 .. 0.96)
        // =====================================================================
        CreateAnchoredText(card.transform, font, "Hdr_Keybinds",
            "KEYBINDINGS  //  CLICK ANY KEY TO REBIND",
            14, FontStyle.Bold, new Color(0.22f, 0.95f, 0.62f),
            new Vector2(0.45f, 0.80f), new Vector2(0.96f, 0.86f), TextAnchor.MiddleLeft);

        _rebindStatusText = CreateAnchoredText(card.transform, font, "Lbl_RebindStatus",
            "Select any action button below and press a key to customize controls.",
            12, FontStyle.Italic, new Color(0.68f, 0.82f, 0.94f),
            new Vector2(0.45f, 0.74f), new Vector2(0.96f, 0.79f), TextAnchor.MiddleLeft);

        _keybindBtnBgs.Clear();
        _keybindBtnTexts.Clear();

        OasisAction[] actions = OasisKeybindManager.AllActions;
        int rowsPerCol = 7;
        for (int i = 0; i < actions.Length; i++)
        {
            OasisAction act = actions[i];
            int col = i / rowsPerCol;
            int row = i % rowsPerCol;

            float colStartX = (col == 0) ? 0.45f : 0.715f;
            float colEndX = (col == 0) ? 0.695f : 0.96f;

            float rowTopY = 0.725f - (row * 0.082f);
            float rowBotY = rowTopY - 0.068f;

            CreateAnchoredPanel(card.transform, $"KeyRowBg_{act}",
                new Vector2(colStartX, rowBotY), new Vector2(colEndX, rowTopY),
                new Color(0.06f, 0.10f, 0.16f, 0.75f));

            CreateAnchoredText(card.transform, font, $"Lbl_Key_{act}",
                OasisKeybindManager.GetActionLabel(act),
                11, FontStyle.Bold, new Color(0.84f, 0.92f, 0.98f),
                new Vector2(colStartX + 0.01f, rowBotY), new Vector2(colStartX + 0.145f, rowTopY),
                TextAnchor.MiddleLeft);

            OasisAction capturedAct = act;
            Button keyBtn = CreateAnchoredButton(card.transform, font, $"Btn_Key_{act}",
                $"[ {OasisKeybindManager.FormatKeyName(OasisKeybindManager.GetKey(act))} ]",
                12, new Color(0.11f, 0.24f, 0.38f, 0.95f), new Color(0.25f, 0.92f, 1.0f),
                new Vector2(colStartX + 0.15f, rowBotY + 0.006f), new Vector2(colEndX - 0.008f, rowTopY - 0.006f),
                () => StartRebind(capturedAct));

            _keybindBtnBgs[act] = keyBtn.GetComponent<Image>();
            _keybindBtnTexts[act] = keyBtn.GetComponentInChildren<Text>();
        }

        // Bottom Bar: Reset Default Keys & Back to Pause Menu
        CreateAnchoredButton(card.transform, font, "Btn_ResetKeys",
            "↺   RESET DEFAULT KEYBINDS",
            13, new Color(0.16f, 0.24f, 0.36f, 0.95f), new Color(0.85f, 0.94f, 1.0f),
            new Vector2(0.53f, 0.04f), new Vector2(0.88f, 0.115f), ResetDefaultKeybinds);

        CreateAnchoredButton(card.transform, font, "Btn_BackPause",
            "✓   SAVE & BACK TO PAUSE MENU",
            14, new Color(0.10f, 0.65f, 0.38f, 1.0f), Color.white,
            new Vector2(0.12f, 0.04f), new Vector2(0.47f, 0.115f), CloseSettingsSubPanel);

        _settingsPanel.SetActive(false);
    }

    private void StartRebind(OasisAction action)
    {
        _awaitingRebindAction = action;
        RefreshSettingsUI();
    }

    private void AssignReboundKey(OasisAction action, KeyCode newKey)
    {
        _awaitingRebindAction = null;
        OasisKeybindManager.SetKey(action, newKey);
        RefreshSettingsUI();
        if (_rebindStatusText != null)
        {
            string actName = OasisKeybindManager.GetActionLabel(action);
            string keyName = OasisKeybindManager.FormatKeyName(newKey);
            _rebindStatusText.text = $"<color=#38EF7D><b>BOUND [{actName}]  →  [{keyName}]</b></color>";
        }
    }

    private void ResetDefaultKeybinds()
    {
        _awaitingRebindAction = null;
        OasisKeybindManager.ResetToDefaults();
        RefreshSettingsUI();
        if (_rebindStatusText != null)
        {
            _rebindStatusText.text = "<color=#38EF7D>All keybindings restored to tactical defaults.</color>";
        }
    }

    private void OnVolumeChanged(float val)
    {
        AudioListener.volume = Mathf.Clamp01(val);
        PlayerPrefs.SetFloat("OasisMasterVolume", AudioListener.volume);
        if (_volumeValueText != null)
        {
            _volumeValueText.text = $"{Mathf.RoundToInt(AudioListener.volume * 100f)}%";
        }
    }

    private void OnSensitivityChanged(float val)
    {
        float clamped = Mathf.Clamp(val, 0.2f, 3.0f);
        PlayerPrefs.SetFloat("OasisMouseSensitivity", clamped);
        var fpc = FindAnyObjectByType<StarterAssets.FirstPersonController>();
        if (fpc != null)
        {
            fpc.UserMouseSensitivity = clamped;
        }
        if (_sensitivityValueText != null)
        {
            _sensitivityValueText.text = $"{clamped:F2}x";
        }
    }

    private void ToggleCameraPerspective()
    {
        if (OasisCombatMovement.Instance != null)
        {
            OasisCombatMovement.Instance.SetThirdPersonMode(!OasisCombatMovement.Instance.isThirdPerson);
        }
        RefreshSettingsUI();
    }

    private void SetQualityTier(int tier)
    {
        _selectedQualityTier = Mathf.Clamp(tier, 0, 2);
        PlayerPrefs.SetInt("OasisGraphicsTier", _selectedQualityTier);
        int qualityCount = QualitySettings.names.Length;
        if (qualityCount > 0)
        {
            int mappedIndex = (_selectedQualityTier == 0) ? 0 : (_selectedQualityTier == 1) ? qualityCount / 2 : qualityCount - 1;
            QualitySettings.SetQualityLevel(mappedIndex, false);
        }
        RefreshSettingsUI();
    }

    private void ToggleFullscreenMode()
    {
        Screen.fullScreen = !Screen.fullScreen;
        RefreshSettingsUI();
    }

    private void RefreshSettingsUI()
    {
        float vol = PlayerPrefs.GetFloat("OasisMasterVolume", 0.85f);
        if (_volumeSlider != null) _volumeSlider.SetValueWithoutNotify(vol);
        if (_volumeValueText != null) _volumeValueText.text = $"{Mathf.RoundToInt(vol * 100f)}%";

        float sens = PlayerPrefs.GetFloat("OasisMouseSensitivity", 1.0f);
        if (_sensitivitySlider != null) _sensitivitySlider.SetValueWithoutNotify(sens);
        if (_sensitivityValueText != null) _sensitivityValueText.text = $"{sens:F2}x";

        bool is3rd = OasisCombatMovement.Instance != null && OasisCombatMovement.Instance.isThirdPerson;
        if (_cameraViewBtnBg != null)
        {
            _cameraViewBtnBg.color = is3rd
                ? new Color(0.14f, 0.42f, 0.68f, 0.95f)
                : new Color(0.10f, 0.56f, 0.42f, 0.95f);
        }
        if (_cameraViewBtnText != null)
        {
            _cameraViewBtnText.text = is3rd ? "● THIRD-PERSON OVER-SHOULDER [V]" : "● FIRST-PERSON FPS VIEW [V]";
        }

        string[] qNames = new string[] { "PERFORMANCE", "BALANCED", "ULTRA" };
        for (int q = 0; q < 3; q++)
        {
            bool isSel = (_selectedQualityTier == q);
            if (_qualityBtnBgs[q] != null)
            {
                _qualityBtnBgs[q].color = isSel
                    ? new Color(0.12f, 0.52f, 0.42f, 0.98f)
                    : new Color(0.08f, 0.12f, 0.18f, 0.85f);
            }
            if (_qualityBtnTexts[q] != null)
            {
                _qualityBtnTexts[q].text = isSel ? $"● {qNames[q]}" : qNames[q];
                _qualityBtnTexts[q].color = isSel ? new Color(0.25f, 0.98f, 0.65f) : new Color(0.75f, 0.84f, 0.92f);
            }
        }

        bool isFs = Screen.fullScreen;
        if (_fullscreenBtnBg != null)
        {
            _fullscreenBtnBg.color = isFs
                ? new Color(0.12f, 0.45f, 0.72f, 0.95f)
                : new Color(0.14f, 0.20f, 0.28f, 0.95f);
        }
        if (_fullscreenBtnText != null)
        {
            _fullscreenBtnText.text = isFs ? "● FULLSCREEN MODE: ENABLED" : "WINDOWED MODE: ENABLED";
        }

        foreach (OasisAction act in OasisKeybindManager.AllActions)
        {
            bool isListening = (_awaitingRebindAction.HasValue && _awaitingRebindAction.Value == act);
            if (_keybindBtnBgs.TryGetValue(act, out Image bg) && bg != null)
            {
                bg.color = isListening
                    ? new Color(0.85f, 0.48f, 0.08f, 0.98f)
                    : new Color(0.11f, 0.24f, 0.38f, 0.95f);
            }
            if (_keybindBtnTexts.TryGetValue(act, out Text txt) && txt != null)
            {
                if (isListening)
                {
                    txt.text = "[ PRESS KEY... ]";
                    txt.color = Color.white;
                }
                else
                {
                    KeyCode currentKey = OasisKeybindManager.GetKey(act);
                    bool isCustom = currentKey != OasisKeybindManager.GetDefaultKey(act);
                    txt.text = $"[ {OasisKeybindManager.FormatKeyName(currentKey)} ]";
                    txt.color = isCustom ? new Color(0.28f, 0.98f, 0.62f) : new Color(0.25f, 0.92f, 1.0f);
                }
            }
        }

        if (_rebindStatusText != null)
        {
            if (_awaitingRebindAction.HasValue)
            {
                string label = OasisKeybindManager.GetActionLabel(_awaitingRebindAction.Value);
                _rebindStatusText.text = $"<color=#FFBD2E><b>REBINDING [{label}]:</b> Press any key to assign, or [ESC] to cancel.</color>";
            }
            else
            {
                _rebindStatusText.text = "Click any action button below and press a key to customize controls.";
            }
        }
    }

    // =========================================================================
    // UI HELPER METHODS
    // =========================================================================

    private GameObject CreateAnchoredPanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        Image img = obj.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return obj;
    }

    private Text CreateAnchoredText(Transform parent, Font font, string name, string content, int fontSize, FontStyle style, Color color, Vector2 anchorMin, Vector2 anchorMax, TextAnchor align)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        Text txt = obj.AddComponent<Text>();
        txt.font = font;
        txt.text = content;
        txt.fontSize = fontSize;
        txt.fontStyle = style;
        txt.color = color;
        txt.alignment = align;
        txt.supportRichText = true;
        txt.raycastTarget = false;
        return txt;
    }

    private Button CreateAnchoredButton(Transform parent, Font font, string name, string label, int fontSize, Color bgColor, Color textColor, Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction action)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);
        RectTransform rt = btnObj.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image img = btnObj.AddComponent<Image>();
        img.color = bgColor;
        img.raycastTarget = true;

        Button btn = btnObj.AddComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
        cb.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        btn.colors = cb;
        btn.targetGraphic = img;
        if (action != null) btn.onClick.AddListener(action);

        CreateAnchoredText(btnObj.transform, font, "Text", label, fontSize, FontStyle.Bold, textColor, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter);
        return btn;
    }

    private Slider CreateStyledSlider(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, float minVal, float maxVal, float initialVal, UnityEngine.Events.UnityAction<float> onValueChanged)
    {
        GameObject sliderObj = new GameObject(name);
        sliderObj.transform.SetParent(parent, false);
        RectTransform sRT = sliderObj.AddComponent<RectTransform>();
        sRT.anchorMin = anchorMin;
        sRT.anchorMax = anchorMax;
        sRT.offsetMin = Vector2.zero;
        sRT.offsetMax = Vector2.zero;

        GameObject bgTrack = CreateAnchoredPanel(sliderObj.transform, "Background", new Vector2(0f, 0.25f), new Vector2(1f, 0.75f), new Color(0.08f, 0.12f, 0.18f, 1f));
        bgTrack.GetComponent<Image>().raycastTarget = true;

        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderObj.transform, false);
        RectTransform faRT = fillArea.AddComponent<RectTransform>();
        faRT.anchorMin = new Vector2(0f, 0.25f);
        faRT.anchorMax = new Vector2(1f, 0.75f);
        faRT.offsetMin = Vector2.zero;
        faRT.offsetMax = Vector2.zero;

        GameObject fill = CreateAnchoredPanel(fillArea.transform, "Fill", Vector2.zero, Vector2.one, new Color(0.15f, 0.82f, 0.98f, 1f));

        Slider slider = sliderObj.AddComponent<Slider>();
        slider.fillRect = fill.GetComponent<RectTransform>();
        slider.targetGraphic = bgTrack.GetComponent<Image>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = minVal;
        slider.maxValue = maxVal;
        slider.value = initialVal;
        if (onValueChanged != null) slider.onValueChanged.AddListener(onValueChanged);

        return slider;
    }
}
