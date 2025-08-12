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

public class RelayService : MonoBehaviour
{
    public static RelayService Instance { get; private set; }

    [Header("Relay Settings")]
    public int maxPlayers = 2;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private async void Start()
    {
        await InitializeUnityServices();
    }

    private async Task InitializeUnityServices()
    {
        try
        {
            await UnityServices.InitializeAsync();
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            Debug.Log("Unity Services initialized successfully");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to initialize Unity Services: {e.Message}");
        }
    }

    public async Task<string> CreateRelay()
    {
        try
        {
            // Create relay allocation
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers);

            // Get join code
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            // Create relay server data
            RelayServerData relayServerData = new RelayServerData(allocation, "dtls");

            // Set transport data
            NetworkManager
                .Singleton.GetComponent<UnityTransport>()
                .SetRelayServerData(relayServerData);

            // Start host
            NetworkManager.Singleton.StartHost();

            Debug.Log($"Relay created successfully. Join code: {joinCode}");
            return joinCode;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to create relay: {e.Message}");
            return null;
        }
    }

    public async Task<bool> JoinRelay(string joinCode)
    {
        try
        {
            // Join relay with code
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(
                joinCode
            );

            // Create relay server data
            RelayServerData relayServerData = new RelayServerData(joinAllocation, "dtls");

            // Set transport data
            NetworkManager
                .Singleton.GetComponent<UnityTransport>()
                .SetRelayServerData(relayServerData);

            // Start client
            NetworkManager.Singleton.StartClient();

            Debug.Log($"Joined relay successfully with code: {joinCode}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to join relay: {e.Message}");
            return false;
        }
    }
}
