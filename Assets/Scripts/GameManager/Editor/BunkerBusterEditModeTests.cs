using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

[TestFixture]
[Category("BunkerBuster")]
public class BunkerBusterEditModeTests
{
    private MapDefinition originalActiveMap;

    [SetUp]
    public void SetUp()
    {
        originalActiveMap = MapCatalog.Active;
    }

    [TearDown]
    public void TearDown()
    {
        MapCatalog.SetActive(originalActiveMap);
    }

    private static T GetPrivateConst<T>(string name)
    {
        FieldInfo field = typeof(BunkerBuster).GetField(
            name,
            BindingFlags.Static | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null, $"BunkerBuster.{name} is missing.");
        return (T)field.GetValue(null);
    }

    [Test]
    public void TheRocketFliesAFixedEightCellsRatherThanToThePickedSquare()
    {
        Assert.That(GetPrivateConst<float>("RocketRangeCells"), Is.EqualTo(8f));
    }

    [Test]
    public void TargetingSpansTheWholeBoardLikeTheSnipersAreaLock()
    {
        UnitData breach = AssetDatabase.LoadAssetAtPath<UnitData>("Assets/UnitStats/Breach.asset");
        UnitData sniper = AssetDatabase.LoadAssetAtPath<UnitData>("Assets/UnitStats/Sniper.asset");
        Assert.That(breach, Is.Not.Null);
        Assert.That(sniper, Is.Not.Null);

        Assert.That(breach.abilitySquareRange, Is.EqualTo(sniper.abilitySquareRange));
        Assert.That(
            breach.abilitySquareRange,
            Is.GreaterThan(GridSystem.ColumnCount + GridSystem.RowCount),
            "Whole-board selection means the range cannot be a real distance limit."
        );
        Assert.That(breach.selectAbilityDirection, Is.False);
        Assert.That(breach.responseDistLine, Is.True, "A flat rocket threatens a lane, not a disc.");
        Assert.That(
            breach.CanTargetOwnCell,
            Is.False,
            "The caster's own cell names no direction, so it must be refused."
        );
    }

    [Test]
    public void TheAbilityIsSmallerAndWeakerThanAGrenade()
    {
        UnitData breach = AssetDatabase.LoadAssetAtPath<UnitData>("Assets/UnitStats/Breach.asset");
        Assert.That(breach, Is.Not.Null);
        Assert.That(breach.abilityRadius, Is.LessThan(2.2f), "Blast radius was reduced from 2.2.");

        FieldInfo damageField = typeof(BunkerBuster).GetField(
            "damage",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(damageField, Is.Not.Null);
        BunkerBuster ability = breach.unitModel.GetComponent<BunkerBuster>();
        Assert.That(ability, Is.Not.Null, "Breach prefab is missing BunkerBuster.");
        float damage = (float)damageField.GetValue(ability);
        Assert.That(damage, Is.EqualTo(55f).Within(0.001f));
    }

    [Test]
    public void BreachsBasicAttackExplodes()
    {
        UnitData breach = AssetDatabase.LoadAssetAtPath<UnitData>("Assets/UnitStats/Breach.asset");
        Assert.That(breach, Is.Not.Null);
        Assert.That(breach.bulletExplodesOnImpact, Is.True);
        Assert.That(breach.bulletAoeRadius, Is.GreaterThan(0f));
        Assert.That(breach.bulletAoeRadius, Is.LessThan(breach.abilityRadius));
    }

    [Test]
    public void RocketReplicatesItsServerAuthoritativeFlight()
    {
        GameObject rocket = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Projectiles/Rocket.prefab"
        );

        Assert.That(rocket, Is.Not.Null);
        Assert.That(rocket.GetComponent<NetworkTransform>(), Is.Not.Null);
    }

    [Test]
    public void GetWallCellsWithinRadius_IncludesCellsInsideTheCircleAndExcludesOutside()
    {
        Vector2Int center = new(5, 5);
        var walls = new List<Vector2Int>
        {
            center, // the impact cell itself, distance 0
            new(7, 5), // distance 2, inside a radius of 2.2
            new(5, 8), // distance 3, outside
            new(7, 7), // dx=2, dy=2 -> distance ~2.83: inside a *square* footprint of radius 2
        };

        List<Vector2Int> hits = BunkerBuster.GetWallCellsWithinRadius(center, 2.2f, walls);

        CollectionAssert.Contains(hits, center);
        CollectionAssert.Contains(hits, new Vector2Int(7, 5));
        CollectionAssert.DoesNotContain(hits, new Vector2Int(5, 8));
        CollectionAssert.DoesNotContain(
            hits,
            new Vector2Int(7, 7),
            "A corner cell within the square footprint but outside the circle must be excluded."
        );
    }

    [Test]
    public void GetWallCellsWithinRadius_ReturnsEmptyForANegativeRadius()
    {
        List<Vector2Int> walls = new() { new Vector2Int(0, 0), new Vector2Int(1, 1) };

        List<Vector2Int> hits = BunkerBuster.GetWallCellsWithinRadius(Vector2Int.zero, -1f, walls);

        Assert.That(hits, Is.Empty);
    }

    [Test]
    public void GetWallCellsWithinRadius_ReturnsEmptyForANullWallSet()
    {
        List<Vector2Int> hits = BunkerBuster.GetWallCellsWithinRadius(Vector2Int.zero, 3f, null);

        Assert.That(hits, Is.Empty);
    }

    [Test]
    public void GetWallCellsWithinRadius_IsExactAtTheRadiusBoundary()
    {
        Vector2Int center = new(3, 3);
        Vector2Int onBoundary = new(5, 3);
        List<Vector2Int> walls = new() { onBoundary };

        List<Vector2Int> hits = BunkerBuster.GetWallCellsWithinRadius(center, 2f, walls);

        CollectionAssert.Contains(hits, onBoundary);
    }

    [Test]
    public void GetWallCellsWithinRadius_ReadsAgainstGameLoopWallLayoutOnASelfContainedScratchMap()
    {
        Vector2Int center = new(5, 5);
        Vector2Int insideBlast = new(7, 5);
        Vector2Int outsideBlast = new(5, 8);
        MapCatalog.SetActive(
            MapDefinition.Scratch(new HashSet<Vector2Int> { center, insideBlast, outsideBlast })
        );

        List<Vector2Int> hits = BunkerBuster.GetWallCellsWithinRadius(
            center,
            2.2f,
            GameLoop.wallLayout
        );

        CollectionAssert.Contains(hits, center);
        CollectionAssert.Contains(hits, insideBlast);
        CollectionAssert.DoesNotContain(hits, outsideBlast);
    }
}
