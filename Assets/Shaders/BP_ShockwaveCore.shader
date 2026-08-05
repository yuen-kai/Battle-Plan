// Battle Plan — the white-hot heart of an impact shockwave, on a default Unity Quad laid flat.
// The dust front is a container and this is what goes in it. The cyan band around it is authored
// near 190 degrees at saturation 0.55, where the tonemapper's ceiling is luminance ~208, so that
// band is physically incapable of carrying a flash however hard it is driven — the flash has to be
// its own layer, and it has to be white.
//   Opaque, not additive: the centre is written far above the buffer's ceiling so it resolves dead
//   flat at 255 in all three channels, and the silhouette terminates on a hard alpha step instead
//   of trailing off. Only the outermost stop of the ramp carries hue; everything inside it is white,
//   because a saturated colour and a clipped one are mutually exclusive on this tonemapper.
//   The rim is wobbled around the circle and bitten into by the same kind of lumpy relief the dust
//   is shaded with, so what fills the crater is torn rather than a lamp.
//   _Radius is normalised to the quad's half-extent, so the core grows in the shader and the
//   transform is never rescaled: the alpha step stays the same number of screen pixels throughout.
//   Colours arrive as LINEAR rgb through SetVector.
Shader "BattlePlan/ShockwaveCore"
{
    Properties
    {
        _WhiteColor ("Clipping White (linear rgb)", Vector) = (3.6, 3.6, 3.6, 1)
        _HotColor ("Hot Shoulder (linear rgb)", Vector) = (1.05, 2.2, 3.0, 1)
        _RimColor ("Rim (linear rgb)", Vector) = (1.0, 2.1, 2.9, 1)
        _Plateau ("White Plateau", Range(0.05, 0.9)) = 0.3
        _Mid ("Shoulder End", Range(0.1, 1)) = 0.58
        _Radius ("Radius", Range(0, 1)) = 0.77
        _Opacity ("Opacity", Range(0, 1)) = 1
        _Wobble ("Rim Wobble", Range(0, 0.4)) = 0.16
        _RagCount ("Rim Cells", Float) = 5
        _Bite ("Rim Bite", Range(0, 0.5)) = 0.2
        _BiteScale ("Bite Scale (cycles per quad)", Float) = 26
        _Seed ("Bite Seed", Float) = 0
        _ShapeSeed ("Rim Seed", Float) = 0
        _EdgePixels ("Edge Pixels", Range(0.5, 4)) = 1.4
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+70"
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
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _WhiteColor;
                float4 _HotColor;
                float4 _RimColor;
                half _Plateau;
                half _Mid;
                half _Radius;
                half _Opacity;
                half _Wobble;
                float _RagCount;
                half _Bite;
                float _BiteScale;
                float _Seed;
                float _ShapeSeed;
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

            float Hash11(float n)
            {
                n = frac(n * 0.1031);
                n *= n + 33.33;
                n *= n + n;
                return frac(n);
            }

            float AngleNoise(float turns, float cells, float seed)
            {
                float scaled = turns * cells;
                float cell = floor(scaled);
                float blend = frac(scaled);
                blend = blend * blend * (3.0 - 2.0 * blend);
                float a = Hash11(fmod(cell, cells) * 13.71 + seed);
                float b = Hash11(fmod(cell + 1.0, cells) * 13.71 + seed);
                return lerp(a, b, blend) * 2.0 - 1.0;
            }

            static const float2 kBiteDir[4] =
            {
                float2(0.9848, 0.1736), float2(0.2079, -0.9781),
                float2(-0.6428, 0.7660), float2(0.7660, 0.6428)
            };
            static const float kBiteFreq[4] = { 1.00, 1.71, 2.63, 4.11 };
            static const float kBiteAmp[4] = { 0.52, 0.30, 0.13, 0.05 };

            float BiteField(float2 p, float scale, float seed)
            {
                float height = 0.0;
                [unroll]
                for (int i = 0; i < 4; i++)
                    height += sin(dot(p, kBiteDir[i]) * (scale * kBiteFreq[i]) + seed * (1.7 + i * 0.83))
                        * kBiteAmp[i];
                return height;
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
                float radius = length(offset) * 2.0 / max(_Radius, 1e-4);
                float turns = atan2(offset.y, offset.x) * 0.15915494 + 0.5;

                float wobble = 1.0 + _Wobble * (
                    AngleNoise(turns, _RagCount, _ShapeSeed) * 0.66 +
                    AngleNoise(turns, _RagCount * 2.0 + 1.0, _ShapeSeed + 21.3) * 0.34);
                // Torn in two dimensions, not just around the circle: a rim modulated only by angle
                // still reads as a circle with a decorated outline.
                float bite = saturate(0.5 + 0.75 * BiteField(offset, _BiteScale, _Seed));
                float edge = max(wobble * (1.0 - _Bite * bite), 0.35);

                float aa = max(fwidth(radius), 1e-6) * _EdgePixels;
                half mask = saturate((edge - radius) / aa);
                clip(mask - 0.004);

                float t = saturate(radius / edge);
                float mid = max(_Mid, _Plateau + 0.02);
                half3 tint = lerp(_WhiteColor.rgb, _HotColor.rgb, smoothstep(_Plateau, mid, t));
                tint = lerp(tint, _RimColor.rgb, smoothstep(mid, 1.0, t));
                return half4(tint, mask * _Opacity);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
