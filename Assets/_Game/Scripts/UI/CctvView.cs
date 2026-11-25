using System.Collections.Generic;
using CoOpHorror.Gameplay;
using CoOpHorror.Localization;
using CoOpHorror.Player;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace CoOpHorror.UI
{
    // Local CCTV viewing in the style of surveillance-room horror games.
    // One render camera draws the selected CCTV camera (lit by that camera's own lamp) into a low-res texture.
    // The CoOp/CctvFisheye material shows it on a curved CRT, both on the ship's monitor and full screen.
    // Full screen: the cursor is free. A map in the corner shows the ship and, as each camera is powered,
    // the route from the ship to it with a button to switch. Drag on the picture to pan/tilt the real camera
    // head, wheel zooms, A/D or 1-9 switch, Q / Tab / Esc / right click leave. Damage or death closes it.
    public sealed class CctvView : MonoBehaviour
    {
        public const int FeedWidth = 640;
        public const int FeedHeight = 480;
        public const float SourceHalfFovH = 66f;
        public const float DragSensitivity = 0.12f;
        public const float MonitorCycleSeconds = 3.5f;
        public const float SwitchStaticSeconds = 0.3f;
        public const float MinZoom = 1f;
        public const float MaxZoom = 3f;
        private const float MapWidth = 440f;
        private const float MapHeight = 330f;

        public static CctvView Instance { get; private set; }
        public static bool Viewing => Instance != null && Instance.viewing;

        public Material FeedMaterial;

        private RenderTexture feed;
        private Camera renderCamera;
        private CctvCamera rendering;
        private Canvas canvas;
        private CanvasGroup group;
        private RectTransform feedRect;
        private Material viewMaterial;
        private Material monitorMaterial;
        private Text nameText, timeText, zoomText, controlsText, centerText;
        private RectTransform mapContent;
        private readonly List<Button> mapButtons = new();
        private readonly List<Image> mapButtonImages = new();
        private readonly List<int> mapButtonCameras = new();
        private int mapMask = -1;
        private readonly Dictionary<CctvCamera, Vector3[]> routes = new();
        private bool viewing;
        private float enteredAt;
        private int enteredFrame;
        private int index;
        private float zoom = 1f;
        private float yaw, pitch;
        private bool dragging;
        private int lastHealth;
        private int monitorIndex = -1;
        private float monitorNext;
        private float staticUntil;

        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int ZoomId = Shader.PropertyToID("_Zoom");
        private static readonly int SignalId = Shader.PropertyToID("_Signal");
        private static readonly int StaticId = Shader.PropertyToID("_Static");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");
        private static readonly Color MapLine = new(0.75f, 0.8f, 0.78f, 0.9f);
        private static readonly Color MapIdle = new(0.25f, 0.27f, 0.27f, 0.95f);
        private static readonly Color MapSelected = new(0.45f, 0.75f, 0.2f, 1f);

        public int SelectedIndex => index;
        public int MapButtonCount => mapButtons.Count;

        private void Awake()
        {
            Instance = this;
            CctvPanel.OpenView = Enter;
            feed = new RenderTexture(FeedWidth, FeedHeight, 16) { name = "CctvFeed", filterMode = FilterMode.Point };

            var cameraObject = new GameObject("CctvRenderCamera");
            cameraObject.transform.SetParent(transform, false);
            renderCamera = cameraObject.AddComponent<Camera>();
            renderCamera.enabled = false;
            renderCamera.targetTexture = feed;
            renderCamera.fieldOfView = 2f * Mathf.Atan(Mathf.Tan(SourceHalfFovH * Mathf.Deg2Rad) * FeedHeight / FeedWidth) * Mathf.Rad2Deg;
            renderCamera.nearClipPlane = 0.05f;
            renderCamera.farClipPlane = 80f;
            renderCamera.clearFlags = CameraClearFlags.SolidColor;
            renderCamera.backgroundColor = Color.black;
            // Renderer 1 = the PC renderer without the PS1 pass, so dark rooms are not crushed to black
            // before the CRT shader lifts them (see ElecMoleLookSetup).
            var data = renderCamera.GetUniversalAdditionalCameraData();
            if (UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset != null &&
                UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset.rendererDataList.Length > CctvRendererIndex)
                data.SetRenderer(CctvRendererIndex);
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;

            if (FeedMaterial != null)
            {
                viewMaterial = MakeMaterial(Screen.width / (float)Mathf.Max(1, Screen.height));
                viewMaterial.SetFloat("_ZTest", (float)CompareFunction.Always);
            }
            BuildUi();
        }

        private void Start()
        {
            var panel = CctvPanel.Instance != null ? CctvPanel.Instance : FindFirstObjectByType<CctvPanel>();
            if (panel == null || panel.Screen == null || FeedMaterial == null) return;
            monitorMaterial = MakeMaterial(4f / 3f);
            panel.Screen.sharedMaterial = monitorMaterial;
        }

        private void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            if (viewing) Exit();
            if (Instance == this) Instance = null;
            if (CctvPanel.OpenView == (System.Func<bool>)Enter) CctvPanel.OpenView = null;
            if (feed != null) feed.Release();
            if (viewMaterial != null) Destroy(viewMaterial);
            if (monitorMaterial != null) Destroy(monitorMaterial);
        }

        // The CCTV camera's lamp only exists for the CCTV render, not for players walking around.
        private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (cam != renderCamera || rendering == null) return;
            if (rendering.Spot != null) rendering.Spot.enabled = true;
            if (rendering.Fill != null) rendering.Fill.enabled = true;
        }

        private void OnEndCamera(ScriptableRenderContext context, Camera cam)
        {
            if (cam != renderCamera || rendering == null) return;
            if (rendering.Spot != null) rendering.Spot.enabled = false;
            if (rendering.Fill != null) rendering.Fill.enabled = false;
        }

        private Material MakeMaterial(float aspect)
        {
            var material = new Material(FeedMaterial);
            material.SetTexture(MainTexId, feed);
            material.SetFloat("_SrcHalfTan", Mathf.Tan(SourceHalfFovH * Mathf.Deg2Rad));
            material.SetFloat("_SrcAspect", FeedWidth / (float)FeedHeight);
            material.SetFloat("_HalfAngle", SourceHalfFovH);
            material.SetFloat(AspectId, aspect);
            return material;
        }

        public const int CctvRendererIndex = 1;

        // Panel click (local only). Public for end-to-end tests.
        public bool Enter()
        {
            var me = PlayerHealth.Local;
            if (viewing || me == null || me.IsDead.Value || viewMaterial == null || CctvCamera.All.Count == 0) return false;
            viewing = true;
            enteredAt = Time.unscaledTime;
            enteredFrame = Time.frameCount;
            lastHealth = me.Health.Value;
            FirstPersonController.ViewLocked = true;
            FirstPersonController.SetCursorCaptured(false);
            var first = NextOnline(-1);
            Select(first >= 0 ? first : 0);
            canvas.enabled = true;
            ProtoAudio.Play(ProtoSound.Click, me.transform.position, 0.6f);
            return true;
        }

        public void Exit()
        {
            if (index >= 0 && index < CctvCamera.All.Count) CctvCamera.All[index].ReleaseLocalAim();
            viewing = false;
            dragging = false;
            canvas.enabled = false;
            FirstPersonController.ViewLocked = false;
            if (PlayerInteractor.Local != null) FirstPersonController.SetCursorCaptured(true);
        }

        public void Select(int next)
        {
            var cams = CctvCamera.All;
            if (cams.Count == 0) return;
            if (index >= 0 && index < cams.Count) cams[index].ReleaseLocalAim();
            index = ((next % cams.Count) + cams.Count) % cams.Count;
            yaw = cams[index].CurrentYaw;
            pitch = cams[index].CurrentPitch;
            zoom = 1f;
            staticUntil = Time.unscaledTime + SwitchStaticSeconds;
        }

        private int NextOnline(int from)
        {
            var cams = CctvCamera.All;
            for (var i = 1; i <= cams.Count; i++)
            {
                var j = ((from + i) % cams.Count + cams.Count) % cams.Count;
                if (cams[j].Online) return j;
            }
            return -1;
        }

        private void Update()
        {
            if (viewing) UpdateViewing();
        }

        private void UpdateViewing()
        {
            var cams = CctvCamera.All;
            var me = PlayerHealth.Local;
            var stage = StageDirector.Instance;
            var forced = me == null || me.IsDead.Value || me.Health.Value < lastHealth || cams.Count == 0
                || (stage != null && stage.IsSpawned && stage.Phase.Value >= StagePhase.Escaped);
            if (me != null) lastHealth = me.Health.Value;
            var k = Keyboard.current;
            var m = Mouse.current;
            var input = Time.frameCount > enteredFrame + 1 && Application.isFocused;
            var leave = input && ((m != null && m.rightButton.wasPressedThisFrame)
                || (k != null && (k.qKey.wasPressedThisFrame || k.tabKey.wasPressedThisFrame || k.escapeKey.wasPressedThisFrame)));
            if (forced || leave) { Exit(); return; }
            if (!input) return;

            if (k != null)
            {
                if (k.dKey.wasPressedThisFrame || k.rightArrowKey.wasPressedThisFrame) Select(index + 1);
                if (k.aKey.wasPressedThisFrame || k.leftArrowKey.wasPressedThisFrame) Select(index - 1);
                for (var n = 0; n < 9 && n < cams.Count; n++)
                    if (k[Key.Digit1 + n].wasPressedThisFrame) Select(n);
            }

            var cam = cams[index];
            if (m == null) return;
            // Drag on the picture (not on the map) to turn the camera head.
            if (m.leftButton.wasPressedThisFrame) dragging = EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject();
            if (!m.leftButton.isPressed) dragging = false;
            if (!cam.Online) return;
            if (dragging)
            {
                var delta = Vector2.ClampMagnitude(m.delta.ReadValue(), 250f) * (DragSensitivity / zoom);
                yaw = cam.ClampYaw(yaw - delta.x);
                pitch = cam.ClampPitch(pitch + delta.y);
            }
            var scroll = m.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f) zoom = Mathf.Clamp(zoom * (scroll > 0f ? 1.12f : 1f / 1.12f), MinZoom, MaxZoom);
            cam.LocalAim(yaw, pitch);
        }

        // After the camera heads turned this frame: place the render camera and update the overlay.
        private void LateUpdate()
        {
            var cams = CctvCamera.All;
            CctvCamera cam = null;
            if (viewing && index < cams.Count) cam = cams[index];
            else if (monitorMaterial != null && LocalAliveInShip() && cams.Count > 0)
            {
                if (Time.time >= monitorNext || monitorIndex < 0 || monitorIndex >= cams.Count || !cams[monitorIndex].Online)
                {
                    monitorNext = Time.time + MonitorCycleSeconds;
                    var next = NextOnline(monitorIndex);
                    if (next != monitorIndex) staticUntil = Time.unscaledTime + SwitchStaticSeconds;
                    monitorIndex = next;
                }
                cam = monitorIndex >= 0 ? cams[monitorIndex] : cams[0];
            }

            var live = cam != null && cam.Online && cam.Lens != null;
            rendering = live ? cam : null;
            renderCamera.enabled = live;
            if (live) renderCamera.transform.SetPositionAndRotation(cam.Lens.position, cam.Lens.rotation);
            var signal = live ? 1f : 0f;
            var switching = Mathf.Clamp01((staticUntil - Time.unscaledTime) / SwitchStaticSeconds);
            if (monitorMaterial != null)
            {
                monitorMaterial.SetFloat(SignalId, signal);
                monitorMaterial.SetFloat(StaticId, switching);
                monitorMaterial.SetFloat(ZoomId, viewing ? zoom : 1f);
            }
            if (viewMaterial != null)
            {
                viewMaterial.SetFloat(SignalId, signal);
                viewMaterial.SetFloat(StaticId, switching);
                viewMaterial.SetFloat(ZoomId, zoom);
                viewMaterial.SetFloat(AspectId, Screen.width / (float)Mathf.Max(1, Screen.height));
            }
            if (!viewing || cam == null) return;
            HudCanvas.Follow(canvas);
            // Lean in toward the monitor.
            var lean = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.unscaledTime - enteredAt) / 0.25f));
            group.alpha = lean;
            feedRect.localScale = Vector3.one * Mathf.Lerp(0.86f, 1f, lean);
            UpdateOverlay(cam, live);
            UpdateMap();
        }

        private static bool LocalAliveInShip()
        {
            var me = PlayerInteractor.Local;
            if (me == null || !me.IsSpawned) return false;
            var health = PlayerHealth.Local;
            if (health != null && health.IsDead.Value) return false;
            var stage = StageDirector.Instance;
            return stage == null || stage.InShip(me.transform.position);
        }

        private void UpdateOverlay(CctvCamera cam, bool live)
        {
            var rec = Mathf.PingPong(Time.unscaledTime, 1f) > 0.5f ? GameText.Get("cctv.rec") : "      ";
            nameText.text = rec + "  " + cam.DisplayName;
            timeText.text = System.DateTime.Now.ToString("HH:mm:ss");
            zoomText.text = GameText.Format("cctv.zoom", zoom.ToString("0.0"));
            centerText.text = live ? string.Empty : GameText.Get("cctv.noSignal");
        }

        // ---------- map ----------

        // Rebuilt whenever the set of powered cameras changes: the ship, a route to each powered camera
        // (orthogonal segments along the NavMesh path from the ship's door) and a button per camera.
        private void UpdateMap()
        {
            var cams = CctvCamera.All;
            var mask = 0;
            for (var i = 0; i < cams.Count && i < 30; i++) if (cams[i].Online) mask |= 1 << i;
            if (mask != mapMask)
            {
                mapMask = mask;
                RebuildMap();
            }
            for (var i = 0; i < mapButtons.Count; i++)
                mapButtonImages[i].color = mapButtonCameras[i] == index ? MapSelected : MapIdle;
        }

        private void RebuildMap()
        {
            foreach (Transform child in mapContent) Destroy(child.gameObject);
            mapButtons.Clear();
            mapButtonImages.Clear();
            mapButtonCameras.Clear();

            var stage = StageDirector.Instance;
            var cams = CctvCamera.All;
            var shipMin = stage != null ? stage.ShipMin : Vector3.zero;
            var shipMax = stage != null ? stage.ShipMax : Vector3.zero;
            var shipCenter = (shipMin + shipMax) * 0.5f;
            var door = stage != null && stage.ShipDoor != null ? stage.ShipDoor.transform.position : shipCenter;
            var outward = Vector3.ProjectOnPlane(door - shipCenter, Vector3.up).normalized;
            var exit = door + outward * 1.5f;

            var points = new List<Vector3> { shipMin, shipMax };
            var online = new List<int>();
            for (var i = 0; i < cams.Count; i++)
            {
                if (!cams[i].Online) continue;
                online.Add(i);
                if (!routes.TryGetValue(cams[i], out var route)) routes[cams[i]] = route = Route(exit, cams[i]);
                points.AddRange(route);
            }
            var min = points[0];
            var max = points[0];
            foreach (var p in points) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            var spanX = Mathf.Max(8f, max.x - min.x);
            var spanZ = Mathf.Max(8f, max.z - min.z);
            var inner = new Vector2(MapWidth - 70f, MapHeight - 70f);
            var scale = Mathf.Min(inner.x / spanX, inner.y / spanZ);
            var mid = (min + max) * 0.5f;
            Vector2 ToMap(Vector3 w) => new((w.x - mid.x) * scale, (w.z - mid.z) * scale);

            foreach (var i in online)
            {
                var route = routes[cams[i]];
                var previous = ToMap(door);
                foreach (var w in route)
                {
                    var next = ToMap(w);
                    var bend = new Vector2(next.x, previous.y);
                    MapLineSegment(previous, bend);
                    MapLineSegment(bend, next);
                    previous = next;
                }
            }
            // Ship box ("you are here").
            var shipA = ToMap(shipMin);
            var shipB = ToMap(shipMax);
            var ship = MapBox("Ship", (shipA + shipB) * 0.5f, new Vector2(Mathf.Max(70f, Mathf.Abs(shipB.x - shipA.x)), Mathf.Max(34f, Mathf.Abs(shipB.y - shipA.y))), MapSelected * 0.8f);
            MapLabel(ship, GameText.Get("cctv.mapShip"), 15);

            foreach (var i in online)
            {
                var node = MapBox("Cam_" + (i + 1), ToMap(cams[i].transform.position), new Vector2(64f, 30f), MapIdle);
                MapLabel(node, $"CAM {i + 1:00}", 15);
                var button = node.gameObject.AddComponent<Button>();
                var image = node.GetComponent<Image>();
                image.raycastTarget = true;
                button.targetGraphic = image;
                var camIndex = i;
                button.onClick.AddListener(() => Select(camIndex));
                mapButtonCameras.Add(i);
                mapButtons.Add(button);
                mapButtonImages.Add(image);
            }
        }

        // Ship door -> camera along walkable space, ending at the camera's wall position.
        private static Vector3[] Route(Vector3 exit, CctvCamera cam)
        {
            var target = cam.transform.position + cam.transform.forward * 1f;
            var path = new NavMeshPath();
            if (NavMesh.SamplePosition(exit, out var a, 3f, NavMesh.AllAreas) && NavMesh.SamplePosition(target, out var b, 3f, NavMesh.AllAreas)
                && NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.corners.Length > 0)
            {
                var corners = new List<Vector3>(path.corners) { cam.transform.position };
                return corners.ToArray();
            }
            return new[] { exit, cam.transform.position };
        }

        private RectTransform MapBox(string name, Vector2 position, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(mapContent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0.9f, 0.95f, 0.92f, 0.9f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            return rect;
        }

        private void MapLabel(RectTransform parent, string value, int size)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var text = go.GetComponent<Text>();
            text.font = GameText.UiFont;
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = value;
        }

        private void MapLineSegment(Vector2 a, Vector2 b)
        {
            var length = Vector2.Distance(a, b);
            if (length < 0.5f) return;
            var go = new GameObject("Line", typeof(RectTransform), typeof(Image));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(mapContent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = a;
            rect.sizeDelta = new Vector2(length, 2.5f);
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
            var image = go.GetComponent<Image>();
            image.color = MapLine;
            image.raycastTarget = false;
        }

        // ---------- UI ----------

        private void BuildUi()
        {
            var go = new GameObject("CctvCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -3; // under the stage HUD (health, battery stay visible)
            group = go.GetComponent<CanvasGroup>();
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440, 900);
            scaler.matchWidthOrHeight = 0.5f;

            var back = new GameObject("Black", typeof(RectTransform), typeof(Image));
            Stretch(back.GetComponent<RectTransform>(), canvas.transform);
            back.GetComponent<Image>().color = Color.black;
            back.GetComponent<Image>().raycastTarget = false;
            var imageGo = new GameObject("Feed", typeof(RectTransform), typeof(RawImage));
            feedRect = imageGo.GetComponent<RectTransform>();
            Stretch(feedRect, canvas.transform);
            var image = imageGo.GetComponent<RawImage>();
            image.texture = feed;
            image.material = viewMaterial;
            image.raycastTarget = false;

            var feedColor = new Color(0.86f, 0.95f, 0.9f);
            nameText = MakeText("Name", 24, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(330, -96), feedColor);
            zoomText = MakeText("Zoom", 16, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(330, -128), feedColor);
            timeText = MakeText("Time", 22, TextAnchor.UpperRight, new Vector2(1, 1), new Vector2(-120, -96), feedColor);
            centerText = MakeText("NoSignal", 30, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Color(1f, 0.45f, 0.35f));
            controlsText = MakeText("Controls", 16, TextAnchor.LowerLeft, new Vector2(0, 0), new Vector2(120, 70), new Color(0.85f, 0.92f, 0.88f, 0.85f));
            controlsText.text = GameText.Get("cctv.controls");

            // Map panel in the bottom-right corner.
            var map = new GameObject("Map", typeof(RectTransform), typeof(Image));
            var mapRect = map.GetComponent<RectTransform>();
            mapRect.SetParent(canvas.transform, false);
            mapRect.anchorMin = mapRect.anchorMax = mapRect.pivot = new Vector2(1f, 0f);
            mapRect.anchoredPosition = new Vector2(-110f, 80f);
            mapRect.sizeDelta = new Vector2(MapWidth, MapHeight);
            var mapImage = map.GetComponent<Image>();
            mapImage.color = new Color(0f, 0f, 0f, 0.55f);
            mapImage.raycastTarget = true; // clicks on the map never pan the camera
            var content = new GameObject("Content", typeof(RectTransform));
            mapContent = content.GetComponent<RectTransform>();
            mapContent.SetParent(mapRect, false);
            mapContent.anchorMin = mapContent.anchorMax = mapContent.pivot = new Vector2(0.5f, 0.5f);
            mapContent.anchoredPosition = new Vector2(0f, -8f);
            var title = MakeText("MapTitle", 14, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(10, -8), feedColor);
            title.rectTransform.SetParent(mapRect, false);
            title.text = GameText.Get("cctv.mapTitle");
            canvas.enabled = false;
        }

        private static void Stretch(RectTransform rect, Transform parent)
        {
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private Text MakeText(string name, int size, TextAnchor alignment, Vector2 anchor, Vector2 position, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(canvas.transform, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(900, 40);
            var text = go.GetComponent<Text>();
            text.font = GameText.UiFont;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            go.GetComponent<Outline>().effectColor = new Color(0, 0, 0, 0.8f);
            return text;
        }
    }
}
