using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Universal Pause & Hub overlay that automatically attaches to any game scene.
/// Allows players to press Escape or P to pause, resume, restart, or return to Home Menu.
/// </summary>
public class InGamePauseManager : MonoBehaviour
{
    public static InGamePauseManager Instance { get; private set; }
    public bool IsPaused => _isPaused;

    private GameObject _pauseCanvas;
    private bool _isPaused = false;
    private string _mainMenuSceneName = "MainMenu";

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
        SceneManager.sceneLoaded += OnSceneLoaded;
        BuildPauseUI();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Unpause when entering any new scene
        ResumeGame();

        // In main menu, make sure cursor is free
        if (scene.name.Equals(_mainMenuSceneName, System.StringComparison.OrdinalIgnoreCase))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void Update()
    {
        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene.Equals(_mainMenuSceneName, System.StringComparison.OrdinalIgnoreCase))
        {
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
            TogglePause();
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
        _isPaused = true;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (_pauseCanvas != null)
        {
            _pauseCanvas.SetActive(true);
        }
    }

    public void ResumeGame()
    {
        _isPaused = false;
        Time.timeScale = 1f;

        if (_pauseCanvas != null)
        {
            _pauseCanvas.SetActive(false);
        }

        string currentScene = SceneManager.GetActiveScene().name;
        if (!currentScene.Equals(_mainMenuSceneName, System.StringComparison.OrdinalIgnoreCase))
        {
            // If in a first person scene, re-lock cursor on resume
            if (currentScene.Contains("Oasis") || currentScene.Contains("Garden") || currentScene.Contains("Terminal"))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }

    public void RestartCurrentScene()
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

    private void BuildPauseUI()
    {
        _pauseCanvas = new GameObject("PauseCanvas");
        _pauseCanvas.transform.SetParent(transform);

        Canvas canvas = _pauseCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;

        CanvasScaler scaler = _pauseCanvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        _pauseCanvas.AddComponent<GraphicRaycaster>();

        // Dark dim background
        GameObject bg = new GameObject("DarkOverlay");
        bg.transform.SetParent(_pauseCanvas.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.04f, 0.05f, 0.08f, 0.88f);
        RectTransform bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.sizeDelta = Vector2.zero;

        // Container panel
        GameObject panel = new GameObject("PausePanel");
        panel.transform.SetParent(_pauseCanvas.transform, false);
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.sizeDelta = new Vector2(500, 520);
        Image panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0.12f, 0.14f, 0.2f, 0.95f);

        // Title
        CreateText(panel.transform, "PAUSED", 36, new Vector2(0, 180), new Vector2(400, 60), FontStyle.Bold, Color.white);

        // Subtitle (hint)
        CreateText(panel.transform, "Press ESC to resume", 16, new Vector2(0, 130), new Vector2(400, 40), FontStyle.Italic, new Color(0.7f, 0.7f, 0.8f));

        // Buttons
        CreateButton(panel.transform, "Resume Game", new Vector2(0, 40), new Vector2(360, 56), new Color(0.18f, 0.55f, 0.35f), ResumeGame);
        CreateButton(panel.transform, "Restart Level", new Vector2(0, -40), new Vector2(360, 56), new Color(0.25f, 0.35f, 0.55f), RestartCurrentScene);
        CreateButton(panel.transform, "Return to Main Menu", new Vector2(0, -120), new Vector2(360, 56), new Color(0.75f, 0.25f, 0.25f), ReturnToMainMenu);

        _pauseCanvas.SetActive(false);
    }

    private GameObject CreateButton(Transform parent, string label, Vector2 position, Vector2 size, Color bgColor, UnityEngine.Events.UnityAction onClick)
    {
        GameObject btnObj = new GameObject(label + "_Btn");
        btnObj.transform.SetParent(parent, false);

        RectTransform rt = btnObj.AddComponent<RectTransform>();
        rt.anchoredPosition = position;
        rt.sizeDelta = size;

        Image img = btnObj.AddComponent<Image>();
        img.color = bgColor;

        Button btn = btnObj.AddComponent<Button>();
        ColorBlock colors = btn.colors;
        colors.normalColor = bgColor;
        colors.highlightedColor = bgColor * 1.25f;
        colors.pressedColor = bgColor * 0.8f;
        btn.colors = colors;
        btn.onClick.AddListener(onClick);

        // Label
        CreateText(btnObj.transform, label, 20, Vector2.zero, size, FontStyle.Bold, Color.white);

        return btnObj;
    }

    private GameObject CreateText(Transform parent, string text, int fontSize, Vector2 position, Vector2 size, FontStyle fontStyle, Color color)
    {
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(parent, false);

        RectTransform rt = textObj.AddComponent<RectTransform>();
        rt.anchoredPosition = position;
        rt.sizeDelta = size;

        Text txt = textObj.AddComponent<Text>();
        txt.text = text;
        txt.fontSize = fontSize;
        txt.fontStyle = fontStyle;
        txt.color = color;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        return textObj;
    }
}
