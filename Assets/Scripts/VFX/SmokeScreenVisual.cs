using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The visible body of a deployed smoke screen: a bank of drifting, camera-facing masses standing
/// over the cells it covers, on a ground stain that keeps the exact footprint readable from
/// straight above. Purely local and non-interactive — the server owns which cells are smoked and
/// what they block, and this only has to make them impossible to miss. A screen drawn as a faint
/// tint on the floor gets walked into, because nothing about it says the sightline is gone.
///
/// <para>
/// The masses are drawn with <c>BattlePlan/AftermathSmoke</c>, which is the same shader every
/// plume an impact throws up is made of. This used to paint radial gradients on
/// <c>Sprites/Default</c>: unlit, so no mass in the bank had a lit side or a shadow side, and
/// keyed brighter than the deck, so instead of standing on the board it washed over it like fog on
/// the lens. Borrowing the shader buys a torn silhouette, a hard two-pixel edge and per-lobe
/// shading for nothing, and it means the one screen that stays on the board for rounds is made of
/// the same material as the smoke that only visits for a second.
/// </para>
/// </summary>
public sealed class SmokeScreenVisual : MonoBehaviour
{
    // Two masses per cell, and both of them big. Three passes at this taught the actual lesson:
    // what makes a bank look frantic is not how noisy one mass is, it is how many bumps end up in
    // the silhouette. Nine cells times three masses times five lobes put well over a hundred
    // bumps inside eight units of cloud, and at that density every one of them is small, so the
    // outline turns to gravel however cleanly each one is shaded. Eighteen masses this wide leave
    // maybe a dozen bumps around the rim, which is what a drawn cloud has.
    //
    // They are nearly opaque, which is the other half of looking drawn. At half alpha every mass
    // showed its own outline through the ones in front of it and the bank read as a stack of
    // translucent eggs; a cartoon cloud is one silhouette with bumps on it, and the only way to
    // get one silhouette out of twenty-seven quads is to let the front ones cover the back ones.
    // Keeping the crews readable inside it is the veil's job, not this one's.
    //
    // Every mass carries two alphas, and which one it is drawn at depends on whether the local
    // crew can see into the cell it belongs to. The rules and the picture were disagreeing, and
    // the rules are right: smoke blocks a line that crosses it and never one that starts or ends
    // in it, so a cell your crew has an unblocked look at is a cell you are entitled to read, and
    // an opaque cloud over it was the visual overruling the game.
    //
    // The depth push is what lets the two halves of "seen" be tuned apart. The body sits behind
    // anyone standing in the footprint and the veil sits in front of him, so the veil alone
    // decides whether he can be made out and the body alone decides how much of the deck and the
    // board behind still reads. Both open on a seen cell, and the veil opens much further: a cell
    // you have sight of should show you what is standing in it plainly, not as a shape guessed at
    // through haze.
    //
    // What this must never become is thin everywhere. That was the bug, and it had two causes,
    // both fixed: fogless matches used to hand the screen all nine of its own cells as seen, and
    // on a board where your crew stands around its own smoke a great many cells are genuinely
    // seen. The first is gone; the second is the honest answer and is what the unseen alphas above
    // are for, because the far side of a screen is what the enemy is behind.
    //
    // The heights are deliberately close together. The board is watched from 73 degrees, where a
    // camera-facing quad lies almost flat and a mass a metre above another projects clear of it
    // instead of over it: stacked on a plume's spacing, this bank read as shelves of cloud hanging
    // in the air with daylight between them. From up there a screen is read by how much of its
    // nine cells it covers, so the masses are spent across the footprint and only the last of them
    // goes upward, to give the bank a top when the camera comes down.
    private static readonly Layer[] Layers =
    {
        new(diameterCells: 2.05f, height: 0.52f, opacity: 0.95f, seen: 0.34f, lobes: 4),
        new(diameterCells: 1.55f, height: 1.18f, opacity: 0.88f, seen: 0.26f, lobes: 3),
        // The veil. See VeilPush: the one mass per cell that rides in front of a unit rather than
        // behind him, and therefore the only one that decides whether he can be made out at all.
        // Its two values are the whole rule. Over a cell nobody can see into it closes like the
        // rest of the bank, because a cell you have no sight of must give nothing away. Over a
        // cell your crew has a look at it drops to a wash, because there the fog has already
        // decided you may read what is standing there and the cloud must not overrule it.
        new(diameterCells: 1.85f, height: 0.86f, opacity: 0.95f, seen: 0.06f, lobes: 4, push: VeilPush),
    };

    /// <summary>
    /// How far the veil masses are moved along the view axis, toward the camera.
    ///
    /// <para>
    /// The board is watched from 73 degrees, and at that pitch height is very nearly distance: a
    /// point 1.8 up is 1.72 nearer the lens than the deck under it. A unit standing inside the
    /// screen is therefore in front of every mass anchored at its own cell, so the bank covered it
    /// from the waist down and its head and shoulders sat on top of the cloud like a sticker. The
    /// masses cannot simply be raised — the bank would leave the ground — so one per cell is
    /// pushed along the ray it is already seen down. Screen position and size are unchanged
    /// because the scale is compensated for the shorter throw; the only thing that moves is where
    /// it lands in the depth buffer.
    /// </para>
    /// <para>
    /// The value is bounded at both ends. It has to beat the 1.72 a standing unit gains from its
    /// own height, and it has to stay under the roughly 2.5 that a unit one cell nearer the camera
    /// gains from its height plus its cell, or the screen would start covering people standing in
    /// front of it.
    /// </para>
    /// <para>
    /// The two obvious alternatives are dead ends, and both were tried. Opening the body up does
    /// not work: a unit in the footprint has masses from several cells between him and the lens,
    /// so even at 0.7 apiece they compound past 0.9 and he is still gone, and the bank has paid
    /// for it by going see-through and showing every one of its own outlines again. Pushing the
    /// body the other way, behind the crews, works on paper and fails on the board — at this pitch
    /// a unit down the view axis is nearly a unit of height, so a mass shoved back far enough to
    /// clear a standing crew member has gone under the deck, and the floor is opaque and does
    /// write depth, so it clips straight through the cloud.
    /// </para>
    /// </summary>
    private const float VeilPush = 1.95f;

    private readonly struct Layer
    {
        public readonly float DiameterCells;
        public readonly float Height;

        /// <summary>What the mass is worth over a cell the local crew has no sight of.</summary>
        public readonly float Opacity;

        /// <summary>And what it drops to once they can see into that cell.</summary>
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

    // The shader paints its lobes across roughly this much of the quad, so a mass asked for a
    // diameter has to be given a quad wider than one. Same figure Aftermath sizes its plume on.
    private const float PaintedSpan = 0.83f;

    // Clear of the move-range overlay's outline quad at 0.227 and below the ability target markers
    // at 0.26, so the stain neither z-fights the board nor buries a marker.
    private const float StainHeight = 0.24f;

    // Exactly one cell. Under a cell the deck's own grid shows between the nine tiles and the
    // footprint reads as tiling; over it the overlaps double-blend and draw that same grid back in
    // darker.
    private const float StainCells = 1f;

    // Darker than the deck it lies on, because a bank this size shades the ground under it. The
    // previous stain was within a few percent of the floor's own value and read as a pale tile
    // rather than as anything standing over the cell.
    private static readonly Color StainColor = new(0.16f, 0.185f, 0.205f, 0.34f);

    // A cold screen, not a burn. Aftermath's plume is warm and goes nearly black in shadow, which
    // is right for something a fire made and wrong for a canister — and the two must not be
    // confused, because on this board one of them means a unit just took damage there. The
    // terminator is kept but the whole range is lifted, so the bank turns over lobe by lobe while
    // still reading as the pale grey a smoke round lays down.
    private static readonly Color PuffLit = new(0.815f, 0.845f, 0.865f);
    private static readonly Color PuffShadow = new(0.315f, 0.345f, 0.375f);

    // The bank boils up rather than appearing. Short enough that it is standing before the round
    // resolves, long enough to read as something being released.
    private const float DeploySeconds = 0.55f;
    private const float DeployStagger = 0.05f;
    private const float DeployRise = 0.4f;
    private const float DeployStartScale = 0.42f;

    // Enough motion to read as a cloud holding its ground, not as drifting away from the cells it
    // is denying — the footprint is exact and the visual may not suggest otherwise.
    //
    // The masses breathe rather than churn. Walking the shader's erosion in and out crawls the rim
    // and turning the quads spins the lobes on the spot; both put movement into the detail, which
    // is the part that was making this restless. A slow swell of the whole shape is the motion a
    // drawn cloud gets, and it leaves the silhouette alone.
    private const float BobHeight = 0.075f;
    private const float BobSpeed = 0.42f;
    private const float BreatheAmount = 0.035f;
    private const float BreatheSpeed = 0.36f;

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

    /// <summary>Long enough that a unit walking into sight does not snap the cloud open.</summary>
    private const float SeenFadeSeconds = 0.35f;

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

            // A second canister landing elsewhere rebuilds the whole screen, so the jitter is
            // seeded from the cell: a screen already standing redraws identically instead of
            // reshuffling itself the moment its neighbour deploys.
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

    /// <summary>
    /// Hands the screen the cells the local crew can currently see into. Called from the client fog
    /// pass with the very set the fog overlay is drawn from, so the cloud and the dark tiles can
    /// never disagree about where sight ends. Null or empty means nothing is seen and the bank
    /// stands at full strength.
    /// </summary>
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

    /// <summary>
    /// The per-mass settings. These are Aftermath's numbers pulled a long way toward a drawing.
    ///
    /// <para>
    /// A plume from an explosion wants to look like something violent happened, so it is mottled,
    /// finely noised and torn at the rim, and it earns that in the second it is on screen. A smoke
    /// screen stands on the board for whole rounds and is a piece of the interface as much as a
    /// piece of the world: it has to say "you cannot see through here" at a glance and then stop
    /// asking for attention. So the density speckle comes almost all the way off, the outline stops
    /// wobbling, the lobes knit into each other instead of clustering, and the light-to-shadow
    /// crossing is narrowed until it reads as two tones with an edge between them rather than as a
    /// gradient. What is left is a shape rather than a texture.
    /// </para>
    /// </summary>
    private static void Dress(Material material, float diameter, int lobes)
    {
        material.SetColor(LitColorId, PuffLit);
        material.SetColor(ShadowColorId, PuffShadow);
        material.SetFloat(OpacityId, 0f);
        material.SetFloat(ErodeId, 0f);
        // Barely any: the rim is drawn, not ripped.
        material.SetFloat(TearId, 0.015f);
        // Roughly a three-pixel silhouette whatever the mass is worth on screen: a hard outline is
        // most of what separates material from haze, and all of what makes this read as drawn.
        material.SetFloat(EdgeWidthId, Mathf.Clamp(0.09f / Mathf.Max(0.35f, diameter), 0.015f, 0.14f));
        material.SetFloat(LobeCountId, lobes);
        // The bumps stay. Simplifying the outline as well as the shading was a mistake worth
        // recording: a mass made of one fat lobe is an egg, and twenty-seven opaque eggs are a
        // pile of eggs. What makes a drawn cloud is a lumpy edge around a simply shaded interior,
        // so the lobes spread out far enough to break the rim and only knit enough to stop reading
        // as separate balls.
        material.SetFloat(SpreadId, 0.47f);
        material.SetFloat(MinLobeId, 0.19f);
        material.SetFloat(MaxLobeId, 0.36f);
        material.SetFloat(KnitId, Random.Range(0.17f, 0.21f));
        material.SetFloat(WarpId, Random.Range(0.02f, 0.035f));
        material.SetFloat(WarpScaleId, 2f);
        // Coarse enough that the little variation left is a soft swell across a whole lobe rather
        // than grain inside it.
        material.SetFloat(NoiseScaleId, Random.Range(2.2f, 3f));
        material.SetFloat(MottleId, Random.Range(0.03f, 0.07f));
        // The terminator is the cartoon part: a narrow span turns the sphere shading into a lit
        // face and a shadow face meeting on a line, which is how a drawn cloud is shaded.
        material.SetFloat(ShadeLowId, Random.Range(0.50f, 0.54f));
        material.SetFloat(ShadeSpanId, Random.Range(0.09f, 0.13f));
        material.SetFloat(SweepId, Random.Range(1.1f, 1.3f));
        // Weighted toward the sweep across the whole mass rather than the per-lobe spheres. With
        // every bump turning over on its own the bank had as many light-and-shadow pairs as it had
        // bumps, and that reads as clutter even when each pair is clean. This gives the cloud one
        // lit side and one shadow side, with just enough per-lobe roundness left to keep the bumps
        // from flattening into a cut-out.
        material.SetFloat(LobeWeightId, Random.Range(0.5f, 0.58f));
        // Creases and rims stay light. Dark seams between the lobes are the detail that made this
        // busy, and a drawn cloud does not have them.
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
            // Board picks raycast the Grid layer, but a live collider here would still swallow
            // clicks meant for the cells underneath.
            quadCollider.enabled = false;
            Destroy(quadCollider);
        }

        Renderer quadRenderer = quad.GetComponent<Renderer>();
        quadRenderer.sharedMaterial = material;
        quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        quadRenderer.receiveShadows = false;
        return quad.transform;
    }

    /// <summary>
    /// Turns every mass to face the camera and drifts it. Billboarding is what gives the bank its
    /// mass from the tactical angle: flat-lying quads would collapse into the floor stain and leave
    /// the screen looking like the tint it used to be.
    /// </summary>
    private void LateUpdate() => Pose(Time.time - bornTime);

    private void Pose(float age)
    {
        Camera viewCamera = GameLoop.Instance != null ? GameLoop.Instance.TeamCamera : null;
        if (viewCamera == null)
            viewCamera = Camera.main;
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

            // Boiling up out of the cell: the mass arrives small and low, and the layers above the
            // base are held back a beat so the bank builds rather than switching on.
            float rise = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01((age - puff.Delay) / DeploySeconds)
            );
            float bob = Mathf.Sin(time * BobSpeed + puff.Phase) * BobHeight;
            Vector3 place = puff.Anchor + Vector3.up * (bob - DeployRise * (1f - rise));

            // Walked along the ray it is already seen down — toward the lens for the veil, away
            // from it for the body — and rescaled by exactly the throw it gave up or gained, so
            // the mass lands in the same pixels at the same size and only its depth changes.
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
            // The light does not follow the roll the billboard is carrying.
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
