// Battle Plan — the live embers inside a debris burn.
// Additive light does not read on this board on its own, but it does read against the char
// BattlePlan/DebrisScorch has already multiplied into the floor, and that pairing is the only way
// a ground mark can be both saturated and bright enough to register as colour rather than dirt.
// Nearly all of the addition goes into red: red buys hue for almost no luminance, which keeps the
// tile seam under the mark readable instead of flooding it flat.
// A bed of coals does not dim as one sheet — seams breathe at their own rate and go out one at a
// time — so extinction here is per patch and never global, because a tail that fades uniformly is
// a static image on an opacity ramp however long it lasts.
// Tones are Vector properties so no gamma conversion sits between the value authored in
// DebrisBurst.cs and the frame.
Shader "BattlePlan/DebrisCoals"
{
    Properties
    {
        _MainTex ("Coals", 2D) = "white" {}
        _CoalTone ("Coal Tone (linear)", Vector) = (0.50, 0.03, 0.0, 0)
        _Glow ("Glow", Range(0, 4)) = 1
        _Cut ("Cut", Range(0, 1)) = 0.14
        _Edge ("Edge", Range(1, 40)) = 10
        _Out ("Going Out", Float) = -0.34
        _OutEdge ("Going Out Edge", Range(1, 12)) = 3.2
        _Flicker ("Flicker", Range(0, 0.5)) = 0
        _Phase ("Flicker Phase", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+5"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
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
                float4 _CoalTone;
                float _Glow;
                float _Cut;
                float _Edge;
                float _Out;
                float _OutEdge;
                float _Flicker;
                float _Phase;
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
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 mark = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                clip(mark.a - _Cut);

                // Red is the coal seam network; blue is how long the ground beneath it holds its
                // heat. Together they rank the seams, and _Out walks that rank, so the bed empties
                // from its coldest filaments inward rather than dimming everywhere at once.
                float rank = saturate(mark.r * 0.52 + mark.b * 0.48);
                float alive = saturate((rank - _Out) * _OutEdge);

                // Two incommensurate rates, offset per patch, so the breathing never resolves into
                // a pulse the whole bed shares.
                float breath = 1.0
                    + _Flicker * (0.62 * sin(_Phase + rank * 21.0 + mark.g * 7.0)
                        + 0.38 * sin(_Phase * 0.41 + mark.b * 13.0 - mark.r * 9.0));

                float heat = saturate((mark.a - _Cut) * _Edge) * mark.r * alive * _Glow;
                return half4(_CoalTone.xyz * max(heat * breath, 0.0), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
