using CoOpHorror.Gameplay;
using CoOpHorror.Localization;
using CoOpHorror.Player;
using UnityEngine;
using UnityEngine.UI;

namespace CoOpHorror.UI
{
    // Runtime-built HUD for the ElecMole stage prototype. Reads replicated state only.
    // Top-left: health and stamina. Left: the nose's energy tank (W) and the team's money. Top: objective.
    // Centre: interaction prompt, skill-check ring, monster warning and stage banners.
    public sealed class PrototypeHud : MonoBehaviour
    {
        private const float BatteryHeight = 220f;
        private const float BarWidth = 260f;
        private const float RingSize = 150f;

        public static bool Visible { get; private set; }

        private Canvas canvas;
        private RectTransform healthFill, staminaFill, batteryFill;
        private Image staminaImage, batteryImage;
        private Text healthText, batteryText, flashlightText, inventoryText, radioText;
        private Text healthNumber, batteryNumber;
        private Text objectiveText, hintText, promptText, warningText, bannerText, reviveText;
        private GameObject ring;
        private Image ringBack, zone, great, needle;
        private Text skillText;
        private RectTransform promptRect;
        private CanvasGroup promptGroup, infoGroup;
        private Text infoText;
        private string shownPrompt = string.Empty, shownInfo = string.Empty;
        private Sprite ringSprite;

        private static readonly Vector2 Center = new(0.5f, 0.5f);
        private static readonly Vector2 Top = new(0.5f, 1f);
        private static readonly Vector2 TopLeft = new(0f, 1f);
        private static readonly Vector2 Left = new(0f, 0.5f);
        private static readonly Color TextColor = new(0.86f, 0.97f, 0.95f);
        private static readonly Color Electric = new(0.35f, 0.95f, 1f);
        private static readonly Color TankColor = new(1f, 0.78f, 0.3f);

        private void Awake()
        {
            var go = new GameObject("PrototypeHudCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -2; // under the room panel; no raycaster so it never blocks clicks
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440, 900);
            scaler.matchWidthOrHeight = 0.5f;
            ringSprite = MakeRingSprite(128, 0.74f, 0.98f);

            // Framed panels (CCTV-terminal look) behind the corner clusters.

            // Top-left: health and stamina.
            healthText = MakeText("HealthText", 12, TextAnchor.LowerLeft, TopLeft, new Vector2(24, -20), new Vector2(300, 20), new Vector2(0, 1));
            healthNumber = MakeNumber("HealthNumber", 24, TopLeft, new Vector2(BarWidth + 34, -36), new Vector2(0, 1));
            healthFill = MakeBar("Health", TopLeft, new Vector2(24, -44), new Vector2(BarWidth, 14), new Color(0.95f, 0.25f, 0.22f), out _);
            staminaFill = MakeBar("Stamina", TopLeft, new Vector2(24, -64), new Vector2(BarWidth, 8), new Color(0.75f, 0.95f, 0.35f), out staminaImage);

            // Left: the nose's energy tank (where the shared-power gauge used to be), money under it.
            batteryFill = MakeVerticalBar("Battery", Left, new Vector2(30, 20), new Vector2(28, BatteryHeight), TankColor, out batteryImage);
            batteryText = MakeText("BatteryText", 12, TextAnchor.UpperLeft, Left, new Vector2(24, 20 - BatteryHeight / 2f - 8), new Vector2(200, 24), new Vector2(0, 1));
            batteryNumber = MakeNumber("BatteryNumber", 24, Left, new Vector2(66, 20 + BatteryHeight / 2f - 4), new Vector2(0, 1));
            flashlightText = MakeText("FlashlightText", 14, TextAnchor.UpperLeft, Left, new Vector2(24, 20 - BatteryHeight / 2f - 30), new Vector2(240, 22), new Vector2(0, 1));
            inventoryText = MakeText("InventoryText", 15, TextAnchor.UpperLeft, Left, new Vector2(24, 20 - BatteryHeight / 2f - 56), new Vector2(320, 60), new Vector2(0, 1));
            radioText = MakeText("RadioText", 18, TextAnchor.UpperCenter, Top, new Vector2(0, -86), new Vector2(1000, 50), new Vector2(0.5f, 1));
            radioText.color = new Color(1f, 0.82f, 0.4f);

            // Top: objective and flow hint.
            objectiveText = MakeText("Objective", 21, TextAnchor.MiddleCenter, Top, new Vector2(0, -30), new Vector2(1000, 30), new Vector2(0.5f, 1));
            hintText = MakeText("Hint", 14, TextAnchor.MiddleCenter, Top, new Vector2(0, -60), new Vector2(1000, 22), new Vector2(0.5f, 1));
            hintText.color = new Color(0.75f, 0.85f, 0.88f, 0.7f);
            hintText.text = string.Empty; // no guide text

            // Centre.
            promptText = MakeText("Prompt", 21, TextAnchor.UpperCenter, Center, new Vector2(0, -44), new Vector2(1000, 70), Center);
            promptRect = promptText.rectTransform;
            // Bare, chunky key prompt: [ LEFT MOUSE ] (no plate, no description). Fades in and out.
            promptText.fontSize = 24;
            promptText.fontStyle = FontStyle.Bold;
            promptGroup = promptText.gameObject.AddComponent<CanvasGroup>();
            promptGroup.alpha = 0f;
            // What the aimed-at thing is and what it is worth, small, under the key prompt.
            infoText = MakeText("HoverInfo", 15, TextAnchor.UpperCenter, Center, new Vector2(0, -84), new Vector2(1000, 70), Center);
            infoText.color = new Color(0.86f, 0.97f, 0.95f, 0.85f);
            infoText.lineSpacing = 1.1f;
            infoGroup = infoText.gameObject.AddComponent<CanvasGroup>();
            infoGroup.alpha = 0f;
            warningText = MakeText("MonsterWarning", 24, TextAnchor.MiddleCenter, Center, new Vector2(0, 160), new Vector2(900, 40), Center);
            warningText.color = new Color(1f, 0.3f, 0.25f);
            warningText.text = GameText.Get("proto.monsterNear");
            bannerText = MakeText("Banner", 34, TextAnchor.MiddleCenter, Center, new Vector2(0, 110), new Vector2(1300, 60), Center);
            reviveText = MakeText("Revive", 20, TextAnchor.UpperCenter, Center, new Vector2(0, -175), new Vector2(900, 60), Center);
            reviveText.color = Electric;

            // Skill check ring.
            ring = new GameObject("SkillCheck", typeof(RectTransform));
            var ringRect = Place(ring, canvas.transform, Center, Vector2.zero, new Vector2(RingSize, RingSize));
            ringRect.pivot = Center;
            ringBack = MakeRingImage("Back", ring.transform, new Color(1f, 1f, 1f, 0.28f), false);
            zone = MakeRingImage("Zone", ring.transform, new Color(0.92f, 0.98f, 1f, 0.95f), true);
            great = MakeRingImage("Great", ring.transform, new Color(1f, 0.85f, 0.2f, 1f), true);
            var needleGo = new GameObject("Needle", typeof(RectTransform), typeof(Image));
            var needleRect = Place(needleGo, ring.transform, Center, Vector2.zero, new Vector2(4, RingSize / 2f + 4));
            needleRect.pivot = new Vector2(0.5f, 0f);
            needle = needleGo.GetComponent<Image>();
            needle.color = new Color(1f, 0.25f, 0.2f);
            needle.raycastTarget = false;
            skillText = MakeText("SkillResult", 22, TextAnchor.MiddleCenter, Center, new Vector2(0, RingSize / 2f + 22), new Vector2(400, 30), Center);
            ring.SetActive(false);

            canvas.enabled = false;
        }

        private void OnDestroy() => Visible = false;

        private void Update()
        {
            var me = PlayerInteractor.Local;
            HudCanvas.Follow(canvas);
            canvas.enabled = me != null && me.IsSpawned;
            Visible = canvas.enabled;
            if (!canvas.enabled) return;

            var health = PlayerHealth.Local;
            var motor = FirstPersonController.Local;
            var hp = health != null ? health.Health.Value : 0;
            healthText.text = GameText.Get("hud.healthLabel");
            healthNumber.text = hp.ToString("000");
            healthNumber.color = hp <= 30 ? new Color(1f, 0.35f, 0.3f) : TextColor;
            SetWidth(healthFill, BarWidth, hp / (float)PlayerHealth.MaxHealth);
            var stamina = motor != null ? motor.Stamina : 1f;
            SetWidth(staminaFill, BarWidth, stamina);
            staminaImage.color = motor != null && motor.Exhausted ? new Color(0.9f, 0.45f, 0.2f) : new Color(0.75f, 0.95f, 0.35f);

            var tank = me.GetComponent<PlayerNose>();
            var fraction = tank != null ? tank.Fraction : 0f;
            batteryFill.sizeDelta = new Vector2(batteryFill.sizeDelta.x, (BatteryHeight - 6) * fraction);
            var flow = tank != null && tank.IsSpawned ? tank.Flow.Value : PlayerNose.FlowNone;
            batteryImage.color = flow != PlayerNose.FlowNone ? Color.Lerp(TankColor, Color.white, Mathf.PingPong(Time.time * 4f, 0.5f))
                : tank != null && tank.Fraction >= 1f ? new Color(0.45f, 1f, 0.6f) : TankColor;
            batteryText.text = GameText.Get("hud.tankLabel");
            batteryNumber.text = (tank != null && tank.IsSpawned ? Mathf.FloorToInt(tank.Energy.Value) : 0).ToString("000"); // seven-segment digits only (the label says W)
            batteryNumber.color = batteryImage.color;
            var stage = StageDirector.Instance;
            flashlightText.text = GameText.Format("hud.money", stage != null && stage.IsSpawned ? stage.Money.Value : 0);
            flashlightText.color = new Color(1f, 0.85f, 0.4f);

            UpdateObjective(health);
            UpdateSkillCheck(me.Check);
            UpdateRadio(me);

            var wanted = FirstPersonController.CursorCaptured && !FirstPersonController.ViewLocked ? KeyPrompt(me) : string.Empty;
            if (!string.IsNullOrEmpty(wanted)) shownPrompt = wanted;    // keep the last text while fading out
            promptGroup.alpha = Mathf.MoveTowards(promptGroup.alpha, string.IsNullOrEmpty(wanted) ? 0f : 1f, Time.deltaTime * (string.IsNullOrEmpty(wanted) ? 6f : 10f));
            promptText.text = promptGroup.alpha > 0f ? shownPrompt : string.Empty;
            promptText.color = me.HoldingUse ? new Color(1f, 0.85f, 0.35f) : TextColor;
            promptRect.anchoredPosition = new Vector2(0, ring.activeSelf ? -RingSize / 2f - 30f : -44f);

            var info = FirstPersonController.CursorCaptured && !FirstPersonController.ViewLocked && me.Target != null && !ring.activeSelf
                ? me.Target.HoverInfo(me) : string.Empty;
            if (!string.IsNullOrEmpty(info)) shownInfo = info;
            infoGroup.alpha = Mathf.MoveTowards(infoGroup.alpha, string.IsNullOrEmpty(info) ? 0f : 1f, Time.deltaTime * (string.IsNullOrEmpty(info) ? 6f : 10f));
            infoText.text = infoGroup.alpha > 0f ? shownInfo : string.Empty;
            infoText.rectTransform.anchoredPosition = new Vector2(0, string.IsNullOrEmpty(wanted) ? -44f : -84f);


            var dead = health != null && health.IsDead.Value;
            reviveText.enabled = dead;
            if (dead)
            {
                var watching = FirstPersonController.SpectatingClient;
                var spectate = watching.HasValue ? GameText.Format("proto.spectating", watching.Value) : GameText.Get("proto.spectatingNobody");
                reviveText.text = spectate + "\n" + GameText.Format("proto.reviveProgress", health.RevivePercent);
            }

            var near = false;
            foreach (var monster in TempMonster.All)
                if (monster != null && monster.IsSpawned && monster.Chasing.Value
                    && (monster.transform.position - me.transform.position).sqrMagnitude < 16f * 16f) near = true;
            warningText.enabled = false; // no "monster near" text: you hear it
        }

        private void UpdateObjective(PlayerHealth health)
        {
            var stage = StageDirector.Instance;
            var transmitter = Transmitter.Instance;
            var phase = stage != null && stage.IsSpawned ? stage.Phase.Value : StagePhase.Exploring;
            var objective = string.Empty;
            var banner = string.Empty;
            var bannerColor = new Color(0.45f, 1f, 0.6f);

            switch (phase)
            {
                case StagePhase.Exploring:
                    var door = stage != null ? stage.ShipDoor : null;
                    if (door != null && door.IsSpawned && !door.Open.Value) objective = GameText.Get("proto.objectiveDoor");
                    else if (transmitter == null || !transmitter.IsSpawned) objective = string.Empty;
                    else if (transmitter.Phase.Value == TransmitterPhase.Charging) objective = GameText.Format("proto.objective", transmitter.StoredWatts, transmitter.RequiredWatts);
                    else
                    {
                        objective = GameText.Get("proto.settling");
                        banner = GameText.Get("proto.settlingBanner");
                        bannerColor = Electric;
                    }
                    break;
                case StagePhase.Blackout:
                    objective = GameText.Get("proto.objectiveReturn");
                    if (stage.InShip(PlayerInteractor.Local.transform.position)) objective = GameText.Get("proto.objectiveLaunch");
                    banner = Mathf.PingPong(Time.unscaledTime, 1f) > 0.3f ? GameText.Get("proto.blackout") : string.Empty;
                    bannerColor = new Color(1f, 0.35f, 0.25f);
                    break;
                case StagePhase.Launching:
                    objective = GameText.Format("proto.launching", Mathf.CeilToInt(stage.LaunchRemaining));
                    // the facility went dark with the transmitter: run back
                    if (!stage.InShip(PlayerInteractor.Local.transform.position))
                    {
                        banner = Mathf.PingPong(Time.unscaledTime, 1f) > 0.3f ? GameText.Format("proto.blackoutRun", Mathf.CeilToInt(stage.LaunchRemaining)) : string.Empty;
                        bannerColor = new Color(1f, 0.35f, 0.25f);
                    }
                    break;
                case StagePhase.Escaped:
                    var aboard = health != null && stage.IsAboard(health.OwnerClientId);
                    banner = aboard ? GameText.Format("proto.escaped", stage.Earned.Value) : GameText.Get("proto.leftBehind");
                    bannerColor = aboard ? new Color(0.45f, 1f, 0.6f) : new Color(1f, 0.35f, 0.25f);
                    break;
                case StagePhase.Defeat:
                    banner = GameText.Get("proto.defeat");
                    bannerColor = new Color(1f, 0.3f, 0.25f);
                    break;
            }
            objectiveText.text = string.Empty; // objectives are learned by playing, not read
            bannerText.text = banner;
            bannerText.color = bannerColor;
        }

        private void UpdateSkillCheck(SkillCheck check)
        {
            var showResult = Time.time - check.LastResultTime < SkillCheck.ResultShowTime;
            ring.SetActive(check.Visible || showResult);
            skillText.enabled = ring.activeSelf;
            if (!ring.activeSelf) return;

            var running = check.Current == SkillCheck.State.Running;
            zone.enabled = great.enabled = check.Visible;
            zone.fillAmount = SkillCheck.ZoneWidth / 360f;
            great.fillAmount = SkillCheck.GreatWidth / 360f;
            zone.rectTransform.localEulerAngles = great.rectTransform.localEulerAngles = new Vector3(0f, 0f, -check.ZoneStart);
            needle.enabled = check.Visible;
            needle.rectTransform.localEulerAngles = new Vector3(0f, 0f, -(running ? check.NeedleAngle : 0f));
            ringBack.color = check.Current == SkillCheck.State.Warning && Mathf.PingPong(Time.time * 8f, 1f) > 0.5f
                ? new Color(1f, 0.9f, 0.3f, 0.6f) : new Color(1f, 1f, 1f, 0.28f);

            if (check.Visible) { skillText.text = GameText.Get("proto.skillPress"); skillText.color = TextColor; return; }
            switch (check.LastResult)
            {
                case SkillCheckResult.Great: skillText.text = GameText.Get("proto.skillGreat"); skillText.color = new Color(1f, 0.85f, 0.2f); break;
                case SkillCheckResult.Good: skillText.text = GameText.Get("proto.skillGood"); skillText.color = TextColor; break;
                default: skillText.text = GameText.Get("proto.skillMiss"); skillText.color = new Color(1f, 0.3f, 0.25f); break;
            }
        }


        // Who is on the radio: what I send, and what I would hear over the radio.
        private void UpdateRadio(PlayerInteractor me)
        {
            var mine = me.GetComponent<PlayerRadio>();
            var lines = new System.Text.StringBuilder();
            if (mine != null && mine.IsSpawned && mine.Mode.Value != RadioMode.Off)
                lines.AppendLine(GameText.Get(mine.Mode.Value == RadioMode.ShipMic ? "proto.radioMic" : "proto.radioWalkie"));
            foreach (var p in PlayerHealth.All)
            {
                if (p == null || !p.TryGetComponent<PlayerRadio>(out var radio) || radio == mine || !radio.IsSpawned) continue;
                if (radio.Mode.Value != RadioMode.Off && VoiceRouting.HearsOverRadio(mine, radio, radio.Mode.Value))
                    lines.AppendLine(GameText.Format(radio.Mode.Value == RadioMode.ShipMic ? "proto.radioHearMic" : "proto.radioHearWalkie", p.OwnerClientId));
            }
            radioText.text = lines.ToString();
        }

        // ---------- building blocks ----------

        private static void SetWidth(RectTransform fill, float width, float fraction) =>
            fill.sizeDelta = new Vector2((width - 4) * Mathf.Clamp01(fraction), fill.sizeDelta.y);

        private RectTransform MakeBar(string name, Vector2 anchor, Vector2 position, Vector2 size, Color color, out Image fillImage)
        {
            var back = MakeImage(name + "Back", canvas.transform, new Color(0f, 0f, 0f, 0.6f), anchor, position, size, new Vector2(0, 1));
            var fill = MakeImage(name + "Fill", back.transform, color, new Vector2(0, 0.5f), new Vector2(2, 0), new Vector2(size.x - 4, size.y - 4), new Vector2(0, 0.5f));
            fillImage = fill;
            return fill.rectTransform;
        }

        private RectTransform MakeVerticalBar(string name, Vector2 anchor, Vector2 position, Vector2 size, Color color, out Image fillImage)
        {
            var back = MakeImage(name + "Back", canvas.transform, new Color(0f, 0f, 0f, 0.6f), anchor, position, size, new Vector2(0, 0.5f));
            var cap = MakeImage(name + "Cap", back.transform, new Color(0f, 0f, 0f, 0.6f), new Vector2(0.5f, 1f), new Vector2(0, 6), new Vector2(size.x * 0.45f, 6), new Vector2(0.5f, 1f));
            cap.rectTransform.pivot = new Vector2(0.5f, 0f);
            cap.rectTransform.anchoredPosition = Vector2.zero;
            var fill = MakeImage(name + "Fill", back.transform, color, new Vector2(0.5f, 0f), new Vector2(0, 3), new Vector2(size.x - 6, size.y - 6), new Vector2(0.5f, 0f));
            fillImage = fill;
            return fill.rectTransform;
        }

        private Image MakeRingImage(string name, Transform parent, Color color, bool filled)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = Place(go, parent, Center, Vector2.zero, new Vector2(RingSize, RingSize));
            rect.pivot = Center;
            var image = go.GetComponent<Image>();
            image.sprite = ringSprite;
            image.color = color;
            image.raycastTarget = false;
            if (filled)
            {
                image.type = Image.Type.Filled;
                image.fillMethod = Image.FillMethod.Radial360;
                image.fillOrigin = (int)Image.Origin360.Top;
                image.fillClockwise = true;
            }
            return image;
        }

        private Image MakeImage(string name, Transform parent, Color color, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = Place(go, parent, anchor, position, size);
            rect.pivot = pivot;
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Text MakeText(string name, int size, TextAnchor alignment, Vector2 anchor, Vector2 position, Vector2 box, Vector2 pivot)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline));
            var rect = Place(go, canvas.transform, anchor, position, box);
            rect.pivot = pivot;
            var text = go.GetComponent<Text>();
            text.font = GameText.UiFont;
            text.fontSize = GameText.PixelSize(size);
            text.alignment = alignment;
            text.color = TextColor;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            go.GetComponent<Outline>().effectColor = new Color(0, 0, 0, 0.75f);
            return text;
        }

        // Only the key, no explanation: [ LEFT MOUSE ], [ HOLD LEFT MOUSE ], [ HOLD E ].
        private static string KeyPrompt(PlayerInteractor me)
        {
            var keys = new System.Collections.Generic.List<string>();
            if (PlayerReviver.LocalLookingAtBody) keys.Add("[ HOLD E ]");
            if (me.Target is ShipMic) keys.Add("[ HOLD V ]");
            switch (me.TargetKind)
            {
                case InteractKind.Press:
                case InteractKind.PressOrHold: keys.Add("[ LEFT MOUSE ]"); break;
                case InteractKind.Hold: keys.Add("[ HOLD LEFT MOUSE ]"); break;
            }
            if (me.TargetKindRight == InteractKind.Hold) keys.Add("[ HOLD RIGHT MOUSE ]");
            if (keys.Count == 0 && me.HoldingUse) keys.Add("[ HOLD MOUSE ]");
            return string.Join("   ", keys);
        }

        // Seven-segment digits (DSEG7) for plain numbers.
        private Text MakeNumber(string name, int size, Vector2 anchor, Vector2 position, Vector2 pivot)
        {
            var text = MakeText(name, size, TextAnchor.UpperLeft, anchor, position, new Vector2(160, size + 8), pivot);
            if (GameText.NumberFont != null) text.font = GameText.NumberFont;
            text.fontSize = size;
            return text;
        }

        private void MakePanel(string name, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Outline));
            var rect = Place(go, canvas.transform, anchor, position, size);
            rect.pivot = pivot;
            go.transform.SetAsFirstSibling();
            var image = go.GetComponent<Image>();
            image.color = new Color(0.02f, 0.05f, 0.05f, 0.55f);
            image.raycastTarget = false;
            var outline = go.GetComponent<Outline>();
            outline.effectColor = new Color(0.35f, 0.95f, 1f, 0.35f);
            outline.effectDistance = new Vector2(2f, -2f);
        }

        private static RectTransform Place(GameObject go, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        // Anti-aliased ring texture generated at runtime (no art assets yet).
        private static Sprite MakeRingSprite(int size, float inner, float outer)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            var c = (size - 1) / 2f;
            var edge = 1.5f / c;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                var a = Mathf.Clamp01((d - inner) / edge) * Mathf.Clamp01((outer - d) / edge);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
