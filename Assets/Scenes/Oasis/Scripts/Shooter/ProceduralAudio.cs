using UnityEngine;

/// <summary>
/// Generates warm, subtle, tactile low-profile audio clips procedurally in memory.
/// Designed to provide soft clicks, muffled low-end thuds, and pleasant hit markers
/// without annoying, high-pitched screeching or harsh synth frequencies.
/// </summary>
public static class ProceduralAudio
{
    private static AudioClip _blasterShotClip;
    private static AudioClip _hitTargetClip;
    private static AudioClip _hitWorldClip;
    private static AudioClip _droneExplosionClip;
    private static AudioClip _reloadClip;

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
}
