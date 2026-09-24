using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

/// <summary>
/// A unit's stats reach the game through TWO independent references, and nothing previously checked
/// that they agree: the catalog (AllUnits.asset) hands the HUD its name, ability name and cooldown,
/// while gameplay reads <c>unitData</c> off components on the spawned prefab
/// (<c>Movement.unitData</c>, <c>Shooting.unitData</c>, ...). A prefab created by duplicating another
/// unit's keeps the original's <c>unitData</c> until it is repointed by hand, and the result is a unit
/// whose card reads correctly while every number driving its behaviour belongs to the unit it was
/// copied from — targeting mode, ability range, radius, health, weapon, all of it.
///
/// That is exactly what shipped for Sentinel: its prefab pointed at Commander.asset, so it aimed with
/// Commander's radius-4 smoke targeting instead of its own adjacent-direction picker, while asset-only
/// tests kept passing because they read Sentinel.asset directly and never opened the prefab.
/// </summary>
[TestFixture]
[Category("UnitPrefabWiring")]
public class UnitPrefabWiringEditModeTests
{
    private const string CatalogPath = "Assets/UnitStats/AllUnits.asset";

    private static UnitDatabase LoadCatalog()
    {
        UnitDatabase catalog = AssetDatabase.LoadAssetAtPath<UnitDatabase>(CatalogPath);
        Assert.That(catalog?.units, Is.Not.Null, $"Could not load {CatalogPath}.");
        return catalog;
    }

    /// <summary>
    /// Every serialized <see cref="UnitData"/>-typed field on the prefab, whatever the component and
    /// whatever the field is called — found by reflection rather than by asking named components, so
    /// a component added later is covered without editing this test.
    /// </summary>
    private static IEnumerable<(string component, string field, UnitData value)> EnumerateUnitDataRefs(
        GameObject prefabRoot
    )
    {
        foreach (MonoBehaviour behaviour in prefabRoot.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null)
                continue;

            FieldInfo[] fields = behaviour
                .GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (FieldInfo field in fields)
            {
                if (field.FieldType != typeof(UnitData))
                    continue;
                bool serialized =
                    field.IsPublic || field.GetCustomAttribute<SerializeField>() != null;
                if (!serialized)
                    continue;

                yield return (
                    behaviour.GetType().Name,
                    field.Name,
                    field.GetValue(behaviour) as UnitData
                );
            }
        }
    }

    [Test]
    public void EveryUnitPrefab_PointsAtItsOwnUnitDataAndNoOtherUnits()
    {
        UnitDatabase catalog = LoadCatalog();

        for (int unitIndex = 0; unitIndex < catalog.units.Count; unitIndex++)
        {
            UnitData expected = catalog.units[unitIndex];
            Assert.That(expected, Is.Not.Null, $"Catalog index {unitIndex} is an empty slot.");
            Assert.That(
                expected.unitModel,
                Is.Not.Null,
                $"{expected.name} (index {unitIndex}) has no unitModel prefab."
            );

            int refCount = 0;
            foreach (var reference in EnumerateUnitDataRefs(expected.unitModel))
            {
                refCount++;
                Assert.That(
                    reference.value,
                    Is.SameAs(expected),
                    $"{expected.unitModel.name}'s {reference.component}.{reference.field} points at "
                        + $"'{(reference.value != null ? reference.value.name : "null")}' instead of "
                        + $"'{expected.name}'. A prefab duplicated from another unit keeps that "
                        + "unit's stats until this is repointed, so this unit would play as that one."
                );
            }

            Assert.That(
                refCount,
                Is.GreaterThan(0),
                $"{expected.unitModel.name} has no UnitData reference at all, so it has no stats."
            );
        }
    }

    /// <summary>
    /// The reverse direction: a UnitData naming someone else's prefab. Already covered indirectly by
    /// an existing roster test that requires distinct prefabs, but asserted here in the terms that
    /// actually matter — the prefab a catalog entry spawns must be the prefab that carries it.
    /// </summary>
    [Test]
    public void EveryUnitData_IsCarriedByThePrefabItSpawns()
    {
        UnitDatabase catalog = LoadCatalog();

        foreach (UnitData unit in catalog.units)
        {
            if (unit == null || unit.unitModel == null)
                continue;

            bool carriesItself = false;
            foreach (var reference in EnumerateUnitDataRefs(unit.unitModel))
                carriesItself |= reference.value == unit;

            Assert.That(
                carriesItself,
                Is.True,
                $"{unit.name}.unitModel is '{unit.unitModel.name}', but that prefab carries no "
                    + $"reference back to {unit.name}."
            );
        }
    }

    /// <summary>
    /// The shield physics contract (footprint width, collision layer, bullets stopping) is asserted
    /// elsewhere against Ramrod's prefab, which still carries a permanently-inactive Shield child
    /// left over from when it owned that kit — Ramrod's ability is DashRush now and never raises it.
    /// So that suite proves the contract against dead art on a unit that cannot use it. This checks
    /// the prefab that actually raises a shield has the parts ShieldStance reaches for.
    /// </summary>
    [Test]
    public void ShieldStancePrefabs_CarryAShieldChildWithACollider()
    {
        UnitDatabase catalog = LoadCatalog();
        int checkedPrefabs = 0;

        foreach (UnitData unit in catalog.units)
        {
            if (unit == null || unit.unitModel == null)
                continue;
            if (unit.unitModel.GetComponent<ShieldStance>() == null)
                continue;

            checkedPrefabs++;
            // ShieldStance resolves the slab with transform.Find("Shield"), which matches immediate
            // children only. Sentinel also has a cosmetic forearm accessory named "Shield" deeper in
            // the model hierarchy, so this asserts the immediate-child lookup finds the real one
            // rather than relying on that ambiguity staying harmless.
            Transform shield = unit.unitModel.transform.Find("Shield");
            Assert.That(
                shield,
                Is.Not.Null,
                $"{unit.unitModel.name} has ShieldStance but no immediate child named 'Shield', so "
                    + "the ability would silently raise nothing."
            );
            Assert.That(
                shield.GetComponent<Collider>(),
                Is.Not.Null,
                $"{unit.unitModel.name}'s Shield has no Collider, so it cannot stop a bullet — the "
                    + "whole point of the ability."
            );
        }

        Assert.That(
            checkedPrefabs,
            Is.GreaterThan(0),
            "No prefab in the catalog carries ShieldStance; this test would pass vacuously."
        );
    }

    [Test]
    public void UnitPrefabAbilityCount_MatchesItsAdvertisedKit()
    {
        UnitDatabase catalog = LoadCatalog();

        foreach (UnitData unit in catalog.units)
        {
            if (unit == null || unit.unitModel == null)
                continue;

            Ability[] abilities = unit.unitModel.GetComponentsInChildren<Ability>(true);
            int expectedCount = string.IsNullOrWhiteSpace(unit.abilityName) ? 0 : 1;
            Assert.That(
                abilities.Length,
                Is.EqualTo(expectedCount),
                $"{unit.unitModel.name} advertises '{unit.abilityName}' but carries "
                    + $"{abilities.Length} Ability components."
            );
        }
    }

    [Test]
    public void UnitsWithoutAbilitiesHaveNeutralAbilityConfiguration()
    {
        UnitDatabase catalog = LoadCatalog();

        foreach (UnitData unit in catalog.units)
        {
            if (unit == null || !string.IsNullOrWhiteSpace(unit.abilityName))
                continue;

            Assert.That(unit.abilitySprite, Is.Null, $"{unit.name} has an unused ability sprite.");
            Assert.That(unit.abilityCardSprite, Is.Null, $"{unit.name} has an unused ability card.");
            Assert.That(unit.abilitySquareRange, Is.Zero, $"{unit.name} has stale ability range.");
            Assert.That(unit.abilityRadius, Is.Zero, $"{unit.name} has stale ability radius.");
            Assert.That(unit.abilityCooldownRounds, Is.Zero, $"{unit.name} has stale cooldown.");
        }
    }

    [Test]
    public void UnitPrefabNetworkHashes_AreNonzeroAndUnique()
    {
        UnitDatabase catalog = LoadCatalog();
        HashSet<uint> hashes = new();

        foreach (UnitData unit in catalog.units)
        {
            if (unit == null || unit.unitModel == null)
                continue;

            NetworkObject networkObject = unit.unitModel.GetComponent<NetworkObject>();
            Assert.That(networkObject, Is.Not.Null, $"{unit.unitModel.name} has no NetworkObject.");

            SerializedProperty hashProperty = new SerializedObject(networkObject).FindProperty(
                "GlobalObjectIdHash"
            );
            Assert.That(hashProperty, Is.Not.Null);
            uint hash = hashProperty.uintValue;
            Assert.That(hash, Is.Not.Zero, $"{unit.unitModel.name} has an invalid network hash.");
            Assert.That(
                hashes.Add(hash),
                Is.True,
                $"{unit.unitModel.name} duplicates network hash {hash}."
            );
        }
    }
}
