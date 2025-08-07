using UnityEngine;
using Unity.Netcode;  
using System.Collections;

public class NetworkHelper: NetworkBehaviour
{
    public static NetworkHelper Instance
    {
        get; private set;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            enabled = false; 
            return;
        }
        Instance = this;
    }


    public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null, ulong? ownerClientId = null, Vector3? scale = null)
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
                Debug.LogWarning($"NetworkObject component missing on {prefab.name}. Adding it dynamically.");
                netObj = instance.AddComponent<NetworkObject>();
            }
            
            if (ownerClientId.HasValue)
                netObj.SpawnWithOwnership(ownerClientId.Value);
            else
                netObj.Spawn();
                
            // Set parent after network spawn to ensure proper parenting across network
            if (parent != null)
            {
                instance.transform.SetParent(parent, false);
            }
        }
        else if (parent != null)
        {
            // For non-server instances, set parent immediately
            instance.transform.SetParent(parent, false);
            Debug.LogWarning("Spawn run on non-server!");
        }

        return instance;
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

    public static GameObject Spawn(GameObject prefab, Transform parent, bool instantiateInWorldSpace)
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

    public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        return Spawn(prefab, position, rotation, null, null, scale);
    }

    public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, ulong ownerClientId)
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

    public static GameObject Spawn(GameObject prefab, Transform parent, ulong ownerClientId, Vector3 scale)
    {
        return Spawn(prefab, Vector3.zero, Quaternion.identity, parent, ownerClientId, scale);
    }

    public void Despawn(GameObject instance, float delay = 0)
    {
        if (instance == null) return;
        NetworkObject netObj = instance.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogWarning(instance.name + " has no net obj");
            Destroy(instance);
        }
        if (netObj != null && netObj.IsSpawned && NetworkManager.Singleton.IsServer)
        {
            StartCoroutine(DespawnDelay(netObj, delay));
        }
    }

    public void SetActive(GameObject gameObject, bool active)
    {
        if (!IsServer) return;
        
        if (gameObject == null) return;
        
        NetworkObject netObj = gameObject.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogWarning($"GameObject {gameObject.name} has no NetworkObject component. Cannot sync SetActive across network.");
            gameObject.SetActive(active);
            return;
        }
        
        // Set active locally on server
        gameObject.SetActive(active);
        
        // Notify all clients
        SetActiveClientRpc(netObj.NetworkObjectId, active);
    }

    [ClientRpc]
    private void SetActiveClientRpc(ulong networkObjectId, bool active)
    {
        if (IsServer) return; // Server already handled it
        
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject netObj))
        {
            netObj.gameObject.SetActive(active);
        }
    }

    public IEnumerator DespawnDelay(NetworkObject netObj, float delay = 0)
    {
        yield return new WaitForSeconds(delay);
        netObj.Despawn();
    }
}
