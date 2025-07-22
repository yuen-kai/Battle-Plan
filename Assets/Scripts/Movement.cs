using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Movement : MonoBehaviour
{
    public int moveDist;
    public float moveSpeed; //cells per second
    public float diveSpeed;
    public bool moving = true;
    float cellSize;
    private Coroutine moveListRoutine;
    private Coroutine moveRoutine;


    // Start is called before the first frame update
    void Start()
    {
        cellSize = GameLoop.cellSize;
    }


    public void StartMovement(List<Vector3> cells, bool dive = false)
    {
        if (moveListRoutine != null) StopCoroutine(moveListRoutine);
        if (moveRoutine != null) StopCoroutine(moveRoutine);
        moveRoutine = StartCoroutine(MoveToCells(cells, dive));
    }


    public IEnumerator MoveToCells(List<Vector3> cells, bool dive = false) //IEnumerator allows for this function to run over multiple frames
    {
        moving = true;
        foreach (Vector3 cell in cells)
        {
            yield return StartCoroutine(MoveToCell(cell, dive)); //yield return pauses the coroutine until MoveToCell is done
        }

        moveRoutine = StartCoroutine(transform.GetComponent<Shooting>().StartShooting());
        moving = false;
    }

    private IEnumerator MoveToCell(Vector3 cell, bool dive = false)
    {
        Vector3 targetPosition = new Vector3(cell.x, GetComponent<Collider>().bounds.size.y / 2, cell.z);

        while (Vector3.Distance(transform.position, targetPosition) > 0.01f) //not 0 because of floating point precision or because movetowards only moves by fixed amount
        {
            yield return null; //go to next frame. Needs to be before transform.position setting cause of some confusing bug
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, (dive ? diveSpeed : moveSpeed) * cellSize * Time.deltaTime);
        }

        transform.position = targetPosition; // Snap to final position
    }

}
