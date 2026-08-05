// Battle Plan — thrown chunks of torn-up ground.
// The board floor renders at luminance 181-184, so a chunk only has a silhouette if it is darker
// than the floor. Shading is therefore driven from explicit linear tones rather than from scene
// lighting, which keeps the rendered pixels inside the 40-70 band whatever the stage light is
// doing, and the terminator is a three-value step so the edge between values stays hard.
// Tones arrive as Vector properties, not Colors, so no gamma conversion sits between the value
// authored in DebrisBurst.cs and the value that reaches the frame.
Shader "BattlePlan/DebrisChunk"
{
    Properties
    {
        _ShadowTone ("Shadow Tone (linear)", Vector) = (0.021, 0.018, 0.0155, 0)
        _LitTone ("Lit Tone (linear)", Vector) = (0.052, 0.044, 0.037, 0)
        _EmberTone ("Ember Tone (linear)", Vector) = (2.4, 0.52, 0.04, 0)
        _KeyDirection ("Key Direction", Vector) = (0.55, 0.72, -0.42, 0)
        _EmberDirection ("Ember Direction", Vector) = (-0.62, 0.48, -0.62, 0)
        _EmberSharpness ("Ember Sharpness", Range(1, 24)) = 7
        _Ember ("Ember", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry+20"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShadowTone;
                float4 _LitTone;
                float4 _EmberTone;
                float4 _KeyDirection;
                float4 _EmberDirection;
                float _EmberSharpness;
                float _Ember;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 normalWS = normalize(IN.normalWS);

                float key = saturate(dot(normalWS, normalize(_KeyDirection.xyz)) * 0.5 + 0.5);
                float tier = floor(key * 2.999) * 0.5;
                float3 facet = lerp(_ShadowTone.xyz, _LitTone.xyz, tier);

                // Vertex colour carries a per-facet tone in .r and an ember licence in .g, baked
                // when the chunk mesh is jittered, so one shared material still gives every piece
                // its own internal value spread.
                float3 body = facet * (0.62 + IN.color.r * 0.78);
                float ember =
                    pow(saturate(dot(normalWS, normalize(_EmberDirection.xyz))), _EmberSharpness)
                    * _Ember
                    * IN.color.g;

                return half4(body + _EmberTone.xyz * ember, 1);
            }
            ENDHLSL
        }

        // Present so the chunk still draws when the renderer primes depth and tests the opaque
        // pass against it; without it the forward pass fails ZTest Equal and the piece vanishes.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShadowTone;
                float4 _LitTone;
                float4 _EmberTone;
                float4 _KeyDirection;
                float4 _EmberDirection;
                float _EmberSharpness;
                float _Ember;
            CBUFFER_END

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            DepthVaryings DepthVert(DepthAttributes IN)
            {
                DepthVaryings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 DepthFrag(DepthVaryings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
