# Headless Blender: model the alarm-clock spider in Tripo's unit box (front +X, left +Y, up +Z, feet z=-0.5)
# and save it as a GLB that rig_animate.py rigs and animates like the Tripo characters.
# Replaced on 2026-11-27 by the Tripo model; kept here with cfg_clockspider_handmade.py and paint_clockspider.py.
# usage: blender --background --python make_clockspider.py -- <cfg_clockspider_handmade.py> <atlas.png> <out.glb>
import bpy, bmesh, sys, math, runpy
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
cfg = runpy.run_path(args[0])
atlas_path, dst = args[1], args[2]
ZC, RC, XF, XB = cfg["ZC"], cfg["RC"], cfg["X_FRONT"], cfg["X_BACK"]

bpy.ops.wm.read_factory_settings(use_empty=True)
bm = bmesh.new()
uv_layer = bm.loops.layers.uv.new("UVMap")

A = 512.0
SW = {  # atlas swatches in pixels (x0, y0, x1, y1), top-left origin
    "case": (384, 0, 512, 64), "bell": (384, 64, 512, 128), "brass": (384, 128, 512, 192),
    "back": (384, 192, 512, 256), "rim": (384, 256, 512, 320), "dark": (384, 320, 512, 384),
}
DIAL_C, DIAL_R = (203.0, 186.0), 168.0   # the painted dial's centre and radius in the atlas
R_DIAL = RC * 0.87


def px(x, y):
    return (x / A, 1.0 - y / A)


def face(points, uvs):
    vs = [bm.verts.new(p) for p in points]
    f = bm.faces.new(vs)
    for loop, uv in zip(f.loops, uvs):
        loop[uv_layer].uv = uv
    return f


def swatch_face(points, kind):
    # each face gets its own little patch of the swatch (projected on its own plane) so the noise shows
    x0, y0, x1, y1 = SW[kind]
    p = [Vector(q) for q in points]
    n = (p[1] - p[0]).cross(p[2] - p[0])
    if n.length < 1e-9:
        n = Vector((0, 0, 1))
    n.normalize()
    ax = (p[1] - p[0]).normalized()
    ay = n.cross(ax)
    loc = [((q - p[0]).dot(ax), (q - p[0]).dot(ay)) for q in p]
    mnx, mny = min(a for a, _ in loc), min(b for _, b in loc)
    span = max(max(a for a, _ in loc) - mnx, max(b for _, b in loc) - mny, 1e-6)
    s = min(1.0, span / 0.2)
    w, h = (x1 - x0 - 4) * s, (y1 - y0 - 4) * s
    uvs = [px(x0 + 2 + (a - mnx) / span * w, y0 + 2 + (b - mny) / span * h) for a, b in loc]
    return face(points, uvs)


def ring(x, r, n=12, off=math.radians(15)):
    return [(x, r * math.cos(off + 2 * math.pi * k / n), ZC + r * math.sin(off + 2 * math.pi * k / n)) for k in range(n)]


def dial_uv(p):
    return px(DIAL_C[0] + p[1] / R_DIAL * DIAL_R, DIAL_C[1] - (p[2] - ZC) / R_DIAL * DIAL_R)


# ---------------- clock case ----------------
N = 12
back_in = ring(XB - 0.012, RC - 0.04, N)
back = ring(XB, RC, N)
front_out = ring(XF - 0.012, RC, N)
front_in = ring(XF, R_DIAL, N)
dial = ring(XF - 0.004, R_DIAL, N)
for k in range(N):
    j = (k + 1) % N
    swatch_face([back[k], back[j], front_out[j], front_out[k]], "case")
    swatch_face([front_out[k], front_out[j], front_in[j], front_in[k]], "case")
    swatch_face([front_in[k], front_in[j], dial[j], dial[k]], "rim")
    swatch_face([back_in[k], back_in[j], back[j], back[k]], "back")
    centre = (XF - 0.004, 0.0, ZC)
    face([dial[k], dial[j], centre], [dial_uv(dial[k]), dial_uv(dial[j]), dial_uv(centre)])
    swatch_face([back_in[j], back_in[k], (XB - 0.012, 0.0, ZC)], "back")


def box(c, size, kind, rot_x=0.0):
    cx, cy, cz = c
    hx, hy, hz = (s / 2 for s in size)
    cr, sr = math.cos(rot_x), math.sin(rot_x)

    def P(x, y, z):
        y2, z2 = y * cr - z * sr, y * sr + z * cr
        return (cx + x, cy + y2, cz + z2)
    v = [P(x, y, z) for x in (-hx, hx) for y in (-hy, hy) for z in (-hz, hz)]
    for q in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)):
        swatch_face([v[i] for i in q], kind)


def basis(n):
    n = Vector(n).normalized()
    a = Vector((1, 0, 0)) if abs(n.x) < 0.9 else Vector((0, 1, 0))
    u = n.cross(a).normalized()
    return n, u, n.cross(u)


def dome(c, axis, r, kind, sides=8, rings=3):
    n, u, w = basis(axis)
    c = Vector(c)
    pts = []
    for i in range(1, rings + 1):
        th = math.radians(90.0 * i / rings)
        pts.append([c + n * (r * math.cos(th)) + (u * math.cos(2 * math.pi * k / sides) + w * math.sin(2 * math.pi * k / sides)) * (r * math.sin(th) * 1.05)
                    for k in range(sides)])
    top = c + n * r
    for k in range(sides):
        j = (k + 1) % sides
        swatch_face([top, pts[0][k], pts[0][j]], kind)
        for i in range(rings - 1):
            swatch_face([pts[i][k], pts[i + 1][k], pts[i + 1][j], pts[i][j]], kind)
        swatch_face([pts[-1][j], pts[-1][k], c - n * 0.01], kind)


def tube(points, radius, kind, sides=4):
    rings = []
    for i, p in enumerate(points):
        d = Vector(points[min(i + 1, len(points) - 1)]) - Vector(points[max(i - 1, 0)])
        n, u, w = basis(d)
        rings.append([Vector(p) + (u * math.cos(2 * math.pi * k / sides + math.pi / 4) + w * math.sin(2 * math.pi * k / sides + math.pi / 4)) * radius
                      for k in range(sides)])
    for i in range(len(rings) - 1):
        for k in range(sides):
            j = (k + 1) % sides
            swatch_face([rings[i][k], rings[i][j], rings[i + 1][j], rings[i + 1][k]], kind)


# ---------------- bells, handle, hammer, winding key ----------------
for s in (1, -1):
    tilt = math.radians(38)
    axis = (0.0, s * math.sin(tilt), math.cos(tilt))
    base = Vector((-0.01, s * 0.18, ZC + RC - 0.04))
    dome(base, axis, 0.115, "bell")
    tube([base - Vector(axis) * 0.03, base + Vector(axis) * 0.02], 0.014, "brass")
arc = [(-0.035, 0.27 * math.cos(math.radians(a)), ZC + 0.2 + 0.27 * math.sin(math.radians(a))) for a in range(32, 150, 14)]
tube(arc, 0.012, "brass")
box((-0.005, 0.0, ZC + RC + 0.02), (0.03, 0.03, 0.06), "brass")
box((-0.005, 0.0, ZC + RC + 0.055), (0.035, 0.05, 0.025), "brass")
tube([(XB - 0.005, 0.0, ZC), (XB - 0.05, 0.0, ZC)], 0.018, "dark", sides=6)
box((XB - 0.06, 0.0, ZC), (0.012, 0.13, 0.05), "dark")
for s in (1, -1):
    box((XB + 0.01, s * 0.12, ZC - RC + 0.01), (0.04, 0.05, 0.03), "dark")


# ---------------- eight fleshy legs ----------------
LEG_SIDES = 6
for side, i in cfg["LEGS"]:
    hip, knee, tip, d = cfg["leg"](side, i)
    hip, knee, tip = Vector(hip), Vector(knee), Vector(tip)
    horiz = Vector((d[0], d[1], 0.0)).normalized()
    side_axis = horiz.cross(Vector((0, 0, 1))).normalized()
    stations = [  # (point, radius)
        (hip.lerp(knee, -0.1), 0.04), (hip.lerp(knee, 0.55), 0.058), (knee, 0.065),
        (knee.lerp(tip, 0.3), 0.058), (knee.lerp(tip, 0.62), 0.04), (knee.lerp(tip, 0.88), 0.018), (tip, 0.0),
    ]
    lengths = [0.0]
    for a, b in zip(stations, stations[1:]):
        lengths.append(lengths[-1] + (b[0] - a[0]).length)
    rings = []
    for idx, (p, r) in enumerate(stations):
        prev = stations[max(idx - 1, 0)][0]
        nxt = stations[min(idx + 1, len(stations) - 1)][0]
        axis = (nxt - prev).normalized()
        u = side_axis
        w = axis.cross(u).normalized()
        u = w.cross(axis).normalized()
        rings.append([p + (u * math.cos(2 * math.pi * k / LEG_SIDES) + w * math.sin(2 * math.pi * k / LEG_SIDES)) * r * (1.0 if k % 3 else 1.15)
                      for k in range(LEG_SIDES)])
    for a in range(len(rings) - 1):
        va, vb = 386 + 124 * lengths[a] / lengths[-1], 386 + 124 * lengths[a + 1] / lengths[-1]
        for k in range(LEG_SIDES):
            j = k + 1
            ua, ub = 8 + 496 * k / LEG_SIDES, 8 + 496 * j / LEG_SIDES
            j %= LEG_SIDES
            if a + 1 == len(rings) - 1:  # the sharp tip
                face([rings[a][k], rings[a][j], tip], [px(ua, va), px(ub, va), px((ua + ub) / 2, vb)])
            else:
                face([rings[a][k], rings[a][j], rings[a + 1][j], rings[a + 1][k]], [px(ua, va), px(ub, va), px(ub, vb), px(ua, vb)])

bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
me = bpy.data.meshes.new("ClockSpider")
bm.to_mesh(me)
bm.free()
obj = bpy.data.objects.new("ClockSpider", me)
bpy.context.scene.collection.objects.link(obj)

mat = bpy.data.materials.new("ClockSpider")
mat.use_nodes = True
tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
tex.image = bpy.data.images.load(atlas_path)
tex.interpolation = "Closest"
bsdf = mat.node_tree.nodes["Principled BSDF"]
mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
bsdf.inputs["Roughness"].default_value = 0.85
me.materials.append(mat)

bpy.ops.object.select_all(action="DESELECT")
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
bpy.ops.export_scene.gltf(filepath=dst, export_format="GLB", use_selection=True, export_yup=True)
print("MODELLED", dst, len(me.vertices), "verts", len(me.polygons), "faces")
