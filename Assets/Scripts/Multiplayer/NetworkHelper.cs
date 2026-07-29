using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NetworkHelper : NetworkBehaviour
{
    public static NetworkHelper Instance { get; private set; }

    public static ClientRpcParams ToClient(ulong clientId) =>
        new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } },
        };

    // Queue for height sync requests when instance isn't ready
    private static Queue<(GameObject obj, Vector3 position)> pendingHeightSyncs = new();

    // Queue for parenting operations when instance isn't ready
    private static Queue<(GameObject instance, Transform parent)> pendingParentingOperations =
        new();

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            enabled = false;
            return;
        }
        Instance = this;

        // Process any pending height syncs
        ProcessPendingHeightSyncs();

        // Process any pending parenting operations
        ProcessPendingParentingOperations();
    }

    private void ProcessPendingHeightSyncs()
    {
        while (pendingHeightSyncs.Count > 0)
        {
            var (obj, position) = pendingHeightSyncs.Dequeue();
            if (obj != null)
            {
                SyncHeightAdjustedPosition(obj, position);
            }
        }
    }

    private void ProcessPendingParentingOperations()
    {
        while (pendingParentingOperations.Count > 0)
        {
            var (instance, parent) = pendingParentingOperations.Dequeue();
            if (instance != null && parent != null)
            {
                StartCoroutine(SetParentWhenSpawned(instance, parent));
            }
        }
    }

    /// <summary>
    /// Static method to sync height-adjusted position, queues if instance isn't ready
    /// </summary>
    public static void SyncHeightAdjustedPositionStatic(
        GameObject obj,
        Vector3 heightAdjustedPosition
    )
    {
        if (Instance != null)
        {
            Instance.SyncHeightAdjustedPosition(obj, heightAdjustedPosition);
        }
        else
        {
            pendingHeightSyncs.Enqueue((obj, heightAdjustedPosition));
        }
    }

    public static GameObject Spawn(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        Transform parent = null,
        ulong? ownerClientId = null,
        Vector3? scale = null,
        NetworkObject.VisibilityDelegate visibility = null
    )
    {
        GameObject instance = Instantiate(prefab, position, rotation);
        if (scale != null)
        {
            instance.transform.localScale = scale.Value;
        }

        if (NetworkManager.Singleton.IsServer)
        {
            NetworkObject netObj = instance.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                Debug.LogWarning(
                    $"[NetworkHelper] NetworkObject component missing on {prefab.name}, adding it dynamically. This may cause network issues."
                );
                netObj = instance.AddComponent<NetworkObject>();
            }
            if (visibility != null)
                netObj.CheckObjectVisibility = visibility;

            if (ownerClientId.HasValue)
            {
                // NGO despawns a disconnected client's owned objects by default, which would delete
                // a dropped player's whole crew before the reconnect window could give it back.
                // Ownership reverts to the server instead and is handed back on rejoin.
                netObj.DontDestroyWithOwner = true;
                netObj.SpawnWithOwnership(ownerClientId.Value);
            }
            else
            {
                netObj.Spawn();
            }

            // Set parent after network spawn to ensure proper parenting across network
            if (parent != null)
            {
                if (Instance != null)
                {
                    Instance.StartCoroutine(Instance.SetParentWhenSpawned(instance, parent));
                }
                else
                {
                    // Queue the parenting operation if NetworkHelper instance isn't ready yet
                    pendingParentingOperations.Enqueue((instance, parent));
                }
            }
        }
        else if (parent != null)
        {
            // For non-server instances, set parent immediately
            instance.transform.SetParent(parent, false);
            Debug.LogWarning(
                "[NetworkHelper] Spawn run on non-server! This may cause network synchronization issues."
            );
        }

        return instance;
    }

    IEnumerator SetParentWhenSpawned(GameObject instance, Transform parent)
    {
        while (!parent.GetComponent<NetworkObject>().IsSpawned)
        {
            yield return null;
        }
        instance.transform.SetParent(parent, false);
    }

    //Overloaded versions of Spawn for convenience
    public static GameObject Spawn(GameObject prefab)
    {
        return Spawn(prefab, Vector3.zero, Quaternion.identity, null, null, null);
    }

    public static GameObject Spawn(GameObject prefab, Transform parent)
    {
        return Spawn(prefab, Vector3.zero, Quaternion.identity, parent, null, null);
    }

    public static GameObject Spawn(
        GameObject prefab,
        Transform parent,
        bool instantiateInWorldSpace
    )
    {
        if (instantiateInWorldSpace)
            return Spawn(prefab, Vector3.zero, Quaternion.identity, parent, null, null);
        else
            return Spawn(prefab, Vector3.zero, Quaternion.identity, parent, null, null);
    }

    public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        return Spawn(prefab, position, rotation, null, null, null);
    }

    public static GameObject Spawn(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        Vector3 scale
    )
    {
        return Spawn(prefab, position, rotation, null, null, scale);
    }

    public static GameObject Spawn(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        ulong ownerClientId
    )
    {
        return Spawn(prefab, position, rotation, null, ownerClientId, null);
    }

    public static GameObject Spawn(GameObject prefab, Transform parent, ulong ownerClientId)
    {
        return Spawn(prefab, Vector3.zero, Quaternion.identity, parent, ownerClientId, null);
    }

    public static GameObject Spawn(GameObject prefab, ulong ownerClientId)
    {
        return Spawn(prefab, Vector3.zero, Quaternion.identity, null, ownerClientId, null);
    }

    public static GameObject Spawn(GameObject prefab, Vector3 scale)
    {
        return Spawn(prefab, Vector3.zero, Quaternion.identity, null, null, scale);
    }

    public static GameObject Spawn(GameObject prefab, Transform parent, Vector3 scale)
    {
        return Spawn(prefab, Vector3.zero, Quaternion.identity, parent, null, scale);
    }

    public static GameObject Spawn(
        GameObject prefab,
        Transform parent,
        ulong ownerClientId,
        Vector3 scale
    )
    {
        return Spawn(prefab, Vector3.zero, Quaternion.identity, parent, ownerClientId, scale);
    }

    public void Despawn(GameObject instance, float delay = 0)
    {
        StartCoroutine(DespawnDelay(instance, delay));
    }

    public IEnumerator DespawnDelay(GameObject instance, float delay = 0)
    {
        if (delay > 0)
        {
            yield return new WaitForSeconds(delay);
        }

        if (instance == null)
            yield break;

        NetworkObject netObj = instance.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogWarning(instance.name + " has no net obj");
            Destroy(instance);
            yield break;
        }

        netObj.Despawn();
    }

    public void SetActive(GameObject obj, bool active)
    {
        SetActiveClientRpc(obj, "", active);
    }

    public void SetActive(GameObject obj, string childPath, bool active)
    {
        SetActiveClientRpc(obj, childPath, active);
    }

    /// <summary>
    /// Sets a child object active/inactive across the network using ClientRpc
    /// </summary>
    [ClientRpc]
    public void SetActiveClientRpc(
        NetworkObjectReference unitRef,
        string childPath,
        bool active,
        ClientRpcParams clientRpcParams = default
    )
    {
        if (unitRef.TryGet(out NetworkObject netObj))
        {
            var child = netObj.transform.Find(childPath);

            if (child != null)
            {
                child.gameObject.SetActive(active);
            }
            else
            {
                Debug.LogWarning(
                    $"[NetworkHelper] Could not find child at path '{childPath}' on {netObj.gameObject.name}"
                );
            }
        }
        else
        {
            Debug.LogWarning(
                "[NetworkHelper] Failed to resolve NetworkObjectReference for SetActiveOnChild"
            );
        }
    }

    /// <summary>
    /// Syncs the height-adjusted position of an object to all clients
    /// </summary>
    public void SyncHeightAdjustedPosition(GameObject obj, Vector3 heightAdjustedPosition)
    {
        if (!IsServer)
            return;

        var netObj = obj.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            SyncHeightAdjustedPositionClientRpc(netObj, heightAdjustedPosition);
        }
    }

    [ClientRpc]
    private void SyncHeightAdjustedPositionClientRpc(
        NetworkObjectReference objRef,
        Vector3 heightAdjustedPosition
    )
    {
        if (IsServer)
            return;
        if (objRef.TryGet(out NetworkObject networkObject))
        {
            networkObject.transform.position = heightAdjustedPosition;
        }
    }

    /// <summary>
    /// Despawns all NetworkObjects (except persistent objects like NetworkManager)
    /// Should be called from server-side before scene transitions
    /// </summary>
    public static void CleanupAllNetworkObjects()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning(
                "[NetworkHelper] Cannot cleanup - NetworkManager is null or not server"
            );
            return;
        }

        if (NetworkManager.Singleton.SpawnManager == null)
        {
            Debug.LogWarning("[NetworkHelper] SpawnManager is null, skipping cleanup");
            return;
        }

        // Create a list copy since we'll be modifying the collection during iteration
        var spawnedObjects = new List<NetworkObject>(
            NetworkManager.Singleton.SpawnManager.SpawnedObjectsList
        );

        // Despawn all NetworkObjects (except persistent objects like NetworkManager, GameLoop, and NetworkHelper)
        foreach (var netObj in spawnedObjects)
        {
            if (netObj != null && netObj.IsSpawned)
            {
                if (
                    netObj.gameObject.GetComponent<GameLoop>() == null
                    && netObj.gameObject.GetComponent<NetworkHelper>() == null
                )
                {
                    netObj.Despawn();
                }
            }
        }
    }
}
