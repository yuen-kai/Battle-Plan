"""
The motions the hand-modelled units share, written against the ten-bone rig in `toyrig`.

These are deliberately the same six states the generated eight already answer to -- Idle, Moving,
Aiming, Shoot, Dodge, DiveRecovery -- because `AnimationHandler` asks every unit for a state by the
same logical name. A roster where half the units understand "Dodge" and half do not is worse than
one where none of them do.

What is different here is what there is to move. The generated rig has hips and a chest; this one
has five limb chains and nothing joining them, so everything the spine would carry -- the bob of a
stride, the lean into a run, the duck of a weave -- is carried by the armature object instead. That
is the node the whole figure hangs off, and in Unity it sits under the Animator as a plain
transform, so a clip can drive it like any other.
"""

import math

from mathutils import Vector

from toyrig import FORWARD, RIGHT, UP, turn

#: Frames per second the actions are authored and exported at.
FPS = 30


def _sine(frame_count, cycles, phase):
    """A full sine over the take, sampled per frame, for looping motion."""
    for f in range(frame_count + 1):
        at = f / frame_count
        yield f, math.sin((at * cycles + phase) * math.tau)


class Take:
    """
    One action under construction. Keys are written straight onto pose bones and the armature
    object, frame by frame, because these motions are cheap to evaluate and sampling every frame
    sidesteps every question about tangents, Euler order and quaternion winding at once.
    """

    def __init__(self, name, seconds, loop=False):
        self.name = name
        self.frames = max(2, round(seconds * FPS))
        self.loop = loop
        self.bone = {}
        self.body = {}

    def swing(self, role, degrees_at):
        """Rotates a bone about the axis a limb swings on -- world Y, the left-right axis."""
        self.rotate(role, RIGHT, degrees_at)

    def rotate(self, role, axis, degrees_at):
        self.bone.setdefault(role, []).append((Vector(axis), degrees_at))

    def lean(self, degrees_at):
        """Pitches the whole figure forward or back, which is what it has instead of a spine."""
        self.body.setdefault("lean", []).append(degrees_at)

    def twist(self, degrees_at):
        self.body.setdefault("twist", []).append(degrees_at)

    def rise(self, units_at):
        self.body.setdefault("rise", []).append(units_at)

    def shift(self, units_at):
        """Sideways travel, for a weave."""
        self.body.setdefault("shift", []).append(units_at)


#: Kept as a switch, but no unit sets it any more.
#:
#: The arms were held still in the first pass for a real reason: the hand-drawn rigs have no torso
#: bone, so their chests were skinned to the upper-arm bones and swinging an arm took a third of the
#: body with it. That is fixed at the rig now rather than worked around here -- Commander and
#: Soldier were given a `Torso` bone and re-skinned onto it, and Sniper, the pogo rider and Ramrod
#: turned out never to have had the problem: their arm bones only ever held arms. So the arms swing.
ARMS_LOCKED = False


def _arms(default):
    """How far the arms may travel on this unit."""
    return 0.0 if ARMS_LOCKED else default


def idle(scale):
    """One slow breath, with nothing in the body arriving anywhere at the same time."""
    t = Take("Idle", 3.2, loop=True)
    n = t.frames
    t.rise([(f, 0.055 * scale * (0.5 + 0.5 * s)) for f, s in _sine(n, 1, 0.0)])
    t.lean([(f, -1.2 * s) for f, s in _sine(n, 1, 0.18)])
    t.swing("arm.L.upper", [(f, _arms(-2.2) * s) for f, s in _sine(n, 1, 0.26)])
    t.swing("arm.R.upper", [(f, _arms(-2.2) * s) for f, s in _sine(n, 1, 0.34)])
    t.rotate("head.lower", UP, [(f, 3.0 * s) for f, s in _sine(n, 1, 0.42)])
    t.swing("head.lower", [(f, -1.1 * s) for f, s in _sine(n, 1, 0.10)])
    return t


def moving(scale):
    """
    Two strides, so the clip loops on the foot it started on. The legs swing from the hip and the
    shins break under them, which this rig can do and the generated one cannot -- these have a
    knee.
    """
    t = Take("Moving", 0.92, loop=True)
    n = t.frames
    for side, phase in (("L", 0.0), ("R", 0.5)):
        t.swing(f"leg.{side}.upper", [(f, -26.0 * s) for f, s in _sine(n, 1, phase)])
        t.swing(f"leg.{side}.lower", [(f, max(0.0, 22.0 * s)) for f, s in _sine(n, 1, phase + 0.25)])
        t.swing(f"arm.{side}.upper", [(f, _arms(24.0) * s) for f, s in _sine(n, 1, phase + 0.5)])
        t.swing(f"arm.{side}.lower", [(f, _arms(-9.0) - _arms(7.0) * s) for f, s in _sine(n, 1, phase + 0.62)])

    t.rise([(f, 0.11 * scale * (0.5 + 0.5 * s)) for f, s in _sine(n, 2, 0.25)])
    t.lean([(f, -7.0) for f in range(n + 1)])
    t.twist([(f, 4.0 * s) for f, s in _sine(n, 1, 0.5)])
    t.rotate("head.lower", UP, [(f, -2.6 * s) for f, s in _sine(n, 1, 0.5)])
    return t


def aiming(scale):
    """Settled on a target: almost nothing moves, which is the whole difference from Idle."""
    t = Take("Aiming", 2.6, loop=True)
    n = t.frames
    t.lean([(f, -3.0) for f in range(n + 1)])
    t.swing("head.lower", [(f, -4.0) for f in range(n + 1)])
    t.swing("arm.R.upper", [(f, _arms(-9.0)) for f in range(n + 1)])
    t.swing("arm.L.upper", [(f, _arms(-7.0)) for f in range(n + 1)])
    t.rise([(f, 0.022 * scale * (0.5 + 0.5 * s)) for f, s in _sine(n, 1, 0.0)])
    t.rotate("head.lower", UP, [(f, 1.2 * s) for f, s in _sine(n, 1, 0.55)])
    return t


def _beat(frames, points):
    """Keys given as (fraction of the take, value), resolved to frames."""
    return [(round(at * frames), value) for at, value in points]


def shoot(scale):
    """Out on the second frame, back over the rest of it. A shot is sold by the asymmetry."""
    t = Take("Shoot", 0.46)
    n = t.frames
    # The recoil stays on the arms even when they are otherwise locked: a kick is short enough
    # that a weapon on its own armature reads as shaken rather than as dropped.
    t.swing("arm.R.upper", _beat(n, [(0, 0), (0.12, 16), (0.7, -3), (1, 0)]))
    t.swing("arm.R.lower", _beat(n, [(0, 0), (0.12, 11), (0.7, -2), (1, 0)]))
    t.swing("arm.L.upper", _beat(n, [(0, 0), (0.12, 12), (0.7, -2), (1, 0)]))
    t.lean(_beat(n, [(0, 0), (0.14, 6), (0.75, -2), (1, 0)]))
    t.swing("head.lower", _beat(n, [(0, 0), (0.16, 4), (1, 0)]))
    return t


def dodge(scale):
    """
    Low and never still. There is no spine to hunch, so the duck is the whole figure dropping and
    weaving while the arms come in, which from the board camera reads as evasion either way.
    """
    t = Take("Dodge", 1.15, loop=True)
    n = t.frames
    t.shift([(f, 0.20 * scale * s) for f, s in _sine(n, 1, 0.0)])
    t.rise([(f, -0.16 * scale + 0.04 * scale * s) for f, s in _sine(n, 2, 0.25)])
    t.lean([(f, -12.0) for f in range(n + 1)])
    t.twist([(f, -13.0 * s) for f, s in _sine(n, 1, 0.25)])
    t.swing("arm.L.upper", [(f, _arms(-20.0) - _arms(7.0) * s) for f, s in _sine(n, 1, 0.5)])
    t.swing("arm.R.upper", [(f, _arms(-20.0) - _arms(7.0) * s) for f, s in _sine(n, 1, 0.0)])
    t.swing("arm.L.lower", [(f, _arms(-26.0)) for f in range(n + 1)])
    t.swing("arm.R.lower", [(f, _arms(-26.0)) for f in range(n + 1)])
    t.swing("leg.L.lower", [(f, 16.0) for f in range(n + 1)])
    t.swing("leg.R.lower", [(f, 16.0) for f in range(n + 1)])
    t.rotate("head.lower", UP, [(f, 9.0 * s) for f, s in _sine(n, 1, 0.35)])
    return t


def dive_recovery(scale):
    """Getting up off the board: pitched flat with the arms out, resolving to standing."""
    t = Take("DiveRecovery", 0.78)
    n = t.frames
    t.lean(_beat(n, [(0, -52), (0.34, -15), (0.74, 7), (1, 0)]))
    t.rise(_beat(n, [(0, -0.42 * scale), (0.34, -0.12 * scale), (0.74, 0.03 * scale), (1, 0)]))
    t.swing("head.lower", _beat(n, [(0, 24), (0.34, 7), (0.74, -5), (1, 0)]))
    for side in ("L", "R"):
        t.swing(f"arm.{side}.upper", _beat(n, [(0, -44), (0.34, -13), (0.74, 6), (1, 0)]))
        t.swing(f"arm.{side}.lower", _beat(n, [(0, -20), (0.34, -6), (1, 0)]))
        t.swing(f"leg.{side}.upper", _beat(n, [(0, 16), (0.34, 5), (0.74, -4), (1, 0)]))
        t.swing(f"leg.{side}.lower", _beat(n, [(0, 30), (0.34, 9), (1, 0)]))
    return t


def rest(scale):
    """
    The rig doing nothing, exported alongside the real motions purely to be measured.

    Unity's FBX importer does not bring an armature object in the way Blender wrote it: it folds
    its own axis conversion and scale factor into that node, so the model's Armature arrives at
    270 degrees and a scale of a hundred where Blender had it square at one. Bones are unaffected —
    they come through the same conversion in both files — but the armature object is where all the
    body motion on this rig lives, so a clip written against Blender's version of that node would
    lay the figure on its back at a hundredth of its size.

    Rather than hard-code the conversion, the bake ships one take of the rig at rest. Whatever that
    clip says the neutral is, is the neutral, and every other clip is rebased onto the prefab's own
    rest pose through it.
    """
    return Take("Rest", 2.0 / FPS)


#: Every shared state, in the order a controller lists them. Idle first: it is the default.
ALL = (rest, idle, moving, aiming, shoot, dodge, dive_recovery)
