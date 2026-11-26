using System.Collections.Generic;
using CoOpHorror.Gameplay;
using CoOpHorror.Localization;
using CoOpHorror.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CoOpHorror.UI
{
    // While you charge something, the same segmented gauge as on the object appears big under the crosshair:
    // cells fill one by one with a pop. Local presentation only.
    public sealed class ChargeGaugeHud : MonoBehaviour
    {
        private readonly List<Image> cells = new();
        private readonly float[] pop = new float[ChargeGauge.Segments];
        private CanvasGroup group;
        private Text label;
        private int filled = -1;
        private float visible;
        private IChargeGauge last;
        private float lastFraction;
        private float lastChange;

        private void Awake()
        {
            var go = new GameObject("ChargeGaugeCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440, 900);
            scaler.matchWidthOrHeight = 0.5f;
            group = go.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;

            var row = new GameObject("Row", typeof(RectTransform)).GetComponent<RectTransform>();
            row.SetParent(go.transform, false);
            row.anchorMin = row.anchorMax = row.pivot = new Vector2(0.5f, 0.5f);
            row.anchoredPosition = new Vector2(0f, -90f);
            for (var i = 0; i < ChargeGauge.Segments; i++)
            {
                var cell = new GameObject("Cell" + i, typeof(RectTransform), typeof(Image), typeof(Outline));
                var rect = cell.GetComponent<RectTransform>();
                rect.SetParent(row, false);
                rect.sizeDelta = new Vector2(22f, 30f);
                rect.anchoredPosition = new Vector2((i - (ChargeGauge.Segments - 1) / 2f) * 28f, 0f);
                var image = cell.GetComponent<Image>();
                image.raycastTarget = false;
                cell.GetComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
                cells.Add(image);
            }
            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(Outline));
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.SetParent(row, false);
            labelRect.anchoredPosition = new Vector2(0f, -32f);
            labelRect.sizeDelta = new Vector2(400f, 26f);
            label = labelGo.GetComponent<Text>();
            label.font = GameText.NumberFont != null ? GameText.NumberFont : GameText.UiFont;
            label.fontSize = 20;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = ChargeGauge.FullColor;
            label.raycastTarget = false;
        }

        private void Update()
        {
            var me = PlayerInteractor.Local;
            var target = me != null ? me.Target as IChargeGauge : null;
            var mouse = Mouse.current;
            var holding = mouse != null && mouse.leftButton.isPressed && FirstPersonController.GameplayInputReady;
            var fraction = target != null ? Mathf.Clamp01(target.GaugeFraction) : 0f;
            if (!ReferenceEquals(target, last)) { last = target; filled = -1; lastFraction = fraction; lastChange = -10f; }
            if (!Mathf.Approximately(fraction, lastFraction)) { lastFraction = fraction; lastChange = Time.time; }
            var show = target != null && holding && (fraction < 1f || Time.time - lastChange < 0.8f) && Time.time - lastChange < 1.5f;
            visible = Mathf.MoveTowards(visible, show ? 1f : 0f, Time.deltaTime * 5f);
            group.alpha = visible;
            if (visible <= 0f) return;

            var now = ChargeGauge.FilledCells(fraction);
            if (filled >= 0 && now > filled)
                for (var i = filled; i < now; i++) pop[i] = 1f;
            filled = now;
            for (var i = 0; i < cells.Count; i++)
            {
                pop[i] = Mathf.MoveTowards(pop[i], 0f, Time.deltaTime * 6f);
                cells[i].rectTransform.localScale = Vector3.one * (1f + pop[i] * 0.6f);
                cells[i].color = i < now ? Color.Lerp(ChargeGauge.FullColor, Color.white, pop[i]) : ChargeGauge.EmptyColor;
            }
            label.text = Mathf.RoundToInt(fraction * 100f) + "%";
        }
    }
}
