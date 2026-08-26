// Battle Plan — holographic protection bubble for a guarded unit. Additive fresnel shell so the
// character stays readable through it, with scrolling latitude bands. Expects a UV sphere whose
// UV.y runs pole to pole. See GuardOrbVisual.cs.
Shader "BattlePlan/GuardOrb"
{
    Properties
    {
        [HDR] _OrbColor ("Orb Color", Color) = (0.42, 0.78, 1, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 3.2
        _RimIntensity ("Rim Intensity", Range(0, 4)) = 0.85
        _BandCount ("Band Count", Float) = 26
        _BandSpeed ("Band Scroll Speed", Float) = 0.4
        _BandIntensity ("Band Intensity", Range(0, 2)) = 0.3
        _BandSharpness ("Band Sharpness", Range(1, 24)) = 10
        _PulseSpeed ("Pulse Speed", Float) = 0.8
        _PulseAmount ("Pulse Amount", Range(0, 1)) = 0.25
        _Bloom ("Bloom", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+20"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Forward"
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OrbColor;
                half _RimPower;
                half _RimIntensity;
                half _BandCount;
                half _BandSpeed;
                half _BandIntensity;
                half _BandSharpness;
                half _PulseSpeed;
                half _PulseAmount;
                half _Bloom;
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
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS = _WorldSpaceCameraPos - positionWS;
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 normalWS = normalize(IN.normalWS);
                half3 viewDirWS = normalize(IN.viewDirWS);

                // Grazing angles carry the shell; facing the camera it thins out so the unit inside
                // is never hidden behind its own indicator.
                half facing = saturate(abs(dot(normalWS, viewDirWS)));
                half rim = pow(1.0 - facing, _RimPower) * _RimIntensity;

                half band = sin((IN.uv.y * _BandCount - _Time.y * _BandSpeed) * 6.2831853) * 0.5 + 0.5;
                band = pow(band, _BandSharpness) * _BandIntensity;

                half pulse = 1.0 - _PulseAmount * 0.5
                    + _PulseAmount * 0.5 * sin(_Time.y * _PulseSpeed * 6.2831853);

                // Bands stay faint across the interior and brighten into the rim, so the shell
                // reads as a surface without painting over the character inside it.
                half mask = (rim + band * (0.15 + rim)) * pulse * _Bloom;
                half3 col = _OrbColor.rgb * _OrbColor.a * mask;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
