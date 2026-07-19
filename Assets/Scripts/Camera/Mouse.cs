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
}
