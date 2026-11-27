using System.Collections.Generic;

/// <summary>
/// A finished match against another person, carried onto a board of the player's own so its
/// replay can be branched. Branching runs real rounds on the server, and two people cannot test
/// different lines on one server, so whoever wants to leaves the room for a loopback bot match
/// stood up from the recorded line.
///
/// The copy always seats the player as the host team. One who sat in the second seat has the
/// recording mirrored so their crew is still theirs, and keeps their own end of the table through
/// <see cref="CameraSeat"/>.
///
/// Like <see cref="SandboxSession"/>, this only ever runs on a loopback host, so it is read
/// statically rather than replicated.
/// </summary>
public static class ReplayCopySession
{
    private static readonly List<ReplayRound> rounds = new();

    public static bool IsActive { get; private set; }

    /// <summary>Set once the join screen has started this copy's host, so a re-enable cannot start a second.</summary>
    public static bool HostStartRequested { get; set; }

    public static bool SeatMirrored { get; private set; }
    public static MatchOptions Options { get; private set; }
    public static MatchResult Result { get; private set; }
    public static HillControlState FinalHill { get; private set; }

    /// <summary>Where the replay was when the room was left: a round and a time in it, or -1 for the results.</summary>
    public static int ResumeRound { get; private set; } = -1;
    public static float ResumeTime { get; private set; }

    public static IReadOnlyList<ReplayRound> Rounds => rounds;

    public static void Begin(
        IEnumerable<ReplayRound> line,
        MatchOptions options,
        MatchResult result,
        int localTeamIndex,
        HillControlState finalHill,
        int resumeRound,
        float resumeTime
    )
    {
        SeatMirrored = localTeamIndex != GameLoop.HostTeamIndex;
        rounds.Clear();
        foreach (ReplayRound round in line)
            rounds.Add(SeatMirrored ? round.MirrorTeams() : round);

        options.opponentType = OpponentType.AI;
        options.fogOfWar = false;
        Options = options.Sanitized();
        Result = SeatMirrored ? MirrorResult(result) : result;
        FinalHill =
            SeatMirrored && finalHill.Status == HillControlStatus.Controlled
                ? new HillControlState(finalHill.Status, Mirror(finalHill.ControllingTeamIndex), finalHill.Streak)
                : finalHill;
        ResumeRound = resumeRound;
        ResumeTime = resumeTime;
        HostStartRequested = false;
        IsActive = true;
    }

    public static void End()
    {
        IsActive = false;
        HostStartRequested = false;
        SeatMirrored = false;
        ResumeRound = -1;
        rounds.Clear();
    }

    public static int CameraSeat(int teamIndex) =>
        IsActive && SeatMirrored ? Mirror(teamIndex) : teamIndex;

    /// <summary>The crew that stood in each roster slot, read off the recording.</summary>
    public static int[] BuildRoster(int teamIndex)
    {
        int[] roster = new int[RosterRules.UnitsPerPlayer];
        for (int slot = 0; slot < roster.Length; slot++)
            roster[slot] = -1;

        int fallback = 0;
        if (rounds.Count > 0)
        {
            foreach (ReplayUnit unit in rounds[0].units)
            {
                if (unit.teamIndex != teamIndex || unit.catalogIndex < 0)
                    continue;
                if (unit.rosterSlot < roster.Length)
                    roster[unit.rosterSlot] = unit.catalogIndex;
                fallback = unit.catalogIndex;
            }
        }

        for (int slot = 0; slot < roster.Length; slot++)
        {
            if (roster[slot] < 0)
                roster[slot] = fallback;
        }
        return roster;
    }

    public static MatchResult MirrorResult(MatchResult result) =>
        result.HasWinner ? MatchResult.ForWinner(Mirror(result.WinningTeamIndex), result.Reason) : result;

    private static int Mirror(int teamIndex) => GameLoop.TeamCount - 1 - teamIndex;
}
