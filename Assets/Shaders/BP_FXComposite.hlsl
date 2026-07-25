#ifndef BATTLEPLAN_FX_COMPOSITE_INCLUDED
#define BATTLEPLAN_FX_COMPOSITE_INCLUDED

// Battle Plan — shared compositing for every BattlePlan/* FX shader.
//
// WHY THIS EXISTS
// Additive blending only reads against a dark background. On a board whose floor sits near linear
// 0.37 an effect needs roughly +3.6 linear to reach near-white through the tonemapper, and at that
// energy the tonemapper has desaturated it to white anyway. Darkening the floor instead costs no
// energy, keeps its hue, and has more display headroom to work with. So every FX shader can now
// composite three ways and the choice is a material setting, not a rewrite.
//
// WHY A FLOAT AND NOT A KEYWORD
// _CompositeMode is a plain material uniform, so the branch below is scalar and uniform across the
// whole draw: no wave divergence, and — the point — no shader variants. A shader_feature here
// would double the variant count of every FX shader for no gain. Blend factors have to be material
// properties regardless, because Blend [_SrcBlend] [_DstBlend] is render state and cannot be
// driven from a MaterialPropertyBlock or an instancing buffer.
//
// PAIRING (the material must set both, they are not independently meaningful)
//   Additive  0  ->  Blend One One                     src = tint * weight
//   Alpha     1  ->  Blend SrcAlpha OneMinusSrcAlpha   src = tint, a = weight
//   Multiply  2  ->  Blend DstColor Zero               src = lerp(white, tint, weight)
//
// Keep these values in sync with FXPalette.Composite in C#.

#define BP_COMPOSITE_ADDITIVE 0.0
#define BP_COMPOSITE_ALPHA 1.0
#define BP_COMPOSITE_MULTIPLY 2.0

/// Resolves a tinted, masked sample into whatever the configured blend state expects.
/// `weight` is the full coverage term — mask x intensity x pulse x tint alpha — and is deliberately
/// NOT clamped before it reaches additive, because HDR headroom above 1.0 is what drives bloom.
half4 BP_Composite(half mode, half3 tint, half weight)
{
    half coverage = saturate(weight);

    if (mode >= BP_COMPOSITE_MULTIPLY)
    {
        // White where the mask is empty, so pixels the effect does not cover survive untouched.
        // A tint below 1 darkens; a tint above 1 brightens without the desaturation additive
        // suffers, which is the cheap way to get a hot spot on a light floor.
        return half4(lerp(half3(1.0, 1.0, 1.0), tint, coverage), 1.0);
    }

    if (mode >= BP_COMPOSITE_ALPHA)
    {
        // Straight (non-premultiplied) alpha: the blender applies coverage to the source.
        return half4(tint, coverage);
    }

    return half4(tint * weight, coverage);
}

#endif
