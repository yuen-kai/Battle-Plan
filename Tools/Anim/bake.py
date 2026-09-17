"""
Bakes the shared motions onto one unit's rig and writes an animation-only FBX beside the model.

Animation-only, and a separate file, for one reason: the hand-modelled units already carry clips
that work -- Sniper's seven, the pogo rider's five -- and the brief is to add to them rather than
replace them. Nothing here reopens or rewrites a source model, so those clips cannot be lost by
this running. Unity imports the result as a second source of clips against the same skeleton.

Run:  blender --background --python Tools/Anim/bake.py -- <model.fbx> <out.fbx> <ArmatureName> [more names...]
"""

import json
import os
import sys

import bpy
from mathutils import Quaternion, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import motions  # noqa: E402
from toyrig import RIGHT, RIGS, TOY_RIG, UP, armature_named, clear, roles, turn  # noqa: E402


def scale_of(armature):
    """
    The rig's size against Commander's, so a motion written once travels the same distance on
    every unit. Sniper is the same skeleton at 0.84, and a bob measured in Blender units would
    otherwise be a twitch on one unit and a hop on another.
    """
    reference = 1.320
    upper = armature.data.bones.get(TOY_RIG["arm.L"][0]) or armature.data.bones.get("upper_arm.L")
    return (upper.length / reference) if upper else 1.0


def apply(armature, take, rig):
    named = roles(rig)
    clear(armature)

    action = bpy.data.actions.new(take.name)
    armature.animation_data_create()
    armature.animation_data.action = action
    if hasattr(action, "slots") and not armature.animation_data.action_slot:
        armature.animation_data.action_slot = action.slots.new(id_type="OBJECT", name=take.name)

    rest_z = armature.location.z
    rest_y = armature.location.y

    # Sampled every frame rather than keyed at extremes. These curves are cheap, and a baked
    # sample has no opinion about tangents, Euler order or which way round a quaternion went.
    for frame in range(take.frames + 1):
        for role, tracks in take.bone.items():
            bone_name = named.get(role)
            if bone_name is None:
                continue
            pb = armature.pose.bones[bone_name]
            spin = Quaternion()
            for axis, degrees_at in tracks:
                value = _at(degrees_at, frame)
                if value is not None:
                    spin = spin @ turn(pb, axis, value)
            pb.rotation_quaternion = spin
            pb.keyframe_insert("rotation_quaternion", frame=frame + 1)

        lean = _sum(take.body.get("lean"), frame)
        twist = _sum(take.body.get("twist"), frame)
        armature.rotation_mode = "QUATERNION"
        armature.rotation_quaternion = Quaternion(RIGHT, _rad(lean)) @ Quaternion(UP, _rad(twist))
        armature.keyframe_insert("rotation_quaternion", frame=frame + 1)

        armature.location.z = rest_z + _sum(take.body.get("rise"), frame)
        armature.location.y = rest_y + _sum(take.body.get("shift"), frame)
        armature.keyframe_insert("location", frame=frame + 1)

    action.use_fake_user = True
    action.use_frame_range = True
    action.frame_start = 1
    action.frame_end = take.frames + 1
    return action


def _at(points, frame):
    for f, value in points:
        if f == frame:
            return value
    return None


def _sum(tracks, frame):
    if not tracks:
        return 0.0
    total = 0.0
    for points in tracks:
        value = _at(points, frame)
        if value is not None:
            total += value
    return total


def _rad(degrees):
    import math

    return math.radians(degrees)


def main():
    args = sys.argv[sys.argv.index("--") + 1 :]
    if args[0] == "--arms-locked":
        motions.ARMS_LOCKED = True
        args = args[1:]
    rig = TOY_RIG
    if args[0] == "--rig":
        rig = RIGS[args[1]]
        args = args[2:]
    source, destination, names = args[0], args[1], args[2:]

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=source)

    # Meshes go: an animation-only file carries the skeleton and the curves and nothing else, so
    # Unity has no second copy of the model to import and no chance of shipping one by accident.
    for obj in [o for o in bpy.data.objects if o.type != "ARMATURE"]:
        bpy.data.objects.remove(obj, do_unlink=True)

    # And so does every skeleton but the one being animated. Sniper and the pogo rider each carry
    # two -- a person and the thing it is holding -- and exporting the idle one alongside writes a
    # clip full of constant curves for bones that share their names with the ones that matter.
    # Unity then cannot tell which "Bone.002" a clip means, and the arms sit still.
    for obj in [o for o in bpy.data.objects if o.type == "ARMATURE" and o.name not in names]:
        bpy.data.objects.remove(obj, do_unlink=True)
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)

    # An empty above the skeleton, purely so Unity has somewhere to root the clips.
    #
    # Exported without it, the armature itself becomes the clip root and every curve on it binds to
    # the empty path -- which Unity reads as root motion and discards when the Animator does not
    # ask for any. That is where all the body motion lives on this rig: it has no spine or hips, so
    # the bob of a stride and the lean into a run are carried by the armature object. One node above
    # it and those curves bind as an ordinary transform under the name the prefab already uses.
    stage = bpy.data.objects.new("Model", None)
    bpy.context.scene.collection.objects.link(stage)
    for armature in [o for o in bpy.data.objects if o.type == "ARMATURE"]:
        armature.parent = stage

    written = []
    for name in names:
        armature = armature_named(name)
        armature.animation_data_clear()
        scale = scale_of(armature)
        for build in motions.ALL:
            take = build(scale)
            strip = bpy.data.actions.new(f"{name}|{take.name}")
            bpy.data.actions.remove(strip)
            action = apply(armature, take, rig)
            action.name = take.name if len(names) == 1 else f"{name} {take.name}"
            written.append(
                {
                    "name": action.name,
                    "state": take.name,
                    "rig": name,
                    "first": 0,
                    "last": take.frames,
                    "loop": take.loop,
                }
            )

    for obj in bpy.data.objects:
        obj.select_set(True)

    bpy.ops.export_scene.fbx(
        filepath=destination,
        use_selection=True,
        add_leaf_bones=False,
        bake_anim=True,
        bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False,
        bake_anim_simplify_factor=0.0,
        armature_nodetype="NULL",
    )
    # A manifest beside the file, because Unity will not take the names as given: it renames
    # whichever take it imports first after the file itself, so one clip always arrives called
    # "Commander@Anim" and the roster loses whichever motion that was. The importer is configured
    # from this instead of from guesswork about which take is which.
    manifest = destination.rsplit(".", 1)[0] + ".clips.json"
    with open(manifest, "w") as handle:
        json.dump(written, handle, indent=2)

    print("BAKED", os.path.basename(destination), len(written), "clips:", ", ".join(c["name"] for c in written))


main()
