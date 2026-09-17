"""
Gives Soldier the skeleton the other hand-modelled units already have.

Soldier is the only one of the five with no rig at all: nine loose meshes, and one of them -- a
single three-thousand-vertex "Cube" -- is the entire body, both arms and both legs together. So it
cannot be animated by parenting parts to joints the way a kit-bashed model could. It needs actual
weights.

They are assigned rigidly, one bone per vertex at full weight, by asking which bone's segment each
vertex is nearest. That is the same binding the eight generated characters use and it suits the
same art: these are rounded solids pushed into one another, so a limb turning inside the mass it
overlaps has nothing to tear, where a blended weight would only pinch at the seams.

Run:  blender --background --python Tools/Anim/rig_soldier.py -- <in.fbx> <out.fbx>
"""

import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from toyrig import TOY_RIG  # noqa: E402


def box(meshes):
    lo = Vector((1e9,) * 3)
    hi = Vector((-1e9,) * 3)
    for mesh in meshes:
        for corner in mesh.bound_box:
            world = mesh.matrix_world @ Vector(corner)
            for i in range(3):
                lo[i] = min(lo[i], world[i])
                hi[i] = max(hi[i], world[i])
    return lo, hi


def build(body_lo, body_hi, head_centre):
    """
    Where each joint goes, read off the model's own proportions rather than typed in.

    The body mesh's box gives the height to divide up and the width to hang arms off; the head
    mesh gives the neck somewhere to point. Everything else is the same proportions the hand-drawn
    rigs already use — shoulders a fifth down from the top, hips just under halfway, knees two
    thirds — so Soldier ends up with a skeleton the shared motions fit.
    """
    top, bottom = body_hi.z, body_lo.z
    height = top - bottom
    half = (body_hi.y - body_lo.y) * 0.5
    mid = (body_hi.y + body_lo.y) * 0.5
    front = (body_hi.x + body_lo.x) * 0.5

    def at(down, across):
        return Vector((front, mid + across, top - height * down))

    return {
        # A torso down the centre line, which the hand-drawn rigs do not have and which is the
        # whole reason their arms could not move: with nothing in the middle to hold the chest, it
        # gets skinned to the upper-arm bones instead and a third of the body swings with an arm.
        "Torso": (at(0.52, 0.0), at(0.14, 0.0)),
        "Bone": (at(0.14, -half * 0.42), at(0.40, -half * 0.80)),
        "Bone.001": (at(0.40, -half * 0.80), at(0.60, -half * 0.94)),
        "Bone.002": (at(0.14, half * 0.42), at(0.40, half * 0.80)),
        "Bone.003": (at(0.40, half * 0.80), at(0.60, half * 0.94)),
        "Bone.004": (at(0.52, -half * 0.26), at(0.76, -half * 0.30)),
        "Bone.005": (at(0.76, -half * 0.30), at(1.00, -half * 0.32)),
        "Bone.006": (at(0.52, half * 0.26), at(0.76, half * 0.30)),
        "Bone.007": (at(0.76, half * 0.30), at(1.00, half * 0.32)),
        "Bone.008": (at(0.04, 0.0), Vector((head_centre.x, head_centre.y, top + (head_centre.z - top) * 0.45))),
        "Bone.009": (Vector((head_centre.x, head_centre.y, top + (head_centre.z - top) * 0.45)), head_centre),
    }


def nearest(point, layout):
    """The bone whose segment this point lies closest to."""
    best, gap = None, 1e18
    for name, (head, tail) in layout.items():
        span = tail - head
        length = span.length_squared
        along = 0.0 if length == 0 else max(0.0, min(1.0, (point - head).dot(span) / length))
        distance = (point - (head + span * along)).length_squared
        if distance < gap:
            best, gap = name, distance
    return best


def main():
    args = sys.argv[sys.argv.index("--") + 1 :]
    source, destination = args[0], args[1]

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=source)

    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    body = max(meshes, key=lambda m: len(m.data.vertices))
    head = max((m for m in meshes if m is not body), key=lambda m: len(m.data.vertices))

    body_lo, body_hi = box([body])
    head_lo, head_hi = box([head])
    layout = build(body_lo, body_hi, (head_lo + head_hi) * 0.5)

    armature = bpy.data.armatures.new("Armature")
    rig = bpy.data.objects.new("Armature", armature)
    bpy.context.scene.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")

    parents = {lower: upper for upper, lower in TOY_RIG.values()}
    parents.pop("Torso", None)
    made = {}
    for name, (start, end) in layout.items():
        bone = armature.edit_bones.new(name)
        bone.head, bone.tail = start, end
        made[name] = bone
    for lower, upper in parents.items():
        made[lower].parent = made[upper]
        made[lower].use_connect = True
    bpy.ops.object.mode_set(mode="OBJECT")

    for mesh in meshes:
        for name in layout:
            mesh.vertex_groups.new(name=name)

        if mesh is body:
            # The one mesh that spans real joints, so it is weighted per vertex.
            for vertex in mesh.data.vertices:
                world = mesh.matrix_world @ vertex.co
                mesh.vertex_groups[nearest(world, layout)].add([vertex.index], 1.0, "REPLACE")
        else:
            # Everything else is a whole part -- a head, an eye, a helmet -- so it rides one bone.
            lo, hi = box([mesh])
            bone = nearest((lo + hi) * 0.5, layout)
            mesh.vertex_groups[bone].add([v.index for v in mesh.data.vertices], 1.0, "REPLACE")

        mesh.parent = rig
        modifier = mesh.modifiers.new("Armature", "ARMATURE")
        modifier.object = rig

    for obj in bpy.data.objects:
        obj.select_set(True)

    bpy.ops.export_scene.fbx(
        filepath=destination,
        use_selection=True,
        add_leaf_bones=False,
        bake_anim=False,
        armature_nodetype="NULL",
    )
    print("RIGGED", os.path.basename(destination), len(layout), "bones,", len(body.data.vertices), "weighted vertices")


main()
