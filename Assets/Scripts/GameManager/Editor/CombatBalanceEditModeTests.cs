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
    public void UnitAssets_UseStrengthBasedAbilityCooldowns()
    {
        Assert.That(LoadUnit("PogoRider").abilityCooldownRounds, Is.EqualTo(2));
        Assert.That(LoadUnit("Shotgunner").abilityCooldownRounds, Is.EqualTo(2));
        Assert.That(LoadUnit("Commander").abilityCooldownRounds, Is.EqualTo(2));
        Assert.That(LoadUnit("Soldier").abilityCooldownRounds, Is.EqualTo(3));
        Assert.That(LoadUnit("Sniper").abilityCooldownRounds, Is.EqualTo(4));
    }

    [Test]
    public void PogoJump_UsesSixCellRangeAndRejectsTheSeventhCell()
    {
        UnitData pogo = LoadUnit("PogoRider");
        Assert.That(pogo.abilitySquareRange, Is.EqualTo(6));
        Assert.That(pogo.abilityCooldownRounds, Is.EqualTo(2));

        Vector2Int start = new(1, 1);
        Assert.That(
            PlanMovement.ValidateAbilityTarget(pogo, start, new Vector2Int(7, 1), true, false),
            Is.EqualTo(AbilityTargetValidationReason.Valid)
        );
        Assert.That(
            PlanMovement.ValidateAbilityTarget(pogo, start, new Vector2Int(8, 1), true, false),
            Is.EqualTo(AbilityTargetValidationReason.OutOfRange)
        );
    }

    [Test]
    public void AreaLock_LeavesTheSniperOwnSquareOutOfItsAbilityRange()
    {
        UnitData sniper = LoadUnit("Sniper");
        Assert.That(sniper.responseDistLine, Is.True, "Area Lock is the line ability.");
        Assert.That(sniper.CanTargetOwnCell, Is.False);

        Vector2Int start = new(4, 4);
        Assert.That(
            PlanMovement.ValidateAbilityTarget(sniper, start, start, true, false),
            Is.EqualTo(AbilityTargetValidationReason.AimedAtOwnCell),
            "The beam is fired through the chosen square, and its own square names no direction."
        );
        Assert.That(
            PlanMovement.GetAbilityTargetFeedback(AbilityTargetValidationReason.AimedAtOwnCell),
            Is.Not.Empty,
            "A refused target has to say why."
        );
        Assert.That(
            PlanMovement.ValidateAbilityTarget(sniper, start, new Vector2Int(4, 5), true, false),
            Is.EqualTo(AbilityTargetValidationReason.Valid),
            "Every other square stays aimable, walls included."
        );

        Assert.That(
            PlanMovement.ValidateAbilityTarget(LoadUnit("Commander"), start, start, true, false),
            Is.EqualTo(AbilityTargetValidationReason.Valid),
            "Abilities that land on their square keep their own: smoke underfoot is a real play."
        );
    }

    [Test]
    public void AbilityLanding_HoldsFireOpenLongEnoughToTurnAroundAndShootBack()
    {
        // A rider walked into is shot at all the way in; one that is set down arrives at once, and
        // the defender's answer starts with a turn that can be the full half-circle.
        float worstTurn = 180f / RosterUnits().Min(unit => unit.rotationSpeed);
        float slowestShotInterval = RosterUnits().Max(unit => unit.timeBetweenShots);

        Assert.That(
            GameLoop.AbilityLandingReturnFireSeconds,
            Is.GreaterThan(worstTurn + slowestShotInterval),
            "A landing outlasted by the defender's own turn cannot be answered at all."
        );
        Assert.That(
            GameLoop.AbilityLandingReturnFireSeconds,
            Is.LessThanOrEqualTo(2f),
            "Longer reads as the round refusing to end rather than as an answer to the landing."
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

    private static UnitData[] RosterUnits()
    {
        return new[] { "Commander", "PogoRider", "Shotgunner", "Sniper", "Soldier" }
            .Select(LoadUnit)
            .ToArray();
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
