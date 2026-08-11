// Battle Plan — the caster's charge for ability wind-ups: a small shaded orb sitting off the muzzle
// and the converging gather of blades that feeds it, drawn on camera-facing quads.
//
// The whole piece is turquoise, and it has to be. On an 8-bit frame a pixel whose red channel has
// clipped reaches luminance 255 * (0.2126 + 0.7874 * (1 - s) + 0.0119 * h * s) for hue h and
// saturation s: at s = 0.45 that does not clear 200 until h = 26, and at s = 0.6 not until h = 37.
// Every warm colour that is both bright and saturated therefore lands within twenty degrees of the
// amber the wind-up is already burning into the floor. Near hue 165 the same saturation reaches
// luminance 220, and 165 sits far enough off the 185-195 arc the death blast owns that the two
// cannot be confused with each other either.
//
// The orb is matter with a light inside it rather than a light with an outline: a spherical term
// runs the face toward the target far above the face away from it, a hot interior sits under that,
// and the white glint is a garnish about a tenth of its area — never a replacement for the colour,
// because everything this tonemapper drives past the clip point comes back as cream. The blades are
// the same hue pushed down into material: saturated, well under the board's own value on their
// unlit side, and carrying a hard keyline so each keeps a silhouette over a floor at luminance 182.
//
// Drive every property per-instance from AbilityWindup.cs.
Shader "BattlePlan/WindupCharge"
{
    Properties
    {
        [HDR] _CoreColor ("Lit Face", Color) = (0.9, 3, 1.78, 1)
        [HDR] _EdgeColor ("Hot Face", Color) = (1.152, 3.84, 2.28, 1)
        [HDR] _DeepColor ("Unlit Face", Color) = (0.522, 1.74, 1.032, 1)
        [HDR] _GlintColor ("Glint Colour", Color) = (3.2, 3.5, 3.4, 1)
        _KeyColor ("Keyline Colour", Color) = (0.008, 0.017, 0.014, 1)

        _BladeMode ("Blade Mode", Range(0, 1)) = 0
        _Fill ("Fill", Range(0, 1)) = 1
        _Glint ("Glint Strength", Range(0, 2)) = 1
        _LightDir ("Key Direction", Vector) = (1, 0.3, 0, 0)
        _KeyWidth ("Keyline Width", Range(0, 0.4)) = 0.07
        _Seed ("Seed", Float) = 0
    }

    SubShader
    {
        Tags
        {
            // After the hazard plate, which is the surface this is standing over.
            "RenderType" = "Transparent"
            "Queue" = "Transparent+50"
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
                float4 _EdgeColor;
                float4 _DeepColor;
                float4 _GlintColor;
                float4 _KeyColor;
                float _BladeMode;
                float _Fill;
                float _Glint;
                float4 _LightDir;
                float _KeyWidth;
                float _Seed;
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
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 q = (IN.uv - 0.5) * 2.0;
                // Blades are scaled hard along one axis, so the two directions cannot share one
                // antialiasing width. Both are taken here, outside every branch.
                float2 pixelQ = max(fwidth(q), 1e-4);
                float aa = max(pixelQ.x, pixelQ.y);
                float2 key = normalize(_LightDir.xy + float2(1e-5, 0));

                float3 col;
                float cover;

                if (_BladeMode > 0.5)
                {
                    // A comet, not a needle: mass toward the head that meets the orb, tapering to
                    // nothing at the tail so the shell reads as travelling rather than radiating.
                    float along = saturate((q.x + 1.0) * 0.5);
                    float taper = 2.0 * along - 1.0;
                    float spine = 0.72 * sin(_Seed) * (along - along * along);
                    float halfWidth = sqrt(saturate(1.0 - taper * taper)) * (0.42 + 0.58 * along);
                    float offset = q.y - spine;

                    cover = saturate((halfWidth - abs(offset)) / pixelQ.y + 0.5)
                        * saturate((1.0 - abs(q.x)) / pixelQ.x + 0.5);

                    // A hard silhouette around a flat fill is a paper cut-out however clean the
                    // outline is, so the blade is a prism: one lit edge, one edge in shadow, and a
                    // head that sees the charge it is falling into.
                    float lateral = clamp(offset / max(halfWidth, 1e-3), -1.0, 1.0);
                    float3 normal = normalize(
                        float3(0.30, lateral * 0.95, sqrt(saturate(1.0 - lateral * lateral)) * 0.75)
                    );
                    float facing = saturate(dot(normal, normalize(float3(key, 0.5))));
                    float shade = saturate(0.26 + 0.82 * facing) * (0.54 + 0.46 * along);

                    col = lerp(_DeepColor.rgb, _CoreColor.rgb, smoothstep(0.20, 0.62, shade));
                    col = lerp(col, _EdgeColor.rgb, pow(saturate((along - 0.68) / 0.32), 1.6));

                    // Never let the keyline swallow a tip: past the shoulders it tracks the local
                    // width instead of holding a fixed one.
                    float keyIn = max(halfWidth - _KeyWidth, halfWidth * 0.4);
                    float rim = saturate(
                        cover - saturate((keyIn - abs(offset)) / pixelQ.y + 0.5)
                    );
                    col = lerp(col, _KeyColor.rgb, rim * saturate(0.32 + 0.9 * (1.0 - facing)));
                }
                else
                {
                    float radius = length(q);
                    float around = atan2(q.y, q.x);
                    // A couple of percent of wander: a machined circle at this size reads as a
                    // piece of interface, not as something being forged.
                    float edge = 1.0
                        - 0.045 * sin(around * 3.0 + _Seed)
                        - 0.026 * sin(around * 5.0 - _Seed * 1.7);
                    cover = saturate((edge - radius) / aa + 0.5);

                    float3 normal = float3(q, sqrt(saturate(1.0 - min(radius * radius, 1.0))));
                    float facing = saturate(dot(normal, normalize(float3(key * 0.92, 0.42))));
                    col = lerp(_DeepColor.rgb, _CoreColor.rgb, pow(facing, 0.62));

                    // The interior source. An orb lit only from outside is a ball bearing; this is
                    // supposed to be something with a furnace in it.
                    float furnace = saturate(1.0 - length(q - key * 0.26) / 0.62);
                    col = lerp(col, _EdgeColor.rgb, furnace * furnace * 0.88);

                    // Hard internal chords that brighten rather than darken. The interior needs
                    // structure and cannot pay for it out of luminance: every band that dips is a
                    // band that stops reading as bright.
                    float chordAt = dot(q, float2(-0.51, 0.86)) * 3.1 + _Seed * 0.4;
                    float chord = 0.5 - abs(frac(chordAt) - 0.5);
                    col *= 1.0 + 0.32 * saturate((0.115 - chord) / (aa * 2.4) + 0.5);

                    // Small on purpose. White is what this tonemapper gives back when hue is spent,
                    // so it is allowed a glint and nothing more.
                    float glint = smoothstep(0.33, 0.145, length(q - key * 0.34));
                    col = lerp(col, _GlintColor.rgb, saturate(glint * _Glint));

                    // Thickest on the face turned away from the target, so the keyline reads as the
                    // orb's own shadow rather than as a hoop drawn round it.
                    float rim = saturate(
                        cover - saturate((edge - _KeyWidth - radius) / aa + 0.5)
                    );
                    float away = saturate(0.5 - 0.5 * dot(q / max(radius, 1e-4), key));
                    col = lerp(col, _KeyColor.rgb, rim * saturate(0.3 + 1.1 * away));
                }

                float alpha = cover * _Fill;
                return half4(col * alpha, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
