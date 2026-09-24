using UnityEngine;

/// <summary>
/// Holds this transform at a fixed world facing, and optionally at a fixed world-space offset from
/// its parent, re-applied after everything else has moved for the frame. Lives on the object being
/// pinned rather than on whatever set it up, so it keeps running on peers where the controlling
/// component is disabled — an <see cref="Ability"/> disables itself everywhere except the server.
///
/// The position half exists because pinning rotation alone is not enough to hold something still
/// relative to the board: a child's local position is still rotated by its parent, so a slab parented
/// to a unit that turns to track targets keeps its facing but swings bodily around the unit. Anything
/// that has to stay put in world space while its parent turns needs both pinned.
/// </summary>
[DisallowMultipleComponent]
public class FixedWorldFacing : MonoBehaviour
{
    public Vector3 worldForward = Vector3.forward;

    /// <summary>
    /// When true, <see cref="LateUpdate"/> also holds this transform at
    /// <c>parent.position + worldOffset</c> in world space. Left off by default so a caller that
    /// only wants the facing locked keeps the parent's own placement.
    /// </summary>
    public bool pinsWorldOffset;

    /// <summary>World-space displacement from the parent, honoured when <see cref="pinsWorldOffset"/>
    /// is set. Already rotated into world space by whoever assigned it.</summary>
    public Vector3 worldOffset;

    private void LateUpdate()
    {
        if (pinsWorldOffset && transform.parent != null)
            transform.position = transform.parent.position + worldOffset;

        Vector3 flattened = new(worldForward.x, 0f, worldForward.z);
        if (flattened.sqrMagnitude < 0.0001f)
            return;
        transform.rotation = Quaternion.LookRotation(flattened.normalized, Vector3.up);
    }
}
