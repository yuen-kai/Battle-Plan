// Battle Plan — the coals still alight in a scorch once the fire bed has burned out.
//
// The bed in BP_AftermathCoals is one field of fire and it dies as one, which leaves the last panel
// of a strip with nothing burning in it at all. What a fire actually leaves is a dozen separate
// lumps glowing in the char, and separate is the whole point: the same area spent as one mass reads
// as a small fire rather than as the remains of a large one.
//
// One coal per cell of a jittered lattice, the corner cells dropped so the scatter cannot land on
// clean floor, which fixes the count and stops the placement clumping. Every coal is cut with a
// near-step alpha and filled at coverage one with the same ember the bed burns at, so a coal's
// pixels land on the fire's own colour rather than blending toward the char under them: dimming a
// coal would walk it out of the luminance band it exists to occupy, so what shrinks is the coal and
// never its heat. Each runs its own flicker, creeps its own outline and goes out at its own moment.
//
// Textureless, driven from Aftermath.cs; _Age is the effect's own clock rather than the scene's.
Shader "BattlePlan/AftermathCoalScatter"
{
    Properties
    {
        [HDR] _EmberColor ("Ember Body", Color) = (1.55, 0.72, 0.145, 1)
        [HDR] _RimColor ("Cooling Rim", Color) = (0.29, 0.04, 0.011, 1)
        _Intensity ("Intensity", Range(0, 1)) = 1
        _Age ("Age (seconds)", Float) = 0
        _Cells ("Lattice Cells", Float) = 4
        _CoalSize ("Coal Diameter (lattice cells)", Range(0.02, 0.9)) = 0.365
        _SizeMin ("Smallest Coal", Range(0.2, 1)) = 0.78
        _SizeMax ("Largest Coal", Range(1, 3)) = 1.42
        _Jitter ("Placement Jitter", Range(0, 0.49)) = 0.36
        _Wobble ("Outline Wobble", Range(0, 0.8)) = 0.34
        _RimSpan ("Cooling Rim Span", Range(0.02, 0.6)) = 0.18
        _EdgePixels ("Edge Width (pixels)", Range(0.5, 6)) = 1
        _Flicker ("Flicker", Range(0, 0.4)) = 0.14
        _DeathStart ("First Coal Out", Float) = 1.3
        _DeathEnd ("Last Coal Out", Float) = 1.52
        _DeathSpan ("Wink Out Time", Range(0.02, 0.5)) = 0.14
        _Seed ("Seed", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+16"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
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
                half4 _EmberColor;
                half4 _RimColor;
                half _Intensity;
                float _Age;
                float _Cells;
                half _CoalSize;
                half _SizeMin;
                half _SizeMax;
                half _Jitter;
                half _Wobble;
                half _RimSpan;
                half _EdgePixels;
                half _Flicker;
                float _DeathStart;
                float _DeathEnd;
                half _DeathSpan;
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

            float ScatterHash1(float n)
            {
                return frac(sin(n * 12.9898) * 43758.5453123);
            }

            float4 ScatterHash4(float2 cell)
            {
                float n = dot(cell, float2(127.1, 311.7)) + _Seed * 13.13;
                float4 s = float4(n, n * 1.37 + 4.7, n * 2.11 + 9.1, n * 3.19 + 15.3);
                return frac(sin(s) * float4(43758.5453, 22578.1459, 19642.3491, 31741.7723));
            }

            float ScatterValue(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = ScatterHash1(cell.x + cell.y * 57.0);
                float b = ScatterHash1(cell.x + 1.0 + cell.y * 57.0);
                float c = ScatterHash1(cell.x + (cell.y + 1.0) * 57.0);
                float d = ScatterHash1(cell.x + 1.0 + (cell.y + 1.0) * 57.0);
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float ScatterFbm(float2 p)
            {
                return ScatterValue(p) * 0.667 + ScatterValue(p * 2.11) * 0.333;
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
                float2 grid = IN.uv * _Cells;
                float2 origin = floor(grid);

                float shape = -1.0;

                [unroll]
                for (int j = -1; j <= 1; j++)
                {
                    [unroll]
                    for (int i = -1; i <= 1; i++)
                    {
                        float2 cell = origin + float2(i, j);

                        // The lattice is square and a burn is not, so every cell whose centre falls
                        // outside the inscribed disc is dropped. That is what fixes the count.
                        float2 fromMiddle = (cell + 0.5) / _Cells - 0.5;
                        if (dot(fromMiddle, fromMiddle) > 0.25)
                            continue;

                        float4 h = ScatterHash4(cell);

                        float breathe =
                            1.0 + _Flicker * sin(_Age * lerp(4.5, 11.5, h.x) + h.y * 6.2831853);
                        float death = lerp(_DeathStart, _DeathEnd, h.w);
                        float alive = 1.0 - saturate((_Age - death) / max(_DeathSpan, 1e-3));
                        float radius =
                            0.5 * _CoalSize * lerp(_SizeMin, _SizeMax, h.z)
                            * _Intensity * alive * breathe;
                        if (radius < 1e-4)
                            continue;

                        float2 offset = grid - (cell + 0.5 + (h.xy - 0.5) * (2.0 * _Jitter));
                        float dist = length(offset);
                        // Well clear of the widest the wobble can push this coal's edge, so no
                        // fragment near a visible boundary is ever decided by this test.
                        if (dist > radius * (1.6 + _Wobble))
                            continue;

                        // A coal is a lump, never a disc, and its outline creeps as it burns down.
                        // Taken on the coal's own direction so no point on the edge loses its step.
                        float2 dir = dist > 1e-5 ? offset / dist : float2(1.0, 0.0);
                        float wobble =
                            (ScatterFbm(dir * 1.3 + h.xy * 31.0 + _Age * 0.5) - 0.5)
                            * _Wobble
                            * saturate(dist / max(radius, 1e-5) * 3.0);
                        shape = max(shape, 1.0 - dist / max(radius * (1.0 + wobble), 1e-5));
                    }
                }

                float coverage = saturate(shape / max(fwidth(shape) * _EdgePixels, 1e-5));
                clip(coverage - 0.02);

                half3 color = lerp(_RimColor.rgb, _EmberColor.rgb, saturate(shape / max(_RimSpan, 1e-3)));
                return half4(color * coverage, coverage);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
