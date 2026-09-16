using UnityEngine;

/// <summary>
/// The translucent slab shown wherever a Shield Wall is about to go up: on the planning board while
/// its direction is being picked, and again through the dodge window that answers it. It is geometry
/// rather than a mark on the floor because what it has to say is where a wall will stand — its facing
/// and the lane it closes are the whole of the information, and neither survives being flattened
/// into an outline on the deck.
///
/// It carries no collider of any kind, so nothing resolved against the board — a line of fire, a
/// vision check, or the response ranges that decide who is alerted to dodge — can be answered
/// against a shield that has not been raised yet.
///
/// Dropped as soon as this screen orders the caster to dive: a dive cancels the diver's own ability,
/// so a slab left standing would promise cover the dodge has already given up. That reads off the
/// local plan while the route is still under the finger, so it can come back if the route is given
/// up again — and it is deliberately local, since the opponent must not be shown which of its
/// alerted units have been answered for.
/// </summary>
public sealed class ShieldStancePreview : MonoBehaviour
{
    public const string GameObjectName = "ShieldWallPreview";

    private const float SlabAlpha = 0.34f;
    private const float FadeSeconds = 0.18f;

    private GameObject caster;
    private Renderer casterRenderer;
    private Renderer slabRenderer;
    private Material slabMaterial;
    private Color slabColor;
    private float visibility = 1f;

    /// <summary>
    /// Builds the slab for whatever <paramref name="caster"/> would raise if it were ordered to
    /// aim at <paramref name="abilitySquare"/>. <paramref name="parent"/> takes the preview when
    /// its lifetime belongs to something else — a planning screen drops a unit's whole plan at
    /// once — and is left off by the dodge window, which tracks its telegraphs itself.
    /// </summary>
    public static GameObject Create(
        GameObject caster,
        Vector3 abilitySquare,
        Color color,
        Transform parent = null
    )
    {
        ShieldStance stance = caster != null ? caster.GetComponent<ShieldStance>() : null;
        if (
            stance == null
            || !stance.TryGetRaisedSlab(abilitySquare, out ShieldStance.RaisedSlab slab)
        )
        {
            return null;
        }

        GameObject previewObject = new(GameObjectName);
        previewObject.transform.SetParent(parent, worldPositionStays: false);
        previewObject.transform.SetPositionAndRotation(slab.Position, slab.Rotation);
        previewObject.transform.localScale = slab.Scale;
        previewObject.AddComponent<MeshFilter>().sharedMesh = slab.Mesh;
        previewObject.AddComponent<ShieldStancePreview>().Build(caster, color);
        return previewObject;
    }

    private void Build(GameObject previewCaster, Color color)
    {
        caster = previewCaster;
        casterRenderer = previewCaster.GetComponentInChildren<Renderer>(includeInactive: true);
        slabColor = color;
        slabMaterial = new Material(Shader.Find("Sprites/Default"))
        {
            name = "Shield Wall Telegraph (Runtime)",
            hideFlags = HideFlags.DontSave,
        };

        slabRenderer = gameObject.AddComponent<MeshRenderer>();
        slabRenderer.sharedMaterial = slabMaterial;
        slabRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        slabRenderer.receiveShadows = false;
        // Inherited here as well as in Update so a slab raised behind host fog never draws for the
        // frame between being built and first following the body it belongs to.
        if (casterRenderer != null)
            slabRenderer.forceRenderingOff = casterRenderer.forceRenderingOff;
        ApplyVisibility();
    }

    private void Update()
    {
        if (caster == null || casterRenderer == null)
        {
            // Fog despawns a hidden caster outright on every peer but the host, taking its own
            // renderers with it. A telegraph for a body this screen no longer has goes with it.
            Destroy(gameObject);
            return;
        }

        // The host cannot NetworkHide its own units, so fog suppresses their renderers locally
        // instead. Follow the body: a slab left drawing would show where a hidden enemy stands.
        slabRenderer.forceRenderingOff = casterRenderer.forceRenderingOff;

        float target =
            PlanMovement.Instance != null && PlanMovement.Instance.HasMovementOrderFor(caster)
                ? 0f
                : 1f;
        if (visibility == target)
            return;

        visibility = Mathf.MoveTowards(visibility, target, Time.deltaTime / FadeSeconds);
        ApplyVisibility();
    }

    private void ApplyVisibility()
    {
        // Scales the colour's own alpha rather than replacing it: a planning preview arrives
        // already dimmed when its unit is not the one being given orders.
        slabMaterial.color = slabColor.WithAlpha(slabColor.a * SlabAlpha * visibility);
    }

    private void OnDestroy()
    {
        if (slabMaterial != null)
            Destroy(slabMaterial);
    }
}
