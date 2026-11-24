using System.Linq;
using System.Text;
using CoOpHorror.Localization;
using CoOpHorror.Player;
using UnityEngine;
using UnityEngine.UI;

namespace CoOpHorror.UI
{
    // Runtime-built HUD: crosshair, hit marker, own health, damage flash, controls hint and scoreboard.
    public sealed class CombatHud : MonoBehaviour
    {
        private Canvas canvas;
        private Image flash;
        private Text hitMarker;
        private Text healthText;
        private Text hintText;
        private Text centerText;
        private Text scoreText;
        private PlayerHealth bound;
        private float hitMarkerUntil;
        private float flashAlpha;
        private float nextScore;

        private void Awake()
        {
            var go = new GameObject("CombatHudCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -1; // under the room panel; no raycaster so it never blocks clicks
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440, 900);
            scaler.matchWidthOrHeight = 0.5f;

            flash = MakeImage("DamageFlash", new Color(0.8f, 0f, 0f, 0f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            MakeImage("CrosshairH", new Color(1, 1, 1, 0.85f), Center, Center, Vector2.zero, new Vector2(18, 2));
            MakeImage("CrosshairV", new Color(1, 1, 1, 0.85f), Center, Center, Vector2.zero, new Vector2(2, 18));
            hitMarker = MakeText("HitMarker", "×", 46, TextAnchor.MiddleCenter, Center, Center, new Vector2(0, 2), new Vector2(80, 80));
            hitMarker.color = new Color(1f, 0.25f, 0.2f, 0f);
            healthText = MakeText("Health", "", 30, TextAnchor.LowerLeft, Vector2.zero, Vector2.zero, new Vector2(28, 64), new Vector2(500, 50));
            hintText = MakeText("Controls", GameText.Get("hud.controls"), 17, TextAnchor.LowerCenter, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 18), new Vector2(1100, 30));
            hintText.color = new Color(0.85f, 0.92f, 0.95f, 0.8f);
            centerText = MakeText("CenterMessage", "", 26, TextAnchor.MiddleCenter, Center, Center, new Vector2(0, -120), new Vector2(1000, 60));
            scoreText = MakeText("Scoreboard", "", 18, TextAnchor.UpperRight, Vector2.one, Vector2.one, new Vector2(-24, -20), new Vector2(420, 240));
            PlayerCombat.LocalHitConfirmed += OnHitConfirmed;
            canvas.enabled = false;
        }

        private static readonly Vector2 Center = new(0.5f, 0.5f);

        private void OnDestroy()
        {
            PlayerCombat.LocalHitConfirmed -= OnHitConfirmed;
            Bind(null);
        }

        private void OnHitConfirmed() => hitMarkerUntil = Time.unscaledTime + 0.25f;

        private void OnHealthChanged(int previous, int current)
        {
            if (current < previous) flashAlpha = 0.45f;
        }

        private void Bind(PlayerHealth health)
        {
            if (bound == health) return;
            if (bound != null) bound.HealthChanged -= OnHealthChanged;
            bound = health;
            if (bound != null) bound.HealthChanged += OnHealthChanged;
        }

        private void Update()
        {
            Bind(PlayerHealth.Local);
            canvas.enabled = bound != null;
            if (bound == null) return;

            healthText.text = GameText.Format("hud.health", bound.Health.Value, PlayerHealth.MaxHealth);
            healthText.color = bound.Health.Value > 30 ? new Color(0.9f, 0.96f, 0.95f) : new Color(1f, 0.4f, 0.35f);
            centerText.text = bound.IsDead.Value ? GameText.Get("hud.dead") : FirstPersonController.CursorCaptured ? "" : GameText.Get("hud.cursorFree");

            var remaining = hitMarkerUntil - Time.unscaledTime;
            hitMarker.color = new Color(1f, 0.25f, 0.2f, Mathf.Clamp01(remaining / 0.25f));
            flashAlpha = Mathf.MoveTowards(flashAlpha, 0f, Time.unscaledDeltaTime * 1.6f);
            flash.color = new Color(0.8f, 0f, 0f, flashAlpha);

            if (Time.unscaledTime < nextScore) return;
            nextScore = Time.unscaledTime + 0.2f;
            var lines = new StringBuilder();
            foreach (var p in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None).OrderBy(x => x.OwnerClientId))
                lines.AppendLine(GameText.Format("hud.scoreLine", p.OwnerClientId, p == bound ? GameText.Get("hud.you") : "", p.Health.Value));
            scoreText.text = lines.ToString();
        }

        private Image MakeImage(string name, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = Place(go, anchorMin, anchorMax, position, size);
            if (anchorMin == Vector2.zero && anchorMax == Vector2.one) { rect.offsetMin = rect.offsetMax = Vector2.zero; }
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Text MakeText(string name, string value, int size, TextAnchor alignment, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 box)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline));
            var rect = Place(go, anchorMin, anchorMax, position, box);
            rect.pivot = anchorMin;
            var text = go.GetComponent<Text>();
            text.font = GameText.UiFont;
            text.text = value;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = new Color(0.9f, 0.96f, 0.95f);
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            go.GetComponent<Outline>().effectColor = new Color(0, 0, 0, 0.7f);
            return text;
        }

        private RectTransform Place(GameObject go, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
        {
            go.transform.SetParent(canvas.transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }
    }
}
