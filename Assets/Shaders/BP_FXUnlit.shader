// Battle Plan — unlit primitives for FX: the white core sphere of an explosion, the backstab slash
// decal, the scorch mark, and the ground contact shadow that tracks a projectile.
//
// Three composite modes. Additive is the hot core on a dark board. Alpha is the opaque saturated
// core on a light board, which reads by occluding the floor instead of out-glowing it. Multiply is
// the contact shadow and the negative flash. See BP_FXComposite.hlsl.
//
// The rim term flips sign: a BRIGHT rim makes a plain sphere read as a plasma shell against
// darkness, and a DARK rim puts a contour around the same sphere so it holds a silhouette against
// a lit floor. Same geometry, same mask, one material float.
Shader "BattlePlan/FXUnlit"
{
    Properties
    {
        [MainTexture] _MainTex ("Mask (greyscale)", 2D) = "white" {}
        [HDR] _TintColor ("Tint (HDR)", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Range(0, 16)) = 1
        _MaskContrast ("Mask Contrast", Range(0.25, 4)) = 1

        [Header(Rim)]
        _FresnelPower ("Rim Power (0 = flat)", Range(0, 8)) = 0
        [Enum(Bright Rim, 0, Dark Rim, 1)] _RimMode ("Rim Mode", Float) = 0
        _RimStrength ("Dark Rim Strength", Range(0, 1)) = 0.85

        [Header(Compositing)]
        [Enum(Additive, 0, Alpha, 1, Multiply, 2)] _CompositeMode ("Composite Mode", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
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
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "BP_FXComposite.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _TintColor;
                half _Intensity;
                half _MaskContrast;
                half _FresnelPower;
                half _RimMode;
                half _RimStrength;
                half _CompositeMode;
                half _SrcBlend;
                half _DstBlend;
                half _Cull;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = positions.positionCS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS = GetWorldSpaceViewDir(positions.positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half mask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a;
                mask = pow(saturate(mask), _MaskContrast);

                half3 tint = _TintColor.rgb * _Intensity;

                if (_FresnelPower > 0.0)
                {
                    half facing = saturate(dot(normalize(IN.normalWS), normalize(IN.viewDirWS)));
                    half rim = pow(1.0 - facing, _FresnelPower);

                    if (_RimMode >= 0.5)
                    {
                        // Dark rim: pull the tint toward black at grazing angles so the primitive
                        // keeps a contour on a lit floor. In multiply mode this is literally a
                        // drawn outline; in alpha mode it is a shaded edge.
                        tint *= 1.0 - saturate(rim * _RimStrength);
                    }
                    else
                    {
                        // Bright rim: a hot shell edge, so a plain ball does not read as a flat
                        // disc from the fixed tactical angle.
                        mask *= rim + 0.35;
                    }
                }

                half coverage = saturate(mask) * _TintColor.a;
                return BP_Composite(_CompositeMode, tint, coverage);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
