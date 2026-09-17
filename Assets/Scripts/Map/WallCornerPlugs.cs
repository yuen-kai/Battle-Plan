using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Squares off the corners a wall shares with a diagonally adjacent wall.
///
/// The wall's convex collider is an octagonal prism with <see cref="ChamferWorld"/> trimmed from
/// each corner, so a bullet-sized sweep can peek diagonally past a single wall. Two of those
/// trims meeting at a point open a slot of twice the single-corner clearance, and the clearance
/// is sized to the projectile radius, so that slot always admits the projectile it was meant to
/// stop. No chamfer width satisfies both cases at once, so the trim is filled back in wherever
/// the diagonal neighbour supplies the other half of it.
/// </summary>
[DisallowMultipleComponent]
public class WallCornerPlugs : MonoBehaviour
{
    public const float ChamferWorld = 0.4f;

    public static readonly Vector2Int[] DiagonalOffsets =
    {
        new(1, 1),
        new(1, -1),
        new(-1, 1),
        new(-1, -1),
    };

    private readonly Dictionary<Vector2Int, GameObject> plugsByOffset = new();

    public Vector2Int Cell =>
        new(
            Mathf.RoundToInt(transform.position.x / GameLoop.cellSize),
            Mathf.RoundToInt(transform.position.z / GameLoop.cellSize)
        );

    private void OnEnable()
    {
        Apply();
        // A replicated wall and the map it reads can arrive in the same frame in either order,
        // so settle once more after everything for the frame has landed.
        if (gameObject.activeInHierarchy)
            StartCoroutine(ApplyNextFrame());
    }

    private IEnumerator ApplyNextFrame()
    {
        yield return null;
        Apply();
    }

    public void Apply()
    {
        if (!gameObject.scene.IsValid())
            return;

        HashSet<Vector2Int> walls = GameLoop.wallLayout;
        if (walls == null)
            return;

        Vector2Int cell = Cell;
        foreach (Vector2Int offset in DiagonalOffsets)
            SetPlug(offset, walls.Contains(cell + offset));
    }

    private void SetPlug(Vector2Int offset, bool wanted)
    {
        plugsByOffset.TryGetValue(offset, out GameObject plug);
        if (!wanted)
        {
            if (plug != null)
            {
                if (Application.isPlaying)
                    Destroy(plug);
                else
                    DestroyImmediate(plug);
            }
            plugsByOffset.Remove(offset);
            return;
        }

        if (plug != null)
            return;

        float chamfer = ChamferWorld / GameLoop.cellSize;
        plug = new GameObject($"CornerPlug_{offset.x}_{offset.y}") { layer = gameObject.layer };
        plug.transform.SetParent(transform, false);
        plug.transform.localPosition = new Vector3(
            offset.x * (0.5f - chamfer * 0.5f),
            0f,
            offset.y * (0.5f - chamfer * 0.5f)
        );
        plug.AddComponent<BoxCollider>().size = new Vector3(chamfer, 1f, chamfer);
        plugsByOffset[offset] = plug;
    }
}
