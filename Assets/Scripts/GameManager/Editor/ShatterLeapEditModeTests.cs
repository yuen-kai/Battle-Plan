using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

// ShatterLeap's ExecuteAbility coroutine flies a lob arc frame by frame and reads IsServer, none of
// which edit mode can drive without a running player loop (see SuppressingFireEditModeTests and
// ChainSurgeEditModeTests for the same constraint on their own coroutines). So, the same way those
// two pull their damage queries out for direct testing, this exercises ShatterLeap's own seams
// instead: FindEnemiesInLandingRadius (the overlap-then-line-of-sight query, mirroring
// SuppressingFire.FindEnemiesInBarrage and ChainSurge.ResolveTargets) and ApplyLandingDamage (the
// damage application, mirroring ChainSurge.ApplyZap). Both are public instance methods on
// ShatterLeap for exactly that reason.
[TestFixture]
[Category("ShatterLeap")]
public class ShatterLeapEditModeTests
{
    private const string WallPrefabPath = "Assets/Prefabs/Map/Wall.prefab";
    private const string BlueTeamTag = "BlueTeam";
    private const string RedTeamLayer = "RedTeam";
    private const float Tolerance = 0.001f;
    private const float StartingHealth = 100f;

    private readonly List<GameObject> spawnedObjects = new();
    private GameObject networkManagerHost;

    [SetUp]
    public void SetUp()
    {
        // Gives NetworkManager.Singleton a safe, never-started instance so NetworkBehaviour.IsServer
        // (read inside Health.TakeDamage via NetworkObject/NetworkManager plumbing) resolves without
        // throwing -- mirrors SuppressingFireEditModeTests' and ChainSurgeEditModeTests' own setup.
        networkManagerHost = new GameObject("ShatterLeapEditModeTestsNetworkManager");
        NetworkManager manager = networkManagerHost.AddComponent<NetworkManager>();
        SetSingleton(manager);
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

        SetSingleton(null);
        Object.DestroyImmediate(networkManagerHost);
    }

    [Test]
    public void DamageAndAreaRadiusDefaults_MatchTheChosenTuning()
    {
        GameObject caster = new("ShatterLeapDefaultsCaster");
        spawnedObjects.Add(caster);
        ShatterLeap ability = caster.AddComponent<ShatterLeap>();

        SerializedProperty damage = new SerializedObject(ability).FindProperty("damage");
        Assert.That(damage, Is.Not.Null, "Missing serialized 'damage' field.");
        Assert.That(damage.floatValue, Is.EqualTo(50f).Within(Tolerance));

        ParameterInfo areaRadiusParameter = typeof(ShatterLeap)
            .GetMethod("ExecuteAbility")
            .GetParameters()[1];
        Assert.That(
            (float)areaRadiusParameter.DefaultValue,
            Is.EqualTo(1.6f).Within(Tolerance),
            "ExecuteAbility's default AreaRadius must match the tuned landing splash."
        );
    }

    [Test]
    public void FindEnemiesInLandingRadius_HitsAClearEnemyInsideTheRadius()
    {
        Vector3 landingPosition = GameLoop.gridCoordToWorld(new Vector2Int(3, 3));
        ShatterLeap ability = CreateCaster(landingPosition);

        GameObject enemy = CreateEnemyCollider(
            landingPosition + new Vector3(0.5f * GameLoop.cellSize, 0f, 0f)
        );
        Physics.SyncTransforms();

        List<GameObject> hitEnemies = ability.FindEnemiesInLandingRadius(landingPosition, 1.6f);

        Assert.That(
            hitEnemies,
            Contains.Item(enemy),
            "An enemy with clear line of sight inside the blast radius must be found."
        );
    }

    [Test]
    public void FindEnemiesInLandingRadius_MissesAnEnemyOutsideTheRadius()
    {
        Vector3 landingPosition = GameLoop.gridCoordToWorld(new Vector2Int(3, 3));
        ShatterLeap ability = CreateCaster(landingPosition);

        // Ten cells out is well past the 1.6-cell default splash.
        GameObject farEnemy = CreateEnemyCollider(
            landingPosition + new Vector3(10f * GameLoop.cellSize, 0f, 0f)
        );
        Physics.SyncTransforms();

        List<GameObject> hitEnemies = ability.FindEnemiesInLandingRadius(landingPosition, 1.6f);

        Assert.That(
            hitEnemies.Contains(farEnemy),
            Is.False,
            "An enemy standing outside the blast radius must not be found."
        );
    }

    [Test]
    public void FindEnemiesInLandingRadius_MissesAnEnemyBehindAWallButStillFindsAClearOne()
    {
        Vector3 landingPosition = GameLoop.gridCoordToWorld(new Vector2Int(3, 3));
        ShatterLeap ability = CreateCaster(landingPosition);

        Vector3 wallPosition = landingPosition + new Vector3(GameLoop.cellSize, 0f, 0f);
        Vector3 blockedEnemyPosition = landingPosition + new Vector3(2f * GameLoop.cellSize, 0f, 0f);
        Vector3 clearEnemyPosition = landingPosition + new Vector3(0f, 0f, 1f * GameLoop.cellSize);

        GameObject wallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallPrefabPath);
        Assert.That(wallPrefab, Is.Not.Null, $"Could not load {WallPrefabPath}.");
        GameObject wall = Object.Instantiate(wallPrefab, wallPosition, Quaternion.identity);
        spawnedObjects.Add(wall);
        wall.transform.position += Helper.heightOffset(wall.transform);

        GameObject blockedEnemy = CreateEnemyCollider(blockedEnemyPosition);
        GameObject clearEnemy = CreateEnemyCollider(clearEnemyPosition);

        Physics.SyncTransforms();

        // A radius wide enough to geometrically reach both candidates, so the wall -- not the
        // radius -- is what has to explain the blocked enemy being left out.
        List<GameObject> hitEnemies = ability.FindEnemiesInLandingRadius(landingPosition, 3f);

        Assert.That(
            hitEnemies.Contains(blockedEnemy),
            Is.False,
            "The wall standing between the landing point and this enemy must block the hit."
        );
        Assert.That(
            hitEnemies,
            Contains.Item(clearEnemy),
            "An enemy with a clear line of sight must still be found."
        );
    }

    [Test]
    public void ApplyLandingDamage_DamagesOnlyTheTargetsItWasGiven()
    {
        GameObject caster = new("ShatterLeapDamageCaster");
        spawnedObjects.Add(caster);
        ShatterLeap ability = caster.AddComponent<ShatterLeap>();

        GameObject hitTarget = CreateDamageTarget("ShatterLeapHitTarget");
        GameObject untouchedTarget = CreateDamageTarget("ShatterLeapUntouchedTarget");

        ability.ApplyLandingDamage(new List<GameObject> { hitTarget });

        Health hitHealth = hitTarget.GetComponent<Health>();
        Assert.That(
            hitHealth.CurrentHealth,
            Is.EqualTo(StartingHealth - 50f).Within(Tolerance),
            "A resolved target must take the ability's damage."
        );

        Health untouchedHealth = untouchedTarget.GetComponent<Health>();
        Assert.That(
            untouchedHealth.CurrentHealth,
            Is.EqualTo(StartingHealth).Within(Tolerance),
            "A target ApplyLandingDamage was never given must be left untouched."
        );
    }

    private ShatterLeap CreateCaster(Vector3 position)
    {
        GameObject caster = new("ShatterLeapCaster");
        spawnedObjects.Add(caster);
        caster.tag = BlueTeamTag;
        caster.transform.position = position;
        return caster.AddComponent<ShatterLeap>();
    }

    private GameObject CreateEnemyCollider(Vector3 position)
    {
        GameObject enemy = new($"ShatterLeapEnemy_{spawnedObjects.Count}");
        spawnedObjects.Add(enemy);
        enemy.transform.position = position;
        enemy.AddComponent<SphereCollider>();

        int teamLayer = LayerMask.NameToLayer(RedTeamLayer);
        Assert.That(teamLayer, Is.GreaterThanOrEqualTo(0), $"Missing physics layer '{RedTeamLayer}'.");
        enemy.layer = teamLayer;
        return enemy;
    }

    private GameObject CreateDamageTarget(string name)
    {
        GameObject target = new(name);
        spawnedObjects.Add(target);

        Health health = target.AddComponent<Health>();
        SetIsServer(health, true);
        SetCurrentHealth(health, StartingHealth);

        return target;
    }

    private static void SetCurrentHealth(Health health, float value)
    {
        FieldInfo field = typeof(Health).GetField(
            "currentHealth",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null, "Missing Health.currentHealth field.");
        var currentHealth = (NetworkVariable<float>)field.GetValue(health);
        currentHealth.Value = value;
    }

    private static void SetIsServer(NetworkBehaviour behaviour, bool value)
    {
        PropertyInfo property = typeof(NetworkBehaviour).GetProperty(
            "IsServer",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        );
        Assert.That(property, Is.Not.Null, "Missing NetworkBehaviour.IsServer property.");
        property.SetValue(behaviour, value);
    }

    private static void SetSingleton(NetworkManager manager)
    {
        PropertyInfo property = typeof(NetworkManager).GetProperty(
            "Singleton",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
        );
        Assert.That(property, Is.Not.Null, "Missing NetworkManager.Singleton property.");
        property.SetValue(null, manager);
    }
}
