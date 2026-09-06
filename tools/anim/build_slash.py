"""Authors combat clips the free KayKit pack does not ship, and renders them.

    blender --background --python tools/anim/build_slash.py -- <out.glb> [--render <dir>]

The free KayKit Adventurers pack has no melee swing: its libraries cover
movement, hits TAKEN, death and item use. So enemies attacked with `Throw` -- a
throwing motion played for a sword stroke -- and the player's swing was posed
bone by bone in C# every frame.

These are keyframed by number rather than by eye, which is exactly why the
script renders them: the loop is author, render, look, adjust. A pose written
without looking at it is a guess with a decimal point on it.

Everything an animator would want to change lives in POSES below. Angles are
degrees, in each bone's local space, as (x, y, z) Euler.
"""
import bpy
import math
import os
import sys

argv = sys.argv[sys.argv.index('--') + 1:]
OUT = argv[0]
RENDER_DIR = argv[argv.index('--render') + 1] if '--render' in argv else None

SOURCE = os.path.join(os.path.dirname(__file__), '..', '..',
                      'game', 'assets', 'kaykit', 'characters', 'Knight.glb')

# One entry per keyframe: (frame, {bone: (rx, ry, rz) in degrees}).
#
# A slash, right-handed, read from the side. The chest carries it: an arm that
# swings while the body stays still reads as a puppet, and it is the first
# thing that looks wrong at any speed.
# The axes are not guessed. Probe renders got the sweep plane wrong twice, so
# the pack's own Throw clip was dumped frame by frame and read instead -- the
# conventions the artist used are written down in the file:
#
#   upperarm.r  Y  = the sweep. Throw runs -55 (wound back) to +65 (released).
#               X  = a secondary lift, -37 to +23.
#   lowerarm.r  Z  = the elbow. 56 at rest, 31 when it extends.
#   chest       Y  = the twist, and it is what carries a Throw: 0 to -34.
#               X  = the lean, small: -10 to +9.
#
# Rest is Throw's own frame 0, so this clip starts and ends where the idle and
# the walk already are and blends into them without a pop.
#
# The lean is worth more here than the twist, and the twist is worse than
# neutral: KayKit's clips are authored for a free camera, but this game is a
# strict side-on orthographic view. At Throw's own -34 of chest Y the character
# turns partly to face the lens mid-swing -- visible in the render, and it
# would be visible in the game. So the twist is cut to about half and the lean
# doubled.
REST = {'upperarm.r': (-37, -29, -11), 'lowerarm.r': (0, 0, 56),
        'upperarm.l': (-37, 29, 11), 'chest': (0, 0, 0)}

SLASH = [
    (0,  REST),
    # Wind-up: the arm goes back and up, the chest opens away from the target.
    (5,  {'upperarm.r': (-8, -86, -18), 'lowerarm.r': (0, 0, 82),
          'upperarm.l': (-30, 40, 14), 'chest': (-11, -14, 0)}),
    # Contact. The shortest interval in the clip lands here, which is what
    # makes a swing hit rather than glide.
    (9,  {'upperarm.r': (14, 84, 12), 'lowerarm.r': (0, 0, 8),
          'upperarm.l': (-40, 8, 6), 'chest': (-20, 10, 0)}),
    # Follow-through, carried across.
    (13, {'upperarm.r': (6, 100, 20), 'lowerarm.r': (0, 0, 30),
          'upperarm.l': (-42, 2, 4), 'chest': (-24, 13, 0)}),
    (20, REST),
]

# A parry, and it is a different SHAPE of motion from the swing, which is the
# whole point: a swing travels across the body, a parry stops in front of it.
#
# Read from the side, the sword has to end up VERTICAL and between the camera
# and the chest, because that is the silhouette a player recognises as "guard"
# in one frame. Three things do that on this rig:
#
#   upperarm.r  X  lifts the whole arm. Throw uses -37..+23; a guard goes past
#                  the top of that range, because the elbow is coming up, not
#                  the hand going forward.
#   lowerarm.r  Z  closes the elbow HARD -- 56 at rest, 31 extended in a throw,
#                  and about 105 here. A parry is a folded arm; an extended one
#                  reads as a reach.
#   upperarm.l  braces across, so the body is not a single arm doing everything
#                  while the rest of it stands around.
#
# The chest leans BACK, opposite to the slash. The slash leans into the blow it
# is delivering; this one is receiving.
#
# The timing is front-loaded on purpose. The guard is up by frame 3 -- 0.12s --
# and then HOLDS, because the parry window is 380ms and a pose that is still
# arriving when the window closes never gets seen. The recovery is the slow part.
PARRY = [
    (0,  REST),
    # Snap to guard. The shortest interval in the clip, and it is at the front.
    (3,  {'upperarm.r': (48, -18, -5), 'lowerarm.r': (0, 0, 108),
          'upperarm.l': (-20, 20, 22), 'chest': (-8, 6, 0)}),
    (6,  {'upperarm.r': (66, -10, 4), 'lowerarm.r': (0, 0, 120),
          'upperarm.l': (-12, 14, 28), 'chest': (-12, 8, 0)}),
    # Hold. Identical to frame 6, so the guard sits still instead of drifting
    # through the window the player is being asked to aim at.
    (14, {'upperarm.r': (66, -10, 4), 'lowerarm.r': (0, 0, 120),
          'upperarm.l': (-12, 14, 28), 'chest': (-12, 8, 0)}),
    (22, REST),
]

CLIPS = {'Slash_A': SLASH, 'Parry_A': PARRY}


def load_rig():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.abspath(SOURCE))
    return next(o for o in bpy.data.objects if o.type == 'ARMATURE')


def build(arm, name, keys):
    action = bpy.data.actions.new(name)
    if arm.animation_data is None:
        arm.animation_data_create()
    arm.animation_data.action = action

    # Every bone this clip touches has to be keyed on EVERY keyframe, not only
    # where it moves. A bone keyed on frames 0 and 9 but not 5 interpolates
    # straight through the wind-up, which silently removes the pose that makes
    # the swing readable.
    touched = sorted({b for _, pose in keys for b in pose})

    for bone in touched:
        pb = arm.pose.bones[bone]
        pb.rotation_mode = 'XYZ'

    for frame, pose in keys:
        for bone in touched:
            pb = arm.pose.bones[bone]
            rx, ry, rz = pose.get(bone, (0, 0, 0))
            pb.rotation_euler = (math.radians(rx), math.radians(ry), math.radians(rz))
            pb.keyframe_insert('rotation_euler', frame=frame)

    action.use_fake_user = True
    return action


def setup_render(arm):
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.render.resolution_x = 400
    scene.render.resolution_y = 500
    scene.render.film_transparent = False

    cam_data = bpy.data.cameras.new('Cam')
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = 3.4
    cam = bpy.data.objects.new('Cam', cam_data)
    scene.collection.objects.link(cam)
    # From -X, at chest height, and both halves of that were paid for:
    #
    # A camera on -Y looks the character in the face, so a forward swing
    # happens straight down the lens -- the first render showed arms that
    # barely moved, and the arms were fine.
    #
    # A camera on +X is the wrong side. upperarm.r sits at x=-0.21, so the
    # swinging arm is behind the torso from there and the second render showed
    # a swing playing out through the character's chest.
    cam.location = (-4.0, 0.0, 1.0)
    cam.rotation_euler = (math.radians(90), 0, math.radians(-90))
    scene.camera = cam


def render(name, keys, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    last = keys[-1][0]
    for frame in range(0, last + 1, 2):
        bpy.context.scene.frame_set(frame)
        bpy.context.scene.render.filepath = os.path.join(out_dir, f'{name}_{frame:03d}.png')
        bpy.ops.render.render(write_still=True)


def main():
    arm = load_rig()
    actions = [build(arm, name, keys) for name, keys in CLIPS.items()]

    if RENDER_DIR:
        setup_render(arm)
        for name, keys in CLIPS.items():
            arm.animation_data.action = bpy.data.actions[name]
            render(name, keys, RENDER_DIR)

    # The ARMATURE only. Selecting its mesh children too dragged the Knight's
    # body and its 1MB texture into what is meant to be an animation library --
    # KayKit's own two libraries carry no mesh at all, which is why they import
    # without a texture beside them.
    bpy.ops.object.select_all(action='DESELECT')
    for child in list(arm.children):
        bpy.data.objects.remove(child, do_unlink=True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm

    bpy.ops.export_scene.gltf(
        filepath=os.path.abspath(OUT),
        export_format='GLB',
        use_selection=True,
        export_animations=True,
        export_animation_mode='ACTIONS',
        export_apply=False,
    )
    print(f'WROTE {OUT} with {len(actions)} action(s): {", ".join(a.name for a in actions)}')


main()
