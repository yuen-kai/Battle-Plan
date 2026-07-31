using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// Transport for solo matches: a host with no remote peers.
///
/// A match against the AI still needs a server, because every rule in
/// <see cref="GameLoop"/> runs behind <c>IsServer</c> and unit state replicates through
/// NetworkVariables. What it does not need is a socket. Netcode skips the host when it
/// fans out a ClientRpc and handles the host's own ServerRpcs inline, so a session with
/// no remote clients never queues a single byte for the wire.
///
/// UnityTransport opens a socket regardless, and a browser is not permitted to listen -
/// it throws "WebGL as a server is not supported" outside the Editor, which is what broke
/// solo play and the tutorial on web. This satisfies <see cref="StartServer"/> without
/// binding anything, so the same match path works on every platform.
/// </summary>
[DisallowMultipleComponent]
public sealed class OfflineTransport : NetworkTransport
{
    public override ulong ServerClientId => 0;

    /// <summary>Routes <paramref name="networkManager"/> through this transport for a solo match.</summary>
    public static void Configure(NetworkManager networkManager)
    {
        if (networkManager == null)
            throw new ArgumentNullException(nameof(networkManager));
        if (IsRunning(networkManager))
            throw new InvalidOperationException(
                "The transport cannot be changed while NGO is running."
            );

        if (!networkManager.TryGetComponent(out OfflineTransport transport))
            transport = networkManager.gameObject.AddComponent<OfflineTransport>();

        networkManager.NetworkConfig.NetworkTransport = transport;
    }

    /// <summary>Routes <paramref name="networkManager"/> back through UnityTransport for networked play.</summary>
    public static void Restore(NetworkManager networkManager)
    {
        if (networkManager == null || networkManager.NetworkConfig == null)
            return;
        if (!(networkManager.NetworkConfig.NetworkTransport is OfflineTransport))
            return;

        // Netcode caches the transport when it initialises, so swapping it mid-session would
        // leave the sender pointing at the old one. Cleanup paths call this unconditionally.
        if (IsRunning(networkManager))
            return;

        if (networkManager.TryGetComponent(out UnityTransport transport))
            networkManager.NetworkConfig.NetworkTransport = transport;
    }

    private static bool IsRunning(NetworkManager networkManager)
    {
        return networkManager.IsListening || networkManager.IsServer || networkManager.IsClient;
    }

    public override void Initialize(NetworkManager networkManager = null) { }

    public override bool StartServer() => true;

    public override bool StartClient()
    {
        Debug.LogError(
            "[OfflineTransport] A solo match has no host to connect to. "
                + "Restore UnityTransport before joining a networked match."
        );
        return false;
    }

    public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Reaching this means something addressed the host as though it were a remote peer,
        // and that message is now lost. Nothing in a solo match should get this far.
        Debug.LogWarning(
            $"[OfflineTransport] Dropped {payload.Count} bytes addressed to client {clientId}."
        );
#endif
    }

    public override NetworkEvent PollEvent(
        out ulong clientId,
        out ArraySegment<byte> payload,
        out float receiveTime
    )
    {
        clientId = ServerClientId;
        payload = default;
        receiveTime = Time.realtimeSinceStartup;
        return NetworkEvent.Nothing;
    }

    public override void DisconnectRemoteClient(ulong clientId) { }

    public override void DisconnectLocalClient() { }

    public override ulong GetCurrentRtt(ulong clientId) => 0;

    public override void Shutdown() { }
}
