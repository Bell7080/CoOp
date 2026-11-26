using CoOpHorror.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace CoOpHorror.Gameplay
{
    // The monitor on the ship's transmitter: how much power this round needs, how much is in, and a bar.
    // Presentation only (every peer, from the replicated transmitter state).
    public sealed class TransmitterDisplay : MonoBehaviour
    {
        public const float PixelsPerMetre = 300f;

        public Transmitter Transmitter;
        public Transform[] Faces;                 // UI roots, each facing its -Z outwards
        public Vector2 FaceSize = new(1.1f, 0.6f);

        private Text[] status, target, stored;
        private RectTransform[] bars;
        private Image[] barImages;

        private static readonly Color Green = new(0.45f, 1f, 0.55f);
        private static readonly Color Amber = new(1f, 0.72f, 0.25f);
        private static readonly Color Dim = new(0.3f, 0.55f, 0.35f);
        private static readonly Color Blue = new(0.4f, 0.75f, 1f);

        private void Start()
        {
            var n = Faces.Length;
            status = new Text[n]; target = new Text[n]; stored = new Text[n];
            bars = new RectTransform[n]; barImages = new Image[n];
            for (var i = 0; i < n; i++) BuildFace(i);
        }

        private void BuildFace(int i)
        {
            var canvasGo = new GameObject("DisplayCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(Faces[i], false);
            canvasGo.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)canvasGo.transform;
            rect.sizeDelta = FaceSize * PixelsPerMetre;
            rect.localScale = Vector3.one / PixelsPerMetre;
            var w = FaceSize.x * PixelsPerMetre;
            var h = FaceSize.y * PixelsPerMetre;
            status[i] = Label(rect, new Rect(12, 8, w - 24, 26), 18, TextAnchor.MiddleLeft, Dim);
            target[i] = Label(rect, new Rect(12, 40, w - 24, 44), 30, TextAnchor.MiddleLeft, Amber);
            stored[i] = Label(rect, new Rect(12, 88, w - 24, 40), 26, TextAnchor.MiddleLeft, Green);
            var back = Block(rect, new Rect(12, h - 44, w - 24, 26), new Color(0.08f, 0.2f, 0.1f));
            barImages[i] = Block(back.rectTransform, new Rect(2, 2, w - 28, 22), Green);
            bars[i] = barImages[i].rectTransform;
        }

        private void Update()
        {
            if (status == null || Transmitter == null) return;
            var tx = Transmitter;
            var goal = Mathf.Max(1, tx.RequiredWatts);
            var value = tx.StoredWatts;
            var phase = tx.IsSpawned ? tx.Phase.Value : TransmitterPhase.Charging;
            string state;
            Color stateColor;
            switch (phase)
            {
                case TransmitterPhase.Charging:
                    state = GameText.Get(tx.IsFull ? "settle.ready" : "settle.charging");
                    stateColor = tx.IsFull ? Green : Blue;
                    break;
                case TransmitterPhase.Transmitting: state = GameText.Get("transmitter.firing"); stateColor = Green; break;
                default: state = GameText.Get("transmitter.done"); stateColor = Dim; break;
            }
            var reached = value >= goal;
            var w = FaceSize.x * PixelsPerMetre - 28f;
            for (var i = 0; i < status.Length; i++)
            {
                status[i].text = state;
                status[i].color = stateColor;
                target[i].text = GameText.Format("settle.target", goal);
                stored[i].text = reached
                    ? GameText.Format("settle.exchange", StageDirector.Instance != null && StageDirector.Instance.IsSpawned ? StageDirector.Instance.Money.Value : 0)
                    : GameText.Format("settle.stored", value);
                stored[i].color = reached ? Green : Color.Lerp(Dim, Green, 0.6f);
                bars[i].sizeDelta = new Vector2(w * Mathf.Clamp01(value / (float)goal), bars[i].sizeDelta.y);
                barImages[i].color = reached ? Green : Amber;
            }
        }

        private static RectTransform Place(GameObject go, Transform parent, Rect area)
        {
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(area.x, -area.y);
            r.sizeDelta = new Vector2(area.width, area.height);
            return r;
        }

        private static Text Label(Transform parent, Rect area, int size, TextAnchor anchor, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            Place(go, parent, area);
            var t = go.AddComponent<Text>();
            t.font = GameText.UiFont;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private static Image Block(Transform parent, Rect area, Color color)
        {
            var go = new GameObject("Bar", typeof(RectTransform));
            Place(go, parent, area);
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }
    }
}
