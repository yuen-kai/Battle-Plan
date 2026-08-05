using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class CameraEffects : NetworkBehaviour
{
    // The legacy intensity that now means "a standard grenade". Callers all pass the default.
    private const float StandardShakeIntensity = 0.1f;
    private const float MaxShakeStrength = 3f;

    public static CameraEffects Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Debug.LogWarning("[CameraEffects] Multiple instances detected, destroying duplicate");
            Destroy(gameObject);
        }
    }

    public override void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        base.OnDestroy();
    }

    [ClientRpc]
    public void CameraShakeClientRpc(float shakeDuration = 0.5f, float shakeIntensity = 0.1f)
    {
        StartCoroutine(CameraShake(shakeDuration, shakeIntensity));
    }

    /// <summary>
    /// Shakes the camera for the local client's team.
    /// </summary>
    /// <param name="shakeDuration">
    /// How long a caller expects to wait on this coroutine. The shake itself is an impulse owned by
    /// <see cref="ImpactCamera"/> and lasts as long as its strength earns, not as long as this.
    /// </param>
    /// <param name="shakeIntensity">Intensity of the shake; 0.1 is a standard hit.</param>
    public IEnumerator CameraShake(float shakeDuration = 0.5f, float shakeIntensity = 0.1f)
    {
        PunchAtViewCentre(shakeIntensity);
        yield return new WaitForSecondsRealtime(Mathf.Clamp(shakeDuration, 0f, 1f));
    }

    /// <summary>
    /// The legacy call carries no impact position, so the hit is treated as happening where the
    /// camera is already looking. The punch still rolls and zooms at full force — those axes do not
    /// need a direction — but it can only shove the board straight down the screen, and every hit
    /// routed through here leans the same way. Callers that know where the impact landed should
    /// reach <see cref="ImpactCamera.Punch"/> directly and pass it.
    /// </summary>
    private static void PunchAtViewCentre(float shakeIntensity)
    {
        Camera teamCamera = GameLoop.Instance != null ? GameLoop.Instance.TeamCamera : null;
        if (teamCamera == null)
        {
            Debug.LogWarning("[CameraEffects] No camera found, shake effect skipped");
            return;
        }

        Transform view = teamCamera.transform;
        Vector3 forward = view.forward;
        float toDeck = forward.y < -0.01f ? view.position.y / -forward.y : GameLoop.cellSize * 5f;

        ImpactCamera.Punch(
            view.position + forward * Mathf.Max(toDeck, GameLoop.cellSize),
            Mathf.Clamp(shakeIntensity / StandardShakeIntensity, 0f, MaxShakeStrength)
        );
    }

    // [ClientRpc]
    // public void FlashClientRpc(string team = "neutral", float flashDuration = 0.2f)
    // {
    //     Color color = Color.white;
    //     if (team == "neutral")
    //     {
    //         color = (Color)(GameLoop.Instance?.executingMoves ?? Color.white);
    //     }
    //     else
    //     {
    //         // Determine if this message is about the client's own team or enemy team
    //         ulong localClientId = NetworkManager.Singleton.LocalClientId;
    //         string localTeam = GameLoop.Instance?.GetLocalClientTeam(localClientId) ?? "neutral";
    //         if (localTeam == team)
    //         {
    //             color = (Color)(GameLoop.Instance?.teamColors[0] ?? Color.white);
    //         }
    //         else
    //         {
    //             color = (Color)(GameLoop.Instance?.teamColors[1] ?? Color.white);
    //         }
    //     }

    //     StartCoroutine(Flash(color, flashDuration));
    // }

    [ClientRpc]
    public void FlashClientRpc(MessagePerspective perspective, float flashDuration = 0.2f)
    {
        if (GameHUDController.Instance != null)
        {
            GameHUDController.Instance.Flash(perspective, flashDuration);
            return;
        }

        Debug.LogWarning("[CameraEffects] UI Toolkit HUD is unavailable; flash skipped.");
    }
}
