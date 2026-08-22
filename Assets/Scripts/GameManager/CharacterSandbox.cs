#if UNITY_EDITOR
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CharacterSandbox
{
    private const string AutoRunKey = "BattlePlan.CharacterSandbox.AutoRun";
    private const string TestUnitKey = "BattlePlan.CharacterSandbox.TestUnit";
    private const string DummyUnitKey = "BattlePlan.CharacterSandbox.DummyUnit";
    private const string DummyFightsBackKey = "BattlePlan.CharacterSandbox.DummyFightsBack";
    private const string EnemyCountKey = "BattlePlan.CharacterSandbox.EnemyCount";

    public const int DefaultDummyUnitIndex = 4;

    public static void Launch(
        int testUnitIndex,
        int dummyUnitIndex,
        bool dummyFightsBack,
        int enemyCount = 1
    )
    {
        UnityEditor.SessionState.SetInt(TestUnitKey, testUnitIndex);
        UnityEditor.SessionState.SetInt(DummyUnitKey, dummyUnitIndex);
        UnityEditor.SessionState.SetBool(DummyFightsBackKey, dummyFightsBack);
        UnityEditor.SessionState.SetInt(EnemyCountKey, enemyCount);

        if (Application.isPlaying)
        {
            BeginSessionAndLoadJoinScene();
            return;
        }

        UnityEditor.SessionState.SetBool(AutoRunKey, true);
        UnityEditor.EditorApplication.EnterPlaymode();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoRunAfterEnterPlay()
    {
        if (!Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor)
            return;
        if (!UnityEditor.SessionState.GetBool(AutoRunKey, false))
            return;
        UnityEditor.SessionState.EraseBool(AutoRunKey);
        BeginSessionAndLoadJoinScene();
    }

    private static void BeginSessionAndLoadJoinScene()
    {
        if (SandboxLaunchRunner.Instance != null)
            Object.Destroy(SandboxLaunchRunner.Instance.gameObject);

        GameObject runner = new("SandboxLaunchRunner");
        Object.DontDestroyOnLoad(runner);
        runner.AddComponent<SandboxLaunchRunner>();
    }

    public static bool IsRunning => Application.isPlaying && SandboxSession.IsActive;

    internal static void BeginSessionFromStoredSelection()
    {
        SandboxSession.Begin(
            UnityEditor.SessionState.GetInt(TestUnitKey, 0),
            UnityEditor.SessionState.GetInt(DummyUnitKey, DefaultDummyUnitIndex),
            UnityEditor.SessionState.GetInt(EnemyCountKey, 1)
        );
        SandboxSession.DummyFightsBack = UnityEditor.SessionState.GetBool(
            DummyFightsBackKey,
            false
        );
    }
}

internal sealed class SandboxLaunchRunner : MonoBehaviour
{
    private const float ShutdownTimeoutSeconds = 10f;

    public static SandboxLaunchRunner Instance { get; private set; }

    private void Awake() => Instance = this;

    private System.Collections.IEnumerator Start()
    {
        SandboxSession.End();

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening)
        {
            if (networkManager.IsServer)
                NetworkHelper.CleanupAllNetworkObjects();
            networkManager.Shutdown();
            MatchOptions.Reset();
            GameLoop.ResetMatchState();

            float deadline = Time.realtimeSinceStartup + ShutdownTimeoutSeconds;
            while (
                NetworkManager.Singleton != null
                && (NetworkManager.Singleton.IsListening || NetworkManager.Singleton.ShutdownInProgress)
            )
            {
                if (Time.realtimeSinceStartup >= deadline)
                {
                    Debug.LogWarning(
                        "[Sandbox] Previous match did not shut down in time; launching anyway."
                    );
                    break;
                }
                yield return null;
            }
        }

        CharacterSandbox.BeginSessionFromStoredSelection();

        MatchOptions.SetCurrent(SandboxSession.BuildMatchOptions());
        SceneManager.LoadScene("JoinGame", LoadSceneMode.Single);

        Instance = null;
        Destroy(gameObject);
    }
}
#endif
