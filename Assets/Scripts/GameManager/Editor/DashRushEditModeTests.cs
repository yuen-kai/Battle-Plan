using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

// DashRush's line-sweep, its ally-boost footprint and the aim preview it shares with the old Shield
// rush are all pure functions of grid state, so they are exercised directly here the same way
// GridSystem's and AbilityKnockback's helpers are elsewhere. The knockback/stun it fires off per
// enemy found and the speed boost it hands each ally are AbilityKnockback's, Unit.ApplyStun's and
// Movement's own responsibilities, covered by their own suites; this file only checks that DashRush
// picks the right units out of the board to hand them to.
[TestFixture]
[Category("DashRush")]
public class DashRushEditModeTests
{
    private const float Tolerance = 0.001f;
    private readonly List<GameObject> spawnedObjects = new();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject spawned in spawnedObjects)
        {
            if (spawned != null)
                UnityEngine.Object.DestroyImmediate(spawned);
        }
        spawnedObjects.Clear();
    }

    [Test]
    public void KnockbackStunSeconds_StaysShortPerTheDesignersCorrection()
    {
        Assert.That(DashRush.KnockbackStunSeconds, Is.GreaterThan(0f));
        Assert.That(
            DashRush.KnockbackStunSeconds,
            Is.LessThan(1f),
            "A dash-through stun must read as a hard, telegraphed interrupt, not a long lockout."
        );
    }

    [Test]
    public void GetSweptCells_WalksACardinalLineFromJustPastStartThroughTheDestination()
    {
        List<Vector2Int> cells = DashRush.GetSweptCells(
            new Vector2Int(2, 2),
            new Vector2Int(5, 2),
            new Vector2Int(1, 0)
        );

        CollectionAssert.AreEqual(
            new[] { new Vector2Int(3, 2), new Vector2Int(4, 2), new Vector2Int(5, 2) },
            cells
        );
    }

    [Test]
    public void GetSweptCells_WalksADiagonalLineTheSameWay()
    {
        List<Vector2Int> cells = DashRush.GetSweptCells(
            new Vector2Int(0, 0),
            new Vector2Int(3, 3),
            new Vector2Int(1, 1)
        );

        CollectionAssert.AreEqual(
            new[] { new Vector2Int(1, 1), new Vector2Int(2, 2), new Vector2Int(3, 3) },
            cells
        );
    }

    [Test]
    public void GetSweptCells_IsEmptyWhenThereIsNoTravel()
    {
        Vector2Int cell = new(4, 4);
        Assert.That(DashRush.GetSweptCells(cell, cell, new Vector2Int(1, 0)), Is.Empty);
        Assert.That(
            DashRush.GetSweptCells(cell, new Vector2Int(6, 4), Vector2Int.zero),
            Is.Empty,
            "A zero direction names no line to sweep."
        );
    }

    [Test]
    public void FindUnitsOnCells_ReturnsOnlyLivingActiveUnitsStandingOnTheSweptCells()
    {
        GameObject onPath = CreateUnitAt(new Vector2Int(3, 2), alive: true, active: true);
        GameObject deadOnPath = CreateUnitAt(new Vector2Int(4, 2), alive: false, active: true);
        GameObject inactiveOnPath = CreateUnitAt(new Vector2Int(5, 2), alive: true, active: false);
        GameObject offPath = CreateUnitAt(new Vector2Int(3, 5), alive: true, active: true);

        List<Vector2Int> sweptCells = new()
        {
            new Vector2Int(3, 2),
            new Vector2Int(4, 2),
            new Vector2Int(5, 2),
        };

        List<GameObject> found = DashRush.FindUnitsOnCells(
            sweptCells,
            new[] { onPath, deadOnPath, inactiveOnPath, offPath }
        );

        CollectionAssert.AreEqual(new[] { onPath }, found);
    }

    [Test]
    public void FindUnitsOnCells_TreatsAUnitWithoutHealthAsLivingWheneverItIsActive()
    {
        GameObject noHealthUnit = new("NoHealthUnit");
        spawnedObjects.Add(noHealthUnit);
        noHealthUnit.transform.position = GameLoop.gridCoordToWorld(new Vector2Int(1, 1));

        List<GameObject> found = DashRush.FindUnitsOnCells(
            new List<Vector2Int> { new(1, 1) },
            new[] { noHealthUnit }
        );

        Assert.That(found, Contains.Item(noHealthUnit));
    }

    [Test]
    public void AllySpeedBoostFootprint_CoversExactlyTheThreeByThreeAroundTheLaunchCell()
    {
        Vector2Int startCell = new(4, 4);
        List<Vector2Int> footprint = GridSystem.GetSquareFootprint(
            startCell,
            DashRush.AllySpeedBoostRadiusCells
        );

        Assert.That(footprint.Count, Is.EqualTo(9));
        Assert.That(footprint, Contains.Item(startCell));
        Assert.That(
            footprint,
            Contains.Item(startCell + new Vector2Int(1, 1)),
            "A 3x3 includes its diagonals, which a circular radius of one would clip."
        );
        Assert.That(
            footprint,
            Has.No.Member(startCell + new Vector2Int(2, 0)),
            "Two cells out is a different ally's problem."
        );
    }

    [Test]
    public void AllySpeedBoost_HurriesEveryAllyInTheFootprintExceptTheChargerItself()
    {
        Vector2Int startCell = new(4, 4);
        GameObject beside = CreateUnitAt(startCell + new Vector2Int(1, 0), alive: true, active: true);
        GameObject diagonal =
            CreateUnitAt(startCell + new Vector2Int(-1, 1), alive: true, active: true);
        GameObject downed = CreateUnitAt(startCell + new Vector2Int(0, 1), alive: false, active: true);
        GameObject faraway = CreateUnitAt(startCell + new Vector2Int(0, 3), alive: true, active: true);
        GameObject charger = CreateUnitAt(startCell, alive: true, active: true);

        List<GameObject> boosted = DashRush.FindUnitsOnCells(
            GridSystem.GetSquareFootprint(startCell, DashRush.AllySpeedBoostRadiusCells),
            new[] { beside, diagonal, downed, faraway, charger }
        );
        boosted.Remove(charger);

        CollectionAssert.AreEquivalent(new[] { beside, diagonal }, boosted);
    }

    [Test]
    public void AllySpeedBoost_HoldsItsMultiplierUntilTheRoundClearsItRatherThanTimingOut()
    {
        const float boostStartedAt = 10f;
        const float baseMoveSpeed = 2f;
        Movement.TimedMoveSpeedBoost speedBoost = new();

        Assert.That(DashRush.AllySpeedBoostMultiplier, Is.EqualTo(1.5f).Within(Tolerance));
        Assert.That(
            speedBoost.TrySetUntilCleared(DashRush.AllySpeedBoostMultiplier, boostStartedAt),
            Is.True
        );
        Assert.That(
            speedBoost.GetEffectiveSpeed(baseMoveSpeed, boostStartedAt - Tolerance),
            Is.EqualTo(baseMoveSpeed).Within(Tolerance),
            "The boost is not retroactive to before the charge launched."
        );
        Assert.That(
            speedBoost.GetEffectiveSpeed(baseMoveSpeed, boostStartedAt + 600f),
            Is.EqualTo(baseMoveSpeed * DashRush.AllySpeedBoostMultiplier).Within(Tolerance),
            "An open-ended boost has no stopwatch to run out."
        );
        Assert.That(speedBoost.IsActive(boostStartedAt + 600f), Is.True);

        speedBoost.Clear();
        Assert.That(
            speedBoost.GetEffectiveSpeed(baseMoveSpeed, boostStartedAt + 600f),
            Is.EqualTo(baseMoveSpeed).Within(Tolerance),
            "The round boundary is what ends it, and it ends immediately when it does."
        );
        Assert.That(speedBoost.IsActive(boostStartedAt + 600f), Is.False);
    }

    [Test]
    public void BuildPlannedPath_PreviewsTheFixedDistanceRushLikeTheOldShieldRushDid()
    {
        UnitData ramrod = LoadUnit("Ramrod");
        Assert.That(ramrod.abilityFixedDistance, Is.GreaterThan(0));

        WithWalls(
            Array.Empty<Vector2Int>(),
            () =>
            {
                GameObject caster = new("DashRushCaster");
                spawnedObjects.Add(caster);
                DashRush dash = caster.AddComponent<DashRush>();
                Vector2Int startCell = new(3, 3);
                caster.transform.position =
                    GameLoop.gridCoordToWorld(startCell) + Helper.heightOffset(caster.transform);

                List<Vector3> points = new();
                AbilityPathKind kind = dash.BuildPlannedPath(
                    GameLoop.gridCoordToWorld(new Vector2Int(4, 3)),
                    ramrod,
                    points
                );

                Assert.That(kind, Is.EqualTo(AbilityPathKind.Ground));
                Assert.That(points.Count, Is.EqualTo(2));
                Assert.That(
                    Vector3.Distance(points[0], GameLoop.gridCoordToWorld(startCell)),
                    Is.LessThan(Tolerance)
                );
                Assert.That(
                    Vector3.Distance(
                        points[1],
                        GameLoop.gridCoordToWorld(
                            startCell + new Vector2Int(ramrod.abilityFixedDistance, 0)
                        )
                    ),
                    Is.LessThan(Tolerance)
                );
            }
        );
    }

    private static UnitData LoadUnit(string unitName)
    {
        string path = $"Assets/UnitStats/{unitName}.asset";
        UnitData data = AssetDatabase.LoadAssetAtPath<UnitData>(path);
        Assert.That(data, Is.Not.Null, $"Could not load {path}.");
        return data;
    }

    /// <summary>Runs a case against a chosen wall layout, restoring the board afterwards.</summary>
    private static void WithWalls(IEnumerable<Vector2Int> walls, Action body)
    {
        MapDefinition original = MapCatalog.Active;
        MapCatalog.SetActive(MapDefinition.Scratch(walls));
        try
        {
            body();
        }
        finally
        {
            MapCatalog.SetActive(original);
        }
    }

    private GameObject CreateUnitAt(Vector2Int cell, bool alive, bool active)
    {
        GameObject unit = new($"Unit_{cell.x}_{cell.y}");
        spawnedObjects.Add(unit);
        unit.transform.position = GameLoop.gridCoordToWorld(cell);
        Health health = unit.AddComponent<Health>();
        SetIsAlive(health, alive);
        unit.SetActive(active);
        return unit;
    }

    private static void SetIsAlive(Health health, bool alive)
    {
        FieldInfo field = typeof(Health).GetField(
            "isAlive",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null, "Missing Health.isAlive field.");
        var isAlive = (NetworkVariable<bool>)field.GetValue(health);
        isAlive.Value = alive;
    }
}
