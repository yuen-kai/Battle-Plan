using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// GameLoop.TryDestroyWallCell is IsServer-gated, and IsServer is only meaningful on a spawned
// NetworkBehaviour, which edit mode cannot provide (see SmokeScreenEditModeTests for the same
// constraint on TryRegisterSmokeFootprint). These tests instead drive the private helpers the
// gated method delegates to -- ApplyWallDestructionLocal, EnsureWallInstanceRegistry,
// EnsureWallDestructionMap, ClearWallDestructionState -- via reflection on a bare, unspawned
// GameLoop component, which exercises the exact same production code without needing IsServer.
[TestFixture]
[Category("WallDestruction")]
public class WallDestructionEditModeTests
{
    private const string WallPrefabPath = "Assets/Prefabs/Map/Wall.prefab";

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

        // MatchOptions.Reset() re-resolves MapCatalog.Active to the canonical Concourse singleton,
        // so it also undoes any MapCatalog.SetActive(...) a test above pointed at a Scratch clone.
        GameLoop.ResetMatchState();
        MatchOptions.Reset();
    }

    [Test]
    public void TryDestroyWallCell_ExposesAPublicServerAuthoritativeSeam()
    {
        MethodInfo method = typeof(GameLoop).GetMethod(
            nameof(GameLoop.TryDestroyWallCell),
            BindingFlags.Instance | BindingFlags.Public
        );
        Assert.That(method, Is.Not.Null);
        Assert.That(method.IsStatic, Is.False);
        Assert.That(method.ReturnType, Is.EqualTo(typeof(bool)));
        CollectionAssert.AreEqual(
            new[] { typeof(Vector2Int) },
            method.GetParameters().Select(parameter => parameter.ParameterType).ToArray()
        );
    }

    [Test]
    public void ApplyWallDestructionLocal_RemovesCellFromActiveMapWithoutTouchingCanonicalSingleton()
    {
        MapCatalog.SetActive(MapId.Concourse);
        int canonicalWallCountBefore = MapCatalog.Concourse.Walls.Count;
        Vector2Int cellToDestroy = MapCatalog.Concourse.Walls.First();

        GameLoop gameLoop = CreateGameLoop();
        InvokePrivate(gameLoop, "ApplyWallDestructionLocal", cellToDestroy);

        Assert.That(
            MapCatalog.Concourse.Walls.Count,
            Is.EqualTo(canonicalWallCountBefore),
            "The canonical singleton must never lose a wall."
        );
        Assert.That(
            MapCatalog.Concourse.Walls.Contains(cellToDestroy),
            Is.True,
            "The canonical singleton must still carry the cell that was 'destroyed'."
        );
        Assert.That(
            MapCatalog.Active,
            Is.Not.SameAs(MapCatalog.Concourse),
            "Destroying a wall must swap MapCatalog.Active to a private clone."
        );
        Assert.That(
            MapCatalog.Active.Walls.Contains(cellToDestroy),
            Is.False,
            "The active clone must have lost the destroyed cell."
        );
    }

    [Test]
    public void ApplyWallDestructionLocal_SecondCallReusesTheSameCloneInsteadOfMakingAnother()
    {
        MapCatalog.SetActive(MapId.Concourse);
        Vector2Int[] cellsToDestroy = MapCatalog.Concourse.Walls.Take(2).ToArray();
        Assert.That(
            cellsToDestroy.Length,
            Is.EqualTo(2),
            "Precondition: Concourse needs at least two walls."
        );

        GameLoop gameLoop = CreateGameLoop();

        InvokePrivate(gameLoop, "ApplyWallDestructionLocal", cellsToDestroy[0]);
        MapDefinition cloneAfterFirst = MapCatalog.Active;
        InvokePrivate(gameLoop, "ApplyWallDestructionLocal", cellsToDestroy[1]);

        Assert.That(
            MapCatalog.Active,
            Is.SameAs(cloneAfterFirst),
            "A second destruction this match must reuse the same clone, not make another."
        );
        Assert.That(MapCatalog.Active.Walls.Contains(cellsToDestroy[0]), Is.False);
        Assert.That(MapCatalog.Active.Walls.Contains(cellsToDestroy[1]), Is.False);
    }

    [Test]
    public void EnsureWallInstanceRegistry_MapsCellToItsPlacedWallInstance()
    {
        Vector2Int cell = new(1000, 2000);
        GameObject wallInstance = InstantiateWallAtCell(cell);

        GameLoop gameLoop = CreateGameLoop();
        InvokePrivate(gameLoop, "EnsureWallInstanceRegistry");

        var registry = (Dictionary<Vector2Int, GameObject>)
            GetPrivateField(gameLoop, "wallInstancesByCell");
        Assert.That(registry.TryGetValue(cell, out GameObject found), Is.True);
        Assert.That(found, Is.SameAs(wallInstance));
    }

    [Test]
    public void ApplyWallDestructionLocal_DeactivatesThePhysicalWallInstanceAtItsCell()
    {
        Vector2Int cell = new(1000, 2001);
        GameObject wallInstance = InstantiateWallAtCell(cell);
        Assert.That(wallInstance.activeSelf, Is.True, "Precondition: the placed wall starts active.");

        MapCatalog.SetActive(MapDefinition.Scratch(new HashSet<Vector2Int> { cell }));
        GameLoop gameLoop = CreateGameLoop();

        InvokePrivate(gameLoop, "ApplyWallDestructionLocal", cell);

        Assert.That(
            wallInstance.activeSelf,
            Is.False,
            "The physical wall must be deactivated so raycasts stop hitting it."
        );
    }

    [Test]
    public void ApplyWallDestructionLocal_SetsServerFogDirty()
    {
        MapCatalog.SetActive(MapId.Concourse);
        Vector2Int cellToDestroy = MapCatalog.Concourse.Walls.First();

        GameLoop gameLoop = CreateGameLoop();
        SetPrivateField(gameLoop, "serverFogDirty", false);
        Assert.That(GetPrivateField(gameLoop, "serverFogDirty"), Is.EqualTo(false), "Precondition.");

        InvokePrivate(gameLoop, "ApplyWallDestructionLocal", cellToDestroy);

        Assert.That(
            GetPrivateField(gameLoop, "serverFogDirty"),
            Is.EqualTo(true),
            "Destroying a wall must dirty fog so a newly opened sightline gets recomputed."
        );
    }

    [Test]
    public void ClearWallDestructionState_ResetsCloneAndRegistryForANewMatch()
    {
        MapCatalog.SetActive(MapId.Concourse);
        Vector2Int cellToDestroy = MapCatalog.Concourse.Walls.First();
        InstantiateWallAtCell(cellToDestroy);
        GameLoop gameLoop = CreateGameLoop();
        InvokePrivate(gameLoop, "ApplyWallDestructionLocal", cellToDestroy);

        Assert.That(GetPrivateField(gameLoop, "wallDestructionMap"), Is.Not.Null);
        var registryBefore = (Dictionary<Vector2Int, GameObject>)
            GetPrivateField(gameLoop, "wallInstancesByCell");
        Assert.That(registryBefore.Count, Is.GreaterThan(0));

        InvokePrivate(gameLoop, "ClearWallDestructionState");

        Assert.That(GetPrivateField(gameLoop, "wallDestructionMap"), Is.Null);
        var registryAfter = (Dictionary<Vector2Int, GameObject>)
            GetPrivateField(gameLoop, "wallInstancesByCell");
        Assert.That(registryAfter.Count, Is.EqualTo(0));
    }

    private GameLoop CreateGameLoop()
    {
        GameObject gameObject = new("WallDestructionTestGameLoop");
        spawnedObjects.Add(gameObject);
        return gameObject.AddComponent<GameLoop>();
    }

    private GameObject InstantiateWallAtCell(Vector2Int cell)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallPrefabPath);
        Assert.That(prefab, Is.Not.Null, $"Could not load {WallPrefabPath}.");
        Vector3 worldPosition = new(cell.x * GameLoop.cellSize, 0f, cell.y * GameLoop.cellSize);
        GameObject instance = Object.Instantiate(prefab, worldPosition, Quaternion.identity);
        spawnedObjects.Add(instance);
        return instance;
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

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target
            .GetType()
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing private field {fieldName}.");
        field.SetValue(target, value);
    }
}
