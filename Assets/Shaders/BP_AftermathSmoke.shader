// Battle Plan — alpha-blended billboard for one mass of the smoke an impact throws up.
// One quad draws a whole cluster of sub-lobes rather than a single convex blob, so a plume built
// from a handful of these has a torn outline instead of reading as a bunch of grapes. Coverage is
// cut with a near-step so the silhouette is a two-pixel edge and the interior is genuinely opaque;
// thinning is done by walking that cut inwards and subtracting noise, which tears the mass into
// pieces rather than greying it out. Shading is a sphere normal taken from whichever sub-lobe owns
// the fragment, so every lobe carries a lit side and a shadow side. Textureless, driven per-mass
// from Aftermath.cs; _Seed decorrelates the silhouette between masses and between hits.
Shader "BattlePlan/AftermathSmoke"
{
    Properties
    {
        _LitColor ("Lit Color", Color) = (0.400, 0.346, 0.280, 1)
        _ShadowColor ("Shadow Color", Color) = (0.005, 0.0041, 0.0033, 1)
        _Opacity ("Opacity", Range(0, 1)) = 1
        _Erode ("Erode", Range(0, 1)) = 0
        _Tear ("Tear", Range(0, 2)) = 0.1
        _EdgeWidth ("Edge Width", Range(0.004, 0.3)) = 0.03
        _LobeCount ("Lobe Count", Range(1, 6)) = 5
        _Spread ("Lobe Spread", Range(0, 0.6)) = 0.46
        _MinLobe ("Min Lobe Radius", Range(0.04, 0.4)) = 0.16
        _MaxLobe ("Max Lobe Radius", Range(0.1, 0.45)) = 0.36
        _Knit ("Knit", Range(0.01, 0.5)) = 0.15
        _Warp ("Domain Warp", Range(0, 0.3)) = 0.1
        _WarpScale ("Warp Scale", Float) = 3
        _NoiseScale ("Density Scale", Float) = 9
        _Mottle ("Density Mottle", Range(0, 1.5)) = 0.8
        _ShadeLow ("Terminator Start", Range(0, 1)) = 0.52
        _ShadeSpan ("Terminator Width", Range(0.02, 1)) = 0.26
        _Sweep ("Form Sweep", Range(0, 4)) = 1.4
        _LobeWeight ("Lobe / Form Balance", Range(0, 1)) = 0.76
        _Crease ("Crease Shadow", Range(0, 1)) = 0.22
        _Rim ("Rim Falloff", Range(0, 1)) = 0.66
        _LightAngle ("Light Angle (radians)", Float) = 0
        _Seed ("Seed", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+50"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Forward"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _LitColor;
                half4 _ShadowColor;
                half _Opacity;
                half _Erode;
                half _Tear;
                half _EdgeWidth;
                float _LobeCount;
                half _Spread;
                half _MinLobe;
                half _MaxLobe;
                half _Knit;
                half _Warp;
                float _WarpScale;
                float _NoiseScale;
                half _Mottle;
                half _ShadeLow;
                half _ShadeSpan;
                half _Sweep;
                half _LobeWeight;
                half _Crease;
                half _Rim;
                float _LightAngle;
                float _Seed;
            CBUFFER_END

            // Light comes from screen upper-left for every mass in the plume. A shared direction is
            // what makes a cluster read as one volume instead of as unrelated shaded balls, so
            // _LightAngle cancels whatever roll the billboard is carrying and holds it there.
            static const float3 SmokeLightDir = float3(-0.51, 0.66, 0.55);

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

            float SmokeHash1(float n)
            {
                return frac(sin(n * 12.9898) * 43758.5453123);
            }

            float3 SmokeHash3(float n)
            {
                float3 s = float3(n * 12.9898, n * 78.233 + 4.1, n * 39.425 + 9.7);
                return frac(sin(s) * float3(43758.5453, 22578.1459, 19642.3491));
            }

            float SmokeValue(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = SmokeHash1(cell.x + cell.y * 57.0);
                float b = SmokeHash1(cell.x + 1.0 + cell.y * 57.0);
                float c = SmokeHash1(cell.x + (cell.y + 1.0) * 57.0);
                float d = SmokeHash1(cell.x + 1.0 + (cell.y + 1.0) * 57.0);
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float SmokeFbm(float2 p)
            {
                float sum = 0.0;
                float amplitude = 0.5;
                for (int i = 0; i < 3; i++)
                {
                    sum += SmokeValue(p) * amplitude;
                    p *= 2.17;
                    amplitude *= 0.5;
                }
                return sum / 0.875;
            }

            float SmokeSmoothMax(float a, float b, float k)
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
                float2 p = IN.uv - 0.5;
                float2 warp = float2(
                    SmokeFbm(p * _WarpScale + _Seed * 0.83),
                    SmokeFbm(p * _WarpScale + _Seed * 0.83 + 13.7)
                ) - 0.5;
                p += warp * _Warp;

                int count = (int)max(1.0, _LobeCount);
                float unioned = -8.0;
                float best = -8.0;
                float2 bestCenter = float2(0.0, 0.0);
                float bestRadius = _MaxLobe;
                float bestTone = 1.0;

                for (int i = 0; i < 6; i++)
                {
                    if (i >= count)
                        break;

                    float3 h = SmokeHash3(_Seed * 3.11 + i * 7.77 + 1.3);
                    // Lobe zero is the mass's own dominant body; the rest hang off it at a fraction
                    // of its size, so even a single quad has an internal size hierarchy.
                    float2 center = i == 0
                        ? (h.xy - 0.5) * (_Spread * 0.22)
                        : (h.xy - 0.5) * _Spread;
                    float radius = i == 0
                        ? _MaxLobe
                        : lerp(_MinLobe, _MaxLobe * 0.74, h.z);

                    float lobe = 1.0 - length(p - center) / max(radius, 1e-4);
                    unioned = SmokeSmoothMax(unioned, lobe, _Knit);
                    if (lobe > best)
                    {
                        best = lobe;
                        bestCenter = center;
                        bestRadius = radius;
                        bestTone = 0.82 + 0.30 * SmokeHash1(_Seed * 5.7 + i * 3.31);
                    }
                }

                float detail = SmokeFbm(p * _NoiseScale + _Seed * 2.3);
                float field = unioned - _Erode - _Tear * (detail - 0.34);

                half alpha = saturate(field / max(_EdgeWidth, 1e-3)) * _Opacity;
                clip(alpha - 0.015);

                float rollSin, rollCos;
                sincos(_LightAngle, rollSin, rollCos);
                float3 light = float3(
                    SmokeLightDir.x * rollCos - SmokeLightDir.y * rollSin,
                    SmokeLightDir.x * rollSin + SmokeLightDir.y * rollCos,
                    SmokeLightDir.z
                );

                // Sphere normal of whichever lobe owns this fragment. Beyond a lobe's own radius the
                // z term collapses and the normal turns edge-on, which is what puts a hard
                // terminator on the underside of every lobe.
                float2 lateral = (p - bestCenter) / max(bestRadius, 1e-4);
                float facing = sqrt(saturate(1.0 - dot(lateral, lateral)));
                float3 normal = normalize(float3(lateral, facing + 0.10));
                half lobeShade = smoothstep(
                    _ShadeLow,
                    _ShadeLow + _ShadeSpan,
                    saturate(dot(normal, light))
                );

                // Form shading across the whole mass, which the lobe term loses as soon as erosion
                // eats the rims. Without it a thinning mass flattens into one value.
                half formShade = saturate(
                    0.5 + dot(p, normalize(light.xy)) * _Sweep
                );
                // Density pockets ride on the shade rather than on the colour, so the lit-to-shadow
                // range does the work and the mass keeps a spread even once it is coming apart.
                half shade = saturate(
                    lerp(formShade, lobeShade, _LobeWeight) + (detail - 0.5) * _Mottle
                );

                half3 color = lerp(_ShadowColor.rgb, _LitColor.rgb, shade) * bestTone;
                // Crevices where two lobes merge sit in each other's shadow.
                color *= lerp(1.0, _Crease, saturate((unioned - best) * 5.0));
                // Thin material at the silhouette is unlit against a bright board, not backlit.
                color *= lerp(_Rim, 1.0, saturate(unioned * 3.2));

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
