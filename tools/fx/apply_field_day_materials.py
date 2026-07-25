#!/usr/bin/env python3
"""Apply the FIELD DAY palette and composite modes to the FX materials.

Run from the repository root:  python3 Tools/fx/apply_field_day_materials.py

Materials are YAML, so they are edited here rather than through the editor: this keeps the whole
palette swap reviewable as one diff and re-runnable after a tuning pass, and it does not need the
Unity lease. Idempotent — running it twice produces the same file.

COMPOSITE MODES come from ArtDirection 9.2.1, which resolves them off the Y-order contract rather
than per effect: an element on the ground plane may Multiply because it can only ever composite
against deck, paint and fog wash, and an element above the plane takes Alpha because at a 73 degree
camera it will overlap cover and units in screen space whatever its world position. The two-frame
HDR flashes stay Additive per palette section 6, which names them explicitly.

COLOURS are linear, because a Color property on a shader is uploaded unconverted; see palette
section 0. The values are the linear column of the palette tables, not the sRGB one.
"""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
FX = ROOT / "Assets/Materials/FX"

ADDITIVE = (0, 1, 1)
ALPHA = (1, 5, 10)
MULTIPLY = (2, 2, 0)

# Linear values, straight from ArtBible-Palette sections 1.3 and 6.
INK = (0.0075, 0.0103, 0.0144)
PAPER = (0.9734, 0.9473, 0.8963)
BLUE = (0.0048, 0.1070, 0.4793)
BLUE_HI = (0.0033, 0.0685, 0.2918)
RED = (0.7605, 0.0437, 0.0595)
RED_HI = (0.3916, 0.0070, 0.0232)
AMBER_DEEP = (0.1119, 0.0307, 0.0009)
COVER_PLATE = (0.0999, 0.1221, 0.1441)
DECK_3 = (0.6445, 0.5841, 0.4678)
DECK_4 = (0.4678, 0.4072, 0.2918)
SHADOW = (0.0482, 0.0723, 0.0908)

MUZZLE_CORE = (4.000, 3.485, 2.578)
IMPACT_FLASH = (3.031, 3.031, 3.031)
OBJECTIVE_CONTESTED = (2.300, 0.671, 0.018)

# name -> (composite, {float: value}, {colour: (r, g, b, a)})
PLAN = {
    # --- Ground plane: Multiply. The tint arrives per instance from a property block, so the
    #     material holds white and only owns the blend state.
    "FX_GroundGlow": (MULTIPLY, {"_Intensity": 1}, {"_GlowColor": (1, 1, 1, 1)}),
    "FX_Scorch": (MULTIPLY, {"_Intensity": 1, "_RimMode": 1}, {"_TintColor": INK + (0.35,)}),
    "FX_VisionCone": (MULTIPLY, {}, {"_ConeColor": SHADOW + (0.22,)}),
    # --- Above the plane: Alpha with a contour.
    #     FX_BeamBlue/Red are the projectile TRACER materials -- the only consumers are the four
    #     prefabs under Assets/Prefabs/Projectiles. The EnergyBeam effects that palette section 6
    #     licenses a hot core for (Area Lock, sniper target lock) build their materials at runtime
    #     in BeamVFX from FXPalette.TeamBeamCore and never load these. A tracer is not one of the
    #     seven emitters, so it carries no HDR at all: flat team -hi, which is what section 8.1
    #     asks for and the only way the streak lands darker than the deck it crosses.
    "FX_BeamRed": (ALPHA, {"_Intensity": 1}, {"_GlowColor": RED_HI + (0.85,), "_CoreColor": RED_HI + (1,)}),
    "FX_BeamBlue": (ALPHA, {"_Intensity": 1}, {"_GlowColor": BLUE_HI + (0.85,), "_CoreColor": BLUE_HI + (1,)}),
    "FX_BeamAmber": (ALPHA, {"_Intensity": 1}, {"_GlowColor": AMBER_DEEP + (0.85,), "_CoreColor": OBJECTIVE_CONTESTED + (1,)}),
    "FX_Slash": (ALPHA, {"_Intensity": 1, "_RimMode": 1}, {"_TintColor": INK + (0.85,)}),
    "FX_SmokePuff": (ALPHA, {"_Intensity": 1}, {"_TintColor": DECK_4 + (0.78,)}),
    "FX_Dust": (ALPHA, {"_Intensity": 1}, {"_TintColor": DECK_3 + (0.45,)}),
    "FX_Debris": (ALPHA, {"_Intensity": 1}, {"_TintColor": COVER_PLATE + (1,)}),
    # Sparks and embers carry their colour in the particle gradient, so the material stays neutral.
    "FX_Spark": (ALPHA, {"_Intensity": 1}, {"_TintColor": (1, 1, 1, 1)}),
    "FX_Ember": (ALPHA, {"_Intensity": 1}, {"_TintColor": (1, 1, 1, 1)}),
    # --- The two-frame HDR flashes. Additive, and the only things here allowed to bloom.
    "FX_MuzzleFlash": (ADDITIVE, {"_Intensity": 2.2}, {"_GlowColor": MUZZLE_CORE + (1,)}),
    "FX_CoreWhite": (ADDITIVE, {"_Intensity": 1}, {"_TintColor": IMPACT_FLASH + (1,)}),
    # --- New: the ink outline shell on every projectile (section 8.1). Front-culled inverted hull.
    "FX_Contour": (ALPHA, {"_Intensity": 1, "_RimMode": 1, "_Cull": 1}, {"_TintColor": INK + (1,)}),
}

FXUNLIT_GUID = "5d66b0de715840a5a1821856e8ffaf49"

CONTOUR_TEMPLATE = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!21 &2100000
Material:
  serializedVersion: 8
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: FX_Contour
  m_Shader: {{fileID: 4800000, guid: {guid}, type: 3}}
  m_Parent: {{fileID: 0}}
  m_ModifiedSerializedProperties: 0
  m_ValidKeywords: []
  m_InvalidKeywords: []
  m_LightmapFlags: 4
  m_EnableInstancingVariants: 1
  m_DoubleSidedGI: 0
  m_CustomRenderQueue: -1
  stringTagMap: {{}}
  disabledShaderPasses: []
  m_LockedProperties: 
  m_SavedProperties:
    serializedVersion: 3
    m_TexEnvs: []
    m_Ints: []
    m_Floats: []
    m_Colors: []
  m_BuildTextureStacks: []
  m_AllowLocking: 1
"""


def fmt(value):
    text = f"{value:.6f}".rstrip("0").rstrip(".")
    return text if text else "0"


def upsert_block(lines, header, entries, render):
    """Rewrites one m_Floats / m_Colors block, replacing known keys and appending new ones."""
    try:
        start = next(i for i, line in enumerate(lines) if line.strip().startswith(header))
    except StopIteration:
        raise SystemExit(f"missing {header} block")

    if lines[start].strip() == f"{header} []":
        lines[start] = lines[start].replace(" []", "")
        end = start + 1
    else:
        end = start + 1
        while end < len(lines) and lines[end].lstrip().startswith("- "):
            end += 1

    body = lines[start + 1 : end]
    for key, value in entries.items():
        rendered = f"    - {key}: {render(value)}"
        for i, line in enumerate(body):
            if line.strip().startswith(f"- {key}:"):
                body[i] = rendered
                break
        else:
            body.append(rendered)

    lines[start + 1 : end] = sorted(body, key=lambda line: line.split(":")[0])
    return lines


def main():
    if not FX.is_dir():
        raise SystemExit(f"not found: {FX}")

    contour = FX / "FX_Contour.mat"
    if not contour.exists():
        contour.write_text(CONTOUR_TEMPLATE.format(guid=FXUNLIT_GUID))
        print("created FX_Contour.mat")

    for name, (composite, floats, colors) in sorted(PLAN.items()):
        path = FX / f"{name}.mat"
        if not path.exists():
            print(f"SKIP  {name}: no such material", file=sys.stderr)
            continue

        mode, src, dst = composite
        all_floats = dict(floats)
        all_floats.update({"_CompositeMode": mode, "_SrcBlend": src, "_DstBlend": dst})

        lines = path.read_text().split("\n")
        lines = upsert_block(lines, "m_Floats:", all_floats, fmt)
        lines = upsert_block(
            lines,
            "m_Colors:",
            colors,
            lambda v: "{r: %s, g: %s, b: %s, a: %s}" % tuple(fmt(c) for c in v),
        )
        path.write_text("\n".join(lines))
        print(f"ok    {name}  mode={mode}")


if __name__ == "__main__":
    main()
