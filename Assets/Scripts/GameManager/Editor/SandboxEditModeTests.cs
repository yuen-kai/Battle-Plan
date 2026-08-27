using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

[TestFixture]
[Category("Sandbox")]
public class SandboxEditModeTests
{
    private const string CatalogPath = "Assets/UnitStats/AllUnits.asset";

    private const int HostTeam = GameLoop.HostTeamIndex;
    private const int OpponentTeam = GameLoop.OpponentTeamIndex;

    [SetUp]
    public void SetUp()
    {
        // Placement is a function of the live wall layout, and the sandbox always runs on Concourse.
        MapCatalog.SetActive(MapId.Concourse);
        SandboxSession.ResetAllCrews();
    }

    [TearDown]
    public void TearDown()
    {
        SandboxSession.End();
        SandboxSession.ResetAllCrews();
        BotFrozenUnits.ClearAll();
    }

    private static List<UnitData> LoadCatalog()
    {
        UnitDatabase catalog = AssetDatabase.LoadAssetAtPath<UnitDatabase>(CatalogPath);
        Assert.That(catalog?.units, Is.Not.Null, $"Could not load {CatalogPath}.");
        return catalog.units;
    }

    [Test]
    public void Begin_OpensWithOneUnitASideAndFreezesTheOpposingBot()
    {
        SandboxSession.Begin();

        Assert.That(SandboxSession.IsActive, Is.True);
        Assert.That(SandboxSession.UnitsForTeam(HostTeam), Is.EqualTo(1));
        Assert.That(SandboxSession.UnitsForTeam(OpponentTeam), Is.EqualTo(1));
        Assert.That(
            BotFrozenUnits.IsFrozen(null, OpponentTeam),
            Is.True,
            "The designer plans the opposing crew, so the bot must choose nothing for it."
        );
        Assert.That(BotFrozenUnits.IsFrozen(null, HostTeam), Is.False);
    }

    [Test]
    public void End_RestoresTheOrdinaryCrewSizeAndThawsTheBot()
    {
        SandboxSession.Begin();
        SandboxSession.End();

        Assert.That(SandboxSession.IsActive, Is.False);
        Assert.That(GameLoop.UnitsPerTeamThisMatch, Is.EqualTo(RosterRules.UnitsPerPlayer));
        Assert.That(BotFrozenUnits.IsFrozen(null, OpponentTeam), Is.False);
    }

    [Test]
    public void Begin_KeepsASetupThatSurvivedTheLastSession()
    {
        SandboxSession.TryAddUnit(HostTeam, 1);
        SandboxSession.TryAddUnit(HostTeam, 2);

        SandboxSession.Begin();

        Assert.That(SandboxSession.UnitsForTeam(HostTeam), Is.EqualTo(3));
        Assert.That(SandboxSession.Crew(HostTeam).unitIndices, Is.EqualTo(new[] { 4, 1, 2 }));
    }

    [Test]
    public void TryAddUnit_FillsUpToAFullCrewAndThenRefuses()
    {
        for (int added = 1; added < SandboxSession.MaxUnitsPerTeam; added++)
            Assert.That(SandboxSession.TryAddUnit(HostTeam, 0), Is.True);

        Assert.That(
            SandboxSession.UnitsForTeam(HostTeam),
            Is.EqualTo(SandboxSession.MaxUnitsPerTeam)
        );
        Assert.That(
            SandboxSession.TryAddUnit(HostTeam, 0),
            Is.False,
            "A crew may never be larger than the roster the rest of the game is built around."
        );
    }

    [Test]
    public void TryAddUnit_AllowsTheSameCharacterSeveralTimes()
    {
        Assert.That(SandboxSession.TryAddUnit(HostTeam, 4), Is.True);
        Assert.That(SandboxSession.TryAddUnit(HostTeam, 4), Is.True);

        Assert.That(SandboxSession.Crew(HostTeam).unitIndices, Is.All.EqualTo(4));
    }

    [Test]
    public void EveryPlacedCellIsLegalAndUnshared()
    {
        for (int added = 1; added < SandboxSession.MaxUnitsPerTeam; added++)
        {
            SandboxSession.TryAddUnit(HostTeam, 0);
            SandboxSession.TryAddUnit(OpponentTeam, 0);
        }

        List<Vector2Int> placed = new();
        for (int teamIndex = 0; teamIndex < GameLoop.TeamCount; teamIndex++)
            placed.AddRange(SandboxSession.Crew(teamIndex).cells);

        Assert.That(placed.Count, Is.EqualTo(SandboxSession.MaxUnitsPerTeam * GameLoop.TeamCount));
        Assert.That(placed, Is.Unique, "Two units would be stood on the same square.");
        foreach (Vector2Int cell in placed)
        {
            Assert.That(GridSystem.IsCellInBounds(cell), Is.True, $"{cell} is off the board.");
            Assert.That(GameLoop.wallLayout, Has.No.Member(cell), $"{cell} is inside cover.");
        }
    }

    [Test]
    public void OpeningRowsLeaveTheTwoCrewsWithinReachOfEachOther()
    {
        Vector2Int host = SandboxSession.Crew(HostTeam).cells[0];
        Vector2Int opponent = SandboxSession.Crew(OpponentTeam).cells[0];

        Assert.That(
            Mathf.Abs(host.y - opponent.y),
            Is.InRange(2, 4),
            "Close enough that every weapon on the board reaches, far enough to leave room to act."
        );
        for (int row = Mathf.Min(host.y, opponent.y) + 1; row < Mathf.Max(host.y, opponent.y); row++)
        {
            Assert.That(
                GameLoop.wallLayout,
                Has.No.Member(new Vector2Int(host.x, row)),
                "Nothing may block the opening sightline between the two crews."
            );
        }
    }

    [Test]
    public void RemoveUnit_ClosesTheGapAndNeverEmptiesASide()
    {
        SandboxSession.TryAddUnit(HostTeam, 1);
        SandboxSession.TryAddUnit(HostTeam, 2);
        Vector2Int lastCell = SandboxSession.Crew(HostTeam).cells[2];

        Assert.That(SandboxSession.RemoveUnit(HostTeam, 0), Is.True);

        Assert.That(SandboxSession.Crew(HostTeam).unitIndices, Is.EqualTo(new[] { 1, 2 }));
        Assert.That(SandboxSession.Crew(HostTeam).cells[1], Is.EqualTo(lastCell));

        Assert.That(SandboxSession.RemoveUnit(HostTeam, 1), Is.True);
        Assert.That(
            SandboxSession.RemoveUnit(HostTeam, 0),
            Is.False,
            "An empty side reads as a wiped crew, which would end the match instead of waiting."
        );
    }

    [Test]
    public void ReplaceUnit_KeepsTheSlotAndItsSquare()
    {
        SandboxSession.TryAddUnit(HostTeam, 1);
        Vector2Int cell = SandboxSession.Crew(HostTeam).cells[1];

        Assert.That(SandboxSession.ReplaceUnit(HostTeam, 1, 7), Is.True);

        Assert.That(SandboxSession.Crew(HostTeam).unitIndices[1], Is.EqualTo(7));
        Assert.That(SandboxSession.Crew(HostTeam).cells[1], Is.EqualTo(cell));
        Assert.That(SandboxSession.ReplaceUnit(HostTeam, 4, 7), Is.False);
    }

    [Test]
    public void TryMoveUnit_AcceptsOpenGroundAndRefusesCoverEdgesAndCompany()
    {
        Vector2Int occupied = SandboxSession.Crew(OpponentTeam).cells[0];
        Vector2Int wall = default;
        foreach (Vector2Int cell in GameLoop.wallLayout)
        {
            wall = cell;
            break;
        }

        Assert.That(SandboxSession.TryMoveUnit(HostTeam, 0, new Vector2Int(0, 0)), Is.True);
        Assert.That(SandboxSession.Crew(HostTeam).cells[0], Is.EqualTo(new Vector2Int(0, 0)));

        Assert.That(SandboxSession.TryMoveUnit(HostTeam, 0, wall), Is.False, "Inside cover.");
        Assert.That(
            SandboxSession.TryMoveUnit(HostTeam, 0, new Vector2Int(-1, 0)),
            Is.False,
            "Off the board."
        );
        Assert.That(
            SandboxSession.TryMoveUnit(HostTeam, 0, occupied),
            Is.False,
            "Occupied, and by the other crew — the board is placed as one board."
        );
        Assert.That(
            SandboxSession.TryMoveUnit(HostTeam, 0, new Vector2Int(0, 0)),
            Is.True,
            "A unit may be dropped back on the square it already holds."
        );
    }

    [Test]
    public void UnitCounts_SizeEachSideIndependently()
    {
        SandboxSession.Begin();
        SandboxSession.TryAddUnit(OpponentTeam, 0);
        SandboxSession.TryAddUnit(OpponentTeam, 0);

        Assert.That(GameLoop.UnitsForTeamThisMatch(HostTeam), Is.EqualTo(1));
        Assert.That(GameLoop.UnitsForTeamThisMatch(OpponentTeam), Is.EqualTo(3));
    }

    [Test]
    public void CreateSpawnLayout_StandsEachCrewOnTheCellsItWasPlacedOn()
    {
        SandboxSession.Begin();
        SandboxSession.TryAddUnit(HostTeam, 3);

        List<Vector2Int[]> layout = SandboxSession.CreateSpawnLayout();

        Assert.That(layout.Count, Is.EqualTo(GameLoop.TeamCount));
        for (int teamIndex = 0; teamIndex < GameLoop.TeamCount; teamIndex++)
        {
            Assert.That(
                layout[teamIndex],
                Is.EqualTo(SandboxSession.Crew(teamIndex).cells.ToArray())
            );
        }
    }

    [Test]
    public void BuildRoster_FieldsARestrictedCharacterAsItself()
    {
        List<UnitData> catalog = LoadCatalog();
        int restrictedIndex = -1;
        for (int unitIndex = 0; unitIndex < catalog.Count; unitIndex++)
        {
            if (catalog[unitIndex] != null && !catalog[unitIndex].IsRosterEligible)
                restrictedIndex = unitIndex;
        }
        Assert.That(restrictedIndex, Is.GreaterThanOrEqualTo(0), "Expected a restricted character.");

        SandboxSession.ReplaceUnit(HostTeam, 0, restrictedIndex);
        SandboxSession.TryAddUnit(HostTeam, 2);

        int[] roster = SandboxSession.BuildRoster(HostTeam);

        Assert.That(roster.Length, Is.EqualTo(RosterRules.UnitsPerPlayer));
        Assert.That(
            roster[0],
            Is.EqualTo(restrictedIndex),
            "A character no ordinary crew may pick is exactly what the sandbox is for, and every "
                + "reader of a roster slot — cards, battle report — has to be told who is there."
        );
        Assert.That(roster[1], Is.EqualTo(2));
        Assert.That(
            RosterRules.Validate(roster, catalog).IsValid,
            Is.False,
            "So the ordinary rule cannot be the thing that admits this board; GameLoop skips it for "
                + "a sandbox match instead."
        );
    }

    [Test]
    public void BuildRoster_NamesRealCatalogEntriesForEveryCharacterTheSandboxOffers()
    {
        List<UnitData> catalog = LoadCatalog();

        for (int unitIndex = 0; unitIndex < catalog.Count; unitIndex++)
        {
            if (catalog[unitIndex] == null || catalog[unitIndex].unitModel == null)
                continue;

            SandboxSession.ReplaceUnit(HostTeam, 0, unitIndex);
            int[] roster = SandboxSession.BuildRoster(HostTeam);

            Assert.That(roster[0], Is.EqualTo(unitIndex));
            foreach (int rosterIndex in roster)
            {
                Assert.That(rosterIndex, Is.InRange(0, catalog.Count - 1));
                Assert.That(
                    catalog[rosterIndex]?.unitModel,
                    Is.Not.Null,
                    $"Roster index {rosterIndex} has no model, so it could not be spawned."
                );
            }
        }
    }

    [Test]
    public void BuildRoster_PadsTheUnfieldedTailWithSomeoneWhoIsActuallyOnTheBoard()
    {
        SandboxSession.ReplaceUnit(HostTeam, 0, 3);

        int[] roster = SandboxSession.BuildRoster(HostTeam);

        Assert.That(SandboxSession.UnitsForTeam(HostTeam), Is.EqualTo(1));
        Assert.That(roster, Is.All.EqualTo(3));
    }

    [Test]
    public void Immortality_IsPerCrewAndOnlyWhileTheSandboxIsRunning()
    {
        SandboxSession.Crew(HostTeam).immortal = true;
        Assert.That(
            SandboxSession.IsImmortal(HostTeam),
            Is.False,
            "Nothing outside a sandbox match may be protected from death."
        );

        SandboxSession.Begin();
        SandboxSession.Crew(HostTeam).immortal = true;

        Assert.That(SandboxSession.IsImmortal(HostTeam), Is.True);
        Assert.That(SandboxSession.IsImmortal(OpponentTeam), Is.False);
        Assert.That(SandboxSession.IsImmortal(-1), Is.False);
    }

    [Test]
    public void ResetAllCrews_ClearsBothSidesBackToOneDefaultUnit()
    {
        SandboxSession.TryAddUnit(HostTeam, 1);
        SandboxSession.Crew(HostTeam).immortal = true;

        SandboxSession.ResetAllCrews();

        for (int teamIndex = 0; teamIndex < GameLoop.TeamCount; teamIndex++)
        {
            Assert.That(SandboxSession.Crew(teamIndex).Count, Is.EqualTo(1));
            Assert.That(
                SandboxSession.Crew(teamIndex).unitIndices[0],
                Is.EqualTo(SandboxSession.DefaultUnitIndex)
            );
            Assert.That(SandboxSession.Crew(teamIndex).immortal, Is.False);
        }
    }

    [Test]
    public void DefaultUnitIndex_IsARealRosterEligibleCatalogEntry()
    {
        Assert.That(
            RosterRules.IsUnitEligible(LoadCatalog(), SandboxSession.DefaultUnitIndex),
            Is.True
        );
    }

    [Test]
    public void BuildMatchOptions_IsASingleClientBotMatchWithFogOffOnConcourse()
    {
        MatchOptions options = SandboxSession.BuildMatchOptions();

        Assert.That(options.IsBotMatch, Is.True, "The sandbox is a solo loopback host.");
        Assert.That(options.fogOfWar, Is.False, "Both crews must stay visible at all times.");
        Assert.That(options.mapId, Is.EqualTo(MapId.Concourse));
        Assert.That(options.gameMode, Is.EqualTo(GameMode.Elimination));
    }

    [Test]
    public void PendingEnemyPlan_IsHandedOverOnceAndThenEmpty()
    {
        GameObject probe = new("SandboxTests_PlanProbe");
        try
        {
            PathsDict plan = new() { [probe] = (false, new List<Vector3> { Vector3.zero }) };
            SandboxSession.SetPendingEnemyPlan(plan);

            Assert.That(SandboxSession.ConsumeEnemyPlan(), Is.SameAs(plan));
            Assert.That(
                SandboxSession.ConsumeEnemyPlan(),
                Is.Empty,
                "A round that was never planned for must not replay the previous round's orders."
            );
        }
        finally
        {
            Object.DestroyImmediate(probe);
        }
    }

    [Test]
    public void BoardEditMode_IsOffWheneverASessionStartsOrEnds()
    {
        SandboxSession.Begin();
        SandboxSession.BoardEditActive = true;
        SandboxSession.End();

        Assert.That(SandboxSession.BoardEditActive, Is.False);

        SandboxSession.BoardEditActive = true;
        SandboxSession.Begin();
        Assert.That(SandboxSession.BoardEditActive, Is.False);
    }

    [Test]
    public void BoardEditMode_IsLiveOnlyBetweenRounds()
    {
        string restorePhase = GameLoop.currentPhase;
        try
        {
            SandboxSession.Begin();
            SandboxSession.BoardEditActive = true;

            GameLoop.currentPhase = "planning";
            Assert.That(SandboxSession.IsBoardEditLive, Is.True);
            GameLoop.currentPhase = "idle";
            Assert.That(SandboxSession.IsBoardEditLive, Is.True);

            GameLoop.currentPhase = "dodging";
            Assert.That(
                SandboxSession.IsBoardEditLive,
                Is.False,
                "A dodge wants the pointer for a dive, so leaving the mode armed cannot cost one."
            );
            GameLoop.currentPhase = "executing";
            Assert.That(SandboxSession.IsBoardEditLive, Is.False);

            GameLoop.currentPhase = "planning";
            SandboxSession.BoardEditActive = false;
            Assert.That(SandboxSession.IsBoardEditLive, Is.False);
        }
        finally
        {
            GameLoop.currentPhase = restorePhase;
        }
    }

    [Test]
    public void TutorialSession_TakesPrecedenceOverASandboxSession()
    {
        SandboxSession.Begin();
        TutorialSession.Begin();
        try
        {
            Assert.That(GameLoop.UnitsPerTeamThisMatch, Is.EqualTo(TutorialSession.UnitsPerTeam));
        }
        finally
        {
            TutorialSession.End();
        }
    }

    [Test]
    public void BotFrozenUnits_SetFrozen_NullUnit_DoesNotThrowAndStaysUnfrozen()
    {
        Assert.DoesNotThrow(() => BotFrozenUnits.SetFrozen(null, true));
        Assert.DoesNotThrow(() => BotFrozenUnits.SetFrozen(null, false));
        Assert.That(BotFrozenUnits.IsFrozen(null, 0), Is.False);
    }

    [Test]
    public void BotFrozenUnits_TeamFreeze_AppliesWithoutAnyRegisteredGameObject()
    {
        const int arbitraryTeamIndex = 1;

        Assert.That(BotFrozenUnits.IsFrozen(null, arbitraryTeamIndex), Is.False);

        BotFrozenUnits.SetTeamFrozen(arbitraryTeamIndex, true);
        Assert.That(
            BotFrozenUnits.IsFrozen(null, arbitraryTeamIndex),
            Is.True,
            "A team-level freeze must apply even when no unit GameObject has been registered."
        );
        Assert.That(
            BotFrozenUnits.IsFrozen(null, arbitraryTeamIndex + 1),
            Is.False,
            "The freeze must be scoped to the team it was set for."
        );

        BotFrozenUnits.SetTeamFrozen(arbitraryTeamIndex, false);
        Assert.That(BotFrozenUnits.IsFrozen(null, arbitraryTeamIndex), Is.False);
    }

    [Test]
    public void BotFrozenUnits_ClearAll_ResetsBothPerUnitAndPerTeamState()
    {
        GameObject probe = new("SandboxTests_FrozenProbe");
        try
        {
            BotFrozenUnits.SetFrozen(probe, true);
            BotFrozenUnits.SetTeamFrozen(0, true);
            Assert.That(BotFrozenUnits.IsFrozen(probe, 1), Is.True);
            Assert.That(BotFrozenUnits.IsFrozen(null, 0), Is.True);

            BotFrozenUnits.ClearAll();

            Assert.That(BotFrozenUnits.IsFrozen(probe, 1), Is.False);
            Assert.That(BotFrozenUnits.IsFrozen(null, 0), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(probe);
        }
    }
}
