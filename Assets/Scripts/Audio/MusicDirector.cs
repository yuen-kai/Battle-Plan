using UnityEngine;

/// <summary>Where the match is, as far as the music is concerned.</summary>
public enum MatchMusicPhase
{
    None = 0,
    Deployment,
    Planning,
    Dodge,
    Execute,
    Results,
}

/// <summary>
/// Chooses the bed. Battle Plan has four screens and a match that swings between deliberation and
/// resolution every round, so the score is cut as a set of loops that share one tempo and one key
/// and are crossfaded between. Phase changes are punctuated by a stinger that ducks the bed, which
/// is also what hides the crossfade seam — the practical alternative to sample-accurate stems, and
/// it survives a round loop of unpredictable length.
///
/// <para>Client-local presentation only: nothing here is replicated or authoritative.</para>
/// </summary>
public static class MusicDirector
{
    public const string TrackTitle = "Title";
    public const string TrackSetup = "Setup";
    public const string TrackCrew = "Crew";
    public const string TrackPlanning = "Planning";
    public const string TrackExecute = "Execute";
    public const string TrackAftermath = "Aftermath";

    private const string GameSceneName = "Game";

    private static MatchMusicPhase currentPhase;
    private static AudioLoopHandle boardAmbience;
    private static bool inMatchScene;

    public static MatchMusicPhase CurrentPhase => currentPhase;

    public static void OnSceneLoaded(string sceneName)
    {
        currentPhase = MatchMusicPhase.None;
        inMatchScene = sceneName == GameSceneName;

        switch (sceneName)
        {
            case "Title Screen":
                BattlePlanAudio.PlayMusic(TrackTitle, 1.2f);
                break;
            case "JoinGame":
                BattlePlanAudio.PlayMusic(TrackSetup, 1.2f);
                break;
            case "HomeScreen":
                BattlePlanAudio.PlayMusic(TrackCrew, 1.2f);
                break;
            case GameSceneName:
                BattlePlanAudio.PlayMusic(TrackPlanning, 1.6f);
                break;
            default:
                return;
        }

        UpdateBoardAmbience();
    }

    /// <summary>
    /// Called from the HUD's phase readout, which is the one place every round transition already
    /// funnels through on the client.
    /// </summary>
    public static void SetMatchPhase(MatchMusicPhase phase)
    {
        if (phase == currentPhase)
            return;

        currentPhase = phase;
        switch (phase)
        {
            case MatchMusicPhase.Deployment:
            case MatchMusicPhase.Planning:
                BattlePlanAudio.PlayMusic(TrackPlanning, 1.4f);
                break;
            case MatchMusicPhase.Dodge:
            case MatchMusicPhase.Execute:
                BattlePlanAudio.PlayMusic(TrackExecute, 0.8f);
                break;
            case MatchMusicPhase.Results:
                BattlePlanAudio.PlayMusic(TrackAftermath, 1.8f);
                break;
        }

        UpdateBoardAmbience();
    }

    private static void UpdateBoardAmbience()
    {
        bool wanted = inMatchScene && currentPhase != MatchMusicPhase.Results;
        if (wanted == boardAmbience.IsPlaying)
            return;

        if (wanted)
            boardAmbience = BattlePlanAudio.PlayLoopFlat(AudioCueId.AmbienceBoard);
        else
            boardAmbience.Stop(0.8f);
    }

    /// <summary>Maps the HUD's phase copy onto the score. Unknown text leaves the bed alone.</summary>
    public static MatchMusicPhase PhaseFromHudMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
            return MatchMusicPhase.None;

        if (message.StartsWith("Planning", System.StringComparison.OrdinalIgnoreCase))
            return MatchMusicPhase.Planning;
        if (message.StartsWith("Dodg", System.StringComparison.OrdinalIgnoreCase))
            return MatchMusicPhase.Dodge;
        if (message.StartsWith("Executing", System.StringComparison.OrdinalIgnoreCase))
            return MatchMusicPhase.Execute;

        return MatchMusicPhase.None;
    }
}

/// <summary>
/// Silences the pre-redesign music sources that still sit on scene objects and on the Music
/// prefab, so the new bed does not play on top of the old one.
///
/// <para>This exists because attaching the new system must not require a scene or prefab edit.
/// Once the coordinator strips those AudioSources (see the audio handoff), this class and its
/// call can be deleted; until then it keeps a build from shipping two soundtracks at once. It
/// matches only the seven known legacy clips, so nothing else in a scene is ever touched.</para>
/// </summary>
public static class LegacyAudioSuppressor
{
    private static readonly string[] LegacyClipNames =
    {
        "Battle_Plan_Main_Menu trimmed",
        "Battle_Plan_Main_Menu",
        "Battle_Plan_Character_Selections",
        "Battle_plan_Settings",
        "In_game",
        "Button click",
        "Character select click",
    };

    private static bool reported;

    public static void SuppressLegacySceneMusic()
    {
        int suppressed = 0;
        foreach (
            AudioSource source in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)
        )
        {
            if (source == null || source.clip == null || !IsLegacyClip(source.clip.name))
                continue;

            source.playOnAwake = false;
            if (source.isPlaying)
                source.Stop();
            source.mute = true;
            suppressed++;
        }

        if (suppressed > 0 && !reported)
        {
            reported = true;
            Debug.Log(
                $"[Audio] Muted {suppressed} legacy scene AudioSource(s) so the new music bed is "
                    + "the only one playing. Remove them from the scenes to retire this shim."
            );
        }
    }

    private static bool IsLegacyClip(string clipName)
    {
        foreach (string legacy in LegacyClipNames)
        {
            if (string.Equals(clipName, legacy, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
