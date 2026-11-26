using System.Collections.Generic;
using CoOpHorror.Gameplay;
using CoOpHorror.Localization;
using CoOpHorror.Player;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace CoOpHorror.UI
{
    // Local presentation of replicated values. UI, damage labels and cosmetic fragments are pooled;
    // only Appliance on the host changes value, broken state, or socket/holder relationships.
    public sealed class ItemFeedback : MonoBehaviour
    {
        private static ItemFeedback instance;
        private sealed class Item
        {
            public Appliance App;
            public Renderer[] Renderers;
            public Text Label;
            public int Value;
            public bool Broken;
            public Bounds Bounds;
        }
        private sealed class Popup { public Text Text; public Vector3 Position; public float Start; }
        private sealed class Fragment { public Transform Transform, Model; public MeshFilter Mesh; public Renderer Renderer; public MaterialPropertyBlock Block; public Vector3 Velocity, Spin, Size; public float Until; }
        private sealed class Halo
        {
            public Interactable Target;
            public readonly List<(Renderer source, Renderer glow)> Parts = new();
        }
        private readonly List<Item> items = new();
        private readonly List<Halo> halos = new();
        private readonly Popup[] popups = new Popup[24];
        private readonly Fragment[] fragments = new Fragment[96];
        private int nextPopup, nextFragment;
        private Canvas canvas;
        private Material outline, shardMaterial;
        private MaterialPropertyBlock block;
        private readonly RaycastHit[] sight = new RaycastHit[16];
        public static void Ensure()
        {
            if (instance == null) new GameObject("ItemFeedback").AddComponent<ItemFeedback>();
        }
        private void Awake()
        {
            instance = this;
            var go = new GameObject("ItemValueCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 4;
            var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            block = new MaterialPropertyBlock();
            var shader = Resources.Load<Shader>("ChargeOutline");
            if (shader != null) outline = new Material(shader);
            var shardShader = Resources.Load<Shader>("FracturePiece");
            if (shardShader != null) shardMaterial = new Material(shardShader);
            for (var i = 0; i < popups.Length; i++) popups[i] = new Popup { Text = Label("ValueLoss", 32, Color.red) };
            for (var i = 0; i < fragments.Length; i++)
            {
                var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                part.name = "ItemFragmentMesh";
                var pivot = new GameObject("ItemFragment").transform; pivot.SetParent(transform);
                part.transform.SetParent(pivot, false);
                Destroy(part.GetComponent<Collider>()); part.SetActive(false);
                part.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                pivot.gameObject.SetActive(false);
                fragments[i] = new Fragment { Transform = pivot, Model = part.transform, Mesh = part.GetComponent<MeshFilter>(), Renderer = part.GetComponent<Renderer>(), Block = new MaterialPropertyBlock() };
            }
        }
        private Text Label(string name, int size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline));
            go.transform.SetParent(canvas.transform, false);
            var text = go.GetComponent<Text>(); text.font = GameText.NumberFont != null ? GameText.NumberFont : GameText.UiFont;
            text.fontSize = size; text.fontStyle = FontStyle.Bold; text.alignment = TextAnchor.MiddleCenter;
            text.color = color; text.raycastTarget = false;
            text.rectTransform.sizeDelta = new Vector2(240, 60);
            go.GetComponent<Outline>().effectColor = new Color(0, 0, 0, 0.85f);
            go.GetComponent<Outline>().effectDistance = new Vector2(2, -2);
            text.enabled = false; return text;
        }
        private static Bounds Measure(Item item)
        {
            var first = true; var result = new Bounds(item.App.transform.position, Vector3.one * 0.25f);
            foreach (var r in item.Renderers)
            {
                if (r == null) continue;
                if (first) { result = r.bounds; first = false; } else result.Encapsulate(r.bounds);
            }
            return result;
        }
        private void LateUpdate()
        {
            var camera = Camera.main;
            foreach (var app in Appliance.All)
            {
                if (app == null || !app.IsSpawned) continue;
                var found = false; foreach (var item in items) if (item.App == app) { found = true; break; }
                if (found) continue;
                var visible = new List<Renderer>();
                foreach (var r in app.GetComponentsInChildren<Renderer>())
                    if (r.enabled && r is MeshRenderer && !r.name.StartsWith("Cell") && r.name != "Stream") visible.Add(r);
                items.Add(new Item { App = app, Renderers = visible.ToArray(), Label = Label("ItemValue", 30, new Color(1f, 0.94f, 0.65f)), Value = app.Value.Value, Broken = app.Broken.Value });
            }
            for (var n = items.Count - 1; n >= 0; n--)
            {
                var item = items[n]; var app = item.App;
                if (app == null) { Destroy(item.Label.gameObject); items.RemoveAt(n); continue; }
                if (!app.IsSpawned) { item.Label.enabled = false; continue; }
                if (!app.Broken.Value) item.Bounds = Measure(item);
                if (app.Value.Value < item.Value)
                    Loss(item.Bounds.center + Vector3.up * item.Bounds.extents.y, item.Value - app.Value.Value);
                item.Value = app.Value.Value;
                if (app.Broken.Value && !item.Broken) Break(item);
                item.Broken = app.Broken.Value;
                item.Label.enabled = false;
                if (camera == null || app.IsGone || app.BaseValue <= 0 || CctvView.Viewing) continue;
                var at = item.Bounds.center + Vector3.up * (item.Bounds.extents.y + 0.15f);
                var screen = camera.WorldToScreenPoint(at);
                if (screen.z <= 0 || screen.z > 12f || screen.x < 0 || screen.x > Screen.width || screen.y < 0 || screen.y > Screen.height) continue;
                var offset = item.Bounds.center - camera.transform.position;
                var count = Physics.RaycastNonAlloc(camera.transform.position, offset.normalized, sight, offset.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                var blocked = false;
                for (var i = 0; i < count; i++)
                    if (!sight[i].transform.IsChildOf(app.transform) && sight[i].collider.GetComponentInParent<FirstPersonController>() == null) { blocked = true; break; }
                if (blocked) continue;
                var visibility = WorldSight.Visibility(item.Bounds.center);
                if (visibility < 0.08f) continue;
                item.Label.enabled = true; item.Label.text = GameText.Format("item.value", item.Value);
                item.Label.color = new Color(1f, 0.94f, 0.65f, visibility);
                item.Label.rectTransform.position = screen + Vector3.up * 52f;
                item.Label.fontSize = Mathf.RoundToInt(Mathf.Lerp(34f, 24f, Mathf.Clamp01(screen.z / 12f)));
            }
            foreach (var popup in popups)
            {
                var age = Time.time - popup.Start;
                popup.Text.enabled = camera != null && age >= 0f && age < 1f && !string.IsNullOrEmpty(popup.Text.text);
                if (!popup.Text.enabled) continue;
                var p = camera.WorldToScreenPoint(popup.Position + Vector3.up * age * 0.65f);
                popup.Text.enabled = p.z > 0f; popup.Text.rectTransform.position = p;
                popup.Text.color = new Color(1f, 0.16f, 0.1f, Mathf.Clamp01((1f - age) * 3f) * WorldSight.Visibility(popup.Position));
                popup.Text.rectTransform.localScale = Vector3.one * (1f + Mathf.Exp(-age * 14f) * 0.3f);
            }
            foreach (var fragment in fragments)
            {
                var remain = fragment.Until - Time.time;
                if (remain <= 0f) { fragment.Transform.gameObject.SetActive(false); continue; }
                fragment.Velocity += Vector3.down * (8f * Time.deltaTime);
                fragment.Transform.position += fragment.Velocity * Time.deltaTime;
                fragment.Transform.Rotate(fragment.Spin * Time.deltaTime);
                fragment.Transform.localScale = fragment.Size * Mathf.Clamp01(remain / 0.25f);
            }
            UpdateHalo();
        }
        private void Loss(Vector3 position, int loss)
        {
            var popup = popups[nextPopup++ % popups.Length]; popup.Position = position;
            popup.Start = Time.time; popup.Text.text = GameText.Format("item.loss", loss);
        }
        private void Break(Item item)
        {
            // GPU clipping partitions the item's original mesh into cells. It works on imported
            // meshes without CPU read/write copies, and retains their UVs instead of white cubes.
            MeshFilter source = null; Material surface = null; var biggest = -1f;
            foreach (var r in item.Renderers)
            {
                if (r == null) continue;
                var filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                var size = r.bounds.size; var volume = size.x * size.y * size.z;
                if (volume <= biggest) continue;
                source = filter; surface = r.sharedMaterial; biggest = volume;
            }
            if (source == null || shardMaterial == null) return;
            var local = source.sharedMesh.bounds;
            for (var i = 0; i < 12; i++)
            {
                var piece = fragments[nextFragment++ % fragments.Length];
                var cellSize = Vector3.Scale(local.size, new Vector3(0.5f, 0.5f, 1f / 3f));
                var min = local.min + Vector3.Scale(cellSize, new Vector3(i % 2, i / 2 % 2, i / 4));
                var max = min + cellSize; var centre = (min + max) * 0.5f;
                piece.Mesh.sharedMesh = source.sharedMesh;
                var materials = new Material[source.sharedMesh.subMeshCount];
                for (var n = 0; n < materials.Length; n++) materials[n] = shardMaterial;
                piece.Renderer.sharedMaterials = materials;
                piece.Block.Clear(); piece.Block.SetVector("_PieceMin", min); piece.Block.SetVector("_PieceMax", max);
                piece.Block.SetColor("_BaseColor", surface != null && surface.HasProperty("_BaseColor") ? surface.GetColor("_BaseColor") : Color.gray);
                var texture = surface != null && surface.HasProperty("_BaseMap") ? surface.GetTexture("_BaseMap") : Texture2D.whiteTexture;
                piece.Block.SetTexture("_BaseMap", texture != null ? texture : Texture2D.whiteTexture);
                piece.Renderer.SetPropertyBlock(piece.Block);
                piece.Transform.position = source.transform.TransformPoint(centre);
                piece.Transform.rotation = source.transform.rotation;
                piece.Size = source.transform.lossyScale;
                piece.Model.localPosition = -centre; piece.Model.localRotation = Quaternion.identity;
                piece.Model.localScale = Vector3.one;
                piece.Velocity = (piece.Transform.position - item.Bounds.center).normalized * Random.Range(1f, 2f) + Vector3.up * Random.Range(1.2f, 2.2f);
                piece.Spin = Random.insideUnitSphere * 220f; piece.Until = Time.time + 1.15f;
                piece.Transform.localScale = piece.Size;
                piece.Model.gameObject.SetActive(true); piece.Transform.gameObject.SetActive(true);
            }
            ProtoAudio.Play(ProtoSound.Shatter, item.Bounds.center, 0.8f);
            ProtoAudio.Play(ProtoSound.Pop, item.Bounds.center, 0.5f);
        }
        private void UpdateHalo()
        {
            var me = PlayerInteractor.Local;
            var target = me != null && !CctvView.Viewing && me.Target is IChargeGauge ? me.Target : null;
            if (target is Appliance app && app.IsGone) target = null;
            if (target != null && outline != null)
            {
                var exists = false; foreach (var halo in halos) if (halo.Target == target) { exists = true; break; }
                if (!exists)
                {
                    var halo = new Halo { Target = target };
                    foreach (var renderer in target.GetComponentsInChildren<MeshRenderer>())
                    {
                        var filter = renderer.GetComponent<MeshFilter>();
                        if (!renderer.enabled || filter == null || filter.sharedMesh == null || renderer.name.StartsWith("Cell") || renderer.name == "ChargeHalo") continue;
                        var go = new GameObject("ChargeHalo"); go.transform.SetParent(renderer.transform, false);
                        go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                        var glow = go.AddComponent<MeshRenderer>();
                        var materials = new Material[filter.sharedMesh.subMeshCount]; for (var i = 0; i < materials.Length; i++) materials[i] = outline;
                        glow.sharedMaterials = materials; glow.shadowCastingMode = ShadowCastingMode.Off;
                        glow.receiveShadows = false; halo.Parts.Add((renderer, glow));
                    }
                    halos.Add(halo);
                }
            }
            foreach (var halo in halos)
            foreach (var pair in halo.Parts)
            {
                if (pair.glow == null) continue;
                pair.glow.enabled = halo.Target != null && halo.Target == target && pair.source != null && pair.source.enabled;
            }
        }
        private void OnDestroy() { if (instance == this) instance = null; if (outline != null) Destroy(outline); if (shardMaterial != null) Destroy(shardMaterial); }
    }
}
