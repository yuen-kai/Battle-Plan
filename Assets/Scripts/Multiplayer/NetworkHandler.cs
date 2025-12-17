using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        if (NetworkManager.Singleton.ConnectedClients.Count == 2)
        {
            if (GameLoop.TESTING)
            {
                List<int[]> unitAssignments = new()
                {
                    new int[] { 0, 1, 2 },
                    new int[] { 2, 3, 4 },
                };

                int index = 0;
                foreach (ulong id in NetworkManager.Singleton.ConnectedClients.Keys)
                {
                    GameLoop.allTeamUnits[id] = unitAssignments[index];
                    index++;
                }
                NetworkManager.Singleton.SceneManager.LoadScene("Game", LoadSceneMode.Single);
                return;
            }
            NetworkManager.Singleton.SceneManager.LoadScene("HomeScreen", LoadSceneMode.Single);
        }
    }
}
