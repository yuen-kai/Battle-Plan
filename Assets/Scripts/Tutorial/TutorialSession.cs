using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lifetime and board setup for the sandboxed tutorial: a one-unit-per-side Elimination match
/// against a scripted opponent, launched straight from the title screen. The tutorial player is
/// always the loopback host, so the match reads this state statically rather than replicating it.
/// </summary>
public static class TutorialSession
{
    public const string CompletedPreferenceKey = "BattlePlan.Tutorial.Completed";

    /// <summary>
    /// One unit a side. Rosters stay the configured crew length so roster validation and
    /// <c>GameLoop.ConfigureTeam</c> are untouched; only the number of units spawned changes.
    /// </summary>
    public const int UnitsPerTeam = 1;

    /// <summary>
    /// Both crews field a Soldier (catalog index 4). Its Grenade is the only ability that both
    /// reaches across the teaching distance and opens a dodge window on whoever it lands near, so
    /// a single unit type covers the ability lesson and the dodge lesson from both sides.
    /// </summary>
    public const int TutorialUnitIndex = 4;

    /// <summary>
    /// Long enough that planning never expires under someone reading a prompt. The tutorial hides
    /// the timer and ends each round on the student's lock-in instead.
    /// </summary>
    public const float PlanningSeconds = 600f;

    /// <summary>
    /// Column 6 carries no wall at any row, so neither crew can break the script by pathing around
    /// cover. Five cells apart starts them outside rifle reach (Soldier <c>targetRange</c> 4) and
    /// inside Grenade reach (<c>abilitySquareRange</c> 5).
    /// </summary>
    public static readonly Vector2Int PlayerSpawn = new(6, 2);
    public static readonly Vector2Int EnemySpawn = new(6, 7);

    public static bool IsActive { get; private set; }

    /// <summary>
    /// Set once the join screen has kicked off this session's loopback host, so a scene re-enable
    /// cannot start a second one.
    /// </summary>
    public static bool HostStartRequested { get; set; }

    public static bool HasCompleted => PlayerPrefs.GetInt(CompletedPreferenceKey, 0) != 0;

    public static void Begin()
    {
        IsActive = true;
        HostStartRequested = false;
    }

    public static void End()
    {
        IsActive = false;
        HostStartRequested = false;
    }

    public static void MarkCompleted()
    {
        PlayerPrefs.SetInt(CompletedPreferenceKey, 1);
        PlayerPrefs.Save();
    }

    public static MatchOptions BuildMatchOptions()
    {
        return new MatchOptions
        {
            gameMode = GameMode.Elimination,
            opponentType = OpponentType.AI,
            fogOfWar = false,
        }.Sanitized();
    }

    public static int[] BuildRoster()
    {
        return RosterRules.BuildPreferredRoster(TutorialUnitIndex);
    }

    public static List<Vector2Int[]> CreateSpawnLayout()
    {
        List<Vector2Int[]> layout = new(GameLoop.TeamCount);
        for (int teamIndex = 0; teamIndex < GameLoop.TeamCount; teamIndex++)
        {
            layout.Add(
                new[] { teamIndex == GameLoop.HostTeamIndex ? PlayerSpawn : EnemySpawn }
            );
        }
        return layout;
    }
}
