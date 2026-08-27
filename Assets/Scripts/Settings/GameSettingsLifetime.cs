using UnityEngine;

/// <summary>
/// Loads and applies the stored settings before the first scene's objects wake, so no frame is ever
/// drawn and no source ever plays at a level the player did not choose.
/// </summary>
public static class GameSettingsLifetime
{
    /// <summary>
    /// Cleared in this order so the mixer's subscription goes with the event it was placed on, to be
    /// re-made by the first source that registers.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        GameSettings.ClearRuntimeState();
        AudioManager.ClearRuntimeState();
        FrameRatePolicy.ClearRuntimeState();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        GameSettings.EnsureLoaded();
        // After the settings, because it overrides the vSync count the quality level just applied.
        FrameRatePolicy.Create();
    }
}
