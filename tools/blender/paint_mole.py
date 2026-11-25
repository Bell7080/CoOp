# Repaint the rigged mole: pale speckled skin (tinted per player in Unity), gentle eyes with catchlights,
# light grey nose. Split into MoleAlien (body, player colour), MoleNose (muted player colour), MoleEyes (as painted).
# usage: blender --background MoleAlien.blend --python paint_mole.py -- <out.fbx> <out_meta.json>
import bpy, sys, os, json, math
import numpy as np
from mathutils import Vector

out_fbx, out_meta = sys.argv[sys.argv.index("--") + 1:][:2]
scene = bpy.context.scene
arm = next(o for o in scene.objects if o.type == "ARMATURE")
mesh = next(o for o in scene.objects if o.type == "MESH")
me = mesh.data
H = max(v.co.z for v in me.vertices)

# ---------- original texture ----------
mat = me.materials[0]
tex_node = next(n for n in mat.node_tree.nodes if n.type == "TEX_IMAGE")
orig = tex_node.image
ow, oh = orig.size
opx = np.empty(ow * oh * 4, dtype=np.float32); orig.pixels.foreach_get(opx); opx = opx.reshape(oh, ow, 4)
olum = opx[..., 0] * 0.3 + opx[..., 1] * 0.59 + opx[..., 2] * 0.11

uv = me.uv_layers.active.data
vert_lum = np.ones(len(me.vertices))
for poly in me.polygons:
    for li in poly.loop_indices:
        u, v = uv[li].uv
        x = min(ow - 1, max(0, int(u * ow))); y = min(oh - 1, max(0, int(v * oh)))
        vi = me.loops[li].vertex_index
        vert_lum[vi] = min(vert_lum[vi], olum[y, x])

co = np.array([v.co[:] for v in me.vertices])
# eyes: dark vertices in the upper front of the head, clustered left/right
dark = (vert_lum < 0.18) & (co[:, 2] > 0.55 * H) & (co[:, 1] < 0)
eyes = []
for side in (-1, 1):
    pts = co[dark & (np.sign(co[:, 0]) == side)]
    c = pts.mean(axis=0)
    r = float(np.percentile(np.linalg.norm(pts - c, axis=1), 95))
    eyes.append((c, r))
print("EYES", [(list(np.round(c, 3)), round(r, 3)) for c, r in eyes])

nose_groups = [mesh.vertex_groups[n].index for n in ("Nose1", "Nose2", "Nose3")]
nose_w = np.zeros(len(me.vertices))
for v in me.vertices:
    for g in v.groups:
        if g.group in nose_groups: nose_w[v.index] += g.weight
# the nose is a tube around the Nose bone chain: distance to that polyline decides what is nose
bones = arm.data.bones
chain = [np.array(bones["Nose1"].head_local), np.array(bones["Nose2"].head_local), np.array(bones["Nose3"].head_local), np.array(bones["Nose3"].tail_local)]
def poly_dist(P):
    best = np.full(P.shape[:-1], 1e9)
    for a, b in zip(chain[:-1], chain[1:]):
        ab = b - a
        t = np.clip(((P - a) @ ab) / (ab @ ab), 0, 1)
        best = np.minimum(best, np.linalg.norm(P - (a + t[..., None] * ab), axis=-1))
    return best
# tube radius from the free-hanging end of the nose (Nose3 segment interior), not where it meets the face
a3, b3 = chain[2], chain[3]
t3 = np.clip(((co - a3) @ (b3 - a3)) / ((b3 - a3) @ (b3 - a3)), 0, 1)
d3 = np.linalg.norm(co - (a3 + t3[:, None] * (b3 - a3)), axis=1)
sel = (t3 > 0.25) & (t3 < 0.85) & (d3 < 0.1 * H)
R = float(np.percentile(d3[sel], 80)) * 1.15
# refit the centreline to the real geometry: per height slice, the frontmost cluster near the middle is nose
tip_z = float(co[sel][:, 2].min()) if sel.any() else float(chain[3][2])
root_z = min(float(chain[0][2]) + 0.04 * H, min(c[2] for c, _ in eyes) - 0.02 * H)
line = []
for zc in np.arange(tip_z + 0.01 * H, root_z, 0.015 * H):
    cand = co[(np.abs(co[:, 0]) < 0.1 * H) & (np.abs(co[:, 2] - zc) < 0.012 * H)]
    if len(cand) == 0: continue
    ymin = cand[:, 1].min()
    front = cand[cand[:, 1] < ymin + 2.0 * R]
    line.append(np.array([front[:, 0].mean(), ymin + R, zc]))
if len(line) >= 2: chain = line
print("NOSE LINE", len(line), "points")
print("NOSE R", round(R, 3))
vd = poly_dist(co)
import heapq
attr = me.color_attributes.new("NoseW", "FLOAT_COLOR", "POINT")
adj = [[] for _ in me.vertices]
for e in me.edges:
    i, j = e.vertices; d = float(np.linalg.norm(co[i] - co[j])); adj[i].append((j, d)); adj[j].append((i, d))
# UV seams split vertices: also link vertices that sit at (almost) the same spot
from collections import defaultdict
buckets = defaultdict(list)
for i, q in enumerate(np.round(co / (0.004 * H)).astype(int)): buckets[tuple(q)].append(i)
for ids in buckets.values():
    for i in ids:
        for j in ids:
            if i != j: adj[i].append((j, 0.0))
front_mid = (np.abs(co[:, 0]) < 0.08 * H) & (co[:, 1] < -0.15 * H) & (co[:, 2] > 0.2 * H)
cands = np.where(front_mid)[0]
tip_i = int(cands[np.argmin(co[cands, 2] + 0.5 * co[cands, 1])])
eye_z = min(c[2] for c, _ in eyes)
root = np.array([0.0, float(co[(np.abs(co[:, 0]) < 0.05 * H) & (np.abs(co[:, 2] - (eye_z - 0.05 * H)) < 0.02 * H)][:, 1].min()), eye_z - 0.05 * H])
geo = np.full(len(co), np.inf); geo[tip_i] = 0; heap = [(0.0, tip_i)]
while heap:
    d, i = heapq.heappop(heap)
    if d > geo[i]: continue
    for j, w in adj[i]:
        if d + w < geo[j]: geo[j] = d + w; heapq.heappush(heap, (d + w, j))
L = float(geo[np.argmin(np.linalg.norm(co - root, axis=1))])
nose_v = geo < L * 1.12
print("NOSE GEO tip", np.round(co[tip_i], 3), "L", round(L, 3), "verts", int(nose_v.sum()))
GEO_SCALE = L * 1.3  # geodesic distance from the nose tip, stored 0..1 in G for the stripes
for i in range(len(co)): attr.data[i].color = (1.0 if nose_v[i] else 0.0, float(min(geo[i], GEO_SCALE) / GEO_SCALE), 0.0, 1.0)

# ---------- bake position and nose mask into texture space ----------
S = 1024
for m in mesh.modifiers: m.show_render = m.show_viewport = False

def bake(name, build):
    img = bpy.data.images.new(name, S, S, alpha=True, float_buffer=True)
    bm = bpy.data.materials.new("Bake_" + name); bm.use_nodes = True
    nt = bm.node_tree; nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial"); em = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(build(nt), em.inputs["Color"]); nt.links.new(em.outputs[0], out.inputs[0])
    tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = img; nt.nodes.active = tn
    me.materials[0] = bm
    scene.render.engine = "CYCLES"; scene.cycles.samples = 1; scene.cycles.device = "CPU"
    scene.render.bake.margin = 6
    bpy.ops.object.select_all(action="DESELECT"); mesh.select_set(True); bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.bake(type="EMIT")
    px = np.empty(S * S * 4, dtype=np.float32); img.pixels.foreach_get(px)
    return px.reshape(S, S, 4)

def pos_node(nt):
    g = nt.nodes.new("ShaderNodeNewGeometry"); return g.outputs["Position"]
def nose_node(nt):
    a = nt.nodes.new("ShaderNodeAttribute"); a.attribute_name = "NoseW"; return a.outputs["Color"]

# coverage mask: bake white first with zero margin? use alpha of the position bake
P = bake("Pos", pos_node)
NB = bake("Nose", nose_node)
N = NB[..., 0]
G = NB[..., 1] * GEO_SCALE
me.materials[0] = mat
valid = P[..., 3] > 0.5
pos = P[..., :3]

# original texture resampled to the bake resolution (same UV layout)
yy = (np.arange(S) * oh // S); xx = (np.arange(S) * ow // S)
lum_s = olum[np.ix_(yy, xx)]

# ---------- paint ----------
rng = np.random.default_rng(7)
skin = np.array([0.9, 0.9, 0.88])
col = np.tile(skin, (S, S, 1)).astype(np.float32)
# soft speckles: random blobs on the body surface (back, sides, top of the head)
spots = np.zeros((S, S), dtype=np.float32)
cand = co[(co[:, 2] > 0.1 * H)]
for c in cand[rng.choice(len(cand), 70, replace=False)]:
    rad = rng.uniform(0.018, 0.04) * H
    d = np.linalg.norm(pos - c, axis=-1)
    spots = np.maximum(spots, np.clip(1 - d / rad, 0, 1) ** 0.6)
col *= (1 - 0.16 * spots)[..., None]
# subtle belly lightening and darker feet
belly = np.clip(1 - np.linalg.norm((pos - np.array([0, -0.35 * H, 0.35 * H])) / np.array([0.35, 0.3, 0.32]) / H, axis=-1), 0, 1)
col = col * (1 - 0.3 * belly[..., None]) + 0.3 * belly[..., None] * np.array([0.97, 0.96, 0.94])

# nose: light neutral grey, a little darker towards the tip; Unity tints it with a muted body colour
nose = np.clip((N - 0.35) / 0.3, 0, 1)
tip = np.clip((0.55 * H - pos[..., 2]) / (0.35 * H), 0, 1)
pink = np.array([0.86, 0.85, 0.84])[None, None, :] * (1 - 0.18 * tip[..., None])
# horizontal wrinkle stripes across the trunk (rings by distance along it from the tip)
period = 0.075 * H / 1.25
ph = (G / period) % 1.0
stripe = np.clip(1 - np.abs(ph - 0.5) / 0.16, 0, 1) ** 0.7
pink = pink * (1 - 0.32 * stripe[..., None])
col = col * (1 - nose[..., None]) + pink * nose[..., None]

# eyes: deep navy, lighter at the bottom, two catchlights -> soft and friendly
fwd = np.array([0, -1, 0]); up = np.array([0, 0, 1])
for c, r in eyes:
    d = np.linalg.norm(pos - c, axis=-1)
    eye = (d < r * 1.25) & (lum_s < 0.3)
    shade = np.clip((c[2] - pos[..., 2]) / r * 0.5 + 0.5, 0, 1)
    base = np.array([0.06, 0.05, 0.1]) * (1 - shade[..., None]) + np.array([0.2, 0.17, 0.3]) * shade[..., None]
    outward = np.array([np.sign(c[0]), 0, 0])
    h1 = c + r * (0.78 * fwd + 0.48 * up - 0.3 * np.array([1, 0, 0])) / np.linalg.norm(0.78 * fwd + 0.48 * up - 0.3 * np.array([1, 0, 0]))
    h2 = c + r * (0.85 * fwd - 0.35 * up + 0.3 * np.array([1, 0, 0])) / np.linalg.norm(0.85 * fwd - 0.35 * up + 0.3 * np.array([1, 0, 0]))
    hl = np.clip((r * 0.26 - np.linalg.norm(pos - h1, axis=-1)) / (r * 0.05), 0, 1)
    hl2 = np.clip((r * 0.16 - np.linalg.norm(pos - h2, axis=-1)) / (r * 0.04), 0, 1)
    e = base * (1 - hl[..., None]) + hl[..., None] * np.array([1, 1, 1])
    e = e * (1 - hl2[..., None]) + hl2[..., None] * np.array([0.95, 0.95, 1])
    col[eye] = e[eye]

out = np.concatenate([np.clip(col, 0, 1), np.ones((S, S, 1), np.float32)], axis=-1)
painted = bpy.data.images.new("MoleAlien_Albedo", S, S, alpha=False)
painted.pixels.foreach_set(out.astype(np.float32).ravel())
dst_dir = os.path.dirname(out_fbx)
painted.filepath_raw = os.path.join(dst_dir, "MoleAlien_Albedo.png"); painted.file_format = "PNG"; painted.save()
tex_node.image = painted

# ---------- cheeks (before splitting: vertex indices still match) ----------
for m in mesh.modifiers: m.show_render = m.show_viewport = True
me.color_attributes.remove(me.color_attributes["NoseW"])
eye_v = np.zeros(len(me.vertices), bool)
for c, r in eyes: eye_v |= (np.linalg.norm(co - c, axis=1) < r * 1.25) & (vert_lum < 0.3)
c, r = eyes[0]
dark_pts = co[dark & (np.sign(co[:, 0]) == np.sign(c[0]))]
eye_bottom = float(dark_pts[:, 2].min())
cx, cz = abs(float(dark_pts[:, 0].mean())), eye_bottom - 0.012 * H   # just under the round of the eye
# shoot rays from the front, straight under the eye's centre, and take the first skin hit below the eyeball
from mathutils.bvhtree import BVHTree
bvh = BVHTree.FromPolygons([v.co.copy() for v in me.vertices], [list(p.vertices) for p in me.polygons])
hit = None
for dz in np.arange(0.004, 0.08, 0.004) * H:
    loc, nrm_hit, fi, _ = bvh.ray_cast(Vector((cx * np.sign(c[0]), -2.0, eye_bottom - dz)), Vector((0, 1, 0)))
    if loc is None: continue
    if not any(eye_v[i] or nose_v[i] for i in me.polygons[fi].vertices):
        hit = (loc, nrm_hit); break
loc, nrm = hit
k = None
meta = {"eye": {"x": cx, "bottom": eye_bottom}, "cheek": {"x": abs(float(loc.x)), "z": float(loc.z), "front": float(-loc.y), "nx": abs(float(nrm.x)), "ny": float(nrm.z), "nz": float(-nrm.y)}}
json.dump(meta, open(out_meta, "w"), indent=1)

# ---------- split: MoleEyes (own colours), MoleNose (muted player colour), body (player colour) ----------
for name, sel in (("Sel_Nose", nose_v & ~eye_v), ("Sel_Eyes", eye_v)):
    g = mesh.vertex_groups.new(name=name)
    g.add([int(i) for i in np.where(sel)[0]], 1.0, "REPLACE")

def split(group, new_name):
    src = mesh
    gi = src.vertex_groups[group].index
    member = np.zeros(len(src.data.vertices), bool)
    for v in src.data.vertices:
        member[v.index] = any(gg.group == gi and gg.weight > 0.5 for gg in v.groups)
    for p in src.data.polygons: p.select = all(member[i] for i in p.vertices)
    for v in src.data.vertices: v.select = False
    for e in src.data.edges: e.select = False
    before = set(o.name for o in scene.objects)
    bpy.ops.object.select_all(action="DESELECT"); src.select_set(True); bpy.context.view_layer.objects.active = src
    bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.separate(type="SELECTED"); bpy.ops.object.mode_set(mode="OBJECT")
    new = next(o for o in scene.objects if o.name not in before)
    new.name = new_name
    return new

nose_obj = split("Sel_Nose", "MoleNose")
eyes_obj = split("Sel_Eyes", "MoleEyes")
for o in (mesh, nose_obj, eyes_obj):
    for g in ("Sel_Nose", "Sel_Eyes"):
        if g in o.vertex_groups: o.vertex_groups.remove(o.vertex_groups[g])
mesh.name = "MoleAlien"

# ---------- blush: small skinned quads on the skin, straight under each eye circle ----------
from mathutils.bvhtree import BVHTree as _BVH
body_bvh = _BVH.FromPolygons([v.co.copy() for v in mesh.data.vertices], [list(p.vertices) for p in mesh.data.polygons])
bverts, bfaces, bweights = [], [], []
BW, BH = 0.085 * H / 1.25, 0.045 * H / 1.25
for ec, er in eyes:
    # the visible eye circle is the separated MoleEyes mesh
    ev = np.array([v.co[:] for v in eyes_obj.data.vertices])
    side_pts = ev[np.sign(ev[:, 0]) == np.sign(ec[0])]
    ex, ebottom = float((side_pts[:, 0].min() + side_pts[:, 0].max()) / 2), float(side_pts[:, 2].min())
    print("EYE CIRCLE", round(ex, 3), "bottom", round(ebottom, 3), "top", round(float(side_pts[:, 2].max()), 3))
    # the eyeball bulges forward and hides the skin right under its mesh edge, so the spot that reads as
    # "just under the circle" from the front sits ~7.5 cm above the eye mesh's lowest vertex
    target_z = ebottom + 0.075 * H / 1.25
    ex += np.sign(ec[0]) * 0.035 * H / 1.25   # the visible circle sits a little outside the eye mesh middle
    loc, nrm, fi, _ = body_bvh.ray_cast(Vector((ex, -2.0, target_z)), Vector((0, 1, 0)))
    n = (Vector(nrm) + Vector((0, -1, 0))).normalized()          # face a bit more to the front
    t = Vector((0, 0, 1)).cross(n).normalized()                   # horizontal tangent
    u = n.cross(t).normalized()                                   # up along the surface
    centre = Vector(loc) + n * 0.004
    base = len(bverts)
    for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
        bverts.append(centre + t * (sx * BW / 2) + u * (sy * BH / 2))
    bfaces.append((base, base + 1, base + 2, base + 3))
    # skin weights of the nearest body vertex
    near = min(mesh.data.vertices, key=lambda v: (v.co - Vector(loc)).length)
    bweights.append([(mesh.vertex_groups[g.group].name, g.weight) for g in near.groups])
bm_mesh = bpy.data.meshes.new("MoleBlush")
bm_mesh.from_pydata([tuple(v) for v in bverts], [], bfaces)
bm_mesh.update()
uvl = bm_mesh.uv_layers.new(name="UVMap")
for poly in bm_mesh.polygons:
    for li, (uu, vv) in zip(poly.loop_indices, ((0, 0), (1, 0), (1, 1), (0, 1))): uvl.data[li].uv = (uu, vv)
blush_mat = bpy.data.materials.new("MoleBlush"); bm_mesh.materials.append(blush_mat)
blush = bpy.data.objects.new("MoleBlush", bm_mesh); scene.collection.objects.link(blush)
blush.parent = arm
for q, ws in enumerate(bweights):
    for name, w in ws:
        g = blush.vertex_groups.get(name) or blush.vertex_groups.new(name=name)
        g.add([q * 4 + k for k in range(4)], w, "REPLACE")
bmod = blush.modifiers.new("Armature", "ARMATURE"); bmod.object = arm
print("BLUSH", [tuple(round(c, 3) for c in v) for v in bverts[::4]])
print("SPLIT body", len(mesh.data.polygons), "nose", len(nose_obj.data.polygons), "eyes", len(eyes_obj.data.polygons))

bpy.ops.object.select_all(action="DESELECT")
for o in (arm, mesh, nose_obj, eyes_obj, blush): o.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(
    filepath=out_fbx, use_selection=True, object_types={"ARMATURE", "MESH"},
    apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
    add_leaf_bones=True, bake_anim=True, bake_anim_use_nla_strips=True, bake_anim_use_all_actions=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
    path_mode="COPY", embed_textures=False, mesh_smooth_type="FACE")
bpy.ops.wm.save_as_mainfile(filepath=out_fbx.replace(".fbx", "_painted.blend"))
print("PAINTED", json.dumps(meta))
