using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Attached to each placeable/editable landmark, bunker, tower, cover station, grove, or rock outcrop
/// so the user can click, drag, rotate, adjust floor height, or delete it in the interactive Map Designer.
/// </summary>
public class OasisMapEditableItem : MonoBehaviour
{
    public string itemId;
    public string displayName;
    public string category; // "BUNKERS", "TOWERS", "COVER", "GROVES & ROCKS"
    public string prefabType; // Used when respawning custom-added items
    public bool isCustomSpawned = false;
    public float heightOffset = 0f;

    public Vector3 defaultPosition;
    public Quaternion defaultRotation;
    public Vector3 defaultScale = Vector3.one;
}

/// <summary>
/// Interactive In-Game 3D Map Designer & Manual Item Positioner for Oasis Protocol.
/// Accessible anytime via [F10], from the Main Menu ("MAP DESIGNER"), or from Operative Staging.
/// Allows clicking & dragging any bunker, watchtower, skybridge, citadel, grove, cover station, or rock
/// directly across the authentic Terrain_01 floor, rotating, raising/lowering, spawning new items,
/// teleporting the player to test, and saving custom layouts persistently.
/// </summary>
public class OasisMapDesigner : MonoBehaviour
{
    public static OasisMapDesigner Instance { get; private set; }

    /// <summary>
    /// Set true by MainMenuManager when launching directly into Map Design Mode from the Main Menu.
    /// </summary>
    public static bool OpenOnSceneLoad = false;

    private const string PlayerPrefsLayoutKey = "OasisCustomMapLayout_v1";

    [Serializable]
    public class SavedItemEntry
    {
        public string itemId;
        public string displayName;
        public string category;
        public string prefabType;
        public bool isCustomSpawned;
        public bool isDeleted;
        public Vector3 position;
        public Vector3 eulerAngles;
        public Vector3 scale = Vector3.one;
        public float heightOffset;
    }

    [Serializable]
    public class SavedMapLayoutData
    {
        public List<SavedItemEntry> items = new List<SavedItemEntry>();
    }

    public bool isDesignerActive = false;
    public bool autoSnapToGround = true;
    public bool dragWithLeftMouse = true;
    public bool isOverheadEagleView = false;

    private readonly List<OasisMapEditableItem> _editableItems = new List<OasisMapEditableItem>();
    private OasisMapEditableItem _selectedItem;
    private string _activeCategoryFilter = "ALL";
    private int _nextCustomSpawnId = 1;

    // Designer Camera State
    private Vector3 _camPos = new Vector3(0f, 38f, -185f);
    private float _camYaw = 0f;
    private float _camPitch = 38f;
    private Vector3 _savedCamLocalPos;
    private Quaternion _savedCamLocalRot;
    private Transform _savedCamParent;
    private float _savedCamFov = 75f;
    private bool _wasCharacterSelectionActive = false;
    private bool _isDraggingSelected = false;
    private Vector3 _dragOffsetXZ = Vector3.zero;

    // 3D Selection Highlight Box & Forward Entrance Arrow
    private GameObject _gizmoRoot;
    private readonly LineRenderer[] _gizmoBoxLines = new LineRenderer[12];
    private LineRenderer _gizmoForwardArrowLine;
    private GameObject _gizmoForwardCone;

    // UI Elements
    private GameObject _designerCanvas;
    private Transform _itemListContainer;
    private Text _selectedTitleText;
    private Text _selectedCoordsText;
    private Text _statusBannerText;
    private float _statusBannerTimer = 0f;
    private Image _snapToggleBg;
    private Text _snapToggleText;
    private Image _dragToggleBg;
    private Text _dragToggleText;
    private Image _viewModeBtnBg;
    private Text _viewModeBtnText;
    private readonly Dictionary<string, Image> _catFilterBtnBgs = new Dictionary<string, Image>();
    private readonly List<GameObject> _spawnedListRowObjs = new List<GameObject>();

    private static string CustomLayoutFilePath => Path.Combine(Application.persistentDataPath, "OasisCustomMapLayout.json");

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
        if (OpenOnSceneLoad)
        {
            OpenOnSceneLoad = false;
            StartCoroutine(OpenDesignerAfterSceneInitRoutine());
        }
    }

    private System.Collections.IEnumerator OpenDesignerAfterSceneInitRoutine()
    {
        yield return null;
        yield return null;
        OpenDesignerMode();
    }

    /// <summary>
    /// Scans all generated landmarks under Oasis_Expanded_World_Landmarks, attaches OasisMapEditableItem metadata,
    /// and applies any saved custom positions/rotations/scales/spawns from disk/PlayerPrefs.
    /// </summary>
    public void RegisterSceneLandmarksAndApplySavedLayout(Transform landmarksRoot)
    {
        _editableItems.Clear();
        if (landmarksRoot == null) return;

        for (int i = 0; i < landmarksRoot.childCount; i++)
        {
            Transform child = landmarksRoot.GetChild(i);
            if (child == null) continue;
            RegisterEditableChild(child.gameObject, false, "");
        }

        ApplySavedCustomLayout();
    }

    private OasisMapEditableItem RegisterEditableChild(GameObject go, bool isCustom, string customPrefabType)
    {
        if (go == null) return null;
        OasisMapEditableItem item = go.GetComponent<OasisMapEditableItem>();
        if (item == null) item = go.AddComponent<OasisMapEditableItem>();

        string n = go.name;
        item.itemId = n;
        item.isCustomSpawned = isCustom;
        item.defaultPosition = go.transform.position;
        item.defaultRotation = go.transform.rotation;
        item.defaultScale = go.transform.localScale;

        if (n.IndexOf("Undercover_Hangar_Hideout", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            item.category = "BUNKERS";
            item.prefabType = "HangarBunker";
            string suffix = n.Replace("Undercover_Hangar_Hideout_", "");
            item.displayName = $"Undercover Bunker #{suffix}";
        }
        else if (n.IndexOf("Watchtower", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            item.category = "TOWERS";
            item.prefabType = "Watchtower";
            item.displayName = isCustom ? $"Sniper Watchtower ({n})" : "East Sniper Watchtower";
        }
        else if (n.IndexOf("Skybridge", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            item.category = "TOWERS";
            item.prefabType = "Skybridge";
            item.displayName = isCustom ? $"Skybridge Compound ({n})" : "West Skybridge Compound";
        }
        else if (n.IndexOf("Citadel", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            item.category = "TOWERS";
            item.prefabType = "Citadel";
            item.displayName = isCustom ? $"Citadel Overlook ({n})" : "South Citadel Overlook";
        }
        else if (n.IndexOf("Tactical_Cover_Station", StringComparison.OrdinalIgnoreCase) >= 0 || customPrefabType == "CoverStation")
        {
            item.category = "COVER";
            item.prefabType = "CoverStation";
            string suffix = n.Replace("Tactical_Cover_Station_", "");
            item.displayName = $"Tactical Cover Station #{suffix}";
        }
        else if (n.IndexOf("Satellite_Oasis_Grove", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            item.category = "GROVES & ROCKS";
            item.prefabType = "OasisGrove";
            string suffix = n.Replace("Satellite_Oasis_Grove_", "");
            item.displayName = $"Oasis Beacon Grove #{suffix}";
        }
        else if (n.IndexOf("Arch", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            item.category = "GROVES & ROCKS";
            item.prefabType = "StoneArch";
            item.displayName = n.Replace("_", " ");
        }
        else
        {
            item.category = "GROVES & ROCKS";
            item.prefabType = "RockOutcrop";
            string suffix = n.Replace("Desert_Rock_Outcrop_", "");
            item.displayName = $"Desert Rock Outcrop #{suffix}";
        }

        if (!string.IsNullOrEmpty(customPrefabType))
        {
            item.prefabType = customPrefabType;
        }

        if (!_editableItems.Contains(item))
        {
            _editableItems.Add(item);
        }
        return item;
    }

    private void Update()
    {
        // Global Hotkey [F10] toggles Map Designer Mode at any time
        bool f10Pressed = false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            f10Pressed = Keyboard.current.f10Key.wasPressedThisFrame;
        }
#else
        f10Pressed = Input.GetKeyDown(KeyCode.F10);
#endif
        if (f10Pressed)
        {
            ToggleDesignerMode();
            return;
        }

        if (!isDesignerActive) return;

        // Enforce unlocked cursor in Map Designer Mode
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (_statusBannerTimer > 0f)
        {
            _statusBannerTimer -= Time.unscaledDeltaTime;
            if (_statusBannerTimer <= 0f && _statusBannerText != null)
            {
                _statusBannerText.text = "TIP: Left-Click any building/bunker in 3D to select, hold Left-Click to drag across the floor, [Q/E] to Rotate, Scroll to adjust Height.";
            }
        }

        HandleDesignerCameraControls();
        HandleViewportSelectionAndDragging();
        HandleKeyboardTransformShortcuts();
        UpdateSelectionGizmoVisuals();
        UpdateInspectorReadout();
    }

    public void ToggleDesignerMode()
    {
        if (isDesignerActive) CloseDesignerMode();
        else OpenDesignerMode();
    }

    public void OpenDesignerMode()
    {
        if (isDesignerActive) return;

        if (OasisWorldExpander.Instance != null)
        {
            OasisWorldExpander.Instance.EnsureWorldExpanded();
        }

        // Refresh list if empty
        if (_editableItems.Count == 0)
        {
            GameObject landmarksRoot = GameObject.Find("Oasis_Expanded_World_Landmarks");
            if (landmarksRoot != null)
            {
                for (int i = 0; i < landmarksRoot.transform.childCount; i++)
                {
                    RegisterEditableChild(landmarksRoot.transform.GetChild(i).gameObject, false, "");
                }
            }
        }

        isDesignerActive = true;

        // Pause match timer while designing
        if (OasisShooterManager.Instance != null)
        {
            OasisShooterManager.Instance.isTimerRunning = false;
            OasisShooterManager.Instance.SetHUDActive(false);
        }

        // Temporarily hide Character Selection Canvas if open
        if (OasisCharacterCustomizer.Instance != null && OasisCharacterCustomizer.Instance.isSelectionActive)
        {
            _wasCharacterSelectionActive = true;
            var selPage = GameObject.Find("Oasis_OperativeSelection_Page");
            if (selPage != null) selPage.SetActive(false);
        }
        else
        {
            _wasCharacterSelectionActive = false;
        }

        if (OasisMapSystem.Instance != null)
        {
            OasisMapSystem.Instance.SetMapCanvasActive(false);
        }

        // Disable FirstPersonController while in Designer Camera mode
        var fpc = FindAnyObjectByType<StarterAssets.FirstPersonController>();
        if (fpc != null) fpc.enabled = false;
        var combatMove = FindAnyObjectByType<OasisCombatMovement>();
        if (combatMove != null) combatMove.enabled = false;
        var weapon = FindAnyObjectByType<BlasterWeapon>();
        if (weapon != null)
        {
            weapon.SetWeaponVisualActive(false);
            weapon.enabled = false;
        }

        // Detach Camera.main so we can fly freely over the entire desert map
        Camera cam = Camera.main;
        if (cam != null)
        {
            _savedCamParent = cam.transform.parent;
            _savedCamLocalPos = cam.transform.localPosition;
            _savedCamLocalRot = cam.transform.localRotation;
            _savedCamFov = cam.fieldOfView;

            cam.transform.SetParent(null, true);
            cam.fieldOfView = 70f;

            Transform playerT = OasisPlayerHelper.FindPlayerTransform();
            Vector3 startFocus = (playerT != null) ? playerT.position : new Vector3(0f, -2f, -150f);
            _camPos = startFocus + new Vector3(0f, 26f, -28f);
            _camYaw = 0f;
            _camPitch = 40f;
            isOverheadEagleView = false;
            ApplyCameraTransform(cam);
        }

        EnsureGizmoCreated();
        BuildDesignerUI();

        if (_selectedItem == null && _editableItems.Count > 0)
        {
            SelectItem(_editableItems[0]);
        }
        else
        {
            SelectItem(_selectedItem);
        }

        ShowStatus("<color=#38EF7D><b>MAP DESIGNER ACTIVE:</b></color> Click & drag any bunker/building in 3D to position it manually!", 5.0f);
    }

    public void CloseDesignerMode()
    {
        if (!isDesignerActive) return;
        isDesignerActive = false;

        // Auto-save custom layout and refresh all hangar spawn points
        SaveCustomLayout();
        if (OasisSpawnSystem.Instance != null)
        {
            OasisSpawnSystem.Instance.RebuildHangarSpawnsFromScene();
        }

        if (_designerCanvas != null)
        {
            Destroy(_designerCanvas);
            _designerCanvas = null;
        }

        if (_gizmoRoot != null)
        {
            _gizmoRoot.SetActive(false);
        }

        // Restore Camera.main to player hierarchy
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.transform.SetParent(_savedCamParent, false);
            cam.transform.localPosition = _savedCamLocalPos;
            cam.transform.localRotation = _savedCamLocalRot;
            cam.fieldOfView = _savedCamFov;
        }

        var fpc = FindAnyObjectByType<StarterAssets.FirstPersonController>();
        if (fpc != null) fpc.enabled = true;
        var combatMove = FindAnyObjectByType<OasisCombatMovement>();
        if (combatMove != null) combatMove.enabled = true;
        var weapon = FindAnyObjectByType<BlasterWeapon>();
        if (weapon != null)
        {
            weapon.enabled = true;
            weapon.SetWeaponVisualActive(true);
        }

        // If player is not yet positioned inside the playable map, place them at the selected item or a safe bunker
        Transform playerT = OasisPlayerHelper.FindPlayerTransform();
        if (playerT != null)
        {
            Vector3 pPos = playerT.position;
            bool outOfArena = (pPos.x < -105f || pPos.x > 115f || pPos.z < -265f || pPos.z > -60f || pPos.y < -6.5f);
            if (_wasCharacterSelectionActive || outOfArena)
            {
                if (_selectedItem != null && _selectedItem.gameObject.activeInHierarchy)
                {
                    TeleportPlayerToSelected();
                }
                else if (OasisSpawnSystem.Instance != null)
                {
                    int myTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
                    OasisSpawnSystem.Instance.RespawnCombatant(playerT, myTeam);
                }
            }
        }

        // Always enter Peaceful Map Exploration Mode (zero enemy fire) when pressing SAVE & PLAY / [F10]!
        if (_wasCharacterSelectionActive && OasisCharacterCustomizer.Instance != null && OasisCharacterCustomizer.Instance.isSelectionActive)
        {
            OasisCharacterCustomizer.Instance.EnterExplorationFromSelection();
        }
        else if (OasisCharacterCustomizer.Instance != null && _wasCharacterSelectionActive)
        {
            OasisCharacterCustomizer.Instance.EnterExplorationFromSelection();
        }
        else if (OasisShooterManager.Instance != null)
        {
            OasisShooterManager.Instance.EnterMapExplorationMode();
        }

        _wasCharacterSelectionActive = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void SaveAndStartCombatMatch()
    {
        if (!isDesignerActive) return;
        isDesignerActive = false;

        SaveCustomLayout();
        if (OasisSpawnSystem.Instance != null)
        {
            OasisSpawnSystem.Instance.RebuildHangarSpawnsFromScene();
        }

        if (_designerCanvas != null)
        {
            Destroy(_designerCanvas);
            _designerCanvas = null;
        }

        if (_gizmoRoot != null)
        {
            _gizmoRoot.SetActive(false);
        }

        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.transform.SetParent(_savedCamParent, false);
            cam.transform.localPosition = _savedCamLocalPos;
            cam.transform.localRotation = _savedCamLocalRot;
            cam.fieldOfView = _savedCamFov;
        }

        var fpc = FindAnyObjectByType<StarterAssets.FirstPersonController>();
        if (fpc != null) fpc.enabled = true;
        var combatMove = FindAnyObjectByType<OasisCombatMovement>();
        if (combatMove != null) combatMove.enabled = true;
        var weapon = FindAnyObjectByType<BlasterWeapon>();
        if (weapon != null)
        {
            weapon.enabled = true;
            weapon.SetWeaponVisualActive(true);
        }

        if (_wasCharacterSelectionActive && OasisCharacterCustomizer.Instance != null)
        {
            OasisCharacterCustomizer.Instance.ConfirmAndEnterGame();
        }
        else if (OasisShooterManager.Instance != null)
        {
            OasisShooterManager.Instance.SetHUDActive(true);
            OasisShooterManager.Instance.StartTeamBattle();
        }

        if (OasisMapSystem.Instance != null)
        {
            OasisMapSystem.Instance.SetMapCanvasActive(true);
        }

        _wasCharacterSelectionActive = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // =========================================================================
    // CAMERA & VIEWPORT CONTROLS
    // =========================================================================

    private void HandleDesignerCameraControls()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        float dt = Time.unscaledDeltaTime;
        bool rmbHeld = false;
        Vector2 mouseDelta = Vector2.zero;
        Vector3 moveInput = Vector3.zero;
        bool fast = false;

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            rmbHeld = Mouse.current.rightButton.isPressed;
            mouseDelta = Mouse.current.delta.ReadValue();
        }
        if (Keyboard.current != null)
        {
            var kb = Keyboard.current;
            if (kb.wKey.isPressed) moveInput.z += 1f;
            if (kb.sKey.isPressed) moveInput.z -= 1f;
            if (kb.dKey.isPressed) moveInput.x += 1f;
            if (kb.aKey.isPressed) moveInput.x -= 1f;
            if (kb.spaceKey.isPressed) moveInput.y += 1f;
            if (kb.cKey.isPressed || kb.leftCtrlKey.isPressed) moveInput.y -= 1f;
            fast = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;

            if (kb.vKey.wasPressedThisFrame) ToggleOverheadView();
            if (kb.fKey.wasPressedThisFrame) FocusSelectedItem();
        }
#else
        rmbHeld = Input.GetMouseButton(1);
        mouseDelta = new Vector2(Input.GetAxisRaw("Mouse X") * 10f, Input.GetAxisRaw("Mouse Y") * 10f);
        if (Input.GetKey(KeyCode.W)) moveInput.z += 1f;
        if (Input.GetKey(KeyCode.S)) moveInput.z -= 1f;
        if (Input.GetKey(KeyCode.D)) moveInput.x += 1f;
        if (Input.GetKey(KeyCode.A)) moveInput.x -= 1f;
        if (Input.GetKey(KeyCode.Space)) moveInput.y += 1f;
        if (Input.GetKey(KeyCode.C) || Input.GetKey(KeyCode.LeftControl)) moveInput.y -= 1f;
        fast = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (Input.GetKeyDown(KeyCode.V)) ToggleOverheadView();
        if (Input.GetKeyDown(KeyCode.F)) FocusSelectedItem();
#endif

        if (rmbHeld && !isOverheadEagleView)
        {
            _camYaw += mouseDelta.x * 0.22f;
            _camPitch = Mathf.Clamp(_camPitch - mouseDelta.y * 0.22f, -15f, 86f);
        }

        float moveSpeed = (fast ? 58f : 24f) * dt;
        if (isOverheadEagleView)
        {
            // Top-down map pan
            Quaternion flatRot = Quaternion.Euler(0f, _camYaw, 0f);
            Vector3 pan = flatRot * new Vector3(moveInput.x, 0f, moveInput.z);
            _camPos += pan * (moveSpeed * 1.6f);
            _camPos.y = Mathf.Clamp(_camPos.y + moveInput.y * moveSpeed, 25f, 165f);
            _camPitch = 85f;
        }
        else
        {
            Quaternion camRot = Quaternion.Euler(_camPitch, _camYaw, 0f);
            Vector3 forward = camRot * Vector3.forward;
            Vector3 right = camRot * Vector3.right;
            _camPos += (forward * moveInput.z + right * moveInput.x + Vector3.up * moveInput.y) * moveSpeed;
            _camPos.y = Mathf.Clamp(_camPos.y, -4.0f, 150f);
        }

        ApplyCameraTransform(cam);
    }

    private void ApplyCameraTransform(Camera cam)
    {
        if (cam == null) return;
        cam.transform.position = _camPos;
        cam.transform.rotation = Quaternion.Euler(_camPitch, _camYaw, 0f);
    }

    public void ToggleOverheadView()
    {
        isOverheadEagleView = !isOverheadEagleView;
        if (isOverheadEagleView)
        {
            Vector3 center = (_selectedItem != null) ? _selectedItem.transform.position : new Vector3(0f, -2f, -150f);
            _camPos = new Vector3(center.x, 95f, center.z - 8f);
            _camPitch = 85f;
            _camYaw = 0f;
            ShowStatus("<color=#26D9FF>OVERHEAD EAGLE VIEW ENABLED:</color> Easily drag bunkers & towers across the entire map!", 3.5f);
        }
        else
        {
            _camPitch = 38f;
            if (_camPos.y > 45f) _camPos.y = 28f;
            ShowStatus("<color=#26D9FF>3D PERSPECTIVE VIEW ENABLED:</color> Hold [Right-Click] + [WASD] to fly down and inspect doorways.", 3.5f);
        }

        if (_viewModeBtnBg != null)
        {
            _viewModeBtnBg.color = isOverheadEagleView
                ? new Color(0.14f, 0.56f, 0.78f, 0.98f)
                : new Color(0.10f, 0.16f, 0.24f, 0.95f);
        }
        if (_viewModeBtnText != null)
        {
            _viewModeBtnText.text = isOverheadEagleView ? "🦅 VIEW: TOP-DOWN [V]" : "🎥 VIEW: 3D FLY [V]";
        }
    }

    public void FocusSelectedItem()
    {
        if (_selectedItem == null) return;
        Vector3 target = _selectedItem.transform.position;
        if (isOverheadEagleView)
        {
            _camPos = new Vector3(target.x, 80f, target.z - 6f);
        }
        else
        {
            Quaternion rot = Quaternion.Euler(0f, _camYaw, 0f);
            _camPos = target - (rot * Vector3.forward * 22f) + Vector3.up * 14f;
            _camPitch = 30f;
        }
        ApplyCameraTransform(Camera.main);
        ShowStatus($"Focused camera on <b>{_selectedItem.displayName}</b>", 2.0f);
    }

    // =========================================================================
    // 3D SELECTION, GROUND DRAGGING & KEYBOARD SHORTCUTS
    // =========================================================================

    private bool IsPointerOverDesignerUI(Vector2 screenPos)
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return true;
        // Left panel (0..0.22), Right panel (0.76..1.0), Top bar (0.91..1.0)
        float nx = screenPos.x / Mathf.Max(1f, Screen.width);
        float ny = screenPos.y / Mathf.Max(1f, Screen.height);
        if (ny > 0.90f) return true;
        if (nx < 0.225f || nx > 0.755f) return true;
        return false;
    }

    private void HandleViewportSelectionAndDragging()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        bool lmbDown = false;
        bool lmbHeld = false;
        bool lmbUp = false;
        Vector2 mousePos = Vector2.zero;
        float scrollDelta = 0f;

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            lmbDown = Mouse.current.leftButton.wasPressedThisFrame;
            lmbHeld = Mouse.current.leftButton.isPressed;
            lmbUp = Mouse.current.leftButton.wasReleasedThisFrame;
            mousePos = Mouse.current.position.ReadValue();
            scrollDelta = Mouse.current.scroll.ReadValue().y;
        }
#else
        lmbDown = Input.GetMouseButtonDown(0);
        lmbHeld = Input.GetMouseButton(0);
        lmbUp = Input.GetMouseButtonUp(0);
        mousePos = Input.mousePosition;
        scrollDelta = Input.mouseScrollDelta.y * 120f;
#endif

        bool overUI = IsPointerOverDesignerUI(mousePos);

        // Scroll wheel adjusts selected item height offset when cursor is in the 3D viewport
        if (!overUI && _selectedItem != null && Mathf.Abs(scrollDelta) > 0.01f)
        {
            float step = (scrollDelta > 0f) ? 0.25f : -0.25f;
            NudgeSelectedHeight(step);
        }

        if (lmbDown && !overUI)
        {
            Ray ray = cam.ScreenPointToRay(mousePos);
            RaycastHit[] hits = Physics.RaycastAll(ray, 500f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            OasisMapEditableItem clickedItem = null;
            foreach (var h in hits)
            {
                if (h.collider == null) continue;
                var editable = h.collider.GetComponentInParent<OasisMapEditableItem>();
                if (editable != null && editable.gameObject.activeInHierarchy)
                {
                    clickedItem = editable;
                    break;
                }
            }

            if (clickedItem != null)
            {
                SelectItem(clickedItem);
                if (dragWithLeftMouse)
                {
                    _isDraggingSelected = true;
                    if (TryRaycastTerrainPoint(ray, out Vector3 groundHit))
                    {
                        _dragOffsetXZ = new Vector3(
                            _selectedItem.transform.position.x - groundHit.x,
                            0f,
                            _selectedItem.transform.position.z - groundHit.z
                        );
                    }
                    else
                    {
                        _dragOffsetXZ = Vector3.zero;
                    }
                }
            }
            else if (dragWithLeftMouse && _selectedItem != null && TryRaycastTerrainPoint(ray, out Vector3 directTerrainPt))
            {
                // Clicking terrain while Drag Mode is on moves the selected item directly to that spot
                _isDraggingSelected = true;
                _dragOffsetXZ = Vector3.zero;
                MoveSelectedItemToXZ(directTerrainPt.x, directTerrainPt.z);
            }
        }
        else if (lmbHeld && _isDraggingSelected && _selectedItem != null)
        {
            Ray ray = cam.ScreenPointToRay(mousePos);
            if (TryRaycastTerrainPoint(ray, out Vector3 terrainPt))
            {
                MoveSelectedItemToXZ(terrainPt.x + _dragOffsetXZ.x, terrainPt.z + _dragOffsetXZ.z);
            }
        }
        else if (lmbUp)
        {
            if (_isDraggingSelected && _selectedItem != null)
            {
                _isDraggingSelected = false;
                if (OasisSpawnSystem.Instance != null)
                {
                    OasisSpawnSystem.Instance.RebuildHangarSpawnsFromScene();
                }
            }
        }
    }

    private bool TryRaycastTerrainPoint(Ray ray, out Vector3 terrainPoint)
    {
        terrainPoint = Vector3.zero;
        if (OasisWorldExpander.Instance != null)
        {
            RaycastHit[] hits = Physics.RaycastAll(ray, 600f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider == null) continue;
                string n = h.collider.gameObject.name;
                if ((n.IndexOf("Terrain_01", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     n.IndexOf("Polybrush", StringComparison.OrdinalIgnoreCase) >= 0) &&
                    n.IndexOf("HangarFloor", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    terrainPoint = h.point;
                    return true;
                }
            }
        }

        // Horizontal plane fallback at y = -2.0f
        Plane groundPlane = new Plane(Vector3.up, new Vector3(0f, -2.0f, 0f));
        if (groundPlane.Raycast(ray, out float enter))
        {
            terrainPoint = ray.GetPoint(enter);
            return true;
        }
        return false;
    }

    private void HandleKeyboardTransformShortcuts()
    {
        if (_selectedItem == null) return;

        bool shift = false;
        bool rotLeft = false;
        bool rotRight = false;
        bool snapGround = false;
        bool teleportPlayer = false;
        bool deletePressed = false;
        Vector2 nudgeXZ = Vector2.zero;
        float nudgeY = 0f;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            var kb = Keyboard.current;
            shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            bool rmb = Mouse.current != null && Mouse.current.rightButton.isPressed;

            if (!rmb)
            {
                if (kb.qKey.wasPressedThisFrame || kb.leftBracketKey.wasPressedThisFrame) rotLeft = true;
                if (kb.eKey.wasPressedThisFrame || kb.rightBracketKey.wasPressedThisFrame) rotRight = true;
            }
            if (kb.rKey.wasPressedThisFrame) rotRight = true;
            if (kb.gKey.wasPressedThisFrame) snapGround = true;
            if (kb.pKey.wasPressedThisFrame) teleportPlayer = true;
            if (kb.deleteKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) deletePressed = true;

            if (kb.upArrowKey.wasPressedThisFrame) nudgeXZ.y += 1f;
            if (kb.downArrowKey.wasPressedThisFrame) nudgeXZ.y -= 1f;
            if (kb.rightArrowKey.wasPressedThisFrame) nudgeXZ.x += 1f;
            if (kb.leftArrowKey.wasPressedThisFrame) nudgeXZ.x -= 1f;
            if (kb.pageUpKey.wasPressedThisFrame) nudgeY += 0.25f;
            if (kb.pageDownKey.wasPressedThisFrame) nudgeY -= 0.25f;
        }
#else
        shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (!Input.GetMouseButton(1))
        {
            if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.LeftBracket)) rotLeft = true;
            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.RightBracket)) rotRight = true;
        }
        if (Input.GetKeyDown(KeyCode.R)) rotRight = true;
        if (Input.GetKeyDown(KeyCode.G)) snapGround = true;
        if (Input.GetKeyDown(KeyCode.P)) teleportPlayer = true;
        if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace)) deletePressed = true;

        if (Input.GetKeyDown(KeyCode.UpArrow)) nudgeXZ.y += 1f;
        if (Input.GetKeyDown(KeyCode.DownArrow)) nudgeXZ.y -= 1f;
        if (Input.GetKeyDown(KeyCode.RightArrow)) nudgeXZ.x += 1f;
        if (Input.GetKeyDown(KeyCode.LeftArrow)) nudgeXZ.x -= 1f;
        if (Input.GetKeyDown(KeyCode.PageUp)) nudgeY += 0.25f;
        if (Input.GetKeyDown(KeyCode.PageDown)) nudgeY -= 0.25f;
#endif

        float stepXZ = shift ? 2.5f : 0.75f;
        if (nudgeXZ.sqrMagnitude > 0.01f)
        {
            NudgeSelectedXZ(nudgeXZ.x * stepXZ, nudgeXZ.y * stepXZ);
        }
        if (Mathf.Abs(nudgeY) > 0.01f)
        {
            NudgeSelectedHeight(nudgeY);
        }
        if (rotLeft) RotateSelectedYaw(shift ? -45f : -15f);
        if (rotRight) RotateSelectedYaw(shift ? 45f : 15f);
        if (snapGround) SnapSelectedFlushToGround();
        if (teleportPlayer) TeleportPlayerToSelected();
        if (deletePressed) DeleteSelectedItem();
    }

    // =========================================================================
    // ITEM MANIPULATION API
    // =========================================================================

    public void SelectItem(OasisMapEditableItem item)
    {
        _selectedItem = item;
        RefreshItemListHighlight();
        UpdateSelectionGizmoVisuals();
        UpdateInspectorReadout();
    }

    public void MoveSelectedItemToXZ(float worldX, float worldZ)
    {
        if (_selectedItem == null) return;
        // Clamp within the expanded playable desert basin (lined with rock walls at the perimeter)
        worldX = Mathf.Clamp(worldX, -98f, 110f);
        worldZ = Mathf.Clamp(worldZ, -262f, -64f);

        Vector3 pos = _selectedItem.transform.position;
        _selectedItem.transform.position = new Vector3(worldX, pos.y, worldZ);

        if (autoSnapToGround && OasisWorldExpander.Instance != null)
        {
            OasisWorldExpander.Instance.SnapBuildingFlushToGround(_selectedItem.transform, _selectedItem.heightOffset);
        }
        else
        {
            Physics.SyncTransforms();
        }
    }

    public void NudgeSelectedXZ(float dx, float dz)
    {
        if (_selectedItem == null) return;
        Vector3 pos = _selectedItem.transform.position;
        MoveSelectedItemToXZ(pos.x + dx, pos.z + dz);
        if (OasisSpawnSystem.Instance != null) OasisSpawnSystem.Instance.RebuildHangarSpawnsFromScene();
    }

    public void NudgeSelectedHeight(float dy)
    {
        if (_selectedItem == null) return;
        _selectedItem.heightOffset = Mathf.Clamp(_selectedItem.heightOffset + dy, -6.0f, 12.0f);
        if (autoSnapToGround && OasisWorldExpander.Instance != null)
        {
            OasisWorldExpander.Instance.SnapBuildingFlushToGround(_selectedItem.transform, _selectedItem.heightOffset);
        }
        else
        {
            _selectedItem.transform.position += Vector3.up * dy;
            Physics.SyncTransforms();
        }
        ShowStatus($"Adjusted <b>{_selectedItem.displayName}</b> height offset to <b>{_selectedItem.heightOffset:+0.00;-0.00;0.00}m</b>", 2.0f);
    }

    public void SnapSelectedFlushToGround()
    {
        if (_selectedItem == null) return;
        _selectedItem.heightOffset = 0f;
        if (OasisWorldExpander.Instance != null)
        {
            OasisWorldExpander.Instance.SnapBuildingFlushToGround(_selectedItem.transform, 0f);
        }
        if (OasisSpawnSystem.Instance != null) OasisSpawnSystem.Instance.RebuildHangarSpawnsFromScene();
        ShowStatus($"<color=#38EF7D>Snapped <b>{_selectedItem.displayName}</b> flush to authentic Terrain_01 floor!</color>", 2.5f);
    }

    public void RotateSelectedYaw(float deltaDegrees)
    {
        if (_selectedItem == null) return;
        _selectedItem.transform.Rotate(0f, deltaDegrees, 0f, Space.World);
        if (autoSnapToGround && OasisWorldExpander.Instance != null)
        {
            OasisWorldExpander.Instance.SnapBuildingFlushToGround(_selectedItem.transform, _selectedItem.heightOffset);
        }
        if (OasisSpawnSystem.Instance != null) OasisSpawnSystem.Instance.RebuildHangarSpawnsFromScene();
    }

    public void RotateSelectedToFaceOasisCenter()
    {
        if (_selectedItem == null) return;
        Vector3 center = (OasisSpawnSystem.Instance != null) ? OasisSpawnSystem.Instance.GetPlayableCenter() : new Vector3(0f, -2f, -150f);
        Vector3 toCenter = center - _selectedItem.transform.position;
        toCenter.y = 0f;
        if (toCenter.sqrMagnitude > 0.01f)
        {
            _selectedItem.transform.rotation = Quaternion.LookRotation(toCenter.normalized);
            if (autoSnapToGround && OasisWorldExpander.Instance != null)
            {
                OasisWorldExpander.Instance.SnapBuildingFlushToGround(_selectedItem.transform, _selectedItem.heightOffset);
            }
            if (OasisSpawnSystem.Instance != null) OasisSpawnSystem.Instance.RebuildHangarSpawnsFromScene();
            ShowStatus($"Oriented <b>{_selectedItem.displayName}</b> entrance toward the central Oasis!", 2.5f);
        }
    }

    public void ScaleSelectedItem(float factor)
    {
        if (_selectedItem == null) return;
        if (Mathf.Approximately(factor, 0f))
        {
            _selectedItem.transform.localScale = _selectedItem.defaultScale;
        }
        else
        {
            Vector3 s = _selectedItem.transform.localScale * factor;
            s.x = Mathf.Clamp(s.x, 0.35f, 3.0f);
            s.y = Mathf.Clamp(s.y, 0.35f, 3.0f);
            s.z = Mathf.Clamp(s.z, 0.35f, 3.0f);
            _selectedItem.transform.localScale = s;
        }
        Physics.SyncTransforms();
    }

    public void ResetSelectedItemToDefault()
    {
        if (_selectedItem == null) return;
        _selectedItem.heightOffset = 0f;
        _selectedItem.transform.position = _selectedItem.defaultPosition;
        _selectedItem.transform.rotation = _selectedItem.defaultRotation;
        _selectedItem.transform.localScale = _selectedItem.defaultScale;
        if (OasisWorldExpander.Instance != null)
        {
            OasisWorldExpander.Instance.SnapBuildingFlushToGround(_selectedItem.transform, 0f);
        }
        if (OasisSpawnSystem.Instance != null) OasisSpawnSystem.Instance.RebuildHangarSpawnsFromScene();
        ShowStatus($"Reset <b>{_selectedItem.displayName}</b> to its default position.", 2.5f);
    }

    public void TeleportPlayerToSelected()
    {
        if (_selectedItem == null) return;
        Transform playerT = OasisPlayerHelper.FindPlayerTransform();
        if (playerT == null) return;

        bool isHangar = _selectedItem.name.IndexOf("Hangar_Hideout", StringComparison.OrdinalIgnoreCase) >= 0;
        Vector3 targetPos = isHangar
            ? _selectedItem.transform.TransformPoint(new Vector3(0f, 0.35f, -1.5f))
            : _selectedItem.transform.position + _selectedItem.transform.forward * 6.5f + Vector3.up * 0.5f;

        if (!isHangar && OasisWorldExpander.Instance != null &&
            OasisWorldExpander.Instance.TrySampleAuthenticTerrainY(targetPos.x, targetPos.z, out float ty))
        {
            targetPos.y = ty + 0.30f;
        }

        var cc = playerT.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        playerT.position = targetPos;
        playerT.rotation = _selectedItem.transform.rotation;
        Physics.SyncTransforms();
        if (cc != null) cc.enabled = true;

        ShowStatus($"<color=#38EF7D><b>PLAYER TELEPORTED</b></color> to <b>{_selectedItem.displayName}</b>! Press [F10] or click 'SAVE & TEST' to walk around.", 3.5f);
    }

    public void SpawnNewMapItem(string prefabType)
    {
        if (OasisWorldExpander.Instance == null) return;

        // Place at center of camera's ground view
        Camera cam = Camera.main;
        Vector3 spawnPos = new Vector3(0f, -2f, -150f);
        if (cam != null)
        {
            Ray centerRay = new Ray(cam.transform.position, cam.transform.forward);
            if (TryRaycastTerrainPoint(centerRay, out Vector3 hitPt))
            {
                spawnPos = hitPt;
            }
            else
            {
                spawnPos = new Vector3(_camPos.x, -2f, _camPos.z + 18f);
            }
        }

        Vector3 center = (OasisSpawnSystem.Instance != null) ? OasisSpawnSystem.Instance.GetPlayableCenter() : new Vector3(0f, -2f, -150f);
        Vector3 toCenter = (center - spawnPos);
        toCenter.y = 0f;
        Quaternion rot = (toCenter.sqrMagnitude > 0.01f) ? Quaternion.LookRotation(toCenter.normalized) : Quaternion.identity;

        int id = _nextCustomSpawnId++;
        GameObject spawned = OasisWorldExpander.Instance.SpawnCustomLandmarkForDesigner(prefabType, id, spawnPos, rot);
        if (spawned != null)
        {
            OasisMapEditableItem editable = RegisterEditableChild(spawned, true, prefabType);
            editable.displayName = $"Custom {prefabType} #{id}";
            RebuildItemListUI();
            SelectItem(editable);
            if (OasisSpawnSystem.Instance != null) OasisSpawnSystem.Instance.RebuildHangarSpawnsFromScene();
            ShowStatus($"<color=#38EF7D>Spawned new <b>{editable.displayName}</b>!</color> Drag it with the mouse to position.", 3.5f);
        }
    }

    public void DeleteSelectedItem()
    {
        if (_selectedItem == null) return;

        // Ensure at least 4 active bunkers remain so all 4 teams have undercover spawn points
        if (_selectedItem.category == "BUNKERS")
        {
            int activeBunkers = 0;
            for (int i = 0; i < _editableItems.Count; i++)
            {
                if (_editableItems[i] != null && _editableItems[i].gameObject.activeSelf && _editableItems[i].category == "BUNKERS")
                    activeBunkers++;
            }
            if (activeBunkers <= 4)
            {
                ShowStatus("<color=#FF6B6B>Cannot delete bunker: At least 4 Undercover Bunkers are required for team spawns! Move it instead.</color>", 3.5f);
                return;
            }
        }

        string removedName = _selectedItem.displayName;
        _selectedItem.gameObject.SetActive(false);

        OasisMapEditableItem nextSel = null;
        for (int i = 0; i < _editableItems.Count; i++)
        {
            if (_editableItems[i] != null && _editableItems[i].gameObject.activeSelf)
            {
                nextSel = _editableItems[i];
                break;
            }
        }

        RebuildItemListUI();
        SelectItem(nextSel);
        if (OasisSpawnSystem.Instance != null) OasisSpawnSystem.Instance.RebuildHangarSpawnsFromScene();
        ShowStatus($"<color=#FFBD2E>Removed <b>{removedName}</b> from the map.</color>", 2.5f);
    }

    // =========================================================================
    // PERSISTENT SAVE / LOAD / RESET
    // =========================================================================

    public void SaveCustomLayout()
    {
        SavedMapLayoutData data = new SavedMapLayoutData();
        for (int i = 0; i < _editableItems.Count; i++)
        {
            var item = _editableItems[i];
            if (item == null) continue;
            data.items.Add(new SavedItemEntry
            {
                itemId = item.itemId,
                displayName = item.displayName,
                category = item.category,
                prefabType = item.prefabType,
                isCustomSpawned = item.isCustomSpawned,
                isDeleted = !item.gameObject.activeSelf,
                position = item.transform.position,
                eulerAngles = item.transform.eulerAngles,
                scale = item.transform.localScale,
                heightOffset = item.heightOffset
            });
        }

        string json = JsonUtility.ToJson(data, true);
        PlayerPrefs.SetString(PlayerPrefsLayoutKey, json);
        PlayerPrefs.Save();

        try
        {
            File.WriteAllText(CustomLayoutFilePath, json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[OasisMapDesigner] Could not write layout JSON file: {ex.Message}");
        }

        if (OasisSpawnSystem.Instance != null)
        {
            OasisSpawnSystem.Instance.RebuildHangarSpawnsFromScene();
        }

        ShowStatus($"<color=#38EF7D><b>✔ SAVED CUSTOM MAP LAYOUT!</b></color> ({data.items.Count} items saved to disk & applied to spawns)", 3.5f);
        Debug.Log($"<color=#38EF7D>[OasisMapDesigner]</color> Saved custom map layout ({data.items.Count} items) to {CustomLayoutFilePath}");
    }

    public void ApplySavedCustomLayout()
    {
        string json = "";
        try
        {
            if (File.Exists(CustomLayoutFilePath))
            {
                json = File.ReadAllText(CustomLayoutFilePath);
            }
        }
        catch { }

        if (string.IsNullOrEmpty(json))
        {
            json = PlayerPrefs.GetString(PlayerPrefsLayoutKey, "");
        }

        if (string.IsNullOrEmpty(json)) return;

        try
        {
            SavedMapLayoutData data = JsonUtility.FromJson<SavedMapLayoutData>(json);
            if (data == null || data.items == null || data.items.Count == 0) return;

            Dictionary<string, OasisMapEditableItem> mapById = new Dictionary<string, OasisMapEditableItem>();
            for (int i = 0; i < _editableItems.Count; i++)
            {
                var it = _editableItems[i];
                if (it != null && !string.IsNullOrEmpty(it.itemId))
                {
                    mapById[it.itemId] = it;
                }
            }

            foreach (var entry in data.items)
            {
                if (entry == null || string.IsNullOrEmpty(entry.itemId)) continue;

                if (mapById.TryGetValue(entry.itemId, out OasisMapEditableItem existing))
                {
                    existing.gameObject.SetActive(!entry.isDeleted);
                    existing.heightOffset = entry.heightOffset;
                    existing.transform.position = entry.position;
                    existing.transform.eulerAngles = entry.eulerAngles;
                    if (entry.scale.sqrMagnitude > 0.01f) existing.transform.localScale = entry.scale;

                    if (existing.gameObject.activeSelf && OasisWorldExpander.Instance != null &&
                        existing.name.IndexOf("Hangar_Hideout", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        OasisWorldExpander.Instance.ClearSceneObstaclesAroundBuilding(existing.transform, 20f, 19f);
                    }
                }
                else if (entry.isCustomSpawned && !entry.isDeleted && OasisWorldExpander.Instance != null)
                {
                    int cId = _nextCustomSpawnId++;
                    GameObject spawned = OasisWorldExpander.Instance.SpawnCustomLandmarkForDesigner(
                        string.IsNullOrEmpty(entry.prefabType) ? "CoverStation" : entry.prefabType,
                        cId,
                        entry.position,
                        Quaternion.Euler(entry.eulerAngles)
                    );
                    if (spawned != null)
                    {
                        spawned.name = entry.itemId;
                        spawned.transform.position = entry.position;
                        spawned.transform.eulerAngles = entry.eulerAngles;
                        if (entry.scale.sqrMagnitude > 0.01f) spawned.transform.localScale = entry.scale;
                        var editable = RegisterEditableChild(spawned, true, entry.prefabType);
                        editable.itemId = entry.itemId;
                        editable.displayName = string.IsNullOrEmpty(entry.displayName) ? entry.itemId : entry.displayName;
                        editable.heightOffset = entry.heightOffset;
                    }
                }
            }

            Physics.SyncTransforms();
            if (OasisSpawnSystem.Instance != null)
            {
                OasisSpawnSystem.Instance.RebuildHangarSpawnsFromScene();
            }
            Debug.Log($"<color=#38EF7D>[OasisMapDesigner]</color> Loaded & applied custom user map layout ({data.items.Count} entries).");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[OasisMapDesigner] Failed to parse saved custom map layout: {ex.Message}");
        }
    }

    public void ResetEntireMapToDefaults()
    {
        PlayerPrefs.DeleteKey(PlayerPrefsLayoutKey);
        PlayerPrefs.Save();
        try
        {
            if (File.Exists(CustomLayoutFilePath)) File.Delete(CustomLayoutFilePath);
        }
        catch { }

        for (int i = _editableItems.Count - 1; i >= 0; i--)
        {
            var item = _editableItems[i];
            if (item == null) continue;
            if (item.isCustomSpawned)
            {
                Destroy(item.gameObject);
                _editableItems.RemoveAt(i);
            }
            else
            {
                item.gameObject.SetActive(true);
                item.heightOffset = 0f;
                item.transform.position = item.defaultPosition;
                item.transform.rotation = item.defaultRotation;
                item.transform.localScale = item.defaultScale;
            }
        }

        Physics.SyncTransforms();
        if (OasisSpawnSystem.Instance != null)
        {
            OasisSpawnSystem.Instance.RebuildHangarSpawnsFromScene();
        }
        RebuildItemListUI();
        if (_editableItems.Count > 0) SelectItem(_editableItems[0]);
        ShowStatus("<color=#26D9FF><b>MAP RESET TO DEFAULT LAYOUT!</b></color> All buildings & bunkers restored.", 3.5f);
    }

    // =========================================================================
    // 3D SELECTION WIREFRAME BOX & ENTRANCE ARROW GIZMO
    // =========================================================================

    private void EnsureGizmoCreated()
    {
        if (_gizmoRoot != null)
        {
            _gizmoRoot.SetActive(true);
            return;
        }

        _gizmoRoot = new GameObject("Oasis_MapDesigner_SelectionGizmo");
        Material lineMat = new Material(OasisShaderHelper.GetUnlitShader());
        lineMat.color = new Color(0.15f, 0.95f, 1.0f, 1f);

        for (int i = 0; i < 12; i++)
        {
            GameObject edgeObj = new GameObject($"GizmoEdge_{i}");
            edgeObj.transform.SetParent(_gizmoRoot.transform, false);
            LineRenderer lr = edgeObj.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.startWidth = 0.16f;
            lr.endWidth = 0.16f;
            lr.sharedMaterial = lineMat;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _gizmoBoxLines[i] = lr;
        }

        Material arrowMat = new Material(OasisShaderHelper.GetUnlitShader());
        arrowMat.color = new Color(0.22f, 1.0f, 0.48f, 1f);

        GameObject arrowObj = new GameObject("GizmoForwardArrow");
        arrowObj.transform.SetParent(_gizmoRoot.transform, false);
        _gizmoForwardArrowLine = arrowObj.AddComponent<LineRenderer>();
        _gizmoForwardArrowLine.positionCount = 2;
        _gizmoForwardArrowLine.startWidth = 0.32f;
        _gizmoForwardArrowLine.endWidth = 0.32f;
        _gizmoForwardArrowLine.sharedMaterial = arrowMat;
        _gizmoForwardArrowLine.useWorldSpace = true;

        _gizmoForwardCone = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _gizmoForwardCone.name = "GizmoForwardTip";
        _gizmoForwardCone.transform.SetParent(_gizmoRoot.transform, false);
        _gizmoForwardCone.transform.localScale = new Vector3(1.1f, 0.45f, 1.4f);
        _gizmoForwardCone.GetComponent<MeshRenderer>().sharedMaterial = arrowMat;
        Destroy(_gizmoForwardCone.GetComponent<Collider>());
    }

    private void UpdateSelectionGizmoVisuals()
    {
        if (_gizmoRoot == null) return;
        if (_selectedItem == null || !_selectedItem.gameObject.activeInHierarchy)
        {
            _gizmoRoot.SetActive(false);
            return;
        }

        _gizmoRoot.SetActive(true);
        Transform t = _selectedItem.transform;

        // Compute local bounds size based on category
        Vector3 halfExtents = new Vector3(4.5f, 3.0f, 4.5f);
        Vector3 centerOffset = new Vector3(0f, 2.5f, 0f);
        string n = _selectedItem.name;
        if (n.IndexOf("Hangar_Hideout", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            halfExtents = new Vector3(7.6f, 3.2f, 6.5f);
            centerOffset = new Vector3(0f, 2.6f, 0.5f);
        }
        else if (n.IndexOf("Watchtower", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            halfExtents = new Vector3(5.6f, 4.5f, 5.6f);
            centerOffset = new Vector3(0f, 4.2f, 0f);
        }
        else if (n.IndexOf("Skybridge", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            halfExtents = new Vector3(6.5f, 3.2f, 13.8f);
            centerOffset = new Vector3(0f, 2.8f, 0f);
        }
        else if (n.IndexOf("Citadel", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            halfExtents = new Vector3(8.5f, 4.5f, 6.5f);
            centerOffset = new Vector3(0f, 4.0f, 0f);
        }

        Vector3[] c = new Vector3[8];
        int idx = 0;
        for (int y = -1; y <= 1; y += 2)
        {
            for (int z = -1; z <= 1; z += 2)
            {
                for (int x = -1; x <= 1; x += 2)
                {
                    Vector3 localPt = centerOffset + new Vector3(x * halfExtents.x, y * halfExtents.y, z * halfExtents.z);
                    c[idx++] = t.TransformPoint(localPt);
                }
            }
        }

        int[,] edges = new int[,]
        {
            {0,1}, {1,3}, {3,2}, {2,0}, // Bottom
            {4,5}, {5,7}, {7,6}, {6,4}, // Top
            {0,4}, {1,5}, {2,6}, {3,7}  // Verticals
        };

        for (int e = 0; e < 12; e++)
        {
            _gizmoBoxLines[e].SetPosition(0, c[edges[e, 0]]);
            _gizmoBoxLines[e].SetPosition(1, c[edges[e, 1]]);
        }

        // Forward Entrance Arrow (+Z)
        Vector3 arrowStart = t.TransformPoint(new Vector3(0f, 1.2f, halfExtents.z * 0.4f));
        Vector3 arrowEnd = t.TransformPoint(new Vector3(0f, 1.2f, halfExtents.z + 4.8f));
        _gizmoForwardArrowLine.SetPosition(0, arrowStart);
        _gizmoForwardArrowLine.SetPosition(1, arrowEnd);
        _gizmoForwardCone.transform.position = arrowEnd;
        _gizmoForwardCone.transform.rotation = t.rotation;
    }

    // =========================================================================
    // DESIGNER STUDIO UI
    // =========================================================================

    private void BuildDesignerUI()
    {
        if (_designerCanvas != null) Destroy(_designerCanvas);

        _designerCanvas = new GameObject("Oasis_MapDesigner_Canvas");
        Canvas canvas = _designerCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 600;

        CanvasScaler scaler = _designerCanvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        _designerCanvas.AddComponent<GraphicRaycaster>();
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

        // 1. TOP BAR (0.0 .. 1.0, Y: 0.915 .. 1.0)
        GameObject topBar = CreatePanel(_designerCanvas.transform, "Top_Bar", new Vector2(0f, 0.915f), new Vector2(1f, 1f), new Color(0.03f, 0.05f, 0.09f, 0.96f));
        CreatePanel(topBar.transform, "BottomLine", new Vector2(0f, 0f), new Vector2(1f, 0.04f), new Color(0.15f, 0.88f, 1.0f, 0.85f));

        CreateLabel(topBar.transform, font, "Title",
            "🛠 OASIS INTERACTIVE MAP DESIGNER  //  MANUAL ITEM POSITIONING",
            18, FontStyle.Bold, new Color(0.25f, 0.92f, 1.0f),
            new Vector2(0.015f, 0.42f), new Vector2(0.46f, 0.95f), TextAnchor.MiddleLeft);

        _statusBannerText = CreateLabel(topBar.transform, font, "StatusBanner",
            "TIP: Left-Click any building/bunker in 3D to select, hold Left-Click to drag across the floor, [Q/E] to Rotate, Scroll to adjust Height.",
            12, FontStyle.Normal, new Color(0.82f, 0.92f, 1.0f),
            new Vector2(0.015f, 0.06f), new Vector2(0.54f, 0.44f), TextAnchor.MiddleLeft);

        // Top Bar Action Buttons
        Button viewBtn = CreateBtn(topBar.transform, font, "Btn_ViewToggle",
            "🎥 VIEW: 3D FLY [V]", 11, new Color(0.10f, 0.22f, 0.34f, 0.95f), Color.white,
            new Vector2(0.495f, 0.16f), new Vector2(0.588f, 0.84f), ToggleOverheadView);
        _viewModeBtnBg = viewBtn.GetComponent<Image>();
        _viewModeBtnText = viewBtn.GetComponentInChildren<Text>();

        CreateBtn(topBar.transform, font, "Btn_SaveLayout",
            "💾 SAVE LAYOUT", 11, new Color(0.10f, 0.56f, 0.34f, 0.98f), Color.white,
            new Vector2(0.594f, 0.16f), new Vector2(0.682f, 0.84f), SaveCustomLayout);

        CreateBtn(topBar.transform, font, "Btn_ResetMap",
            "↺ RESET DEFAULT", 11, new Color(0.48f, 0.24f, 0.12f, 0.95f), Color.white,
            new Vector2(0.688f, 0.16f), new Vector2(0.776f, 0.84f), ResetEntireMapToDefaults);

        CreateBtn(topBar.transform, font, "Btn_StartCombat",
            "⚔ START COMBAT", 11, new Color(0.14f, 0.44f, 0.72f, 0.98f), Color.white,
            new Vector2(0.782f, 0.16f), new Vector2(0.874f, 0.84f), SaveAndStartCombatMatch);

        CreateBtn(topBar.transform, font, "Btn_SaveAndExit",
            "✔ SAVE & EXPLORE [F10]", 12, new Color(0.12f, 0.68f, 0.42f, 1.0f), Color.white,
            new Vector2(0.880f, 0.16f), new Vector2(0.992f, 0.84f), CloseDesignerMode);

        // 2. LEFT PANEL: ITEM HIERARCHY & CATEGORY FILTER (X: 0.008 .. 0.220, Y: 0.02 .. 0.90)
        GameObject leftPanel = CreatePanel(_designerCanvas.transform, "Left_Hierarchy_Panel", new Vector2(0.008f, 0.02f), new Vector2(0.220f, 0.90f), new Color(0.03f, 0.05f, 0.09f, 0.92f));
        CreatePanel(leftPanel.transform, "TopAccent", new Vector2(0f, 0.993f), new Vector2(1f, 1f), new Color(0.15f, 0.88f, 1.0f, 0.9f));

        CreateLabel(leftPanel.transform, font, "Hdr_List",
            "MAP ITEMS & BUILDINGS (CLICK TO SELECT)",
            12, FontStyle.Bold, new Color(0.25f, 0.90f, 1.0f),
            new Vector2(0.04f, 0.945f), new Vector2(0.96f, 0.988f), TextAnchor.MiddleLeft);

        // Category Filter Pills
        string[] cats = new string[] { "ALL", "BUNKERS", "TOWERS", "COVER", "GROVES & ROCKS" };
        string[] catLabels = new string[] { "ALL", "BUNKERS", "TOWERS", "COVER", "ROCKS" };
        _catFilterBtnBgs.Clear();
        for (int c = 0; c < cats.Length; c++)
        {
            string catKey = cats[c];
            float minX = 0.025f + c * 0.192f;
            float maxX = minX + 0.184f;
            Button cBtn = CreateBtn(leftPanel.transform, font, $"Btn_Cat_{catKey}",
                catLabels[c], 10, new Color(0.08f, 0.14f, 0.22f, 0.95f), Color.white,
                new Vector2(minX, 0.90f), new Vector2(maxX, 0.942f), () => SetCategoryFilter(catKey));
            _catFilterBtnBgs[catKey] = cBtn.GetComponent<Image>();
        }

        // Scrollable Item List Container
        GameObject listScrollObj = CreatePanel(leftPanel.transform, "ItemList_Scroll", new Vector2(0.025f, 0.015f), new Vector2(0.975f, 0.888f), new Color(0.015f, 0.025f, 0.045f, 0.85f));
        listScrollObj.GetComponent<Image>().raycastTarget = true;
        RectMask2D mask = listScrollObj.AddComponent<RectMask2D>();

        GameObject contentObj = new GameObject("Content");
        contentObj.transform.SetParent(listScrollObj.transform, false);
        RectTransform contentRT = contentObj.AddComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0f, 1f);
        contentRT.anchorMax = new Vector2(1f, 1f);
        contentRT.pivot = new Vector2(0.5f, 1f);
        contentRT.anchoredPosition = Vector2.zero;
        contentRT.sizeDelta = new Vector2(0f, 800f);
        _itemListContainer = contentObj.transform;

        ScrollRect sr = listScrollObj.AddComponent<ScrollRect>();
        sr.content = contentRT;
        sr.horizontal = false;
        sr.vertical = true;
        sr.scrollSensitivity = 28f;
        sr.movementType = ScrollRect.MovementType.Clamped;

        // 3. RIGHT PANEL: MANUAL POSITIONING INSPECTOR & SPAWN PALETTE (X: 0.760 .. 0.992, Y: 0.02 .. 0.90)
        GameObject rightPanel = CreatePanel(_designerCanvas.transform, "Right_Inspector_Panel", new Vector2(0.760f, 0.02f), new Vector2(0.992f, 0.90f), new Color(0.03f, 0.05f, 0.09f, 0.92f));
        CreatePanel(rightPanel.transform, "TopAccent", new Vector2(0f, 0.993f), new Vector2(1f, 1f), new Color(0.22f, 0.92f, 0.58f, 0.9f));

        CreateLabel(rightPanel.transform, font, "Hdr_Insp",
            "SELECTED ITEM CONTROLS",
            13, FontStyle.Bold, new Color(0.22f, 0.95f, 0.62f),
            new Vector2(0.04f, 0.945f), new Vector2(0.96f, 0.988f), TextAnchor.MiddleLeft);

        _selectedTitleText = CreateLabel(rightPanel.transform, font, "Selected_Title",
            "NONE SELECTED",
            15, FontStyle.Bold, Color.white,
            new Vector2(0.04f, 0.895f), new Vector2(0.96f, 0.942f), TextAnchor.MiddleLeft);

        _selectedCoordsText = CreateLabel(rightPanel.transform, font, "Selected_Coords",
            "POS: (0, 0, 0)  |  ROT: 0°  |  FLOOR OFFSET: 0.0m",
            11, FontStyle.Normal, new Color(0.72f, 0.86f, 0.98f),
            new Vector2(0.04f, 0.845f), new Vector2(0.96f, 0.895f), TextAnchor.MiddleLeft);

        // Mode toggles: Mouse Drag & Auto Ground Snap
        Button dragBtn = CreateBtn(rightPanel.transform, font, "Btn_DragToggle",
            "● MOUSE DRAG: ON", 11, new Color(0.10f, 0.52f, 0.36f, 0.95f), Color.white,
            new Vector2(0.04f, 0.785f), new Vector2(0.485f, 0.838f), ToggleMouseDragMode);
        _dragToggleBg = dragBtn.GetComponent<Image>();
        _dragToggleText = dragBtn.GetComponentInChildren<Text>();

        Button snapBtn = CreateBtn(rightPanel.transform, font, "Btn_SnapToggle",
            "● AUTO FLOOR SNAP: ON", 11, new Color(0.10f, 0.52f, 0.36f, 0.95f), Color.white,
            new Vector2(0.515f, 0.785f), new Vector2(0.96f, 0.838f), ToggleAutoGroundSnap);
        _snapToggleBg = snapBtn.GetComponent<Image>();
        _snapToggleText = snapBtn.GetComponentInChildren<Text>();

        // Section A: Horizontal Position Nudging (X / Z)
        CreateLabel(rightPanel.transform, font, "Lbl_MoveXZ",
            "MOVE POSITION (OR DRAG IN 3D WITH MOUSE)",
            11, FontStyle.Bold, new Color(0.25f, 0.90f, 1.0f),
            new Vector2(0.04f, 0.735f), new Vector2(0.96f, 0.775f), TextAnchor.MiddleLeft);

        CreateBtn(rightPanel.transform, font, "Btn_West", "◀ WEST (-2m)", 11, new Color(0.10f, 0.18f, 0.28f, 0.95f), Color.white,
            new Vector2(0.04f, 0.680f), new Vector2(0.26f, 0.730f), () => NudgeSelectedXZ(-2f, 0f));
        CreateBtn(rightPanel.transform, font, "Btn_North", "▲ NORTH (+2m)", 11, new Color(0.10f, 0.18f, 0.28f, 0.95f), Color.white,
            new Vector2(0.275f, 0.680f), new Vector2(0.495f, 0.730f), () => NudgeSelectedXZ(0f, 2f));
        CreateBtn(rightPanel.transform, font, "Btn_South", "▼ SOUTH (-2m)", 11, new Color(0.10f, 0.18f, 0.28f, 0.95f), Color.white,
            new Vector2(0.505f, 0.680f), new Vector2(0.725f, 0.730f), () => NudgeSelectedXZ(0f, -2f));
        CreateBtn(rightPanel.transform, font, "Btn_East", "EAST (+2m) ▶", 11, new Color(0.10f, 0.18f, 0.28f, 0.95f), Color.white,
            new Vector2(0.74f, 0.680f), new Vector2(0.96f, 0.730f), () => NudgeSelectedXZ(2f, 0f));

        // Section B: Floor Height & Ground Alignment
        CreateLabel(rightPanel.transform, font, "Lbl_Height",
            "FLOOR HEIGHT & GROUND ALIGNMENT (SCROLL WHEEL)",
            11, FontStyle.Bold, new Color(0.25f, 0.90f, 1.0f),
            new Vector2(0.04f, 0.625f), new Vector2(0.96f, 0.665f), TextAnchor.MiddleLeft);

        CreateBtn(rightPanel.transform, font, "Btn_LowerY", "▼ LOWER (-0.25m)", 11, new Color(0.10f, 0.18f, 0.28f, 0.95f), Color.white,
            new Vector2(0.04f, 0.568f), new Vector2(0.33f, 0.620f), () => NudgeSelectedHeight(-0.25f));
        CreateBtn(rightPanel.transform, font, "Btn_SnapFloor", "⚓ SNAP FLUSH [G]", 11, new Color(0.12f, 0.48f, 0.68f, 0.98f), Color.white,
            new Vector2(0.345f, 0.568f), new Vector2(0.655f, 0.620f), SnapSelectedFlushToGround);
        CreateBtn(rightPanel.transform, font, "Btn_RaiseY", "▲ RAISE (+0.25m)", 11, new Color(0.10f, 0.18f, 0.28f, 0.95f), Color.white,
            new Vector2(0.67f, 0.568f), new Vector2(0.96f, 0.620f), () => NudgeSelectedHeight(0.25f));

        // Section C: Rotation & Entrance Direction
        CreateLabel(rightPanel.transform, font, "Lbl_Rot",
            "ROTATION & ENTRANCE DIRECTION (KEYS: [Q] / [E])",
            11, FontStyle.Bold, new Color(0.25f, 0.90f, 1.0f),
            new Vector2(0.04f, 0.515f), new Vector2(0.96f, 0.555f), TextAnchor.MiddleLeft);

        CreateBtn(rightPanel.transform, font, "Btn_RotL45", "↺ -45°", 11, new Color(0.10f, 0.18f, 0.28f, 0.95f), Color.white,
            new Vector2(0.04f, 0.460f), new Vector2(0.21f, 0.510f), () => RotateSelectedYaw(-45f));
        CreateBtn(rightPanel.transform, font, "Btn_RotL15", "↺ -15°", 11, new Color(0.10f, 0.18f, 0.28f, 0.95f), Color.white,
            new Vector2(0.22f, 0.460f), new Vector2(0.39f, 0.510f), () => RotateSelectedYaw(-15f));
        CreateBtn(rightPanel.transform, font, "Btn_RotR15", "↻ +15°", 11, new Color(0.10f, 0.18f, 0.28f, 0.95f), Color.white,
            new Vector2(0.40f, 0.460f), new Vector2(0.57f, 0.510f), () => RotateSelectedYaw(15f));
        CreateBtn(rightPanel.transform, font, "Btn_RotR45", "↻ +45°", 11, new Color(0.10f, 0.18f, 0.28f, 0.95f), Color.white,
            new Vector2(0.58f, 0.460f), new Vector2(0.75f, 0.510f), () => RotateSelectedYaw(45f));
        CreateBtn(rightPanel.transform, font, "Btn_FaceCenter", "🎯 CENTER", 10, new Color(0.14f, 0.44f, 0.62f, 0.95f), Color.white,
            new Vector2(0.76f, 0.460f), new Vector2(0.96f, 0.510f), RotateSelectedToFaceOasisCenter);

        // Section D: Scale & Quick Actions
        CreateLabel(rightPanel.transform, font, "Lbl_Actions",
            "SCALE, TEST & ITEM ACTIONS",
            11, FontStyle.Bold, new Color(0.25f, 0.90f, 1.0f),
            new Vector2(0.04f, 0.405f), new Vector2(0.96f, 0.445f), TextAnchor.MiddleLeft);

        CreateBtn(rightPanel.transform, font, "Btn_ScaleDown", "SCALE -10%", 11, new Color(0.10f, 0.18f, 0.28f, 0.95f), Color.white,
            new Vector2(0.04f, 0.350f), new Vector2(0.33f, 0.400f), () => ScaleSelectedItem(0.9f));
        CreateBtn(rightPanel.transform, font, "Btn_ScaleReset", "SCALE 100%", 11, new Color(0.10f, 0.18f, 0.28f, 0.95f), Color.white,
            new Vector2(0.345f, 0.350f), new Vector2(0.655f, 0.400f), () => ScaleSelectedItem(0f));
        CreateBtn(rightPanel.transform, font, "Btn_ScaleUp", "SCALE +10%", 11, new Color(0.10f, 0.18f, 0.28f, 0.95f), Color.white,
            new Vector2(0.67f, 0.350f), new Vector2(0.96f, 0.400f), () => ScaleSelectedItem(1.1f));

        CreateBtn(rightPanel.transform, font, "Btn_FocusCam", "🔍 FOCUS [F]", 11, new Color(0.12f, 0.36f, 0.56f, 0.95f), Color.white,
            new Vector2(0.04f, 0.288f), new Vector2(0.33f, 0.340f), FocusSelectedItem);
        CreateBtn(rightPanel.transform, font, "Btn_TeleportPlayer", "⚡ TELEPORT PLAYER [P]", 11, new Color(0.10f, 0.56f, 0.38f, 0.98f), Color.white,
            new Vector2(0.345f, 0.288f), new Vector2(0.96f, 0.340f), TeleportPlayerToSelected);

        CreateBtn(rightPanel.transform, font, "Btn_ResetItem", "↺ RESET THIS ITEM", 11, new Color(0.38f, 0.26f, 0.12f, 0.95f), Color.white,
            new Vector2(0.04f, 0.228f), new Vector2(0.485f, 0.278f), ResetSelectedItemToDefault);
        CreateBtn(rightPanel.transform, font, "Btn_DeleteItem", "🗑 DELETE ITEM [DEL]", 11, new Color(0.62f, 0.16f, 0.18f, 0.95f), Color.white,
            new Vector2(0.515f, 0.228f), new Vector2(0.96f, 0.278f), DeleteSelectedItem);

        // Section E: Spawn New Buildings / Items Palette
        CreatePanel(rightPanel.transform, "Div_Palette", new Vector2(0.04f, 0.208f), new Vector2(0.96f, 0.212f), new Color(0.20f, 0.85f, 1.0f, 0.35f));
        CreateLabel(rightPanel.transform, font, "Lbl_Spawn",
            "+ SPAWN NEW BUILDING / COVER AT VIEW CENTER",
            11, FontStyle.Bold, new Color(0.22f, 0.95f, 0.62f),
            new Vector2(0.04f, 0.165f), new Vector2(0.96f, 0.202f), TextAnchor.MiddleLeft);

        CreateBtn(rightPanel.transform, font, "Btn_AddBunker", "+ UNDERCOVER BUNKER", 11, new Color(0.10f, 0.44f, 0.62f, 0.95f), Color.white,
            new Vector2(0.04f, 0.108f), new Vector2(0.485f, 0.158f), () => SpawnNewMapItem("HangarBunker"));
        CreateBtn(rightPanel.transform, font, "Btn_AddTower", "+ SNIPER WATCHTOWER", 11, new Color(0.10f, 0.44f, 0.62f, 0.95f), Color.white,
            new Vector2(0.515f, 0.108f), new Vector2(0.96f, 0.158f), () => SpawnNewMapItem("Watchtower"));

        CreateBtn(rightPanel.transform, font, "Btn_AddBridge", "+ SKYBRIDGE COMPOUND", 11, new Color(0.10f, 0.44f, 0.62f, 0.95f), Color.white,
            new Vector2(0.04f, 0.048f), new Vector2(0.485f, 0.098f), () => SpawnNewMapItem("Skybridge"));
        CreateBtn(rightPanel.transform, font, "Btn_AddCover", "+ TACTICAL COVER PAD", 11, new Color(0.10f, 0.44f, 0.62f, 0.95f), Color.white,
            new Vector2(0.515f, 0.048f), new Vector2(0.96f, 0.098f), () => SpawnNewMapItem("CoverStation"));

        SetCategoryFilter(_activeCategoryFilter);
    }

    private void SetCategoryFilter(string cat)
    {
        _activeCategoryFilter = cat;
        foreach (var kv in _catFilterBtnBgs)
        {
            if (kv.Value == null) continue;
            kv.Value.color = (kv.Key == cat)
                ? new Color(0.14f, 0.56f, 0.78f, 0.98f)
                : new Color(0.08f, 0.14f, 0.22f, 0.95f);
        }
        RebuildItemListUI();
    }

    private void RebuildItemListUI()
    {
        if (_itemListContainer == null) return;
        for (int i = 0; i < _spawnedListRowObjs.Count; i++)
        {
            if (_spawnedListRowObjs[i] != null) Destroy(_spawnedListRowObjs[i]);
        }
        _spawnedListRowObjs.Clear();

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        float rowHeight = 34f;
        float gap = 4f;
        int visibleIdx = 0;

        for (int i = 0; i < _editableItems.Count; i++)
        {
            var item = _editableItems[i];
            if (item == null || !item.gameObject.activeSelf) continue;
            if (_activeCategoryFilter != "ALL" && item.category != _activeCategoryFilter) continue;

            GameObject rowObj = new GameObject($"Row_{item.itemId}");
            rowObj.transform.SetParent(_itemListContainer, false);
            RectTransform rRT = rowObj.AddComponent<RectTransform>();
            rRT.anchorMin = new Vector2(0.02f, 1f);
            rRT.anchorMax = new Vector2(0.98f, 1f);
            rRT.pivot = new Vector2(0.5f, 1f);
            rRT.anchoredPosition = new Vector2(0f, -6f - visibleIdx * (rowHeight + gap));
            rRT.sizeDelta = new Vector2(0f, rowHeight);

            Image rBg = rowObj.AddComponent<Image>();
            bool isSel = (item == _selectedItem);
            rBg.color = isSel ? new Color(0.14f, 0.52f, 0.76f, 0.98f) : new Color(0.08f, 0.12f, 0.18f, 0.92f);

            Button rBtn = rowObj.AddComponent<Button>();
            rBtn.targetGraphic = rBg;
            OasisMapEditableItem captured = item;
            rBtn.onClick.AddListener(() =>
            {
                SelectItem(captured);
                FocusSelectedItem();
            });

            CreateLabel(rowObj.transform, font, "Label",
                $"  {item.displayName}", 12, isSel ? FontStyle.Bold : FontStyle.Normal,
                isSel ? Color.white : new Color(0.82f, 0.90f, 0.98f),
                new Vector2(0f, 0f), new Vector2(1f, 1f), TextAnchor.MiddleLeft);

            _spawnedListRowObjs.Add(rowObj);
            visibleIdx++;
        }

        RectTransform cRT = _itemListContainer.GetComponent<RectTransform>();
        if (cRT != null)
        {
            cRT.sizeDelta = new Vector2(0f, Mathf.Max(400f, 16f + visibleIdx * (rowHeight + gap)));
        }
    }

    private void RefreshItemListHighlight()
    {
        RebuildItemListUI();
    }

    private void ToggleMouseDragMode()
    {
        dragWithLeftMouse = !dragWithLeftMouse;
        if (_dragToggleBg != null)
        {
            _dragToggleBg.color = dragWithLeftMouse
                ? new Color(0.10f, 0.52f, 0.36f, 0.95f)
                : new Color(0.28f, 0.16f, 0.16f, 0.95f);
        }
        if (_dragToggleText != null)
        {
            _dragToggleText.text = dragWithLeftMouse ? "● MOUSE DRAG: ON" : "○ MOUSE DRAG: OFF";
        }
    }

    private void ToggleAutoGroundSnap()
    {
        autoSnapToGround = !autoSnapToGround;
        if (_snapToggleBg != null)
        {
            _snapToggleBg.color = autoSnapToGround
                ? new Color(0.10f, 0.52f, 0.36f, 0.95f)
                : new Color(0.28f, 0.16f, 0.16f, 0.95f);
        }
        if (_snapToggleText != null)
        {
            _snapToggleText.text = autoSnapToGround ? "● AUTO FLOOR SNAP: ON" : "○ AUTO FLOOR SNAP: OFF";
        }
    }

    private void UpdateInspectorReadout()
    {
        if (_selectedTitleText == null || _selectedCoordsText == null) return;
        if (_selectedItem == null)
        {
            _selectedTitleText.text = "NO ITEM SELECTED";
            _selectedCoordsText.text = "Click any building or bunker in 3D or on the left list.";
            return;
        }

        _selectedTitleText.text = $"{_selectedItem.displayName.ToUpper()}  <size=11><color=#26D9FF>[{_selectedItem.category}]</color></size>";
        Vector3 p = _selectedItem.transform.position;
        float yaw = _selectedItem.transform.eulerAngles.y;
        _selectedCoordsText.text = $"POS: ({p.x:F1}, {p.y:F1}, {p.z:F1})   •   YAW: {yaw:F0}°   •   OFFSET: {_selectedItem.heightOffset:+0.00;-0.00;0.00}m";
    }

    private void ShowStatus(string richText, float duration)
    {
        if (_statusBannerText != null)
        {
            _statusBannerText.text = richText;
            _statusBannerTimer = duration;
        }
    }

    // =========================================================================
    // UI BUILDER HELPERS
    // =========================================================================

    private GameObject CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
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
        img.raycastTarget = true;
        return obj;
    }

    private Text CreateLabel(Transform parent, Font font, string name, string text, int fontSize, FontStyle style, Color color, Vector2 anchorMin, Vector2 anchorMax, TextAnchor align)
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
        txt.text = text;
        txt.fontSize = fontSize;
        txt.fontStyle = style;
        txt.color = color;
        txt.alignment = align;
        txt.supportRichText = true;
        txt.raycastTarget = false;
        return txt;
    }

    private Button CreateBtn(Transform parent, Font font, string name, string label, int fontSize, Color bgColor, Color textColor, Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction onClick)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        Image img = obj.AddComponent<Image>();
        img.color = bgColor;
        img.raycastTarget = true;
        Button btn = obj.AddComponent<Button>();
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(onClick);
        CreateLabel(obj.transform, font, "Text", label, fontSize, FontStyle.Bold, textColor, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter);
        return btn;
    }
}
