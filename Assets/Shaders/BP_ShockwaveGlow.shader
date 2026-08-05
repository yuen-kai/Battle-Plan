// Battle Plan — the skirt of hue the shockwave's white core sits inside, on a Unity Quad laid flat.
// The reference never puts white straight onto the board: there is always saturated colour between
// the flash and whatever it is standing on. This is that colour, and it is the one part of the core
// allowed to be soft, because it is light rather than matter.
//   Additive, and authored on a secondary axis near 190 degrees so green and blue may clip while
//   red is held down and the hue survives. It reaches from the core's rim outward and dies before
//   the dust front, so it lights the crater rather than washing the deck.
//   _Inner and _Reach are normalised to the quad's half-extent. Colour arrives as LINEAR rgb.
Shader "BattlePlan/ShockwaveGlow"
{
    Properties
    {
        _GlowColor ("Ability Hue (linear rgb)", Vector) = (0.075, 0.475, 1, 1)
        _Intensity ("Intensity", Range(0, 8)) = 1
        _Inner ("Inner Radius", Range(0, 1)) = 0.6
        _Reach ("Reach", Range(0.02, 1)) = 0.35
        _Falloff ("Falloff", Range(0.3, 4)) = 1.8
        _Uneven ("Angular Unevenness", Range(0, 1)) = 0.45
        _RagCount ("Unevenness Cells", Float) = 7
        _ShapeSeed ("Seed", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+65"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Forward"
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _GlowColor;
                half _Intensity;
                half _Inner;
                half _Reach;
                half _Falloff;
                half _Uneven;
                float _RagCount;
                float _ShapeSeed;
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
                float turns = atan2(offset.y, offset.x) * 0.15915494 + 0.5;

                float along = saturate((radius - _Inner) / max(_Reach, 1e-4));
                float fall = pow(max(1.0 - along, 1e-5), _Falloff);
                // A blast does not light its crater evenly, and an even skirt reads as a printed
                // halo. The core's own rim seed drives this, so the two agree on where the mass is.
                float uneven = lerp(1.0 - _Uneven, 1.0,
                    saturate(0.5 + 0.6 * AngleNoise(turns, _RagCount, _ShapeSeed + 5.9)));
                // Out before the quad runs out, or the skirt would end on a straight mesh edge.
                float3 glow = _GlowColor.rgb * (fall * uneven * _Intensity * saturate((1.0 - radius) / 0.12));
                clip(max(glow.r, max(glow.g, glow.b)) - 0.004);
                return half4(glow, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
