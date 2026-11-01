using System;
using System.IO;
using System.Text;
using System.Collections;
using UnityEngine;

/// <summary>
/// Real-time In-Game Recording Overlay & Outplayed Monitor.
/// Automatically detects when Overwolf Outplayed begins/ends screen capture
/// and renders a clean high-tech "● REC [MM:SS] • OUTPLAYED" status pill.
/// Also supports manual hotkey override [F9] or [Ctrl+F4].
/// </summary>
public class OasisOutplayedOverlay : MonoBehaviour
{
    public static OasisOutplayedOverlay Instance { get; private set; }

    public const string PREF_AUTO_RECORD = "OasisAutoRecordOutplayed";
    public const string PREF_DONT_ASK_RECORD = "OasisDontAskRecordPrompt";

    public static bool AutoRecordEnabled
    {
        get => PlayerPrefs.GetInt(PREF_AUTO_RECORD, 0) == 1;
        set
        {
            PlayerPrefs.SetInt(PREF_AUTO_RECORD, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    public static bool DontAskRecordPrompt
    {
        get => PlayerPrefs.GetInt(PREF_DONT_ASK_RECORD, 0) == 1;
        set
        {
            PlayerPrefs.SetInt(PREF_DONT_ASK_RECORD, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    public static void RequestStartRecording()
    {
        if (Instance != null)
        {
            Instance.StartRecordingIfInactive();
        }
        else
        {
            SimulateOutplayedGlobalHotkey();
        }
    }

    public static void RequestStopRecording()
    {
        if (Instance != null)
        {
            Instance.StopRecordingIfActive();
        }
        else
        {
            SimulateOutplayedGlobalHotkey();
        }
    }

    public bool isRecording { get; private set; } = false;
    public float recordingDuration { get; private set; } = 0f;
    public bool isManualOverride { get; private set; } = false;

    private float _recordingStartTime = 0f;
    private string _outplayedLogPath = "";
    private float _nextPollTime = 0f;
    private bool _wasRecordingLastPoll = false;
    private string _outplayedTempCaptureDir = "";
    private float _lastHotkeyDispatchTime = -10f;
    private const float HOTKEY_COOLDOWN = 1.5f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        // Determine Outplayed background log path
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _outplayedLogPath = Path.Combine(localAppData, "Overwolf", "Log", "Apps", "Outplayed", "background.html.log");

        // Determine Outplayed temp capture directory
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _outplayedTempCaptureDir = Path.Combine(userProfile, "Videos", "Overwolf", "Outplayed", "temp-capture");
    }

    private void Update()
    {
        // NOTE: Outplayed installs a system-wide global low-level Windows hook for [Ctrl+F4].
        // When the user physically presses Ctrl+F4 on their keyboard, Windows delivers it
        // directly to Outplayed. We MUST NEVER re-synthesize Ctrl+F4 in response to detecting
        // keyboard input, as that causes an infinite keypress recursion storm.

        // Poll Outplayed actual capture state from background log and active capture writes
        if (Time.time >= _nextPollTime)
        {
            _nextPollTime = Time.time + 0.5f;
            PollOutplayedState();
        }

        // Update timer while recording
        if (isRecording)
        {
            recordingDuration = Time.time - _recordingStartTime;
        }
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

    private const int KEYEVENTF_KEYUP = 0x0002;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_F4 = 0x73;
#endif

    public static void SimulateOutplayedGlobalHotkey()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        try
        {
            // Synthesize single clean Ctrl + F4 keystroke to notify Outplayed
            keybd_event(VK_CONTROL, 0, 0, 0);
            keybd_event(VK_F4, 0, 0, 0);
            keybd_event(VK_F4, 0, KEYEVENTF_KEYUP, 0);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[OutplayedOverlay] Error sending hotkey event: " + ex.Message);
        }
#endif
    }

    /// <summary>
    /// Invoked ONLY when clicking the on-screen GUI pill with the mouse.
    /// Debounced with a strict 1.5s cooldown to prevent double-firing.
    /// </summary>
    public void ToggleManualRecording()
    {
        if (Time.realtimeSinceStartup - _lastHotkeyDispatchTime < HOTKEY_COOLDOWN)
        {
            return;
        }
        _lastHotkeyDispatchTime = Time.realtimeSinceStartup;

        // Synthesize single clean Windows hotkey so Overwolf Outplayed toggles capture
        SimulateOutplayedGlobalHotkey();

        isManualOverride = true;
        SetRecordingState(!isRecording);
        CancelInvoke(nameof(ClearManualOverride));
        Invoke(nameof(ClearManualOverride), 2.5f);
    }

    public void StartRecordingIfInactive()
    {
        if (!isRecording)
        {
            _lastHotkeyDispatchTime = Time.realtimeSinceStartup;
            SimulateOutplayedGlobalHotkey();
            isManualOverride = true;
            SetRecordingState(true);
            CancelInvoke(nameof(ClearManualOverride));
            Invoke(nameof(ClearManualOverride), 2.5f);
            OasisShooterManager.Instance?.ShowNotificationBanner("<color=#FF3B30><b>● OUTPLAYED AUTO-RECORDING STARTED</b></color> (Auto-stops at match end)", 3.0f);
        }
    }

    public void StopRecordingIfActive()
    {
        if (isRecording)
        {
            _lastHotkeyDispatchTime = Time.realtimeSinceStartup;
            SimulateOutplayedGlobalHotkey();
            isManualOverride = true;
            SetRecordingState(false);
            CancelInvoke(nameof(ClearManualOverride));
            Invoke(nameof(ClearManualOverride), 2.5f);
            OasisShooterManager.Instance?.ShowNotificationBanner($"<color=#26D9FF>✔ OUTPLAYED CAPTURE AUTO-SAVED ({recordingDuration:F0}s)</color>", 3.0f);
        }
    }

    private void ClearManualOverride()
    {
        isManualOverride = false;
    }

    private void SetRecordingState(bool active)
    {
        if (isRecording == active) return;

        isRecording = active;
        if (isRecording)
        {
            _recordingStartTime = Time.time;
            recordingDuration = 0f;
            OasisShooterManager.Instance?.ShowNotificationBanner("<color=#FF3B30><b>● OUTPLAYED CAPTURE ACTIVE</b></color>", 2.0f);
            var aSrc = GetComponent<AudioSource>();
            if (aSrc != null) aSrc.PlayOneShot(ProceduralAudio.GetCountdownBeepClip(true), 0.5f);
        }
        else
        {
            OasisShooterManager.Instance?.ShowNotificationBanner($"<color=#26D9FF>✔ OUTPLAYED CAPTURE SAVED ({recordingDuration:F0}s)</color>", 2.5f);
            var aSrc = GetComponent<AudioSource>();
            if (aSrc != null) aSrc.PlayOneShot(ProceduralAudio.GetCountdownBeepClip(false), 0.4f);
        }
    }

    private void PollOutplayedState()
    {
        if (isManualOverride) return;

        bool detectedActive = false;

        // Method A: Check Outplayed background.html.log for captureState
        if (!string.IsNullOrEmpty(_outplayedLogPath) && File.Exists(_outplayedLogPath))
        {
            try
            {
                using (var fs = new FileStream(_outplayedLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long readLen = Math.Min(fs.Length, 4096);
                    fs.Seek(-readLen, SeekOrigin.End);
                    byte[] buffer = new byte[readLen];
                    int bytesRead = fs.Read(buffer, 0, (int)readLen);
                    string tail = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    int idxActive = tail.LastIndexOf("captureState changed: active", StringComparison.OrdinalIgnoreCase);
                    int idxStarting = tail.LastIndexOf("captureState changed: starting", StringComparison.OrdinalIgnoreCase);
                    int idxStopping = tail.LastIndexOf("captureState changed: stopping", StringComparison.OrdinalIgnoreCase);
                    int idxIdle = tail.LastIndexOf("captureState changed: idle", StringComparison.OrdinalIgnoreCase);
                    int idxEnded = tail.LastIndexOf("Session ended", StringComparison.OrdinalIgnoreCase);

                    int lastActiveIdx = Math.Max(idxActive, idxStarting);
                    int lastInactiveIdx = Math.Max(idxStopping, Math.Max(idxIdle, idxEnded));

                    if (lastActiveIdx > lastInactiveIdx)
                    {
                        detectedActive = true;
                    }
                    else if (lastInactiveIdx > lastActiveIdx)
                    {
                        detectedActive = false;
                    }
                }
            }
            catch {}
        }

        // Method B: Corroborate with temp-capture folder file writes
        if (!detectedActive && !string.IsNullOrEmpty(_outplayedTempCaptureDir) && Directory.Exists(_outplayedTempCaptureDir))
        {
            try
            {
                var files = Directory.GetFiles(_outplayedTempCaptureDir, "*.*", SearchOption.TopDirectoryOnly);
                foreach (var f in files)
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".mp4" || ext == ".ts" || ext == ".mkv" || ext == ".tmp")
                    {
                        var info = new FileInfo(f);
                        if ((DateTime.Now - info.LastWriteTime).TotalSeconds < 4.0)
                        {
                            detectedActive = true;
                            break;
                        }
                    }
                }
            }
            catch {}
        }

        if (detectedActive != _wasRecordingLastPoll)
        {
            _wasRecordingLastPoll = detectedActive;
            SetRecordingState(detectedActive);
        }
    }

    private void OnGUI()
    {
        // Draw the sleek Outplayed Status Pill at Top-Left
        float pillW = isRecording ? 235f : 175f;
        float pillH = 36f;
        float pillX = 20f;
        float pillY = 16f;

        // Semi-transparent dark pill background
        GUI.color = new Color(0.04f, 0.08f, 0.14f, 0.92f);
        GUI.DrawTexture(new Rect(pillX, pillY, pillW, pillH), Texture2D.whiteTexture);

        // Border: Pulsing red when recording, subtle cyan when standby
        if (isRecording)
        {
            float pulse = Mathf.PingPong(Time.time * 2.5f, 1f);
            GUI.color = Color.Lerp(new Color(0.85f, 0.15f, 0.15f, 0.9f), new Color(1f, 0.45f, 0.45f, 1f), pulse);
        }
        else
        {
            GUI.color = new Color(0.2f, 0.85f, 1f, 0.45f);
        }
        GUI.Box(new Rect(pillX, pillY, pillW, pillH), "");
        GUI.color = Color.white;

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            richText = true,
            normal = { textColor = Color.white }
        };

        if (isRecording)
        {
            int m = Mathf.FloorToInt(recordingDuration / 60f);
            int s = Mathf.FloorToInt(recordingDuration % 60f);
            float dotAlpha = Mathf.PingPong(Time.time * 2.8f, 1f);
            string dotCol = dotAlpha > 0.35f ? "#FF3B30" : "#881111";

            string text = $"<color={dotCol}><b>● REC</b></color>  <b>{m:00}:{s:00}</b>  <color=#26D9FF>OUTPLAYED</color> <size=10><color=#AAAAAA>[Ctrl+F4]</color></size>";
            if (GUI.Button(new Rect(pillX, pillY, pillW, pillH), text, style))
            {
                ToggleManualRecording();
            }
        }
        else
        {
            string text = "<color=#666666>○</color> <color=#DDDDDD>OUTPLAYED</color> <size=10><color=#26D9FF>[Ctrl+F4]</color></size>";
            if (GUI.Button(new Rect(pillX, pillY, pillW, pillH), text, style))
            {
                ToggleManualRecording();
            }
        }
    }
}
