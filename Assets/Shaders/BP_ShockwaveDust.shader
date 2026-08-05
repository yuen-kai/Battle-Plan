// Battle Plan — dark, occluding ground dust front for impact shockwaves.
// The deck sits near luminance 182 of 255, which leaves 73 levels of headroom upward and 182
// downward, so dust here is matter rather than light: alpha-blended geometry several stops below
// the deck, ending in a hard alpha step so it owns a silhouette. Sits on a default Unity Quad laid
// flat above the fog overlay.
//   _Radius / _Thickness are normalised to the quad's half-extent, so the front travels in the
//   shader and the transform is never rescaled — screen-space edge width therefore stays constant.
//   The boundary is deliberately irregular, vented, and heavier on one side. Edge hardness is a
//   property of the step along each ray's own normal, so raggedness costs the silhouette nothing,
//   while a closed constant-width hoop reads as a manufactured object rather than a pressure front.
//   Inside the silhouette the band is a lit surface, not a fill: a wall of dust that rises off the
//   trailing edge and falls over the leading one, lumped by a five-octave relief field, shaded by a
//   grazing lamp sitting in the crater. Every lump therefore has a face toward the blast and a face
//   away from it, and the palette is wide enough for that split to be worth 70 levels rather than
//   the 10 a narrow one allows. The lamp carries the ability's colour, so the lit faces are tinted
//   and the shadows keep the dust's own warm albedo.
//   Two things about that surface are deliberate and were not obvious. The wall's own form is taken
//   from where a pixel sits ACROSS the band, not from a surface normal: a radial slope steep enough
//   to read as a wall turns the whole band edge-on to a grazing lamp, which saturates the key over
//   half of it and joins every lump's terminator into one continuous hard line running inside the
//   material. Dust has no terminator. And the lamp is wrapped rather than clamped at ninety degrees,
//   because a clump of dust is lit by scatter arriving from the whole crater: it has a bright face,
//   a dim face and no line between them. What is left is a cluster of individually lit lumps, which
//   is what displaced material looks like, instead of one unbroken glaze across the ribbon.
//   Colours arrive as LINEAR rgb through SetVector so the composited value in frame is predictable.
Shader "BattlePlan/ShockwaveDust"
{
    Properties
    {
        _DustDark ("Dust Shadow (linear rgb)", Vector) = (0.0116, 0.0093, 0.0073, 1)
        _DustLit ("Dust Sky-Lit (linear rgb)", Vector) = (0.396, 0.359, 0.307, 1)
        _DustKeyLit ("Dust Blast-Lit (linear rgb)", Vector) = (0.212, 0.410, 0.508, 1)
        _Opacity ("Opacity", Range(0, 1)) = 1
        _Radius ("Outer Radius", Range(0, 1)) = 0.7
        _Thickness ("Band Thickness", Range(0.01, 0.98)) = 0.32
        _InnerRag ("Inner Raggedness", Range(0, 1)) = 0.42
        _OuterRag ("Outer Raggedness", Range(0, 0.4)) = 0.17
        _Erode ("Erode", Range(-0.4, 1.05)) = -0.4
        _GrainCells ("Grain Cells", Float) = 5
        _RagCount ("Boundary Cells", Float) = 7
        _Lean ("Directional Mass Lean", Range(0, 0.5)) = 0.29
        _BiasDir ("Heavy Side (xz)", Vector) = (1, 0, 0, 0)
        _Vents ("Vent Count", Range(0, 4)) = 4
        _VentWidth ("Vent Half Width (turns)", Range(0, 0.09)) = 0.038
        _Seed ("Grain Seed", Float) = 0
        _ShapeSeed ("Silhouette Seed", Float) = 0
        _EdgePixels ("Edge Pixels", Range(0.5, 4)) = 1.2
        _LumpScale ("Lump Scale (cycles per quad)", Float) = 26
        _LumpRelief ("Lump Slope", Range(0, 2)) = 0.55
        _LumpShare ("Lump Share Of Key", Range(0, 1)) = 0.4
        _FormIn ("Wall Crest (across band)", Range(0, 1)) = 0
        _FormOut ("Wall Foot (across band)", Range(0, 1)) = 0.92
        _Ambient ("Ambient", Range(0, 0.4)) = 0.012
        _Key ("Blast Key", Range(0, 1.4)) = 0.95
        _KeyElev ("Blast Elevation (radians)", Range(0.05, 1.4)) = 0.314
        _KeyGamma ("Lump Contrast", Range(0.3, 2.5)) = 1
        _Sky ("Sky Fill", Range(0, 0.6)) = 0.02
        _Micro ("Micro Value Break", Range(0, 0.3)) = 0.05
        _TintReach ("Key Tint Reach", Range(0, 2)) = 1.05
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
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DustDark;
                half4 _DustLit;
                half4 _DustKeyLit;
                half _Opacity;
                half _Radius;
                half _Thickness;
                half _InnerRag;
                half _OuterRag;
                half _Erode;
                float _GrainCells;
                float _RagCount;
                half _Lean;
                float4 _BiasDir;
                float _Vents;
                float _VentWidth;
                float _Seed;
                float _ShapeSeed;
                half _EdgePixels;
                float _LumpScale;
                half _LumpRelief;
                half _LumpShare;
                half _FormIn;
                half _FormOut;
                half _Ambient;
                half _Key;
                half _KeyElev;
                half _KeyGamma;
                half _Sky;
                half _Micro;
                half _TintReach;
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

            // Sine-free so the field is identical on every GPU and does not band at large inputs.
            float Hash11(float n)
            {
                n = frac(n * 0.1031);
                n *= n + 33.33;
                n *= n + n;
                return frac(n);
            }

            // Value noise around the circle. The cell count is a whole number so the field wraps
            // exactly at atan2's cut and the silhouette never shows a seam along one axis.
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

            // Places where the front simply is not. A wall of pressurised dust vents where it meets
            // least resistance; three deep tears and one shallow one keep the ring from closing.
            float FrontOpening(float turns, float seed)
            {
                float open = 1.0;
                float phase = Hash11(seed * 0.61 + 4.9);
                [unroll]
                for (int k = 0; k < 4; k++)
                {
                    float place = Hash11(seed * 3.1 + k * 7.13 + 1.7);
                    float span = Hash11(seed * 1.9 + k * 3.71 + 9.3);
                    float centre = (k + 0.10 + 0.80 * place) * 0.25 + phase;
                    float halfWidth = _VentWidth * (0.45 + 0.85 * span);
                    float depth = saturate(_Vents - k) * (k == 3 ? 0.55 : 1.0);
                    float gap = abs(frac(turns - centre + 0.5) - 0.5);
                    float gate = smoothstep(halfWidth * 0.42, halfWidth, gap);
                    open = min(open, lerp(1.0, gate, depth));
                }
                return open;
            }

            static const float2 kReliefDir[5] =
            {
                float2(0.9848, 0.1736), float2(0.2079, -0.9781), float2(-0.6428, 0.7660),
                float2(0.7660, 0.6428), float2(-0.3420, -0.9397)
            };
            static const float kReliefFreq[5] = { 1.00, 1.71, 2.63, 4.11, 6.37 };
            static const float kReliefAmp[5] = { 0.50, 0.31, 0.13, 0.05, 0.02 };

            // The slope carries its own weights. An octave's contribution to the height is its
            // amplitude but its contribution to the SLOPE is amplitude times frequency, and on the
            // relief spectrum above those products are near enough equal that the finest octave
            // swings the shading as hard as the coarsest — across two or three pixels rather than
            // across a lump. Rolling the slope off leaves the mottling intact and takes the
            // steepness out of it.
            static const float kSlopeAmp[5] = { 0.879, 0.283, 0.0836, 0.0214, 0.0055 };

            // Lumped relief with an exact gradient, so the surface normal costs no extra taps. The
            // directions are deliberately off-axis and the frequencies incommensurate: a product of
            // two axis-aligned sines resolves into corduroy the moment it is lit from one side.
            void Relief(float2 p, float scale, float seed, out float height, out float2 slope)
            {
                height = 0.0;
                slope = 0.0;
                [unroll]
                for (int i = 0; i < 5; i++)
                {
                    float k = scale * kReliefFreq[i];
                    float phase = dot(p, kReliefDir[i]) * k + seed * (1.7 + i * 0.83);
                    height += sin(phase) * kReliefAmp[i];
                    slope += cos(phase) * (kSlopeAmp[i] * k) * kReliefDir[i];
                }
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
                float turns = angle * 0.15915494 + 0.5;

                // Derivative-driven so the transition is a fixed number of screen pixels whatever
                // the ring's on-screen size: a step, never a gradient.
                float aa = max(fwidth(radius), 1e-6) * _EdgePixels;

                float outline =
                    AngleNoise(turns, _RagCount, _ShapeSeed) * 0.62 +
                    AngleNoise(turns, _RagCount * 2.0, _ShapeSeed + 31.7) * 0.38;
                float innerLine = AngleNoise(turns, _RagCount + 3.0, _ShapeSeed + 57.3);
                float open = FrontOpening(turns, _ShapeSeed);

                float2 facing = offset / max(length(offset), 1e-5);
                float2 bias = normalize(_BiasDir.xy + float2(1e-5, 1e-5));
                float heavy = 0.5 + 0.5 * dot(facing, bias);

                float outerRadius = _Radius
                    * (1.0 + _OuterRag * outline)
                    * lerp(1.0 - _Lean * 0.12, 1.0 + _Lean * 0.12, heavy)
                    * lerp(0.62, 1.0, open);
                float band = _Thickness * _Radius
                    * (1.0 + _InnerRag * innerLine)
                    * lerp(1.0 - _Lean, 1.0 + _Lean, heavy)
                    * lerp(0.10, 1.0, open);
                float innerRadius = max(outerRadius - band, 0.008);

                half outer = saturate((outerRadius - radius) / aa);
                half inner = saturate((radius - innerRadius) / aa);
                half mask = outer * inner * step(0.055, open);

                half across = saturate((radius - innerRadius) / max(outerRadius - innerRadius, 1e-4));

                float grain =
                    sin(angle * (_GrainCells * 3.3 + 5.0) + _Seed * 2.1 + across * 6.0) * 0.5 +
                    sin(angle * (_GrainCells * 7.1 + 2.0) - _Seed * 1.7 + across * 11.0) * 0.3 +
                    sin(angle * (_GrainCells * 12.7 + 3.0) + _Seed * 0.9 - across * 17.0) * 0.2;

                // Dust dies by breaking apart rather than dissolving, so what is left of it is
                // still fully opaque and still has an edge on the last frame it exists.
                float intactField = 0.5 + 0.5 * grain;
                mask *= saturate(
                    (intactField - _Erode) / max(fwidth(intactField) * _EdgePixels, 1e-5)
                );
                clip(mask - 0.004);

                float height;
                float2 lumpSlope;
                Relief(offset, _LumpScale, _Seed, height, lumpSlope);

                // Only the lumps bend the surface. The wall's own tilt is left out of the normal on
                // purpose; it is applied below as a value, where it cannot become a terminator.
                float2 slope = lumpSlope * _LumpRelief;
                float invLen = rsqrt(dot(slope, slope) + 1.0);
                float3 normal = float3(-slope * invLen, invLen);

                // The blast is the lamp and it sits low in the crater. Wrapped over the whole
                // hemisphere, so each lump gets a bright face and a dim one with no edge between.
                float3 keyDir = float3(-facing * cos(_KeyElev), sin(_KeyElev));
                half lumpKey = pow(saturate(0.5 + 0.5 * dot(normal, keyDir)), _KeyGamma);

                // A wall, not a disc: it rises off the trailing edge and falls over the leading
                // one, so the mass has one lit half and one dark half at a glance. Spread across
                // the band's full width, which is what keeps that read off a single hard step.
                half form = 1.0 - smoothstep(_FormIn, max(_FormOut, _FormIn + 0.02), across);
                half key = _Key * form * lerp(1.0 - _LumpShare, 1.0 + _LumpShare * 0.6, lumpKey);
                half sky = saturate(dot(normal, float3(0.341, 0.521, 0.790)));

                half shade = saturate(_Ambient + key + _Sky * sky + _Micro * height);
                // Only what the blast lights takes the blast's colour; the rest keeps the dust's
                // own albedo, so the mass runs warm in shadow and cool where it is lit.
                half keyShare = saturate(key / max(shade, 1e-4) * _TintReach);
                half3 litColor = lerp(_DustLit.rgb, _DustKeyLit.rgb, keyShare);
                half3 color = lerp(_DustDark.rgb, litColor, shade);
                return half4(color, mask * _Opacity);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
