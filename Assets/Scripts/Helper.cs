using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Helper : MonoBehaviour
{
    public static GameObject DisplayGridRange(Vector3 currentPos, int dist, GameObject overlayPrefab)
    {
        GameObject overlay = new GameObject("MoveOverlay");

        for (int i = -dist; i <= dist; i++)
        {
            int horizontalDist = dist - Mathf.Abs(i);
            for (int j = -horizontalDist; j <= horizontalDist; j++)
            {
                float cellSize = GameLoop.cellSize;
                Vector2 overlayCellPos = new Vector2(currentPos.x + j * cellSize, currentPos.z + i * cellSize);
                if (GameLoop.gridBounds.Contains(overlayCellPos))
                {
                    GameObject overlayCell = Instantiate(overlayPrefab, new Vector3(overlayCellPos.x, 0.2f, overlayCellPos.y), Quaternion.identity);
                    overlayCell.transform.parent = overlay.transform;
                }
            }
        }

        return overlay;
    }

    public static Vector3 heightOffset(Transform transform)
    {
        if (transform.TryGetComponent<Collider>(out Collider collider))
        {
            return new Vector3(0, collider.bounds.size.y / 2, 0);
        }
        if (transform.TryGetComponent<Renderer>(out Renderer renderer))
        {
            return new Vector3(0, renderer.bounds.size.y / 2, 0);
        }
        return Vector3.zero;
    }
}
