# Closes the open sides of a static prop FBX (Tripo models sometimes come with whole walls missing, which
# shows as a see-through hole in game). Welds the UV-seam duplicates, fills every open boundary loop,
# points the new faces at the texture colour the prop's body mostly has, and re-exports in place with the
# same settings as glb_to_prop.py (so the Unity .meta and its GUID stay valid).
# usage: blender --background --python fill_prop_holes.py -- <prop.fbx> [<body uv u> <body uv v>]
import bpy, bmesh, sys, os
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
path = args[0]
body_uv = (float(args[1]), float(args[2])) if len(args) >= 3 else None
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=path)
ob = next(o for o in bpy.context.scene.objects if o.type == "MESH")
for o in list(bpy.context.scene.objects):
    if o is not ob: bpy.data.objects.remove(o, do_unlink=True)
me = ob.data
bm = bmesh.new(); bm.from_mesh(me)
uv = bm.loops.layers.uv.active
before = len(bm.faces)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0005 * max(ob.dimensions))
boundary = [e for e in bm.edges if e.is_boundary]
print("FILL boundary edges after weld:", len(boundary))
old = set(bm.faces)
bmesh.ops.holes_fill(bm, edges=boundary, sides=0)
new = [f for f in bm.faces if f not in old]
res = bmesh.ops.triangulate(bm, faces=new, quad_method="BEAUTY", ngon_method="BEAUTY")
new = res["faces"]
if body_uv is None:
    # the UV most of the side faces use: average UV of the faces around the holes
    acc, n = Vector((0.0, 0.0)), 0
    for e in boundary:
        for f in e.link_faces:
            if f in old:
                for l in f.loops: acc += l[uv].uv; n += 1
    body_uv = tuple(acc / max(n, 1))
for f in new:
    for l in f.loops: l[uv].uv = body_uv
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
print("FILL faces", before, "->", len(bm.faces), "new", len(new), "uv", tuple(round(c, 3) for c in body_uv))
bm.to_mesh(me); bm.free(); me.update()
bpy.ops.object.select_all(action="DESELECT"); ob.select_set(True)
bpy.context.view_layer.objects.active = ob
bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_UNITS",
                         axis_forward="-Z", axis_up="Y", bake_anim=False, path_mode="COPY", embed_textures=False, mesh_smooth_type="FACE")
