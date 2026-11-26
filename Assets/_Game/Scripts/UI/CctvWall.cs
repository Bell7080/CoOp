using System.Collections.Generic;
using CoOpHorror.Gameplay;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CoOpHorror.UI
{
    // The status board on the wall behind the CCTV desk: two rows of small monitors, one per camera. One
    // tile is refreshed per frame (low resolution, each camera's lamp lit just for its shot); cameras without
    // power show a dim "no signal" green. Local presentation only, and only while you are in the ship.
    public sealed class CctvWall : MonoBehaviour
    {
        public const int Columns = 4, Rows = 2;
        public const int TileWidth = 320, TileHeight = 240;

        public Renderer[] Tiles;      // Columns * Rows quads, row-major from the top-left

        private RenderTexture atlas;
        private Camera shot;
        private int next;
        private readonly List<Material> materials = new();
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int RectId = Shader.PropertyToID("_Rect");
        private static readonly int SignalId = Shader.PropertyToID("_Signal");
        private static readonly int StaticId = Shader.PropertyToID("_Static");
        private bool feedLook;   // tiles use the CCTV monitor shader (same static as the desk monitor)

        private void Awake()
        {
            atlas = new RenderTexture(TileWidth * Columns, TileHeight * Rows, 16) { name = "CctvWall", filterMode = FilterMode.Bilinear };
            var go = new GameObject("CctvWallCamera");
            go.transform.SetParent(transform, false);
            shot = go.AddComponent<Camera>();
            shot.enabled = false;
            shot.targetTexture = atlas;
            shot.fieldOfView = 75f;
            shot.nearClipPlane = 0.05f;
            shot.farClipPlane = 40f;
            shot.clearFlags = CameraClearFlags.SolidColor;
            shot.backgroundColor = Color.black;
            var data = shot.GetUniversalAdditionalCameraData();
            if (UniversalRenderPipeline.asset != null && UniversalRenderPipeline.asset.rendererDataList.Length > CctvView.CctvRendererIndex)
                data.SetRenderer(CctvView.CctvRendererIndex);
            data.volumeLayerMask &= ~(1 << 29); // player viewing lens must not bend the preview feeds
            var feedMaterial = FindFirstObjectByType<CctvView>(FindObjectsInactive.Include) is { FeedMaterial: { } fm } ? fm : null;
            feedLook = feedMaterial != null;
            for (var i = 0; i < Tiles.Length; i++)
            {
                if (Tiles[i] == null) { materials.Add(null); continue; }
                if (feedLook)
                {
                    // Same CRT shader as the desk monitor, reading this tile's cell of the atlas.
                    var f = new Material(feedMaterial);
                    f.SetTexture(MainTex, atlas);
                    f.SetVector(RectId, new Vector4((i % Columns) / (float)Columns, (Rows - 1 - i / Columns) / (float)Rows, 1f / Columns, 1f / Rows));
                    f.SetFloat("_Aspect", TileWidth / (float)TileHeight);
                    f.SetFloat("_SrcAspect", TileWidth / (float)TileHeight);
                    f.SetFloat("_Lines", 120f);
                    Tiles[i].sharedMaterial = f;
                    materials.Add(f);
                    continue;
                }
                var m = new Material(Tiles[i].sharedMaterial);
                m.SetTexture(BaseMap, atlas);
                var col = i % Columns;
                var row = Rows - 1 - i / Columns; // texture rows start at the bottom
                m.SetTextureScale(BaseMap, new Vector2(1f / Columns, 1f / Rows));
                m.SetTextureOffset(BaseMap, new Vector2(col / (float)Columns, row / (float)Rows));
                Tiles[i].sharedMaterial = m;
                materials.Add(m);
            }
        }

        private void ClearTile(int i)
        {
            var col = i % Columns;
            var row = Rows - 1 - i / Columns;
            var previous = RenderTexture.active;
            RenderTexture.active = atlas;
            GL.PushMatrix();
            GL.Viewport(new Rect(col * atlas.width / (float)Columns, row * atlas.height / (float)Rows, atlas.width / (float)Columns, atlas.height / (float)Rows));
            GL.Clear(false, true, Color.black);
            GL.PopMatrix();
            RenderTexture.active = previous;
        }

        private void OnDestroy()
        {
            if (atlas != null) { atlas.Release(); Destroy(atlas); }
            foreach (var m in materials) if (m != null) Destroy(m);
        }

        private void Update()
        {
            var cams = CctvCamera.All;
            var stage = StageDirector.Instance;
            var me = Player.PlayerInteractor.Local;
            var view = Camera.main != null ? Camera.main.transform.position : (me != null ? me.transform.position : Vector3.zero);
            var inShip = me != null && me.IsSpawned && (stage == null || stage.InShip(view)); // ghosts watching too
            for (var i = 0; i < materials.Count; i++)
            {
                if (materials[i] == null) continue;
                var online = i < cams.Count && cams[i].Online;
                if (feedLook)
                {
                    // Drinking a camera's power fills its tile with the same static as the desk monitor.
                    materials[i].SetFloat(SignalId, online ? 1f : 0f);
                    materials[i].SetFloat(StaticId, online ? cams[i].FeedStatic : 1f);
                    continue;
                }
                // a drained camera's tile flickers harder and darker
                var weak = online ? cams[i].FeedStatic : 1f;
                var flicker = 0.9f + Mathf.PerlinNoise(Time.time * (3f + weak * 25f), i) * (0.2f + weak * 1.2f) - weak * 0.6f;
                materials[i].SetColor(BaseColor, online ? new Color(0.8f, 1f, 0.85f) * Mathf.Clamp(flicker, 0.1f, 1.1f) : new Color(0.05f, 0.12f, 0.08f));
            }
            if (!inShip || cams.Count == 0 || CctvView.Viewing) return;

            // Refresh one powered camera's tile per frame.
            for (var tries = 0; tries < cams.Count; tries++)
            {
                var i = next;
                next = (next + 1) % Mathf.Min(cams.Count, Columns * Rows);
                if (i >= cams.Count || cams[i].Lens == null) continue;
                if (!cams[i].Online) { ClearTile(i); continue; } // a dead camera's tile goes black, not frozen
                var cam = cams[i];
                shot.transform.SetPositionAndRotation(cam.Lens.position, cam.Lens.rotation);
                var col = i % Columns;
                var row = Rows - 1 - i / Columns;
                shot.rect = new Rect(col / (float)Columns, row / (float)Rows, 1f / Columns, 1f / Rows);
                if (cam.Spot != null) cam.Spot.enabled = true;
                if (cam.Fill != null) cam.Fill.enabled = true;
                shot.Render();
                if (cam.Spot != null) cam.Spot.enabled = false;
                if (cam.Fill != null) cam.Fill.enabled = false;
                break;
            }
        }
    }
}
