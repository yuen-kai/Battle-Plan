using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps the first NGO NetworkManager as the single persistent runtime instance. Revisiting the
/// JoinGame scene otherwise creates another DontDestroyOnLoad manager on every visit.
/// </summary>
public static class NetworkManagerLifetime
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        SceneManager.sceneLoaded -= RemoveDuplicateManagers;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        SceneManager.sceneLoaded -= RemoveDuplicateManagers;
        SceneManager.sceneLoaded += RemoveDuplicateManagers;
    }

    private static void RemoveDuplicateManagers(Scene scene, LoadSceneMode mode)
    {
        NetworkManager[] managers = Object.FindObjectsByType<NetworkManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );
        if (managers.Length <= 1)
            return;

        NetworkManager managerToKeep = NetworkManager.Singleton;
        if (managerToKeep == null)
        {
            managerToKeep = managers[0];
            managerToKeep.SetSingleton();
        }

        foreach (NetworkManager manager in managers)
        {
            if (manager == null || manager == managerToKeep)
                continue;

            manager.gameObject.SetActive(false);
            Object.Destroy(manager.gameObject);
        }
    }
}
