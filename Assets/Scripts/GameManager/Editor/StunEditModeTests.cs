using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

// Pure coverage for the short, wall-clock stun window: StunState's progress math needs no scene or
// NetworkManager at all, and ApplyStun's server-authoritative interrupt is exercised against a real
// (but unspawned) NetworkManager singleton plus a reflected IsServer, the same way
// GameplayNetworkEditModeTests reflects into other NetworkBehaviour-adjacent private state. The
// coroutine that hands the unit back after the window closes is runtime acceptance and is
// intentionally not exercised here.
[TestFixture]
[Category("Stun")]
public class StunEditModeTests
{
    private GameObject networkManagerHost;

    [SetUp]
    public void SetUp()
    {
        // Gives NetworkManager.Singleton a safe, never-started instance so NetworkManager.ServerTime
        // (read inside ApplyStun and Unit.IsStunned, via NetworkBehaviour.NetworkManager's fallback to
        // NetworkManager.Singleton) resolves to a default NetworkTime instead of throwing. AddComponent
        // never runs Awake() outside Play mode, and Awake() is what normally assigns Singleton, so it's
        // set by hand here.
        networkManagerHost = new GameObject("StunEditModeTestsNetworkManager");
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

    [Test]
    public void ProgressAt_TracksZeroAtStartHalfAtMidpointAndOneAfterExpiry()
    {
        StunState state = new(startServerTime: 10.0, duration: 0.5f);

        Assert.That(
            state.ProgressAt(10.0),
            Is.EqualTo(0f).Within(0.0001f),
            "No time has passed at the instant the stun lands."
        );
        Assert.That(
            state.ProgressAt(10.25),
            Is.EqualTo(0.5f).Within(0.0001f),
            "Halfway through the window is half progress."
        );
        Assert.That(
            state.ProgressAt(10.5),
            Is.EqualTo(1f).Within(0.0001f),
            "Progress reaches exactly 1 the instant the stun expires."
        );
        Assert.That(
            state.ProgressAt(11.0),
            Is.EqualTo(1f),
            "Progress never exceeds 1 once the stun has worn off."
        );
    }

    [Test]
    public void IsStunnedAt_TrueThroughTheWindowAndFalseOnceItCloses()
    {
        StunState inactive = default;
        StunState state = new(startServerTime: 5.0, duration: 0.4f);

        Assert.That(Unit.IsStunnedAt(inactive, 5.0), Is.False, "An inactive state is never stunned.");
        Assert.That(Unit.IsStunnedAt(state, 5.0), Is.True, "Stunned the instant it lands.");
        Assert.That(Unit.IsStunnedAt(state, 5.2), Is.True, "Still stunned mid-window.");
        Assert.That(
            Unit.IsStunnedAt(state, 5.4),
            Is.False,
            "No longer stunned the instant the window closes."
        );
        Assert.That(
            Unit.IsStunnedAt(state, 5.9),
            Is.False,
            "Stays clear well after the window closes."
        );
    }

    [Test]
    public void ApplyStun_PausesMovementAndStandsDownShootingOnTheServer()
    {
        GameObject unitObject = new("StunTestUnit");
        try
        {
            Unit unit = unitObject.AddComponent<Unit>();
            Movement movement = unitObject.AddComponent<Movement>();
            Shooting shooting = unitObject.AddComponent<Shooting>();
            movement.moving = true;
            shooting.allowShooting = true;
            shooting.stillShooting = true;
            SetIsServer(unit, true);

            unit.ApplyStun(0.3f);

            Assert.That(movement.moving, Is.False, "ApplyStun must stop the unit's movement dead.");
            Assert.That(
                shooting.allowShooting,
                Is.False,
                "ApplyStun must stand shooting down rather than merely pausing it."
            );
            Assert.That(shooting.stillShooting, Is.False);
            Assert.That(unit.IsStunned, Is.True, "The replicated stun state must go active immediately.");
        }
        finally
        {
            Object.DestroyImmediate(unitObject);
        }
    }

    [Test]
    public void AbilityRecovery_DoesNotResumeShootingWhileStunned()
    {
        GameObject unitObject = new("StunnedAbilityUnit");
        try
        {
            Unit unit = unitObject.AddComponent<Unit>();
            unitObject.AddComponent<Movement>();
            Shooting shooting = unitObject.AddComponent<Shooting>();
            SetIsServer(unit, true);

            unit.PauseShootingForAbility();
            unit.ApplyStun(0.3f);
            unit.ResumeShootingAfterAbility();

            Assert.That(unit.IsStunned, Is.True);
            Assert.That(shooting.allowShooting, Is.False);
            Assert.That(shooting.stillShooting, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(unitObject);
        }
    }

    [Test]
    public void AbilityInterruptionUsesCoroutineHandlesInsteadOfBooleanState()
    {
        FieldInfo execution = typeof(Ability).GetField(
            "execution",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        FieldInfo interruptibleExecution = typeof(Ability).GetField(
            "interruptibleExecution",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        Assert.That(execution?.FieldType, Is.EqualTo(typeof(Coroutine)));
        Assert.That(interruptibleExecution?.FieldType, Is.EqualTo(typeof(Coroutine)));
        Assert.That(
            typeof(Unit).GetField(
                "abilityActionInProgress",
                BindingFlags.Instance | BindingFlags.NonPublic
            ),
            Is.Null
        );
    }

    [Test]
    public void TransitionToShootingCannotOverrideAnActiveStun()
    {
        GameObject unitObject = new("StunnedTransitionUnit");
        try
        {
            Unit unit = unitObject.AddComponent<Unit>();
            Movement movement = unitObject.AddComponent<Movement>();
            Shooting shooting = unitObject.AddComponent<Shooting>();
            SetIsServer(unit, true);

            unit.ApplyStun(0.3f);
            movement.transitionToShooting();

            Assert.That(unit.IsStunned, Is.True);
            Assert.That(shooting.allowShooting, Is.False);
            Assert.That(shooting.stillShooting, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(unitObject);
        }
    }

    [Test]
    public void AbilityResetDoesNotResumeShooting()
    {
        GameObject unitObject = new("ResetAbilityUnit");
        try
        {
            unitObject.AddComponent<Unit>();
            unitObject.AddComponent<Movement>();
            Shooting shooting = unitObject.AddComponent<Shooting>();
            Ability ability = unitObject.AddComponent<ChainSurge>();
            shooting.StandDown();

            ability.ResetForRespawn();

            Assert.That(shooting.allowShooting, Is.False);
            Assert.That(shooting.stillShooting, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(unitObject);
        }
    }

    [Test]
    public void ApplyStun_IsANoOpWithoutServerAuthorityOrAPositiveDuration()
    {
        GameObject unitObject = new("StunTestUnitGuarded");
        try
        {
            Unit unit = unitObject.AddComponent<Unit>();
            Movement movement = unitObject.AddComponent<Movement>();
            Shooting shooting = unitObject.AddComponent<Shooting>();
            movement.moving = true;
            shooting.allowShooting = true;

            unit.ApplyStun(0.3f); // IsServer defaults to false and must be ignored.

            Assert.That(movement.moving, Is.True, "A non-server call must not touch movement.");
            Assert.That(shooting.allowShooting, Is.True, "A non-server call must not touch shooting.");
            Assert.That(unit.IsStunned, Is.False);

            SetIsServer(unit, true);
            unit.ApplyStun(0f); // A non-positive duration must also be ignored.

            Assert.That(movement.moving, Is.True);
            Assert.That(unit.IsStunned, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(unitObject);
        }
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
}
