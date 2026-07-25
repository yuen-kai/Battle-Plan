// Battle Plan — soft light-shaft for procedural vision cone meshes (Bullet Echo flashlight).
// Expects a fan mesh with UV.x = angular position across the cone (0..1, 0.5 = center ray)
// and UV.y = normalized distance from the apex (0 apex -> 1 far edge). See VisionConeVisual.cs.
Shader "BattlePlan/VisionCone"
{
    Properties
    {
        [HDR] _ConeColor ("Cone Color", Color) = (0.55, 0.8, 1, 1)
        _ApexIntensity ("Apex Intensity", Range(0, 2)) = 0.25
        _FarFade ("Far Fade Start (0-1)", Range(0, 1)) = 0.55
        _SideSoftness ("Side Softness", Range(0.01, 1)) = 0.35
        _FlickerSpeed ("Flicker Speed (0 = off)", Float) = 0
        _FlickerAmount ("Flicker Amount", Range(0, 1)) = 0.1

        // An additive cone is invisible on a bright floor. The non-additive paths exist so the
        // cone can instead read as a tinted or shaded wedge; which one it becomes is the art
        // director's fog-of-war ruling, not a rendering choice, so the default is unchanged.
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
            "Queue" = "Transparent+30"
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
                half4 _ConeColor;
                half _ApexIntensity;
                half _FarFade;
                half _SideSoftness;
                half _FlickerSpeed;
                half _FlickerAmount;
                half _CompositeMode;
                half _SrcBlend;
                half _DstBlend;
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

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Fade toward the far edge of the cone
                half distFade = 1.0 - smoothstep(_FarFade, 1.0, IN.uv.y);

                // Fade toward the lateral edges (uv.x: 0 and 1 are the cone sides)
                half side = 1.0 - abs(IN.uv.x - 0.5) * 2.0;
                side = smoothstep(0.0, _SideSoftness, side);

                // Slightly hotter near the apex, like a real lamp
                half apex = lerp(1.0, 1.0 + _ApexIntensity, saturate(1.0 - IN.uv.y * 2.5));

                half flicker = 1.0;
                if (_FlickerSpeed > 0.0)
                {
                    flicker = 1.0 - _FlickerAmount * 0.5
                        + _FlickerAmount * 0.5 * sin(_Time.y * _FlickerSpeed * 6.2831853);
                }

                half mask = distFade * side * apex * flicker;
                return BP_Composite(_CompositeMode, _ConeColor.rgb, mask * _ConeColor.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
