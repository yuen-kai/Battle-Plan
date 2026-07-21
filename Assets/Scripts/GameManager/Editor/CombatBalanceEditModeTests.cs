using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

[TestFixture]
[Category("CombatBalance")]
public class CombatBalanceEditModeTests
{
    private const float StandardHealth = 120f;
    private const float FloatTolerance = 0.001f;
    private const string UnitStatsRoot = "Assets/UnitStats/";

    [Test]
    public void UnitAssets_MatchAccuracyAdjustedDamageProfile()
    {
        AssertStats("Commander", damage: 8, magazineSize: 5, maxHealth: 120);
        AssertStats("PogoRider", damage: 12, magazineSize: 3, maxHealth: 120);
        AssertStats("Shotgunner", damage: 8, magazineSize: 10, maxHealth: 160);
        AssertStats("Sniper", damage: 50, magazineSize: 1, maxHealth: 80);
        AssertStats("Soldier", damage: 10, magazineSize: 5, maxHealth: 120);

        UnitData pogo = LoadUnit("PogoRider");
        Assert.That(pogo.backstabMultiplier, Is.EqualTo(2f).Within(FloatTolerance));
        Assert.That(
            AreaLock.AbilityDamage,
            Is.EqualTo(130f).Within(FloatTolerance),
            "Area Lock must remain independent from the Sniper's direct-fire damage."
        );
    }

    [Test]
    public void StandardTarget_ProvisionalMissAllowancesStayWithinTwoToFourMagazines()
    {
        UnitData commander = LoadUnit("Commander");
        UnitData pogo = LoadUnit("PogoRider");
        UnitData shotgunner = LoadUnit("Shotgunner");
        UnitData sniper = LoadUnit("Sniper");
        UnitData soldier = LoadUnit("Soldier");

        // These are explicit design allowances, not measured hit rates. Replace them with
        // range-segmented telemetry once representative matches are available.
        (string name, float magazines)[] attackModes =
        {
            (
                "Commander",
                AllowanceAdjustedMagazineEquivalents(
                    StandardHealth,
                    commander.damage,
                    commander.magazineSize,
                    0.8f
                )
            ),
            (
                "Pogo frontal",
                AllowanceAdjustedMagazineEquivalents(
                    StandardHealth,
                    pogo.damage,
                    pogo.magazineSize,
                    5f / 6f
                )
            ),
            (
                "Pogo backstab",
                AllowanceAdjustedMagazineEquivalents(
                    StandardHealth,
                    pogo.damage * pogo.backstabMultiplier,
                    pogo.magazineSize,
                    5f / 6f
                )
            ),
            (
                "Shotgunner point-blank",
                AllowanceAdjustedMagazineEquivalents(
                    StandardHealth,
                    shotgunner.damage,
                    shotgunner.magazineSize,
                    0.6f
                )
            ),
            (
                "Sniper",
                AllowanceAdjustedMagazineEquivalents(
                    StandardHealth,
                    sniper.damage,
                    sniper.magazineSize,
                    0.9f
                )
            ),
            (
                "Soldier",
                AllowanceAdjustedMagazineEquivalents(
                    StandardHealth,
                    soldier.damage,
                    soldier.magazineSize,
                    0.8f
                )
            ),
        };

        foreach ((string name, float magazines) in attackModes)
        {
            Assert.That(
                magazines,
                Is.InRange(2f - FloatTolerance, 4f + FloatTolerance),
                $"{name} should stay within the provisional 3 ± 1 magazine allowance."
            );
        }

        Assert.That(
            attackModes.Average(mode => mode.magazines),
            Is.EqualTo(3.1f).Within(0.02f),
            "The provisional miss-adjusted center should remain near three magazines."
        );
    }

    [Test]
    public void HealthTierHitBreakpoints_PreserveWeaponRoles()
    {
        UnitData commander = LoadUnit("Commander");
        UnitData pogo = LoadUnit("PogoRider");
        UnitData shotgunner = LoadUnit("Shotgunner");
        UnitData sniper = LoadUnit("Sniper");
        UnitData soldier = LoadUnit("Soldier");

        AssertHitBreakpoints(commander.damage, 10, 15, 20, "Commander");
        AssertHitBreakpoints(pogo.damage, 7, 10, 14, "Pogo frontal");
        AssertHitBreakpoints(
            pogo.damage * pogo.backstabMultiplier,
            4,
            5,
            7,
            "Pogo backstab"
        );
        AssertHitBreakpoints(shotgunner.damage, 10, 15, 20, "Shotgunner");
        AssertHitBreakpoints(sniper.damage, 2, 3, 4, "Sniper");
        AssertHitBreakpoints(soldier.damage, 8, 12, 16, "Soldier");
    }

    private static void AssertStats(
        string assetName,
        int damage,
        int magazineSize,
        float maxHealth
    )
    {
        UnitData unit = LoadUnit(assetName);
        Assert.That(unit.damage, Is.EqualTo(damage), $"{assetName} damage drifted.");
        Assert.That(unit.magazineSize, Is.EqualTo(magazineSize), $"{assetName} magazine drifted.");
        Assert.That(
            unit.maxHealth,
            Is.EqualTo(maxHealth).Within(FloatTolerance),
            $"{assetName} health drifted."
        );
    }

    private static UnitData LoadUnit(string assetName)
    {
        string path = $"{UnitStatsRoot}{assetName}.asset";
        UnitData unit = AssetDatabase.LoadAssetAtPath<UnitData>(path);
        Assert.That(unit, Is.Not.Null, $"Could not load {path}.");
        return unit;
    }

    private static float LandedMagazinesToKill(
        float targetHealth,
        float damagePerHit,
        int magazineSize
    )
    {
        Assert.That(damagePerHit, Is.GreaterThan(0f));
        Assert.That(magazineSize, Is.GreaterThan(0));
        return HitsToKill(targetHealth, damagePerHit) / (float)magazineSize;
    }

    private static float AllowanceAdjustedMagazineEquivalents(
        float targetHealth,
        float damagePerHit,
        int magazineSize,
        float designHitConversion
    )
    {
        Assert.That(designHitConversion, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f));
        return LandedMagazinesToKill(targetHealth, damagePerHit, magazineSize)
            / designHitConversion;
    }

    private static void AssertHitBreakpoints(
        float damagePerHit,
        int glassHits,
        int standardHits,
        int tankHits,
        string attackMode
    )
    {
        Assert.That(HitsToKill(80f, damagePerHit), Is.EqualTo(glassHits), $"{attackMode} vs glass.");
        Assert.That(
            HitsToKill(120f, damagePerHit),
            Is.EqualTo(standardHits),
            $"{attackMode} vs standard."
        );
        Assert.That(HitsToKill(160f, damagePerHit), Is.EqualTo(tankHits), $"{attackMode} vs tank.");
    }

    private static int HitsToKill(float targetHealth, float damagePerHit)
    {
        Assert.That(damagePerHit, Is.GreaterThan(0f));
        return Mathf.CeilToInt(targetHealth / damagePerHit);
    }
}
