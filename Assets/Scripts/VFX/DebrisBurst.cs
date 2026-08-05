using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The material thrown outward by a hit: chunks of torn-up ground, the burning matter at the
/// centre, the dust they came out of, and the dark slivers that outrun all three.
/// <para>
/// Debris is what makes an explosion feel like it displaced something rather than just glowed. The
/// board floor renders at luminance 181-184, so this burst is built out of matter that is *darker*
/// than the floor rather than brighter: charred chunks, dark kicked dust and ground scars carry the
/// silhouette. The hot part of the burst is matter too, not additive light — a deep ember orange
/// dark enough to occlude the floor — so that the flash and the mass are the same object and
/// therefore peak on the same frame.
/// </para>
/// <para>Purely local and visual. Call on every peer at the moment of impact.</para>
/// </summary>
public static class DebrisBurst
{
    // A three-frame strip cuts its impact frame inside the first tenth of a second and its
    // aftermath frame about 0.53s after the hit. An impact has exactly one money frame, so light,
    // opaque material and colour all have to arrive together and stay: the dust is at footprint by
    // 0.05s, the burning matter holds whole until 0.105s, and nothing begins collapsing before
    // 0.11s. Every thrown piece is off the floor before 0.70s; only the burn outlives that, and it
    // has to be gone again by 1.4s: the shot runs 1.9s and its clean plate is the median of the
    // last six frames, so anything still on the board at 1.73s poisons every measurement.
    const float LifeSeconds = 1.34f;
    const float ExitSeconds = 0.12f;
    const float EmberHoldSeconds = 0.115f;
    const float EmberCoolSeconds = 0.30f;
    const float FireHoldSeconds = 0.105f;
    const float FireFadeSeconds = 0.085f;

    // The burn is the only thing left after the pieces have gone, so it is the whole aftermath
    // frame and has to still be there whenever that frame is cut. It arrives with the blast at
    // most of its width, runs out to full inside a quarter second, then cools and erodes instead
    // of holding still: a mark that locks its outline for half a second reads as a decal.
    // The soot creep has to stop well before the erosion does. Left running the two grow and eat
    // the rim at the same rate, and the outline stands still while both are moving.
    const float ScorchSpreadSeconds = 0.26f;
    const float ScorchCreepSeconds = 0.40f;
    const float ScorchFadeStart = 1.04f;
    const float ScorchFadeSeconds = 0.28f;

    // Cooling and going out are per patch, not per frame: one clock walks a rank baked into the
    // mark so the coals empty seam by seam and the char greys behind them. The rates are set so
    // saturated ember colour survives to roughly 1.10s, and the last of the mark is gone by 1.32s.
    // Every one of them is inert until 0.20s, because the frame a strip cuts for the impact lands
    // inside the first tenth of a second and it is already carrying everything it needs.
    const float ScorchCoolStart = 0.30f;
    const float ScorchCoolRate = 1.20f;
    const float ScorchAshBite = 0.26f;
    const float ScorchCoalOutStart = 0.30f;
    const float ScorchCoalOutRate = 1.70f;
    // Far enough below zero that the sharpest rank in the mark is still fully alight at the start:
    // any less and the coldest coals would be dimmed from the first frame.
    const float ScorchCoalOutFrom = -0.34f;
    const float ScorchCoalOutTo = 1.10f;

    // The bed is held off until the flame that made it has gone, both because that is the order a
    // fire leaves ground in and because the impact frame is already carrying its own material.
    // Ash then crusts over patch by patch rather than everywhere at once.
    const float ScorchBedStart = 0.20f;
    const float ScorchBedSeconds = 0.20f;
    const float ScorchBedRate = 2.02f;
    const float ScorchBedCover = 0.62f;
    const float ScorchEmberFlicker = 0.22f;
    const float ScorchEmberRate = 16f;

    // A second, smaller throw of embers. One volley emitted at the contact frame peaks on that
    // frame and only that frame; the strip is sampled at 30Hz and needs the hot pixels held for
    // roughly three of them before anything starts leaving.
    const float EmberWaveTwoAt = 0.052f;

    // Radius the tuning was authored against; a caller asking for less gets a tighter burst rather
    // than a differently shaped one.
    const float ReferenceRadius = 3.5f;

    const float BiasConeDegrees = 62f;

    // Ground clearance for the flat cards. Fog tiles sit at y = 0.05 and shockwaves at y = 0.08.
    // The burning cards ride above the dust so they win the depth test against it for their whole
    // life; without that separation the dust simply covers them. The burn sits under everything,
    // so the dust that is still in the air correctly hides the ground it came out of.
    const float ScorchHeight = 0.060f;
    const float BedHeight = 0.064f;
    const float CoalHeight = 0.068f;
    const float SurgeHeight = 0.19f;
    const float LandingDustHeight = 0.20f;
    const float DustCeiling = 0.72f;
    const float FireFloor = 1.10f;

    // Linear values, chosen so the rendered pixels land in the 40-110 band a pale floor needs
    // before it will show a silhouette at all. Warm charred grey throughout: the ability's own
    // colour deliberately does not tint displaced ground, because a pale tint of it disappears here.
    static readonly Vector4 ChunkShadowTone = new(0.024f, 0.0205f, 0.0175f, 0f);
    static readonly Vector4 ChunkLitTone = new(0.052f, 0.044f, 0.037f, 0f);
    static readonly Vector4 ChunkEmberTone = new(2.4f, 0.52f, 0.04f, 0f);
    static readonly Vector4 ChunkKeyDirection = new(0.55f, 0.72f, -0.42f, 0f);
    static readonly Vector4 ChunkEmberDirection = new(-0.62f, 0.48f, -0.62f, 0f);

    // Dust sits one value step above the chunks so the burst reads as three tiers rather than one
    // dark mass, and still far enough under the floor to kill a tile seam underneath it.
    static readonly Vector4 DustToneFresh = new(0.105f, 0.093f, 0.081f, 0f);
    static readonly Vector4 DustToneSettled = new(0.075f, 0.069f, 0.063f, 0f);

    // The hot part of the burst is the dust's own interior, not a sprite laid over it. This tone
    // replaces the grey inside the lobes rather than adding to it, so the cloud gains hue without
    // gaining light: rendered it lands near (208, 100, 55), which is saturated past 0.7 and still
    // some sixty levels under the floor, so the same pixels stay occluding material. Deliberately
    // orange rather than deep red — red alone buys almost no luminance, and a hue that cannot
    // reach a readable value reads as a dark smudge however saturated it measures.
    static readonly Vector4 DustEmberTone = new(0.652f, 0.130f, 0.036f, 0f);

    // Burning ground. Authored so the body tonemaps to roughly (184-205, 84-102, 34-42): saturated
    // past 0.79 and still darker than the floor, which is the only way a hot colour can be opaque
    // material on this board. Only the core is allowed to clip, and it clips in all three channels
    // rather than drifting through cream on the way.
    static readonly Vector4 FireBodyTone = new(0.62f, 0.085f, 0.015f, 0f);
    static readonly Vector4 FireAshTone = new(0.105f, 0.070f, 0.052f, 0f);
    static readonly Vector4 FireCoreTone = new(6.4f, 5.0f, 4.3f, 0f);

    // The burn. Char multiplies the floor it lies on, the bed lays the burn's own matter over the
    // result, and coals add light back on top of both. Under a multiply the channel ratios are the
    // hue and the magnitude is the darkening, and they are independent: this pair carries an ember
    // hue at the same Rec.601 weight a neutral char would have, so the mark loses none of its
    // depth and the tile seam under it keeps exactly the proportional contrast it had.
    // Ash is where the char cools to once the coals are out.
    static readonly Vector4 ScorchCharTone = new(0.55f, 0.14f, 0.06f, 0f);
    static readonly Vector4 ScorchAshTone = new(0.56f, 0.48f, 0.45f, 0f);
    static readonly Vector4 ScorchCoalTone = new(0.50f, 0.030f, 0.002f, 0f);

    // A multiply can only scale what is already on the board, so on its own the mark is the floor's
    // own grid in a new hue: it holds colour where the floor is bright and loses it where the floor
    // is dark, which leaves the tile seam the most saturated line inside the burn. The bed is the
    // burn's own material laid over that, so the mark's colour stops depending on the board.
    static readonly Vector4 ScorchBedTone = new(0.150f, 0.030f, 0.010f, 0f);
    static readonly Vector4 ScorchBedSeam = new(0.245f, 0.052f, 0.014f, 0f);

    static readonly Vector4 StreakTone = new(0.038f, 0.032f, 0.027f, 0f);
    static readonly Vector4 StreakEmberTone = new(3.1f, 0.72f, 0.06f, 0f);

    // Direction the whole burst is lit from, matching the chunks' own key. Flat cards carry their
    // lit side along +u, so a card only has to know which way to mirror.
    static readonly Vector2 KeyHeading = new(0.795f, -0.607f);

    const float StreakCut = 0.42f;
    const float EmberIntensity = 5.5f;

    static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");
    static readonly int ToneId = Shader.PropertyToID("_Tone");
    static readonly int ToneFloorId = Shader.PropertyToID("_ToneFloor");
    static readonly int ToneSpreadId = Shader.PropertyToID("_ToneSpread");
    static readonly int EmberToneId = Shader.PropertyToID("_EmberTone");
    static readonly int EmberFloorId = Shader.PropertyToID("_EmberFloor");
    static readonly int EmberSpreadId = Shader.PropertyToID("_EmberSpread");
    static readonly int EmberCutId = Shader.PropertyToID("_EmberCut");
    static readonly int EmberEdgeId = Shader.PropertyToID("_EmberEdge");
    static readonly int EmberFillId = Shader.PropertyToID("_EmberFill");
    static readonly int CharToneId = Shader.PropertyToID("_CharTone");
    static readonly int AshToneId = Shader.PropertyToID("_AshTone");
    static readonly int CoalToneId = Shader.PropertyToID("_CoalTone");
    static readonly int BedToneId = Shader.PropertyToID("_BedTone");
    static readonly int BedSeamId = Shader.PropertyToID("_BedSeam");
    static readonly int LayId = Shader.PropertyToID("_Lay");
    static readonly int CoverId = Shader.PropertyToID("_Cover");
    static readonly int CoolId = Shader.PropertyToID("_Cool");
    static readonly int BiteId = Shader.PropertyToID("_Bite");
    static readonly int OutId = Shader.PropertyToID("_Out");
    static readonly int OutEdgeId = Shader.PropertyToID("_OutEdge");
    static readonly int FlickerId = Shader.PropertyToID("_Flicker");
    static readonly int PhaseId = Shader.PropertyToID("_Phase");
    static readonly int DensityId = Shader.PropertyToID("_Density");
    static readonly int GlowId = Shader.PropertyToID("_Glow");
    static readonly int EdgeId = Shader.PropertyToID("_Edge");
    static readonly int CutId = Shader.PropertyToID("_Cut");
    static readonly int CoreCutId = Shader.PropertyToID("_CoreCut");
    static readonly int CoreToneId = Shader.PropertyToID("_CoreTone");
    static readonly int EmberId = Shader.PropertyToID("_Ember");
    static readonly int EmberSharpnessId = Shader.PropertyToID("_EmberSharpness");
    static readonly int ShadowToneId = Shader.PropertyToID("_ShadowTone");
    static readonly int LitToneId = Shader.PropertyToID("_LitTone");
    static readonly int KeyDirectionId = Shader.PropertyToID("_KeyDirection");
    static readonly int EmberDirectionId = Shader.PropertyToID("_EmberDirection");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
    static readonly int AlphaClipId = Shader.PropertyToID("_AlphaClip");
    static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
    static readonly int GlossinessId = Shader.PropertyToID("_Glossiness");
    static readonly int MetallicId = Shader.PropertyToID("_Metallic");
    static readonly int TintId = Shader.PropertyToID("_Tint");
    static readonly int TintColorId = Shader.PropertyToID("_TintColor");
    static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    /// <summary>One board cell in world units. Every size below is authored as a fraction of one.</summary>
    static float Cell => Mathf.Max(0.5f, GameLoop.cellSize);

    /// <summary>
    /// Throws a burst of debris outward from <paramref name="position"/>.
    /// </summary>
    /// <param name="position">World origin of the burst.</param>
    /// <param name="color">
    /// Ability colour. Accepted for signature compatibility but intentionally not applied: displaced
    /// ground is charred, and a wash of team colour over it measured as invisible against the board.
    /// </param>
    /// <param name="radius">Roughly how far the pieces should travel, in metres.</param>
    /// <param name="count">How many pieces to throw.</param>
    /// <param name="direction">
    /// Bias for a directional burst, such as a slam or a shot. <see cref="Vector3.zero"/> throws
    /// evenly in all directions.
    /// </param>
    public static void Spawn(
        Vector3 position,
        Color color,
        float radius = 3f,
        int count = 18,
        Vector3 direction = default
    )
    {
        if (count <= 0)
            return;

        GameObject root = new("DebrisBurst");
        root.transform.position = new Vector3(position.x, Mathf.Max(0f, position.y), position.z);
        root.AddComponent<Burst>().Build(Mathf.Max(0.8f, radius), count, direction);
    }

    /// <summary>
    /// Drives the burst. Chunks, dust and fire are transforms integrated by hand so their timing can
    /// be aimed at the exact frames a strip cuts, while the slivers and sparks ride particle systems
    /// that only need to be thrown once.
    /// </summary>
    sealed class Burst : MonoBehaviour
    {
        sealed class Chunk
        {
            public Transform Body;
            public Vector3 Ground;
            public Vector3 Outward;
            public float LaunchHeight;
            public float ApexHeight;
            public float RiseSeconds;
            public float FallSeconds;
            public float BounceHeight;
            public float BounceSeconds;
            public float HorizontalSpeed;
            public float DragSeconds;
            public float SkidSeconds;
            public float RestHeight;
            public Vector3 Scale;
            public Quaternion Rotation;
            public Vector3 SpinAxis;
            public float SpinSpeed;
            public float ExitAt;
            public bool KicksDust;
            public bool Kicked;
            public bool Retired;
        }

        /// <summary>
        /// A flat alpha-cut card lying in the ground plane: a dust lobe, a lick of the outward
        /// surge, or the scar a hero chunk carves when it lands.
        /// </summary>
        sealed class Card
        {
            public Transform Node;
            public MeshRenderer Renderer;
            public Vector3 Center;
            public Vector3 Drift;
            public Vector3 Push;
            public float PushDrag;
            public float StartAt;
            public float GrowSeconds;
            public float HoldSeconds;
            public float ClearSeconds;
            public float StartSize;
            public float EndSize;
            public float StartAspect;
            public float EndAspect;
            public float OpenCut;
            public float BaseCut;
            public float Creep;
            public float Tone;
            public float EmberFill;
            public Vector4 ToneFresh;
            public Vector4 ToneSettled;
            public float SettleSeconds;
            public Vector4 Tiling;
            public bool Retired;
        }

        /// <summary>Burning ground at the centre of the burst: opaque body, clipped core.</summary>
        sealed class Flame
        {
            public Transform Node;
            public MeshRenderer Renderer;
            public Vector3 Center;
            public Vector3 Drift;
            public float StartSize;
            public float EndSize;
            public float Aspect;
            public float Heading;
            public float Spin;
            public Vector4 Tiling;
            public bool Retired;
        }

        Chunk[] chunks;
        Card[] cards;
        Flame[] flames;
        Mesh[] chunkMeshes;
        Mesh cardMesh;
        Material chunkMaterial;
        Material[] dustMaterials;
        Material surgeMaterial;
        Material scorchMaterial;
        Material bedMaterial;
        Material coalMaterial;
        Material[] fireMaterials;
        Material streakMaterial;
        Material emberMaterial;
        Texture2D[] dustTextures;
        Texture2D surgeTexture;
        Texture2D scorchTexture;
        Texture2D[] fireTextures;
        Texture2D streakTexture;
        Texture2D emberTexture;
        MaterialPropertyBlock cardBlock;

        Transform scorchNode;
        Transform bedNode;
        Transform coalNode;
        float scorchStartWidth;
        float scorchFullWidth;
        float scorchAspect;

        ParticleSystem emberSystem;
        int emberWaveTwo;
        float emberReach;
        float emberScale;
        bool emberWaveTwoSent;

        int landingStart;
        int landingEnd;

        float age;
        bool biased;
        float biasDegrees;

        public void Build(float radius, int count, Vector3 direction)
        {
            Vector3 flatDirection = new(direction.x, 0f, direction.z);
            biased = flatDirection.sqrMagnitude > 0.0001f;
            if (biased)
                biasDegrees = Mathf.Atan2(flatDirection.x, flatDirection.z) * Mathf.Rad2Deg;

            float reach = Mathf.Clamp(radius / ReferenceRadius, 0.55f, 1.5f);
            float sizeScale = Mathf.Clamp(radius / ReferenceRadius, 0.72f, 1.35f);
            float loft = Mathf.Clamp(radius / ReferenceRadius, 0.8f, 1.25f);

            cardBlock = new MaterialPropertyBlock();
            cardMesh = BuildCardMesh();
            BuildMaterials();

            int heroCount = Mathf.Clamp(Mathf.RoundToInt(count * 0.14f), 2, 4);
            int midCount = Mathf.Clamp(Mathf.RoundToInt(count * 0.28f), 4, 8);

            BuildScorch(sizeScale);
            BuildChunks(heroCount, midCount, reach, sizeScale, loft);
            BuildCards(sizeScale, heroCount);
            BuildFlames(sizeScale);
            BuildStreaks(count, reach, sizeScale);
            BuildEmbers(count, reach, sizeScale);

            StepChunks(0f);
            StepCards();
            StepFlames();
            StepScorch();
        }

        void BuildMaterials()
        {
            Shader chunkShader = Shader.Find("BattlePlan/DebrisChunk");
            if (chunkShader != null)
            {
                chunkMaterial = new Material(chunkShader) { name = "DebrisChunk" };
                chunkMaterial.SetVector(ShadowToneId, ChunkShadowTone);
                chunkMaterial.SetVector(LitToneId, ChunkLitTone);
                chunkMaterial.SetVector(EmberToneId, ChunkEmberTone);
                chunkMaterial.SetVector(KeyDirectionId, ChunkKeyDirection);
                chunkMaterial.SetVector(EmberDirectionId, ChunkEmberDirection);
                chunkMaterial.SetFloat(EmberSharpnessId, 7f);
                chunkMaterial.SetFloat(EmberId, 1f);
            }
            else
            {
                Shader lit =
                    Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                    ?? Shader.Find("Standard");
                if (lit != null)
                {
                    chunkMaterial = new Material(lit) { name = "DebrisChunk" };
                    Color charred = new(0.085f, 0.072f, 0.061f, 1f);
                    if (chunkMaterial.HasProperty(BaseColorId))
                        chunkMaterial.SetColor(BaseColorId, charred);
                    if (chunkMaterial.HasProperty(ColorId))
                        chunkMaterial.SetColor(ColorId, charred);
                    if (chunkMaterial.HasProperty(SmoothnessId))
                        chunkMaterial.SetFloat(SmoothnessId, 0.12f);
                    if (chunkMaterial.HasProperty(GlossinessId))
                        chunkMaterial.SetFloat(GlossinessId, 0.12f);
                    if (chunkMaterial.HasProperty(MetallicId))
                        chunkMaterial.SetFloat(MetallicId, 0f);
                }
            }

            // Three dust sprites rather than one. A single sprite stamped seven times is a bunch of
            // grapes however ragged the sprite is, and mirroring alone does not hide a repeat. The
            // first carries the wide voids and is always given to the dominant lobe, because a
            // hole only reads as a hole where no smaller lobe is sitting behind it.
            dustTextures = new Texture2D[3];
            dustMaterials = new Material[dustTextures.Length];
            for (int i = 0; i < dustTextures.Length; i++)
            {
                dustTextures[i] = i == 0
                    ? BuildPuffTexture(112, 6, 0.73f)
                    : BuildPuffTexture(112, 11, 0.79f);
                dustMaterials[i] = BuildMatterMaterial(
                    "DebrisDust",
                    dustTextures[i],
                    DustToneFresh,
                    0.42f,
                    1.36f,
                    0.30f
                );
                // The hue's readable window is far narrower than the grey's, so the ember runs on
                // its own compressed value range: too dark and it is a brown smudge, too bright
                // and it stops occluding the floor it is supposed to be made of.
                SetEmber(dustMaterials[i], DustEmberTone, 0.66f, 0.72f, 0.34f, 5.5f);
            }

            surgeTexture = BuildSurgeTexture(88);
            surgeMaterial = BuildMatterMaterial(
                "DebrisSurge",
                surgeTexture,
                DustToneFresh,
                0.44f,
                1.30f,
                0.30f
            );
            SetEmber(surgeMaterial, DustEmberTone, 0.62f, 0.70f, 0.42f, 5.0f);

            streakTexture = BuildStreakTexture(96, 24);
            streakMaterial = BuildMatterMaterial(
                "DebrisStreak",
                streakTexture,
                StreakTone,
                0.55f,
                0.72f,
                StreakCut
            );
            // A sliver's hot spot is already a narrow painted gradient, so it takes the mask
            // straight and needs no value structure of its own.
            SetEmber(streakMaterial, StreakEmberTone, 1f, 0f, 0f, 1f);

            fireTextures = new Texture2D[2];
            fireMaterials = new Material[fireTextures.Length];
            for (int i = 0; i < fireTextures.Length; i++)
            {
                fireTextures[i] = BuildFireTexture(80);
                fireMaterials[i] = BuildFireMaterial(fireTextures[i]);
            }

            // Char, bed and coals share one sprite so the embers always sit inside the burn they
            // came out of: alpha is the mark, green its char density, red the coal seams and blue
            // how long each patch holds its heat.
            Shader scorch = Shader.Find("BattlePlan/DebrisScorch");
            Shader coals = Shader.Find("BattlePlan/DebrisCoals");
            if (scorch != null && coals != null)
            {
                scorchTexture = BuildScorchTexture(160);

                scorchMaterial = new Material(scorch) { name = "DebrisScorch" };
                scorchMaterial.SetTexture(MainTexId, scorchTexture);
                scorchMaterial.SetVector(CharToneId, ScorchCharTone);
                scorchMaterial.SetVector(AshToneId, ScorchAshTone);
                scorchMaterial.SetFloat(DensityId, 1f);
                scorchMaterial.SetFloat(CutId, 0.10f);
                scorchMaterial.SetFloat(EdgeId, 12f);
                scorchMaterial.SetFloat(CoolId, 0f);
                scorchMaterial.SetFloat(BiteId, ScorchAshBite);

                coalMaterial = new Material(coals) { name = "DebrisCoals" };
                coalMaterial.SetTexture(MainTexId, scorchTexture);
                coalMaterial.SetVector(CoalToneId, ScorchCoalTone);
                coalMaterial.SetFloat(GlowId, 1f);
                coalMaterial.SetFloat(CutId, 0.14f);
                coalMaterial.SetFloat(EdgeId, 10f);
                coalMaterial.SetFloat(OutId, ScorchCoalOutFrom);
                coalMaterial.SetFloat(OutEdgeId, 3.2f);
                coalMaterial.SetFloat(FlickerId, 0f);
                coalMaterial.SetFloat(PhaseId, 0f);

                // Optional: without it the burn is still the char and its coals, only with the
                // board's own grid showing through more of the mark's colour.
                Shader bed = Shader.Find("BattlePlan/DebrisBed");
                if (bed != null)
                {
                    bedMaterial = new Material(bed) { name = "DebrisBed" };
                    bedMaterial.SetTexture(MainTexId, scorchTexture);
                    bedMaterial.SetVector(BedToneId, ScorchBedTone);
                    bedMaterial.SetVector(BedSeamId, ScorchBedSeam);
                    bedMaterial.SetFloat(LayId, 0f);
                    bedMaterial.SetFloat(CoverId, ScorchBedCover);
                    bedMaterial.SetFloat(DensityId, 1f);
                    bedMaterial.SetFloat(CutId, 0.10f);
                    bedMaterial.SetFloat(EdgeId, 12f);
                    bedMaterial.SetFloat(CoolId, 0f);
                    bedMaterial.SetFloat(BiteId, ScorchAshBite);
                }
            }

            emberTexture = BuildEmberTexture(64);
            Shader additive =
                Shader.Find("BattlePlan/DebrisSpark")
                ?? Shader.Find("Legacy Shaders/Particles/Additive")
                ?? Shader.Find("Particles/Additive")
                ?? Shader.Find("Mobile/Particles/Additive");
            if (additive != null)
            {
                emberMaterial = new Material(additive) { name = "DebrisEmber" };
                if (emberMaterial.HasProperty(MainTexId))
                    emberMaterial.SetTexture(MainTexId, emberTexture);
                else
                    emberMaterial.mainTexture = emberTexture;
                if (emberMaterial.HasProperty(TintId))
                    emberMaterial.SetColor(TintId, Color.white);
                if (emberMaterial.HasProperty(IntensityId))
                    emberMaterial.SetFloat(IntensityId, EmberIntensity);
                // The legacy fallback has no intensity of its own, so fold it into the tint there.
                if (emberMaterial.HasProperty(TintColorId))
                    emberMaterial.SetColor(TintColorId, Color.white * 2f);
            }
        }

        static Material BuildMatterMaterial(
            string name,
            Texture2D sprite,
            Vector4 tone,
            float toneFloor,
            float toneSpread,
            float cut
        )
        {
            Shader matter = Shader.Find("BattlePlan/DebrisMatter");
            if (matter != null)
            {
                Material material = new(matter) { name = name };
                material.SetTexture(MainTexId, sprite);
                material.SetVector(ToneId, tone);
                material.SetFloat(ToneFloorId, toneFloor);
                material.SetFloat(ToneSpreadId, toneSpread);
                material.SetFloat(CutId, cut);
                return material;
            }

            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent Cutout");
            if (unlit == null)
                return null;

            Material fallback = new(unlit) { name = name };
            if (fallback.HasProperty(BaseMapId))
                fallback.SetTexture(BaseMapId, sprite);
            else
                fallback.mainTexture = sprite;
            if (fallback.HasProperty(BaseColorId))
                fallback.SetColor(BaseColorId, new Color(tone.x, tone.y, tone.z, 1f));
            if (fallback.HasProperty(CutoffId))
                fallback.SetFloat(CutoffId, cut);
            if (fallback.HasProperty(AlphaClipId))
                fallback.SetFloat(AlphaClipId, 1f);
            fallback.EnableKeyword("_ALPHATEST_ON");
            fallback.renderQueue = 2450;
            return fallback;
        }

        /// <summary>
        /// Arms the hue that replaces a material's grey where the sprite's red mask says the
        /// matter is hot. The fallback unlit shader has no such notion, so the piece degrades to
        /// an all-grey burst rather than throwing.
        /// </summary>
        static void SetEmber(
            Material material,
            Vector4 tone,
            float floor,
            float spread,
            float cut,
            float edge
        )
        {
            if (material == null || !material.HasProperty(EmberToneId))
                return;
            material.SetVector(EmberToneId, tone);
            material.SetFloat(EmberFloorId, floor);
            material.SetFloat(EmberSpreadId, spread);
            material.SetFloat(EmberCutId, cut);
            material.SetFloat(EmberEdgeId, edge);
            material.SetFloat(EmberFillId, 1f);
        }

        /// <summary>
        /// Burning ground wants a core that erodes rather than dims, because a dimming white passes
        /// through cream and cream is what drains hue out of the frame. Where that shader is
        /// missing the piece degrades to a soft-cored version of the same matter.
        /// </summary>
        static Material BuildFireMaterial(Texture2D sprite)
        {
            Shader fire = Shader.Find("BattlePlan/DebrisFire");
            if (fire == null)
            {
                Material soft = BuildMatterMaterial(
                    "DebrisFire",
                    sprite,
                    FireBodyTone,
                    0.46f,
                    1.20f,
                    0.30f
                );
                SetEmber(soft, FireCoreTone, 1f, 0f, 0.52f, 22f);
                if (soft != null)
                    soft.renderQueue = 2490;
                return soft;
            }

            Material material = new(fire) { name = "DebrisFire" };
            material.SetTexture(MainTexId, sprite);
            material.SetVector(ToneId, FireBodyTone);
            material.SetVector(CoreToneId, FireCoreTone);
            material.SetFloat(ToneFloorId, 0.46f);
            material.SetFloat(ToneSpreadId, 1.20f);
            material.SetFloat(CutId, 0.30f);
            material.SetFloat(CoreCutId, 0.52f);
            return material;
        }

        /// <summary>
        /// Lays the burn under the burst: one irregular mark roughly two cells across, arriving
        /// with the blast rather than after it. Char, bed and coals are separate renderers because
        /// a mark that darkens the floor, carries its own matter and glows cannot be one blend,
        /// and keeping them apart lets each die on its own clock while the others are still there.
        /// </summary>
        void BuildScorch(float sizeScale)
        {
            if (scorchMaterial == null || coalMaterial == null || cardMesh == null)
                return;

            // The painted mark reaches about 0.89 of the card, so this puts the burn's long axis
            // between 1.7 and 1.9 cells. Two cells is the top of the band it can occupy before it
            // stops being a mark under an explosion and starts being the explosion.
            scorchFullWidth = Random.Range(1.95f, 2.15f) * Cell * sizeScale;
            scorchStartWidth = scorchFullWidth * Random.Range(0.54f, 0.62f);
            scorchAspect = Random.Range(0.74f, 0.90f);

            float heading = Random.Range(0f, 360f);
            scorchNode = MakeScorchCard("DebrisScorch", scorchMaterial, ScorchHeight, heading);
            if (bedMaterial != null)
                bedNode = MakeScorchCard("DebrisBed", bedMaterial, BedHeight, heading);
            coalNode = MakeScorchCard("DebrisCoals", coalMaterial, CoalHeight, heading);
        }

        Transform MakeScorchCard(string name, Material material, float height, float heading)
        {
            GameObject node = new(name);
            node.transform.SetParent(transform, worldPositionStays: false);
            node.transform.SetPositionAndRotation(
                new Vector3(transform.position.x, height, transform.position.z),
                Quaternion.Euler(0f, heading, 0f)
            );
            node.transform.localScale = new Vector3(
                scorchStartWidth,
                1f,
                scorchStartWidth * scorchAspect
            );
            node.AddComponent<MeshFilter>().sharedMesh = cardMesh;

            MeshRenderer markRenderer = node.AddComponent<MeshRenderer>();
            markRenderer.sharedMaterial = material;
            markRenderer.shadowCastingMode = ShadowCastingMode.Off;
            markRenderer.receiveShadows = false;
            markRenderer.lightProbeUsage = LightProbeUsage.Off;
            markRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return node.transform;
        }

        void BuildChunks(int heroCount, int midCount, float reach, float sizeScale, float loft)
        {
            if (chunkMaterial == null)
                return;

            int total = heroCount + midCount;
            chunks = new Chunk[total];
            chunkMeshes = new Mesh[total];

            for (int i = 0; i < total; i++)
            {
                bool hero = i < heroCount;
                bool skimmer = !hero && i >= total - 2;

                float angle = SampleAngle(i, total) * Mathf.Deg2Rad;
                Vector3 outward = new(Mathf.Sin(angle), 0f, Mathf.Cos(angle));

                // Three masses to one, not fifteen of a size: an even spread of equal pieces is the
                // definition of confetti, and the hero chunks are what give the burst a hierarchy.
                float width =
                    hero ? Random.Range(0.50f, 0.62f)
                    : skimmer ? Random.Range(0.16f, 0.22f)
                    : Random.Range(0.19f, 0.27f);
                width *= Cell * sizeScale;

                Chunk chunk = new()
                {
                    Outward = outward,
                    Ground = new Vector3(
                        transform.position.x + outward.x * width * 0.35f,
                        0f,
                        transform.position.z + outward.z * width * 0.35f
                    ),
                    LaunchHeight = Random.Range(0.10f, 0.42f),
                    Scale = new Vector3(width, width * Random.Range(0.86f, 1.1f), width * Random.Range(0.88f, 1.12f)),
                    RestHeight = width * 0.28f,
                    Rotation = Random.rotation,
                    SpinAxis = Random.onUnitSphere,
                    KicksDust = hero || skimmer,
                };

                // Front-loaded: a decelerating rise puts the piece at the top of its arc by the
                // frame a strip cuts as impact, and it hangs there rather than streaking past.
                if (hero)
                {
                    chunk.ApexHeight = Random.Range(3.1f, 4.2f) * loft;
                    chunk.RiseSeconds = Random.Range(0.100f, 0.125f);
                    chunk.FallSeconds = Random.Range(0.185f, 0.235f);
                    chunk.HorizontalSpeed = Random.Range(7f, 11f) * reach;
                    chunk.DragSeconds = 0.20f;
                    chunk.SkidSeconds = 0.10f;
                    chunk.SpinSpeed = Random.Range(150f, 320f);
                }
                else if (skimmer)
                {
                    chunk.ApexHeight = Random.Range(0.6f, 1.05f) * loft;
                    chunk.RiseSeconds = Random.Range(0.045f, 0.070f);
                    chunk.FallSeconds = Random.Range(0.090f, 0.130f);
                    chunk.HorizontalSpeed = Random.Range(20f, 26f) * reach;
                    chunk.DragSeconds = 0.26f;
                    chunk.SkidSeconds = 0.22f;
                    chunk.BounceHeight = Random.Range(0.28f, 0.55f);
                    chunk.BounceSeconds = Random.Range(0.11f, 0.16f);
                    chunk.SpinSpeed = Random.Range(600f, 1100f);
                }
                else
                {
                    chunk.ApexHeight = Random.Range(2.0f, 3.3f) * loft;
                    chunk.RiseSeconds = Random.Range(0.075f, 0.105f);
                    chunk.FallSeconds = Random.Range(0.140f, 0.195f);
                    chunk.HorizontalSpeed = Random.Range(13f, 19f) * reach;
                    chunk.DragSeconds = 0.22f;
                    chunk.SkidSeconds = 0.14f;
                    chunk.BounceHeight = Random.Range(0.20f, 0.45f);
                    chunk.BounceSeconds = Random.Range(0.10f, 0.15f);
                    chunk.SpinSpeed = Random.Range(420f, 900f);
                }

                // Held whole through the frame a strip cuts as aftermath, then gone well before
                // 0.7s. Nothing may outlive this, scars included.
                float settled = chunk.RiseSeconds + chunk.FallSeconds + chunk.BounceSeconds;
                chunk.ExitAt = Mathf.Max(settled + 0.12f, Random.Range(0.48f, 0.57f));

                Mesh mesh = BuildChunkMesh();
                chunkMeshes[i] = mesh;

                GameObject body = new("Chunk");
                body.transform.SetParent(transform, worldPositionStays: false);
                body.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer bodyRenderer = body.AddComponent<MeshRenderer>();
                bodyRenderer.sharedMaterial = chunkMaterial;
                bodyRenderer.shadowCastingMode = ShadowCastingMode.Off;
                bodyRenderer.receiveShadows = false;
                bodyRenderer.lightProbeUsage = LightProbeUsage.Off;
                bodyRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                chunk.Body = body.transform;

                chunks[i] = chunk;
            }
        }

        void BuildCards(float sizeScale, int heroCount)
        {
            // Card widths run wider than the dust in them, because the cut throws away the outer
            // fifth of the sprite. This is the middle the chunks come out of; the aftermath layer
            // owns anything that lingers, so all of it is gone inside 0.28s.
            float[] lobeWidths = { 1.55f, 0.99f, 0.81f, 0.64f, 0.53f, 0.46f, 0.42f };
            const int surgeCount = 4;
            int landingCount = heroCount + 2;

            // How much of each lobe's interior is allowed to be hot. The burst has to read as a
            // dust cloud with a molten heart, not as a red cloud, so the hue is concentrated in
            // the dominant mass and the outlying lobes stay almost entirely grey.
            float[] lobeEmber = { 1f, 0.88f, 0.88f, 0.62f, 0.62f, 0.38f, 0.38f };

            bool hasDust = dustMaterials != null && dustMaterials[0] != null;
            int lobeCount = hasDust ? lobeWidths.Length : 0;
            int surgeSlots = surgeMaterial != null ? surgeCount : 0;
            int landingSlots = hasDust ? landingCount : 0;

            cards = new Card[lobeCount + surgeSlots + landingSlots];
            landingStart = lobeCount + surgeSlots;
            landingEnd = landingStart + landingSlots;

            for (int i = 0; i < lobeCount; i++)
            {
                float width = lobeWidths[i] * Cell * sizeScale * Random.Range(0.9f, 1.12f);
                float angle = (i / (float)Mathf.Max(1, lobeCount - 1)) * Mathf.PI * 2f
                    + Random.Range(-0.5f, 0.5f);
                float offset = i == 0 ? 0f : Random.Range(0.20f, 0.36f) * Cell;
                Vector3 center =
                    transform.position
                    + new Vector3(Mathf.Cos(angle) * offset, 0f, Mathf.Sin(angle) * offset)
                    + Vector3.up * (i == 0 ? 0.46f : Random.Range(0.18f, DustCeiling));

                float heading = Random.Range(0f, 360f);
                Card card = MakeCard(dustMaterials[i % dustMaterials.Length], center, heading);
                card.Drift =
                    new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Random.Range(0.8f, 1.5f)
                    + Vector3.up * Random.Range(0.6f, 1.15f);

                // The dust is at its footprint before the strip can cut anything: four lobes are
                // already open on the contact frame, the rest arrive inside two, and the whole
                // cloud is clearing again by 0.15s so the burst's heaviest frame cannot drift out
                // past the frame that carries its light.
                card.StartAt = i < 4 ? 0f : Random.Range(0.006f, 0.026f);
                card.GrowSeconds = Random.Range(0.028f, 0.044f);
                card.HoldSeconds = Random.Range(0.075f, 0.105f);
                card.ClearSeconds = Random.Range(0.105f, 0.135f);
                card.StartSize = width * 0.78f;
                card.EndSize = width;
                card.StartAspect = Random.Range(0.86f, 1.14f);
                card.EndAspect = i == 0 ? Random.Range(0.88f, 1.18f) : Random.Range(0.74f, 1.38f);
                card.OpenCut = 0.15f;
                card.BaseCut = Random.Range(0.24f, 0.38f);
                // Grain-by-grain erosion while the lobe is open, so a held silhouette is never a
                // held image.
                card.Creep = 0.03f;
                card.Tone = Random.Range(0.84f, 1.14f);
                card.EmberFill = lobeEmber[i % lobeEmber.Length];
                card.ToneFresh = DustToneFresh;
                card.ToneSettled = DustToneSettled;
                card.SettleSeconds = 0.25f;
                cards[i] = card;
            }

            for (int i = 0; i < surgeSlots; i++)
            {
                int slot = lobeCount + i;
                float heading = SampleAngle(i, surgeSlots);
                float radians = heading * Mathf.Deg2Rad;
                Vector3 outward = new(Mathf.Sin(radians), 0f, Mathf.Cos(radians));

                Vector3 center =
                    transform.position
                    + outward * (Random.Range(0.08f, 0.16f) * Cell)
                    + Vector3.up * Random.Range(0.14f, SurgeHeight + 0.07f);

                Card card = MakeCard(surgeMaterial, center, heading);
                // A blast skirt runs out fast and stops, which is what carries the burst to its
                // full width inside two frames without anything sliding on afterwards.
                card.Push = outward * (Random.Range(12f, 16f) * sizeScale);
                card.PushDrag = Random.Range(0.045f, 0.065f);
                card.Drift = Vector3.up * Random.Range(0.25f, 0.55f);
                card.StartAt = i == 0 ? 0f : Random.Range(0.004f, 0.024f);
                card.GrowSeconds = Random.Range(0.030f, 0.046f);
                card.HoldSeconds = Random.Range(0.070f, 0.095f);
                card.ClearSeconds = Random.Range(0.095f, 0.125f);
                float across = Random.Range(0.44f, 0.64f) * Cell * sizeScale;
                card.StartSize = across * 0.55f;
                card.EndSize = across;
                card.StartAspect = 0.85f;
                card.EndAspect = Random.Range(1.5f, 1.95f);
                card.OpenCut = 0.18f;
                card.BaseCut = Random.Range(0.26f, 0.40f);
                card.Tone = Random.Range(0.88f, 1.05f);
                card.EmberFill = 0.55f;
                card.ToneFresh = DustToneFresh;
                card.ToneSettled = DustToneSettled;
                card.SettleSeconds = 0.22f;
                cards[slot] = card;
            }

            for (int i = landingStart; i < landingEnd; i++)
            {
                // Never the canopy sprite: its voids are sized for the dominant lobe and would
                // shred a puff a fifth of that width.
                Card card = MakeCard(
                    dustMaterials[Mathf.Min(1 + i % 2, dustMaterials.Length - 1)],
                    transform.position,
                    Random.Range(0f, 360f)
                );
                card.StartAt = float.PositiveInfinity;
                card.GrowSeconds = 0.075f;
                card.HoldSeconds = 0.06f;
                card.ClearSeconds = 0.16f;
                card.StartAspect = Random.Range(0.8f, 1.25f);
                card.EndAspect = Random.Range(0.8f, 1.25f);
                card.OpenCut = 0.20f;
                card.BaseCut = Random.Range(0.28f, 0.38f);
                card.Tone = Random.Range(0.80f, 1.0f);
                // Dirt kicked up by a chunk that has already landed is cold; it has no business
                // glowing a third of a second after the blast.
                card.EmberFill = 0.20f;
                card.ToneFresh = DustToneFresh;
                card.ToneSettled = DustToneSettled;
                card.SettleSeconds = 0.22f;
                cards[i] = card;
            }
        }

        Card MakeCard(Material material, Vector3 center, float heading)
        {
            GameObject node = new("DebrisCard");
            node.transform.SetParent(transform, worldPositionStays: false);
            node.transform.position = center;
            node.transform.localRotation = Quaternion.Euler(0f, heading, 0f);
            node.AddComponent<MeshFilter>().sharedMesh = cardMesh;

            MeshRenderer cardRenderer = node.AddComponent<MeshRenderer>();
            cardRenderer.sharedMaterial = material;
            cardRenderer.shadowCastingMode = ShadowCastingMode.Off;
            cardRenderer.receiveShadows = false;
            cardRenderer.lightProbeUsage = LightProbeUsage.Off;
            cardRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            // Held back until the step that owns it sizes it; an unsized card is a one-metre blob.
            node.SetActive(false);

            return new Card
            {
                Node = node.transform,
                Renderer = cardRenderer,
                Center = center,
                Tone = 1f,
                EmberFill = 1f,
                StartAspect = 1f,
                EndAspect = 1f,
                Tiling = TilingFor(heading),
                ToneFresh = DustToneFresh,
                ToneSettled = DustToneSettled,
                SettleSeconds = 0.25f,
            };
        }

        /// <summary>
        /// Which way to mirror a card so its painted lit side ends up facing the burst's key. Every
        /// flat sprite carries its terminator along +u, so one sign is the whole decision; the v
        /// axis is then free to flip for silhouette variety.
        /// </summary>
        static Vector4 TilingFor(float heading)
        {
            float radians = heading * Mathf.Deg2Rad;
            float acrossX = Mathf.Cos(radians);
            float acrossZ = -Mathf.Sin(radians);
            bool mirrorU = acrossX * KeyHeading.x + acrossZ * KeyHeading.y < 0f;
            bool mirrorV = Random.value < 0.5f;
            return new Vector4(
                mirrorU ? -1f : 1f,
                mirrorV ? -1f : 1f,
                mirrorU ? 1f : 0f,
                mirrorV ? 1f : 0f
            );
        }

        void BuildFlames(float sizeScale)
        {
            if (fireMaterials == null || fireMaterials[0] == null)
                return;

            // One dominant mass and four smaller, scattered off-centre. Fire that is concentric
            // reads as a lens flare, and a single card reads as a decal.
            float[] widths = { 0.42f, 0.27f, 0.22f, 0.16f, 0.13f };
            flames = new Flame[widths.Length];

            for (int i = 0; i < widths.Length; i++)
            {
                float width = widths[i] * Cell * sizeScale * Random.Range(0.92f, 1.1f);
                float angle = i * 2.39996f + Random.Range(-0.55f, 0.55f);
                float offset = i == 0
                    ? Random.Range(0f, 0.05f) * Cell
                    : Random.Range(0.10f, 0.30f) * Cell;
                Vector3 center =
                    transform.position
                    + new Vector3(Mathf.Cos(angle) * offset, 0f, Mathf.Sin(angle) * offset)
                    + Vector3.up * Random.Range(FireFloor, FireFloor + 0.48f);

                float heading = Random.Range(0f, 360f);
                GameObject node = new("DebrisFire");
                node.transform.SetParent(transform, worldPositionStays: false);
                node.transform.position = center;
                node.transform.localRotation = Quaternion.Euler(0f, heading, 0f);
                node.AddComponent<MeshFilter>().sharedMesh = cardMesh;

                int variant = i == 0 ? 0 : Mathf.Min(1, fireMaterials.Length - 1);
                MeshRenderer flameRenderer = node.AddComponent<MeshRenderer>();
                flameRenderer.sharedMaterial = fireMaterials[variant];
                flameRenderer.shadowCastingMode = ShadowCastingMode.Off;
                flameRenderer.receiveShadows = false;
                flameRenderer.lightProbeUsage = LightProbeUsage.Off;
                flameRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

                bool mirrorU = Random.value < 0.5f;
                bool mirrorV = Random.value < 0.5f;

                flames[i] = new Flame
                {
                    Node = node.transform,
                    Renderer = flameRenderer,
                    Center = center,
                    Drift =
                        new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Random.Range(0.2f, 0.7f)
                        + Vector3.up * Random.Range(0.55f, 1.15f),
                    StartSize = width * 0.84f,
                    EndSize = width * 1.08f,
                    Aspect = Random.Range(0.78f, 1.34f),
                    Heading = heading,
                    Spin = Random.Range(-64f, 64f),
                    Tiling = new Vector4(
                        mirrorU ? -1f : 1f,
                        mirrorV ? -1f : 1f,
                        mirrorU ? 1f : 0f,
                        mirrorV ? 1f : 0f
                    ),
                };
            }
        }

        void BuildStreaks(int count, float reach, float sizeScale)
        {
            if (streakMaterial == null)
                return;

            ParticleSystem streaks = BuildSystem(
                "Streaks",
                streakMaterial,
                ParticleSystemRenderMode.Stretch,
                0.4f,
                48
            );
            if (streaks == null)
                return;

            ParticleSystem.MainModule main = streaks.main;
            main.gravityModifier = 2.4f;

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = streaks.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.6f, 0.95f), new Keyframe(1f, 0.3f))
            );

            // Alpha drives the cutoff, not a blend: a sliver leaves by eroding to nothing rather
            // than thinning into a ghost, and the ember in it stays saturated until the money
            // window has closed rather than greying out inside it.
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = streaks.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient cooling = new();
            cooling.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(0.6f, 0.6f, 0.6f), 0.72f),
                    new GradientColorKey(new Color(0.14f, 0.14f, 0.14f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 0.62f),
                    new GradientAlphaKey(0.3f, 1f),
                }
            );
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(cooling);

            ParticleSystemRenderer streakRenderer = streaks.GetComponent<ParticleSystemRenderer>();
            if (streakRenderer != null)
            {
                streakRenderer.lengthScale = 3.4f;
                streakRenderer.velocityScale = 0.018f;
                streakRenderer.cameraVelocityScale = 0f;
            }

            int streakCount = Mathf.Clamp(Mathf.RoundToInt(count * 0.45f), 6, 14);

            streaks.Play();
            ParticleSystem.EmitParams emitParams = new();
            for (int i = 0; i < streakCount; i++)
            {
                float angle = SampleAngle(i, streakCount) * Mathf.Deg2Rad;
                float elevation = Mathf.Lerp(8f, 46f, Random.value) * Mathf.Deg2Rad;
                float cosElevation = Mathf.Cos(elevation);
                Vector3 heading = new(
                    Mathf.Sin(angle) * cosElevation,
                    Mathf.Sin(elevation),
                    Mathf.Cos(angle) * cosElevation
                );

                // These are dark, not additive: they are what carries the silhouette past the
                // chunks, and an additive streak over a pale floor adds nothing to a silhouette.
                bool far = i % 4 == 0;
                float speed = (far ? Random.Range(30f, 40f) : Random.Range(19f, 27f)) * reach;
                emitParams.position = heading * 0.35f + Vector3.up * Random.Range(0.15f, 0.55f);
                emitParams.velocity = heading * speed;
                emitParams.startLifetime = far ? Random.Range(0.15f, 0.21f) : Random.Range(0.14f, 0.19f);
                emitParams.startSize = Random.Range(0.30f, 0.54f) * sizeScale;
                emitParams.startColor = Color.white;
                streaks.Emit(emitParams, 1);
            }
        }

        void BuildEmbers(int count, float reach, float sizeScale)
        {
            if (emberMaterial == null)
                return;

            emberSystem = BuildSystem(
                "Embers",
                emberMaterial,
                ParticleSystemRenderMode.Billboard,
                0.4f,
                64
            );
            if (emberSystem == null)
                return;

            ParticleSystem.MainModule main = emberSystem.main;
            main.gravityModifier = 2.5f;

            // Flat-topped rather than peaked. A curve that starts at its maximum puts every hot
            // pixel on the contact frame and nowhere else, which is exactly the one-frame peak a
            // 30Hz strip is free to miss.
            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = emberSystem.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.94f),
                    new Keyframe(0.16f, 1f),
                    new Keyframe(0.46f, 1f),
                    new Keyframe(1f, 0f)
                )
            );

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = emberSystem.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient fade = new();
            fade.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 0.70f),
                    new GradientAlphaKey(0f, 1f),
                }
            );
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(fade);

            emberReach = reach;
            emberScale = sizeScale;
            emberWaveTwo = Mathf.Clamp(Mathf.RoundToInt(count * 0.28f), 4, 9);

            emberSystem.Play();
            EmitEmbers(Mathf.Clamp(Mathf.RoundToInt(count * 0.65f), 8, 20), reach, sizeScale, 1f);
        }

        void EmitEmbers(int count, float reach, float sizeScale, float speedScale)
        {
            if (emberSystem == null || count <= 0)
                return;

            ParticleSystem.EmitParams emitParams = new();
            for (int i = 0; i < count; i++)
            {
                float angle = SampleAngle(i, count) * Mathf.Deg2Rad;
                Vector3 outward = new(Mathf.Sin(angle), 0f, Mathf.Cos(angle));

                // Kept inside the dust so the additive layer has dark material to sit against.
                emitParams.position =
                    outward * (Random.Range(0.1f, 0.6f) * Cell) + Vector3.up * Random.Range(0.2f, 1.1f);
                emitParams.velocity =
                    (outward * (Random.Range(2f, 7f) * reach) + Vector3.up * Random.Range(3f, 9f))
                    * speedScale;
                emitParams.startLifetime = Random.Range(0.24f, 0.34f);
                emitParams.startSize = Random.Range(0.28f, 0.55f) * sizeScale;
                emitParams.startColor = Color.white;
                emberSystem.Emit(emitParams, 1);
            }
        }

        void Update()
        {
            float delta = Mathf.Min(Time.deltaTime, 0.05f);
            age += delta;

            StepChunks(delta);
            StepCards();
            StepFlames();
            StepScorch();

            if (!emberWaveTwoSent && age >= EmberWaveTwoAt)
            {
                emberWaveTwoSent = true;
                EmitEmbers(emberWaveTwo, emberReach, emberScale, 0.72f);
            }

            if (chunkMaterial != null && chunkMaterial.HasProperty(EmberId))
            {
                float cooled = Mathf.InverseLerp(EmberHoldSeconds, EmberCoolSeconds, age);
                chunkMaterial.SetFloat(EmberId, 1f - Mathf.SmoothStep(0f, 1f, cooled));
            }

            if (age >= LifeSeconds)
                Destroy(gameObject);
        }

        void StepChunks(float delta)
        {
            if (chunks == null)
                return;

            for (int i = 0; i < chunks.Length; i++)
            {
                Chunk chunk = chunks[i];
                if (chunk == null || chunk.Retired || chunk.Body == null)
                    continue;

                float landAt = chunk.RiseSeconds + chunk.FallSeconds;
                float height = ChunkHeight(chunk, age, landAt);

                float travelTime = Mathf.Min(age, landAt + chunk.SkidSeconds);
                float travel =
                    chunk.HorizontalSpeed
                    * chunk.DragSeconds
                    * (1f - Mathf.Exp(-travelTime / chunk.DragSeconds));
                Vector3 position = chunk.Ground + chunk.Outward * travel + Vector3.up * height;

                // Spin bleeds off once the piece is down; a chunk still tumbling on the floor reads
                // as weightless.
                if (age > landAt)
                    chunk.SpinSpeed = Mathf.MoveTowards(chunk.SpinSpeed, 0f, 2600f * delta);
                if (delta > 0f)
                    chunk.Rotation =
                        Quaternion.AngleAxis(chunk.SpinSpeed * delta, chunk.SpinAxis) * chunk.Rotation;

                if (chunk.KicksDust && !chunk.Kicked && age >= landAt)
                {
                    chunk.Kicked = true;
                    KickLandingDust(position, chunk.Scale.x);
                }

                float exit =
                    age > chunk.ExitAt
                        ? Mathf.Clamp01(1f - (age - chunk.ExitAt) / ExitSeconds)
                        : 1f;
                if (exit <= 0f)
                {
                    chunk.Retired = true;
                    chunk.Body.gameObject.SetActive(false);
                    continue;
                }

                // Pieces leave by sinking into the floor while they close: an opaque chunk that
                // thins into a ghost stops reading as matter well before it is gone.
                float shrink = Mathf.Pow(exit, 0.55f);
                float sink = (1f - exit) * chunk.Scale.y * 1.35f;

                chunk.Body.SetPositionAndRotation(
                    new Vector3(position.x, position.y - sink, position.z),
                    chunk.Rotation
                );
                chunk.Body.localScale = chunk.Scale * shrink;
            }
        }

        static float ChunkHeight(Chunk chunk, float time, float landAt)
        {
            if (time <= 0f)
                return chunk.LaunchHeight;

            if (time < chunk.RiseSeconds)
            {
                float rise = time / chunk.RiseSeconds;
                return Mathf.Lerp(chunk.LaunchHeight, chunk.ApexHeight, rise * (2f - rise));
            }

            if (time < landAt)
            {
                float fall = (time - chunk.RiseSeconds) / chunk.FallSeconds;
                return Mathf.Lerp(chunk.ApexHeight, chunk.RestHeight, fall * fall);
            }

            if (chunk.BounceHeight > 0f && chunk.BounceSeconds > 0f)
            {
                float hop = (time - landAt) / chunk.BounceSeconds;
                if (hop < 1f)
                    return chunk.RestHeight + chunk.BounceHeight * 4f * hop * (1f - hop);
            }

            return chunk.RestHeight;
        }

        void StepCards()
        {
            if (cards == null)
                return;

            for (int i = 0; i < cards.Length; i++)
            {
                Card card = cards[i];
                if (card == null || card.Retired || card.Renderer == null || card.Node == null)
                    continue;

                float time = age - card.StartAt;
                if (time < 0f)
                    continue;

                float total = card.GrowSeconds + card.HoldSeconds + card.ClearSeconds;
                if (time >= total)
                {
                    card.Retired = true;
                    card.Node.gameObject.SetActive(false);
                    continue;
                }
                if (!card.Node.gameObject.activeSelf)
                    card.Node.gameObject.SetActive(true);

                float grown = Mathf.Clamp01(time / Mathf.Max(0.001f, card.GrowSeconds));
                float eased = 1f - (1f - grown) * (1f - grown);
                float size = Mathf.Lerp(card.StartSize, card.EndSize, eased);
                float aspect = Mathf.Lerp(card.StartAspect, card.EndAspect, eased);

                Vector3 pushed = card.PushDrag > 0f
                    ? card.Push * card.PushDrag * (1f - Mathf.Exp(-time / card.PushDrag))
                    : Vector3.zero;
                card.Node.position = card.Center + card.Drift * time + pushed;
                card.Node.localScale = new Vector3(size, 1f, size * aspect);

                float held = Mathf.Clamp01(
                    (time - card.GrowSeconds) / Mathf.Max(0.001f, card.HoldSeconds)
                );
                float clearing = Mathf.Clamp01(
                    (time - card.GrowSeconds - card.HoldSeconds) / Mathf.Max(0.001f, card.ClearSeconds)
                );
                float cut = Mathf.Lerp(
                    Mathf.Lerp(card.OpenCut, card.BaseCut + card.Creep * held, eased),
                    1.05f,
                    clearing * clearing
                );

                cardBlock.Clear();
                cardBlock.SetVector(MainTexStId, card.Tiling);
                cardBlock.SetFloat(CutId, cut);
                // Kicked ground stops glowing as it disperses, so the hue leaves with the clear
                // rather than outliving the mass and stranding a coloured tail behind it.
                cardBlock.SetFloat(EmberFillId, card.EmberFill * (1f - clearing));
                cardBlock.SetVector(
                    ToneId,
                    Vector4.Lerp(
                        card.ToneFresh,
                        card.ToneSettled,
                        Mathf.Clamp01(time / Mathf.Max(0.01f, card.SettleSeconds))
                    ) * card.Tone
                );
                card.Renderer.SetPropertyBlock(cardBlock);
            }
        }

        void StepFlames()
        {
            if (flames == null)
                return;

            float fade = Mathf.Clamp01((age - FireHoldSeconds) / FireFadeSeconds);
            float opened = Mathf.Clamp01(age / FireHoldSeconds);
            float eased = 1f - (1f - opened) * (1f - opened);

            for (int i = 0; i < flames.Length; i++)
            {
                Flame flame = flames[i];
                if (flame == null || flame.Retired || flame.Renderer == null || flame.Node == null)
                    continue;

                if (fade >= 1f)
                {
                    flame.Retired = true;
                    flame.Node.gameObject.SetActive(false);
                    continue;
                }

                float size = Mathf.Lerp(flame.StartSize, flame.EndSize, eased) * (1f - 0.22f * fade);
                flame.Node.position = flame.Center + flame.Drift * age;
                flame.Node.localRotation = Quaternion.Euler(0f, flame.Heading + flame.Spin * age, 0f);
                flame.Node.localScale = new Vector3(size, 1f, size * flame.Aspect);

                cardBlock.Clear();
                cardBlock.SetVector(MainTexStId, flame.Tiling);
                cardBlock.SetFloat(CutId, Mathf.Lerp(0.30f, 1.08f, fade * fade));
                cardBlock.SetFloat(CoreCutId, Mathf.Lerp(0.52f, 1.06f, fade));
                cardBlock.SetVector(ToneId, Vector4.Lerp(FireBodyTone, FireAshTone, fade));
                flame.Renderer.SetPropertyBlock(cardBlock);
            }
        }

        /// <summary>
        /// Runs the burn out from the blast, then lets it die in pieces. Every patch of the mark
        /// carries a rank for how long it holds heat, and one clock walks that rank: coals go out
        /// seam by seam while the char greys and flakes behind them, and each is on its own rate.
        /// Nothing in the tail is a global dim, and the erosion is never cancelled by the spread.
        /// </summary>
        void StepScorch()
        {
            if (scorchNode == null || coalNode == null)
                return;

            float running = Mathf.Clamp01(age / ScorchSpreadSeconds);
            float spread = 1f - (1f - running) * (1f - running);
            // Soot settles outward for a moment after the fire stops throwing it and then stops,
            // which leaves the rest of the tail to the erosion alone.
            float creep = 1f + 0.055f
                * Mathf.Clamp01((age - ScorchSpreadSeconds) / ScorchCreepSeconds);
            float width = Mathf.Lerp(scorchStartWidth, scorchFullWidth, spread) * creep;

            float cool = Mathf.Max(0f, (age - ScorchCoolStart) * ScorchCoolRate);
            float faded = Mathf.Clamp01((age - ScorchFadeStart) / ScorchFadeSeconds);
            // Creeps past the mark's thinnest grain only at the very end, so the burn breaks up as
            // it goes rather than dimming as one piece.
            float cut = Mathf.Lerp(0.10f, 0.52f, Mathf.Clamp01(age / LifeSeconds));
            float settling = Mathf.Max(0f, age - ScorchBedStart);
            float bedded = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(settling / ScorchBedSeconds));

            scorchNode.localScale = new Vector3(width, 1f, width * scorchAspect);
            scorchMaterial.SetFloat(CoolId, cool);
            scorchMaterial.SetFloat(DensityId, 1f - faded);
            scorchMaterial.SetFloat(CutId, cut);

            if (bedNode != null && bedMaterial != null)
            {
                bedNode.localScale = new Vector3(width, 1f, width * scorchAspect);
                bedMaterial.SetFloat(CoolId, cool);
                bedMaterial.SetFloat(DensityId, 1f - faded);
                bedMaterial.SetFloat(CutId, cut);
                bedMaterial.SetFloat(LayId, settling * ScorchBedRate);
            }

            // Coals sit a little inside the char and shrink away from it, so the burn's rim goes
            // cold first the way a real one does.
            float coalWidth = width * Mathf.Lerp(0.97f, 0.88f, Mathf.Clamp01(age / LifeSeconds));
            float lit = Mathf.Clamp01(age / 0.09f);
            float goingOut = Mathf.Min(
                ScorchCoalOutTo,
                ScorchCoalOutFrom + Mathf.Max(0f, age - ScorchCoalOutStart) * ScorchCoalOutRate
            );

            coalNode.localScale = new Vector3(coalWidth, 1f, coalWidth * scorchAspect);
            coalMaterial.SetFloat(
                GlowId,
                Mathf.Lerp(0.62f, 1f, lit) * (1f - 0.32f * Mathf.Clamp01((age - 0.50f) / 0.66f))
            );
            coalMaterial.SetFloat(
                CutId,
                Mathf.Lerp(0.14f, 0.46f, Mathf.Clamp01(settling / (LifeSeconds - ScorchBedStart)))
            );
            coalMaterial.SetFloat(OutId, goingOut);
            // Embers breathe once the flame over them is gone. Held off until then so the frame
            // the strip cuts for the impact carries exactly the coal bed it did before.
            coalMaterial.SetFloat(FlickerId, ScorchEmberFlicker * bedded);
            coalMaterial.SetFloat(PhaseId, age * ScorchEmberRate);
        }

        /// <summary>
        /// Claims a dormant card for the dust a landing chunk throws up. This is what says the floor
        /// was hit rather than that a rock happened to arrive on it.
        /// </summary>
        void KickLandingDust(Vector3 position, float width)
        {
            if (cards == null)
                return;

            for (int i = landingStart; i < landingEnd; i++)
            {
                Card card = cards[i];
                if (card == null || card.Retired || card.Node == null)
                    continue;
                if (!float.IsPositiveInfinity(card.StartAt))
                    continue;

                card.StartAt = age;
                card.Center = new Vector3(position.x, LandingDustHeight, position.z);
                card.Drift = new Vector3(
                    Random.Range(-0.4f, 0.4f),
                    Random.Range(0.35f, 0.7f),
                    Random.Range(-0.4f, 0.4f)
                );
                // Small on purpose: a hero chunk should throw up about half its own width of dirt,
                // not stage a second explosion in the aftermath layer's slot.
                card.StartSize = width * 0.5f;
                card.EndSize = width * 1.45f;
                card.Node.position = card.Center;
                return;
            }
        }

        void OnDestroy()
        {
            DestroyOwned(chunkMaterial);
            DestroyOwned(surgeMaterial);
            DestroyOwned(scorchMaterial);
            DestroyOwned(bedMaterial);
            DestroyOwned(coalMaterial);
            DestroyOwned(streakMaterial);
            DestroyOwned(emberMaterial);
            DestroyOwned(surgeTexture);
            DestroyOwned(scorchTexture);
            DestroyOwned(streakTexture);
            DestroyOwned(emberTexture);
            DestroyOwned(cardMesh);
            DestroyAll(dustMaterials);
            DestroyAll(fireMaterials);
            DestroyAll(dustTextures);
            DestroyAll(fireTextures);
            DestroyAll(chunkMeshes);
        }

        static void DestroyAll<T>(T[] owned)
            where T : Object
        {
            if (owned == null)
                return;
            foreach (T item in owned)
                DestroyOwned(item);
        }

        static void DestroyOwned(Object owned)
        {
            if (owned != null)
                Destroy(owned);
        }

        float SampleAngle(int index, int total)
        {
            // Deliberately uneven. Evenly spaced throws read as a ring, and a ring with an empty
            // middle is exactly what got the first pass called confetti.
            float slot = (index + Random.Range(-0.45f, 0.45f)) / Mathf.Max(1, total);
            return biased ? biasDegrees + (slot * 2f - 1f) * BiasConeDegrees : slot * 360f;
        }

        ParticleSystem BuildSystem(
            string name,
            Material material,
            ParticleSystemRenderMode renderMode,
            float duration,
            int maxParticles
        )
        {
            GameObject host = new(name);
            host.transform.SetParent(transform, worldPositionStays: false);

            ParticleSystem system = host.AddComponent<ParticleSystem>();
            if (system == null)
                return null;

            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = duration;
            main.startSpeed = 0f;
            main.startSize = 0.2f;
            main.startLifetime = 0.3f;
            main.maxParticles = maxParticles;
            main.gravityModifier = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.stopAction = ParticleSystemStopAction.None;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;

            ParticleSystemRenderer systemRenderer = host.GetComponent<ParticleSystemRenderer>();
            if (systemRenderer != null)
            {
                systemRenderer.renderMode = renderMode;
                systemRenderer.sharedMaterial = material;
                systemRenderer.shadowCastingMode = ShadowCastingMode.Off;
                systemRenderer.receiveShadows = false;
                systemRenderer.sortMode = ParticleSystemSortMode.None;
                systemRenderer.alignment = ParticleSystemRenderSpace.View;
            }

            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return system;
        }

        /// <summary>
        /// A unit card lying in the ground plane. The board camera looks down at 73 degrees, so a
        /// flat card is within a few percent of a camera-facing one and needs no camera to track.
        /// </summary>
        static Mesh BuildCardMesh()
        {
            Mesh mesh = new() { name = "DebrisCard" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, -0.5f),
            };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f),
            };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        static readonly Vector3[] SeedCorners =
        {
            new(-1f, 1.618034f, 0f),
            new(1f, 1.618034f, 0f),
            new(-1f, -1.618034f, 0f),
            new(1f, -1.618034f, 0f),
            new(0f, -1f, 1.618034f),
            new(0f, 1f, 1.618034f),
            new(0f, -1f, -1.618034f),
            new(0f, 1f, -1.618034f),
            new(1.618034f, 0f, -1f),
            new(1.618034f, 0f, 1f),
            new(-1.618034f, 0f, -1f),
            new(-1.618034f, 0f, 1f),
        };

        static readonly int[][] SeedFaces =
        {
            new[] { 0, 11, 5 },
            new[] { 0, 5, 1 },
            new[] { 0, 1, 7 },
            new[] { 0, 7, 10 },
            new[] { 0, 10, 11 },
            new[] { 1, 5, 9 },
            new[] { 5, 11, 4 },
            new[] { 11, 10, 2 },
            new[] { 10, 7, 6 },
            new[] { 7, 1, 8 },
            new[] { 3, 9, 4 },
            new[] { 3, 4, 2 },
            new[] { 3, 2, 6 },
            new[] { 3, 6, 8 },
            new[] { 3, 8, 9 },
            new[] { 4, 9, 5 },
            new[] { 2, 4, 11 },
            new[] { 6, 2, 10 },
            new[] { 8, 6, 7 },
            new[] { 9, 8, 1 },
        };

        /// <summary>
        /// A flat-shaded lump with every corner pushed somewhere different and a couple pulled well
        /// out. Clean cubes and octahedra read as placeholder; irregular, slightly flattened rubble
        /// with a few sharp corners reads as broken material. Normalised so the widest horizontal
        /// span is exactly one unit, which lets the caller ask for a size in cells and get it.
        /// </summary>
        static Mesh BuildChunkMesh()
        {
            Vector3 squash = new(1f, Random.Range(0.52f, 0.74f), 1f);
            Vector3[] corners = new Vector3[SeedCorners.Length];
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 direction = SeedCorners[i].normalized;
                corners[i] =
                    Vector3.Scale(direction * Random.Range(0.62f, 1.08f), squash)
                    + Random.insideUnitSphere * 0.08f;
            }

            int spikes = Random.Range(2, 4);
            for (int i = 0; i < spikes; i++)
                corners[Random.Range(0, corners.Length)] *= Random.Range(1.3f, 1.7f);

            float widest = 0.0001f;
            foreach (Vector3 corner in corners)
                widest = Mathf.Max(widest, Mathf.Max(Mathf.Abs(corner.x), Mathf.Abs(corner.z)));
            float normalise = 0.5f / widest;
            for (int i = 0; i < corners.Length; i++)
                corners[i] *= normalise;

            int vertexCount = SeedFaces.Length * 3;
            Vector3[] vertices = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            Color[] colors = new Color[vertexCount];
            int[] triangles = new int[vertexCount];

            int vertexIndex = 0;
            foreach (int[] face in SeedFaces)
            {
                Vector3 first = corners[face[0]];
                Vector3 second = corners[face[1]];
                Vector3 third = corners[face[2]];

                // Every lump still wraps the origin, so a face's own centre says which way is out
                // and the winding can be corrected instead of hand-ordered.
                Vector3 normal = Vector3.Cross(second - first, third - first).normalized;
                if (Vector3.Dot(normal, (first + second + third) / 3f) < 0f)
                {
                    normal = -normal;
                    (second, third) = (third, second);
                }

                Color facet = new(
                    Random.Range(0.21f, 0.77f),
                    Random.value < 0.32f ? Random.Range(0.6f, 1f) : 0f,
                    0f,
                    1f
                );

                vertices[vertexIndex] = first;
                normals[vertexIndex] = normal;
                colors[vertexIndex] = facet;
                triangles[vertexIndex] = vertexIndex;
                vertexIndex++;

                vertices[vertexIndex] = second;
                normals[vertexIndex] = normal;
                colors[vertexIndex] = facet;
                triangles[vertexIndex] = vertexIndex;
                vertexIndex++;

                vertices[vertexIndex] = third;
                normals[vertexIndex] = normal;
                colors[vertexIndex] = facet;
                triangles[vertexIndex] = vertexIndex;
                vertexIndex++;
            }

            Mesh mesh = new() { name = "DebrisChunkMesh" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Random harmonics describing one closed, off-centre outline. A mass built by unioning
        /// discs always shows its discs; pushing a single boundary around with a first harmonic in
        /// it gives a lumpy shape with no circular arc anywhere on it.
        /// </summary>
        static float[] BuildOutline(int harmonics, float spread)
        {
            float[] terms = new float[harmonics * 2];
            for (int k = 0; k < harmonics; k++)
            {
                terms[k * 2] = Random.Range(0.35f, 1f) * spread / (1f + k * 0.7f);
                terms[k * 2 + 1] = Random.Range(0f, Mathf.PI * 2f);
            }
            return terms;
        }

        static float SampleOutline(float[] terms, float angle)
        {
            float sum = 0f;
            for (int k = 0; k < terms.Length / 2; k++)
                sum += terms[k * 2] * Mathf.Sin((k + 1) * angle + terms[k * 2 + 1]);
            return sum;
        }

        /// <summary>
        /// One billow of kicked dust. Packs the hot interior in red, the lobe's own value
        /// structure in green, and the silhouette in alpha. Alpha falls off fast enough that any
        /// cut is a two-pixel step, and it is broken up at grain scale so raising the cut erodes
        /// the lobe into hard islands instead of dissolving it evenly.
        /// <para>
        /// Most of green's variance is deliberately at grain scale rather than in the broad ramp.
        /// A lobe can hold a wide range of values across its whole width and still read as a flat
        /// cut-out, because what a viewer sees as texture is contrast inside a few pixels, not
        /// contrast across a hand's width. The lobe is also punched through: real dust has gaps.
        /// </para>
        /// </summary>
        static Texture2D BuildPuffTexture(int size, int voidResolution, float voidThreshold)
        {
            float[] outline = BuildOutline(5, 0.28f);
            const float baseRadius = 0.72f;

            float biteAngle = Random.Range(0f, Mathf.PI * 2f);
            Vector2 biteCenter = new(Mathf.Cos(biteAngle) * 0.70f, Mathf.Sin(biteAngle) * 0.70f);
            float biteRadius = Random.Range(0.22f, 0.34f);

            float fragmentAngle = biteAngle + Random.Range(1.6f, 4.7f);
            Vector2 fragmentCenter = new(
                Mathf.Cos(fragmentAngle) * Random.Range(0.66f, 0.78f),
                Mathf.Sin(fragmentAngle) * Random.Range(0.66f, 0.78f)
            );
            float fragmentRadius = Random.Range(0.08f, 0.14f);

            // The hot interior is pushed off the middle and given its own ragged outline. A
            // concentric hot disc inside a lobe is the lens-flare read all over again.
            float emberAngle = Random.Range(0f, Mathf.PI * 2f);
            float emberOffset = Random.Range(0.04f, 0.19f);
            Vector2 emberCenter = new(
                Mathf.Cos(emberAngle) * emberOffset,
                Mathf.Sin(emberAngle) * emberOffset
            );
            float[] emberOutline = BuildOutline(4, 0.34f);
            float emberRadius = Random.Range(0.40f, 0.48f);

            const int coarseResolution = 7;
            const int fineResolution = 26;
            const int speckResolution = 40;
            float[] coarse = BuildNoiseGrid(coarseResolution);
            float[] fine = BuildNoiseGrid(fineResolution);
            float[] speck = BuildNoiseGrid(speckResolution);
            float[] voids = BuildNoiseGrid(voidResolution);

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float down = (y + 0.5f) / size * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float across = (x + 0.5f) / size * 2f - 1f;
                    float u = (across + 1f) * 0.5f;
                    float v = (down + 1f) * 0.5f;

                    float coarseNoise = SampleNoise(coarse, coarseResolution, u, v);
                    float fineNoise = SampleNoise(fine, fineResolution, u, v);
                    float speckNoise = SampleNoise(speck, speckResolution, u, v);
                    float clumpNoise = SampleNoise(
                        coarse,
                        coarseResolution,
                        u * 2.2f + 0.31f,
                        v * 2.2f + 0.67f
                    );

                    float radius = Mathf.Sqrt(across * across + down * down);
                    float angle = Mathf.Atan2(down, across);
                    float boundary = Mathf.Clamp(
                        baseRadius + SampleOutline(outline, angle),
                        0.36f,
                        0.82f
                    );

                    float field = boundary - radius
                        + (coarseNoise - 0.5f) * 0.10f
                        + (fineNoise - 0.5f) * 0.05f;
                    field = Mathf.Min(field, Distance(across, down, biteCenter) - biteRadius);
                    field = Mathf.Max(
                        field,
                        fragmentRadius - Distance(across, down, fragmentCenter)
                    );

                    float mass = Mathf.Clamp01(field * 3.4f + 0.62f);
                    float grain = 0.55f + 0.62f * fineNoise;
                    float border = Mathf.Clamp01(
                        (1f - Mathf.Max(Mathf.Abs(across), Mathf.Abs(down))) * 10f
                    );
                    float alpha = Mathf.Clamp01(mass * grain) * border;
                    float depth = Mathf.Clamp01(radius / Mathf.Max(0.05f, boundary));

                    // Gaps torn through the outer half of the lobe, on the same short ramp as the
                    // silhouette so a hole has a rim rather than a halo. They are kept out of the
                    // middle deliberately: that is where the smaller lobes sit, and a hole with
                    // another lobe behind it is not a hole.
                    float interior =
                        Mathf.Clamp01((field - 0.05f) * 8f) * Mathf.Clamp01(depth * 2.2f - 0.35f);
                    float carve =
                        Mathf.Clamp01((SampleNoise(voids, voidResolution, u, v) - voidThreshold) * 8f)
                        * interior;
                    alpha = Mathf.Min(alpha, 1f - carve);

                    float lit = Mathf.Clamp01(0.5f + 0.60f * across + 0.24f * (coarseNoise - 0.5f));
                    float value = Mathf.Clamp01(
                        0.28f
                        + 0.42f * lit
                        + 0.40f * (fineNoise - 0.5f)
                        + 0.34f * (speckNoise - 0.5f)
                        + 0.22f * (clumpNoise - 0.5f)
                        - 0.16f * depth * depth
                    );

                    float emberHere = Distance(across, down, emberCenter);
                    float emberEdge = Mathf.Max(
                        0.10f,
                        emberRadius
                            + SampleOutline(
                                emberOutline,
                                Mathf.Atan2(down - emberCenter.y, across - emberCenter.x)
                            ) * emberRadius
                    );
                    // Ragged at its boundary but a single coherent region, not a scatter: one
                    // large hot mass reads as a molten interior, a hundred hot specks read as
                    // confetti and measure as nothing.
                    float ember = Mathf.Clamp01(
                        (emberEdge - emberHere) * 2.4f + 0.5f
                        + (fineNoise - 0.5f) * 0.40f
                        + (clumpNoise - 0.5f) * 0.28f
                    );

                    pixels[y * size + x] = new Color(ember, value, 0f, alpha);
                }
            }
            return BuildSprite("DebrisPuffSprite", size, size, pixels);
        }

        /// <summary>
        /// The skirt of dust that runs out along the floor: narrow where it left the crater, wide
        /// and ragged at the front, with fingers rather than a rolled hem.
        /// </summary>
        static Texture2D BuildSurgeTexture(int size)
        {
            const int coarseResolution = 6;
            const int fineResolution = 20;
            float[] coarse = BuildNoiseGrid(coarseResolution);
            float[] fine = BuildNoiseGrid(fineResolution);
            float[] fingers = BuildNoiseGrid(5);

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float down = (y + 0.5f) / size * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float across = (x + 0.5f) / size * 2f - 1f;
                    float u = (across + 1f) * 0.5f;
                    float v = (down + 1f) * 0.5f;

                    float coarseNoise = SampleNoise(coarse, coarseResolution, u, v);
                    float fineNoise = SampleNoise(fine, fineResolution, u, v);

                    // Wedge: pinched to nothing at the trailing end, broadest just short of the
                    // front, and torn into fingers along the leading edge.
                    float along = Mathf.Clamp01(v);
                    float spread =
                        Mathf.Clamp01(along * 5.2f)
                        * Mathf.Clamp01(0.12f + 1.5f * along)
                        * Mathf.Clamp01(1.30f - along * 0.5f)
                        * 0.80f;
                    float front = 0.86f
                        + 0.14f * (SampleNoise(fingers, 5, u * 1.7f, 0.31f) - 0.5f);
                    float body = spread - Mathf.Abs(across);
                    float cap = front - along;
                    float field = Mathf.Min(body, cap * 2.2f)
                        + (coarseNoise - 0.5f) * 0.13f
                        + (fineNoise - 0.5f) * 0.06f;

                    float mass = Mathf.Clamp01(field * 3.1f + 0.60f);
                    float grain = 0.52f + 0.66f * fineNoise;
                    float border = Mathf.Clamp01(
                        (1f - Mathf.Max(Mathf.Abs(across), Mathf.Abs(down))) * 10f
                    );
                    float alpha = Mathf.Clamp01(mass * grain) * border;

                    float speckNoise = SampleNoise(
                        fine,
                        fineResolution,
                        u * 1.9f + 0.43f,
                        v * 1.9f + 0.17f
                    );
                    float lit = Mathf.Clamp01(0.5f + 0.60f * across + 0.26f * (coarseNoise - 0.5f));
                    float value = Mathf.Clamp01(
                        0.30f
                        + 0.40f * lit
                        + 0.38f * (fineNoise - 0.5f)
                        + 0.30f * (speckNoise - 0.5f)
                        - 0.18f * along
                    );

                    // Hot only where the skirt left the crater; by the torn front it is cold dirt.
                    float ember = Mathf.Clamp01(
                        (0.42f - along) * 3.4f + (fineNoise - 0.5f) * 0.44f
                    );

                    pixels[y * size + x] = new Color(ember, value, 0f, alpha);
                }
            }
            return BuildSprite("DebrisSurgeSprite", size, size, pixels);
        }

        /// <summary>
        /// The burn left on the floor. Alpha is the mark, green the char's density, red a network
        /// of coal seams the additive layer lights up, and blue how long each patch holds its
        /// heat. All four are deliberately patchy: a mark with one flat interior reads as a
        /// sticker whatever colour it is, the coals have to be filaments rather than a disc or the
        /// burn reads as a puddle, and a burn that cools everywhere at once has no tail at all.
        /// </summary>
        static Texture2D BuildScorchTexture(int size)
        {
            float[] outline = BuildOutline(5, 0.26f);
            const float baseRadius = 0.74f;

            // Two extra bulges and a bite, so the mark is lopsided rather than a blown-up circle.
            float bulgeAngle = Random.Range(0f, Mathf.PI * 2f);
            Vector2 bulgeOne = new(
                Mathf.Cos(bulgeAngle) * Random.Range(0.30f, 0.46f),
                Mathf.Sin(bulgeAngle) * Random.Range(0.30f, 0.46f)
            );
            float bulgeOneRadius = Random.Range(0.30f, 0.42f);
            float bulgeTwoAngle = bulgeAngle + Random.Range(1.9f, 4.4f);
            Vector2 bulgeTwo = new(
                Mathf.Cos(bulgeTwoAngle) * Random.Range(0.34f, 0.52f),
                Mathf.Sin(bulgeTwoAngle) * Random.Range(0.34f, 0.52f)
            );
            float bulgeTwoRadius = Random.Range(0.20f, 0.30f);
            float biteAngle = bulgeAngle + Mathf.PI + Random.Range(-0.7f, 0.7f);
            Vector2 biteCenter = new(
                Mathf.Cos(biteAngle) * 0.78f,
                Mathf.Sin(biteAngle) * 0.78f
            );
            float biteRadius = Random.Range(0.24f, 0.36f);

            // One side of the burn outlives the other, and it is the side with the most matter on
            // it. Without that lean the mark erodes concentrically and its centroid never moves,
            // which reads exactly like not eroding at all.
            Vector2 coolHeading = new(Mathf.Cos(bulgeAngle), Mathf.Sin(bulgeAngle));

            const int coarseResolution = 7;
            const int fineResolution = 22;
            const int veinResolution = 13;
            float[] coarse = BuildNoiseGrid(coarseResolution);
            float[] fine = BuildNoiseGrid(fineResolution);
            float[] vein = BuildNoiseGrid(veinResolution);

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float down = (y + 0.5f) / size * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float across = (x + 0.5f) / size * 2f - 1f;
                    float u = (across + 1f) * 0.5f;
                    float v = (down + 1f) * 0.5f;

                    float coarseNoise = SampleNoise(coarse, coarseResolution, u, v);
                    float fineNoise = SampleNoise(fine, fineResolution, u, v);

                    float radius = Mathf.Sqrt(across * across + down * down);
                    float angle = Mathf.Atan2(down, across);
                    float boundary = Mathf.Clamp(
                        baseRadius + SampleOutline(outline, angle),
                        0.38f,
                        0.86f
                    );

                    float field = boundary - radius
                        + (coarseNoise - 0.5f) * 0.15f
                        + (fineNoise - 0.5f) * 0.06f;
                    field = Mathf.Max(field, bulgeOneRadius - Distance(across, down, bulgeOne));
                    field = Mathf.Max(field, bulgeTwoRadius - Distance(across, down, bulgeTwo));
                    field = Mathf.Min(field, Distance(across, down, biteCenter) - biteRadius);

                    float mass = Mathf.Clamp01(field * 3.0f + 0.56f);
                    float border = Mathf.Clamp01(
                        (1f - Mathf.Max(Mathf.Abs(across), Mathf.Abs(down))) * 10f
                    );
                    float alpha = Mathf.Clamp01(mass * (0.44f + 0.70f * fineNoise)) * border;

                    float depth = Mathf.Clamp01(radius / Mathf.Max(0.05f, boundary));
                    float charred = Mathf.Clamp01(
                        0.26f
                        + 0.46f * (1f - depth * depth)
                        + 0.36f * (coarseNoise - 0.5f)
                        + 0.26f * (fineNoise - 0.5f)
                    );

                    // Ridged noise: the crest runs along a contour rather than pooling at a
                    // maximum, so the coals come out as connected cracks through the char.
                    float ridge = 1f - Mathf.Abs(
                        SampleNoise(vein, veinResolution, u * 1.15f + 0.19f, v * 1.15f + 0.53f) - 0.5f
                    ) * 2f;
                    float coal = Mathf.Clamp01((ridge - 0.30f) * 2.2f)
                        * Mathf.Clamp01(0.52f + 0.72f * (1f - depth))
                        + Mathf.Clamp01((fineNoise - 0.62f) * 2.6f) * 0.5f;
                    // Coals only where the ground is charred. Glowing on ash the multiply barely
                    // touched would add light to a near-white floor, and that is how a saturated
                    // ember turns into a pale pink wash.
                    coal *= Mathf.Clamp01(0.30f + 0.95f * charred);
                    float coalStrength = Mathf.Clamp01(coal);

                    // Heat rank: the middle of the burn and the coal seams hold on, the rim and
                    // the downwind side let go first. Spread wide enough that the clock walking it
                    // is always partway through some patch.
                    float heatRank = Mathf.Clamp01(
                        0.30f
                        + 0.34f * (1f - depth)
                        + 0.30f * coalStrength
                        + 0.17f * (across * coolHeading.x + down * coolHeading.y)
                        + 0.26f * (coarseNoise - 0.5f)
                        + 0.14f * (fineNoise - 0.5f)
                    );

                    pixels[y * size + x] = new Color(coalStrength, charred, heatRank, alpha);
                }
            }
            return BuildSprite("DebrisScorchSprite", size, size, pixels);
        }

        /// <summary>
        /// Burning ground. Red carries a small off-centre core the shader erodes rather than dims,
        /// green the body's own hot-to-cool structure, alpha a torn silhouette. Deliberately not
        /// concentric: fire drawn as rings around a centre reads as a lens flare.
        /// </summary>
        static Texture2D BuildFireTexture(int size)
        {
            float[] outline = BuildOutline(6, 0.30f);
            const float baseRadius = 0.70f;

            // The white core is a torn cluster of a few percent of the card, pushed off the middle.
            // It only has to clip; everything around it is the saturated colour a bigger, rounder
            // core would bleach away, and one clean disc of white reads as a lens flare.
            float coreAngle = Random.Range(0f, Mathf.PI * 2f);
            Vector2[] coreCenters = new Vector2[3];
            float[] coreRadii = new float[3];
            float[][] coreOutlines = new float[3][];
            for (int i = 0; i < 3; i++)
            {
                float lobeAngle = coreAngle + i * 2.39996f + Random.Range(-0.6f, 0.6f);
                float lobeOffset = i == 0
                    ? Random.Range(0.03f, 0.14f)
                    : Random.Range(0.20f, 0.34f);
                coreCenters[i] = new Vector2(
                    Mathf.Cos(lobeAngle) * lobeOffset,
                    Mathf.Sin(lobeAngle) * lobeOffset
                );
                coreRadii[i] = i == 0 ? Random.Range(0.24f, 0.29f) : Random.Range(0.11f, 0.17f);
                coreOutlines[i] = BuildOutline(3, 0.30f);
            }

            const int coarseResolution = 6;
            const int fineResolution = 18;
            float[] coarse = BuildNoiseGrid(coarseResolution);
            float[] fine = BuildNoiseGrid(fineResolution);

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float down = (y + 0.5f) / size * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float across = (x + 0.5f) / size * 2f - 1f;
                    float u = (across + 1f) * 0.5f;
                    float v = (down + 1f) * 0.5f;

                    float coarseNoise = SampleNoise(coarse, coarseResolution, u, v);
                    float fineNoise = SampleNoise(fine, fineResolution, u, v);

                    float radius = Mathf.Sqrt(across * across + down * down);
                    float angle = Mathf.Atan2(down, across);
                    float boundary = Mathf.Clamp(
                        baseRadius + SampleOutline(outline, angle),
                        0.34f,
                        0.80f
                    );

                    float field = boundary - radius
                        + (coarseNoise - 0.5f) * 0.14f
                        + (fineNoise - 0.5f) * 0.07f;
                    float mass = Mathf.Clamp01(field * 3.6f + 0.62f);
                    float grain = 0.56f + 0.60f * fineNoise;
                    float border = Mathf.Clamp01(
                        (1f - Mathf.Max(Mathf.Abs(across), Mathf.Abs(down))) * 10f
                    );
                    float alpha = Mathf.Clamp01(mass * grain) * border;

                    float core = 0f;
                    for (int lobe = 0; lobe < coreCenters.Length; lobe++)
                    {
                        Vector2 center = coreCenters[lobe];
                        float coreDistance = Distance(across, down, center);
                        float coreAngleHere = Mathf.Atan2(down - center.y, across - center.x);
                        float coreEdge = Mathf.Max(
                            0.06f,
                            coreRadii[lobe]
                                + SampleOutline(coreOutlines[lobe], coreAngleHere) * coreRadii[lobe]
                        );
                        core = Mathf.Max(
                            core,
                            Mathf.Clamp01(
                                1.35f * (1f - Mathf.Pow(Mathf.Clamp01(coreDistance / coreEdge), 1.5f))
                            )
                        );
                    }

                    // Burning matter, not a glowing disc: turbulence over the hot-to-cool falloff
                    // and dark fissures cut through it, so the body has structure of its own.
                    float depth = Mathf.Clamp01(radius / Mathf.Max(0.05f, boundary));
                    float turbulence =
                        0.62f * fineNoise
                        + 0.38f * SampleNoise(coarse, coarseResolution, u * 1.9f + 0.37f, v * 1.9f + 0.11f);
                    float fissure = Mathf.Abs(
                        SampleNoise(fine, fineResolution, u * 0.8f + 0.53f, v * 0.8f + 0.29f) - 0.5f
                    ) * 2f;
                    float value = Mathf.Clamp01(
                        0.04f
                        + 0.62f * (1f - Mathf.Pow(depth, 1.4f))
                        + 0.40f * turbulence
                        - 0.32f * (1f - fissure)
                    );

                    pixels[y * size + x] = new Color(core, value, 0f, alpha);
                }
            }
            return BuildSprite("DebrisFireSprite", size, size, pixels);
        }

        static Texture2D BuildStreakTexture(int width, int height)
        {
            Color[] pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                float across = (y + 0.5f) / height * 2f - 1f;
                float profile = Mathf.Clamp01(1f - Mathf.Pow(Mathf.Abs(across), 2.6f));
                for (int x = 0; x < width; x++)
                {
                    float along = (x + 0.5f) / width;
                    float taper = Mathf.Pow(Mathf.Clamp01(Mathf.Sin(along * Mathf.PI)), 0.45f);
                    float alpha = Mathf.Clamp01(profile * taper);
                    float ember =
                        Mathf.Exp(-(along - 0.63f) * (along - 0.63f) / 0.01f) * alpha;
                    float value = 0.62f + 0.38f * Mathf.Clamp01(1f - Mathf.Abs(across));
                    pixels[y * width + x] = new Color(ember, value, 0f, alpha);
                }
            }
            return BuildSprite("DebrisStreakSprite", width, height, pixels);
        }

        static Texture2D BuildEmberTexture(int size)
        {
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float down = (y + 0.5f) / size * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float across = (x + 0.5f) / size * 2f - 1f;
                    float distance = Mathf.Sqrt(across * across + down * down);
                    float body = Mathf.Pow(Mathf.Clamp01(1f - distance), 1.9f);
                    float core = Mathf.Exp(-distance * distance * 22f);

                    // White only at the very centre; saturated ember owns everything around it.
                    Color hue = Color.Lerp(
                        new Color(1f, 0.17f, 0.01f),
                        Color.white,
                        Mathf.Clamp01(core * 1.6f)
                    );
                    pixels[y * size + x] = new Color(
                        hue.r,
                        hue.g,
                        hue.b,
                        Mathf.Clamp01(body * 0.85f + core)
                    );
                }
            }
            return BuildSprite("DebrisEmberSprite", size, size, pixels);
        }

        static float Distance(float across, float down, Vector2 center)
        {
            float dx = across - center.x;
            float dy = down - center.y;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        static Texture2D BuildSprite(string name, int width, int height, Color[] pixels)
        {
            // Built linear: these channels are masks and authored tones, not colours to be decoded.
            Texture2D texture = new(width, height, TextureFormat.RGBA32, false, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            texture.SetPixels(pixels);
            texture.Apply(updateMipmaps: false);
            return texture;
        }

        static float[] BuildNoiseGrid(int resolution)
        {
            float[] grid = new float[resolution * resolution];
            for (int i = 0; i < grid.Length; i++)
                grid[i] = Random.value;
            return grid;
        }

        static float SampleNoise(float[] grid, int resolution, float x, float y)
        {
            float sampleX = x * resolution;
            float sampleY = y * resolution;
            int columnLow = Mathf.FloorToInt(sampleX);
            int rowLow = Mathf.FloorToInt(sampleY);

            float acrossFraction = sampleX - columnLow;
            float downFraction = sampleY - rowLow;
            acrossFraction = acrossFraction * acrossFraction * (3f - 2f * acrossFraction);
            downFraction = downFraction * downFraction * (3f - 2f * downFraction);

            int left = Wrap(columnLow, resolution);
            int right = Wrap(columnLow + 1, resolution);
            int bottom = Wrap(rowLow, resolution) * resolution;
            int top = Wrap(rowLow + 1, resolution) * resolution;

            return Mathf.Lerp(
                Mathf.Lerp(grid[bottom + left], grid[bottom + right], acrossFraction),
                Mathf.Lerp(grid[top + left], grid[top + right], acrossFraction),
                downFraction
            );
        }

        static int Wrap(int value, int resolution) => ((value % resolution) + resolution) % resolution;
    }
}
