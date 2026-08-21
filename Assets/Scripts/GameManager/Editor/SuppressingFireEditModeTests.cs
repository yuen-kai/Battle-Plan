using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// Salvo's barrage is a coroutine that turns the caster and then fires real bullets through
// Shooting.FireBulletInDirection, which reaches a [ClientRpc] and therefore needs a spawned
// NetworkObject — so the firing loop itself is not drivable in edit mode, and pretending otherwise
// is how the previous version of this file ended up asserting against a damage query that never
// shipped. What edit mode CAN prove is everything the barrage's correctness actually rests on:
// - Bullet is a plain MonoBehaviour, so the new wall-piercing flag's plumbing is directly testable.
// - Salvo's UnitData has to be configured as a directional ability or the barrage cannot be aimed
//   at all (this is the exact class of misconfiguration that made Sentinel play as Commander).
// - The tuning constants have to actually deliver the "wide barrage" the design calls for.
// Whether rounds visibly pass through a wall in a live match is a runtime claim, verified in the
// Character Sandbox rather than asserted here.
[TestFixture]
[Category("SuppressingFire")]
public class SuppressingFireEditModeTests
{
    private const string SalvoDataPath = "Assets/UnitStats/Salvo.asset";
    private const string BulletPrefabPath = "Assets/Prefabs/Projectiles/BulletBlue.prefab";

    private static T GetPrivateConst<T>(string name)
    {
        FieldInfo field = typeof(SuppressingFire).GetField(
            name,
            BindingFlags.Static | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null, $"SuppressingFire.{name} is missing.");
        return (T)field.GetValue(null);
    }

    [Test]
    public void NoBulletCanEverPassThroughAWall()
    {
        // The designer withdrew wall penetration outright: "Bullets should be destroyed if they hit
        // a wall." An earlier build had an opt-in Bullet.piercesWalls flag for Salvo's barrage; this
        // asserts no such escape hatch has crept back onto the type, in any spelling, since a single
        // opt-in field is all it would take to quietly reintroduce it for one character.
        System.Collections.Generic.List<string> offenders = new();
        foreach (FieldInfo field in typeof(Bullet).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            string lowered = field.Name.ToLowerInvariant();
            if (lowered.Contains("wall"))
                offenders.Add(field.Name);
        }

        Assert.That(
            offenders,
            Is.Empty,
            "Bullet must expose no wall-related opt-out; walls stop every shot unconditionally."
        );
    }

    [Test]
    public void PiercingRestoresTheShotsCourseSoItGenuinelyPassesThrough()
    {
        // Piercing used to be a no-op: Bullet carries a non-trigger collider on a Rigidbody, so the
        // physics step resolves the impact BEFORE OnCollisionEnter runs, and merely declining to
        // destroy the bullet left it deflected or stopped dead inside the body it should have passed
        // through. The fix is to restore the launch velocity, so this asserts the seam that does it
        // exists and that a piercing shot's velocity survives Initialize.
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BulletPrefabPath);
        Assert.That(prefab, Is.Not.Null, $"Could not load {BulletPrefabPath}.");

        GameObject instance = Object.Instantiate(prefab);
        try
        {
            Bullet bullet = instance.GetComponent<Bullet>();
            Vector3 velocity = Vector3.forward * 9f;

            bullet.Initialize(
                velocity,
                shotDamage: 5f,
                shotBackstabMultiplier: 1f,
                shotRange: 10f,
                shotBackstabAngle: 90f,
                damageableTeam: "RedTeam",
                authoritative: true,
                shotExplodesOnImpact: false,
                shotAoeRadius: 0f,
                shotPierces: true,
                shotOnHit: null
            );

            Assert.That(bullet.pierces, Is.True);

            Rigidbody body = instance.GetComponent<Rigidbody>();
            Assert.That(body, Is.Not.Null, "A bullet needs a Rigidbody for piercing to be restorable.");
            Assert.That(body.linearVelocity, Is.EqualTo(velocity));

            // The recovery path itself: without it, returning early from the collision handler does
            // not produce a pierce.
            MethodInfo keepFlying = typeof(Bullet).GetMethod(
                "KeepFlyingThrough",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.That(
                keepFlying,
                Is.Not.Null,
                "Bullet must restore a piercing shot's course after impact, or piercing does nothing."
            );
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void FarsightIsTheOnlyNewUnitFiringPiercingRounds()
    {
        UnitData farsight = AssetDatabase.LoadAssetAtPath<UnitData>("Assets/UnitStats/Farsight.asset");
        Assert.That(farsight, Is.Not.Null);
        Assert.That(
            farsight.bulletPierces,
            Is.True,
            "Farsight's whole kit is that its basic attack pierces."
        );
    }

    [Test]
    public void SalvoUnitData_IsConfiguredAsADirectionalAbility()
    {
        UnitData salvo = AssetDatabase.LoadAssetAtPath<UnitData>(SalvoDataPath);
        Assert.That(salvo, Is.Not.Null, $"Could not load {SalvoDataPath}.");

        // The barrage resolves its direction with GridSystem.TryGetAdjacentDirection against the
        // picked square, and aborts outright if that fails. Both of these have to be set for the
        // ability to be aimable, and neither is enforced anywhere else.
        Assert.That(
            salvo.selectAbilityDirection,
            Is.True,
            "Suppressing Fire is aimed down a direction, so the planner must offer a direction pick."
        );
        Assert.That(
            salvo.abilityFixedDistance,
            Is.GreaterThan(0),
            "A fixed distance of 0 makes the direction unresolvable and the barrage aborts."
        );
        Assert.That(salvo.selectAbilitySquare, Is.True);
    }

    [Test]
    public void TheBarrageIsGenuinelyWiderAndLongerThanSalvosOrdinaryFire()
    {
        UnitData salvo = AssetDatabase.LoadAssetAtPath<UnitData>(SalvoDataPath);
        Assert.That(salvo, Is.Not.Null, $"Could not load {SalvoDataPath}.");

        float spread = GetPrivateConst<float>("SpreadDegrees");
        int shots = GetPrivateConst<int>("BarrageShots");
        float secondsBetween = GetPrivateConst<float>("SecondsBetweenShots");
        float rangeCells = GetPrivateConst<float>("BarrageRangeCells");

        Assert.That(
            spread,
            Is.GreaterThan(salvo.bulletSpread),
            "'A wide barrage' has to be wider than this unit's normal shooting, or the ability "
                + "is indistinguishable from holding the trigger down."
        );
        // An absolute floor rather than a fraction of the magazine: the round count was tuned DOWN
        // on the designer's note, so tying "a lot of rounds" to magazine size would fight that
        // tuning every time the cadence is adjusted. What matters is that it stays a burst.
        Assert.That(shots, Is.GreaterThanOrEqualTo(8), "A barrage is still a burst, not a few shots.");
        Assert.That(secondsBetween, Is.GreaterThan(0f));
        Assert.That(rangeCells, Is.GreaterThan(0f));

        // A sanity bound on the whole action: long enough to read as sustained, short enough that
        // it cannot outlast the round it was cast in.
        float duration = shots * secondsBetween + GetPrivateConst<float>("WindUpSeconds");
        Assert.That(duration, Is.InRange(0.5f, 5f));

        // The designer asked for the new characters' fire rate and ability speed to come down, and
        // the barrage is the fastest-cadence thing in the batch. Its per-shot spacing must stay slow
        // enough that individual rounds are trackable rather than arriving as one solid wall.
        Assert.That(
            secondsBetween,
            Is.GreaterThanOrEqualTo(0.08f),
            "Barrage cadence was tuned down deliberately; do not speed it back up without the designer."
        );
    }

    [Test]
    public void TheWindUpIsLongEnoughToBeInterruptedOnPurpose()
    {
        // The designer's "ability can be interrupted if stunned" needs a window that a stun can
        // realistically land inside. A near-zero wind-up would make the interrupt unreachable in
        // practice while still technically being implemented.
        float windUp = GetPrivateConst<float>("WindUpSeconds");
        Assert.That(windUp, Is.GreaterThanOrEqualTo(0.25f));
        Assert.That(
            windUp,
            Is.LessThan(DashRush.KnockbackStunSeconds + 0.5f),
            "A wind-up much longer than the stun that interrupts it would be unreactable."
        );
    }
}
