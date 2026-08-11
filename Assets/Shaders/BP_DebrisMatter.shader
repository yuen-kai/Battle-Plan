// Battle Plan — the flat, opaque half of a debris burst: kicked dust, contact shadows and the
// dark slivers thrown past the chunks.
// Everything here is alpha-cut rather than blended. Material only reads on this board if it
// occludes the floor, and an edge only reads if it is a step rather than a ramp, so the silhouette
// comes from clip() and the effect leaves by eroding into hard islands instead of fading to a
// ghost. Tones are Vector properties so no gamma conversion sits between the authored linear value
// and the frame.
// The ember is a *replacement* tone rather than an additive one: hot matter is the same material
// with a different hue, so tinting a lobe's interior costs no extra light and cannot bleach. It
// carries its own floor and spread because the hue's readable value window is much narrower than
// the grey's. See DebrisBurst.cs.
Shader "BattlePlan/DebrisMatter"
{
    Properties
    {
        _MainTex ("Matter", 2D) = "white" {}
        _Tone ("Tone (linear)", Vector) = (0.115, 0.103, 0.092, 0)
        _EmberTone ("Ember Tone (linear)", Vector) = (0, 0, 0, 0)
        _ToneFloor ("Tone Floor", Range(0, 2)) = 0.68
        _ToneSpread ("Tone Spread", Range(0, 2)) = 0.62
        _EmberFloor ("Ember Floor", Range(0, 2)) = 1
        _EmberSpread ("Ember Spread", Range(0, 2)) = 0
        _EmberCut ("Ember Cut", Range(0, 1)) = 0
        _EmberEdge ("Ember Edge", Range(1, 24)) = 1
        _EmberFill ("Ember Fill", Range(0, 1)) = 1
        _Cut ("Cut", Range(0, 2)) = 0.36
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest+20"
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
                float4 _EmberTone;
                float _ToneFloor;
                float _ToneSpread;
                float _EmberFloor;
                float _EmberSpread;
                float _EmberCut;
                float _EmberEdge;
                float _EmberFill;
                float _Cut;
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
                half4 matter = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                clip(matter.a * IN.color.a - _Cut);

                // Green is a baked value break-up: a lobe with a flat interior reads as a decal.
                float3 grey = _Tone.xyz * (_ToneFloor + matter.g * _ToneSpread);
                float3 ember = _EmberTone.xyz * (_EmberFloor + matter.g * _EmberSpread);
                float fill =
                    saturate((matter.r - _EmberCut) * _EmberEdge) * _EmberFill * IN.color.r;
                return half4(lerp(grey, ember, fill), 1);
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
                float4 _EmberTone;
                float _ToneFloor;
                float _ToneSpread;
                float _EmberFloor;
                float _EmberSpread;
                float _EmberCut;
                float _EmberEdge;
                float _EmberFill;
                float _Cut;
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
                half4 matter = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                clip(matter.a * IN.color.a - _Cut);
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
