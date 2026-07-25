// Battle Plan — energy beam for LineRenderers (Area Lock laser, Target Lock, trails).
// White-hot core + colored glow falloff across the line width (UV.y), subtle scrolling
// energy noise along the length (UV.x).
//
// Three composite modes, because a beam needs a different construction depending on how bright the
// floor is. Additive is the dark-board form: HDR colors that URP Bloom picks up. Alpha is the
// opaque body of a light-board beam, which reads by occluding the floor rather than by out-glowing
// it. Multiply is the contour line that runs underneath both, darkening the deck along the beam —
// on a light board that dark edge carries most of the legibility.
//
// See BeamVFX.cs for the three-layer stack and BP_FXComposite.hlsl for why the mode is a float.
Shader "BattlePlan/EnergyBeam"
{
    Properties
    {
        [HDR] _GlowColor ("Glow Color", Color) = (1, 0.14, 0.25, 1)
        [HDR] _CoreColor ("Core Color", Color) = (4, 4, 4, 1)
        _CoreWidth ("Core Width (0-1 of beam)", Range(0.01, 1)) = 0.25
        _EdgeSoftness ("Edge Softness", Range(0.01, 1)) = 0.5
        _ScrollSpeed ("Scroll Speed", Float) = 3
        _NoiseScale ("Noise Scale (along length)", Float) = 8
        _NoiseStrength ("Noise Strength", Range(0, 1)) = 0.35
        _Intensity ("Overall Intensity", Range(0, 4)) = 1

        [Header(Compositing)]
        [Enum(Additive, 0, Alpha, 1, Multiply, 2)] _CompositeMode ("Composite Mode", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1
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
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "BP_FXComposite.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GlowColor;
                half4 _CoreColor;
                half _CoreWidth;
                half _EdgeSoftness;
                half _ScrollSpeed;
                half _NoiseScale;
                half _NoiseStrength;
                half _Intensity;
                half _CompositeMode;
                half _SrcBlend;
                half _DstBlend;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            // Cheap 1D value noise, good enough for beam shimmer.
            half hash(half n) { return frac(sin(n) * 43758.5453); }
            half vnoise(half x)
            {
                half i = floor(x);
                half f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(hash(i), hash(i + 1.0), f);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color; // LineRenderer start/end color & alpha ride here
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Distance from beam center line: uv.y in [0,1], center at 0.5
                half d = abs(IN.uv.y - 0.5) * 2.0; // 0 center -> 1 edge

                // Scrolling energy shimmer along the beam
                half n = vnoise(IN.uv.x * _NoiseScale - _Time.y * _ScrollSpeed);
                half shimmer = 1.0 - _NoiseStrength + _NoiseStrength * n;

                // Soft outer glow falloff
                half glow = saturate(1.0 - d);
                glow = pow(glow, 1.0 + (1.0 - _EdgeSoftness) * 4.0);

                // Hot core
                half core = 1.0 - smoothstep(_CoreWidth * 0.5, _CoreWidth, d);

                if (_CompositeMode >= BP_COMPOSITE_ALPHA)
                {
                    // Alpha and multiply have a single source colour to spend, so the core and the
                    // glow become one tint chosen by how central the pixel is, and their sum
                    // becomes coverage. Summing colours the way additive does would just wash the
                    // core out here.
                    half3 tint = lerp(_GlowColor.rgb, _CoreColor.rgb, saturate(core));
                    half coverage = saturate(glow + core) * shimmer * _Intensity * IN.color.a;
                    return BP_Composite(_CompositeMode, tint * IN.color.rgb, coverage);
                }

                half3 col = _GlowColor.rgb * glow + _CoreColor.rgb * core;
                col *= shimmer * _Intensity;

                // Respect LineRenderer vertex color (tint + fade-out animations)
                col *= IN.color.rgb * IN.color.a;
                return half4(col, saturate((glow + core) * IN.color.a));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
