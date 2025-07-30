using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Movement : MonoBehaviour
{
    public UnitData unitData;

    [HideInInspector] public bool moving = true;

    private Coroutine moveListRoutine;
    private Coroutine moveRoutine;
    private Coroutine rotateRoutine;


    public void StopMovement()
    {
        if (moveListRoutine != null) StopCoroutine(moveListRoutine);
        if (moveRoutine != null) StopCoroutine(moveRoutine);
        if (rotateRoutine != null) StopCoroutine(rotateRoutine);
    }

    public void StartMovement(List<Vector3> cells, bool dive = false)
    {
        StopMovement(); //stop any previous movement
        moveListRoutine = StartCoroutine(MoveToCells(cells, dive));
    }

    public void transitionToShooting()
    {
        if (rotateRoutine != null) StopCoroutine(rotateRoutine);
        transform.GetComponent<Shooting>().StartShooting();
        moving = false;
    }


    public IEnumerator MoveToCells(List<Vector3> cells, bool dive = false) //IEnumerator allows for this function to run over multiple frames
    {
        moving = true;
        foreach (Vector3 cell in cells)
        {
            yield return moveRoutine = StartCoroutine(MoveToCell(cell, dive)); //yield return pauses the coroutine until MoveToCell is done
        }

        transitionToShooting();
    }

    private IEnumerator MoveToCell(Vector3 cell, bool dive = false)
    {
        Vector3 targetPosition = cell + Helper.heightOffset(transform);

        if (rotateRoutine != null) StopCoroutine(rotateRoutine);
        rotateRoutine = StartCoroutine(RotateToFaceTarget(targetPosition, unitData.rotationSpeed)); //Rotate while moving

        while (Vector3.Distance(transform.position, targetPosition) > 0.01f) //not 0 because of floating point precision or because movetowards only moves by fixed amount
        {
            yield return null; //go to next frame. Before transform.position setting incase is being moved by something else
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, (dive ? unitData.diveSpeed : unitData.moveSpeed) * GameLoop.cellSize * Time.deltaTime);
        }

        transform.position = targetPosition; // Snap to final position
    }

    public IEnumerator RotateToFaceTarget(Vector3 targetPosition, float rotationSpeed)
    {
        Vector3 direction = targetPosition - transform.position;
        if (direction.magnitude < 0.01f) yield break; // Check if positions are close enough

        direction = direction.normalized; // Only normalize if we're proceeding
        Quaternion targetRotation = Quaternion.LookRotation(direction);
        while (Quaternion.Angle(transform.rotation, targetRotation) > 1f)
        {
            yield return null;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }

        transform.rotation = targetRotation; // Snap to final rotation
    }

    public Vector2Int ConvertToGridCoords(Vector3 position)
    {
        //Assuming grid's bottom left corner is at (0,0) and the grid is aligned with the world axes
        int x = Mathf.RoundToInt(position.x / GameLoop.cellSize);
        int z = Mathf.RoundToInt(position.z / GameLoop.cellSize);
        return new Vector2Int(x, z);
    }

}
