using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

[TestFixture]
[Category("ArcSurge")]
public class ArcSurgeEditModeTests
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
        networkManagerHost = new GameObject("ArcSurgeEditModeTestsNetworkManager");
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
    public void StunSeconds_IsShorterThanDashRushsKnockbackStunPerTheDesignersLighterStunDirection()
    {
        Assert.That(ArcSurge.StunSeconds, Is.GreaterThan(0f));
        Assert.That(
            ArcSurge.StunSeconds,
            Is.LessThan(DashRush.KnockbackStunSeconds),
            "A zap's stun is 'some stun', not the knockback-grade interrupt DashRush's own "
                + "designer-corrected constant already owns -- it must read as lighter."
        );
    }

    [Test]
    public void MaxTargets_IsCappedAtFive()
    {
        FieldInfo field = typeof(ArcSurge).GetField(
            "MaxTargets",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null, "Missing private const MaxTargets.");
        Assert.That((int)field.GetValue(null), Is.EqualTo(5));
    }

    [Test]
    public void Damage_DefaultsToSeventy()
    {
        GameObject caster = new("ArcSurgeDamageCaster");
        spawnedObjects.Add(caster);
        ArcSurge ability = caster.AddComponent<ArcSurge>();

        SerializedProperty damage = new SerializedObject(ability).FindProperty("damage");
        Assert.That(damage, Is.Not.Null, "Missing serialized 'damage' field.");
        Assert.That(damage.floatValue, Is.EqualTo(70f).Within(Tolerance));
    }

    private static readonly Vector3[] SevenDistinctDirections =
    {
        new Vector3(1f, 0f, 0f),
        new Vector3(0f, 0f, 1f),
        new Vector3(-1f, 0f, 0f),
        new Vector3(0f, 0f, -1f),
        new Vector3(1f, 0f, 1f).normalized,
        new Vector3(-1f, 0f, 1f).normalized,
        new Vector3(1f, 0f, -1f).normalized,
    };

    [Test]
    public void ResolveTargets_CapsAtFiveNearestEnemiesEvenWithMoreInRange()
    {
        Vector3 casterPosition = new(1000f, 0f, 1000f);
        ArcSurge arcSurge = CreateCaster(casterPosition);

        List<GameObject> enemies = new();
        for (int index = 1; index <= SevenDistinctDirections.Length; index++)
        {
            Vector3 position =
                casterPosition
                + SevenDistinctDirections[index - 1] * (index * GameLoop.cellSize);
            enemies.Add(CreateEnemyCollider(position));
        }

        Physics.SyncTransforms();

        List<GameObject> targets = arcSurge.ResolveTargets(casterPosition, radiusCells: 10f);

        Assert.That(targets.Count, Is.EqualTo(5), "A single cast may reach at most five enemies.");
        for (int index = 0; index < 5; index++)
        {
            Assert.That(
                targets,
                Contains.Item(enemies[index]),
                $"Enemy #{index + 1} is among the five nearest and must be included."
            );
        }
        for (int index = 5; index < enemies.Count; index++)
        {
            Assert.That(
                targets.Contains(enemies[index]),
                Is.False,
                $"Enemy #{index + 1} is the {index + 1}th nearest and must fall outside the cap."
            );
        }
    }

    [Test]
    public void ResolveTargets_DoesNotHitAnEnemyBehindAWallButStillHitsAClearOne()
    {
        Vector3 casterPosition = new(2000f, 0f, 2000f);
        ArcSurge arcSurge = CreateCaster(casterPosition);

        Vector3 wallPosition = casterPosition + new Vector3(GameLoop.cellSize, 0f, 0f);
        Vector3 blockedEnemyPosition = casterPosition + new Vector3(2f * GameLoop.cellSize, 0f, 0f);
        Vector3 clearEnemyPosition = casterPosition + new Vector3(0f, 0f, 2f * GameLoop.cellSize);

        GameObject wallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallPrefabPath);
        Assert.That(wallPrefab, Is.Not.Null, $"Could not load {WallPrefabPath}.");
        GameObject wall = Object.Instantiate(wallPrefab, wallPosition, Quaternion.identity);
        spawnedObjects.Add(wall);
        wall.transform.position += Helper.heightOffset(wall.transform);

        GameObject blockedEnemy = CreateEnemyCollider(blockedEnemyPosition);
        GameObject clearEnemy = CreateEnemyCollider(clearEnemyPosition);

        Physics.SyncTransforms();

        List<GameObject> targets = arcSurge.ResolveTargets(casterPosition, radiusCells: 5f);

        Assert.That(
            targets.Contains(blockedEnemy),
            Is.False,
            "The wall standing between the caster and this enemy must stop the zap from reaching it."
        );
        Assert.That(
            targets,
            Contains.Item(clearEnemy),
            "An enemy with a clear line of sight must still be reachable."
        );
    }

    [Test]
    public void ResolveTargets_IncludesTheDisplayedRadiusBoundaryRegardlessOfColliderHeight()
    {
        Vector3 casterPosition = new(3000f, 0f, 3000f);
        ArcSurge arcSurge = CreateCaster(casterPosition);
        GameObject boundaryEnemy = CreateEnemyCollider(
            casterPosition + new Vector3(3f * GameLoop.cellSize, 1f, 0f)
        );

        Physics.SyncTransforms();

        Assert.That(
            arcSurge.ResolveTargets(casterPosition, radiusCells: 3f),
            Contains.Item(boundaryEnemy)
        );
    }

    [Test]
    public void ResolveTargets_EnemiesDoNotBlockArcsToEachOther()
    {
        Vector3 casterPosition = new(3500f, 0f, 3500f);
        ArcSurge arcSurge = CreateCaster(casterPosition);
        GameObject nearEnemy = CreateEnemyCollider(
            casterPosition + Vector3.forward * GameLoop.cellSize
        );
        GameObject farEnemy = CreateEnemyCollider(
            casterPosition + Vector3.forward * 3f * GameLoop.cellSize
        );

        Physics.SyncTransforms();

        List<GameObject> targets = arcSurge.ResolveTargets(casterPosition, radiusCells: 3f);
        Assert.That(targets, Contains.Item(nearEnemy));
        Assert.That(targets, Contains.Item(farEnemy));
    }

    [Test]
    public void ResolveTargets_ReturnsOneUnitForMultipleChildColliders()
    {
        Vector3 casterPosition = new(4000f, 0f, 4000f);
        ArcSurge arcSurge = CreateCaster(casterPosition);
        GameObject enemy = new("ArcSurgeMultiColliderEnemy");
        spawnedObjects.Add(enemy);
        enemy.transform.position = casterPosition + Vector3.right * GameLoop.cellSize;
        enemy.AddComponent<Unit>();

        int teamLayer = LayerMask.NameToLayer(RedTeamLayer);
        for (int index = 0; index < 2; index++)
        {
            GameObject colliderObject = new($"Collider{index}") { layer = teamLayer };
            colliderObject.transform.SetParent(enemy.transform, false);
            colliderObject.transform.localPosition = Vector3.up * index * 0.25f;
            colliderObject.AddComponent<SphereCollider>();
        }

        Physics.SyncTransforms();

        List<GameObject> targets = arcSurge.ResolveTargets(casterPosition, radiusCells: 3f);
        Assert.That(targets, Is.EqualTo(new[] { enemy }));
    }

    [Test]
    public void ApplyZap_DamagesAndStunsOnlyTheTargetsItWasGiven()
    {
        GameObject caster = new("ArcSurgeZapCaster");
        spawnedObjects.Add(caster);
        ArcSurge arcSurge = caster.AddComponent<ArcSurge>();

        GameObject hitTarget = CreateZapTarget("ArcSurgeHitTarget");
        GameObject untouchedTarget = CreateZapTarget("ArcSurgeUntouchedTarget");

        arcSurge.ApplyZap(new List<GameObject> { hitTarget });

        Health hitHealth = hitTarget.GetComponent<Health>();
        Unit hitUnit = hitTarget.GetComponent<Unit>();
        Assert.That(
            hitHealth.CurrentHealth,
            Is.EqualTo(StartingHealth - 70f).Within(Tolerance),
            "A resolved target must take the ability's damage."
        );
        Assert.That(hitUnit.IsStunned, Is.True, "A resolved target must be stunned.");

        Health untouchedHealth = untouchedTarget.GetComponent<Health>();
        Unit untouchedUnit = untouchedTarget.GetComponent<Unit>();
        Assert.That(
            untouchedHealth.CurrentHealth,
            Is.EqualTo(StartingHealth).Within(Tolerance),
            "A target ApplyZap was never given must be left untouched."
        );
        Assert.That(untouchedUnit.IsStunned, Is.False);
    }

    private ArcSurge CreateCaster(Vector3 position)
    {
        GameObject caster = new("ArcSurgeCaster");
        spawnedObjects.Add(caster);
        caster.tag = BlueTeamTag;
        caster.transform.position = position;
        return caster.AddComponent<ArcSurge>();
    }

    private GameObject CreateEnemyCollider(Vector3 position)
    {
        GameObject enemy = new($"ArcSurgeEnemy_{spawnedObjects.Count}");
        spawnedObjects.Add(enemy);
        enemy.transform.position = position;
        enemy.AddComponent<SphereCollider>();

        int teamLayer = LayerMask.NameToLayer(RedTeamLayer);
        Assert.That(teamLayer, Is.GreaterThanOrEqualTo(0), $"Missing physics layer '{RedTeamLayer}'.");
        enemy.layer = teamLayer;
        return enemy;
    }

    private GameObject CreateZapTarget(string name)
    {
        GameObject target = new(name);
        spawnedObjects.Add(target);

        Health health = target.AddComponent<Health>();
        Unit unit = target.AddComponent<Unit>();
        target.AddComponent<Movement>();
        target.AddComponent<Shooting>();

        SetIsServer(health, true);
        SetIsServer(unit, true);
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
