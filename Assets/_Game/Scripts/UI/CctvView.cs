using System.Text;
using CoOpHorror.Gameplay;
using CoOpHorror.Localization;
using CoOpHorror.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace CoOpHorror.UI
{
    // Local CCTV viewing. One render camera draws the selected CCTV camera into a low-res texture; the
    // CoOp/CctvFisheye material turns it into a curvilinear (five-point perspective) surveillance image,
    // both on the ship's monitor and in the full-screen view opened from the panel.
    // Full-screen view: mouse pans/tilts the real camera head, wheel zooms, A/D or 1-9 switch cameras,
    // right click / Q / Tab leave. Taking damage, dying or the ship leaving also closes it.
    public sealed class CctvView : MonoBehaviour
    {
        public const int FeedWidth = 640;
        public const int FeedHeight = 480;
        public const float SourceHalfFovH = 70f;
        public const float LookSensitivity = 0.05f;
        public const float MonitorCycleSeconds = 3.5f;
        public const float MinZoom = 1f;
        public const float MaxZoom = 3f;

        public static CctvView Instance { get; private set; }
        public static bool Viewing => Instance != null && Instance.viewing;

        public Material FeedMaterial;

        private RenderTexture feed;
        private Camera renderCamera;
        private Canvas canvas;
        private Material viewMaterial;
        private Material monitorMaterial;
        private Text nameText, timeText, zoomText, listText, controlsText, centerText;
        private bool viewing;
        private int index;
        private float zoom = 1f;
        private float yaw, pitch;
        private int enteredFrame;
        private int lastHealth;
        private int monitorIndex = -1;
        private float monitorNext;
        private readonly StringBuilder list = new();

        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int ZoomId = Shader.PropertyToID("_Zoom");
        private static readonly int SignalId = Shader.PropertyToID("_Signal");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");

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
            if (viewing) Exit();
            if (Instance == this) Instance = null;
            if (CctvPanel.OpenView == (System.Func<bool>)Enter) CctvPanel.OpenView = null;
            if (feed != null) feed.Release();
            if (viewMaterial != null) Destroy(viewMaterial);
            if (monitorMaterial != null) Destroy(monitorMaterial);
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

        // Panel click (local only).
        private bool Enter()
        {
            var me = PlayerHealth.Local;
            if (viewing || me == null || me.IsDead.Value || viewMaterial == null || CctvCamera.All.Count == 0) return false;
            viewing = true;
            enteredFrame = Time.frameCount;
            lastHealth = me.Health.Value;
            FirstPersonController.ViewLocked = true;
            var first = NextOnline(-1);
            Select(first >= 0 ? first : 0);
            canvas.enabled = true;
            ProtoAudio.Play(ProtoSound.Click, me.transform.position, 0.6f);
            return true;
        }

        private void Exit()
        {
            if (index >= 0 && index < CctvCamera.All.Count) CctvCamera.All[index].ReleaseLocalAim();
            viewing = false;
            FirstPersonController.ViewLocked = false;
            canvas.enabled = false;
        }

        private void Select(int next)
        {
            var cams = CctvCamera.All;
            if (index >= 0 && index < cams.Count) cams[index].ReleaseLocalAim();
            index = ((next % cams.Count) + cams.Count) % cams.Count;
            yaw = cams[index].CurrentYaw;
            pitch = cams[index].CurrentPitch;
            zoom = 1f;
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
            var input = Time.frameCount > enteredFrame && FirstPersonController.CursorCaptured;
            var leave = input && ((m != null && m.rightButton.wasPressedThisFrame) || (k != null && (k.qKey.wasPressedThisFrame || k.tabKey.wasPressedThisFrame)));
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
            if (!cam.Online || m == null) return;
            var delta = Vector2.ClampMagnitude(m.delta.ReadValue(), 250f) * (LookSensitivity / zoom);
            yaw = cam.ClampYaw(yaw + delta.x);
            pitch = cam.ClampPitch(pitch - delta.y);
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
                    monitorIndex = NextOnline(monitorIndex);
                }
                cam = monitorIndex >= 0 ? cams[monitorIndex] : cams[0];
            }

            var live = cam != null && cam.Online && cam.Lens != null;
            renderCamera.enabled = live;
            if (live) renderCamera.transform.SetPositionAndRotation(cam.Lens.position, cam.Lens.rotation);
            var signal = live ? 1f : 0f;
            if (monitorMaterial != null)
            {
                monitorMaterial.SetFloat(SignalId, signal);
                monitorMaterial.SetFloat(ZoomId, viewing ? zoom : 1f);
            }
            if (viewMaterial != null)
            {
                viewMaterial.SetFloat(SignalId, signal);
                viewMaterial.SetFloat(ZoomId, zoom);
                viewMaterial.SetFloat(AspectId, Screen.width / (float)Mathf.Max(1, Screen.height));
            }
            if (viewing && cam != null) UpdateOverlay(cam, live);
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
            timeText.text = System.DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss");
            zoomText.text = GameText.Format("cctv.zoom", zoom.ToString("0.0"));
            centerText.text = live ? string.Empty : GameText.Get("cctv.noSignal");

            list.Clear();
            var cams = CctvCamera.All;
            for (var i = 0; i < cams.Count; i++)
            {
                list.Append(i == index ? "[" : " ").Append(i + 1).Append(cams[i].Online ? "●" : "○").Append(i == index ? "]" : " ");
                if (i < cams.Count - 1) list.Append("  ");
            }
            listText.text = list.ToString();
        }

        // ---------- UI ----------

        private void BuildUi()
        {
            var go = new GameObject("CctvCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -3; // under the stage HUD (health, battery stay visible), no raycaster
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440, 900);
            scaler.matchWidthOrHeight = 0.5f;

            var backGo = new GameObject("Black", typeof(RectTransform), typeof(Image));
            Stretch(backGo.GetComponent<RectTransform>());
            backGo.GetComponent<Image>().color = Color.black;
            var imageGo = new GameObject("Feed", typeof(RectTransform), typeof(RawImage));
            Stretch(imageGo.GetComponent<RectTransform>());
            var image = imageGo.GetComponent<RawImage>();
            image.texture = feed;
            image.material = viewMaterial;
            image.raycastTarget = false;

            var feedColor = new Color(0.75f, 1f, 0.82f);
            nameText = MakeText("Name", 24, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(330, -90), feedColor);
            zoomText = MakeText("Zoom", 16, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(330, -122), feedColor);
            timeText = MakeText("Time", 20, TextAnchor.UpperRight, new Vector2(1, 1), new Vector2(-40, -90), feedColor);
            centerText = MakeText("NoSignal", 30, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Color(1f, 0.4f, 0.3f));
            listText = MakeText("List", 22, TextAnchor.LowerCenter, new Vector2(0.5f, 0), new Vector2(0, 74), feedColor);
            controlsText = MakeText("Controls", 16, TextAnchor.LowerCenter, new Vector2(0.5f, 0), new Vector2(0, 44), new Color(0.8f, 0.9f, 0.85f, 0.8f));
            controlsText.text = GameText.Get("cctv.controls");
            canvas.enabled = false;

            void Stretch(RectTransform rect)
            {
                rect.SetParent(canvas.transform, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
            }
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
