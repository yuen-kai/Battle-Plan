"""
Shared vocabulary for the hand-modelled units' skeletons.

Commander, PogoRider's rider and Sniper's "Person" are the same rig: ten bones in five
unparented two-bone chains -- two arms, two legs, and a neck carrying a head -- with no spine or
hips between them. Commander and the pogo rider are identical down to the third decimal; Sniper is
the same thing at 0.84 scale. So one set of motions drives all three, and Soldier gets the same
skeleton built onto it so it can join them.

Axes, measured off the models rather than assumed: the faces sit at -X and the figures are far
wider across Y than deep in X, so forward is -X, up is +Z, and the character's own right is +Y.
Limbs therefore swing about world Y and lean about world X.
"""

import math

import bpy
from mathutils import Quaternion, Vector

FORWARD = Vector((-1.0, 0.0, 0.0))
UP = Vector((0.0, 0.0, 1.0))
RIGHT = Vector((0.0, 1.0, 0.0))

#: Role -> bone name, for the ten-bone rig. The pairs are (upper, lower) down each chain.
TOY_RIG = {
    "arm.L": ("Bone", "Bone.001"),
    "arm.R": ("Bone.002", "Bone.003"),
    "leg.L": ("Bone.004", "Bone.005"),
    "leg.R": ("Bone.006", "Bone.007"),
    "head": ("Bone.008", "Bone.009"),
}


#: Ramrod's skeleton, which is a Rigify metarig rather than the hand-drawn ten-bone chain: a real
#: spine, named limbs, feet and toes. Only the joints the shared motions actually ask for are
#: mapped; the rest of its twenty-seven bones simply go along with their parents.
RIGIFY_RIG = {
    "arm.L": ("upper_arm.L", "forearm.L"),
    "arm.R": ("upper_arm.R", "forearm.R"),
    "leg.L": ("thigh.L", "shin.L"),
    "leg.R": ("thigh.R", "shin.R"),
    "head": ("spine.004", "spine.005"),
}

RIGS = {"toy": TOY_RIG, "rigify": RIGIFY_RIG}


def roles(rig):
    """Every role flattened to `role.part` -> bone name, e.g. `arm.L.upper`."""
    out = {}
    for role, (upper, lower) in rig.items():
        out[role + ".upper"] = upper
        out[role + ".lower"] = lower
    return out


def turn(pose_bone, axis, degrees):
    """
    A rotation of `degrees` about a *world* axis, expressed in the bone's own space.

    Pose rotations are relative to a bone's rest orientation, and these bones are rolled every
    which way -- arms hang down, the neck points up, and the chains were drawn by hand. Converting
    the axis into bone space means a motion can be written as "swing the thigh forward" without
    anyone having to know which way that bone happens to be rolled.
    """
    basis = pose_bone.bone.matrix_local.to_3x3()
    local = basis.inverted() @ Vector(axis)
    return Quaternion(local.normalized(), math.radians(degrees))


def clear(armature):
    """Drops any pose the import left behind, so a motion is written against rest."""
    for pb in armature.pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = Quaternion()
        pb.location = Vector()
        pb.scale = Vector((1.0, 1.0, 1.0))


def fcurves_of(action):
    """Blender 4.4+ moved f-curves behind action slots; this yields them whatever the layout."""
    for layer in action.layers:
        for strip in layer.strips:
            for slot in action.slots:
                bag = strip.channelbag(slot)
                if bag:
                    for fc in bag.fcurves:
                        yield fc


def armature_named(name):
    return next(o for o in bpy.data.objects if o.type == "ARMATURE" and o.name == name)
