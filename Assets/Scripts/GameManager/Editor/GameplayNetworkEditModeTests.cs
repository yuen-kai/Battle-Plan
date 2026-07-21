using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[TestFixture]
[Category("GameplayNetwork")]
public class GameplayNetworkEditModeTests
{
    [SetUp]
    public void SetUp()
    {
        MatchOptions.Reset();
        GameLoop.ResetMatchState();
    }

    [TearDown]
    public void TearDown()
    {
        GameLoop.ResetMatchState();
        MatchOptions.Reset();
    }

    [Test]
    public void MatchOptions_DefaultsAndUnsupportedModesAreSanitized()
    {
        MatchOptions defaults = MatchOptions.Default;
        Assert.That(defaults.gameMode, Is.EqualTo(GameMode.Elimination));
        Assert.That(defaults.opponentType, Is.EqualTo(OpponentType.Player));
        Assert.That(defaults.fogOfWar, Is.True);

        MatchOptions kingOfTheHill = defaults;
        kingOfTheHill.gameMode = GameMode.KingOfTheHill;
        MatchOptions sanitizedKingOfTheHill = kingOfTheHill.Sanitized();
        Assert.That(sanitizedKingOfTheHill.gameMode, Is.EqualTo(GameMode.KingOfTheHill));
        Assert.That(sanitizedKingOfTheHill.IsKingOfTheHill, Is.True);
        Assert.That(sanitizedKingOfTheHill.GameModeDisplayName, Is.EqualTo("King of the Hill"));

        MatchOptions unsupported = defaults;
        unsupported.gameMode = GameMode.CaptureTheFlag;
        Assert.That(unsupported.Sanitized().gameMode, Is.EqualTo(GameMode.Elimination));

        MatchOptions invalid = new()
        {
            gameMode = (GameMode)byte.MaxValue,
            opponentType = (OpponentType)byte.MaxValue,
            fogOfWar = false,
        };
        MatchOptions sanitized = invalid.Sanitized();
        Assert.That(sanitized.gameMode, Is.EqualTo(GameMode.Elimination));
        Assert.That(sanitized.opponentType, Is.EqualTo(OpponentType.Player));
        Assert.That(sanitized.fogOfWar, Is.False);

        MatchOptions.SetCurrent(invalid);
        Assert.That(MatchOptions.Current, Is.EqualTo(sanitized));
        MatchOptions.Reset();
        Assert.That(MatchOptions.Current, Is.EqualTo(MatchOptions.Default));
    }

    [Test]
    public void MatchOptions_NetworkRoundTripPreservesSupportedValuesAndSanitizesInput()
    {
        MatchOptions written = new()
        {
            gameMode = GameMode.KingOfTheHill,
            opponentType = OpponentType.AI,
            fogOfWar = false,
        };

        using FastBufferWriter writer = new(16, Allocator.Temp);
        writer.WriteNetworkSerializable(written);

        using FastBufferReader reader = new(writer, Allocator.Temp);
        reader.ReadNetworkSerializable(out MatchOptions roundTripped);

        Assert.That(roundTripped.gameMode, Is.EqualTo(GameMode.KingOfTheHill));
        Assert.That(roundTripped.opponentType, Is.EqualTo(OpponentType.AI));
        Assert.That(roundTripped.fogOfWar, Is.False);

        MatchOptions invalid = written;
        invalid.gameMode = (GameMode)99;
        using FastBufferWriter invalidWriter = new(16, Allocator.Temp);
        invalidWriter.WriteNetworkSerializable(invalid);
        using FastBufferReader invalidReader = new(invalidWriter, Allocator.Temp);
        invalidReader.ReadNetworkSerializable(out MatchOptions sanitizedRoundTrip);
        Assert.That(sanitizedRoundTrip.gameMode, Is.EqualTo(GameMode.Elimination));
    }

    [Test]
    public void ConnectionApproval_EnforcesModeCapacityAndClosesAfterLobby()
    {
        MatchOptions playerMatch = MatchOptions.Default;
        MatchOptions botMatch = playerMatch;
        botMatch.opponentType = OpponentType.AI;

        Assert.That(NetworkHandler.RequiredClientCount(playerMatch), Is.EqualTo(2));
        Assert.That(NetworkHandler.RequiredClientCount(botMatch), Is.EqualTo(1));
        Assert.That(
            NetworkHandler.CanApproveConnection(true, false, false, int.MaxValue, 1),
            Is.True,
            "The host's local client must always be admitted."
        );
        Assert.That(NetworkHandler.CanApproveConnection(false, true, true, 2, 2), Is.True);
        Assert.That(
            NetworkHandler.CanApproveConnection(false, true, true, 2, 1),
            Is.False,
            "AI matches may not admit a second human."
        );
        Assert.That(
            NetworkHandler.CanApproveConnection(false, false, true, 2, 2),
            Is.False,
            "The gate closes once scene progression begins."
        );
        Assert.That(
            NetworkHandler.CanApproveConnection(false, true, false, 2, 2),
            Is.False,
            "Late joins outside the JoinGame scene are rejected."
        );
        Assert.That(
            NetworkHandler.CanApproveConnection(false, true, true, 3, 2),
            Is.False,
            "Pending connections count against capacity."
        );

        Assert.That(GameLoop.IsAuthorizedGameplayObserver(0, 0, new[] { 0UL, 7UL }), Is.True);
        Assert.That(GameLoop.IsAuthorizedGameplayObserver(7, 0, new[] { 0UL, 7UL }), Is.True);
        Assert.That(
            GameLoop.IsAuthorizedGameplayObserver(9, 0, new[] { 0UL, 7UL }),
            Is.False,
            "Unassigned clients must not receive unit spawn state."
        );
        Assert.That(
            GameLoop.IsAuthorizedGameplayObserver(
                GameLoop.BotParticipantId,
                0,
                new[] { 0UL, GameLoop.BotParticipantId }
            ),
            Is.False,
            "The bot sentinel is never an NGO observer."
        );
    }

    [Test]
    public void ExitRpc_DerivesIdentityFromServerRpcParams()
    {
        MethodInfo exitRpc = typeof(GameLoop).GetMethod(
            "ExitServerRpc",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        Assert.That(exitRpc, Is.Not.Null);
        ParameterInfo[] parameters = exitRpc.GetParameters();
        Assert.That(parameters, Has.Length.EqualTo(1));
        Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(ServerRpcParams)));
    }

    [Test]
    public void AbilityCharges_ClampAndCannotBeConsumedPastZero()
    {
        UnitData data = ScriptableObject.CreateInstance<UnitData>();
        try
        {
            data.uses = -3;
            Assert.That(Unit.GetInitialAbilityUses(data, true), Is.Zero);
            data.uses = 2;
            Assert.That(Unit.GetInitialAbilityUses(data, false), Is.Zero);

            int remaining = Unit.GetInitialAbilityUses(data, true);
            Assert.That(remaining, Is.EqualTo(2));
            Assert.That(Unit.TryConsumeAbilityCharge(ref remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(1));
            Assert.That(Unit.TryConsumeAbilityCharge(ref remaining), Is.True);
            Assert.That(remaining, Is.Zero);
            Assert.That(Unit.TryConsumeAbilityCharge(ref remaining), Is.False);
            Assert.That(remaining, Is.Zero);
        }
        finally
        {
            Object.DestroyImmediate(data);
        }
    }

    [Test]
    public void DodgeValidation_RejectsAbilityFlaggedAndIncompletePlans()
    {
        List<Vector3> movementPath = new() { Vector3.zero, Vector3.right };

        Assert.That(GameLoop.IsValidDodgeMovementPlan(false, movementPath), Is.True);
        Assert.That(
            GameLoop.IsValidDodgeMovementPlan(true, movementPath),
            Is.False,
            "Ability target validation must never be reused as dodge movement validation."
        );
        Assert.That(GameLoop.IsValidDodgeMovementPlan(false, null), Is.False);
        Assert.That(GameLoop.IsValidDodgeMovementPlan(false, new[] { Vector3.zero }), Is.False);
    }

    [Test]
    public void DodgeAlerts_CanBeTargetedWithoutBroadcastingHiddenUnitReferences()
    {
        MethodInfo setActiveRpc = typeof(NetworkHelper).GetMethod(
            nameof(NetworkHelper.SetActiveClientRpc)
        );

        Assert.That(setActiveRpc, Is.Not.Null);
        ParameterInfo[] parameters = setActiveRpc.GetParameters();
        Assert.That(parameters, Has.Length.EqualTo(4));
        Assert.That(parameters[^1].ParameterType, Is.EqualTo(typeof(ClientRpcParams)));
        Assert.That(parameters[^1].HasDefaultValue, Is.True);
    }

    [Test]
    public void TeamMapping_SeparatesLogicalBotTeamFromHumanClient()
    {
        const ulong hostClientId = 17;
        GameLoop.ConfigureTeam(GameLoop.HostTeamIndex, hostClientId, new[] { 0, 1, 2 });
        GameLoop.ConfigureTeam(
            GameLoop.OpponentTeamIndex,
            GameLoop.BotParticipantId,
            GameLoop.DefaultBotRoster
        );

        Assert.That(
            GameLoop.TryGetConfiguredParticipantId(GameLoop.HostTeamIndex, out ulong mappedHost),
            Is.True
        );
        Assert.That(mappedHost, Is.EqualTo(hostClientId));
        Assert.That(
            GameLoop.TryGetConfiguredParticipantId(GameLoop.OpponentTeamIndex, out ulong mappedBot),
            Is.True
        );
        Assert.That(mappedBot, Is.EqualTo(GameLoop.BotParticipantId));
        Assert.That(GameLoop.IsBotParticipant(mappedBot), Is.True);
        Assert.That(GameLoop.IsBotParticipant(mappedHost), Is.False);
    }

    [Test]
    public void DirectionalAbilityTargeting_AcceptsEightAdjacentCellsOnly()
    {
        Vector2Int start = new(4, 4);
        Vector2Int[] expectedDirections =
        {
            new(-1, -1),
            Vector2Int.down,
            new(1, -1),
            Vector2Int.left,
            Vector2Int.right,
            new(-1, 1),
            Vector2Int.up,
            new(1, 1),
        };

        foreach (Vector2Int expectedDirection in expectedDirections)
        {
            Assert.That(
                GridSystem.TryGetAdjacentDirection(
                    start,
                    start + expectedDirection,
                    out Vector2Int actualDirection
                ),
                Is.True,
                $"Direction {expectedDirection} should be selectable."
            );
            Assert.That(actualDirection, Is.EqualTo(expectedDirection));
        }
        Assert.That(
            GridSystem.TryGetAdjacentDirection(start, start, out _),
            Is.False,
            "The center cell is not a rush direction."
        );
        Assert.That(
            GridSystem.TryGetAdjacentDirection(start, new Vector2Int(6, 4), out _),
            Is.False,
            "Direction selection is limited to the surrounding 3x3 cells."
        );
    }

    [Test]
    public void DirectionalDestination_UsesFixedLengthAndStopsBeforeObstacles()
    {
        Vector2Int start = new(4, 4);
        Vector2Int[] directions =
        {
            new(-1, -1),
            Vector2Int.down,
            new(1, -1),
            Vector2Int.left,
            Vector2Int.right,
            new(-1, 1),
            Vector2Int.up,
            new(1, 1),
        };

        foreach (Vector2Int direction in directions)
        {
            Assert.That(
                GridSystem.GetDirectionalDestination(
                    start,
                    direction,
                    3,
                    new HashSet<Vector2Int>()
                ),
                Is.EqualTo(start + direction * 3),
                $"Open-board direction {direction} should travel exactly three cells."
            );
        }
        Assert.That(
            GridSystem.GetDirectionalDestination(
                start,
                Vector2Int.right,
                3,
                new HashSet<Vector2Int> { new Vector2Int(6, 4) }
            ),
            Is.EqualTo(new Vector2Int(5, 4)),
            "A wall stops the rush on the last legal cell."
        );
        Assert.That(
            GridSystem.GetDirectionalDestination(
                new Vector2Int(13, 9),
                Vector2Int.right,
                3,
                new HashSet<Vector2Int>()
            ),
            Is.EqualTo(new Vector2Int(14, 9)),
            "The board edge shortens the rush."
        );
        Assert.That(
            GridSystem.GetDirectionalDestination(
                start,
                new Vector2Int(1, 1),
                3,
                new HashSet<Vector2Int> { new Vector2Int(5, 4) }
            ),
            Is.EqualTo(start),
            "A diagonal rush cannot cut through a wall corner."
        );
    }

    [Test]
    public void GridBfs_IsDeterministicAdjacentAndAvoidsBlockedCells()
    {
        Vector2Int start = new(0, 0);
        Vector2Int goal = new(3, 0);
        HashSet<Vector2Int> blocked = new() { new Vector2Int(1, 0) };

        List<Vector2Int> path = GridSystem.FindPath(start, goal, blocked);

        Assert.That(path, Is.Not.Null);
        Assert.That(path.First(), Is.EqualTo(start));
        Assert.That(path.Last(), Is.EqualTo(goal));
        Assert.That(path.Contains(new Vector2Int(1, 0)), Is.False);
        Assert.That(path.Any(GameLoop.wallLayout.Contains), Is.False);
        for (int index = 1; index < path.Count; index++)
        {
            int distance =
                Mathf.Abs(path[index].x - path[index - 1].x)
                + Mathf.Abs(path[index].y - path[index - 1].y);
            Assert.That(distance, Is.EqualTo(1));
        }

        CollectionAssert.AreEqual(path, GridSystem.FindPath(start, goal, blocked));
    }

    [Test]
    public void ExpandedBoardLayout_IsSymmetricConnectedAndUsesFullWidth()
    {
        Assert.That(GridSystem.ColumnCount, Is.EqualTo(15));
        Assert.That(GridSystem.RowCount, Is.EqualTo(10));
        Assert.That(GameLoop.wallLayout.Count, Is.EqualTo(18));
        Assert.That(GridSystem.IsCellInBounds(new Vector2Int(14, 9)), Is.True);
        Assert.That(GridSystem.IsCellInBounds(new Vector2Int(15, 9)), Is.False);
        Assert.That(
            GameLoop.gridBounds.width,
            Is.EqualTo(14 * GameLoop.cellSize + 0.1f).Within(0.001f)
        );

        foreach (Vector2Int wall in GameLoop.wallLayout)
        {
            Assert.That(GridSystem.IsCellInBounds(wall), Is.True);
            Assert.That(
                GameLoop.wallLayout.Contains(
                    new Vector2Int(GridSystem.ColumnCount - 1 - wall.x, wall.y)
                ),
                Is.True,
                $"{wall} has no horizontal mirror."
            );
            Assert.That(
                GameLoop.wallLayout.Contains(
                    new Vector2Int(wall.x, GridSystem.RowCount - 1 - wall.y)
                ),
                Is.True,
                $"{wall} has no vertical mirror."
            );
        }

        List<Vector2Int> reachable = GridSystem.GetReachableCells(new Vector2Int(2, 0), 999);
        Assert.That(
            reachable.Count,
            Is.EqualTo(GridSystem.ColumnCount * GridSystem.RowCount - GameLoop.wallLayout.Count)
        );
        Assert.That(GridSystem.FindPath(new Vector2Int(2, 0), new Vector2Int(12, 9)), Is.Not.Null);
    }

    [Test]
    public void KingOfTheHillCells_AreCentralSymmetricAndTraversable()
    {
        HashSet<Vector2Int> expected = new()
        {
            new Vector2Int(6, 4),
            new Vector2Int(7, 4),
            new Vector2Int(8, 4),
            new Vector2Int(6, 5),
            new Vector2Int(7, 5),
            new Vector2Int(8, 5),
        };

        Assert.That(GridSystem.ColumnCount, Is.EqualTo(15));
        Assert.That(GridSystem.RowCount, Is.EqualTo(10));
        Assert.That(GameLoop.gridBounds.xMin, Is.EqualTo(0f));
        Assert.That(
            GameLoop.gridBounds.xMax,
            Is.EqualTo((GridSystem.ColumnCount - 1) * GameLoop.cellSize + 0.1f).Within(0.001f)
        );
        Assert.That(GameLoop.KingOfTheHillCells.Count, Is.EqualTo(6));
        CollectionAssert.AreEquivalent(expected, GameLoop.KingOfTheHillCells);
        foreach (Vector2Int cell in GameLoop.KingOfTheHillCells)
        {
            Assert.That(GridSystem.IsCellInBounds(cell), Is.True);
            Assert.That(GameLoop.wallLayout.Contains(cell), Is.False);
            Assert.That(
                GameLoop.KingOfTheHillCells.Contains(
                    new Vector2Int(
                        GridSystem.ColumnCount - 1 - cell.x,
                        GridSystem.RowCount - 1 - cell.y
                    )
                ),
                Is.True,
                $"{cell} has no rotationally symmetric hill cell."
            );
        }
    }

    [Test]
    public void HillControl_DetectsEmptySoleAndContestedOccupancy()
    {
        Assert.That(
            GameLoop.DetermineHillControl(null, null, out int emptyController),
            Is.EqualTo(HillControlStatus.Empty)
        );
        Assert.That(emptyController, Is.EqualTo(GameLoop.NoHillController));

        Assert.That(
            GameLoop.DetermineHillControl(
                new[] { new Vector2Int(7, 4) },
                new[] { new Vector2Int(0, 9) },
                out int hostController
            ),
            Is.EqualTo(HillControlStatus.Controlled)
        );
        Assert.That(hostController, Is.EqualTo(GameLoop.HostTeamIndex));

        Assert.That(
            GameLoop.DetermineHillControl(
                new[] { new Vector2Int(6, 5) },
                new[] { new Vector2Int(8, 4) },
                out int contestedController
            ),
            Is.EqualTo(HillControlStatus.Contested)
        );
        Assert.That(contestedController, Is.EqualTo(GameLoop.NoHillController));
    }

    [Test]
    public void HillControlStreak_RequiresThreeConsecutiveSoleControlRounds()
    {
        HillControlState state = HillControlState.Empty;
        state = GameLoop.AdvanceHillControlState(
            state,
            HillControlStatus.Controlled,
            GameLoop.HostTeamIndex
        );
        Assert.That(state.Streak, Is.EqualTo(1));
        state = GameLoop.AdvanceHillControlState(
            state,
            HillControlStatus.Controlled,
            GameLoop.HostTeamIndex
        );
        Assert.That(state.Streak, Is.EqualTo(2));

        state = GameLoop.AdvanceHillControlState(
            state,
            HillControlStatus.Empty,
            GameLoop.NoHillController
        );
        Assert.That(state.Status, Is.EqualTo(HillControlStatus.Empty));
        Assert.That(state.Streak, Is.Zero);

        state = GameLoop.AdvanceHillControlState(
            state,
            HillControlStatus.Controlled,
            GameLoop.HostTeamIndex
        );
        state = GameLoop.AdvanceHillControlState(
            state,
            HillControlStatus.Contested,
            GameLoop.NoHillController
        );
        Assert.That(state.Status, Is.EqualTo(HillControlStatus.Contested));
        Assert.That(state.Streak, Is.Zero);

        state = GameLoop.AdvanceHillControlState(
            state,
            HillControlStatus.Controlled,
            GameLoop.HostTeamIndex
        );
        state = GameLoop.AdvanceHillControlState(
            state,
            HillControlStatus.Controlled,
            GameLoop.OpponentTeamIndex
        );
        Assert.That(state.ControllingTeamIndex, Is.EqualTo(GameLoop.OpponentTeamIndex));
        Assert.That(state.Streak, Is.EqualTo(1), "A control swap starts a new streak.");

        state = GameLoop.AdvanceHillControlState(
            state,
            HillControlStatus.Controlled,
            GameLoop.OpponentTeamIndex
        );
        state = GameLoop.AdvanceHillControlState(
            state,
            HillControlStatus.Controlled,
            GameLoop.OpponentTeamIndex
        );
        Assert.That(state.Streak, Is.EqualTo(GameLoop.HillControlRoundsToWin));
    }

    [Test]
    public void KingOfTheHillRespawn_RequiresSurvivorsOnBothTeams()
    {
        Assert.That(
            GameLoop.ShouldRespawnEliminatedUnits(GameMode.KingOfTheHill, GameLoop.TeamCount),
            Is.True
        );
        Assert.That(
            GameLoop.ShouldRespawnEliminatedUnits(GameMode.KingOfTheHill, 1),
            Is.False,
            "A full-team wipe must still end the match before respawns."
        );
        Assert.That(
            GameLoop.ShouldRespawnEliminatedUnits(GameMode.Elimination, GameLoop.TeamCount),
            Is.False
        );
    }

    [Test]
    public void HillControlState_RoundTripsAndLivesOnAlwaysVisibleGameLoop()
    {
        HillControlState written = new(HillControlStatus.Controlled, GameLoop.OpponentTeamIndex, 2);
        using FastBufferWriter writer = new(32, Allocator.Temp);
        writer.WriteNetworkSerializable(written);
        using FastBufferReader reader = new(writer, Allocator.Temp);
        reader.ReadNetworkSerializable(out HillControlState roundTripped);
        Assert.That(roundTripped, Is.EqualTo(written));

        FieldInfo field = typeof(GameLoop).GetField(
            "replicatedHillControl",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null);
        Assert.That(field.FieldType, Is.EqualTo(typeof(NetworkVariable<HillControlState>)));
    }

    [Test]
    public void BotMovement_PrioritizesAndHoldsKingOfTheHillCells()
    {
        List<Vector2Int> targets = BotPlayer.GetStrategicTargets(
            GameMode.KingOfTheHill,
            new[] { new Vector2Int(12, 0) }
        );
        CollectionAssert.AreEqual(
            GameLoop.KingOfTheHillCells.OrderBy(cell => cell.x).ThenBy(cell => cell.y).ToList(),
            targets
        );

        Vector2Int start = new(7, 9);
        List<Vector2Int> approach = BotPlayer.BuildMovementPath(
            start,
            targets,
            new HashSet<Vector2Int>(),
            new HashSet<Vector2Int>(),
            3
        );
        int startDistance = targets.Min(cell =>
            Mathf.Abs(start.x - cell.x) + Mathf.Abs(start.y - cell.y)
        );
        int endDistance = targets.Min(cell =>
            Mathf.Abs(approach[^1].x - cell.x) + Mathf.Abs(approach[^1].y - cell.y)
        );
        Assert.That(endDistance, Is.LessThan(startDistance));

        Vector2Int controlledCell = new(7, 5);
        List<Vector2Int> hold = BotPlayer.BuildMovementPath(
            controlledCell,
            targets,
            new HashSet<Vector2Int>(),
            new HashSet<Vector2Int>(),
            3
        );
        CollectionAssert.AreEqual(new[] { controlledCell }, hold);
        Assert.That(
            BotPlayer.WouldAbandonHill(
                GameMode.KingOfTheHill,
                controlledCell,
                new Vector2Int(7, 8)
            ),
            Is.True
        );
        Assert.That(
            BotPlayer.WouldAbandonHill(
                GameMode.KingOfTheHill,
                controlledCell,
                new Vector2Int(6, 4)
            ),
            Is.False
        );
        Assert.That(
            BotPlayer.WouldAbandonHill(GameMode.Elimination, controlledCell, new Vector2Int(7, 8)),
            Is.False
        );
    }

    [Test]
    public void BotKnowledge_KeepsLastKnownCellWithoutReceivingHiddenPosition()
    {
        BotKnowledge knowledge = new();
        const ulong enemyId = 42;
        Vector2Int lastSeen = new(2, 2);

        knowledge.Update(
            new HashSet<Vector2Int> { lastSeen },
            new[] { new BotEnemySighting(enemyId, lastSeen) }
        );
        // The enemy's hidden current cell is intentionally absent from this API call.
        knowledge.Update(new HashSet<Vector2Int> { new Vector2Int(0, 0) }, null);

        Assert.That(knowledge.LastKnownCells[enemyId], Is.EqualTo(lastSeen));

        // Once the old cell itself is visible and empty, the stale memory is removed.
        knowledge.Update(new HashSet<Vector2Int> { lastSeen }, null);
        Assert.That(knowledge.LastKnownCells.ContainsKey(enemyId), Is.False);
    }

    [Test]
    public void BotMovement_StopsShortOfVisibleEnemyButMaySearchLastKnownCell()
    {
        Vector2Int start = new(0, 0);
        Vector2Int target = new(4, 0);
        HashSet<Vector2Int> blocked = new();

        List<Vector2Int> visiblePath = BotPlayer.BuildMovementPath(
            start,
            new[] { target },
            new HashSet<Vector2Int> { target },
            blocked,
            10
        );
        List<Vector2Int> searchPath = BotPlayer.BuildMovementPath(
            start,
            new[] { target },
            new HashSet<Vector2Int>(),
            blocked,
            10
        );

        Assert.That(visiblePath.Last(), Is.EqualTo(new Vector2Int(3, 0)));
        Assert.That(searchPath.Last(), Is.EqualTo(target));
    }

    [Test]
    public void BotDodge_FromGrenadeCenterSelectsStraightTwoCellEscape()
    {
        Vector2Int start = new(1, 1);
        List<Vector2Int> reachable = GridSystem.GetReachableCells(start, 2);
        BotDodgeThreat threat = new(false, start, start, 1.6f);

        Vector2Int chosen = BotPlayer.ChooseSafestDodgeCell(
            start,
            reachable,
            new[] { threat },
            new[] { new Vector2Int(1, 0) }
        );

        Assert.That(reachable, Does.Contain(chosen));
        Assert.That(GridSystem.IsCellInBounds(chosen), Is.True);
        Assert.That(GameLoop.wallLayout.Contains(chosen), Is.False);
        Assert.That(Vector2.Distance(chosen, start), Is.EqualTo(2f).Within(0.001f));
        Assert.That(
            Mathf.Abs(chosen.x - start.x) + Mathf.Abs(chosen.y - start.y),
            Is.EqualTo(2)
        );
    }

    [Test]
    public void ReplayQuorum_CountsOnlyHumanParticipants()
    {
        ulong host = 0;
        ulong remote = 5;

        Assert.That(
            GameLoop.HasReplayQuorum(new[] { host, GameLoop.BotParticipantId }, new[] { host }),
            Is.True
        );
        Assert.That(GameLoop.HasReplayQuorum(new[] { host, remote }, new[] { host }), Is.False);
        Assert.That(
            GameLoop.HasReplayQuorum(new[] { host, remote }, new[] { host, remote }),
            Is.True
        );

        CollectionAssert.AreEqual(
            new[] { host },
            GameLoop.FilterConnectedHumanParticipants(
                new[] { host, remote, GameLoop.BotParticipantId },
                new[] { host }
            )
        );
    }

    // === Roster validation (Wave 1) ===

    [Test]
    public void Roster_Validate_AcceptsDistinctEligibleTriplet()
    {
        List<UnitData> catalog = CreateCatalog(true, true, true, true);
        try
        {
            RosterValidationResult result = RosterRules.Validate(new[] { 0, 2, 3 }, catalog);
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Reason, Is.EqualTo(RosterValidationReason.None));
            Assert.That(result.SlotIndex, Is.EqualTo(-1));
            Assert.That(result.UnitIndex, Is.EqualTo(-1));
            Assert.That(result, Is.EqualTo(RosterValidationResult.Valid));
            Assert.That(RosterRules.GetUserMessage(result), Is.Empty);
        }
        finally
        {
            DestroyCatalog(catalog);
        }
    }

    [Test]
    public void Roster_Validate_RejectsMissingWrongLengthAndMissingCatalog()
    {
        List<UnitData> catalog = CreateCatalog(true, true, true);
        try
        {
            Assert.That(
                RosterRules.Validate(null, catalog).Reason,
                Is.EqualTo(RosterValidationReason.MissingRoster)
            );
            Assert.That(
                RosterRules.Validate(new[] { 0, 1 }, catalog).Reason,
                Is.EqualTo(RosterValidationReason.IncorrectUnitCount),
                "A short fireteam is rejected before any per-unit inspection."
            );
            Assert.That(
                RosterRules.Validate(new[] { 0, 1, 2, 0 }, catalog).Reason,
                Is.EqualTo(RosterValidationReason.IncorrectUnitCount),
                "An oversized fireteam is rejected on shape, not duplicates."
            );
            Assert.That(
                RosterRules.Validate(new[] { 0, 1, 2 }, null).Reason,
                Is.EqualTo(RosterValidationReason.UnitCatalogUnavailable),
                "A correctly shaped roster still fails without a catalog to validate against."
            );
        }
        finally
        {
            DestroyCatalog(catalog);
        }
    }

    [Test]
    public void Roster_Validate_RejectsDuplicateWithStableSlotAndIndex()
    {
        List<UnitData> catalog = CreateCatalog(true, true, true, true);
        try
        {
            RosterValidationResult early = RosterRules.Validate(new[] { 1, 1, 2 }, catalog);
            Assert.That(early.Reason, Is.EqualTo(RosterValidationReason.DuplicateUnit));
            Assert.That(
                early.SlotIndex,
                Is.EqualTo(1),
                "The repeated slot is reported deterministically."
            );
            Assert.That(early.UnitIndex, Is.EqualTo(1));

            RosterValidationResult tail = RosterRules.Validate(new[] { 2, 3, 2 }, catalog);
            Assert.That(tail.Reason, Is.EqualTo(RosterValidationReason.DuplicateUnit));
            Assert.That(tail.SlotIndex, Is.EqualTo(2));
            Assert.That(tail.UnitIndex, Is.EqualTo(2));
            Assert.That(
                RosterRules.GetUserMessage(tail),
                Is.EqualTo("Choose three different units; duplicate picks are not allowed.")
            );
        }
        finally
        {
            DestroyCatalog(catalog);
        }
    }

    [Test]
    public void Roster_Validate_RejectsNegativeAndOutOfRangeIndices()
    {
        List<UnitData> catalog = CreateCatalog(true, true, true);
        try
        {
            RosterValidationResult negative = RosterRules.Validate(new[] { -1, 0, 1 }, catalog);
            Assert.That(negative.Reason, Is.EqualTo(RosterValidationReason.UnitIndexOutOfRange));
            Assert.That(negative.SlotIndex, Is.EqualTo(0));
            Assert.That(negative.UnitIndex, Is.EqualTo(-1));

            RosterValidationResult tooHigh = RosterRules.Validate(
                new[] { 0, 1, catalog.Count },
                catalog
            );
            Assert.That(tooHigh.Reason, Is.EqualTo(RosterValidationReason.UnitIndexOutOfRange));
            Assert.That(tooHigh.SlotIndex, Is.EqualTo(2));
            Assert.That(tooHigh.UnitIndex, Is.EqualTo(catalog.Count));

            // Range is enforced before duplicate detection, so the first illegal slot wins.
            RosterValidationResult rangeBeatsDuplicate = RosterRules.Validate(
                new[] { 9, 9, 0 },
                catalog
            );
            Assert.That(
                rangeBeatsDuplicate.Reason,
                Is.EqualTo(RosterValidationReason.UnitIndexOutOfRange)
            );
            Assert.That(rangeBeatsDuplicate.SlotIndex, Is.EqualTo(0));
        }
        finally
        {
            DestroyCatalog(catalog);
        }
    }

    [Test]
    public void Roster_Validate_RejectsIneligibleUnitAndHonorsFailurePriority()
    {
        // Slot 1 is opted out of rosters; the rest are eligible.
        List<UnitData> catalog = CreateCatalog(true, false, true, true);
        try
        {
            Assert.That(RosterRules.IsUnitEligible(catalog, 1), Is.False);
            Assert.That(RosterRules.IsUnitEligible(catalog, 0), Is.True);

            RosterValidationResult ineligible = RosterRules.Validate(new[] { 0, 1, 2 }, catalog);
            Assert.That(ineligible.Reason, Is.EqualTo(RosterValidationReason.UnitUnavailable));
            Assert.That(ineligible.SlotIndex, Is.EqualTo(1));
            Assert.That(ineligible.UnitIndex, Is.EqualTo(1));
            Assert.That(
                RosterRules.GetUserMessage(ineligible),
                Is.EqualTo("That unit is unavailable for deployment. Choose another unit.")
            );

            // Duplicate detection runs before availability, so a duplicate outranks an ineligible pick.
            RosterValidationResult duplicateFirst = RosterRules.Validate(
                new[] { 0, 0, 1 },
                catalog
            );
            Assert.That(duplicateFirst.Reason, Is.EqualTo(RosterValidationReason.DuplicateUnit));
            Assert.That(duplicateFirst.SlotIndex, Is.EqualTo(1));

            Assert.That(RosterRules.Validate(new[] { 0, 2, 3 }, catalog).IsValid, Is.True);
        }
        finally
        {
            DestroyCatalog(catalog);
        }
    }

    [Test]
    public void UnitCatalogAsset_HasFiveEligibleUnitsWithValidDevRosters()
    {
        const string catalogPath = "Assets/UnitStats/AllUnits.asset";
        UnitDatabase catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<UnitDatabase>(catalogPath);
        Assert.That(catalog, Is.Not.Null, $"Could not load {catalogPath}.");
        Assert.That(catalog.units, Is.Not.Null);
        Assert.That(catalog.units.Count, Is.EqualTo(5), "AllUnits must stay at five units.");
        Assert.That(
            catalog.units,
            Has.None.Null,
            "The catalog must not contain null unit entries."
        );

        // Serialized catalog order: the Commander sits at index 0 and is selectable with Smoke Screen.
        Assert.That(catalog.units[0].unitName, Is.EqualTo("Commander"));
        Assert.That(
            catalog.units[0].IsRosterEligible,
            Is.True,
            "Commander must be player-selectable once Smoke Screen is configured."
        );
        for (int index = 1; index < catalog.units.Count; index++)
        {
            Assert.That(
                catalog.units[index].IsRosterEligible,
                Is.True,
                $"Catalog unit {index} ({catalog.units[index].unitName}) should be roster-eligible."
            );
        }

        // Configured dev/bot rosters must stay distinct and valid against the live catalog.
        (string name, int[] roster)[] configuredRosters =
        {
            ("DefaultBotRoster", GameLoop.DefaultBotRoster),
            ("DevHostRoster", GameLoop.DevHostRoster),
            ("DevOpponentRoster", GameLoop.DevOpponentRoster),
            ("DevBotHostRoster", GameLoop.DevBotHostRoster),
        };
        foreach ((string name, int[] roster) in configuredRosters)
        {
            Assert.That(
                roster.Length,
                Is.EqualTo(RosterRules.FireteamSize),
                $"{name} must field exactly three units."
            );
            Assert.That(
                roster.Distinct().Count(),
                Is.EqualTo(roster.Length),
                $"{name} must contain distinct units."
            );
            RosterValidationResult result = RosterRules.Validate(roster, catalog.units);
            Assert.That(
                result.IsValid,
                Is.True,
                $"{name} must validate against the live catalog (reason {result.Reason})."
            );
        }
    }

    // === Match results (Wave 1) ===

    [Test]
    public void MatchResult_SimultaneousWipeIsDrawWithExactCopy()
    {
        MatchResult result = GameLoop.ResolveEliminationResult(false, false);

        Assert.That(result.Outcome, Is.EqualTo(MatchOutcome.Draw));
        Assert.That(result.Reason, Is.EqualTo(MatchResultReason.SimultaneousElimination));
        Assert.That(result.HasWinner, Is.False);
        Assert.That(result.WinningTeamIndex, Is.EqualTo(GameLoop.NoHillController));
        Assert.That(result.IsValid, Is.True);
        Assert.That(
            result,
            Is.EqualTo(MatchResult.Draw(MatchResultReason.SimultaneousElimination))
        );
        Assert.That(
            result.GetStatusForTeam(GameLoop.HostTeamIndex),
            Is.EqualTo("Draw — both fireteams eliminated.")
        );
        Assert.That(
            result.GetStatusForTeam(GameLoop.OpponentTeamIndex),
            Is.EqualTo("Draw — both fireteams eliminated."),
            "A draw reads identically from either fireteam's perspective."
        );
    }

    [Test]
    public void MatchResult_SoleSurvivorWinsByEliminationWithPerspectiveCopy()
    {
        MatchResult hostWin = GameLoop.ResolveEliminationResult(true, false);
        Assert.That(hostWin.Outcome, Is.EqualTo(MatchOutcome.Win));
        Assert.That(hostWin.Reason, Is.EqualTo(MatchResultReason.Elimination));
        Assert.That(hostWin.HasWinner, Is.True);
        Assert.That(hostWin.WinningTeamIndex, Is.EqualTo(GameLoop.HostTeamIndex));
        Assert.That(
            hostWin,
            Is.EqualTo(MatchResult.ForWinner(GameLoop.HostTeamIndex, MatchResultReason.Elimination))
        );
        Assert.That(hostWin.GetStatusForTeam(GameLoop.HostTeamIndex), Is.EqualTo("You win!"));
        Assert.That(hostWin.GetStatusForTeam(GameLoop.OpponentTeamIndex), Is.EqualTo("You lose!"));

        MatchResult opponentWin = GameLoop.ResolveEliminationResult(false, true);
        Assert.That(opponentWin.WinningTeamIndex, Is.EqualTo(GameLoop.OpponentTeamIndex));
        Assert.That(opponentWin.Reason, Is.EqualTo(MatchResultReason.Elimination));
        Assert.That(
            opponentWin.GetStatusForTeam(GameLoop.OpponentTeamIndex),
            Is.EqualTo("You win!")
        );
        Assert.That(opponentWin.GetStatusForTeam(GameLoop.HostTeamIndex), Is.EqualTo("You lose!"));
        Assert.That(opponentWin, Is.Not.EqualTo(hostWin));
    }

    [Test]
    public void MatchResult_BothAliveIsNotTerminalAndDefaultIsNotAResult()
    {
        // The pure seam refuses to resolve a terminal result while both fireteams still live.
        Assert.That(
            () => GameLoop.ResolveEliminationResult(true, true),
            Throws.InstanceOf<System.InvalidOperationException>()
        );

        // A default (unresolved) result is not a valid, terminal outcome.
        MatchResult unresolved = default;
        Assert.That(unresolved.Outcome, Is.EqualTo(MatchOutcome.None));
        Assert.That(unresolved.HasWinner, Is.False);
        Assert.That(unresolved.IsValid, Is.False);
        Assert.That(
            unresolved.GetStatusForTeam(GameLoop.HostTeamIndex),
            Is.EqualTo("Match complete.")
        );
    }

    [Test]
    public void MatchResult_KingOfTheHillAndDisconnectStatusesAreDistinct()
    {
        MatchResult hillWin = MatchResult.ForWinner(
            GameLoop.HostTeamIndex,
            MatchResultReason.KingOfTheHill
        );
        Assert.That(
            hillWin.GetStatusForTeam(GameLoop.HostTeamIndex),
            Is.EqualTo(
                $"You win! Held the hill for {GameLoop.HillControlRoundsToWin} consecutive rounds."
            )
        );
        Assert.That(
            hillWin.GetStatusForTeam(GameLoop.OpponentTeamIndex),
            Is.EqualTo(
                $"You lose! Held the hill for {GameLoop.HillControlRoundsToWin} consecutive rounds."
            )
        );

        MatchResult forfeit = MatchResult.ForWinner(
            GameLoop.OpponentTeamIndex,
            MatchResultReason.DisconnectForfeit
        );
        Assert.That(
            forfeit.GetStatusForTeam(GameLoop.OpponentTeamIndex),
            Is.EqualTo("You win! Opponent disconnected.")
        );
        Assert.That(
            forfeit.GetStatusForTeam(GameLoop.HostTeamIndex),
            Is.EqualTo("You lose! Opponent disconnected.")
        );

        MatchResult elimination = MatchResult.ForWinner(
            GameLoop.HostTeamIndex,
            MatchResultReason.Elimination
        );
        Assert.That(hillWin, Is.Not.EqualTo(forfeit));
        Assert.That(hillWin, Is.Not.EqualTo(elimination));
        Assert.That(
            hillWin.GetStatusForTeam(GameLoop.HostTeamIndex),
            Is.Not.EqualTo(elimination.GetStatusForTeam(GameLoop.HostTeamIndex)),
            "KOTH and elimination victory copy must be distinguishable."
        );
    }

    [Test]
    public void MatchResult_NetworkRoundTripPreservesTypedOutcome()
    {
        MatchResult[] samples =
        {
            MatchResult.ForWinner(GameLoop.OpponentTeamIndex, MatchResultReason.KingOfTheHill),
            MatchResult.ForWinner(GameLoop.HostTeamIndex, MatchResultReason.Elimination),
            MatchResult.Draw(MatchResultReason.SimultaneousElimination),
        };

        foreach (MatchResult written in samples)
        {
            using FastBufferWriter writer = new(16, Allocator.Temp);
            writer.WriteNetworkSerializable(written);
            using FastBufferReader reader = new(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out MatchResult roundTripped);

            Assert.That(roundTripped, Is.EqualTo(written));
            Assert.That(roundTripped.IsValid, Is.True);
            Assert.That(
                roundTripped.GetStatusForTeam(GameLoop.HostTeamIndex),
                Is.EqualTo(written.GetStatusForTeam(GameLoop.HostTeamIndex))
            );
        }
    }

    [Test]
    public void MatchResult_InvalidWinnerAndDrawConstructionsThrow()
    {
        Assert.That(
            () => MatchResult.ForWinner(GameLoop.NoHillController, MatchResultReason.Elimination),
            Throws.ArgumentException,
            "A win requires a real winning team index."
        );
        Assert.That(
            () => MatchResult.ForWinner(GameLoop.HostTeamIndex, MatchResultReason.None),
            Throws.ArgumentException
        );
        Assert.That(
            () =>
                MatchResult.ForWinner(
                    GameLoop.HostTeamIndex,
                    MatchResultReason.SimultaneousElimination
                ),
            Throws.ArgumentException,
            "A simultaneous elimination can never be a win."
        );
        Assert.That(() => MatchResult.Draw(MatchResultReason.None), Throws.ArgumentException);
        Assert.That(
            () => MatchResult.Draw(MatchResultReason.Elimination),
            Throws.ArgumentException,
            "Only a simultaneous elimination is a valid draw."
        );
    }

    // === Dodge guidance copy (Wave 1) ===

    [Test]
    public void DodgeGuidance_ExposesExactPerspectiveStringsAndMapping()
    {
        Assert.That(
            GameLoop.ThreatenedDodgeGuidance,
            Is.EqualTo("DODGE now — drag flashing units to safety")
        );
        Assert.That(GameLoop.CasterDodgeGuidance, Is.EqualTo("Opponent is dodging your ability"));
        Assert.That(GameLoop.NeutralDodgeGuidance, Is.EqualTo("Waiting for dodge response"));

        Assert.That(
            GameLoop.GetDodgeGuidance(true, false),
            Is.EqualTo(GameLoop.ThreatenedDodgeGuidance)
        );
        Assert.That(
            GameLoop.GetDodgeGuidance(false, true),
            Is.EqualTo(GameLoop.CasterDodgeGuidance)
        );
        Assert.That(
            GameLoop.GetDodgeGuidance(false, false),
            Is.EqualTo(GameLoop.NeutralDodgeGuidance)
        );
        Assert.That(
            GameLoop.GetDodgeGuidance(true, true),
            Is.EqualTo(GameLoop.ThreatenedDodgeGuidance),
            "Being threatened outranks being the caster."
        );

        Assert.That(
            GameLoop.GetDodgeGuidancePerspective(true, false),
            Is.EqualTo(MessagePerspective.Enemy)
        );
        Assert.That(
            GameLoop.GetDodgeGuidancePerspective(false, true),
            Is.EqualTo(MessagePerspective.Friendly)
        );
        Assert.That(
            GameLoop.GetDodgeGuidancePerspective(false, false),
            Is.EqualTo(MessagePerspective.Neutral)
        );
        Assert.That(
            GameLoop.GetDodgeGuidancePerspective(true, true),
            Is.EqualTo(MessagePerspective.Enemy)
        );

        Assert.That(
            new[]
            {
                GameLoop.ThreatenedDodgeGuidance,
                GameLoop.CasterDodgeGuidance,
                GameLoop.NeutralDodgeGuidance,
            }
                .Distinct()
                .Count(),
            Is.EqualTo(3),
            "The three dodge perspectives must map to three distinct strings."
        );
    }

    // === No-new-mode / no-size-drift guards (Wave 1) ===

    [Test]
    public void SupportedGameModes_RemainEliminationAndKingOfTheHillOnly()
    {
        List<GameMode> supported = new();
        foreach (GameMode mode in System.Enum.GetValues(typeof(GameMode)))
        {
            MatchOptions options = MatchOptions.Default;
            options.gameMode = mode;
            if (options.Sanitized().gameMode == mode)
                supported.Add(mode);
        }

        CollectionAssert.AreEquivalent(
            new[] { GameMode.Elimination, GameMode.KingOfTheHill },
            supported,
            "Only Elimination and King of the Hill may survive sanitization."
        );

        MatchOptions ctf = MatchOptions.Default;
        ctf.gameMode = GameMode.CaptureTheFlag;
        Assert.That(ctf.Sanitized().gameMode, Is.EqualTo(GameMode.Elimination));
    }

    [Test]
    public void RosterAndModeConstants_HaveNoSizeOrCountDrift()
    {
        Assert.That(RosterRules.FireteamSize, Is.EqualTo(3));
        Assert.That(GameLoop.TeamCount, Is.EqualTo(2));

        int[][] configuredRosters =
        {
            GameLoop.DefaultBotRoster,
            GameLoop.DevHostRoster,
            GameLoop.DevOpponentRoster,
            GameLoop.DevBotHostRoster,
        };
        foreach (int[] roster in configuredRosters)
        {
            Assert.That(roster.Length, Is.EqualTo(RosterRules.FireteamSize));
            Assert.That(roster.Distinct().Count(), Is.EqualTo(roster.Length));
            Assert.That(roster, Has.All.GreaterThanOrEqualTo(0));
        }
    }

    private static List<UnitData> CreateCatalog(params bool[] eligibility)
    {
        List<UnitData> catalog = new(eligibility.Length);
        foreach (bool eligible in eligibility)
        {
            UnitData unit = ScriptableObject.CreateInstance<UnitData>();
            if (!eligible)
            {
                typeof(UnitData)
                    .GetField(
                        "unavailableForRoster",
                        BindingFlags.Instance | BindingFlags.NonPublic
                    )
                    .SetValue(unit, true);
            }
            catalog.Add(unit);
        }
        return catalog;
    }

    private static void DestroyCatalog(List<UnitData> catalog)
    {
        foreach (UnitData unit in catalog)
        {
            if (unit != null)
                Object.DestroyImmediate(unit);
        }
    }
}
