using System.Collections;
using UnityEngine;

/// <summary>
/// Farsight's active: three piercing arrows fired in one chosen direction at slightly diverging
/// angles -- a horizontal fan, not a single shot. The designer's brief verbatim: "Basic kit
/// pierces. Ability is it launches a cone of three arrows horizontally." The basic weapon's pierce
/// is plain <see cref="UnitData.bulletPierces"/> data wired through <see cref="Shooting.FireBullet"/>
/// already and is not this class's concern; this class only fires the three-shot fan and makes
/// sure every one of those three shots pierces regardless of how that data field ends up set.
/// <para>
/// Each of the three shots goes out via <see cref="Shooting.FireBulletInDirection"/> rather than
/// <see cref="Shooting.FireBullet"/>: the fan's three headings are exact, fixed offsets from the
/// direction the player chose (see <see cref="FanSpreadDegrees"/>), not a random draw, and
/// <c>FireBulletInDirection</c> exists specifically so a caller can hand it one explicit
/// world-space direction per shot instead of <c>FireBullet</c>'s <c>transform.forward</c>-plus-
/// random-spread. Firing three real, separately-aimed bullets (rather than one bullet with some
/// wide hit-scan cone) is also what makes "which of the three arrows hit" legible to a player
/// watching the fight, the same way SuppressingFire's cosmetic bullets are real bullets rather than
/// a pure damage query with a VFX bolted on.
/// </para>
/// <para>
/// Never overrides <see cref="Ability.BuildPlannedPath"/>: like <see cref="AreaLock"/>,
/// <see cref="ShieldStance"/> and <see cref="ChainSurge"/>, this ability never relocates the
/// caster, so there is no travel route to preview and the base class's "report nothing to draw"
/// default (<see cref="AbilityPathKind.None"/>) is already correct.
/// </para>
/// </summary>
public class TripleVolley : Ability
{
    /// <summary>
    /// How far the two outer arrows diverge from the centre shot, in degrees, rotated around
    /// <see cref="Vector3.up"/> the same way <see cref="SuppressingFire"/>'s own cosmetic-bullet
    /// jitter rotates its aim. Fixed and identical on both sides -- unlike that jitter, which is
    /// randomized per shot -- because these three headings are the whole point of the ability and
    /// have to land in the same three places every time it is cast.
    /// </summary>
    private const float FanSpreadDegrees = 12f;

    /// <summary>
    /// Gap between the three arrows. They used to leave on the same frame, which made the volley an
    /// instant event with nothing to watch; loosing them in sequence is both readable and slower,
    /// per the designer's note on ability speed.
    /// </summary>
    private const float SecondsBetweenArrows = 0.14f;

    /// <summary>Dead time after the volley before this unit may shoot again.</summary>
    private const float RecoverySeconds = 0.75f;

    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 0)
    {
        if (!IsServer)
            yield break;

        Movement movement = GetComponent<Movement>();
        Shooting shooting = GetComponent<Shooting>();
        UnitData data = movement != null ? movement.unitData : null;
        if (movement == null || shooting == null || data == null)
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
            // Same reasoning as SuppressingFire's own fallback: reaching here means the plan named
            // a cell that is not adjacent, which the planner and SanitizeAbilityPlan both reject
            // before this ever runs -- so it names a misconfigured caster, not a misaimed one. A
            // fan of three precisely-angled shots has no "face wherever the unit already happens
            // to be pointed" fallback that reads as intentional, so this bails out cleanly instead
            // of firing a three-arrow volley down a direction nobody chose.
            Debug.LogWarning(
                $"[TripleVolley] {name} could not resolve a volley direction from {abilitySquare} "
                    + $"(caster at {casterCell}); aborting the ability. Check this unit's UnitData "
                    + "has selectAbilityDirection and abilityFixedDistance set."
            );
            yield break;
        }

        // Manual shots are about to be fired outside of Shooting's own auto-fire loop
        // (InitiateShooting); pausing first stops that coroutine from also trying to fire this
        // frame and racing the volley below, the same discipline SuppressingFire uses for its own
        // manual bullets.
        shooting.PauseShooting();

        Vector3 direction3D = new(direction.x, 0f, direction.y);
        Vector3 facingTarget = transform.position + direction3D * GameLoop.cellSize;
        yield return StartCoroutine(movement.RotateToFaceTarget(facingTarget, data.rotationSpeed));

        // Damage is left at -1 so FireBulletInDirection falls back to unitData.damage itself, the
        // same way ordinary FireBullet calls elsewhere do -- there is no designer ask for this
        // ability's arrows to hit harder or softer than the basic weapon's own shots, only for
        // three of them to go out at once. Pierce is passed explicitly as true on every shot,
        // rather than left to default to unitData.bulletPierces, so this ability's fan always
        // pierces even if whoever wires Farsight's UnitData asset gets that field wrong.
        Vector3[] fanDirections = ComputeFanDirections(direction3D);
        for (int index = 0; index < fanDirections.Length; index++)
        {
            shooting.FireBulletInDirection(fanDirections[index], pierces: true);
            if (index < fanDirections.Length - 1)
                yield return new WaitForSeconds(SecondsBetweenArrows);
        }

        // Recovery before the weapon is handed back -- see RecoverySeconds.
        yield return new WaitForSeconds(RecoverySeconds);
        movement.transitionToShooting();
    }

    /// <summary>
    /// The three world-space headings the fan fires along for a given <paramref name="baseDirection"/>:
    /// the direction itself, flattened to the horizontal plane and normalized, plus one shot rotated
    /// <see cref="FanSpreadDegrees"/> to either side of it around <see cref="Vector3.up"/>. Pulled out
    /// as its own pure function -- rather than inlined in the coroutine above -- purely so the exact
    /// three headings this ability commits to have a seam an edit-mode test can call directly, the
    /// same way DashRush's line-sweep and SuppressingFire's barrage query are pulled out of their own
    /// coroutines for the same reason.
    /// </summary>
    public static Vector3[] ComputeFanDirections(Vector3 baseDirection)
    {
        Vector3 flatDirection = new(baseDirection.x, 0f, baseDirection.z);
        if (flatDirection.sqrMagnitude <= Mathf.Epsilon)
            return new[] { Vector3.zero, Vector3.zero, Vector3.zero };

        flatDirection.Normalize();
        return new[]
        {
            Quaternion.AngleAxis(-FanSpreadDegrees, Vector3.up) * flatDirection,
            flatDirection,
            Quaternion.AngleAxis(FanSpreadDegrees, Vector3.up) * flatDirection,
        };
    }
}
