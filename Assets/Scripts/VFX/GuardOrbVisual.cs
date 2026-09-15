using UnityEngine;

public sealed class GuardOrbVisual : MonoBehaviour
{
    public const string GameObjectName = "GuardOrb";
    public const float HitboxScale = 1.5f;

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

        if (!TryMeasureHitbox(out CapsuleCollider hitbox, out bool hiddenByFog))
            return;

        float radius = hitbox.radius * HitboxScale;
        float height = Mathf.Max(hitbox.height * HitboxScale, radius * 2f);
        diameters = new Vector3(radius * 2f, height * 0.5f, radius * 2f);

        GameObject orbObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        orbObject.name = GameObjectName;
        orbObject.layer = gameObject.layer;
        Collider orbCollider = orbObject.GetComponent<Collider>();
        if (orbCollider != null)
        {
            orbCollider.enabled = false;
            Discard(orbCollider);
        }
        orbObject.transform.SetParent(transform, worldPositionStays: false);

        orbObject.transform.localPosition = hitbox.center;
        orbObject.transform.localRotation = CapsuleRotation(hitbox.direction);

        orbMaterial = new Material(orbShader)
        {
            name = "Guard Orb (Runtime)",
            hideFlags = HideFlags.DontSave,
        };

        orbRenderer = orbObject.GetComponent<Renderer>();
        orbRenderer.sharedMaterial = orbMaterial;
        orbRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        orbRenderer.receiveShadows = false;
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

    private static Quaternion CapsuleRotation(int direction) =>
        direction switch
        {
            0 => Quaternion.Euler(0f, 0f, 90f),
            2 => Quaternion.Euler(90f, 0f, 0f),
            _ => Quaternion.identity,
        };

    private bool TryMeasureHitbox(out CapsuleCollider hitbox, out bool hiddenByFog)
    {
        hiddenByFog = false;
        foreach (Renderer candidate in GetComponentsInChildren<Renderer>(true))
        {
            if (DiveRecoveryPulse.IsCharacterRenderer(candidate))
                hiddenByFog |= candidate.forceRenderingOff;
        }

        hitbox = GetComponent<CapsuleCollider>();
        return hitbox != null;
    }
}
