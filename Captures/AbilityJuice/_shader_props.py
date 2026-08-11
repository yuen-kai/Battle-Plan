"""Cross-check every shader property Aftermath.cs sets against the shader that owns it."""

import re
from pathlib import Path

ROOT = Path("/Users/cykai/Battle-Plan")
CS = ROOT / "Assets/Scripts/VFX/Aftermath.cs"
SHADERS = {
    "scorch": ROOT / "Assets/Shaders/BP_AftermathScorch.shader",
    "coals": ROOT / "Assets/Shaders/BP_AftermathCoals.shader",
    "smoke": ROOT / "Assets/Shaders/BP_AftermathSmoke.shader",
    "ember": ROOT / "Assets/Shaders/BP_AftermathEmber.shader",
}

src = CS.read_text()

# Shader.PropertyToID("_Foo") assigned to FooId
ids = dict(
    re.findall(r"static readonly int (\w+) = Shader\.PropertyToID\(\"(\w+)\"\);", src)
)

# Which material each setter block targets, by the variable used.
TARGET = {
    "markMaterial": "scorch",
    "coalMaterial": "coals",
}


def declared(path):
    text = path.read_text()
    block = text.split("Properties", 1)[1].split("SubShader", 1)[0]
    props = set(re.findall(r"^\s*(?:\[[^\]]*\]\s*)*(_\w+)\s*\(", block, re.M))
    cbuf = set(re.findall(r"^\s*(?:half|float|int)\d?4?\s+(_\w+);", text, re.M))
    return props, cbuf


ok = True
for name, path in SHADERS.items():
    props, cbuf = declared(path)
    missing_cbuf = props - cbuf
    extra_cbuf = cbuf - props
    if missing_cbuf or extra_cbuf:
        ok = False
        print(f"[{name}] property/CBUFFER mismatch: missing {missing_cbuf} extra {extra_cbuf}")
    else:
        print(f"[{name}] {len(props)} properties, CBUFFER consistent")

# Every setter call, attributed to the material variable on the same line.
print()
calls = re.findall(r"(\w+)\.Set(?:Float|Color)\((\w+),", src)
by_target = {}
for var, ident in calls:
    by_target.setdefault(var, set()).add(ids.get(ident, ident))

# `material` is the local in BuildPuffs and BuildSparks; split by source position.
puffs_start = src.index("void BuildPuffs")
sparks_start = src.index("void BuildSparks")
sparks_end = src.index("float SizeScale()")
generic = {"smoke": set(), "ember": set()}
for m in re.finditer(r"material\.Set(?:Float|Color)\((\w+),", src):
    prop = ids.get(m.group(1), m.group(1))
    if puffs_start < m.start() < sparks_start:
        generic["smoke"].add(prop)
    elif sparks_start < m.start() < sparks_end:
        generic["ember"].add(prop)

used = {k: set() for k in SHADERS}
for var, props in by_target.items():
    if var in TARGET:
        used[TARGET[var]] |= props
for k, v in generic.items():
    used[k] |= v
# runtime updates via puff.Material / spark.Material
for m in re.finditer(r"puff\.Material\.Set(?:Float|Color)\((\w+),", src):
    used["smoke"].add(ids.get(m.group(1), m.group(1)))
for m in re.finditer(r"spark\.Material\.Set(?:Float|Color)\((\w+),", src):
    used["ember"].add(ids.get(m.group(1), m.group(1)))

print("=== C# setters vs shader properties ===")
for name, path in SHADERS.items():
    props, _ = declared(path)
    unknown = used[name] - props
    unset = props - used[name]
    status = "OK " if not unknown else "BAD"
    if unknown:
        ok = False
    print(f"{status} [{name}] sets {len(used[name])}; unknown {sorted(unknown) or '-'}; "
          f"left at default {sorted(unset) or '-'}")

# Brace balance in each HLSL block.
print()
for name, path in SHADERS.items():
    text = path.read_text()
    if text.count("{") != text.count("}"):
        ok = False
        print(f"[{name}] BRACE MISMATCH {text.count('{')} open vs {text.count('}')} close")
    if text.count("HLSLPROGRAM") != text.count("ENDHLSL"):
        ok = False
        print(f"[{name}] HLSLPROGRAM/ENDHLSL mismatch")
print("braces balanced" if ok else "PROBLEMS FOUND")
