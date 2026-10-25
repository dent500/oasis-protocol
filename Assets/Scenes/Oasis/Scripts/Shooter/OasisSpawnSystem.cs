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
    /// Samples ground height down to the authentic desert sand dunes, strictly ignoring building roofs, canopies, and invisible safety catch planes.
    /// </summary>
    public float SampleGroundY(float x, float z, float fallbackY)
    {
        RaycastHit[] hits = Physics.RaycastAll(new Vector3(x, 40f, z), Vector3.down, 80f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider == null) continue;
            if (hit.collider.isTrigger) continue;
            if (hit.collider.GetComponent<CharacterController>() != null) continue;
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
                n.IndexOf("Perimeter", System.StringComparison.OrdinalIgnoreCase) >= 0)
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
    /// Configures canonical base sectors (Alpha -> South, Bravo -> North, Charlie -> West, Delta -> East)
    /// to ensure 100% deterministic spawn and respawn synchronization between Host, Clients, and AI.
    /// </summary>
    public void PickRandomMatchup()
    {
        if (teamSectors == null || teamSectors.Length < 4)
        {
            teamSectors = new SpawnSector[4];
        }

        teamSectors[0] = SpawnSector.SouthCanyonPass; // Team 0: Alpha (Blue) - South Base
        teamSectors[1] = SpawnSector.NorthDunes;       // Team 1: Bravo (Red) - North Base
        teamSectors[2] = SpawnSector.WestOutpost;      // Team 2: Charlie (Green) - West Base
        teamSectors[3] = SpawnSector.EastGorge;        // Team 3: Delta (Amber) - East Base

        alphaSector = teamSectors[0];
        bravoSector = teamSectors[1];
        charlieSector = teamSectors[2];
        deltaSector = teamSectors[3];

        Debug.Log($"<color=#26D9FF>[OasisSpawnSystem]</color> Tactical Sectors Configured: Alpha [{GetSectorDisplayName(alphaSector)}], Bravo [{GetSectorDisplayName(bravoSector)}], Charlie [{GetSectorDisplayName(charlieSector)}], Delta [{GetSectorDisplayName(deltaSector)}]");
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

    public Vector3[] GetSectorSpawnOffsets(SpawnSector sector)
    {
        switch (sector)
        {
            case SpawnSector.SouthCanyonPass:
                return new Vector3[]
                {
                    new Vector3(-16f, 0f, -121f),
                    new Vector3(-5f, 0f, -125f),
                    new Vector3( 5f, 0f, -125f),
                    new Vector3( 16f, 0f, -121f)
                };
            case SpawnSector.NorthDunes:
                return new Vector3[]
                {
                    new Vector3(-16f, 0f, -183f),
                    new Vector3(-5f, 0f, -180f),
                    new Vector3( 5f, 0f, -180f),
                    new Vector3( 16f, 0f, -183f)
                };
            case SpawnSector.WestOutpost:
                return new Vector3[]
                {
                    new Vector3(-44f, 0f, -142f),
                    new Vector3(-48f, 0f, -150f),
                    new Vector3(-45f, 0f, -158f),
                    new Vector3(-41f, 0f, -150f)
                };
            case SpawnSector.EastGorge:
                return new Vector3[]
                {
                    new Vector3( 44f, 0f, -142f),
                    new Vector3( 48f, 0f, -150f),
                    new Vector3( 45f, 0f, -158f),
                    new Vector3( 41f, 0f, -150f)
                };
            case SpawnSector.OasisCore:
            default:
                return new Vector3[]
                {
                    new Vector3(-6f, 0f, -147f),
                    new Vector3( 6f, 0f, -147f),
                    new Vector3(-6f, 0f, -153f),
                    new Vector3( 6f, 0f, -153f)
                };
        }
    }

    /// <summary>
    /// Gets a grounded spawn position tailored strictly for the designated team ID's base sector.
    /// Alpha (Team 0) -> South Canyon Pass
    /// Bravo (Team 1) -> North Dunes
    /// Charlie (Team 2) -> West Outpost
    /// Delta (Team 3) -> East Gorge
    /// </summary>
    public Vector3 GetTeamSpawnPosition(int teamId, int squadSlot)
    {
        SpawnSector targetSector = GetTeamSector(teamId);
        Vector3[] sectorSpawns = GetSectorSpawnOffsets(targetSector);
        int idx = Mathf.Clamp(Mathf.Abs(squadSlot), 0, sectorSpawns.Length - 1);
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
        return GetTeamSpawnPosition(teamId, isTeammateOfPlayer ? 1 : 0);
    }

    /// <summary>
    /// Gets a spawn position for the designated team ID.
    /// </summary>
    public Vector3 GetRandomSpawnPosition(int teamId)
    {
        return GetTeamSpawnPosition(teamId, 0);
    }

    /// <summary>
    /// Safely teleports a player or AI to their team's designated sector base,
    /// facing inward toward the battlefield center (0, 0, -150).
    /// All members of the same team spawn at the same base in squad formation.
    /// </summary>
    public void RespawnCombatant(Transform combatant, int teamId, int squadSlot = -1)
    {
        if (combatant == null) return;

        if (squadSlot < 0)
        {
            if (combatant.GetComponent<StarterAssets.FirstPersonController>() != null)
            {
                squadSlot = (OasisNetworkManager.Instance != null) ? OasisNetworkManager.Instance.myPlayerId : 0;
            }
            else if (combatant.name.StartsWith("CoOp_Operative_"))
            {
                string[] parts = combatant.name.Split('_');
                if (parts.Length > 2 && int.TryParse(parts[2], out int opId))
                {
                    squadSlot = opId;
                }
            }
            else if (combatant.GetComponent<OasisCombatAI>() != null)
            {
                squadSlot = Random.Range(0, 4);
            }
            else
            {
                squadSlot = 0;
            }
        }

        Vector3 spawnPos = GetTeamSpawnPosition(teamId, squadSlot);

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
