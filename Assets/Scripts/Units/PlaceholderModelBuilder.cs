using System.Collections.Generic;
using UnityEngine;

// Anchor points on the placeholder silhouette that an accessory can be attached to. Values map
// 1:1 onto a child Transform created under the "Anchors" group in BuildHumanoidSilhouette; adding
// a new anchor here requires adding a matching entry in that method's anchor-position table too.
public enum PlaceholderBodyAnchor
{
    Torso,
    Head,
    Pelvis,
    LeftForearm,
    RightForearm,
    LeftHand,
    RightHand,
}

[System.Serializable]
public class PlaceholderAccessorySpec
{
    public string Name = "Accessory";
    public PrimitiveType PrimitiveType = PrimitiveType.Cube;
    public PlaceholderBodyAnchor Anchor = PlaceholderBodyAnchor.Torso;
    public Vector3 LocalPosition = Vector3.zero;
    public Vector3 LocalEulerAngles = Vector3.zero;
    public Vector3 LocalScale = Vector3.one;
    public Color Color = new(0.55f, 0.55f, 0.58f, 1f);
}

[System.Serializable]
public class PlaceholderModelSpec
{
    public float HeightScale = 1f;
    public float WidthScale = 1f;
    public Color AccentColor = new(0.6f, 0.6f, 0.65f, 1f);
    public List<PlaceholderAccessorySpec> Accessories = new();
}

// Builds an unanimated, primitive-only humanoid silhouette to stand in for a character model while
// AI model generation is unavailable. Every new placeholder character in this batch should get its
// own PlaceholderModelSpec (see BuildSentinel below for the pattern) rather than new logic in here
// — the builder only knows about generic body proportions and anchor points, never about a
// specific character.
public static class PlaceholderModelBuilder
{
    private const float BaseHeight = 1.8f;
    private const float BaseShoulderWidth = 0.5f;

    public static GameObject BuildHumanoidSilhouette(string name, PlaceholderModelSpec spec)
    {
        spec ??= new PlaceholderModelSpec();

        GameObject root = new(name);
        Dictionary<Color, Material> materialCache = new();

        Transform body = new GameObject("Body").transform;
        body.SetParent(root.transform, false);
        Transform anchorsGroup = new GameObject("Anchors").transform;
        anchorsGroup.SetParent(root.transform, false);

        float height = BaseHeight * Mathf.Max(spec.HeightScale, 0.01f);
        float shoulderWidth = BaseShoulderWidth * Mathf.Max(spec.WidthScale, 0.01f);

        float legHeight = height * 0.48f;
        float torsoHeight = height * 0.34f;
        float headDiameter = height * 0.18f;
        float torsoRadius = shoulderWidth * 0.5f;
        float limbRadius = shoulderWidth * 0.16f;
        float armRadius = shoulderWidth * 0.13f;
        float upperArmLength = torsoHeight * 0.55f;
        float forearmLength = torsoHeight * 0.5f;

        float hipY = legHeight;
        float shoulderY = hipY + torsoHeight;
        float torsoCenterY = hipY + torsoHeight * 0.5f;
        float headCenterY = shoulderY + headDiameter * 0.5f;

        // Character faces +Z; +X is the character's right side (Unity's usual right-handed sense
        // when viewed from behind the character looking down +Z).
        float armX = shoulderWidth * 0.5f + armRadius;
        float upperArmCenterY = shoulderY - upperArmLength * 0.5f;
        float forearmCenterY = shoulderY - upperArmLength - forearmLength * 0.5f;
        float handY = shoulderY - upperArmLength - forearmLength;
        float legX = shoulderWidth * 0.28f;

        Material bodyMaterial = GetOrCreateMaterial(materialCache, spec.AccentColor);

        CreatePart(
            "Torso",
            PrimitiveType.Capsule,
            body,
            new Vector3(0f, torsoCenterY, 0f),
            Vector3.zero,
            new Vector3(torsoRadius * 2f, torsoHeight * 0.5f, torsoRadius * 2f),
            bodyMaterial
        );
        CreatePart(
            "Head",
            PrimitiveType.Sphere,
            body,
            new Vector3(0f, headCenterY, 0f),
            Vector3.zero,
            Vector3.one * headDiameter,
            bodyMaterial
        );
        CreatePart(
            "LeftUpperArm",
            PrimitiveType.Cylinder,
            body,
            new Vector3(-armX, upperArmCenterY, 0f),
            Vector3.zero,
            new Vector3(armRadius * 2f, upperArmLength * 0.5f, armRadius * 2f),
            bodyMaterial
        );
        CreatePart(
            "RightUpperArm",
            PrimitiveType.Cylinder,
            body,
            new Vector3(armX, upperArmCenterY, 0f),
            Vector3.zero,
            new Vector3(armRadius * 2f, upperArmLength * 0.5f, armRadius * 2f),
            bodyMaterial
        );
        CreatePart(
            "LeftForearm",
            PrimitiveType.Cylinder,
            body,
            new Vector3(-armX, forearmCenterY, 0f),
            Vector3.zero,
            new Vector3(armRadius * 1.8f, forearmLength * 0.5f, armRadius * 1.8f),
            bodyMaterial
        );
        CreatePart(
            "RightForearm",
            PrimitiveType.Cylinder,
            body,
            new Vector3(armX, forearmCenterY, 0f),
            Vector3.zero,
            new Vector3(armRadius * 1.8f, forearmLength * 0.5f, armRadius * 1.8f),
            bodyMaterial
        );
        CreatePart(
            "LeftLeg",
            PrimitiveType.Cylinder,
            body,
            new Vector3(-legX, hipY * 0.5f, 0f),
            Vector3.zero,
            new Vector3(limbRadius * 2f, hipY * 0.5f, limbRadius * 2f),
            bodyMaterial
        );
        CreatePart(
            "RightLeg",
            PrimitiveType.Cylinder,
            body,
            new Vector3(legX, hipY * 0.5f, 0f),
            Vector3.zero,
            new Vector3(limbRadius * 2f, hipY * 0.5f, limbRadius * 2f),
            bodyMaterial
        );

        Dictionary<PlaceholderBodyAnchor, Transform> anchors = new()
        {
            [PlaceholderBodyAnchor.Torso] = CreateAnchor(
                anchorsGroup,
                PlaceholderBodyAnchor.Torso,
                new Vector3(0f, torsoCenterY, 0f)
            ),
            [PlaceholderBodyAnchor.Head] = CreateAnchor(
                anchorsGroup,
                PlaceholderBodyAnchor.Head,
                new Vector3(0f, headCenterY, 0f)
            ),
            [PlaceholderBodyAnchor.Pelvis] = CreateAnchor(
                anchorsGroup,
                PlaceholderBodyAnchor.Pelvis,
                new Vector3(0f, hipY, 0f)
            ),
            [PlaceholderBodyAnchor.LeftForearm] = CreateAnchor(
                anchorsGroup,
                PlaceholderBodyAnchor.LeftForearm,
                new Vector3(-armX, forearmCenterY, 0f)
            ),
            [PlaceholderBodyAnchor.RightForearm] = CreateAnchor(
                anchorsGroup,
                PlaceholderBodyAnchor.RightForearm,
                new Vector3(armX, forearmCenterY, 0f)
            ),
            [PlaceholderBodyAnchor.LeftHand] = CreateAnchor(
                anchorsGroup,
                PlaceholderBodyAnchor.LeftHand,
                new Vector3(-armX, handY, 0f)
            ),
            [PlaceholderBodyAnchor.RightHand] = CreateAnchor(
                anchorsGroup,
                PlaceholderBodyAnchor.RightHand,
                new Vector3(armX, handY, 0f)
            ),
        };

        if (spec.Accessories != null)
        {
            foreach (PlaceholderAccessorySpec accessorySpec in spec.Accessories)
            {
                if (accessorySpec == null || !anchors.TryGetValue(accessorySpec.Anchor, out Transform anchor))
                    continue;

                Material accessoryMaterial = GetOrCreateMaterial(materialCache, accessorySpec.Color);
                CreatePart(
                    accessorySpec.Name,
                    accessorySpec.PrimitiveType,
                    anchor,
                    accessorySpec.LocalPosition,
                    accessorySpec.LocalEulerAngles,
                    accessorySpec.LocalScale,
                    accessoryMaterial
                );
            }
        }

        return root;
    }

    public static GameObject BuildSentinel()
    {
        return BuildHumanoidSilhouette("Sentinel", CreateSentinelSpec());
    }

    // Bulkier proportions than a lean unit (wider shoulders/torso, slightly shorter) to read as a
    // support unit that plants and holds ground, plus a shield near the left forearm and a pistol
    // in the right hand. Color is a saturated steel blue, distinct from the desaturated
    // gunmetal/navy/teal already used across Assets/Materials/Characters (Char_Gunmetal,
    // Char_CoatNavy, Char_TealAccent) so Sentinel doesn't read as a recolor of an existing unit.
    private static PlaceholderModelSpec CreateSentinelSpec()
    {
        Color accent = new(0.18f, 0.4f, 0.58f, 1f);
        Color plating = new(0.32f, 0.36f, 0.4f, 1f);
        Color gunmetal = new(0.12f, 0.12f, 0.14f, 1f);

        return new PlaceholderModelSpec
        {
            HeightScale = 0.94f,
            WidthScale = 1.25f,
            AccentColor = accent,
            Accessories = new List<PlaceholderAccessorySpec>
            {
                new()
                {
                    Name = "Shield",
                    PrimitiveType = PrimitiveType.Cube,
                    Anchor = PlaceholderBodyAnchor.LeftForearm,
                    LocalPosition = new Vector3(-0.08f, 0f, 0.05f),
                    LocalEulerAngles = Vector3.zero,
                    LocalScale = new Vector3(0.08f, 0.55f, 0.4f),
                    Color = plating,
                },
                new()
                {
                    Name = "Pistol",
                    PrimitiveType = PrimitiveType.Cube,
                    Anchor = PlaceholderBodyAnchor.RightHand,
                    LocalPosition = new Vector3(0.03f, -0.02f, 0.08f),
                    LocalEulerAngles = Vector3.zero,
                    LocalScale = new Vector3(0.07f, 0.09f, 0.22f),
                    Color = gunmetal,
                },
            },
        };
    }

    private static Transform CreateAnchor(Transform parent, PlaceholderBodyAnchor anchor, Vector3 localPosition)
    {
        GameObject anchorObject = new(anchor.ToString());
        anchorObject.transform.SetParent(parent, false);
        anchorObject.transform.localPosition = localPosition;
        return anchorObject.transform;
    }

    private static GameObject CreatePart(
        string name,
        PrimitiveType primitiveType,
        Transform parent,
        Vector3 localPosition,
        Vector3 localEulerAngles,
        Vector3 localScale,
        Material material
    )
    {
        GameObject part = GameObject.CreatePrimitive(primitiveType);
        part.name = name;

        Collider collider = part.GetComponent<Collider>();
        if (collider != null)
            Object.DestroyImmediate(collider);

        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localRotation = Quaternion.Euler(localEulerAngles);
        part.transform.localScale = localScale;

        MeshRenderer renderer = part.GetComponent<MeshRenderer>();
        if (renderer != null)
            renderer.sharedMaterial = material;

        return part;
    }

    private static Material GetOrCreateMaterial(Dictionary<Color, Material> cache, Color color)
    {
        if (cache.TryGetValue(color, out Material existing))
            return existing;

        Shader shader =
            Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Universal Render Pipeline/Simple Lit")
            ?? Shader.Find("Standard")
            ?? Shader.Find("Sprites/Default");
        Material material = new(shader) { name = $"Placeholder_{ColorUtility.ToHtmlStringRGB(color)}" };
        Texture2D texture = BuildMottledTexture(color);
        if (material.HasProperty("_BaseMap"))
            material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_MainTex"))
            material.SetTexture("_MainTex", texture);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", Color.white);

        cache[color] = material;
        return material;
    }

    /// <summary>
    /// A small tileable noise texture so a placeholder surface reads as material rather than flat
    /// plastic — Perlin-shaded brightness around the requested color, no external texture asset.
    /// </summary>
    private static Texture2D BuildMottledTexture(Color color)
    {
        const int size = 64;
        const float noiseScale = 6f;
        const float brightnessVariance = 0.12f;

        Texture2D texture = new(size, size, TextureFormat.RGBA32, mipChain: true)
        {
            name = $"PlaceholderTex_{ColorUtility.ToHtmlStringRGB(color)}",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
        };

        Color.RGBToHSV(color, out float hue, out float saturation, out float value);
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float noise = Mathf.PerlinNoise(
                    x / (float)size * noiseScale,
                    y / (float)size * noiseScale
                );
                float shadedValue = Mathf.Clamp01(
                    value + (noise - 0.5f) * 2f * brightnessVariance
                );
                Color shaded = Color.HSVToRGB(hue, saturation, shadedValue);
                shaded.a = color.a;
                pixels[y * size + x] = shaded;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    /// <summary>
    /// Scales a built placeholder so it stands <paramref name="targetWorldHeight"/> tall once it is
    /// parented under a root of <paramref name="rootScale"/>, while cancelling out any
    /// non-uniformity in that root scale.
    /// <para>
    /// Unit prefab roots in this project carry a deliberately non-uniform scale (x/z wider than y),
    /// which the hand-authored FBX models are drawn to compensate for. A procedurally built body has
    /// no such compensation baked in, so parenting it under that root squashes it — this puts the
    /// inverse of the root's own distortion back on the model.
    /// </para>
    /// </summary>
    public static void FitUnderPrefabRoot(
        GameObject model,
        Vector3 rootScale,
        float targetWorldHeight
    )
    {
        if (model == null || targetWorldHeight <= 0f)
            return;
        if (rootScale.x <= 0f || rootScale.y <= 0f || rootScale.z <= 0f)
            return;

        model.transform.localScale = Vector3.one;
        float intrinsicHeight = MeasureLocalHeight(model);
        if (intrinsicHeight <= 0.0001f)
            return;

        // One uniform factor applied to the model, expressed per-axis as the amount each axis must
        // contribute so that rootScale * localScale is the same on every axis.
        float uniform = targetWorldHeight / (intrinsicHeight * rootScale.y);
        model.transform.localScale = new Vector3(
            uniform * rootScale.y / rootScale.x,
            uniform,
            uniform * rootScale.y / rootScale.z
        );
    }

    private static float MeasureLocalHeight(GameObject model)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        bool any = false;
        Bounds bounds = default;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;
            if (!any)
            {
                bounds = renderer.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }
        return any ? bounds.size.y : 0f;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Saves every runtime-created material/texture under a built placeholder as real project
    /// assets and repoints each renderer at the saved copies. A material or texture that only ever
    /// existed in memory does not survive <c>PrefabUtility.SaveAsPrefabAsset</c> — the reference
    /// silently drops to null on the next load — so anything baked into a prefab must go through
    /// this first. Safe to call more than once; assets already saved under <paramref name="folder"/>
    /// are reused rather than duplicated.
    /// </summary>
    public static void PersistGeneratedMaterials(GameObject root, string folder)
    {
        if (root == null)
            return;
        if (!UnityEditor.AssetDatabase.IsValidFolder(folder))
        {
            string parent = System.IO.Path.GetDirectoryName(folder)?.Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(folder);
            if (!string.IsNullOrEmpty(parent) && !UnityEditor.AssetDatabase.IsValidFolder(parent))
                UnityEditor.AssetDatabase.CreateFolder(
                    System.IO.Path.GetDirectoryName(parent)?.Replace('\\', '/'),
                    System.IO.Path.GetFileName(parent)
                );
            UnityEditor.AssetDatabase.CreateFolder(parent, leaf);
        }

        Dictionary<Material, Material> persisted = new();
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material shared = renderer.sharedMaterial;
            if (shared == null)
                continue;
            if (UnityEditor.AssetDatabase.Contains(shared))
                continue; // already a real asset (e.g. reused across characters)

            if (!persisted.TryGetValue(shared, out Material savedMaterial))
            {
                Texture existingTexture = shared.HasProperty("_BaseMap")
                    ? shared.GetTexture("_BaseMap")
                    : null;
                if (existingTexture is Texture2D texture2D && !UnityEditor.AssetDatabase.Contains(texture2D))
                {
                    string texturePath = UnityEditor.AssetDatabase.GenerateUniqueAssetPath(
                        $"{folder}/{texture2D.name}.asset"
                    );
                    UnityEditor.AssetDatabase.CreateAsset(texture2D, texturePath);
                }

                string materialPath = UnityEditor.AssetDatabase.GenerateUniqueAssetPath(
                    $"{folder}/{shared.name}.mat"
                );
                UnityEditor.AssetDatabase.CreateAsset(shared, materialPath);
                savedMaterial = shared;
                persisted[shared] = savedMaterial;
            }

            renderer.sharedMaterial = savedMaterial;
        }

        UnityEditor.AssetDatabase.SaveAssets();
    }
#endif
}
