// Battle Plan — one plate of broken deck tipping into the hole, on a quad held to the camera.
// Angular rather than lobed: the ragged outline is cut by two straight fractures, because deck
// breaks along flat faces and a piece without them reads as a pebble. Opaque and far darker than
// the board, with the face turned up to the sky at luminance 46 and only the edge turned down into
// the shaft carrying the void's cyan, which puts a hundred levels of contrast inside one small mass
// without turning the plate itself into a lamp.
//   It leaves by going under rather than by fading: _Sink walks a hard cut up the card while the
//   driver lowers it, which is what separates matter that was buried from a sprite whose alpha
//   ran out.
//   Tones arrive as LINEAR rgb through SetVector so the composited value in frame is predictable.
Shader "BattlePlan/DeathShard"
{
    Properties
    {
        _ShadowTone ("Shadow (linear rgb)", Vector) = (0.0075, 0.0105, 0.0130, 1)
        _LitTone ("Lit (linear rgb)", Vector) = (0.030, 0.036, 0.042, 1)
        _GlowColor ("Void Light (linear rgb)", Vector) = (0.020, 0.62, 1.30, 1)
        _Glow ("Glow", Range(0, 1)) = 0
        _Opacity ("Opacity", Range(0, 1)) = 1
        _Radius ("Radius", Range(0, 1)) = 0.8
        _Rag ("Raggedness", Range(0, 0.7)) = 0.4
        _RagCount ("Rag Count", Float) = 3
        _Sink ("Sink", Range(-0.1, 1.2)) = -0.05
        _Facets ("Facets", Range(0, 1)) = 0.55
        _Seed ("Seed", Float) = 0
        _LightDir ("Key Light Direction", Vector) = (0.55, 0.84, 0, 0)
        _EdgePixels ("Edge Pixels", Range(0.5, 4)) = 1.2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+30"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Forward"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowTone;
                half4 _LitTone;
                half4 _GlowColor;
                half _Glow;
                half _Opacity;
                half _Radius;
                half _Rag;
                float _RagCount;
                half _Sink;
                half _Facets;
                float _Seed;
                float4 _LightDir;
                half _EdgePixels;
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

            float ShardHash(float n)
            {
                return frac(sin(n) * 43758.5453);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 offset = IN.uv - 0.5;
                float radius = length(offset) * 2.0;
                float angle = atan2(offset.y, offset.x);
                float aa = max(fwidth(radius), 1e-6) * _EdgePixels;

                float rag =
                    sin(angle * _RagCount + _Seed) * 0.54 +
                    sin(angle * (_RagCount * 2.0 + 1.0) - _Seed * 1.3) * 0.30 +
                    sin(angle * (_RagCount * 3.0 + 2.0) + _Seed * 0.6) * 0.16;

                float edge = max(_Radius * (1.0 + _Rag * rag), 1e-4);
                half mask = saturate((edge - radius) / aa);

                // Deck breaks along flat faces. Two straight fractures at unrelated angles turn a
                // lobed blob into a plate, and no two pieces are cut the same way.
                float2 cutA = float2(cos(_Seed * 1.9), sin(_Seed * 1.9));
                float2 cutB = float2(cos(_Seed * 3.4 + 2.1), sin(_Seed * 3.4 + 2.1));
                float slabA =
                    _Radius * 0.5 * (0.58 + 0.34 * ShardHash(_Seed * 1.1)) - dot(offset, cutA);
                float slabB =
                    _Radius * 0.5 * (0.52 + 0.40 * ShardHash(_Seed * 2.7 + 1.0)) - dot(offset, cutB);
                mask *= saturate(slabA / max(fwidth(slabA) * _EdgePixels, 1e-5));
                mask *= saturate(slabB / max(fwidth(slabB) * _EdgePixels, 1e-5));

                // Below the cut is under the deck. A hard line, so the piece is buried rather than
                // dissolved.
                mask *= saturate((IN.uv.y - _Sink) / max(fwidth(IN.uv.y) * _EdgePixels, 1e-5));
                clip(mask - 0.004);

                float2 key = normalize(_LightDir.xy + float2(1e-5, 1e-5));
                float plane = saturate(dot(offset, key) * 2.4 / max(_Radius, 1e-4) + 0.5);
                float crease = sin((offset.x * 1.7 - offset.y) * 9.0 + _Seed * 4.0);
                half shade = saturate(0.06 + 0.72 * plane + _Facets * 0.22 * crease);

                half3 color = lerp(_ShadowTone.rgb, _LitTone.rgb, shade);

                // A rim, not a coat of paint. The shaft under these pieces is now bright enough
                // that a plate washed all over in its colour reads as a pale chip floating in the
                // hole; only the edge turned down into the light should carry it, and the rest of
                // the plate has to stay dark or it stops being a silhouette.
                float rim = saturate((1.0 - abs(edge - radius) / max(edge * 0.22, 1e-5)));
                float underside = saturate(0.5 - dot(offset, key) * 2.4 / max(_Radius, 1e-4));
                color += _GlowColor.rgb * (_Glow * rim * rim * underside);

                return half4(color, mask * _Opacity);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
