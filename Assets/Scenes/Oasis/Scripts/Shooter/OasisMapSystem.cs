using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Tactical In-Game Map System for the Oasis FPS Shooter:
/// 1. Real-time Circular Minimap / Tactical Radar on HUD (Player heading, hostile drone blips, POIs, Compass).
/// 2. Fullscreen Strategic Tactical Map (Toggled via M or Tab key) showing the entire expanded 400x400m world,
///    sector grid, real-time drone locations, and player beacon.
/// </summary>
public class OasisMapSystem : MonoBehaviour
{
    public static OasisMapSystem Instance { get; private set; }

    [Header("Map Range & Settings")]
    public float minimapWorldRadius = 90f; // Radar detection radius (meters)
    public float fullscreenWorldRadius = 220f; // Full expanded world radius (meters)

    // References
    private Transform _playerTransform;
    private Camera _playerCamera;

    // Minimap UI Elements
    private GameObject _mapCanvas;
    private RectTransform _radarContainer;
    private RectTransform _radarPlayerIcon;
    private RectTransform _radarCompassRing;
    private readonly List<RectTransform> _radarDroneBlips = new List<RectTransform>();
    private readonly List<RectTransform> _radarPoiBlips = new List<RectTransform>();

    // Fullscreen Map UI Elements
    private GameObject _fullscreenMapPanel;
    private RectTransform _fullMapContent;
    private RectTransform _fullPlayerIcon;
    private Text _fullPlayerCoordsText;
    private Text _fullHostileCountText;
    private readonly List<RectTransform> _fullDroneBlips = new List<RectTransform>();

    private bool _isFullscreenOpen = false;
    public bool IsFullscreenOpen => _isFullscreenOpen;

    // Points of Interest
    private struct POI
    {
        public string name;
        public Vector3 worldPos;
        public Color color;
    }
    private readonly List<POI> _pointsOfInterest = new List<POI>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        CachePlayerReferences();
        RegisterPointsOfInterest();
        BuildMapUI();

        if (OasisCharacterCustomizer.Instance != null && OasisCharacterCustomizer.Instance.isSelectionActive)
        {
            SetMapCanvasActive(false);
        }
    }

    public void SetMapCanvasActive(bool active)
    {
        if (_mapCanvas != null) _mapCanvas.SetActive(active);
    }

    private void Update()
    {
        if (OasisCharacterCustomizer.Instance != null && OasisCharacterCustomizer.Instance.isSelectionActive)
        {
            return;
        }

        if (_playerTransform == null || _playerCamera == null)
        {
            CachePlayerReferences();
        }

        HandleMapToggleInput();
        UpdateMinimap();

        if (_isFullscreenOpen)
        {
            UpdateFullscreenMap();
        }
    }

    private void CachePlayerReferences()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            _playerTransform = player.transform;
        }
        _playerCamera = Camera.main;
    }

    private void RegisterPointsOfInterest()
    {
        _pointsOfInterest.Clear();
        _pointsOfInterest.Add(new POI { name = "OASIS CORE", worldPos = new Vector3(0f, 0f, -150f), color = new Color(0.2f, 0.9f, 0.95f) });
        _pointsOfInterest.Add(new POI { name = "WEST OUTPOST", worldPos = new Vector3(-38f, 0f, -150f), color = new Color(1f, 0.65f, 0.2f) });
        _pointsOfInterest.Add(new POI { name = "NORTH DUNES", worldPos = new Vector3(0f, 0f, -188f), color = new Color(0.95f, 0.85f, 0.3f) });
        _pointsOfInterest.Add(new POI { name = "EAST GORGE", worldPos = new Vector3(38f, 0f, -150f), color = new Color(0.95f, 0.4f, 0.3f) });
        _pointsOfInterest.Add(new POI { name = "SOUTH CANYON PASS", worldPos = new Vector3(0f, 0f, -114f), color = new Color(0.7f, 0.6f, 0.9f) });
    }

    private void HandleMapToggleInput()
    {
        bool mPressed = OasisKeybindManager.WasPressedThisFrame(OasisAction.Map);
        bool escPressed = false;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            escPressed = Keyboard.current.escapeKey.wasPressedThisFrame;
        }
#else
        escPressed = Input.GetKeyDown(KeyCode.Escape);
#endif

        if (InGamePauseManager.Instance != null && InGamePauseManager.Instance.IsPaused) return;
        if (mPressed)
        {
            SetFullscreenMap(!_isFullscreenOpen);
        }
    }

    public void SetFullscreenMap(bool open)
    {
        _isFullscreenOpen = open;
        if (_fullscreenMapPanel != null)
        {
            _fullscreenMapPanel.SetActive(_isFullscreenOpen);
        }

        // Handle cursor lock
        if (_isFullscreenOpen)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    // =========================================================================
    // MINIMAP / TACTICAL RADAR UPDATE
    // =========================================================================

    private void UpdateMinimap()
    {
        Transform focusTransform = _playerTransform;
        if (focusTransform == null || !focusTransform.gameObject.activeInHierarchy)
        {
            if (Camera.main != null) focusTransform = Camera.main.transform;
            else if (OasisCombatAI.ActiveBots.Count > 0 && OasisCombatAI.ActiveBots[0] != null) 
                focusTransform = OasisCombatAI.ActiveBots[0].transform;
        }

        if (_radarContainer == null || focusTransform == null) return;

        // Rotate sweep beam
        if (_radarSweep != null)
        {
            _radarSweep.localRotation = Quaternion.Euler(0, 0, -Time.time * 110f);
        }

        // 1. Rotate player icon to match view heading
        float playerHeading = _playerCamera != null ? _playerCamera.transform.eulerAngles.y : focusTransform.eulerAngles.y;
        _radarPlayerIcon.localRotation = Quaternion.Euler(0, 0, -playerHeading);

        // 2. Update hostile drones & AI operatives on radar
        int myTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
        List<(Vector3 pos, Color color)> combatants = new List<(Vector3 pos, Color color)>();

        // Add AI Bots
        foreach (var bot in OasisCombatAI.ActiveBots)
        {
            if (bot == null) continue;
            Color col = (bot.teamId == myTeam) ? new Color(0.15f, 0.85f, 1.0f) : new Color(1.0f, 0.23f, 0.19f);
            combatants.Add((bot.transform.position, col));
        }

        // Add Remote Operatives
        if (OasisNetworkManager.Instance != null)
        {
            var rOps = OasisNetworkManager.Instance.GetRemotePlayerInstances();
            for (int r = 0; r < rOps.Count; r++)
            {
                var rop = rOps[r];
                if (rop != null && rop.gameObject != null && rop.gameObject.activeInHierarchy)
                {
                    Color rCol = (rop.teamId == myTeam) ? new Color(0.15f, 0.85f, 1.0f) : new Color(1.0f, 0.23f, 0.19f);
                    combatants.Add((rop.transform.position, rCol));
                }
            }
        }

        // Add Target Drones
        TargetDrone[] drones = FindObjectsByType<TargetDrone>();
        for (int d = 0; d < drones.Length; d++)
        {
            if (drones[d] != null) combatants.Add((drones[d].transform.position, new Color(1.0f, 0.3f, 0.2f)));
        }

        EnsureBlipCount(_radarDroneBlips, _radarContainer, combatants.Count, isFullMap: false, isEnemy: true);
        float radarRadiusPx = 75f;

        for (int i = 0; i < combatants.Count; i++)
        {
            Vector3 diff = combatants[i].pos - focusTransform.position;
            Vector2 flatDiff = new Vector2(diff.x, diff.z);
            float dist = flatDiff.magnitude;

            if (dist <= minimapWorldRadius)
            {
                _radarDroneBlips[i].gameObject.SetActive(true);
                Vector2 normalizedPos = (flatDiff / minimapWorldRadius) * radarRadiusPx;
                _radarDroneBlips[i].anchoredPosition = normalizedPos;

                Image bImg = _radarDroneBlips[i].GetComponent<Image>();
                if (bImg != null) bImg.color = combatants[i].color;

                float pulse = 0.85f + Mathf.PingPong(Time.time * 3f, 0.3f);
                _radarDroneBlips[i].localScale = Vector3.one * pulse;
            }
            else
            {
                _radarDroneBlips[i].gameObject.SetActive(false);
            }
        }

        // Hide unused blips
        for (int i = combatants.Count; i < _radarDroneBlips.Count; i++)
        {
            _radarDroneBlips[i].gameObject.SetActive(false);
        }

        // 3. Update POIs on radar
        EnsureBlipCount(_radarPoiBlips, _radarContainer, _pointsOfInterest.Count, isFullMap: false, isEnemy: false);
        for (int i = 0; i < _pointsOfInterest.Count; i++)
        {
            Vector3 diff = _pointsOfInterest[i].worldPos - focusTransform.position;
            Vector2 flatDiff = new Vector2(diff.x, diff.z);
            float dist = flatDiff.magnitude;

            if (dist <= minimapWorldRadius)
            {
                _radarPoiBlips[i].gameObject.SetActive(true);
                Vector2 norm = (flatDiff / minimapWorldRadius) * radarRadiusPx;
                _radarPoiBlips[i].anchoredPosition = norm;
                _radarPoiBlips[i].GetComponent<Image>().color = _pointsOfInterest[i].color;
            }
            else
            {
                // Clamp to radar edge as directional indicator
                _radarPoiBlips[i].gameObject.SetActive(true);
                Vector2 edgePos = flatDiff.normalized * radarRadiusPx;
                _radarPoiBlips[i].anchoredPosition = edgePos;
                _radarPoiBlips[i].GetComponent<Image>().color = _pointsOfInterest[i].color * 0.7f;
            }
        }
    }

    // =========================================================================
    // FULLSCREEN STRATEGIC MAP UPDATE
    // =========================================================================

    public static Vector2 WorldToMapPosition(Vector3 worldPos)
    {
        // Playable canyon arena center is (0, 0, -150).
        // UI markers: OASIS CORE=(0,0), NORTH DUNES=(0,200), SOUTH CANYON PASS=(0,-200), WEST OUTPOST=(-200,0), EAST GORGE=(200,0).
        // Radius between center and sector bases is ~38-40m in world space.
        Vector3 mapCenter = new Vector3(0f, 0f, -150f);
        float sectorRadius = 40f;
        float markerRadiusPx = 200f;
        float nx = (worldPos.x - mapCenter.x) / sectorRadius;
        float ny = -(worldPos.z - mapCenter.z) / sectorRadius;
        return new Vector2(Mathf.Clamp(nx, -1.6f, 1.6f), Mathf.Clamp(ny, -1.6f, 1.6f)) * markerRadiusPx;
    }

    private void UpdateFullscreenMap()
    {
        if (_fullMapContent == null) return;

        Transform focusTransform = _playerTransform;
        if (focusTransform == null || !focusTransform.gameObject.activeInHierarchy)
        {
            if (Camera.main != null) focusTransform = Camera.main.transform;
            else if (OasisCombatAI.ActiveBots.Count > 0 && OasisCombatAI.ActiveBots[0] != null) 
                focusTransform = OasisCombatAI.ActiveBots[0].transform;
        }

        // 1. Update player / spectator icon on strategic map
        if (focusTransform != null && _fullPlayerIcon != null)
        {
            Vector3 pPos = focusTransform.position;
            _fullPlayerIcon.gameObject.SetActive(true);
            _fullPlayerIcon.anchoredPosition = WorldToMapPosition(pPos);

            float playerHeading = _playerCamera != null ? _playerCamera.transform.eulerAngles.y : focusTransform.eulerAngles.y;
            _fullPlayerIcon.localRotation = Quaternion.Euler(0, 0, -playerHeading);

            if (_fullPlayerCoordsText != null)
            {
                string sector = GetSectorName(pPos);
                string focusName = (_playerTransform != null && _playerTransform.gameObject.activeInHierarchy) ? "YOU" : "SPECTATOR";
                _fullPlayerCoordsText.text = $"FOCUS: {focusName}\nSECTOR: {sector}\nX: {pPos.x:F1}  Z: {pPos.z:F1}  ALT: {pPos.y:F1}m";
            }
        }
        else if (_fullPlayerIcon != null)
        {
            _fullPlayerIcon.gameObject.SetActive(false);
        }

        // 2. Update active operatives & drones on strategic map
        int myTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
        List<(Vector3 pos, Color color)> combatants = new List<(Vector3 pos, Color color)>();
        foreach (var bot in OasisCombatAI.ActiveBots)
        {
            if (bot == null) continue;
            Color col = (bot.teamId == myTeam) ? new Color(0.15f, 0.85f, 1.0f) : new Color(1.0f, 0.23f, 0.19f);
            combatants.Add((bot.transform.position, col));
        }

        if (OasisNetworkManager.Instance != null)
        {
            var rOps = OasisNetworkManager.Instance.GetRemotePlayerInstances();
            for (int r = 0; r < rOps.Count; r++)
            {
                var rop = rOps[r];
                if (rop != null && rop.gameObject != null && rop.gameObject.activeInHierarchy)
                {
                    Color rCol = (rop.teamId == myTeam) ? new Color(0.15f, 0.85f, 1.0f) : new Color(1.0f, 0.23f, 0.19f);
                    combatants.Add((rop.transform.position, rCol));
                }
            }
        }

        TargetDrone[] drones = FindObjectsByType<TargetDrone>();
        for (int d = 0; d < drones.Length; d++)
        {
            if (drones[d] != null) combatants.Add((drones[d].transform.position, new Color(1.0f, 0.3f, 0.2f)));
        }

        EnsureBlipCount(_fullDroneBlips, _fullMapContent, combatants.Count, isFullMap: true, isEnemy: true);

        if (_fullHostileCountText != null)
        {
            _fullHostileCountText.text = $"ACTIVE COMBATANTS: {combatants.Count}  [BLUE: ALLIES  |  RED: HOSTILES]";
        }

        for (int i = 0; i < combatants.Count; i++)
        {
            Vector3 dPos = combatants[i].pos;
            _fullDroneBlips[i].gameObject.SetActive(true);
            _fullDroneBlips[i].anchoredPosition = WorldToMapPosition(dPos);

            Image img = _fullDroneBlips[i].GetComponent<Image>();
            if (img != null) img.color = combatants[i].color;
        }

        for (int i = combatants.Count; i < _fullDroneBlips.Count; i++)
        {
            _fullDroneBlips[i].gameObject.SetActive(false);
        }
    }

    public static string GetSectorName(Vector3 pos)
    {
        Vector3 delta = pos - new Vector3(0f, 0f, -150f);
        if (Mathf.Abs(delta.x) < 22f && Mathf.Abs(delta.z) < 22f) return "OASIS CORE";
        if (delta.z < -20f) return "NORTH DUNES";
        if (delta.z > 20f) return "SOUTH CANYON PASS";
        if (delta.x < -20f) return "WEST OUTPOST";
        if (delta.x > 20f) return "EAST GORGE";
        return "CANYON WASTELAND";
    }

    // Procedural Sprites for Anti-Aliased Rings, Circles, Radar Sweep, Chevron Arrow, and Diamonds
    private static Sprite _ringSprite;
    private static Sprite _circleSprite;
    private static Sprite _arrowSprite;
    private static Sprite _diamondSprite;
    private static Sprite _sweepSprite;

    private static Sprite GetRingSprite()
    {
        if (_ringSprite != null) return _ringSprite;
        int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        Color[] cols = new Color[size * size];
        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float outerR = size * 0.48f;
        float innerR = size * 0.44f;
        float aa = 1.2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center);
                float alphaOuter = Mathf.Clamp01((outerR - d) / aa);
                float alphaInner = Mathf.Clamp01((d - innerR) / aa);
                float a = Mathf.Min(alphaOuter, alphaInner);
                cols[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(cols);
        tex.Apply();
        _ringSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return _ringSprite;
    }

    private static Sprite GetCircleSprite()
    {
        if (_circleSprite != null) return _circleSprite;
        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        Color[] cols = new Color[size * size];
        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = size * 0.48f;
        float aa = 1.2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center);
                float a = Mathf.Clamp01((radius - d) / aa);
                cols[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(cols);
        tex.Apply();
        _circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return _circleSprite;
    }

    private static Sprite GetArrowSprite()
    {
        if (_arrowSprite != null) return _arrowSprite;
        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        Color[] cols = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x - size * 0.5f) / (size * 0.5f);
                float ny = (y - size * 0.5f) / (size * 0.5f);

                bool inside = false;
                if (ny <= 0.85f)
                {
                    float widthAtY = (0.85f - ny) * 0.55f;
                    if (Mathf.Abs(nx) <= widthAtY)
                    {
                        float notchY = -0.75f + Mathf.Abs(nx) * 0.6f;
                        if (ny >= notchY) inside = true;
                    }
                }
                cols[y * size + x] = inside ? Color.white : Color.clear;
            }
        }
        tex.SetPixels(cols);
        tex.Apply();
        _arrowSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return _arrowSprite;
    }

    private static Sprite GetDiamondSprite()
    {
        if (_diamondSprite != null) return _diamondSprite;
        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        Color[] cols = new Color[size * size];
        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float half = size * 0.44f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float manhattan = (Mathf.Abs(x - center.x) + Mathf.Abs(y - center.y)) / half;
                float a = Mathf.Clamp01((1f - manhattan) * 8f);
                cols[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(cols);
        tex.Apply();
        _diamondSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return _diamondSprite;
    }

    private static Sprite GetSweepGradientSprite()
    {
        if (_sweepSprite != null) return _sweepSprite;
        int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        Color[] cols = new Color[size * size];
        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = size * 0.48f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 diff = new Vector2(x, y) - center;
                float dist = diff.magnitude;
                if (dist > radius)
                {
                    cols[y * size + x] = Color.clear;
                    continue;
                }
                float angle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;
                if (angle < 0) angle += 360f;
                float sweepAlpha = 0f;
                if (angle <= 65f)
                {
                    sweepAlpha = Mathf.Pow(angle / 65f, 2f) * 0.45f * (1f - dist / radius * 0.3f);
                }
                cols[y * size + x] = new Color(0.2f, 0.9f, 1f, sweepAlpha);
            }
        }
        tex.SetPixels(cols);
        tex.Apply();
        _sweepSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return _sweepSprite;
    }

    private void EnsureBlipCount(List<RectTransform> list, Transform parent, int requiredCount, bool isFullMap, bool isEnemy)
    {
        while (list.Count < requiredCount)
        {
            GameObject blip = new GameObject(isEnemy ? "DroneBlip" : "PoiBlip");
            blip.transform.SetParent(parent, false);

            RectTransform rt = blip.AddComponent<RectTransform>();
            rt.sizeDelta = isFullMap ? (isEnemy ? new Vector2(16, 16) : new Vector2(18, 18))
                                     : (isEnemy ? new Vector2(10, 10) : new Vector2(8, 8));

            Image img = blip.AddComponent<Image>();
            img.sprite = isEnemy ? GetDiamondSprite() : GetCircleSprite();
            img.color = isEnemy ? new Color(1f, 0.25f, 0.2f) : Color.yellow;
            img.raycastTarget = false;

            list.Add(rt);
        }
    }

    // =========================================================================
    // UI BUILDER (MINIMAP & FULLSCREEN MAP)
    // =========================================================================

    private RectTransform _radarSweep;

    private void BuildMapUI()
    {
        _mapCanvas = new GameObject("OasisMapCanvas");
        _mapCanvas.transform.SetParent(transform);

        Canvas canvas = _mapCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 95; // Just below pause menu, above HUD

        CanvasScaler scaler = _mapCanvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        _mapCanvas.AddComponent<GraphicRaycaster>();

        BuildMinimapUI(_mapCanvas.transform);
        BuildFullscreenMapUI(_mapCanvas.transform);
    }

    private void BuildMinimapUI(Transform parent)
    {
        // 1. Radar Outer Frame (Top Right)
        GameObject frameObj = new GameObject("MinimapFrame");
        frameObj.transform.SetParent(parent, false);

        RectTransform frameRt = frameObj.AddComponent<RectTransform>();
        frameRt.anchorMin = new Vector2(1, 1);
        frameRt.anchorMax = new Vector2(1, 1);
        frameRt.pivot = new Vector2(1, 1);
        frameRt.anchoredPosition = new Vector2(-35, -35);
        frameRt.sizeDelta = new Vector2(200, 225);

        Image frameBg = frameObj.AddComponent<Image>();
        frameBg.color = new Color(0.03f, 0.05f, 0.08f, 0.88f);
        frameBg.raycastTarget = false;

        // Outer Glow Rim
        CreateCircleRing(frameObj.transform, 172f, new Color(0.2f, 0.8f, 1f, 0.45f));

        // Radar Container (Clipped Circle / Area)
        GameObject radarObj = new GameObject("RadarCircle");
        radarObj.transform.SetParent(frameObj.transform, false);
        _radarContainer = radarObj.AddComponent<RectTransform>();
        _radarContainer.anchoredPosition = new Vector2(0, 12);
        _radarContainer.sizeDelta = new Vector2(160, 160);

        Image radarBg = radarObj.AddComponent<Image>();
        radarBg.sprite = GetCircleSprite();
        radarBg.color = new Color(0.04f, 0.09f, 0.14f, 0.85f);
        radarBg.raycastTarget = false;

        // Radar Rotating Sweep Beam
        GameObject sweepObj = new GameObject("RadarSweep");
        sweepObj.transform.SetParent(radarObj.transform, false);
        _radarSweep = sweepObj.AddComponent<RectTransform>();
        _radarSweep.sizeDelta = new Vector2(156, 156);
        Image sweepImg = sweepObj.AddComponent<Image>();
        sweepImg.sprite = GetSweepGradientSprite();
        sweepImg.raycastTarget = false;

        // Concentric Radar Rings
        CreateCircleRing(radarObj.transform, 160f, new Color(0.2f, 0.85f, 1f, 0.6f));
        CreateCircleRing(radarObj.transform, 106f, new Color(0.2f, 0.85f, 1f, 0.25f));
        CreateCircleRing(radarObj.transform, 52f, new Color(0.2f, 0.85f, 1f, 0.20f));

        // Crosshairs
        CreateLine(radarObj.transform, Vector2.zero, new Vector2(160, 1), new Color(0.2f, 0.85f, 1f, 0.22f));
        CreateLine(radarObj.transform, Vector2.zero, new Vector2(1, 160), new Color(0.2f, 0.85f, 1f, 0.22f));

        // Compass Cardinal Points (N, S, E, W)
        CreateTextLabel(radarObj.transform, "N", 11, new Vector2(0, 70), FontStyle.Bold, new Color(1f, 0.35f, 0.35f));
        CreateTextLabel(radarObj.transform, "S", 10, new Vector2(0, -70), FontStyle.Normal, new Color(0.6f, 0.85f, 0.95f));
        CreateTextLabel(radarObj.transform, "E", 10, new Vector2(70, 0), FontStyle.Normal, new Color(0.6f, 0.85f, 0.95f));
        CreateTextLabel(radarObj.transform, "W", 10, new Vector2(-70, 0), FontStyle.Normal, new Color(0.6f, 0.85f, 0.95f));

        // Player Icon (Chevron / Stealth Arrow)
        GameObject playerIcon = new GameObject("PlayerIcon");
        playerIcon.transform.SetParent(radarObj.transform, false);
        _radarPlayerIcon = playerIcon.AddComponent<RectTransform>();
        _radarPlayerIcon.sizeDelta = new Vector2(14, 16);

        Image pImg = playerIcon.AddComponent<Image>();
        pImg.sprite = GetArrowSprite();
        pImg.color = new Color(0.15f, 1f, 0.9f);
        pImg.raycastTarget = false;

        // Subtitle Key Prompt
        CreateTextLabel(frameObj.transform, "RADAR 90M  [M: MAP]", 11, new Vector2(0, -92), FontStyle.Bold, new Color(0.3f, 0.9f, 1f));
    }

    private void BuildFullscreenMapUI(Transform parent)
    {
        _fullscreenMapPanel = new GameObject("FullscreenStrategicMap");
        _fullscreenMapPanel.transform.SetParent(parent, false);

        RectTransform panelRt = _fullscreenMapPanel.AddComponent<RectTransform>();
        panelRt.anchorMin = Vector2.zero;
        panelRt.anchorMax = Vector2.one;
        panelRt.sizeDelta = Vector2.zero;

        Image panelBg = _fullscreenMapPanel.AddComponent<Image>();
        panelBg.color = new Color(0.03f, 0.05f, 0.08f, 0.96f); // Deep tactical black

        // Map Header Banner
        GameObject header = new GameObject("MapHeader");
        header.transform.SetParent(_fullscreenMapPanel.transform, false);
        RectTransform headerRt = header.AddComponent<RectTransform>();
        headerRt.anchorMin = new Vector2(0.5f, 1);
        headerRt.anchorMax = new Vector2(0.5f, 1);
        headerRt.pivot = new Vector2(0.5f, 1);
        headerRt.anchoredPosition = new Vector2(0, -25);
        headerRt.sizeDelta = new Vector2(820, 65);

        CreateTextLabel(header.transform, "TACTICAL DESERT SATELLITE SURVEY", 26, new Vector2(0, 14), FontStyle.Bold, new Color(0.3f, 0.9f, 1f));
        CreateTextLabel(header.transform, "EXPANDED OASIS CANYON BASIN // 440M SATELLITE TELEMETRY", 13, new Vector2(0, -14), FontStyle.Normal, new Color(0.7f, 0.85f, 0.95f));

        // Strategic Map Grid Display
        GameObject mapBox = new GameObject("MapGridDisplay");
        mapBox.transform.SetParent(_fullscreenMapPanel.transform, false);
        _fullMapContent = mapBox.AddComponent<RectTransform>();
        _fullMapContent.anchoredPosition = new Vector2(-120, -10);
        _fullMapContent.sizeDelta = new Vector2(740, 740);

        Image mapBg = mapBox.AddComponent<Image>();
        mapBg.color = new Color(0.04f, 0.08f, 0.12f, 0.92f);

        // Blueprint Grid lines
        for (int i = 0; i <= 6; i++)
        {
            float offset = (i - 3) * (740f / 6f);
            CreateLine(mapBox.transform, new Vector2(0, offset), new Vector2(740, 1), new Color(0.2f, 0.65f, 0.85f, 0.20f));
            CreateLine(mapBox.transform, new Vector2(offset, 0), new Vector2(1, 740), new Color(0.2f, 0.65f, 0.85f, 0.20f));
        }

        // Concentric Range Rings (Circular Sprites!)
        CreateCircleRing(mapBox.transform, 720f, new Color(0.25f, 0.85f, 1f, 0.35f));
        CreateCircleRing(mapBox.transform, 480f, new Color(0.25f, 0.85f, 1f, 0.22f));
        CreateCircleRing(mapBox.transform, 240f, new Color(0.25f, 0.85f, 1f, 0.18f));

        // Sector Landmarks & Labels
        CreateSectorMarker(mapBox.transform, "OASIS CORE", new Vector2(0, 0), new Color(0.2f, 0.95f, 0.9f));
        CreateSectorMarker(mapBox.transform, "NORTH DUNES", new Vector2(0, 200), new Color(0.95f, 0.85f, 0.3f));
        CreateSectorMarker(mapBox.transform, "WEST OUTPOST", new Vector2(-200, 0), new Color(1f, 0.65f, 0.2f));
        CreateSectorMarker(mapBox.transform, "EAST GORGE", new Vector2(200, 0), new Color(0.95f, 0.4f, 0.3f));
        CreateSectorMarker(mapBox.transform, "SOUTH CANYON PASS", new Vector2(0, -200), new Color(0.7f, 0.6f, 0.9f));

        // Strategic Player Icon
        GameObject fullPlayer = new GameObject("FullPlayerIcon");
        fullPlayer.transform.SetParent(mapBox.transform, false);
        _fullPlayerIcon = fullPlayer.AddComponent<RectTransform>();
        _fullPlayerIcon.sizeDelta = new Vector2(22, 26);

        Image fullPImg = fullPlayer.AddComponent<Image>();
        fullPImg.sprite = GetArrowSprite();
        fullPImg.color = new Color(0.15f, 1f, 0.85f);
        fullPImg.raycastTarget = false;

        // Side Info & Legend Panel (Right Side)
        GameObject sidePanel = new GameObject("MapSidePanel");
        sidePanel.transform.SetParent(_fullscreenMapPanel.transform, false);
        RectTransform sideRt = sidePanel.AddComponent<RectTransform>();
        sideRt.anchorMin = new Vector2(1, 0.5f);
        sideRt.anchorMax = new Vector2(1, 0.5f);
        sideRt.pivot = new Vector2(1, 0.5f);
        sideRt.anchoredPosition = new Vector2(-60, -10);
        sideRt.sizeDelta = new Vector2(340, 740);

        Image sideBg = sidePanel.AddComponent<Image>();
        sideBg.color = new Color(0.05f, 0.08f, 0.12f, 0.94f);

        CreateTextLabel(sidePanel.transform, "OPERATIONAL STATUS", 19, new Vector2(0, 330), FontStyle.Bold, new Color(0.3f, 0.9f, 1f));

        _fullPlayerCoordsText = CreateTextLabel(sidePanel.transform, "POSITION: OASIS CORE\nX: 0.0  Z: 0.0  ALT: 1.8m", 14, new Vector2(0, 270), FontStyle.Normal, Color.white);
        _fullHostileCountText = CreateTextLabel(sidePanel.transform, "HOSTILE COMBATANTS: 5", 14, new Vector2(0, 200), FontStyle.Bold, new Color(1f, 0.35f, 0.35f));

        // Legend
        CreateTextLabel(sidePanel.transform, "TACTICAL LEGEND", 17, new Vector2(0, 110), FontStyle.Bold, new Color(0.8f, 0.88f, 0.95f));
        CreateLegendItem(sidePanel.transform, "YOU (OPERATIVE)", new Color(0.15f, 1f, 0.85f), new Vector2(0, 60), isArrow: true);
        CreateLegendItem(sidePanel.transform, "HOSTILE UNIT", new Color(1f, 0.25f, 0.2f), new Vector2(0, 20), isDiamond: true);
        CreateLegendItem(sidePanel.transform, "FRIENDLY ALLY", new Color(0.15f, 0.85f, 1f), new Vector2(0, -20), isDiamond: true);
        CreateLegendItem(sidePanel.transform, "KEY OUTPOST / FORTS", new Color(1f, 0.65f, 0.2f), new Vector2(0, -60));
        CreateLegendItem(sidePanel.transform, "BASIN / OASIS CORE", new Color(0.2f, 0.95f, 0.9f), new Vector2(0, -100));

        // Close Button
        GameObject closeBtn = new GameObject("CloseMapButton");
        closeBtn.transform.SetParent(sidePanel.transform, false);
        RectTransform closeRt = closeBtn.AddComponent<RectTransform>();
        closeRt.anchoredPosition = new Vector2(0, -280);
        closeRt.sizeDelta = new Vector2(280, 50);

        Image closeImg = closeBtn.AddComponent<Image>();
        closeImg.color = new Color(0.15f, 0.45f, 0.65f);

        Button btn = closeBtn.AddComponent<Button>();
        btn.onClick.AddListener(() => SetFullscreenMap(false));

        CreateTextLabel(closeBtn.transform, "CLOSE MAP [M / ESC]", 17, Vector2.zero, FontStyle.Bold, Color.white);

        _fullscreenMapPanel.SetActive(false);
    }

    private void CreateSectorMarker(Transform parent, string label, Vector2 pos, Color col)
    {
        GameObject marker = new GameObject($"Marker_{label}");
        marker.transform.SetParent(parent, false);
        RectTransform rt = marker.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(170, 32);

        Image bg = marker.AddComponent<Image>();
        bg.color = new Color(col.r, col.g, col.b, 0.16f);
        bg.raycastTarget = false;

        GameObject txtObj = new GameObject("Text");
        txtObj.transform.SetParent(marker.transform, false);
        RectTransform tRt = txtObj.AddComponent<RectTransform>();
        tRt.sizeDelta = new Vector2(160, 28);
        Text txt = txtObj.AddComponent<Text>();
        txt.text = $"[ {label} ]";
        txt.fontSize = 12;
        txt.fontStyle = FontStyle.Bold;
        txt.color = col;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.raycastTarget = false;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private void CreateLegendItem(Transform parent, string label, Color color, Vector2 pos, bool isDiamond = false, bool isArrow = false)
    {
        GameObject item = new GameObject("LegendItem");
        item.transform.SetParent(parent, false);
        RectTransform rt = item.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(280, 30);

        // Icon
        GameObject dot = new GameObject("Icon");
        dot.transform.SetParent(item.transform, false);
        RectTransform dotRt = dot.AddComponent<RectTransform>();
        dotRt.anchoredPosition = new Vector2(-115, 0);
        dotRt.sizeDelta = new Vector2(16, 16);
        Image dotImg = dot.AddComponent<Image>();
        if (isArrow) dotImg.sprite = GetArrowSprite();
        else if (isDiamond) dotImg.sprite = GetDiamondSprite();
        else dotImg.sprite = GetCircleSprite();
        dotImg.color = color;
        dotImg.raycastTarget = false;

        // Label
        GameObject lbl = new GameObject("Label");
        lbl.transform.SetParent(item.transform, false);
        RectTransform lblRt = lbl.AddComponent<RectTransform>();
        lblRt.anchoredPosition = new Vector2(15, 0);
        lblRt.sizeDelta = new Vector2(230, 26);
        Text txt = lbl.AddComponent<Text>();
        txt.text = label;
        txt.fontSize = 13;
        txt.color = Color.white;
        txt.alignment = TextAnchor.MiddleLeft;
        txt.raycastTarget = false;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private void CreateCircleRing(Transform parent, float diameter, Color color)
    {
        GameObject ring = new GameObject("Ring");
        ring.transform.SetParent(parent, false);
        RectTransform rt = ring.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(diameter, diameter);

        Image img = ring.AddComponent<Image>();
        img.sprite = GetRingSprite();
        img.color = color;
        img.raycastTarget = false;
    }

    private void CreateLine(Transform parent, Vector2 pos, Vector2 size, Color color)
    {
        GameObject line = new GameObject("Line");
        line.transform.SetParent(parent, false);
        RectTransform rt = line.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Image img = line.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
    }

    private Text CreateTextLabel(Transform parent, string text, int fontSize, Vector2 pos, FontStyle style, Color color)
    {
        GameObject obj = new GameObject("Text");
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(320, 45);

        Text txt = obj.AddComponent<Text>();
        txt.text = text;
        txt.fontSize = fontSize;
        txt.fontStyle = style;
        txt.color = color;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.raycastTarget = false;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        return txt;
    }
}
