using UnityEngine;

/// <summary>
/// Generates warm, subtle, tactile low-profile audio clips procedurally in memory.
/// Designed to provide soft clicks, muffled low-end thuds, and pleasant hit markers
/// without annoying, high-pitched screeching or harsh synth frequencies.
/// </summary>
public static class ProceduralAudio
{
    private static AudioClip _blasterShotClip;
    private static AudioClip _shotgunShotClip;
    private static AudioClip _bombLaunchClip;
    private static AudioClip _bombExplosionClip;
    private static AudioClip _beamHumClip;
    private static AudioClip _hitTargetClip;
    private static AudioClip _hitWorldClip;
    private static AudioClip _droneExplosionClip;
    private static AudioClip _reloadClip;
    private static AudioClip _abilityChargeClip;
    private static AudioClip _empClip;
    private static AudioClip _healPulseClip;
    private static AudioClip _scanPingClip;

    public static AudioClip GetAbilityChargeClip()
    {
        if (_abilityChargeClip == null)
            _abilityChargeClip = CreatePneumaticLaunch("AbilityCharge", 0.28f, 180f, 620f, 0.45f);
        return _abilityChargeClip;
    }

    public static AudioClip GetEMPClip()
    {
        if (_empClip == null)
            _empClip = CreateFilteredNoise("EMPDischarge", 0.45f, 0.5f);
        return _empClip;
    }

    public static AudioClip GetHealPulseClip()
    {
        if (_healPulseClip == null)
            _healPulseClip = CreatePneumaticLaunch("HealPulse", 0.35f, 440f, 880f, 0.40f);
        return _healPulseClip;
    }

    public static AudioClip GetScanPingClip()
    {
        if (_scanPingClip == null)
            _scanPingClip = CreateSoftTick("ScanPing", 0.12f, 1100f, 0.35f);
        return _scanPingClip;
    }

    private static AudioClip _thrusterClip;
    public static AudioClip GetThrusterBoostClip()
    {
        if (_thrusterClip == null)
            _thrusterClip = CreatePneumaticLaunch("ThrusterBoost", 0.40f, 130f, 420f, 0.55f);
        return _thrusterClip;
    }

    /// <summary>
    /// Soft, low-profile suppressed laser pulse / thud. No harsh saw-wave screech.
    /// </summary>
    public static AudioClip GetBlasterShotClip()
    {
        if (_blasterShotClip == null)
        {
            _blasterShotClip = CreateSoftThud("BlasterShot", 0.08f, 210f, 75f, 0.22f);
        }
        return _blasterShotClip;
    }

    /// <summary>
    /// Heavy punchy scatter shotgun blast with deep bass and high crackle.
    /// </summary>
    public static AudioClip GetShotgunShotClip()
    {
        if (_shotgunShotClip == null)
        {
            _shotgunShotClip = CreateShotgunBlast("ShotgunShot", 0.16f, 0.42f);
        }
        return _shotgunShotClip;
    }

    /// <summary>
    /// Pneumatic mortar thump for launching plasma bombs.
    /// </summary>
    public static AudioClip GetBombLaunchClip()
    {
        if (_bombLaunchClip == null)
        {
            _bombLaunchClip = CreatePneumaticLaunch("BombLaunch", 0.14f, 160f, 45f, 0.45f);
        }
        return _bombLaunchClip;
    }

    /// <summary>
    /// Massive rolling sub-bass explosion shockwave for bomb detonation.
    /// </summary>
    public static AudioClip GetBombExplosionClip()
    {
        if (_bombExplosionClip == null)
        {
            _bombExplosionClip = CreateWarmExplosion("BombExplosion", 0.65f, 0.65f);
        }
        return _bombExplosionClip;
    }

    /// <summary>
    /// Continuous sci-fi resonant phase beam energy hum.
    /// </summary>
    public static AudioClip GetBeamHumClip()
    {
        if (_beamHumClip == null)
        {
            _beamHumClip = CreateBeamHum("PhaseBeamHum", 0.5f, 0.28f);
        }
        return _beamHumClip;
    }

    /// <summary>
    /// Crisp, understated soft hit tick / click. Replaces the annoying 1400Hz loud beep.
    /// </summary>
    public static AudioClip GetHitTargetClip()
    {
        if (_hitTargetClip == null)
        {
            _hitTargetClip = CreateSoftTick("TargetHit", 0.04f, 850f, 0.28f);
        }
        return _hitTargetClip;
    }

    /// <summary>
    /// Muffled soft dust / dirt surface puff.
    /// </summary>
    public static AudioClip GetHitWorldClip()
    {
        if (_hitWorldClip == null)
        {
            _hitWorldClip = CreateFilteredNoise("WorldImpact", 0.06f, 0.18f);
        }
        return _hitWorldClip;
    }

    /// <summary>
    /// Deep, bassy, satisfying explosion rumble with no high-frequency screech.
    /// </summary>
    public static AudioClip GetDroneExplosionClip()
    {
        if (_droneExplosionClip == null)
        {
            _droneExplosionClip = CreateWarmExplosion("DroneExplosion", 0.45f, 0.55f);
        }
        return _droneExplosionClip;
    }

    /// <summary>
    /// Smooth mechanical slide / click.
    /// </summary>
    public static AudioClip GetReloadClip()
    {
        if (_reloadClip == null)
        {
            _reloadClip = CreateSubtleClick("BlasterReload", 0.22f, 320f, 0.25f);
        }
        return _reloadClip;
    }

    private static AudioClip _warpSpawnClip;
    /// <summary>
    /// Gentle bass whoosh instead of the loud, piercing rising chirp.
    /// </summary>
    public static AudioClip GetWarpSpawnClip()
    {
        if (_warpSpawnClip == null)
        {
            _warpSpawnClip = CreateSubtleWhoosh("DroneWarpIn", 0.22f, 130f, 220f, 0.22f);
        }
        return _warpSpawnClip;
    }

    private static AudioClip _jumpClip;
    /// <summary>
    /// Soft air puff for jumping, replacing the loud laser chirp.
    /// </summary>
    public static AudioClip GetJumpClip()
    {
        if (_jumpClip == null)
        {
            _jumpClip = CreateFilteredNoise("CombatJump", 0.08f, 0.15f);
        }
        return _jumpClip;
    }

    private static AudioClip _dashClip;
    /// <summary>
    /// Low air woosh for tactical dash / slide.
    /// </summary>
    public static AudioClip GetDashClip()
    {
        if (_dashClip == null)
        {
            _dashClip = CreateFilteredNoise("CombatDash", 0.14f, 0.25f);
        }
        return _dashClip;
    }

    private static AudioClip _playerHurtClip;
    /// <summary>
    /// Muffled blunt impact thud instead of the sharp synth chirp.
    /// </summary>
    public static AudioClip GetPlayerHurtClip()
    {
        if (_playerHurtClip == null)
        {
            _playerHurtClip = CreateSoftThud("ShieldHit", 0.12f, 190f, 80f, 0.45f);
        }
        return _playerHurtClip;
    }

    private static AudioClip _shieldBreakClip;
    public static AudioClip GetShieldBreakClip()
    {
        if (_shieldBreakClip == null)
        {
            _shieldBreakClip = CreateWarmExplosion("ShieldBreak", 0.35f, 0.5f);
        }
        return _shieldBreakClip;
    }

    private static AudioClip _countdownTickClip;
    private static AudioClip _countdownFinalClip;
    /// <summary>
    /// Tactile low-frequency tick for tactical countdown, with a higher pitch tone for the final 0s launch.
    /// </summary>
    public static AudioClip GetCountdownBeepClip(bool isFinal = false)
    {
        if (isFinal)
        {
            if (_countdownFinalClip == null)
            {
                _countdownFinalClip = CreateSoftTick("CountdownFinal", 0.16f, 880f, 0.40f);
            }
            return _countdownFinalClip;
        }
        else
        {
            if (_countdownTickClip == null)
            {
                _countdownTickClip = CreateSoftTick("CountdownTick", 0.06f, 440f, 0.32f);
            }
            return _countdownTickClip;
        }
    }

    private static AudioClip _killConfirmedClip;
    /// <summary>
    /// Crisp tactical two-tone elimination chime to confirm hostile neutralisation.
    /// </summary>
    public static AudioClip GetKillConfirmedClip()
    {
        if (_killConfirmedClip == null)
        {
            int sampleRate = 44100;
            float duration = 0.22f;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleCount;
                float freq = t < 0.35f ? 1150f : 1720f;
                float envelope = t < 0.35f ? Mathf.Exp(-12f * t) : Mathf.Exp(-14f * (t - 0.35f));
                float wave = Mathf.Sin(2f * Mathf.PI * freq * (float)i / sampleRate);
                samples[i] = wave * envelope * 0.45f;
            }

            _killConfirmedClip = AudioClip.Create("KillConfirmedChime", sampleCount, 1, sampleRate, false);
            _killConfirmedClip.SetData(samples, 0);
        }
        return _killConfirmedClip;
    }

    private static AudioClip _playerDeathClip;
    /// <summary>
    /// Deep chassis shutdown thud when the player operative's armor integrity reaches zero.
    /// </summary>
    public static AudioClip GetPlayerDeathClip()
    {
        if (_playerDeathClip == null)
        {
            _playerDeathClip = CreateSoftThud("PlayerChassisDestroyed", 0.65f, 160f, 32f, 0.65f);
        }
        return _playerDeathClip;
    }

    // ==========================================
    // WARM & TACTILE LOW-PROFILE SOUND GENERATION
    // ==========================================

    /// <summary>
    /// Pure sine wave sweep with exponential decay. Warm, bassy, and completely free of harsh harmonics.
    /// </summary>
    private static AudioClip CreateSoftThud(string name, float duration, float startFreq, float endFreq, float volume)
    {
        int sampleRate = 44100;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        float phase = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleCount;
            float currentFreq = Mathf.Lerp(startFreq, endFreq, Mathf.Sqrt(t));
            phase += 2f * Mathf.PI * currentFreq / sampleRate;

            // Pure smooth sine wave with subtle sub-bass undertone
            float sine = Mathf.Sin(phase);
            float sub = Mathf.Sin(phase * 0.5f) * 0.35f;

            // Smooth steep exponential envelope
            float envelope = Mathf.Exp(-8f * t);
            samples[i] = (sine + sub) * envelope * volume;
        }

        AudioClip clip = AudioClip.Create(name, sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>
    /// Very short, soft tactile click/tick (like a satisfying mechanical keyboard or subtle UI tick).
    /// </summary>
    private static AudioClip CreateSoftTick(string name, float duration, float freq, float volume)
    {
        int sampleRate = 44100;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleCount;
            // Short sine pulse with fast envelope decay (no lingering tone)
            float envelope = Mathf.Exp(-35f * t);
            samples[i] = Mathf.Sin(2f * Mathf.PI * freq * (float)i / sampleRate) * envelope * volume;
        }

        AudioClip clip = AudioClip.Create(name, sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>
    /// Heavily filtered low-pass noise that sounds like a soft puff of air or muffled thud.
    /// </summary>
    private static AudioClip CreateFilteredNoise(string name, float duration, float volume)
    {
        int sampleRate = 44100;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        float filterState = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleCount;
            float rawNoise = Random.value * 2f - 1f;

            // Strong 1-pole low-pass filter to remove any harsh hiss or sizzle
            filterState += (rawNoise - filterState) * 0.12f;

            float envelope = Mathf.Exp(-14f * t);
            samples[i] = filterState * envelope * volume;
        }

        AudioClip clip = AudioClip.Create(name, sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>
    /// Deep, warm, low-frequency explosion with low-pass filtering and deep sub rumble.
    /// </summary>
    private static AudioClip CreateWarmExplosion(string name, float duration, float volume)
    {
        int sampleRate = 44100;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        float lowPass = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleCount;
            float white = Random.value * 2f - 1f;
            lowPass += (white - lowPass) * 0.08f; // Soft, dark low-pass rumble

            float sub = Mathf.Sin(2f * Mathf.PI * 55f * (float)i / sampleRate);
            float envelope = Mathf.Exp(-5.5f * t);

            samples[i] = (lowPass * 0.7f + sub * 0.4f) * envelope * volume;
        }

        AudioClip clip = AudioClip.Create(name, sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>
    /// Gentle dual-pulse click for reloading.
    /// </summary>
    private static AudioClip CreateSubtleClick(string name, float duration, float freq, float volume)
    {
        int sampleRate = 44100;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleCount;
            float env1 = Mathf.Exp(-30f * Mathf.Abs(t - 0.25f));
            float env2 = Mathf.Exp(-30f * Mathf.Abs(t - 0.75f));
            float tone = Mathf.Sin(2f * Mathf.PI * freq * (float)i / sampleRate);

            samples[i] = tone * (env1 + env2 * 0.8f) * volume;
        }

        AudioClip clip = AudioClip.Create(name, sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>
    /// Gentle, low sine whoosh.
    /// </summary>
    private static AudioClip CreateSubtleWhoosh(string name, float duration, float startFreq, float endFreq, float volume)
    {
        int sampleRate = 44100;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        float phase = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleCount;
            float currentFreq = Mathf.Lerp(startFreq, endFreq, t);
            phase += 2f * Mathf.PI * currentFreq / sampleRate;

            float envelope = Mathf.Sin(t * Mathf.PI);
            samples[i] = Mathf.Sin(phase) * envelope * volume;
        }

        AudioClip clip = AudioClip.Create(name, sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>
    /// Heavy punchy shotgun blast with explosive noise + deep bass pitch drop.
    /// </summary>
    private static AudioClip CreateShotgunBlast(string name, float duration, float volume)
    {
        int sampleRate = 44100;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        float phase = 0f;
        float lowPass = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleCount;
            float freq = Mathf.Lerp(140f, 38f, t);
            phase += 2f * Mathf.PI * freq / sampleRate;

            float sub = Mathf.Sin(phase);
            float noise = Random.value * 2f - 1f;
            lowPass += (noise - lowPass) * 0.25f;

            float env = Mathf.Exp(-14f * t);
            samples[i] = (sub * 0.55f + lowPass * 0.45f) * env * volume;
        }

        AudioClip clip = AudioClip.Create(name, sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>
    /// Heavy pneumatic mortar thump for launching plasma bombs.
    /// </summary>
    private static AudioClip CreatePneumaticLaunch(string name, float duration, float startFreq, float endFreq, float volume)
    {
        int sampleRate = 44100;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        float phase = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleCount;
            float freq = Mathf.Lerp(startFreq, endFreq, t);
            phase += 2f * Mathf.PI * freq / sampleRate;

            float tone = Mathf.Sin(phase);
            float env = Mathf.Sin(t * Mathf.PI * 0.5f) * Mathf.Exp(-9f * t);
            samples[i] = tone * env * volume;
        }

        AudioClip clip = AudioClip.Create(name, sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>
    /// Resonant buzzing phase laser beam loop.
    /// </summary>
    private static AudioClip CreateBeamHum(string name, float duration, float volume)
    {
        int sampleRate = 44100;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        float phase1 = 0f;
        float phase2 = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            phase1 += 2f * Mathf.PI * 180f / sampleRate;
            phase2 += 2f * Mathf.PI * 360f / sampleRate;

            float tone = Mathf.Sin(phase1) * 0.6f + Mathf.Sin(phase2) * 0.4f;
            samples[i] = tone * volume;
        }

        AudioClip clip = AudioClip.Create(name, sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static AudioClip _waterSplashClip;
    private static AudioClip _waterWadeClip;

    public static AudioClip GetWaterSplashClip()
    {
        if (_waterSplashClip == null)
            _waterSplashClip = CreateWaterSplash("WaterSplash", 0.42f, 0.48f);
        return _waterSplashClip;
    }

    public static AudioClip GetWaterWadeClip()
    {
        if (_waterWadeClip == null)
            _waterWadeClip = CreateWaterSplash("WaterWade", 0.26f, 0.32f);
        return _waterWadeClip;
    }

    private static AudioClip CreateWaterSplash(string name, float duration, float volume)
    {
        int sampleRate = 44100;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        float lastVal = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleCount;
            float rawNoise = UnityEngine.Random.Range(-1f, 1f);
            lastVal = Mathf.Lerp(lastVal, rawNoise, 0.16f);
            float env = Mathf.Sin(t * Mathf.PI) * Mathf.Exp(-3.8f * t);
            float thump = Mathf.Sin(2f * Mathf.PI * 65f * (float)i / sampleRate) * Mathf.Exp(-11f * t);
            samples[i] = (lastVal * 0.72f + thump * 0.28f) * env * volume;
        }

        AudioClip clip = AudioClip.Create(name, sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    // =========================================================================
    // DISTINCT CLASS WEAPON & ENHANCED ABILITY SOUND EFFECTS
    // =========================================================================

    private static AudioClip _autocannonShotClip;
    private static AudioClip _burstRifleShotClip;
    private static AudioClip _sniperShotClip;
    private static AudioClip _handCannonShotClip;
    private static AudioClip _carbineShotClip;
    private static AudioClip _orbitalStrikeClip;
    private static AudioClip _aegisOverdriveClip;
    private static AudioClip _phantomBlinkClip;
    private static AudioClip _deadshotOverclockClip;
    private static AudioClip _oasisThemeTrackClip;

    /// <summary>
    /// Heavy Pulse Autocannon: Deep mechanical bolt punch + low-end plasma thump.
    /// </summary>
    public static AudioClip GetAutocannonShotClip()
    {
        if (_autocannonShotClip == null)
        {
            int sampleRate = 44100;
            float duration = 0.13f;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];
            float phase = 0f;
            float lowPass = 0f;

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleCount;
                float freq = Mathf.Lerp(175f, 44f, Mathf.Sqrt(t));
                phase += 2f * Mathf.PI * freq / sampleRate;

                float body = Mathf.Sin(phase) + Mathf.Sin(phase * 0.5f) * 0.45f;
                float mechClick = Mathf.Sin(2f * Mathf.PI * 680f * (float)i / sampleRate) * Mathf.Exp(-42f * t);
                float rawNoise = Random.value * 2f - 1f;
                lowPass += (rawNoise - lowPass) * 0.22f;

                float env = Mathf.Exp(-13f * t);
                samples[i] = Mathf.Clamp((body * 0.55f + lowPass * 0.30f + mechClick * 0.25f) * env * 0.42f, -0.95f, 0.95f);
            }
            _autocannonShotClip = AudioClip.Create("AutocannonShot", sampleCount, 1, sampleRate, false);
            _autocannonShotClip.SetData(samples, 0);
        }
        return _autocannonShotClip;
    }

    /// <summary>
    /// 3-Round Burst Battle Rifle: Crisp, tight high-velocity tactical plasma crack.
    /// </summary>
    public static AudioClip GetBurstRifleShotClip()
    {
        if (_burstRifleShotClip == null)
        {
            int sampleRate = 44100;
            float duration = 0.085f;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];
            float phase = 0f;
            float lp = 0f;

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleCount;
                float freq = Mathf.Lerp(460f, 115f, t);
                phase += 2f * Mathf.PI * freq / sampleRate;

                float snap = Mathf.Sin(phase) * 0.65f + Mathf.Sin(phase * 1.5f) * 0.25f;
                float noise = Random.value * 2f - 1f;
                lp += (noise - lp) * 0.28f;

                float env = Mathf.Exp(-18f * t);
                samples[i] = (snap + lp * 0.25f) * env * 0.34f;
            }
            _burstRifleShotClip = AudioClip.Create("BurstRifleShot", sampleCount, 1, sampleRate, false);
            _burstRifleShotClip.SetData(samples, 0);
        }
        return _burstRifleShotClip;
    }

    /// <summary>
    /// Long-Range Scout Marksman Rifle: High-velocity rail snap + resonant canyon echo tail.
    /// </summary>
    public static AudioClip GetSniperShotClip()
    {
        if (_sniperShotClip == null)
        {
            int sampleRate = 44100;
            float duration = 0.24f;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];
            float phase = 0f;
            float lp = 0f;

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleCount;
                float freq = Mathf.Lerp(620f, 58f, Mathf.Pow(t, 0.45f));
                phase += 2f * Mathf.PI * freq / sampleRate;

                float railCrack = Mathf.Sin(phase) + Mathf.Sin(phase * 2f) * Mathf.Exp(-28f * t) * 0.35f;
                float noise = Random.value * 2f - 1f;
                lp += (noise - lp) * 0.18f;

                float env = Mathf.Exp(-9.5f * t);
                samples[i] = Mathf.Clamp((railCrack * 0.6f + lp * 0.4f) * env * 0.44f, -0.95f, 0.95f);
            }
            _sniperShotClip = AudioClip.Create("MarksmanSniperShot", sampleCount, 1, sampleRate, false);
            _sniperShotClip.SetData(samples, 0);
        }
        return _sniperShotClip;
    }

    /// <summary>
    /// Heavy Plasma Hand Cannon: Deep revolver-style plasma discharge with heavy sub punch.
    /// </summary>
    public static AudioClip GetHandCannonShotClip()
    {
        if (_handCannonShotClip == null)
        {
            int sampleRate = 44100;
            float duration = 0.17f;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];
            float phase = 0f;
            float lp = 0f;

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleCount;
                float freq = Mathf.Lerp(320f, 52f, Mathf.Sqrt(t));
                phase += 2f * Mathf.PI * freq / sampleRate;

                float slug = Mathf.Sin(phase) * 0.7f + Mathf.Sin(phase * 0.5f) * 0.45f;
                float noise = Random.value * 2f - 1f;
                lp += (noise - lp) * 0.20f;

                float env = Mathf.Exp(-11f * t);
                samples[i] = Mathf.Clamp((slug + lp * 0.3f) * env * 0.44f, -0.95f, 0.95f);
            }
            _handCannonShotClip = AudioClip.Create("HandCannonShot", sampleCount, 1, sampleRate, false);
            _handCannonShotClip.SetData(samples, 0);
        }
        return _handCannonShotClip;
    }

    /// <summary>
    /// Tactical Defense Carbine: Smooth, rapid sci-fi pulse carbine zip.
    /// </summary>
    public static AudioClip GetCarbineShotClip()
    {
        if (_carbineShotClip == null)
        {
            _carbineShotClip = CreateSoftThud("CarbinePulseShot", 0.075f, 340f, 95f, 0.28f);
        }
        return _carbineShotClip;
    }

    public static AudioClip GetWeaponShotClipForClass(string classId)
    {
        switch (classId)
        {
            case "heavy_warrior": return GetAutocannonShotClip();
            case "desert_commando": return GetBurstRifleShotClip();
            case "wasteland_nomad": return GetSniperShotClip();
            case "cyber_outlaw": return GetHandCannonShotClip();
            case "combat_medic": return GetCarbineShotClip();
            case "cyborg_specops":
            case "unit07_android": return GetBeamHumClip();
            default: return GetBlasterShotClip();
        }
    }

    public static AudioClip GetOrbitalStrikeClip()
    {
        if (_orbitalStrikeClip == null)
        {
            int sampleRate = 44100;
            float duration = 0.55f;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];
            float phase = 0f;
            float lp = 0f;

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleCount;
                float freq = t < 0.35f ? Mathf.Lerp(920f, 180f, t / 0.35f) : Mathf.Lerp(140f, 38f, (t - 0.35f) / 0.65f);
                phase += 2f * Mathf.PI * freq / sampleRate;
                float noise = Random.value * 2f - 1f;
                lp += (noise - lp) * (t < 0.35f ? 0.12f : 0.18f);

                float env = t < 0.35f ? Mathf.Sin((t / 0.35f) * Mathf.PI * 0.5f) * 0.6f : Mathf.Exp(-5.5f * (t - 0.35f));
                samples[i] = Mathf.Clamp((Mathf.Sin(phase) * 0.6f + lp * 0.5f) * env * 0.52f, -0.95f, 0.95f);
            }
            _orbitalStrikeClip = AudioClip.Create("OrbitalPrecisionStrike", sampleCount, 1, sampleRate, false);
            _orbitalStrikeClip.SetData(samples, 0);
        }
        return _orbitalStrikeClip;
    }

    public static AudioClip GetAegisOverdriveClip()
    {
        if (_aegisOverdriveClip == null)
        {
            _aegisOverdriveClip = CreatePneumaticLaunch("AegisOverdrive", 0.42f, 140f, 520f, 0.48f);
        }
        return _aegisOverdriveClip;
    }

    public static AudioClip GetPhantomBlinkClip()
    {
        if (_phantomBlinkClip == null)
        {
            _phantomBlinkClip = CreateSubtleWhoosh("PhantomBlink", 0.30f, 540f, 150f, 0.42f);
        }
        return _phantomBlinkClip;
    }

    public static AudioClip GetDeadshotOverclockClip()
    {
        if (_deadshotOverclockClip == null)
        {
            _deadshotOverclockClip = CreatePneumaticLaunch("DeadshotOverclock", 0.36f, 260f, 780f, 0.44f);
        }
        return _deadshotOverclockClip;
    }

    /// <summary>
    /// Synthesizes a 16-second seamless stereo Sci-Fi Canyon Synthwave / Tactical Ambient Backing Track
    /// (D minor progression: Dm -> Bb -> F -> C) that plays continuously from the Home Page into the Squad Lobby.
    /// </summary>
    public static AudioClip GetOasisThemeTrackClip()
    {
        if (_oasisThemeTrackClip != null) return _oasisThemeTrackClip;

        int sampleRate = 44100;
        float duration = 16.0f; // 4 bars of 4.0s each (60 BPM / 120 BPM eighth-note pulse)
        int totalFrames = Mathf.CeilToInt(sampleRate * duration);
        float[] stereoSamples = new float[totalFrames * 2];

        // Root bass frequencies for 4 chords (Dm -> Bb -> F -> C)
        float[] bassRoots = new float[] { 73.42f, 58.27f, 87.31f, 65.41f };
        // Chord triad frequencies (3 notes per bar)
        float[][] chordNotes = new float[][]
        {
            new float[] { 146.83f, 174.61f, 220.00f }, // D3, F3, A3 (Dm)
            new float[] { 116.54f, 146.83f, 174.61f }, // Bb2, D3, F3 (Bb)
            new float[] { 174.61f, 220.00f, 261.63f }, // F3, A3, C4 (F)
            new float[] { 130.81f, 164.81f, 196.00f }  // C3, E3, G3 (C)
        };
        // Atmospheric cyan arpeggio notes (8 steps per bar = 0.5s per step)
        float[][] arpNotes = new float[][]
        {
            new float[] { 293.66f, 349.23f, 440.00f, 587.33f, 440.00f, 349.23f, 523.25f, 440.00f },
            new float[] { 233.08f, 293.66f, 349.23f, 466.16f, 349.23f, 293.66f, 440.00f, 349.23f },
            new float[] { 349.23f, 440.00f, 523.25f, 698.46f, 523.25f, 440.00f, 587.33f, 523.25f },
            new float[] { 261.63f, 329.63f, 392.00f, 523.25f, 392.00f, 329.63f, 493.88f, 392.00f }
        };

        for (int i = 0; i < totalFrames; i++)
        {
            float time = (float)i / sampleRate;
            int barIndex = Mathf.Clamp((int)(time / 4.0f), 0, 3);
            float barTime = time - (barIndex * 4.0f); // 0..4s

            // Smooth chord crossfade envelope at bar boundaries so transitions are velvety
            float barEnv = 1.0f;
            if (barTime < 0.18f) barEnv = barTime / 0.18f;
            else if (barTime > 3.82f) barEnv = (4.0f - barTime) / 0.18f;

            // 1. Deep Warm Sub-Bass + Subtle 8th-Note Synthwave Pulse (2Hz)
            float bassFreq = bassRoots[barIndex];
            float pulseGate = 0.72f + 0.28f * Mathf.Sin(2f * Mathf.PI * 2.0f * time);
            float subBass = (Mathf.Sin(2f * Mathf.PI * bassFreq * time) * 0.75f +
                             Mathf.Sin(2f * Mathf.PI * (bassFreq * 0.5f) * time) * 0.35f) * pulseGate * barEnv * 0.32f;

            // 2. Lush Stereo Detuned Canyon Synth Pad
            float[] triad = chordNotes[barIndex];
            float padL = 0f;
            float padR = 0f;
            for (int n = 0; n < 3; n++)
            {
                float f = triad[n];
                float slowMod = Mathf.Sin(2f * Mathf.PI * (0.25f + n * 0.08f) * time) * 0.45f;
                padL += Mathf.Sin(2f * Mathf.PI * (f * 0.998f) * time + slowMod);
                padR += Mathf.Sin(2f * Mathf.PI * (f * 1.002f) * time - slowMod);
            }
            padL = (padL / 3f) * barEnv * 0.22f;
            padR = (padR / 3f) * barEnv * 0.22f;

            // 3. Crystalline Desert-Night Arpeggio Pluck (0.5s step)
            int arpStep = Mathf.Clamp((int)(barTime / 0.5f), 0, 7);
            float stepTime = barTime - (arpStep * 0.5f);
            float arpFreq = arpNotes[barIndex][arpStep];
            float arpEnv = Mathf.Exp(-7.5f * stepTime) * Mathf.Min(1f, stepTime / 0.012f);
            float arpWave = (Mathf.Sin(2f * Mathf.PI * arpFreq * time) * 0.75f +
                             Mathf.Sin(2f * Mathf.PI * (arpFreq * 2f) * time) * 0.25f) * arpEnv * 0.14f;

            // Stereo pan the arpeggio notes gently between left and right
            float pan = (arpStep % 2 == 0) ? 0.68f : 0.32f;
            float arpL = arpWave * pan;
            float arpR = arpWave * (1f - pan);

            // 4. Soft Tactical Heartbeat Kick on every 1.0s beat
            float beatTime = barTime % 1.0f;
            float kickFreq = Mathf.Lerp(115f, 38f, Mathf.Sqrt(Mathf.Clamp01(beatTime / 0.18f)));
            float kickEnv = Mathf.Exp(-14f * beatTime);
            float kick = Mathf.Sin(2f * Mathf.PI * kickFreq * beatTime) * kickEnv * 0.22f;

            float left = Mathf.Clamp(subBass + kick + padL + arpL, -0.95f, 0.95f);
            float right = Mathf.Clamp(subBass + kick + padR + arpR, -0.95f, 0.95f);

            stereoSamples[i * 2] = left;
            stereoSamples[i * 2 + 1] = right;
        }

        _oasisThemeTrackClip = AudioClip.Create("OasisProtocol_ThemeBackingTrack", totalFrames, 2, sampleRate, false);
        _oasisThemeTrackClip.SetData(stereoSamples, 0);
        return _oasisThemeTrackClip;
    }
}

/// <summary>
/// Persistent cross-scene background music manager that plays the Oasis Protocol backing track
/// seamlessly from the Home Page (MainMenu) into the Operative Selection / Squad Lobby (OasisScene),
/// smoothly fading out when deploying into active combat and fading back in when returning to menu/lobby.
/// </summary>
public class OasisMusicManager : MonoBehaviour
{
    public static OasisMusicManager Instance { get; private set; }

    private AudioSource _musicSource;
    private bool _shouldPlayMenuMusic = true;
    private float _targetVolume = 0.34f;

    public static void SetMenuMusicActive(bool active)
    {
        if (Instance == null)
        {
            GameObject go = new GameObject("OasisMusicManager_Persistent");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<OasisMusicManager>();
        }
        Instance._shouldPlayMenuMusic = active;
        if (active && Instance._musicSource != null && !Instance._musicSource.isPlaying)
        {
            Instance._musicSource.Play();
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
        DontDestroyOnLoad(gameObject);

        _musicSource = gameObject.AddComponent<AudioSource>();
        _musicSource.clip = ProceduralAudio.GetOasisThemeTrackClip();
        _musicSource.loop = true;
        _musicSource.spatialBlend = 0f;
        _musicSource.priority = 0;
        _musicSource.volume = _targetVolume;
        _musicSource.playOnAwake = false;
        _musicSource.Play();
    }

    private void Update()
    {
        if (_musicSource == null) return;

        float desired = _shouldPlayMenuMusic ? _targetVolume : 0f;
        _musicSource.volume = Mathf.MoveTowards(_musicSource.volume, desired, Time.unscaledDeltaTime * 0.45f);

        if (_shouldPlayMenuMusic && !_musicSource.isPlaying)
        {
            _musicSource.Play();
        }
        else if (!_shouldPlayMenuMusic && _musicSource.volume <= 0.001f && _musicSource.isPlaying)
        {
            _musicSource.Pause(); // Pause rather than Stop so resuming preserves exact phrase continuity
        }
    }
}
