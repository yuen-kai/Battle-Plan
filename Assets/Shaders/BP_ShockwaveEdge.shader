// Battle Plan — the burning near face of an impact shockwave, on a default Unity Quad laid flat.
// It is not a ring and it is not a stroke. It is a handful of separate arcs of very different
// widths, each a slab of incandescent matter some tens of pixels deep, riding the dust front's own
// leading boundary and torn by the same lumps the dust is shaded with.
//   The boundary and relief fields here are the ones BP_ShockwaveDust evaluates; feed both
//   materials the same _ShapeSeed, _Seed, _LumpScale, _RagCount, _OuterRag, _Lean, _BiasDir and
//   vent settings and the arcs track the dust exactly, including its tears and its surface.
//   Saturated colour and clipping are only compatible on a secondary axis, where two channels may
//   clip and the third holds the hue: authored near 190 degrees the green and blue channels go and
//   red is held down, which is the one cool arc that reaches luminance 205-225 while staying above
//   0.5 saturation. Depth is therefore spent on AREA, never on amplitude — dimming a slab drops it
//   under luminance 200 and hands the colour straight back.
//   Nothing here is allowed to approach white. A hot core on the crest line reads as a specular
//   highlight, and dust has no specular lobe; it also spends the frame's only clipping pixels on a
//   hairline where they cannot be seen. The flash belongs to BP_ShockwaveCore, which owns the whole
//   white budget and can hold it as area. This layer stays entirely inside its hue.
//   Magnitude lives in _Intensity; colours arrive as LINEAR rgb through SetVector.
Shader "BattlePlan/ShockwaveEdge"
{
    Properties
    {
        _GlowColor ("Ability Hue (linear rgb)", Vector) = (0.075, 0.475, 1, 1)
        _EmberColor ("Hot Core (linear rgb)", Vector) = (0.105, 0.6, 1, 1)
        _Intensity ("Intensity", Range(0, 12)) = 3.05
        _Radius ("Front Radius", Range(0, 1)) = 0.7
        _Width ("Band Width", Range(0.002, 0.5)) = 0.11
        _RimPush ("Band Push (band widths)", Range(-1, 1)) = -0.3
        _SlabDepth ("Slab Depth (band widths)", Range(0.1, 3)) = 1.15
        _SlabHold ("Slab Plateau", Range(0.1, 0.95)) = 0.5
        _SlabCut ("Slab Threshold", Range(0, 0.9)) = 0.2
        _SlabSoft ("Slab Threshold Softness", Range(0.02, 0.4)) = 0.13
        _SlabOut ("Slab Overhang (band widths)", Range(0.02, 0.5)) = 0.09
        _SlabSwing ("Slab Value Swing", Range(0, 0.4)) = 0.16
        _Streak ("Slab Streaking", Range(0, 0.9)) = 0.45
        _StreakCells ("Streak Cells", Float) = 34
        _EmberShare ("Hot Core Share", Range(0.02, 0.6)) = 0.22
        _TailIn ("Inward Tail (band widths)", Range(0.1, 4)) = 1.15
        _TailAmp ("Inward Tail Amount", Range(0, 1)) = 0.42
        _TailPow ("Inward Tail Falloff", Range(0.3, 4)) = 1.5
        _OutReach ("Arc Halo Reach (band widths)", Range(0.1, 4)) = 1.45
        _OutAmp ("Arc Halo Amount", Range(0, 1)) = 0.36
        _OutPow ("Arc Halo Falloff", Range(0.3, 4)) = 0.85
        _GroundAmp ("Ground Glow Amount", Range(0, 1.2)) = 0.52
        _GroundReach ("Ground Glow Reach (band widths)", Range(0.1, 6)) = 2
        _GroundPow ("Ground Glow Falloff", Range(0.3, 4)) = 0.65
        _ArcCount ("Arc Count", Range(1, 8)) = 6
        _ArcSpan ("Arc Half Span (turns)", Range(0.005, 0.14)) = 0.076
        _ArcSeed ("Arc Seed", Float) = 0
        _OuterRag ("Outer Raggedness", Range(0, 0.4)) = 0.17
        _RagCount ("Boundary Cells", Float) = 7
        _Lean ("Directional Mass Lean", Range(0, 0.5)) = 0.29
        _BiasDir ("Heavy Side (xz)", Vector) = (1, 0, 0, 0)
        _Vents ("Vent Count", Range(0, 4)) = 4
        _VentWidth ("Vent Half Width (turns)", Range(0, 0.09)) = 0.038
        _ShapeSeed ("Silhouette Seed", Float) = 0
        _Seed ("Relief Seed", Float) = 0
        _LumpScale ("Lump Scale (cycles per quad)", Float) = 26
        _LumpRelief ("Lump Slope", Range(0, 2)) = 0.55
        _ReliefBite ("Relief Bite", Range(0, 0.9)) = 0.42
        _ReliefGamma ("Relief Bite Shape", Range(0.2, 3)) = 0.75
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+45"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Forward"
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GlowColor;
                half4 _EmberColor;
                half _Intensity;
                half _Radius;
                half _Width;
                half _RimPush;
                half _SlabDepth;
                half _SlabHold;
                half _SlabCut;
                half _SlabSoft;
                half _SlabOut;
                half _SlabSwing;
                half _Streak;
                float _StreakCells;
                half _EmberShare;
                half _TailIn;
                half _TailAmp;
                half _TailPow;
                half _OutReach;
                half _OutAmp;
                half _OutPow;
                half _GroundAmp;
                half _GroundReach;
                half _GroundPow;
                float _ArcCount;
                float _ArcSpan;
                float _ArcSeed;
                half _OuterRag;
                float _RagCount;
                half _Lean;
                float4 _BiasDir;
                float _Vents;
                float _VentWidth;
                float _ShapeSeed;
                float _Seed;
                float _LumpScale;
                half _LumpRelief;
                half _ReliefBite;
                half _ReliefGamma;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float Hash11(float n)
            {
                n = frac(n * 0.1031);
                n *= n + 33.33;
                n *= n + n;
                return frac(n);
            }

            float AngleNoise(float turns, float cells, float seed)
            {
                float scaled = turns * cells;
                float cell = floor(scaled);
                float blend = frac(scaled);
                blend = blend * blend * (3.0 - 2.0 * blend);
                float a = Hash11(fmod(cell, cells) * 13.71 + seed);
                float b = Hash11(fmod(cell + 1.0, cells) * 13.71 + seed);
                return lerp(a, b, blend) * 2.0 - 1.0;
            }

            float FrontOpening(float turns, float seed)
            {
                float open = 1.0;
                float phase = Hash11(seed * 0.61 + 4.9);
                [unroll]
                for (int k = 0; k < 4; k++)
                {
                    float place = Hash11(seed * 3.1 + k * 7.13 + 1.7);
                    float span = Hash11(seed * 1.9 + k * 3.71 + 9.3);
                    float centre = (k + 0.10 + 0.80 * place) * 0.25 + phase;
                    float halfWidth = _VentWidth * (0.45 + 0.85 * span);
                    float depth = saturate(_Vents - k) * (k == 3 ? 0.55 : 1.0);
                    float gap = abs(frac(turns - centre + 0.5) - 0.5);
                    float gate = smoothstep(halfWidth * 0.42, halfWidth, gap);
                    open = min(open, lerp(1.0, gate, depth));
                }
                return open;
            }

            static const float2 kReliefDir[5] =
            {
                float2(0.9848, 0.1736), float2(0.2079, -0.9781), float2(-0.6428, 0.7660),
                float2(0.7660, 0.6428), float2(-0.3420, -0.9397)
            };
            static const float kReliefFreq[5] = { 1.00, 1.71, 2.63, 4.11, 6.37 };
            static const float kReliefAmp[5] = { 0.50, 0.31, 0.13, 0.05, 0.02 };
            // The dust's slope weights, so the faces the crest picks out are the faces the dust is
            // shaded on. Feeding the two different spectra puts the burning slab across the lumps.
            static const float kSlopeAmp[5] = { 0.879, 0.283, 0.0836, 0.0214, 0.0055 };

            void Relief(float2 p, float scale, float seed, out float height, out float2 slope, out float fine)
            {
                height = 0.0;
                slope = 0.0;
                fine = 0.0;
                [unroll]
                for (int i = 0; i < 5; i++)
                {
                    float k = scale * kReliefFreq[i];
                    float phase = dot(p, kReliefDir[i]) * k + seed * (1.7 + i * 0.83);
                    float wave = sin(phase);
                    height += wave * kReliefAmp[i];
                    slope += cos(phase) * (kSlopeAmp[i] * k) * kReliefDir[i];
                    fine += i >= 3 ? wave * kReliefAmp[i] * 2.6 : 0.0;
                }
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 offset = IN.uv - 0.5;
                float radius = length(offset) * 2.0;
                float angle = atan2(offset.y, offset.x);
                float turns = angle * 0.15915494 + 0.5;

                float outline =
                    AngleNoise(turns, _RagCount, _ShapeSeed) * 0.62 +
                    AngleNoise(turns, _RagCount * 2.0, _ShapeSeed + 31.7) * 0.38;
                float open = FrontOpening(turns, _ShapeSeed);

                float2 facing = offset / max(length(offset), 1e-5);
                float2 bias = normalize(_BiasDir.xy + float2(1e-5, 1e-5));
                float heavy = 0.5 + 0.5 * dot(facing, bias);

                float boundary = _Radius
                    * (1.0 + _OuterRag * outline)
                    * lerp(1.0 - _Lean * 0.12, 1.0 + _Lean * 0.12, heavy)
                    * lerp(0.62, 1.0, open);

                // Nothing beyond the ground glow's reach can contribute and the quad is mostly
                // beyond it, so the arc loop only ever runs on the annulus it can reach.
                float fromEdge = radius - boundary;
                float inwardReach = _Width * max(_SlabDepth * 2.7, _TailIn);
                float outwardReach = _Width * max(_OutReach, _GroundReach);
                clip(min(fromEdge + inwardReach, outwardReach - fromEdge));

                // The crest only exists where there is material to crest, so it dies in the vents
                // with the dust instead of hanging over bare deck.
                float onMaterial = smoothstep(0.15, 0.60, open);

                float height;
                float2 lumpSlope;
                float fine;
                Relief(offset, _LumpScale, _Seed, height, lumpSlope, fine);
                float2 slope = lumpSlope * _LumpRelief;
                float invLen = rsqrt(dot(slope, slope) + 1.0);
                // Which lumps present a face to the crest. The burning slab lives on those and
                // skips the hollows, so it is the dust's own surface alight rather than a decal.
                float crestFace = saturate(dot(-slope * invLen, facing));
                float lumpDepth = 0.55 + 0.80 * saturate(0.5 + 0.5 * height);
                float streak = lerp(
                    1.0 - _Streak,
                    1.0 + _Streak * 0.5,
                    saturate(0.5 + 0.62 * AngleNoise(turns, _StreakCells, _ArcSeed + 12.7))
                );

                float arcPhase = Hash11(_ArcSeed * 0.37 + 5.1);
                half3 accum = 0;

                [unroll]
                for (int k = 0; k < 8; k++)
                {
                    float place = Hash11(_ArcSeed * 2.7 + k * 5.13 + 0.7);
                    float spanRoll = Hash11(_ArcSeed * 4.1 + k * 9.71 + 3.3);
                    float power = Hash11(_ArcSeed * 1.3 + k * 2.57 + 7.9);
                    float push = Hash11(_ArcSeed * 5.9 + k * 6.41 + 1.1);

                    // Squaring the width roll spreads the arcs over better than a 3:1 range, so
                    // one of them dominates and the rest read as fragments of the same crest.
                    float centre = (k + 0.08 + 0.84 * place) * 0.125 + arcPhase;
                    float halfSpan = _ArcSpan * (0.26 + 0.74 * spanRoll * spanRoll);
                    float gap = abs(frac(turns - centre + 0.5) - 0.5);
                    float along = sqrt(saturate(1.0 - gap / max(halfSpan, 1e-4)))
                        * saturate(_ArcCount - k) * onMaterial;

                    float d = fromEdge - _Width * (_RimPush + 0.30 * (push - 0.5));

                    // How far the burning face runs, not how bright it is. Its inner edge follows
                    // the lumps and is combed by the streaks, so the slab is torn rather than a
                    // constant-width insert — and every pixel of it stays above luminance 200.
                    float depth = _SlabDepth * _Width * (0.70 + 0.60 * spanRoll) * lumpDepth * streak;
                    float shape = along * (0.80 + 0.34 * power) * lerp(0.86, 1.10, heavy)
                        * lerp(1.0 - _ReliefBite, 1.0, pow(max(crestFace, 1e-5), _ReliefGamma));
                    float live = smoothstep(_SlabCut - _SlabSoft, _SlabCut + _SlabSoft, shape);
                    float body = live
                        * (1.0 - smoothstep(_SlabHold * depth, depth, -d))
                        * (1.0 - smoothstep(0.0, _SlabOut * _Width, d))
                        * lerp(1.0 - _SlabSwing, 1.0 + _SlabSwing * 0.4, saturate(0.5 + 0.5 * fine));

                    // Light around the burning matter, which is the one thing here allowed to be
                    // soft: inward it dies into the dust, outward it crosses onto bare deck.
                    float tail = _TailAmp * pow(max(1.0 + d / (_TailIn * _Width), 1e-5), _TailPow);
                    float spread = _OutAmp * pow(max(1.0 - d / (_OutReach * _Width), 1e-5), _OutPow);
                    float halo = saturate(d > 0.0 ? spread : tail) * along * lerp(0.86, 1.10, heavy);

                    float env = max(body, halo);

                    // Two channels clip and one is held down, so the hot core moves up the same
                    // hue instead of bleaching across it. Nowhere on the crest goes to white.
                    float emberW = pow(max(1.0 - abs(d + _Width * 0.02) / (_Width * _EmberShare), 1e-5), 1.6);
                    half3 arc = lerp(_GlowColor.rgb, _EmberColor.rgb, emberW) * env;

                    // Overlapping arcs take the brighter of the two rather than summing: a stacked
                    // hot spot is exactly the all-channel overdrive that bleaches hue away.
                    accum = max(accum, arc);
                }

                // The blast lights the deck all the way round, not only where the crest burns, and
                // it does so unevenly. This is the only part of the effect that touches bare floor.
                float ground = _GroundAmp
                    * pow(max(1.0 - fromEdge / (_GroundReach * _Width), 1e-5), _GroundPow)
                    * step(0.0, fromEdge)
                    * lerp(0.55, 1.0, onMaterial)
                    * lerp(0.82, 1.12, heavy)
                    * lerp(0.42, 1.0, saturate(0.5 + 0.6 * AngleNoise(turns, 9.0, _ArcSeed + 3.3)))
                    // Out before the quad runs out, or the raggedest arcs would have their glow
                    // cut off along a straight line where the mesh ends.
                    * saturate((1.0 - radius) / 0.12);
                accum = max(accum, _GlowColor.rgb * ground);

                accum *= _Intensity;
                clip(max(accum.r, max(accum.g, accum.b)) - 0.004);
                return half4(accum, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
