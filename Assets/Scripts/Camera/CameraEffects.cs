using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class CameraEffects : NetworkBehaviour
{
    public static CameraEffects Instance { get; private set; }
    public GameObject flash;

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
        Color color =
            perspective == MessagePerspective.Friendly
                ? (Color)(GameLoop.Instance?.teamColors[0] ?? Color.white)
                : (
                    perspective == MessagePerspective.Enemy
                        ? (Color)(GameLoop.Instance?.teamColors[1] ?? Color.white)
                        : (Color)(GameLoop.Instance?.executingMoves ?? Color.white)
                );
        StartCoroutine(Flash(color, flashDuration));
    }

    public IEnumerator Flash(Color color = default, float flashDuration = 0.2f)
    {
        var image = flash.GetComponent<Image>();
        float originalAlpha = image.color.a;
        Color newColor = color;
        newColor.a = originalAlpha;
        image.color = newColor;

        flash.SetActive(true);
        yield return new WaitForSecondsRealtime(flashDuration);
        flash.SetActive(false);
    }
}
