// Battle Plan — opaque charge shards for ability wind-ups.
//
// These are matter, not light: a hard diamond silhouette with a dark keyline, so a shard keeps a
// readable outline over both the pale floor and the dark hazard plate. The particle system stretches
// the quad along velocity, which turns the diamond into a shard pointing where it is travelling.
// Particle vertex colour carries alpha only; hue comes from the material so it stays HDR-capable.
Shader "BattlePlan/WindupMote"
{
    Properties
    {
        [HDR] _CoreColor ("Core Colour", Color) = (1, 0.26, 0.0005, 1)
        _RimColor ("Rim Colour", Color) = (0.0055, 0.0012, 0.0003, 1)
        _RimWidth ("Rim Width", Range(0, 0.5)) = 0.22
        _Sharpness ("Silhouette Sharpness", Range(0.4, 3)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+46"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "Forward"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _RimColor;
                float _RimWidth;
                float _Sharpness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 d = (IN.uv - 0.5) * 2.0;
                float shard = abs(d.x) * _Sharpness + abs(d.y);
                float aa = max(fwidth(shard), 1e-4);

                float body = 1.0 - smoothstep(1.0 - aa, 1.0, shard);
                float coreEdge = max(1.0 - _RimWidth, 0.05);
                float core = 1.0 - smoothstep(coreEdge - aa, coreEdge, shard);

                float alpha = body * saturate(IN.color.a);
                float3 col = lerp(_RimColor.rgb, _CoreColor.rgb, core);
                return half4(col * alpha, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
