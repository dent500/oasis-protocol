using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

public enum OasisAction
{
    MoveForward = 0,
    MoveBack = 1,
    MoveLeft = 2,
    MoveRight = 3,
    Jump = 4,
    Sprint = 5,
    Crouch = 6,
    Dash = 7,
    Reload = 8,
    Revive = 9,
    Map = 10,
    Scoreboard = 11,
    Ability = 12
}

/// <summary>
/// Manages persistent custom keybindings stored in PlayerPrefs and provides
/// cross-input-backend polling (Unity Input System Keyboard.current + Legacy Input).
/// </summary>
public static class OasisKeybindManager
{
    private static readonly Dictionary<OasisAction, KeyCode> _cachedKeys = new Dictionary<OasisAction, KeyCode>();
    private static bool _initialized = false;

    public static readonly OasisAction[] AllActions = (OasisAction[])Enum.GetValues(typeof(OasisAction));

    public static KeyCode GetDefaultKey(OasisAction action)
    {
        switch (action)
        {
            case OasisAction.MoveForward: return KeyCode.W;
            case OasisAction.MoveBack: return KeyCode.S;
            case OasisAction.MoveLeft: return KeyCode.A;
            case OasisAction.MoveRight: return KeyCode.D;
            case OasisAction.Jump: return KeyCode.Space;
            case OasisAction.Sprint: return KeyCode.LeftShift;
            case OasisAction.Crouch: return KeyCode.C;
            case OasisAction.Dash: return KeyCode.Q;
            case OasisAction.Reload: return KeyCode.R;
            case OasisAction.Revive: return KeyCode.E;
            case OasisAction.Map: return KeyCode.M;
            case OasisAction.Scoreboard: return KeyCode.Tab;
            case OasisAction.Ability: return KeyCode.F;
            default: return KeyCode.None;
        }
    }

    public static string GetActionLabel(OasisAction action)
    {
        switch (action)
        {
            case OasisAction.MoveForward: return "MOVE FORWARD";
            case OasisAction.MoveBack: return "MOVE BACKWARD";
            case OasisAction.MoveLeft: return "STRAFE LEFT";
            case OasisAction.MoveRight: return "STRAFE RIGHT";
            case OasisAction.Jump: return "JUMP / VAULT";
            case OasisAction.Sprint: return "SPRINT";
            case OasisAction.Crouch: return "CROUCH / SLIDE";
            case OasisAction.Dash: return "TACTICAL DASH";
            case OasisAction.Reload: return "RELOAD BLASTER";
            case OasisAction.Revive: return "REVIVE ALLY";
            case OasisAction.Map: return "TACTICAL MAP";
            case OasisAction.Scoreboard: return "SCOREBOARD";
            case OasisAction.Ability: return "SPECIAL ABILITY";
            default: return action.ToString().ToUpperInvariant();
        }
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        foreach (OasisAction act in AllActions)
        {
            int saved = PlayerPrefs.GetInt("OasisKey_" + act, (int)GetDefaultKey(act));
            _cachedKeys[act] = (KeyCode)saved;
        }
    }

    public static KeyCode GetKey(OasisAction action)
    {
        EnsureInitialized();
        if (_cachedKeys.TryGetValue(action, out KeyCode code))
        {
            return code;
        }
        return GetDefaultKey(action);
    }

    public static void SetKey(OasisAction action, KeyCode newKey)
    {
        EnsureInitialized();
        KeyCode previousKey = GetKey(action);

        // If another action already uses newKey, swap it to previousKey so bindings stay conflict-free
        foreach (OasisAction other in AllActions)
        {
            if (other != action && GetKey(other) == newKey)
            {
                _cachedKeys[other] = previousKey;
                PlayerPrefs.SetInt("OasisKey_" + other, (int)previousKey);
            }
        }

        _cachedKeys[action] = newKey;
        PlayerPrefs.SetInt("OasisKey_" + action, (int)newKey);
        PlayerPrefs.Save();
    }

    public static void ResetToDefaults()
    {
        _initialized = true;
        foreach (OasisAction act in AllActions)
        {
            KeyCode def = GetDefaultKey(act);
            _cachedKeys[act] = def;
            PlayerPrefs.SetInt("OasisKey_" + act, (int)def);
        }
        PlayerPrefs.Save();
    }

    public static string FormatKeyName(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.LeftShift: return "L-SHIFT";
            case KeyCode.RightShift: return "R-SHIFT";
            case KeyCode.LeftControl: return "L-CTRL";
            case KeyCode.RightControl: return "R-CTRL";
            case KeyCode.LeftAlt: return "L-ALT";
            case KeyCode.RightAlt: return "R-ALT";
            case KeyCode.Space: return "SPACE";
            case KeyCode.Return:
            case KeyCode.KeypadEnter: return "ENTER";
            case KeyCode.Backspace: return "BACKSPC";
            case KeyCode.CapsLock: return "CAPS";
            case KeyCode.Tab: return "TAB";
            case KeyCode.UpArrow: return "UP";
            case KeyCode.DownArrow: return "DOWN";
            case KeyCode.LeftArrow: return "LEFT";
            case KeyCode.RightArrow: return "RIGHT";
            case KeyCode.Alpha0: return "0";
            case KeyCode.Alpha1: return "1";
            case KeyCode.Alpha2: return "2";
            case KeyCode.Alpha3: return "3";
            case KeyCode.Alpha4: return "4";
            case KeyCode.Alpha5: return "5";
            case KeyCode.Alpha6: return "6";
            case KeyCode.Alpha7: return "7";
            case KeyCode.Alpha8: return "8";
            case KeyCode.Alpha9: return "9";
            default: return key.ToString().ToUpperInvariant();
        }
    }

    public static bool IsPressed(OasisAction action)
    {
        return IsKeyCodePressed(GetKey(action));
    }

    public static bool WasPressedThisFrame(OasisAction action)
    {
        return WasKeyCodePressedThisFrame(GetKey(action));
    }

    public static bool WasReleasedThisFrame(OasisAction action)
    {
        return WasKeyCodeReleasedThisFrame(GetKey(action));
    }

    public static bool IsKeyCodePressed(KeyCode keyCode)
    {
        if (keyCode == KeyCode.None) return false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && TryGetKeyControl(Keyboard.current, keyCode, out KeyControl ctrl) && ctrl != null)
        {
            return ctrl.isPressed;
        }
#else
        return Input.GetKey(keyCode);
#endif
        return false;
    }

    public static bool WasKeyCodePressedThisFrame(KeyCode keyCode)
    {
        if (keyCode == KeyCode.None) return false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && TryGetKeyControl(Keyboard.current, keyCode, out KeyControl ctrl) && ctrl != null)
        {
            return ctrl.wasPressedThisFrame;
        }
#else
        return Input.GetKeyDown(keyCode);
#endif
        return false;
    }

    public static bool WasKeyCodeReleasedThisFrame(KeyCode keyCode)
    {
        if (keyCode == KeyCode.None) return false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && TryGetKeyControl(Keyboard.current, keyCode, out KeyControl ctrl) && ctrl != null)
        {
            return ctrl.wasReleasedThisFrame;
        }
#else
        return Input.GetKeyUp(keyCode);
#endif
        return false;
    }

#if ENABLE_INPUT_SYSTEM
    public static bool TryGetKeyControl(Keyboard kb, KeyCode keyCode, out KeyControl control)
    {
        control = null;
        if (kb == null) return false;

        if (keyCode >= KeyCode.A && keyCode <= KeyCode.Z)
        {
            Key k = (Key)((int)Key.A + ((int)keyCode - (int)KeyCode.A));
            control = kb[k];
            return control != null;
        }
        if (keyCode >= KeyCode.Alpha1 && keyCode <= KeyCode.Alpha9)
        {
            Key k = (Key)((int)Key.Digit1 + ((int)keyCode - (int)KeyCode.Alpha1));
            control = kb[k];
            return control != null;
        }
        if (keyCode == KeyCode.Alpha0) { control = kb.digit0Key; return true; }
        if (keyCode >= KeyCode.F1 && keyCode <= KeyCode.F12)
        {
            Key k = (Key)((int)Key.F1 + ((int)keyCode - (int)KeyCode.F1));
            control = kb[k];
            return control != null;
        }

        switch (keyCode)
        {
            case KeyCode.W: control = kb.wKey; return true;
            case KeyCode.A: control = kb.aKey; return true;
            case KeyCode.S: control = kb.sKey; return true;
            case KeyCode.D: control = kb.dKey; return true;
            case KeyCode.F: control = kb.fKey; return true;
            case KeyCode.Q: control = kb.qKey; return true;
            case KeyCode.E: control = kb.eKey; return true;
            case KeyCode.R: control = kb.rKey; return true;
            case KeyCode.C: control = kb.cKey; return true;
            case KeyCode.M: control = kb.mKey; return true;
            case KeyCode.Space: control = kb.spaceKey; return true;
            case KeyCode.LeftShift: control = kb.leftShiftKey; return true;
            case KeyCode.RightShift: control = kb.rightShiftKey; return true;
            case KeyCode.LeftControl: control = kb.leftCtrlKey; return true;
            case KeyCode.RightControl: control = kb.rightCtrlKey; return true;
            case KeyCode.LeftAlt: control = kb.leftAltKey; return true;
            case KeyCode.RightAlt: control = kb.rightAltKey; return true;
            case KeyCode.Tab: control = kb.tabKey; return true;
            case KeyCode.CapsLock: control = kb.capsLockKey; return true;
            case KeyCode.Backspace: control = kb.backspaceKey; return true;
            case KeyCode.Return: control = kb.enterKey; return true;
            case KeyCode.KeypadEnter: control = kb.numpadEnterKey; return true;
            case KeyCode.UpArrow: control = kb.upArrowKey; return true;
            case KeyCode.DownArrow: control = kb.downArrowKey; return true;
            case KeyCode.LeftArrow: control = kb.leftArrowKey; return true;
            case KeyCode.RightArrow: control = kb.rightArrowKey; return true;
            case KeyCode.BackQuote: control = kb.backquoteKey; return true;
            case KeyCode.Minus: control = kb.minusKey; return true;
            case KeyCode.Equals: control = kb.equalsKey; return true;
            case KeyCode.LeftBracket: control = kb.leftBracketKey; return true;
            case KeyCode.RightBracket: control = kb.rightBracketKey; return true;
            case KeyCode.Semicolon: control = kb.semicolonKey; return true;
            case KeyCode.Quote: control = kb.quoteKey; return true;
            case KeyCode.Comma: control = kb.commaKey; return true;
            case KeyCode.Period: control = kb.periodKey; return true;
            case KeyCode.Slash: control = kb.slashKey; return true;
            case KeyCode.Backslash: control = kb.backslashKey; return true;
            default: return false;
        }
    }

    public static bool TryDetectAnyPressedKeyCode(out KeyCode detected)
    {
        detected = KeyCode.None;
        if (Keyboard.current == null) return false;

        // Check all bindable KeyCodes against Keyboard.current
        for (KeyCode kc = KeyCode.A; kc <= KeyCode.Z; kc++)
        {
            if (WasKeyCodePressedThisFrame(kc)) { detected = kc; return true; }
        }
        for (KeyCode kc = KeyCode.Alpha0; kc <= KeyCode.Alpha9; kc++)
        {
            if (WasKeyCodePressedThisFrame(kc)) { detected = kc; return true; }
        }
        for (KeyCode kc = KeyCode.F1; kc <= KeyCode.F12; kc++)
        {
            if (WasKeyCodePressedThisFrame(kc)) { detected = kc; return true; }
        }

        KeyCode[] specials = new KeyCode[]
        {
            KeyCode.Space, KeyCode.LeftShift, KeyCode.RightShift,
            KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftAlt, KeyCode.RightAlt,
            KeyCode.Tab, KeyCode.CapsLock, KeyCode.Backspace, KeyCode.Return,
            KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
            KeyCode.BackQuote, KeyCode.Minus, KeyCode.Equals, KeyCode.LeftBracket,
            KeyCode.RightBracket, KeyCode.Semicolon, KeyCode.Quote, KeyCode.Comma,
            KeyCode.Period, KeyCode.Slash, KeyCode.Backslash
        };
        for (int i = 0; i < specials.Length; i++)
        {
            if (WasKeyCodePressedThisFrame(specials[i]))
            {
                detected = specials[i];
                return true;
            }
        }
        return false;
    }
#endif
}

/// <summary>
/// Controls the Home Main Menu for Oasis Protocol:
/// - Styled title header & faded sci-fi canyon oasis background artwork
/// - Streamlined Host / Deploy card & Join Session card (match format & squad side are configured on the Character Selection screen)
/// - Interactive Settings Modal (Master Volume, Mouse Sensitivity, Graphics Quality, Fullscreen, and Custom Keybind Selection)
/// - Interactive Credits Modal
/// </summary>
public class MainMenuManager : MonoBehaviour
{
    [Header("Scene Names")]
    public string oasisSceneName = "OasisScene";
    public string gardenSceneName = "GardenScene";
    public string cockpitSceneName = "CockpitScene";

    [Header("UI Panels")]
    public GameObject mainPanel;
    public GameObject loadingPanel;
    public Slider loadingProgressBar;
    public GameObject settingsModal;
    public GameObject creditsModal;

    [Header("Co-Op Lobby UI Elements")]
    public Text coOpStatusText;
    public Button launchMissionBtn;
    public Button generateCodeBtn;
    public Button joinSessionBtn;
    public InputField joinInputField;

    [Header("Legacy Match Settings (Configured in Character Selection)")]
    public string selectedMatchFormat = "2v2";
    public int selectedTeam = 0;

    [Header("Legacy Match Config Buttons")]
    public Button btn2v2;
    public Button btn3v3;
    public Button btn4v4;
    public Button btnAlpha;
    public Button btnBravo;

    // Settings UI state
    private Slider _volumeSlider;
    private Text _volumeValueText;
    private Slider _sensitivitySlider;
    private Text _sensitivityValueText;
    private Text _fullscreenBtnText;
    private Image _fullscreenBtnBg;
    private readonly Image[] _qualityBtnBgs = new Image[3];
    private readonly Text[] _qualityBtnTexts = new Text[3];
    private int _selectedQualityTier = 2; // 0 = Low, 1 = Medium, 2 = High/Ultra

    // Keybind UI state
    private readonly Dictionary<OasisAction, Image> _keybindBtnBgs = new Dictionary<OasisAction, Image>();
    private readonly Dictionary<OasisAction, Text> _keybindBtnTexts = new Dictionary<OasisAction, Text>();
    private OasisAction? _awaitingRebindAction = null;
    private Text _rebindStatusBannerText;
    private Text _controlsHintText;

    private RawImage _bgArtRawImage;
    private Texture2D _proceduralBgTexture;
    private bool _isLoading = false;
    private string _lastCopiedCode = "";

    private void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 1f;

        // Apply saved user settings on startup
        float savedVol = PlayerPrefs.GetFloat("OasisMasterVolume", 0.85f);
        AudioListener.volume = Mathf.Clamp01(savedVol);
        _selectedQualityTier = PlayerPrefs.GetInt("OasisGraphicsTier", 2);

        if (loadingPanel != null)
        {
            loadingPanel.SetActive(false);
        }

        BuildOrUpgradeHomeMenuUI();
        UpdateLobbyStatusDisplay();
    }

    private void OnDestroy()
    {
        if (_proceduralBgTexture != null)
        {
            Destroy(_proceduralBgTexture);
            _proceduralBgTexture = null;
        }
    }

    // Legacy compatibility methods kept so existing serialized scene events never break
    public void SetMatchFormat(string format)
    {
        selectedMatchFormat = format;
        PlayerPrefs.SetString("OasisMatchFormat", selectedMatchFormat);
        PlayerPrefs.Save();
    }

    public void SetPlayerTeam(int team)
    {
        selectedTeam = team;
        PlayerPrefs.SetInt("OasisPlayerTeam", selectedTeam);
        PlayerPrefs.Save();
    }

    public void SelectFormat2v2() => SetMatchFormat("2v2");
    public void SelectFormat3v3() => SetMatchFormat("3v3");
    public void SelectFormat4v4() => SetMatchFormat("4v4");
    public void SelectTeamAlpha() => SetPlayerTeam(0);
    public void SelectTeamBravo() => SetPlayerTeam(1);
    public void RefreshMatchConfigUI() { }

    public void JoinFromInputField()
    {
        if (joinInputField != null)
        {
            JoinOasisSession(joinInputField.text);
        }
    }

    private void OnInputFieldSubmitted(string text)
    {
        if (!string.IsNullOrEmpty(text) && text.Trim().Length >= 4)
        {
            JoinOasisSession(text);
        }
    }

    // =========================================================================
    // COMPLETE MODERN HOME PAGE UI BUILDER
    // =========================================================================

    public void BuildOrUpgradeHomeMenuUI()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        // Clear old child objects under this Canvas to ensure a 100% clean, modern layout
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (loadingPanel != null && child.gameObject == loadingPanel) continue;
            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }

        // 1. Deep Space / Canyon Base Backdrop
        GameObject baseBgObj = new GameObject("Background_Base");
        baseBgObj.transform.SetParent(transform, false);
        RectTransform baseBgRT = baseBgObj.AddComponent<RectTransform>();
        baseBgRT.anchorMin = Vector2.zero;
        baseBgRT.anchorMax = Vector2.one;
        baseBgRT.offsetMin = Vector2.zero;
        baseBgRT.offsetMax = Vector2.zero;
        Image baseBgImg = baseBgObj.AddComponent<Image>();
        baseBgImg.color = new Color(0.025f, 0.04f, 0.065f, 1f);
        baseBgImg.raycastTarget = false;

        // 2. Faded Background Artwork Image (Loads OasisMenuBackground from Resources or generates rich procedural horizon)
        GameObject artBgObj = new GameObject("Background_FadedArtwork");
        artBgObj.transform.SetParent(transform, false);
        RectTransform artBgRT = artBgObj.AddComponent<RectTransform>();
        artBgRT.anchorMin = Vector2.zero;
        artBgRT.anchorMax = Vector2.one;
        artBgRT.offsetMin = Vector2.zero;
        artBgRT.offsetMax = Vector2.zero;
        _bgArtRawImage = artBgObj.AddComponent<RawImage>();
        _bgArtRawImage.raycastTarget = false;

        Texture2D bgTex = Resources.Load<Texture2D>("OasisMenuBackground");
        if (bgTex != null)
        {
            _bgArtRawImage.texture = bgTex;
            _bgArtRawImage.color = new Color(1.0f, 1.0f, 1.0f, 0.92f); // Crisp, vibrant background artwork
        }
        else
        {
            _proceduralBgTexture = GenerateProceduralOasisBackdrop(640, 360);
            _bgArtRawImage.texture = _proceduralBgTexture;
            _bgArtRawImage.color = new Color(1f, 1f, 1f, 0.92f);
        }

        // Subtle Bottom Footer Backing Strip
        CreateAnchoredPanel(transform, "Bottom_Vignette", new Vector2(0f, 0f), new Vector2(1f, 0.115f), new Color(0.01f, 0.02f, 0.04f, 0.45f));

        // 3. Styled Title Header Banner
        BuildStyledHeader(transform, font);

        // 4. Center Container: Simplified & Tidied Host + Join Cards
        GameObject cardsContainer = new GameObject("CardsContainer");
        cardsContainer.transform.SetParent(transform, false);
        RectTransform cardsRT = cardsContainer.AddComponent<RectTransform>();
        cardsRT.anchorMin = new Vector2(0.08f, 0.16f);
        cardsRT.anchorMax = new Vector2(0.92f, 0.73f);
        cardsRT.offsetMin = Vector2.zero;
        cardsRT.offsetMax = Vector2.zero;

        BuildHostCard(cardsContainer.transform, font);
        BuildJoinCard(cardsContainer.transform, font);

        // 5. Bottom Navigation & Utility Bar (Controls hint + Settings, Credits, Quit buttons)
        BuildFooterBar(transform, font);

        // 6. Build Hidden Modals (Settings & Credits)
        BuildSettingsModal(transform, font);
        BuildCreditsModal(transform, font);
    }

    private void BuildStyledHeader(Transform parent, Font font)
    {
        GameObject headerObj = new GameObject("Styled_Header");
        headerObj.transform.SetParent(parent, false);
        RectTransform hRT = headerObj.AddComponent<RectTransform>();
        hRT.anchorMin = new Vector2(0.15f, 0.75f);
        hRT.anchorMax = new Vector2(0.85f, 0.96f);
        hRT.offsetMin = Vector2.zero;
        hRT.offsetMax = Vector2.zero;

        // Low-opacity dark glass header backing plate (22% alpha so background shines through)
        CreateAnchoredPanel(headerObj.transform, "Header_BackdropPlate", new Vector2(0.12f, 0.08f), new Vector2(0.88f, 0.95f), new Color(0.015f, 0.03f, 0.06f, 0.22f));

        // Glowing top & bottom cyan trim lines on the header plate
        CreateAnchoredPanel(headerObj.transform, "Top_Trim", new Vector2(0.22f, 0.94f), new Vector2(0.78f, 0.96f), new Color(0.15f, 0.88f, 1.0f, 0.85f));
        CreateAnchoredPanel(headerObj.transform, "Bottom_Trim", new Vector2(0.12f, 0.08f), new Vector2(0.88f, 0.095f), new Color(0.15f, 0.88f, 1.0f, 0.50f));

        // Left & Right decorative accent ticks
        CreateAnchoredPanel(headerObj.transform, "Left_Accent", new Vector2(0.12f, 0.08f), new Vector2(0.124f, 0.95f), new Color(0.15f, 0.88f, 1.0f, 0.85f));
        CreateAnchoredPanel(headerObj.transform, "Right_Accent", new Vector2(0.876f, 0.08f), new Vector2(0.88f, 0.95f), new Color(0.15f, 0.88f, 1.0f, 0.85f));

        // Small classification overline
        CreateAnchoredText(headerObj.transform, font, "OVERLINE",
            "◆   OASIS COMMAND NETWORK   //   SECTOR 07 TACTICAL UPLINK   ◆",
            12, FontStyle.Bold, new Color(0.38f, 0.88f, 1.0f, 0.95f),
            new Vector2(0.14f, 0.74f), new Vector2(0.86f, 0.91f), TextAnchor.MiddleCenter);

        // Main Title Drop Shadow / Glow Layer
        CreateAnchoredText(headerObj.transform, font, "Title_GlowShadow",
            "O A S I S   P R O T O C O L",
            48, FontStyle.Bold, new Color(0.05f, 0.65f, 0.95f, 0.45f),
            new Vector2(0.102f, 0.27f), new Vector2(0.902f, 0.75f), TextAnchor.MiddleCenter);

        // Main Title Crisp Foreground
        CreateAnchoredText(headerObj.transform, font, "Title_Main",
            "O A S I S   P R O T O C O L",
            48, FontStyle.Bold, new Color(0.96f, 0.98f, 1.0f, 1.0f),
            new Vector2(0.10f, 0.29f), new Vector2(0.90f, 0.77f), TextAnchor.MiddleCenter);

        // Subtitle Banner Pill
        GameObject subPill = CreateAnchoredPanel(headerObj.transform, "Subtitle_Pill", new Vector2(0.24f, 0.12f), new Vector2(0.76f, 0.29f), new Color(0.02f, 0.05f, 0.10f, 0.35f));
        CreateAnchoredText(subPill.transform, font, "Subtitle_Text",
            "TACTICAL SCI-FI SQUAD COMBAT   •   SOLO, CO-OP & MULTI-TEAM AI ARENA",
            13, FontStyle.Bold, new Color(0.22f, 0.95f, 0.68f, 1f),
            Vector2.zero, Vector2.one, TextAnchor.MiddleCenter);
    }

    private void BuildHostCard(Transform parent, Font font)
    {
        // Left Card: Low-opacity dark glass panel (24% alpha so background image is clearly visible through it)
        GameObject card = CreateAnchoredPanel(parent, "DEPLOY SQUAD_Card", new Vector2(0.02f, 0.02f), new Vector2(0.48f, 0.98f), new Color(0.015f, 0.03f, 0.06f, 0.24f));

        // Thin 1px Cyan Edge Borders & Top Stripe (instead of Outline component which floods the fill)
        CreateAnchoredPanel(card.transform, "TopStripe", new Vector2(0f, 0.982f), new Vector2(1f, 1f), new Color(0.15f, 0.88f, 1.0f, 0.90f));
        CreateAnchoredPanel(card.transform, "BottomBorder", new Vector2(0f, 0f), new Vector2(1f, 0.005f), new Color(0.15f, 0.88f, 1.0f, 0.45f));
        CreateAnchoredPanel(card.transform, "LeftBorder", new Vector2(0f, 0f), new Vector2(0.004f, 1f), new Color(0.15f, 0.88f, 1.0f, 0.45f));
        CreateAnchoredPanel(card.transform, "RightBorder", new Vector2(0.996f, 0f), new Vector2(1f, 1f), new Color(0.15f, 0.88f, 1.0f, 0.45f));

        // Category Tag
        CreateAnchoredText(card.transform, font, "Tag",
            "MISSION COMMAND // SOLO OR HOST LOBBY",
            13, FontStyle.Bold, new Color(0.25f, 0.92f, 1.0f),
            new Vector2(0.06f, 0.87f), new Vector2(0.94f, 0.95f), TextAnchor.MiddleCenter);

        // Card Title
        CreateAnchoredText(card.transform, font, "Title",
            "HOST / DEPLOY MISSION",
            30, FontStyle.Bold, Color.white,
            new Vector2(0.06f, 0.75f), new Vector2(0.94f, 0.87f), TextAnchor.MiddleCenter);

        // Divider
        CreateAnchoredPanel(card.transform, "Divider", new Vector2(0.08f, 0.72f), new Vector2(0.92f, 0.725f), new Color(0.20f, 0.88f, 1.0f, 0.40f));

        // Clean Description
        CreateAnchoredText(card.transform, font, "Desc",
            "Host a tactical mission session and enter the Operative Staging Bay.\n\nSelect your combat chassis, configure 2–4 Teams & Squad Size (1–4 per team), or run an AI Simulation.\n\nA 6-digit Unity Relay Join Code is automatically generated and copied to your clipboard so friends can join your squad seamlessly!",
            14, FontStyle.Normal, new Color(0.90f, 0.95f, 1.0f),
            new Vector2(0.07f, 0.35f), new Vector2(0.93f, 0.68f), TextAnchor.MiddleCenter);

        // Primary Single Host Button: Launches straight into Character Selection & begins Relay host session!
        launchMissionBtn = CreateAnchoredButton(card.transform, font, "HostMissionBtn",
            "HOST MISSION  (ENTER STAGING)",
            18, new Color(0.10f, 0.62f, 0.36f, 0.85f), Color.white,
            new Vector2(0.08f, 0.12f), new Vector2(0.92f, 0.26f), HostMissionFromMenu);
        generateCodeBtn = launchMissionBtn;
    }

    private void BuildJoinCard(Transform parent, Font font)
    {
        // Right Card: Low-opacity dark glass panel (24% alpha so background image is clearly visible through it)
        GameObject card = CreateAnchoredPanel(parent, "JOIN SQUAD_Card", new Vector2(0.52f, 0.02f), new Vector2(0.98f, 0.98f), new Color(0.015f, 0.03f, 0.06f, 0.24f));

        // Thin 1px Emerald Edge Borders & Top Stripe (instead of Outline component which floods the fill)
        CreateAnchoredPanel(card.transform, "TopStripe", new Vector2(0f, 0.982f), new Vector2(1f, 1f), new Color(0.22f, 0.92f, 0.58f, 0.90f));
        CreateAnchoredPanel(card.transform, "BottomBorder", new Vector2(0f, 0f), new Vector2(1f, 0.005f), new Color(0.22f, 0.92f, 0.58f, 0.45f));
        CreateAnchoredPanel(card.transform, "LeftBorder", new Vector2(0f, 0f), new Vector2(0.004f, 1f), new Color(0.22f, 0.92f, 0.58f, 0.45f));
        CreateAnchoredPanel(card.transform, "RightBorder", new Vector2(0.996f, 0f), new Vector2(1f, 1f), new Color(0.22f, 0.92f, 0.58f, 0.45f));

        // Category Tag
        CreateAnchoredText(card.transform, font, "Tag",
            "MULTIPLAYER UPLINK // RELAY & LAN CLIENT",
            13, FontStyle.Bold, new Color(0.28f, 0.95f, 0.62f),
            new Vector2(0.06f, 0.87f), new Vector2(0.94f, 0.95f), TextAnchor.MiddleCenter);

        // Card Title
        CreateAnchoredText(card.transform, font, "Title",
            "JOIN SQUAD SESSION",
            30, FontStyle.Bold, Color.white,
            new Vector2(0.06f, 0.75f), new Vector2(0.94f, 0.87f), TextAnchor.MiddleCenter);

        // Divider
        CreateAnchoredPanel(card.transform, "Divider", new Vector2(0.08f, 0.72f), new Vector2(0.92f, 0.725f), new Color(0.22f, 0.92f, 0.58f, 0.35f));

        // Clean Description
        CreateAnchoredText(card.transform, font, "Desc",
            "Enter a 6-digit Unity Relay Join Code shared by your squad host,\nor enter a local LAN IP address to link directly into their mission.\n\nNo router port-forwarding required. Unfilled slots are backed by AI operatives.",
            14, FontStyle.Normal, new Color(0.90f, 0.95f, 1.0f),
            new Vector2(0.07f, 0.48f), new Vector2(0.93f, 0.70f), TextAnchor.MiddleCenter);

        // Join Code / IP Input Field (Translucent dark glass input box without full-quad Outline)
        GameObject inputObj = CreateAnchoredPanel(card.transform, "JoinInputField", new Vector2(0.08f, 0.31f), new Vector2(0.92f, 0.44f), new Color(0.015f, 0.035f, 0.07f, 0.48f));
        inputObj.GetComponent<Image>().raycastTarget = true;
        CreateAnchoredPanel(inputObj.transform, "InputTopBorder", new Vector2(0f, 0.96f), new Vector2(1f, 1f), new Color(0.22f, 0.85f, 0.98f, 0.55f));
        CreateAnchoredPanel(inputObj.transform, "InputBottomBorder", new Vector2(0f, 0f), new Vector2(1f, 0.04f), new Color(0.22f, 0.85f, 0.98f, 0.55f));

        joinInputField = inputObj.AddComponent<InputField>();
        Text placeholderTxt = CreateAnchoredText(inputObj.transform, font, "Placeholder",
            "ENTER 6-DIGIT RELAY CODE OR LAN IP...",
            15, FontStyle.Italic, new Color(0.75f, 0.88f, 0.98f, 0.65f),
            new Vector2(0.04f, 0f), new Vector2(0.96f, 1f), TextAnchor.MiddleCenter);
        Text inputTxt = CreateAnchoredText(inputObj.transform, font, "Text",
            "",
            17, FontStyle.Bold, Color.white,
            new Vector2(0.04f, 0f), new Vector2(0.96f, 1f), TextAnchor.MiddleCenter);
        joinInputField.textComponent = inputTxt;
        joinInputField.placeholder = placeholderTxt;
        joinInputField.text = "";
        joinInputField.onEndEdit.AddListener(OnInputFieldSubmitted);

        // Primary Single Connect & Join Button: Enters Character Selection immediately!
        joinSessionBtn = CreateAnchoredButton(card.transform, font, "JoinSessionBtn",
            "CONNECT & JOIN SQUAD",
            18, new Color(0.12f, 0.46f, 0.76f, 0.85f), Color.white,
            new Vector2(0.08f, 0.12f), new Vector2(0.92f, 0.26f), JoinFromInputField);
    }

    private void BuildFooterBar(Transform parent, Font font)
    {
        GameObject footerObj = new GameObject("Footer_Bar");
        footerObj.transform.SetParent(parent, false);
        RectTransform fRT = footerObj.AddComponent<RectTransform>();
        fRT.anchorMin = new Vector2(0.04f, 0.025f);
        fRT.anchorMax = new Vector2(0.96f, 0.105f);
        fRT.offsetMin = Vector2.zero;
        fRT.offsetMax = Vector2.zero;

        // Top subtle line
        CreateAnchoredPanel(footerObj.transform, "Footer_Line", new Vector2(0f, 0.96f), new Vector2(1f, 1f), new Color(0.20f, 0.40f, 0.60f, 0.45f));

        // Left: Controls Quick Summary (dynamically reflects custom keybinds)
        _controlsHintText = CreateAnchoredText(footerObj.transform, font, "Controls_Hint",
            "",
            13, FontStyle.Normal, new Color(0.62f, 0.74f, 0.86f),
            new Vector2(0.0f, 0.05f), new Vector2(0.58f, 0.90f), TextAnchor.MiddleLeft);
        UpdateFooterControlsHint();

        // Right Utility Buttons: SETTINGS, CREDITS, QUIT GAME
        CreateAnchoredButton(footerObj.transform, font, "Btn_Settings",
            "⚙  SETTINGS",
            14, new Color(0.12f, 0.22f, 0.34f, 0.95f), new Color(0.85f, 0.95f, 1.0f),
            new Vector2(0.60f, 0.12f), new Vector2(0.725f, 0.85f), OpenSettingsModal);

        CreateAnchoredButton(footerObj.transform, font, "Btn_Credits",
            "★  CREDITS",
            14, new Color(0.12f, 0.22f, 0.34f, 0.95f), new Color(0.85f, 0.95f, 1.0f),
            new Vector2(0.738f, 0.12f), new Vector2(0.863f, 0.85f), OpenCreditsModal);

        CreateAnchoredButton(footerObj.transform, font, "Btn_QuitGame",
            "✕  QUIT GAME",
            14, new Color(0.45f, 0.14f, 0.16f, 0.95f), Color.white,
            new Vector2(0.876f, 0.12f), new Vector2(1.0f, 0.85f), QuitGame);
    }

    private void UpdateFooterControlsHint()
    {
        if (_controlsHintText == null) return;
        string fwd = OasisKeybindManager.FormatKeyName(OasisKeybindManager.GetKey(OasisAction.MoveForward));
        string left = OasisKeybindManager.FormatKeyName(OasisKeybindManager.GetKey(OasisAction.MoveLeft));
        string back = OasisKeybindManager.FormatKeyName(OasisKeybindManager.GetKey(OasisAction.MoveBack));
        string right = OasisKeybindManager.FormatKeyName(OasisKeybindManager.GetKey(OasisAction.MoveRight));
        string jump = OasisKeybindManager.FormatKeyName(OasisKeybindManager.GetKey(OasisAction.Jump));
        string sprint = OasisKeybindManager.FormatKeyName(OasisKeybindManager.GetKey(OasisAction.Sprint));
        string dash = OasisKeybindManager.FormatKeyName(OasisKeybindManager.GetKey(OasisAction.Dash));
        string score = OasisKeybindManager.FormatKeyName(OasisKeybindManager.GetKey(OasisAction.Scoreboard));

        _controlsHintText.text =
            $"CONTROLS:  [{fwd}{left}{back}{right}] Move   •   [{jump}] Jump   •   [{sprint}] Sprint   •   [{dash}] Dash   •   [RMB] Aim   •   [{score}] Leaderboard";
    }

    // =========================================================================
    // SETTINGS & CREDITS MODALS
    // =========================================================================

    public void OpenSettingsModal()
    {
        _awaitingRebindAction = null;
        if (creditsModal != null) creditsModal.SetActive(false);
        if (settingsModal != null)
        {
            settingsModal.SetActive(true);
            RefreshSettingsModalUI();
        }
    }

    public void CloseSettingsModal()
    {
        _awaitingRebindAction = null;
        PlayerPrefs.Save();
        UpdateFooterControlsHint();
        if (settingsModal != null) settingsModal.SetActive(false);
    }

    public void OpenCreditsModal()
    {
        _awaitingRebindAction = null;
        if (settingsModal != null) settingsModal.SetActive(false);
        if (creditsModal != null) creditsModal.SetActive(true);
    }

    public void CloseCreditsModal()
    {
        if (creditsModal != null) creditsModal.SetActive(false);
    }

    private void BuildSettingsModal(Transform parent, Font font)
    {
        settingsModal = CreateAnchoredPanel(parent, "Settings_Modal_Overlay", Vector2.zero, Vector2.one, new Color(0.01f, 0.02f, 0.04f, 0.88f));
        settingsModal.GetComponent<Image>().raycastTarget = true; // Block clicks behind modal

        GameObject cardBorder = CreateAnchoredPanel(settingsModal.transform, "Settings_Card_Border", new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.92f), new Color(0.18f, 0.78f, 0.98f, 0.9f));
        GameObject card = new GameObject("Inner");
        card.transform.SetParent(cardBorder.transform, false);
        RectTransform cRT = card.AddComponent<RectTransform>();
        cRT.anchorMin = Vector2.zero;
        cRT.anchorMax = Vector2.one;
        cRT.offsetMin = new Vector2(2, 2);
        cRT.offsetMax = new Vector2(-2, -2);
        Image cardBg = card.AddComponent<Image>();
        cardBg.color = new Color(0.04f, 0.065f, 0.11f, 0.98f);

        CreateAnchoredPanel(card.transform, "TopStripe", new Vector2(0f, 0.975f), new Vector2(1f, 1f), new Color(0.18f, 0.88f, 1.0f, 1f));

        CreateAnchoredText(card.transform, font, "Title",
            "SYSTEM, DISPLAY & KEYBIND SETTINGS",
            22, FontStyle.Bold, Color.white,
            new Vector2(0.04f, 0.885f), new Vector2(0.96f, 0.965f), TextAnchor.MiddleCenter);

        CreateAnchoredPanel(card.transform, "Divider", new Vector2(0.03f, 0.87f), new Vector2(0.97f, 0.874f), new Color(0.2f, 0.85f, 1.0f, 0.35f));

        // Vertical Divider separating System/Display (Left) from Keybind Selection (Right)
        CreateAnchoredPanel(card.transform, "VerticalDivider", new Vector2(0.425f, 0.17f), new Vector2(0.428f, 0.85f), new Color(0.2f, 0.85f, 1.0f, 0.25f));

        // =====================================================================
        // LEFT COLUMN: AUDIO, SENSITIVITY & DISPLAY SETTINGS (0.03 .. 0.405)
        // =====================================================================
        CreateAnchoredText(card.transform, font, "Hdr_System",
            "AUDIO, AIM & DISPLAY",
            14, FontStyle.Bold, new Color(0.22f, 0.90f, 1.0f),
            new Vector2(0.04f, 0.79f), new Vector2(0.40f, 0.85f), TextAnchor.MiddleLeft);

        // 1. Master Audio Volume Row
        CreateAnchoredText(card.transform, font, "Lbl_Volume",
            "MASTER AUDIO VOLUME",
            12, FontStyle.Bold, new Color(0.82f, 0.90f, 0.98f),
            new Vector2(0.04f, 0.70f), new Vector2(0.30f, 0.76f), TextAnchor.MiddleLeft);

        _volumeValueText = CreateAnchoredText(card.transform, font, "Val_Volume",
            "85%",
            13, FontStyle.Bold, new Color(0.22f, 0.92f, 1.0f),
            new Vector2(0.31f, 0.70f), new Vector2(0.40f, 0.76f), TextAnchor.MiddleRight);

        _volumeSlider = CreateStyledSlider(card.transform, "Slider_Volume", new Vector2(0.04f, 0.64f), new Vector2(0.40f, 0.69f), 0f, 1f, PlayerPrefs.GetFloat("OasisMasterVolume", 0.85f), OnVolumeChanged);

        // 2. Mouse Look Sensitivity Row
        CreateAnchoredText(card.transform, font, "Lbl_Sensitivity",
            "LOOK / AIM SENSITIVITY",
            12, FontStyle.Bold, new Color(0.82f, 0.90f, 0.98f),
            new Vector2(0.04f, 0.54f), new Vector2(0.30f, 0.60f), TextAnchor.MiddleLeft);

        _sensitivityValueText = CreateAnchoredText(card.transform, font, "Val_Sensitivity",
            "1.0x",
            13, FontStyle.Bold, new Color(0.22f, 0.92f, 1.0f),
            new Vector2(0.31f, 0.54f), new Vector2(0.40f, 0.60f), TextAnchor.MiddleRight);

        _sensitivitySlider = CreateStyledSlider(card.transform, "Slider_Sensitivity", new Vector2(0.04f, 0.48f), new Vector2(0.40f, 0.53f), 0.2f, 3.0f, PlayerPrefs.GetFloat("OasisMouseSensitivity", 1.0f), OnSensitivityChanged);

        // 3. Graphics Quality Preset Row
        CreateAnchoredText(card.transform, font, "Lbl_Quality",
            "GRAPHICS QUALITY PRESET",
            12, FontStyle.Bold, new Color(0.82f, 0.90f, 0.98f),
            new Vector2(0.04f, 0.38f), new Vector2(0.40f, 0.44f), TextAnchor.MiddleLeft);

        string[] qLabels = new string[] { "PERFORMANCE", "BALANCED", "ULTRA" };
        float[] qMinX = new float[] { 0.04f, 0.165f, 0.29f };
        float[] qMaxX = new float[] { 0.155f, 0.28f, 0.40f };
        for (int q = 0; q < 3; q++)
        {
            int tier = q;
            Button qBtn = CreateAnchoredButton(card.transform, font, $"Btn_Quality_{q}",
                qLabels[q], 11, new Color(0.08f, 0.12f, 0.18f, 0.9f), Color.white,
                new Vector2(qMinX[q], 0.305f), new Vector2(qMaxX[q], 0.375f), () => SetQualityTier(tier));
            _qualityBtnBgs[q] = qBtn.GetComponent<Image>();
            _qualityBtnTexts[q] = qBtn.GetComponentInChildren<Text>();
        }

        // 4. Display Mode (Fullscreen / Windowed)
        CreateAnchoredText(card.transform, font, "Lbl_Display",
            "DISPLAY WINDOW MODE",
            12, FontStyle.Bold, new Color(0.82f, 0.90f, 0.98f),
            new Vector2(0.04f, 0.225f), new Vector2(0.40f, 0.285f), TextAnchor.MiddleLeft);

        Button fsBtn = CreateAnchoredButton(card.transform, font, "Btn_Fullscreen",
            "FULLSCREEN: ON", 12, new Color(0.12f, 0.45f, 0.72f, 0.95f), Color.white,
            new Vector2(0.04f, 0.155f), new Vector2(0.40f, 0.22f), ToggleFullscreenMode);
        _fullscreenBtnBg = fsBtn.GetComponent<Image>();
        _fullscreenBtnText = fsBtn.GetComponentInChildren<Text>();

        // =====================================================================
        // RIGHT COLUMN: KEYBIND SELECTION GRID (0.45 .. 0.97)
        // =====================================================================
        CreateAnchoredText(card.transform, font, "Hdr_Keybinds",
            "KEYBINDINGS  //  CLICK ANY KEY TO REBIND",
            14, FontStyle.Bold, new Color(0.22f, 0.95f, 0.62f),
            new Vector2(0.45f, 0.79f), new Vector2(0.96f, 0.85f), TextAnchor.MiddleLeft);

        _rebindStatusBannerText = CreateAnchoredText(card.transform, font, "Lbl_RebindStatus",
            "Select any action button below and press a key to customize controls.",
            12, FontStyle.Italic, new Color(0.68f, 0.82f, 0.94f),
            new Vector2(0.45f, 0.74f), new Vector2(0.96f, 0.79f), TextAnchor.MiddleLeft);

        _keybindBtnBgs.Clear();
        _keybindBtnTexts.Clear();

        OasisAction[] actions = OasisKeybindManager.AllActions;
        int rowsPerCol = 6;
        for (int i = 0; i < actions.Length; i++)
        {
            OasisAction act = actions[i];
            int col = i / rowsPerCol; // 0 = left sub-column, 1 = right sub-column
            int row = i % rowsPerCol; // 0..5 top to bottom

            float colStartX = (col == 0) ? 0.45f : 0.715f;
            float colEndX = (col == 0) ? 0.695f : 0.96f;

            float rowTopY = 0.725f - (row * 0.092f);
            float rowBotY = rowTopY - 0.076f;

            // Subtle row container background
            CreateAnchoredPanel(card.transform, $"KeyRowBg_{act}",
                new Vector2(colStartX, rowBotY), new Vector2(colEndX, rowTopY),
                new Color(0.06f, 0.10f, 0.16f, 0.75f));

            // Action Label
            CreateAnchoredText(card.transform, font, $"Lbl_Key_{act}",
                OasisKeybindManager.GetActionLabel(act),
                11, FontStyle.Bold, new Color(0.84f, 0.92f, 0.98f),
                new Vector2(colStartX + 0.01f, rowBotY), new Vector2(colStartX + 0.145f, rowTopY),
                TextAnchor.MiddleLeft);

            // Interactive Keybind Button
            OasisAction capturedAct = act;
            Button keyBtn = CreateAnchoredButton(card.transform, font, $"Btn_Key_{act}",
                $"[ {OasisKeybindManager.FormatKeyName(OasisKeybindManager.GetKey(act))} ]",
                12, new Color(0.11f, 0.24f, 0.38f, 0.95f), new Color(0.25f, 0.92f, 1.0f),
                new Vector2(colStartX + 0.15f, rowBotY + 0.008f), new Vector2(colEndX - 0.008f, rowTopY - 0.008f),
                () => StartRebind(capturedAct));

            _keybindBtnBgs[act] = keyBtn.GetComponent<Image>();
            _keybindBtnTexts[act] = keyBtn.GetComponentInChildren<Text>();
        }

        // Bottom Bar: Reset Default Keys & Save/Close
        CreateAnchoredButton(card.transform, font, "Btn_ResetKeys",
            "↺  RESET DEFAULT KEYBINDS",
            13, new Color(0.16f, 0.24f, 0.36f, 0.95f), new Color(0.85f, 0.94f, 1.0f),
            new Vector2(0.53f, 0.045f), new Vector2(0.88f, 0.125f), ResetDefaultKeybinds);

        CreateAnchoredButton(card.transform, font, "Btn_CloseSettings",
            "✓  SAVE & RETURN TO MENU",
            14, new Color(0.10f, 0.65f, 0.38f, 1.0f), Color.white,
            new Vector2(0.12f, 0.045f), new Vector2(0.47f, 0.125f), CloseSettingsModal);

        RefreshSettingsModalUI();
        settingsModal.SetActive(false);
    }

    private void StartRebind(OasisAction action)
    {
        _awaitingRebindAction = action;
        RefreshSettingsModalUI();
    }

    private void ResetDefaultKeybinds()
    {
        _awaitingRebindAction = null;
        OasisKeybindManager.ResetToDefaults();
        RefreshSettingsModalUI();
        UpdateFooterControlsHint();
        if (_rebindStatusBannerText != null)
        {
            _rebindStatusBannerText.text = "<color=#38EF7D>All keybindings restored to tactical defaults.</color>";
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
        PlayerPrefs.SetFloat("OasisMouseSensitivity", val);
        if (_sensitivityValueText != null)
        {
            _sensitivityValueText.text = $"{val:F1}x";
        }
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
        RefreshSettingsModalUI();
    }

    private void ToggleFullscreenMode()
    {
        Screen.fullScreen = !Screen.fullScreen;
        RefreshSettingsModalUI();
    }

    private void RefreshSettingsModalUI()
    {
        float vol = PlayerPrefs.GetFloat("OasisMasterVolume", 0.85f);
        if (_volumeSlider != null) _volumeSlider.SetValueWithoutNotify(vol);
        if (_volumeValueText != null) _volumeValueText.text = $"{Mathf.RoundToInt(vol * 100f)}%";

        float sens = PlayerPrefs.GetFloat("OasisMouseSensitivity", 1.0f);
        if (_sensitivitySlider != null) _sensitivitySlider.SetValueWithoutNotify(sens);
        if (_sensitivityValueText != null) _sensitivityValueText.text = $"{sens:F1}x";

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

        // Refresh Keybind Buttons
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

        if (_rebindStatusBannerText != null)
        {
            if (_awaitingRebindAction.HasValue)
            {
                string label = OasisKeybindManager.GetActionLabel(_awaitingRebindAction.Value);
                _rebindStatusBannerText.text = $"<color=#FFBD2E><b>REBINDING [{label}]:</b> Press any key to assign, or [ESC] to cancel.</color>";
            }
            else
            {
                _rebindStatusBannerText.text = "Click any action button below and press a key to customize controls.";
            }
        }
    }

    private void BuildCreditsModal(Transform parent, Font font)
    {
        creditsModal = CreateAnchoredPanel(parent, "Credits_Modal_Overlay", Vector2.zero, Vector2.one, new Color(0.01f, 0.02f, 0.04f, 0.88f));
        creditsModal.GetComponent<Image>().raycastTarget = true;

        GameObject cardBorder = CreateAnchoredPanel(creditsModal.transform, "Credits_Card_Border", new Vector2(0.24f, 0.16f), new Vector2(0.76f, 0.84f), new Color(0.22f, 0.88f, 0.62f, 0.9f));
        GameObject card = new GameObject("Inner");
        card.transform.SetParent(cardBorder.transform, false);
        RectTransform cRT = card.AddComponent<RectTransform>();
        cRT.anchorMin = Vector2.zero;
        cRT.anchorMax = Vector2.one;
        cRT.offsetMin = new Vector2(2, 2);
        cRT.offsetMax = new Vector2(-2, -2);
        Image cardBg = card.AddComponent<Image>();
        cardBg.color = new Color(0.045f, 0.07f, 0.115f, 0.98f);

        CreateAnchoredPanel(card.transform, "TopStripe", new Vector2(0f, 0.97f), new Vector2(1f, 1f), new Color(0.22f, 0.92f, 0.58f, 1f));

        CreateAnchoredText(card.transform, font, "Title",
            "OASIS PROTOCOL  //  PROJECT CREDITS",
            24, FontStyle.Bold, Color.white,
            new Vector2(0.06f, 0.85f), new Vector2(0.94f, 0.95f), TextAnchor.MiddleCenter);

        CreateAnchoredPanel(card.transform, "Divider", new Vector2(0.06f, 0.82f), new Vector2(0.94f, 0.825f), new Color(0.22f, 0.92f, 0.58f, 0.35f));

        string creditsBody =
            "<color=#26D9FF><b>CORE GAMEPLAY & TACTICAL SYSTEMS</b></color>\n" +
            "Multi-Team Tactical Arena (2–4 Squads: Alpha, Bravo, Charlie & Delta)\n" +
            "7 Specialized Combat Operatives with 3D Staging & Custom Loadouts\n\n" +
            "<color=#38EF7D><b>AUTONOMOUS AI & TELEMETRY ENGINE</b></color>\n" +
            "Tactical Squad AI with Dynamic Cover, Flanking, Revival & Anti-Stuck Vaulting\n" +
            "Real-Time Match Telemetry Logger & Automated Markdown Analytics Reports\n\n" +
            "<color=#FFBD2E><b>NETWORKING & INFRASTRUCTURE</b></color>\n" +
            "Unity Relay Cross-Network Co-Op & Direct UDP LAN Synchronization\n" +
            "Built with Unity 6 Universal Render Pipeline (URP)";

        Text bodyTxt = CreateAnchoredText(card.transform, font, "Credits_Body",
            creditsBody,
            14, FontStyle.Normal, new Color(0.84f, 0.90f, 0.96f),
            new Vector2(0.07f, 0.20f), new Vector2(0.93f, 0.79f), TextAnchor.MiddleCenter);
        bodyTxt.supportRichText = true;
        bodyTxt.lineSpacing = 1.22f;

        CreateAnchoredButton(card.transform, font, "Btn_CloseCredits",
            "CLOSE CREDITS",
            15, new Color(0.14f, 0.52f, 0.78f, 1.0f), Color.white,
            new Vector2(0.30f, 0.05f), new Vector2(0.70f, 0.15f), CloseCreditsModal);

        creditsModal.SetActive(false);
    }

    // =========================================================================
    // PROCEDURAL FADED BACKGROUND FALLBACK GENERATOR
    // =========================================================================

    private Texture2D GenerateProceduralOasisBackdrop(int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color[] pixels = new Color[width * height];

        Color skyTop = new Color(0.02f, 0.05f, 0.11f, 1f);
        Color skyMid = new Color(0.05f, 0.16f, 0.26f, 1f);
        Color horizonGlow = new Color(0.14f, 0.48f, 0.62f, 1f);
        Color duneDark = new Color(0.04f, 0.07f, 0.12f, 1f);
        Color duneRim = new Color(0.12f, 0.35f, 0.48f, 1f);

        for (int y = 0; y < height; y++)
        {
            float ny = (float)y / height;
            for (int x = 0; x < width; x++)
            {
                float nx = (float)x / width;

                // Layered canyon & dune silhouettes
                float ridge1 = 0.36f + 0.07f * Mathf.Sin(nx * 5.5f + 0.4f) + 0.03f * Mathf.Cos(nx * 14f);
                float ridge2 = 0.25f + 0.05f * Mathf.Sin(nx * 8.2f + 2.1f) + 0.02f * Mathf.Sin(nx * 21f);

                Color c;
                if (ny > ridge1)
                {
                    float t = Mathf.InverseLerp(ridge1, 1f, ny);
                    c = Color.Lerp(horizonGlow, Color.Lerp(skyMid, skyTop, t), Mathf.Pow(t, 0.65f));

                    // Subtle central oasis beacon glow
                    float distCenter = Vector2.Distance(new Vector2(nx, ny), new Vector2(0.5f, 0.42f));
                    if (distCenter < 0.35f)
                    {
                        c += new Color(0.08f, 0.28f, 0.38f, 0f) * (1f - distCenter / 0.35f) * 0.45f;
                    }
                }
                else if (ny > ridge2)
                {
                    float rim = Mathf.Clamp01(1f - (ridge1 - ny) * 28f);
                    c = Color.Lerp(duneDark * 1.25f, duneRim, rim * 0.55f);
                }
                else
                {
                    float rim = Mathf.Clamp01(1f - (ridge2 - ny) * 32f);
                    c = Color.Lerp(duneDark * 0.85f, duneRim * 0.8f, rim * 0.4f);

                    // Subtle perspective tactical grid lines on canyon floor
                    if (y % 14 == 0 || Mathf.Abs((nx - 0.5f) * (0.35f - ny) * 40f % 1f) < 0.04f)
                    {
                        c += new Color(0.04f, 0.18f, 0.26f, 0f) * 0.35f;
                    }
                }

                // Subtle scanline texture
                if (y % 3 == 0) c *= 0.94f;
                c.a = 1f;
                pixels[y * width + x] = c;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
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

        Shadow shadow = obj.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);
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

    // =========================================================================
    // RUNTIME UPDATE & NETWORK HANDLERS
    // =========================================================================

    private void OnGUI()
    {
        if (settingsModal == null || !settingsModal.activeSelf || !_awaitingRebindAction.HasValue) return;

        Event e = Event.current;
        if (e != null && e.isKey && e.type == EventType.KeyDown && e.keyCode != KeyCode.None)
        {
            if (e.keyCode == KeyCode.Escape)
            {
                _awaitingRebindAction = null;
                RefreshSettingsModalUI();
                e.Use();
                return;
            }

            AssignReboundKey(_awaitingRebindAction.Value, e.keyCode);
            e.Use();
        }
    }

    private void AssignReboundKey(OasisAction action, KeyCode newKey)
    {
        _awaitingRebindAction = null;
        OasisKeybindManager.SetKey(action, newKey);
        RefreshSettingsModalUI();
        UpdateFooterControlsHint();
        if (_rebindStatusBannerText != null)
        {
            string actName = OasisKeybindManager.GetActionLabel(action);
            string keyName = OasisKeybindManager.FormatKeyName(newKey);
            _rebindStatusBannerText.text = $"<color=#38EF7D><b>BOUND [{actName}]  →  [{keyName}]</b></color>";
        }
    }

    private void Update()
    {
        if (_isLoading) return;

        UpdateLobbyStatusDisplay();

        // Handle active Keybind Listening Mode inside Settings Modal
        if (settingsModal != null && settingsModal.activeSelf && _awaitingRebindAction.HasValue)
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                _awaitingRebindAction = null;
                RefreshSettingsModalUI();
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
                RefreshSettingsModalUI();
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

        // Check if user is in a modal or input field
        if ((settingsModal != null && settingsModal.activeSelf) ||
            (creditsModal != null && creditsModal.activeSelf) ||
            (joinInputField != null && joinInputField.isFocused))
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CloseSettingsModal();
                CloseCreditsModal();
            }
#else
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CloseSettingsModal();
                CloseCreditsModal();
            }
#endif
            return;
        }

        // Keyboard shortcuts support: Enter, Space, 1
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (Keyboard.current.enterKey.wasPressedThisFrame ||
                Keyboard.current.numpadEnterKey.wasPressedThisFrame ||
                Keyboard.current.spaceKey.wasPressedThisFrame ||
                Keyboard.current.digit1Key.wasPressedThisFrame ||
                Keyboard.current.numpad1Key.wasPressedThisFrame)
            {
                LaunchMissionFromMenu();
            }
        }
#else
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Alpha1))
        {
            LaunchMissionFromMenu();
        }
#endif
    }

    private void UpdateLobbyStatusDisplay()
    {
        if (coOpStatusText == null) return;

        int numTeams = Mathf.Clamp(PlayerPrefs.GetInt("OasisNumTeams", 2), 2, 4);
        int playersPerTeam = Mathf.Clamp(PlayerPrefs.GetInt("OasisPlayersPerTeam", 2), 1, 4);

        if (OasisNetworkManager.Instance == null || OasisNetworkManager.Instance.currentRole == OasisNetworkManager.NetworkRole.Offline)
        {
            coOpStatusText.text = $"STATUS: READY  //  PRESET: {numTeams} TEAMS × {playersPerTeam}/TEAM\n<color=#38EF7D>Customize Operative, Squad & Teams in Staging Bay</color>";
        }
        else if (OasisNetworkManager.Instance.currentRole == OasisNetworkManager.NetworkRole.Host)
        {
            int players = OasisNetworkManager.Instance.ConnectedPlayersCount;
            string code = OasisNetworkManager.Instance.activeRelayJoinCode;
            if (!string.IsNullOrEmpty(code))
            {
                if (string.IsNullOrEmpty(_lastCopiedCode) || _lastCopiedCode != code)
                {
                    _lastCopiedCode = code;
                    GUIUtility.systemCopyBuffer = code;
                    Debug.Log($"[MainMenuManager] Host Relay Code {code} copied to clipboard!");
                }
                coOpStatusText.text = $"<color=#26D9FF>HOST JOIN CODE: {code}</color> (COPIED TO CLIPBOARD)\n<color=#38EF7D>LOBBY: {players} REAL OPERATIVE{(players > 1 ? "S" : "")} CONNECTED</color>";
            }
            else
            {
                coOpStatusText.text = $"HOSTING RELAY ROOM...\nLOBBY: {players} REAL OPERATIVE{(players > 1 ? "S" : "")} CONNECTED";
            }

            if (launchMissionBtn != null)
            {
                launchMissionBtn.interactable = true;
            }
        }
        else if (OasisNetworkManager.Instance.currentRole == OasisNetworkManager.NetworkRole.Client)
        {
            int players = OasisNetworkManager.Instance.ConnectedPlayersCount;
            coOpStatusText.text = $"<color=#38EF7D>CONNECTED TO SQUAD HOST!</color>\nLOBBY: {players} OPERATIVES // ONLY HOST CAN START GAME";
        }
    }

    private OasisNetworkManager GetOrCreateNetworkManager()
    {
        if (OasisNetworkManager.Instance != null) return OasisNetworkManager.Instance;
        GameObject go = new GameObject("OasisNetworkManager");
        return go.AddComponent<OasisNetworkManager>();
    }

    public void HostMissionFromMenu()
    {
        if (_isLoading) return;
        var netMgr = GetOrCreateNetworkManager();
        netMgr.StartRelayHost();
        Debug.Log("[MainMenuManager] Host allocating Relay session and launching directly into Character Selection!");
        LoadGameScene(oasisSceneName);
    }

    public void GenerateLobbyCode()
    {
        HostMissionFromMenu();
    }

    public void LaunchMissionFromMenu()
    {
        HostMissionFromMenu();
    }

    public void PlayOasis()
    {
        if (_isLoading) return;
        OasisNetworkManager.PendingLaunchMode = "Offline";
        OasisNetworkManager.PendingTargetCodeOrIP = "";
        Debug.Log("[MainMenuManager] Deploying to Oasis Station (Solo / Offline)...");
        LoadGameScene(oasisSceneName);
    }

    public void HostOasisRelay()
    {
        GenerateLobbyCode();
    }

    public void HostOasisLan()
    {
        if (_isLoading) return;
        var netMgr = GetOrCreateNetworkManager();
        netMgr.StartHost();
    }

    public void JoinOasisSession(string codeOrIp)
    {
        if (_isLoading) return;
        var netMgr = GetOrCreateNetworkManager();

        codeOrIp = (codeOrIp ?? "").Trim();
        if (string.IsNullOrEmpty(codeOrIp))
        {
            if (!string.IsNullOrEmpty(netMgr.activeRelayJoinCode))
            {
                codeOrIp = netMgr.activeRelayJoinCode;
            }
            else if (!string.IsNullOrEmpty(GUIUtility.systemCopyBuffer) && GUIUtility.systemCopyBuffer.Trim().Length == 6)
            {
                codeOrIp = GUIUtility.systemCopyBuffer.Trim();
            }
            else
            {
                codeOrIp = "127.0.0.1";
            }
        }

        if (coOpStatusText != null)
        {
            coOpStatusText.text = $"<color=#38EF7D>CONNECTING TO SQUAD ({codeOrIp.ToUpper()}) — ENTERING CHARACTER SELECTION...</color>";
        }

        netMgr.ConnectOrJoinSquadIntoStaging(codeOrIp);
        LoadGameScene(oasisSceneName);
    }

    public void PlayGarden()
    {
        if (_isLoading) return;
        LoadGameScene(gardenSceneName);
    }

    public void PlayCockpit()
    {
        if (_isLoading) return;
        LoadGameScene(cockpitSceneName);
    }

    public void LoadGameScene(string sceneName)
    {
        _isLoading = true;
        Debug.Log("[MainMenuManager] Loading game scene: " + sceneName);
        try
        {
            SceneManager.LoadScene(sceneName);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[MainMenuManager] Could not load by name (" + ex.Message + "), trying build index fallback...");
            if (sceneName.IndexOf("Oasis", System.StringComparison.OrdinalIgnoreCase) >= 0)
                SceneManager.LoadScene(1);
            else if (sceneName.IndexOf("Garden", System.StringComparison.OrdinalIgnoreCase) >= 0)
                SceneManager.LoadScene(2);
            else if (sceneName.IndexOf("Cockpit", System.StringComparison.OrdinalIgnoreCase) >= 0)
                SceneManager.LoadScene(3);
            else
                Debug.LogError("[MainMenuManager] Unable to load scene: " + sceneName);
        }
    }

    public void QuitGame()
    {
        Debug.Log("[MainMenuManager] Quitting Game...");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
