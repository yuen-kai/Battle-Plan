using UnityEngine;
using Unity.Netcode;  
using System.Collections;

public class NetworkHelper: MonoBehaviour
{
    public static GameObject SpawnNetworked(GameObject prefab, Vector3 position, Quaternion rotation, ulong? ownerClientId = null)
    {
        GameObject instance = Object.Instantiate(prefab, position, rotation);

        if (NetworkManager.Singleton.IsServer)
        {
            NetworkObject netObj = instance.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                if (ownerClientId.HasValue)
                    netObj.SpawnWithOwnership(ownerClientId.Value);
                else
                    netObj.Spawn();
            }
        }

        return instance;
    }

    public static void DespawnNetworked(GameObject instance, float delay = 0)
    {
        if (instance == null) return;
        NetworkObject netObj = instance.GetComponent<NetworkObject>();
        if (netObj != null && netObj.IsSpawned && NetworkManager.Singleton.IsServer)
        {
            DespawnDelay(netObj, delay);
        }
    }

    public static IEnumerator DespawnDelay(NetworkObject netObj, float delay = 0)
    {
        yield return new WaitForSeconds(delay);
        netObj.Despawn();
    }
}
