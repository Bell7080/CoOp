# Korean-style power strip (멀티탭) with N round two-hole outlets in a row on top, a red rocker switch at one
# end and the cord tail at the other. Length along X (Unity X), outlets face up. Each outlet also gets an
# empty "Outlet_<i>" at the centre of its face so the game knows exactly where plugs go in. Real size.
# usage: blender --background --python make_multitap.py -- <out_dir> 2 3 5 8
import bpy, sys, os, math

args = sys.argv[sys.argv.index("--") + 1:]
out_dir, counts = args[0], [int(a) for a in args[1:]]
SPACING, WIDTH, HEIGHT, END = 0.075, 0.062, 0.042, 0.06

def material(name, rgb, rough=0.55, metal=0.0, emit=None):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    return m

def build(n):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    shell = material("TapShell", (0.86, 0.85, 0.8))
    face = material("TapFace", (0.78, 0.77, 0.72))
    hole = material("TapHole", (0.03, 0.03, 0.03), rough=0.9)
    switch = material("TapSwitch", (0.75, 0.08, 0.06), rough=0.35)
    cord = material("TapCord", (0.85, 0.85, 0.82), rough=0.7)
    parts = []
    half = n * SPACING / 2 + END
    def box(name, size, loc, mat, bevel=0.0):
        bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
        o = bpy.context.active_object; o.name = name; o.scale = size
        bpy.ops.object.transform_apply(scale=True)
        if bevel > 0:
            mod = o.modifiers.new("b", "BEVEL"); mod.width = bevel; mod.segments = 2
            bpy.ops.object.modifier_apply(modifier="b")
        o.data.materials.append(mat); parts.append(o); return o
    def cyl(name, r, d, loc, mat, verts=20, rot=(0, 0, 0)):
        bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=d, location=loc, rotation=rot)
        o = bpy.context.active_object; o.name = name; o.data.materials.append(mat); parts.append(o); return o
    box("Body", (half * 2, WIDTH, HEIGHT), (0, 0, HEIGHT / 2), shell, bevel=0.008)
    for i in range(n):
        x = (i - (n - 1) / 2) * SPACING
        cyl("Face%d" % i, 0.024, 0.004, (x, 0, HEIGHT + 0.0005), face)
        for s in (-1, 1):
            cyl("Hole%d_%d" % (i, s), 0.0032, 0.006, (x + s * 0.0095, 0, HEIGHT + 0.0005), hole, verts=8)
        e = bpy.data.objects.new("Outlet_%d" % i, None)
        bpy.context.scene.collection.objects.link(e)
        e.location = (x, 0, HEIGHT + 0.003)
    # rocker switch at +X, cord tail at -X
    sx = half - END / 2
    box("Switch", (0.03, 0.026, 0.012), (sx, 0, HEIGHT + 0.004), switch, bevel=0.003)
    cyl("Tail", 0.008, 0.03, (-half - 0.012, 0, HEIGHT * 0.45), cord, verts=10, rot=(0, math.radians(90), 0))
    for o in parts: o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    strip = bpy.context.active_object
    strip.name = "Multitap%d" % n
    bpy.ops.object.shade_flat()
    for o in bpy.context.scene.objects: o.select_set(True)
    path = os.path.join(out_dir, "Multitap%d" % n, "Multitap%d.fbx" % n)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH", "EMPTY"}, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", bake_anim=False, mesh_smooth_type="FACE")
    print("TAP", n, "outlets", "length", round(half * 2, 3))

for n in counts: build(n)
