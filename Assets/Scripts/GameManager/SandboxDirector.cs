using UnityEngine;

/// <summary>
/// Runs the sandbox match. <see cref="GameLoop"/> attaches this at runtime when a
/// <see cref="SandboxSession"/> is active, and only ever on the loopback host, so it reads server
/// state and the local planner directly instead of going through RPCs — the same arrangement
/// <see cref="TutorialDirector"/> uses.
///
/// Three jobs, none of which the session's pure setup state can do for itself: keep the sandbox
/// panel attached to the live HUD, hold the HUD in its sandbox presentation, and own the board-edit
/// gesture that drags a unit from one square to another. Everything the panel's buttons actually do
/// to the board is a <see cref="GameLoop"/> call, because the board is the round loop's to rebuild.
/// </summary>
public class SandboxDirector : MonoBehaviour
{
    private GameObject draggedUnit;

#if UNITY_EDITOR
    private SandboxPanel panel;
#endif

    private bool hudPrepared;

    private void OnDisable()
    {
#if UNITY_EDITOR
        panel?.Dispose();
        panel = null;
#endif
        hudPrepared = false;
        SandboxSession.BoardEditActive = false;
    }

    private void Update()
    {
        if (!SandboxSession.IsActive)
            return;

        PrepareHud();
#if UNITY_EDITOR
        panel ??= SandboxPanel.TryCreate();
        panel?.Tick();
#endif
        UpdateBoardEditDrag();
    }

    /// <summary>
    /// The sandbox is paced by the designer, so the countdown is hidden the way the tutorial hides
    /// it, and the exit is offered from the dock because a sandbox match need never reach a result
    /// screen to be over.
    /// </summary>
    private void PrepareHud()
    {
        if (hudPrepared || GameHUDController.Instance == null)
            return;

        hudPrepared = true;
        GameHUDController.Instance.SuppressTimer(true);
        GameHUDController.Instance.ShowExitMatch(
            "Exit",
            () => GameLoop.Instance?.ExitToMainMenu()
        );
    }

    private void UpdateBoardEditDrag()
    {
        if (!SandboxSession.IsBoardEditLive)
        {
            draggedUnit = null;
            return;
        }

        if (Input.GetMouseButtonDown(0))
            draggedUnit = Mouse.GetUnitUnderMouse();

        if (Input.GetMouseButtonUp(0))
        {
            draggedUnit = null;
            return;
        }

        if (draggedUnit == null || !Input.GetMouseButton(0))
            return;

        // The unit follows the pointer square by square rather than jumping on release: a grid
        // board has nowhere in between to draw a ghost, so the unit itself is the preview.
        Vector3? hovered = Mouse.GetGridCellUnderMouse();
        if (hovered == null)
            return;

        GameLoop.Instance?.SandboxPlaceUnitAtCell(
            draggedUnit,
            GridSystem.ConvertToGridCoords(hovered.Value)
        );
    }
}
