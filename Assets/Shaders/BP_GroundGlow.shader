// Battle Plan — additive radial glow for ground FX on a default Unity Quad.
// One shader covers three shapes via _RingWidth:
//   _RingWidth = 1   -> soft filled disc (telegraphs, lock markers, ability glow pools)
//   _RingWidth < 1   -> ring/shockwave (expanding impact rings)
// Tint per-instance via MaterialPropertyBlock ("_GlowColor") — see ImpactShockwave.cs.
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
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

                half pulse = 1.0;
                if (pulseSpeed > 0.0)
                {
                    pulse = 1.0 - pulseAmount * 0.5 + pulseAmount * 0.5 * sin(_Time.y * pulseSpeed * 6.2831853);
                }

                half3 col = glowColor.rgb * mask * intensity * pulse * glowColor.a;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
