# Cut the mole's right arm out of the painted body as a static mesh for the first-person view.
# Pivot = shoulder (ArmR1 head). usage: blender --background MoleAlien_painted.blend --python make_arm.py -- <out.fbx>
import bpy, sys, bmesh
from mathutils import Vector

out = sys.argv[sys.argv.index("--") + 1]
scene = bpy.context.scene
body = bpy.data.objects["MoleAlien"]
arm = next(o for o in scene.objects if o.type == "ARMATURE")
groups = {body.vertex_groups[n].index for n in ("ArmR1", "ArmR2") if n in body.vertex_groups}
me = body.data
w = [0.0] * len(me.vertices)
for v in me.vertices:
    w[v.index] = sum(g.weight for g in v.groups if g.group in groups)

bm = bmesh.new()
bm.from_mesh(me)
uv = bm.loops.layers.uv.active
keep = [f for f in bm.faces if sum(w[v.index] for v in f.verts) / len(f.verts) > 0.55]
drop = [f for f in bm.faces if f not in set(keep)]
bmesh.ops.delete(bm, geom=drop, context="FACES")
shoulder = Vector(arm.data.bones["ArmR1"].head_local)
for v in bm.verts:
    v.co -= shoulder
new_me = bpy.data.meshes.new("MoleArm")
bm.to_mesh(new_me)
bm.free()
for m in me.materials:
    new_me.materials.append(m)
ob = bpy.data.objects.new("MoleArm", new_me)
scene.collection.objects.link(ob)
print("ARM faces", len(new_me.polygons), "shoulder", tuple(round(c, 3) for c in shoulder))

bpy.ops.object.select_all(action="DESELECT")
ob.select_set(True)
bpy.context.view_layer.objects.active = ob
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={"MESH"},
                         apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                         bake_anim=False, path_mode="COPY", embed_textures=False, mesh_smooth_type="FACE")
