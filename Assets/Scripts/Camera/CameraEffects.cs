using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class CameraEffects : NetworkBehaviour
{
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
    /// Shakes the camera for the local client's team
    /// </summary>
    /// <param name="shakeDuration">Duration of the shake</param>
    /// <param name="shakeIntensity">Intensity of the shake</param>
    public IEnumerator CameraShake(float shakeDuration = 0.5f, float shakeIntensity = 0.1f)
    {
        Camera teamCamera = GameLoop.Instance.TeamCamera;
        if (teamCamera == null)
        {
            Debug.LogWarning("[CameraEffects] No camera found, shake effect skipped");
            yield break;
        }

        Vector3 originalPosition = teamCamera.transform.position;
        float elapsed = 0f;

        while (elapsed < shakeDuration)
        {
            float x = Random.Range(-1f, 1f) * shakeIntensity;
            float y = Random.Range(-1f, 1f) * shakeIntensity;

            teamCamera.transform.position = originalPosition + new Vector3(x, y, 0);

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        teamCamera.transform.position = originalPosition;
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
