using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes every FX sprite mask in the project from code.
///
///     Battle Plan ▸ FX ▸ Bake Sprite Masks
///
/// These are masks, not pictures: the shape lives in the alpha channel, RGB is flat white, and the
/// colour is applied at runtime from FXPalette. That makes each one a mathematical function, so it
/// is authored here as ~20 lines of maths rather than a binary blob nobody can diff or retune.
/// A tweak is a number in the block below, not a re-roll, and the result is bit-identical on every
/// machine — the noise is a hash of the pixel coordinate, not System.Random and not
/// Mathf.PerlinNoise, whose output Unity does not contract to stay stable across versions.
///
/// T_FX_Noise is the one exception to the layout: it carries its value in RGB with alpha at 1,
/// because shaders sample it as a scalar field through .r rather than as a coverage mask.
///
/// TUNING NOTE. The PNGs already in Assets/Textures/FX were produced by an earlier throwaway
/// script and this baker supersedes them. Running it is expected to change two of the seven:
/// MuzzleStar gets sharper arms, because at the old sharpness it read as a lens flare rather than
/// a muzzle spike, and Scorch gets a harder, crumbling rim, because the old soft edge read as a
/// cloud rather than a burn. Both are improvements and should be accepted. The other five come out
/// visually equivalent. After that first bake this file is the source of truth, and any subsequent
/// unexplained change means a constant has drifted.
///
/// The organic masks are deliberately not clean. A perfect analytic gradient reads as
/// computer-generated in exactly the way a painted or photographed one does not, so smoke, scorch
/// and the streak carry noise, asymmetry and edge irregularity. The geometric ones — the radial
/// glow, the star, the slash — are left clean, because those should be.
/// </summary>
public static class BattlePlanFXMaskBaker
{
    private const string OutputFolder = "Assets/Textures/FX";

    // =====================================================================================
    // SHAPE CONSTANTS — the whole tuning surface. Nothing below this block holds a magic number.
    // =====================================================================================

    // Radial glow (256²). Clean by intent: it is the base of ground rings and muzzle discs, and
    // any break-up in it would show up in every one of them at once.
    private const int GlowSize = 256;
    private const float GlowFalloffPower = 1.9f;
    private const float GlowPeak = 0.94f;

    // Muzzle star (512²). Four long arms on the axes plus four short ones on the diagonals, over a
    // hot round core. Clean — it is on screen for 0.06 s and only the silhouette registers.
    private const int StarSize = 512;
    private const float StarArmReach = 0.5f;
    private const float StarDiagonalReach = 0.27f;
    private const float StarCoreReach = 0.13f;
    // Sharpness below about 5 stops reading as a muzzle spike and starts reading as a lens flare.
    private const float StarArmSharpness = 6f;
    private const float StarArmFalloff = 2.3f;
    private const float StarCoreFalloff = 2.4f;

    // Spark streak (256x64). Head at u = 1, tail at u = 0, drawn with Stretched Billboard so the
    // long axis lies along velocity. Thin, with a tight head and a smooth taper.
    private const int StreakWidth = 256;
    private const int StreakHeight = 64;
    private const float StreakHeadHalfWidth = 0.26f;
    private const float StreakTailHalfWidth = 0.03f;
    private const float StreakWidthCurve = 2.1f;
    private const float StreakEdgeSoftness = 0.85f;
    private const float StreakTailFalloff = 1.35f;
    private const float StreakHeadCap = 0.09f;
    private const float StreakNoiseAmount = 0.12f;

    // Smoke puff card (256²). Built from overlapping blobs rather than one circle, then broken up
    // by two octaves of noise at different scales — a single circle plus noise still reads as a
    // circle, which is what made the first pass look blocky.
    private const int PuffSize = 256;
    private const float PuffEdgeChaos = 0.22f;
    private const float PuffEdgeSoftness = 0.3f;
    private const float PuffInteriorVariation = 0.34f;
    private const float PuffPeak = 0.95f;
    private const int PuffShapePeriod = 4;
    private const int PuffDetailPeriod = 11;

    // Offset from centre in UV, and radius. Asymmetric on purpose: a symmetric puff reads as a
    // logo. Held as a constant array so the shape is versioned rather than seeded.
    private static readonly Vector3[] PuffBlobs =
    {
        new(0.00f, 0.02f, 0.30f),
        new(-0.16f, -0.07f, 0.22f),
        new(0.17f, 0.05f, 0.20f),
        new(0.04f, 0.18f, 0.17f),
        new(-0.09f, 0.15f, 0.14f),
        new(0.12f, -0.16f, 0.15f),
    };

    // Scorch (512²). Flat and sooty with an irregular edge. Explicitly no bright core — a hot
    // centre turns the burn into a starburst, which is what the first pass looked like.
    private const int ScorchSize = 512;
    private const float ScorchRadius = 0.42f;
    // Two rim terms: broad lobes that make the burn asymmetric, plus finer nibbles. One term alone
    // gives either a dented circle or a starburst, depending which frequency you pick.
    private const float ScorchRimVariation = 0.2f;
    private const float ScorchRimDetail = 0.075f;
    // A soft edge turns the burn into a cloud. It needs a boundary.
    private const float ScorchEdgeSoftness = 0.05f;
    // Grain is thresholded rather than scaled, so it punches actual holes in the rim instead of
    // dimming it uniformly — that is the difference between a crumbling burn and a blurry disc.
    private const float ScorchGrainLow = 0.3f;
    private const float ScorchGrainHigh = 0.8f;
    private const float ScorchMottle = 0.26f;
    private const float ScorchPeak = 0.94f;
    private const int ScorchRimPeriod = 3;
    private const int ScorchRimDetailPeriod = 8;
    private const int ScorchMottlePeriod = 9;
    private const int ScorchGrainPeriod = 26;

    // Slash (256²). A struck arc for the heavy-hit beat: thin at both ends, widest a third of the
    // way in, so it reads as a blade drawn through rather than a painted crescent. Clean.
    private const int SlashSize = 256;
    private const float SlashArcRadius = 0.42f;
    private const float SlashSweepDegrees = 104f;
    private const float SlashPeakPosition = 0.34f;
    private const float SlashMaxHalfWidth = 0.052f;
    private const float SlashEdgeSoftness = 1.5f;

    // Noise (256², tiling). Four octaves of value noise; the definition, and clean by intent.
    private const int NoiseSize = 256;
    private const int NoiseBasePeriod = 4;
    private const int NoiseOctaves = 4;
    private const float NoiseContrast = 1.15f;

    // Seeds. Fixed so the bake is reproducible; distinct so no two masks share a field.
    private const int SeedStreak = 21;
    private const int SeedPuffShape = 53;
    private const int SeedPuffDetail = 97;
    private const int SeedScorchRim = 131;
    private const int SeedScorchRimDetail = 157;
    private const int SeedScorchMottle = 179;
    private const int SeedScorchGrain = 223;
    private const int SeedNoise = 271;

    // =====================================================================================
    // ENTRY POINT
    // =====================================================================================

    [MenuItem("Battle Plan/FX/Bake Sprite Masks")]
    public static void BakeAll()
    {
        EnsureFolder(OutputFolder);

        Write("T_FX_RadialGlow", BakeRadialGlow(), TextureWrapMode.Clamp);
        Write("T_FX_MuzzleStar", BakeMuzzleStar(), TextureWrapMode.Clamp);
        Write("T_FX_SparkStreak", BakeSparkStreak(), TextureWrapMode.Clamp);
        Write("T_FX_SmokePuff", BakeSmokePuff(), TextureWrapMode.Clamp);
        Write("T_FX_Scorch", BakeScorch(), TextureWrapMode.Clamp);
        Write("T_FX_Slash", BakeSlash(), TextureWrapMode.Clamp);
        Write("T_FX_Noise", BakeNoise(), TextureWrapMode.Repeat);

        AssetDatabase.Refresh();
        Debug.Log($"[BattlePlanFXMaskBaker] Baked 7 masks into {OutputFolder}.");
    }

    // =====================================================================================
    // MASKS
    // =====================================================================================

    private static Texture2D BakeRadialGlow()
    {
        return BakeMask(
            GlowSize,
            GlowSize,
            (u, v) =>
            {
                float r = Radius(u, v) * 2f;
                return Mathf.Pow(Smoothstep(1f, 0f, r), GlowFalloffPower) * GlowPeak;
            }
        );
    }

    private static Texture2D BakeMuzzleStar()
    {
        return BakeMask(
            StarSize,
            StarSize,
            (u, v) =>
            {
                float dx = u - 0.5f;
                float dy = v - 0.5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float angle = Mathf.Atan2(dy, dx);

                // cos(2a) peaks on the axes, cos(2a - 90°) on the diagonals.
                float axisArm = Mathf.Pow(Mathf.Abs(Mathf.Cos(2f * angle)), StarArmSharpness);
                float diagonalArm = Mathf.Pow(
                    Mathf.Abs(Mathf.Cos(2f * angle - Mathf.PI * 0.5f)),
                    StarArmSharpness
                );

                float alpha = Arm(r, StarArmReach * axisArm, StarArmFalloff);
                alpha = Mathf.Max(alpha, Arm(r, StarDiagonalReach * diagonalArm, StarArmFalloff));
                alpha = Mathf.Max(alpha, Arm(r, StarCoreReach, StarCoreFalloff));
                return alpha;
            }
        );
    }

    private static float Arm(float radius, float reach, float falloff)
    {
        if (reach <= 1e-4f)
            return 0f;
        return Mathf.Pow(Mathf.Clamp01(1f - radius / reach), falloff);
    }

    private static Texture2D BakeSparkStreak()
    {
        return BakeMask(
            StreakWidth,
            StreakHeight,
            (u, v) =>
            {
                float acrossCentre = Mathf.Abs(v - 0.5f) * 2f;
                float halfWidth = Mathf.Lerp(
                    StreakTailHalfWidth,
                    StreakHeadHalfWidth,
                    Mathf.Pow(u, StreakWidthCurve)
                );
                float across = Mathf.Pow(
                    Mathf.Clamp01(1f - acrossCentre / Mathf.Max(halfWidth, 1e-4f)),
                    StreakEdgeSoftness
                );

                float taper = Mathf.Pow(u, StreakTailFalloff);
                // Rounds the very tip so the head is a hot bead rather than a flat cut.
                float cap = Smoothstep(
                    0f,
                    1f,
                    Mathf.InverseLerp(1f, 1f - StreakHeadCap, u)
                );
                float alpha = across * taper * cap;

                // A dead-straight gaussian reads as a laser, not a spark thrown off metal.
                float grain = Fbm(u * 3f, v, 8, 2, SeedStreak);
                return alpha * Mathf.Lerp(1f - StreakNoiseAmount, 1f, grain);
            }
        );
    }

    private static Texture2D BakeSmokePuff()
    {
        return BakeMask(
            PuffSize,
            PuffSize,
            (u, v) =>
            {
                float coverage = 0f;
                foreach (Vector3 blob in PuffBlobs)
                {
                    float dx = u - 0.5f - blob.x;
                    float dy = v - 0.5f - blob.y;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    coverage = Mathf.Max(coverage, 1f - distance / blob.z);
                }

                float shapeNoise = Fbm(u, v, PuffShapePeriod, 3, SeedPuffShape);
                float edge = coverage + (shapeNoise - 0.5f) * PuffEdgeChaos;
                float alpha = Smoothstep(0f, PuffEdgeSoftness, edge);

                float detail = Fbm(u, v, PuffDetailPeriod, 3, SeedPuffDetail);
                alpha *= Mathf.Lerp(1f - PuffInteriorVariation, 1f, detail);

                // Guarantees the card has no hard corner even if a blob is retuned outward.
                alpha *= Smoothstep(1f, 0.82f, Radius(u, v) * 2f);
                return alpha * PuffPeak;
            }
        );
    }

    private static Texture2D BakeScorch()
    {
        return BakeMask(
            ScorchSize,
            ScorchSize,
            (u, v) =>
            {
                float dx = u - 0.5f;
                float dy = v - 0.5f;
                float radius = Mathf.Sqrt(dx * dx + dy * dy);

                // Low frequency pushes the outline in and out in a few broad lobes; the detail term
                // nibbles it. Driving the rim from one high-frequency term is what produced a
                // starburst on the first pass.
                float rim = Fbm(u, v, ScorchRimPeriod, 2, SeedScorchRim);
                float rimDetail = Fbm(u, v, ScorchRimDetailPeriod, 2, SeedScorchRimDetail);
                float edgeRadius =
                    ScorchRadius
                    * (
                        1f
                        + (rim - 0.5f) * 2f * ScorchRimVariation
                        + (rimDetail - 0.5f) * 2f * ScorchRimDetail
                    );

                float alpha = Smoothstep(
                    edgeRadius,
                    edgeRadius - ScorchEdgeSoftness * ScorchRadius,
                    radius
                );

                // Fine grain only near the rim, so the burn crumbles at its edge and stays solid
                // in the middle.
                float nearEdge = Mathf.Pow(Mathf.Clamp01(radius / Mathf.Max(edgeRadius, 1e-4f)), 4f);
                float grain = Smoothstep(
                    ScorchGrainLow,
                    ScorchGrainHigh,
                    Fbm(u, v, ScorchGrainPeriod, 3, SeedScorchGrain)
                );
                alpha *= Mathf.Lerp(1f, grain, nearEdge);

                float mottle = Fbm(u, v, ScorchMottlePeriod, 3, SeedScorchMottle);
                alpha *= Mathf.Lerp(1f - ScorchMottle, 1f, mottle);

                return alpha * ScorchPeak;
            }
        );
    }

    private static Texture2D BakeSlash()
    {
        float halfSweep = SlashSweepDegrees * 0.5f * Mathf.Deg2Rad;
        return BakeMask(
            SlashSize,
            SlashSize,
            (u, v) =>
            {
                // Arc centred left of the card so the visible span is the outer part of the sweep.
                float dx = u - 0.5f + SlashArcRadius * 0.62f;
                float dy = v - 0.5f;
                float radius = Mathf.Sqrt(dx * dx + dy * dy);
                float angle = Mathf.Atan2(dy, dx);
                if (Mathf.Abs(angle) > halfSweep)
                    return 0f;

                float along = Mathf.InverseLerp(-halfSweep, halfSweep, angle);
                // Widest at SlashPeakPosition, tapering to nothing at both ends.
                float taper =
                    along < SlashPeakPosition
                        ? Mathf.InverseLerp(0f, SlashPeakPosition, along)
                        : Mathf.InverseLerp(1f, SlashPeakPosition, along);
                float halfWidth = SlashMaxHalfWidth * Smoothstep(0f, 1f, taper);
                if (halfWidth <= 1e-5f)
                    return 0f;

                float acrossArc = Mathf.Abs(radius - SlashArcRadius);
                return Mathf.Pow(
                    Mathf.Clamp01(1f - acrossArc / halfWidth),
                    SlashEdgeSoftness
                );
            }
        );
    }

    /// <summary>
    /// Tiling value-noise field. Unlike the masks, this one carries its value in RGB with alpha at
    /// 1, because the shaders sample it as a scalar through .r.
    /// </summary>
    private static Texture2D BakeNoise()
    {
        Texture2D texture = new(NoiseSize, NoiseSize, TextureFormat.RGBA32, true, true);
        Color[] pixels = new Color[NoiseSize * NoiseSize];
        for (int y = 0; y < NoiseSize; y++)
        {
            for (int x = 0; x < NoiseSize; x++)
            {
                float u = x / (float)NoiseSize;
                float v = y / (float)NoiseSize;
                float value = Fbm(u, v, NoiseBasePeriod, NoiseOctaves, SeedNoise);
                value = Mathf.Clamp01((value - 0.5f) * NoiseContrast + 0.5f);
                pixels[y * NoiseSize + x] = new Color(value, value, value, 1f);
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    // =====================================================================================
    // MASK PLUMBING
    // =====================================================================================

    /// <summary>
    /// Evaluates <paramref name="shape"/> over the UV grid into the alpha channel, leaving RGB flat
    /// white. White RGB is what lets the runtime tint be a straight multiply: the mask contributes
    /// coverage, the palette contributes colour, and no baked hue can fight the swap.
    /// </summary>
    private static Texture2D BakeMask(int width, int height, System.Func<float, float, float> shape)
    {
        Texture2D texture = new(width, height, TextureFormat.RGBA32, true, true);
        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                float v = (y + 0.5f) / height;
                pixels[y * width + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(shape(u, v)));
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    private static void Write(string name, Texture2D texture, TextureWrapMode wrapMode)
    {
        string path = $"{OutputFolder}/{name}.png";
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
            return;

        importer.textureType = TextureImporterType.Default;
        importer.wrapMode = wrapMode;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = true;
        importer.alphaIsTransparency = true;
        // Straight alpha. Premultiplying would bake a black fringe into a mask whose RGB is meant
        // to stay uniformly white for the runtime tint.
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.sRGBTexture = false;
        importer.SaveAndReimport();
    }

    // =====================================================================================
    // DETERMINISTIC NOISE
    // =====================================================================================

    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFFu) / (float)0xFFFFFF;
        }
    }

    /// <summary>Value noise on a wrapping lattice, so every field baked here tiles seamlessly.</summary>
    private static float ValueNoise(float x, float y, int period, int seed)
    {
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        float fx = Smoothstep(0f, 1f, x - x0);
        float fy = Smoothstep(0f, 1f, y - y0);

        int xa = Mod(x0, period);
        int xb = Mod(x0 + 1, period);
        int ya = Mod(y0, period);
        int yb = Mod(y0 + 1, period);

        float top = Mathf.Lerp(Hash(xa, ya, seed), Hash(xb, ya, seed), fx);
        float bottom = Mathf.Lerp(Hash(xa, yb, seed), Hash(xb, yb, seed), fx);
        return Mathf.Lerp(top, bottom, fy);
    }

    private static float Fbm(float u, float v, int basePeriod, int octaves, int seed)
    {
        float sum = 0f;
        float amplitude = 1f;
        float total = 0f;
        int period = Mathf.Max(1, basePeriod);

        for (int octave = 0; octave < octaves; octave++)
        {
            sum += ValueNoise(u * period, v * period, period, seed + octave * 101) * amplitude;
            total += amplitude;
            amplitude *= 0.5f;
            period *= 2;
        }
        return total > 0f ? sum / total : 0f;
    }

    private static int Mod(int value, int period)
    {
        int result = value % period;
        return result < 0 ? result + period : result;
    }

    private static float Radius(float u, float v)
    {
        float dx = u - 0.5f;
        float dy = v - 0.5f;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// GLSL-style smoothstep. Named apart from Mathf.SmoothStep on purpose: Unity's overload takes
    /// (from, to, t) and interpolates between the first two arguments, which is a different
    /// function and an easy way to get a shape subtly wrong.
    /// </summary>
    private static float Smoothstep(float edge0, float edge1, float x)
    {
        if (Mathf.Approximately(edge0, edge1))
            return x < edge0 ? 0f : 1f;
        float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
