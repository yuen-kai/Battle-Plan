// Battle Plan — burn taken out of the ground under an impact.
//
// This multiplies the board rather than laying a dark film over it, and that is what makes it a
// burn instead of a shadow. An alpha-blended near-black char is dominated by the (1 - alpha) term,
// which carries the floor's own blue-grey through unchanged, so however warm the char colour is
// the result measures as a neutral dimming. A per-channel multiplier crushes blue about twice as
// hard as red whatever the surface underneath happens to be, so the mark lands as scorch colour.
// Depth is Beer-Lambert — the multiplier is raised to the local density — so a thin edge and a
// deep core share one hue instead of the shallow parts washing back to grey.
//
// The outline is a union of offset lobes through a warped domain, cut against its own screen-space
// gradient so the floor either burned here or it did not: the boundary is a two-pixel step at any
// distance rather than a ramp, which is the difference between material and a stain. Bays and grit
// are taken on the outline's own direction, so the burn wanders well off a circle without any point
// on its edge losing that step — raggedness is measured along each ray's own normal. Soot flecks
// straddle the lip and a sparse fringe of thrown material lies outside it, so nothing about the
// boundary is smooth or closed. Ash creeps across the char as it cools, which keeps the tail
// changing frame to frame instead of holding one silhouette. Textureless; _Seed decorrelates hits
// on one board.
Shader "BattlePlan/AftermathScorch"
{
    Properties
    {
        _MarkColor ("Char Multiplier", Color) = (0.44, 0.255, 0.115, 1)
        _EdgeColor ("Rim Multiplier", Color) = (0.74, 0.6, 0.445, 1)
        _AshColor ("Cooled Ash Multiplier", Color) = (0.78, 0.66, 0.54, 1)
        _Opacity ("Density", Range(0, 1)) = 1
        _Ash ("Ash Creep", Range(0, 1)) = 0
        _AshScale ("Ash Scale", Float) = 4.4
        _Drift ("Smoulder Drift", Float) = 0
        _RimAlpha ("Rim Density", Range(0, 1)) = 0.34
        _EdgeCut ("Edge Cut", Range(0, 0.3)) = 0.05
        _EdgePixels ("Edge Width (pixels)", Range(0.5, 6)) = 2.2
        _CoreDepth ("Core Depth", Range(0.05, 1)) = 0.42
        _LobeCount ("Lobe Count", Range(1, 5)) = 4
        _Spread ("Lobe Spread", Range(0, 0.4)) = 0.28
        _MinLobe ("Min Lobe Radius", Range(0.05, 0.4)) = 0.15
        _MaxLobe ("Max Lobe Radius", Range(0.1, 0.5)) = 0.27
        _Knit ("Knit", Range(0.01, 0.5)) = 0.14
        _Warp ("Domain Warp", Range(0, 0.4)) = 0.145
        _WarpScale ("Warp Scale", Float) = 3.6
        _Bays ("Bay Depth", Range(0, 1.2)) = 0.48
        _BayScale ("Bay Frequency", Float) = 0.36
        _Grit ("Rim Grit", Range(0, 0.5)) = 0.14
        _GritScale ("Grit Frequency", Float) = 1.3
        _NoiseScale ("Mottle Scale", Float) = 7.5
        _Mottle ("Mottle", Range(0, 0.8)) = 0.3
        _Soot ("Lip Soot Density", Range(0, 1)) = 0.78
        _SootBand ("Lip Soot Band", Range(0.01, 0.4)) = 0.06
        _SootCut ("Lip Soot Cut", Range(0.3, 0.95)) = 0.68
        _SootScale ("Lip Soot Scale", Float) = 17
        _SpatterAmount ("Spatter Amount", Range(0, 1)) = 0.55
        _RayFrequency ("Ejecta Frequency", Float) = 3.4
        _FringeCut ("Ejecta Cut", Range(0.3, 0.9)) = 0.605
        _Seed ("Seed", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+10"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Forward"
            Blend DstColor Zero, Zero One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _MarkColor;
                half4 _EdgeColor;
                half4 _AshColor;
                half _Opacity;
                half _Ash;
                float _AshScale;
                float _Drift;
                half _RimAlpha;
                half _EdgeCut;
                half _EdgePixels;
                half _CoreDepth;
                float _LobeCount;
                half _Spread;
                half _MinLobe;
                half _MaxLobe;
                half _Knit;
                half _Warp;
                float _WarpScale;
                half _Bays;
                float _BayScale;
                half _Grit;
                float _GritScale;
                float _NoiseScale;
                half _Mottle;
                half _Soot;
                half _SootBand;
                half _SootCut;
                float _SootScale;
                half _SpatterAmount;
                float _RayFrequency;
                half _FringeCut;
                float _Seed;
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

            float ScorchHash1(float n)
            {
                return frac(sin(n * 12.9898) * 43758.5453123);
            }

            float3 ScorchHash3(float n)
            {
                float3 s = float3(n * 12.9898, n * 78.233 + 2.7, n * 39.425 + 6.1);
                return frac(sin(s) * float3(43758.5453, 22578.1459, 19642.3491));
            }

            float ScorchValue(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = ScorchHash1(cell.x + cell.y * 57.0);
                float b = ScorchHash1(cell.x + 1.0 + cell.y * 57.0);
                float c = ScorchHash1(cell.x + (cell.y + 1.0) * 57.0);
                float d = ScorchHash1(cell.x + 1.0 + (cell.y + 1.0) * 57.0);
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float ScorchFbm(float2 p)
            {
                float sum = 0.0;
                float amplitude = 0.5;
                for (int i = 0; i < 3; i++)
                {
                    sum += ScorchValue(p) * amplitude;
                    p *= 2.09;
                    amplitude *= 0.5;
                }
                return sum / 0.875;
            }

            float ScorchSmoothMax(float a, float b, float k)
            {
                float h = saturate(0.5 + 0.5 * (a - b) / max(k, 1e-4));
                return lerp(b, a, h) + k * h * (1.0 - h);
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
                float2 raw = IN.uv - 0.5;
                float2 warp = float2(
                    ScorchFbm(raw * _WarpScale + _Seed * 0.61),
                    ScorchFbm(raw * _WarpScale + _Seed * 0.61 + 7.9)
                ) - 0.5;
                float2 p = raw + warp * _Warp;

                int count = (int)max(1.0, _LobeCount);
                float unioned = -8.0;
                for (int i = 0; i < 5; i++)
                {
                    if (i >= count)
                        break;

                    float3 h = ScorchHash3(_Seed * 2.7 + i * 5.13 + 0.9);
                    float2 center = i == 0
                        ? (h.xy - 0.5) * (_Spread * 0.3)
                        : (h.xy - 0.5) * _Spread;
                    float radius = i == 0 ? _MaxLobe : lerp(_MinLobe, _MaxLobe * 0.82, h.z);
                    unioned = ScorchSmoothMax(
                        unioned,
                        1.0 - length(p - center) / max(radius, 1e-4),
                        _Knit
                    );
                }

                float angle = atan2(raw.y, raw.x);
                float radial = length(raw);

                // Bays and grit cut on the outline's own direction rather than on position, so the
                // boundary wanders two ways off a circle while every point on it still crosses
                // along its own normal. Both fade out toward the middle, where the direction is
                // undefined and the outline is nowhere near anyway.
                float2 heading = radial > 1e-5 ? raw / radial : float2(1.0, 0.0);
                float shaped =
                    unioned
                    + (
                        (ScorchFbm(heading * _BayScale + _Seed * 1.13) - 0.5) * _Bays
                        + (ScorchFbm(heading * _GritScale + _Seed * 2.71 + 4.3) - 0.5) * _Grit
                    ) * saturate(radial * 12.0);

                // The burn is still hot, so its mottling crawls. Nothing else in the tail moves
                // once the smoke has gone, and a mark that holds the same bytes for four frames
                // running reads as a decal however good its outline is.
                float2 drift = float2(_Drift, _Drift * 0.63);
                float mottle = ScorchFbm(p * _NoiseScale + _Seed * 4.3 + drift);
                // The floor either burned here or it did not. Normalising the cut by the
                // boundary's own screen gradient holds that step at a couple of pixels whatever
                // the mark is worth on screen, and it is the whole difference between displaced
                // material and a stain laid over the tile.
                float boundary =
                    saturate((shaped - _EdgeCut) / max(fwidth(shaped) * _EdgePixels, 1e-5));
                float core = saturate(shaped / max(_CoreDepth, 1e-3));

                half density =
                    _Opacity * boundary * lerp(_RimAlpha, 1.0, core)
                    * lerp(1.0 - _Mottle, 1.0, mottle);

                // Soot thrown at the lip of the burn. Hard flecks straddling the outline are what
                // stop a ray crossing it from finding a clean ramp, and because they only win
                // where the char is still shallow they cost the mark almost no area.
                float sootField = ScorchFbm(p * _SootScale + _Seed * 6.7 + drift * 0.4);
                float soot =
                    saturate((sootField - _SootCut) / max(fwidth(sootField) * _EdgePixels, 1e-5))
                    * (1.0 - saturate(abs(shaped - _EdgeCut) / max(_SootBand, 1e-3)));

                // Material thrown clear of the burn. Sparse specks in the ring just outside the
                // boundary are what stop the mark reading as a stamped decal.
                float rays = ScorchFbm(float2(angle * _RayFrequency, radial * 11.0) + _Seed * 1.7);
                float band =
                    smoothstep(-0.30, -0.16, shaped) * (1.0 - smoothstep(-0.06, 0.05, shaped));
                float fringe =
                    saturate((rays - _FringeCut) / max(fwidth(rays) * _EdgePixels, 1e-5))
                    * band
                    * (1.0 - smoothstep(0.36, 0.5, radial));
                density = max(density, fringe * _Opacity * _SpatterAmount);
                density = max(density, soot * _Opacity * _Soot);

                clip(density - 0.004);

                half3 burn = lerp(_EdgeColor.rgb, _MarkColor.rgb, core);
                burn = lerp(burn, _MarkColor.rgb, soot * 0.7 * (1.0 - core));
                // Ash advances across the char rather than the whole mark lightening together.
                float ash =
                    saturate(
                        (ScorchFbm(p * _AshScale + _Seed * 8.9 + drift * 0.7) - (0.82 - _Ash * 0.46))
                        / 0.20
                    ) * 0.72;
                burn = lerp(burn, _AshColor.rgb, ash);

                return half4(pow(max(burn, 1e-4), density), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
