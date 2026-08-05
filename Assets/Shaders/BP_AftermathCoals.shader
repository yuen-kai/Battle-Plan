// Battle Plan — bed of burning material left inside a scorch mark.
//
// Blending is premultiplied, and that is the whole point. The board floor sits at scene-linear
// (0.46, 0.58, 0.62); anything added on top of it inherits that green and blue, so an additive
// fire bright enough to clear the floor's luminance 182 cannot hold saturation — pushing red until
// it clips only drags green up with it and the result tonemaps to cream. Fire is burning matter:
// covering the floor first frees the hue entirely.
//
// Coverage and colour run on separate ramps, and that separation is the second half of the fix. A
// blotch's outline is cut with a near-step two pixels wide, but its heat is graded over a span an
// order of magnitude wider, so the ramp from a dark red rim through the ember body to the hot core
// is spread across the blotch instead of collapsing onto its outline. The white-hot glint rides an
// independent high-frequency field gated on the top of the heat ramp, so it stays a speck inside
// the orange rather than replacing it.
//
// Blotches thin out with radius rather than stopping at a circle, so the bed has no silhouette of
// its own. Driven from Aftermath.cs; _Intensity carries the take-hold-cool envelope and pulls the
// heat ramp and the coverage threshold down with it, so the fire cools to red before it goes out.
Shader "BattlePlan/AftermathCoals"
{
    Properties
    {
        [HDR] _DeepColor ("Cooling Rim", Color) = (0.29, 0.04, 0.011, 1)
        [HDR] _EmberColor ("Ember Body", Color) = (1.55, 0.72, 0.145, 1)
        [HDR] _CoreColor ("Hot Core", Color) = (2.55, 1.26, 0.305, 1)
        [HDR] _GlintColor ("Glint", Color) = (3.6, 2.9, 2.05, 1)
        _Intensity ("Intensity", Range(0, 1)) = 1
        _CrackScale ("Crack Scale", Float) = 12.5
        _Threshold ("Coverage Threshold", Range(0.2, 0.9)) = 0.428
        _ThresholdSlope ("Radial Thinning", Range(0, 1)) = 0.2
        _Feather ("Edge Feather", Range(0.005, 0.2)) = 0.022
        _HeatSpan ("Heat Ramp Span", Range(0.02, 0.6)) = 0.3
        _CoreCut ("Core Cut", Range(0.1, 0.95)) = 0.36
        _Cooling ("Burnout Shrink", Range(0, 0.6)) = 0.24
        _GlintScale ("Glint Scale", Float) = 38
        _GlintCut ("Glint Cut", Range(0.5, 0.95)) = 0.78
        _GlintHeat ("Glint Heat Gate", Range(0.5, 1)) = 0.72
        _Warp ("Domain Warp", Range(0, 0.6)) = 0.32
        _Seed ("Seed", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+15"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Forward"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _EmberColor;
                half4 _CoreColor;
                half4 _GlintColor;
                half _Intensity;
                float _CrackScale;
                half _Threshold;
                half _ThresholdSlope;
                half _Feather;
                half _HeatSpan;
                half _CoreCut;
                half _Cooling;
                float _GlintScale;
                half _GlintCut;
                half _GlintHeat;
                half _Warp;
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

            float CoalHash1(float n)
            {
                return frac(sin(n * 12.9898) * 43758.5453123);
            }

            float CoalValue(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = CoalHash1(cell.x + cell.y * 57.0);
                float b = CoalHash1(cell.x + 1.0 + cell.y * 57.0);
                float c = CoalHash1(cell.x + (cell.y + 1.0) * 57.0);
                float d = CoalHash1(cell.x + 1.0 + (cell.y + 1.0) * 57.0);
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float CoalFbm(float2 p)
            {
                float sum = 0.0;
                float amplitude = 0.5;
                for (int i = 0; i < 3; i++)
                {
                    sum += CoalValue(p) * amplitude;
                    p *= 2.13;
                    amplitude *= 0.5;
                }
                return sum / 0.875;
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
                float radial = length(p) * 2.0;
                clip(1.0 - radial);

                float burn = saturate(_Intensity);

                float2 warp = float2(
                    CoalFbm(p * 4.0 + _Seed * 1.3),
                    CoalFbm(p * 4.0 + _Seed * 1.3 + 5.5)
                ) - 0.5;
                float heat = CoalFbm((p + warp * _Warp) * _CrackScale + _Seed * 2.9);

                // Burning out raises the bar the noise has to clear, so the bed breaks into fewer,
                // smaller coals rather than dimming as one piece.
                float threshold =
                    _Threshold + radial * radial * _ThresholdSlope + (1.0 - burn) * _Cooling;
                float excess = heat - threshold;

                float coverage = saturate(excess / max(_Feather, 1e-3)) * saturate(burn * 3.0);
                clip(coverage - 0.02);

                float warmth = saturate(excess / max(_HeatSpan, 1e-3)) * burn;

                half3 color = lerp(_DeepColor.rgb, _EmberColor.rgb, saturate(warmth / max(_CoreCut, 1e-3)));
                color = lerp(
                    color,
                    _CoreColor.rgb,
                    saturate((warmth - _CoreCut) / max(1.0 - _CoreCut, 1e-3))
                );

                float glint =
                    smoothstep(_GlintCut, _GlintCut + 0.025, CoalFbm(p * _GlintScale + _Seed * 6.1))
                    * saturate((warmth - _GlintHeat) / 0.06);
                color += _GlintColor.rgb * glint;

                return half4(color * coverage, coverage);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
