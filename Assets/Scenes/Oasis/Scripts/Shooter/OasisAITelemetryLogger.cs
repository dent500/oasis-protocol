using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// AI Combat & Behavior Analytics Logger for Oasis FPS Simulation:
/// 1. Chronological Event Journal (Spawns, Shots, Hits, Knockdowns, Revives, Reloads, Maneuvers, Stuck triggers).
/// 2. Per-Bot Comprehensive Metrics (Accuracy %, Damage Dealt/Taken, K/D/R, Engagement Range, Maneuver Distribution).
/// 3. Positional Heatmap Telemetry (Periodic spatial coordinates to detect corner clustering or arena mobility).
/// 4. Auto-export to JSON & Markdown report files + One-click Clipboard Copy for instant AI behavior analysis!
/// 5. Live Interactive OnGUI Telemetry Dashboard (Toggle with F8 or On-Screen Button).
/// </summary>
public class OasisAITelemetryLogger : MonoBehaviour
{
    public static OasisAITelemetryLogger Instance { get; private set; }

    [System.Serializable]
    public class BotMetrics
    {
        public string botName;
        public int teamId; // 0 = Alpha, 1 = Bravo
        public string operativeClass;

        // Combat Stats
        public int knockdownsInflicted = 0;
        public int eliminationsCompleted = 0;
        public int timesDowned = 0;
        public int fullDeaths = 0;
        public int revivesInitiated = 0;
        public int revivesCompleted = 0;
        public int revivesInterrupted = 0;

        // Gunnery Stats
        public int shotsFired = 0;
        public int shotsHit = 0;
        public float totalDamageDealt = 0f;
        public float totalDamageTaken = 0f;
        public int reloadsPerformed = 0;

        // Mobility & State Stats
        public int stuckIncidents = 0;
        public float timeInAggressivePush = 0f;
        public float timeInLateralFlank = 0f;
        public float timeInTacticalFallback = 0f;
        public float timeInReviveAction = 0f;
        public float timeInDownedState = 0f;
        public float totalDistanceMoved = 0f;
        public Vector3 lastRecordedPosition;

        // Engagement Ranges
        public float totalEngagementDistance = 0f;
        public int engagementDistanceSamples = 0;

        public float Accuracy => shotsFired > 0 ? Mathf.Min(100f, ((float)shotsHit / shotsFired) * 100f) : 0f;
        public float AverageEngagementRange => engagementDistanceSamples > 0 ? totalEngagementDistance / engagementDistanceSamples : 0f;
    }

    [System.Serializable]
    public class TelemetryJournalEntry
    {
        public float matchTimestamp;
        public string eventCategory; // SPAWN, MANEUVER, BURST, HIT, DOWNED, REVIVE_START, REVIVE_DONE, BLEEDOUT, RELOAD, STUCK
        public string subjectBot;
        public string targetOrAlly;
        public float range;
        public string narrative;
    }

    [System.Serializable]
    public class SpatialSample
    {
        public float timestamp;
        public List<BotCoord> bots = new List<BotCoord>();
    }

    [System.Serializable]
    public class BotCoord
    {
        public string name;
        public int team;
        public float x;
        public float y;
        public float z;
        public float hp;
        public float shield;
        public string state;
    }

    // Session Data
    public string sessionMatchId { get; private set; }
    public float sessionStartTime { get; private set; }
    public float sessionEndTime { get; private set; }
    public float matchElapsedTime => (sessionEndTime > 0f) ? (sessionEndTime - sessionStartTime) : (Time.time - sessionStartTime);
    public bool isSimulationActive = false;

    private readonly Dictionary<string, BotMetrics> _botMetricsMap = new Dictionary<string, BotMetrics>();
    private readonly List<TelemetryJournalEntry> _eventJournal = new List<TelemetryJournalEntry>();
    private readonly List<SpatialSample> _spatialHeatmap = new List<SpatialSample>();

    private float _nextHeatmapSampleTime = 0f;
    private const float HEATMAP_INTERVAL = 2.5f;

    // Live HUD GUI Settings
    public bool showLiveTelemetryHUD = true;
    private Vector2 _journalScrollPos;
    private string _exportStatusMessage = "";
    private float _exportStatusTimer = 0f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        if (string.IsNullOrEmpty(sessionMatchId))
        {
            StartNewSession();
        }
    }

    public void StartNewSession()
    {
        sessionMatchId = "SIM_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        sessionStartTime = Time.time;
        sessionEndTime = 0f;
        isSimulationActive = true;
        _botMetricsMap.Clear();
        _eventJournal.Clear();
        _spatialHeatmap.Clear();
        _nextHeatmapSampleTime = Time.time + HEATMAP_INTERVAL;

        LogEvent("SESSION_START", "SYSTEM", "ALL", 0f, $"AI Simulation Session Started (ID: {sessionMatchId})");
        Debug.Log($"<color=#26D9FF>[AI_TELEMETRY]</color> Session initialized: {sessionMatchId}");
    }

    public void EndSession(string outcome = "")
    {
        if (!isSimulationActive) return;
        sessionEndTime = Time.time;
        isSimulationActive = false;
        LogEvent("SESSION_END", "SYSTEM", "ALL", 0f, $"Match Concluded. Result: {outcome}");
        ExportReports();
    }

    private void Update()
    {
        bool f8Pressed = false;
        bool f9Pressed = false;
        bool cPressed = false;
        bool ePressed = false;
        bool rPressed = false;

#if ENABLE_INPUT_SYSTEM
        if (UnityEngine.InputSystem.Keyboard.current != null)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            f8Pressed = kb.f8Key.wasPressedThisFrame;
            f9Pressed = kb.f9Key.wasPressedThisFrame;
            cPressed = kb.cKey.wasPressedThisFrame;
            ePressed = kb.eKey.wasPressedThisFrame;
            rPressed = kb.rKey.wasPressedThisFrame;
        }
#else
        f8Pressed = Input.GetKeyDown(KeyCode.F8);
        f9Pressed = Input.GetKeyDown(KeyCode.F9);
        cPressed = Input.GetKeyDown(KeyCode.C);
        ePressed = Input.GetKeyDown(KeyCode.E);
        rPressed = Input.GetKeyDown(KeyCode.R);
#endif

        // Toggle live HUD with F8
        if (f8Pressed)
        {
            showLiveTelemetryHUD = !showLiveTelemetryHUD;
            if (!showLiveTelemetryHUD)
            {
                var pInputs = FindAnyObjectByType<StarterAssets.StarterAssetsInputs>();
                if (pInputs != null && !pInputs.ShouldKeepCursorUnlocked())
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
        }

        // Toggle / Restart Simulation with F9
        if (f9Pressed)
        {
            if (OasisShooterManager.Instance != null)
            {
                OasisShooterManager.Instance.ToggleSimulationMode();
            }
        }

        if (showLiveTelemetryHUD)
        {
            if (cPressed) CopyMarkdownToClipboard();
            if (ePressed) ExportReports();
            if (rPressed && OasisShooterManager.Instance != null) OasisShooterManager.Instance.StartSimulationMode();
        }

        if (_exportStatusTimer > 0f)
        {
            _exportStatusTimer -= Time.deltaTime;
        }

        if (!isSimulationActive) return;

        // Periodic spatial heatmap sampling
        if (Time.time >= _nextHeatmapSampleTime)
        {
            _nextHeatmapSampleTime = Time.time + HEATMAP_INTERVAL;
            RecordSpatialHeatmapSample();
        }

        // Accumulate active state durations for bots
        foreach (var bot in OasisCombatAI.ActiveBots)
        {
            if (bot == null) continue;
            var stats = GetOrCreateBotMetrics(bot.botName, bot.teamId, bot.operativeClassId);

            // Update distance moved
            if (stats.lastRecordedPosition != Vector3.zero)
            {
                stats.totalDistanceMoved += Vector3.Distance(bot.transform.position, stats.lastRecordedPosition);
            }
            stats.lastRecordedPosition = bot.transform.position;

            // Track state durations
            if (bot.isDowned)
            {
                stats.timeInDownedState += Time.deltaTime;
            }
        }

        // Accumulate active state durations for local human player
        if (OasisCombatMovement.Instance != null && (OasisShooterManager.Instance == null || !OasisShooterManager.Instance.isSimulationMode))
        {
            var pStats = GetOrCreateBotMetrics(GetLocalPlayerName(), GetLocalPlayerTeam(), GetLocalPlayerClass());
            Vector3 pos = OasisCombatMovement.Instance.transform.position;
            if (pStats.lastRecordedPosition != Vector3.zero)
            {
                pStats.totalDistanceMoved += Vector3.Distance(pos, pStats.lastRecordedPosition);
            }
            pStats.lastRecordedPosition = pos;
            if (OasisShooterManager.Instance != null && OasisShooterManager.Instance.isPlayerDowned)
            {
                pStats.timeInDownedState += Time.deltaTime;
            }
        }
    }

    // =========================================================================
    // PLAYER IDENTIFICATION HELPERS
    // =========================================================================

    public string GetLocalPlayerName()
    {
        int myTeam = PlayerPrefs.GetInt("OasisPlayerTeam", 0);
        string teamPrefix = $"[{OasisShooterManager.GetTeamShortName(myTeam)}] ";
        return teamPrefix + "YOU (OPERATIVE)";
    }

    public string GetLocalPlayerClass()
    {
        int classIdx = PlayerPrefs.GetInt("OasisSelectedOperative", 0);
        if (OasisCharacterCustomizer.Instance != null && OasisCharacterCustomizer.Instance.operatives != null && classIdx >= 0 && classIdx < OasisCharacterCustomizer.Instance.operatives.Count)
        {
            return OasisCharacterCustomizer.Instance.operatives[classIdx].id;
        }
        return "operative";
    }

    public int GetLocalPlayerTeam()
    {
        return PlayerPrefs.GetInt("OasisPlayerTeam", 0);
    }

    // =========================================================================
    // EVENT RECORDING METHODS
    // =========================================================================

    public BotMetrics GetOrCreateBotMetrics(string botName, int teamId = 0, string operativeClass = "operative")
    {
        if (!_botMetricsMap.TryGetValue(botName, out BotMetrics stats))
        {
            stats = new BotMetrics
            {
                botName = botName,
                teamId = teamId,
                operativeClass = operativeClass
            };
            _botMetricsMap[botName] = stats;
        }
        return stats;
    }

    public void LogEvent(string category, string bot, string target, float range, string narrative)
    {
        var entry = new TelemetryJournalEntry
        {
            matchTimestamp = matchElapsedTime,
            eventCategory = category,
            subjectBot = bot,
            targetOrAlly = target,
            range = range,
            narrative = narrative
        };
        _eventJournal.Add(entry);

        if (_eventJournal.Count > 2500)
        {
            _eventJournal.RemoveAt(0);
        }
    }

    public void RecordBotSpawn(OasisCombatAI bot)
    {
        if (bot == null) return;
        var stats = GetOrCreateBotMetrics(bot.botName, bot.teamId, bot.operativeClassId);
        stats.lastRecordedPosition = bot.transform.position;
        string sector = OasisMapSystem.GetSectorName(bot.transform.position);
        LogEvent("SPAWN", bot.botName, sector, 0f, $"Spawned at ({bot.transform.position.x:F1}, {bot.transform.position.y:F1}, {bot.transform.position.z:F1}) [{sector}]");
    }

    public void RecordManeuverChange(OasisCombatAI bot, int maneuverId)
    {
        if (bot == null) return;
        var stats = GetOrCreateBotMetrics(bot.botName, bot.teamId, bot.operativeClassId);
        string mName = (maneuverId == 0) ? "Aggressive Push" : (maneuverId == 1) ? "Lateral Flank" : "Tactical Fallback";
        LogEvent("MANEUVER", bot.botName, "", 0f, $"Switched behavior to {mName}");
    }

    public void RecordShotFired(OasisCombatAI bot)
    {
        if (bot == null) return;
        var stats = GetOrCreateBotMetrics(bot.botName, bot.teamId, bot.operativeClassId);
        stats.shotsFired++;
    }

    public void RecordHitLanded(OasisCombatAI attacker, OasisCombatAI victim, float damage, float distance)
    {
        if (attacker != null)
        {
            var aStats = GetOrCreateBotMetrics(attacker.botName, attacker.teamId, attacker.operativeClassId);
            aStats.shotsHit++;
            aStats.totalDamageDealt += damage;
            aStats.totalEngagementDistance += distance;
            aStats.engagementDistanceSamples++;
        }

        if (victim != null)
        {
            var vStats = GetOrCreateBotMetrics(victim.botName, victim.teamId, victim.operativeClassId);
            vStats.totalDamageTaken += damage;
        }

        string attName = attacker != null ? attacker.botName : "UNKNOWN";
        string vicName = victim != null ? victim.botName : "UNKNOWN";
        float postHp = victim != null ? Mathf.Max(0f, victim.currentHealth - Mathf.Max(0f, damage - victim.currentShield)) : 0f;
        LogEvent("HIT", attName, vicName, distance, $"Landed {damage:F0} DMG at {distance:F1}m (HP: {postHp:F0})");
    }

    public void RecordKnockdown(OasisCombatAI victim, int attackerId, float distance)
    {
        if (victim == null) return;
        var vStats = GetOrCreateBotMetrics(victim.botName, victim.teamId, victim.operativeClassId);
        vStats.timesDowned++;

        int myPlayerId = OasisNetworkManager.Instance != null ? OasisNetworkManager.Instance.myPlayerId : 0;
        bool isPlayer = (attackerId == 0 || (myPlayerId != 0 && attackerId == myPlayerId));
        if (OasisShooterManager.Instance != null && OasisShooterManager.Instance.isSimulationMode)
        {
            isPlayer = false;
        }

        string attackerName = "OPPONENT";
        if (isPlayer)
        {
            attackerName = GetLocalPlayerName();
            var aStats = GetOrCreateBotMetrics(attackerName, GetLocalPlayerTeam(), GetLocalPlayerClass());
            aStats.knockdownsInflicted++;
            if (distance <= 0.01f && OasisCombatMovement.Instance != null)
            {
                distance = Vector3.Distance(OasisCombatMovement.Instance.transform.position, victim.transform.position);
            }
        }
        else
        {
            foreach (var bot in OasisCombatAI.ActiveBots)
            {
                if (bot != null && bot.GetHashCode() == attackerId)
                {
                    attackerName = bot.botName;
                    var aStats = GetOrCreateBotMetrics(bot.botName, bot.teamId, bot.operativeClassId);
                    aStats.knockdownsInflicted++;
                    if (distance <= 0.01f)
                    {
                        distance = Vector3.Distance(bot.transform.position, victim.transform.position);
                    }
                    break;
                }
            }
        }

        LogEvent("KNOCKDOWN", attackerName, victim.botName, distance, $"Knocked down {victim.botName} at {distance:F1}m! [DOWNED 8s]");
        Debug.Log($"<color=#FF9933>[AI_TELEMETRY]</color> {attackerName} knocked down {victim.botName} ({distance:F1}m)");
    }

    public void RecordReviveStart(OasisCombatAI reviver, OasisCombatAI fallen, float distance)
    {
        if (reviver == null || fallen == null) return;
        var rStats = GetOrCreateBotMetrics(reviver.botName, reviver.teamId, reviver.operativeClassId);
        rStats.revivesInitiated++;
        LogEvent("REVIVE_START", reviver.botName, fallen.botName, distance, $"Initiated revive run to rescue {fallen.botName} from {distance:F1}m away");
    }

    public void RecordReviveStartOnPlayer(OasisCombatAI reviver, float distance)
    {
        if (reviver == null) return;
        var rStats = GetOrCreateBotMetrics(reviver.botName, reviver.teamId, reviver.operativeClassId);
        rStats.revivesInitiated++;
        string pName = GetLocalPlayerName();
        LogEvent("REVIVE_START", reviver.botName, pName, distance, $"Initiated revive run to rescue {pName} from {distance:F1}m away");
    }

    public void RecordReviveSuccess(OasisCombatAI reviver, OasisCombatAI revived)
    {
        if (reviver == null || revived == null) return;
        var rStats = GetOrCreateBotMetrics(reviver.botName, reviver.teamId, reviver.operativeClassId);
        rStats.revivesCompleted++;
        LogEvent("REVIVE_SUCCESS", reviver.botName, revived.botName, 0f, $"Successfully resurrected {revived.botName}! Both re-entering active combat.");
        Debug.Log($"<color=#26D9FF>[AI_TELEMETRY]</color> {reviver.botName} resurrected {revived.botName}!");
    }

    public void RecordReviveSuccessOnPlayer(OasisCombatAI reviver)
    {
        if (reviver == null) return;
        var rStats = GetOrCreateBotMetrics(reviver.botName, reviver.teamId, reviver.operativeClassId);
        rStats.revivesCompleted++;
        string pName = GetLocalPlayerName();
        LogEvent("REVIVE_SUCCESS", reviver.botName, pName, 0f, $"Successfully resurrected {pName}! Both re-entering active combat.");
        Debug.Log($"<color=#26D9FF>[AI_TELEMETRY]</color> {reviver.botName} resurrected {pName}!");
    }

    public void RecordBleedoutDeath(OasisCombatAI victim)
    {
        if (victim == null) return;
        var vStats = GetOrCreateBotMetrics(victim.botName, victim.teamId, victim.operativeClassId);
        vStats.fullDeaths++;
        LogEvent("BLEEDOUT", victim.botName, "TIMER", 0f, $"Bleedout timer expired without revive. Full elimination.");
    }

    public void RecordReloadStart(OasisCombatAI bot)
    {
        if (bot == null) return;
        var stats = GetOrCreateBotMetrics(bot.botName, bot.teamId, bot.operativeClassId);
        stats.reloadsPerformed++;
        LogEvent("RELOAD", bot.botName, "", 0f, "Magazine depleted (16/16). Initiated tactical reload.");
    }

    public void RecordStuckIncident(OasisCombatAI bot, Vector3 pos, string action = "Tactical Detour")
    {
        if (bot == null) return;
        var stats = GetOrCreateBotMetrics(bot.botName, bot.teamId, bot.operativeClassId);
        stats.stuckIncidents++;
        LogEvent("STUCK", bot.botName, "", 0f, $"Impeded while advancing at ({pos.x:F1}, {pos.z:F1}). Executed {action} over obstacle.");
    }

    public void RecordPlayerSpawn(Vector3 pos)
    {
        if (OasisShooterManager.Instance != null && OasisShooterManager.Instance.isSimulationMode) return;
        string pName = GetLocalPlayerName();
        var stats = GetOrCreateBotMetrics(pName, GetLocalPlayerTeam(), GetLocalPlayerClass());
        stats.lastRecordedPosition = pos;
        string sector = OasisMapSystem.GetSectorName(pos);
        LogEvent("SPAWN", pName, sector, 0f, $"Spawned at ({pos.x:F1}, {pos.y:F1}, {pos.z:F1}) [{sector}]");
    }

    public void RecordPlayerShotFired()
    {
        if (OasisShooterManager.Instance != null && OasisShooterManager.Instance.isSimulationMode) return;
        string pName = GetLocalPlayerName();
        var stats = GetOrCreateBotMetrics(pName, GetLocalPlayerTeam(), GetLocalPlayerClass());
        stats.shotsFired++;
    }

    public void RecordPlayerHitLanded(OasisCombatAI victim, float damage, float distance)
    {
        if (OasisShooterManager.Instance != null && OasisShooterManager.Instance.isSimulationMode) return;
        string pName = GetLocalPlayerName();
        var pStats = GetOrCreateBotMetrics(pName, GetLocalPlayerTeam(), GetLocalPlayerClass());
        pStats.shotsHit++;
        pStats.totalDamageDealt += damage;
        pStats.totalEngagementDistance += distance;
        pStats.engagementDistanceSamples++;

        if (victim != null)
        {
            var vStats = GetOrCreateBotMetrics(victim.botName, victim.teamId, victim.operativeClassId);
            vStats.totalDamageTaken += damage;
        }

        string vicName = victim != null ? victim.botName : "UNKNOWN";
        float postHp = victim != null ? Mathf.Max(0f, victim.currentHealth - Mathf.Max(0f, damage - victim.currentShield)) : 0f;
        LogEvent("HIT", pName, vicName, distance, $"Landed {damage:F0} DMG at {distance:F1}m (HP: {postHp:F0})");
    }

    public void RecordPlayerDamageTaken(float damage, string attackerName)
    {
        if (OasisShooterManager.Instance != null && OasisShooterManager.Instance.isSimulationMode) return;
        string pName = GetLocalPlayerName();
        var pStats = GetOrCreateBotMetrics(pName, GetLocalPlayerTeam(), GetLocalPlayerClass());
        pStats.totalDamageTaken += damage;
    }

    public void RecordPlayerKnockedDown(string attackerName)
    {
        if (OasisShooterManager.Instance != null && OasisShooterManager.Instance.isSimulationMode) return;
        string pName = GetLocalPlayerName();
        var pStats = GetOrCreateBotMetrics(pName, GetLocalPlayerTeam(), GetLocalPlayerClass());
        pStats.timesDowned++;
        LogEvent("KNOCKDOWN", attackerName, pName, 0f, $"Knocked down {pName}! [DOWNED 8s]");
    }

    public void RecordPlayerBleedout()
    {
        if (OasisShooterManager.Instance != null && OasisShooterManager.Instance.isSimulationMode) return;
        string pName = GetLocalPlayerName();
        var pStats = GetOrCreateBotMetrics(pName, GetLocalPlayerTeam(), GetLocalPlayerClass());
        pStats.fullDeaths++;
        LogEvent("BLEEDOUT", pName, "TIMER", 0f, $"Bleedout timer expired without revive. Full elimination.");
    }

    public void RecordPlayerReviveSuccess(string allyName)
    {
        if (OasisShooterManager.Instance != null && OasisShooterManager.Instance.isSimulationMode) return;
        string pName = GetLocalPlayerName();
        var pStats = GetOrCreateBotMetrics(pName, GetLocalPlayerTeam(), GetLocalPlayerClass());
        pStats.revivesInitiated++;
        pStats.revivesCompleted++;
        LogEvent("REVIVE_SUCCESS", pName, allyName, 0f, $"Successfully resurrected {allyName}! Both re-entering active combat.");
    }

    public void RecordPlayerReload()
    {
        if (OasisShooterManager.Instance != null && OasisShooterManager.Instance.isSimulationMode) return;
        string pName = GetLocalPlayerName();
        var pStats = GetOrCreateBotMetrics(pName, GetLocalPlayerTeam(), GetLocalPlayerClass());
        pStats.reloadsPerformed++;
        LogEvent("RELOAD", pName, "", 0f, "Magazine depleted (16/16). Initiated tactical reload.");
    }

    private void RecordSpatialHeatmapSample()
    {
        var sample = new SpatialSample { timestamp = matchElapsedTime };
        foreach (var bot in OasisCombatAI.ActiveBots)
        {
            if (bot == null) continue;
            sample.bots.Add(new BotCoord
            {
                name = bot.botName,
                team = bot.teamId,
                x = Mathf.Round(bot.transform.position.x * 10f) / 10f,
                y = Mathf.Round(bot.transform.position.y * 10f) / 10f,
                z = Mathf.Round(bot.transform.position.z * 10f) / 10f,
                hp = Mathf.Round(bot.currentHealth),
                shield = Mathf.Round(bot.currentShield),
                state = bot.isDowned ? "DOWNED" : (bot.isReloading ? "RELOAD" : "COMBAT")
            });
        }

        if (OasisCombatMovement.Instance != null && (OasisShooterManager.Instance == null || !OasisShooterManager.Instance.isSimulationMode))
        {
            var pTransform = OasisCombatMovement.Instance.transform;
            int pTeam = GetLocalPlayerTeam();
            float pHp = (OasisShooterManager.Instance != null) ? OasisShooterManager.Instance.currentHealth : 100f;
            float pShield = pHp;
            bool pDowned = (OasisShooterManager.Instance != null) && OasisShooterManager.Instance.isPlayerDowned;

            sample.bots.Add(new BotCoord
            {
                name = GetLocalPlayerName(),
                team = pTeam,
                x = Mathf.Round(pTransform.position.x * 10f) / 10f,
                y = Mathf.Round(pTransform.position.y * 10f) / 10f,
                z = Mathf.Round(pTransform.position.z * 10f) / 10f,
                hp = Mathf.Round(pHp),
                shield = Mathf.Round(pShield),
                state = pDowned ? "DOWNED" : "COMBAT"
            });
        }

        _spatialHeatmap.Add(sample);
    }

    // =========================================================================
    // EXPORT & REPORT GENERATION
    // =========================================================================

    public string GenerateMarkdownReport()
    {
        StringBuilder sb = new StringBuilder();
        int nTeams = (OasisShooterManager.Instance != null) ? OasisShooterManager.Instance.activeNumTeams : PlayerPrefs.GetInt("OasisNumTeams", 2);
        int pPerTeam = (OasisShooterManager.Instance != null) ? OasisShooterManager.Instance.activePlayersPerTeam : PlayerPrefs.GetInt("OasisPlayersPerTeam", 2);
        sb.AppendLine($"# Oasis FPS - AI Simulation Match Telemetry Report ({nTeams} Teams × {pPerTeam} Players)");
        sb.AppendLine($"**Session ID:** `{sessionMatchId}`  ");
        sb.AppendLine($"**Match Duration:** `{Mathf.FloorToInt(matchElapsedTime / 60):00}:{Mathf.FloorToInt(matchElapsedTime % 60):00}`  ");
        string scoreLine = (OasisShooterManager.Instance != null) 
            ? OasisShooterManager.Instance.FormatAllTeamScoresPlain() 
            : "ALPHA: 0 | BRAVO: 0";
        sb.AppendLine($"**Final Score:** **{scoreLine}**  ");
        string outcome = (OasisShooterManager.Instance != null && !string.IsNullOrEmpty(OasisShooterManager.Instance.matchEndTitle))
            ? OasisShooterManager.Instance.matchEndTitle
            : "IN PROGRESS";
        sb.AppendLine($"**Match Status:** `{outcome}`  ");
        sb.AppendLine();

        sb.AppendLine("## 1. Operative Performance Roster");
        sb.AppendLine("| Operative Name | Team | Class | KD (Knock/Die) | Revives (Done/Att) | Accuracy | Dmg Dealt | Dmg Taken | Avg Range | Stuck Hits |");
        sb.AppendLine("| :--- | :---: | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: |");

        foreach (var kvp in _botMetricsMap)
        {
            var b = kvp.Value;
            string teamStr = OasisShooterManager.GetTeamShortName(b.teamId);
            sb.AppendLine($"| **{b.botName}** | {teamStr} | {b.operativeClass} | {b.knockdownsInflicted} / {b.fullDeaths} | {b.revivesCompleted} / {b.revivesInitiated} | {b.Accuracy:F1}% ({b.shotsHit}/{b.shotsFired}) | {b.totalDamageDealt:F0} | {b.totalDamageTaken:F0} | {b.AverageEngagementRange:F1}m | {b.stuckIncidents} |");
        }
        sb.AppendLine();

        sb.AppendLine("## 2. Key Behavioral Analytics & Insights");
        float totalShots = 0;
        float totalHits = 0;
        float totalKnocks = 0;
        float totalRevives = 0;
        float totalStucks = 0;
        foreach (var b in _botMetricsMap.Values)
        {
            totalShots += b.shotsFired;
            totalHits += b.shotsHit;
            totalKnocks += b.knockdownsInflicted;
            totalRevives += b.revivesCompleted;
            totalStucks += b.stuckIncidents;
        }
        float overallAcc = totalShots > 0 ? Mathf.Min(100f, (totalHits / totalShots) * 100f) : 0f;
        sb.AppendLine($"- **Combat Accuracy:** Operatives fired `{totalShots}` total blaster bolts with `{overallAcc:F1}%` overall accuracy.");
        sb.AppendLine($"- **Revival Effectiveness:** `{totalRevives}` fallen teammates were successfully saved before bleedout timeout.");
        sb.AppendLine($"- **Mobility & Navigation Health:** Total anti-stuck corrections triggered: `{totalStucks}` events.");
        sb.AppendLine($"- **Spatial Breadth:** Heatmap logged `{_spatialHeatmap.Count}` position samples over the canyon terrain.");
        sb.AppendLine();

        sb.AppendLine($"## 3. Chronological Event Journal ({_eventJournal.Count} Total Events)");
        sb.AppendLine("| Time | Category | Subject | Target / Ally | Range | Event Narrative |");
        sb.AppendLine("| :---: | :---: | :--- | :--- | :---: | :--- |");
        for (int i = 0; i < _eventJournal.Count; i++)
        {
            var e = _eventJournal[i];
            sb.AppendLine($"| {Mathf.FloorToInt(e.matchTimestamp):00}s | `{e.eventCategory}` | {e.subjectBot} | {e.targetOrAlly} | {e.range:F1}m | {e.narrative} |");
        }
        sb.AppendLine();

        return sb.ToString();
    }

    public string GenerateJsonReport()
    {
        var data = new SimulationReportData
        {
            matchId = sessionMatchId,
            durationSeconds = matchElapsedTime,
            alphaScore = OasisShooterManager.Instance != null ? OasisShooterManager.Instance.alphaKills : 0,
            bravoScore = OasisShooterManager.Instance != null ? OasisShooterManager.Instance.bravoKills : 0,
            bots = new List<BotMetrics>(_botMetricsMap.Values),
            recentEvents = _eventJournal,
            heatmap = _spatialHeatmap
        };
        return JsonUtility.ToJson(data, true);
    }

    [System.Serializable]
    private class SimulationReportData
    {
        public string matchId;
        public float durationSeconds;
        public int alphaScore;
        public int bravoScore;
        public List<BotMetrics> bots;
        public List<TelemetryJournalEntry> recentEvents;
        public List<SpatialSample> heatmap;
    }

    public void ExportReports()
    {
        try
        {
            string dir = Path.Combine(Application.dataPath, "Scenes/Oasis/Logs");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string mdPath = Path.Combine(dir, "AI_2v2_Match_Report.md");
            string jsonPath = Path.Combine(dir, "AI_2v2_Telemetry_Report.json");

            File.WriteAllText(mdPath, GenerateMarkdownReport(), Encoding.UTF8);
            File.WriteAllText(jsonPath, GenerateJsonReport(), Encoding.UTF8);

            _exportStatusMessage = "LOGS SAVED: Scenes/Oasis/Logs/AI_2v2_Match_Report.md";
            _exportStatusTimer = 4.0f;
            Debug.Log($"<color=#26D9FF>[AI_TELEMETRY]</color> Reports successfully written to disk at {mdPath}");
        }
        catch (Exception ex)
        {
            _exportStatusMessage = "EXPORT FAILED: " + ex.Message;
            _exportStatusTimer = 4.0f;
            Debug.LogError($"[AI_TELEMETRY] Export failed: {ex}");
        }
    }

    public void CopyMarkdownToClipboard()
    {
        string md = GenerateMarkdownReport();
        GUIUtility.systemCopyBuffer = md;
        _exportStatusMessage = "COPIED SUMMARY TO CLIPBOARD! Ready to paste into chat.";
        _exportStatusTimer = 4.0f;
    }

    // =========================================================================
    // ONGUI LIVE TELEMETRY DASHBOARD HUD
    // =========================================================================

    private void OnGUI()
    {
        if (!showLiveTelemetryHUD)
        {
            return;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Draw Live Dashboard Window
        float winW = 540f;
        float winH = 460f;
        float winX = 14f;
        float winY = 14f;

        // Dark Blueprint Panel Box
        GUI.color = new Color(0.04f, 0.08f, 0.12f, 0.94f);
        GUI.DrawTexture(new Rect(winX, winY, winW, winH), Texture2D.whiteTexture);
        GUI.color = new Color(0.2f, 0.85f, 1f, 0.8f);
        GUI.Box(new Rect(winX, winY, winW, winH), "");
        GUI.color = Color.white;

        GUILayout.BeginArea(new Rect(winX + 12, winY + 8, winW - 24, winH - 16));

        // Header
        GUILayout.BeginHorizontal();
        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.25f, 0.95f, 1f) }
        };
        GUILayout.Label("AI COMBAT TELEMETRY DASHBOARD", titleStyle);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("HIDE [F8]", GUILayout.Width(75), GUILayout.Height(22)))
        {
            showLiveTelemetryHUD = false;
        }
        GUILayout.EndHorizontal();

        // Match Info
        string scoresRich = (OasisShooterManager.Instance != null) 
            ? OasisShooterManager.Instance.FormatAllTeamScoresRich(false) 
            : "<color=#26D9FF>ALPHA: 0</color>  •  <color=#FF5544>BRAVO: 0</color>";
        GUILayout.Label($"Match ID: {sessionMatchId} | Time: {Mathf.FloorToInt(matchElapsedTime / 60):00}:{Mathf.FloorToInt(matchElapsedTime % 60):00} | {scoresRich}");

        GUILayout.Space(4);

        // Operatives Table Header
        GUIStyle colHeaderStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.7f, 0.85f, 1f) }
        };
        GUILayout.Label("OPERATIVE LIVE STATUS & METRICS:", colHeaderStyle);

        // Roster Entries
        foreach (var bot in OasisCombatAI.ActiveBots)
        {
            if (bot == null) continue;
            var b = GetOrCreateBotMetrics(bot.botName, bot.teamId, bot.operativeClassId);

            Color teamCol = bot.GetTacticalColor();
            string stateStr = bot.isDowned ? "<color=#FFAA00>[DOWNED]</color>" :
                              bot.isReloading ? "<color=#FFDD44>[RELOADING]</color>" : "<color=#44FF88>[ACTIVE]</color>";

            GUIStyle botRowStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, richText = true };
            GUILayout.BeginHorizontal("box");
            GUILayout.Label($"<color=#{ColorUtility.ToHtmlStringRGB(teamCol)}><b>{bot.GetFullDisplayName()}</b></color> {stateStr}", botRowStyle, GUILayout.Width(180));
            GUILayout.Label($"HP:{bot.currentHealth:F0} SH:{bot.currentShield:F0}", botRowStyle, GUILayout.Width(90));
            GUILayout.Label($"KD:{b.knockdownsInflicted}/{b.fullDeaths}", botRowStyle, GUILayout.Width(50));
            GUILayout.Label($"REZ:{b.revivesCompleted}", botRowStyle, GUILayout.Width(45));
            GUILayout.Label($"ACC:{b.Accuracy:F0}%", botRowStyle, GUILayout.Width(65));
            GUILayout.Label($"STK:{b.stuckIncidents}", botRowStyle, GUILayout.Width(45));
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(4);

        // Recent Event Log
        GUILayout.Label("REAL-TIME COMBAT CHRONICLE:", colHeaderStyle);
        _journalScrollPos = GUILayout.BeginScrollView(_journalScrollPos, GUILayout.Height(120));
        int startIdx = Mathf.Max(0, _eventJournal.Count - 16);
        for (int i = _eventJournal.Count - 1; i >= startIdx; i--)
        {
            var e = _eventJournal[i];
            Color evtCol = (e.eventCategory == "KNOCKDOWN") ? new Color(1f, 0.4f, 0.3f) :
                           (e.eventCategory == "REVIVE_SUCCESS") ? new Color(0.2f, 1f, 0.8f) :
                           (e.eventCategory == "STUCK") ? new Color(1f, 0.9f, 0.3f) : Color.white;

            GUIStyle logStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, richText = true };
            GUILayout.Label($"[{Mathf.FloorToInt(e.matchTimestamp)}s] <color=#{ColorUtility.ToHtmlStringRGB(evtCol)}><b>{e.eventCategory}</b></color> {e.narrative}", logStyle);
        }
        GUILayout.EndScrollView();

        GUILayout.Space(6);

        // Control Actions
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("COPY SUMMARY", GUILayout.Height(28)))
        {
            CopyMarkdownToClipboard();
        }
        if (GUILayout.Button("EXPORT FILES", GUILayout.Height(28)))
        {
            ExportReports();
        }
        if (GUILayout.Button("RESTART SIM", GUILayout.Height(28)))
        {
            if (OasisShooterManager.Instance != null)
            {
                OasisShooterManager.Instance.StartSimulationMode();
            }
        }
        GUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(_exportStatusMessage) && _exportStatusTimer > 0f)
        {
            GUIStyle statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.2f, 1f, 0.5f) }
            };
            GUILayout.Label(_exportStatusMessage, statusStyle);
        }

        GUILayout.EndArea();
    }
}
