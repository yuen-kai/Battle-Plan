using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

[TestFixture]
[Category("GameplayNetwork")]
public class GameplayNetworkEditModeTests
{
    // Id 2 was Capture the Flag before it was dropped. A peer on an older build can still put it
    // on the wire, so sanitization must keep folding unknown ids back to Elimination.
    private const GameMode RetiredGameModeId = (GameMode)2;

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
        unsupported.gameMode = RetiredGameModeId;
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
    public void AbilityCooldowns_StartTickAndSaturateDeterministically()
    {
        UnitData data = ScriptableObject.CreateInstance<UnitData>();
        try
        {
            data.abilityCooldownRounds = -3;
            Assert.That(Unit.GetConfiguredAbilityCooldownRounds(data, true), Is.EqualTo(1));
            data.abilityCooldownRounds = 2;
            Assert.That(Unit.GetConfiguredAbilityCooldownRounds(data, false), Is.Zero);

            int remaining = 0;
            Assert.That(
                Unit.TryStartAbilityCooldown(
                    ref remaining,
                    Unit.GetConfiguredAbilityCooldownRounds(data, true),
                    true
                ),
                Is.True
            );
            Assert.That(remaining, Is.EqualTo(2));
            Assert.That(Unit.TryStartAbilityCooldown(ref remaining, 2, true), Is.False);
            Assert.That(remaining, Is.EqualTo(2));
            Assert.That(Unit.TickAbilityCooldownRound(ref remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(1));
            Assert.That(Unit.TickAbilityCooldownRound(ref remaining), Is.True);
            Assert.That(remaining, Is.Zero);
            Assert.That(Unit.TickAbilityCooldownRound(ref remaining), Is.False);
            Assert.That(remaining, Is.Zero);
            Assert.That(Unit.TryStartAbilityCooldown(ref remaining, 2, false), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(data);
        }
    }

    /// <summary>
    /// The dial counts up while the cooldown counts down, so the two are complements. Getting this
    /// backwards is invisible in a screenshot — a full dial and an empty one are both plausible
    /// pictures — and would tell the player an ability is ready on the exact round it is not.
    /// </summary>
    [Test]
    public void AbilityChargeDial_CountsUpAsTheCooldownCountsDown()
    {
        Assert.That(AbilityStatusRing.ChargedSlots(4, 4), Is.Zero, "Just spent, nothing charged.");
        Assert.That(AbilityStatusRing.ChargedSlots(3, 4), Is.EqualTo(1));
        Assert.That(AbilityStatusRing.ChargedSlots(1, 4), Is.EqualTo(3));
        Assert.That(
            AbilityStatusRing.ChargedSlots(0, 4),
            Is.EqualTo(4),
            "A cooldown of zero is the full dial the ready state is drawn from."
        );
        Assert.That(
            AbilityStatusRing.ChargedSlots(9, 2),
            Is.Zero,
            "A cooldown longer than the dial empties it rather than running it negative."
        );
        Assert.That(
            AbilityStatusRing.ChargedSlots(0, 0),
            Is.Zero,
            "A unit with no ability has no slots to fill."
        );
    }

    [TestCase(5, 30f)]
    [TestCase(4, 24f)]
    [TestCase(3, 18f)]
    [TestCase(2, 12f)]
    [TestCase(1, 12f)]
    [TestCase(0, 12f)]
    public void PlanningDuration_ScalesDownWithLivingCrew(int livingUnits, float expectedSeconds)
    {
        Assert.That(GameLoop.GetPlanningDurationSeconds(livingUnits), Is.EqualTo(expectedSeconds));
    }

    [Test]
    public void PlanningSession_RejectsSupersededSubmissionAndRosterRefresh()
    {
        GameObject gameObject = new("PlanningSessionVersionTest");
        try
        {
            PlanMovement planning = gameObject.AddComponent<PlanMovement>();
            GameObject marker = new("CurrentDodgeRosterMarker");
            marker.transform.SetParent(gameObject.transform);
            FieldInfo versionField = typeof(PlanMovement).GetField(
                "planningSessionVersion",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            FieldInfo activeField = typeof(PlanMovement).GetField(
                "planningActive",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            FieldInfo submittedField = typeof(PlanMovement).GetField(
                "planningSubmitted",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            FieldInfo teamCharactersField = typeof(PlanMovement).GetField(
                "teamCharacters",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            MethodInfo submitMethod = typeof(PlanMovement).GetMethod(
                "SubmitCurrentPlan",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            MethodInfo refreshMethod = typeof(PlanMovement).GetMethod(
                "TryRefreshTeamCharactersForSession",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

            Assert.That(versionField, Is.Not.Null);
            Assert.That(activeField, Is.Not.Null);
            Assert.That(submittedField, Is.Not.Null);
            Assert.That(teamCharactersField, Is.Not.Null);
            Assert.That(submitMethod, Is.Not.Null);
            Assert.That(refreshMethod, Is.Not.Null);

            versionField.SetValue(planning, 7);
            activeField.SetValue(planning, true);
            submittedField.SetValue(planning, false);
            List<GameObject> currentDodgeRoster = new() { marker };
            teamCharactersField.SetValue(planning, currentDodgeRoster);

            Assert.That(refreshMethod.Invoke(planning, new object[] { 6 }), Is.False);
            Assert.That(teamCharactersField.GetValue(planning), Is.SameAs(currentDodgeRoster));
            submitMethod.Invoke(planning, new object[] { 6 });

            Assert.That(activeField.GetValue(planning), Is.True);
            Assert.That(submittedField.GetValue(planning), Is.False);

            planning.EndPlanningSession();
            Assert.That(versionField.GetValue(planning), Is.EqualTo(8));
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void PlanningCommitRpcs_DeriveTeamFromSenderAndCarryRoundAndRevisionTokens()
    {
        MethodInfo submitRpc = typeof(GameLoop).GetMethod(
            "SendPathsToServerRpc",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        MethodInfo retractRpc = typeof(GameLoop).GetMethod(
            "RetractPathsServerRpc",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        Assert.That(submitRpc, Is.Not.Null);
        Assert.That(retractRpc, Is.Not.Null);

        ParameterInfo[] submitParameters = submitRpc.GetParameters();
        Assert.That(submitParameters, Has.Length.EqualTo(4));
        Assert.That(submitParameters[0].ParameterType, Is.EqualTo(typeof(PathsDict)));
        Assert.That(submitParameters[1].ParameterType, Is.EqualTo(typeof(int)));
        Assert.That(submitParameters[2].ParameterType, Is.EqualTo(typeof(int)));
        Assert.That(submitParameters[3].ParameterType, Is.EqualTo(typeof(ServerRpcParams)));
        Assert.That(
            submitParameters.Any(parameter => parameter.Name.Contains("team")),
            Is.False,
            "The client must never provide its logical team index."
        );

        ParameterInfo[] retractParameters = retractRpc.GetParameters();
        Assert.That(retractParameters, Has.Length.EqualTo(3));
        Assert.That(retractParameters[0].ParameterType, Is.EqualTo(typeof(int)));
        Assert.That(retractParameters[1].ParameterType, Is.EqualTo(typeof(int)));
        Assert.That(retractParameters[2].ParameterType, Is.EqualTo(typeof(ServerRpcParams)));
        Assert.That(
            retractParameters.Any(parameter => parameter.Name.Contains("team")),
            Is.False,
            "An unlock request must derive its logical team from the authenticated sender."
        );
    }

    [Test]
    public void PlanningUnlock_AcceptsOnlyCurrentRoundAndCommitRevision()
    {
        GameObject gameObject = new("PlanningUnlockStateTest");
        try
        {
            PlanMovement planning = gameObject.AddComponent<PlanMovement>();
            SetPrivateField(planning, "planningActive", true);
            SetPrivateField(planning, "planningInitialized", true);
            SetPrivateField(planning, "planningSubmitted", true);
            SetPrivateField(planning, "planningLockPending", false);
            SetPrivateField(planning, "planningUnlockPending", false);
            SetPrivateField(planning, "lockInAvailable", true);
            SetPrivateField(planning, "planningRoundToken", 12);
            SetPrivateField(planning, "planningCommitVersion", 3);

            int requestedRevision = -1;
            SetPrivateField(
                planning,
                "planningUnlockCallback",
                (System.Action<int>)(revision => requestedRevision = revision)
            );

            Assert.That(planning.CanUnlockPlan, Is.True);
            Assert.That(planning.TryUnlock(), Is.True);
            Assert.That(requestedRevision, Is.EqualTo(3));
            Assert.That(planning.CanUnlockPlan, Is.False);
            Assert.That(planning.CanEditPlan, Is.False);

            planning.NotifyPlanningCommitRetracted(11, 3);
            planning.NotifyPlanningCommitRetracted(12, 2);
            Assert.That(planning.CanEditPlan, Is.False, "Stale acknowledgements must be ignored.");

            planning.NotifyPlanningCommitRetracted(12, 3);
            Assert.That(planning.CanEditPlan, Is.True);
            Assert.That(planning.CanUnlockPlan, Is.False);

            planning.NotifyPlanningCommitFinalized(12);
            Assert.That(planning.CanEditPlan, Is.False);
            Assert.That(planning.CanUnlockPlan, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void PlanningUnlock_StaleRevisionCannotRemoveRelockedOrders()
    {
        GameObject gameObject = new("PlanningUnlockRevisionTest");
        try
        {
            GameLoop gameLoop = gameObject.AddComponent<GameLoop>();
            var paths = (Dictionary<int, PathsDict>)
                GetPrivateField(gameLoop, "submittedTeamPaths");
            var versions = (Dictionary<int, int>)
                GetPrivateField(gameLoop, "latestTeamPlanVersions");
            var fallbacks = (Dictionary<int, PathsDict>)
                GetPrivateField(gameLoop, "retractedTeamPathFallbacks");
            MethodInfo retractMethod = typeof(GameLoop).GetMethod(
                "TryRetractPlanningSubmission",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            MethodInfo newerVersionMethod = typeof(GameLoop).GetMethod(
                "IsNewerPlanningCommitVersion",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            MethodInfo restoreFallbacksMethod = typeof(GameLoop).GetMethod(
                "RestoreRetractedPlanningFallbacks",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

            Assert.That(retractMethod, Is.Not.Null);
            Assert.That(newerVersionMethod, Is.Not.Null);
            Assert.That(restoreFallbacksMethod, Is.Not.Null);
            paths[0] = new PathsDict();
            versions[0] = 4;
            paths[1] = new PathsDict();
            versions[1] = 7;

            Assert.That(retractMethod.Invoke(gameLoop, new object[] { 0, 3 }), Is.False);
            Assert.That(paths.ContainsKey(0), Is.True);
            Assert.That(versions[0], Is.EqualTo(4));

            Assert.That(retractMethod.Invoke(gameLoop, new object[] { 0, 4 }), Is.True);
            Assert.That(paths.ContainsKey(0), Is.False);
            Assert.That(versions[0], Is.EqualTo(4), "Revision high-water mark must survive unlock.");
            Assert.That(fallbacks.ContainsKey(0), Is.True);
            Assert.That(newerVersionMethod.Invoke(gameLoop, new object[] { 0, 4 }), Is.False);
            Assert.That(newerVersionMethod.Invoke(gameLoop, new object[] { 0, 5 }), Is.True);
            Assert.That(paths.ContainsKey(1), Is.True, "Unlock must not remove another team.");

            restoreFallbacksMethod.Invoke(gameLoop, null);
            Assert.That(paths.ContainsKey(0), Is.True, "Finalization must retain last locked orders.");
            Assert.That(fallbacks, Is.Empty);
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
        }
    }

    [TestCase(9.99d, 10d, false)]
    [TestCase(10d, 10d, true)]
    [TestCase(10.01d, 10d, true)]
    public void PlanningUnlock_DeadlineComparisonIsDeterministic(
        double serverTime,
        double deadline,
        bool expectedElapsed
    )
    {
        Assert.That(
            PlanMovement.HasPlanningDeadlineElapsed(serverTime, deadline),
            Is.EqualTo(expectedElapsed)
        );
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
        GameLoop.ConfigureTeam(
            GameLoop.HostTeamIndex,
            hostClientId,
            Enumerable.Range(0, RosterRules.UnitsPerPlayer).ToArray()
        );
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
    public void ConfigureTeam_AcceptsRosterWithRepeatedUnitIndices()
    {
        // A crew may field the same unit in more than one slot; ConfigureTeam only rejects
        // wrong-length rosters and negative indices, not repeats.
        const ulong hostClientId = 23;
        int[] repeatedRoster = Enumerable.Repeat(0, RosterRules.UnitsPerPlayer).ToArray();

        Assert.That(
            () => GameLoop.ConfigureTeam(GameLoop.HostTeamIndex, hostClientId, repeatedRoster),
            Throws.Nothing
        );
        Assert.That(
            GameLoop.TryGetConfiguredParticipantId(GameLoop.HostTeamIndex, out ulong mappedHost),
            Is.True
        );
        Assert.That(mappedHost, Is.EqualTo(hostClientId));
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
            Is.EqualTo(new Vector2Int(7, 7)),
            "One blocked orthogonal neighbor must not reject an otherwise open diagonal rush."
        );
        Assert.That(
            GridSystem.GetDirectionalDestination(
                start,
                new Vector2Int(1, 1),
                3,
                new HashSet<Vector2Int> { new Vector2Int(6, 6) }
            ),
            Is.EqualTo(new Vector2Int(5, 5)),
            "A wall on the diagonal path stops the rush on its last legal cell."
        );
        Assert.That(
            GridSystem.GetDirectionalDestination(
                start,
                new Vector2Int(1, 1),
                3,
                new HashSet<Vector2Int> { new Vector2Int(5, 4), new Vector2Int(4, 5) }
            ),
            Is.EqualTo(start),
            "Two blocked corner-adjacent cells still close the diagonal path."
        );
    }

    [Test]
    public void ShieldFootprint_WidensByOneGridCellPerSide()
    {
        const string prefabPath = "Assets/Prefabs/Units/Ramrod.prefab";
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Assert.That(prefab, Is.Not.Null, $"Could not load {prefabPath}.");
        Assert.That(Shield.ShieldWidthIncreaseCellsPerSide, Is.EqualTo(1));

        GameObject instance = Object.Instantiate(prefab);
        try
        {
            Transform shield = instance.transform.Find("Shield");
            Assert.That(shield, Is.Not.Null, "Ramrod prefab must keep its Shield child.");
            BoxCollider shieldCollider = shield.GetComponent<BoxCollider>();
            Assert.That(
                shieldCollider,
                Is.Not.Null,
                "The widened visual must retain the server-side bullet-blocking collider."
            );
            float widthBefore = Mathf.Abs(
                shieldCollider.size.x * shield.lossyScale.x
            );

            Assert.That(Shield.TryExpandShieldFootprint(shield), Is.True);

            float widthAfter = Mathf.Abs(
                shieldCollider.size.x * shield.lossyScale.x
            );
            Assert.That(
                widthAfter - widthBefore,
                Is.EqualTo(
                        Shield.ShieldWidthIncreaseCellsPerSide * 2f * GameLoop.cellSize
                    )
                    .Within(0.001f)
            );
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [TestCase(
        GameLoop.HostTeamIndex,
        "BlueTeam",
        Shield.BlueShieldLayerName,
        "Assets/Prefabs/Projectiles/BulletBlue.prefab",
        "Assets/Prefabs/Projectiles/BulletRed.prefab"
    )]
    [TestCase(
        GameLoop.OpponentTeamIndex,
        "RedTeam",
        Shield.RedShieldLayerName,
        "Assets/Prefabs/Projectiles/BulletRed.prefab",
        "Assets/Prefabs/Projectiles/BulletBlue.prefab"
    )]
    public void ShieldCollisionLayer_TargetingSkipsItButEnemyBulletsStillCollide(
        int teamIndex,
        string teamLayerName,
        string shieldLayerName,
        string friendlyBulletPath,
        string enemyBulletPath
    )
    {
        const string ramrodPrefabPath = "Assets/Prefabs/Units/Ramrod.prefab";
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            ramrodPrefabPath
        );
        GameObject friendlyBulletPrefab =
            UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(friendlyBulletPath);
        GameObject enemyBulletPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            enemyBulletPath
        );
        Assert.That(prefab, Is.Not.Null, $"Could not load {ramrodPrefabPath}.");
        Assert.That(friendlyBulletPrefab, Is.Not.Null, $"Could not load {friendlyBulletPath}.");
        Assert.That(enemyBulletPrefab, Is.Not.Null, $"Could not load {enemyBulletPath}.");

        GameObject instance = Object.Instantiate(prefab);
        try
        {
            int teamLayer = LayerMask.NameToLayer(teamLayerName);
            int shieldLayer = LayerMask.NameToLayer(shieldLayerName);
            int projectileLayer = LayerMask.NameToLayer("Projectile");
            Assert.That(teamLayer, Is.GreaterThanOrEqualTo(0));
            Assert.That(shieldLayer, Is.GreaterThanOrEqualTo(0));
            Assert.That(projectileLayer, Is.GreaterThanOrEqualTo(0));

            GameLoop.SetGroupLayer(instance, teamLayer);
            Transform shield = instance.transform.Find("Shield");
            Assert.That(shield, Is.Not.Null, "Ramrod must keep its Shield child.");
            BoxCollider shieldCollider = shield.GetComponent<BoxCollider>();
            Assert.That(shieldCollider, Is.Not.Null);
            Assert.That(shieldCollider.enabled, Is.True);
            Assert.That(shieldCollider.isTrigger, Is.False);
            Assert.That(
                shield.CompareTag("Untagged"),
                Is.True,
                "A shield must block the swept projectile query without being treated as a unit."
            );
            Assert.That(
                Shield.TryApplyCollisionLayer(shield, teamIndex),
                Is.True,
                "Replicated shield state must restore the dedicated team-shield layer."
            );
            shield.gameObject.SetActive(true);
            Assert.That(shield.gameObject.activeInHierarchy, Is.True);
            Assert.That(
                shield.gameObject.layer,
                Is.EqualTo(Shield.GetCollisionLayerForTeam(teamIndex))
            );
            Assert.That(
                shield.gameObject.layer,
                Is.EqualTo(shieldLayer)
            );

            int shieldLayerBit = 1 << shieldLayer;
            int unitTargetingMask = LayerMask.GetMask("Walls", teamLayerName);
            int boardSelectionMask = LayerMask.GetMask("Grid", "PathNode");
            Assert.That(
                unitTargetingMask & shieldLayerBit,
                Is.Zero,
                "Auto-targeting and ability unit queries must pass through an active shield."
            );
            Assert.That(
                unitTargetingMask & (1 << instance.layer),
                Is.Not.Zero,
                "The Ramrod body behind its own shield must remain targetable."
            );
            Assert.That(
                boardSelectionMask & shieldLayerBit,
                Is.Zero,
                "Grid and path-node mouse picks must pass through an active shield."
            );

            SphereCollider friendlyBulletCollider =
                friendlyBulletPrefab.GetComponentInChildren<SphereCollider>(
                    includeInactive: true
                );
            SphereCollider enemyBulletCollider =
                enemyBulletPrefab.GetComponentInChildren<SphereCollider>(includeInactive: true);
            Assert.That(friendlyBulletCollider, Is.Not.Null);
            Assert.That(enemyBulletCollider, Is.Not.Null);
            Assert.That(enemyBulletCollider.isTrigger, Is.False);
            Assert.That(enemyBulletPrefab.GetComponent<Bullet>(), Is.Not.Null);
            Assert.That(
                friendlyBulletCollider.excludeLayers.value & shieldLayerBit,
                Is.Not.Zero,
                "A team's bullets must continue to ignore its own shield."
            );
            Assert.That(
                enemyBulletCollider.excludeLayers.value & shieldLayerBit,
                Is.Zero,
                "Enemy bullets must continue to include this shield layer."
            );
            Assert.That(
                Physics.GetIgnoreLayerCollision(projectileLayer, shieldLayer),
                Is.False,
                "The global physics matrix must continue allowing projectile-shield contacts."
            );
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void BulletColour_IsCarriedByBothPrefabsSoEitherSideCanReadItAsItsOwnFire()
    {
        GameObject blue = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Projectiles/BulletBlue.prefab"
        );
        GameObject red = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Projectiles/BulletRed.prefab"
        );
        Assert.That(blue, Is.Not.Null);
        Assert.That(red, Is.Not.Null);
        Bullet blueBullet = blue.GetComponent<Bullet>();
        Bullet redBullet = red.GetComponent<Bullet>();
        Assert.That(blueBullet, Is.Not.Null);
        Assert.That(redBullet, Is.Not.Null);

        // Which prefab a shot spawns from stays absolute — it carries the shield layers the round
        // passes through — so each prefab is also what says who fired it, on every peer.
        Assert.That(
            Bullet.GetShooterTeamIndex(blueBullet.enemyTeam),
            Is.EqualTo(GameLoop.HostTeamIndex),
            "A shot that can damage red was fired by blue."
        );
        Assert.That(
            Bullet.GetShooterTeamIndex(redBullet.enemyTeam),
            Is.EqualTo(GameLoop.OpponentTeamIndex)
        );

        foreach (Bullet bullet in new[] { blueBullet, redBullet })
        {
            Assert.That(
                bullet.teamMaterials,
                Has.Count.EqualTo(2),
                "Either team's shot has to be drawable as own fire or as enemy fire."
            );
            Assert.That(bullet.teamMaterials[0], Is.Not.Null);
            Assert.That(bullet.teamMaterials[1], Is.Not.Null);
            Assert.That(
                bullet.teamMaterials[0],
                Is.Not.EqualTo(bullet.teamMaterials[1]),
                "Own fire and incoming fire must not look the same."
            );
        }
        Assert.That(
            redBullet.teamMaterials[0],
            Is.EqualTo(blueBullet.teamMaterials[0]),
            "Both prefabs answer to the same pair, so the variant inherits rather than diverges."
        );

        // The look each prefab is authored in is the host's view of it; the client resolves the
        // other way round at spawn.
        Assert.That(
            blue.GetComponentInChildren<Renderer>(true).sharedMaterial,
            Is.EqualTo(blueBullet.teamMaterials[0])
        );
        Assert.That(
            red.GetComponentInChildren<Renderer>(true).sharedMaterial,
            Is.EqualTo(redBullet.teamMaterials[1])
        );

        Assert.That(
            GameLoop.IsTeamFriendlyToLocalPlayer(-1),
            Is.False,
            "A shot with no readable shooter falls back to enemy fire rather than to own fire."
        );
    }

    [Test]
    public void ShieldRushBoost_OnlySelectsNearbyLivingAllies()
    {
        Vector2Int casterCell = new(4, 4);
        int casterTeamIndex = GameLoop.HostTeamIndex;

        Assert.That(
            Shield.IsEligibleAllyForSpeedBoost(
                casterCell,
                casterTeamIndex,
                new Vector2Int(6, 4),
                casterTeamIndex,
                true,
                false
            ),
            Is.True,
            "A living ally exactly two cells away is inside the rush radius."
        );
        Assert.That(
            Shield.IsEligibleAllyForSpeedBoost(
                casterCell,
                casterTeamIndex,
                new Vector2Int(5, 5),
                casterTeamIndex,
                true,
                false
            ),
            Is.True,
            "A nearby diagonal ally is inside the radial rush boost."
        );
        Assert.That(
            Shield.IsEligibleAllyForSpeedBoost(
                casterCell,
                casterTeamIndex,
                casterCell,
                casterTeamIndex,
                true,
                true
            ),
            Is.False,
            "The caster uses its fixed dash speed and does not buff itself."
        );
        Assert.That(
            Shield.IsEligibleAllyForSpeedBoost(
                casterCell,
                casterTeamIndex,
                new Vector2Int(5, 4),
                GameLoop.OpponentTeamIndex,
                true,
                false
            ),
            Is.False,
            "Nearby enemies never receive an allied rush boost."
        );
        Assert.That(
            Shield.IsEligibleAllyForSpeedBoost(
                casterCell,
                casterTeamIndex,
                new Vector2Int(5, 4),
                casterTeamIndex,
                false,
                false
            ),
            Is.False,
            "Dead allies never receive the rush boost."
        );
        Assert.That(
            Shield.IsEligibleAllyForSpeedBoost(
                casterCell,
                casterTeamIndex,
                new Vector2Int(6, 6),
                casterTeamIndex,
                true,
                false
            ),
            Is.False,
            "Living allies outside the two-cell radius are not boosted."
        );
    }

    [Test]
    public void ShieldRushBoost_ExpiresAndRestoresBaseMoveSpeed()
    {
        const float boostStartedAt = 10f;
        const float baseMoveSpeed = 2f;
        Movement.TimedMoveSpeedBoost speedBoost = new();

        Assert.That(Shield.AllySpeedBoostRadiusCells, Is.EqualTo(2f).Within(0.001f));
        Assert.That(Shield.AllySpeedBoostMultiplier, Is.EqualTo(1.5f).Within(0.001f));
        Assert.That(Shield.AllySpeedBoostDurationSeconds, Is.EqualTo(3f).Within(0.001f));
        Assert.That(
            speedBoost.TrySet(
                Shield.AllySpeedBoostMultiplier,
                Shield.AllySpeedBoostDurationSeconds,
                boostStartedAt
            ),
            Is.True
        );
        Assert.That(
            speedBoost.GetEffectiveSpeed(baseMoveSpeed, boostStartedAt - 0.001f),
            Is.EqualTo(baseMoveSpeed).Within(0.001f)
        );
        Assert.That(speedBoost.IsActive(boostStartedAt - 0.001f), Is.False);
        Assert.That(
            speedBoost.GetEffectiveSpeed(baseMoveSpeed, boostStartedAt + 1f),
            Is.EqualTo(baseMoveSpeed * Shield.AllySpeedBoostMultiplier).Within(0.001f)
        );
        Assert.That(speedBoost.IsActive(boostStartedAt + 1f), Is.True);
        Assert.That(
            speedBoost.GetEffectiveSpeed(
                baseMoveSpeed,
                boostStartedAt + Shield.AllySpeedBoostDurationSeconds
            ),
            Is.EqualTo(baseMoveSpeed).Within(0.001f),
            "The boost restores base speed at the exact end of the shield window."
        );
        Assert.That(
            speedBoost.IsActive(boostStartedAt + Shield.AllySpeedBoostDurationSeconds),
            Is.False,
            "The active-state decision ends exactly with the gameplay speed boost."
        );
        Assert.That(
            speedBoost.TrySet(
                Shield.AllySpeedBoostMultiplier,
                Shield.AllySpeedBoostDurationSeconds,
                boostStartedAt
            ),
            Is.True
        );
        speedBoost.Clear();
        Assert.That(
            speedBoost.GetEffectiveSpeed(baseMoveSpeed, boostStartedAt + 1f),
            Is.EqualTo(baseMoveSpeed).Within(0.001f),
            "Respawn cleanup immediately restores base speed."
        );
        Assert.That(
            speedBoost.IsActive(boostStartedAt + 1f),
            Is.False,
            "Explicit cleanup immediately marks the boost inactive."
        );
    }

    [Test]
    public void ShieldRushBoostIndicator_BuildsLocalGroundVisualWithoutColliders()
    {
        GameObject unit = new("Shield Rush indicator test unit");
        unit.transform.position = Vector3.up;
        unit.AddComponent<CapsuleCollider>();
        MeshRenderer hiddenUnitRenderer = unit.AddComponent<MeshRenderer>();
        hiddenUnitRenderer.forceRenderingOff = true;
        try
        {
            SpeedBoostIndicatorVisual visual = SpeedBoostIndicatorVisual.Create(unit.transform);

            Assert.That(visual, Is.Not.Null);
            Assert.That(visual.name, Is.EqualTo(SpeedBoostIndicatorVisual.GameObjectName));
            Assert.That(visual.IsVisible, Is.False, "The indicator starts dormant.");
            Collider unitCollider = unit.GetComponent<Collider>();
            Assert.That(
                visual.transform.position.y,
                Is.EqualTo(unitCollider.bounds.min.y + 0.08f).Within(0.001f),
                "The visual stays just above the unit's floor contact rather than obscuring it."
            );
            Assert.That(
                visual.GetComponentsInChildren<Renderer>(includeInactive: true).Length,
                Is.EqualTo(4),
                "One ring and three directional streaks make up the indicator."
            );
            Assert.That(
                visual
                    .GetComponentsInChildren<Renderer>(includeInactive: true)
                    .Select(renderer => renderer.forceRenderingOff),
                Is.All.True,
                "A lazily-created indicator must inherit host fog suppression before activation."
            );
            Assert.That(
                visual.GetComponentsInChildren<Collider>(includeInactive: true),
                Is.Empty,
                "The local presentation must never affect gameplay physics."
            );
            Assert.That(
                visual.GetComponentsInChildren<Light>(includeInactive: true),
                Is.Empty,
                "The indicator must not add gameplay-scene lighting cost."
            );
            Assert.That(
                visual
                    .GetComponentsInChildren<Renderer>(includeInactive: true)
                    .Select(renderer => renderer.shadowCastingMode),
                Is.All.EqualTo(UnityEngine.Rendering.ShadowCastingMode.Off)
            );
            Assert.That(
                visual
                    .GetComponentsInChildren<Renderer>(includeInactive: true)
                    .Select(renderer => renderer.receiveShadows),
                Is.All.False
            );
            Assert.That(
                visual
                    .GetComponentsInChildren<Renderer>(includeInactive: true)
                    .Select(renderer => renderer.sharedMaterial.shader.name),
                Is.All.EqualTo("BattlePlan/GroundGlow")
            );
            Assert.That(
                SpeedBoostIndicatorVisual.Create(unit.transform),
                Is.SameAs(visual),
                "Repeated state application must reuse the runtime-local visual."
            );

            visual.SetForceRenderingOff(false);
            visual.SetVisible(true);
            Assert.That(visual.IsVisible, Is.True);
            visual.SetVisible(false);
            Assert.That(visual.IsVisible, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(unit);
        }
    }

    [Test]
    public void AbilityChargeDial_BuildsLocalPlateVisualWithoutColliders()
    {
        GameObject unit = new("Ability charge dial test unit");
        unit.transform.position = Vector3.up;
        unit.transform.localScale = new Vector3(1.3f, 1.56f, 1.3f);
        unit.AddComponent<CapsuleCollider>();
        MeshRenderer hiddenUnitRenderer = unit.AddComponent<MeshRenderer>();
        hiddenUnitRenderer.forceRenderingOff = true;
        try
        {
            AbilityStatusRing dial = AbilityStatusRing.Create(unit.transform);

            Assert.That(dial, Is.Not.Null);
            Assert.That(dial.name, Is.EqualTo(AbilityStatusRing.GameObjectName));
            Assert.That(
                Vector3.Distance(dial.transform.lossyScale, Vector3.one),
                Is.LessThan(0.001f),
                "The dial cancels the unit's scale so it is the same size on every prefab variant."
            );
            Assert.That(
                dial.GetComponentsInChildren<Renderer>(includeInactive: true)
                    .Select(renderer => renderer.sharedMaterial.shader.name),
                Is.All.EqualTo(AbilityStatusRing.ShaderName)
            );
            Assert.That(
                dial.GetComponentsInChildren<Renderer>(includeInactive: true)
                    .Select(renderer => renderer.forceRenderingOff),
                Is.All.True,
                "A dial built for a hidden unit must inherit host fog suppression immediately."
            );
            Assert.That(
                dial.GetComponentsInChildren<Collider>(includeInactive: true),
                Is.Empty,
                "The local presentation must never affect gameplay physics."
            );
            Assert.That(
                dial.GetComponentsInChildren<Renderer>(includeInactive: true)
                    .Select(renderer => renderer.shadowCastingMode),
                Is.All.EqualTo(UnityEngine.Rendering.ShadowCastingMode.Off)
            );
            Assert.That(
                AbilityStatusRing.Create(unit.transform),
                Is.SameAs(dial),
                "Every cooldown change reapplies state, so construction must be idempotent."
            );

            dial.SetCharge(remainingRounds: 4, configuredRounds: 4, animate: false);
            Assert.That(dial.Slots, Is.EqualTo(4));
            Assert.That(dial.DisplayedCharge, Is.EqualTo(0f).Within(0.001f));
            Assert.That(dial.IsReady, Is.False);

            dial.SetCharge(remainingRounds: 0, configuredRounds: 4, animate: false);
            Assert.That(
                dial.DisplayedCharge,
                Is.EqualTo(4f).Within(0.001f),
                "State that predates this client's view of the unit is adopted, not animated."
            );
            Assert.That(dial.IsReady, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(unit);
        }
    }

    /// <summary>
    /// The dial is engraved into the base plate with a multiply, so a dial that only clears the
    /// board plane is depth-buried by the very puck it is supposed to be cut into.
    /// </summary>
    [Test]
    public void AbilityChargeDial_TakesTheHigherOfItsPlateAndTheRangeOverlay()
    {
        // A plate the depth of the ones the units actually carry. The dial would sit inside it
        // without the lift, and on top of it the range overlay would still bury the pair, so the
        // overlay's floor is what decides the height here.
        GameObject unit = BuildPlatedUnit("Ability charge dial plate test unit", 0.12f);
        Collider unitCollider = unit.GetComponent<Collider>();
        float boardPlane = unitCollider.bounds.min.y;
        float plateTop = boardPlane + 0.12f;

        try
        {
            Transform dial = AbilityStatusRing.Create(unit.transform).transform;
            Assert.That(
                dial.position.y,
                Is.GreaterThan(plateTop),
                "The dial must clear the plate face it is cut into rather than sit inside it."
            );
            Assert.That(
                dial.position.y,
                Is.EqualTo(boardPlane + 0.245f).Within(0.001f),
                "A plate this shallow leaves the range overlay as the binding floor."
            );
            Assert.That(
                dial.GetChild(0).rotation.eulerAngles.x,
                Is.EqualTo(90f).Within(0.001f),
                "The dial lies flat on the plate."
            );

            // The dial is built the instant a unit spawns, on the same frame the unit is placed,
            // and a collider's bounds lag a transform that moved this frame where a renderer's do
            // not. Consulting the plate alone is what keeps the two from disagreeing, so the dial
            // has to land in the same place with the collider gone entirely.
            Object.DestroyImmediate(unitCollider);
            Object.DestroyImmediate(dial.gameObject);
            Transform plateOnly = AbilityStatusRing.Create(unit.transform).transform;
            Assert.That(
                plateOnly.position.y,
                Is.EqualTo(boardPlane + 0.245f).Within(0.001f),
                "Placement must read the base plate and nothing else."
            );
        }
        finally
        {
            Object.DestroyImmediate(unit);
        }

        // Give a unit a plate tall enough to stand above the overlay and the plate wins again, so
        // the lift is a floor rather than a fixed height every dial is pinned to.
        GameObject stilted = BuildPlatedUnit("Ability charge dial tall plate unit", 0.4f);
        float stiltedBoard = stilted.GetComponent<Collider>().bounds.min.y;
        try
        {
            Assert.That(
                AbilityStatusRing.Create(stilted.transform).transform.position.y,
                Is.EqualTo(stiltedBoard + 0.4f + 0.035f).Within(0.001f),
                "A plate that already clears the overlay keeps the dial engraved in its face."
            );
        }
        finally
        {
            Object.DestroyImmediate(stilted);
        }
    }

    /// <summary>
    /// A unit carrying a base plate of the given thickness, resting on the board plane its collider
    /// defines.
    /// </summary>
    private static GameObject BuildPlatedUnit(string name, float plateThickness)
    {
        GameObject unit = new(name);
        unit.transform.position = Vector3.up;
        unit.AddComponent<CapsuleCollider>();

        GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name = UnitBasePlate.NamePrefix;
        plate.transform.SetParent(unit.transform, worldPositionStays: false);
        plate.transform.localScale = new Vector3(1.75f, plateThickness, 1.75f);
        plate.transform.position = new Vector3(
            0f,
            unit.GetComponent<Collider>().bounds.min.y + plateThickness * 0.5f,
            0f
        );
        return unit;
    }

    [Test]
    public void GroundVisuals_MeasureBasePlateClearanceFromTheUnitOrigin()
    {
        GameObject unit = new("Origin clearance test unit");
        unit.transform.position = new Vector3(0f, 3f, 0f);

        GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name = UnitBasePlate.NamePrefix;
        plate.transform.SetParent(unit.transform, worldPositionStays: false);
        plate.transform.localScale = new Vector3(1.75f, 0.1f, 1.75f);
        plate.transform.localPosition = new Vector3(0f, -1.2f, 0f);

        try
        {
            Assert.That(
                UnitBasePlate.ClearanceAboveOrigin(unit.transform, 0.035f),
                Is.EqualTo(-1.2f + 0.05f + 0.035f).Within(0.001f),
                "A plate below the origin puts the clearance below it too."
            );

            Object.DestroyImmediate(plate);
            Assert.That(
                UnitBasePlate.ClearanceAboveOrigin(unit.transform, 0.035f),
                Is.EqualTo(0.035f).Within(0.001f),
                "Without a plate or a collider the offset is all there is to go on."
            );
            Assert.That(
                UnitBasePlate.ClearanceAboveOrigin(null, 0.035f),
                Is.EqualTo(0.035f).Within(0.001f),
                "A missing unit must not throw on a purely presentational lookup."
            );
        }
        finally
        {
            Object.DestroyImmediate(unit);
        }
    }

    // === DODGE RECOVERY ===

    [Test]
    public void DodgeRecovery_CostsTheDodgerTwoSecondsOnTheFloor()
    {
        Assert.That(
            GameLoop.DodgeRecoverySeconds,
            Is.EqualTo(2f).Within(0.0001f),
            "A dive that costs nothing is a free answer to every telegraphed ability."
        );
    }

    [Test]
    public void DiveRecoveryState_ReportsProgressFromTheServerClock()
    {
        Assert.That(
            default(DiveRecoveryState).Active,
            Is.False,
            "A unit on its feet is not recovering."
        );

        const double landedAt = 120.5d;
        DiveRecoveryState recovery = new(landedAt, GameLoop.DodgeRecoverySeconds);

        Assert.That(recovery.Active, Is.True);
        Assert.That(recovery.ProgressAt(landedAt), Is.EqualTo(0f).Within(0.0001f));
        Assert.That(recovery.ProgressAt(landedAt + 1d), Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(recovery.ProgressAt(landedAt + 2d), Is.EqualTo(1f).Within(0.0001f));
        Assert.That(
            recovery.ProgressAt(landedAt + 30d),
            Is.EqualTo(1f).Within(0.0001f),
            "A peer that arrives late must draw a finished recovery, not an overrun one."
        );
        Assert.That(
            recovery.ProgressAt(landedAt - 5d),
            Is.EqualTo(0f).Within(0.0001f),
            "Clock skew must not run the gauge backwards past the landing."
        );
        Assert.That(
            recovery,
            Is.Not.EqualTo(new DiveRecoveryState(landedAt + 1d, GameLoop.DodgeRecoverySeconds)),
            "Each dive is its own replicated value, so a second one must not compare equal."
        );
    }

    /// <summary>
    /// Shield Rush's ring was being drawn inside the base plate the unit stands on, where depth
    /// testing hid it and every one of its streaks. Every unit prefab carries such a plate, so a
    /// ground effect that only clears the board plane is invisible in every real match.
    /// </summary>
    [Test]
    public void GroundVisuals_ClearTheBasePlateTheUnitStandsOn()
    {
        GameObject unit = new("Base plate clearance test unit");
        unit.transform.position = Vector3.up;
        unit.AddComponent<CapsuleCollider>();

        GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name = UnitBasePlate.NamePrefix + "Rim";
        plate.transform.SetParent(unit.transform, worldPositionStays: false);
        plate.transform.localScale = new Vector3(2f, 0.2f, 2f);
        Collider unitCollider = unit.GetComponent<Collider>();
        plate.transform.position = new Vector3(0f, unitCollider.bounds.min.y + 0.1f, 0f);
        float plateTop = plate.GetComponent<Renderer>().bounds.max.y;
        float boardPlane = unitCollider.bounds.min.y;

        try
        {
            Assert.That(
                UnitBasePlate.ClearanceAbovePlane(unit.transform, 0.08f),
                Is.EqualTo(plateTop - boardPlane + 0.08f).Within(0.001f),
                "Clearance is measured from the plate's top face, not the board plane."
            );

            Transform visual = SpeedBoostIndicatorVisual.Create(unit.transform).transform;
            Assert.That(
                visual.position.y,
                Is.EqualTo(plateTop + 0.08f).Within(0.001f),
                "The ring must sit above the plate, not inside it where depth buries it."
            );
            Assert.That(
                visual.position.y,
                Is.GreaterThan(boardPlane + 0.08f),
                "The ring must be lifted clear of the bare board plane."
            );
        }
        finally
        {
            Object.DestroyImmediate(unit);
        }
    }

    /// <summary>
    /// The ability dial has to survive a shown range. Every unit's base plate tops out below the
    /// range overlay, whose tile is opaque and writes depth, so a dial that only cleared its own
    /// plate vanished outright the moment a player asked where a unit could go — the one moment
    /// the cooldown is being read. Measured against the overlay prefab rather than a copied
    /// number, so moving the tile fails here instead of in a match.
    /// </summary>
    [Test]
    public void AbilityDial_ClearsTheRangeOverlayThatPavesTheCellUnderIt()
    {
        float overlayTop = RangeOverlayTop();

        // The deepest plate any unit prefab carries, so the test binds the clearance to the worst
        // real case rather than to a plate invented to pass it.
        GameObject unit = BuildPlatedUnit("Ability dial clearance test unit", 0.125f);
        float boardPlane = unit.GetComponent<Collider>().bounds.min.y;

        try
        {
            AbilityStatusRing ring = AbilityStatusRing.Create(unit.transform);
            Assert.That(ring, Is.Not.Null, "The dial's shader must be present in the project.");
            Assert.That(
                ring.transform.position.y,
                Is.GreaterThan(boardPlane + overlayTop),
                "A dial at or under the range overlay fails the depth test against its opaque "
                    + "tile and is hidden completely, not merely dimmed."
            );

            Shader dial = Shader.Find(AbilityStatusRing.ShaderName);
            Assert.That(dial, Is.Not.Null);
            Assert.That(
                dial.renderQueue,
                Is.GreaterThan(3000),
                "The overlay's highlight quad is a translucent full-cell wash at 3000. Drawn "
                    + "after the dial it repaints the charge in its own colour."
            );
            Assert.That(
                dial.renderQueue,
                Is.LessThan(3030),
                "Smoke, the vision cone and the explosion juice still have to cover a dial; the "
                    + "readout wins against the floor, not against what legitimately hides a unit."
            );
        }
        finally
        {
            Object.DestroyImmediate(unit);
        }
    }

    /// <summary>
    /// Top of the range overlay's stack in the prefab's own frame, plus the height GridSystem lays
    /// it at: the tile plane and the highlight quad riding above it.
    /// </summary>
    private static float RangeOverlayTop()
    {
        const string overlayPath = "Assets/Prefabs/Visuals/MoveOverlayCell.prefab";
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(overlayPath);
        Assert.That(prefab, Is.Not.Null, $"Could not import {overlayPath}.");

        // Instantiated and measured in world space rather than read off localPosition: the tile's
        // parts are laid out in the root's own scaled frame, where the highlight's 0.1 is 0.027 of
        // a world unit.
        GameObject tile = Object.Instantiate(prefab);
        try
        {
            tile.transform.position = new Vector3(0f, GridSystem.RangeOverlayHeight, 0f);
            float top = GridSystem.RangeOverlayHeight;
            foreach (Renderer part in tile.GetComponentsInChildren<Renderer>(true))
                top = Mathf.Max(top, part.bounds.max.y);
            return top;
        }
        finally
        {
            Object.DestroyImmediate(tile);
        }
    }

    [Test]
    public void GroundVisuals_FallBackToTheBoardPlaneWithoutABasePlate()
    {
        GameObject unit = new("Plateless clearance test unit");
        unit.transform.position = Vector3.up;
        unit.AddComponent<CapsuleCollider>();
        try
        {
            Assert.That(
                UnitBasePlate.ClearanceAbovePlane(unit.transform, 0.08f),
                Is.EqualTo(0.08f).Within(0.001f)
            );
            Assert.That(
                UnitBasePlate.ClearanceAbovePlane(null, 0.08f),
                Is.EqualTo(0.08f).Within(0.001f),
                "A missing unit must not throw on a purely presentational lookup."
            );
        }
        finally
        {
            Object.DestroyImmediate(unit);
        }
    }

    [Test]
    public void DodgeRecoveryPulse_TintsTheCharacterAndRestoresItOnStandingUp()
    {
        GameObject unit = new("Dodge recovery pulse test unit");
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.transform.SetParent(unit.transform, worldPositionStays: false);
        Renderer bodyRenderer = body.GetComponent<Renderer>();
        try
        {
            DiveRecoveryPulse pulse = DiveRecoveryPulse.Attach(unit);
            Assert.That(pulse, Is.Not.Null);
            Assert.That(pulse.IsPulsing, Is.False, "A unit on its feet is not pulsing.");
            Assert.That(
                DiveRecoveryPulse.Attach(unit),
                Is.SameAs(pulse),
                "Repeated state application must reuse the one driver on the unit."
            );

            pulse.SetRecovery(new DiveRecoveryState(0d, GameLoop.DodgeRecoverySeconds));
            Assert.That(pulse.IsPulsing, Is.True);
            Assert.That(
                bodyRenderer.HasPropertyBlock(),
                Is.True,
                "The pulse opens lit, on the frame the dodger hits the floor."
            );

            // Read per material slot, not per renderer: the body mesh carries several materials
            // and each one is tinted from its own colour rather than the renderer's first.
            MaterialPropertyBlock lit = new();
            bodyRenderer.GetPropertyBlock(lit, 0);
            Color tinted = lit.GetColor("_BaseColor");
            Color untouched = bodyRenderer.sharedMaterial.GetColor("_BaseColor");
            Assert.That(
                tinted,
                Is.Not.EqualTo(untouched),
                "The body has to actually change colour for the pulse to read."
            );
            Assert.That(
                tinted.r,
                Is.GreaterThan(tinted.b),
                "Recovery is amber, which must stay distinct from a white hit flash."
            );

            pulse.SetRecovery(default);
            Assert.That(pulse.IsPulsing, Is.False);
            Assert.That(
                bodyRenderer.HasPropertyBlock(),
                Is.False,
                "Standing back up must hand the character's own materials back untouched."
            );
        }
        finally
        {
            Object.DestroyImmediate(unit);
        }
    }

    [Test]
    public void DodgeRecoveryPulse_LeavesTheUnitsOwnEffectRenderersAlone()
    {
        GameObject unit = new("Dodge recovery pulse effect-exclusion test unit");
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.transform.SetParent(unit.transform, worldPositionStays: false);

        // Ground rings, the vision cone and the target laser drive their own property blocks on
        // BattlePlan/* shaders; tinting them would recolour them and clearing would wipe them.
        GameObject effect = GameObject.CreatePrimitive(PrimitiveType.Quad);
        effect.transform.SetParent(unit.transform, worldPositionStays: false);
        Renderer effectRenderer = effect.GetComponent<Renderer>();
        effectRenderer.sharedMaterial = new Material(Shader.Find("BattlePlan/GroundGlow"));
        LineRenderer laser = unit.AddComponent<LineRenderer>();

        try
        {
            Assert.That(
                DiveRecoveryPulse.IsCharacterRenderer(body.GetComponent<Renderer>()),
                Is.True
            );
            Assert.That(DiveRecoveryPulse.IsCharacterRenderer(effectRenderer), Is.False);
            Assert.That(DiveRecoveryPulse.IsCharacterRenderer(laser), Is.False);

            DiveRecoveryPulse pulse = DiveRecoveryPulse.Attach(unit);
            pulse.SetRecovery(new DiveRecoveryState(0d, GameLoop.DodgeRecoverySeconds));

            Assert.That(
                effectRenderer.HasPropertyBlock(),
                Is.False,
                "A body tint must not reach the effects parked on the unit."
            );
        }
        finally
        {
            Object.DestroyImmediate(unit);
        }
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
            new Vector2Int(6, 3),
            new Vector2Int(7, 3),
            new Vector2Int(8, 3),
            new Vector2Int(6, 4),
            new Vector2Int(7, 4),
            new Vector2Int(8, 4),
            new Vector2Int(6, 5),
            new Vector2Int(7, 5),
            new Vector2Int(8, 5),
            new Vector2Int(6, 6),
            new Vector2Int(7, 6),
            new Vector2Int(8, 6),
        };

        Assert.That(GridSystem.ColumnCount, Is.EqualTo(15));
        Assert.That(GridSystem.RowCount, Is.EqualTo(10));
        Assert.That(GameLoop.gridBounds.xMin, Is.EqualTo(0f));
        Assert.That(
            GameLoop.gridBounds.xMax,
            Is.EqualTo((GridSystem.ColumnCount - 1) * GameLoop.cellSize + 0.1f).Within(0.001f)
        );
        Assert.That(GameLoop.KingOfTheHillCells.Count, Is.EqualTo(12));
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
    public void RespawnPolicy_CurrentModesKeepEliminatedUnitsDead()
    {
        Assert.That(
            GameLoop.ShouldRespawnEliminatedUnits(GameMode.KingOfTheHill, GameLoop.TeamCount),
            Is.False,
            "KOTH casualties must remain dead while both teams still have survivors."
        );
        Assert.That(
            GameLoop.ShouldRespawnEliminatedUnits(GameMode.KingOfTheHill, 1),
            Is.False,
            "A full-team wipe must remain terminal."
        );
        Assert.That(
            GameLoop.ShouldRespawnEliminatedUnits(GameMode.Elimination, GameLoop.TeamCount),
            Is.False,
            "Elimination must not opt into the reusable respawn lifecycle."
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
    public void Roster_Validate_AcceptsConfiguredEligibleRoster()
    {
        List<UnitData> catalog = CreateEligibleCatalog(RosterRules.UnitsPerPlayer);
        try
        {
            RosterValidationResult result = RosterRules.Validate(CreateValidRoster(), catalog);
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
        int[] validRoster = CreateValidRoster();
        List<UnitData> catalog = CreateEligibleCatalog(RosterRules.UnitsPerPlayer + 1);
        try
        {
            Assert.That(
                RosterRules.Validate(null, catalog).Reason,
                Is.EqualTo(RosterValidationReason.MissingRoster)
            );
            Assert.That(
                RosterRules.Validate(validRoster.Take(validRoster.Length - 1).ToArray(), catalog)
                    .Reason,
                Is.EqualTo(RosterValidationReason.IncorrectUnitCount),
                "A short crew is rejected before any per-unit inspection."
            );
            Assert.That(
                RosterRules.Validate(
                        validRoster.Concat(new[] { validRoster[0] }).ToArray(),
                        catalog
                    )
                    .Reason,
                Is.EqualTo(RosterValidationReason.IncorrectUnitCount),
                "An oversized crew is rejected on shape."
            );
            Assert.That(
                RosterRules.Validate(validRoster, null).Reason,
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
    public void Roster_ValidateCatalog_RequiresEnoughEligibleUnits()
    {
        List<UnitData> shortCatalog = CreateEligibleCatalog(RosterRules.UnitsPerPlayer - 1);
        try
        {
            RosterValidationResult result = RosterRules.ValidateCatalog(shortCatalog);
            Assert.That(result.Reason, Is.EqualTo(RosterValidationReason.InsufficientEligibleUnits));
            Assert.That(
                RosterRules.GetUserMessage(result),
                Is.EqualTo(
                    $"At least {RosterRules.UnitsPerPlayer} eligible units are required to start a match."
                )
            );
        }
        finally
        {
            DestroyCatalog(shortCatalog);
        }
    }

    [Test]
    public void Roster_Validate_AcceptsRosterWithRepeatedUnits()
    {
        // Repeats are an intentional feature: a crew may field the same unit in more than
        // one slot, up to and including every slot.
        int[] earlyRepeat = CreateValidRoster();
        earlyRepeat[1] = earlyRepeat[0];
        int[] tailRepeat = CreateValidRoster();
        tailRepeat[^1] = tailRepeat[0];
        int[] allSameUnit = Enumerable.Repeat(0, RosterRules.UnitsPerPlayer).ToArray();
        List<UnitData> catalog = CreateEligibleCatalog(RosterRules.UnitsPerPlayer);
        try
        {
            Assert.That(RosterRules.Validate(earlyRepeat, catalog).IsValid, Is.True);
            Assert.That(RosterRules.Validate(tailRepeat, catalog).IsValid, Is.True);
            Assert.That(
                RosterRules.Validate(allSameUnit, catalog).IsValid,
                Is.True,
                "A crew of five copies of the same unit is a valid repeat pick."
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
        int[] negativeRoster = CreateValidRoster();
        negativeRoster[0] = -1;
        List<UnitData> catalog = CreateEligibleCatalog(RosterRules.UnitsPerPlayer);
        try
        {
            RosterValidationResult negative = RosterRules.Validate(negativeRoster, catalog);
            Assert.That(negative.Reason, Is.EqualTo(RosterValidationReason.UnitIndexOutOfRange));
            Assert.That(negative.SlotIndex, Is.EqualTo(0));
            Assert.That(negative.UnitIndex, Is.EqualTo(-1));

            int[] tooHighRoster = CreateValidRoster();
            tooHighRoster[^1] = catalog.Count;
            RosterValidationResult tooHigh = RosterRules.Validate(tooHighRoster, catalog);
            Assert.That(tooHigh.Reason, Is.EqualTo(RosterValidationReason.UnitIndexOutOfRange));
            Assert.That(tooHigh.SlotIndex, Is.EqualTo(tooHighRoster.Length - 1));
            Assert.That(tooHigh.UnitIndex, Is.EqualTo(catalog.Count));
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
        bool[] eligibility = Enumerable.Repeat(true, RosterRules.UnitsPerPlayer + 1).ToArray();
        eligibility[1] = false;
        List<UnitData> catalog = CreateCatalog(eligibility);
        try
        {
            Assert.That(RosterRules.IsUnitEligible(catalog, 1), Is.False);
            Assert.That(RosterRules.IsUnitEligible(catalog, 0), Is.True);

            RosterValidationResult ineligible = RosterRules.Validate(CreateValidRoster(), catalog);
            Assert.That(ineligible.Reason, Is.EqualTo(RosterValidationReason.UnitUnavailable));
            Assert.That(ineligible.SlotIndex, Is.EqualTo(1));
            Assert.That(ineligible.UnitIndex, Is.EqualTo(1));
            Assert.That(
                RosterRules.GetUserMessage(ineligible),
                Is.EqualTo("That unit is unavailable for deployment. Choose another unit.")
            );

            int[] eligibleRoster = Enumerable
                .Range(0, RosterRules.UnitsPerPlayer + 1)
                .Where(index => index != 1)
                .Take(RosterRules.UnitsPerPlayer)
                .ToArray();
            Assert.That(RosterRules.Validate(eligibleRoster, catalog).IsValid, Is.True);
        }
        finally
        {
            DestroyCatalog(catalog);
        }
    }

    [Test]
    public void UnitCatalogAsset_HasEnoughEligibleUnitsWithValidDevRosters()
    {
        const string catalogPath = "Assets/UnitStats/AllUnits.asset";
        UnitDatabase catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<UnitDatabase>(catalogPath);
        Assert.That(catalog, Is.Not.Null, $"Could not load {catalogPath}.");
        Assert.That(catalog.units, Is.Not.Null);
        Assert.That(catalog.units.Count, Is.GreaterThanOrEqualTo(RosterRules.UnitsPerPlayer));
        Assert.That(RosterRules.ValidateCatalog(catalog.units).IsValid, Is.True);
        Assert.That(
            catalog.units,
            Has.None.Null,
            "The catalog must not contain null unit entries."
        );

        string[] expectedCatalogOrder =
        {
            "Commander",
            "PogoRider",
            "Ramrod",
            "Sniper",
            "Soldier",
        };
        CollectionAssert.AreEqual(
            expectedCatalogOrder,
            catalog.units.Take(expectedCatalogOrder.Length).Select(unit => unit.unitName).ToArray(),
            "Roster indices are a serialized network/gameplay contract."
        );

        // Serialized catalog order: the Commander sits at index 0 and is selectable with Smoke Screen.
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

        string networkPrefabsMarkup = File.ReadAllText(
            "Assets/DefaultNetworkPrefabs.asset"
        );
        HashSet<GameObject> distinctModels = new();
        foreach (UnitData unit in catalog.units.Where(unit => unit.IsRosterEligible))
        {
            Assert.That(unit.unitModel, Is.Not.Null, $"{unit.unitName} needs a unit prefab.");
            Assert.That(
                distinctModels.Add(unit.unitModel),
                Is.True,
                $"{unit.unitName} must use a distinct unit prefab."
            );
            string prefabPath = UnityEditor.AssetDatabase.GetAssetPath(unit.unitModel);
            string prefabGuid = UnityEditor.AssetDatabase.AssetPathToGUID(prefabPath);
            Assert.That(
                networkPrefabsMarkup,
                Does.Contain($"guid: {prefabGuid}"),
                $"{unit.unitName} prefab must be registered for NGO spawning."
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
                Is.EqualTo(RosterRules.UnitsPerPlayer),
                $"{name} must field exactly {RosterRules.UnitsPerPlayer} units."
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

    [Test]
    public void PreferredRostersAndSpawnLayoutsTrackUnitsPerPlayer()
    {
        int[] preferredOrder = Enumerable
            .Range(0, RosterRules.UnitsPerPlayer + 2)
            .Reverse()
            .ToArray();
        int[] roster = RosterRules.BuildPreferredRoster(preferredOrder);

        Assert.That(roster.Length, Is.EqualTo(RosterRules.UnitsPerPlayer));
        CollectionAssert.AreEqual(
            preferredOrder.Take(RosterRules.UnitsPerPlayer).ToArray(),
            roster
        );
        int[] shortPreference = Enumerable
            .Range(0, RosterRules.UnitsPerPlayer - 1)
            .Reverse()
            .ToArray();
        int[] filledRoster = RosterRules.BuildPreferredRoster(shortPreference);
        Assert.That(filledRoster.Length, Is.EqualTo(RosterRules.UnitsPerPlayer));
        Assert.That(filledRoster.Distinct().Count(), Is.EqualTo(filledRoster.Length));
        CollectionAssert.AreEqual(
            shortPreference,
            filledRoster.Take(shortPreference.Length).ToArray()
        );

        int[] supportedTestCounts = new[] { 1, 3, RosterRules.UnitsPerPlayer, 7 }
            .Distinct()
            .ToArray();
        foreach (int unitCount in supportedTestCounts)
        {
            foreach (bool useDevLayout in new[] { false, true })
            {
                for (int teamIndex = 0; teamIndex < GameLoop.TeamCount; teamIndex++)
                {
                    Vector2Int[] positions = GameLoop.CreateSpawnPositions(
                        useDevLayout,
                        teamIndex,
                        unitCount
                    );

                    Assert.That(positions.Length, Is.EqualTo(unitCount));
                    Assert.That(positions.Distinct().Count(), Is.EqualTo(positions.Length));
                    foreach (Vector2Int cell in positions)
                    {
                        Assert.That(cell.x, Is.InRange(0, GridSystem.ColumnCount - 1));
                        Assert.That(cell.y, Is.InRange(0, GridSystem.RowCount - 1));
                        Assert.That(GameLoop.wallLayout.Contains(cell), Is.False);
                    }
                }
            }

            Vector2Int[] hostProduction = GameLoop.CreateSpawnPositions(
                false,
                GameLoop.HostTeamIndex,
                unitCount
            );
            Vector2Int[] opponentProduction = GameLoop.CreateSpawnPositions(
                false,
                GameLoop.OpponentTeamIndex,
                unitCount
            );
            for (int slotIndex = 0; slotIndex < unitCount; slotIndex++)
            {
                Assert.That(
                    opponentProduction[slotIndex].x,
                    Is.EqualTo(GridSystem.ColumnCount - 1 - hostProduction[slotIndex].x)
                );
            }
            Assert.That(
                hostProduction.Intersect(opponentProduction).Any(),
                Is.False,
                "Production deployment rows must keep opposing teams separated."
            );
        }

        Assert.That(
            () =>
                GameLoop.CreateSpawnPositions(
                    false,
                    GameLoop.HostTeamIndex,
                    GridSystem.ColumnCount + 1
                ),
            Throws.InvalidOperationException
        );
    }

    [Test]
    public void GameHudBuildsOneRuntimeCardPerUnitWithoutSerializedCardInstances()
    {
        const string hudPath = "Assets/UI/Game/GameHUD.uxml";
        const string cardPath = "Assets/UI/Shared/Templates/UnitCard.uxml";
        VisualTreeAsset hudAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(hudPath);
        VisualTreeAsset cardAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            cardPath
        );
        Assert.That(hudAsset, Is.Not.Null, $"Could not load {hudPath}.");
        Assert.That(cardAsset, Is.Not.Null, $"Could not load {cardPath}.");
        string cardGuid = UnityEditor.AssetDatabase.AssetPathToGUID(cardPath);
        Assert.That(
            File.ReadAllText("Assets/Scenes/Game.unity"),
            Does.Match($"unitCardTemplate: .*guid: {cardGuid}"),
            "The Game scene must serialize the authored card template onto its HUD controller."
        );

        GameObject hudObject = new("Dynamic HUD test");
        hudObject.SetActive(false);
        try
        {
            UIDocument document = hudObject.AddComponent<UIDocument>();
            document.visualTreeAsset = hudAsset;
            GameHUDController controller = hudObject.AddComponent<GameHUDController>();

            VisualElement root = document.rootVisualElement;
            Assert.That(root, Is.Not.Null);
            VisualElement cardsContainer = root.Q<VisualElement>("unit-cards");
            Assert.That(cardsContainer, Is.Not.Null);
            VisualElement enemyCardsContainer = root.Q<VisualElement>("enemy-unit-cards");
            Assert.That(enemyCardsContainer, Is.Not.Null);
            typeof(GameHUDController)
                .GetField("cardsContainer", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(controller, cardsContainer);
            typeof(GameHUDController)
                .GetField("enemyCardsContainer", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(controller, enemyCardsContainer);
            typeof(GameHUDController)
                .GetField("unitCardTemplate", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(controller, cardAsset);
            typeof(GameHUDController)
                .GetMethod("BuildCards", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, null);

            for (int index = 0; index < RosterRules.UnitsPerPlayer; index++)
            {
                VisualElement card = root.Q<VisualElement>($"unit-card-{index}");
                Assert.That(card, Is.Not.Null);
                Assert.That(
                    card.Q<VisualElement>($"unit-card-{index}-root"),
                    Is.Not.Null
                );

                VisualElement enemyCard = root.Q<VisualElement>($"enemy-unit-card-{index}");
                Assert.That(enemyCard, Is.Not.Null);
                Assert.That(
                    enemyCard.Q<VisualElement>($"enemy-unit-card-{index}-root"),
                    Is.Not.Null
                );
            }
            Assert.That(
                root.Query<VisualElement>(className: "unit-card-host--last").ToList().Count,
                Is.EqualTo(1)
            );
            Assert.That(
                root.Query<VisualElement>(className: "enemy-unit-card-host--last").ToList().Count,
                Is.EqualTo(1)
            );
            Assert.That(
                root.Q<VisualElement>($"unit-card-{RosterRules.UnitsPerPlayer}"),
                Is.Null,
                "The HUD should not generate a card beyond the configured roster size."
            );
            Assert.That(
                root.Q<VisualElement>($"enemy-unit-card-{RosterRules.UnitsPerPlayer}"),
                Is.Null,
                "The HUD should not generate an enemy card beyond the configured roster size."
            );
        }
        finally
        {
            Object.DestroyImmediate(hudObject);
        }
    }

    [Test]
    public void FriendlyUnitCardForwardsActivationAndShowsCooldownInFlipIndicator()
    {
        const string cardPath = "Assets/UI/Shared/Templates/UnitCard.uxml";
        VisualTreeAsset cardAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            cardPath
        );
        Assert.That(cardAsset, Is.Not.Null, $"Could not import {cardPath}.");

        TemplateContainer host = cardAsset.Instantiate();
        UnityEditor.EditorWindow window =
            ScriptableObject.CreateInstance<UnityEditor.EditorWindow>();
        window.Show();
        window.rootVisualElement.Add(host);
        UnitCardElement card = new(host, 0);
        UnitData data = ScriptableObject.CreateInstance<UnitData>();
        data.unitName = "Commander";
        data.abilityName = "Smoke Screen";
        int activationRequests = 0;
        Button selectButton = host.Q<Button>("unit-card-select-0");
        System.Action submitCard = () =>
        {
            using NavigationSubmitEvent submitEvent = new() { target = selectButton };
            selectButton.SendEvent(submitEvent);
        };

        try
        {
            Assert.That(selectButton, Is.Not.Null);
            Assert.That(selectButton.panel, Is.Not.Null);
            card.Configure(
                data,
                true,
                0,
                () => activationRequests++
            );
            card.SetInteractable(true);

            submitCard();
            Assert.That(
                activationRequests,
                Is.EqualTo(1),
                "The card forwards its first activation to the planner."
            );

            card.SetPlanningState(true, false);
            submitCard();
            Assert.That(
                activationRequests,
                Is.EqualTo(2),
                "The selected movement face forwards reactivation to the planner."
            );

            card.SetPlanningState(true, true);
            VisualElement root = host.Q<VisualElement>("unit-card-0-root");
            Assert.That(
                root.ClassListContains("unit-card--friendly"),
                Is.True,
                "An ability card styles itself from its own modifier, not from lacking the enemy one."
            );
            Assert.That(root.ClassListContains("unit-card--enemy"), Is.False);
            Assert.That(root.ClassListContains("unit-card--ability"), Is.True);
            Assert.That(host.Q<Label>("unit-card-ability").text, Is.EqualTo("Smoke Screen"));
            Assert.That(
                host.Q<VisualElement>("unit-card-flip-indicator")
                    .ClassListContains("hidden"),
                Is.False
            );

            submitCard();
            Assert.That(
                activationRequests,
                Is.EqualTo(3),
                "The selected ability face forwards reactivation to the planner."
            );

            card.SetPlanningState(true, false);
            card.SetAbilityCooldown(2);

            Label cooldown = host.Q<Label>("unit-card-cooldown");
            Assert.That(cooldown.text, Is.EqualTo("2"));
            Assert.That(cooldown.ClassListContains("hidden"), Is.False);
            Assert.That(
                host.Q<VisualElement>("unit-card-flip-indicator")
                    .ClassListContains("unit-card__flip-indicator--cooldown"),
                Is.True
            );

            submitCard();
            Assert.That(
                activationRequests,
                Is.EqualTo(4),
                "A cooling card still reaches the planner so it can explain why it is unavailable."
            );

            card.SetPlanningState(true, true);
            Assert.That(
                root.ClassListContains("unit-card--ability"),
                Is.False,
                "Cooldown prevents the ability face from becoming active."
            );

            card.SetAbilityCooldown(0);
            card.SetPlanningState(true, true);
            card.SetDisabled(true);
            Assert.That(root.ClassListContains("unit-card--selected"), Is.False);
            Assert.That(root.ClassListContains("unit-card--ability"), Is.False);
            Assert.That(selectButton.enabledInHierarchy, Is.False);
            int requestsBeforeDisabledSubmit = activationRequests;
            submitCard();
            Assert.That(
                activationRequests,
                Is.EqualTo(requestsBeforeDisabledSubmit),
                "Disabled cards must ignore keyboard submit."
            );

            card.Configure(data, false, 0, () => activationRequests++);
            card.SetPlanningState(true, true);
            Assert.That(host.Q<Label>("unit-card-ability").text, Is.EqualTo("Move only"));
            Assert.That(root.ClassListContains("unit-card--ability"), Is.False);
            Assert.That(
                host.Q<VisualElement>("unit-card-flip-indicator")
                    .ClassListContains("hidden"),
                Is.True,
                "Move-only units must not advertise a card flip."
            );
        }
        finally
        {
            card.Dispose();
            window.Close();
            Object.DestroyImmediate(data);
        }
    }

    [Test]
    public void EnemyUnitCardShowsLiveHealthAbilityAndEliminationState()
    {
        const string cardPath = "Assets/UI/Shared/Templates/UnitCard.uxml";
        VisualTreeAsset cardAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            cardPath
        );
        Assert.That(cardAsset, Is.Not.Null, $"Could not import {cardPath}.");

        TemplateContainer host = cardAsset.Instantiate();
        UnitCardElement card = new(host, 0, true);
        UnitData data = ScriptableObject.CreateInstance<UnitData>();
        data.unitName = "Soldier";
        data.abilityName = "Area Lock";
        try
        {
            card.ConfigureEnemy(data, true, 0, 75f, 120f, true);

            Assert.That(
                host.Q<Label>("unit-card-health-value").text,
                Is.EqualTo("75 / 120 HP")
            );
            Assert.That(
                host.Q<Label>("unit-card-ability").text,
                Is.EqualTo("Area Lock · ready")
            );
            Assert.That(host.Q<Label>("unit-card-state").text, Is.EqualTo("ACTIVE"));
            Assert.That(
                host.Q<VisualElement>("enemy-unit-card-0-root")
                    .ClassListContains("unit-card--enemy-active"),
                Is.True
            );
            Assert.That(host.Q<Button>("enemy-unit-card-select-0").focusable, Is.False);
            Assert.That(
                host.Q<Button>("enemy-unit-card-select-0").enabledInHierarchy,
                Is.False
            );
            Assert.That(
                host.Q<VisualElement>("unit-card-flip-indicator")
                    .ClassListContains("hidden"),
                Is.True,
                "Enemy status cards must not expose the friendly mode indicator."
            );

            card.ConfigureEnemy(data, true, 2, 0f, 120f, false);

            Assert.That(host.Q<Label>("unit-card-health-value").text, Is.EqualTo("0 / 120 HP"));
            Assert.That(
                host.Q<Label>("unit-card-ability").text,
                Is.EqualTo("Area Lock · ready in 2 rounds")
            );
            Assert.That(host.Q<Label>("unit-card-state").text, Is.EqualTo("ELIMINATED"));
            Assert.That(
                host.Q<VisualElement>("enemy-unit-card-0-root")
                    .ClassListContains("unit-card--enemy-eliminated"),
                Is.True
            );
        }
        finally
        {
            card.Dispose();
            Object.DestroyImmediate(data);
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
            Is.EqualTo("Draw — both crews eliminated.")
        );
        Assert.That(
            result.GetStatusForTeam(GameLoop.OpponentTeamIndex),
            Is.EqualTo("Draw — both crews eliminated."),
            "A draw reads identically from either crew's perspective."
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
        // The pure seam refuses to resolve a terminal result while both crews still live.
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
    public void BattleReport_NetworkRoundTripPreservesEveryCommittedOrder()
    {
        BattleReport written = new() { gameMode = GameMode.KingOfTheHill };
        written.crew.Add(
            new BattleReportCrewMember
            {
                teamIndex = GameLoop.HostTeamIndex,
                rosterSlot = 0,
                catalogIndex = 3,
                maxHealth = 80,
            }
        );
        written.crew.Add(
            new BattleReportCrewMember
            {
                teamIndex = GameLoop.OpponentTeamIndex,
                rosterSlot = 4,
                catalogIndex = 1,
                maxHealth = 120,
            }
        );

        BattleReportRound round = new()
        {
            roundNumber = 7,
            hillControllerTeamIndex = GameLoop.OpponentTeamIndex,
            hillStreak = 2,
            hillContested = false,
        };
        round.entries.Add(
            new BattleReportEntry
            {
                teamIndex = GameLoop.HostTeamIndex,
                rosterSlot = 0,
                order = BattleReportOrder.Ability,
                startCell = new Vector2Int(3, 2),
                endCell = new Vector2Int(3, 2),
                hasAbilityTarget = true,
                abilityTarget = new Vector2Int(9, 6),
                aliveAtRoundEnd = true,
                healthAtRoundEnd = 55,
                path = new List<Vector2Int>(),
            }
        );
        round.entries.Add(
            new BattleReportEntry
            {
                teamIndex = GameLoop.OpponentTeamIndex,
                rosterSlot = 4,
                order = BattleReportOrder.Dodge,
                startCell = new Vector2Int(9, 6),
                endCell = new Vector2Int(9, 8),
                aliveAtRoundEnd = false,
                diedThisRound = true,
                healthAtRoundEnd = 0,
                path = new List<Vector2Int>
                {
                    new(9, 6),
                    new(9, 7),
                    new(9, 8),
                },
            }
        );
        written.rounds.Add(round);

        using FastBufferWriter writer = new(1024, Allocator.Temp);
        writer.WriteNetworkSerializable(written);
        using FastBufferReader reader = new(writer, Allocator.Temp);
        reader.ReadNetworkSerializable(out BattleReport read);

        Assert.That(read.gameMode, Is.EqualTo(GameMode.KingOfTheHill));
        Assert.That(read.crew.Count, Is.EqualTo(2));
        Assert.That(
            read.TryGetCrewMember(GameLoop.OpponentTeamIndex, 4, out BattleReportCrewMember member),
            Is.True
        );
        Assert.That(member.catalogIndex, Is.EqualTo(1));
        Assert.That(member.maxHealth, Is.EqualTo(120));

        Assert.That(read.rounds.Count, Is.EqualTo(1));
        BattleReportRound readRound = read.rounds[0];
        Assert.That(readRound.roundNumber, Is.EqualTo(7));
        Assert.That(readRound.hillControllerTeamIndex, Is.EqualTo(GameLoop.OpponentTeamIndex));
        Assert.That(readRound.hillStreak, Is.EqualTo(2));
        Assert.That(readRound.hillContested, Is.False);
        Assert.That(readRound.entries.Count, Is.EqualTo(2));

        BattleReportEntry ability = readRound.entries[0];
        Assert.That(ability.order, Is.EqualTo(BattleReportOrder.Ability));
        Assert.That(ability.hasAbilityTarget, Is.True);
        Assert.That(ability.abilityTarget, Is.EqualTo(new Vector2Int(9, 6)));
        Assert.That(ability.startCell, Is.EqualTo(new Vector2Int(3, 2)));
        Assert.That(ability.healthAtRoundEnd, Is.EqualTo(55));
        Assert.That(ability.path, Is.Empty);

        BattleReportEntry dodge = readRound.entries[1];
        Assert.That(dodge.order, Is.EqualTo(BattleReportOrder.Dodge));
        Assert.That(dodge.diedThisRound, Is.True);
        Assert.That(dodge.aliveAtRoundEnd, Is.False);
        Assert.That(
            dodge.path,
            Is.EqualTo(
                new List<Vector2Int>
                {
                    new(9, 6),
                    new(9, 7),
                    new(9, 8),
                }
            )
        );
    }

    [Test]
    public void BattleReport_NegativeHillControllerSurvivesRoundTrip()
    {
        BattleReport written = new() { gameMode = GameMode.KingOfTheHill };
        written.rounds.Add(
            new BattleReportRound
            {
                roundNumber = 1,
                hillControllerTeamIndex = GameLoop.NoHillController,
                hillContested = true,
            }
        );

        using FastBufferWriter writer = new(128, Allocator.Temp);
        writer.WriteNetworkSerializable(written);
        using FastBufferReader reader = new(writer, Allocator.Temp);
        reader.ReadNetworkSerializable(out BattleReport read);

        Assert.That(
            read.rounds[0].hillControllerTeamIndex,
            Is.EqualTo(GameLoop.NoHillController),
            "An uncontrolled hill is -1, so the controller field cannot be an unsigned byte."
        );
        Assert.That(read.rounds[0].hillContested, Is.True);
    }

    [Test]
    public void BattleReport_RecordingIsBoundedAndRevealedOnlyAtMatchEnd()
    {
        string source = File.ReadAllText("Assets/Scripts/GameManager/GameLoop.cs");

        Assert.That(
            source,
            Does.Contain("RecordBattleReportPlans(paths)"),
            "Orders must be captured after the dodge window folds dives into the plan."
        );
        Assert.That(
            source.IndexOf("RecordBattleReportPlans(paths)", System.StringComparison.Ordinal),
            Is.LessThan(source.IndexOf("ExecuteMoves(paths);", System.StringComparison.Ordinal)),
            "diveUnitsThisRound is cleared by ExecuteMoves, so dodges must be read before it runs."
        );
        Assert.That(
            BattleReport.MaxRecordedRounds,
            Is.GreaterThan(GameLoop.HillControlRoundsToWin),
            "The cap must never truncate the shortest possible King of the Hill win."
        );

        MethodInfo send = typeof(GameLoop).GetMethod(
            "SendBattleReportClientRpc",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(send, Is.Not.Null);
        Assert.That(
            send.GetCustomAttribute<ClientRpcAttribute>(),
            Is.Not.Null,
            "The reveal is server-authored and pushed to clients, never requested by them."
        );
    }

    /// <summary>
    /// Wires the HUD's report elements without running OnEnable, matching the inactive-GameObject
    /// pattern the other HUD tests use.
    /// </summary>
    private static void BindReportElements(GameHUDController controller, VisualElement root)
    {
        void Bind(string field, VisualElement element)
        {
            typeof(GameHUDController)
                .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(controller, element);
        }

        VisualElement board = root.Q<VisualElement>("report-board");
        Bind("reportReveal", root.Q<VisualElement>("report-reveal"));
        Bind("reportBoardElement", board);
        Bind("reportOrders", root.Q<VisualElement>("report-orders"));
        Bind("reportEmpty", root.Q<Label>("report-empty"));
        Bind("reportRoundLabel", root.Q<Label>("report-round-label"));
        Bind("reportSummary", root.Q<Label>("report-summary"));
        Bind("reportPrevButton", root.Q<Button>("report-prev-button"));
        Bind("reportNextButton", root.Q<Button>("report-next-button"));

        typeof(GameHUDController)
            .GetField("reportBoard", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(controller, new BattleReportBoard(board));
    }

    private static BattleReport BuildTwoRoundReport()
    {
        BattleReport report = new() { gameMode = GameMode.KingOfTheHill };
        for (int team = 0; team < GameLoop.TeamCount; team++)
        {
            report.crew.Add(
                new BattleReportCrewMember
                {
                    teamIndex = team,
                    rosterSlot = 0,
                    catalogIndex = 0,
                    maxHealth = 120,
                }
            );
        }

        BattleReportRound first = new() { roundNumber = 1, hillContested = true };
        first.entries.Add(
            new BattleReportEntry
            {
                teamIndex = GameLoop.HostTeamIndex,
                rosterSlot = 0,
                order = BattleReportOrder.Move,
                startCell = new Vector2Int(1, 1),
                endCell = new Vector2Int(1, 3),
                aliveAtRoundEnd = true,
                healthAtRoundEnd = 120,
                path = new List<Vector2Int>
                {
                    new(1, 1),
                    new(1, 2),
                    new(1, 3),
                },
            }
        );
        first.entries.Add(
            new BattleReportEntry
            {
                teamIndex = GameLoop.OpponentTeamIndex,
                rosterSlot = 0,
                order = BattleReportOrder.Ability,
                startCell = new Vector2Int(10, 8),
                endCell = new Vector2Int(10, 8),
                hasAbilityTarget = true,
                abilityTarget = new Vector2Int(7, 5),
                aliveAtRoundEnd = true,
                healthAtRoundEnd = 96,
                path = new List<Vector2Int>(),
            }
        );

        BattleReportRound second = new()
        {
            roundNumber = 2,
            hillControllerTeamIndex = GameLoop.OpponentTeamIndex,
            hillStreak = 1,
        };
        second.entries.Add(
            new BattleReportEntry
            {
                teamIndex = GameLoop.HostTeamIndex,
                rosterSlot = 0,
                order = BattleReportOrder.Dodge,
                startCell = new Vector2Int(1, 3),
                endCell = new Vector2Int(2, 3),
                aliveAtRoundEnd = false,
                diedThisRound = true,
                healthAtRoundEnd = 0,
                path = new List<Vector2Int> { new(1, 3), new(2, 3) },
            }
        );
        second.entries.Add(
            new BattleReportEntry
            {
                teamIndex = GameLoop.OpponentTeamIndex,
                rosterSlot = 0,
                order = BattleReportOrder.Held,
                startCell = new Vector2Int(10, 8),
                endCell = new Vector2Int(10, 8),
                aliveAtRoundEnd = true,
                healthAtRoundEnd = 96,
                path = new List<Vector2Int>(),
            }
        );

        report.rounds.Add(first);
        report.rounds.Add(second);
        return report;
    }

    [Test]
    public void BattleReportReveal_OpensOnFinalRoundAndNamesEveryCommittedOrder()
    {
        VisualTreeAsset hudAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            "Assets/UI/Game/GameHUD.uxml"
        );
        Assert.That(hudAsset, Is.Not.Null);

        GameObject hudObject = new("Battle report reveal test");
        hudObject.SetActive(false);
        try
        {
            UIDocument document = hudObject.AddComponent<UIDocument>();
            document.visualTreeAsset = hudAsset;
            GameHUDController controller = hudObject.AddComponent<GameHUDController>();
            VisualElement root = document.rootVisualElement;
            BindReportElements(controller, root);

            controller.SetBattleReport(BuildTwoRoundReport(), GameLoop.HostTeamIndex);

            Label roundLabel = root.Q<Label>("report-round-label");
            Assert.That(
                roundLabel.text,
                Is.EqualTo("Round 2 of 2"),
                "The reveal opens on the round the player just lived through."
            );
            Assert.That(root.Q<VisualElement>("report-reveal").ClassListContains("hidden"), Is.False);
            Assert.That(root.Q<Label>("report-empty").ClassListContains("hidden"), Is.True);
            Assert.That(root.Q<Button>("report-next-button").enabledSelf, Is.False);
            Assert.That(root.Q<Button>("report-prev-button").enabledSelf, Is.True);

            VisualElement orders = root.Q<VisualElement>("report-orders");
            List<VisualElement> rows = orders
                .Query<VisualElement>(className: "report-order-row")
                .ToList();
            Assert.That(rows.Count, Is.EqualTo(2), "One row per unit in the round.");
            Assert.That(
                orders.Query<Label>(className: "report-team-heading").ToList().Count,
                Is.EqualTo(2),
                "Rows are grouped into the viewer's crew and the opponent's."
            );

            string friendlyOrder = rows[0].Q<Label>(className: "report-order-row__order").text;
            Assert.That(friendlyOrder, Does.Contain("Dodged"));
            Assert.That(rows[0].ClassListContains("report-order-row--dead"), Is.True);
            Assert.That(rows[0].ClassListContains("report-order-row--enemy"), Is.False);
            Assert.That(
                rows[0].Q<Label>(className: "report-order-row__state").text,
                Is.EqualTo("Eliminated")
            );

            Assert.That(rows[1].ClassListContains("report-order-row--enemy"), Is.True);
            Assert.That(
                rows[1].Q<Label>(className: "report-order-row__state").text,
                Is.EqualTo("96/120 HP")
            );

            Label summary = root.Q<Label>("report-summary");
            Assert.That(summary.text, Does.Contain("You lost 1 unit"));
            Assert.That(
                summary.text,
                Does.Contain($"They held the hill (1/{GameLoop.HillControlRoundsToWin})")
            );

            // Stepping back must reach the round whose ability target the loser wants explained.
            typeof(GameHUDController)
                .GetMethod("StepReportRound", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, new object[] { -1 });

            Assert.That(roundLabel.text, Is.EqualTo("Round 1 of 2"));
            Assert.That(root.Q<Button>("report-prev-button").enabledSelf, Is.False);
            Assert.That(root.Q<Button>("report-next-button").enabledSelf, Is.True);
            Assert.That(summary.text, Does.Contain("No one was eliminated."));
            Assert.That(summary.text, Does.Contain("The hill was contested."));

            List<VisualElement> firstRoundRows = orders
                .Query<VisualElement>(className: "report-order-row")
                .ToList();
            Assert.That(
                firstRoundRows[1].Q<Label>(className: "report-order-row__order").text,
                Does.Contain("Ability on (7, 5)"),
                "The reveal must name where an enemy ability was actually aimed."
            );
        }
        finally
        {
            Object.DestroyImmediate(hudObject);
        }
    }

    [Test]
    public void BattleReportReveal_FallsBackToANoticeWhenNothingWasRecorded()
    {
        VisualTreeAsset hudAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            "Assets/UI/Game/GameHUD.uxml"
        );
        GameObject hudObject = new("Empty battle report test");
        hudObject.SetActive(false);
        try
        {
            UIDocument document = hudObject.AddComponent<UIDocument>();
            document.visualTreeAsset = hudAsset;
            GameHUDController controller = hudObject.AddComponent<GameHUDController>();
            VisualElement root = document.rootVisualElement;
            BindReportElements(controller, root);

            // A match that ends before any round resolves still has to close cleanly.
            controller.SetBattleReport(null, GameLoop.HostTeamIndex);
            Assert.That(root.Q<VisualElement>("report-reveal").ClassListContains("hidden"), Is.True);
            Assert.That(root.Q<Label>("report-empty").ClassListContains("hidden"), Is.False);
            Assert.That(root.Q<Label>("report-round-label").text, Is.Empty);

            controller.SetBattleReport(new BattleReport(), GameLoop.HostTeamIndex);
            Assert.That(
                root.Q<VisualElement>("report-reveal").ClassListContains("hidden"),
                Is.True,
                "A report with no rounds is as empty as no report at all."
            );
        }
        finally
        {
            Object.DestroyImmediate(hudObject);
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

        MatchOptions retired = MatchOptions.Default;
        retired.gameMode = RetiredGameModeId;
        Assert.That(retired.Sanitized().gameMode, Is.EqualTo(GameMode.Elimination));
    }

    [Test]
    public void RosterAndModeConstants_HaveNoSizeOrCountDrift()
    {
        Assert.That(RosterRules.UnitsPerPlayer, Is.GreaterThan(0));
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
            Assert.That(roster.Length, Is.EqualTo(RosterRules.UnitsPerPlayer));
            Assert.That(roster.Distinct().Count(), Is.EqualTo(roster.Length));
            Assert.That(roster, Has.All.GreaterThanOrEqualTo(0));
        }
    }

    private static int[] CreateValidRoster()
    {
        return Enumerable.Range(0, RosterRules.UnitsPerPlayer).ToArray();
    }

    private static List<UnitData> CreateEligibleCatalog(int count)
    {
        return CreateCatalog(Enumerable.Repeat(true, count).ToArray());
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target
            .GetType()
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing private field {fieldName}.");
        field.SetValue(target, value);
    }

    private static object GetPrivateField(object target, string fieldName)
    {
        FieldInfo field = target
            .GetType()
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing private field {fieldName}.");
        return field.GetValue(target);
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
