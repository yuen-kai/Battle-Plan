using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Movement : MonoBehaviour
{
    public float cellSize;

    // Start is called before the first frame update
    void Start()
    {
        cellSize = GridSystem.cellSize;
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetMouseButtonDown(0)) // Check for left mouse button click
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition); // Create a ray from the camera to the mouse position
            RaycastHit hit; // Variable to store raycast hit information

            if (Physics.Raycast(ray, out hit))
            {
                Vector3 worldPosition = hit.point;
                Vector3 targetPosition = GetNearestGridCell(worldPosition);
                transform.position = targetPosition;
            }
        }
    }

    public Vector3 GetNearestGridCell(Vector3 position)
    {
        float x = Mathf.Round(position.x / cellSize) * cellSize;
        float z = Mathf.Round(position.z / cellSize) * cellSize;
        return new Vector3(x, transform.GetComponent<Renderer>().bounds.size.y/2, z);
    }
}
