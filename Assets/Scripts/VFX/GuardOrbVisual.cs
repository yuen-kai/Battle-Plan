using UnityEngine;

public sealed class GuardOrbVisual : MonoBehaviour
{
    public const string GameObjectName = "GuardOrb";

    private const float PaddingCells = 0.16f;
    private const float BloomInSeconds = 0.22f;
    private const float BloomOutSeconds = 0.3f;
    private const float SettledScale = 1f;
    private const float BurstScale = 1.18f;

    private static readonly int BloomId = Shader.PropertyToID("_Bloom");

    private Transform orb;
    private Renderer orbRenderer;
    private Material orbMaterial;
    private Vector3 diameters;
    private float bloom;
    private float targetBloom;

    public bool IsGuarded => targetBloom > 0f;
    public float Bloom => bloom;

    public static GuardOrbVisual Attach(GameObject unit)
    {
        if (unit == null)
            return null;
        if (!unit.TryGetComponent(out GuardOrbVisual visual))
            visual = unit.AddComponent<GuardOrbVisual>();
        return visual;
    }

    public void SetGuarded(bool guarded)
    {
        targetBloom = guarded ? 1f : 0f;
        if (!guarded)
            return;

        EnsureBuilt();
        if (orb != null)
            orb.gameObject.SetActive(true);
        Draw();
    }

    private void Update()
    {
        if (orb == null)
            return;

        float rate = targetBloom > bloom ? BloomInSeconds : BloomOutSeconds;
        bloom = Mathf.MoveTowards(bloom, targetBloom, Time.deltaTime / Mathf.Max(0.01f, rate));
        Draw();

        if (bloom <= 0f && orb.gameObject.activeSelf)
            orb.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        targetBloom = 0f;
        bloom = 0f;
        if (orb != null)
            orb.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (orbMaterial != null)
            Discard(orbMaterial);
    }

    // Editor tooling builds a shell outside Play mode (asset checks, capture rigs), where Destroy
    // is an error rather than a deferral.
    private static void Discard(Object target)
    {
        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }

    private void Draw()
    {
        if (orbMaterial != null)
            orbMaterial.SetFloat(BloomId, bloom);

        // Overshoots on the way in so the shell reads as thrown up rather than faded up.
        float overshoot = BurstScale - (BurstScale - SettledScale) * Mathf.Clamp01(bloom);
        Vector3 scale = diameters * Mathf.Lerp(0.55f, overshoot, Mathf.Clamp01(bloom));
        orb.localScale = ToLocalScale(scale);
    }

    private void EnsureBuilt()
    {
        if (orb != null)
            return;

        Shader orbShader = Shader.Find("BattlePlan/GuardOrb");
        if (orbShader == null)
        {
            Debug.LogWarning(
                "[GuardOrbVisual] BattlePlan/GuardOrb shader not found; the damage guard has no "
                    + "indicator."
            );
            return;
        }

        if (!TryMeasureCharacter(out Bounds characterBounds, out bool hiddenByFog))
            return;

        // Per axis rather than one enclosing sphere: a ball sized to a standing figure's height is
        // two cells wide, and five of them in a 3x3 huddle merge into one blob.
        float padding = PaddingCells * GameLoop.cellSize;
        diameters = characterBounds.size + new Vector3(padding, padding, padding);

        GameObject orbObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        orbObject.name = GameObjectName;
        orbObject.layer = gameObject.layer;
        // Disabled before it is discarded: Destroy is deferred to the end of the frame, and a live
        // collider on a unit for even one frame is something bullets and targeting casts can hit.
        Collider orbCollider = orbObject.GetComponent<Collider>();
        if (orbCollider != null)
        {
            orbCollider.enabled = false;
            Discard(orbCollider);
        }
        orbObject.transform.SetParent(transform, worldPositionStays: false);

        orbObject.transform.localPosition = ToLocalScale(
            characterBounds.center - transform.position
        );
        orbObject.transform.localRotation = Quaternion.identity;

        orbMaterial = new Material(orbShader)
        {
            name = "Guard Orb (Runtime)",
            hideFlags = HideFlags.DontSave,
        };

        orbRenderer = orbObject.GetComponent<Renderer>();
        orbRenderer.sharedMaterial = orbMaterial;
        orbRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        orbRenderer.receiveShadows = false;
        // GameLoop's host-side fog suppresses a hidden unit's existing renderers, so a shell built
        // while its unit is hidden has to start out suppressed too.
        orbRenderer.forceRenderingOff = hiddenByFog;

        orb = orbObject.transform;
    }

    private Vector3 ToLocalScale(Vector3 world)
    {
        Vector3 parentScale = transform.lossyScale;
        return new Vector3(
            world.x / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
            world.y / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
            world.z / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z))
        );
    }

    private bool TryMeasureCharacter(out Bounds characterBounds, out bool hiddenByFog)
    {
        characterBounds = default;
        hiddenByFog = false;
        bool measured = false;

        foreach (Renderer candidate in GetComponentsInChildren<Renderer>(true))
        {
            if (!DiveRecoveryPulse.IsCharacterRenderer(candidate))
                continue;

            hiddenByFog |= candidate.forceRenderingOff;
            if (!measured)
            {
                characterBounds = candidate.bounds;
                measured = true;
                continue;
            }
            characterBounds.Encapsulate(candidate.bounds);
        }
        return measured;
    }
}
