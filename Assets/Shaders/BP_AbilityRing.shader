// Battle Plan — ability charge dial, cut into a unit's own base plate. Drawn on a default Unity
// Quad laid flat just above the plate; drive every property from AbilityStatusRing.cs.
//
// The dial is one slot per round of the ability's cooldown, and it is two things at once. Empty
// slots are *material*: the plate is darkened under them so an ability that is three rounds out
// still shows three sockets waiting to be filled. Charged slots are *light*, added on top.
// Additive alone cannot draw an absence — it can only ever brighten — so a purely additive dial
// reads as "nothing here" instead of "three to go", which is the whole message for every round but
// the last one.
//
// Both happen in one pass, because premultiplied blending is exactly this pair of operations:
// One OneMinusSrcAlpha resolves to light + plate * (1 - alpha), so alpha engraves the socket and
// rgb fills it. Two passes would be the obvious way to write it and would be wrong — URP draws one
// pass per shader tag, so a second untagged pass is silently dropped and only the engraving ever
// reaches the screen.
//
// When the final slot lands the gaps close, the ring becomes one unbroken hoop and a soft wash
// fills its interior. Available is a different shape, not merely a brighter one, so it survives
// being glanced at across a board with nine other units on it.
Shader "BattlePlan/AbilityRing"
{
    Properties
    {
        _QuadSize ("Quad World Size", Float) = 2
        _Radius ("Dial Radius", Float) = 0.8
        _Thickness ("Dial Thickness", Float) = 0.15
        _Slots ("Cooldown Slots", Float) = 3
        _Charge ("Charged Slots", Float) = 0
        _Gap ("Slot Gap", Float) = 0.085
        _SocketFloor ("Empty Socket Floor", Range(0, 1)) = 0.2
        [HDR] _ChargeColor ("Charged Colour", Color) = (1.93, 0.72, 0.02, 1)
        [HDR] _HeadColor ("Charging Head Colour", Color) = (3.4, 2, 0.7, 1)
        _Ready ("Ready", Range(0, 1)) = 0
        _Flash ("Charge Flash", Range(0, 1)) = 0
        _PulseSpeed ("Ready Pulse Speed", Float) = 0.75
        _PulseAmount ("Ready Pulse Amount", Range(0, 1)) = 0.28
        _CoreStrength ("Ready Core Strength", Range(0, 1)) = 0.13
        _Intensity ("Intensity", Range(0, 4)) = 1
    }

    SubShader
    {
        Tags
        {
            // After the wind-up plate a unit may be standing on and before the additive juice
            // layers: the dial is a readout, so neither the floor under it nor the effects over it
            // are allowed to paint it out.
            "RenderType" = "Transparent"
            "Queue" = "Transparent-80"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ChargeDial"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define BP_TAU 6.2831853

            CBUFFER_START(UnityPerMaterial)
                float _QuadSize;
                float _Radius;
                float _Thickness;
                float _Slots;
                float _Charge;
                float _Gap;
                float _SocketFloor;
                float4 _ChargeColor;
                float4 _HeadColor;
                float _Ready;
                float _Flash;
                float _PulseSpeed;
                float _PulseAmount;
                float _CoreStrength;
                float _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 plate : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.plate = (IN.uv - 0.5) * _QuadSize;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 p = IN.plate;
                float pixel = max(max(fwidth(p.x), fwidth(p.y)), 1e-4);
                float radius = length(p);

                float track = saturate((_Thickness * 0.5 - abs(radius - _Radius)) / pixel + 0.5);

                float slots = max(_Slots, 1.0);
                // One turn clockwise from the top of the board. Board-locked rather than
                // unit-locked, because a dial is counted, and a readout that spins with the unit it
                // belongs to cannot be counted at a glance.
                float turn = frac(atan2(p.x, p.y) / BP_TAU + 1.0);
                float scan = turn * slots;
                float index = floor(scan);
                float local = scan - index;

                // Slot geometry is measured in world arc length, so the gap between two slots is
                // the same hairline on the Sniper's four-slot dial as on the Commander's two-slot
                // one. The gaps close as the dial fills, leaving one unbroken hoop.
                float arc = BP_TAU * _Radius / slots;
                float toGap = (0.5 - abs(local - 0.5)) * arc;
                float halfGap = _Gap * 0.5 * (1.0 - _Ready);
                float slot = lerp(saturate((toGap - halfGap) / pixel + 0.5), 1.0, _Ready);

                float fill = saturate(_Charge - index);
                float charge = max(saturate((fill - local) * arc / pixel + 0.5), _Ready);

                // Only a slot caught mid-fill carries a head; a full or an empty one would light
                // its own boundary and read as a permanent hot spot.
                float filling = step(0.02, fill) * step(fill, 0.98);
                float toHead = abs(local - fill) * arc;
                float head = filling * exp(-toHead * toHead * 260.0);

                float inner = _Radius - _Thickness * 0.5;
                float core = 1.0 - smoothstep(inner * 0.3, inner, radius);

                // Slow enough to be breathing rather than blinking: the dial says the ability is
                // there to be spent, it is not asking to be spent this round.
                float pulse = 1.0 + _Ready * _PulseAmount * sin(_Time.y * _PulseSpeed * BP_TAU);
                float lit = track * slot;

                float3 light = _ChargeColor.rgb * lit * charge;
                light += _HeadColor.rgb * lit * head;
                light += _ChargeColor.rgb * core * _CoreStrength * _Ready;
                light *= _Intensity * pulse;
                // The moment the last slot lands, once, over the top of everything else.
                light += _HeadColor.rgb * _Flash * (lit + core * 0.35);

                // How much of the plate each slot takes out from under itself. An empty slot only
                // darkens, so it is the team plate in shadow rather than a third hue on top of it;
                // a charged one takes the plate away entirely. That last part is what keeps the
                // charge amber: added over a plate this saturated the ultramarine survives
                // underneath and the slot resolves to cream, which is the wrong colour on the blue
                // team and nearly invisible on the red one.
                float socket = lit * lerp(1.0 - _SocketFloor, 1.0, saturate(charge));
                return half4(light, saturate(socket));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
