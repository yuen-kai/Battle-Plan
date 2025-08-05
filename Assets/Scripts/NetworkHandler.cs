using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class NetworkHandler : NetworkBehaviour
{
    public static System.Action StartGame;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (NetworkManager.Singleton.IsServer)
        {
            NetworkManager.OnClientConnectedCallback += OnClientConnected;
        }
    }
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (NetworkManager.Singleton.IsServer)
        {
            NetworkManager.OnClientConnectedCallback -= OnClientConnected;
        }
    }

    void OnClientConnected(ulong clientId)
    {
        Debug.Log($"Client connected: {clientId}");
        if (NetworkManager.Singleton.IsServer && NetworkManager.ConnectedClients.Count == 1)
        {
            StartGame?.Invoke();
        }
    }
}
