using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class NetworkHandler : NetworkBehaviour
{
    public static System.Action StartGame;

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            enabled = false;
            return;
        }
        base.OnNetworkSpawn();
        NetworkManager.OnClientConnectedCallback += OnClientConnected;
    }
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
         NetworkManager.OnClientConnectedCallback -= OnClientConnected;
    }

    void OnClientConnected(ulong clientId)
    {
        if(NetworkManager.Singleton.ConnectedClients.Count == 2)
        {
            StartGame?.Invoke();
        }
    }
}
