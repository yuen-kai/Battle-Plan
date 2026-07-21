using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

[TestFixture]
[Category("WallCornerPeek")]
public class WallCornerPeekEditModeTests
{
    private const string WallPrefabPath = "Assets/Prefabs/Map/Wall.prefab";
    private const string ColliderMeshPath = "Assets/Prefabs/Map/WallColliderOctagon.asset";
    private const string BlueBulletPrefabPath = "Assets/Prefabs/Projectiles/BulletBlue.prefab";
    private const string RedBulletPrefabPath = "Assets/Prefabs/Projectiles/BulletRed.prefab";
    private const float CornerChamferWorld = 0.4f;
    private const float Tolerance = 0.001f;
    private static readonly Vector3 TestCenter = new(1000f, 0f, 1000f);

    private GameObject wall;
    private float bulletCollisionRadius;

    [SetUp]
    public void SetUp()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallPrefabPath);
        Assert.That(prefab, Is.Not.Null, $"Could not load {WallPrefabPath}.");
        wall = Object.Instantiate(prefab, TestCenter, Quaternion.identity);
        bulletCollisionRadius = GetProjectileCollisionRadius(BlueBulletPrefabPath);
        Assert.That(
            GetProjectileCollisionRadius(RedBulletPrefabPath),
            Is.EqualTo(bulletCollisionRadius).Within(Tolerance),
            "Both teams must use the same projectile clearance."
        );
        Physics.SyncTransforms();
    }

    [TearDown]
    public void TearDown()
    {
        if (wall != null)
            Object.DestroyImmediate(wall);
    }

    [Test]
    public void WallCollider_IsAFullFaceChamferedOctagonalPrism()
    {
        Assert.That(wall.GetComponent<BoxCollider>(), Is.Null);

        MeshCollider collider = wall.GetComponent<MeshCollider>();
        Assert.That(collider, Is.Not.Null);
        Assert.That(collider.convex, Is.True);
        Assert.That(collider.isTrigger, Is.False);
        Assert.That(collider.sharedMesh, Is.Not.Null);
        Assert.That(wall.layer, Is.EqualTo(LayerMask.NameToLayer("Walls")));
        Assert.That(wall.transform.localScale.x, Is.EqualTo(GameLoop.cellSize).Within(Tolerance));
        Assert.That(wall.transform.localScale.z, Is.EqualTo(GameLoop.cellSize).Within(Tolerance));
        Assert.That(bulletCollisionRadius, Is.EqualTo(0.25f).Within(Tolerance));
        Assert.That(
            AssetDatabase.GetAssetPath(collider.sharedMesh),
            Is.EqualTo(ColliderMeshPath)
        );

        Mesh mesh = collider.sharedMesh;
        Assert.That(mesh.vertexCount, Is.EqualTo(16));
        Assert.That(mesh.bounds.size.x, Is.EqualTo(1f).Within(Tolerance));
        Assert.That(mesh.bounds.size.y, Is.EqualTo(1f).Within(Tolerance));
        Assert.That(mesh.bounds.size.z, Is.EqualTo(1f).Within(Tolerance));

        float expectedInner = 0.5f - CornerChamferWorld / GameLoop.cellSize;
        Assert.That(
            mesh.vertices.Any(vertex =>
                Mathf.Abs(Mathf.Abs(vertex.x) - 0.5f) <= Tolerance
                && Mathf.Abs(Mathf.Abs(vertex.z) - expectedInner) <= Tolerance
            ),
            Is.True,
            "The octagon should retain full-width faces and trim only its corners."
        );
        Assert.That(
            mesh.vertices.Any(vertex =>
                Mathf.Abs(Mathf.Abs(vertex.x) - 0.5f) <= Tolerance
                && Mathf.Abs(Mathf.Abs(vertex.z) - 0.5f) <= Tolerance
            ),
            Is.False,
            "No collider vertex should retain a square corner."
        );
    }

    [Test]
    public void WallCollider_BlocksStraightShotsButAllowsSingleCornerPeeks()
    {
        Vector3 straightStart = TestCenter + Vector3.left * GameLoop.cellSize;
        Vector3 straightEnd = TestCenter + Vector3.right * GameLoop.cellSize;
        Assert.That(
            SphereCastHitsWall(straightStart, straightEnd),
            Is.True,
            "Straight fire through the wall face must remain blocked."
        );

        Vector3 diagonalStart = TestCenter + Vector3.left * GameLoop.cellSize;
        Vector3 diagonalEnd = TestCenter + Vector3.forward * GameLoop.cellSize;
        Assert.That(
            SphereCastHitsWall(diagonalStart, diagonalEnd),
            Is.False,
            "A bullet-sized projectile should clear one chamfered wall corner diagonally."
        );
    }

    [Test]
    public void WallCollider_ShallowCornerLaneUsesProjectileClearanceNotThinRay()
    {
        Vector3 start = TestCenter + Vector3.back * (2f * GameLoop.cellSize);
        Vector3 end = TestCenter + (Vector3.right + Vector3.forward) * GameLoop.cellSize;

        Assert.That(
            RaycastHitsWall(start, end),
            Is.False,
            "This fixture must exercise a lane that a zero-radius targeting ray can clear."
        );
        Assert.That(
            SphereCastHitsWall(start, end),
            Is.True,
            "Targeting must reject a chamfer lane that the real projectile radius cannot clear."
        );
    }

    [Test]
    public void DiagonalTargeting_RemainsBoundedByFogVisibility()
    {
        Vector2Int viewer = new(3, 3);
        Vector2Int target = new(8, 7);
        HashSet<Vector2Int> visibleCells = GridSystem.ComputeVisibleCells(
            new[] { (viewer, 7) }
        );

        Assert.That(visibleCells.Contains(target), Is.False);
        Assert.That(
            GameLoop.IsTargetObservable(true, visibleCells, target, false),
            Is.False,
            "A physics corner peek must not acquire a cell hidden by authoritative fog."
        );
        Assert.That(GameLoop.IsTargetObservable(true, visibleCells, target, true), Is.True);
        Assert.That(GameLoop.IsTargetObservable(false, visibleCells, target, false), Is.True);
    }

    private bool SphereCastHitsWall(Vector3 start, Vector3 end)
    {
        Vector3 direction = end - start;
        return Physics.SphereCast(
            start,
            bulletCollisionRadius,
            direction.normalized,
            out RaycastHit hit,
            direction.magnitude,
            LayerMask.GetMask("Walls"),
            QueryTriggerInteraction.Ignore
        ) && hit.collider.transform.root.gameObject == wall;
    }

    private bool RaycastHitsWall(Vector3 start, Vector3 end)
    {
        Vector3 direction = end - start;
        return Physics.Raycast(
            start,
            direction.normalized,
            out RaycastHit hit,
            direction.magnitude,
            LayerMask.GetMask("Walls"),
            QueryTriggerInteraction.Ignore
        ) && hit.collider.transform.root.gameObject == wall;
    }

    private static float GetProjectileCollisionRadius(string prefabPath)
    {
        GameObject projectile = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Assert.That(projectile, Is.Not.Null, $"Could not load {prefabPath}.");
        SphereCollider collider = projectile.GetComponent<SphereCollider>();
        Assert.That(collider, Is.Not.Null, $"{prefabPath} must have a SphereCollider.");
        return Shooting.GetProjectileCollisionRadius(projectile);
    }
}
