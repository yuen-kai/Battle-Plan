// Battle Plan — one dark, occluding clod of settled dust, on a default Unity Quad laid flat.
// What a blast leaves on the deck once the front has gone: a lumpy opaque mass darker than the
// floor, with a hard alpha step for an edge and a real surface inside it, so it reads as a mound
// rather than a decal. Textureless, so a burst can stamp a dozen of them without a new .asset.
//   The relief is the same five-octave field BP_ShockwaveDust shades its wall with, carrying an
//   exact gradient, so a clod is lit by a grazing lamp back at the impact point and every lump on
//   it has a face toward the blast and a face away from it. The palette is wide enough for that
//   split to be worth seventy levels; a narrow one turns the same lighting into a flat fill.
//   As on the dust, the mound's own curvature is applied as a value rather than through the normal,
//   and the lamp is wrapped: curvature steep enough to turn the surface away at the rim otherwise
//   drives a hard ring of terminator round every clod, and a clod of dust has no terminator.
//   _Seed decorrelates outline, lumps and holes between clods in the same burst; _BlastDir and
//   _ErodeDir are fed pre-rotated per clod so every mass in the burst is lit from the same
//   apparent direction and gives way from the same upwind flank, which walks a settling mass off
//   its own centroid instead of letting it thin in place.
//   Colours arrive as LINEAR rgb through SetVector so the composited value in frame is predictable.
Shader "BattlePlan/ShockwavePuff"
{
    Properties
    {
        _DustDark ("Dust Shadow (linear rgb)", Vector) = (0.0139, 0.0111, 0.0088, 1)
        _DustLit ("Dust Sky-Lit (linear rgb)", Vector) = (0.417, 0.379, 0.325, 1)
        _DustKeyLit ("Dust Blast-Lit (linear rgb)", Vector) = (0.228, 0.430, 0.530, 1)
        _Opacity ("Opacity", Range(0, 1)) = 1
        _Radius ("Radius", Range(0, 1)) = 0.75
        _Rag ("Raggedness", Range(0, 0.5)) = 0.26
        _Erode ("Erode", Range(-0.8, 1.2)) = -0.8
        _ErodeBias ("Erode Asymmetry", Range(0, 0.6)) = 0.3
        _ErodeDir ("Surviving Side", Vector) = (1, 0, 0, 0)
        _BlastDir ("Toward The Blast", Vector) = (1, 0, 0, 0)
        _RagCount ("Rag Count", Float) = 4
        _ErodeScale ("Erode Field Scale", Float) = 16
        _LumpScale ("Lump Scale (cycles per quad)", Float) = 26
        _LumpRelief ("Lump Slope", Range(0, 2)) = 0.55
        _LumpShare ("Lump Share Of Key", Range(0, 1)) = 0.4
        _DomeRelief ("Dome Shading", Range(0, 1)) = 0.7
        _FormIn ("Lit Flank (toward blast)", Range(0, 1)) = 0
        _FormOut ("Shadow Flank (toward blast)", Range(0, 1)) = 0.92
        _Ambient ("Ambient", Range(0, 0.4)) = 0.012
        _Key ("Blast Key", Range(0, 1.4)) = 0.95
        _KeyElev ("Blast Elevation (radians)", Range(0.05, 1.4)) = 0.34
        _KeyGamma ("Lump Contrast", Range(0.3, 2.5)) = 1
        _Sky ("Sky Fill", Range(0, 0.6)) = 0.02
        _Micro ("Micro Value Break", Range(0, 0.3)) = 0.06
        _TintReach ("Key Tint Reach", Range(0, 2)) = 1.05
        _Seed ("Seed", Float) = 0
        _EdgePixels ("Edge Pixels", Range(0.5, 4)) = 1.2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+20"
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
                half _Rag;
                half _Erode;
                half _ErodeBias;
                float4 _ErodeDir;
                float4 _BlastDir;
                float _RagCount;
                float _ErodeScale;
                float _LumpScale;
                half _LumpRelief;
                half _LumpShare;
                half _DomeRelief;
                half _FormIn;
                half _FormOut;
                half _Ambient;
                half _Key;
                half _KeyElev;
                half _KeyGamma;
                half _Sky;
                half _Micro;
                half _TintReach;
                float _Seed;
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

            static const float2 kReliefDir[5] =
            {
                float2(0.9848, 0.1736), float2(0.2079, -0.9781), float2(-0.6428, 0.7660),
                float2(0.7660, 0.6428), float2(-0.3420, -0.9397)
            };
            static const float kReliefFreq[5] = { 1.00, 1.71, 2.63, 4.11, 6.37 };
            static const float kReliefAmp[5] = { 0.50, 0.31, 0.13, 0.05, 0.02 };

            // Slope weights of their own: amplitude times frequency is near-constant across this
            // spectrum, so left alone the finest octave swings the shading as hard as the coarsest
            // over two or three pixels. Rolling it off keeps the mottling and loses the steepness.
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
                float aa = max(fwidth(radius), 1e-6) * _EdgePixels;

                float rag =
                    sin(angle * _RagCount + _Seed) * 0.50 +
                    sin(angle * (_RagCount * 1.7 + 1.0) - _Seed * 1.4) * 0.32 +
                    sin(angle * (_RagCount * 2.7 + 2.0) + _Seed * 0.8) * 0.18;

                float edge = _Radius * (1.0 + _Rag * rag);
                half mask = saturate((edge - radius) / aa);

                float height;
                float2 lumpSlope;
                Relief(offset, _LumpScale, _Seed, height, lumpSlope);

                float2 facing = offset / max(length(offset), 1e-5);
                float2 survives = normalize(_ErodeDir.xy + float2(1e-5, 1e-5));

                // Clods thin out by losing pieces, so the survivors keep a hard edge to the end
                // instead of turning into a stain on an opacity ramp — and they give way from one
                // flank first, so what is left of a mass is somewhere else from where it started.
                // Deliberately a different field from the relief: how a clod breaks up is settled
                // and must not move when how it is lit changes.
                float crumble =
                    sin(offset.x * _ErodeScale + _Seed * 3.1) *
                    sin(offset.y * _ErodeScale * 1.3 - _Seed * 2.2) +
                    0.6 * sin((offset.x + offset.y) * _ErodeScale * 2.1 + _Seed);
                float intactField = 0.5 + 0.35 * crumble + _ErodeBias * dot(facing, survives);
                mask *= saturate(
                    (intactField - _Erode) / max(fwidth(intactField) * _EdgePixels, 1e-5)
                );
                clip(mask - 0.004);

                // Only the lumps bend the surface. The mound's own curvature is applied below as a
                // value, where it shades the mass without cutting a hard ring round its rim.
                float dome = saturate(1.0 - radius / max(edge, 1e-4));
                float2 slope = lumpSlope * _LumpRelief;
                float invLen = rsqrt(dot(slope, slope) + 1.0);
                float3 normal = float3(-slope * invLen, invLen);

                float2 toBlast = normalize(_BlastDir.xy + float2(1e-5, 1e-5));
                float3 keyDir = float3(toBlast * cos(_KeyElev), sin(_KeyElev));
                half lumpKey = pow(saturate(0.5 + 0.5 * dot(normal, keyDir)), _KeyGamma);

                // A mound: the flank turned toward the blast is lit, the far flank is not, and the
                // whole thing falls off toward its own rim where the surface curves away.
                half away = 1.0 - saturate(0.5 + 0.5 * dot(facing, toBlast));
                half form = (1.0 - smoothstep(_FormIn, max(_FormOut, _FormIn + 0.02), away))
                    * lerp(1.0, sqrt(dome), _DomeRelief);
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
