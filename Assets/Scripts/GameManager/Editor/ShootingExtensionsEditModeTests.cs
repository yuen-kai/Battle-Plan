using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

// Pure coverage for the fire-rate warm-up ramp's interpolation math and the temporary fire-rate
// boost primitive, both of which need no scene, NetworkManager, or Play mode. The IsServer-gated
// entry points are exercised against a bare, unspawned Shooting component with a reflected
// IsServer, the same way StunEditModeTests reflects into NetworkBehaviour-adjacent private state.
// InitiateShooting's coroutine actually consuming these per shot is runtime acceptance and is
// intentionally not exercised here.
[TestFixture]
[Category("ShootingExtensions")]
public class ShootingExtensionsEditModeTests
{
    private const string UnitCatalogPath = "Assets/UnitStats/AllUnits.asset";

    private GameObject networkManagerHost;

    [SetUp]
    public void SetUp()
    {
        // Gives NetworkManager.Singleton a safe, never-started instance so NetworkBehaviour.IsServer
        // (reflected below) has something to resolve against without throwing. Mirrors
        // StunEditModeTests's SetUp for the identical reason.
        networkManagerHost = new GameObject("ShootingExtensionsEditModeTestsNetworkManager");
        NetworkManager manager = networkManagerHost.AddComponent<NetworkManager>();
        SetSingleton(manager);
    }

    [TearDown]
    public void TearDown()
    {
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

    // === Fire-rate warm-up ramp: pure interpolation ===

    [Test]
    public void ComputeRampedShotDelay_DisabledRampAlwaysReturnsTheFloorDelay()
    {
        Assert.That(
            Shooting.ComputeRampedShotDelay(
                shotsFiredThisBurst: 0,
                rampShots: 0,
                rampStartDelay: 1f,
                floorDelay: 0.3f
            ),
            Is.EqualTo(0.3f).Within(0.0001f),
            "rampShots <= 0 is today's constant-delay behavior for every existing unit."
        );
        Assert.That(
            Shooting.ComputeRampedShotDelay(
                shotsFiredThisBurst: 50,
                rampShots: -1,
                rampStartDelay: 1f,
                floorDelay: 0.3f
            ),
            Is.EqualTo(0.3f).Within(0.0001f)
        );
    }

    [Test]
    public void ComputeRampedShotDelay_InterpolatesFromStartDelayDownToTheFloor()
    {
        Assert.That(
            Shooting.ComputeRampedShotDelay(0, rampShots: 4, rampStartDelay: 1f, floorDelay: 0.2f),
            Is.EqualTo(1f).Within(0.0001f),
            "No shots fired yet is the full starting delay."
        );
        Assert.That(
            Shooting.ComputeRampedShotDelay(2, rampShots: 4, rampStartDelay: 1f, floorDelay: 0.2f),
            Is.EqualTo(0.6f).Within(0.0001f),
            "Halfway through the ramp is halfway between start and floor."
        );
        Assert.That(
            Shooting.ComputeRampedShotDelay(4, rampShots: 4, rampStartDelay: 1f, floorDelay: 0.2f),
            Is.EqualTo(0.2f).Within(0.0001f),
            "Reaching rampShots lands exactly on the floor delay."
        );
    }

    [Test]
    public void ComputeRampedShotDelay_ClampsAtTheFloorPastTheConfiguredRampShots()
    {
        Assert.That(
            Shooting.ComputeRampedShotDelay(9, rampShots: 4, rampStartDelay: 1f, floorDelay: 0.2f),
            Is.EqualTo(0.2f).Within(0.0001f),
            "A burst that outlasts the ramp stays pinned at the fully-warmed-up delay."
        );
    }

    // === Temporary fire-rate boost primitive ===

    [Test]
    public void FireRateBoost_ExpiresAndRestoresTheBaseDelay()
    {
        const float boostStartedAt = 10f;
        const float baseDelay = 0.3f;
        const float multiplier = 2f;
        const float duration = 1.5f;
        Shooting.TimedFireRateBoost boost = new();

        Assert.That(boost.TrySet(multiplier, duration, boostStartedAt), Is.True);
        Assert.That(
            boost.GetEffectiveDelay(baseDelay, boostStartedAt - 0.001f),
            Is.EqualTo(baseDelay).Within(0.0001f),
            "Before the window opens the delay is untouched."
        );
        Assert.That(boost.IsActive(boostStartedAt - 0.001f), Is.False);
        Assert.That(
            boost.GetEffectiveDelay(baseDelay, boostStartedAt + 0.5f),
            Is.EqualTo(baseDelay / multiplier).Within(0.0001f),
            "A higher multiplier fires faster, so it divides the delay rather than scaling it."
        );
        Assert.That(boost.IsActive(boostStartedAt + 0.5f), Is.True);
        Assert.That(
            boost.GetEffectiveDelay(baseDelay, boostStartedAt + duration),
            Is.EqualTo(baseDelay).Within(0.0001f),
            "The boost restores the base delay at the exact end of its window."
        );
        Assert.That(boost.IsActive(boostStartedAt + duration), Is.False);
    }

    [Test]
    public void FireRateBoost_TrySetRejectsAMultiplierThatWouldNotSpeedAnythingUp()
    {
        Shooting.TimedFireRateBoost boost = new();

        Assert.That(boost.TrySet(1f, 1f, 0f), Is.False, "A multiplier of 1 boosts nothing.");
        Assert.That(boost.TrySet(0.5f, 1f, 0f), Is.False, "A multiplier below 1 would slow shots.");
        Assert.That(boost.TrySet(2f, 0f, 0f), Is.False, "A non-positive duration is not a window.");
        Assert.That(
            boost.TrySet(float.NaN, 1f, 0f),
            Is.False,
            "NaN inputs must not silently become an active boost."
        );
        Assert.That(boost.TrySet(2f, 1f, 0f), Is.True, "A valid request is still accepted.");
    }

    [Test]
    public void TryApplyTemporaryFireRateBoost_IsServerGated()
    {
        GameObject shooterObject = new("ShootingExtensionsTestShooter");
        try
        {
            Shooting shooting = shooterObject.AddComponent<Shooting>();

            Assert.That(
                shooting.TryApplyTemporaryFireRateBoost(2f, 1f, 0f),
                Is.False,
                "A non-server call must be rejected outright."
            );

            SetIsServer(shooting, true);
            Assert.That(shooting.TryApplyTemporaryFireRateBoost(2f, 1f, 0f), Is.True);

            shooting.ClearTemporaryFireRateBoost();
            Assert.That(
                shooting.TryApplyTemporaryFireRateBoost(2f, 1f, 0f),
                Is.True,
                "Clearing must leave the boost usable again rather than stuck."
            );
        }
        finally
        {
            Object.DestroyImmediate(shooterObject);
        }
    }

    // === Regression guard: every opt-in field on UnitData still defaults to today's behavior ===

    [Test]
    public void UnitData_NewOptInFieldsDefaultOff()
    {
        UnitData data = ScriptableObject.CreateInstance<UnitData>();
        try
        {
            Assert.That(data.fireRateRampShots, Is.EqualTo(0));
            Assert.That(data.fireRateRampStartDelay, Is.EqualTo(0f));
            Assert.That(data.canShootWhileMoving, Is.False);
            Assert.That(data.bulletExplodesOnImpact, Is.False);
            Assert.That(data.bulletAoeRadius, Is.EqualTo(0f));
            Assert.That(data.bulletPierces, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(data);
        }
    }

    // Units that are supposed to carry each opt-in trait, now that real characters use them —
    // updated as each one ships. Anyone NOT named here must still default off, so this stays a
    // regression guard against a trait leaking onto a unit that never asked for it, rather than
    // the "nobody opts in yet" blanket check this test started as.
    private static readonly string[] UnitsWithFireRateRamp = { "Salvo" };
    private static readonly string[] UnitsThatShootWhileMoving = { "Outrider" };
    private static readonly string[] UnitsWithExplodingBullets = { "Breach" };
    private static readonly string[] UnitsWithPiercingBullets = { "Farsight" };

    [Test]
    public void AllRosterUnits_OnlyCarryTheNewOptInBehaviorTheyAreSupposedTo()
    {
        UnitDatabase catalog = AssetDatabase.LoadAssetAtPath<UnitDatabase>(UnitCatalogPath);
        Assert.That(catalog, Is.Not.Null, $"Could not load {UnitCatalogPath}.");
        Assert.That(catalog.units, Is.Not.Null.And.Not.Empty);

        foreach (UnitData unit in catalog.units)
        {
            Assert.That(unit, Is.Not.Null);

            bool expectsFireRateRamp = System.Array.IndexOf(UnitsWithFireRateRamp, unit.unitName) >= 0;
            Assert.That(
                unit.fireRateRampShots > 0,
                Is.EqualTo(expectsFireRateRamp),
                $"{unit.unitName} must keep a constant fire rate unless it is a machine gunner that opts in."
            );

            bool expectsShootWhileMoving =
                System.Array.IndexOf(UnitsThatShootWhileMoving, unit.unitName) >= 0;
            Assert.That(unit.canShootWhileMoving, Is.EqualTo(expectsShootWhileMoving), $"{unit.unitName}");

            bool expectsExplodingBullets =
                System.Array.IndexOf(UnitsWithExplodingBullets, unit.unitName) >= 0;
            Assert.That(
                unit.bulletExplodesOnImpact,
                Is.EqualTo(expectsExplodingBullets),
                $"{unit.unitName}"
            );
            Assert.That(
                unit.bulletAoeRadius > 0f,
                Is.EqualTo(expectsExplodingBullets),
                $"{unit.unitName}"
            );

            bool expectsPiercingBullets =
                System.Array.IndexOf(UnitsWithPiercingBullets, unit.unitName) >= 0;
            Assert.That(unit.bulletPierces, Is.EqualTo(expectsPiercingBullets), $"{unit.unitName}");
        }
    }
}
