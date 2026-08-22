using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

[TestFixture]
[Category("BulletExtensions")]
public class BulletExtensionsEditModeTests
{
    private const string EnemyTeamLayer = "BlueTeam";
    private const string WallsLayer = "Walls";

    private readonly List<GameObject> spawnedObjects = new();
    private GameObject networkManagerHost;

    [SetUp]
    public void SetUp()
    {
        // Health.TakeDamage is IsServer-gated. Gives NetworkManager.Singleton a safe, never-started
        // instance the same way StunEditModeTests does, so the reflected IsServer below has
        // something to resolve against.
        networkManagerHost = new GameObject("BulletExtensionsEditModeTestsNetworkManager");
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

    private static void SetSingleton(NetworkManager manager)
    {
        PropertyInfo property = typeof(NetworkManager).GetProperty(
            "Singleton",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
        );
        Assert.That(property, Is.Not.Null, "Missing NetworkManager.Singleton property.");
        property.SetValue(null, manager);
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

    /// <summary>
    /// A live Health with a starting HP set directly on its NetworkVariable (OnNetworkSpawn, which
    /// would normally seed it from UnitData, never runs on a bare AddComponent outside Play mode)
    /// and IsServer forced true so TakeDamage's server gate passes.
    /// </summary>
    private static Health AddLivingHealth(GameObject target, float startingHealth)
    {
        Health health = target.AddComponent<Health>();
        SetIsServer(health, true);
        FieldInfo field = typeof(Health).GetField(
            "currentHealth",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        Assert.That(field, Is.Not.Null, "Missing Health.currentHealth.");
        ((NetworkVariable<float>)field.GetValue(health)).Value = startingHealth;
        return health;
    }

    private GameObject Track(GameObject instance)
    {
        spawnedObjects.Add(instance);
        return instance;
    }

    private static void InvokeApplyDirectHitDamage(Bullet bullet, GameObject hitObject)
    {
        MethodInfo method = typeof(Bullet).GetMethod(
            "ApplyDirectHitDamage",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        Assert.That(method, Is.Not.Null, "Missing Bullet.ApplyDirectHitDamage.");
        method.Invoke(bullet, new object[] { hitObject });
    }

    private static void InvokeResolveAreaImpact(Bullet bullet, Vector3 impactPosition)
    {
        MethodInfo method = typeof(Bullet).GetMethod(
            "ResolveAreaImpact",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        Assert.That(method, Is.Not.Null, "Missing Bullet.ResolveAreaImpact.");
        method.Invoke(bullet, new object[] { impactPosition });
    }

    private static HashSet<GameObject> GetHitTargets(Bullet bullet)
    {
        FieldInfo field = typeof(Bullet).GetField(
            "hitTargets",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        Assert.That(field, Is.Not.Null, "Missing Bullet.hitTargets.");
        return (HashSet<GameObject>)field.GetValue(bullet);
    }

    private static Bullet CreateBullet(GameObject owner, Vector3 position)
    {
        Bullet bullet = owner.AddComponent<Bullet>();
        owner.transform.position = position;
        return bullet;
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ApplyDirectHitDamage_UsesBackstabAdjustedDamage(bool pierces)
    {
        GameObject bulletObject = Track(new GameObject("TestBullet"));
        Bullet bullet = CreateBullet(bulletObject, Vector3.zero);

        GameObject frontHitTarget = Track(new GameObject("FrontHitTarget"));
        frontHitTarget.transform.position = new Vector3(0f, 0f, 5f);
        // Facing back toward the shooter: not a backstab.
        frontHitTarget.transform.rotation = Quaternion.LookRotation(Vector3.back);
        Health frontHitHealth = AddLivingHealth(frontHitTarget, startingHealth: 100f);

        bullet.Initialize(
            velocity: Vector3.zero,
            shotDamage: 10f,
            shotBackstabMultiplier: 3f,
            shotRange: 100f,
            shotBackstabAngle: 90f,
            damageableTeam: EnemyTeamLayer,
            authoritative: true,
            shotExplodesOnImpact: false,
            shotAoeRadius: 0f,
            shotPierces: pierces
        );

        InvokeApplyDirectHitDamage(bullet, frontHitTarget);

        Assert.That(
            frontHitHealth.CurrentHealth,
            Is.EqualTo(90f).Within(0.001f),
            "A front hit takes the base damage, no backstab multiplier."
        );

        // Now a backstab: facing away from the shooter along the bullet's own travel direction.
        GameObject backstabTarget = Track(new GameObject("BackstabTarget"));
        backstabTarget.transform.position = new Vector3(0f, 0f, 5f);
        backstabTarget.transform.rotation = Quaternion.LookRotation(Vector3.forward);
        Health backstabHealth = AddLivingHealth(backstabTarget, startingHealth: 100f);

        Bullet secondBullet = CreateBullet(Track(new GameObject("SecondTestBullet")), Vector3.zero);
        secondBullet.Initialize(
            velocity: Vector3.zero,
            shotDamage: 10f,
            shotBackstabMultiplier: 3f,
            shotRange: 100f,
            shotBackstabAngle: 90f,
            damageableTeam: EnemyTeamLayer,
            authoritative: true,
            shotExplodesOnImpact: false,
            shotAoeRadius: 0f,
            shotPierces: pierces
        );

        InvokeApplyDirectHitDamage(secondBullet, backstabTarget);

        Assert.That(
            backstabHealth.CurrentHealth,
            Is.EqualTo(70f).Within(0.001f),
            "A backstab must take damage * backstabMultiplier (10 * 3 = 30 off 100)."
        );
    }

    // === Item 1: AoE splash mirrors Grenade.ExplodeGrenade's overlap + line-of-sight pattern ===

    [Test]
    public void ResolveAreaImpact_DamagesClearEnemiesButSkipsOnesBlockedByAWall()
    {
        int enemyLayer = LayerMask.NameToLayer(EnemyTeamLayer);
        int wallLayer = LayerMask.NameToLayer(WallsLayer);
        Assert.That(enemyLayer, Is.GreaterThanOrEqualTo(0), $"Missing the {EnemyTeamLayer} layer.");
        Assert.That(wallLayer, Is.GreaterThanOrEqualTo(0), $"Missing the {WallsLayer} layer.");

        Vector3 impactPosition = new(2000f, 0f, 2000f);

        GameObject clearEnemy = Track(CreateSphereCollider("ClearEnemy", enemyLayer));
        clearEnemy.transform.position = impactPosition + new Vector3(1f, 0f, 0f);
        Health clearEnemyHealth = AddLivingHealth(clearEnemy, 100f);

        GameObject blockedEnemy = Track(CreateSphereCollider("BlockedEnemy", enemyLayer));
        blockedEnemy.transform.position = impactPosition + new Vector3(0f, 0f, 4f);
        Health blockedEnemyHealth = AddLivingHealth(blockedEnemy, 100f);

        GameObject blockingWall = Track(CreateBoxCollider("BlockingWall", wallLayer));
        blockingWall.transform.position = impactPosition + new Vector3(0f, 0f, 2f);
        blockingWall.transform.localScale = new Vector3(3f, 3f, 0.2f);

        Physics.SyncTransforms();

        GameObject bulletObject = Track(new GameObject("AoeTestBullet"));
        Bullet bullet = CreateBullet(bulletObject, impactPosition);
        bullet.Initialize(
            velocity: Vector3.zero,
            shotDamage: 10f,
            shotBackstabMultiplier: 1f,
            shotRange: 100f,
            shotBackstabAngle: 90f,
            damageableTeam: EnemyTeamLayer,
            authoritative: true,
            shotExplodesOnImpact: true,
            shotAoeRadius: 5f,
            shotPierces: false
        );

        InvokeResolveAreaImpact(bullet, impactPosition);

        Assert.That(clearEnemyHealth.CurrentHealth, Is.EqualTo(90f).Within(0.001f));
        Assert.That(blockedEnemyHealth.CurrentHealth, Is.EqualTo(100f).Within(0.001f));
    }

    [Test]
    public void ResolveAreaImpact_DoesNotDoubleCountATargetAlreadyCreditedAsTheDirectHit()
    {
        int enemyLayer = LayerMask.NameToLayer(EnemyTeamLayer);
        Assert.That(enemyLayer, Is.GreaterThanOrEqualTo(0));

        Vector3 impactPosition = new(2100f, 0f, 2100f);
        GameObject directHitEnemy = Track(CreateSphereCollider("DirectHitEnemy", enemyLayer));
        directHitEnemy.transform.position = impactPosition;
        Health directHitHealth = AddLivingHealth(directHitEnemy, 100f);

        GameObject bulletObject = Track(new GameObject("AoeDedupeTestBullet"));
        Bullet bullet = CreateBullet(bulletObject, impactPosition);
        bullet.Initialize(
            velocity: Vector3.zero,
            shotDamage: 10f,
            shotBackstabMultiplier: 1f,
            shotRange: 100f,
            shotBackstabAngle: 90f,
            damageableTeam: EnemyTeamLayer,
            authoritative: true,
            shotExplodesOnImpact: true,
            shotAoeRadius: 5f,
            shotPierces: false
        );

        // The direct target is credited before splash targets are resolved.
        GetHitTargets(bullet).Add(directHitEnemy);
        InvokeApplyDirectHitDamage(bullet, directHitEnemy);
        Physics.SyncTransforms();

        InvokeResolveAreaImpact(bullet, impactPosition);

        Assert.That(directHitHealth.CurrentHealth, Is.EqualTo(90f).Within(0.001f));
    }

    private static GameObject CreateSphereCollider(string name, int layer)
    {
        GameObject instance = new(name) { layer = layer };
        instance.AddComponent<SphereCollider>();
        return instance;
    }

    private static GameObject CreateBoxCollider(string name, int layer)
    {
        GameObject instance = new(name) { layer = layer };
        instance.AddComponent<BoxCollider>();
        return instance;
    }
}
