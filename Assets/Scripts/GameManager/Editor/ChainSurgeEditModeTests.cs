using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

// ChainSurge's ExecuteAbility coroutine has no yield inside it beyond the IsServer guard, so unlike
// DashRush or SuppressingFire there is no coroutine machinery to work around here -- but the same
// two seams those suites pull out for direct testing exist on ChainSurge for the same reason:
// ResolveTargets (the overlap-then-line-of-sight-then-cap query) and ApplyZap (the damage/stun
// application) are both public precisely so target selection and its effects can be exercised
// without a running NetworkManager driving the coroutine itself.
[TestFixture]
[Category("ChainSurge")]
public class ChainSurgeEditModeTests
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
        // Gives NetworkManager.Singleton a safe, never-started instance so NetworkManager.ServerTime
        // (read inside Unit.ApplyStun and Unit.IsStunned, via NetworkBehaviour.NetworkManager's
        // fallback to NetworkManager.Singleton) resolves to a default NetworkTime instead of
        // throwing -- mirrors StunEditModeTests' and SuppressingFireEditModeTests' own setup.
        networkManagerHost = new GameObject("ChainSurgeEditModeTestsNetworkManager");
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
        Assert.That(ChainSurge.StunSeconds, Is.GreaterThan(0f));
        Assert.That(
            ChainSurge.StunSeconds,
            Is.LessThan(DashRush.KnockbackStunSeconds),
            "A zap's stun is 'some stun', not the knockback-grade interrupt DashRush's own "
                + "designer-corrected constant already owns -- it must read as lighter."
        );
    }

    [Test]
    public void MaxTargets_IsCappedAtFive()
    {
        FieldInfo field = typeof(ChainSurge).GetField(
            "MaxTargets",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null, "Missing private const MaxTargets.");
        Assert.That((int)field.GetValue(null), Is.EqualTo(5));
    }

    [Test]
    public void Damage_DefaultsToFortyFive()
    {
        GameObject caster = new("ChainSurgeDamageCaster");
        spawnedObjects.Add(caster);
        ChainSurge ability = caster.AddComponent<ChainSurge>();

        SerializedProperty damage = new SerializedObject(ability).FindProperty("damage");
        Assert.That(damage, Is.Not.Null, "Missing serialized 'damage' field.");
        Assert.That(damage.floatValue, Is.EqualTo(45f).Within(Tolerance));
    }

    // Seven distinct directions from a caster, no two parallel, so a ray to the farthest of them
    // never happens to pass through a nearer one's own collider -- that self-occlusion is exactly
    // how a real cast should behave (an enemy standing behind another blocks the one behind it, the
    // same as Grenade's blast), but it would wrongly explain away a miss in a test built to isolate
    // the five-target cap on its own.
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
        ChainSurge chainSurge = CreateCaster(casterPosition);

        // Seven enemies, each one cell farther than the last but each out along its own direction
        // -- well within a ten-cell radius, so range alone never explains a miss, and no two ever
        // stand on the same ray so nobody blocks anybody else's line of sight either.
        List<GameObject> enemies = new();
        for (int index = 1; index <= SevenDistinctDirections.Length; index++)
        {
            Vector3 position =
                casterPosition
                + SevenDistinctDirections[index - 1] * (index * GameLoop.cellSize);
            enemies.Add(CreateEnemyCollider(position));
        }

        Physics.SyncTransforms();

        List<GameObject> targets = chainSurge.ResolveTargets(casterPosition, radiusCells: 10f);

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
        // Contrast with Salvo's ability elsewhere in this batch, which deliberately fires through
        // walls: a lightning bolt is not a special case like that one and must be blocked exactly
        // the way Grenade's own blast already is.
        Vector3 casterPosition = new(2000f, 0f, 2000f);
        ChainSurge chainSurge = CreateCaster(casterPosition);

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

        List<GameObject> targets = chainSurge.ResolveTargets(casterPosition, radiusCells: 5f);

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
    public void ApplyZap_DamagesAndStunsOnlyTheTargetsItWasGiven()
    {
        GameObject caster = new("ChainSurgeZapCaster");
        spawnedObjects.Add(caster);
        ChainSurge chainSurge = caster.AddComponent<ChainSurge>();

        GameObject hitTarget = CreateZapTarget("ChainSurgeHitTarget");
        GameObject untouchedTarget = CreateZapTarget("ChainSurgeUntouchedTarget");

        chainSurge.ApplyZap(new List<GameObject> { hitTarget });

        Health hitHealth = hitTarget.GetComponent<Health>();
        Unit hitUnit = hitTarget.GetComponent<Unit>();
        Assert.That(
            hitHealth.CurrentHealth,
            Is.EqualTo(StartingHealth - 45f).Within(Tolerance),
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

    private ChainSurge CreateCaster(Vector3 position)
    {
        GameObject caster = new("ChainSurgeCaster");
        spawnedObjects.Add(caster);
        caster.tag = BlueTeamTag;
        caster.transform.position = position;
        return caster.AddComponent<ChainSurge>();
    }

    private GameObject CreateEnemyCollider(Vector3 position)
    {
        GameObject enemy = new($"ChainSurgeEnemy_{spawnedObjects.Count}");
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
