using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The defensive half of what used to be Shield's kit, with the rush and the ally speed buff both
/// stripped out: the shield raises facing whichever adjacent direction the caster targeted (same
/// direction-pick input as DashRush's dash), holds for <see cref="AbilityDurationSeconds"/>, then
/// lowers. Reuses Shield's static footprint/collision helpers directly rather than duplicating
/// them, since those are pure functions of a shield transform and a team index that don't care
/// which ability raised it.
///
/// The shield stays parented to the caster the whole time, but while raised its local rotation is
/// re-solved every frame (see <see cref="LateUpdate"/>) to cancel out the caster's own yaw, so the
/// world facing holds at whatever direction the player picked. Shooting keeps rotating the caster to
/// track targets while the shield is up, and a shield that simply inherited that rotation would
/// swing with every retarget. Counter-rotating rather than detaching to world space is deliberate:
/// the shield's footprint is sized as a fraction of the caster's own scale (see
/// <see cref="Shield.TryExpandShieldFootprint"/>), and reparenting it in and out of that scale
/// compounds the width/height every time it is raised.
/// </summary>
public class ShieldStance : Ability
{
    public const float AbilityDurationSeconds = Shield.AbilityDurationSeconds;

    private Transform shieldTransform;
    private bool shieldFootprintExpanded;
    private Vector3 loweredLocalPosition;
    private Quaternion loweredLocalRotation;
    private bool capturedLoweredTransform;

    // NetworkVariables (not ClientRpc) so shield state survives fog NetworkHide/NetworkShow, same
    // reasoning as Shield.shieldActive.
    private NetworkVariable<bool> shieldActive = new(false);
    private NetworkVariable<Vector2Int> shieldDirection = new(Vector2Int.zero);

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        shieldTransform = transform.Find("Shield");
        CaptureLoweredTransform();
        EnsureExpandedShieldFootprint();
        shieldActive.OnValueChanged += OnShieldStateChanged;
        shieldDirection.OnValueChanged += OnShieldStateChanged;
        ApplyShieldState(shieldActive.Value);
    }

    public override void OnNetworkDespawn()
    {
        shieldActive.OnValueChanged -= OnShieldStateChanged;
        shieldDirection.OnValueChanged -= OnShieldStateChanged;
        base.OnNetworkDespawn();
    }

    public override void ResetForRespawn()
    {
        base.ResetForRespawn();
        if (IsServer)
            shieldActive.Value = false;
        ApplyShieldState(false);
    }

    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 0)
    {
        if (!IsServer)
            yield break;

        Vector2Int casterCell = GridSystem.ConvertToGridCoords(
            GridSystem.GetNearestGridCell(gameObject)
        );
        if (
            !GridSystem.TryGetAdjacentDirection(
                casterCell,
                GridSystem.ConvertToGridCoords(abilitySquare),
                out Vector2Int direction
            )
        )
        {
            // Reaching here means the plan named a cell that is not adjacent, which the planner and
            // SanitizeAbilityPlan both reject — so it indicates the caster is misconfigured rather
            // than misaimed. Raise the shield the way the unit is already facing instead of at a
            // fixed compass direction: a hardcoded north looks like a working shield pointing the
            // wrong way, which is far harder to spot than one that simply faces front.
            direction = NearestCardinal(transform.forward);
            Debug.LogWarning(
                $"[ShieldStance] {name} could not resolve a shield direction from {abilitySquare} "
                    + $"(caster at {casterCell}); falling back to its current facing {direction}. "
                    + "Check this unit's UnitData has selectAbilityDirection and abilityFixedDistance set."
            );
        }

        shieldDirection.Value = direction;
        shieldActive.Value = true;
        yield return new WaitForSeconds(AbilityDurationSeconds);
        shieldActive.Value = false;
    }

    private void OnShieldStateChanged<T>(T previousValue, T newValue)
    {
        ApplyShieldState(shieldActive.Value);
    }

    private void ApplyShieldState(bool active)
    {
        if (shieldTransform == null)
            shieldTransform = transform.Find("Shield");
        CaptureLoweredTransform();
        EnsureExpandedShieldFootprint();
        if (shieldTransform == null)
            return;

        Unit identity = GetComponent<Unit>();
        Shield.TryApplyCollisionLayer(shieldTransform, identity != null ? identity.TeamIndex : -1);

        Vector2Int direction = shieldDirection.Value;
        if (active && direction != Vector2Int.zero)
        {
            // The lock lives on the shield itself rather than here: Ability disables its own
            // component on every non-server peer (see Ability.OnNetworkSpawn), so a LateUpdate on
            // this class would only ever run on the host and clients would see the shield swing
            // with the caster.
            FixedWorldFacing facing = shieldTransform.GetComponent<FixedWorldFacing>();
            if (facing == null)
                facing = shieldTransform.gameObject.AddComponent<FixedWorldFacing>();

            Vector3 worldDirection = new(direction.x, 0f, direction.y);
            facing.worldForward = worldDirection;
            // The standoff has to be pinned in world space as well as the facing. The shield is
            // parented to the caster, so its authored local offset (out in front, slightly to one
            // side, at chest height) is rotated by the caster's own yaw -- and Shooting turns the
            // caster to track targets all round. Pinning only the rotation therefore produced a
            // shield that faced the chosen direction while orbiting bodily around the unit, ending up
            // beside or behind it. Re-aiming the authored offset at the chosen direction keeps the
            // slab planted on that side for as long as it is raised.
            facing.pinsWorldOffset = true;
            facing.worldOffset = Quaternion.LookRotation(worldDirection, Vector3.up)
                * ScaledLoweredOffset();
        }
        else if (capturedLoweredTransform)
        {
            FixedWorldFacing facing = shieldTransform.GetComponent<FixedWorldFacing>();
            if (facing != null)
                facing.pinsWorldOffset = false;
            shieldTransform.localRotation = loweredLocalRotation;
            shieldTransform.localPosition = loweredLocalPosition;
        }

        shieldTransform.gameObject.SetActive(active);
    }

    /// <summary>
    /// The shield's authored resting offset with the caster's scale applied but its rotation removed,
    /// so it can be re-aimed at an arbitrary world direction. Read from the captured lowered position
    /// rather than hardcoded, so moving the shield on the prefab moves the raised one with it.
    /// </summary>
    private Vector3 ScaledLoweredOffset()
    {
        Vector3 parentScale = shieldTransform.parent != null
            ? shieldTransform.parent.lossyScale
            : Vector3.one;
        return new Vector3(
            loweredLocalPosition.x * parentScale.x,
            loweredLocalPosition.y * parentScale.y,
            loweredLocalPosition.z * parentScale.z
        );
    }

    /// <summary>The grid direction a world-space forward vector points most nearly along.</summary>
    private static Vector2Int NearestCardinal(Vector3 worldForward)
    {
        if (Mathf.Abs(worldForward.x) > Mathf.Abs(worldForward.z))
            return worldForward.x >= 0f ? Vector2Int.right : Vector2Int.left;
        return worldForward.z >= 0f ? Vector2Int.up : Vector2Int.down;
    }

    private void CaptureLoweredTransform()
    {
        if (capturedLoweredTransform || shieldTransform == null)
            return;
        loweredLocalPosition = shieldTransform.localPosition;
        loweredLocalRotation = shieldTransform.localRotation;
        capturedLoweredTransform = true;
    }

    private void EnsureExpandedShieldFootprint()
    {
        if (!shieldFootprintExpanded && Shield.TryExpandShieldFootprint(shieldTransform))
            shieldFootprintExpanded = true;
    }
}
