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
                n.IndexOf("AI_", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Bot_", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }
            return hit.point.y;
        }

        // If fallback is below the bedrock catch plane or in the sky, clamp to authentic canyon sand level
        if (fallbackY < -7.0f || fallbackY > 15.0f)
        {
            return -2.2f;
        }
        return fallbackY;
    }

    public enum SpawnSector
    {
        SouthCanyonPass = 0,
        NorthDunes = 1,
        WestOutpost = 2,
        EastGorge = 3,
        OasisCore = 4
    }

    [Header("Active Matchup Sectors")]
    public SpawnSector alphaSector = SpawnSector.SouthCanyonPass;
    public SpawnSector bravoSector = SpawnSector.NorthDunes;
    public SpawnSector charlieSector = SpawnSector.WestOutpost;
    public SpawnSector deltaSector = SpawnSector.EastGorge;
    public SpawnSector[] teamSectors = new SpawnSector[]
    {
        SpawnSector.SouthCanyonPass, // Team 0: Alpha (Blue) - South Base
        SpawnSector.NorthDunes,       // Team 1: Bravo (Red) - North Base
        SpawnSector.WestOutpost,      // Team 2: Charlie (Green) - West Base
        SpawnSector.EastGorge         // Team 3: Delta (Amber) - East Base
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
    /// Randomly assigns distinct tactical sectors to all active teams at match start.
    /// Eliminates static, predictable base camping across multiple matches.
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

        // Fisher-Yates shuffle to randomly assign base sectors
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

        alphaSector = teamSectors[0];
        bravoSector = teamSectors[1];
        charlieSector = teamSectors[2];
        deltaSector = teamSectors[3];

        Debug.Log($"<color=#26D9FF>[OasisSpawnSystem]</color> Randomized Matchup Bases: Alpha [{GetSectorDisplayName(alphaSector)}], Bravo [{GetSectorDisplayName(bravoSector)}], Charlie [{GetSectorDisplayName(charlieSector)}], Delta [{GetSectorDisplayName(deltaSector)}]");
    }

    public static string GetSectorDisplayName(SpawnSector sector)
    {
        switch (sector)
        {
            case SpawnSector.NorthDunes: return "NORTH DUNES";
            case SpawnSector.SouthCanyonPass: return "SOUTH CANYON PASS";
            case SpawnSector.WestOutpost: return "WEST OUTPOST";
            case SpawnSector.EastGorge: return "EAST GORGE";
            case SpawnSector.OasisCore: return "OASIS CORE";
            default: return "CANYON";
        }
    }

    public Vector3 GetSectorCenter(SpawnSector sector)
    {
        switch (sector)
        {
            case SpawnSector.SouthCanyonPass: return new Vector3(0f, 0f, -124f);
            case SpawnSector.NorthDunes: return new Vector3(0f, 0f, -180f);
            case SpawnSector.WestOutpost: return new Vector3(-38f, 0f, -150f);
            case SpawnSector.EastGorge: return new Vector3(38f, 0f, -150f);
            case SpawnSector.OasisCore: return new Vector3(0f, 0f, -150f);
            default: return new Vector3(0f, 0f, -150f);
        }
    }

    /// <summary>
    /// Neutral and perimeter flank rally nodes used for anti-spawn-camping fallbacks
    /// when an entire team base is being swarmed by enemy campers.
    /// </summary>
    public static readonly Vector3[] TacticalFlankSpawns = new Vector3[]
    {
        new Vector3(-26f, 0f, -136f), // South-West Flank Terrace
        new Vector3( 26f, 0f, -136f), // South-East Flank Terrace
        new Vector3(-26f, 0f, -164f), // North-West Dune Crest
        new Vector3( 26f, 0f, -164f), // North-East Dune Crest
        new Vector3(-34f, 0f, -148f), // Mid-West Canyon Defilade
        new Vector3( 34f, 0f, -148f)  // Mid-East Canyon Defilade
    };

    public Vector3[] GetSectorSpawnOffsets(SpawnSector sector)
    {
        switch (sector)
        {
            case SpawnSector.SouthCanyonPass:
                return new Vector3[]
                {
                    new Vector3(-16f, 0f, -121f),
                    new Vector3( -5f, 0f, -125f),
                    new Vector3(  5f, 0f, -125f),
                    new Vector3( 16f, 0f, -121f),
                    new Vector3( -9f, 0f, -115f),
                    new Vector3(  9f, 0f, -115f),
                    new Vector3(-22f, 0f, -126f),
                    new Vector3( 22f, 0f, -126f)
                };
            case SpawnSector.NorthDunes:
                return new Vector3[]
                {
                    new Vector3(-14f, 0f, -177f),
                    new Vector3( -5f, 0f, -175f),
                    new Vector3(  5f, 0f, -175f),
                    new Vector3( 14f, 0f, -177f),
                    new Vector3( -9f, 0f, -183f),
                    new Vector3(  9f, 0f, -183f),
                    new Vector3(-20f, 0f, -172f),
                    new Vector3( 20f, 0f, -172f)
                };
            case SpawnSector.WestOutpost:
                return new Vector3[]
                {
                    new Vector3(-44f, 0f, -142f),
                    new Vector3(-48f, 0f, -150f),
                    new Vector3(-45f, 0f, -158f),
                    new Vector3(-41f, 0f, -150f),
                    new Vector3(-46f, 0f, -137f),
                    new Vector3(-46f, 0f, -163f),
                    new Vector3(-36f, 0f, -144f),
                    new Vector3(-36f, 0f, -156f)
                };
            case SpawnSector.EastGorge:
                return new Vector3[]
                {
                    new Vector3( 44f, 0f, -142f),
                    new Vector3( 48f, 0f, -150f),
                    new Vector3( 45f, 0f, -158f),
                    new Vector3( 41f, 0f, -150f),
                    new Vector3( 46f, 0f, -137f),
                    new Vector3( 46f, 0f, -163f),
                    new Vector3( 36f, 0f, -144f),
                    new Vector3( 36f, 0f, -156f)
                };
            case SpawnSector.OasisCore:
            default:
                return new Vector3[]
                {
                    new Vector3(-12f, 0f, -145f),
                    new Vector3( 12f, 0f, -145f),
                    new Vector3(-12f, 0f, -155f),
                    new Vector3( 12f, 0f, -155f),
                    new Vector3(-24f, 0f, -150f),
                    new Vector3( 24f, 0f, -150f),
                    new Vector3(  0f, 0f, -138f),
                    new Vector3(  0f, 0f, -162f)
                };
        }
    }

    /// <summary>
    /// Intelligently finds a randomized, tactically safe spawn location for a combatant.
    /// Evaluates distance to active enemies: eliminates or penalizes any spawn point where enemies
    /// are closer than 18m (spawn-camping distance). If the base sector is completely overrun,
    /// automatically selects an alternate tactical flank position.
    /// </summary>
    public Vector3 GetTacticalSafeSpawnPosition(int teamId, Transform combatant = null)
    {
        SpawnSector baseSector = GetTeamSector(teamId);
        Vector3[] baseSpawns = GetSectorSpawnOffsets(baseSector);

        // Collect all active enemy positions (bots + player)
        List<Vector3> enemyPositions = new List<Vector3>();
        foreach (var bot in OasisCombatAI.ActiveBots)
        {
            if (bot != null && bot.teamId != teamId && !bot.isDead && bot.gameObject.activeInHierarchy)
            {
                enemyPositions.Add(bot.transform.position);
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

        // Build list of candidate positions: base spawns first, flank spawns second
        List<Vector3> candidates = new List<Vector3>(baseSpawns);
        candidates.AddRange(TacticalFlankSpawns);

        // If no enemies in play yet (e.g. initial launch), pick randomly from base spawns with slight lateral jitter
        if (enemyPositions.Count == 0)
        {
            Vector3 raw = baseSpawns[Random.Range(0, baseSpawns.Length)];
            float jx = Random.Range(-2.0f, 2.0f);
            float jz = Random.Range(-2.0f, 2.0f);
            float gy = SampleGroundY(raw.x + jx, raw.z + jz, GetPlayableCenter().y);
            return new Vector3(raw.x + jx, gy + 0.08f, raw.z + jz);
        }

        Vector3 bestPos = baseSpawns[Random.Range(0, baseSpawns.Length)];
        float bestScore = -99999f;

        for (int i = 0; i < candidates.Count; i++)
        {
            Vector3 c = candidates[i];
            float minEnemyDist = float.MaxValue;
            for (int e = 0; e < enemyPositions.Count; e++)
            {
                float d = Vector3.Distance(new Vector3(c.x, 0f, c.z), new Vector3(enemyPositions[e].x, 0f, enemyPositions[e].z));
                if (d < minEnemyDist) minEnemyDist = d;
            }

            // Anti-camp scoring: heavily penalize points where enemies are within 16m
            float score = minEnemyDist;
            if (minEnemyDist < 16f)
            {
                score -= 1000f; // Strongly avoid camped points
            }
            else if (i < baseSpawns.Length)
            {
                // Mild preference for home base nodes when safe
                score += 8f;
            }

            // Add small random noise to prevent deterministic identical picks
            score += Random.Range(0f, 6f);

            if (score > bestScore)
            {
                bestScore = score;
                bestPos = c;
            }
        }

        // Apply randomized lateral dispersion (+/- 2.0m)
        float jitterX = Random.Range(-2.0f, 2.0f);
        float jitterZ = Random.Range(-2.0f, 2.0f);
        float spawnX = bestPos.x + jitterX;
        float spawnZ = bestPos.z + jitterZ;
        float groundY = SampleGroundY(spawnX, spawnZ, GetPlayableCenter().y);

        return new Vector3(spawnX, groundY + 0.08f, spawnZ);
    }

    /// <summary>
    /// Gets a grounded spawn position tailored strictly for the designated team ID's base sector.
    /// </summary>
    public Vector3 GetTeamSpawnPosition(int teamId, int squadSlot)
    {
        SpawnSector targetSector = GetTeamSector(teamId);
        Vector3[] sectorSpawns = GetSectorSpawnOffsets(targetSector);
        int idx = Mathf.Abs(squadSlot) % sectorSpawns.Length;
        Vector3 chosen = sectorSpawns[idx];
        float fallbackY = GetPlayableCenter().y;
        float gy = SampleGroundY(chosen.x, chosen.z, fallbackY);
        return new Vector3(chosen.x, gy + 0.08f, chosen.z);
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

        // Use smart tactical safe spawn to eliminate spawn camping
        Vector3 spawnPos = GetTacticalSafeSpawnPosition(teamId, combatant);

        var cc = combatant.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        combatant.position = spawnPos;

        // Face inward toward the arena center (0, 0, -150)
        Vector3 arenaCenter = new Vector3(0f, spawnPos.y, -150f);
        Vector3 toCenter = (arenaCenter - spawnPos);
        toCenter.y = 0f;
        if (toCenter.sqrMagnitude > 0.1f)
        {
            combatant.rotation = Quaternion.LookRotation(toCenter.normalized);
        }

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
