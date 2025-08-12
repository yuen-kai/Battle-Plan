using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraEffects : MonoBehaviour
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

    /// <summary>
    /// Shakes the camera for the local client's team
    /// </summary>
    /// <param name="shakeDuration">Duration of the shake</param>
    /// <param name="shakeIntensity">Intensity of the shake</param>
    public IEnumerator CameraShake(float shakeDuration = 0.5f, float shakeIntensity = 0.1f)
    {
        Camera teamCamera = GameLoop.Instance.teamCamera;
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
}
