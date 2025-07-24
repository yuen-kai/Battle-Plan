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
}
