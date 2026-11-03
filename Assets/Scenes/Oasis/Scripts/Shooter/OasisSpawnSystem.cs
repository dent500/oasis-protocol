using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tactical Spawn System for Oasis Team Combat.
/// Dynamically anchors spawn points around the authentic playable desert canyon
/// ensuring all players and AI operatives remain 100% contained inside the map.
/// </summary>
public class OasisSpawnSystem : MonoBehaviour
{
    private static OasisSpawnSystem _instance;
    public static OasisSpawnSystem Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindAnyObjectByType<OasisSpawnSystem>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("OasisSpawnSystem");
                    _instance = go.AddComponent<OasisSpawnSystem>();
                }
            }
            return _instance;
        }
        private set { _instance = value; }
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this);
            return;
        }
        _instance = this;
    }

    /// <summary>
    /// Gets the authentic center anchor of the playable Oasis map.
    /// </summary>
    public Vector3 GetPlayableCenter()
    {
        return new Vector3(0f, -2.0f, -150.0f);
    }

    /// <summary>
    /// Samples ground height down to the authentic desert sand dunes, strictly ignoring building roofs, canopies,
    /// character colliders, AI meshes, and invisible safety catch planes.
    /// </summary>
    public float SampleGroundY(float x, float z, float fallbackY)
    {
        RaycastHit[] hits = Physics.RaycastAll(new Vector3(x, 40f, z), Vector3.down, 80f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider == null) continue;
            if (hit.collider.isTrigger) continue;
            if (hit.collider.GetComponentInParent<CharacterController>() != null) continue;
            if (hit.collider.GetComponentInParent<OasisCombatAI>() != null) continue;
            if (hit.collider.CompareTag("Player")) continue;

            string n = hit.collider.gameObject.name;
            if (n.IndexOf("Roof", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Canopy", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Ceiling", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Bedrock", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Underground", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Safety_Floor", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Bunker", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Pillar", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Perimeter", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Wall", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Baffle", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Shield", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Lintel", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Parapet", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Upper_Obs_Deck", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Dish", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Mast", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Beacon", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Palm", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Shrub", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Arch", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Buttress", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("AI_", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Bot_", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }
            return hit.point.y;
        }

        if (OasisWorldExpander.Instance != null &&
            OasisWorldExpander.Instance.TrySampleAuthenticTerrainY(x, z, out float authenticY))
        {
            return authenticY;
        }

        // If fallback is below the bedrock catch plane or in the sky, clamp to authentic canyon sand level
        if (fallbackY < -6.5f || fallbackY > 12.0f)
        {
            return -2.2f;
        }
        return fallbackY;
    }

    /// <summary>
    /// Samples floor height strictly inside an undercover Hangar Hideout (starting below the 4.8m roof)
    /// so overhangs, palm fronds, or rooftop structures above the hangar can never intercept the raycast.
    /// </summary>
    private float SampleHangarInteriorFloorY(Vector3 slotPos)
    {
        RaycastHit[] hits = Physics.RaycastAll(new Vector3(slotPos.x, slotPos.y + 2.2f, slotPos.z), Vector3.down, 5.5f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider == null || hit.collider.isTrigger) continue;
            string n = hit.collider.gameObject.name;
            if (n.IndexOf("HangarFloor", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return hit.point.y;
            }
        }
        foreach (var hit in hits)
        {
            if (hit.collider == null || hit.collider.isTrigger) continue;
            if (hit.collider.GetComponentInParent<CharacterController>() != null) continue;
            if (hit.collider.GetComponentInParent<OasisCombatAI>() != null) continue;
            if (hit.collider.CompareTag("Player")) continue;
            string n = hit.collider.gameObject.name;
            if (n.IndexOf("HangarFoundation", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Terrain_01", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return hit.point.y;
            }
        }
        return slotPos.y;
    }

    public enum SpawnSector
    {
        SouthCanyonPass = 0,
        NorthDunes = 1,
        WestOutpost = 2,
        EastGorge = 3,
        OasisCore = 4
    }

    /// <summary>
    /// World coordinates of the 8 Enclosed Undercover Sci-Fi Hangar Hideouts placed around the
    /// expanded outer valley floor of Terrain_01 (54m - 74m from center, NEVER in the middle of the map,
    /// and 100% inside the flat walkable desert basin lined by the perimeter rock walls).
    /// </summary>
    public static readonly Vector3[] UndercoverHangarLocations = new Vector3[]
    {
        new Vector3( 16f, 0f,  -94f), // Hangar 0: South Canyon Hangar Outpost
        new Vector3(  2f, 0f, -214f), // Hangar 1: North Dune Hangar Complex
        new Vector3(-58f, 0f, -150f), // Hangar 2: West Ridge Hangar Bastion
        new Vector3( 64f, 0f, -150f), // Hangar 3: East Gorge Hangar Bastion
        new Vector3(-44f, 0f, -104f), // Hangar 4: South-West Cliff Hangar Bay
        new Vector3( 50f, 0f, -106f), // Hangar 5: South-East Cliff Hangar Bay
        new Vector3(-48f, 0f, -198f), // Hangar 6: North-West Basin Hangar Bay
        new Vector3( 52f, 0f, -198f)  // Hangar 7: North-East Basin Hangar Bay
    };

    private readonly List<Vector3> _registeredUndercoverSpots = new List<Vector3>();
    private readonly List<Quaternion> _registeredUndercoverRotations = new List<Quaternion>();
    private readonly List<int> _registeredSpotHangarIndex = new List<int>();
    private int[] _teamInitialHangarIndex = new int[] { 0, 1, 2, 3 };

    public void ClearRegisteredHangarSpawns()
    {
        _registeredUndercoverSpots.Clear();
        _registeredUndercoverRotations.Clear();
        _registeredSpotHangarIndex.Clear();
    }

    public void RegisterHangarInteriorSpawn(int hangarIndex, Vector3 worldPos, Quaternion worldRot)
    {
        _registeredUndercoverSpots.Add(worldPos);
        _registeredUndercoverRotations.Add(worldRot);
        _registeredSpotHangarIndex.Add(hangarIndex);
    }

    /// <summary>
    /// Rebuilds all registered undercover hangar interior spawn slots directly from active
    /// Undercover_Hangar_Hideout transforms in the scene so manual positioning/rotation in the Map Designer
    /// is immediately reflected in player and bot spawning.
    /// </summary>
    public void RebuildHangarSpawnsFromScene()
    {
        ClearRegisteredHangarSpawns();
        Vector3[] localSlots = new Vector3[]
        {
            new Vector3(-3.2f, 0.22f, -1.8f),
            new Vector3( 3.2f, 0.22f, -1.8f),
            new Vector3(-2.0f, 0.22f, -2.6f),
            new Vector3( 2.0f, 0.22f, -2.6f)
        };

        GameObject landmarksRoot = GameObject.Find("Oasis_Expanded_World_Landmarks");
        if (landmarksRoot != null)
        {
            int hCounter = 0;
            for (int c = 0; c < landmarksRoot.transform.childCount; c++)
            {
                Transform child = landmarksRoot.transform.GetChild(c);
                if (child == null || !child.gameObject.activeInHierarchy) continue;
                if (child.name.IndexOf("Undercover_Hangar_Hideout", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    for (int s = 0; s < localSlots.Length; s++)
                    {
                        Vector3 slotWorld = child.TransformPoint(localSlots[s]);
                        RegisterHangarInteriorSpawn(hCounter, slotWorld, child.rotation);
                    }
                    hCounter++;
                }
            }
        }
    }

    [Header("Active Matchup Sectors")]
    public SpawnSector alphaSector = SpawnSector.SouthCanyonPass;
    public SpawnSector bravoSector = SpawnSector.NorthDunes;
    public SpawnSector charlieSector = SpawnSector.WestOutpost;
    public SpawnSector deltaSector = SpawnSector.EastGorge;
    public SpawnSector[] teamSectors = new SpawnSector[]
    {
        SpawnSector.SouthCanyonPass, // Team 0: Alpha (Blue)
        SpawnSector.NorthDunes,      // Team 1: Bravo (Red)
        SpawnSector.WestOutpost,     // Team 2: Charlie (Green)
        SpawnSector.EastGorge        // Team 3: Delta (Amber)
    };

    public SpawnSector GetTeamSector(int teamId)
    {
        teamId = Mathf.Clamp(teamId, 0, 3);
        if (teamSectors != null && teamId < teamSectors.Length)
        {
            return teamSectors[teamId];
        }
        switch (teamId)
        {
            case 0: return SpawnSector.SouthCanyonPass;
            case 1: return SpawnSector.NorthDunes;
            case 2: return SpawnSector.WestOutpost;
            case 3: return SpawnSector.EastGorge;
            default: return SpawnSector.SouthCanyonPass;
        }
    }

    /// <summary>
    /// Randomly assigns distinct undercover Hangar Hideouts across the 8 perimeter hangars at match start.
    /// Zero center spawns; every spawn is inside an enclosed undercover hangar bay.
    /// </summary>
    public void PickRandomMatchup()
    {
        if (teamSectors == null || teamSectors.Length < 4)
        {
            teamSectors = new SpawnSector[4];
        }

        List<SpawnSector> pool = new List<SpawnSector>
        {
            SpawnSector.SouthCanyonPass,
            SpawnSector.NorthDunes,
            SpawnSector.WestOutpost,
            SpawnSector.EastGorge
        };

        for (int i = pool.Count - 1; i > 0; i--)
        {
            int rnd = Random.Range(0, i + 1);
            SpawnSector tmp = pool[i];
            pool[i] = pool[rnd];
            pool[rnd] = tmp;
        }

        for (int t = 0; t < 4; t++)
        {
            teamSectors[t] = pool[t];
        }

        // Also shuffle the 8 undercover Hangar Hideouts for initial team deployment
        List<int> hangarPool = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7 };
        for (int i = hangarPool.Count - 1; i > 0; i--)
        {
            int rnd = Random.Range(0, i + 1);
            int tmp = hangarPool[i];
            hangarPool[i] = hangarPool[rnd];
            hangarPool[rnd] = tmp;
        }
        _teamInitialHangarIndex = new int[] { hangarPool[0], hangarPool[1], hangarPool[2], hangarPool[3] };

        alphaSector = teamSectors[0];
        bravoSector = teamSectors[1];
        charlieSector = teamSectors[2];
        deltaSector = teamSectors[3];

        Debug.Log($"<color=#26D9FF>[OasisSpawnSystem]</color> Randomized Undercover Hangar Matchup: Alpha [Hangar {_teamInitialHangarIndex[0]}], Bravo [Hangar {_teamInitialHangarIndex[1]}], Charlie [Hangar {_teamInitialHangarIndex[2]}], Delta [Hangar {_teamInitialHangarIndex[3]}]");
    }

    public static string GetSectorDisplayName(SpawnSector sector)
    {
        switch (sector)
        {
            case SpawnSector.NorthDunes: return "NORTH DUNE HANGAR";
            case SpawnSector.SouthCanyonPass: return "SOUTH CANYON HANGAR";
            case SpawnSector.WestOutpost: return "WEST RIDGE HANGAR";
            case SpawnSector.EastGorge: return "EAST GORGE HANGAR";
            default: return "PERIMETER HANGAR";
        }
    }

    public Vector3 GetSectorCenter(SpawnSector sector)
    {
        switch (sector)
        {
            case SpawnSector.SouthCanyonPass: return UndercoverHangarLocations[0];
            case SpawnSector.NorthDunes: return UndercoverHangarLocations[1];
            case SpawnSector.WestOutpost: return UndercoverHangarLocations[2];
            case SpawnSector.EastGorge: return UndercoverHangarLocations[3];
            default: return UndercoverHangarLocations[0];
        }
    }

    /// <summary>
    /// Computes the 4 sheltered interior bay positions inside a given Hangar Hideout (0..7).
    /// Always undercover beneath the solid hangar roof and behind the front blast shield.
    /// </summary>
    private Vector3[] ComputeHangarInteriorBaySlots(int hangarIdx)
    {
        hangarIdx = Mathf.Clamp(hangarIdx, 0, UndercoverHangarLocations.Length - 1);
        Vector3 hangarCenter = UndercoverHangarLocations[hangarIdx];
        Vector3 arenaCenter = GetPlayableCenter();
        Vector3 toCenter = (arenaCenter - hangarCenter);
        toCenter.y = 0f;
        if (toCenter.sqrMagnitude < 0.01f) toCenter = Vector3.forward;
        Quaternion rot = Quaternion.LookRotation(toCenter.normalized);

        float gy = SampleGroundY(hangarCenter.x, hangarCenter.z, arenaCenter.y);
        Vector3 basePos = new Vector3(hangarCenter.x, gy + 0.18f, hangarCenter.z);

        // 4 interior undercover slots aligned with the left and right exit lanes
        return new Vector3[]
        {
            basePos + rot * new Vector3(-3.2f, 0f, -2.0f),
            basePos + rot * new Vector3( 3.2f, 0f, -2.0f),
            basePos + rot * new Vector3(-2.1f, 0f, -2.8f),
            basePos + rot * new Vector3( 2.1f, 0f, -2.8f)
        };
    }

    public Vector3[] GetSectorSpawnOffsets(SpawnSector sector)
    {
        int hIdx = 0;
        switch (sector)
        {
            case SpawnSector.SouthCanyonPass: hIdx = 0; break;
            case SpawnSector.NorthDunes: hIdx = 1; break;
            case SpawnSector.WestOutpost: hIdx = 2; break;
            case SpawnSector.EastGorge: hIdx = 3; break;
            default: hIdx = Random.Range(0, UndercoverHangarLocations.Length); break;
        }
        return ComputeHangarInteriorBaySlots(hIdx);
    }

    /// <summary>
    /// Intelligently selects a RANDOMIZED undercover Hangar Hideout spawn point on every spawn/respawn.
    /// Evaluates all 8 perimeter Hangar Hideouts against active enemy positions (AI bots, remote players, and local player),
    /// rejecting any hangar where enemies are within 35m and picking randomly among safe undercover hangars
    /// so opponents can NEVER predict or camp where a player or bot will respawn.
    /// </summary>
    public Vector3 GetTacticalSafeSpawnPosition(int teamId, Transform combatant = null)
    {
        GetTacticalSafeSpawnTransform(teamId, combatant, out Vector3 spawnPos, out _);
        return spawnPos;
    }

    public void GetTacticalSafeSpawnTransform(int teamId, Transform combatant, out Vector3 spawnPos, out Quaternion spawnRot)
    {
        // Collect all active living enemy positions (AI bots + remote real players + local player)
        List<Vector3> enemyPositions = new List<Vector3>();
        foreach (var bot in OasisCombatAI.ActiveBots)
        {
            if (bot != null && bot.teamId != teamId && !bot.isDead && bot.gameObject.activeInHierarchy)
            {
                enemyPositions.Add(bot.transform.position);
            }
        }

        if (OasisNetworkManager.Instance != null)
        {
            var remotes = OasisNetworkManager.Instance.GetRemotePlayerInstances();
            for (int r = 0; r < remotes.Count; r++)
            {
                var rp = remotes[r];
                if (rp != null && rp.rootObject != null && !rp.isDead && rp.teamId != teamId)
                {
                    enemyPositions.Add(rp.rootObject.transform.position);
                }
            }
        }

        if (OasisShooterManager.Instance != null && !OasisShooterManager.Instance.isSimulationMode)
        {
            int pTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
            if (pTeam != teamId && OasisShooterManager.Instance.currentHealth > 0f)
            {
                var pt = OasisShooterManager.Instance.GetPlayerTransform();
                if (pt != null) enemyPositions.Add(pt.position);
            }
        }

        // Build candidate list from registered undercover Hangar Hideouts (or compute directly if not yet registered)
        List<Vector3> candidatePositions = new List<Vector3>();
        List<Quaternion> candidateRotations = new List<Quaternion>();
        if (_registeredUndercoverSpots.Count > 0)
        {
            candidatePositions.AddRange(_registeredUndercoverSpots);
            candidateRotations.AddRange(_registeredUndercoverRotations);
        }
        else
        {
            Vector3 arenaCenter = GetPlayableCenter();
            for (int h = 0; h < UndercoverHangarLocations.Length; h++)
            {
                Vector3[] slots = ComputeHangarInteriorBaySlots(h);
                Vector3 toC = (arenaCenter - UndercoverHangarLocations[h]);
                toC.y = 0f;
                Quaternion rot = (toC.sqrMagnitude > 0.01f) ? Quaternion.LookRotation(toC.normalized) : Quaternion.identity;
                for (int s = 0; s < slots.Length; s++)
                {
                    candidatePositions.Add(slots[s]);
                    candidateRotations.Add(rot);
                }
            }
        }

        // Filter candidates that have no enemy within 35m (strict anti-spawn-camp buffer)
        List<int> safeIndices = new List<int>();
        int furthestIdx = Random.Range(0, candidatePositions.Count);
        float furthestEnemyDist = -1f;

        for (int i = 0; i < candidatePositions.Count; i++)
        {
            Vector3 c = candidatePositions[i];
            float minEnemyDist = float.MaxValue;
            for (int e = 0; e < enemyPositions.Count; e++)
            {
                float d = Vector2.Distance(new Vector2(c.x, c.z), new Vector2(enemyPositions[e].x, enemyPositions[e].z));
                if (d < minEnemyDist) minEnemyDist = d;
            }

            if (minEnemyDist > furthestEnemyDist)
            {
                furthestEnemyDist = minEnemyDist;
                furthestIdx = i;
            }

            if (minEnemyDist >= 35f)
            {
                safeIndices.Add(i);
            }
        }

        // Pick completely at random among all safe undercover hangar bays so spawns are unpredictable!
        int chosenIdx = (safeIndices.Count > 0)
            ? safeIndices[Random.Range(0, safeIndices.Count)]
            : furthestIdx;

        Vector3 chosenPos = candidatePositions[chosenIdx];
        spawnRot = (chosenIdx >= 0 && chosenIdx < candidateRotations.Count) ? candidateRotations[chosenIdx] : Quaternion.identity;

        float groundY = (_registeredUndercoverSpots.Count > 0)
            ? SampleHangarInteriorFloorY(chosenPos)
            : SampleGroundY(chosenPos.x, chosenPos.z, chosenPos.y);
        spawnPos = new Vector3(chosenPos.x, groundY + 0.12f, chosenPos.z);
    }

    /// <summary>
    /// Gets an initial grounded undercover spawn position inside the team's assigned randomized Hangar Hideout.
    /// </summary>
    public Vector3 GetTeamSpawnPosition(int teamId, int squadSlot)
    {
        int clampedTeam = Mathf.Clamp(teamId, 0, 3);
        int hangarIdx = (_teamInitialHangarIndex != null && clampedTeam < _teamInitialHangarIndex.Length)
            ? _teamInitialHangarIndex[clampedTeam]
            : (clampedTeam % UndercoverHangarLocations.Length);

        if (_registeredUndercoverSpots.Count > 0)
        {
            List<Vector3> teamHangarSlots = new List<Vector3>();
            for (int i = 0; i < _registeredUndercoverSpots.Count; i++)
            {
                if (_registeredSpotHangarIndex[i] == hangarIdx)
                {
                    teamHangarSlots.Add(_registeredUndercoverSpots[i]);
                }
            }
            if (teamHangarSlots.Count > 0)
            {
                Vector3 slotPos = teamHangarSlots[Mathf.Abs(squadSlot) % teamHangarSlots.Count];
                float gy = SampleHangarInteriorFloorY(slotPos);
                return new Vector3(slotPos.x, gy + 0.12f, slotPos.z);
            }
        }

        Vector3[] slots = ComputeHangarInteriorBaySlots(hangarIdx);
        Vector3 chosen = slots[Mathf.Abs(squadSlot) % slots.Length];
        float groundY = SampleGroundY(chosen.x, chosen.z, chosen.y);
        return new Vector3(chosen.x, groundY + 0.12f, chosen.z);
    }

    /// <summary>
    /// Gets a spawn position for the designated team ID.
    /// </summary>
    public Vector3 GetRandomSpawnPosition(int teamId, bool isTeammateOfPlayer = false)
    {
        return GetTacticalSafeSpawnPosition(teamId);
    }

    /// <summary>
    /// Gets a spawn position for the designated team ID.
    /// </summary>
    public Vector3 GetRandomSpawnPosition(int teamId)
    {
        return GetTacticalSafeSpawnPosition(teamId);
    }

    /// <summary>
    /// Safely teleports a player or AI to their team's designated sector base or tactical safe flank,
    /// facing inward toward the battlefield center (0, 0, -150).
    /// Prevents spawn camping by dynamically picking clear locations and granting 2.5s spawn shield.
    /// </summary>
    public void RespawnCombatant(Transform combatant, int teamId, int squadSlot = -1)
    {
        if (combatant == null) return;

        // Use smart tactical safe spawn to eliminate spawn camping and face directly out the hangar exit portal
        GetTacticalSafeSpawnTransform(teamId, combatant, out Vector3 spawnPos, out Quaternion spawnRot);

        var cc = combatant.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        combatant.position = spawnPos;
        combatant.rotation = spawnRot;

        // Apply Spawn Shield Protection Grace Period (2.5 seconds)
        var botAI = combatant.GetComponent<OasisCombatAI>();
        if (botAI != null)
        {
            botAI.GrantSpawnProtection(2.5f);
        }
        else if (OasisShooterManager.Instance != null)
        {
            OasisShooterManager.Instance.GrantPlayerSpawnProtection(2.5f);
        }

        // Visual warp effect
        BlasterVFX.SpawnWarpIn(spawnPos);

        // If player has StarterAssets FirstPersonController, level the camera pitch to horizon
        var fpc = combatant.GetComponent<StarterAssets.FirstPersonController>();
        if (fpc != null)
        {
            fpc.ResetCameraPitch(0f);
        }
        var inputs = combatant.GetComponent<StarterAssets.StarterAssetsInputs>();
        if (inputs != null)
        {
            inputs.look = Vector2.zero;
        }
        if (Camera.main != null && (combatant.CompareTag("Player") || fpc != null))
        {
            Camera.main.transform.rotation = combatant.rotation;
        }

        // Restore clean running posture and reset any crouch/downed speeds
        var combatMove = combatant.GetComponent<OasisCombatMovement>();
        if (combatMove != null)
        {
            combatMove.ResetMovementState();
        }

        Physics.SyncTransforms();
        if (cc != null) cc.enabled = true;

        if (combatant.CompareTag("Player") || fpc != null)
        {
            OasisAITelemetryLogger.Instance?.RecordPlayerSpawn(spawnPos);
        }
    }
}
