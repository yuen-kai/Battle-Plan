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
}
