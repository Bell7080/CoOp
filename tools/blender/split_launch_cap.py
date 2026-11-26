# Splits the launch console prop into two objects in one FBX: "Base" (pedestal, screen head, blue ring) and
# "Cap" (the red mushroom button and its stem: every face entirely above the ring), so the game can push the
# button in. Same axes as glb_to_prop.py, so the prop fits exactly as before.
# usage: blender --background --python split_launch_cap.py -- <ShipLaunchConsole.fbx> [cut fraction of height]
import bpy, bmesh, sys

args = sys.argv[sys.argv.index("--") + 1:]
path = args[0]
cut = float(args[1]) if len(args) > 1 else 0.80
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=path, axis_forward="-Z", axis_up="Y")
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
for o in bpy.context.scene.objects: o.select_set(o in meshes)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1: bpy.ops.object.join()
ob = bpy.context.view_layer.objects.active
bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in list(bpy.context.scene.objects):
    if o is not ob: bpy.data.objects.remove(o, do_unlink=True)
ob.name = "Base"
zs = [v.co.z for v in ob.data.vertices]
z0, z1 = min(zs), max(zs)
limit = z0 + (z1 - z0) * cut
bpy.ops.object.mode_set(mode="EDIT")
bm = bmesh.from_edit_mesh(ob.data)
for f in bm.faces: f.select = all(v.co.z > limit for v in f.verts)
bmesh.update_edit_mesh(ob.data)
bpy.ops.mesh.separate(type="SELECTED")
bpy.ops.object.mode_set(mode="OBJECT")
cap = [o for o in bpy.context.scene.objects if o is not ob][0]
cap.name = "Cap"
print("SPLIT cap faces", len(cap.data.polygons), "base faces", len(ob.data.polygons), "cut z", round(limit, 3), "of", round(z1, 3))
bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_UNITS",
                         axis_forward="-Z", axis_up="Y", bake_anim=False, path_mode="COPY", embed_textures=False, mesh_smooth_type="FACE")
