using UnityEngine;

/// <summary>
/// The match's spine: phase changes, the planning clock, the objective, and the result.
///
/// <para>Everything routes through the HUD because that is where each of these already funnels on
/// the client — the server tells the HUD what to say, and the HUD is the one place that knows what
/// the player is currently being shown. Hooking the readout rather than the server loop keeps the
/// audio strictly client-local and guarantees a cue never disagrees with the screen.</para>
/// </summary>
public static class HudAudio
{
    // From T-10 s the clock ticks once a second; from T-5 s it doubles and lifts two semitones.
    private const float TickWindowSeconds = 10f;
    private const float UrgentWindowSeconds = 5f;

    private static MatchMusicPhase lastPhase = MatchMusicPhase.None;
    private static float nextTickAt = float.MaxValue;
    private static bool timerArmed;
    private static HillControlStatus lastHillStatus = HillControlStatus.Empty;
    private static int lastHillTeam = -1;
    private static string lastFeedback = string.Empty;

    public static void PhaseChanged(string message)
    {
        MatchMusicPhase phase = MusicDirector.PhaseFromHudMessage(message);
        if (phase == MatchMusicPhase.None || phase == lastPhase)
            return;

        // The round that just finished resolves before the next one is announced.
        if (lastPhase == MatchMusicPhase.Execute && phase == MatchMusicPhase.Planning)
            BattlePlanAudio.Play(AudioCueId.PhaseRoundEnd);
        if (lastPhase == MatchMusicPhase.Dodge && phase != MatchMusicPhase.Dodge)
            BattlePlanAudio.Play(AudioCueId.PhaseDodgeResolve);

        lastPhase = phase;
        CombatAudio.RoundChanged();

        switch (phase)
        {
            case MatchMusicPhase.Planning:
                BattlePlanAudio.Play(AudioCueId.PhasePlanning);
                break;
            case MatchMusicPhase.Dodge:
                BattlePlanAudio.Play(AudioCueId.PhaseDodge);
                BattlePlanAudio.Play(AudioCueId.DodgeAlert);
                break;
            case MatchMusicPhase.Execute:
                BattlePlanAudio.Play(AudioCueId.PhaseExecute);
                break;
        }

        MusicDirector.SetMatchPhase(phase);
    }

    /// <summary>
    /// Driven from the HUD's per-frame timer update. The cadence is worked out here rather than
    /// per displayed second so the rate can double inside the final five seconds.
    /// </summary>
    public static void TimerUpdated(float secondsRemaining)
    {
        if (secondsRemaining <= 0f || secondsRemaining > TickWindowSeconds)
        {
            timerArmed = secondsRemaining > TickWindowSeconds;
            nextTickAt = float.MaxValue;
            return;
        }

        if (!timerArmed)
            return;

        bool urgent = secondsRemaining <= UrgentWindowSeconds;
        float interval = urgent ? 0.5f : 1f;

        // Align to whole seconds so the tick lands with the number changing on screen.
        float now = Time.unscaledTime;
        if (nextTickAt > now + interval * 2f)
            nextTickAt = now;
        if (now < nextTickAt)
            return;

        nextTickAt = now + interval;
        BattlePlanAudio.Play(urgent ? AudioCueId.TimerFinal : AudioCueId.TimerTick);
    }

    public static void TargetFeedback(string message, bool isError)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            lastFeedback = string.Empty;
            return;
        }
        if (message == lastFeedback)
            return;

        lastFeedback = message;
        BattlePlanAudio.Play(isError ? AudioCueId.UiError : AudioCueId.TargetConfirm);
    }

    public static void HillControlChanged(HillControlState state)
    {
        bool sameStatus = state.Status == lastHillStatus;
        bool sameTeam = state.ControllingTeamIndex == lastHillTeam;
        if (sameStatus && sameTeam)
            return;

        HillControlStatus previousStatus = lastHillStatus;
        int previousTeam = lastHillTeam;
        lastHillStatus = state.Status;
        lastHillTeam = state.ControllingTeamIndex;

        switch (state.Status)
        {
            case HillControlStatus.Contested:
                BattlePlanAudio.Play(AudioCueId.HillContested);
                break;
            case HillControlStatus.Controlled:
                BattlePlanAudio.Play(
                    GameLoop.IsTeamFriendlyToLocalPlayer(state.ControllingTeamIndex)
                        ? AudioCueId.HillCaptured
                        : AudioCueId.HillLost
                );
                break;
            default:
                // Losing an uncontested hill is only worth a cue if you were the one holding it.
                if (
                    previousStatus == HillControlStatus.Controlled
                    && GameLoop.IsTeamFriendlyToLocalPlayer(previousTeam)
                )
                {
                    BattlePlanAudio.Play(AudioCueId.HillLost);
                }
                break;
        }
    }

    public static void Deploying()
    {
        BattlePlanAudio.Play(AudioCueId.MatchDeploy);
    }

    public static void ResultsShown(string status)
    {
        AudioCueId cue = AudioCueId.MatchDraw;
        if (!string.IsNullOrEmpty(status))
        {
            if (status.StartsWith("You win", System.StringComparison.OrdinalIgnoreCase))
                cue = AudioCueId.MatchWin;
            else if (status.StartsWith("You lose", System.StringComparison.OrdinalIgnoreCase))
                cue = AudioCueId.MatchLose;
        }

        BattlePlanAudio.Play(cue);
        MusicDirector.SetMatchPhase(MatchMusicPhase.Results);
        ResetForNewMatch();
    }

    public static void ResetForNewMatch()
    {
        lastPhase = MatchMusicPhase.None;
        lastHillStatus = HillControlStatus.Empty;
        lastHillTeam = -1;
        lastFeedback = string.Empty;
        timerArmed = false;
        nextTickAt = float.MaxValue;
        PlanningAudio.RouteReset();
        CombatAudio.RoundChanged();
    }
}
