// Battle Plan — dark, soft-edged shadow for per-cell fog-of-war quads.
// _EdgeMask marks fog boundaries in UV order: left, right, bottom, top.
Shader "BattlePlan/FogOverlay"
{
    Properties
    {
        _FogColor ("Fog Color", Color) = (0.006, 0.009, 0.016, 0.76)
        _EdgeSoftness ("Boundary Softness", Range(0.01, 0.5)) = 0.22
        _BoundaryOpacity ("Boundary Opacity", Range(0, 1)) = 0.32
        [PerRendererData] _EdgeMask ("Boundary Edges", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
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
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _FogColor;
                half _EdgeSoftness;
                half _BoundaryOpacity;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(half4, _EdgeMask)
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
                half4 edgeMask = UNITY_ACCESS_INSTANCED_PROP(Props, _EdgeMask);
                half boundaryDistance = 1.0;

                if (edgeMask.x > 0.5)
                    boundaryDistance = min(boundaryDistance, IN.uv.x);
                if (edgeMask.y > 0.5)
                    boundaryDistance = min(boundaryDistance, 1.0 - IN.uv.x);
                if (edgeMask.z > 0.5)
                    boundaryDistance = min(boundaryDistance, IN.uv.y);
                if (edgeMask.w > 0.5)
                    boundaryDistance = min(boundaryDistance, 1.0 - IN.uv.y);

                half edgeFade = smoothstep(0.0, _EdgeSoftness, boundaryDistance);
                half alpha = _FogColor.a * lerp(_BoundaryOpacity, 1.0, edgeFade);
                return half4(_FogColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
