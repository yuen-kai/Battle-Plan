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

    [Test]
    public void AllRosterUnits_StillDefaultToNoneOfTheNewOptInBehavior()
    {
        UnitDatabase catalog = AssetDatabase.LoadAssetAtPath<UnitDatabase>(UnitCatalogPath);
        Assert.That(catalog, Is.Not.Null, $"Could not load {UnitCatalogPath}.");
        Assert.That(catalog.units, Is.Not.Null.And.Not.Empty);

        foreach (UnitData unit in catalog.units)
        {
            Assert.That(unit, Is.Not.Null);
            Assert.That(
                unit.fireRateRampShots,
                Is.EqualTo(0),
                $"{unit.unitName} must keep a constant fire rate until a machine gunner opts in."
            );
            Assert.That(unit.canShootWhileMoving, Is.False, $"{unit.unitName}");
            Assert.That(unit.bulletExplodesOnImpact, Is.False, $"{unit.unitName}");
            Assert.That(unit.bulletAoeRadius, Is.EqualTo(0f), $"{unit.unitName}");
            Assert.That(unit.bulletPierces, Is.False, $"{unit.unitName}");
        }
    }
}
