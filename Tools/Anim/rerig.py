"""
Gives the hand-drawn skeletons the one bone they were missing, and re-skins them onto it.

Commander, Sniper's "Person" and the pogo rider were all drawn with the same ten bones: two arms,
two legs, a neck and a head, in five chains with nothing joining them. There is no torso. So when
the models were skinned, the chest had to go somewhere, and it went to the upper-arm bones -- half
each. Swing an arm on that rig and you take a third of the body with it, which is why the first
pass at animating these units had to hold their arms still to look like anything at all.

The fix is one bone and a re-skin. A `Torso` is added down the centre line from the hips to the
shoulders, and every vertex of every mesh is re-assigned to whichever bone it actually lies
nearest. The chest lands on the torso, the arms keep only arms, and an arm can finally move on its
own.

Nothing that already exists is touched: no bone is renamed, moved, or re-parented, and the new one
hangs off the armature root like the other five chains do. That matters because Sniper and the pogo
rider carry hand-made clips that bind to these bones by path -- leave the paths alone and those
clips go on working, minus the chest-swinging they never meant to cause.

Run:  blender --background --python Tools/Anim/rerig.py -- <in.fbx> <out.fbx> <ArmatureName>
"""

import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from toyrig import TOY_RIG  # noqa: E402

TORSO = "Torso"


def nearest(point, segments):
    best, gap = None, 1e18
    for name, (head, tail) in segments.items():
        span = tail - head
        length = span.length_squared
        along = 0.0 if length == 0 else max(0.0, min(1.0, (point - head).dot(span) / length))
        distance = (point - (head + span * along)).length_squared
        if distance < gap:
            best, gap = name, distance
    return best


def main():
    args = sys.argv[sys.argv.index("--") + 1 :]
    source, destination, arm_name = args[0], args[1], args[2]

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=source)

    rig = next(o for o in bpy.data.objects if o.type == "ARMATURE" and o.name == arm_name)
    bones = rig.data.bones

    # Bone positions are armature-local and vertices are world; two of these three rigs sit at a
    # transform of their own, so comparing the two spaces directly puts the whole torso somewhere
    # off the model. Everything below is in world space.
    to_world = rig.matrix_world
    shoulders = [to_world @ bones[TOY_RIG[r][0]].head_local for r in ("arm.L", "arm.R") if TOY_RIG[r][0] in bones]
    hips = [to_world @ bones[TOY_RIG[r][0]].head_local for r in ("leg.L", "leg.R") if TOY_RIG[r][0] in bones]
    if not shoulders or not hips:
        print("SKIPPED", os.path.basename(source), "- not the ten-bone rig")
        return

    # Down the middle, from the line the legs hang off to the line the arms do. Taken from the rig
    # rather than from the mesh so the torso always meets the limbs it has to sit between.
    top = sum(shoulders, Vector()) / len(shoulders)
    bottom = sum(hips, Vector()) / len(hips)
    top.x = bottom.x = (top.x + bottom.x) * 0.5
    top.y = bottom.y = 0.0

    local = to_world.inverted()
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    if TORSO in rig.data.edit_bones:
        rig.data.edit_bones.remove(rig.data.edit_bones[TORSO])
    torso = rig.data.edit_bones.new(TORSO)
    torso.head, torso.tail = local @ bottom, local @ top
    bpy.ops.object.mode_set(mode="OBJECT")

    segments = {b.name: (to_world @ b.head_local, to_world @ b.tail_local) for b in rig.data.bones}

    moved = 0
    for mesh in [o for o in bpy.data.objects if o.type == "MESH"]:
        for name in segments:
            if name not in mesh.vertex_groups:
                mesh.vertex_groups.new(name=name)

        for vertex in mesh.data.vertices:
            world = mesh.matrix_world @ vertex.co
            winner = nearest(world, segments)
            for name in segments:
                if name == winner:
                    mesh.vertex_groups[name].add([vertex.index], 1.0, "REPLACE")
                    if name == TORSO:
                        moved += 1
                else:
                    mesh.vertex_groups[name].add([vertex.index], 0.0, "REPLACE")

        if not any(m.type == "ARMATURE" for m in mesh.modifiers):
            mesh.parent = rig
            mesh.modifiers.new("Armature", "ARMATURE").object = rig

    for obj in bpy.data.objects:
        obj.select_set(True)

    bpy.ops.export_scene.fbx(
        filepath=destination,
        use_selection=True,
        add_leaf_bones=False,
        bake_anim=False,
        armature_nodetype="NULL",
    )
    print("RERIGGED", os.path.basename(destination), len(segments), "bones,", moved, "vertices moved onto the torso")


main()
