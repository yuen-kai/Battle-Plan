using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Two walls touching only at a corner must not be shootable between. The octagonal collider
/// trims each corner by just enough for a projectile to peek past one wall diagonally, so two
/// trims facing each other leave twice that clearance and the projectile fits. Squaring the
/// shared corner off closes it without costing any single-wall peek.
/// </summary>
[TestFixture]
[Category("WallCornerPeek")]
public class WallDiagonalPinchEditModeTests
{
    private const string WallPrefabPath = "Assets/Prefabs/Map/Wall.prefab";
    private const string BulletPrefabPath = "Assets/Prefabs/Projectiles/BulletBlue.prefab";
    private static readonly Vector2Int LowerCell = new(4, 4);
    private static readonly Vector2Int DiagonalCell = new(5, 5);

    private MapDefinition previousMap;
    private readonly List<GameObject> walls = new();
    private float bulletRadius;

    [SetUp]
    public void SetUp()
    {
        previousMap = MapCatalog.Active;
        GameObject bullet = AssetDatabase.LoadAssetAtPath<GameObject>(BulletPrefabPath);
        Assert.That(bullet, Is.Not.Null, $"Could not load {BulletPrefabPath}.");
        bulletRadius = Shooting.GetProjectileCollisionRadius(bullet);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject wall in walls)
        {
            if (wall != null)
                Object.DestroyImmediate(wall);
        }
        walls.Clear();
        MapCatalog.SetActive(previousMap);
    }

    [Test]
    public void DiagonallyAdjacentWalls_BlockTheShotBetweenThem()
    {
        BuildBoard(LowerCell, DiagonalCell);

        Assert.That(
            SphereCastHitsWall(CellWorld(new Vector2Int(5, 4)), CellWorld(new Vector2Int(4, 5))),
            Is.True,
            "A projectile must not squeeze through the point where two walls touch."
        );
    }

    [Test]
    public void LoneWall_StillAllowsTheDiagonalCornerPeek()
    {
        BuildBoard(LowerCell);

        Assert.That(
            SphereCastHitsWall(CellWorld(new Vector2Int(5, 4)), CellWorld(new Vector2Int(4, 5))),
            Is.False,
            "Squaring off pinched corners must not cost the single-wall diagonal peek."
        );
    }

    [Test]
    public void DestroyingTheDiagonalNeighbour_ReopensTheCornerPeek()
    {
        BuildBoard(LowerCell, DiagonalCell);

        MapCatalog.SetActive(MapDefinition.Scratch(new[] { LowerCell }));
        walls[1].SetActive(false);
        foreach (GameObject wall in walls)
            wall.GetComponent<WallCornerPlugs>().Apply();
        Physics.SyncTransforms();

        Assert.That(
            SphereCastHitsWall(CellWorld(new Vector2Int(5, 4)), CellWorld(new Vector2Int(4, 5))),
            Is.False,
            "The gap a destroyed wall leaves must be as diagonally shootable as any other corner."
        );
    }

    private void BuildBoard(params Vector2Int[] cells)
    {
        MapCatalog.SetActive(MapDefinition.Scratch(cells));
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallPrefabPath);
        Assert.That(prefab, Is.Not.Null, $"Could not load {WallPrefabPath}.");

        foreach (Vector2Int cell in cells)
        {
            GameObject wall = Object.Instantiate(prefab, CellWorld(cell), Quaternion.identity);
            WallCornerPlugs plugs = wall.GetComponent<WallCornerPlugs>();
            Assert.That(plugs, Is.Not.Null, "The wall prefab must carry WallCornerPlugs.");
            plugs.Apply();
            walls.Add(wall);
        }
        Physics.SyncTransforms();
    }

    private static Vector3 CellWorld(Vector2Int cell)
    {
        return new Vector3(cell.x * GameLoop.cellSize, 0f, cell.y * GameLoop.cellSize);
    }

    private bool SphereCastHitsWall(Vector3 start, Vector3 end)
    {
        Vector3 direction = end - start;
        return Physics.SphereCast(
            start,
            bulletRadius,
            direction.normalized,
            out RaycastHit _,
            direction.magnitude,
            LayerMask.GetMask("Walls"),
            QueryTriggerInteraction.Ignore
        );
    }
}
