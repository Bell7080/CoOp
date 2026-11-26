# Low-poly hand crank for the core engine: a spoked wheel with a red grip, axle along X, pivot at the hub.
# usage: blender --background --python make_crank.py -- <out.fbx>
import bpy, sys, math
out = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)

def mat(name, rgb):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*rgb, 1)
    return m

metal = mat("CrankMetal", (0.45, 0.47, 0.5))
red = mat("CrankGrip", (0.7, 0.08, 0.05))
parts = []
bpy.ops.mesh.primitive_torus_add(major_radius=0.4, minor_radius=0.045, major_segments=14, minor_segments=6, rotation=(0, math.pi / 2, 0))
parts.append((bpy.context.object, metal))
bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.09, depth=0.14, rotation=(0, math.pi / 2, 0))
parts.append((bpy.context.object, metal))
for k in range(4):
    a = k * math.pi / 2
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, math.cos(a) * 0.2, math.sin(a) * 0.2), rotation=(a, 0, 0), scale=(0.03, 0.04, 0.4))
    parts.append((bpy.context.object, metal))
# handle sticking out of the rim, with a grip
bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.025, depth=0.18, location=(0.09, 0, 0.4), rotation=(0, math.pi / 2, 0))
parts.append((bpy.context.object, metal))
bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.045, depth=0.16, location=(0.24, 0, 0.4), rotation=(0, math.pi / 2, 0))
parts.append((bpy.context.object, red))
for ob, m in parts:
    ob.data.materials.append(m)
bpy.ops.object.select_all(action="DESELECT")
for ob, _ in parts: ob.select_set(True)
bpy.context.view_layer.objects.active = parts[0][0]
bpy.ops.object.join()
crank = bpy.context.object
crank.name = "ShipCrank"
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_UNITS",
                         axis_forward="-Z", axis_up="Y", bake_anim=False, mesh_smooth_type="FACE")
print("CRANK", len(crank.data.polygons))
