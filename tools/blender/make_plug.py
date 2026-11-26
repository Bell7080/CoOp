# Appliance plug for the cord ends: a round Korean (type C/F style) two-pin plug - ribbed grip body, a shoulder
# face with two round metal pins, and a strain-relief tail where the cord leaves. Pins point to -Y (Unity +Z),
# cord leaves to +Y (Unity -Z), centred on the body axis. Real size x1.4 so it reads in the player's hand.
# usage: blender --background --python make_plug.py -- <out.fbx>
import bpy, bmesh, sys, os, math

dst = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)

def material(name, rgb, metal=0.0, rough=0.6):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
    bsdf.inputs["Metallic"].default_value = metal
    bsdf.inputs["Roughness"].default_value = rough
    return m

plastic = material("PlugPlastic", (0.07, 0.07, 0.075))
rubber = material("PlugRubber", (0.04, 0.04, 0.045), rough=0.9)
metal = material("PlugPins", (0.72, 0.72, 0.7), metal=1.0, rough=0.35)

S = 1.4
parts = []

def cylinder(name, radius, depth, y, mat, verts=16, scale_xz=(1, 1)):
    # axis along Y, centred at y
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=radius * S, depth=depth * S, location=(0, y * S, 0), rotation=(math.radians(90), 0, 0))
    ob = bpy.context.active_object
    ob.name = name
    ob.scale.x *= scale_xz[0]
    ob.scale.z *= scale_xz[1]
    ob.data.materials.append(mat)
    parts.append(ob)
    return ob

# grip body (slightly flattened) with a bevelled shoulder face
body = cylinder("Body", 0.021, 0.038, 0.0, plastic, verts=20, scale_xz=(1.0, 0.86))
cylinder("Shoulder", 0.0235, 0.006, -0.021, plastic, verts=20, scale_xz=(1.0, 0.86))
# grip ribs around the body
for i in range(5):
    cylinder("Rib%d" % i, 0.0218, 0.0022, 0.004 + i * 0.0045, rubber, verts=20, scale_xz=(1.0, 0.86))
# strain-relief tail, tapering to the cord
cylinder("Tail", 0.0085, 0.02, 0.028, rubber, verts=12)
cylinder("TailTip", 0.006, 0.012, 0.042, rubber, verts=12)
# two round pins, 19 mm apart, sticking out of the shoulder face
for side in (-1, 1):
    cylinder("Pin%d" % side, 0.0024, 0.019, -0.0335, metal, verts=10).location.x = side * 0.0095 * S

for ob in parts: ob.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.object.join()
plug = bpy.context.active_object
plug.name = "CordPlug"
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
bpy.ops.object.shade_flat()

os.makedirs(os.path.dirname(dst), exist_ok=True)
bpy.ops.export_scene.fbx(filepath=dst, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_UNITS",
                         axis_forward="-Z", axis_up="Y", bake_anim=False, mesh_smooth_type="FACE")
print("PLUG", len(plug.data.polygons), "faces", tuple(round(d, 3) for d in plug.dimensions))
