using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Movement system on a grid, handling movement and rotation. Transitions to shooting mode after movement.
/// </summary>
public class Movement : NetworkBehaviour
{
    public UnitData unitData;

    [HideInInspector] public bool moving = true;

    private Coroutine moveListRoutine;
    private Coroutine moveRoutine;
    private Coroutine rotateRoutine;

    private AnimationHandler animator;

    // CONTROLLER
    void Start()
    {
        if (!IsServer)
        {
            enabled = false;
            return;
        }
        animator = GetComponent<AnimationHandler>();
    }

    public void PauseMovement()
    {
        if (moveListRoutine != null) StopCoroutine(moveListRoutine);
        if (moveRoutine != null) StopCoroutine(moveRoutine);
        if (rotateRoutine != null) StopCoroutine(rotateRoutine);
    }

    public void StartMovement(List<Vector3> cells, bool dive = false)
    {
        PauseMovement();
        moveListRoutine = StartCoroutine(MoveToCells(cells, dive));
    }

    public void transitionToShooting()
    {
        if (animator != null)
        {
            animator.PlayAnimationExclusive("Idle");
        }

        PauseMovement();
        moving = false;

        transform.GetComponent<Shooting>().StartShooting();
    }


    // MOVEMENT
    public IEnumerator MoveToCells(List<Vector3> cells, bool dive = false)
    {
        if (animator != null)
        {
            animator.PlayAnimationExclusive("Moving");
        }

        moving = true;
        foreach (Vector3 cell in cells)
        {
            yield return moveRoutine = StartCoroutine(MoveToCell(cell, dive));
        }

        transitionToShooting();
    }

    private IEnumerator MoveToCell(Vector3 cell, bool dive = false)
    {
        Vector3 targetPosition = cell + Helper.heightOffset(transform);

        if (rotateRoutine != null) StopCoroutine(rotateRoutine);
        rotateRoutine = StartCoroutine(RotateToFaceTarget(targetPosition, unitData.rotationSpeed));

        while (Vector3.Distance(transform.position, targetPosition) > 0.01f)
        {
            yield return null;
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, (dive ? unitData.diveSpeed : unitData.moveSpeed) * GameLoop.cellSize * Time.deltaTime);
        }

        transform.position = targetPosition;
    }

    public IEnumerator RotateToFaceTarget(Vector3 targetPosition, float rotationSpeed)
    {
        Vector3 direction = targetPosition - transform.position;
        if (direction.magnitude < 0.01f) yield break;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);
        while (Quaternion.Angle(transform.rotation, targetRotation) > 1f)
        {
            yield return null;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }

        transform.rotation = targetRotation;
    }

    // HELPER
    //public Vector2Int ConvertToGridCoords(Vector3 position)
    //{
    //    int x = Mathf.RoundToInt(position.x / GameLoop.cellSize);
    //    int z = Mathf.RoundToInt(position.z / GameLoop.cellSize);
    //    return new Vector2Int(x, z);
    //}
}
