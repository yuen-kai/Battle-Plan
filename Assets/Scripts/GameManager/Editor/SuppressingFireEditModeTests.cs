using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

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
    public void PiercingUsesSweptTransformMotionWithoutARigidbody()
    {
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
                shotPierces: true
            );

            Assert.That(bullet.pierces, Is.True);
            Assert.That(instance.GetComponent<Rigidbody>(), Is.Null);
            Assert.That(instance.GetComponentInChildren<SphereCollider>(), Is.Not.Null);

            FieldInfo launchVelocity = typeof(Bullet).GetField(
                "launchVelocity",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.That(
                launchVelocity,
                Is.Not.Null,
                "Bullet must retain its transform velocity while resolving swept impacts."
            );
            Assert.That((Vector3)launchVelocity.GetValue(bullet), Is.EqualTo(velocity));
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
    public void SalvosReloadIsEffectivelySeamless()
    {
        UnitData salvo = AssetDatabase.LoadAssetAtPath<UnitData>(SalvoDataPath);

        Assert.That(salvo, Is.Not.Null, $"Could not load {SalvoDataPath}.");
        Assert.That(salvo.reloadTime, Is.InRange(0f, 0.1f));
    }

    [Test]
    public void TheBarrageIsGenuinelyWiderAndLongerThanSalvosOrdinaryFire()
    {
        UnitData salvo = AssetDatabase.LoadAssetAtPath<UnitData>(SalvoDataPath);
        Assert.That(salvo, Is.Not.Null, $"Could not load {SalvoDataPath}.");

        float spread = GetPrivateConst<float>("BarrageSpreadDegrees");
        int shots = GetPrivateConst<int>("BarrageShots");
        float secondsBetween = GetPrivateConst<float>("SecondsBetweenShots");
        float rangeCells = GetPrivateConst<float>("BarrageRangeCells");

        Assert.That(
            spread,
            Is.GreaterThan(salvo.bulletSpread),
            "'A wide barrage' has to be wider than this unit's normal shooting, or the ability "
                + "is indistinguishable from holding the trigger down."
        );
        Assert.That(shots, Is.GreaterThanOrEqualTo(8), "A barrage is still a burst, not a few shots.");
        Assert.That(secondsBetween, Is.GreaterThan(0f));
        Assert.That(rangeCells, Is.GreaterThan(0f));

        float duration = shots * secondsBetween + GetPrivateConst<float>("WindUpSeconds");
        Assert.That(duration, Is.GreaterThanOrEqualTo(5f));

        Assert.That(
            secondsBetween,
            Is.GreaterThanOrEqualTo(0.08f),
            "Barrage cadence was tuned down deliberately; do not speed it back up without the designer."
        );
    }

    [Test]
    public void TheWindUpIsLongEnoughToBeInterruptedOnPurpose()
    {
        float windUp = GetPrivateConst<float>("WindUpSeconds");
        Assert.That(windUp, Is.GreaterThanOrEqualTo(0.25f));
        Assert.That(
            windUp,
            Is.LessThan(DashRush.KnockbackStunSeconds + 0.5f),
            "A wind-up much longer than the stun that interrupts it would be unreactable."
        );
    }
}
