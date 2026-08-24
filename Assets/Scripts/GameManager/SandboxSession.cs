using System.Collections.Generic;
using UnityEngine;

public static class SandboxSession
{
    public const int UnitsPerTeam = 1;

    public static int EnemyCount { get; private set; } = 1;

    public static int MaxEnemyCount => EnemySpawns.Length;

    public static readonly Vector2Int[] EnemySpawns =
    {
        new(7, 6),
        new(7, 5),
        new(6, 5),
        new(8, 5),
        new(6, 4),
    };

    public const float PlanningSeconds = 600f;

    public static readonly Vector2Int PlayerSpawn = new(7, 3);
    public static Vector2Int EnemySpawn => EnemySpawns[0];

    public static readonly Vector2Int PlayerAdjacentWall = new(7, 2);
    public static readonly Vector2Int EnemyAdjacentWall = new(7, 7);

    public static bool IsActive { get; private set; }

    public static int TestUnitIndex { get; private set; }

    public static int DummyUnitIndex { get; private set; }

    public static bool DummyFightsBack;

    public static bool HostStartRequested { get; set; }

    public static void Begin(
        int testUnitIndex,
        int dummyUnitIndex,
        int enemyCount = 1
    )
    {
        IsActive = true;
        HostStartRequested = false;
        TestUnitIndex = Mathf.Max(0, testUnitIndex);
        DummyUnitIndex = Mathf.Max(0, dummyUnitIndex);
        EnemyCount = Mathf.Clamp(enemyCount, 1, MaxEnemyCount);

        BotFrozenUnits.SetTeamFrozen(GameLoop.OpponentTeamIndex, true);
    }

    public static void End()
    {
        IsActive = false;
        HostStartRequested = false;
        EnemyCount = 1;
        BotFrozenUnits.ClearAll();
    }

    public static int UnitsForTeam(int teamIndex)
    {
        return teamIndex == GameLoop.OpponentTeamIndex ? EnemyCount : UnitsPerTeam;
    }

    public static int GetRecommendedEnemyCount(UnitData unit, int requestedCount)
    {
        bool testsMultipleTargets =
            unit != null
            && unit.unitModel != null
            && unit.unitModel.GetComponent<ArcSurge>() != null;
        return testsMultipleTargets
            ? MaxEnemyCount
            : Mathf.Clamp(requestedCount, 1, MaxEnemyCount);
    }

    public static MatchOptions BuildMatchOptions()
    {
        return new MatchOptions
        {
            gameMode = GameMode.Elimination,
            opponentType = OpponentType.AI,
            fogOfWar = false,
            mapId = MapId.Concourse,
        }.Sanitized();
    }

    public static int[] BuildHostRoster() => RosterRules.BuildPreferredRoster(TestUnitIndex);

    public static int[] BuildOpponentRoster()
    {
        int[] roster = new int[RosterRules.UnitsPerPlayer];
        System.Array.Fill(roster, DummyUnitIndex);
        return roster;
    }

    public static List<Vector2Int[]> CreateSpawnLayout()
    {
        List<Vector2Int[]> layout = new(GameLoop.TeamCount);
        for (int teamIndex = 0; teamIndex < GameLoop.TeamCount; teamIndex++)
        {
            if (teamIndex == GameLoop.HostTeamIndex)
                layout.Add(new[] { PlayerSpawn });
            else
                layout.Add(EnemySpawns[..EnemyCount]);
        }
        return layout;
    }
}
