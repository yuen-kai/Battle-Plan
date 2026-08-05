// Battle Plan — hazard plate for ability wind-ups, drawn on a default Unity Quad laid flat.
//
// Two passes. The first multiplies the floor down into a saturated hazard tint, snapped to whole
// grid cells, so the board's own tile seams and contact shadows survive underneath and the player
// can count the squares that are about to hurt. Cells never fade in together: each one switches on
// when the countdown reaches it, ordered outward from the caster, so the telegraph is a literal
// count rather than an opacity ramp. The same multiply cuts the countdown ring and the per-cell
// chevrons one step deeper than the tint, which keeps them amber material instead of grey haze.
// The second pass inks the hard geometry on top: the staircase border, its dark keyline, the
// interior cell divisions, and the ring's hot core at the instant it closes.
//
// The danger area is expressed as a segment plus a half width, so a disc is the degenerate case of
// a lane. Drive every property per-instance from AbilityWindup.cs.
Shader "BattlePlan/WindupTelegraph"
{
    Properties
    {
        _TintMul ("Hazard Tint Multiplier", Vector) = (0.66, 0.26, 0.075, 0)
        _TintStrength ("Tint Strength", Range(0, 1)) = 1
        _BullseyeMul ("Bullseye Extra Multiply", Range(0.3, 1)) = 0.66
        _MarkMul ("Engraved Mark Multiplier", Vector) = (0.25, 0.108, 0.027, 0)
        _MarkOnTint ("Mark Strength Over Tint", Range(0, 1)) = 0.8
        _RingOnTint ("Ring Strength Over Tint", Range(0, 1)) = 0.72
        [HDR] _EdgeColor ("Border Colour", Color) = (1, 0.26, 0.0005, 1)
        [HDR] _RingColor ("Ring Core Colour", Color) = (2.6, 0.285, 0.004, 1)
        _KeyColor ("Keyline Colour", Color) = (0.0055, 0.0012, 0.0003, 1)
        _SeamColor ("Cell Seam Colour", Color) = (0.31, 0.041, 0.003, 1)
        _SeamAlpha ("Cell Seam Alpha", Range(0, 1)) = 0.5

        _QuadSize ("Quad World Size", Float) = 12
        _CellSize ("Cell Size", Float) = 2.7
        _CellOffset ("Cell Grid Offset", Vector) = (0, 0, 0, 0)
        _Segment ("Danger Segment (ax, az, bx, bz)", Vector) = (0, 0, 0, 0)
        _HalfWidth ("Danger Half Width", Float) = 4.3

        _BorderWidth ("Border Width", Float) = 0.15
        _KeyWidth ("Keyline Width", Float) = 0.06
        _SeamWidth ("Cell Seam Half Width", Float) = 0.03
        _BullseyeWidth ("Bullseye Border Width", Float) = 0.1

        _Caster ("Caster (x, z, aimX, aimZ)", Vector) = (0, -5.4, 1, 0)
        _OrderLateral ("Order Tie-break Axis", Vector) = (0, 1, 0, 0)
        _OrderBias ("Order Tie-break Weight", Float) = 0.13
        _RevealFront ("Reveal Front, order units", Float) = -100000
        _ArmFront ("Newest Armed Order", Float) = -100000
        _ArmFlash ("Arm Flash", Range(0, 1)) = 0

        _ChevronReach ("Chevron Apex Offset", Float) = 0.45
        _ChevronHalf ("Chevron Half Span", Float) = 0.9
        _ChevronWidth ("Chevron Stroke Half Width", Float) = 0.21

        _RingRadius ("Ring Radius", Float) = 0
        _RingThickness ("Ring Thickness", Float) = 0.11
        _RingAlpha ("Ring Alpha", Range(0, 1)) = 0
        _RingHot ("Ring Core Heat", Range(0, 1)) = 0
        _RingHotFrac ("Ring Core Fraction", Range(0, 1)) = 0.55
        _RingWobble ("Ring Wobble", Float) = 0.014
    }

    SubShader
    {
        Tags
        {
            // Ahead of the additive juice layers: this is the surface everything else lands on.
            "RenderType" = "Transparent"
            "Queue" = "Transparent-90"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _TintMul;
            float _TintStrength;
            float _BullseyeMul;
            float4 _MarkMul;
            float _MarkOnTint;
            float _RingOnTint;
            float4 _EdgeColor;
            float4 _RingColor;
            float4 _KeyColor;
            float4 _SeamColor;
            float _SeamAlpha;
            float _QuadSize;
            float _CellSize;
            float4 _CellOffset;
            float4 _Segment;
            float _HalfWidth;
            float _BorderWidth;
            float _KeyWidth;
            float _SeamWidth;
            float _BullseyeWidth;
            float4 _Caster;
            float4 _OrderLateral;
            float _OrderBias;
            float _RevealFront;
            float _ArmFront;
            float _ArmFlash;
            float _ChevronReach;
            float _ChevronHalf;
            float _ChevronWidth;
            float _RingRadius;
            float _RingThickness;
            float _RingAlpha;
            float _RingHot;
            float _RingHotFrac;
            float _RingWobble;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 board : TEXCOORD0;
        };

        Varyings vert(Attributes IN)
        {
            Varyings OUT;
            OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
            OUT.board = (IN.uv - 0.5) * _QuadSize;
            return OUT;
        }

        /// A perfect circle reads as a machined washer, so the hoop carries a couple of percent of
        /// low-frequency wander. It scales with the radius and vanishes as the ring closes.
        float RingRadiusAt(float2 p)
        {
            float a = atan2(p.y, p.x);
            float wander = sin(a * 3.0 + 0.7) * 0.62 + sin(a * 5.0 + 2.3) * 0.38;
            return _RingRadius * (1.0 + _RingWobble * wander);
        }

        /// Signed distance out from the ring band; negative on the band itself.
        float RingBand(float2 p, float widthScale)
        {
            return abs(length(p) - RingRadiusAt(p)) - _RingThickness * 0.5 * widthScale;
        }

        float SegmentDistance(float2 p)
        {
            float2 a = _Segment.xy;
            float2 ab = _Segment.zw - a;
            float along = saturate(dot(p - a, ab) / max(dot(ab, ab), 1e-5));
            return length(p - a - ab * along);
        }

        float2 CellCentre(float2 index)
        {
            return index * _CellSize + _CellOffset.xy;
        }

        /// Position of a cell in the countdown, running outward from the caster. The lateral term
        /// only breaks ties between cells the caster is equidistant from, so no two arm together.
        float CellOrder(float2 index)
        {
            float2 toCell = CellCentre(index) - _Caster.xy;
            return length(toCell) + _OrderBias * dot(toCell, _OrderLateral.xy);
        }

        bool CellLive(float2 index)
        {
            return SegmentDistance(CellCentre(index)) <= _HalfWidth
                && CellOrder(index) <= _RevealFront;
        }

        /// A chevron aimed away from the caster, so an armed cell says which way the danger is
        /// travelling as well as that it is armed.
        float ChevronMask(float2 p, float2 centre, float pixel)
        {
            float2 raw = centre - _Caster.xy;
            float span = length(raw);
            float2 dir = span > 1e-3 ? raw / max(span, 1e-3) : _Caster.zw;
            float2 q = p - centre;
            float along = dot(q, dir);
            float across = abs(dot(q, float2(-dir.y, dir.x)));

            // The stroke runs diagonally through this metric, so its true width is the band width
            // over root two and its antialiasing footprint is that much wider.
            float band = abs(along + across - _ChevronReach);
            float edge = saturate((_ChevronWidth - band) / (pixel * 1.4142) + 0.5);
            float arm = saturate((_ChevronHalf - across) / pixel + 0.5);
            return edge * arm;
        }

        struct Plate
        {
            float inside;
            float border;
            float key;
            float seam;
            float bullseye;
            float bullEdge;
            float chevron;
            float flash;
            float jitter;
        };

        Plate SamplePlate(float2 p, float pixel)
        {
            Plate o;
            o.inside = 0.0;
            o.border = 0.0;
            o.key = 0.0;
            o.seam = 0.0;
            o.bullseye = 0.0;
            o.bullEdge = 0.0;
            o.chevron = 0.0;
            o.flash = 0.0;
            o.jitter = 0.0;

            float2 rel = (p - _CellOffset.xy) / _CellSize;
            float2 index = floor(rel + 0.5);
            float2 f = rel - index;

            float toRight = (0.5 - f.x) * _CellSize;
            float toLeft = (f.x + 0.5) * _CellSize;
            float toUp = (0.5 - f.y) * _CellSize;
            float toDown = (f.y + 0.5) * _CellSize;

            bool liveRight = CellLive(index + float2(1, 0));
            bool liveLeft = CellLive(index + float2(-1, 0));
            bool liveUp = CellLive(index + float2(0, 1));
            bool liveDown = CellLive(index + float2(0, -1));

            // Distance to the nearest edge shared with a dead cell, and with a live one. The first
            // is where the silhouette and its border sit; the second is an interior cell division.
            float outerDist = 1e6;
            float innerDist = 1e6;
            outerDist = liveRight ? outerDist : min(outerDist, toRight);
            outerDist = liveLeft ? outerDist : min(outerDist, toLeft);
            outerDist = liveUp ? outerDist : min(outerDist, toUp);
            outerDist = liveDown ? outerDist : min(outerDist, toDown);
            innerDist = liveRight ? min(innerDist, toRight) : innerDist;
            innerDist = liveLeft ? min(innerDist, toLeft) : innerDist;
            innerDist = liveUp ? min(innerDist, toUp) : innerDist;
            innerDist = liveDown ? min(innerDist, toDown) : innerDist;

            if (CellLive(index))
            {
                o.inside = saturate(outerDist / pixel + 0.5);
                o.border = saturate((_BorderWidth - outerDist) / pixel + 0.5) * o.inside;
                o.seam = saturate((_SeamWidth - innerDist) / pixel + 0.5) * o.inside;
                o.seam = min(o.seam, 1.0 - o.border);
                o.chevron = ChevronMask(p, CellCentre(index), pixel) * o.inside;
                // Only the cell that armed most recently pings, and it is always the highest
                // ordered live cell, so one comparison isolates it.
                o.flash = CellOrder(index) > _ArmFront ? _ArmFlash : 0.0;
                o.jitter = frac(sin(dot(index, float2(37.13, 71.97))) * 4371.31);
                o.bullseye = (abs(index.x) < 0.5 && abs(index.y) < 0.5) ? 1.0 : 0.0;
                float nearest = min(min(toRight, toLeft), min(toUp, toDown));
                o.bullEdge =
                    saturate((_BullseyeWidth - nearest) / pixel + 0.5) * o.bullseye * o.inside;
            }
            else
            {
                o.key = saturate((_KeyWidth - innerDist) / pixel + 0.5);
            }

            return o;
        }
        ENDHLSL

        Pass
        {
            Name "HazardTint"
            Blend DstColor Zero
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragTint

            half4 fragTint(Varyings IN) : SV_Target
            {
                float pixel = max(max(fwidth(IN.board.x), fwidth(IN.board.y)), 1e-4);
                Plate plate = SamplePlate(IN.board, pixel);

                float depth = plate.inside * _TintStrength * (1.0 - 0.09 * plate.jitter);
                float3 tint = lerp(float3(1, 1, 1), _TintMul.rgb, saturate(depth));
                tint *= lerp(1.0, _BullseyeMul, plate.bullseye * depth);

                // Chevrons and the countdown are the same engraved ink. Over bare floor it cuts at
                // full strength; over the tint it is eased back so the mark keeps the plate's hue
                // instead of bottoming out into black. The chevrons hold that easing, but the
                // countdown walks off it as it closes: a hoop that fades into the plate it is
                // crossing has stopped counting at the exact moment it matters most.
                float ring = saturate(-RingBand(IN.board, 1.0) / pixel + 0.5)
                    * _RingAlpha * (1.0 - _RingHot);
                float chevron = saturate(plate.chevron * _TintStrength);
                float onTint = plate.inside * _TintStrength;
                tint *= lerp(
                    float3(1, 1, 1),
                    _MarkMul.rgb,
                    saturate(chevron * lerp(1.0, _MarkOnTint, onTint))
                );
                tint *= lerp(
                    float3(1, 1, 1),
                    _MarkMul.rgb,
                    saturate(ring * lerp(1.0, _RingOnTint, onTint))
                );
                return half4(tint, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "HazardInk"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragInk

            void Over(inout float4 dst, float3 c, float a)
            {
                a = saturate(a);
                dst.rgb = c * a + dst.rgb * (1.0 - a);
                dst.a = a + dst.a * (1.0 - a);
            }

            half4 fragInk(Varyings IN) : SV_Target
            {
                float2 p = IN.board;
                float pixel = max(max(fwidth(p.x), fwidth(p.y)), 1e-4);
                Plate plate = SamplePlate(p, pixel);

                float4 ink = float4(0, 0, 0, 0);
                Over(ink, _SeamColor.rgb, plate.seam * _SeamAlpha);
                Over(ink, _EdgeColor.rgb, plate.bullEdge);
                Over(ink, _EdgeColor.rgb, plate.border);
                Over(ink, _KeyColor.rgb, plate.key);
                // The cell that just armed pings once. Held well under the border's own value so
                // the plate still reads as one continuous mass rather than a hole punched in it.
                Over(ink, _EdgeColor.rgb, plate.flash * plate.inside * 0.45);

                // The countdown only turns into light at the very end, as the hoop runs out of
                // radius. Until then it is material, and it lives in the multiply pass.
                float hot = saturate(-RingBand(p, _RingHotFrac) / pixel + 0.5);
                Over(ink, _RingColor.rgb, hot * _RingHot * _RingAlpha);

                return half4(ink);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
