using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace CoOpHorror.UI
{
    // An oblique perspective capture of the actual level. Camera-room coverage clips the display;
    // the sweep/filter is local presentation and never reveals unobserved player/monster positions.
    public sealed class RadarDisplay : MonoBehaviour
    {
        private Camera mapCamera;
        private Light mapLight;
        private RenderTexture texture;
        private Material material;
        private List<(Vector3 min, Vector3 max)> rooms;
        private readonly Vector4[] corners = new Vector4[36];
        private readonly List<(Renderer renderer, bool enabled)> roofs = new();
        private bool restoring, fog;
        private Color ambient;
        private AmbientMode ambientMode;
        public Camera MapCamera => mapCamera;
        private static Sprite ping;
        public static Sprite Ping
        {
            get
            {
                if (ping != null) return ping;
                var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
                for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
                {
                    var d = new Vector2((x - 15.5f) / 15.5f, (y - 15.5f) / 15.5f).magnitude;
                    tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.2f, 1f, d))));
                }
                tex.Apply(false, true);
                ping = Sprite.Create(tex, new Rect(0, 0, 32, 32), Vector2.one * 0.5f);
                return ping;
            }
        }
        private void Awake()
        {
            texture = new RenderTexture(1200, 800, 16) { name = "PerspectiveRadar", filterMode = FilterMode.Bilinear };
            var image = GetComponent<RawImage>(); image.texture = texture; image.raycastTarget = false;
            var shader = Resources.Load<Shader>("RadarCRT");
            if (shader != null) { material = new Material(shader); image.material = material; }
            var go = new GameObject("RadarMapCamera"); go.transform.SetParent(CctvView.Instance.transform, false);
            mapCamera = go.AddComponent<Camera>(); mapCamera.enabled = false;
            mapCamera.targetTexture = texture; mapCamera.fieldOfView = 55f; mapCamera.aspect = 1.5f;
            mapCamera.nearClipPlane = 0.2f; mapCamera.farClipPlane = 150f;
            mapCamera.clearFlags = CameraClearFlags.SolidColor; mapCamera.backgroundColor = Color.black;
            mapCamera.transform.position = new Vector3(0f, 52f, -30f);
            mapCamera.transform.LookAt(Vector3.zero);
            var data = mapCamera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.volumeLayerMask = 0;
            if (UniversalRenderPipeline.asset != null && UniversalRenderPipeline.asset.rendererDataList.Length > CctvView.CctvRendererIndex) data.SetRenderer(CctvView.CctvRendererIndex);
            var lightGo = new GameObject("RadarCaptureLight"); lightGo.transform.SetParent(go.transform, false);
            mapLight = lightGo.AddComponent<Light>(); mapLight.type = LightType.Directional;
            mapLight.color = Color.white; mapLight.intensity = 1.6f; mapLight.shadows = LightShadows.None; mapLight.enabled = false;
            RenderPipelineManager.beginCameraRendering += Begin;
            RenderPipelineManager.endCameraRendering += End;
        }
        public void SetRooms(List<(Vector3 min, Vector3 max)> watched) => rooms = watched;
        public Vector2 Project(Vector3 world)
        {
            var p = mapCamera.WorldToViewportPoint(world);
            var rect = (RectTransform)transform;
            return new Vector2((p.x - 0.5f) * rect.rect.width, (p.y - 0.5f) * rect.rect.height);
        }
        private void LateUpdate()
        {
            mapCamera.enabled = CctvView.Viewing && gameObject.activeInHierarchy;
            if (material == null || rooms == null) return;
            var count = Mathf.Min(rooms.Count, 9);
            for (var i = 0; i < count; i++)
            {
                var (min, max) = rooms[i];
                var a = mapCamera.WorldToViewportPoint(new Vector3(min.x, 0f, min.z));
                var b = mapCamera.WorldToViewportPoint(new Vector3(max.x, 0f, min.z));
                var c = mapCamera.WorldToViewportPoint(new Vector3(max.x, 0f, max.z));
                var d = mapCamera.WorldToViewportPoint(new Vector3(min.x, 0f, max.z));
                corners[i * 4] = new Vector4(a.x, a.y, 0, 0); corners[i * 4 + 1] = new Vector4(b.x, b.y, 0, 0);
                corners[i * 4 + 2] = new Vector4(c.x, c.y, 0, 0); corners[i * 4 + 3] = new Vector4(d.x, d.y, 0, 0);
            }
            material.SetVectorArray("_Rooms", corners); material.SetFloat("_RoomCount", count);
        }
        private void Begin(ScriptableRenderContext context, Camera camera)
        {
            if (camera != mapCamera) return;
            if (roofs.Count == 0)
                foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    if (renderer.name.StartsWith("Ceiling_") || renderer.name == "HullRoof" || renderer.name == "RibTop")
                        roofs.Add((renderer, renderer.enabled));
            foreach (var (renderer, enabled) in roofs) if (renderer != null) renderer.enabled = false;
            fog = RenderSettings.fog; ambient = RenderSettings.ambientLight; ambientMode = RenderSettings.ambientMode;
            restoring = true; RenderSettings.fog = false; RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.55f); mapLight.enabled = true;
        }
        private void End(ScriptableRenderContext context, Camera camera) { if (camera == mapCamera) Restore(); }
        private void Restore()
        {
            if (!restoring) return;
            foreach (var (renderer, enabled) in roofs) if (renderer != null) renderer.enabled = enabled;
            RenderSettings.fog = fog; RenderSettings.ambientLight = ambient; RenderSettings.ambientMode = ambientMode;
            if (mapLight != null) mapLight.enabled = false;
            restoring = false;
        }
        private void OnDisable() { Restore(); if (mapCamera != null) mapCamera.enabled = false; }
        private void OnDestroy()
        {
            Restore(); RenderPipelineManager.beginCameraRendering -= Begin; RenderPipelineManager.endCameraRendering -= End;
            if (texture != null) { texture.Release(); Destroy(texture); }
            if (mapCamera != null) Destroy(mapCamera.gameObject);
            if (material != null) Destroy(material);
        }
    }
}
