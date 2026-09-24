// Battle Plan — the wave a shield throws out from the point something hit it.
// Drawn as a second pass over the slab's own mesh, additively, so the barrier keeps its material
// and this only adds light to it. The shield writes no depth, so the overlay needs no offset.
//   The read is a WAVE, not a flash: the whole point of a block is that the shield took the hit
//   somewhere, and a slab that brightens uniformly says only that something happened. So the whole
//   effect is a function of distance across the face from the impact — a hot crest travelling
//   outward, a charge left in its wake, and a hex lattice whose cells fire one ring at a time as
//   the crest reaches each of them. The lattice is what makes it a barrier rather than a ripple in
//   water: energy arriving in discrete panels reads as something engineered holding a load.
//   Distance is measured in METRES across the face, not in UV, because the slab's scale is wildly
//   non-uniform and a UV-space circle comes out as an ellipse three times wider than it is tall.
//   Feed _FaceScale the slab's world x/y scale and put impacts in the same units.
//   Three pulse slots, oldest evicted: a Sentinel under fire, or caught by ArcSurge's fan, takes
//   several hits inside one pulse's lifetime and each is worth its own wave.
Shader "BattlePlan/ShieldBlockPulse"
{
    Properties
    {
        _PulseColor ("Wave Hue (linear rgb)", Vector) = (0.24, 0.62, 1, 1)
        _CoreColor ("Crest Core (linear rgb)", Vector) = (0.56, 0.84, 1, 1)
        _FaceScale ("Face Scale (world x, world y)", Vector) = (1, 1, 0, 0)
        _Pulse0 ("Pulse 0 (faceX, faceY, front, envelope)", Vector) = (0, 0, 0, 0)
        _Pulse1 ("Pulse 1 (faceX, faceY, front, envelope)", Vector) = (0, 0, 0, 0)
        _Pulse2 ("Pulse 2 (faceX, faceY, front, envelope)", Vector) = (0, 0, 0, 0)
        _Intensity ("Intensity", Range(0, 8)) = 1.6
        _EdgeGain ("Top-Edge Gain", Range(0, 6)) = 3.2
        _Band ("Crest Width (m)", Range(0.02, 2)) = 0.34
        _Wake ("Wake Length (m)", Range(0.05, 4)) = 1.15
        _Cell ("Lattice Cell (m)", Range(0.05, 2)) = 0.30
        _Seam ("Lattice Seam", Range(0.01, 0.5)) = 0.13
        _CrestGain ("Crest Gain", Range(0, 4)) = 1.8
        _FillGain ("Cell Fill Gain", Range(0, 4)) = 0.22
        _SeamGain ("Cell Seam Gain", Range(0, 6)) = 0.9
        _BloomGain ("Impact Bloom Gain", Range(0, 8)) = 0.75
        _BloomReach ("Impact Bloom Reach (m)", Range(0.02, 2)) = 0.30
        _Ambient ("Whole-Slab Lift", Range(0, 1)) = 0.035
        _Seed ("Lattice Seed", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+50"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _PulseColor;
                float4 _CoreColor;
                float4 _FaceScale;
                float4 _Pulse0;
                float4 _Pulse1;
                float4 _Pulse2;
                float _Intensity;
                float _EdgeGain;
                float _Band;
                float _Wake;
                float _Cell;
                float _Seam;
                float _CrestGain;
                float _FillGain;
                float _SeamGain;
                float _BloomGain;
                float _BloomReach;
                float _Ambient;
                float _Seed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 face : TEXCOORD0;
                // How squarely this bit of the slab faces the sky, which is the whole of what the
                // board camera can see of it.
                half up : TEXCOORD1;
            };

            float Hash21(float2 cell)
            {
                float n = dot(cell, float2(127.1, 311.7)) + _Seed;
                n = frac(n * 0.1031);
                n *= n + 33.33;
                n *= n + n;
                return frac(n);
            }

            // Which hex of the lattice a face point sits in, and where inside it. Two offset
            // rectangular grids, nearest centre wins: a hex lattice is exactly the Voronoi diagram
            // of those two lattices, so this is the whole tiling with no trigonometry in it.
            //   Returns xy = offset from the cell centre, zw = the cell's own centre.
            float4 HexCell(float2 p)
            {
                const float2 span = float2(1.0, 1.7320508);
                float4 centres =
                    floor(float4(p, p - float2(0.5, 1.0)) / span.xyxy) + 0.5;
                float4 offsets = float4(
                    p - centres.xy * span,
                    p - (centres.zw + 0.5) * span
                );
                return dot(offsets.xy, offsets.xy) < dot(offsets.zw, offsets.zw)
                    ? float4(offsets.xy, centres.xy * span)
                    : float4(offsets.zw, (centres.zw + 0.5) * span);
            }

            /// How far out of its cell a point is, 0 at the centre and 1 on the boundary. The cell
            /// is the Voronoi region of a triangular lattice, so its faces are the perpendicular
            /// bisectors toward the three neighbour directions and this is the largest of them.
            float HexEdge(float2 offset)
            {
                const float2 diagonal = float2(0.5, 0.8660254);
                offset = abs(offset);
                return 2.0 * max(offset.x, dot(offset, diagonal));
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.face = IN.positionOS.xy * _FaceScale.xy;
                OUT.up = saturate(TransformObjectToWorldNormal(IN.normalOS).y);
                return OUT;
            }

            /// One wave's contribution, split by whether it travels. `standing` is the barrier's
            /// flat acknowledgement of the hit, spread over the whole slab; `travelling` is
            /// everything that moves — the lattice firing, the crest, the contact bloom — and only
            /// that gets flared along the top edge. Amplifying the standing part there as well
            /// floods the one strip the camera can see and the travelling bead stops reading.
            void Wave(
                float4 pulse,
                float2 face,
                float cellDistance,
                float cellRoll,
                float seam,
                inout float3 standing,
                inout float3 travelling
            )
            {
                float envelope = pulse.w;
                if (envelope <= 0.001)
                    return;

                float distance = length(face - pulse.xy);
                float front = pulse.z;

                // The crest, and the only thing here allowed near white: a narrow slab of light
                // riding the wavefront, brightest dead on it.
                float ride = saturate(1.0 - abs(distance - front) / max(_Band, 1e-4));
                float crest = ride * ride * (3.0 - 2.0 * ride);

                // What the crest leaves behind, dying off over _Wake metres. Ahead of the front
                // the shield is untouched, which is what keeps the wave a front rather than a glow.
                float behind = step(distance, front)
                    * saturate(1.0 - (front - distance) / max(_Wake, 1e-4));

                // Each cell fires as the crest reaches ITS centre rather than the pixel's own
                // distance, so a whole panel lights at once and the lattice reads as panels.
                float arrival = step(cellDistance, front)
                    * saturate(1.0 - (front - cellDistance) / max(_Wake, 1e-4));
                float panel = arrival * arrival * (0.45 + 0.85 * cellRoll);

                // The hit itself, held at the point of contact for as long as the wave lasts.
                float bloom = exp(-distance / max(_BloomReach, 1e-4)) * _BloomGain;

                standing += _PulseColor.rgb * (behind * 0.18 + _Ambient) * envelope;
                travelling += (
                    _PulseColor.rgb * (panel * (_FillGain + seam * _SeamGain))
                    + _CoreColor.rgb * (crest * _CrestGain + bloom)
                ) * envelope;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float4 hex = HexCell(IN.face / max(_Cell, 1e-4));
                float2 cellCentre = hex.zw * _Cell;
                float edge = HexEdge(hex.xy);
                // Hard inside, bright on the boundary: a lattice whose cells are filled evenly
                // reads as a texture, and one that is only outlines reads as a wireframe.
                float seam = smoothstep(1.0 - _Seam, 1.0, edge);
                float roll = Hash21(hex.zw * 7.31);

                float3 standing = 0.0;
                float3 travelling = 0.0;
                Wave(_Pulse0, IN.face, length(cellCentre - _Pulse0.xy), roll, seam, standing, travelling);
                Wave(_Pulse1, IN.face, length(cellCentre - _Pulse1.xy), roll, seam, standing, travelling);
                Wave(_Pulse2, IN.face, length(cellCentre - _Pulse2.xy), roll, seam, standing, travelling);

                // The board is watched from seventy-three degrees above the deck, which sees a
                // standing slab's face at a grazing angle and compresses the whole of it into a
                // ribbon a fraction of a metre deep. The rounded top edge is the one part of the
                // barrier pointed at the camera, so the travelling part of the wave flares along
                // it: from above, a block reads as a bead of light running the length of the
                // shield rather than as a pattern on a face nobody can see square on.
                float3 light =
                    (standing + travelling * (1.0 + _EdgeGain * IN.up * IN.up)) * _Intensity;
                clip(max(light.r, max(light.g, light.b)) - 0.004);
                return half4(light, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
