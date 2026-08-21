using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

// GameLoop.TryRegisterSmokeFootprint is IsServer-gated the same way TryDestroyWallCell is (see
// WallDestructionEditModeTests), and IsServer is only meaningful on a spawned NetworkBehaviour,
// which edit mode cannot provide. These tests instead drive the private core mutations the gated
// method delegates to -- RegisterSmokeFootprintLocal and the round-boundary tick
// AdvanceSmokeDeploymentsForRoundBoundary -- via reflection on a bare, unspawned GameLoop
// component, exercising the exact same production code the server runs without needing IsServer.
// SmokeScreenEditModeTests already covers the pure footprint/segment/visibility math and the
// serialized Commander contract; this file is scoped to the round-persistence bookkeeping layered
// on top of that.
[TestFixture]
[Category("SmokeScreen")]
public class SmokePersistenceEditModeTests
{
    private readonly List<GameObject> spawnedObjects = new();

    [SetUp]
    public void SetUp()
    {
        MatchOptions.Reset();
        GameLoop.ResetMatchState();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject spawned in spawnedObjects)
        {
            if (spawned != null)
                Object.DestroyImmediate(spawned);
        }
        spawnedObjects.Clear();

        GameLoop.ResetMatchState();
        MatchOptions.Reset();
    }

    [Test]
    public void RoundBoundaryTick_DeploymentSurvivesOneRoundThenClearsOnTheSecond()
    {
        GameLoop gameLoop = CreateGameLoop();
        Vector2Int center = new(4, 4);

        RegisterFootprint(gameLoop, center);
        Assert.That(
            gameLoop.IsSmokeCellActive(center),
            Is.True,
            "A freshly thrown canister must be active immediately."
        );
        Assert.That(
            gameLoop.ActiveSmokeCells,
            Does.Contain(center),
            "The public snapshot must agree with IsSmokeCellActive."
        );

        // Round boundary: round N -> round N+1.
        AdvanceRoundBoundary(gameLoop);
        Assert.That(
            gameLoop.IsSmokeCellActive(center),
            Is.True,
            "A deployment thrown in round N must still be active through all of round N+1."
        );

        // Round boundary: round N+1 -> round N+2.
        AdvanceRoundBoundary(gameLoop);
        Assert.That(
            gameLoop.IsSmokeCellActive(center),
            Is.False,
            "The deployment must clear once round N+2's boundary tick is reached."
        );
        Assert.That(gameLoop.ActiveSmokeCells, Is.Empty);
    }

    [Test]
    public void RoundBoundaryTick_OverlappingDeploymentsFromDifferentRoundsExpireIndependently()
    {
        GameLoop gameLoop = CreateGameLoop();
        // Two 3x3 footprints one cell apart: they share a column, but each also has a cell unique
        // to itself, so the union can be probed at three distinct points.
        Vector2Int centerA = new(4, 4);
        Vector2Int centerB = new(5, 4);
        Vector2Int onlyA = new(3, 4);
        Vector2Int onlyB = new(6, 4);
        Vector2Int shared = new(4, 4);

        // A thrown in round N.
        RegisterFootprint(gameLoop, centerA);

        // Round boundary: N -> N+1. A: 2 rounds remaining -> 1 (still active).
        AdvanceRoundBoundary(gameLoop);
        Assert.That(gameLoop.IsSmokeCellActive(onlyA), Is.True, "Precondition: A still active.");

        // B thrown during round N+1, overlapping part of A.
        RegisterFootprint(gameLoop, centerB);
        Assert.That(gameLoop.IsSmokeCellActive(onlyB), Is.True);
        Assert.That(gameLoop.IsSmokeCellActive(shared), Is.True, "Covered by both A and B.");

        // Round boundary: N+1 -> N+2. A: 1 -> 0, removed. B: 2 -> 1, still active.
        AdvanceRoundBoundary(gameLoop);
        Assert.That(
            gameLoop.IsSmokeCellActive(onlyA),
            Is.False,
            "A expires on its own schedule regardless of B."
        );
        Assert.That(
            gameLoop.IsSmokeCellActive(onlyB),
            Is.True,
            "B is unaffected by A's expiry."
        );
        Assert.That(
            gameLoop.IsSmokeCellActive(shared),
            Is.True,
            "The shared cell stays smoked because B alone still covers it."
        );

        // Round boundary: N+2 -> N+3. B: 1 -> 0, removed.
        AdvanceRoundBoundary(gameLoop);
        Assert.That(gameLoop.IsSmokeCellActive(onlyB), Is.False);
        Assert.That(gameLoop.IsSmokeCellActive(shared), Is.False);
        Assert.That(gameLoop.ActiveSmokeCells, Is.Empty);
    }

    [Test]
    public void ClearActiveSmokeCells_FullyClearsRegardlessOfRemainingRounds()
    {
        GameLoop gameLoop = CreateGameLoop();
        Vector2Int center = new(4, 4);

        RegisterFootprint(gameLoop, center);
        Assert.That(gameLoop.IsSmokeCellActive(center), Is.True, "Precondition.");

        IList deployments = (IList)GetPrivateField(gameLoop, "smokeDeployments");
        Assert.That(deployments.Count, Is.EqualTo(1), "Precondition: one live deployment.");

        // The four match-lifecycle sites (OnNetworkSpawn, OnNetworkDespawn, ResetMatchState,
        // FinishGame) clear unconditionally regardless of how many rounds a deployment has left;
        // this is the shared private method three of them call.
        InvokePrivate(gameLoop, "ClearActiveSmokeCells", false);

        Assert.That(
            gameLoop.IsSmokeCellActive(center),
            Is.False,
            "A match-lifecycle clear must drop everything even with rounds still remaining."
        );
        Assert.That(
            deployments.Count,
            Is.EqualTo(0),
            "The deployment list itself must be emptied, not just its flattened cell cache."
        );
        Assert.That(gameLoop.ActiveSmokeCells, Is.Empty);
    }

    private static void RegisterFootprint(GameLoop gameLoop, Vector2Int center)
    {
        InvokePrivate(gameLoop, "RegisterSmokeFootprintLocal", center);
    }

    private static void AdvanceRoundBoundary(GameLoop gameLoop)
    {
        InvokePrivate(gameLoop, "AdvanceSmokeDeploymentsForRoundBoundary");
    }

    private GameLoop CreateGameLoop()
    {
        GameObject gameObject = new("SmokePersistenceTestGameLoop");
        spawnedObjects.Add(gameObject);
        return gameObject.AddComponent<GameLoop>();
    }

    private static void InvokePrivate(object target, string methodName, params object[] args)
    {
        MethodInfo method = target
            .GetType()
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Missing private method {methodName}.");
        method.Invoke(target, args);
    }

    private static object GetPrivateField(object target, string fieldName)
    {
        FieldInfo field = target
            .GetType()
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing private field {fieldName}.");
        return field.GetValue(target);
    }
}
