// Battle Plan — the burning ground at the centre of a debris burst.
// The board floor renders at luminance 181-184, so the hot part of an impact is built as opaque
// material rather than as additive light: a deep ember orange dark enough to occlude the floor and
// saturated far enough past 0.6 to survive the tonemapper, with a small white core clipped into it.
// That makes the flash and the mass the same object, so they cannot peak on different frames.
// The core erodes with _CoreCut instead of dimming, because a dimming white passes through cream
// and cream is what drains hue out of the frame. Tones are Vector properties so no gamma conversion
// sits between the value authored in DebrisBurst.cs and the value that reaches the frame.
Shader "BattlePlan/DebrisFire"
{
    Properties
    {
        _MainTex ("Fire", 2D) = "white" {}
        _Tone ("Body Tone (linear)", Vector) = (0.62, 0.085, 0.015, 0)
        _CoreTone ("Core Tone (linear)", Vector) = (6.4, 5.0, 4.3, 0)
        _ToneFloor ("Tone Floor", Range(0, 2)) = 0.46
        _ToneSpread ("Tone Spread", Range(0, 2)) = 1.2
        _Cut ("Cut", Range(0, 2)) = 0.3
        _CoreCut ("Core Cut", Range(0, 2)) = 0.52
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest+40"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Tone;
                float4 _CoreTone;
                float _ToneFloor;
                float _ToneSpread;
                float _Cut;
                float _CoreCut;
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

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 fire = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                clip(fire.a * IN.color.a - _Cut);

                // Green is the body's own hot-to-cool structure; a flat interior reads as a decal.
                float3 body = _Tone.xyz * (_ToneFloor + fire.g * _ToneSpread);
                float core = saturate((fire.r - _CoreCut) * 22.0);
                return half4(body + _CoreTone.xyz * core, 1);
            }
            ENDHLSL
        }

        // Present so the card still draws when the renderer primes depth and tests the opaque pass
        // against it; without it the forward pass fails ZTest Equal and the card vanishes.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Tone;
                float4 _CoreTone;
                float _ToneFloor;
                float _ToneSpread;
                float _Cut;
                float _CoreCut;
            CBUFFER_END

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            DepthVaryings DepthVert(DepthAttributes IN)
            {
                DepthVaryings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color = IN.color;
                return OUT;
            }

            half4 DepthFrag(DepthVaryings IN) : SV_Target
            {
                half4 fire = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                clip(fire.a * IN.color.a - _Cut);
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
