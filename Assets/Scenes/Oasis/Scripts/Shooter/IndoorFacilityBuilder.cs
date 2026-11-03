using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Procedural World Builder for the "Subterra Research Complex" (Indoor Sci-Fi Facility Map).
/// Features:
/// - Multi-level Central Reactor Core with raised dais, animated plasma conduit pillar, and catwalk mezzanine.
/// - 4 Specialized Branching Sectors:
///     * Sector 1 (North): Command & Tactical Vault (server racks, holographic terminals, tactical deck)
///     * Sector 2 (South): Armory & Security Garrison (weapon lockers, security checkpoint, blast shields)
///     * Sector 3 (West):  Bio-Containment & Cryo Lab (cryo pods, examination desks, cleanroom airlock)
///     * Sector 4 (East):  Heavy Cargo Logistics & Airlock (shipping container stacks, generators, blast doors)
/// - Outer Perimeter Ring Corridors for continuous 3-lane tactical flanking without dead ends.
/// - Atmospheric interior lighting, sector color cues, and full solid colliders on all floors, walls, and ceilings.
/// </summary>
public class IndoorFacilityBuilder : MonoBehaviour
{
    public static IndoorFacilityBuilder Instance { get; private set; }

    [Header("Facility Configuration")]
    public float floorY = 0f;
    public float wingCeilingHeight = 5.4f;
    public float coreCeilingHeight = 7.8f;

    // Materials
    private Material _matFloor;
    private Material _matFloorGrate;
    private Material _matWall;
    private Material _matWallTrim;
    private Material _matCeiling;
    private Material _matCorePlasma;
    private Material _matCoreRings;
    private Material _matTerminalScreen;
    private Material _matCryoGlass;
    private Material _matCargoBlue;
    private Material _matCargoOrange;
    private Material _matCargoOlive;
    private Material _matBarrier;

    // References to animated elements
    private Transform _coreRing1;
    private Transform _coreRing2;
    private Transform _coreRing3;
    private Light _coreLight;

    private GameObject _facilityRoot;
    private GameObject _landmarksRoot;
    private bool _isBuilt = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        EnsureFacilityBuilt();
    }

    private void Update()
    {
        // Smoothly rotate the containment rings and pulse the reactor light
        if (_coreRing1 != null) _coreRing1.Rotate(Vector3.up, 35f * Time.deltaTime, Space.World);
        if (_coreRing2 != null) _coreRing2.Rotate(Vector3.up, -48f * Time.deltaTime, Space.World);
        if (_coreRing3 != null) _coreRing3.Rotate(Vector3.up, 22f * Time.deltaTime, Space.World);

        if (_coreLight != null)
        {
            float pulse = Mathf.PingPong(Time.time * 1.5f, 0.6f);
            _coreLight.intensity = 2.2f + pulse;
        }
    }

    public void EnsureFacilityBuilt()
    {
        if (_isBuilt) return;

        // Check if an existing root object already exists in scene
        _facilityRoot = GameObject.Find("Subterra_Facility_Root");
        if (_facilityRoot != null)
        {
            _isBuilt = true;
            ConfigureSceneAtmosphere();
            RegisterFacilitySpawns();
            return;
        }

        _facilityRoot = new GameObject("Subterra_Facility_Root");
        _landmarksRoot = new GameObject("Facility_Landmarks");
        _landmarksRoot.transform.SetParent(_facilityRoot.transform, false);

        InitializeMaterials();
        ConfigureSceneAtmosphere();

        // 1. Central Core Atrium (28m x 28m)
        BuildCentralReactorAtrium();

        // 2. Main Wings (4 Cardinal Directions)
        BuildCommandVault(new Vector3(0f, 0f, 29f));   // North
        BuildArmoryGarrison(new Vector3(0f, 0f, -29f)); // South
        BuildBioLab(new Vector3(-29f, 0f, 0f));         // West
        BuildCargoLogistics(new Vector3(29f, 0f, 0f));  // East

        // 3. Main Connecting Hallways
        BuildMainHallway(new Vector3(0f, 0f, 16.5f), new Vector3(6f, wingCeilingHeight, 9f), true);   // North Hall
        BuildMainHallway(new Vector3(0f, 0f, -16.5f), new Vector3(6f, wingCeilingHeight, 9f), true);  // South Hall
        BuildMainHallway(new Vector3(-16.5f, 0f, 0f), new Vector3(9f, wingCeilingHeight, 6f), false); // West Hall
        BuildMainHallway(new Vector3(16.5f, 0f, 0f), new Vector3(9f, wingCeilingHeight, 6f), false);  // East Hall

        // 4. Outer Perimeter Ring Corridors (Corners)
        BuildPerimeterRingCorridors();

        // 5. Solid Bedrock Sub-Floor (Void Protection)
        BuildSafetyFloor();

        // 6. Register Spawns with OasisSpawnSystem
        RegisterFacilitySpawns();

        // 7. Register landmarks with Map Designer if present
        if (OasisMapDesigner.Instance != null)
        {
            OasisMapDesigner.Instance.RegisterSceneLandmarksAndApplySavedLayout(_landmarksRoot.transform);
        }

        // 8. Add subtle ambient power hum
        AttachAmbientHum();

        _isBuilt = true;
        Debug.Log("<color=cyan><b>[IndoorFacilityBuilder]</b> Subterra Research Complex successfully constructed!</color>");
    }

    private void InitializeMaterials()
    {
        _matFloor = OasisShaderHelper.CreateLitMaterial(new Color(0.18f, 0.20f, 0.23f), metallic: 0.82f, smoothness: 0.65f);
        _matFloorGrate = OasisShaderHelper.CreateLitMaterial(new Color(0.12f, 0.14f, 0.16f), metallic: 0.90f, smoothness: 0.45f);
        _matWall = OasisShaderHelper.CreateLitMaterial(new Color(0.24f, 0.26f, 0.30f), metallic: 0.40f, smoothness: 0.30f);
        _matWallTrim = OasisShaderHelper.CreateLitMaterial(new Color(0.13f, 0.15f, 0.18f), metallic: 0.92f, smoothness: 0.75f);
        _matCeiling = OasisShaderHelper.CreateLitMaterial(new Color(0.14f, 0.15f, 0.18f), metallic: 0.50f, smoothness: 0.25f);

        _matCorePlasma = OasisShaderHelper.CreateUnlitMaterial(new Color(0.12f, 0.88f, 1.0f) * 2.8f);
        _matCoreRings = OasisShaderHelper.CreateLitMaterial(new Color(0.25f, 0.75f, 1.0f), metallic: 0.95f, smoothness: 0.85f);
        _matTerminalScreen = OasisShaderHelper.CreateUnlitMaterial(new Color(0.05f, 0.92f, 0.70f) * 1.9f);
        _matCryoGlass = OasisShaderHelper.CreateLitMaterial(new Color(0.10f, 0.95f, 0.65f), metallic: 0.2f, smoothness: 0.95f);

        _matCargoBlue = OasisShaderHelper.CreateLitMaterial(new Color(0.18f, 0.38f, 0.58f), metallic: 0.60f, smoothness: 0.45f);
        _matCargoOrange = OasisShaderHelper.CreateLitMaterial(new Color(0.85f, 0.45f, 0.12f), metallic: 0.55f, smoothness: 0.40f);
        _matCargoOlive = OasisShaderHelper.CreateLitMaterial(new Color(0.28f, 0.34f, 0.24f), metallic: 0.60f, smoothness: 0.40f);
        _matBarrier = OasisShaderHelper.CreateLitMaterial(new Color(0.20f, 0.70f, 0.95f), metallic: 0.1f, smoothness: 0.9f);
    }

    private void ConfigureSceneAtmosphere()
    {
        // Dark, moody indoor facility lighting (no blinding sun)
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.12f, 0.14f, 0.18f, 1f);
        RenderSettings.fog = false;

        // Dim directional lights in the scene to simulate sealed bunker
        var lights = FindObjectsByType<Light>(FindObjectsInactive.Include);
        foreach (var l in lights)
        {
            if (l.type == LightType.Directional)
            {
                l.intensity = 0.08f;
                l.color = new Color(0.15f, 0.20f, 0.28f);
            }
        }
    }

    // =========================================================================
    // 1. CENTRAL REACTOR ATRIUM (CORE HUB)
    // =========================================================================
    private void BuildCentralReactorAtrium()
    {
        GameObject room = new GameObject("Sector_Core_Atrium");
        room.transform.SetParent(_facilityRoot.transform, false);

        // Floor (28m x 28m)
        CreateBox("Core_Floor", room.transform, new Vector3(0f, -0.2f, 0f), new Vector3(28f, 0.4f, 28f), _matFloor);

        // Ceiling (28m x 28m at Y = 7.8m)
        CreateBox("Core_Ceiling", room.transform, new Vector3(0f, coreCeilingHeight + 0.2f, 0f), new Vector3(28f, 0.4f, 28f), _matCeiling);

        // Raised Central Dais (12m x 12m, Y = 0.85m)
        CreateBox("Core_Dais", room.transform, new Vector3(0f, 0.425f, 0f), new Vector3(12f, 0.85f, 12f), _matFloorGrate);

        // 4 Access Ramps leading onto the Dais
        CreateRamp("Core_Ramp_N", room.transform, new Vector3(0f, 0.425f, 6.8f), new Vector3(3.2f, 0.85f, 2.4f), 18f);
        CreateRamp("Core_Ramp_S", room.transform, new Vector3(0f, 0.425f, -6.8f), new Vector3(3.2f, 0.85f, 2.4f), -18f);
        CreateRamp("Core_Ramp_E", room.transform, new Vector3(6.8f, 0.425f, 0f), new Vector3(2.4f, 0.85f, 3.2f), 18f, rotateY90: true);
        CreateRamp("Core_Ramp_W", room.transform, new Vector3(-6.8f, 0.425f, 0f), new Vector3(2.4f, 0.85f, 3.2f), -18f, rotateY90: true);

        // Central Reactor Column (Pillar)
        GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pillar.name = "Reactor_Core_Pillar";
        pillar.transform.SetParent(room.transform, false);
        pillar.transform.position = new Vector3(0f, coreCeilingHeight * 0.5f, 0f);
        pillar.transform.localScale = new Vector3(3.0f, coreCeilingHeight * 0.5f, 3.0f);
        pillar.GetComponent<MeshRenderer>().sharedMaterial = _matCorePlasma;

        // Plasma Conduit Rings
        _coreRing1 = CreateTorusRing("Reactor_Ring_1", room.transform, new Vector3(0f, 2.4f, 0f), 4.8f, 0.28f, _matCoreRings);
        _coreRing2 = CreateTorusRing("Reactor_Ring_2", room.transform, new Vector3(0f, 4.4f, 0f), 5.4f, 0.32f, _matCoreRings);
        _coreRing3 = CreateTorusRing("Reactor_Ring_3", room.transform, new Vector3(0f, 6.2f, 0f), 4.6f, 0.28f, _matCoreRings);

        // Core Point Light
        GameObject coreLightObj = new GameObject("Reactor_Core_Light");
        coreLightObj.transform.SetParent(room.transform, false);
        coreLightObj.transform.position = new Vector3(0f, 3.8f, 0f);
        _coreLight = coreLightObj.AddComponent<Light>();
        _coreLight.type = LightType.Point;
        _coreLight.color = new Color(0.20f, 0.90f, 1.0f);
        _coreLight.range = 24f;
        _coreLight.intensity = 2.4f;

        // 4 Heavy Corner Support Pillars
        CreateSupportPillar("Core_Pillar_NW", room.transform, new Vector3(-11f, 0f, 11f), coreCeilingHeight);
        CreateSupportPillar("Core_Pillar_NE", room.transform, new Vector3(11f, 0f, 11f), coreCeilingHeight);
        CreateSupportPillar("Core_Pillar_SW", room.transform, new Vector3(-11f, 0f, -11f), coreCeilingHeight);
        CreateSupportPillar("Core_Pillar_SE", room.transform, new Vector3(11f, 0f, -11f), coreCeilingHeight);

        // North & South Catwalk Mezzanines (Y = 3.8m)
        BuildCatwalk("Catwalk_North", room.transform, new Vector3(0f, 3.8f, 11.5f), new Vector3(24f, 0.3f, 3.2f), isNorth: true);
        BuildCatwalk("Catwalk_South", room.transform, new Vector3(0f, 3.8f, -11.5f), new Vector3(24f, 0.3f, 3.2f), isNorth: false);

        // Half-Wall Tactical Cover Station on Dais
        CreateTacticalBarrier("Cover_Core_NW", _landmarksRoot.transform, new Vector3(-4.5f, 0.85f, 4.5f), 45f);
        CreateTacticalBarrier("Cover_Core_NE", _landmarksRoot.transform, new Vector3(4.5f, 0.85f, 4.5f), -45f);
        CreateTacticalBarrier("Cover_Core_SW", _landmarksRoot.transform, new Vector3(-4.5f, 0.85f, -4.5f), -45f);
        CreateTacticalBarrier("Cover_Core_SE", _landmarksRoot.transform, new Vector3(4.5f, 0.85f, -4.5f), 45f);

        // Core Perimeter Walls (with 6m wide portal archways)
        BuildPortalWall("Core_Wall_N", room.transform, new Vector3(0f, 0f, 14f), 28f, coreCeilingHeight, portalWidth: 6.5f, portalHeight: 4.8f, isFacingZ: true);
        BuildPortalWall("Core_Wall_S", room.transform, new Vector3(0f, 0f, -14f), 28f, coreCeilingHeight, portalWidth: 6.5f, portalHeight: 4.8f, isFacingZ: true);
        BuildPortalWall("Core_Wall_W", room.transform, new Vector3(-14f, 0f, 0f), 28f, coreCeilingHeight, portalWidth: 6.5f, portalHeight: 4.8f, isFacingZ: false);
        BuildPortalWall("Core_Wall_E", room.transform, new Vector3(14f, 0f, 0f), 28f, coreCeilingHeight, portalWidth: 6.5f, portalHeight: 4.8f, isFacingZ: false);
    }

    // =========================================================================
    // 2. SECTOR 1 (NORTH) - COMMAND & TACTICAL VAULT
    // =========================================================================
    private void BuildCommandVault(Vector3 center)
    {
        GameObject room = new GameObject("Sector_Command_Vault");
        room.transform.SetParent(_facilityRoot.transform, false);

        Vector3 roomSize = new Vector3(24f, wingCeilingHeight, 20f);

        // Floor & Ceiling
        CreateBox("Cmd_Floor", room.transform, center + new Vector3(0f, -0.2f, 0f), new Vector3(roomSize.x, 0.4f, roomSize.z), _matFloor);
        CreateBox("Cmd_Ceiling", room.transform, center + new Vector3(0f, wingCeilingHeight + 0.2f, 0f), new Vector3(roomSize.x, 0.4f, roomSize.z), _matCeiling);

        // Walls
        BuildSolidWall("Cmd_Wall_North", room.transform, center + new Vector3(0f, 0f, roomSize.z * 0.5f), roomSize.x, wingCeilingHeight, isFacingZ: true);
        BuildPortalWall("Cmd_Wall_South", room.transform, center - new Vector3(0f, 0f, roomSize.z * 0.5f), roomSize.x, wingCeilingHeight, portalWidth: 6.5f, portalHeight: 4.5f, isFacingZ: true);
        BuildPortalWall("Cmd_Wall_West", room.transform, center - new Vector3(roomSize.x * 0.5f, 0f, 0f), roomSize.z, wingCeilingHeight, portalWidth: 4.5f, portalHeight: 4.0f, isFacingZ: false);
        BuildPortalWall("Cmd_Wall_East", room.transform, center + new Vector3(roomSize.x * 0.5f, 0f, 0f), roomSize.z, wingCeilingHeight, portalWidth: 4.5f, portalHeight: 4.0f, isFacingZ: false);

        // Elevated Tactical Observation Deck at North Wall
        CreateBox("Cmd_TacticalDeck", room.transform, center + new Vector3(0f, 0.7f, 6.8f), new Vector3(18f, 1.4f, 4.5f), _matFloorGrate);
        CreateRamp("Cmd_DeckRamp_L", room.transform, center + new Vector3(-6.5f, 0.7f, 3.8f), new Vector3(2.4f, 1.4f, 2.8f), -24f);
        CreateRamp("Cmd_DeckRamp_R", room.transform, center + new Vector3(6.5f, 0.7f, 3.8f), new Vector3(2.4f, 1.4f, 2.8f), -24f);

        // Server Bank Aisles (Cover & Sci-Fi Atmosphere)
        for (int i = -1; i <= 1; i += 2)
        {
            float xOffset = i * 6.2f;
            CreateServerRackStack("Server_Rack_" + i + "_A", _landmarksRoot.transform, center + new Vector3(xOffset, 0f, -2.5f));
            CreateServerRackStack("Server_Rack_" + i + "_B", _landmarksRoot.transform, center + new Vector3(xOffset, 0f, 1.5f));
        }

        // Central Holographic Command Consoles
        CreateCommandConsole("Cmd_MainConsole_1", _landmarksRoot.transform, center + new Vector3(-2.2f, 0f, 0f), 20f);
        CreateCommandConsole("Cmd_MainConsole_2", _landmarksRoot.transform, center + new Vector3(2.2f, 0f, 0f), -20f);

        // Sector Lighting (Cool Blue / White)
        CreateSectorLight("Cmd_Light_Center", room.transform, center + new Vector3(0f, 4.5f, 0f), new Color(0.35f, 0.85f, 1.0f), range: 18f, intensity: 2.2f);
        CreateSectorLight("Cmd_Light_Deck", room.transform, center + new Vector3(0f, 4.5f, 6.5f), new Color(0.85f, 0.95f, 1.0f), range: 14f, intensity: 1.8f);
    }

    // =========================================================================
    // 3. SECTOR 2 (SOUTH) - ARMORY & SECURITY GARRISON
    // =========================================================================
    private void BuildArmoryGarrison(Vector3 center)
    {
        GameObject room = new GameObject("Sector_Armory_Garrison");
        room.transform.SetParent(_facilityRoot.transform, false);

        Vector3 roomSize = new Vector3(24f, wingCeilingHeight, 20f);

        // Floor & Ceiling
        CreateBox("Armory_Floor", room.transform, center + new Vector3(0f, -0.2f, 0f), new Vector3(roomSize.x, 0.4f, roomSize.z), _matFloor);
        CreateBox("Armory_Ceiling", room.transform, center + new Vector3(0f, wingCeilingHeight + 0.2f, 0f), new Vector3(roomSize.x, 0.4f, roomSize.z), _matCeiling);

        // Walls
        BuildSolidWall("Armory_Wall_South", room.transform, center - new Vector3(0f, 0f, roomSize.z * 0.5f), roomSize.x, wingCeilingHeight, isFacingZ: true);
        BuildPortalWall("Armory_Wall_North", room.transform, center + new Vector3(0f, 0f, roomSize.z * 0.5f), roomSize.x, wingCeilingHeight, portalWidth: 6.5f, portalHeight: 4.5f, isFacingZ: true);
        BuildPortalWall("Armory_Wall_West", room.transform, center - new Vector3(roomSize.x * 0.5f, 0f, 0f), roomSize.z, wingCeilingHeight, portalWidth: 4.5f, portalHeight: 4.0f, isFacingZ: false);
        BuildPortalWall("Armory_Wall_East", room.transform, center + new Vector3(roomSize.x * 0.5f, 0f, 0f), roomSize.z, wingCeilingHeight, portalWidth: 4.5f, portalHeight: 4.0f, isFacingZ: false);

        // Fortified Security Checkpoint & Gun Racks
        CreateTacticalBarrier("Armory_Barrier_1", _landmarksRoot.transform, center + new Vector3(-3.5f, 0f, 2.5f), 0f);
        CreateTacticalBarrier("Armory_Barrier_2", _landmarksRoot.transform, center + new Vector3(3.5f, 0f, 2.5f), 0f);

        // Weapon Lockers along south wall
        for (int x = -8; x <= 8; x += 4)
        {
            CreateWeaponLocker("Locker_" + x, _landmarksRoot.transform, center + new Vector3(x, 0f, -8.2f));
        }

        // Ammo Crate Fortifications
        CreateCrateCluster("Armory_Crates_L", _landmarksRoot.transform, center + new Vector3(-6.5f, 0f, -2.5f), _matCargoOlive);
        CreateCrateCluster("Armory_Crates_R", _landmarksRoot.transform, center + new Vector3(6.5f, 0f, -2.5f), _matCargoOlive);

        // Sector Lighting (Tactical Amber / Yellow)
        CreateSectorLight("Armory_Light_Center", room.transform, center + new Vector3(0f, 4.5f, 0f), new Color(1.0f, 0.75f, 0.25f), range: 18f, intensity: 2.2f);
    }

    // =========================================================================
    // 4. SECTOR 3 (WEST) - BIO-CONTAINMENT & CRYO LAB
    // =========================================================================
    private void BuildBioLab(Vector3 center)
    {
        GameObject room = new GameObject("Sector_BioLab_Cryo");
        room.transform.SetParent(_facilityRoot.transform, false);

        Vector3 roomSize = new Vector3(20f, wingCeilingHeight, 24f);

        // Floor & Ceiling
        CreateBox("Bio_Floor", room.transform, center + new Vector3(0f, -0.2f, 0f), new Vector3(roomSize.x, 0.4f, roomSize.z), _matFloor);
        CreateBox("Bio_Ceiling", room.transform, center + new Vector3(0f, wingCeilingHeight + 0.2f, 0f), new Vector3(roomSize.x, 0.4f, roomSize.z), _matCeiling);

        // Walls
        BuildSolidWall("Bio_Wall_West", room.transform, center - new Vector3(roomSize.x * 0.5f, 0f, 0f), roomSize.z, wingCeilingHeight, isFacingZ: false);
        BuildPortalWall("Bio_Wall_East", room.transform, center + new Vector3(roomSize.x * 0.5f, 0f, 0f), roomSize.z, wingCeilingHeight, portalWidth: 6.5f, portalHeight: 4.5f, isFacingZ: false);
        BuildPortalWall("Bio_Wall_North", room.transform, center + new Vector3(0f, 0f, roomSize.z * 0.5f), roomSize.x, wingCeilingHeight, portalWidth: 4.5f, portalHeight: 4.0f, isFacingZ: true);
        BuildPortalWall("Bio_Wall_South", room.transform, center - new Vector3(0f, 0f, roomSize.z * 0.5f), roomSize.x, wingCeilingHeight, portalWidth: 4.5f, portalHeight: 4.0f, isFacingZ: true);

        // 6 Glowing Cryogenic Pods in dual banks
        float[] zPositions = new float[] { -6f, 0f, 6f };
        for (int i = 0; i < zPositions.Length; i++)
        {
            CreateCryoPod("CryoPod_L_" + i, _landmarksRoot.transform, center + new Vector3(-4.8f, 0f, zPositions[i]));
            CreateCryoPod("CryoPod_R_" + i, _landmarksRoot.transform, center + new Vector3(4.8f, 0f, zPositions[i]));
        }

        // Central Medical Examination Station
        CreateCommandConsole("Bio_Console_Center", _landmarksRoot.transform, center + new Vector3(0f, 0f, 0f), 90f);

        // Sector Lighting (Emerald Green / Cyan)
        CreateSectorLight("Bio_Light_Center", room.transform, center + new Vector3(0f, 4.5f, 0f), new Color(0.15f, 0.95f, 0.65f), range: 18f, intensity: 2.2f);
    }

    // =========================================================================
    // 5. SECTOR 4 (EAST) - CARGO AIRLOCK & LOGISTICS DEPOT
    // =========================================================================
    private void BuildCargoLogistics(Vector3 center)
    {
        GameObject room = new GameObject("Sector_Cargo_Logistics");
        room.transform.SetParent(_facilityRoot.transform, false);

        Vector3 roomSize = new Vector3(20f, wingCeilingHeight, 24f);

        // Floor & Ceiling
        CreateBox("Cargo_Floor", room.transform, center + new Vector3(0f, -0.2f, 0f), new Vector3(roomSize.x, 0.4f, roomSize.z), _matFloor);
        CreateBox("Cargo_Ceiling", room.transform, center + new Vector3(0f, wingCeilingHeight + 0.2f, 0f), new Vector3(roomSize.x, 0.4f, roomSize.z), _matCeiling);

        // Walls
        BuildSolidWall("Cargo_Wall_East", room.transform, center + new Vector3(roomSize.x * 0.5f, 0f, 0f), roomSize.z, wingCeilingHeight, isFacingZ: false);
        BuildPortalWall("Cargo_Wall_West", room.transform, center - new Vector3(roomSize.x * 0.5f, 0f, 0f), roomSize.z, wingCeilingHeight, portalWidth: 6.5f, portalHeight: 4.5f, isFacingZ: false);
        BuildPortalWall("Cargo_Wall_North", room.transform, center + new Vector3(0f, 0f, roomSize.z * 0.5f), roomSize.x, wingCeilingHeight, portalWidth: 4.5f, portalHeight: 4.0f, isFacingZ: true);
        BuildPortalWall("Cargo_Wall_South", room.transform, center - new Vector3(0f, 0f, roomSize.z * 0.5f), roomSize.x, wingCeilingHeight, portalWidth: 4.5f, portalHeight: 4.0f, isFacingZ: true);

        // Large Shipping Containers (Climbable / Jetpack Verticality!)
        CreateShippingContainer("Container_Blue_1", _landmarksRoot.transform, center + new Vector3(-3.5f, 0f, -5.5f), new Vector3(5.5f, 2.4f, 2.6f), _matCargoBlue, 15f);
        CreateShippingContainer("Container_Orange_1", _landmarksRoot.transform, center + new Vector3(3.5f, 0f, 4.5f), new Vector3(5.5f, 2.4f, 2.6f), _matCargoOrange, -10f);

        // Double-Stacked Container in Corner
        CreateShippingContainer("Container_Stack_Bottom", _landmarksRoot.transform, center + new Vector3(4.5f, 0f, -6.5f), new Vector3(4.8f, 2.2f, 2.4f), _matCargoBlue, 0f);
        CreateShippingContainer("Container_Stack_Top", _landmarksRoot.transform, center + new Vector3(4.5f, 2.2f, -6.5f), new Vector3(4.8f, 2.2f, 2.4f), _matCargoOrange, 5f);

        // Power Generator with Hazard Decal
        CreateBox("Power_Generator", _landmarksRoot.transform, center + new Vector3(-4.5f, 1.2f, 6.5f), new Vector3(3.2f, 2.4f, 2.8f), _matFloorGrate);

        // Sector Lighting (Industrial Warm Orange)
        CreateSectorLight("Cargo_Light_Center", room.transform, center + new Vector3(0f, 4.5f, 0f), new Color(1.0f, 0.55f, 0.15f), range: 18f, intensity: 2.2f);
    }

    // =========================================================================
    // 6. MAIN CONNECTING HALLWAYS (CARDINAL CHANNELS)
    // =========================================================================
    private void BuildMainHallway(Vector3 center, Vector3 size, bool isFacingZ)
    {
        GameObject hall = new GameObject(isFacingZ ? "Hallway_Z" : "Hallway_X");
        hall.transform.SetParent(_facilityRoot.transform, false);

        // Floor & Ceiling
        CreateBox("Hall_Floor", hall.transform, center + new Vector3(0f, -0.2f, 0f), new Vector3(size.x, 0.4f, size.z), _matFloor);
        CreateBox("Hall_Ceiling", hall.transform, center + new Vector3(0f, size.y + 0.2f, 0f), new Vector3(size.x, 0.4f, size.z), _matCeiling);

        // Lateral Enclosing Walls
        if (isFacingZ)
        {
            CreateBox("Hall_Wall_L", hall.transform, center + new Vector3(-size.x * 0.5f - 0.2f, size.y * 0.5f, 0f), new Vector3(0.4f, size.y, size.z), _matWall);
            CreateBox("Hall_Wall_R", hall.transform, center + new Vector3(size.x * 0.5f + 0.2f, size.y * 0.5f, 0f), new Vector3(0.4f, size.y, size.z), _matWall);
        }
        else
        {
            CreateBox("Hall_Wall_F", hall.transform, center + new Vector3(0f, size.y * 0.5f, size.z * 0.5f + 0.2f), new Vector3(size.x, size.y, 0.4f), _matWall);
            CreateBox("Hall_Wall_B", hall.transform, center + new Vector3(0f, size.y * 0.5f, -size.z * 0.5f - 0.2f), new Vector3(size.x, size.y, 0.4f), _matWall);
        }

        // Corridor Overhead Downlight
        CreateSectorLight("Hall_Light", hall.transform, center + new Vector3(0f, size.y - 0.5f, 0f), new Color(0.85f, 0.95f, 1.0f), range: 10f, intensity: 1.4f);
    }

    // =========================================================================
    // 7. OUTER PERIMETER RING CORRIDORS (CORNER BY-PASSES)
    // =========================================================================
    private void BuildPerimeterRingCorridors()
    {
        GameObject ring = new GameObject("Facility_Perimeter_Ring");
        ring.transform.SetParent(_facilityRoot.transform, false);

        // Corner Junctions:
        // NW: (-26, 0, 26) connecting Command West to BioLab North
        // NE: (26, 0, 26) connecting Command East to Cargo North
        // SW: (-26, 0, -26) connecting Armory West to BioLab South
        // SE: (26, 0, -26) connecting Armory East to Cargo South

        BuildCornerJunction("Junction_NW", ring.transform, new Vector3(-26f, 0f, 26f));
        BuildCornerJunction("Junction_NE", ring.transform, new Vector3(26f, 0f, 26f));
        BuildCornerJunction("Junction_SW", ring.transform, new Vector3(-26f, 0f, -26f));
        BuildCornerJunction("Junction_SE", ring.transform, new Vector3(26f, 0f, -26f));
    }

    private void BuildCornerJunction(string name, Transform parent, Vector3 center)
    {
        GameObject junc = new GameObject(name);
        junc.transform.SetParent(parent, false);

        float size = 8.5f;
        float h = 4.6f;

        CreateBox(name + "_Floor", junc.transform, center + new Vector3(0f, -0.2f, 0f), new Vector3(size, 0.4f, size), _matFloor);
        CreateBox(name + "_Ceiling", junc.transform, center + new Vector3(0f, h + 0.2f, 0f), new Vector3(size, 0.4f, size), _matCeiling);

        // Outer corner solid walls
        float signX = Mathf.Sign(center.x);
        float signZ = Mathf.Sign(center.z);

        CreateBox(name + "_OuterWallX", junc.transform, center + new Vector3(signX * (size * 0.5f + 0.2f), h * 0.5f, 0f), new Vector3(0.4f, h, size), _matWall);
        CreateBox(name + "_OuterWallZ", junc.transform, center + new Vector3(0f, h * 0.5f, signZ * (size * 0.5f + 0.2f)), new Vector3(size, h, 0.4f), _matWall);

        // Cover Crate in Corner
        CreateCrateCluster(name + "_Crates", _landmarksRoot.transform, center + new Vector3(signX * 1.8f, 0f, signZ * 1.8f), _matCargoBlue);

        // Soft Downlight
        CreateSectorLight(name + "_Light", junc.transform, center + new Vector3(0f, h - 0.4f, 0f), new Color(0.9f, 0.95f, 1.0f), range: 9f, intensity: 1.2f);
    }

    // =========================================================================
    // 8. ARCHITECTURAL HELPERS (PRIMITIVES & COLLIDERS)
    // =========================================================================
    private GameObject CreateBox(string name, Transform parent, Vector3 pos, Vector3 size, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = size;
        if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    private void BuildSolidWall(string name, Transform parent, Vector3 pos, float length, float height, bool isFacingZ)
    {
        Vector3 size = isFacingZ ? new Vector3(length, height, 0.5f) : new Vector3(0.5f, height, length);
        CreateBox(name, parent, pos + new Vector3(0f, height * 0.5f, 0f), size, _matWall);
    }

    private void BuildPortalWall(string name, Transform parent, Vector3 pos, float totalLength, float height, float portalWidth, float portalHeight, bool isFacingZ)
    {
        GameObject wallGroup = new GameObject(name);
        wallGroup.transform.SetParent(parent, false);

        float sideWidth = (totalLength - portalWidth) * 0.5f;

        if (isFacingZ)
        {
            // Left Wall
            CreateBox("Side_L", wallGroup.transform, pos + new Vector3(-portalWidth * 0.5f - sideWidth * 0.5f, height * 0.5f, 0f), new Vector3(sideWidth, height, 0.5f), _matWall);
            // Right Wall
            CreateBox("Side_R", wallGroup.transform, pos + new Vector3(portalWidth * 0.5f + sideWidth * 0.5f, height * 0.5f, 0f), new Vector3(sideWidth, height, 0.5f), _matWall);
            // Lintel over portal
            float topH = height - portalHeight;
            if (topH > 0.1f)
            {
                CreateBox("Lintel", wallGroup.transform, pos + new Vector3(0f, portalHeight + topH * 0.5f, 0f), new Vector3(portalWidth, topH, 0.5f), _matWallTrim);
            }
        }
        else
        {
            // Back Wall
            CreateBox("Side_B", wallGroup.transform, pos + new Vector3(0f, height * 0.5f, -portalWidth * 0.5f - sideWidth * 0.5f), new Vector3(0.5f, height, sideWidth), _matWall);
            // Front Wall
            CreateBox("Side_F", wallGroup.transform, pos + new Vector3(0f, height * 0.5f, portalWidth * 0.5f + sideWidth * 0.5f), new Vector3(0.5f, height, sideWidth), _matWall);
            // Lintel over portal
            float topH = height - portalHeight;
            if (topH > 0.1f)
            {
                CreateBox("Lintel", wallGroup.transform, pos + new Vector3(0f, portalHeight + topH * 0.5f, 0f), new Vector3(0.5f, topH, portalWidth), _matWallTrim);
            }
        }
    }

    private void CreateSupportPillar(string name, Transform parent, Vector3 pos, float height)
    {
        CreateBox(name, parent, pos + new Vector3(0f, height * 0.5f, 0f), new Vector3(1.8f, height, 1.8f), _matWallTrim);
    }

    private void CreateRamp(string name, Transform parent, Vector3 pos, Vector3 size, float angleX, bool rotateY90 = false)
    {
        GameObject ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ramp.name = name;
        ramp.transform.SetParent(parent, false);
        ramp.transform.position = pos;
        ramp.transform.localScale = size;
        ramp.transform.rotation = Quaternion.Euler(angleX, rotateY90 ? 90f : 0f, 0f);
        ramp.GetComponent<MeshRenderer>().sharedMaterial = _matFloorGrate;
    }

    private Transform CreateTorusRing(string name, Transform parent, Vector3 pos, float diameter, float thickness, Material mat)
    {
        GameObject ring = new GameObject(name);
        ring.transform.SetParent(parent, false);
        ring.transform.position = pos;

        int segments = 16;
        float radius = diameter * 0.5f;
        for (int i = 0; i < segments; i++)
        {
            float theta = (i / (float)segments) * Mathf.PI * 2f;
            float x = Mathf.Cos(theta) * radius;
            float z = Mathf.Sin(theta) * radius;

            GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seg.transform.SetParent(ring.transform, false);
            seg.transform.localPosition = new Vector3(x, 0f, z);
            seg.transform.localScale = new Vector3(thickness * 1.5f, thickness, (2f * Mathf.PI * radius / segments) * 1.05f);
            seg.transform.localRotation = Quaternion.Euler(0f, -theta * Mathf.Rad2Deg, 0f);
            seg.GetComponent<MeshRenderer>().sharedMaterial = mat;
            // Disable colliders on visual energy rings
            var col = seg.GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }

        return ring.transform;
    }

    private void BuildCatwalk(string name, Transform parent, Vector3 pos, Vector3 size, bool isNorth)
    {
        GameObject catwalk = new GameObject(name);
        catwalk.transform.SetParent(parent, false);

        // Floor
        CreateBox("Walkway_Floor", catwalk.transform, pos, size, _matFloorGrate);

        // Railing (Front edge facing core)
        float railZ = isNorth ? pos.z - size.z * 0.5f : pos.z + size.z * 0.5f;
        CreateBox("Railing_Main", catwalk.transform, new Vector3(pos.x, pos.y + 0.6f, railZ), new Vector3(size.x, 1.1f, 0.15f), _matWallTrim);

        // Stairs accessing catwalk
        float stairX = isNorth ? -10.5f : 10.5f;
        float stairSign = isNorth ? -1f : 1f;
        CreateRamp("Catwalk_Stairs", catwalk.transform, new Vector3(stairX, pos.y * 0.5f, pos.z + stairSign * 1.8f), new Vector3(2.2f, 0.3f, 4.5f), stairSign * 35f);
    }

    private void CreateTacticalBarrier(string name, Transform parent, Vector3 pos, float rotY)
    {
        GameObject barrier = GameObject.CreatePrimitive(PrimitiveType.Cube);
        barrier.name = name;
        barrier.transform.SetParent(parent, false);
        barrier.transform.position = pos + new Vector3(0f, 0.6f, 0f);
        barrier.transform.localScale = new Vector3(2.6f, 1.2f, 0.38f);
        barrier.transform.rotation = Quaternion.Euler(0f, rotY, 0f);
        barrier.GetComponent<MeshRenderer>().sharedMaterial = _matWallTrim;
    }

    private void CreateCommandConsole(string name, Transform parent, Vector3 pos, float rotY)
    {
        GameObject console = new GameObject(name);
        console.transform.SetParent(parent, false);
        console.transform.position = pos;
        console.transform.rotation = Quaternion.Euler(0f, rotY, 0f);

        // Desk Base
        CreateBox("Desk_Base", console.transform, pos + new Vector3(0f, 0.45f, 0f), new Vector3(2.4f, 0.9f, 0.9f), _matWallTrim);
        // Angled Screen
        GameObject screen = CreateBox("Holo_Screen", console.transform, pos + new Vector3(0f, 1.15f, 0.18f), new Vector3(2.2f, 0.6f, 0.1f), _matTerminalScreen);
        screen.transform.rotation = Quaternion.Euler(-25f, rotY, 0f);
    }

    private void CreateServerRackStack(string name, Transform parent, Vector3 pos)
    {
        GameObject stack = CreateBox(name, parent, pos + new Vector3(0f, 1.8f, 0f), new Vector3(1.4f, 3.6f, 0.9f), _matWallTrim);
        // LED status strip on front
        CreateBox("LED_Strip", stack.transform, pos + new Vector3(0f, 1.8f, 0.48f), new Vector3(1.1f, 3.2f, 0.05f), _matTerminalScreen);
    }

    private void CreateWeaponLocker(string name, Transform parent, Vector3 pos)
    {
        CreateBox(name, parent, pos + new Vector3(0f, 1.4f, 0f), new Vector3(1.8f, 2.8f, 0.65f), _matWallTrim);
    }

    private void CreateCryoPod(string name, Transform parent, Vector3 pos)
    {
        GameObject pod = new GameObject(name);
        pod.transform.SetParent(parent, false);
        pod.transform.position = pos;

        // Base & Cap
        CreateBox("Pod_Base", pod.transform, pos + new Vector3(0f, 0.2f, 0f), new Vector3(1.6f, 0.4f, 1.6f), _matWallTrim);
        CreateBox("Pod_Cap", pod.transform, pos + new Vector3(0f, 3.0f, 0f), new Vector3(1.6f, 0.4f, 1.6f), _matWallTrim);

        // Glass Chamber
        GameObject glass = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        glass.name = "Pod_Chamber";
        glass.transform.SetParent(pod.transform, false);
        glass.transform.position = pos + new Vector3(0f, 1.6f, 0f);
        glass.transform.localScale = new Vector3(1.3f, 1.2f, 1.3f);
        glass.GetComponent<MeshRenderer>().sharedMaterial = _matCryoGlass;
    }

    private void CreateShippingContainer(string name, Transform parent, Vector3 pos, Vector3 size, Material mat, float rotY)
    {
        GameObject cont = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cont.name = name;
        cont.transform.SetParent(parent, false);
        cont.transform.position = pos + new Vector3(0f, size.y * 0.5f, 0f);
        cont.transform.localScale = size;
        cont.transform.rotation = Quaternion.Euler(0f, rotY, 0f);
        cont.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    private void CreateCrateCluster(string name, Transform parent, Vector3 pos, Material mat)
    {
        GameObject cluster = new GameObject(name);
        cluster.transform.SetParent(parent, false);

        CreateBox("Crate_1", cluster.transform, pos + new Vector3(-0.6f, 0.6f, 0f), new Vector3(1.2f, 1.2f, 1.2f), mat);
        CreateBox("Crate_2", cluster.transform, pos + new Vector3(0.6f, 0.6f, 0f), new Vector3(1.2f, 1.2f, 1.2f), mat);
        CreateBox("Crate_3", cluster.transform, pos + new Vector3(0f, 1.6f, 0f), new Vector3(1.0f, 1.0f, 1.0f), mat);
    }

    private void CreateSectorLight(string name, Transform parent, Vector3 pos, Color color, float range, float intensity)
    {
        GameObject lightObj = new GameObject(name);
        lightObj.transform.SetParent(parent, false);
        lightObj.transform.position = pos;
        Light l = lightObj.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.range = range;
        l.intensity = intensity;
    }

    private void BuildSafetyFloor()
    {
        // Solid safety catch plane underneath facility at Y = -4m
        CreateBox("Bedrock_Catch_Floor", _facilityRoot.transform, new Vector3(0f, -4.0f, 0f), new Vector3(160f, 1.0f, 160f), _matFloor);
    }

    // =========================================================================
    // 9. SPAWN CONFIGURATION FOR INDOOR FACILITY
    // =========================================================================
    private void RegisterFacilitySpawns()
    {
        if (OasisSpawnSystem.Instance == null) return;

        OasisSpawnSystem.Instance.ClearRegisteredHangarSpawns();

        // Register 4 Team Staging Bases:
        // Sector 0 (South - Armory): Team 0 (Alpha)
        // Sector 1 (North - Command): Team 1 (Bravo)
        // Sector 2 (West - BioLab): Team 2 (Charlie)
        // Sector 3 (East - Cargo): Team 3 (Delta)

        // Team 0 (Alpha) - South Armory
        RegisterTeamSpawnCluster(0, new Vector3(0f, 0.2f, -34f), Quaternion.Euler(0f, 0f, 0f));

        // Team 1 (Bravo) - North Command
        RegisterTeamSpawnCluster(1, new Vector3(0f, 0.2f, 34f), Quaternion.Euler(0f, 180f, 0f));

        // Team 2 (Charlie) - West Bio-Lab
        RegisterTeamSpawnCluster(2, new Vector3(-34f, 0.2f, 0f), Quaternion.Euler(0f, 90f, 0f));

        // Team 3 (Delta) - East Cargo Logistics
        RegisterTeamSpawnCluster(3, new Vector3(34f, 0.2f, 0f), Quaternion.Euler(0f, -90f, 0f));

        // Also register 4 perimeter tactical fallback spawns in the corner junctions
        OasisSpawnSystem.Instance.RegisterHangarInteriorSpawn(4, new Vector3(-24f, 0.2f, 24f), Quaternion.Euler(0f, 135f, 0f));
        OasisSpawnSystem.Instance.RegisterHangarInteriorSpawn(5, new Vector3(24f, 0.2f, 24f), Quaternion.Euler(0f, -135f, 0f));
        OasisSpawnSystem.Instance.RegisterHangarInteriorSpawn(6, new Vector3(-24f, 0.2f, -24f), Quaternion.Euler(0f, 45f, 0f));
        OasisSpawnSystem.Instance.RegisterHangarInteriorSpawn(7, new Vector3(24f, 0.2f, -24f), Quaternion.Euler(0f, -45f, 0f));

        if (OasisShooterManager.Instance != null)
        {
            OasisShooterManager.Instance.SafeShelterCenter = new Vector3(0f, 0.2f, -34f);
            OasisShooterManager.Instance.SafeShelterRadius = 9.0f;
            OasisShooterManager.Instance.HasSafeShelter = true;
        }
    }

    private void RegisterTeamSpawnCluster(int teamId, Vector3 basePos, Quaternion baseRot)
    {
        Vector3 right = baseRot * Vector3.right;
        Vector3 forward = baseRot * Vector3.forward;

        Vector3[] offsets = new Vector3[]
        {
            -right * 2.5f - forward * 1.2f,
             right * 2.5f - forward * 1.2f,
            -right * 1.2f - forward * 3.0f,
             right * 1.2f - forward * 3.0f
        };

        for (int s = 0; s < 4; s++)
        {
            Vector3 p = basePos + offsets[s];
            OasisSpawnSystem.Instance.RegisterHangarInteriorSpawn(teamId, p, baseRot);
        }
    }

    private void AttachAmbientHum()
    {
        var audio = gameObject.GetComponent<AudioSource>();
        if (audio == null) audio = gameObject.AddComponent<AudioSource>();
        audio.loop = true;
        audio.playOnAwake = true;
        audio.volume = 0.22f;
        audio.spatialBlend = 0f; // 2D ambient facility hum
        audio.clip = ProceduralAudio.GetBeamHumClip();
        if (audio.clip != null && !audio.isPlaying) audio.Play();
    }
}
