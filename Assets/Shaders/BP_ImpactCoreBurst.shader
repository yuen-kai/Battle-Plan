// Battle Plan — the impact flash, drawn as material rather than as light (see ImpactCore.cs).
//
// The board floor sits at luminance 183 of 255 and the Neutral tonemapper resolves pure white to
// 217, so an additive layer on this board can lift the frame by a handful of levels and no more.
// Everything here therefore alpha-blends at full coverage: the written radiance survives verbatim,
// the shape gets a silhouette the board cannot show through, and the ramp can carry real chroma
// instead of whatever the floor bleaches it down to.
//
// A mesh only has to carry its normalised radius in uv.x; the ramp does the rest:
//   uv.x below _MidStart   -> _CoreColor, dead flat, no gradient at all
//   _MidStart .. _MidEnd   -> _CoreColor into _MidColor
//   _MidEnd .. 1           -> _MidColor into _EdgeColor
//
// Colours are Vector properties, not Color properties, so they arrive as the literal linear
// radiances the ramp was measured against with no sRGB conversion applied on the way in.
Shader "BattlePlan/ImpactCoreBurst"
{
    Properties
    {
        _CoreColor ("Core (linear)", Vector) = (6, 6, 6, 1)
        _MidColor ("Mid (linear)", Vector) = (0.2, 0.24, 1, 1)
        _EdgeColor ("Edge (linear)", Vector) = (0.02, 0.05, 0.6, 1)
        _MidStart ("Ramp Start", Range(0, 1)) = 0.69
        _MidEnd ("Ramp End", Range(0, 1)) = 0.78
        _FadeStart ("Fade Start", Range(0, 1)) = 1
        _Opacity ("Opacity", Range(0, 1)) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTestMode ("ZTest", Float) = 8
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
            ZTest [_ZTestMode]
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _MidColor;
                float4 _EdgeColor;
                float _MidStart;
                float _MidEnd;
                float _FadeStart;
                float _Opacity;
                float _ZTestMode;
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
                float radius = saturate(IN.uv.x);

                // The stops are animated from script and can be driven together; keeping them
                // strictly ordered here is what stops smoothstep dividing by zero on the frame
                // the white core finishes collapsing.
                float midStart = min(_MidStart, 0.995);
                float midEnd = clamp(_MidEnd, midStart + 0.002, 0.998);

                float3 tint = lerp(_CoreColor.rgb, _MidColor.rgb, smoothstep(midStart, midEnd, radius));
                tint = lerp(tint, _EdgeColor.rgb, smoothstep(midEnd, 1.0, radius));

                // At _FadeStart = 1 this collapses to a hard geometric edge: coverage holds full
                // right up to the rim, so the silhouette terminates within a pixel instead of
                // trailing off over a gradient. Lower values buy a soft shoulder for light spill.
                float fade = saturate((1.0 - radius) / max(1.0 - _FadeStart, 1e-3));
                float alpha = _Opacity * fade * fade * (3.0 - 2.0 * fade);
                clip(alpha - 0.002);

                return half4(tint, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
