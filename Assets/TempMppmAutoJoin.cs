// TEMPORARY test bootstrap for the baseline playtest (see docs/TESTING_BASELINE.md).
// Auto-starts an NGO client in MPPM virtual players so a 2-client match can be
// exercised without manual UI clicks in the clone editor. Safe to delete.
#if UNITY_EDITOR
using System.Collections;
using Unity.Netcode;
using UnityEngine;

public static class TempMppmAutoJoin
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        if (Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor)
            return;
        var runner = new GameObject("TempMppmAutoJoin").AddComponent<TempMppmAutoJoinRunner>();
        Object.DontDestroyOnLoad(runner.gameObject);
    }
}

public class TempMppmAutoJoinRunner : MonoBehaviour
{
    IEnumerator Start()
    {
        float deadline = Time.realtimeSinceStartup + 60f;
        while (Time.realtimeSinceStartup < deadline)
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && !nm.IsClient && !nm.IsServer && !nm.IsListening)
            {
                yield return new WaitForSecondsRealtime(3f);
                nm = NetworkManager.Singleton;
                if (nm == null || nm.IsClient || nm.IsServer)
                    yield break;
                Debug.Log("[TempMppmAutoJoin] Starting client...");
                nm.StartClient();
                // Give the connection 10s; if it failed, loop and retry.
                yield return new WaitForSecondsRealtime(10f);
                nm = NetworkManager.Singleton;
                if (nm != null && nm.IsConnectedClient)
                {
                    Debug.Log("[TempMppmAutoJoin] Connected.");
                    yield break;
                }
                if (nm != null && nm.IsClient)
                {
                    Debug.Log("[TempMppmAutoJoin] Connection attempt stalled, shutting down to retry.");
                    nm.Shutdown();
                    yield return new WaitForSecondsRealtime(2f);
                }
            }
            yield return null;
        }
    }
}
#endif
