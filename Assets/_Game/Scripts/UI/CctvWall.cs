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
        public const int TileWidth = 160, TileHeight = 120;

        public Renderer[] Tiles;      // Columns * Rows quads, row-major from the top-left

        private RenderTexture atlas;
        private Camera shot;
        private int next;
        private readonly List<Material> materials = new();
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        private void Awake()
        {
            atlas = new RenderTexture(TileWidth * Columns, TileHeight * Rows, 16) { name = "CctvWall", filterMode = FilterMode.Point };
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
            for (var i = 0; i < Tiles.Length; i++)
            {
                if (Tiles[i] == null) { materials.Add(null); continue; }
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

        private void OnDestroy()
        {
            if (atlas != null) atlas.Release();
            foreach (var m in materials) if (m != null) Destroy(m);
        }

        private void Update()
        {
            var cams = CctvCamera.All;
            var stage = StageDirector.Instance;
            var me = Player.PlayerInteractor.Local;
            var inShip = me != null && me.IsSpawned && (stage == null || stage.InShip(me.transform.position));
            for (var i = 0; i < materials.Count; i++)
            {
                if (materials[i] == null) continue;
                var online = i < cams.Count && cams[i].Online;
                var flicker = 0.9f + Mathf.PerlinNoise(Time.time * 3f, i) * 0.2f;
                materials[i].SetColor(BaseColor, online ? new Color(0.8f, 1f, 0.85f) * flicker : new Color(0.05f, 0.12f, 0.08f));
            }
            if (!inShip || cams.Count == 0 || CctvView.Viewing) return;

            // Refresh one powered camera's tile per frame.
            for (var tries = 0; tries < cams.Count; tries++)
            {
                var i = next;
                next = (next + 1) % Mathf.Min(cams.Count, Columns * Rows);
                if (i >= cams.Count || !cams[i].Online || cams[i].Lens == null) continue;
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
