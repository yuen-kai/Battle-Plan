using System;
using UnityEngine;

/// <summary>
/// The player's volume and display choices, stored in PlayerPrefs and applied on launch by
/// GameSettingsLifetime. Shaped like MatchOptions: one value type plus a static current copy.
/// </summary>
public struct GameSettings : IEquatable<GameSettings>
{
    public const string MasterVolumeKey = "BattlePlan.Audio.MasterVolume";
    public const string MusicVolumeKey = "BattlePlan.Audio.MusicVolume";
    public const string SfxVolumeKey = "BattlePlan.Audio.SfxVolume";
    public const string QualityLevelKey = "BattlePlan.Graphics.QualityLevel";

    /// <summary>
    /// Stored only once the player picks a level. Until then the project's own default stands,
    /// which is also what the platform picked on first launch.
    /// </summary>
    public const int UnsetQualityLevel = -1;
    public const float DefaultVolume = 1f;

    public float masterVolume;
    public float musicVolume;
    public float sfxVolume;
    public int qualityLevel;

    private static GameSettings current = Default;
    private static bool loaded;

    public static event Action<GameSettings> Changed;

    /// <summary>Every channel open: the authored mix is the mix until a player moves a slider.</summary>
    public static GameSettings Default =>
        new()
        {
            masterVolume = DefaultVolume,
            musicVolume = DefaultVolume,
            sfxVolume = DefaultVolume,
            qualityLevel = UnsetQualityLevel,
        };

    public static GameSettings Current
    {
        get
        {
            EnsureLoaded();
            return current;
        }
    }

    public int ResolvedQualityLevel =>
        qualityLevel >= 0 ? qualityLevel : QualitySettings.GetQualityLevel();

    public static int ToPercent(float volume)
    {
        return Mathf.RoundToInt(Mathf.Clamp01(volume) * 100f);
    }

    /// <summary>
    /// Drops the loaded copy and every listener. Statics survive a reload-free Play session, where
    /// last session's subscribers would otherwise still be attached to destroyed objects.
    /// </summary>
    public static void ClearRuntimeState()
    {
        Changed = null;
        loaded = false;
        current = Default;
    }

    public static void EnsureLoaded()
    {
        if (loaded)
            return;

        loaded = true;
        GameSettings stored = Default;
        stored.masterVolume = PlayerPrefs.GetFloat(MasterVolumeKey, stored.masterVolume);
        stored.musicVolume = PlayerPrefs.GetFloat(MusicVolumeKey, stored.musicVolume);
        stored.sfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, stored.sfxVolume);
        stored.qualityLevel = PlayerPrefs.GetInt(QualityLevelKey, stored.qualityLevel);
        current = stored.Sanitized();
        Apply(current.qualityLevel >= 0);
    }

    /// <summary>Re-reads PlayerPrefs the way a fresh launch would.</summary>
    public static void Reload()
    {
        loaded = false;
        EnsureLoaded();
    }

    public static void SetCurrent(GameSettings settings)
    {
        EnsureLoaded();
        GameSettings sanitized = settings.Sanitized();
        if (sanitized == current)
            return;

        bool qualityChanged = sanitized.qualityLevel != current.qualityLevel;
        current = sanitized;
        Persist();
        Apply(qualityChanged);
        Changed?.Invoke(current);
    }

    public static void SetMasterVolume(float volume)
    {
        GameSettings next = Current;
        next.masterVolume = volume;
        SetCurrent(next);
    }

    public static void SetMusicVolume(float volume)
    {
        GameSettings next = Current;
        next.musicVolume = volume;
        SetCurrent(next);
    }

    public static void SetSfxVolume(float volume)
    {
        GameSettings next = Current;
        next.sfxVolume = volume;
        SetCurrent(next);
    }

    public static void SetQualityLevel(int level)
    {
        GameSettings next = Current;
        next.qualityLevel = level;
        SetCurrent(next);
    }

    /// <summary>
    /// Pushes the in-memory copy to disk. Volume changes stream in while a slider is dragged, so
    /// the disk write waits until the player leaves the panel.
    /// </summary>
    public static void Flush()
    {
        EnsureLoaded();
        Persist();
        PlayerPrefs.Save();
    }

    public GameSettings Sanitized()
    {
        GameSettings sanitized = this;
        sanitized.masterVolume = SanitizeVolume(sanitized.masterVolume);
        sanitized.musicVolume = SanitizeVolume(sanitized.musicVolume);
        sanitized.sfxVolume = SanitizeVolume(sanitized.sfxVolume);

        if (sanitized.qualityLevel < 0)
        {
            sanitized.qualityLevel = UnsetQualityLevel;
        }
        else
        {
            int levelCount = QualitySettings.names.Length;
            sanitized.qualityLevel =
                levelCount > 0
                    ? Mathf.Min(sanitized.qualityLevel, levelCount - 1)
                    : UnsetQualityLevel;
        }

        return sanitized;
    }

    /// <summary>
    /// Whole percent steps. The readouts are percentages and a slider's arrow-key step is a
    /// hundredth of its range, so a finer stored value could only ever disagree with the panel.
    /// </summary>
    private static float SanitizeVolume(float volume)
    {
        if (float.IsNaN(volume))
            return DefaultVolume;

        return Mathf.Round(Mathf.Clamp01(volume) * 100f) / 100f;
    }

    private static void Persist()
    {
        PlayerPrefs.SetFloat(MasterVolumeKey, current.masterVolume);
        PlayerPrefs.SetFloat(MusicVolumeKey, current.musicVolume);
        PlayerPrefs.SetFloat(SfxVolumeKey, current.sfxVolume);
        PlayerPrefs.SetInt(QualityLevelKey, current.qualityLevel);
    }

    /// <summary>
    /// Master rides AudioListener so sources nobody classified still obey it; the music and effects
    /// levels are applied per source by AudioManager.
    /// </summary>
    private static void Apply(bool applyQuality)
    {
        AudioListener.volume = current.masterVolume;
        if (applyQuality)
            QualitySettings.SetQualityLevel(current.ResolvedQualityLevel, true);
    }

    public bool Equals(GameSettings other)
    {
        return masterVolume == other.masterVolume
            && musicVolume == other.musicVolume
            && sfxVolume == other.sfxVolume
            && qualityLevel == other.qualityLevel;
    }

    public override bool Equals(object obj)
    {
        return obj is GameSettings other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(masterVolume, musicVolume, sfxVolume, qualityLevel);
    }

    public static bool operator ==(GameSettings left, GameSettings right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(GameSettings left, GameSettings right)
    {
        return !left.Equals(right);
    }
}
