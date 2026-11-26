import bpy, sys, os
from mathutils import Vector
out = sys.argv[sys.argv.index("--") + 1]
scene = bpy.context.scene
arm = next(o for o in scene.objects if o.type == "ARMATURE")
scene.render.engine = "BLENDER_WORKBENCH"; scene.display.shading.color_type = "TEXTURE"; scene.display.shading.light = "STUDIO"
scene.render.resolution_x = scene.render.resolution_y = 400
cam_data = bpy.data.cameras.new("C"); cam_data.lens = 50
cam = bpy.data.objects.new("C", cam_data); scene.collection.objects.link(cam); scene.camera = cam
target = Vector((0, -0.22, 0.65)); cam.location = target + Vector((0, -1.6, 0.1))
cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
tracks = arm.animation_data.nla_tracks
for name, frac, tag in (("Idle", 0.0, "rest"), ("Attack", 0.4, "attack")):
    for t in tracks: t.mute = t.name != name
    s = tracks[name].strips[0]; scene.frame_set(int(s.frame_start + (s.frame_end - s.frame_start) * frac))
    jaw = arm.matrix_world @ arm.pose.bones["Jaw"].head
    cam.location = jaw + Vector((0, -0.9, 0.05)); cam.rotation_euler = (jaw - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(out, f"face_{tag}.png"); bpy.ops.render.render(write_still=True)
