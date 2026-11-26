using System;
using System.Collections.Generic;
using CoOpHorror.Localization;
using CoOpHorror.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace CoOpHorror.Gameplay
{
    // The ship's supply terminal: a touch screen with the shop list (name, price) and the team's money. Aim at a
    // row and left-click to pick it; the screen asks "$40 - 구매하시겠습니까?" with 확인 / 취소 buttons. Confirming
    // asks the host, which checks range, the team's money and that the station is free, takes the money and sends
    // the item to the station out in the facility.
    // The money is replicated; which page each player sees is local (everyone taps the screen for themselves).
    public sealed class SupplyTerminal : Interactable
    {
        public const float MessageSeconds = 2.4f;
        public const float PixelsPerMetre = 500f;

        private enum Page { List, Confirm, Message }
        private enum BuyResult : byte { Ok, NoMoney, Busy, TooFar }

        public static SupplyTerminal Instance { get; private set; }

        public Collider ScreenHit;                 // the touch surface (other colliders just block the view)
        public Transform ScreenRoot;               // UI is built here, facing local -Z of this transform
        public Vector2 ScreenSize = new(0.62f, 0.46f);


        private sealed class Button
        {
            public RectTransform Rect;
            public Image Back;
            public Text Label;
            public Func<bool> Enabled;
            public Action Click;
        }

        public const int VisibleRows = 5;      // the list scrolls (mouse wheel) when a tab holds more
        private const float RowsTop = 70f, RowStep = 27f;

        private Page page;
        private int selected;
        private int tab;                       // index into SupplyStation.Categories
        private int scroll;                    // first visible row of the current tab
        private float messageUntil;
        private string message = string.Empty;
        private bool messageBad;
        private Button hovered;

        private RectTransform canvasRect;
        private GameObject listPage, confirmPage, messagePage;
        private Text creditsText, footerText, confirmText, messageText;
        private readonly List<Button> listButtons = new();   // one per catalog item (same index)
        private readonly List<Button> tabButtons = new();
        private readonly List<Button> hoverable = new();
        private RectTransform scrollTrack, scrollThumb;
        private readonly List<Button> confirmButtons = new();

        private static readonly Color Green = new(0.45f, 1f, 0.55f);
        private static readonly Color Dim = new(0.25f, 0.5f, 0.3f);
        private static readonly Color Amber = new(1f, 0.72f, 0.25f);
        private static readonly Color Red = new(1f, 0.35f, 0.3f);
        private static readonly Color RowBack = new(0.1f, 0.3f, 0.14f, 0.35f);
        private static readonly Color RowHover = new(0.35f, 1f, 0.45f, 0.35f);

        protected override void Awake()
        {
            base.Awake();
            Instance = this;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        // Purchases are paid with the team's money (power exchanged at the transmitter).
        private static int Money => StageDirector.Instance != null && StageDirector.Instance.IsSpawned ? StageDirector.Instance.Money.Value : 0;

        private void Start() => BuildScreen();

        public override bool TargetedBy(Collider c) => c == ScreenHit;

        public override InteractKind Describe(PlayerInteractor who, out string prompt)
        {
            prompt = string.Empty;
            return hovered != null && hovered.Enabled() ? InteractKind.Press : InteractKind.None;
        }

        // Taps are handled on the screen locally; only a confirmed purchase goes to the host.
        public override bool OwnerUse(PlayerInteractor who)
        {
            if (hovered != null && hovered.Enabled()) hovered.Click();
            return true;
        }

        // ---------------- host ----------------

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void BuyRpc(int index, RpcParams rpcParams = default)
        {
            var sender = rpcParams.Receive.SenderClientId;
            ResultRpc(sender, (byte)ServerBuy(sender, index), index);
        }

        private BuyResult ServerBuy(ulong sender, int index)
        {
            if (index < 0 || index >= SupplyStation.Catalog.Length) return BuyResult.Busy;
            var who = NetworkManager.ConnectedClients.TryGetValue(sender, out var client) && client.PlayerObject != null
                ? client.PlayerObject.GetComponent<PlayerInteractor>() : null;
            var health = who != null ? who.GetComponent<PlayerHealth>() : null;
            if (who == null || (health != null && health.IsDead.Value) || !ServerInRange(who)) return BuyResult.TooFar;
            var station = SupplyStation.Instance;
            if (station == null || !station.IsSpawned || station.IsBusy) return BuyResult.Busy;
            var item = SupplyStation.Catalog[index];
            if (!station.Available(item)) return BuyResult.Busy;
            var price = SupplyStation.Price(item);
            var stage = StageDirector.Instance;
            if (stage == null || stage.Money.Value < price) return BuyResult.NoMoney;
            if (!station.ServerRequest(item)) return BuyResult.Busy;
            stage.ServerSpend(price);
            ProtoAudio.Play(ProtoSound.Click, transform.position + Vector3.up, 1f);
            return BuyResult.Ok;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ResultRpc(ulong client, byte result, int index)
        {
            if (NetworkManager.LocalClientId != client) return;
            var name = index >= 0 && index < SupplyStation.Catalog.Length ? GameText.Get(SupplyStation.NameKey(SupplyStation.Catalog[index])) : string.Empty;
            switch ((BuyResult)result)
            {
                case BuyResult.Ok: ShowMessage(GameText.Format("shop.bought", name), false); break;
                case BuyResult.NoMoney: ShowMessage(GameText.Get("shop.noMoney"), true); break;
                case BuyResult.Busy: ShowMessage(GameText.Get("shop.busy"), true); break;
                default: ShowMessage(GameText.Get("shop.tooFar"), true); break;
            }
        }

        // ---------------- local screen ----------------

        private void Pick(int index)
        {
            selected = index;
            var station = SupplyStation.Instance;
            if (station != null && station.IsBusy) { ShowMessage(GameText.Get("shop.busy"), true); return; }
            if (station != null && !station.Available(SupplyStation.Catalog[index])) { ShowMessage(GameText.Get("shop.soldOut"), true); return; }
            page = Page.Confirm;
            Beep(1.2f);
        }

        private void Confirm()
        {
            var price = SupplyStation.Price(SupplyStation.Catalog[selected]);
            if (Money < price) { ShowMessage(GameText.Get("shop.noMoney"), true); return; }
            ShowMessage(GameText.Get("shop.ordering"), false);
            messageUntil = Time.time + 10f; // until the host answers
            BuyRpc(selected);
            Beep(1.5f);
        }

        private void Cancel()
        {
            page = Page.List;
            Beep(0.8f);
        }

        private void ShowMessage(string text, bool bad)
        {
            message = text;
            messageBad = bad;
            messageUntil = Time.time + MessageSeconds;
            page = Page.Message;
            if (bad) Beep(0.6f);
        }

        private void Beep(float pitch) => ProtoAudio.PlayPitched(ProtoSound.Beep, transform.position + Vector3.up * 1.4f, 0.35f, pitch);

        private void Update()
        {
            if (canvasRect == null) return;
            if (page == Page.Message && Time.time >= messageUntil) page = Page.List;
            UpdateHover();
            UpdateScroll();
            Refresh();
        }

        // Items of the current tab, in catalog order.
        private List<int> TabItems()
        {
            var items = new List<int>();
            var category = SupplyStation.Categories[tab];
            for (var i = 0; i < SupplyStation.Catalog.Length; i++)
                if (SupplyStation.Category(SupplyStation.Catalog[i]) == category) items.Add(i);
            return items;
        }

        // Mouse wheel over the screen scrolls the list.
        private void UpdateScroll()
        {
            var me = PlayerInteractor.Local;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            var max = Mathf.Max(0, TabItems().Count - VisibleRows);
            if (page == Page.List && me != null && me.Target == this && mouse != null && FirstPersonController.GameplayInputReady)
            {
                var wheel = mouse.scroll.ReadValue().y;
                if (wheel > 0.01f) scroll--;
                else if (wheel < -0.01f) scroll++;
            }
            scroll = Mathf.Clamp(scroll, 0, max);
        }

        private void SelectTab(int index)
        {
            if (tab == index) return;
            tab = index;
            scroll = 0;
            Beep(1.1f);
        }

        // Where the local player's crosshair touches the screen.
        private void UpdateHover()
        {
            hovered = null;
            var me = PlayerInteractor.Local;
            if (me == null || !me.IsSpawned || me.Target != this || ScreenHit == null) return;
            var cam = Camera.main;
            var ray = cam != null && cam.transform.IsChildOf(me.transform)
                ? cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                : new Ray(me.EyePosition, me.AimForward);
            if (!ScreenHit.Raycast(ray, out var hit, UseRange + 0.5f)) return;
            hoverable.Clear();
            if (page == Page.List) { hoverable.AddRange(tabButtons); hoverable.AddRange(listButtons); }
            else if (page == Page.Confirm) hoverable.AddRange(confirmButtons);
            var buttons = hoverable;
            foreach (var b in buttons)
                if (b.Rect.gameObject.activeInHierarchy && b.Rect.rect.Contains(b.Rect.InverseTransformPoint(hit.point))) { hovered = b; break; }
        }

        private void Refresh()
        {
            creditsText.text = GameText.Format("shop.credits", Money);
            listPage.SetActive(page == Page.List);
            confirmPage.SetActive(page == Page.Confirm);
            messagePage.SetActive(page == Page.Message);

            LayoutList();
            foreach (var b in listButtons) Paint(b);
            foreach (var b in confirmButtons) Paint(b);
            for (var i = 0; i < tabButtons.Count; i++)
            {
                var b = tabButtons[i];
                b.Back.color = i == tab ? new Color(0.3f, 0.85f, 0.4f, 0.55f) : b == hovered ? RowHover : RowBack;
                b.Label.color = i == tab ? Color.black : Green;
            }

            if (page == Page.List)
            {
                var station = SupplyStation.Instance;
                string footer;
                var focus = listButtons.IndexOf(hovered);
                if (focus >= 0) footer = GameText.Get(SupplyStation.EffectKey(SupplyStation.Catalog[focus]));
                else if (station != null && station.IsSpawned && station.Phase.Value == SupplyPhase.Incoming) footer = GameText.Get("supply.sending");
                else if (station != null && station.IsSpawned && station.Phase.Value == SupplyPhase.Open) footer = GameText.Get("supply.waiting");
                else footer = GameText.Get("shop.hint");
                footerText.text = footer;
                footerText.color = station != null && station.IsBusy && focus < 0 ? Amber : Dim;
            }
            else if (page == Page.Confirm)
            {
                var item = SupplyStation.Catalog[selected];
                confirmText.text = GameText.Get(SupplyStation.NameKey(item)) + "\n$" + SupplyStation.Price(item) + "\n" + GameText.Get("shop.confirm");
            }
            else
            {
                messageText.text = message;
                messageText.color = messageBad ? Red : Green;
            }
        }

        // Only the current tab's items, VisibleRows at a time from the scroll position; a scrollbar when it overflows.
        private void LayoutList()
        {
            foreach (var b in listButtons) b.Rect.gameObject.SetActive(false);
            var items = TabItems();
            for (var k = 0; k < items.Count; k++)
            {
                var row = k - scroll;
                if (row < 0 || row >= VisibleRows) continue;
                var b = listButtons[items[k]];
                b.Rect.gameObject.SetActive(true);
                b.Rect.anchoredPosition = new Vector2(b.Rect.anchoredPosition.x, -(RowsTop + row * RowStep));
            }
            var overflow = items.Count > VisibleRows;
            scrollTrack.gameObject.SetActive(overflow);
            if (!overflow) return;
            var track = VisibleRows * RowStep - 2f;
            var thumb = track * VisibleRows / items.Count;
            scrollThumb.sizeDelta = new Vector2(scrollThumb.sizeDelta.x, thumb);
            scrollThumb.anchoredPosition = new Vector2(0f, -(track - thumb) * scroll / Mathf.Max(1, items.Count - VisibleRows));
        }

        private void Paint(Button b)
        {
            var on = b.Enabled();
            b.Back.color = b == hovered && on ? RowHover : RowBack;
            b.Label.color = on ? Green : Dim;
        }

        // ---------------- building the screen (every peer, at runtime) ----------------

        private void BuildScreen()
        {
            var root = ScreenRoot != null ? ScreenRoot : transform;
            var canvasGo = new GameObject("ShopCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(root, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasRect = (RectTransform)canvasGo.transform;
            canvasRect.sizeDelta = ScreenSize * PixelsPerMetre;
            canvasRect.localScale = Vector3.one / PixelsPerMetre;
            var w = ScreenSize.x * PixelsPerMetre;
            var h = ScreenSize.y * PixelsPerMetre;

            // header: shop name and the team's money
            Label(canvasRect, "Title", new Rect(10, 6, w - 20, 26), 17, TextAnchor.MiddleLeft, Green).text = GameText.Get("shop.title");
            creditsText = Label(canvasRect, "Credits", new Rect(10, 6, w - 20, 26), 17, TextAnchor.MiddleRight, Amber);
            Block(canvasRect, "Rule", new Rect(8, 34, w - 16, 2), Dim);

            listPage = Panel("ListPage");
            // tabs (categories) across the top of the list
            var tabs = SupplyStation.Categories.Length;
            var tabW = (w - 16 - (tabs - 1) * 4f) / tabs;
            for (var t = 0; t < tabs; t++)
            {
                var tabIndex = t;
                var tb = MakeButton(listPage.transform, "Tab" + t, new Rect(8 + t * (tabW + 4f), 40, tabW, 24), 14, TextAnchor.MiddleCenter, () => true, () => SelectTab(tabIndex));
                tb.Label.text = GameText.Get(SupplyStation.CategoryKey(SupplyStation.Categories[t]));
                tabButtons.Add(tb);
            }
            for (var i = 0; i < SupplyStation.Catalog.Length; i++)
            {
                var index = i;
                var item = SupplyStation.Catalog[i];
                var b = MakeButton(listPage.transform, "Row" + i, new Rect(8, RowsTop, w - 26, 24), 15, TextAnchor.MiddleLeft,
                    () => SupplyStation.Instance == null || (!SupplyStation.Instance.IsBusy && SupplyStation.Instance.Available(item)), () => Pick(index));
                b.Label.text = "  " + GameText.Get(SupplyStation.NameKey(item));
                var price = Label(b.Rect, "Price", new Rect(0, 0, w - 36, 24), 15, TextAnchor.MiddleRight, Green);
                price.text = "$" + SupplyStation.Price(item);
                listButtons.Add(b);
            }
            scrollTrack = Block(listPage.transform, "ScrollTrack", new Rect(w - 14, RowsTop, 5, VisibleRows * RowStep - 2f), new Color(0.1f, 0.25f, 0.12f)).rectTransform;
            scrollThumb = Block(scrollTrack, "ScrollThumb", new Rect(0, 0, 5, 20), Green).rectTransform;
            footerText = Label(listPage.transform, "Footer", new Rect(10, h - 27, w - 20, 24), 12, TextAnchor.MiddleLeft, Dim);

            confirmPage = Panel("ConfirmPage");
            confirmText = Label(confirmPage.transform, "Question", new Rect(10, 42, w - 20, 80), 16, TextAnchor.MiddleCenter, Green);
            var ok = MakeButton(confirmPage.transform, "Ok", new Rect(20, h - 52, (w - 60) / 2f, 36), 17, TextAnchor.MiddleCenter, () => true, Confirm);
            ok.Label.text = GameText.Get("shop.ok");
            var cancel = MakeButton(confirmPage.transform, "Cancel", new Rect(40 + (w - 60) / 2f, h - 52, (w - 60) / 2f, 36), 17, TextAnchor.MiddleCenter, () => true, Cancel);
            cancel.Label.text = GameText.Get("shop.cancel");
            confirmButtons.Add(ok);
            confirmButtons.Add(cancel);

            messagePage = Panel("MessagePage");
            messageText = Label(messagePage.transform, "Message", new Rect(10, 42, w - 20, h - 52), 16, TextAnchor.MiddleCenter, Green);
            Refresh();
        }

        private GameObject Panel(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(canvasRect, false);
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
            return go;
        }

        // Rects are in canvas units from the top-left corner.
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

        private static Text Label(Transform parent, string name, Rect area, int size, TextAnchor anchor, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
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

        private static Image Block(Transform parent, string name, Rect area, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Place(go, parent, area);
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Button MakeButton(Transform parent, string name, Rect area, int size, TextAnchor anchor, Func<bool> enabled, Action click)
        {
            var back = Block(parent, name, area, RowBack);
            var label = Label(back.transform, "Label", new Rect(0, 0, area.width, area.height), size, anchor, Green);
            return new Button { Rect = back.rectTransform, Back = back, Label = label, Enabled = enabled, Click = click };
        }
    }
}
