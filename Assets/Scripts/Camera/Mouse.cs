using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mouse : MonoBehaviour
{
    private static bool cameraGestureActive;

    /// <summary>
    /// True while a multi-touch camera gesture owns the screen. Planning input stays disabled until
    /// every finger has lifted, so the last finger of a pinch cannot become a route drag or tap.
    /// </summary>
    public static bool CameraGestureActive => cameraGestureActive;

    /// <summary>The primary mouse or touch position in Unity's bottom-left screen coordinates.</summary>
    public static Vector2 PointerPosition =>
        Input.touchCount > 0 ? Input.GetTouch(0).position : (Vector2)Input.mousePosition;

    public static bool PrimaryPointerDown =>
        !cameraGestureActive
        && (
            Input.touchCount > 0
                ? Input.GetTouch(0).phase == TouchPhase.Began
                : Input.GetMouseButtonDown(0)
        );

    public static bool PrimaryPointerHeld =>
        !cameraGestureActive
        && (
            Input.touchCount > 0
                ? Input.GetTouch(0).phase is TouchPhase.Began
                    or TouchPhase.Moved
                    or TouchPhase.Stationary
                : Input.GetMouseButton(0)
        );

    public static bool PrimaryPointerUp =>
        !cameraGestureActive
        && (
            Input.touchCount > 0
                ? Input.GetTouch(0).phase is TouchPhase.Ended or TouchPhase.Canceled
                : Input.GetMouseButtonUp(0)
        );

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        cameraGestureActive = false;
    }

    public static void SetCameraGestureActive(bool active)
    {
        cameraGestureActive = active;
    }

    public static RaycastHit? GetHitUnderMouse(int layerMask)
    {
        if (cameraGestureActive || GameHUDController.IsPointerOverUI(PointerPosition))
            return null;

        Camera teamCamera = GameLoop.Instance?.TeamCamera;
        if (teamCamera == null)
            return null;

        Ray ray = teamCamera.ScreenPointToRay(PointerPosition);
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
    /// <remarks>
    /// Own crew only. A press on a team-mate's body outranks the square it stands on while an
    /// ability is being aimed; the opposing crew keeps answering that press with its cell, since
    /// aiming at it is the point. The sandbox, where the designer commands both crews, uses
    /// <see cref="GetUnitUnderMouse"/> instead so an enemy body is the same kind of pick.
    /// </remarks>
    public static GameObject GetFriendlyUnitUnderMouse()
    {
        string teamLayer = GameLoop.Instance != null
            ? GameLoop.GetTeamName(GameLoop.Instance.LocalTeamIndex)
            : null;
        if (string.IsNullOrEmpty(teamLayer))
            return null;

        return PickUnit(teamLayer);
    }

    /// <summary>
    /// The unit of either crew whose body the pointer is over. The sandbox uses this so a press on
    /// an enemy asks for that enemy the way a press on an ally asks for that ally, while the ground
    /// of their square stays a square.
    /// </summary>
    public static GameObject GetUnitUnderMouse()
    {
        string[] layers = new string[GameLoop.TeamCount];
        for (int teamIndex = 0; teamIndex < layers.Length; teamIndex++)
            layers[teamIndex] = GameLoop.GetTeamName(teamIndex);
        return PickUnit(layers);
    }

    private static GameObject PickUnit(params string[] teamLayers)
    {
        // Walls are in the mask but never accepted, so cover a unit is standing behind stops the
        // pick rather than being clicked through.
        string[] mask = new string[teamLayers.Length + 1];
        System.Array.Copy(teamLayers, mask, teamLayers.Length);
        mask[^1] = "Walls";

        RaycastHit? hit = GetHitUnderMouse(LayerMask.GetMask(mask));
        Unit unit = hit?.collider.GetComponentInParent<Unit>();
        return unit != null ? unit.gameObject : null;
    }
}
