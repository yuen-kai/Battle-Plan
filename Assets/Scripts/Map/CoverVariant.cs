using UnityEngine;

/// <summary>
/// Picks which cover silhouette a spawned wall shows. Purely cosmetic: it enables one of the
/// variant children and touches nothing else, so it can never disagree between clients.
///
/// The choice folds the cell about column 7 and row 4.5 first, because <c>GameLoop.wallLayout</c>
/// is mirror-symmetric on both axes and the camera flips per team. A variant picked from the raw
/// coordinate would give the two players visually different boards and destroy the shared
/// landmark vocabulary the layout is built on. The 18 walls fold into exactly five groups, so the
/// mapping below is a table rather than a hash.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class CoverVariant : MonoBehaviour
{
    [Tooltip("Variant roots in build order. Exactly one is enabled at spawn.")]
    public GameObject[] variants = new GameObject[0];

    [Tooltip("Variant index used for each folded wall group, in the order listed in the builder.")]
    public int forcedVariant = -1;

#if UNITY_EDITOR
    /// <summary>
    /// DEV: forces one silhouette on every wall, for capture. The container and stack variants
    /// carry deep grooves in a dark slate body; up close they are depot detail, but at the board
    /// scale a trailer shoots from they collapse into black slots that read as a broken texture.
    /// </summary>
    public static int devForcedVariant = -1;
#endif

    private void OnEnable() => Apply();

    private void OnValidate() => Apply();

    public void Apply()
    {
        if (variants == null || variants.Length == 0)
            return;
        // Nothing to choose while this is a prefab asset rather than a placed wall.
        if (!gameObject.scene.IsValid())
            return;

        int column = Mathf.RoundToInt(transform.position.x / GameLoop.cellSize);
        int row = Mathf.RoundToInt(transform.position.z / GameLoop.cellSize);
        int chosen = forcedVariant >= 0 ? forcedVariant : VariantForCell(column, row);
#if UNITY_EDITOR
        if (devForcedVariant >= 0)
            chosen = devForcedVariant;
#endif
        Quaternion facing = Quaternion.Euler(0f, YawForCell(column, row), 0f);

        for (int i = 0; i < variants.Length; i++)
        {
            if (variants[i] == null)
                continue;
            if (variants[i].activeSelf != (i == chosen))
                variants[i].SetActive(i == chosen);
            if (i == chosen)
                variants[i].transform.localRotation = facing;
        }
    }

    /// <summary>
    /// Maps a wall's world position to a variant index. Kept static and side-effect free so the
    /// arena builder can call it in edit mode to preview the same distribution.
    /// </summary>
    public static int VariantForWorldPosition(Vector3 world)
    {
        int column = Mathf.RoundToInt(world.x / GameLoop.cellSize);
        int row = Mathf.RoundToInt(world.z / GameLoop.cellSize);
        return VariantForCell(column, row);
    }

    /// <summary>
    /// Turns the detailed face of a block toward the middle of the board, in 90° steps. Only the
    /// visual child is rotated; the root's transform and its collider are never touched.
    /// Folding leaves the magnitudes unchanged, so this stays mirror-symmetric too.
    /// </summary>
    public static float YawForCell(int column, int row)
    {
        float toCentreX = (GridSystem.ColumnCount - 1) * 0.5f - column;
        float toCentreZ = (GridSystem.RowCount - 1) * 0.5f - row;
        return Mathf.Abs(toCentreX) > Mathf.Abs(toCentreZ) ? 90f : 0f;
    }

    public static int VariantForCell(int column, int row)
    {
        int foldedColumn = Mathf.Min(column, GridSystem.ColumnCount - 1 - column);
        int foldedRow = Mathf.Min(row, GridSystem.RowCount - 1 - row);

        // The five folded groups the 18-wall layout collapses into, each given a form that
        // matches the tactical job that group of cells does.
        if (foldedColumn == 7)
            return 3; // Central approach plug — the pillar is the board's primary landmark.
        if (foldedRow <= 1)
            return 1; // Deployment-rank shoulder — depot containers.
        if (foldedColumn == 5)
            return 2; // Hill shoulder — stacked blocks mark the contested corners.
        return 0; // Flank spine and edge closers — the quiet base block.
    }
}
