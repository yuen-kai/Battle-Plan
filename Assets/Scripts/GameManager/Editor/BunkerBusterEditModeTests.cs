using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// BunkerBuster's wall-clearing pass is IsServer-gated the same way GameLoop.TryDestroyWallCell is
// (see WallDestructionEditModeTests for that constraint), and DestroyWallsInBlast further depends
// on a live GameLoop.Instance, neither of which edit mode can provide. What edit mode *can* exercise
// directly is the pure grid math the blast radius is decided by:
// BunkerBuster.GetWallCellsWithinRadius, a public static helper kept free of any Ability/NetworkBehaviour
// state for exactly this reason, mirroring how DashRush exposes GetSweptCells for testability.
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
        // Restore whatever was active before this fixture ran, so a Scratch map any test below
        // pointed MapCatalog at never leaks into another test file's hardcoded wall-count
        // assertions against the canonical maps.
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
        // The designer's rule: "a set range (unless interrupted by an enemy or wall) ... this range
        // should be 8 for now". The square only aims the shot, so the range must be a standalone
        // constant and must NOT be clamped against the distance to the chosen square -- an earlier
        // version took Mathf.Min(distanceToSquare, range), which made a near square shorten the shot
        // and quietly turned the square back into a destination.
        Assert.That(GetPrivateConst<float>("RocketRangeCells"), Is.EqualTo(8f));
    }

    [Test]
    public void TargetingSpansTheWholeBoardLikeTheSnipersAreaLock()
    {
        UnitData breach = AssetDatabase.LoadAssetAtPath<UnitData>("Assets/UnitStats/Breach.asset");
        UnitData sniper = AssetDatabase.LoadAssetAtPath<UnitData>("Assets/UnitStats/Sniper.asset");
        Assert.That(breach, Is.Not.Null);
        Assert.That(sniper, Is.Not.Null);

        // Same shape as Area Lock: pick anywhere, the square names a direction, and the dodge
        // telegraph is a lane rather than a disc.
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
        // Tuned down on the designer's note ("less big and less damaging"). Grenade is the project's
        // reference blast at 80 damage / 1.6 cells, and the rocket is no longer allowed to dwarf it.
        UnitData breach = AssetDatabase.LoadAssetAtPath<UnitData>("Assets/UnitStats/Breach.asset");
        Assert.That(breach, Is.Not.Null);
        Assert.That(breach.abilityRadius, Is.LessThan(2.2f), "Blast radius was reduced from 2.2.");

        FieldInfo damageField = typeof(BunkerBuster).GetField(
            "damage",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(damageField, Is.Not.Null);
        GameObject host = new("BunkerBusterDamageProbe");
        try
        {
            BunkerBuster ability = host.AddComponent<BunkerBuster>();
            float damage = (float)damageField.GetValue(ability);
            Assert.That(damage, Is.LessThan(90f), "Ability damage was reduced from 90.");
            Assert.That(damage, Is.GreaterThan(0f));
        }
        finally
        {
            Object.DestroyImmediate(host);
        }
    }

    [Test]
    public void BreachsBasicAttackExplodes()
    {
        // "rocket luancher's basic attack should explode" -- and its splash must stay clearly under
        // the ability's, or the ability stops being the bigger event.
        UnitData breach = AssetDatabase.LoadAssetAtPath<UnitData>("Assets/UnitStats/Breach.asset");
        Assert.That(breach, Is.Not.Null);
        Assert.That(breach.bulletExplodesOnImpact, Is.True);
        Assert.That(breach.bulletAoeRadius, Is.GreaterThan(0f));
        Assert.That(breach.bulletAoeRadius, Is.LessThan(breach.abilityRadius));
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
            // but outside the 2.2 circle, which is exactly the corner case a circular test
            // needs to reject and a square-footprint scan would not.
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
        // dx=2, dy=0 is exactly distance 2 from the centre: the "<=" in the circle test must count
        // a cell sitting precisely on the boundary as a hit, not exclude it by a hair.
        Vector2Int center = new(3, 3);
        Vector2Int onBoundary = new(5, 3);
        List<Vector2Int> walls = new() { onBoundary };

        List<Vector2Int> hits = BunkerBuster.GetWallCellsWithinRadius(center, 2f, walls);

        CollectionAssert.Contains(hits, onBoundary);
    }

    [Test]
    public void GetWallCellsWithinRadius_ReadsAgainstGameLoopWallLayoutOnASelfContainedScratchMap()
    {
        // Exactly the WallDestructionEditModeTests discipline: build a private Scratch map rather
        // than mutating a canonical one, point MapCatalog at it for the duration of this test only,
        // and rely on TearDown to hand MapCatalog.Active back to whatever it was before this test ran.
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
