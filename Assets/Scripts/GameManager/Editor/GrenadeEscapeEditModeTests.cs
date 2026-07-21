using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

[TestFixture]
[Category("GrenadeEscape")]
public class GrenadeEscapeEditModeTests
{
    private const string SoldierDataPath = "Assets/UnitStats/Soldier.asset";
    private const string SoldierPrefabPath = "Assets/Prefabs/Units/Soldier.prefab";
    private const string UnitCatalogPath = "Assets/UnitStats/AllUnits.asset";
    private const float ExpectedRadiusCells = 1.6f;
    private const float MinimumFullDiveClearance = 0.1f;
    private const float Tolerance = 0.001f;

    [Test]
    public void SoldierGrenade_KeepsTwoCellDiveWithSmallerBlast()
    {
        UnitData soldier = LoadSoldier();

        Assert.That(soldier.abilitySquareRange, Is.EqualTo(5));
        Assert.That(soldier.abilityRadius, Is.EqualTo(ExpectedRadiusCells).Within(Tolerance));
        Assert.That(soldier.diveRange, Is.EqualTo(2));
        Assert.That(soldier.responseRange, Is.EqualTo(3f).Within(Tolerance));

        GameObject soldierPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SoldierPrefabPath);
        Assert.That(soldierPrefab, Is.Not.Null, $"Could not load {SoldierPrefabPath}.");
        Grenade grenade = soldierPrefab.GetComponent<Grenade>();
        Assert.That(grenade, Is.Not.Null, "The Soldier prefab must retain its Grenade component.");
        SerializedProperty damage = new SerializedObject(grenade).FindProperty("damage");
        Assert.That(damage, Is.Not.Null);
        Assert.That(damage.floatValue, Is.EqualTo(80f).Within(Tolerance));
    }

    [Test]
    public void CenterTarget_StraightTwoCellDiveClearsEveryUnitColliderButOneStepDoesNot()
    {
        UnitData soldier = LoadSoldier();
        UnitDatabase catalog = AssetDatabase.LoadAssetAtPath<UnitDatabase>(UnitCatalogPath);
        Assert.That(catalog, Is.Not.Null, $"Could not load {UnitCatalogPath}.");
        Assert.That(catalog.units, Is.Not.Null.And.Not.Empty);

        float blastRadius = soldier.abilityRadius * GameLoop.cellSize;
        for (int index = 0; index < catalog.units.Count; index++)
        {
            UnitData target = catalog.units[index];
            Assert.That(target, Is.Not.Null);
            Assert.That(target.unitModel, Is.Not.Null, $"{target.unitName} has no unit prefab.");

            AssertBlastOverlap(target, index, 1, blastRadius, expectedOverlap: true);
            AssertBlastOverlap(
                target,
                index,
                soldier.diveRange,
                blastRadius,
                expectedOverlap: false
            );
        }
    }

    private static UnitData LoadSoldier()
    {
        UnitData soldier = AssetDatabase.LoadAssetAtPath<UnitData>(SoldierDataPath);
        Assert.That(soldier, Is.Not.Null, $"Could not load {SoldierDataPath}.");
        return soldier;
    }

    private static void AssertBlastOverlap(
        UnitData target,
        int targetIndex,
        int distanceCells,
        float blastRadius,
        bool expectedOverlap
    )
    {
        GameObject instance = Object.Instantiate(target.unitModel);
        try
        {
            Collider targetCollider = instance.GetComponent<Collider>();
            Assert.That(
                targetCollider,
                Is.Not.Null,
                $"{target.unitName} must have a root damage collider."
            );
            int enemyLayer = LayerMask.NameToLayer("BlueTeam");
            Assert.That(enemyLayer, Is.GreaterThanOrEqualTo(0));
            SetLayerRecursively(instance, enemyLayer);

            Vector3 explosionCenter = new(
                1000f + targetIndex * 20f,
                0f,
                1000f
            );
            instance.transform.position =
                explosionCenter + Vector3.right * (distanceCells * GameLoop.cellSize);
            Physics.SyncTransforms();

            // Align to the collider's widest horizontal section. This is at least as strict as the
            // runtime grenade, whose center height is derived from the throwing Soldier.
            explosionCenter.y = targetCollider.bounds.center.y;
            bool overlaps = Physics
                .OverlapSphere(
                    explosionCenter,
                    blastRadius,
                    LayerMask.GetMask("BlueTeam"),
                    QueryTriggerInteraction.UseGlobal
                )
                .Contains(targetCollider);
            float surfaceDistance = Vector3.Distance(
                explosionCenter,
                targetCollider.ClosestPoint(explosionCenter)
            );

            Assert.That(
                overlaps,
                Is.EqualTo(expectedOverlap),
                $"{target.unitName} at {distanceCells} cell(s): blast {blastRadius:F3}, "
                    + $"collider surface {surfaceDistance:F3}."
            );
            if (!expectedOverlap)
            {
                Assert.That(
                    surfaceDistance - blastRadius,
                    Is.GreaterThan(MinimumFullDiveClearance),
                    $"{target.unitName}'s full dive needs numerical clearance from the blast edge."
                );
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        foreach (Transform child in root.transform)
            SetLayerRecursively(child.gameObject, layer);
    }
}
