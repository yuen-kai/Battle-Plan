// Battle Plan — spark thrown out of an aftermath, drawn as a comet rather than a dot.
// The quad is stretched along the spark's screen velocity by Aftermath.cs; this shader fills it
// with a tail that widens into a head, so a fast spark leaves a motion streak and a slow one
// collapses into a mote.
//
// Blending is premultiplied rather than additive, and that is the whole point of this shader. The
// board floor sits at scene-linear (0.46, 0.58, 0.62); anything added on top of it inherits that
// green and blue, which caps an additive spark at saturation 0.29 while it is still bright — a
// cream sliver, whatever colour is fed in. Pushing the red hard enough to beat the floor only
// reaches saturation 0.62 by falling to 1.11x board luminance, so there is no additive answer. A
// spark is burning matter and occludes what is behind it: covering the floor first frees the hue
// entirely, and the tail lands at rgb (251, 177, 0) — saturation 1.0 at 0.97x board.
//
// The core is a lift toward yellow, not toward white. A core bright enough to clip green as well
// as red tonemaps to cream and takes the tail's hue with it, so it is authored to leave the head
// at rgb (255, 228, 94) — still saturation 0.63 at luminance 224.
Shader "BattlePlan/AftermathEmber"
{
    Properties
    {
        [HDR] _CoreColor ("Core Color", Color) = (1.05, 0.78, 0.2, 1)
        [HDR] _TailColor ("Tail Color", Color) = (2.3, 0.52, 0.06, 1)
        _Intensity ("Intensity", Range(0, 4)) = 1
        _HeadY ("Head Position", Range(-0.5, 0.5)) = 0.3
        _TailY ("Tail Position", Range(-0.5, 0.5)) = -0.42
        _HeadWidth ("Head Width", Range(0.02, 0.5)) = 0.3
        _TailWidth ("Tail Width", Range(0.005, 0.3)) = 0.055
        _Solidity ("Spine Solidity", Range(1, 6)) = 2.6
        _TailAlpha ("Tail Coverage", Range(0, 1)) = 0.34
        _CoreCut ("Core Cut", Range(0.1, 0.95)) = 0.58
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+70"
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
                half4 _CoreColor;
                half4 _TailColor;
                half _Intensity;
                half _HeadY;
                half _TailY;
                half _HeadWidth;
                half _TailWidth;
                half _Solidity;
                half _TailAlpha;
                half _CoreCut;
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
                float2 p = IN.uv - 0.5;
                float2 tailPoint = float2(0.0, _TailY);
                float2 headPoint = float2(0.0, _HeadY);
                float2 axis = headPoint - tailPoint;

                float along = saturate(dot(p - tailPoint, axis) / max(dot(axis, axis), 1e-5));
                float offAxis = length(p - (tailPoint + axis * along));
                float width = lerp(_TailWidth, _HeadWidth, along * along);

                float body = saturate(1.0 - offAxis / max(width, 1e-4));

                // The spine covers the board outright and the streak thins to a smear behind it,
                // so the head keeps its hue and the trail still reads as motion rather than as a
                // second solid object.
                float coverage = saturate(body * _Solidity) * lerp(_TailAlpha, 1.0, along * along);

                // Fragments cool along the trail: the head is where the material still burns.
                float heat = body * lerp(0.28, 1.0, along * along);
                float core =
                    saturate((body - _CoreCut) / max(1.0 - _CoreCut, 1e-3))
                    * smoothstep(0.55, 0.95, along);

                // Dimming has to take the coverage with it, or a spent spark leaves an opaque
                // black splinter on the board.
                half alpha = coverage * saturate(_Intensity);
                clip(alpha - 0.004);

                half3 emissive = _TailColor.rgb * heat + _CoreColor.rgb * core * core;
                return half4(emissive * (_Intensity * coverage), alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
