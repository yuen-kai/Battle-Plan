using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Local smoke-screen presentation: camera-facing masses over smoked cells, thinned where the
/// local crew can see into a cell. Occupant hiding for opaque cells lives on <see cref="GameLoop"/>.
/// </summary>
public sealed class SmokeScreenVisual : MonoBehaviour
{
    private static readonly Layer[] Layers =
    {
        new(diameterCells: 2.05f, height: 0.52f, opacity: 0.95f, seen: 0.34f, lobes: 4),
        new(diameterCells: 1.55f, height: 1.18f, opacity: 0.88f, seen: 0.26f, lobes: 3),
        new(diameterCells: 1.85f, height: 0.86f, opacity: 0.95f, seen: 0.06f, lobes: 4, push: VeilPush),
    };

    private const float VeilPush = 1.95f;

    private readonly struct Layer
    {
        public readonly float DiameterCells;
        public readonly float Height;
        public readonly float Opacity;
        public readonly float Seen;
        public readonly int Lobes;
        public readonly float Push;

        public Layer(
            float diameterCells,
            float height,
            float opacity,
            float seen,
            int lobes,
            float push = 0f
        )
        {
            DiameterCells = diameterCells;
            Height = height;
            Opacity = opacity;
            Seen = seen;
            Lobes = lobes;
            Push = push;
        }
    }

    private const float PuffJitterCells = 0.17f;
    private const float PaintedSpan = 0.83f;
    private const float StainHeight = 0.24f;
    private const float StainCells = 1f;
    private static readonly Color StainColor = new(0.16f, 0.185f, 0.205f, 0.34f);
    private static readonly Color PuffLit = new(0.815f, 0.845f, 0.865f);
    private static readonly Color PuffShadow = new(0.315f, 0.345f, 0.375f);

    private const float DeploySeconds = 0.55f;
    private const float DeployStagger = 0.05f;
    private const float DeployRise = 0.4f;
    private const float DeployStartScale = 0.42f;
    private const float BobHeight = 0.075f;
    private const float BobSpeed = 0.42f;
    private const float BreatheAmount = 0.035f;
    private const float BreatheSpeed = 0.36f;
    private const float SeenFadeSeconds = 0.35f;

    private static readonly int LitColorId = Shader.PropertyToID("_LitColor");
    private static readonly int ShadowColorId = Shader.PropertyToID("_ShadowColor");
    private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
    private static readonly int ErodeId = Shader.PropertyToID("_Erode");
    private static readonly int TearId = Shader.PropertyToID("_Tear");
    private static readonly int EdgeWidthId = Shader.PropertyToID("_EdgeWidth");
    private static readonly int LobeCountId = Shader.PropertyToID("_LobeCount");
    private static readonly int SpreadId = Shader.PropertyToID("_Spread");
    private static readonly int MinLobeId = Shader.PropertyToID("_MinLobe");
    private static readonly int MaxLobeId = Shader.PropertyToID("_MaxLobe");
    private static readonly int KnitId = Shader.PropertyToID("_Knit");
    private static readonly int WarpId = Shader.PropertyToID("_Warp");
    private static readonly int WarpScaleId = Shader.PropertyToID("_WarpScale");
    private static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");
    private static readonly int MottleId = Shader.PropertyToID("_Mottle");
    private static readonly int ShadeLowId = Shader.PropertyToID("_ShadeLow");
    private static readonly int ShadeSpanId = Shader.PropertyToID("_ShadeSpan");
    private static readonly int SweepId = Shader.PropertyToID("_Sweep");
    private static readonly int LobeWeightId = Shader.PropertyToID("_LobeWeight");
    private static readonly int CreaseId = Shader.PropertyToID("_Crease");
    private static readonly int RimId = Shader.PropertyToID("_Rim");
    private static readonly int LightAngleId = Shader.PropertyToID("_LightAngle");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");

    private readonly List<Puff> puffs = new();
    private readonly List<Material> ownedMaterials = new();
    private Material stainMaterial;
    private float bornTime;

    private struct Puff
    {
        public Transform Transform;
        public Material Material;
        public Vector3 Anchor;
        public Vector2Int Cell;
        public float Size;
        public float Opacity;
        public float OpacitySeen;
        public float Push;
        public float Delay;
        public float Phase;
        public float Roll;
        public float SeenTarget;
        public float SeenWeight;
    }

    public static SmokeScreenVisual Create(
        Transform parent,
        IReadOnlyList<Vector3> cellWorldPositions,
        float cellSize
    )
    {
        GameObject root = new("SmokeScreenVisuals");
        root.transform.SetParent(parent, true);
        SmokeScreenVisual visual = root.AddComponent<SmokeScreenVisual>();
        visual.Build(cellWorldPositions, cellSize);
        return visual;
    }

    private void Build(IReadOnlyList<Vector3> cellWorldPositions, float cellSize)
    {
        bornTime = Time.time;

        Shader sprite = Shader.Find("Sprites/Default");
        stainMaterial = new Material(sprite) { name = "Smoke Stain (Runtime)", color = StainColor };

        Shader smoke = Shader.Find("BattlePlan/AftermathSmoke");
        if (smoke == null)
            Debug.LogWarning("[SmokeScreen] BattlePlan/AftermathSmoke missing; bank will be flat.");

        if (cellWorldPositions == null)
            return;

        foreach (Vector3 cellWorldPosition in cellWorldPositions)
        {
            CreateQuad(
                "SmokeStain",
                cellWorldPosition + Vector3.up * StainHeight,
                Quaternion.Euler(90f, 0f, 0f),
                cellSize * StainCells,
                stainMaterial
            );

            Vector2Int cell = GridSystem.ConvertToGridCoords(cellWorldPosition);
            Random.State callerState = Random.state;
            Random.InitState(cell.x * 73856093 ^ cell.y * 19349663);
            for (int layer = 0; layer < Layers.Length; layer++)
                CreatePuff(cellWorldPosition, cell, cellSize, layer, smoke);
            Random.state = callerState;
        }

        Pose(0f);
    }

    private void CreatePuff(
        Vector3 cellWorldPosition,
        Vector2Int cell,
        float cellSize,
        int index,
        Shader smoke
    )
    {
        Layer layer = Layers[index];
        Vector3 anchor =
            cellWorldPosition
            + new Vector3(
                Random.Range(-PuffJitterCells, PuffJitterCells) * cellSize,
                layer.Height + Random.Range(-0.08f, 0.08f),
                Random.Range(-PuffJitterCells, PuffJitterCells) * cellSize
            );

        float diameter = layer.DiameterCells * cellSize * Random.Range(0.96f, 1.04f);
        Material material = smoke != null ? new Material(smoke) { name = "Smoke Mass (Runtime)" } : stainMaterial;
        if (smoke != null)
        {
            ownedMaterials.Add(material);
            Dress(material, diameter, layer.Lobes);
        }

        Transform puff = CreateQuad("SmokeMass", anchor, Quaternion.identity, diameter, material);

        puffs.Add(
            new Puff
            {
                Transform = puff,
                Material = smoke != null ? material : null,
                Anchor = anchor,
                Cell = cell,
                Size = diameter / PaintedSpan,
                Opacity = layer.Opacity,
                OpacitySeen = layer.Seen,
                Push = layer.Push,
                Delay = index * DeployStagger + Random.Range(0f, DeployStagger),
                Phase = Random.value * Mathf.PI * 2f,
                Roll = Random.value * 360f,
            }
        );
    }

    public void SetSeenCells(ICollection<Vector2Int> visibleCells)
    {
        for (int i = 0; i < puffs.Count; i++)
        {
            Puff puff = puffs[i];
            puff.SeenTarget =
                visibleCells != null && visibleCells.Contains(puff.Cell) ? 1f : 0f;
            puffs[i] = puff;
        }
    }

    private static void Dress(Material material, float diameter, int lobes)
    {
        material.SetColor(LitColorId, PuffLit);
        material.SetColor(ShadowColorId, PuffShadow);
        material.SetFloat(OpacityId, 0f);
        material.SetFloat(ErodeId, 0f);
        material.SetFloat(TearId, 0.015f);
        material.SetFloat(EdgeWidthId, Mathf.Clamp(0.09f / Mathf.Max(0.35f, diameter), 0.015f, 0.14f));
        material.SetFloat(LobeCountId, lobes);
        material.SetFloat(SpreadId, 0.47f);
        material.SetFloat(MinLobeId, 0.19f);
        material.SetFloat(MaxLobeId, 0.36f);
        material.SetFloat(KnitId, Random.Range(0.17f, 0.21f));
        material.SetFloat(WarpId, Random.Range(0.02f, 0.035f));
        material.SetFloat(WarpScaleId, 2f);
        material.SetFloat(NoiseScaleId, Random.Range(2.2f, 3f));
        material.SetFloat(MottleId, Random.Range(0.03f, 0.07f));
        material.SetFloat(ShadeLowId, Random.Range(0.50f, 0.54f));
        material.SetFloat(ShadeSpanId, Random.Range(0.09f, 0.13f));
        material.SetFloat(SweepId, Random.Range(1.1f, 1.3f));
        material.SetFloat(LobeWeightId, Random.Range(0.5f, 0.58f));
        material.SetFloat(CreaseId, Random.Range(0.66f, 0.76f));
        material.SetFloat(RimId, Random.Range(0.86f, 0.92f));
        material.SetFloat(SeedId, Random.Range(0f, 30f));
    }

    private Transform CreateQuad(
        string name,
        Vector3 position,
        Quaternion rotation,
        float size,
        Material material
    )
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        quad.layer = 0;
        quad.transform.SetParent(transform, true);
        quad.transform.SetPositionAndRotation(position, rotation);
        quad.transform.localScale = Vector3.one * size;

        Collider quadCollider = quad.GetComponent<Collider>();
        if (quadCollider != null)
        {
            quadCollider.enabled = false;
            Destroy(quadCollider);
        }

        Renderer quadRenderer = quad.GetComponent<Renderer>();
        quadRenderer.sharedMaterial = material;
        quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        quadRenderer.receiveShadows = false;
        return quad.transform;
    }

    private void LateUpdate() => Pose(Time.time - bornTime);

    private void Pose(float age)
    {
        Camera viewCamera = GameLoop.ViewCamera;
        if (viewCamera == null)
            return;

        Quaternion facing = viewCamera.transform.rotation;
        Vector3 eye = viewCamera.transform.position;
        Vector3 viewForward = facing * Vector3.forward;
        float time = Time.time;
        float seenStep = Time.deltaTime / SeenFadeSeconds;

        for (int i = 0; i < puffs.Count; i++)
        {
            Puff puff = puffs[i];
            if (puff.Transform == null)
                continue;

            puff.SeenWeight = Mathf.MoveTowards(puff.SeenWeight, puff.SeenTarget, seenStep);
            puffs[i] = puff;

            float rise = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01((age - puff.Delay) / DeploySeconds)
            );
            float bob = Mathf.Sin(time * BobSpeed + puff.Phase) * BobHeight;
            Vector3 place = puff.Anchor + Vector3.up * (bob - DeployRise * (1f - rise));

            float shrink = 1f;
            if (!Mathf.Approximately(puff.Push, 0f))
            {
                float depth = Vector3.Dot(place - eye, viewForward);
                if (depth > puff.Push + 0.5f)
                {
                    place -= viewForward * puff.Push;
                    shrink = (depth - puff.Push) / depth;
                }
            }

            puff.Transform.SetPositionAndRotation(
                place,
                facing * Quaternion.AngleAxis(puff.Roll, Vector3.forward)
            );
            float breathe = 1f + Mathf.Sin(time * BreatheSpeed + puff.Phase * 1.7f) * BreatheAmount;
            puff.Transform.localScale =
                Vector3.one
                * (puff.Size * Mathf.Lerp(DeployStartScale, 1f, rise) * breathe * shrink);

            if (puff.Material == null)
                continue;

            float opacity = Mathf.Lerp(puff.Opacity, puff.OpacitySeen, puff.SeenWeight);
            puff.Material.SetFloat(OpacityId, opacity * rise);
            puff.Material.SetFloat(LightAngleId, -puff.Roll * Mathf.Deg2Rad);
        }
    }

    private void OnDestroy()
    {
        if (stainMaterial != null)
            Destroy(stainMaterial);
        foreach (Material material in ownedMaterials)
        {
            if (material != null)
                Destroy(material);
        }
    }
}
