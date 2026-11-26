using System.Collections.Generic;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // The look of telekinesis: a translucent, slowly churning low-poly gas shell wrapped around the held object,
    // a wide wobbling gas stream from each holder's paw, and a thin crackling current inside it.
    // Presentation only (driven by Grabbable on every peer).
    public sealed class TelekinesisAura : MonoBehaviour
    {
        private const int StreamPoints = 14;
        private static Material gasMaterial, sparkMaterial;
        private static Mesh baseShell;

        private Transform shell;
        private Mesh shellMesh;
        private Vector3[] baseVertices, vertices;
        private Renderer shellRenderer;
        private readonly List<LineRenderer> streams = new();
        private readonly List<LineRenderer> sparks = new();
        private readonly Vector3[] points = new Vector3[StreamPoints];
        private float strength;
        private float nextSpark;
        private Bounds localBounds;

        public static TelekinesisAura For(Transform target)
        {
            var aura = target.GetComponentInChildren<TelekinesisAura>();
            if (aura != null) return aura;
            var go = new GameObject("TelekinesisAura");
            go.transform.SetParent(target, false);
            return go.AddComponent<TelekinesisAura>();
        }

        private void Awake()
        {
            if (gasMaterial == null)
            {
                gasMaterial = new Material(Shader.Find("Sprites/Default")) { renderQueue = 3100 };
                sparkMaterial = new Material(Shader.Find("Sprites/Default")) { renderQueue = 3110 };
            }
            if (baseShell == null) baseShell = Icosphere();
            // Fit the shell around the object's renderers (local space of the parent).
            var parent = transform.parent;
            var first = true;
            foreach (var r in parent.GetComponentsInChildren<Renderer>())
            {
                if (r.transform.IsChildOf(transform) || r is LineRenderer) continue;
                var b = r.bounds;
                var c = parent.InverseTransformPoint(b.center);
                var e = parent.InverseTransformVector(b.extents);
                var lb = new Bounds(c, new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z)) * 2f);
                if (first) { localBounds = lb; first = false; } else localBounds.Encapsulate(lb);
            }
            if (first) localBounds = new Bounds(Vector3.up * 0.25f, Vector3.one * 0.5f);

            shell = new GameObject("GasShell").transform;
            shell.SetParent(transform, false);
            shell.localPosition = localBounds.center;
            shell.localScale = localBounds.size * 1.35f + Vector3.one * 0.12f;
            shellMesh = Instantiate(baseShell);
            baseVertices = shellMesh.vertices;
            vertices = new Vector3[baseVertices.Length];
            shell.gameObject.AddComponent<MeshFilter>().sharedMesh = shellMesh;
            shellRenderer = shell.gameObject.AddComponent<MeshRenderer>();
            shellRenderer.sharedMaterial = gasMaterial;
            shellRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shellRenderer.receiveShadows = false;
        }

        // from: each holder's paw position. Called every frame by the Grabbable.
        public void Show(IReadOnlyList<Vector3> from, float weightSag)
        {
            strength = Mathf.MoveTowards(strength, from.Count > 0 ? 1f : 0f, Time.deltaTime * (from.Count > 0 ? 6f : 3f));
            shellRenderer.enabled = strength > 0.01f;
            var t = Time.time;
            if (shellRenderer.enabled)
            {
                // Churning gas: low-poly vertices breathe in and out on slow noise; heavy things look strained.
                for (var i = 0; i < baseVertices.Length; i++)
                {
                    var v = baseVertices[i];
                    var n = Mathf.PerlinNoise(v.x * 2.1f + t * 1.3f, v.y * 2.1f + v.z * 1.7f - t * 0.9f);
                    vertices[i] = v * (0.86f + n * 0.3f + Mathf.Sin(t * 7f + i) * 0.015f * weightSag) * strength;
                }
                shellMesh.vertices = vertices;
                shellMesh.RecalculateNormals();
                var pulse = 0.16f + 0.06f * Mathf.Sin(t * 5.3f);
                shellRenderer.sharedMaterial.color = new Color(0.45f, 0.95f, 1f, pulse * strength);
            }

            while (streams.Count < from.Count) { streams.Add(Line(0.14f, 0.22f, gasMaterial)); sparks.Add(Line(0.025f, 0.012f, sparkMaterial)); }
            var centre = transform.parent.TransformPoint(localBounds.center);
            var sparkNow = t >= nextSpark;
            if (sparkNow) nextSpark = t + 0.05f;
            for (var s = 0; s < streams.Count; s++)
            {
                var on = s < from.Count && strength > 0.01f;
                streams[s].enabled = on;
                sparks[s].enabled = on && Random.value > 0.15f;
                if (!on) continue;
                var a = from[s];
                var side = Vector3.Cross((centre - a).normalized, Vector3.up).normalized;
                // gas stream: smooth wobble, sagging a little under the object's weight
                for (var p = 0; p < StreamPoints; p++)
                {
                    var f = p / (StreamPoints - 1f);
                    var bend = Mathf.Sin(f * Mathf.PI);
                    var wobble = side * (Mathf.Sin(t * 4f + f * 9f + s) * 0.08f) + Vector3.up * (Mathf.Cos(t * 3.1f + f * 7f) * 0.06f - 0.12f * weightSag);
                    points[p] = Vector3.Lerp(a, centre, f) + wobble * bend;
                }
                streams[s].SetPositions(points);
                streams[s].startColor = new Color(0.5f, 0.95f, 1f, 0.22f * strength);
                streams[s].endColor = new Color(0.6f, 1f, 1f, 0.12f * strength);
                if (!sparkNow) continue;
                // the current crackling inside the gas
                for (var p = 0; p < StreamPoints; p++)
                    if (p > 0 && p < StreamPoints - 1) points[p] += Random.insideUnitSphere * 0.07f * Mathf.Sin(p / (StreamPoints - 1f) * Mathf.PI);
                sparks[s].SetPositions(points);
                var flicker = 0.6f + Random.value * 0.4f;
                sparks[s].startColor = new Color(0.7f, 1f, 1f, flicker);
                sparks[s].endColor = new Color(1f, 1f, 1f, flicker * 0.7f);
            }
        }

        private LineRenderer Line(float start, float end, Material material)
        {
            var go = new GameObject("Stream");
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.positionCount = StreamPoints;
            line.useWorldSpace = true;
            line.startWidth = start;
            line.endWidth = end;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }

        // Low-poly sphere (icosahedron subdivided once), radius 0.5.
        private static Mesh Icosphere()
        {
            var t = (1f + Mathf.Sqrt(5f)) / 2f;
            var verts = new List<Vector3>
            {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t),
                new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
            };
            var tris = new List<int> { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
            var cache = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                var key = ((long)Mathf.Min(a, b) << 32) + Mathf.Max(a, b);
                if (cache.TryGetValue(key, out var i)) return i;
                verts.Add((verts[a] + verts[b]) * 0.5f);
                cache[key] = verts.Count - 1;
                return verts.Count - 1;
            }
            var next = new List<int>();
            for (var i = 0; i < tris.Count; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            for (var i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized * 0.5f;
            var mesh = new Mesh { name = "GasShell" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(next, 0);
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
