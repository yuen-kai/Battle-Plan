using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

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
        Assert.That(damage.floatValue, Is.EqualTo(60f).Within(Tolerance));

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
            Is.EqualTo(StartingHealth - 60f).Within(Tolerance),
            "A resolved target must take the ability's damage."
        );

        Health untouchedHealth = untouchedTarget.GetComponent<Health>();
        Assert.That(
            untouchedHealth.CurrentHealth,
            Is.EqualTo(StartingHealth).Within(Tolerance),
            "A target ApplyLandingDamage was never given must be left untouched."
        );
    }

    [Test]
    public void InterruptionRestoresTheCasterToASafeGroundedState()
    {
        GameObject caster = new("InterruptedShatterLeapCaster");
        spawnedObjects.Add(caster);
        Movement movement = caster.AddComponent<Movement>();
        Collider unitCollider = caster.AddComponent<CapsuleCollider>();
        ShatterLeap ability = caster.AddComponent<ShatterLeap>();
        Vector3 launchPosition = new(2f, 0.5f, 3f);

        caster.transform.position = new Vector3(4f, 5f, 6f);
        movement.moving = true;
        unitCollider.enabled = false;

        FieldInfo returnPosition = typeof(ShatterLeap).GetField(
            "returnPositionOnInterrupt",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        MethodInfo interrupt = typeof(ShatterLeap).GetMethod(
            "OnAbilityInterrupted",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(returnPosition, Is.Not.Null);
        Assert.That(interrupt, Is.Not.Null);

        returnPosition.SetValue(ability, (Vector3?)launchPosition);
        interrupt.Invoke(ability, null);

        Assert.That(caster.transform.position, Is.EqualTo(launchPosition));
        Assert.That(movement.moving, Is.False);
        Assert.That(unitCollider.enabled, Is.True);
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
