using System.Linq;
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

    private static T GetConst<T>(string name)
    {
        FieldInfo field = typeof(SuppressingFire).GetField(
            name,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
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
    public void BarrageBulletsIgnoreEveryAdjacentWallNotJustTheAimedOne()
    {
        MethodInfo ignoreAdjacent = typeof(Bullet).GetMethod(
            "IgnoreAdjacentWalls",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(
            ignoreAdjacent,
            Is.Not.Null,
            "Suppressing Fire must ignore the full Moore neighborhood of walls around Salvo."
        );

        ParameterInfo[] fireParams = typeof(Shooting)
            .GetMethod(nameof(Shooting.FireBullet))
            ?.GetParameters();
        Assert.That(fireParams, Is.Not.Null);
        Assert.That(
            fireParams.Any(parameter => parameter.Name == "ignoreAdjacentWalls"),
            Is.True,
            "FireBullet must carry the adjacent-wall exception through host and client bullet creation."
        );
        Assert.That(
            fireParams.All(parameter => parameter.Name != "ignoredWallCell"),
            Is.True,
            "The single aimed-wall exception was replaced by ignoreAdjacentWalls."
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
    public void TheBarrageIsGenuinelyWiderThanSalvosOrdinaryFire()
    {
        UnitData salvo = AssetDatabase.LoadAssetAtPath<UnitData>(SalvoDataPath);
        Assert.That(salvo, Is.Not.Null, $"Could not load {SalvoDataPath}.");

        float spread = GetConst<float>("BarrageSpreadDegrees");
        int shots = GetConst<int>("BarrageShots");
        float secondsBetween = GetConst<float>("SecondsBetweenShots");

        Assert.That(
            spread,
            Is.GreaterThan(salvo.bulletSpread),
            "'A wide barrage' has to be wider than this unit's normal shooting, or the ability "
                + "is indistinguishable from holding the trigger down."
        );
        Assert.That(shots, Is.GreaterThanOrEqualTo(8), "A barrage is still a burst, not a few shots.");
        Assert.That(secondsBetween, Is.GreaterThan(0f));
        Assert.That(
            salvo.bulletRange,
            Is.GreaterThan(0f),
            "Barrage shots inherit ordinary bullet range."
        );

        float duration =
            shots * secondsBetween
            + GetConst<float>("WindUpSeconds")
            + GetConst<float>("RecoverySeconds");
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
        float windUp = GetConst<float>("WindUpSeconds");
        Assert.That(windUp, Is.GreaterThanOrEqualTo(0.25f));
        Assert.That(
            windUp,
            Is.LessThan(DashRush.KnockbackStunSeconds + 0.5f),
            "A wind-up much longer than the stun that interrupts it would be unreactable."
        );
    }

    [Test]
    public void ConePreview_MatchesTheBarrageEnvelope()
    {
        UnitData salvo = AssetDatabase.LoadAssetAtPath<UnitData>(SalvoDataPath);
        Assert.That(salvo, Is.Not.Null);

        Mesh mesh = SuppressingFire.BuildConeMesh(
            SuppressingFire.BarrageSpreadDegrees,
            salvo.bulletRange
        );
        Assert.That(mesh, Is.Not.Null);
        Assert.That(mesh.vertexCount, Is.EqualTo(26), "Apex plus 25 arc samples.");

        float range = salvo.bulletRange * GameLoop.cellSize;
        Assert.That(mesh.vertices[0], Is.EqualTo(Vector3.zero));

        Vector3 left = mesh.vertices[1];
        Vector3 right = mesh.vertices[^1];
        Assert.That(left.magnitude, Is.EqualTo(range).Within(0.001f));
        Assert.That(right.magnitude, Is.EqualTo(range).Within(0.001f));

        float halfAngle = SuppressingFire.BarrageSpreadDegrees;
        Assert.That(
            Vector3.Angle(Vector3.forward, left),
            Is.EqualTo(halfAngle).Within(0.05f)
        );
        Assert.That(
            Vector3.Angle(Vector3.forward, right),
            Is.EqualTo(halfAngle).Within(0.05f)
        );
    }

    [Test]
    public void PlanningPreview_DrawsAConeInsteadOfALine()
    {
        UnitData salvo = AssetDatabase.LoadAssetAtPath<UnitData>(SalvoDataPath);
        Assert.That(salvo, Is.Not.Null);
        Assert.That(salvo.unitModel, Is.Not.Null);

        GameObject caster = Object.Instantiate(salvo.unitModel);
        caster.transform.position =
            GameLoop.gridCoordToWorld(new Vector2Int(5, 5))
            + Helper.heightOffset(caster.transform);
        try
        {
            Vector3 start = GameLoop.gridCoordToWorld(new Vector2Int(5, 5));
            Vector3 square = GameLoop.gridCoordToWorld(new Vector2Int(6, 5));
            GameObject preview = PlanMovement.BuildAbilityPreview(
                null,
                caster,
                start,
                square,
                salvo,
                rosterSlot: 0,
                selected: true
            );
            try
            {
                Assert.That(
                    preview.GetComponentInChildren<MeshFilter>(),
                    Is.Not.Null,
                    "Suppressing Fire must preview as a cone mesh, not a line laser."
                );
                Assert.That(
                    preview.GetComponent<LineRenderer>(),
                    Is.Null,
                    "The old responseDistLine laser must not draw for Salvo anymore."
                );
            }
            finally
            {
                Object.DestroyImmediate(preview);
            }
        }
        finally
        {
            Object.DestroyImmediate(caster);
        }
    }
}
