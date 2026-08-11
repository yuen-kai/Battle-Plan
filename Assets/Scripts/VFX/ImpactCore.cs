using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The impact frame: the single hardest-hitting instant of an ability, when the screen is briefly
/// dominated by the hit.
/// <para>
/// The flash is built as opaque material, not as light. This board's floor renders at luminance 183
/// of 255 and the scene's Neutral tonemapper resolves pure white to 217, which leaves an additive
/// layer here almost nowhere to go — measured against the floor, an additive wash a tenth as strong
/// as another is the same pixel. So the centre is an opaque disc written far above white, and the
/// falloff drops out of that into the ability's hue at full chroma. The hue only exists where the
/// white stops, and the whole silhouette terminates on geometry rather than on a gradient.
/// </para>
/// <para>
/// An impact gets one money frame, and a strip sampled at 30 Hz has to be unable to miss it. Every
/// layer is therefore posed at full strength on frame one and held there for <see cref="PeakHold"/>
/// before any of them is allowed to start collapsing.
/// </para>
/// <para>Purely local and visual. Call on every peer at the exact instant of impact.</para>
/// </summary>
public sealed class ImpactCore : MonoBehaviour
{
    private const string FlashShaderName = "BattlePlan/ImpactCoreBurst";

    // Radii in board cells, because cells are the only unit the frame is ever judged in: one cell
    // is 2.7 world units, and about 219 px at the distance these are filmed from. The predecessor
    // to this effect was sized by eye and landed at half a cell of readable flash.
    private const float WhiteRadiusCells = 0.96f;
    private const float BodyRadiusCells = 1.46f;
    private const float ShardBaseCells = 0.35f;
    private const float ShardTipMinCells = 1.6f;
    private const float ShardTipMaxCells = 2.05f;
    private const float WashRadiusCells = 1.95f;

    // Where the wash stops being solid ground cover and starts being spill, as a fraction of its
    // radius. Held out far enough that the ability's hue still measures saturated at 1.6 cells.
    private const float WashSolidFraction = 0.8f;

    // How far a rim is allowed off a true circle. A circle this clean reads as a printed mark
    // rather than as something that just went off, and the ramp rides the wobble with it, so the
    // blown-out centre ends up with the same irregular boundary the outline has.
    private const float BodyWobble = 0.1f;
    private const float WashWobble = 0.05f;

    // The caller passes roughly the hit radius in world units, and a standard hit passes this.
    private const float StandardScale = 2.6f;

    // Low chest height on a 1.6-1.8 unit model: high enough to read as an airburst over the
    // victim, low enough that it still lands on the cell it belongs to.
    private const float FlashHeight = 0.75f;
    private const float WashHeight = 0.07f;
    private const float BoardPitchDegrees = 73f;

    // The plateau every layer shares. A peak that lasts one 60 Hz tick is sampled on its decayed
    // frame more often than on its good one, and the panel that ships is then the wreckage rather
    // than the hit. Three consecutive 30 Hz samples have to land on a full-strength flash.
    private const float PeakHold = 0.105f;

    private const float LightLife = 0.185f;
    private const float TotalLife = 0.28f;

    private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
    private static readonly int MidColorId = Shader.PropertyToID("_MidColor");
    private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
    private static readonly int MidStartId = Shader.PropertyToID("_MidStart");
    private static readonly int MidEndId = Shader.PropertyToID("_MidEnd");
    private static readonly int FadeStartId = Shader.PropertyToID("_FadeStart");
    private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
    private static readonly int ZTestModeId = Shader.PropertyToID("_ZTestMode");

    /// <summary>
    /// One element of the flash. Silhouette and colour ramp are both stated on three beats — at
    /// spawn, at the end of the plateau, and at death — so a layer can hold its money frame and
    /// still be measurably different in every one of the frames it holds it for.
    /// </summary>
    private struct Layer
    {
        public Transform Transform;
        public Material Material;
        public float Diameter;
        public float ScaleFrom;
        public float ScaleHeld;
        public float ScaleTo;

        /// <summary>Where the white plateau ends, as a fraction of the layer's radius.</summary>
        public float RampFrom;
        public float RampHeld;
        public float RampTo;
        public float RampSpan;

        public float ShapeHold;

        /// <summary>
        /// Exponent on the collapse. Above one the layer eases off the plateau instead of falling
        /// off it, which keeps the frame after the hold from being a cliff of its own.
        /// </summary>
        public float CollapseShape;

        public float Opacity;
        public float FadeHold;
        public float Life;
    }

    private readonly List<Layer> layers = new();
    private readonly List<Mesh> meshes = new();
    private Transform burstPivot;
    private Light popLight;
    private float lightPeak;
    private float elapsed;

    /// <summary>
    /// Fires the core flash at <paramref name="position"/>.
    /// </summary>
    /// <param name="position">World position of the hit.</param>
    /// <param name="color">Ability colour, which owns everything outside the white centre.</param>
    /// <param name="scale">World-space scale of the flash, roughly its radius in metres.</param>
    /// <param name="intensity">Multiplier on the hit's weight; 1 is a standard hit.</param>
    public static void Spawn(Vector3 position, Color color, float scale = 2f, float intensity = 1f)
    {
        intensity = Mathf.Max(0f, intensity);
        if (intensity < 0.01f)
            return;
        scale = Mathf.Max(0.2f, scale);

        GameObject root = new("ImpactCore");
        root.transform.position = new Vector3(position.x, 0f, position.z);
        root.AddComponent<ImpactCore>().Build(position, color, scale, intensity);
    }

    private void Build(Vector3 position, Color color, float scale, float intensity)
    {
        Shader flashShader = Shader.Find(FlashShaderName);
        if (flashShader == null)
        {
            Debug.LogWarning($"[ImpactCore] {FlashShaderName} not found; no flash will be drawn");
            Destroy(gameObject);
            return;
        }

        // Brightness is pinned at the frame's ceiling either way, so a heavier hit buys area and a
        // wider blown-out centre rather than a higher number that the tonemapper would discard.
        float punch = Mathf.Clamp(intensity, 0.3f, 2.2f);
        float cell = GameLoop.cellSize > 0.01f ? GameLoop.cellSize : 2.7f;
        float reach =
            Mathf.Sqrt(Mathf.Clamp(scale / StandardScale, 0.2f, 6f)) * Mathf.Pow(punch, 0.32f);
        float unit = cell * reach;

        Color.RGBToHSV(color, out float hue, out float saturation, out _);
        if (saturation < 0.15f)
            Color.RGBToHSV(AbilityJuice.Alarm, out hue, out _, out _);

        burstPivot = new GameObject("Burst").transform;
        burstPivot.SetParent(transform, false);
        burstPivot.localPosition = new Vector3(0f, Mathf.Max(FlashHeight, position.y + 0.2f), 0f);
        burstPivot.rotation = ViewRotation();

        // The silhouette is seeded from where the hit landed rather than from the session clock, so
        // a re-shot capture of the same impact produces the same shape and two revisions can be
        // diffed frame for frame.
        Random.State callerState = Random.state;
        Random.InitState(
            Mathf.RoundToInt(position.x * 64f) * 73856093
                ^ Mathf.RoundToInt(position.z * 64f) * 19349663
        );
        BuildWash(flashShader, hue, unit);
        BuildShards(flashShader, hue, unit);
        BuildBody(flashShader, hue, unit, punch);
        Random.state = callerState;

        BuildLight(color, unit, punch);

        // A flash that fades up looks soft, and Spawn is called from a coroutine after this frame's
        // Update pass has already gone by. The peak is therefore posed here rather than waited for,
        // so the very first rendered frame is already the hardest one.
        ApplyFrame(0f);
    }

    /// <summary>
    /// The board repaint. A ground-plane disc that pushes every tile within about 1.6 cells into
    /// the ability's hue, so the flash is something the scene is standing in rather than a decal
    /// floating over it. Its outer shoulder is deliberately soft: this layer is light, not material.
    /// </summary>
    private void BuildWash(Shader shader, float hue, float unit)
    {
        Material material = NewMaterial(
            shader,
            "Wash",
            Stop(hue, 0.72f, 1f),
            Stop(hue, 0.72f, 1f),
            Stop(hue, 0.9f, 0.85f),
            fadeStart: WashSolidFraction,
            zTest: UnityEngine.Rendering.CompareFunction.LessEqual,
            renderQueue: 3000
        );

        GameObject host = new("Wash");
        host.transform.SetParent(transform, false);
        host.transform.localPosition = new Vector3(0f, WashHeight, 0f);
        host.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        host.AddComponent<MeshFilter>().sharedMesh = BuildDiscMesh("ImpactCoreWash", WashWobble);
        ConfigureRenderer(host.AddComponent<MeshRenderer>(), material);

        layers.Add(
            new Layer
            {
                Transform = host.transform,
                Material = material,
                Diameter = WashRadiusCells * 2f * unit,
                ScaleFrom = 1f,
                ScaleHeld = 1.1f,
                ScaleTo = 1.26f,
                RampFrom = 0.05f,
                RampHeld = 0.05f,
                RampTo = 0.05f,
                RampSpan = 0.35f,
                ShapeHold = PeakHold,
                CollapseShape = 0.7f,

                // Deliberately short of opaque: the deck's tile grid has to stay legible through
                // this, or it reads as a painted halo instead of as light pooling on the board.
                // Its chroma is held for the plateau too — the frame that carries the clipped
                // pixels has to be the frame that carries the colour.
                Opacity = 0.8f,
                FadeHold = PeakHold,
                Life = 0.26f,
            }
        );
    }

    /// <summary>
    /// Ejecta. Five to seven shards of uneven length, width and spacing that break the circle, each
    /// one asymmetric about its own axis. The unevenness is the whole point: a matched set of arms
    /// on the cardinals and diagonals reads as a lens flare rather than as something detonating.
    /// </summary>
    private void BuildShards(Shader shader, float hue, float unit)
    {
        Material material = NewMaterial(
            shader,
            "Shards",
            new Vector4(4f, 4f, 4f, 1f),
            Stop(hue, 0.55f, 1f),
            Stop(hue, 0.94f, 0.62f),
            fadeStart: 1f,
            zTest: UnityEngine.Rendering.CompareFunction.Always,
            renderQueue: 3045
        );

        GameObject host = new("Shards");
        host.transform.SetParent(burstPivot, false);
        host.AddComponent<MeshFilter>().sharedMesh = BuildShardMesh("ImpactCoreShards");
        ConfigureRenderer(host.AddComponent<MeshRenderer>(), material);

        layers.Add(
            new Layer
            {
                Transform = host.transform,
                Material = material,
                Diameter = ShardTipMaxCells * 2f * unit,
                ScaleFrom = 1f,
                ScaleHeld = 1.15f,
                ScaleTo = 1.28f,
                RampFrom = 0.62f,
                RampHeld = 0.58f,
                RampTo = 0.05f,
                RampSpan = 0.15f,
                ShapeHold = PeakHold,
                CollapseShape = 0.8f,
                Opacity = 1f,
                FadeHold = 0.115f,
                Life = 0.2f,
            }
        );
    }

    /// <summary>
    /// The centre, and the only thing in the frame that is allowed to be white. Opaque, written far
    /// above the buffer's ceiling so it resolves dead flat, and it never fades — it collapses. A
    /// white disc that dims drops off the ceiling on its second frame; one that shrinks stays
    /// blown out for its whole life and changes silhouette every frame instead.
    /// <para>
    /// The white's share of the radius is very nearly constant for the whole life, so the disc
    /// collapses into itself rather than hollowing out. Retracting the ramp while the mesh shrinks
    /// leaves the hue rim as the widest thing on screen and the panel reads as an eye — a blue iris
    /// around a white pupil — instead of as a flash. For the same reason the rim's darkest stop is
    /// held about eighty levels under the board rather than a hundred and forty: past that it
    /// carries more visual mass than the core it is supposed to be framing.
    /// </para>
    /// </summary>
    private void BuildBody(Shader shader, float hue, float unit, float punch)
    {
        Material material = NewMaterial(
            shader,
            "Body",
            new Vector4(6f, 6f, 6f, 1f),
            Stop(hue, 0.6f, 1f),
            Stop(hue, 0.62f, 0.85f),
            fadeStart: 1f,
            zTest: UnityEngine.Rendering.CompareFunction.Always,
            renderQueue: 3050
        );

        GameObject host = new("Body");
        host.transform.SetParent(burstPivot, false);
        host.AddComponent<MeshFilter>().sharedMesh = BuildDiscMesh("ImpactCoreBody", BodyWobble);
        ConfigureRenderer(host.AddComponent<MeshRenderer>(), material);

        float white = Mathf.Min(
            Mathf.Clamp(0.55f + 0.45f * punch, 0.4f, 1.15f) * WhiteRadiusCells / BodyRadiusCells,
            0.86f
        );

        layers.Add(
            new Layer
            {
                Transform = host.transform,
                Material = material,
                Diameter = BodyRadiusCells * 2f * unit,
                ScaleFrom = 1f,
                ScaleHeld = 0.96f,
                ScaleTo = 0.14f,
                RampFrom = white,
                RampHeld = white * 0.978f,
                RampTo = white * 0.955f,
                RampSpan = 0.05f,
                ShapeHold = PeakHold,
                CollapseShape = 1.35f,
                Opacity = 1f,
                FadeHold = 0.165f,
                Life = 0.205f,
            }
        );
    }

    /// <summary>
    /// A real point light, so surfaces the flash does not cover still take the hit. It carries most
    /// of the ability's chroma rather than washing to white: on a floor this bright, hue is the only
    /// thing a light here can still add once the value has run out of room.
    /// </summary>
    private void BuildLight(Color abilityColor, float unit, float punch)
    {
        GameObject lightObject = new("Pop");
        lightObject.transform.SetParent(transform, false);
        lightObject.transform.localPosition = new Vector3(0f, unit * 0.35f, 0f);

        popLight = lightObject.AddComponent<Light>();
        popLight.type = LightType.Point;
        popLight.color = Color.Lerp(abilityColor, Color.white, 0.3f);
        popLight.range = unit * 4.2f;
        popLight.shadows = LightShadows.None;
        lightPeak = 22f * punch;
        popLight.intensity = lightPeak;
    }

    private Material NewMaterial(
        Shader shader,
        string name,
        Vector4 core,
        Vector4 mid,
        Vector4 edge,
        float fadeStart,
        UnityEngine.Rendering.CompareFunction zTest,
        int renderQueue
    )
    {
        Material material = new(shader) { name = $"ImpactCore {name} (Runtime)" };
        material.SetVector(CoreColorId, core);
        material.SetVector(MidColorId, mid);
        material.SetVector(EdgeColorId, edge);
        material.SetFloat(FadeStartId, fadeStart);
        material.SetFloat(ZTestModeId, (int)zTest);
        material.renderQueue = renderQueue;
        return material;
    }

    private static void ConfigureRenderer(MeshRenderer meshRenderer, Material material)
    {
        if (meshRenderer == null)
            return;
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    private void LateUpdate()
    {
        if (burstPivot != null)
            burstPivot.rotation = ViewRotation();

        ApplyFrame(elapsed);
        elapsed += Time.deltaTime;

        if (elapsed >= TotalLife)
            Destroy(gameObject);
    }

    private void ApplyFrame(float time)
    {
        foreach (Layer layer in layers)
        {
            if (layer.Transform == null || layer.Material == null)
                continue;

            if (time >= layer.Life)
            {
                if (layer.Transform.gameObject.activeSelf)
                    layer.Transform.gameObject.SetActive(false);
                continue;
            }

            float scale = Plateau(time, layer, layer.ScaleFrom, layer.ScaleHeld, layer.ScaleTo);
            layer.Transform.localScale = Vector3.one * (layer.Diameter * scale);

            layer.Material.SetFloat(
                OpacityId,
                layer.Opacity * Falloff(time, layer.Life, layer.FadeHold)
            );

            float rampStart = Plateau(time, layer, layer.RampFrom, layer.RampHeld, layer.RampTo);
            layer.Material.SetFloat(MidStartId, rampStart);
            layer.Material.SetFloat(MidEndId, Mathf.Min(rampStart + layer.RampSpan, 0.97f));
        }

        if (popLight != null)
            popLight.intensity = lightPeak * Falloff(time, LightLife, PeakHold);
    }

    /// <summary>
    /// A quantity that drifts from <paramref name="from"/> to <paramref name="held"/> across the
    /// layer's plateau, then runs on to <paramref name="to"/> over whatever life is left. The
    /// drift is small on purpose: it is what keeps three held frames from being byte-identical
    /// without costing the hold any of the size it exists to protect.
    /// </summary>
    private static float Plateau(float time, Layer layer, float from, float held, float to)
    {
        float hold = layer.ShapeHold;
        if (time <= hold)
            return Mathf.Lerp(from, held, Mathf.Clamp01(time / Mathf.Max(0.0001f, hold)));

        float progress = Mathf.Clamp01((time - hold) / Mathf.Max(0.0001f, layer.Life - hold));
        return Mathf.Lerp(held, to, Mathf.Pow(progress, Mathf.Max(0.05f, layer.CollapseShape)));
    }

    /// <summary>Full strength for <paramref name="holdSeconds"/>, then a decaying tail to zero.</summary>
    /// <remarks>
    /// Opacity is the last thing allowed to move on the flash's core: alpha under one blends the
    /// written radiance back toward the board, and a white disc that does that stops clipping
    /// while it is still the largest thing on screen.
    /// </remarks>
    private static float Falloff(float time, float life, float holdSeconds)
    {
        if (time <= holdSeconds)
            return 1f;
        if (time >= life)
            return 0f;
        float progress = (time - holdSeconds) / Mathf.Max(0.0001f, life - holdSeconds);
        return (1f - progress) * (1f - progress);
    }

    /// <summary>
    /// A stop on the flash's colour ramp: the ability's own hue at a chosen chroma and value,
    /// handed over as literal linear radiance. Chroma is stated well above the palette's, because
    /// a pale tint of a team colour measures as grey once it is on this board.
    /// </summary>
    private static Vector4 Stop(float hue, float saturation, float value)
    {
        Color linear = Color.HSVToRGB(hue, saturation, value).linear;
        return new Vector4(linear.r, linear.g, linear.b, 1f);
    }

    /// <summary>
    /// A flat disc whose rim is pushed off a true circle by <paramref name="wobble"/>, and whose
    /// vertices carry their normalised radius in uv.x — which is all the ramp in the flash shader
    /// needs. uv.x stays on the clean fraction while the position takes the wobble, so the colour
    /// ramp stretches with the outline and the white centre inherits the same irregular boundary.
    /// Built at radius 0.5, so a layer's world diameter is its local scale.
    /// </summary>
    private Mesh BuildDiscMesh(string name, float wobble)
    {
        const int Segments = 96;
        float[] ringRadius = { 0.42f, 0.72f, 0.9f, 1f };
        float[] rim = BuildWobble(Segments, wobble);

        int rings = ringRadius.Length;
        Vector3[] vertices = new Vector3[1 + rings * Segments];
        Vector2[] uvs = new Vector2[vertices.Length];
        vertices[0] = Vector3.zero;
        uvs[0] = Vector2.zero;

        for (int ring = 0; ring < rings; ring++)
        {
            for (int segment = 0; segment < Segments; segment++)
            {
                float angle = segment / (float)Segments * Mathf.PI * 2f;
                Place(
                    vertices,
                    uvs,
                    1 + ring * Segments + segment,
                    angle,
                    ringRadius[ring] * rim[segment],
                    ringRadius[ring]
                );
            }
        }

        List<int> triangles = new(Segments * rings * 6);
        for (int segment = 0; segment < Segments; segment++)
        {
            int next = (segment + 1) % Segments;
            triangles.Add(0);
            triangles.Add(1 + segment);
            triangles.Add(1 + next);

            for (int ring = 0; ring < rings - 1; ring++)
            {
                int inner = 1 + ring * Segments;
                int outer = 1 + (ring + 1) * Segments;
                triangles.Add(inner + segment);
                triangles.Add(outer + segment);
                triangles.Add(inner + next);
                triangles.Add(inner + next);
                triangles.Add(outer + segment);
                triangles.Add(outer + next);
            }
        }

        return Finish(name, vertices, uvs, triangles);
    }

    /// <summary>
    /// Per-segment radius multipliers for a rim: three harmonics at random phase, normalised so the
    /// rim never strays further than <paramref name="amount"/> off the circle it started as.
    /// </summary>
    private static float[] BuildWobble(int segments, float amount)
    {
        float[] rim = new float[segments];
        if (amount <= 0.0001f)
        {
            for (int segment = 0; segment < segments; segment++)
                rim[segment] = 1f;
            return rim;
        }

        int slow = Random.Range(2, 4);
        int middle = Random.Range(4, 7);
        int fast = Random.Range(8, 12);
        float slowPhase = Random.Range(0f, Mathf.PI * 2f);
        float middlePhase = Random.Range(0f, Mathf.PI * 2f);
        float fastPhase = Random.Range(0f, Mathf.PI * 2f);

        for (int segment = 0; segment < segments; segment++)
        {
            float angle = segment / (float)segments * Mathf.PI * 2f;
            float shape =
                Mathf.Sin(angle * slow + slowPhase)
                + 0.55f * Mathf.Sin(angle * middle + middlePhase)
                + 0.28f * Mathf.Sin(angle * fast + fastPhase);
            rim[segment] = 1f + amount * shape / 1.83f;
        }
        return rim;
    }

    /// <summary>
    /// The shards. Count, spacing, length, width and tip skew are all drawn independently, and each
    /// shard's two flanks are cut to different fractions so it is not symmetric about its own axis
    /// either. Lengths come off a hierarchy rather than a distribution — one dominant, two mid, the
    /// rest short — so the set clears the body at a spread of about three to one.
    /// </summary>
    private Mesh BuildShardMesh(string name)
    {
        int count = Random.Range(5, 8);
        float baseFraction = ShardBaseCells / ShardTipMaxCells;
        float shortFraction = ShardTipMinCells / ShardTipMaxCells;

        float[] angles = new float[count];
        float[] steps = new float[count];
        float total = 0f;
        for (int shard = 0; shard < count; shard++)
        {
            steps[shard] = Random.Range(0.55f, 1.65f);
            total += steps[shard];
        }

        float walk = Random.Range(0f, Mathf.PI * 2f);
        for (int shard = 0; shard < count; shard++)
        {
            angles[shard] = walk;
            walk += steps[shard] / total * Mathf.PI * 2f;
        }

        // A pair sitting opposite each other is the one arrangement that reads as an optical
        // artefact rather than as ejecta, so any near-opposite pair gets pushed off the axis.
        for (int first = 0; first < count; first++)
        {
            for (int second = first + 1; second < count; second++)
            {
                float apart = Mathf.Abs(
                    Mathf.DeltaAngle(angles[first] * Mathf.Rad2Deg, angles[second] * Mathf.Rad2Deg)
                );
                if (apart > 172f)
                    angles[second] += 15f * Mathf.Deg2Rad;
            }
        }

        float[] reach = new float[count];
        reach[0] = 1f;
        reach[1] = Mathf.Lerp(shortFraction, 1f, Random.Range(0.55f, 0.72f));
        reach[2] = Mathf.Lerp(shortFraction, 1f, Random.Range(0.28f, 0.44f));
        for (int shard = 3; shard < count; shard++)
            reach[shard] = Mathf.Lerp(shortFraction, 1f, Random.Range(0f, 0.1f));

        for (int shard = count - 1; shard > 0; shard--)
        {
            int swap = Random.Range(0, shard + 1);
            (reach[shard], reach[swap]) = (reach[swap], reach[shard]);
        }

        Vector3[] vertices = new Vector3[count * 7];
        Vector2[] uvs = new Vector2[vertices.Length];
        List<int> triangles = new(count * 15);

        for (int shard = 0; shard < count; shard++)
        {
            float angle = angles[shard];
            float tip = reach[shard];
            float span = tip - baseFraction;

            // Width tracks length rather than being drawn beside it, so the dominant shard is also
            // the chunkiest and the short ones read as slivers. Drawing the two independently let
            // the set come out at a near-uniform width often enough to read as a printed star.
            float weight = Mathf.InverseLerp(shortFraction, 1f, tip);
            float leftWidth = Mathf.Lerp(0.085f, 0.24f, weight) * Random.Range(0.8f, 1.25f);
            float rightWidth = leftWidth * Random.Range(0.42f, 0.92f);
            float skew = Random.Range(-0.4f, 0.4f) * Mathf.Min(leftWidth, rightWidth);

            // The taper is held off until three quarters of the way out, so what clears the body is
            // a wedge with mass rather than the needle a base-to-tip triangle would leave.
            int first = shard * 7;
            Place(vertices, uvs, first, angle - leftWidth, baseFraction);
            Place(vertices, uvs, first + 1, angle + rightWidth, baseFraction);
            Place(vertices, uvs, first + 2, angle - leftWidth * 1.05f, baseFraction + span * 0.42f);
            Place(vertices, uvs, first + 3, angle + rightWidth * 0.8f, baseFraction + span * 0.34f);
            Place(vertices, uvs, first + 4, angle - leftWidth * 0.5f, baseFraction + span * 0.76f);
            Place(vertices, uvs, first + 5, angle + rightWidth * 0.36f, baseFraction + span * 0.68f);
            Place(vertices, uvs, first + 6, angle + skew, tip);

            AddTriangle(triangles, first, first + 1, first + 3);
            AddTriangle(triangles, first, first + 3, first + 2);
            AddTriangle(triangles, first + 2, first + 3, first + 5);
            AddTriangle(triangles, first + 2, first + 5, first + 4);
            AddTriangle(triangles, first + 4, first + 5, first + 6);
        }

        return Finish(name, vertices, uvs, triangles);
    }

    private static void AddTriangle(List<int> triangles, int a, int b, int c)
    {
        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(c);
    }

    /// <summary>A vertex whose ramp coordinate is simply where it sits.</summary>
    private static void Place(
        Vector3[] vertices,
        Vector2[] uvs,
        int index,
        float angle,
        float radius
    )
    {
        Place(vertices, uvs, index, angle, radius, radius);
    }

    /// <summary>
    /// Positions a rim vertex at <paramref name="radius"/> while handing the ramp
    /// <paramref name="rampCoordinate"/>, which lets a wobbled outline keep a clean colour ramp.
    /// </summary>
    private static void Place(
        Vector3[] vertices,
        Vector2[] uvs,
        int index,
        float angle,
        float radius,
        float rampCoordinate
    )
    {
        vertices[index] = new Vector3(
            Mathf.Cos(angle) * radius * 0.5f,
            Mathf.Sin(angle) * radius * 0.5f,
            0f
        );
        uvs[index] = new Vector2(rampCoordinate, 0f);
    }

    private Mesh Finish(string name, Vector3[] vertices, Vector2[] uvs, List<int> triangles)
    {
        Mesh mesh = new() { name = name };
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateBounds();
        meshes.Add(mesh);
        return mesh;
    }

    private static Quaternion ViewRotation()
    {
        Camera viewCamera = GameLoop.Instance != null ? GameLoop.Instance.TeamCamera : null;
        if (viewCamera == null)
            viewCamera = Camera.main;
        return viewCamera != null
            ? viewCamera.transform.rotation
            : Quaternion.Euler(BoardPitchDegrees, 0f, 0f);
    }

    private void OnDestroy()
    {
        foreach (Layer layer in layers)
        {
            if (layer.Material != null)
                Destroy(layer.Material);
        }
        layers.Clear();

        foreach (Mesh mesh in meshes)
        {
            if (mesh != null)
                Destroy(mesh);
        }
        meshes.Clear();
    }
}
