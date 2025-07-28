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

    private Animator anim;


    // Start is called before the first frame update
    void Start()
    {
        cellSize = GameLoop.cellSize;
        //Getting the component for the all classes animators.
        anim = GetComponent<Animator>();
    }


    public void StopMovement()
    {
        if (moveListRoutine != null) StopCoroutine(moveListRoutine);
        if (moveRoutine != null) StopCoroutine(moveRoutine);
        //Added bool for Idle pogo stick animation.
        anim.SetBool("Moving", false);
    }

    public void StartMovement(List<Vector3> cells, bool dive = false)
    {
        StopMovement(); //stop any previous movement
        moveListRoutine = StartCoroutine(MoveToCells(cells, dive));
    }

    public void transitionToShooting()
    {
        transform.GetComponent<Shooting>().StartShooting();
        moving = false;
    }


    public IEnumerator MoveToCells(List<Vector3> cells, bool dive = false) //IEnumerator allows for this function to run over multiple frames
    {
        moving = true;
        //Added bool for Pogo stick bouncing animation.
        anim.SetBool("Moving", true);
        foreach (Vector3 cell in cells)
        {
            yield return moveRoutine = StartCoroutine(MoveToCell(cell, dive)); //yield return pauses the coroutine until MoveToCell is done
        }

        transitionToShooting();
    }

    private IEnumerator MoveToCell(Vector3 cell, bool dive = false)
    {
        Vector3 targetPosition = cell + new Vector3(0, GetComponent<Collider>().bounds.size.y / 2, 0);

        while (Vector3.Distance(transform.position, targetPosition) > 0.01f) //not 0 because of floating point precision or because movetowards only moves by fixed amount
        {
            yield return null; //go to next frame. Before transform.position setting incase is being moved by something else
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, (dive ? diveSpeed : moveSpeed) * cellSize * Time.deltaTime);
        }

        transform.position = targetPosition; // Snap to final position
    }

    public Vector2Int ConvertToGridCoords(Vector3 position)
    {
        //Assuming grid's bottom left corner is at (0,0) and the grid is aligned with the world axes
        int x = Mathf.RoundToInt(position.x / cellSize);
        int z = Mathf.RoundToInt(position.z / cellSize);
        return new Vector2Int(x, z);
    }

}
