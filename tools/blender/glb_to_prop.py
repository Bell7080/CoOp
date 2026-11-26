# Tripo GLB -> static prop FBX for Unity: front -Y (Unity +Z), sitting on z=0, centred, unit-box scale kept
# (the level builder fits it into a box). Texture saved as <name>_Albedo.png next to the FBX.
# usage: blender --background --python glb_to_prop.py -- <in.glb> <out.fbx>
import bpy, sys, os
from mathutils import Vector

src, dst = sys.argv[sys.argv.index("--") + 1:][:2]
name = os.path.splitext(os.path.basename(dst))[0]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
for o in bpy.context.scene.objects: o.select_set(o in meshes)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1: bpy.ops.object.join()
ob = bpy.context.view_layer.objects.active
ob.name = name
bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in list(bpy.context.scene.objects):
    if o is not ob: bpy.data.objects.remove(o, do_unlink=True)
me = ob.data
# Drop stray specks: loose parts with almost no vertices lying outside the main body's bounds.
import bmesh
bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
seen, parts = set(), []
for v in bm.verts:
    if v.index in seen: continue
    stack, part = [v], []
    seen.add(v.index)
    while stack:
        cur = stack.pop(); part.append(cur)
        for e in cur.link_edges:
            o = e.other_vert(cur)
            if o.index not in seen: seen.add(o.index); stack.append(o)
    parts.append(part)
total = len(bm.verts)
big = [p for p in parts if len(p) >= total * 0.02]
if big:
    lo = Vector((min(v.co.x for p in big for v in p), min(v.co.y for p in big for v in p), min(v.co.z for p in big for v in p)))
    hi = Vector((max(v.co.x for p in big for v in p), max(v.co.y for p in big for v in p), max(v.co.z for p in big for v in p)))
    pad = (hi - lo) * 0.05
    junk = [v for p in parts if len(p) < total * 0.02 for v in p
            if not all(lo[i] - pad[i] <= v.co[i] <= hi[i] + pad[i] for i in range(3))]
    if junk: bmesh.ops.delete(bm, geom=junk, context="VERTS")
    print("CLEAN removed", len(junk), "stray verts")
elif os.environ.get("PROP_KEEP_ALL"):
    print("CLEAN skipped (PROP_KEEP_ALL): tall thin parts such as a pylon are real")
else:
    # made of many small pieces: drop pieces lying wholly far above where almost all vertices are
    zs = sorted(v.co.z for v in bm.verts)
    z95, span = zs[int(len(zs) * 0.95)], zs[-1] - zs[0]
    cut = z95 + (z95 - zs[0]) * 0.25  # well above the body: a stray strand (vertices, even if connected)
    junk = [v for v in bm.verts if v.co.z > cut]
    if junk: bmesh.ops.delete(bm, geom=junk, context="VERTS")
    print("CLEAN (pieces) removed", len(junk), "stray verts")
bm.to_mesh(me); bm.free()
for v in me.vertices:  # front +X -> -Y
    x, y, z = v.co
    v.co = Vector((y, -x, z))
mn = Vector((min(v.co.x for v in me.vertices), min(v.co.y for v in me.vertices), min(v.co.z for v in me.vertices)))
mx = Vector((max(v.co.x for v in me.vertices), max(v.co.y for v in me.vertices), max(v.co.z for v in me.vertices)))
off = Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z))
for v in me.vertices: v.co -= off
me.update()
os.makedirs(os.path.dirname(dst), exist_ok=True)
for img in bpy.data.images:
    if img.size[0] > 0:
        img.filepath_raw = os.path.join(os.path.dirname(dst), name + "_Albedo.png"); img.file_format = "PNG"; img.save()
        break
bpy.ops.object.select_all(action="DESELECT"); ob.select_set(True)
bpy.ops.export_scene.fbx(filepath=dst, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_UNITS",
                         axis_forward="-Z", axis_up="Y", bake_anim=False, path_mode="COPY", embed_textures=False, mesh_smooth_type="FACE")
print("PROP", name, len(me.polygons), "faces", tuple(round(c, 2) for c in (mx - mn)))
