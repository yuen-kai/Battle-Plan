using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Helper : MonoBehaviour
{
    public static Vector3 heightOffset(Transform transform)
    {
        if (transform.TryGetComponent<Collider>(out Collider collider))
        {
            return new Vector3(0, collider.bounds.size.y / 2, 0);
        }
        if (transform.TryGetComponent<Renderer>(out Renderer renderer))
        {
            Debug.LogWarning(
                $"[Helper] No Collider found on {transform.name}, falling back to Renderer bounds for height offset"
            );
            return new Vector3(0, renderer.bounds.size.y / 2, 0);
        }
        Debug.LogWarning(
            $"[Helper] No Collider or Renderer found on {transform.name}, falling back to zero height offset"
        );
        return Vector3.zero;
    }



    /// <summary>
    /// Helper to create a GameObject with a given name and parent.
    /// </summary>
    public static GameObject CreateGameObject(string name, GameObject parent)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent != null ? parent.transform : null);
        return obj;
    }
}
