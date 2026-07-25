using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The grenade's blast mark (ArtDirection §9.3): 4.0 units across, --bp-void at 0.35 alpha,
/// fading to nothing over three seconds.
///
/// The hard rule is the second half of that spec — "the scorch must be gone before the next
/// planning phase, so it never reads as terrain". A dead cell that still looks burnt is a cell
/// players will misread as blocked, so <see cref="ClearAll"/> is driven from the execution-boundary
/// ClientRpc and wipes every decal on every peer whatever its fade has left to run. It cannot be
/// driven off GameLoop.currentPhase: that static is only ever written on the server, so a remote
/// client would either never clear or clear instantly.
/// </summary>
public sealed class ScorchDecal : MonoBehaviour
{
    private static readonly int TintColorId = Shader.PropertyToID("_TintColor");
    private static readonly List<ScorchDecal> active = new();

    public const float Diameter = 4f;
    public const float FadeSeconds = 3f;
    public const float PeakAlpha = 0.35f;

    private Renderer decalRenderer;
    private MaterialPropertyBlock properties;
    private Color baseTint;
    private float elapsed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        active.Clear();
    }

    /// <summary>Stamps a scorch centred on <paramref name="worldPosition"/>.</summary>
    public static ScorchDecal Stamp(Vector3 worldPosition, float diameter = Diameter)
    {
        GameObject spawned = FXAssets.Spawn(
            FXAssets.ScorchDecal,
            new Vector3(worldPosition.x, FXPalette.YScorchDecal, worldPosition.z),
            Quaternion.Euler(90f, Random.Range(0f, 360f), 0f)
        );
        if (spawned == null)
            return null;

        spawned.transform.localScale = Vector3.one * diameter;
        ScorchDecal decal = spawned.GetComponent<ScorchDecal>();
        if (decal == null)
            decal = spawned.AddComponent<ScorchDecal>();
        return decal;
    }

    /// <summary>Clears every scorch on this client immediately. Called at the execution boundary.</summary>
    public static void ClearAll()
    {
        for (int index = active.Count - 1; index >= 0; index--)
        {
            if (active[index] != null)
                Destroy(active[index].gameObject);
        }
        active.Clear();
    }

    private void Awake()
    {
        decalRenderer = GetComponent<Renderer>();
        properties = new MaterialPropertyBlock();
        baseTint = decalRenderer != null && decalRenderer.sharedMaterial != null
            ? decalRenderer.sharedMaterial.GetColor(TintColorId)
            : new Color(FXPalette.Ink.r, FXPalette.Ink.g, FXPalette.Ink.b, PeakAlpha);
        active.Add(this);
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float remaining = 1f - Mathf.Clamp01(elapsed / FadeSeconds);
        if (remaining <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        if (decalRenderer == null)
            return;

        decalRenderer.GetPropertyBlock(properties);
        properties.SetColor(
            TintColorId,
            new Color(baseTint.r, baseTint.g, baseTint.b, PeakAlpha * remaining)
        );
        decalRenderer.SetPropertyBlock(properties);
    }

    private void OnDestroy()
    {
        active.Remove(this);
    }
}
