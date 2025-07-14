using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Movement : MonoBehaviour
{
    private float cellSize;
    [SerializeField] private const float MOVE_SPEED = 5f;

    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }

    public IEnumerator MoveToCells(List<Vector3> cells) //IEnumerator allows for this function to run over multiple frames
    {
        foreach (Vector3 cell in cells)
        {
            yield return StartCoroutine(MoveToCell(cell)); //yield return pauses the coroutine until MoveToCell is done
        }
    }

    private IEnumerator MoveToCell(Vector3 cell)
    {
        Vector3 targetPosition = new Vector3(cell.x, GetComponent<Renderer>().bounds.size.y / 2, cell.z);

        while (Vector3.Distance(transform.position, targetPosition) > 0.01f) //not 0 because of floating point precision or because movetowards only moves by fixed amount
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, MOVE_SPEED * Time.deltaTime);
            yield return null; //go to next frame
        }

        transform.position = targetPosition; // Snap to final position
    }

}
