using NUnit.Framework;
using UnityEditor;
using UnityEngine;

[TestFixture]
[Category("ShootingExtensions")]
public class ShootingExtensionsEditModeTests
{
    private const string UnitCatalogPath = "Assets/UnitStats/AllUnits.asset";

    [Test]
    public void ComputeRampedShotDelay_DisabledRampAlwaysReturnsTheFloorDelay()
    {
        Assert.That(
            Shooting.ComputeRampedShotDelay(
                continuousShotsFired: 0,
                rampShots: 0,
                rampStartDelay: 1f,
                floorDelay: 0.3f
            ),
            Is.EqualTo(0.3f).Within(0.0001f),
            "Units with the ramp disabled always use their configured shot delay."
        );
        Assert.That(
            Shooting.ComputeRampedShotDelay(
                continuousShotsFired: 50,
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
            "A firing run that outlasts the ramp stays at the fully-warmed-up delay."
        );
    }

    [Test]
    public void UnitData_NewOptInFieldsDefaultOff()
    {
        UnitData data = ScriptableObject.CreateInstance<UnitData>();
        try
        {
            Assert.That(data.fireRateRampShots, Is.EqualTo(0));
            Assert.That(data.fireRateRampStartDelay, Is.EqualTo(0f));
            Assert.That(data.fireRateRampResetDelay, Is.EqualTo(1.5f));
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
            if (expectsFireRateRamp)
            {
                Assert.That(unit.fireRateRampResetDelay, Is.EqualTo(1.5f));
                Assert.That(
                    unit.fireRateRampResetDelay,
                    Is.GreaterThan(unit.timeBetweenShots + unit.reloadTime),
                    $"{unit.unitName}'s reload must preserve its warmed-up firing run."
                );
            }

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
