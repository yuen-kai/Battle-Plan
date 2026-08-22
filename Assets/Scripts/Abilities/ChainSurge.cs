using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class ChainSurge : Ability
{
    private const int MaxTargets = 5;

    public const float StunSeconds = 0.3f;

    [SerializeField]
    private float damage = 45f;

    private const float ChargeSeconds = 0.7f;

    private const float RecoverySeconds = 1.1f;

    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 3f)
    {
        if (!IsServer)
            yield break;

        BeginInterruptibleAbilityAction();
        ShowChargeClientRpc(NetworkObjectId, transform.position);

        yield return new WaitForSeconds(ChargeSeconds);

        List<GameObject> targets = ResolveTargets(transform.position, AreaRadius);
        ApplyZap(targets);

        ShowZapClientRpc(HandOrigin(), StrikePoints(targets));

        yield return new WaitForSeconds(RecoverySeconds);
        CompleteInterruptibleAbilityAction();
    }

    public List<GameObject> ResolveTargets(Vector3 center, float radiusCells)
    {
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
                    distanceToCandidate,
                    LayerMask.GetMask("Walls")
                )
            )
                continue;

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

    private const float BoltIntensity = 2.4f;

    private const float BoltLifetime = 0.3f;
    private const float BoltLifetimeJitter = 0.04f;

    private const float DischargeScale = 1.2f;
    private const float DischargeIntensity = 1.1f;
    private const float StrikeScale = 0.9f;
    private const float StrikeIntensity = 0.7f;

    [ClientRpc]
    private void ShowChargeClientRpc(ulong casterId, Vector3 casterPosition)
    {
        Color bolt = AbilityJuice.Hot(LightningBolt.ElectricBlue, BoltIntensity);
        ChainSurgeVFX.SpawnCharge(casterId, casterPosition, bolt, ChargeSeconds);
    }

    [ClientRpc]
    private void CancelChargeClientRpc(ulong casterId)
    {
        ChainSurgeVFX.CancelCharge(casterId);
    }

    [ClientRpc]
    private void ShowZapClientRpc(Vector3 origin, Vector3[] strikePoints)
    {
        Color bolt = AbilityJuice.Hot(LightningBolt.ElectricBlue, BoltIntensity);
        ChainSurgeVFX.SpawnDischarge(origin, bolt, DischargeScale, DischargeIntensity);

        if (strikePoints == null)
            return;

        foreach (Vector3 strikePoint in strikePoints)
        {
            LightningBolt.Spawn(
                origin,
                strikePoint,
                bolt,
                BoltLifetime + Random.Range(-BoltLifetimeJitter, BoltLifetimeJitter)
            );
            ChainSurgeVFX.SpawnStrike(strikePoint, bolt, StrikeScale, StrikeIntensity);
        }
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

    private static Vector3[] StrikePoints(List<GameObject> targets)
    {
        if (targets == null)
            return System.Array.Empty<Vector3>();

        List<Vector3> points = new(targets.Count);
        foreach (GameObject target in targets)
        {
            if (target == null)
                continue;

            points.Add(
                target.TryGetComponent(out Collider body)
                    ? body.bounds.center
                    : target.transform.position + Vector3.up * StrikeHeightFallback
            );
        }
        return points.ToArray();
    }
}
