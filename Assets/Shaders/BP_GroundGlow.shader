// Battle Plan — radial ground FX on a default Unity Quad. One shader covers five roles:
//   _RingWidth = 1   -> soft filled disc (telegraphs, lock markers, ability glow pools)
//   _RingWidth < 1   -> ring/shockwave (expanding impact rings)
//   _CompositeMode 2 -> the same disc or ring as a DARKENING, which is how a contact shadow, a
//                       negative flash and the dark half of a two-tone shockwave are all drawn
//                       without a second shader.
// Tint per-instance via MaterialPropertyBlock ("_GlowColor") — see ImpactShockwave.cs.
//
// _MaskTex multiplies the procedural shape with an authored greyscale mask, which is how the
// muzzle flash gets its four-point star while still being a GroundGlow disc (ArtDirection §8.5).
// It defaults to white, so every material and every runtime `new Material(shader)` that predates
// it is unchanged.
//
// Blend state defaults to additive, so materials authored before the composite work behave exactly
// as they did. See BP_FXComposite.hlsl for why the mode is a float and not a keyword.
Shader "BattlePlan/GroundGlow"
{
    Properties
    {
        [HDR] _GlowColor ("Glow Color", Color) = (1, 0.14, 0.25, 1)
        _RingWidth ("Ring Width (1 = filled disc)", Range(0.02, 1)) = 1
        _EdgeSoftness ("Edge Softness", Range(0.01, 1)) = 0.5
        _Intensity ("Intensity", Range(0, 8)) = 1
        _PulseSpeed ("Pulse Speed (0 = off)", Float) = 0
        _PulseAmount ("Pulse Amount", Range(0, 1)) = 0.3
        _MaskTex ("Shape Mask (greyscale)", 2D) = "white" {}

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
            "Queue" = "Transparent+40"
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
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "BP_FXComposite.hlsl"

            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);
            float4 _MaskTex_ST;

            // Render state has to come from the material, so these cannot live in the instancing
            // buffer alongside the tint. Two composite modes therefore mean two materials — which
            // is correct anyway, since they cannot batch into one draw call.
            half _CompositeMode;
            half _SrcBlend;
            half _DstBlend;

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(half4, _GlowColor)
                UNITY_DEFINE_INSTANCED_PROP(half, _RingWidth)
                UNITY_DEFINE_INSTANCED_PROP(half, _EdgeSoftness)
                UNITY_DEFINE_INSTANCED_PROP(half, _Intensity)
                UNITY_DEFINE_INSTANCED_PROP(half, _PulseSpeed)
                UNITY_DEFINE_INSTANCED_PROP(half, _PulseAmount)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                half4 glowColor = UNITY_ACCESS_INSTANCED_PROP(Props, _GlowColor);
                half ringWidth = UNITY_ACCESS_INSTANCED_PROP(Props, _RingWidth);
                half edgeSoft = UNITY_ACCESS_INSTANCED_PROP(Props, _EdgeSoftness);
                half intensity = UNITY_ACCESS_INSTANCED_PROP(Props, _Intensity);
                half pulseSpeed = UNITY_ACCESS_INSTANCED_PROP(Props, _PulseSpeed);
                half pulseAmount = UNITY_ACCESS_INSTANCED_PROP(Props, _PulseAmount);

                // Radial distance from quad center, 0 center -> 1 at inscribed circle edge
                half r = length(IN.uv - 0.5) * 2.0;

                // Outer edge fade
                half outer = 1.0 - smoothstep(1.0 - edgeSoft, 1.0, r);
                // Inner cut for ring mode (ringWidth 1 leaves a filled disc)
                half innerEdge = 1.0 - ringWidth;
                half inner = smoothstep(innerEdge - edgeSoft * 0.5, innerEdge + 0.001, r);

                half mask = saturate(outer * inner);
                mask = pow(mask, 1.5);
                mask *= SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, TRANSFORM_TEX(IN.uv, _MaskTex)).a;

                half pulse = 1.0;
                if (pulseSpeed > 0.0)
                {
                    pulse = 1.0 - pulseAmount * 0.5 + pulseAmount * 0.5 * sin(_Time.y * pulseSpeed * 6.2831853);
                }

                // In additive mode intensity is HDR headroom and drives bloom. In multiply mode the
                // same number reads as how hard the shadow bites, because coverage is what gets
                // scaled and the tint stays put.
                half weight = mask * intensity * pulse * glowColor.a;
                return BP_Composite(_CompositeMode, glowColor.rgb, weight);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
