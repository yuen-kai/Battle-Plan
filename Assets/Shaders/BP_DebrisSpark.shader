// Battle Plan — additive sprite for thrown debris: spark streaks and star sparkles.
// Particle vertex colours are 8-bit, so they can never clear the scene's 1.8 bloom threshold on
// their own. Brightness therefore lives in _Intensity and _Tint on the material, and the per
// particle colour is left to carry hue and fade only. See DebrisBurst.cs.
Shader "BattlePlan/DebrisSpark"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
        [HDR] _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Range(0, 24)) = 4
        _BlastShadow ("Blast Shadow (originX, originZ, slabs, feather)", Vector) = (0, 0, 0, 0.3)
        _BlastSlab0 ("Slab 0 (centreX, centreZ, normalX, normalZ)", Vector) = (0, 0, 0, 0)
        _BlastSlabEdge0 ("Slab 0 Edge (rightX, rightZ, halfWidth, top)", Vector) = (0, 0, 0, 0)
        _BlastSlab1 ("Slab 1 (centreX, centreZ, normalX, normalZ)", Vector) = (0, 0, 0, 0)
        _BlastSlabEdge1 ("Slab 1 Edge (rightX, rightZ, halfWidth, top)", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+30"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
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

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Tint;
                half _Intensity;
                float4 _BlastShadow;
                float4 _BlastSlab0;
                float4 _BlastSlabEdge0;
                float4 _BlastSlab1;
                float4 _BlastSlabEdge1;
            CBUFFER_END

            #include "BP_BlastShadow.hlsl"

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
                float3 positionWS : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 sprite = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                half mask = sprite.a * IN.color.a;
                half3 col = sprite.rgb * IN.color.rgb * _Tint.rgb * _Intensity * mask
                    * BlastShadow(IN.positionWS);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
