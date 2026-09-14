using UnityEngine;

/// <summary>
/// Local board-camera navigation. A mouse can wheel to zoom, drag with the middle or right button
/// to pan, and use WASD/arrows. Touch keeps one finger for the game's existing tap/route controls;
/// two fingers pan and pinch, claiming the gesture until both fingers have lifted.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(-200)]
public sealed class BoardCameraController : MonoBehaviour
{
    private const float BoardPlaneHeight = 0f;
    private const float MinCameraHeight = 10f;
    private const float MaxCameraHeight = 38f;
    private const float MouseZoomDistance = 2.8f;
    private const float TouchZoomDistance = 28f;
    private const float KeyboardPanSpeed = 18f;
    private const float PanBoundsPaddingInCells = 1f;

    private Camera boardCamera;
    private bool mousePanActive;
    private Vector2 previousMousePosition;
    private bool touchGestureClaimed;
    private bool touchCameraMotionAllowed;
    private bool singleTouchTracked;
    private bool releaseCameraGestureNextFrame;

    public static BoardCameraController Attach(Camera camera)
    {
        if (camera == null)
            return null;

        BoardCameraController controls = camera.GetComponent<BoardCameraController>();
        return controls != null ? controls : camera.gameObject.AddComponent<BoardCameraController>();
    }

    private void Awake()
    {
        boardCamera = GetComponent<Camera>();
    }

    private void OnDisable()
    {
        mousePanActive = false;
        touchGestureClaimed = false;
        touchCameraMotionAllowed = false;
        singleTouchTracked = false;
        releaseCameraGestureNextFrame = false;
        PlanMovement.Instance?.FinishPotentialCameraGesture();
        Mouse.SetCameraGestureActive(false);
    }

    private void Update()
    {
        if (boardCamera == null)
            return;

        if (releaseCameraGestureNextFrame)
        {
            releaseCameraGestureNextFrame = false;
            Mouse.SetCameraGestureActive(false);
        }

        UpdateTouchGesture();
        if (touchGestureClaimed || Input.touchCount > 0)
            return;

        UpdateMouseZoom();
        UpdateMousePan();
        UpdateKeyboardPan();
    }

    /// <summary>
    /// Called after the match assigns the local team's camera pose. It ends any gesture without
    /// changing that authored pose.
    /// </summary>
    public void ResetInputState()
    {
        mousePanActive = false;
        touchGestureClaimed = false;
        touchCameraMotionAllowed = false;
        singleTouchTracked = false;
        releaseCameraGestureNextFrame = false;
        PlanMovement.Instance?.FinishPotentialCameraGesture();
        Mouse.SetCameraGestureActive(false);
    }

    private void UpdateTouchGesture()
    {
        int touchCount = Input.touchCount;
        if (touchGestureClaimed)
        {
            Mouse.SetCameraGestureActive(true);
            if (touchCount == 0)
            {
                touchGestureClaimed = false;
                touchCameraMotionAllowed = false;
                // Hold the claim through this frame. Legacy touch-to-mouse simulation can report a
                // mouse-up as the last finger vanishes; releasing next frame keeps that synthetic
                // event from settling or submitting a gameplay drag.
                releaseCameraGestureNextFrame = true;
                return;
            }

            if (touchCount < 2)
                return;

            if (touchCameraMotionAllowed)
                ApplyTwoFingerGesture(Input.GetTouch(0), Input.GetTouch(1));
            return;
        }

        if (touchCount == 0)
        {
            if (singleTouchTracked)
                PlanMovement.Instance?.FinishPotentialCameraGesture();
            singleTouchTracked = false;
            return;
        }

        if (touchCount == 1)
        {
            Touch touch = Input.GetTouch(0);
            if (!singleTouchTracked && touch.phase == TouchPhase.Began)
            {
                singleTouchTracked = true;
                PlanMovement.Instance?.BeginPotentialCameraGesture();
            }

            if (touch.phase is TouchPhase.Ended or TouchPhase.Canceled)
            {
                PlanMovement.Instance?.FinishPotentialCameraGesture();
                singleTouchTracked = false;
            }
            return;
        }

        Touch first = Input.GetTouch(0);
        Touch second = Input.GetTouch(1);

        // Two touches always claim gameplay, even when one is over the HUD. Whether the camera is
        // allowed to move is a separate decision: gestures must begin fully on the board so a
        // two-finger card/scroll interaction cannot drag the battlefield behind the UI.
        if (!singleTouchTracked)
            PlanMovement.Instance?.BeginPotentialCameraGesture();
        PlanMovement.Instance?.RestorePotentialCameraGesture();
        singleTouchTracked = false;

        touchGestureClaimed = true;
        touchCameraMotionAllowed =
            !GameHUDController.IsPointerOverUI(first.position)
            && !GameHUDController.IsPointerOverUI(second.position);
        Mouse.SetCameraGestureActive(true);
        PathSelection.Instance?.CancelCurrentDrag();
        if (touchCameraMotionAllowed)
            ApplyTwoFingerGesture(first, second);
    }

    private void ApplyTwoFingerGesture(Touch first, Touch second)
    {
        Vector2 previousFirst = first.position - first.deltaPosition;
        Vector2 previousSecond = second.position - second.deltaPosition;
        Vector2 previousCentre = (previousFirst + previousSecond) * 0.5f;
        Vector2 currentCentre = (first.position + second.position) * 0.5f;

        PanBetweenScreenPoints(previousCentre, currentCentre);

        float previousSeparation = Vector2.Distance(previousFirst, previousSecond);
        float currentSeparation = Vector2.Distance(first.position, second.position);
        float shortestScreenSide = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height));
        float normalizedPinch = (currentSeparation - previousSeparation) / shortestScreenSide;
        ZoomAt(currentCentre, normalizedPinch * TouchZoomDistance);
    }

    private void UpdateMouseZoom()
    {
        float scroll = Input.mouseScrollDelta.y;
        if (
            Mathf.Approximately(scroll, 0f)
            || GameHUDController.IsPointerOverUI(Input.mousePosition)
        )
            return;

        ZoomAt(Input.mousePosition, scroll * MouseZoomDistance);
    }

    private void UpdateMousePan()
    {
        bool panPressed = Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
        bool panHeld = Input.GetMouseButton(1) || Input.GetMouseButton(2);

        if (panPressed && !GameHUDController.IsPointerOverUI(Input.mousePosition))
        {
            mousePanActive = true;
            previousMousePosition = Input.mousePosition;
        }

        if (!panHeld)
        {
            mousePanActive = false;
            return;
        }

        if (!mousePanActive)
            return;

        Vector2 current = Input.mousePosition;
        PanBetweenScreenPoints(previousMousePosition, current);
        previousMousePosition = current;
    }

    private void UpdateKeyboardPan()
    {
        Vector2 direction = new(
            (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f),
            (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f)
        );
        if (direction.sqrMagnitude <= 0f)
            return;

        direction.Normalize();
        Vector3 right = Vector3.ProjectOnPlane(boardCamera.transform.right, Vector3.up).normalized;
        Vector3 forward = Vector3.ProjectOnPlane(boardCamera.transform.forward, Vector3.up).normalized;
        float altitudeScale = Mathf.Clamp(
            boardCamera.transform.position.y / 24.4f,
            0.5f,
            1.75f
        );
        PanByWorldDelta(
            (right * direction.x + forward * direction.y)
                * (KeyboardPanSpeed * altitudeScale * Time.unscaledDeltaTime)
        );
    }

    private void PanBetweenScreenPoints(Vector2 previous, Vector2 current)
    {
        if (
            !TryGetBoardPoint(previous, out Vector3 previousPoint)
            || !TryGetBoardPoint(current, out Vector3 currentPoint)
        )
            return;

        PanByWorldDelta(previousPoint - currentPoint);
    }

    private void PanByWorldDelta(Vector3 delta)
    {
        delta.y = 0f;
        if (delta.sqrMagnitude <= 0.000001f)
            return;

        ImpactCamera.PrepareForExternalCameraMotion(boardCamera);
        boardCamera.transform.position += delta;
        ClampBoardFocus();
    }

    private void ZoomAt(Vector2 screenPosition, float requestedDolly)
    {
        if (Mathf.Abs(requestedDolly) <= 0.0001f)
            return;

        ImpactCamera.PrepareForExternalCameraMotion(boardCamera);
        if (!TryGetBoardPoint(screenPosition, out Vector3 before))
            return;

        Transform view = boardCamera.transform;
        float dolly = ClampedDollyDistance(
            view.position.y,
            view.forward.y,
            requestedDolly,
            MinCameraHeight,
            MaxCameraHeight
        );
        if (Mathf.Abs(dolly) <= 0.0001f)
            return;

        view.position += view.forward * dolly;
        if (TryGetBoardPoint(screenPosition, out Vector3 after))
            view.position += before - after;
        ClampBoardFocus();
    }

    private bool TryGetBoardPoint(Vector2 screenPosition, out Vector3 point)
    {
        Ray ray = boardCamera.ScreenPointToRay(screenPosition);
        Plane board = new(Vector3.up, new Vector3(0f, BoardPlaneHeight, 0f));
        if (board.Raycast(ray, out float distance))
        {
            point = ray.GetPoint(distance);
            return true;
        }

        point = default;
        return false;
    }

    private void ClampBoardFocus()
    {
        Transform view = boardCamera.transform;
        Ray centreRay = new(view.position, view.forward);
        Plane board = new(Vector3.up, new Vector3(0f, BoardPlaneHeight, 0f));
        if (!board.Raycast(centreRay, out float distance))
            return;

        Vector3 focus = centreRay.GetPoint(distance);
        Vector2 clamped = ClampFocusToBoard(
            new Vector2(focus.x, focus.z),
            GameLoop.gridBounds,
            GameLoop.cellSize * PanBoundsPaddingInCells
        );
        view.position += new Vector3(clamped.x - focus.x, 0f, clamped.y - focus.z);
    }

    public static float ClampedDollyDistance(
        float currentHeight,
        float forwardY,
        float requestedDistance,
        float minHeight,
        float maxHeight
    )
    {
        if (Mathf.Abs(forwardY) <= 0.0001f)
            return 0f;

        float desiredHeight = currentHeight + forwardY * requestedDistance;
        float clampedHeight = Mathf.Clamp(desiredHeight, minHeight, maxHeight);
        return (clampedHeight - currentHeight) / forwardY;
    }

    public static Vector2 ClampFocusToBoard(Vector2 focus, Rect boardBounds, float padding)
    {
        float safePadding = Mathf.Max(0f, padding);
        return new Vector2(
            Mathf.Clamp(focus.x, boardBounds.xMin - safePadding, boardBounds.xMax + safePadding),
            Mathf.Clamp(focus.y, boardBounds.yMin - safePadding, boardBounds.yMax + safePadding)
        );
    }
}
