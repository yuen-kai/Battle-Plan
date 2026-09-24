using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class ArcSurge : Ability
{
    private const int MaxTargets = 99;

    public const float StunSeconds = 1.5f;

    [SerializeField]
    private float damage = 70f;

    private const float ChargeSeconds = .5f;

    private const float RecoverySeconds = 1f;

    // Every arc is raycast out of the caster's own hands, so a unit inside the radius but behind a
    // wall is not a target at all.
    public override AbilityLineOfFire LineOfFire => AbilityLineOfFire.Required;

    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 3f)
    {
        if (!IsServer)
            yield break;

        BeginInterruptibleAbilityAction();
        ShowChargeClientRpc(NetworkObjectId, transform.position, HandOrigin(), AreaRadius);

        yield return new WaitForSeconds(ChargeSeconds);

        List<GameObject> targets = ResolveTargets(
            transform.position,
            AreaRadius,
            out List<BlockedStrike> blocked
        );
        ApplyZap(targets);
        foreach (BlockedStrike strike in blocked)
            GameLoop.Instance?.ReportShieldBlockedDamage(strike.ShieldOwner, strike.Point);

        ShowZapClientRpc(
            transform.position,
            HandOrigin(),
            StrikePoints(targets, blocked),
            AreaRadius
        );

        yield return new WaitForSeconds(RecoverySeconds);
        CompleteInterruptibleAbilityAction();
    }

    public readonly struct BlockedStrike
    {
        public readonly Vector3 Point;
        public readonly GameObject ShieldOwner;

        public BlockedStrike(Vector3 point, GameObject shieldOwner)
        {
            Point = point;
            ShieldOwner = shieldOwner;
        }
    }

    public List<GameObject> ResolveTargets(Vector3 center, float radiusCells)
    {
        return ResolveTargets(center, radiusCells, out _);
    }

    public List<GameObject> ResolveTargets(
        Vector3 center,
        float radiusCells,
        out List<BlockedStrike> blocked
    )
    {
        blocked = new List<BlockedStrike>();
        string enemyTeam = GameLoop.GetEnemyTeam(gameObject.tag);
        float radius = radiusCells * GameLoop.cellSize;

        Physics.SyncTransforms();

        Collider[] candidates = Physics.OverlapSphere(
            center,
            radius + GameLoop.cellSize,
            LayerMask.GetMask(enemyTeam)
        );

        List<GameObject> reachable = new();
        HashSet<GameObject> resolvedUnits = new();
        foreach (Collider candidate in candidates)
        {
            Unit identity = candidate.GetComponentInParent<Unit>();
            GameObject target = identity != null ? identity.gameObject : candidate.gameObject;
            if (!resolvedUnits.Add(target))
                continue;

            float radiusWithTolerance = radius + GameLoop.cellSize * 0.01f;
            if (
                HorizontalDistanceSquared(center, target.transform.position)
                > radiusWithTolerance * radiusWithTolerance
            )
                continue;

            Vector3 targetPoint = candidate.bounds.center;
            Vector3 directionToCandidate = (targetPoint - center).normalized;
            float distanceToCandidate = Vector3.Distance(center, targetPoint);
            if (
                Physics.Raycast(
                    center,
                    directionToCandidate,
                    out RaycastHit blocker,
                    distanceToCandidate,
                    LayerMask.GetMask("Walls") | ShieldRush.GetEnemyShieldMask(gameObject)
                )
            )
            {
                if (ShieldRush.TryGetShieldOwner(blocker.collider, out GameObject shieldOwner))
                    blocked.Add(new BlockedStrike(blocker.point, shieldOwner));
                continue;
            }

            reachable.Add(target);
        }

        reachable.Sort(
            (a, b) =>
                HorizontalDistanceSquared(center, a.transform.position)
                    .CompareTo(HorizontalDistanceSquared(center, b.transform.position))
        );

        if (reachable.Count > MaxTargets)
            reachable.RemoveRange(MaxTargets, reachable.Count - MaxTargets);

        return reachable;
    }

    private static float HorizontalDistanceSquared(Vector3 from, Vector3 to)
    {
        float x = to.x - from.x;
        float z = to.z - from.z;
        return x * x + z * z;
    }

    public void ApplyZap(List<GameObject> targets)
    {
        if (targets == null)
            return;

        foreach (GameObject target in targets)
        {
            if (target == null)
                continue;

            target.GetComponent<Health>()?.TakeDamage(damage);
            target.GetComponent<Unit>()?.ApplyStun(StunSeconds);
        }
    }

    private static readonly string[] HandAnchorPaths =
    {
        "Body/Anchors/LeftHand",
        "Body/Anchors/RightHand",
    };

    private const float HandHeightFallback = 1.02f;

    private const float StrikeHeightFallback = 0.95f;

    private const float BoltIntensity = 2.2f;

    // A single arc left standing for the length of the surge reads as a decal stuck to the board.
    // The discharge is held open by restriking it instead: each burst redraws every arc with fresh
    // geometry, so the fan is never the same shape two frames running.
    private const float BoltLifetime = 0.34f;
    private const int SurgeBursts = 5;
    private const float BurstSeconds = 0.16f;

    // Nearest target first, a beat apart, so the fan snaps outward instead of appearing whole.
    private const float ArcStaggerSeconds = 0.04f;

    private const float DischargeIntensity = 1.1f;

    private const float CoreShare = 0.3f;
    private const float CorePunch = 0.6f;

    private const float ShockwaveSeconds = 0.5f;

    private const float CameraPunch = 1.15f;

    [ClientRpc]
    private void ShowChargeClientRpc(
        ulong casterId,
        Vector3 casterPosition,
        Vector3 focus,
        float areaRadius
    )
    {
        ArcSurgeVFX.SpawnCharge(
            casterId,
            casterPosition,
            focus,
            BoltColor(),
            ChargeSeconds,
            areaRadius * GameLoop.cellSize
        );
    }

    [ClientRpc]
    private void CancelChargeClientRpc(ulong casterId)
    {
        ArcSurgeVFX.CancelCharge(casterId);
    }

    [ClientRpc]
    private void ShowZapClientRpc(
        Vector3 casterPosition,
        Vector3 origin,
        Vector3[] strikePoints,
        float areaRadius
    )
    {
        Color bolt = BoltColor();
        float blast = areaRadius * GameLoop.cellSize;

        ArcSurgeVFX.SpawnDischarge(casterPosition, bolt, blast, DischargeIntensity);

        // No ground dust: a discharge displaces nothing, and the dirt read as a grenade's leftovers.
        ImpactShockwave.Spawn(
            casterPosition,
            LightningBolt.ElectricBlue,
            blast,
            ShockwaveSeconds,
            withLightPop: true,
            groundDust: 0f
        );
        ImpactCore.Spawn(origin, bolt, GameLoop.cellSize * CoreShare, CorePunch);
        ImpactCamera.Punch(casterPosition, CameraPunch);

        ArcSurgeVFX.SpawnArcs(
            origin,
            strikePoints,
            bolt,
            BoltLifetime,
            ArcStaggerSeconds,
            SurgeBursts,
            BurstSeconds,
            StunSeconds
        );
    }

    private static Color BoltColor()
    {
        return AbilityJuice.Hot(LightningBolt.ElectricBlue, BoltIntensity);
    }

    protected override void OnAbilityInterrupted()
    {
        if (IsServer && IsSpawned)
            CancelChargeClientRpc(NetworkObjectId);
    }

    private Vector3 HandOrigin()
    {
        foreach (string anchorPath in HandAnchorPaths)
        {
            Transform hand = transform.Find(anchorPath);
            if (hand != null)
                return hand.position;
        }
        return transform.position + Vector3.up * HandHeightFallback;
    }

    private static Vector3[] StrikePoints(
        List<GameObject> targets,
        List<BlockedStrike> blocked = null
    )
    {
        if (targets == null && blocked == null)
            return System.Array.Empty<Vector3>();

        List<Vector3> points = new((targets?.Count ?? 0) + (blocked?.Count ?? 0));
        foreach (GameObject target in targets ?? new List<GameObject>())
        {
            if (target == null)
                continue;

            points.Add(
                target.TryGetComponent(out Collider body)
                    ? body.bounds.center
                    : target.transform.position + Vector3.up * StrikeHeightFallback
            );
        }

        if (blocked != null)
        {
            foreach (BlockedStrike strike in blocked)
                points.Add(strike.Point);
        }
        return points.ToArray();
    }
}
