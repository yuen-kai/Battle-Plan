using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One side of a sandbox board: which characters stand on it, where each of them stands, and
/// whether they can be killed. Slots are dense — index 0 through <see cref="Count"/>-1 — because a
/// slot is a fielded unit rather than a roster position that may be empty.
/// </summary>
public sealed class SandboxCrew
{
    public readonly List<int> unitIndices = new();
    public readonly List<Vector2Int> cells = new();
    public bool immortal;

    public int Count => unitIndices.Count;

    public bool HasSlot(int slot) => slot >= 0 && slot < Count;
}

/// <summary>
/// Lifetime and board setup for the sandbox: a loopback-host Elimination match on Concourse where
/// the designer owns both crews outright — who is on the board, where they stand, and what each of
/// them does with its round.
///
/// The sandbox player is always the loopback host, so this is read statically rather than
/// replicated, the same arrangement <see cref="TutorialSession"/> and <c>EscortSeries</c> use. Every
/// rule here is a pure function of the two crews so it can be exercised in edit mode; anything that
/// touches live units belongs in <see cref="SandboxDirector"/>.
///
/// The opponent is planned by hand, so the bot is frozen for the whole session: it never chooses a
/// move or an ability, and the orders it would have contributed are replaced by the designer's.
/// What it is still left to do is answer dodge windows, which a frozen team answers by standing
/// still — the "unplanned units hold" rule, applied to the one phase the panel has no say in.
/// </summary>
public static class SandboxSession
{
    /// <summary>
    /// A crew may field up to the ordinary roster size, which is what keeps <c>ConfigureTeam</c>,
    /// <see cref="RosterRules"/> validation and both HUD card strips untouched by the sandbox.
    /// </summary>
    public const int MaxUnitsPerTeam = RosterRules.UnitsPerPlayer;

    /// <summary>Soldier: the plainest kit on the board, so a fresh sandbox reads as a control.</summary>
    public const int DefaultUnitIndex = 4;

    /// <summary>
    /// Effectively open-ended. The sandbox is paced by a designer inspecting a kit, so the round
    /// ends on the ready button and the HUD hides the countdown.
    /// </summary>
    public const float PlanningSeconds = 3600f;

    /// <summary>
    /// The rows the two crews open on: three cells apart, so every weapon on the board reaches
    /// across and there is still room to move, dash or drop an ability in between.
    /// </summary>
    private const int HostRow = 3;
    private const int OpponentRow = 6;

    /// <summary>
    /// Columns clear of cover on both of those rows, ordered so a growing crew spreads outward from
    /// the middle rather than hugging an edge. Checked against the live wall layout before use, so a
    /// board that disagrees falls back to a search instead of standing a unit inside a wall.
    /// </summary>
    private static readonly int[] PreferredColumns = { 7, 6, 8, 4, 10 };

    private static readonly SandboxCrew[] crews = { new(), new() };

    private static PathsDict pendingEnemyPlan;
    private static PathsDict pendingEnemyDodge;

    public static bool IsActive { get; private set; }

    /// <summary>
    /// Set once the join screen has kicked off this session's loopback host, so a scene re-enable
    /// cannot start a second one.
    /// </summary>
    public static bool HostStartRequested { get; set; }

    /// <summary>
    /// Board-edit mode: planning input is suspended and a drag moves a unit between squares
    /// instead of drawing it a route. <see cref="SandboxDirector"/> owns the gesture.
    /// </summary>
    public static bool BoardEditActive { get; set; }

    /// <summary>
    /// Whether that gesture is actually live: armed, and between rounds. A unit dragged while it is
    /// running a route would be pulled two ways at once, and the dodge window wants the pointer for
    /// a drag of its own — so leaving the mode armed never costs the designer a dodge.
    /// </summary>
    public static bool IsBoardEditLive =>
        IsActive
        && BoardEditActive
        && (GameLoop.currentPhase == "planning" || GameLoop.currentPhase == "idle");

    public static SandboxCrew Crew(int teamIndex)
    {
        return crews[Mathf.Clamp(teamIndex, 0, crews.Length - 1)];
    }

    public static void Begin()
    {
        IsActive = true;
        HostStartRequested = false;
        BoardEditActive = false;
        pendingEnemyPlan = null;
        pendingEnemyDodge = null;

        for (int teamIndex = 0; teamIndex < crews.Length; teamIndex++)
        {
            if (crews[teamIndex].Count == 0)
                ResetCrew(teamIndex);
        }

        BotFrozenUnits.SetTeamFrozen(GameLoop.OpponentTeamIndex, true);
    }

    public static void End()
    {
        IsActive = false;
        HostStartRequested = false;
        BoardEditActive = false;
        pendingEnemyPlan = null;
        pendingEnemyDodge = null;
        BotFrozenUnits.ClearAll();
    }

    /// <summary>One default character on its opening cell — the smallest legal crew.</summary>
    public static void ResetCrew(int teamIndex)
    {
        SandboxCrew crew = Crew(teamIndex);
        crew.unitIndices.Clear();
        crew.cells.Clear();
        crew.immortal = false;
        TryAddUnit(teamIndex, DefaultUnitIndex);
    }

    public static void ResetAllCrews()
    {
        for (int teamIndex = 0; teamIndex < crews.Length; teamIndex++)
            ResetCrew(teamIndex);
    }

    public static bool TryAddUnit(int teamIndex, int catalogIndex)
    {
        SandboxCrew crew = Crew(teamIndex);
        if (crew.Count >= MaxUnitsPerTeam || catalogIndex < 0)
            return false;
        if (!TryFindFreeCell(teamIndex, out Vector2Int cell))
            return false;

        crew.unitIndices.Add(catalogIndex);
        crew.cells.Add(cell);
        return true;
    }

    /// <summary>
    /// Drops a unit, closing the gap behind it so the remaining slots stay dense. A crew never
    /// empties: a side with nobody on it is a wiped crew, which the round loop would resolve as a
    /// finished match rather than as a board waiting to be set up.
    /// </summary>
    public static bool RemoveUnit(int teamIndex, int slot)
    {
        SandboxCrew crew = Crew(teamIndex);
        if (!crew.HasSlot(slot) || crew.Count <= 1)
            return false;

        crew.unitIndices.RemoveAt(slot);
        crew.cells.RemoveAt(slot);
        return true;
    }

    public static bool ReplaceUnit(int teamIndex, int slot, int catalogIndex)
    {
        SandboxCrew crew = Crew(teamIndex);
        if (!crew.HasSlot(slot) || catalogIndex < 0)
            return false;

        crew.unitIndices[slot] = catalogIndex;
        return true;
    }

    public static bool TryMoveUnit(int teamIndex, int slot, Vector2Int cell)
    {
        SandboxCrew crew = Crew(teamIndex);
        if (!crew.HasSlot(slot) || !IsCellPlaceable(cell))
            return false;
        if (!IsCellFree(cell, teamIndex, slot))
            return false;

        crew.cells[slot] = cell;
        return true;
    }

    public static int UnitsForTeam(int teamIndex)
    {
        return Mathf.Max(1, Crew(teamIndex).Count);
    }

    public static bool IsImmortal(int teamIndex)
    {
        return IsActive && teamIndex >= 0 && teamIndex < crews.Length && crews[teamIndex].immortal;
    }

    public static bool IsCellPlaceable(Vector2Int cell)
    {
        return GridSystem.IsCellInBounds(cell) && !GameLoop.wallLayout.Contains(cell);
    }

    /// <summary>
    /// Whether no other unit on either side is set up on the given cell. Two units cannot share a
    /// square, and the crews are placed as one board rather than two.
    /// </summary>
    public static bool IsCellFree(Vector2Int cell, int exceptTeamIndex = -1, int exceptSlot = -1)
    {
        for (int teamIndex = 0; teamIndex < crews.Length; teamIndex++)
        {
            SandboxCrew crew = crews[teamIndex];
            for (int slot = 0; slot < crew.Count; slot++)
            {
                if (teamIndex == exceptTeamIndex && slot == exceptSlot)
                    continue;
                if (crew.cells[slot] == cell)
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// The next cell a newly added unit should stand on: its team's opening row first, then any
    /// legal square, searched outward from that row so a crew stays together on a crowded board.
    /// </summary>
    public static bool TryFindFreeCell(int teamIndex, out Vector2Int cell)
    {
        int row = teamIndex == GameLoop.OpponentTeamIndex ? OpponentRow : HostRow;
        foreach (int column in PreferredColumns)
        {
            cell = new Vector2Int(column, row);
            if (IsCellPlaceable(cell) && IsCellFree(cell))
                return true;
        }

        for (int rowOffset = 0; rowOffset < GridSystem.RowCount; rowOffset++)
        {
            foreach (int candidateRow in RowsAtOffset(row, rowOffset))
            {
                for (int column = 0; column < GridSystem.ColumnCount; column++)
                {
                    cell = new Vector2Int(column, candidateRow);
                    if (IsCellPlaceable(cell) && IsCellFree(cell))
                        return true;
                }
            }
        }

        cell = default;
        return false;
    }

    private static IEnumerable<int> RowsAtOffset(int row, int offset)
    {
        if (offset == 0)
        {
            yield return row;
            yield break;
        }
        if (row - offset >= 0)
            yield return row - offset;
        if (row + offset < GridSystem.RowCount)
            yield return row + offset;
    }

    /// <summary>
    /// A full-length roster for <c>GameLoop.ConfigureTeam</c>, naming exactly who is on the board.
    /// Only the fielded slots are ever read — for spawning, for the cards and for the battle report
    /// — and the tail repeats the crew's first pick to fill the shape the rest of the game expects.
    /// <para>
    /// The picks ride through as themselves, restricted characters included. Standing one in for an
    /// eligible substitute was tried and dropped: it needs a catalog to test eligibility against,
    /// and the roster is built on the join screen before any of them is loaded, so a restricted pick
    /// reached spawning unsubstituted and threw. It also lied to everything downstream that names a
    /// unit from its roster slot. <c>GameLoop.SetupUnitsAndCards</c> skips the eligibility rule for
    /// a sandbox board instead, which is the rule the sandbox is actually outside of.
    /// </para>
    /// </summary>
    public static int[] BuildRoster(int teamIndex)
    {
        SandboxCrew crew = Crew(teamIndex);
        int[] roster = new int[RosterRules.UnitsPerPlayer];
        for (int slot = 0; slot < roster.Length; slot++)
        {
            roster[slot] = crew.HasSlot(slot)
                ? crew.unitIndices[slot]
                : (crew.Count > 0 ? crew.unitIndices[0] : DefaultUnitIndex);
        }
        return roster;
    }

    public static List<Vector2Int[]> CreateSpawnLayout()
    {
        List<Vector2Int[]> layout = new(GameLoop.TeamCount);
        for (int teamIndex = 0; teamIndex < GameLoop.TeamCount; teamIndex++)
        {
            SandboxCrew crew = Crew(teamIndex);
            Vector2Int[] cells = new Vector2Int[UnitsForTeam(teamIndex)];
            for (int slot = 0; slot < cells.Length; slot++)
            {
                cells[slot] = crew.HasSlot(slot)
                    ? crew.cells[slot]
                    : new Vector2Int(
                        PreferredColumns[slot % PreferredColumns.Length],
                        teamIndex == GameLoop.OpponentTeamIndex ? OpponentRow : HostRow
                    );
            }
            layout.Add(cells);
        }
        return layout;
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

    /// <summary>
    /// The opponent's orders for this round, authored by the designer through the same planning
    /// session as their own crew. Held here rather than sent over the wire because the sandbox host
    /// owns both seats: the submit RPC only ever accepts the sender's own team.
    /// </summary>
    public static void SetPendingEnemyPlan(PathsDict plan)
    {
        pendingEnemyPlan = plan;
    }

    public static PathsDict ConsumeEnemyPlan()
    {
        PathsDict plan = pendingEnemyPlan;
        pendingEnemyPlan = null;
        return plan ?? new PathsDict();
    }

    /// <summary>
    /// The opposing crew's dives for the open dodge window. Held apart from its orders because the
    /// two are answered in different phases and are accepted by different server paths — a dive
    /// replaces the order it was given in response to.
    /// </summary>
    public static void SetPendingEnemyDodge(PathsDict dives)
    {
        pendingEnemyDodge = dives;
    }

    public static PathsDict ConsumeEnemyDodge()
    {
        PathsDict dives = pendingEnemyDodge;
        pendingEnemyDodge = null;
        return dives ?? new PathsDict();
    }
}
