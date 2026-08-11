// Battle Plan — the burn a debris burst leaves on the floor.
// A scar has to let the tile grid read through it, so this multiplies the floor rather than
// covering it: the seam under the mark keeps its own proportional contrast while the ground it
// runs over turns dark and red. An alpha-blended mark cannot do both — averaging against a pale
// blue-grey floor either hides the seam or bleaches the hue out of the burn.
// Under a multiply the channel ratios are the hue and the magnitude is the darkening, and the two
// are independent: this tone carries an ember hue at the Rec.601 weight a neutral char would have,
// so choosing the colour costs the mark none of its depth. The multiplier is a Vector so no gamma
// conversion sits between the value authored in DebrisBurst.cs and the frame.
// Pairs with BattlePlan/DebrisBed, which lays the burn's own material over this, and
// BattlePlan/DebrisCoals, which lays the live embers over both.
Shader "BattlePlan/DebrisScorch"
{
    Properties
    {
        _MainTex ("Scorch", 2D) = "white" {}
        _CharTone ("Char Tone (linear multiplier)", Vector) = (0.55, 0.14, 0.06, 0)
        _AshTone ("Ash Tone (linear multiplier)", Vector) = (0.56, 0.48, 0.45, 0)
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
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend DstColor Zero
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
                float4 _CharTone;
                float4 _AshTone;
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

                // Blue ranks how long each patch of the burn holds its heat, and _Cool walks that
                // rank. Grey and the flaking that goes with it therefore arrive patch by patch on
                // separate clocks: a mark that greys as one image is a decal on an opacity ramp.
                float cooled = saturate((_Cool - mark.b) * 2.2);
                clip(mark.a - _Cut - cooled * _Bite);

                // Green is a baked char-density break-up, so the burn has scorched patches and
                // thinner ash between them rather than one flat token laid on the board.
                float density = saturate((mark.a - _Cut) * _Edge) * _Density;
                density = saturate(density * (0.52 + mark.g * 0.72));
                float3 tone = lerp(_CharTone.xyz, _AshTone.xyz, cooled);
                return half4(lerp(float3(1, 1, 1), tone, density), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
