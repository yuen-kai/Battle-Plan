using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools.Utils;

// ShieldStance is the stationary half of what used to be ShieldRush's kit: it reuses ShieldRush's own
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
    private readonly List<GameObject> spawnedObjects = new();

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
        foreach (GameObject spawned in spawnedObjects)
        {
            if (spawned != null)
                Object.DestroyImmediate(spawned);
        }
        spawnedObjects.Clear();
    }

    [Test]
    public void AbilityDurationSeconds_ReusesShieldsExistingDurationRatherThanANewNumber()
    {
        Assert.That(ShieldStance.AbilityDurationSeconds, Is.EqualTo(ShieldRush.AbilityDurationSeconds));
    }

    [Test]
    public void ApplyShieldState_ExpandsTheFootprintByTheSameAmountShieldsOwnHelperDoes()
    {
        float widthBefore = Mathf.Abs(shieldCollider.size.x * shieldChild.lossyScale.x);

        InvokeApplyShieldState(true);

        float widthAfter = Mathf.Abs(shieldCollider.size.x * shieldChild.lossyScale.x);
        Assert.That(
            widthAfter - widthBefore,
            Is.EqualTo(ShieldRush.ShieldWidthIncreaseCellsPerSide * 2f * GameLoop.cellSize).Within(0.001f)
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

    [TestCase(GameLoop.HostTeamIndex, ShieldRush.BlueShieldLayerName)]
    [TestCase(GameLoop.OpponentTeamIndex, ShieldRush.RedShieldLayerName)]
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

    [Test]
    public void TryGetRaisedSlab_StandsWhereTheRaisedShieldsOwnPinningWouldPutIt()
    {
        GiveTheShieldAMesh();
        unitObject.transform.position = GameLoop.gridCoordToWorld(new Vector2Int(3, 3));
        shieldChild.localPosition = new Vector3(-0.151f, 0.283f, 1.587f);
        shieldChild.localScale = new Vector3(2.95f, 2.12f, 0.18f);

        Assert.That(
            shieldStance.TryGetRaisedSlab(
                GameLoop.gridCoordToWorld(new Vector2Int(4, 3)),
                out ShieldStance.RaisedSlab slab
            ),
            Is.True
        );

        SetShieldDirection(new Vector2Int(1, 0));
        InvokeApplyShieldState(true);
        FixedWorldFacing facing = shieldChild.GetComponent<FixedWorldFacing>();
        Assert.That(
            slab.Position,
            Is.EqualTo(unitObject.transform.position + facing.worldOffset)
                .Using(Vector3EqualityComparer.Instance),
            "A preview that does not stand on the shield's own pinned offset promises cover "
                + "somewhere the slab will not be."
        );
        Assert.That(
            Quaternion.Angle(
                slab.Rotation,
                Quaternion.LookRotation(facing.worldForward.normalized, Vector3.up)
            ),
            Is.LessThan(0.01f)
        );
        Assert.That(
            slab.Scale,
            Is.EqualTo(shieldChild.lossyScale).Using(Vector3EqualityComparer.Instance),
            "The preview has to carry the widened footprint, since that is the width the raised "
                + "shield actually blocks with."
        );
    }

    [Test]
    public void TryGetRaisedSlab_RefusesACellTheShieldCouldNotBeRaisedToward()
    {
        GiveTheShieldAMesh();
        unitObject.transform.position = GameLoop.gridCoordToWorld(new Vector2Int(3, 3));

        Assert.That(
            shieldStance.TryGetRaisedSlab(
                GameLoop.gridCoordToWorld(new Vector2Int(6, 3)),
                out _
            ),
            Is.False,
            "Only the adjacent direction cells resolve a facing; anything else has no slab to draw."
        );
    }

    [Test]
    public void Create_BuildsTheDodgePreviewWithoutAnyColliderToBeResolvedAgainst()
    {
        GiveTheShieldAMesh();
        unitObject.transform.position = GameLoop.gridCoordToWorld(new Vector2Int(3, 3));
        shieldStance.TryGetRaisedSlab(
            GameLoop.gridCoordToWorld(new Vector2Int(4, 3)),
            out ShieldStance.RaisedSlab slab
        );

        GameObject preview = ShieldStancePreview.Create(
            unitObject,
            GameLoop.gridCoordToWorld(new Vector2Int(4, 3)),
            Color.white
        );
        Assert.That(preview, Is.Not.Null);
        spawnedObjects.Add(preview);

        Assert.That(
            preview.GetComponentsInChildren<Collider>(true),
            Is.Empty,
            "The preview is information, not cover: a collider on it would let a line of fire, a "
                + "vision check or a dodge alert be answered against a shield that is not up yet."
        );
        Assert.That(
            preview.GetComponent<MeshFilter>().sharedMesh,
            Is.SameAs(shieldChild.GetComponent<MeshFilter>().sharedMesh)
        );
        Assert.That(
            preview.transform.position,
            Is.EqualTo(slab.Position).Using(Vector3EqualityComparer.Instance)
        );
        Assert.That(
            preview.GetComponent<Renderer>().sharedMaterial.color.a,
            Is.GreaterThan(0f).And.LessThan(1f),
            "A solid slab would read as a shield that is already blocking."
        );
    }

    [Test]
    public void Create_CutsTheSlabFromTheShippingSentinelsOwnShield()
    {
        GameObject sentinel = InstantiateSentinelAt(new Vector2Int(3, 3));

        GameObject preview = ShieldStancePreview.Create(
            sentinel,
            GameLoop.gridCoordToWorld(new Vector2Int(3, 4)),
            Color.white
        );
        Assert.That(
            preview,
            Is.Not.Null,
            "The dodge window previews the Sentinel's shield off the unit's own geometry, so the "
                + "shipping prefab has to carry a mesh the slab can be cut from."
        );
        spawnedObjects.Add(preview);
        Assert.That(preview.GetComponentsInChildren<Collider>(true), Is.Empty);

        Vector3 offsetFromCaster = preview.transform.position - sentinel.transform.position;
        Assert.That(
            offsetFromCaster.z,
            Is.GreaterThan(0f),
            "A shield raised toward the cell to the north stands north of its caster."
        );
        Assert.That(
            Mathf.Abs(offsetFromCaster.x),
            Is.LessThan(GameLoop.cellSize),
            "The slab is planted in front of the unit rather than orbiting off to one side."
        );
        Assert.That(
            preview.transform.localScale.x,
            Is.GreaterThan(ShieldRush.ShieldWidthIncreaseCellsPerSide * 2f * GameLoop.cellSize),
            "The previewed slab has to carry the widened footprint the raised shield blocks with."
        );
    }

    [TestCase(false, 2, true, TestName = "A route drawn for the caster cancels the shield")]
    [TestCase(false, 1, false, TestName = "A caster left standing keeps its shield")]
    [TestCase(true, 2, false, TestName = "An ability plan is not a dive")]
    public void HasMovementOrderFor_IsWhatDropsThePreviewWhileTheRouteIsStillBeingDrawn(
        bool abilityPlan,
        int routeLength,
        bool expected
    )
    {
        GameObject plannerObject = new GameObject("DodgePlanner");
        spawnedObjects.Add(plannerObject);
        PlanMovement planner = plannerObject.AddComponent<PlanMovement>();

        List<Vector3> route = new();
        for (int step = 0; step < routeLength; step++)
            route.Add(GameLoop.gridCoordToWorld(new Vector2Int(3 + step, 3)));
        planner.plans[unitObject] = (abilityPlan, route);

        Assert.That(planner.HasMovementOrderFor(unitObject), Is.EqualTo(expected));
    }

    [Test]
    public void BuildAbilityPreview_ShowsTheSlabWhileTheDirectionIsStillBeingPicked()
    {
        const string dataPath = "Assets/UnitStats/Sentinel.asset";
        UnitData sentinelData = UnityEditor.AssetDatabase.LoadAssetAtPath<UnitData>(dataPath);
        Assert.That(sentinelData, Is.Not.Null, $"Could not load {dataPath}.");

        Vector3 start = GameLoop.gridCoordToWorld(new Vector2Int(3, 3));
        GameObject sentinel = InstantiateSentinelAt(new Vector2Int(3, 3));

        GameObject preview = PlanMovement.BuildAbilityPreview(
            null,
            sentinel,
            start,
            GameLoop.gridCoordToWorld(new Vector2Int(3, 4)),
            sentinelData,
            rosterSlot: 0,
            selected: true
        );
        spawnedObjects.Add(preview);

        ShieldStancePreview slab = preview.GetComponentInChildren<ShieldStancePreview>(true);
        Assert.That(
            slab,
            Is.Not.Null,
            "A shield aimed on the planning board has to show the wall it will raise, not only the "
                + "cell it faces: the facing is the whole of what the order decides."
        );
        Assert.That(
            slab.transform.position.z,
            Is.GreaterThan(start.z),
            "The slab stands toward the cell the direction was picked from."
        );
        Assert.That(
            preview.GetComponentsInChildren<Collider>(true),
            Is.Empty,
            "A plan is not cover: the preview must not put a collider on the board."
        );
    }

    [Test]
    public void BuildAbilityPreview_DimsTheSlabOfAUnitThatIsNotTheOneBeingGivenOrders()
    {
        float selectedAlpha = BuildSentinelPlanSlabAlpha(selected: true);
        float unselectedAlpha = BuildSentinelPlanSlabAlpha(selected: false);

        Assert.That(
            unselectedAlpha,
            Is.GreaterThan(0f).And.LessThan(selectedAlpha),
            "The slab steps back with the rest of its plan when another card is in hand, rather "
                + "than drawing as loudly as the order being edited."
        );
    }

    private float BuildSentinelPlanSlabAlpha(bool selected)
    {
        UnitData sentinelData = UnityEditor.AssetDatabase.LoadAssetAtPath<UnitData>(
            "Assets/UnitStats/Sentinel.asset"
        );
        Vector3 start = GameLoop.gridCoordToWorld(new Vector2Int(3, 3));
        GameObject sentinel = InstantiateSentinelAt(new Vector2Int(3, 3));

        GameObject preview = PlanMovement.BuildAbilityPreview(
            null,
            sentinel,
            start,
            GameLoop.gridCoordToWorld(new Vector2Int(3, 4)),
            sentinelData,
            rosterSlot: 0,
            selected
        );
        spawnedObjects.Add(preview);

        ShieldStancePreview slab = preview.GetComponentInChildren<ShieldStancePreview>(true);
        Assert.That(slab, Is.Not.Null);
        return slab.GetComponent<Renderer>().sharedMaterial.color.a;
    }

    private GameObject InstantiateSentinelAt(Vector2Int cell)
    {
        const string prefabPath = "Assets/Prefabs/Units/Sentinel.prefab";
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Assert.That(prefab, Is.Not.Null, $"Could not load {prefabPath}.");

        GameObject sentinel = Object.Instantiate(prefab);
        spawnedObjects.Add(sentinel);
        sentinel.transform.position = GameLoop.gridCoordToWorld(cell);
        return sentinel;
    }

    private void GiveTheShieldAMesh()
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh mesh = cube.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(cube);
        shieldChild.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        shieldChild.gameObject.AddComponent<MeshRenderer>();
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
