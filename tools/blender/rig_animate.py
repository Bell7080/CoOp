# Headless Blender: import a Tripo GLB, normalise it (front -Y, feet on the floor), build a simple armature,
# skin it with smooth nearest-segment weights, key looping actions and export FBX for Unity.
# usage: blender --background --python rig_animate.py -- <config.py> <in.glb> <out.fbx>
import bpy, sys, math, os, runpy
from mathutils import Vector, Quaternion, Matrix

args = sys.argv[sys.argv.index("--") + 1:]
cfg = runpy.run_path(args[0])
src, dst = args[1], args[2]
H = cfg["HEIGHT"]
FPS = 30

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = FPS
bpy.ops.import_scene.gltf(filepath=src)

meshes = [o for o in scene.objects if o.type == "MESH"]
for o in scene.objects:
    o.select_set(o in meshes)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
mesh = bpy.context.view_layer.objects.active
mesh.name = cfg["NAME"]
for o in list(scene.objects):
    if o is not mesh and o.type != "MESH":
        o.select_set(False)
bpy.ops.object.select_all(action="DESELECT")
mesh.select_set(True)
bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in list(scene.objects):
    if o is not mesh:
        bpy.data.objects.remove(o, do_unlink=True)

# Tripo's unit box has the front at +X, up +Z, feet at z=-0.5. -> front -Y, feet at z=0, height H.
S = H
def tf(p):
    x, y, z = p
    return Vector((y * S, -x * S, (z + 0.5) * S))

me = mesh.data
orig = [(v.co.x, v.co.y, v.co.z) for v in me.vertices]  # Tripo unit-box coordinates, for HARD weights
for v in me.vertices:
    v.co = tf((v.co.x, v.co.y, v.co.z))
me.update()

# ---------- armature ----------
arm_data = bpy.data.armatures.new(cfg["NAME"] + "_Rig")
arm = bpy.data.objects.new(cfg["NAME"] + "_Rig", arm_data)
scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
for name, head, tail, parent, deform in cfg["BONES"]:
    b = arm_data.edit_bones.new(name)
    b.head, b.tail = tf(head), tf(tail)
    b.roll = 0.0
    b.use_deform = deform
    if parent:
        b.parent = arm_data.edit_bones[parent]
        b.use_connect = False
bpy.ops.object.mode_set(mode="OBJECT")

# ---------- weights: smooth inverse-distance to bone segments ----------
deform = [b for b in arm_data.bones if b.use_deform]
segs = [(b.name, Vector(b.head_local), Vector(b.tail_local)) for b in deform]
bias = cfg.get("WEIGHT_BIAS", {})
for name, _, _ in segs:
    mesh.vertex_groups.new(name=name)
def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
    return (a + ab * t - p).length
power = cfg.get("WEIGHT_POWER", 6.0)
for v in me.vertices:
    p = v.co
    ds = []
    for name, a, b in segs:
        d = seg_dist(p, a, b) * bias.get(name, 1.0)
        ds.append((max(d, 1e-4), name))
    ds.sort()
    best = ds[:3]
    ws = [(1.0 / d ** power, n) for d, n in best]
    tot = sum(w for w, _ in ws)
    for w, n in ws:
        if w / tot > 0.02:
            mesh.vertex_groups[n].add([v.index], w / tot, "REPLACE")

# Hard weights: rigid parts (a TV cabinet) or small regions (a jaw) that must follow one bone only.
for bone, test in cfg.get("HARD", []):
    for v in me.vertices:
        if not test(*orig[v.index]): continue
        for g in list(v.groups):
            mesh.vertex_groups[g.group].remove([v.index])
        mesh.vertex_groups[bone].add([v.index], 1.0, "REPLACE")

mesh.parent = arm
mod = mesh.modifiers.new("Armature", "ARMATURE")
mod.object = arm

# ---------- actions ----------
# world axes after normalising: side X, forward -Y, up Z.
AX = {"pitch": Vector((1, 0, 0)), "roll": Vector((0, 1, 0)), "yaw": Vector((0, 0, 1))}
for pb in arm.pose.bones:
    pb.rotation_mode = "QUATERNION"

def pose(bone, rots, loc=(0, 0, 0)):
    pb = arm.pose.bones[bone]
    rest = pb.bone.matrix_local.to_3x3()
    inv = rest.inverted()
    q = Quaternion()
    for axis, deg in rots:
        la = (inv @ AX[axis]).normalized()
        q = Quaternion(la, math.radians(deg)) @ q
    pb.rotation_quaternion = q
    pb.location = inv @ Vector(loc)

arm.animation_data_create()
for anim_name, (seconds, fn) in cfg["ANIMS"].items():
    act = bpy.data.actions.new(anim_name)
    act.use_fake_user = True
    arm.animation_data.action = act
    frames = max(2, int(round(seconds * FPS)))
    step = 2
    for f in list(range(0, frames, step)) + [frames]:
        t = (f % frames) / frames
        for pb in arm.pose.bones:
            pb.rotation_quaternion = Quaternion()
            pb.location = Vector()
        for bone, rots, loc in fn(t, math):
            pose(bone, rots, loc)
        for pb in arm.pose.bones:
            pb.keyframe_insert("rotation_quaternion", frame=f)
            pb.keyframe_insert("location", frame=f)
    act.frame_range = (0, frames)
    # push down to an NLA strip so every action exports as its own take
    track = arm.animation_data.nla_tracks.new()
    track.name = anim_name
    strip = track.strips.new(anim_name, 0, act)
    strip.name = anim_name
    arm.animation_data.action = None
for pb in arm.pose.bones:
    pb.rotation_quaternion = Quaternion()
    pb.location = Vector()

# ---------- texture next to the fbx ----------
os.makedirs(os.path.dirname(dst), exist_ok=True)
for img in bpy.data.images:
    if img.size[0] > 0:
        img.filepath_raw = os.path.join(os.path.dirname(dst), cfg["NAME"] + "_Albedo.png")
        img.file_format = "PNG"
        img.save()
        break

bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True)
mesh.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(
    filepath=dst, use_selection=True, object_types={"ARMATURE", "MESH"},
    apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
    add_leaf_bones=True, bake_anim=True, bake_anim_use_nla_strips=True, bake_anim_use_all_actions=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
    path_mode="COPY", embed_textures=False, mesh_smooth_type="FACE")
bpy.ops.wm.save_as_mainfile(filepath=dst.replace(".fbx", ".blend"))
print("RIGGED", cfg["NAME"], len(me.vertices), "verts", len(arm_data.bones), "bones", list(cfg["ANIMS"].keys()))
