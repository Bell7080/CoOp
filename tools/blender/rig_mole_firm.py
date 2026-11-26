# Firm body + one segmented nose for the painted mole (run after paint_mole.py).
# usage: blender --background MoleAlien_painted.blend --python rig_mole_firm.py -- <out.fbx> [segments=16]
#
# 1. One nose: paint_mole.py split the nose in two - the snout (where it leaves the face) stayed in the body
#    mesh, the trunk below became MoleNose - so in game it looked like a nose on the head with a second nose
#    hanging off it. Both parts are now skinned to the same chain of segment bones laid along the real nose
#    centre line (face -> snout tip -> trunk tip), so the whole nose stretches as one (Unity tints both alike).
# 2. Firm jelly: the body core, eyes and blush ride on Spine alone (no soft blending over Hips/Spine/Head that
#    bent the blob like water jelly); only the limbs and the nose bend. The clips are calmed down too: the
#    nose bones are no longer keyed (the nose follows the head; Unity adds a slow, damped lag), and the hip /
#    spine / head motion is scaled down.
# 3. Segmented nose: the trunk mesh is subdivided once and, with the snout, skinned to a dense chain of
#    NoseSeg01..NN bones (like a centipede's segments) children of Head; in Unity MoleRig lays them along a
#    curve to stretch the real nose. The tip rides rigidly on the last segment.
import bpy, sys
from mathutils import Vector, Quaternion

args = sys.argv[sys.argv.index("--") + 1:]
out_fbx = args[0]
N = int(args[1]) if len(args) > 1 else 16
CALM = 0.4          # hip / spine / head motion kept from the clips

scene = bpy.context.scene
arm = next(o for o in scene.objects if o.type == "ARMATURE")
body = bpy.data.objects["MoleAlien"]
nose = bpy.data.objects["MoleNose"]
# ---------- 0. calmer clips ----------
def curve_sets(act):
    """F-curve collections of an action (legacy actions, or Blender 4.4+ layered actions)."""
    if hasattr(act, "fcurves"):
        yield act.fcurves
        return
    for layer in act.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                yield bag.fcurves


for act in bpy.data.actions:
  for curves in list(curve_sets(act)):
    for fc in list(curves):
        path = fc.data_path
        if not path.startswith('pose.bones["'):
            continue
        bone = path.split('"')[1]
        if bone.startswith("Nose"):
            curves.remove(fc)
            continue
        if bone in ("Hips", "Spine", "Head"):
            for k in fc.keyframe_points:
                if path.endswith("rotation_quaternion"):
                    rest = 1.0 if fc.array_index == 0 else 0.0
                else:
                    rest = 0.0
                k.co.y = rest + (k.co.y - rest) * CALM
                k.handle_left.y = rest + (k.handle_left.y - rest) * CALM
                k.handle_right.y = rest + (k.handle_right.y - rest) * CALM
print("CALM clips", [a.name for a in bpy.data.actions])

# ---------- 1. the nose centre line: face -> snout tip -> down the trunk ----------
LINE = [Vector((0.0, -0.33, 0.79)), Vector((0.0, -0.52, 0.775)), Vector((0.0, -0.508, 0.40))]
SEGS = [(LINE[i], LINE[i + 1]) for i in range(len(LINE) - 1)]
LINE_LEN = sum((b - a).length for a, b in SEGS)


def line_param(p):
    best, s_best, start = None, 0.0, 0.0
    for a, b in SEGS:
        ab = b - a
        t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
        d = (a + ab * t - p).length
        if best is None or d < best:
            best, s_best = d, start + ab.length * t
        start += ab.length
    return s_best


def line_point(a_len):
    start = 0.0
    for a, b in SEGS:
        L = (b - a).length
        if a_len <= start + L or (a, b) == SEGS[-1]:
            d = (b - a).normalized()
            return a + d * (a_len - start), d
        start += L


def is_snout(p):
    return p.y < -0.33 and p.z > 0.6 and abs(p.x) < 0.16


# denser trunk mesh (linear subdivision keeps the shape and UVs)
bpy.ops.object.select_all(action="DESELECT")
nose.select_set(True)
bpy.context.view_layer.objects.active = nose
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.mesh.subdivide(number_cuts=1)
bpy.ops.object.mode_set(mode="OBJECT")

# ---------- 2. firm body ----------
CORE = ("Hips", "Spine", "Head", "Nose1", "Nose2", "Nose3")


def firm(obj):
    groups = {g.index: g.name for g in obj.vertex_groups}
    spine = obj.vertex_groups.get("Spine") or obj.vertex_groups.new(name="Spine")
    for v in obj.data.vertices:
        core = sum(g.weight for g in v.groups if groups[g.group] in CORE)
        if core <= 0.0:
            continue
        for g in list(v.groups):
            if groups[g.group] in CORE and groups[g.group] != "Spine":
                obj.vertex_groups[groups[g.group]].remove([v.index])
        spine.add([v.index], core, "REPLACE")


for name in ("MoleAlien", "MoleEyes", "MoleBlush"):
    o = bpy.data.objects.get(name)
    if o is not None:
        firm(o)

# ---------- 3. segment bones (children of Head) and weights for the snout and the trunk ----------
reach = LINE_LEN * 0.92   # the last bit (tip) stays rigid on the last segment
bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
eb = arm.data.edit_bones
for old in [b for b in eb if b.name.startswith("NoseSeg")]:
    eb.remove(old)
spacing = reach / (N - 1)
seg_names = []
for k in range(N):
    head, d = line_point(spacing * k)
    name = "NoseSeg%02d" % (k + 1)
    b = eb.new(name)
    b.head = head
    b.tail = head + d * max(spacing, 0.02)
    b.roll = 0.0
    b.parent = eb["Head"]
    b.use_connect = False
    b.use_deform = True
    seg_names.append(name)
for n in ("Nose1", "Nose2", "Nose3"):
    eb[n].use_deform = False
bpy.ops.object.mode_set(mode="OBJECT")


def seg_weights(p):
    x = max(0.0, min(1.0, line_param(p) / reach)) * (N - 1)
    k0 = min(int(x), N - 1)
    t = x - k0
    if k0 >= N - 1 or t < 1e-3:
        return [(k0, 1.0)]
    return [(k0, 1.0 - t), (k0 + 1, t)]


# trunk: all segments
for g in list(nose.vertex_groups):
    nose.vertex_groups.remove(g)
ngroups = [nose.vertex_groups.new(name=n) for n in seg_names]
for v in nose.data.vertices:
    for k, w in seg_weights(v.co):
        ngroups[k].add([v.index], w, "REPLACE")
if not any(m.type == "ARMATURE" for m in nose.modifiers):
    nose.modifiers.new("Armature", "ARMATURE").object = arm

# snout (in the body mesh): segments too, blending into the head right at the face
bgroups = [body.vertex_groups.get(n) or body.vertex_groups.new(name=n) for n in seg_names]
bnames = {g.index: g.name for g in body.vertex_groups}
snout = 0
for v in body.data.vertices:
    p = v.co
    if not is_snout(p):
        continue
    snout += 1
    blend = max(0.0, min(1.0, (-0.33 - p.y) / 0.05))   # 0 at the face, 1 once clear of it
    blend = blend * blend * (3 - 2 * blend)
    for g in list(v.groups):
        name = bnames[g.group]
        if name.startswith("NoseSeg"):
            body.vertex_groups[name].remove([v.index])
        else:
            body.vertex_groups[name].add([v.index], g.weight * (1.0 - blend), "REPLACE")
    for k, w in seg_weights(p):
        bgroups[k].add([v.index], w * blend, "REPLACE")
print("NOSE trunk verts", len(nose.data.vertices), "snout verts", snout, "segments", N, "line", round(LINE_LEN, 3))

# ---------- export (same settings as rig_animate.py / paint_mole.py) ----------
bpy.ops.object.select_all(action="DESELECT")
objs = [arm] + [o for o in scene.objects if o.type == "MESH" and o.parent == arm]
for o in objs:
    o.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(
    filepath=out_fbx, use_selection=True, object_types={"ARMATURE", "MESH"},
    apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
    add_leaf_bones=True, bake_anim=True, bake_anim_use_nla_strips=True, bake_anim_use_all_actions=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
    path_mode="COPY", embed_textures=False, mesh_smooth_type="FACE")
bpy.ops.wm.save_as_mainfile(filepath=out_fbx.replace(".fbx", "_firm.blend"))
print("FIRM_RIG", out_fbx, [o.name for o in objs])
