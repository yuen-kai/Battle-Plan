using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The character sandbox decides its whole shape before the board is built — one unit a side, at
/// fixed cells, with a fixed set of match options — so almost all of it IS statically checkable
/// without Play mode, unlike the version of this tool that patched a running 5-a-side match. What
/// stays out of reach here is the human half: entering Play mode and handing over the HUD.
///
/// <see cref="SandboxSession"/> is shared static state read while a match spawns, so every test here
/// must end it in <see cref="TearDown"/> — a leaked active session would field one unit a side in
/// every other fixture that asserts a full crew.
/// </summary>
[TestFixture]
[Category("CharacterSandbox")]
public class CharacterSandboxEditModeTests
{
    private const string CatalogPath = "Assets/UnitStats/AllUnits.asset";

    [TearDown]
    public void TearDown()
    {
        SandboxSession.End();
        SandboxSession.DummyFightsBack = false;
        BotFrozenUnits.ClearAll();
    }

    private static UnitDatabase LoadCatalog()
    {
        UnitDatabase catalog = AssetDatabase.LoadAssetAtPath<UnitDatabase>(CatalogPath);
        Assert.That(catalog?.units, Is.Not.Null, $"Could not load {CatalogPath}.");
        return catalog;
    }

    [Test]
    public void SpawnCells_AreInBoundsAndNotWalls()
    {
        Assert.That(GridSystem.IsCellInBounds(SandboxSession.PlayerSpawn), Is.True);
        Assert.That(GridSystem.IsCellInBounds(SandboxSession.EnemySpawn), Is.True);
        Assert.That(GameLoop.wallLayout, Has.No.Member(SandboxSession.PlayerSpawn));
        Assert.That(GameLoop.wallLayout, Has.No.Member(SandboxSession.EnemySpawn));
        Assert.That(SandboxSession.PlayerSpawn, Is.Not.EqualTo(SandboxSession.EnemySpawn));
    }

    [Test]
    public void SpawnCells_ShareAClearColumnCloseEnoughToTradeFire()
    {
        Vector2Int player = SandboxSession.PlayerSpawn;
        Vector2Int enemy = SandboxSession.EnemySpawn;
        Assert.That(player.x, Is.EqualTo(enemy.x), "The pair is documented as sharing a column.");

        int separation = Mathf.Abs(player.y - enemy.y);
        Assert.That(
            separation,
            Is.InRange(2, 4),
            "Close enough that every current unit's weapon reaches, far enough to leave room to "
                + "move, dash or place an ability between them."
        );

        for (int row = Mathf.Min(player.y, enemy.y) + 1; row < Mathf.Max(player.y, enemy.y); row++)
        {
            Assert.That(
                GameLoop.wallLayout,
                Has.No.Member(new Vector2Int(player.x, row)),
                "Nothing may block line of sight between the two spawns."
            );
        }
    }

    [Test]
    public void AdjacentWallCells_AreRealWallsNextToTheirSpawns()
    {
        // The sandbox promises "a wall nearby" without shipping a bespoke board, so the advertised
        // cover cells have to actually be walls on the map the session selects.
        Assert.That(GameLoop.wallLayout, Has.Member(SandboxSession.PlayerAdjacentWall));
        Assert.That(GameLoop.wallLayout, Has.Member(SandboxSession.EnemyAdjacentWall));
        Assert.That(
            StepDistance(SandboxSession.PlayerSpawn, SandboxSession.PlayerAdjacentWall),
            Is.EqualTo(1),
            "The advertised cover must be reachable in one step, not merely somewhere on the board."
        );
        Assert.That(
            StepDistance(SandboxSession.EnemySpawn, SandboxSession.EnemyAdjacentWall),
            Is.EqualTo(1)
        );
    }

    private static int StepDistance(Vector2Int from, Vector2Int to) =>
        Mathf.Abs(from.x - to.x) + Mathf.Abs(from.y - to.y);

    [Test]
    public void Begin_FieldsExactlyOneUnitPerTeam()
    {
        Assert.That(
            GameLoop.UnitsPerTeamThisMatch,
            Is.EqualTo(RosterRules.UnitsPerPlayer),
            "Baseline: no session, full crew."
        );

        SandboxSession.Begin(0, CharacterSandbox.DefaultDummyUnitIndex);

        Assert.That(GameLoop.UnitsPerTeamThisMatch, Is.EqualTo(1));
    }

    [Test]
    public void End_RestoresTheFullCrewSizeSoTheNextMatchIsOrdinary()
    {
        SandboxSession.Begin(0, CharacterSandbox.DefaultDummyUnitIndex);
        SandboxSession.End();

        Assert.That(SandboxSession.IsActive, Is.False);
        Assert.That(GameLoop.UnitsPerTeamThisMatch, Is.EqualTo(RosterRules.UnitsPerPlayer));
    }

    [Test]
    public void Begin_FreezesTheDummyTeamBeforeAnyUnitGameObjectExists()
    {
        // The freeze has to be registered at team level here rather than per unit, because the bot
        // plans the opening round before the dummy's GameObject exists.
        Assert.That(BotFrozenUnits.IsFrozen(null, GameLoop.OpponentTeamIndex), Is.False);

        SandboxSession.Begin(0, CharacterSandbox.DefaultDummyUnitIndex);

        Assert.That(BotFrozenUnits.IsFrozen(null, GameLoop.OpponentTeamIndex), Is.True);
        Assert.That(
            BotFrozenUnits.IsFrozen(null, GameLoop.HostTeamIndex),
            Is.False,
            "The character under test must stay under the designer's control."
        );
    }

    [Test]
    public void End_ThawsTheDummyTeam()
    {
        SandboxSession.Begin(0, CharacterSandbox.DefaultDummyUnitIndex);
        SandboxSession.End();

        Assert.That(BotFrozenUnits.IsFrozen(null, GameLoop.OpponentTeamIndex), Is.False);
    }

    [Test]
    public void DummyFightsBack_DefaultsToOffSoAKitCanBeWatchedWithoutReturnFire()
    {
        Assert.That(SandboxSession.DummyFightsBack, Is.False);
    }

    [Test]
    public void CreateSpawnLayout_PlacesOneUnitPerTeamOnTheDocumentedCells()
    {
        SandboxSession.Begin(0, CharacterSandbox.DefaultDummyUnitIndex);

        var layout = SandboxSession.CreateSpawnLayout();

        Assert.That(layout.Count, Is.EqualTo(GameLoop.TeamCount));
        foreach (Vector2Int[] team in layout)
            Assert.That(team.Length, Is.EqualTo(SandboxSession.UnitsPerTeam));
        Assert.That(layout[GameLoop.HostTeamIndex][0], Is.EqualTo(SandboxSession.PlayerSpawn));
        Assert.That(layout[GameLoop.OpponentTeamIndex][0], Is.EqualTo(SandboxSession.EnemySpawn));
    }

    [Test]
    public void EveryExtraSpawnCellIsLegalAndDistinct()
    {
        // The crowd cells are hardcoded, so nothing but a test stops one from being placed inside a
        // wall or on top of another unit's cell after a map edit.
        List<Vector2Int> all = new() { SandboxSession.PlayerSpawn };
        all.AddRange(SandboxSession.EnemySpawns);
        all.AddRange(SandboxSession.AllySpawns);

        foreach (Vector2Int cell in all)
        {
            Assert.That(GridSystem.IsCellInBounds(cell), Is.True, $"{cell} is off the board.");
            Assert.That(GameLoop.wallLayout, Has.No.Member(cell), $"{cell} is inside a wall.");
        }
        Assert.That(all, Is.Unique, "Two units would be spawned on the same cell.");
    }

    [Test]
    public void EveryCrowdCellIsCloseEnoughForARadiusAbilityToReachIt()
    {
        // The whole point of fielding a crowd is to exercise an ability that reaches several units
        // at once, so every extra body has to sit inside a typical ability radius (3 cells) of the
        // character under test. A cell further out would make a working ability look broken.
        const float TypicalAbilityRadiusCells = 3f;
        List<Vector2Int> crowd = new();
        crowd.AddRange(SandboxSession.EnemySpawns);
        crowd.AddRange(SandboxSession.AllySpawns);

        foreach (Vector2Int cell in crowd)
        {
            float distance = Vector2.Distance(cell, SandboxSession.PlayerSpawn);
            Assert.That(
                distance,
                Is.LessThanOrEqualTo(TypicalAbilityRadiusCells + 0.001f),
                $"{cell} is {distance:0.00} cells away — outside a 3-cell ability radius."
            );
        }
    }

    [Test]
    public void CreateSpawnLayout_SizesEachSideIndependently()
    {
        SandboxSession.Begin(
            0,
            CharacterSandbox.DefaultDummyUnitIndex,
            enemyCount: SandboxSession.MaxEnemyCount,
            allyCount: 3
        );

        var layout = SandboxSession.CreateSpawnLayout();

        Assert.That(
            layout[GameLoop.OpponentTeamIndex].Length,
            Is.EqualTo(SandboxSession.MaxEnemyCount)
        );
        Assert.That(layout[GameLoop.HostTeamIndex].Length, Is.EqualTo(3));
        // The character under test always stands on its own documented cell, whoever joins it.
        Assert.That(layout[GameLoop.HostTeamIndex][0], Is.EqualTo(SandboxSession.PlayerSpawn));
        Assert.That(
            GameLoop.UnitsForTeamThisMatch(GameLoop.OpponentTeamIndex),
            Is.EqualTo(SandboxSession.MaxEnemyCount)
        );
        Assert.That(GameLoop.UnitsForTeamThisMatch(GameLoop.HostTeamIndex), Is.EqualTo(3));
    }

    [Test]
    public void CrowdCountsAreClampedAndResetWhenTheSessionEnds()
    {
        SandboxSession.Begin(0, CharacterSandbox.DefaultDummyUnitIndex, enemyCount: 99, allyCount: 99);
        Assert.That(SandboxSession.EnemyCount, Is.EqualTo(SandboxSession.MaxEnemyCount));
        Assert.That(SandboxSession.AllyCount, Is.EqualTo(SandboxSession.MaxAllyCount));

        SandboxSession.Begin(0, CharacterSandbox.DefaultDummyUnitIndex, enemyCount: 0, allyCount: -5);
        Assert.That(SandboxSession.EnemyCount, Is.EqualTo(1));
        Assert.That(SandboxSession.AllyCount, Is.EqualTo(1));

        SandboxSession.Begin(0, CharacterSandbox.DefaultDummyUnitIndex, enemyCount: 4, allyCount: 2);
        SandboxSession.End();
        // A leaked crowd size would field the wrong board in every later sandbox launch.
        Assert.That(SandboxSession.EnemyCount, Is.EqualTo(1));
        Assert.That(SandboxSession.AllyCount, Is.EqualTo(1));
    }

    [Test]
    public void BuildMatchOptions_IsASingleClientBotMatchWithFogOffOnConcourse()
    {
        MatchOptions options = SandboxSession.BuildMatchOptions();

        Assert.That(options.IsBotMatch, Is.True, "The sandbox is a solo loopback host.");
        Assert.That(options.fogOfWar, Is.False, "The dummy must stay visible at all times.");
        Assert.That(options.mapId, Is.EqualTo(MapId.Concourse));
        Assert.That(options.gameMode, Is.EqualTo(GameMode.Elimination));
    }

    [Test]
    public void BuildRosters_PutTheChosenUnitsInTheOnlyFieldedSlot()
    {
        UnitDatabase catalog = LoadCatalog();
        const int testUnitIndex = 5;
        const int dummyUnitIndex = CharacterSandbox.DefaultDummyUnitIndex;

        SandboxSession.Begin(testUnitIndex, dummyUnitIndex);

        int[] hostRoster = SandboxSession.BuildHostRoster();
        int[] opponentRoster = SandboxSession.BuildOpponentRoster();

        // Slot 0 is the only slot spawned (UnitsPerTeam == 1), but the roster stays full length so
        // GameLoop.ConfigureTeam's validation is untouched.
        Assert.That(hostRoster[0], Is.EqualTo(testUnitIndex));
        Assert.That(opponentRoster[0], Is.EqualTo(dummyUnitIndex));
        Assert.That(hostRoster.Length, Is.EqualTo(RosterRules.UnitsPerPlayer));
        Assert.That(opponentRoster.Length, Is.EqualTo(RosterRules.UnitsPerPlayer));
        Assert.That(RosterRules.Validate(hostRoster, catalog.units).IsValid, Is.True);
        Assert.That(RosterRules.Validate(opponentRoster, catalog.units).IsValid, Is.True);
    }

    [Test]
    public void DefaultDummyUnitIndex_IsARealRosterEligibleCatalogEntry()
    {
        UnitDatabase catalog = LoadCatalog();

        Assert.That(
            RosterRules.IsUnitEligible(catalog.units, CharacterSandbox.DefaultDummyUnitIndex),
            Is.True
        );
    }

    [Test]
    public void EveryCatalogEntry_CanBeChosenAsTheCharacterUnderTest()
    {
        // The picker window offers the whole catalog, so every eligible entry must survive roster
        // validation as a sandbox host crew — otherwise a character would be listed but unlaunchable.
        UnitDatabase catalog = LoadCatalog();

        for (int unitIndex = 0; unitIndex < catalog.units.Count; unitIndex++)
        {
            if (!RosterRules.IsUnitEligible(catalog.units, unitIndex))
                continue;

            SandboxSession.Begin(unitIndex, CharacterSandbox.DefaultDummyUnitIndex);
            int[] roster = SandboxSession.BuildHostRoster();
            Assert.That(
                RosterRules.Validate(roster, catalog.units).IsValid,
                Is.True,
                $"Catalog index {unitIndex} ({catalog.units[unitIndex]?.unitName}) is offered by the "
                    + "sandbox window but cannot form a valid roster."
            );
            Assert.That(roster[0], Is.EqualTo(unitIndex));
        }
    }

    [Test]
    public void TutorialSession_TakesPrecedenceOverASandboxSession()
    {
        // Both are one-a-side sandboxes reading the same seams. They should never overlap, but if
        // they do the tutorial must win, because a student's match is the one with a script driving
        // it that would break.
        SandboxSession.Begin(0, CharacterSandbox.DefaultDummyUnitIndex);
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
        GameObject probe = new("CharacterSandboxTests_FrozenProbe");
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
