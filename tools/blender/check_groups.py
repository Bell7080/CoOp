import bpy
for ob in bpy.data.objects:
    if ob.type != "MESH": continue
    counts = {}
    for v in ob.data.vertices:
        for g in v.groups:
            if g.weight > 0.5:
                n = ob.vertex_groups[g.group].name
                counts[n] = counts.get(n, 0) + 1
    keys = [k for k in counts if k.startswith(("Jaw", "Finger0A", "Finger2B", "Body", "Thigh"))]
    print("GROUPS", ob.name, {k: counts[k] for k in sorted(keys)})
