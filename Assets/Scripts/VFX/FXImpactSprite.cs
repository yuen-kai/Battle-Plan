using UnityEngine;

/// <summary>
/// The short-lived BattlePlan/FXUnlit pieces: the white core sphere that owns the impact frame
/// (ArtDirection §9.3) and the backstab slash decal (§9.3, Pogo). Both are the same job — punch
/// in, hold for two or three frames, get out inside the aftermath window — so they share one
/// component driven by a scale curve and a tint fade.
///
/// Everything here is local, non-networked and self-destructing.
/// </summary>
public sealed class FXImpactSprite : MonoBehaviour
{
    private static readonly int TintColorId = Shader.PropertyToID("_TintColor");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    private Renderer spriteRenderer;
    private MaterialPropertyBlock properties;

    private float lifetime = 0.1f;
    private float elapsed;
    private float startScale;
    private float endScale;
    private bool billboard;
    private float rollDegrees;

    // The HDR magnitude rides in tint (a [HDR] property), so intensity stays at 1 and the fade is
    // a straight alpha ramp. Pushing both would double-scale the core.
    private const float Intensity = 1f;
    private Color tint = Color.white;

    /// <summary>
    /// The white-hot core of an explosion: a sphere that snaps open from nothing to
    /// <paramref name="diameter"/> and is gone within the impact frame.
    /// </summary>
    public static FXImpactSprite Core(
        Vector3 position,
        float diameter,
        Color hdrColor,
        float lifetime
    )
    {
        GameObject spawned = FXAssets.Spawn(FXAssets.CoreSphere, position, Quaternion.identity);
        if (spawned == null)
            return null;

        return Configure(spawned, hdrColor, 0f, diameter, lifetime, faceCamera: false);
    }

    /// <summary>
    /// The three-stroke slash over a backstabbed unit. Rolled at random so repeated hits do not
    /// stamp the same decal twice.
    /// </summary>
    public static FXImpactSprite Slash(Vector3 position, float size, Color hdrColor, float lifetime)
    {
        GameObject spawned = FXAssets.Spawn(FXAssets.SlashDecal, position, Quaternion.identity);
        if (spawned == null)
            return null;

        FXImpactSprite slash = Configure(spawned, hdrColor, size, size * 1.15f, lifetime, true);
        if (slash != null)
            slash.rollDegrees = Random.Range(0f, 360f);
        return slash;
    }

    private static FXImpactSprite Configure(
        GameObject spawned,
        Color hdrColor,
        float startScale,
        float endScale,
        float lifetime,
        bool faceCamera
    )
    {
        FXImpactSprite sprite = spawned.GetComponent<FXImpactSprite>();
        if (sprite == null)
            sprite = spawned.AddComponent<FXImpactSprite>();

        sprite.spriteRenderer = spawned.GetComponent<Renderer>();
        sprite.properties = new MaterialPropertyBlock();
        sprite.tint = hdrColor;
        sprite.startScale = startScale;
        sprite.endScale = endScale;
        sprite.lifetime = Mathf.Max(0.01f, lifetime);
        sprite.billboard = faceCamera;
        sprite.elapsed = 0f;
        sprite.transform.localScale = Vector3.one * startScale;
        sprite.Push(1f);
        return sprite;
    }

    private void LateUpdate()
    {
        elapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(elapsed / lifetime);
        if (progress >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        // Open fast, hold, then drop away — the impact-frame envelope from §9.1.
        float opening = 1f - Mathf.Pow(1f - progress, 3f);
        transform.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, opening);
        Push(progress < 0.35f ? 1f : 1f - (progress - 0.35f) / 0.65f);

        if (billboard)
        {
            Camera viewCamera = GameLoop.Instance != null ? GameLoop.Instance.TeamCamera : null;
            if (viewCamera == null)
                viewCamera = Camera.main;
            if (viewCamera != null)
            {
                transform.rotation =
                    viewCamera.transform.rotation * Quaternion.Euler(0f, 0f, rollDegrees);
            }
        }
    }

    private void Push(float strength)
    {
        if (spriteRenderer == null)
            return;
        spriteRenderer.GetPropertyBlock(properties);
        properties.SetColor(TintColorId, new Color(tint.r, tint.g, tint.b, tint.a * strength));
        properties.SetFloat(IntensityId, Intensity);
        spriteRenderer.SetPropertyBlock(properties);
    }
}
