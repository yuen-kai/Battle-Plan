using UnityEngine;

/// <summary>
/// Client-side smoothing for the batched transform snapshots that replaced per-unit
/// <c>NetworkTransform</c>. Snapshots arrive at a fixed cadence rather than every frame, so each
/// unit eases toward the last authoritative pose over roughly one snapshot interval, which is the
/// same job NetworkTransform's interpolator was doing before.
///
/// Local-only and server-free: the host moves its units directly and never creates one of these.
/// </summary>
public sealed class UnitTransformInterpolator : MonoBehaviour
{
    /// <summary>
    /// A snapshot older than this is treated as a teleport rather than something to ease into, so a
    /// unit reappearing out of fog or rejoining a match snaps to where it actually is.
    /// </summary>
    private const float TeleportDistance = 3f;

    private Vector3 targetPosition;
    private float targetYaw;
    private float smoothingSeconds = 0.1f;
    private bool hasTarget;

    public static UnitTransformInterpolator For(GameObject unit)
    {
        UnitTransformInterpolator existing = unit.GetComponent<UnitTransformInterpolator>();
        return existing != null ? existing : unit.AddComponent<UnitTransformInterpolator>();
    }

    public void SetTarget(Vector3 position, float yaw, float intervalSeconds)
    {
        targetPosition = position;
        targetYaw = yaw;
        smoothingSeconds = Mathf.Max(0.01f, intervalSeconds);

        if (!hasTarget || Vector3.Distance(transform.position, position) > TeleportDistance)
        {
            transform.position = position;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        hasTarget = true;
    }

    private void Update()
    {
        if (!hasTarget)
            return;

        // Framerate-independent easing: the same fraction of the remaining gap is closed per unit of
        // time no matter how often Update runs.
        float t = 1f - Mathf.Exp(-Time.deltaTime / smoothingSeconds);
        transform.position = Vector3.Lerp(transform.position, targetPosition, t);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.Euler(0f, targetYaw, 0f),
            t
        );
    }
}
