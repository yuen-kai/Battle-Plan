using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum EscortLegReason : byte
{
    None = 0,
    Extracted = 1,
    PresidentDown = 2,
    DefendersHeld = 3,
    GroundCovered = 4,
    DefenceEliminated = 5,
}

public readonly struct EscortLegResult
{
    public readonly bool Decided;
    public readonly int WinningTeamIndex;
    public readonly EscortLegReason Reason;

    private EscortLegResult(bool decided, int winningTeamIndex, EscortLegReason reason)
    {
        Decided = decided;
        WinningTeamIndex = winningTeamIndex;
        Reason = reason;
    }

    public static EscortLegResult Undecided =>
        new(false, GameLoop.NoHillController, EscortLegReason.None);

    public static EscortLegResult Won(int winningTeamIndex, EscortLegReason reason) =>
        new(true, winningTeamIndex, reason);

    public static EscortLegResult Level(EscortLegReason reason) =>
        new(true, GameLoop.NoHillController, reason);

    public bool HasWinner => Decided && WinningTeamIndex != GameLoop.NoHillController;
}

public readonly struct EscortStanding
{
    public readonly int TeamIndex;
    public readonly bool Extracted;
    public readonly bool PresidentDown;
    public readonly bool OpposingCrewWiped;
    public readonly int StepsRemaining;

    public EscortStanding(
        int teamIndex,
        bool extracted,
        bool presidentDown,
        bool opposingCrewWiped,
        int stepsRemaining
    )
    {
        TeamIndex = teamIndex;
        Extracted = extracted;
        PresidentDown = presidentDown;
        OpposingCrewWiped = opposingCrewWiped;
        StepsRemaining = presidentDown ? int.MaxValue : Mathf.Max(0, stepsRemaining);
    }

    public bool Resolved => Extracted || PresidentDown;
}

public static class EscortSeries
{
    public const int PresidentRosterSlot = RosterRules.UnitsPerPlayer / 2;

    /// <summary>Upper bound on a leg so it cannot run forever.</summary>
    public const int RoundsPerLeg = 12;

    /// <summary>Rounds left when the leg clock announces itself across the screen.</summary>
    public const int FinalWarningRounds = 3;

    public const int LegsToWin = 2;
    public const int DeciderLeg = 3;
    public const int ExtractionZoneWidthCells = 3;
    public const int DefenderRowsFromExtraction = 3;

    private static readonly int[] legWins = new int[GameLoop.TeamCount];

    public static bool IsActive { get; private set; }
    public static int LegNumber { get; private set; } = 1;
    public static int CompletedLegs => Mathf.Max(0, LegNumber - 1);

    public static int GetLegWins(int teamIndex) =>
        teamIndex >= 0 && teamIndex < legWins.Length ? legWins[teamIndex] : 0;

    public static void Begin()
    {
        IsActive = true;
        LegNumber = 1;
        System.Array.Clear(legWins, 0, legWins.Length);
    }

    public static void End()
    {
        IsActive = false;
        LegNumber = 1;
        System.Array.Clear(legWins, 0, legWins.Length);
    }

    public static void RecordLeg(int winningTeamIndex)
    {
        if (winningTeamIndex >= 0 && winningTeamIndex < legWins.Length)
            legWins[winningTeamIndex]++;
        LegNumber++;
    }

    public static void SyncFromServer(int legNumber, int hostLegWins, int opponentLegWins)
    {
        IsActive = true;
        LegNumber = Mathf.Max(1, legNumber);
        legWins[GameLoop.HostTeamIndex] = Mathf.Max(0, hostLegWins);
        legWins[GameLoop.OpponentTeamIndex] = Mathf.Max(0, opponentLegWins);
    }

    public static bool IsEscortingTeam(int teamIndex) => IsEscortingTeam(LegNumber, teamIndex);

    public static bool IsEscortingTeam(int legNumber, int teamIndex)
    {
        if (teamIndex != GameLoop.HostTeamIndex && teamIndex != GameLoop.OpponentTeamIndex)
            return false;

        return legNumber switch
        {
            1 => teamIndex == GameLoop.HostTeamIndex,
            2 => teamIndex == GameLoop.OpponentTeamIndex,
            _ => true,
        };
    }

    public static bool IsDecider(int legNumber) => legNumber >= DeciderLeg;

    public static bool IsSeriesDecided(
        int hostLegWins,
        int opponentLegWins,
        int completedLegs,
        out int winningTeamIndex
    )
    {
        winningTeamIndex = GameLoop.NoHillController;
        if (hostLegWins >= LegsToWin)
            winningTeamIndex = GameLoop.HostTeamIndex;
        else if (opponentLegWins >= LegsToWin)
            winningTeamIndex = GameLoop.OpponentTeamIndex;
        else if (completedLegs < DeciderLeg)
            return false;

        return true;
    }

    public static int HomeRow(int teamIndex) =>
        teamIndex == GameLoop.HostTeamIndex ? 0 : GridSystem.RowCount - 1;

    public static int ExtractionRowFor(int escortingTeamIndex) =>
        HomeRow(GameLoop.GetEnemyTeamIndex(escortingTeamIndex));

    public static HashSet<Vector2Int> ExtractionCellsFor(int escortingTeamIndex)
    {
        HashSet<Vector2Int> cells = new();
        int row = ExtractionRowFor(escortingTeamIndex);
        int centreColumn = GridSystem.ColumnCount / 2;
        int reach = ExtractionZoneWidthCells / 2;
        HashSet<Vector2Int> walls = GameLoop.wallLayout;

        for (int column = centreColumn - reach; column <= centreColumn + reach; column++)
        {
            Vector2Int cell = new(column, row);
            if (GridSystem.IsCellInBounds(cell) && !walls.Contains(cell))
                cells.Add(cell);
        }
        return cells;
    }

    public static int StepsToExtraction(Vector2Int presidentCell, int escortingTeamIndex)
    {
        int fewest = int.MaxValue;
        foreach (Vector2Int cell in ExtractionCellsFor(escortingTeamIndex))
            fewest = Mathf.Min(fewest, GridSystem.GetGridDistance(presidentCell, cell));
        return fewest;
    }

    public static List<Vector2Int[]> CreateSpawnLayout(int legNumber)
    {
        Vector2Int[][] layout = new Vector2Int[GameLoop.TeamCount][];
        HashSet<Vector2Int> taken = new();

        for (int teamIndex = 0; teamIndex < GameLoop.TeamCount; teamIndex++)
        {
            if (!IsEscortingTeam(legNumber, teamIndex))
                continue;

            layout[teamIndex] = GameLoop.CreateSpawnPositions(
                false,
                teamIndex,
                RosterRules.UnitsPerPlayer
            );
            taken.UnionWith(layout[teamIndex]);
        }

        for (int teamIndex = 0; teamIndex < GameLoop.TeamCount; teamIndex++)
        {
            if (layout[teamIndex] != null)
                continue;

            layout[teamIndex] = CreateDefenderDeployment(
                teamIndex,
                RosterRules.UnitsPerPlayer,
                taken
            );
            taken.UnionWith(layout[teamIndex]);
        }

        return layout.ToList();
    }

    private static Vector2Int[] CreateDefenderDeployment(
        int defendingTeamIndex,
        int unitCount,
        IEnumerable<Vector2Int> takenCells
    )
    {
        Vector2Int[] backRank = GameLoop.CreateSpawnPositions(false, defendingTeamIndex, unitCount);
        int extractionRow = HomeRow(defendingTeamIndex);
        int forward = extractionRow == 0 ? DefenderRowsFromExtraction : -DefenderRowsFromExtraction;
        int preferredRow = Mathf.Clamp(extractionRow + forward, 0, GridSystem.RowCount - 1);

        HashSet<Vector2Int> blocked = new(takenCells ?? Enumerable.Empty<Vector2Int>());
        blocked.UnionWith(ExtractionCellsFor(GameLoop.GetEnemyTeamIndex(defendingTeamIndex)));

        Vector2Int[] positions = new Vector2Int[unitCount];
        for (int index = 0; index < unitCount; index++)
        {
            Vector2Int preferred = new(backRank[index].x, preferredRow);
            if (!TryFindNearestOpenCell(preferred, blocked, out positions[index]))
            {
                throw new System.InvalidOperationException(
                    $"No open defender deployment cell near ({preferred.x},{preferred.y}) on "
                        + $"{MapCatalog.Active.DisplayName}."
                );
            }
            blocked.Add(positions[index]);
        }
        return positions;
    }

    private static bool TryFindNearestOpenCell(
        Vector2Int preferred,
        HashSet<Vector2Int> blocked,
        out Vector2Int found
    )
    {
        found = preferred;
        HashSet<Vector2Int> walls = GameLoop.wallLayout;
        List<Vector2Int> candidates = new();

        for (int row = 0; row < GridSystem.RowCount; row++)
        {
            for (int column = 0; column < GridSystem.ColumnCount; column++)
            {
                Vector2Int cell = new(column, row);
                if (!walls.Contains(cell) && !blocked.Contains(cell))
                    candidates.Add(cell);
            }
        }

        if (candidates.Count == 0)
            return false;

        candidates.Sort(
            (left, right) =>
            {
                int byDistance = GridSystem
                    .GetGridDistance(preferred, left)
                    .CompareTo(GridSystem.GetGridDistance(preferred, right));
                return byDistance != 0 ? byDistance : GridSystem.CompareCellsRowMajor(left, right);
            }
        );
        found = candidates[0];
        return true;
    }

    public static EscortLegResult ResolveLeg(
        int legNumber,
        IReadOnlyList<EscortStanding> standings,
        int roundsRemaining
    )
    {
        if (standings == null || standings.Count == 0)
            return EscortLegResult.Undecided;

        return IsDecider(legNumber)
            ? ResolveDeciderLeg(standings, roundsRemaining)
            : ResolveSinglePresidentLeg(legNumber, standings, roundsRemaining);
    }

    private static EscortLegResult ResolveSinglePresidentLeg(
        int legNumber,
        IReadOnlyList<EscortStanding> standings,
        int roundsRemaining
    )
    {
        int escortIndex = -1;
        for (int index = 0; index < standings.Count; index++)
        {
            if (IsEscortingTeam(legNumber, standings[index].TeamIndex))
            {
                escortIndex = index;
                break;
            }
        }
        if (escortIndex < 0)
            return EscortLegResult.Undecided;

        EscortStanding escort = standings[escortIndex];
        int defendingTeamIndex = GameLoop.GetEnemyTeamIndex(escort.TeamIndex);

        if (escort.Extracted)
            return EscortLegResult.Won(escort.TeamIndex, EscortLegReason.Extracted);

        // Ahead of OpposingCrewWiped: a mutual wipe takes the president with it.
        if (escort.PresidentDown)
            return EscortLegResult.Won(defendingTeamIndex, EscortLegReason.PresidentDown);

        if (escort.OpposingCrewWiped)
            return EscortLegResult.Won(escort.TeamIndex, EscortLegReason.DefenceEliminated);

        return roundsRemaining > 0
            ? EscortLegResult.Undecided
            : EscortLegResult.Won(defendingTeamIndex, EscortLegReason.DefendersHeld);
    }

    private static EscortLegResult ResolveDeciderLeg(
        IReadOnlyList<EscortStanding> standings,
        int roundsRemaining
    )
    {
        bool anyResolved = standings.Any(standing => standing.Resolved);
        if (!anyResolved && roundsRemaining > 0)
            return EscortLegResult.Undecided;

        EscortStanding best = standings[0];
        bool tied = false;
        for (int index = 1; index < standings.Count; index++)
        {
            EscortStanding contender = standings[index];
            if (contender.StepsRemaining < best.StepsRemaining)
            {
                best = contender;
                tied = false;
            }
            else if (contender.StepsRemaining == best.StepsRemaining)
            {
                tied = true;
            }
        }

        if (tied)
            return EscortLegResult.Level(EscortLegReason.GroundCovered);

        EscortLegReason reason =
            best.Extracted ? EscortLegReason.Extracted
            : anyResolved ? EscortLegReason.PresidentDown
            : EscortLegReason.GroundCovered;
        return EscortLegResult.Won(best.TeamIndex, reason);
    }
}
