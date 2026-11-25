# blender --background <rigged.blend> --python render_sheet.py -- <out_dir>
import bpy, sys, os, math
from mathutils import Vector

out = sys.argv[sys.argv.index("--") + 1]
os.makedirs(out, exist_ok=True)
scene = bpy.context.scene
arm = next(o for o in scene.objects if o.type == "ARMATURE")
mesh = next(o for o in scene.objects if o.type == "MESH")
height = max((mesh.matrix_world @ Vector(c)).z for c in mesh.bound_box)

scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "TEXTURE"
scene.render.resolution_x = 360
scene.render.resolution_y = 360
scene.render.film_transparent = False
world = bpy.data.worlds.new("W"); scene.world = world

cam_data = bpy.data.cameras.new("Cam"); cam_data.type = "ORTHO"; cam_data.ortho_scale = height * 1.5
cam = bpy.data.objects.new("Cam", cam_data); scene.collection.objects.link(cam); scene.camera = cam

def look(view):
    target = Vector((0, 0, height * 0.5))
    d = {"front": Vector((0, -1, 0)), "side": Vector((1, 0, 0)), "three": Vector((0.8, -1, 0.35))}[view].normalized()
    cam.location = target + d * 6
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()

tracks = list(arm.animation_data.nla_tracks)
for tr in tracks:
    for tr2 in tracks: tr2.mute = tr2 is not tr
    strip = tr.strips[0]
    length = strip.frame_end - strip.frame_start
    for view in ("three", "side"):
        look(view)
        for k, frac in enumerate((0.0, 0.25, 0.5)):
            scene.frame_set(int(strip.frame_start + length * frac))
            scene.render.filepath = os.path.join(out, f"{tr.name}_{view}_{k}.png")
            bpy.ops.render.render(write_still=True)
for tr in tracks: tr.mute = True
look("front"); scene.frame_set(0)
scene.render.filepath = os.path.join(out, "Rest_front.png"); bpy.ops.render.render(write_still=True)
print("SHEET", [t.name for t in tracks])
