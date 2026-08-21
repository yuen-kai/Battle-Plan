using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Breach's active: a heavy rocket fired FLAT at a chosen square — the designer's direction,
/// verbatim, is "rocket launcher ability should be a horizontal rocket (no arch)". It travels level
/// at launch height, detonates on the first thing it reaches, deals big area damage, and permanently
/// removes any wall inside the blast.
/// <para>
/// Flying flat rather than lobbing is what makes the wall-breaking mean something. A lobbed shell
/// arcs OVER cover and lands behind it, so "breaks walls" could only ever apply to whatever happened
/// to be under the landing point. A flat rocket runs into the wall instead — the wall is what stops
/// it, and the wall is therefore what it blows open. The two halves of the brief only line up in the
/// horizontal version.
/// </para>
/// <para>
/// Where it stops is resolved by raycast against the "Walls" layer rather than by walking grid
/// cells: the target square can sit diagonally from the caster, and a ray follows the line the
/// rocket is actually drawn travelling along instead of an approximation of it.
/// </para>
/// </summary>
public class BunkerBuster : Ability
{
    /// <summary>
    /// Cells per second. Slow enough to read as heavy ordnance crossing the board — the same "slow
    /// projectile" trait the basic attack has — and tuned down from 6 on the designer's note that
    /// ability speed was too high across the new characters. A rocket that can be watched travelling
    /// is also a rocket that can be reacted to.
    /// </summary>
    private const float RocketSpeedCellsPerSecond = 3.5f;

    /// <summary>
    /// How far the rocket flies, in cells. A SET range, per the designer: the shot always travels
    /// exactly this far down the aimed line and detonates at the end of it, whatever square was
    /// picked — the only things that cut it short are an enemy or a wall in the way.
    /// <para>
    /// Targeting and range are deliberately two separate things here. Aiming works like the Sniper's
    /// Area Lock — the whole board is selectable (<c>abilitySquareRange 99</c>) and the chosen square
    /// names a DIRECTION, not a destination — while this constant alone decides how far down that
    /// line the warhead gets. So picking a near square does not shorten the shot and picking a far
    /// one does not lengthen it; both simply aim it.
    /// </para>
    /// </summary>
    private const float RocketRangeCells = 8f;

    /// <summary>
    /// Dead time after the shot before this unit may shoot again. A launcher this size does not
    /// reacquire instantly, and the pause is what stops the ability from being a free extra volley
    /// on top of a normal round's shooting. Applied by holding the ability's own coroutine open at
    /// the very end rather than through any shared mechanism — recovery is per-ability, and only the
    /// characters added in this batch have one.
    /// </summary>
    private const float RecoverySeconds = 1.1f;

    /// <summary>
    /// How high off the deck the rocket flies, as a fraction of a cell. Launched and flown at a
    /// constant height: level flight is the entire point, so this never varies over the trajectory.
    /// </summary>
    private const float FlightHeightCells = 0.45f;

    /// <summary>
    /// Tuned down from 90 on the designer's note that the ability should be "less big and less
    /// damaging". At 90 a single rocket erased a full-health standard unit outright, which left no
    /// room for the basic attack — now also an exploding round — to be the character's bread and
    /// butter.
    /// </summary>
    [SerializeField]
    private float damage = 55f;

    public GameObject rocketPrefab;
    public GameObject rocketExplosionPrefab;

    public override AbilityPathKind BuildPlannedPath(
        Vector3 targetSquare,
        UnitData data,
        List<Vector3> points
    )
    {
        // A flat run, not a lob — the preview has to show the straight line the rocket will take,
        // including the fact that a wall in the way is where it will actually stop.
        return AbilityTrajectory.BuildGroundRun(
            transform.position,
            ResolveImpactPoint(targetSquare, out _),
            points
        )
            ? AbilityPathKind.Ground
            : AbilityPathKind.None;
    }

    /// <summary>
    /// Where the rocket detonates: <see cref="RocketRangeCells"/> down the aimed line, or wherever an
    /// enemy or a wall interrupts it first. Shared by the planning preview and the live shot, so the
    /// line drawn while planning ends exactly where the rocket will actually go off.
    /// </summary>
    /// <param name="interrupted">True when something stopped the shot short of its full range.</param>
    private Vector3 ResolveImpactPoint(Vector3 abilitySquare, out bool interrupted)
    {
        Vector3 launchPoint = GetLaunchPoint();
        Vector3 aimPoint = new(abilitySquare.x, launchPoint.y, abilitySquare.z);

        Vector3 toTarget = aimPoint - launchPoint;
        toTarget.y = 0f;
        interrupted = false;
        if (toTarget.sqrMagnitude <= Mathf.Epsilon)
            return aimPoint;

        Vector3 direction = toTarget.normalized;

        // The square only aims the shot; the distance it travels is this constant and nothing else.
        float travel = RocketRangeCells * GameLoop.cellSize;

        if (
            Physics.Raycast(
                launchPoint,
                direction,
                out RaycastHit hit,
                travel,
                BlockingLayerMask(),
                QueryTriggerInteraction.Ignore
            )
        )
        {
            interrupted = true;
            return hit.point;
        }

        return launchPoint + direction * travel;
    }

    /// <summary>
    /// What can cut the flight short: walls, and enemy bodies. The caster's own team is deliberately
    /// absent, so a rocket never detonates on a friendly standing in the lane.
    /// <para>
    /// The enemy team is resolved from <see cref="Unit.TeamIndex"/> (a NetworkVariable) rather than
    /// from <c>gameObject.tag</c>, because this method also runs during planning to draw the path
    /// preview — and tags are assigned server-side and do not replicate, so a tag-based lookup would
    /// silently produce a walls-only mask on a client and preview a longer flight than the server
    /// will actually resolve.
    /// </para>
    /// </summary>
    private int BlockingLayerMask()
    {
        int walls = LayerMask.GetMask("Walls");

        Unit identity = GetComponent<Unit>();
        if (identity == null || identity.TeamIndex < 0)
            return walls;

        string enemyTeam = GameLoop.GetTeamName(GameLoop.GetEnemyTeamIndex(identity.TeamIndex));
        if (string.IsNullOrEmpty(enemyTeam))
            return walls;

        return walls | LayerMask.GetMask(enemyTeam);
    }

    /// <summary>Muzzle height: level with the flight path, so the rocket does not visibly rise or dip.</summary>
    private Vector3 GetLaunchPoint()
    {
        Vector3 position = transform.position;
        return new Vector3(position.x, FlightHeightCells * GameLoop.cellSize, position.z);
    }

    /// <summary>
    /// Blast radius in cells. Tuned down from 2.2 per the designer ("less big"): a 2.2 blast cleared
    /// a 5-cell-wide circle and took three walls out of a single shot, which read more like a map
    /// edit than a weapon.
    /// </summary>
    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 1.5f)
    {
        // Wall removal is permanent and server-authoritative (see TryDestroyWallCell); running this
        // on a client would either desync the board or silently no-op.
        if (!IsServer)
            yield break;

        Vector3 launchPoint = GetLaunchPoint();
        Vector3 impactPoint = ResolveImpactPoint(abilitySquare, out _);

        Vector3 flight = impactPoint - launchPoint;
        flight.y = 0f;
        Vector3 direction =
            flight.sqrMagnitude > 0.0001f ? flight.normalized : transform.forward;

        Movement movement = GetComponent<Movement>();
        Shooting shooting = GetComponent<Shooting>();
        UnitData data = movement != null ? movement.unitData : null;

        // The launcher owns this unit's weapon for the shot and the recovery that follows. Without
        // standing normal fire down, the unit kept shooting its rifle right through the cast and the
        // recovery below would have meant nothing.
        if (shooting != null)
            shooting.PauseShooting();

        if (movement != null && data != null)
        {
            // Face the shot before taking it: a launcher firing sideways out of its own model is
            // the tell that a projectile was spawned rather than fired.
            yield return StartCoroutine(
                movement.RotateToFaceTarget(
                    transform.position + direction * GameLoop.cellSize,
                    data.rotationSpeed
                )
            );
        }

        GameObject rocket = NetworkHelper.Spawn(
            rocketPrefab,
            launchPoint,
            Quaternion.LookRotation(direction, Vector3.up)
        );

        float travelDistance = Vector3.Distance(launchPoint, impactPoint);
        float travelDuration =
            travelDistance > 0f
                ? travelDistance / (RocketSpeedCellsPerSecond * GameLoop.cellSize)
                : 0f;

        float elapsed = 0f;
        while (elapsed < travelDuration)
        {
            elapsed += Time.deltaTime;
            if (rocket == null)
                break;
            // Straight-line interpolation and a fixed height: no arc, by design.
            rocket.transform.position = Vector3.Lerp(
                launchPoint,
                impactPoint,
                Mathf.Clamp01(elapsed / travelDuration)
            );
            yield return null;
        }
        if (rocket != null)
            rocket.transform.position = impactPoint;

        // Damage first, wall removal second. DetonateRocket's line-of-sight check reads
        // GameLoop.wallLayout as it stands right now, so resolving the blast before any wall in it
        // is cleared means a target that was still behind standing cover at the moment of impact is
        // judged against the board that was actually there — not against the hole this same
        // explosion is about to cut through it.
        CameraEffects.Instance?.CameraShakeClientRpc(0.7f, 0.18f);
        ExplosionFxClientRpc(impactPoint, AreaRadius);
        DetonateRocket(impactPoint, AreaRadius);
        DestroyWallsInBlast(impactPoint, AreaRadius);

        if (rocketExplosionPrefab != null)
        {
            GameObject explosionEffect = NetworkHelper.Spawn(
                rocketExplosionPrefab,
                impactPoint,
                Quaternion.identity
            );
            ParticleSystem particles = explosionEffect.GetComponent<ParticleSystem>();
            NetworkHelper.Instance.Despawn(
                explosionEffect,
                particles != null ? particles.main.duration : 1f
            );
        }
        if (rocket != null)
            NetworkHelper.Instance.Despawn(rocket);

        // Recovery: hold here before handing the weapon back, so the unit visibly cannot shoot for a
        // beat after firing. The wait is the last thing the ability does, which is what makes the
        // round wait for it too (GameLoop.RunAbility counts this coroutine as a running ability).
        yield return new WaitForSeconds(RecoverySeconds);
        if (movement != null)
            movement.transitionToShooting();
    }

    [ClientRpc]
    private void ExplosionFxClientRpc(Vector3 explosionPosition, float areaRadius)
    {
        float blast = areaRadius * GameLoop.cellSize;
        Color scorch = new(1f, 0.35f, 0.05f);

        ImpactCore.Spawn(explosionPosition, AbilityJuice.Hot(scorch, 2f), blast * 0.5f, 1.6f);
        ImpactShockwave.Spawn(explosionPosition, scorch, blast, 0.7f);

        // Thrown a wider fraction of the blast than a grenade's debris: this explosion also cuts
        // chunks out of any wall it reaches, so pieces travelling further read as rubble from that.
        DebrisBurst.Spawn(explosionPosition, scorch, blast * 0.65f, 32);
        Aftermath.Spawn(explosionPosition, scorch, blast * 0.6f, AftermathKind.Scorch);
    }

    private void DetonateRocket(Vector3 explosionPosition, float AreaRadius)
    {
        string enemyTeam = GameLoop.GetEnemyTeam(gameObject.tag);

        // Units move by Transform during execution. Sync before the overlap query so a completed
        // dodge is evaluated at its current position even when no physics tick ran this frame.
        Physics.SyncTransforms();

        Collider[] enemiesInRange = Physics.OverlapSphere(
            explosionPosition,
            AreaRadius * GameLoop.cellSize,
            LayerMask.GetMask(enemyTeam)
        );

        foreach (Collider enemy in enemiesInRange)
        {
            Vector3 directionToEnemy = (enemy.transform.position - explosionPosition).normalized;
            float distanceToEnemy = Vector3.Distance(explosionPosition, enemy.transform.position);
            if (
                Physics.Raycast(
                    explosionPosition,
                    directionToEnemy,
                    out RaycastHit hit,
                    distanceToEnemy,
                    LayerMask.GetMask("Walls", enemyTeam)
                )
            )
            {
                if (hit.collider != enemy)
                    continue;
            }

            enemy.transform.GetComponent<Health>()?.TakeDamage(damage);
        }
    }

    /// <summary>
    /// Permanently clears every wall cell within <paramref name="areaRadius"/> cells of
    /// <paramref name="explosionPosition"/>. Filters against <see cref="GameLoop.wallLayout"/> first
    /// so this only issues an RPC for a cell that is actually a wall.
    /// </summary>
    private void DestroyWallsInBlast(Vector3 explosionPosition, float areaRadius)
    {
        Vector2Int impactCell = GridSystem.ConvertToGridCoords(explosionPosition);
        foreach (
            Vector2Int cell in GetWallCellsWithinRadius(
                impactCell,
                areaRadius,
                GameLoop.wallLayout
            )
        )
        {
            GameLoop.Instance.TryDestroyWallCell(cell);
        }
    }

    /// <summary>
    /// Every cell in <paramref name="wallCells"/> within <paramref name="radiusCells"/> of
    /// <paramref name="center"/>, by a circular (not square) test — <c>dx*dx + dy*dy &lt;=
    /// radius*radius</c> — so a non-integer radius reads as a blast circle rather than a blocky
    /// square footprint. Pure grid math taking the wall set as a parameter rather than reading
    /// <see cref="GameLoop.wallLayout"/> itself, kept static so it can be exercised without a scene,
    /// the same way DashRush.GetSweptCells is.
    /// </summary>
    public static List<Vector2Int> GetWallCellsWithinRadius(
        Vector2Int center,
        float radiusCells,
        IEnumerable<Vector2Int> wallCells
    )
    {
        List<Vector2Int> hits = new();
        if (wallCells == null || radiusCells < 0f)
            return hits;

        float radiusSquared = radiusCells * radiusCells;
        int scanExtent = Mathf.CeilToInt(radiusCells);

        foreach (Vector2Int cell in wallCells)
        {
            int dx = cell.x - center.x;
            int dy = cell.y - center.y;
            if (Mathf.Abs(dx) > scanExtent || Mathf.Abs(dy) > scanExtent)
                continue;
            if (dx * dx + dy * dy <= radiusSquared)
                hits.Add(cell);
        }
        return hits;
    }
}
