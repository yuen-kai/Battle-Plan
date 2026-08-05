// Battle Plan — the charred matter a debris burn leaves lying on the floor.
// A multiply can only scale what is already on the board, so a mark made of nothing but
// BattlePlan/DebrisScorch is the tile grid repainted in a new hue: wherever the floor beneath is
// dark the burn goes dark with it, and the seam ends up the most saturated line inside the mark.
// This lays the burn's own material over that result at partial cover, so the mark keeps its hue
// where the floor has none to give and the grid's contrast under it is attenuated rather than
// amplified. Partial on purpose — full cover would erase the seam the occlusion test reads.
// Tones are Vectors so no gamma conversion sits between the value authored in DebrisBurst.cs and
// the frame, and the colour is premultiplied because the same alpha both carries it and cuts the
// floor underneath.
Shader "BattlePlan/DebrisBed"
{
    Properties
    {
        _MainTex ("Scorch", 2D) = "white" {}
        _BedTone ("Bed Tone (linear)", Vector) = (0.150, 0.030, 0.010, 0)
        _BedSeam ("Bed Seam Tone (linear)", Vector) = (0.245, 0.052, 0.014, 0)
        _Lay ("Bed Clock", Float) = 0
        _Cover ("Bed Cover", Range(0, 1)) = 0.62
        _Density ("Density", Range(0, 1)) = 1
        _Cut ("Cut", Range(0, 1)) = 0.1
        _Edge ("Edge", Range(1, 40)) = 12
        _Cool ("Cool", Float) = 0
        _Bite ("Ash Bite", Range(0, 0.5)) = 0.26
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+2"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend One OneMinusSrcAlpha
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
                float4 _BedTone;
                float4 _BedSeam;
                float _Lay;
                float _Cover;
                float _Density;
                float _Cut;
                float _Edge;
                float _Cool;
                float _Bite;
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

                // Shares the char's silhouette and the char's per-patch heat rank, so the two
                // cards flake away together instead of one outliving the other's outline.
                float cooled = saturate((_Cool - mark.b) * 2.2);
                clip(mark.a - _Cut - cooled * _Bite);

                // Only the charred core carries matter. The thin edge of the mark is stain, and
                // laying material there would give the burn a hard rim it has not earned.
                float density = saturate((mark.a - _Cut) * _Edge) * _Density;
                float body = saturate(density * (0.30 + mark.g * 1.15));

                // Ash crusts over each patch as that patch stops burning, so the mark is still
                // changing across its whole area for half a second after the flame has gone.
                float arrive = saturate((_Lay - mark.b * 0.5) * 6.0);
                float cover = saturate(_Cover * arrive * body * (1.0 - 0.72 * cooled));
                clip(cover - 0.004);

                float3 bed = lerp(_BedTone.xyz, _BedSeam.xyz, saturate(mark.r * 1.1));
                return half4(bed * cover, cover);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
