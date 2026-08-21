using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools.Utils;

// ShieldStance is the stationary half of what used to be Shield's kit: it reuses Shield's own
// static footprint/collision helpers rather than duplicating them, so this suite exercises that
// reuse directly on a bare, unspawned GameObject the same way GameplayNetworkEditModeTests exercises
// those helpers against the live Ramrod prefab -- ApplyShieldState and EnsureExpandedShieldFootprint
// are private here, so they are driven by reflection the same way WallDestructionEditModeTests drives
// GameLoop's own IsServer-gated private helpers. ExecuteAbility's WaitForSeconds hold is runtime
// acceptance and is intentionally not exercised here, mirroring StunEditModeTests' own carve-out for
// the wall-clock half of a stun.
[TestFixture]
[Category("ShieldStance")]
public class ShieldStanceEditModeTests
{
    private GameObject unitObject;
    private ShieldStance shieldStance;
    private Transform shieldChild;
    private BoxCollider shieldCollider;

    [SetUp]
    public void SetUp()
    {
        unitObject = new GameObject("ShieldStanceTestUnit");
        shieldStance = unitObject.AddComponent<ShieldStance>();

        GameObject shieldObject = new GameObject("Shield");
        shieldObject.transform.SetParent(unitObject.transform, worldPositionStays: false);
        shieldChild = shieldObject.transform;
        shieldCollider = shieldObject.AddComponent<BoxCollider>();
        shieldCollider.size = new Vector3(1f, 1f, 1f);
    }

    [TearDown]
    public void TearDown()
    {
        // A raised shield detaches to world space, so it can outlive unitObject's destruction if
        // not cleaned up separately.
        if (shieldChild != null)
            Object.DestroyImmediate(shieldChild.gameObject);
        if (unitObject != null)
            Object.DestroyImmediate(unitObject);
    }

    [Test]
    public void AbilityDurationSeconds_ReusesShieldsExistingDurationRatherThanANewNumber()
    {
        Assert.That(ShieldStance.AbilityDurationSeconds, Is.EqualTo(Shield.AbilityDurationSeconds));
    }

    [Test]
    public void ApplyShieldState_ExpandsTheFootprintByTheSameAmountShieldsOwnHelperDoes()
    {
        float widthBefore = Mathf.Abs(shieldCollider.size.x * shieldChild.lossyScale.x);

        InvokeApplyShieldState(true);

        float widthAfter = Mathf.Abs(shieldCollider.size.x * shieldChild.lossyScale.x);
        Assert.That(
            widthAfter - widthBefore,
            Is.EqualTo(Shield.ShieldWidthIncreaseCellsPerSide * 2f * GameLoop.cellSize).Within(0.001f)
        );
    }

    [Test]
    public void ApplyShieldState_OnlyExpandsTheFootprintOnceAcrossRepeatedCalls()
    {
        InvokeApplyShieldState(true);
        float widthAfterFirstCall = Mathf.Abs(shieldCollider.size.x * shieldChild.lossyScale.x);

        InvokeApplyShieldState(false);
        InvokeApplyShieldState(true);

        float widthAfterMoreCalls = Mathf.Abs(shieldCollider.size.x * shieldChild.lossyScale.x);
        Assert.That(
            widthAfterMoreCalls,
            Is.EqualTo(widthAfterFirstCall).Within(0.001f),
            "A shield raised a second time must not widen again on top of the first expansion."
        );
    }

    [Test]
    public void ApplyShieldState_TogglesTheShieldChildActiveWithTheGivenState()
    {
        InvokeApplyShieldState(true);
        Assert.That(shieldChild.gameObject.activeSelf, Is.True);

        InvokeApplyShieldState(false);
        Assert.That(shieldChild.gameObject.activeSelf, Is.False);
    }

    [TestCase(GameLoop.HostTeamIndex, Shield.BlueShieldLayerName)]
    [TestCase(GameLoop.OpponentTeamIndex, Shield.RedShieldLayerName)]
    public void ApplyShieldState_AppliesTheCollisionLayerForTheCastersTeam(
        int teamIndex,
        string expectedLayerName
    )
    {
        Unit identity = unitObject.AddComponent<Unit>();
        SetTeamIndexWithoutTriggeringPresentation(identity, teamIndex);

        InvokeApplyShieldState(true);

        Assert.That(shieldChild.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer(expectedLayerName)));
    }

    [Test]
    public void ExecuteAbility_RaisesTheShieldImmediatelyOnTheServer()
    {
        SetIsServer(shieldStance, true);

        var routine = shieldStance.ExecuteAbility(Vector3.zero, 0f);
        routine.MoveNext();

        Assert.That(
            GetShieldActive(),
            Is.True,
            "The shield must go active the instant the ability runs on the server."
        );
    }

    [Test]
    public void ExecuteAbility_ResolvesTheShieldDirectionFromTheTargetedAdjacentCell()
    {
        SetIsServer(shieldStance, true);
        unitObject.transform.position = GameLoop.gridCoordToWorld(new Vector2Int(3, 3));

        var routine = shieldStance.ExecuteAbility(
            GameLoop.gridCoordToWorld(new Vector2Int(4, 3)),
            0f
        );
        routine.MoveNext();

        Assert.That(GetShieldDirection(), Is.EqualTo(new Vector2Int(1, 0)));
    }

    [Test]
    public void ApplyShieldState_PinsTheRaisedShieldToTheChosenWorldDirection()
    {
        SetShieldDirection(new Vector2Int(0, -1));

        InvokeApplyShieldState(true);

        FixedWorldFacing facing = shieldChild.GetComponent<FixedWorldFacing>();
        Assert.That(
            facing,
            Is.Not.Null,
            "A raised shield must carry the facing lock, so it holds its direction on every peer "
                + "(Ability disables itself on non-server peers, so the lock cannot live there)."
        );
        Assert.That(facing.worldForward.normalized, Is.EqualTo(Vector3.back).Using(Vector3EqualityComparer.Instance));
    }

    [Test]
    public void ApplyShieldState_KeepsTheShieldParentedSoItsFootprintScaleCannotCompound()
    {
        Vector3 originalLocalPosition = shieldChild.localPosition;
        Quaternion originalLocalRotation = shieldChild.localRotation;
        SetShieldDirection(new Vector2Int(1, 0));

        InvokeApplyShieldState(true);
        Assert.That(
            shieldChild.parent,
            Is.SameAs(unitObject.transform),
            "The shield must stay parented: its footprint is sized against the caster's own scale, "
                + "so reparenting in and out of that scale compounds its width every raise."
        );
        Vector3 raisedLossyScale = shieldChild.lossyScale;

        InvokeApplyShieldState(false);
        InvokeApplyShieldState(true);
        Assert.That(
            shieldChild.lossyScale,
            Is.EqualTo(raisedLossyScale).Using(Vector3EqualityComparer.Instance),
            "Raising the shield a second time must not grow it again."
        );

        InvokeApplyShieldState(false);
        Assert.That(
            Vector3.Distance(shieldChild.localPosition, originalLocalPosition),
            Is.LessThan(0.001f)
        );
        Assert.That(
            Quaternion.Angle(shieldChild.localRotation, originalLocalRotation),
            Is.LessThan(0.01f)
        );
    }

    [Test]
    public void ExecuteAbility_NeverRaisesTheShieldWithoutServerAuthority()
    {
        var routine = shieldStance.ExecuteAbility(Vector3.zero, 0f);
        bool hasMore = routine.MoveNext();

        Assert.That(hasMore, Is.False, "A non-server call must yield break immediately.");
        Assert.That(GetShieldActive(), Is.False);
    }

    private void InvokeApplyShieldState(bool active)
    {
        MethodInfo method = typeof(ShieldStance).GetMethod(
            "ApplyShieldState",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(method, Is.Not.Null, "Missing private method ApplyShieldState.");
        method.Invoke(shieldStance, new object[] { active });
    }

    private bool GetShieldActive()
    {
        FieldInfo field = typeof(ShieldStance).GetField(
            "shieldActive",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null, "Missing private field shieldActive.");
        var shieldActive = (NetworkVariable<bool>)field.GetValue(shieldStance);
        return shieldActive.Value;
    }

    private Vector2Int GetShieldDirection()
    {
        var shieldDirection = (NetworkVariable<Vector2Int>)GetShieldDirectionField()
            .GetValue(shieldStance);
        return shieldDirection.Value;
    }

    private void SetShieldDirection(Vector2Int direction)
    {
        var shieldDirection = (NetworkVariable<Vector2Int>)GetShieldDirectionField()
            .GetValue(shieldStance);
        shieldDirection.Value = direction;
    }

    private static FieldInfo GetShieldDirectionField()
    {
        FieldInfo field = typeof(ShieldStance).GetField(
            "shieldDirection",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null, "Missing private field shieldDirection.");
        return field;
    }

    private static void SetTeamIndexWithoutTriggeringPresentation(Unit identity, int teamIndex)
    {
        FieldInfo field = typeof(Unit).GetField(
            "teamIndex",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.That(field, Is.Not.Null, "Missing private field Unit.teamIndex.");
        var teamIndexVariable = (NetworkVariable<int>)field.GetValue(identity);
        teamIndexVariable.Value = teamIndex;
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
