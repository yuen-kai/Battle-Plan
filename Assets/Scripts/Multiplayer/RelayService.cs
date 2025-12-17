using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

public class RelayManager : MonoBehaviour
{
    public static RelayManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
    }

    public async Task<string> StartHost(int maxConnections)
    {
        await UnityServices.InitializeAsync();
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
        var allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
        NetworkManager
            .Singleton.GetComponent<UnityTransport>()
            .SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "wss"));
        NetworkManager.Singleton.GetComponent<UnityTransport>().UseWebSockets = true;
        var joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
        return NetworkManager.Singleton.StartHost() ? joinCode : null;
    }

    public async Task<bool> JoinClient(string joinCode)
    {
        await UnityServices.InitializeAsync();
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        var allocation = await RelayService.Instance.JoinAllocationAsync(joinCode: joinCode);
        NetworkManager
            .Singleton.GetComponent<UnityTransport>()
            .SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "wss"));
        NetworkManager.Singleton.GetComponent<UnityTransport>().UseWebSockets = true;
        return !string.IsNullOrEmpty(joinCode) && NetworkManager.Singleton.StartClient();
    }
}
