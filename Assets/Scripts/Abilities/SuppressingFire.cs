using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Salvo's active: turn to face the chosen direction, then empty a long, wide-spread burst of
/// ordinary bullets down it. The bullets ARE the ability — they carry the damage, the spread, the
/// tracers and the impacts, the same way every other weapon in this game works.
/// <para>
/// The designer's original brief asked for a barrage that reached "through adjacent walls", and an
/// earlier version delivered that with a wall-piercing bullet flag. That was withdrawn: bullets are
/// now destroyed by walls without exception ("Bullets should be destroyed if they hit a wall"), so
/// this is a wide, sustained volley that respects cover like every other shot on the board. Nothing
/// here needs to know about walls — the rounds simply stop on them.
/// </para>
/// <para>
/// An even earlier version resolved damage with an invisible <c>Physics.OverlapSphere</c> in front of
/// the caster and fired a handful of separate, purely decorative bullets documented as not
/// mattering. That was wrong twice over: the thing the player watched was not the thing that dealt
/// the damage, and a barrage's whole character — where the shots went, what they hit, how long it
/// lasted — was invented by a query rather than played out.
/// </para>
/// </summary>
public class SuppressingFire : Ability
{
    /// <summary>
    /// Telegraph before the first round leaves the barrel. This is the window the designer's
    /// "interrupted if stunned" clause lives in: land a stun inside it and nothing is fired at all.
    /// </summary>
    private const float WindUpSeconds = 0.5f;

    /// <summary>
    /// How many rounds the barrage puts downrange, and how fast they leave. Tuned down from a
    /// 24-round burst at 0.045s spacing on the designer's note that fire rate and ability speed were
    /// too high across the new characters: the volley now reads as a deliberate, trackable stream of
    /// individual shots rather than a solid wall of them arriving at once.
    /// </summary>
    private const int BarrageShots = 12;
    private const float SecondsBetweenShots = 0.14f;

    /// <summary>
    /// Dead time after the barrage before this unit may shoot again. A machine gun that has just
    /// emptied a burst is the clearest case for a recovery: without it the ability read as a free
    /// extra magazine bolted onto the round's normal shooting.
    /// </summary>
    private const float RecoverySeconds = 1f;

    /// <summary>
    /// Half-angle of the spray, in degrees. Wide on purpose — the brief says "wide barrage", so a
    /// target does not have to be stood dead-centre in the chosen direction to be caught by it.
    /// Deliberately not read from <c>UnitData.bulletSpread</c>: that governs Salvo's ordinary
    /// shooting, and the point of the ability is to be wider than that.
    /// </summary>
    private const float SpreadDegrees = 18f;

    /// <summary>
    /// Reach of the barrage in cells, overriding the weapon's own bulletRange. Shorter than normal
    /// fire: the trade for the width and volume is that it only threatens what is fairly close.
    /// </summary>
    private const float BarrageRangeCells = 4f;

    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 0)
    {
        if (!IsServer)
            yield break;

        Movement movement = GetComponent<Movement>();
        Shooting shooting = GetComponent<Shooting>();
        Unit identity = GetComponent<Unit>();
        UnitData data = movement != null ? movement.unitData : null;
        if (movement == null || shooting == null || identity == null || data == null)
            yield break;

        // The aim is just "toward the square the player picked". Snapped to the grid direction of
        // that square rather than the raw world vector so the barrage lines up with the board the
        // player was looking at when they chose it.
        Vector2Int casterCell = GridSystem.ConvertToGridCoords(
            GridSystem.GetNearestGridCell(gameObject)
        );
        Vector2Int targetCell = GridSystem.ConvertToGridCoords(abilitySquare);
        if (
            !GridSystem.TryGetAdjacentDirection(casterCell, targetCell, out Vector2Int direction)
        )
        {
            // The planner and SanitizeAbilityPlan both reject a non-adjacent square before this
            // runs, so arriving here means the caster is misconfigured rather than misaimed. Firing
            // a two-dozen-round burst down a direction nobody chose is worse than firing none.
            Debug.LogWarning(
                $"[SuppressingFire] {name} could not resolve a barrage direction from "
                    + $"{abilitySquare} (caster at {casterCell}); aborting. Check this unit's "
                    + "UnitData has selectAbilityDirection and abilityFixedDistance set."
            );
            yield break;
        }

        Vector3 direction3D = new(direction.x, 0f, direction.y);

        // Movement and normal auto-fire both stand down: the barrage owns this unit's weapon for
        // its duration, and Shooting.InitiateShooting would otherwise be picking its own targets
        // and firing at them in the middle of it.
        movement.PauseMovement();
        shooting.PauseShooting();

        yield return StartCoroutine(
            movement.RotateToFaceTarget(
                transform.position + direction3D * GameLoop.cellSize,
                data.rotationSpeed
            )
        );

        yield return new WaitForSeconds(WindUpSeconds);

        // The designer's explicit interrupt: a stun landed during the wind-up fizzles the whole
        // barrage rather than delaying it. Checked after the wait, so the window that matters is
        // the telegraph the enemy can actually see and react to.
        if (identity.IsStunned)
        {
            movement.transitionToShooting();
            yield break;
        }

        BarrageFxClientRpc(transform.position, direction3D);

        for (int shot = 0; shot < BarrageShots; shot++)
        {
            // A stun landing mid-burst cuts it short too. The barrage is a sustained action, not an
            // instant one, so an interrupt that only counted before the first round would make the
            // stun feel like it did nothing for most of the ability's duration.
            if (identity.IsStunned)
                break;

            float spread = Random.Range(-SpreadDegrees, SpreadDegrees);
            Vector3 shotDirection = Quaternion.AngleAxis(spread, Vector3.up) * direction3D;

            shooting.FireBulletInDirection(shotDirection, range: BarrageRangeCells);

            yield return new WaitForSeconds(SecondsBetweenShots);
        }

        // Recovery before the weapon is handed back -- see RecoverySeconds.
        yield return new WaitForSeconds(RecoverySeconds);
        movement.transitionToShooting();
    }

    /// <summary>
    /// The muzzle end of the barrage. The rounds themselves are real bullets with their own tracers
    /// and impacts, so all this adds is the one thing they cannot show on their own: a sustained
    /// flash at the barrel saying the noise is coming from here.
    /// </summary>
    [ClientRpc]
    private void BarrageFxClientRpc(Vector3 origin, Vector3 direction)
    {
        Color muzzle = AbilityJuice.Alarm;
        Vector3 muzzlePosition = origin + direction.normalized * (GameLoop.cellSize * 0.4f);

        ImpactCore.Spawn(muzzlePosition, muzzle, 1.1f, 1.4f);
        CameraEffects.Instance?.CameraShakeClientRpc(
            BarrageShots * SecondsBetweenShots,
            0.02f
        );
    }
}
