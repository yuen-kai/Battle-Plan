// Battle Plan — the matter knocked off a body at the moment of contact (see HitReaction.cs).
//
// One uneven lump of spall on a camera-facing card, planted on the side the blow arrived from so
// that it bites into the victim's silhouette and spills onto the floor beyond it. The board floor
// sits at luminance 182 of 255: an additive wash has seventy levels to climb and an occluder has a
// hundred and eighty to fall, so the structure here is opaque matter darker than the board and the
// light is a small core burning inside it, never a glow laid over it.
//
// The ember is authored so that red clips and green and blue do not. A warm colour driven past the
// scene's 1.8 bloom threshold in all three channels tonemaps to cream whatever hue it started as,
// and its halo eats the dark outline it is supposed to sit against — which is exactly how the
// previous version of this effect deleted the unit it was marking.
//
// Every boundary is a one-pixel step. A silhouette that fades over twenty pixels reads as light;
// only a stopped edge reads as material.
Shader "BattlePlan/HitStamp"
{
    Properties
    {
        // Vector rather than Color throughout: a Color property is converted from gamma on upload
        // in a linear project, and every value here was solved against the measured post chain, so
        // it has to arrive at the frame exactly as written.
        _MassLit ("Mass Lit (linear rgb)", Vector) = (0.07, 0.058, 0.055, 1)
        _MassShade ("Mass Shade (linear rgb)", Vector) = (0.016, 0.012, 0.013, 1)
        _HotColor ("Ember (linear rgb)", Vector) = (1.55, 0.32, 0.045, 1)
        _CoreColor ("Ember Core (linear rgb)", Vector) = (3.2, 0.34, 0.05, 1)
        _GlintColor ("Glint (linear rgb)", Vector) = (4.4, 2.2, 0.7, 1)
        _Facing ("Facing (xy, screen space)", Vector) = (1, 0, 0, 0)
        _Reach ("Reach", Range(0.2, 1.4)) = 1
        _Solid ("Solid", Range(0, 1)) = 1
        _Hot ("Hot", Range(0, 1.4)) = 1
        _Break ("Break", Range(0, 1)) = 0
        _Seed ("Seed", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+48"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "Forward"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MassLit;
                float4 _MassShade;
                float4 _HotColor;
                float4 _CoreColor;
                float4 _GlintColor;
                float4 _Facing;
                float _Reach;
                float _Solid;
                float _Hot;
                float _Break;
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
                float2 card = (IN.uv - 0.5) * 2.0;

                float facingLength = length(_Facing.xy);
                float2 facing = facingLength > 1e-4 ? _Facing.xy / facingLength : float2(1.0, 0.0);

                // Rotated so +x runs back along the blow, out onto clean floor.
                float2 blow = float2(
                    dot(card, facing),
                    card.x * facing.y - card.y * facing.x
                );
                float span = length(blow);
                float angle = atan2(blow.y, blow.x);
                float aa = max(fwidth(span), 1e-4);

                // One decisive body with needles off its rim, which is what the reference impacts
                // are: a stopped edge and a few long points, never a lumpy outline. Three prime
                // frequencies on unrelated phases means no two points are the same length and the
                // ring of them never closes into a cog.
                float points =
                      0.34 * pow(max(0.0, cos(7.0 * angle + 0.90)), 8.0)
                    + 0.27 * pow(max(0.0, cos(3.0 * angle - 2.10)), 6.0)
                    + 0.13 * pow(max(0.0, cos(11.0 * angle + 0.30)), 10.0);

                // Slightly long away from the body and short across it: the mass lies on the floor
                // it was struck onto while still covering the near edge of the silhouette. The
                // clamp keeps the longest needle inside the card at full reach.
                float lean = 0.58 + 0.42 * saturate(0.5 + 0.5 * blow.x / max(span, 1e-4));
                float edge = min(0.42 + points, 0.76) * lean * _Reach * (1.0 - 0.3 * _Break);

                // Chunks leave one at a time. A shape that dissolves on a single opacity ramp is a
                // decal fading out; a shape that loses pieces is matter coming apart.
                float chunk = floor((angle + 3.14159265) * 9.0 / 6.28318531);
                float chunkLife = 0.2 + 0.8 * frac(sin(chunk * 12.9898 + _Seed * 7.13) * 43758.5453);
                float mass = 1.0 - smoothstep(edge - aa, edge + aa, span);
                mass *= step(_Break, chunkLife);

                // Three struck-off pieces at a third, a fifth and a tenth of the body's size, so
                // the event has a size hierarchy and its outline is not one closed curve.
                float grit = 1.0 - smoothstep(0.085 - aa, 0.085 + aa, length(blow - float2(0.70, 0.30) * _Reach));
                grit = max(grit, 1.0 - smoothstep(0.052 - aa, 0.052 + aa, length(blow - float2(0.52, -0.55) * _Reach)));
                grit = max(grit, 1.0 - smoothstep(0.034 - aa, 0.034 + aa, length(blow - float2(0.82, -0.16) * _Reach)));
                mass = max(mass, grit * step(_Break, 0.55));
                mass *= 1.0 - smoothstep(0.955, 0.995, length(card));

                // The ember sits in the outer half of the lump, with a band of spall left between
                // it and the unit: the light has to be a separate mass in front of the character,
                // not a wash across it.
                float2 emberOffset = blow - float2(0.34, 0.0);
                float emberDistance = length(emberOffset);
                float emberAngle = atan2(emberOffset.y, emberOffset.x);
                float emberEdge = _Hot * (
                      0.300
                    + 0.050 * cos(3.0 * emberAngle + 1.4)
                    + 0.028 * cos(5.0 * emberAngle - 0.7)
                );
                float ember = 1.0 - smoothstep(emberEdge - aa, emberEdge + aa, emberDistance);
                float core = 1.0 - smoothstep(emberEdge * 0.50 - aa, emberEdge * 0.50 + aa, emberDistance);
                float glint = 1.0 - smoothstep(emberEdge * 0.18 - aa, emberEdge * 0.18 + aa, emberDistance);
                ember *= mass;
                core *= mass;
                glint *= mass;

                // A lit side and a shadow side with a hard terminator, keyed off the screen rather
                // than off the blow, so the lump is lit by the board and not by its own rotation.
                float shade = dot(normalize(card + float2(1e-5, 1e-5)), float2(-0.55, 0.835));
                float shadeAA = max(fwidth(shade), 1e-4);
                float lit = smoothstep(-0.12 - shadeAA, -0.12 + shadeAA, shade);

                float coverage = saturate(mass * _Solid);
                float3 tone = lerp(_MassShade.rgb, _MassLit.rgb, lit);
                tone = lerp(tone, _HotColor.rgb, ember);
                tone = lerp(tone, _CoreColor.rgb, core);
                tone = lerp(tone, _GlintColor.rgb, glint);

                return half4(tone * coverage, coverage);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
