// Battle Plan — "unpainted board" fog-of-war wash for per-cell quads (ArtDirection §7.9).
// _EdgeMask marks fog boundaries in UV order: left, right, bottom, top.
//
// THE EDGE DOES THE WORK, NOT THE FILL. The fill is a flat chroma-free wash barely off the deck at
// 1.38:1; the boundary is a crisp drawn line at 4.07:1 that is the whole legibility mechanism.
// That needs a SECOND colour, which is why this is not the pure colour change §7.9 expected — see
// _BoundaryColor below. It also needs the boundary term to run the other way: the previous shader
// FADED the wash out at its own seam, producing the soft-edged fog that anti-pattern 17 rules out.
Shader "BattlePlan/FogOverlay"
{
    Properties
    {
        _FogColor ("Fog Fill", Color) = (0.749, 0.7569, 0.7412, 0.68)
        _BoundaryColor ("Boundary Line", Color) = (0.1176, 0.149, 0.1804, 0.85)
        _EdgeSoftness ("Boundary Width", Range(0.01, 0.5)) = 0.06
        _BoundaryOpacity ("Boundary Strength", Range(0, 1)) = 1
        [PerRendererData] _EdgeMask ("Boundary Edges", Vector) = (0, 0, 0, 0)

        // Alpha, and it always has been: the shipped shader was hardcoded Blend SrcAlpha
        // OneMinusSrcAlpha with a near-black _FogColor, so it darkened. The Multiply and Additive
        // paths exist for parity with the rest of the FX set, but the fog is the one overlay that
        // must not multiply — a multiply cannot make the board LIGHTER than the deck, and
        // "unpainted board" is defined as being lighter than what is painted.
        [Header(Compositing)]
        [Enum(Additive, 0, Alpha, 1, Multiply, 2)] _CompositeMode ("Composite Mode", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
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
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "BP_FXComposite.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _FogColor;
                half4 _BoundaryColor;
                half _EdgeSoftness;
                half _BoundaryOpacity;
                half _CompositeMode;
                half _SrcBlend;
                half _DstBlend;
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

                // 1 exactly on a fogged/unfogged seam, 0 once _EdgeSoftness inside the cell. The
                // old shader used the complement of this to thin the wash at its own boundary,
                // which feathered the fog outward. Here it draws a line instead.
                // Named lineStrength, not line: `line` is an HLSL geometry-primitive keyword.
                half lineStrength = (1.0 - smoothstep(0.0, _EdgeSoftness, boundaryDistance))
                            * _BoundaryOpacity;

                half3 tint = lerp(_FogColor.rgb, _BoundaryColor.rgb, lineStrength);
                half coverage = lerp(_FogColor.a, _BoundaryColor.a, lineStrength);
                return BP_Composite(_CompositeMode, tint, coverage);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
