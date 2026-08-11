// Battle Plan — the hole a dead unit leaves, on a quad laid flat over the cell it stood on.
// A negative, not a mark: the deck sits at luminance 182 of 255 and this is the one event that has
// to read as material removed, so the throat floor is authored down at luminance 6 with a hard torn
// boundary, and the body sinks behind it rather than in front of it.
//   The quad is yawed so that +v points away from the viewer along the ground. A cylindrical pit of
//   radius R seen at 73 degrees projects to the rim ellipse with its own floor drawn as the same
//   ellipse slid down the screen; everything between the two outlines is inner wall and everything
//   inside the lower one is floor. That single offset is the whole geometry: it puts the lit surface
//   against the far rim, leaves the near rim overhanging in shadow, and drops the deepest point
//   below the middle. A hole lit in its middle is a glow inside a stain, which is what this was.
//   Colour is the other half of the read. A landing owns warm orange, so a death owns cold cyan and
//   owns it in area rather than as a rim. It is authored so blue clips first and red never does,
//   which keeps the saturated cyan up at the luminance where it can be seen instead of only down in
//   the dark, and keeps the bloom in its own colour instead of tonemapping to cream.
//   Colours arrive as LINEAR rgb through SetVector so the composited value in frame is predictable.
Shader "BattlePlan/DeathMark"
{
    Properties
    {
        _FloorColor ("Throat Floor (linear rgb)", Vector) = (0.0062, 0.0082, 0.0104, 1)
        _RubbleColor ("Lit Rubble (linear rgb)", Vector) = (0.0195, 0.0375, 0.0530, 1)
        _WallColor ("Inner Wall Rock (linear rgb)", Vector) = (0.0062, 0.0105, 0.0148, 1)
        _LipColor ("Broken Lip (linear rgb)", Vector) = (0.0290, 0.0385, 0.0455, 1)
        _GlowColor ("Void Light (linear rgb)", Vector) = (0.46, 3.80, 4.95, 1)
        _CoreColor ("Throat Light (linear rgb)", Vector) = (0.020, 0.135, 0.95, 1)
        _GlintColor ("Glint (linear rgb)", Vector) = (3.20, 4.60, 4.40, 1)
        _Glow ("Glow", Range(0, 1)) = 0
        _Fill ("Throat Fill", Range(0, 1.4)) = 0
        _CrackGlow ("Crack Glow", Range(0, 1)) = 0
        _Crack ("Crack Reach", Range(0, 1)) = 0
        _CrackLength ("Crack Length", Range(0, 1.2)) = 0.9
        _Radius ("Radius", Range(0, 1)) = 0.465
        _Rag ("Raggedness", Range(0, 0.6)) = 0.36
        _RagCount ("Rag Count", Float) = 3
        _Throat ("Throat Depth", Range(0.2, 1.6)) = 0.88
        _WallSoft ("Wall Base Softness", Range(0.01, 0.4)) = 0.09
        _CrestAt ("Crest Start", Range(0, 0.9)) = 0.30
        _LipWidth ("Near Lip Width", Range(0.02, 0.5)) = 0.16
        _Close ("Close", Range(0, 1.4)) = 0
        _CloseAxis ("Close Axis", Vector) = (0.6, -0.8, 0, 0)
        _Erode ("Erode", Range(-0.8, 1.3)) = -0.7
        _Grain ("Relief", Range(0, 1)) = 1
        _GrainScale ("Relief Scale", Float) = 168
        _CreviceScale ("Crevice Scale", Float) = 74
        _VeinScale ("Vein Scale", Float) = 41
        _Vein ("Vein", Range(0, 1.5)) = 0.7
        _Opacity ("Opacity", Range(0, 1)) = 1
        _Seed ("Seed", Float) = 0
        _Phase ("Phase", Float) = 0
        _EdgePixels ("Edge Pixels", Range(0.5, 4)) = 1.2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+10"
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
                half4 _FloorColor;
                half4 _RubbleColor;
                half4 _WallColor;
                half4 _LipColor;
                half4 _GlowColor;
                half4 _CoreColor;
                half4 _GlintColor;
                half _Glow;
                half _Fill;
                half _CrackGlow;
                half _Crack;
                half _CrackLength;
                half _Radius;
                half _Rag;
                float _RagCount;
                half _Throat;
                half _WallSoft;
                half _CrestAt;
                half _LipWidth;
                half _Close;
                float4 _CloseAxis;
                half _Erode;
                half _Grain;
                float _GrainScale;
                float _CreviceScale;
                float _VeinScale;
                half _Vein;
                half _Opacity;
                float _Seed;
                float _Phase;
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

            float DeathHash(float n)
            {
                return frac(sin(n) * 43758.5453);
            }

            /// A network of thin splits rather than a stripe field: three warped sine fronts at
            /// unrelated orientations, thinned by taking the nearest of the three. Returns the
            /// distance to the nearest front, so 0 is on a crack and 1 is well clear of one.
            float DeathCrackle(float2 p, float scale, float seed)
            {
                float2 f = p * scale;
                float a = sin(f.x * 1.13 + 1.2 * sin(f.y * 0.61 + seed) + seed);
                float b = sin(f.y * 0.97 - 1.1 * sin(f.x * 0.73 + seed * 1.7) + seed * 2.1);
                float c = sin((f.x * 0.62 + f.y * 0.79) * 1.31
                    + sin((f.x * 0.79 - f.y * 0.62) * 0.55 + seed) + seed * 0.7);
                return min(min(abs(a), abs(b)), abs(c));
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
                float2 p = IN.uv - 0.5;
                float span = length(p);
                float radius = span * 2.0;
                float angle = atan2(p.y, p.x);
                float aa = max(fwidth(radius), 1e-6) * _EdgePixels;

                // Three harmonics at coprime counts and unequal weights: a torn outline with no
                // mirror line and no constant width anywhere along it.
                float rag =
                    sin(angle * _RagCount + _Phase) * 0.52 +
                    sin(angle * (_RagCount * 1.9 + 1.0) - _Phase * 0.6 + _Seed) * 0.31 +
                    sin(angle * (_RagCount * 3.3 + 2.0) + _Phase * 0.35 + _Seed * 1.7) * 0.17;

                // The deck does not close evenly. One flank slides back over the hole ahead of the
                // other, so the outline and its centroid both keep moving on the way out.
                float2 outward = p / max(span, 1e-5);
                float2 closing = normalize(_CloseAxis.xy + float2(1e-5, 1e-5));
                float lean = 0.55 + 0.45 * dot(outward, closing);
                float edge = max(_Radius * (1.0 + _Rag * rag) * saturate(1.0 - _Close * lean), 1e-5);

                half bore = saturate((edge - radius) / aa);

                // The deck closes by taking pieces out, so what survives keeps a hard outline to
                // the end instead of thinning into a stain. The bite pattern drifts on its own
                // slow clock, so the pieces that go are different ones every frame.
                float drift = _Phase * 0.25;
                float bite =
                    sin(p.x * 13.0 * 0.85 + drift + _Seed * 1.4) *
                    sin(p.y * 13.0 * 1.1 - drift * 0.76 + _Seed * 2.6) +
                    0.55 * sin((p.x + p.y) * 13.0 * 1.9 + drift * 0.52);
                float intact = 0.5 + 0.32 * bite;
                half inside = bore * saturate(
                    (intact - _Erode) / max(fwidth(intact) * _EdgePixels, 1e-5)
                );

                // Fissures run out across the deck ahead of the hole, uneven in aim, width and
                // length, and they wander as they go. They are what keeps the footprint from
                // having a circle anywhere on its boundary.
                float fracture = 0.0;
                [unroll]
                for (int i = 0; i < 5; i++)
                {
                    float index = (float)i;
                    float aim = _Seed * 0.83 + index * 1.2566 + sin(_Seed + index * 2.7) * 0.66;
                    float delta = angle - aim + 0.14 * sin(radius * 7.0 + _Seed * 1.9 + index * 2.2);
                    delta = atan2(sin(delta), cos(delta));
                    // Every fissure has to finish clear of the rim or the hole it came from
                    // swallows it, so the shortest is still two thirds of the longest.
                    float run =
                        _CrackLength
                        * (0.68 + 0.32 * DeathHash(_Seed * 1.7 + index * 5.3))
                        * saturate(_Crack * (1.35 - 0.6 * DeathHash(_Seed * 2.3 + index * 3.9)));
                    // Widest where it leaves the hole and tapering to a point at the tip, measured
                    // against the torn boundary rather than the nominal radius. These run across
                    // ground the hole never darkens, so their width is bought colour area that
                    // costs the void none of its black.
                    float along = saturate((run - radius) / max(run - edge, 1e-3));
                    float width = (0.100 + 0.092 * DeathHash(_Seed * 2.9 + index * 7.1)) * along;
                    fracture = max(fracture, saturate(1.0 - abs(delta) / max(width, 1e-5)));
                }
                fracture *= 1.0 - bore;
                half crackMask = saturate(
                    (fracture - 0.18) / max(fwidth(fracture) * _EdgePixels, 1e-5)
                );

                half alpha = max(inside, crackMask);
                clip(alpha - 0.004);

                // ---- the pit, as the camera actually sees one -------------------------------
                float2 q = p * 2.0 / max(_Radius, 1e-4);
                float rim = length(q);
                float2 sunk = q + float2(0.0, _Throat);
                float below = length(sunk);

                // Everything on the wall is indexed by the angle around the sunk floor, so the
                // facets, the grooves and the broken base all run down the shaft together instead
                // of being three unrelated patterns laid over each other.
                float around = atan2(sunk.y, sunk.x + 1e-5);
                float facet =
                    0.55 * sin(around * 6.0 + _Seed * 1.3) +
                    0.30 * sin(around * 11.0 - _Seed * 1.9) +
                    0.15 * sin(around * 3.0 + _Seed * 0.5);

                float wallBand = saturate((below - 1.0 - 0.075 * facet) / max(_WallSoft, 1e-4));
                float floorIn = 1.0 - wallBand;
                float sink = pow(saturate(1.0 - below), 0.85);
                float nearLip =
                    saturate((rim - (1.0 - _LipWidth)) / max(_LipWidth, 1e-4))
                    * saturate(-q.y - 0.15);

                // Rubble relief. Four octaves at unrelated orientations so the field is lumpy
                // rather than a woven grid, with the slope taken analytically so every lump has a
                // face toward the light and a face away from it. The last octave is deliberately
                // near the nine-pixel window the interior gets scored in: relief coarser than that
                // averages out inside the window and measures as a flat fill however it looks.
                float2 f = p * _GrainScale;
                float4 ax = float4(1.00, 0.63, 1.71, 2.90);
                float4 ay = float4(0.31, 1.44, -1.13, 2.30);
                float4 amp = float4(0.62, 0.44, 0.29, 0.24);
                float4 phase = float4(
                    _Seed * 3.1, _Seed * 1.7 + 1.9, _Seed * 0.9 + 3.6, _Seed * 2.4 + 5.1
                );
                float4 arg = f.x * ax + f.y * ay + phase;
                float4 ca = cos(arg);
                float slopeX = dot(amp * ax, ca);
                float slopeY = dot(amp * ay, ca);

                // The light lives down the throat, so a wall face turned downward catches it while
                // a floor face turned back up toward the far wall catches it. One key, flipped
                // between the two surfaces.
                float2 key = float2(-0.26 + 0.50 * floorIn, -0.97 + 1.94 * floorIn);
                float facing = dot(float2(slopeX, slopeY), key) / (length(key) * 1.6);
                float shade = saturate(0.5 + 0.5 * tanh(facing * 1.5 * _Grain));

                float crevice = pow(
                    saturate(1.0 - DeathCrackle(p, _CreviceScale, _Seed) / 0.30), 1.5
                );
                float vein = pow(
                    saturate(1.0 - DeathCrackle(p, _VeinScale, _Seed * 2.3 + 4.0) / 0.07), 2.0
                );
                vein *= _Vein * pow(sink, 1.6) * (0.4 + 0.6 * shade);

                // Runnels down the shaft: the relief squeezed across, so it streaks the way heat
                // scars a shaft rather than reading as rock texture pasted onto a curve.
                float2 r = p * _GrainScale * float2(0.62, 0.16);
                float runnel =
                    0.6 * sin(r.x * 1.07 + 0.8 * sin(r.y * 1.3 + _Seed) + _Seed * 2.6) +
                    0.4 * sin(r.x * 2.13 - 0.5 * sin(r.y * 0.9 - _Seed) + _Seed);

                // ---- matter: dead rock, shaded, never a flat fill ---------------------------
                float boulder = 0.5 + 0.5
                    * sin(p.x * _GrainScale * 0.27 + _Seed * 2.0)
                    * sin(p.y * _GrainScale * 0.21 - _Seed * 1.1);
                float rockAmount = shade * (0.72 + 0.28 * boulder)
                    * (1.0 - 0.85 * crevice) * (1.0 - 0.30 * sink);
                half3 rock = lerp(_FloorColor.rgb, _RubbleColor.rgb, rockAmount);
                rock = lerp(rock, _WallColor.rgb, wallBand * 0.85);
                rock = lerp(rock, _LipColor.rgb, nearLip);

                // ---- light: on the rim and the far inner wall, never in the middle ----------
                // How far up the shaft this pixel sits: 1 against the rim where the light spills
                // over the broken edge, 0 at the base where the wall meets the floor.
                float upWall = saturate((rim - _CrestAt) / max(1.0 - _CrestAt, 1e-4));
                float fall = 0.18 + 0.82 * pow(upWall, 0.8);

                // A shaft is faceted, not turned. The big planes carry the read at thumbnail size,
                // the rubble relief carries it up close, and the grooves where the planes meet are
                // what puts sixty to eighty levels between the face toward the light and the face
                // away from it.
                float plane = 0.5 + 0.5 * facet;
                float groove = pow(saturate(1.0 - abs(facet) / 0.13), 1.4);
                float wallLight = wallBand * fall
                    * (0.56 + 0.44 * plane)
                    * (0.82 + 0.18 * shade)
                    * (1.0 - 0.85 * groove) * (1.0 - 0.40 * crevice)
                    * (0.90 + 0.10 * saturate(0.5 + 0.5 * runnel));

                // What is left in the throat once the light has drained out of it. _Fill holds it
                // brim-full while the body is still going down, so the black arrives three frames
                // later than the hole does and the corpse has something to be a silhouette
                // against instead of vanishing into a shape its own colour.
                float throatLight = min(
                    floorIn * saturate(1.0 - sink * 2.6) * (0.05 + 0.16 * shade)
                    + _Fill * floorIn * saturate(1.0 - 0.35 * sink),
                    1.35
                );

                // The white-hot centre is a glint inside the colour, never a replacement for it:
                // a couple of hundred pixels scattered along the broken rim where the light comes
                // over, surrounded by a hundred times as many that are bright and still cyan.
                float glint = pow(saturate((rim - 0.90) / 0.10), 2.0) * wallBand
                    * saturate((DeathCrackle(p, _CreviceScale * 0.6, _Seed + 9.0) - 0.72) / 0.24);

                half3 light = (
                    _GlowColor.rgb * wallLight
                    + _CoreColor.rgb * (throatLight + vein * 2.4)
                    + _GlintColor.rgb * glint
                ) * _Glow;

                // ---- fissures: broken deck either side of a lit core ------------------------
                half3 fissure = lerp(
                    _LipColor.rgb, _FloorColor.rgb, saturate((fracture - 0.18) / 0.34)
                );
                fissure += _CoreColor.rgb
                    * (_CrackGlow * 1.5 * pow(saturate((fracture - 0.46) / 0.22), 1.3));
                fissure += _GlowColor.rgb
                    * (_CrackGlow * (0.30 + 0.54 * saturate(0.5 + 0.5 * runnel))
                        * pow(saturate((fracture - 0.62) / 0.20), 1.2));

                half3 color = lerp(fissure, rock + light, inside);
                return half4(color, alpha * _Opacity);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
