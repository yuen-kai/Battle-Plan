using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The last beat of a match (ArtDirection §9.4): a shake, a short freeze, and the vignette
/// closing from 0.28 to 0.45 over 0.6 s while the results scrim comes up. The board stays visible
/// behind the panel, so the vignette is what tells the eye the match is over rather than a
/// blackout.
///
/// The vignette is applied through a *temporary* Volume at a higher priority than the scene's,
/// blended in by weight. Editing the scene's own profile would dirty a shared .asset in play mode
/// and would collide with whoever owns Assets/Settings; this touches nothing and disappears with
/// the object.
/// </summary>
public sealed class MatchEndFX : MonoBehaviour
{
    public const float ShakeDuration = 0.35f;
    public const float ShakeIntensity = 0.1f;
    public const float HitstopSeconds = 0.1f;
    public const float HitstopTimeScale = 0.15f;
    public const float VignetteBlendSeconds = 0.6f;
    public const float VignetteTargetIntensity = 0.45f;
    public const float VignetteSmoothness = 0.42f;

    /// <summary>--bp-void #05070B, the vignette colour the whole direction uses.</summary>
    private static readonly Color VignetteColor = new(0.0196f, 0.0275f, 0.0431f, 1f);

    private static MatchEndFX instance;

    private Volume volume;
    private VolumeProfile profile;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    /// <summary>
    /// Plays the match-end beat on this peer. Called from the match-end ClientRpc, so every
    /// client runs it locally and no extra traffic is needed to synchronise it.
    /// </summary>
    public static void Play()
    {
        if (instance != null)
            return;

        GameObject host = new("MatchEndFX");
        instance = host.AddComponent<MatchEndFX>();
        instance.Begin();
    }

    /// <summary>Tears the vignette down. Called when a rematch starts.</summary>
    public static void Clear()
    {
        if (instance != null)
            Destroy(instance.gameObject);
        instance = null;
    }

    private void Begin()
    {
        // Skipped in dev mode by FXHitstop itself, so automated runs never see a timeScale dip.
        FXHitstop.Request(HitstopSeconds, HitstopTimeScale);

        if (CameraEffects.Instance != null)
        {
            // Run the shake locally rather than through CameraShakeClientRpc: every peer reaches
            // this point on its own, and a broadcast would be redundant traffic from the server.
            CameraEffects.Instance.StartCoroutine(
                CameraEffects.Instance.CameraShake(ShakeDuration, ShakeIntensity)
            );
        }

        StartCoroutine(BlendVignette());
    }

    private IEnumerator BlendVignette()
    {
        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "GameDiorama Match End (Runtime)";

        Vignette vignette = profile.Add<Vignette>(overrides: false);
        vignette.color.overrideState = true;
        vignette.color.value = VignetteColor;
        vignette.intensity.overrideState = true;
        vignette.intensity.value = VignetteTargetIntensity;
        vignette.smoothness.overrideState = true;
        vignette.smoothness.value = VignetteSmoothness;
        vignette.rounded.overrideState = true;
        vignette.rounded.value = false;

        volume = gameObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 100f;
        volume.sharedProfile = profile;
        volume.weight = 0f;

        float elapsed = 0f;
        while (elapsed < VignetteBlendSeconds)
        {
            // Unscaled: the hitstop above is still running for the first tenth of a second.
            elapsed += Time.unscaledDeltaTime;
            volume.weight = Mathf.Clamp01(elapsed / VignetteBlendSeconds);
            yield return null;
        }
        volume.weight = 1f;
    }

    private void OnDestroy()
    {
        if (profile != null)
            Destroy(profile);
        if (instance == this)
            instance = null;
    }
}
