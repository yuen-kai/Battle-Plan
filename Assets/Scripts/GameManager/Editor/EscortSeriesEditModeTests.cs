using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// Pure/serialized coverage for Escort the President: leg rules, series arithmetic, board geometry
// and the president's serialized contract. The server-authoritative half (the round hook, the leg
// intermission, the scene rebuild between legs) needs a spawned GameLoop and is not exercised here.
[TestFixture]
[Category("Escort")]
public class EscortSeriesEditModeTests
{
    private const string CatalogPath = "Assets/UnitStats/AllUnits.asset";
    private const string PresidentDataPath = "Assets/UnitStats/President.asset";
    private const string PresidentPrefabPath = "Assets/Prefabs/Units/President.prefab";

    private static EscortStanding Escorting(
        int teamIndex,
        bool extracted = false,
        bool presidentDown = false,
        bool opposingCrewWiped = false,
        int stepsRemaining = 5
    )
    {
        return new EscortStanding(
            teamIndex,
            extracted,
            presidentDown,
            opposingCrewWiped,
            stepsRemaining
        );
    }

    [SetUp]
    public void UseTheDefaultBoard()
    {
        MapCatalog.SetActive(MapId.Concourse);
    }

    // === WHO ESCORTS ===

    [Test]
    public void PresidentTakesTheMiddleCrewSlot()
    {
        Assert.That(EscortSeries.PresidentRosterSlot, Is.EqualTo(RosterRules.UnitsPerPlayer / 2));
        Assert.That(EscortSeries.PresidentRosterSlot, Is.EqualTo(2));
    }

    [Test]
    public void EachCrewEscortsOnceThenBothDoInTheDecider()
    {
        Assert.That(EscortSeries.IsEscortingTeam(1, GameLoop.HostTeamIndex), Is.True);
        Assert.That(EscortSeries.IsEscortingTeam(1, GameLoop.OpponentTeamIndex), Is.False);
        Assert.That(EscortSeries.IsEscortingTeam(2, GameLoop.HostTeamIndex), Is.False);
        Assert.That(EscortSeries.IsEscortingTeam(2, GameLoop.OpponentTeamIndex), Is.True);
        Assert.That(EscortSeries.IsEscortingTeam(3, GameLoop.HostTeamIndex), Is.True);
        Assert.That(EscortSeries.IsEscortingTeam(3, GameLoop.OpponentTeamIndex), Is.True);
    }

    // === SERIES ARITHMETIC ===

    [Test]
    public void TwoLegsSettleASweepAndSplitRunsTheDecider()
    {
        Assert.That(
            EscortSeries.IsSeriesDecided(1, 0, 1, out int afterOne),
            Is.False,
            "A single leg cannot settle a best-of-three."
        );

        Assert.That(EscortSeries.IsSeriesDecided(2, 0, 2, out int sweep), Is.True);
        Assert.That(sweep, Is.EqualTo(GameLoop.HostTeamIndex));

        Assert.That(
            EscortSeries.IsSeriesDecided(1, 1, 2, out int split),
            Is.False,
            "1-1 has to open the decider rather than close the series."
        );
        Assert.That(split, Is.EqualTo(GameLoop.NoHillController));
    }

    [Test]
    public void DeciderClosesTheSeriesEvenWhenNobodyBanksIt()
    {
        Assert.That(EscortSeries.IsSeriesDecided(2, 1, 3, out int winner), Is.True);
        Assert.That(winner, Is.EqualTo(GameLoop.HostTeamIndex));

        Assert.That(EscortSeries.IsSeriesDecided(1, 1, 3, out int level), Is.True);
        Assert.That(level, Is.EqualTo(GameLoop.NoHillController));
    }

    // === LEG RULES: ONE PRESIDENT ===

    [Test]
    public void ExtractionWinsTheLegForTheEscortingCrew()
    {
        EscortLegResult result = EscortSeries.ResolveLeg(
            1,
            new List<EscortStanding> { Escorting(GameLoop.HostTeamIndex, extracted: true, stepsRemaining: 0) },
            5
        );
        Assert.That(result.Decided, Is.True);
        Assert.That(result.WinningTeamIndex, Is.EqualTo(GameLoop.HostTeamIndex));
        Assert.That(result.Reason, Is.EqualTo(EscortLegReason.Extracted));
    }

    [Test]
    public void ADeadPresidentHandsTheLegToTheDefence()
    {
        EscortLegResult result = EscortSeries.ResolveLeg(
            1,
            new List<EscortStanding> { Escorting(GameLoop.HostTeamIndex, presidentDown: true) },
            5
        );
        Assert.That(result.WinningTeamIndex, Is.EqualTo(GameLoop.OpponentTeamIndex));
        Assert.That(result.Reason, Is.EqualTo(EscortLegReason.PresidentDown));
    }

    [Test]
    public void AWipedDefenceEndsTheLegRatherThanBeingWalkedPast()
    {
        EscortLegResult result = EscortSeries.ResolveLeg(
            1,
            new List<EscortStanding> { Escorting(GameLoop.HostTeamIndex, opposingCrewWiped: true) },
            5
        );
        Assert.That(result.WinningTeamIndex, Is.EqualTo(GameLoop.HostTeamIndex));
        Assert.That(result.Reason, Is.EqualTo(EscortLegReason.DefenceEliminated));
    }

    [Test]
    public void AMutualWipeStillReadsAsAPresidentLost()
    {
        EscortLegResult result = EscortSeries.ResolveLeg(
            1,
            new List<EscortStanding>
            {
                Escorting(GameLoop.HostTeamIndex, presidentDown: true, opposingCrewWiped: true),
            },
            5
        );
        Assert.That(result.WinningTeamIndex, Is.EqualTo(GameLoop.OpponentTeamIndex));
        Assert.That(result.Reason, Is.EqualTo(EscortLegReason.PresidentDown));
    }

    [Test]
    public void TheClockIsWhatMakesTheEscortPush()
    {
        List<EscortStanding> quiet = new() { Escorting(GameLoop.HostTeamIndex, stepsRemaining: 4) };
        Assert.That(EscortSeries.ResolveLeg(1, quiet, 1).Decided, Is.False);

        EscortLegResult expired = EscortSeries.ResolveLeg(1, quiet, 0);
        Assert.That(expired.WinningTeamIndex, Is.EqualTo(GameLoop.OpponentTeamIndex));
        Assert.That(expired.Reason, Is.EqualTo(EscortLegReason.DefendersHeld));
    }

    [Test]
    public void LegTwoReadsTheOpponentAsTheEscort()
    {
        EscortLegResult result = EscortSeries.ResolveLeg(
            2,
            new List<EscortStanding> { Escorting(GameLoop.OpponentTeamIndex, stepsRemaining: 3) },
            0
        );
        Assert.That(result.WinningTeamIndex, Is.EqualTo(GameLoop.HostTeamIndex));
        Assert.That(result.Reason, Is.EqualTo(EscortLegReason.DefendersHeld));
    }

    // === LEG RULES: THE DECIDER ===

    [Test]
    public void DeciderRunsOnUntilAPresidentResolves()
    {
        EscortLegResult result = EscortSeries.ResolveLeg(
            3,
            new List<EscortStanding>
            {
                Escorting(GameLoop.HostTeamIndex, stepsRemaining: 4),
                Escorting(GameLoop.OpponentTeamIndex, stepsRemaining: 2),
            },
            5
        );
        Assert.That(result.Decided, Is.False);
    }

    [Test]
    public void DeciderIsSettledByGroundCoveredHoweverThePresidentsResolve()
    {
        EscortLegResult arrival = EscortSeries.ResolveLeg(
            3,
            new List<EscortStanding>
            {
                Escorting(GameLoop.HostTeamIndex, extracted: true, stepsRemaining: 0),
                Escorting(GameLoop.OpponentTeamIndex, presidentDown: true),
            },
            5
        );
        Assert.That(arrival.WinningTeamIndex, Is.EqualTo(GameLoop.HostTeamIndex));
        Assert.That(arrival.Reason, Is.EqualTo(EscortLegReason.Extracted));

        EscortLegResult survival = EscortSeries.ResolveLeg(
            3,
            new List<EscortStanding>
            {
                Escorting(GameLoop.HostTeamIndex, presidentDown: true),
                Escorting(GameLoop.OpponentTeamIndex, stepsRemaining: 6),
            },
            5
        );
        Assert.That(survival.WinningTeamIndex, Is.EqualTo(GameLoop.OpponentTeamIndex));
        Assert.That(survival.Reason, Is.EqualTo(EscortLegReason.PresidentDown));

        EscortLegResult onTheClock = EscortSeries.ResolveLeg(
            3,
            new List<EscortStanding>
            {
                Escorting(GameLoop.HostTeamIndex, stepsRemaining: 5),
                Escorting(GameLoop.OpponentTeamIndex, stepsRemaining: 1),
            },
            0
        );
        Assert.That(onTheClock.WinningTeamIndex, Is.EqualTo(GameLoop.OpponentTeamIndex));
        Assert.That(onTheClock.Reason, Is.EqualTo(EscortLegReason.GroundCovered));
    }

    [Test]
    public void OnlyAnExactMirrorEndsTheDeciderLevel()
    {
        foreach (
            List<EscortStanding> mirror in new[]
            {
                new List<EscortStanding>
                {
                    Escorting(GameLoop.HostTeamIndex, extracted: true, stepsRemaining: 0),
                    Escorting(GameLoop.OpponentTeamIndex, extracted: true, stepsRemaining: 0),
                },
                new List<EscortStanding>
                {
                    Escorting(GameLoop.HostTeamIndex, presidentDown: true),
                    Escorting(GameLoop.OpponentTeamIndex, presidentDown: true),
                },
            }
        )
        {
            EscortLegResult result = EscortSeries.ResolveLeg(3, mirror, 5);
            Assert.That(result.Decided, Is.True);
            Assert.That(result.HasWinner, Is.False);
            Assert.That(result.Reason, Is.EqualTo(EscortLegReason.GroundCovered));
        }
    }

    [Test]
    public void ALevelDeciderIsAValidDrawResult()
    {
        MatchResult draw = MatchResult.Draw(MatchResultReason.EscortStalemate);
        Assert.That(draw.IsValid, Is.True);
        Assert.That(draw.HasWinner, Is.False);

        MatchResult win = MatchResult.ForWinner(
            GameLoop.OpponentTeamIndex,
            MatchResultReason.EscortSeries
        );
        Assert.That(win.IsValid, Is.True);
        Assert.That(win.WinningTeamIndex, Is.EqualTo(GameLoop.OpponentTeamIndex));
    }

    // === BOARD GEOMETRY ===

    [Test]
    public void EachCrewRunsAtTheFarEdgeOfTheBoard()
    {
        HashSet<Vector2Int> hostZone = EscortSeries.ExtractionCellsFor(GameLoop.HostTeamIndex);
        HashSet<Vector2Int> opponentZone = EscortSeries.ExtractionCellsFor(
            GameLoop.OpponentTeamIndex
        );

        Assert.That(hostZone.Count, Is.EqualTo(EscortSeries.ExtractionZoneWidthCells));
        Assert.That(hostZone.All(cell => cell.y == GridSystem.RowCount - 1), Is.True);
        Assert.That(opponentZone.All(cell => cell.y == 0), Is.True);
        Assert.That(hostZone.Intersect(opponentZone).Any(), Is.False);
    }

    [Test]
    public void EveryBoardDeploysBothCrewsOnOpenGround()
    {
        foreach (MapDefinition map in MapCatalog.All)
        {
            MapCatalog.SetActive(map.Id);
            for (int leg = 1; leg <= EscortSeries.DeciderLeg; leg++)
            {
                List<Vector2Int[]> layout = EscortSeries.CreateSpawnLayout(leg);
                List<Vector2Int> everyone = layout.SelectMany(cells => cells).ToList();

                Assert.That(
                    everyone.Count,
                    Is.EqualTo(GameLoop.TeamCount * RosterRules.UnitsPerPlayer),
                    $"{map.DisplayName} leg {leg} fielded the wrong number of units."
                );
                Assert.That(
                    everyone.Distinct().Count(),
                    Is.EqualTo(everyone.Count),
                    $"{map.DisplayName} leg {leg} put two units on one cell."
                );
                Assert.That(
                    everyone.Any(cell => GameLoop.wallLayout.Contains(cell)),
                    Is.False,
                    $"{map.DisplayName} leg {leg} deployed a unit inside cover."
                );
                Assert.That(
                    everyone.All(GridSystem.IsCellInBounds),
                    Is.True,
                    $"{map.DisplayName} leg {leg} deployed a unit off the board."
                );
            }
        }
    }

    [Test]
    public void TheDefenceDeploysOffTheZoneItIsGuarding()
    {
        List<Vector2Int[]> layout = EscortSeries.CreateSpawnLayout(1);
        HashSet<Vector2Int> zone = EscortSeries.ExtractionCellsFor(GameLoop.HostTeamIndex);

        Assert.That(
            layout[GameLoop.HostTeamIndex].All(cell => cell.y == 0),
            Is.True,
            "The escorting crew starts on its own back rank."
        );
        Assert.That(
            layout[GameLoop.OpponentTeamIndex].Any(cell => zone.Contains(cell)),
            Is.False,
            "The defence must not begin standing on the objective."
        );
        Assert.That(
            layout[GameLoop.OpponentTeamIndex].All(cell => cell.y > 0 && cell.y < GridSystem.RowCount - 1),
            Is.True,
            "The defence holds mid-board."
        );
    }

    [Test]
    public void TheDeciderDeploysBothCrewsSymmetrically()
    {
        List<Vector2Int[]> layout = EscortSeries.CreateSpawnLayout(EscortSeries.DeciderLeg);
        Assert.That(layout[GameLoop.HostTeamIndex].All(cell => cell.y == 0), Is.True);
        Assert.That(
            layout[GameLoop.OpponentTeamIndex].All(cell => cell.y == GridSystem.RowCount - 1),
            Is.True
        );
    }

    [Test]
    public void ADeadPresidentIsInfinitelyFarFromHisZone()
    {
        EscortStanding down = Escorting(GameLoop.HostTeamIndex, presidentDown: true, stepsRemaining: 1);
        Assert.That(down.StepsRemaining, Is.EqualTo(int.MaxValue));
        Assert.That(down.Resolved, Is.True);

        int fromOwnRank = EscortSeries.StepsToExtraction(
            new Vector2Int(GridSystem.ColumnCount / 2, 0),
            GameLoop.HostTeamIndex
        );
        Assert.That(fromOwnRank, Is.EqualTo(GridSystem.RowCount - 1));
    }

    // === BOT ROLE TARGETING ===

    [Test]
    public void TheEscortCrewScreensAheadOfItsPresidentRatherThanBehindHim()
    {
        int team = GameLoop.HostTeamIndex;
        foreach (int row in new[] { 0, 2, 4, 6 })
        {
            Vector2Int president = new(GridSystem.ColumnCount / 2, row);
            int presidentSteps = EscortSeries.StepsToExtraction(president, team);
            List<Vector2Int> screen = BotPlayer.BuildEscortScreenTargets(president, team);

            Assert.That(screen, Is.Not.Empty);
            Assert.That(
                screen.Average(cell => EscortSeries.StepsToExtraction(cell, team)),
                Is.LessThan(presidentSteps),
                $"The screen has to sit between him at row {row} and the zone."
            );
        }
    }

    [Test]
    public void TheScreenConvergesOnTheZoneRatherThanRunningUpTheFlank()
    {
        int team = GameLoop.HostTeamIndex;
        Vector2Int flanking = new(1, 2);
        List<Vector2Int> screen = BotPlayer.BuildEscortScreenTargets(flanking, team);

        Assert.That(screen.Average(cell => cell.x), Is.GreaterThan(flanking.x));
        Assert.That(screen.Average(cell => cell.y), Is.GreaterThan(flanking.y));
    }

    [Test]
    public void TheDefenceOnlyEverTakesGroundBetweenThePresidentAndHisZone()
    {
        int team = GameLoop.HostTeamIndex;
        for (int row = 0; row < GridSystem.RowCount - 1; row++)
        {
            for (int column = 0; column < GridSystem.ColumnCount; column++)
            {
                Vector2Int president = new(column, row);
                if (GameLoop.wallLayout.Contains(president))
                    continue;

                int presidentSteps = EscortSeries.StepsToExtraction(president, team);
                List<Vector2Int> intercept = BotPlayer.BuildEscortInterceptTargets(president, team);

                Assert.That(intercept, Is.Not.Empty, $"No intercept ground for {president}.");
                Assert.That(
                    intercept.All(cell =>
                        EscortSeries.StepsToExtraction(cell, team) <= presidentSteps
                    ),
                    Is.True,
                    $"Intercept ground for a president at {president} reached behind him."
                );
            }
        }
    }

    [Test]
    public void TheDefenceGoesStraightAtAPresidentAlreadyOnTheZone()
    {
        int team = GameLoop.HostTeamIndex;
        Vector2Int onTheZone = EscortSeries
            .ExtractionCellsFor(team)
            .OrderBy(cell => cell.x)
            .First();

        List<Vector2Int> intercept = BotPlayer.BuildEscortInterceptTargets(onTheZone, team);
        Assert.That(intercept, Does.Contain(onTheZone));
    }

    [Test]
    public void NeitherRoleEverNamesCoverOrGroundOffTheBoard()
    {
        foreach (MapDefinition map in MapCatalog.All)
        {
            MapCatalog.SetActive(map.Id);
            for (int team = 0; team < GameLoop.TeamCount; team++)
            {
                foreach (int row in new[] { 0, 4, GridSystem.RowCount - 1 })
                {
                    Vector2Int president = new(GridSystem.ColumnCount / 2, row);
                    foreach (
                        List<Vector2Int> role in new[]
                        {
                            BotPlayer.BuildEscortScreenTargets(president, team),
                            BotPlayer.BuildEscortInterceptTargets(president, team),
                        }
                    )
                    {
                        Assert.That(role.All(GridSystem.IsCellInBounds), Is.True, map.DisplayName);
                        Assert.That(
                            role.Any(cell => GameLoop.wallLayout.Contains(cell)),
                            Is.False,
                            $"{map.DisplayName} named a wall cell as somewhere to stand."
                        );
                    }
                }
            }
        }
    }

    [Test]
    public void NoStayPutRulePinsAnEscortDefenceToItsZone()
    {
        Vector2Int onZone = EscortSeries
            .ExtractionCellsFor(GameLoop.HostTeamIndex)
            .OrderBy(cell => cell.x)
            .First();
        Vector2Int offZone = new(onZone.x, onZone.y - 3);

        Assert.That(
            BotPlayer.WouldAbandonHill(GameMode.EscortThePresident, onZone, offZone),
            Is.False
        );
        Assert.That(
            BotPlayer.WouldAbandonHill(
                GameMode.KingOfTheHill,
                GameLoop.KingOfTheHillCells.OrderBy(cell => cell.x).First(),
                new Vector2Int(0, 0)
            ),
            Is.True,
            "King of the Hill still holds its pad."
        );
    }

    // === THE PRESIDENT'S SERIALIZED CONTRACT ===

    [Test]
    public void ThePresidentIsInTheCatalogueButNeverPickable()
    {
        UnitDatabase catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<UnitDatabase>(CatalogPath);
        UnitData president = UnityEditor.AssetDatabase.LoadAssetAtPath<UnitData>(PresidentDataPath);

        Assert.That(catalog, Is.Not.Null);
        Assert.That(president, Is.Not.Null);
        Assert.That(
            catalog.units.Contains(president),
            Is.True,
            "GameLoop substitutes him by catalogue index, so he has to be in the catalogue."
        );
        Assert.That(
            president.IsRosterEligible,
            Is.False,
            "He is substituted into the middle slot, never chosen, so roster validation must reject him."
        );
    }

    [Test]
    public void ThePresidentCarriesTheRecallAndNothingThatOpensADodgeWindow()
    {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            PresidentPrefabPath
        );
        UnitData president = UnityEditor.AssetDatabase.LoadAssetAtPath<UnitData>(PresidentDataPath);

        Assert.That(prefab, Is.Not.Null);
        Assert.That(
            prefab.GetComponent<PresidentialRecall>(),
            Is.Not.Null,
            "GameLoop.GetPresident finds him by this component, not by his crew slot."
        );
        Assert.That(president.unitModel, Is.EqualTo(prefab));

        Assert.That(president.selectAbilitySquare, Is.False);
        Assert.That(president.responseRange, Is.EqualTo(0f));
        Assert.That(president.abilityCooldownRounds, Is.GreaterThan(1));
    }

    [Test]
    public void ThePresidentIsTheFragileUnitTheModeNeedsHimToBe()
    {
        UnitDatabase catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<UnitDatabase>(CatalogPath);
        UnitData president = UnityEditor.AssetDatabase.LoadAssetAtPath<UnitData>(PresidentDataPath);

        float weakestCombatant = catalog
            .units.Where(unit => unit != null && unit != president)
            .Min(unit => unit.maxHealth);
        Assert.That(
            president.maxHealth,
            Is.LessThan(weakestCombatant),
            "Killing him has to be a real win condition, so he cannot out-live the crew guarding him."
        );

        float weakestShot = catalog
            .units.Where(unit => unit != null && unit != president)
            .Min(unit => unit.damage);
        Assert.That(
            president.damage,
            Is.LessThanOrEqualTo(weakestShot),
            "He is armed so he is not a passenger, not so he can trade."
        );

        float[] otherRanges = catalog
            .units.Where(unit => unit != null && unit != president)
            .Select(unit => unit.targetRange)
            .OrderBy(range => range)
            .ToArray();
        Assert.That(
            president.targetRange,
            Is.LessThan(otherRanges[otherRanges.Length / 2]),
            "His reach has to sit in the near half of the roster."
        );

        Assert.That(president.visionRange, Is.GreaterThanOrEqualTo(president.targetRange));
    }

    // === ORDER CANCELLATION ===

    [Test]
    public void TheRecallIsTheOnlyAbilityThatCancelsAlliedOrders()
    {
        UnitDatabase catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<UnitDatabase>(CatalogPath);
        List<string> cancellers = new();
        foreach (UnitData unit in catalog.units)
        {
            Ability ability = unit?.unitModel?.GetComponent<Ability>();
            if (ability != null && ability.CancelsAlliedOrders)
                cancellers.Add(unit.unitName);
        }

        Assert.That(cancellers, Is.EquivalentTo(new[] { "The President" }));
    }

    [Test]
    public void AChargeComesBackOnlyWhenOneWasActuallySpent()
    {
        int spent = 3;
        Assert.That(Unit.RefundAbilityCooldown(ref spent), Is.True);
        Assert.That(spent, Is.EqualTo(0), "A refund clears the cooldown rather than ticking it.");

        int ready = 0;
        Assert.That(
            Unit.RefundAbilityCooldown(ref ready),
            Is.False,
            "A unit already off cooldown has nothing to be handed back."
        );
        Assert.That(ready, Is.EqualTo(0));

        int fresh = 0;
        Assert.That(
            Unit.TryStartAbilityCooldown(
                ref fresh,
                Unit.GetConfiguredAbilityCooldownRounds(
                    UnityEditor.AssetDatabase.LoadAssetAtPath<UnitData>(PresidentDataPath),
                    true
                ),
                true
            ),
            Is.True
        );
        Assert.That(fresh, Is.GreaterThan(0));
        Assert.That(Unit.RefundAbilityCooldown(ref fresh), Is.True);
        Assert.That(fresh, Is.EqualTo(0));
    }

    [Test]
    public void TheRallyAnimationFitsInsideTheRoundItIsCastIn()
    {
        // The recall holds the round open for as long as it runs, so the rally stays inside the
        // range the existing abilities already hold for (Shield's 4.5s is the longest).
        Assert.That(PresidentialRecall.BeckonSeconds, Is.GreaterThan(0f));
        Assert.That(
            PresidentialRecall.MaxRecallSeconds(RosterRules.UnitsPerPlayer - 1),
            Is.LessThan(2.5f)
        );

        // Every ally after the first sets off a stagger later, so the crew converges rather than
        // snapping in as one block.
        Assert.That(PresidentialRecall.LeapStaggerSeconds, Is.GreaterThan(0f));
        Assert.That(
            PresidentialRecall.MaxRecallSeconds(4),
            Is.GreaterThan(PresidentialRecall.MaxRecallSeconds(1))
        );

        // Travel is priced in cells per second, so nobody crosses the board faster than the fastest
        // thing the game already moves at (a dodge dive).
        Assert.That(PresidentialRecall.MinLeapSeconds, Is.GreaterThan(0f));
        Assert.That(
            PresidentialRecall.MaxLeapSeconds,
            Is.GreaterThan(PresidentialRecall.MinLeapSeconds)
        );
        float longestLeapCells = PresidentialRecall.MaxLeapSeconds
            * PresidentialRecall.LeapCellsPerSecond;
        Assert.That(longestLeapCells, Is.GreaterThanOrEqualTo(GridSystem.RowCount / 2f));
    }

    [Test]
    public void TheGuardOrbHugsItsUnitAndCarriesNoCollider()
    {
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            PresidentPrefabPath
        );
        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        try
        {
            instance.transform.position = Vector3.zero;
            GuardOrbVisual orb = GuardOrbVisual.Attach(instance);
            orb.SetGuarded(true);

            Transform shell = instance.transform.Find(GuardOrbVisual.GameObjectName);
            Assert.That(shell, Is.Not.Null, "A guarded unit has to carry a shell.");

            Collider shellCollider = shell.GetComponent<Collider>();
            Assert.That(
                shellCollider == null || !shellCollider.enabled,
                Is.True,
                "An indicator that bullets and targeting casts can hit is not an indicator."
            );

            Bounds body = MeasureCharacter(instance);
            Bounds settled = SettleShell(orb, shell);
            Assert.That(
                settled.size.x,
                Is.LessThan(GameLoop.cellSize),
                "The shell has to stay inside its own cell or a huddle turns into one blob."
            );
            Assert.That(settled.Contains(body.min) && settled.Contains(body.max), Is.True);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    private static Bounds MeasureCharacter(GameObject unit)
    {
        Bounds bounds = default;
        bool measured = false;
        foreach (Renderer candidate in unit.GetComponentsInChildren<Renderer>(true))
        {
            if (!DiveRecoveryPulse.IsCharacterRenderer(candidate))
                continue;
            if (!measured)
            {
                bounds = candidate.bounds;
                measured = true;
                continue;
            }
            bounds.Encapsulate(candidate.bounds);
        }
        return bounds;
    }

    /// <summary>Runs the bloom to full, which edit mode has no Update to do.</summary>
    private static Bounds SettleShell(GuardOrbVisual orb, Transform shell)
    {
        const System.Reflection.BindingFlags Hidden =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        typeof(GuardOrbVisual).GetField("bloom", Hidden).SetValue(orb, 1f);
        typeof(GuardOrbVisual).GetMethod("Draw", Hidden).Invoke(orb, null);
        return shell.GetComponent<Renderer>().bounds;
    }

    [Test]
    public void TheRecallHalvesIncomingDamageAndClosesToOneRing()
    {
        Assert.That(PresidentialRecall.DamageTakenMultiplier, Is.EqualTo(0.5f));
        Assert.That(PresidentialRecall.RecallRingRadiusCells, Is.EqualTo(1));

        Assert.That(
            GridSystem.GetSquareFootprint(
                new Vector2Int(7, 5),
                PresidentialRecall.RecallRingRadiusCells
            ).Count - 1,
            Is.GreaterThanOrEqualTo(RosterRules.UnitsPerPlayer - 1)
        );
    }
}
