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

    public static List<GameObject> GetObjectsInRange(Vector3 position, string tag, float range)
    {
        List<GameObject> objectsInRange = new();
        GameObject[] allObjectsWithTag = GameObject.FindGameObjectsWithTag(tag);

        if (allObjectsWithTag.Length == 0)
        {
            Debug.LogWarning(
                $"[Helper] No units found with tag '{tag}' for range calculation"
            );
        }

        foreach (GameObject unit in allObjectsWithTag)
        {
            float distance = Vector3.Distance(position, unit.transform.position);
            if (distance <= range * GameLoop.cellSize)
            {
                objectsInRange.Add(unit);
            }
        }

        return objectsInRange;
    }
}

#if UNITY_EDITOR
// TEMPORARY: bootstraps the MCP for Unity HTTP server + bridge once, using the same
// public APIs the "Start Server" button calls. Safe to delete after the bridge connects.
[UnityEditor.InitializeOnLoad]
internal static class TempMcpBootstrap
{
    static TempMcpBootstrap()
    {
        UnityEditor.EditorApplication.delayCall += () => _ = Run();
    }

    private static async System.Threading.Tasks.Task Run()
    {
        try
        {
            if (MCPForUnity.Editor.Services.MCPServiceLocator.TransportManager.IsRunning(MCPForUnity.Editor.Services.Transport.TransportMode.Http))
            {
                Debug.Log("[TempMcpBootstrap] HTTP transport already running.");
                return;
            }

            if (!MCPForUnity.Editor.Services.MCPServiceLocator.Server.IsLocalHttpServerReachable())
            {
                bool started = MCPForUnity.Editor.Services.MCPServiceLocator.Server.StartLocalHttpServer(quiet: true);
                Debug.Log($"[TempMcpBootstrap] StartLocalHttpServer -> {started}");
                if (!started)
                {
                    MCPForUnity.Editor.Services.MCPServiceLocator.Server.LogLocalHttpServerLaunchFailure();
                    return;
                }
            }

            for (int i = 0; i < 40; i++)
            {
                if (MCPForUnity.Editor.Services.MCPServiceLocator.Server.IsLocalHttpServerReachable()) break;
                await System.Threading.Tasks.Task.Delay(500);
            }

            bool connected = await MCPForUnity.Editor.Services.MCPServiceLocator.Bridge.StartAsync();
            Debug.Log($"[TempMcpBootstrap] Bridge.StartAsync -> {connected}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[TempMcpBootstrap] Failed: {ex}");
        }
    }
}
#endif

