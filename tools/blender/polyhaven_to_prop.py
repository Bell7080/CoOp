# Poly Haven glTF (CC0) -> low-poly static prop FBX for Unity, in the game's PS1 look:
# meshes joined, decimated to at most MAX_TRIS triangles, every texture shrunk to TEX px and saved as PNG next to
# the FBX, sitting on z=0 and centred. The front stays the glTF front (+Z), which lands on Unity +Z like the Tripo
# props (Blender -Y with this export).
# usage: blender --background --python polyhaven_to_prop.py -- <in.gltf> <out.fbx> [max_tris] [tex_px]
import bpy, sys, os
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
src, dst = args[0], args[1]
MAX_TRIS = int(args[2]) if len(args) > 2 else 3000
TEX = int(args[3]) if len(args) > 3 else 128
name = os.path.splitext(os.path.basename(dst))[0]
out_dir = os.path.dirname(dst)
os.makedirs(out_dir, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
if not meshes:
    raise SystemExit("no mesh in " + src)
bpy.ops.object.select_all(action="DESELECT")
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1: bpy.ops.object.join()
ob = bpy.context.view_layer.objects.active
ob.name = name
bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in list(bpy.context.scene.objects):
    if o is not ob: bpy.data.objects.remove(o, do_unlink=True)

# decimate down to the triangle budget
me = ob.data
tris = sum(len(p.vertices) - 2 for p in me.polygons)
if tris > MAX_TRIS:
    mod = ob.modifiers.new("Decimate", "DECIMATE")
    mod.ratio = max(0.01, MAX_TRIS / tris)
    bpy.ops.object.modifier_apply(modifier=mod.name)
me = ob.data

# floor at z=0, centred on x/y
mn = Vector((min(v.co.x for v in me.vertices), min(v.co.y for v in me.vertices), min(v.co.z for v in me.vertices)))
mx = Vector((max(v.co.x for v in me.vertices), max(v.co.y for v in me.vertices), max(v.co.z for v in me.vertices)))
off = Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z))
for v in me.vertices: v.co -= off
me.update()

# small textures: only the base colour is kept (no normal / roughness maps in the PS1 look)
for mat in me.materials:
    if mat is None or not mat.use_nodes: continue
    bsdf = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if bsdf is None: continue
    for inp in list(bsdf.inputs):
        if inp.name != "Base Color":
            for link in list(inp.links): mat.node_tree.links.remove(link)
    link = bsdf.inputs["Base Color"].links[0] if bsdf.inputs["Base Color"].links else None
    node = link.from_node if link else None
    while node is not None and node.type != "TEX_IMAGE" and node.inputs and node.inputs[0].links:
        node = node.inputs[0].links[0].from_node
    if node is None or node.type != "TEX_IMAGE" or node.image is None: continue
    img = node.image
    w, h = img.size
    if w > TEX or h > TEX:
        s = TEX / max(w, h)
        img.scale(max(1, int(w * s)), max(1, int(h * s)))
    img.filepath_raw = os.path.join(out_dir, f"{name}_{mat.name}.png".replace(" ", "_"))
    img.file_format = "PNG"
    img.save()

bpy.ops.object.select_all(action="DESELECT"); ob.select_set(True)
bpy.ops.export_scene.fbx(filepath=dst, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_UNITS",
                         axis_forward="-Z", axis_up="Y", bake_anim=False, path_mode="RELATIVE", embed_textures=False, mesh_smooth_type="FACE")
print("PROP", name, sum(len(p.vertices) - 2 for p in me.polygons), "tris", tuple(round(c, 2) for c in (mx - mn)))
