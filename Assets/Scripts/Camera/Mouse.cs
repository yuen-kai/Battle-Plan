using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mouse : MonoBehaviour
{
    public static RaycastHit? GetHitUnderMouse(int layerMask)
    {
        if (GameHUDController.IsPointerOverUI(Input.mousePosition))
            return null;

        Camera teamCamera = GameLoop.Instance?.TeamCamera;
        if (teamCamera == null)
            return null;

        Ray ray = teamCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, layerMask))
        {
            return hit;
        }
        return null;
    }

    public static GameObject GetObjectUnderMouse(int layerMask)
    {
        return GetHitUnderMouse(layerMask)?.collider.gameObject;
    }

    public static GameObject GetObjectUnderMouse(string layerMaskName)
    {
        return GetObjectUnderMouse(LayerMask.GetMask(layerMaskName));
    }

    public static Vector3? GetGridCellUnderMouse()
    {
        RaycastHit? hit = GetHitUnderMouse(LayerMask.GetMask("Grid"));
        return hit == null ? null : GridSystem.GetNearestGridCell(hit.Value.point);
    }

    /// <summary>
    /// The unsnapped board position under the pointer, for picks that need to tell apart several
    /// things drawn inside the same cell.
    /// </summary>
    public static Vector3? GetGridPointUnderMouse()
    {
        return GetHitUnderMouse(LayerMask.GetMask("Grid"))?.point;
    }

    /// <summary>
    /// The unit of your own team whose body the pointer is over, or null when the pointer is over
    /// the board instead. This is what tells a click on a unit apart from a click on the square it
    /// happens to be standing on: a unit is half a cell wide on a board of 2.7-wide cells, so its
    /// own square keeps plenty of clickable ground around it.
    /// </summary>
    public static GameObject GetFriendlyUnitUnderMouse()
    {
        string teamLayer = GameLoop.Instance != null
            ? GameLoop.GetTeamName(GameLoop.Instance.LocalTeamIndex)
            : null;
        if (string.IsNullOrEmpty(teamLayer))
            return null;

        // Walls are in the mask but never accepted, so cover a unit is standing behind stops the
        // pick rather than being clicked through.
        RaycastHit? hit = GetHitUnderMouse(LayerMask.GetMask(teamLayer, "Walls"));
        Unit unit = hit?.collider.GetComponentInParent<Unit>();
        return unit != null ? unit.gameObject : null;
    }
}
