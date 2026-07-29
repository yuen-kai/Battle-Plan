using System;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

public enum ReconnectTransportMode : byte
{
    None,
    Direct,
    Relay,
}

/// <summary>
/// The client half of reconnect grace: a stable identity the server can recognise across
/// connections, and enough of the route back to re-establish it.
/// <para>
/// The identity is a locally generated token persisted per player profile, sent as the NGO
/// connection-approval payload. It is deliberately not the UGS player ID: Relay authentication is
/// optional here (direct and loopback matches never sign in), and a token the server only ever
/// compares against a seat it already issued needs no authority behind it beyond being
/// unguessable.
/// </para>
/// </summary>
public static class ReconnectSession
{
    /// <summary>Guid.ToString("N"): 32 lowercase hex characters.</summary>
    public const int TokenLength = 32;

    // The payload is read from an unauthenticated peer, so it is length-capped before it is ever
    // decoded rather than trusted to be the token this build writes.
    private const int MaximumPayloadBytes = 64;
    private const string TokenPrefsKeyPrefix = "BattlePlan.ReconnectToken.";

    private static string localToken;
    private static ReconnectTransportMode transportMode = ReconnectTransportMode.None;
    private static string relayJoinCode;

    /// <summary>How this client would reach the host again if it dropped.</summary>
    public static ReconnectTransportMode TransportMode => transportMode;

    public static bool CanAttemptRejoin =>
        transportMode != ReconnectTransportMode.None && IsValidToken(LocalToken);

    /// <summary>
    /// This installation's identity for the active player profile. MPPM virtual players share the
    /// machine's PlayerPrefs store, so the key carries the same launch profile Relay uses; without
    /// that, a host and its clone would present the same identity and race for one seat.
    /// </summary>
    public static string LocalToken
    {
        get
        {
            if (IsValidToken(localToken))
                return localToken;

            string key =
                TokenPrefsKeyPrefix
                + RelayManager.BuildAuthenticationProfile(Environment.GetCommandLineArgs());
            string stored = PlayerPrefs.GetString(key, string.Empty);
            if (!IsValidToken(stored))
            {
                stored = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(key, stored);
                PlayerPrefs.Save();
            }

            localToken = stored;
            return localToken;
        }
    }

    public static bool IsValidToken(string token)
    {
        if (token == null || token.Length != TokenLength)
            return false;

        foreach (char character in token)
        {
            bool isHex =
                (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f');
            if (!isHex)
                return false;
        }

        return true;
    }

    public static byte[] EncodePayload(string token)
    {
        return IsValidToken(token) ? Encoding.ASCII.GetBytes(token) : Array.Empty<byte>();
    }

    /// <summary>Reads an approval payload back into a token, or null if it is not one.</summary>
    public static string DecodeToken(byte[] payload)
    {
        if (payload == null || payload.Length != TokenLength || payload.Length > MaximumPayloadBytes)
            return null;

        string token = Encoding.ASCII.GetString(payload);
        return IsValidToken(token) ? token : null;
    }

    /// <summary>Stamps this client's identity onto the next connection attempt.</summary>
    public static void ApplyConnectionPayload(NetworkManager networkManager)
    {
        if (networkManager == null || networkManager.NetworkConfig == null)
            return;

        networkManager.NetworkConfig.ConnectionData = EncodePayload(LocalToken);
    }

    /// <summary>Records that a dropped connection can be retried against the current transport.</summary>
    public static void RememberDirectClient()
    {
        transportMode = ReconnectTransportMode.Direct;
        relayJoinCode = null;
    }

    /// <summary>Records the Relay allocation a dropped connection would have to re-enter.</summary>
    public static void RememberRelayClient(string joinCode)
    {
        if (!RelayManager.TryNormalizeJoinCode(joinCode, out string normalized))
        {
            Clear();
            return;
        }

        transportMode = ReconnectTransportMode.Relay;
        relayJoinCode = normalized;
    }

    /// <summary>Forgets the route back. Called whenever this client leaves a match on purpose.</summary>
    public static void Clear()
    {
        transportMode = ReconnectTransportMode.None;
        relayJoinCode = null;
    }

    /// <summary>
    /// Reconfigures the transport for another connection attempt. Direct and loopback matches keep
    /// the endpoint the transport already holds; Relay has to re-enter the allocation, because the
    /// client's own binding to it went down with the connection.
    /// </summary>
    public static Task PrepareTransportAsync()
    {
        return transportMode == ReconnectTransportMode.Relay
            ? PrepareRelayTransportAsync(relayJoinCode)
            : Task.CompletedTask;
    }

    private static async Task PrepareRelayTransportAsync(string joinCode)
    {
        RelayManager relayManager = RelayManager.Instance;
        GameObject temporaryHost = null;

        // The scene-owned RelayManager lives in JoinGame; a match rejoining from the Game scene has
        // to stand one up for the duration of the call.
        if (relayManager == null)
        {
            temporaryHost = new GameObject("ReconnectRelayManager");
            UnityEngine.Object.DontDestroyOnLoad(temporaryHost);
            relayManager = temporaryHost.AddComponent<RelayManager>();
        }

        try
        {
            await relayManager.PrepareClientAsync(joinCode);
        }
        finally
        {
            if (temporaryHost != null)
                UnityEngine.Object.Destroy(temporaryHost);
        }
    }
}
