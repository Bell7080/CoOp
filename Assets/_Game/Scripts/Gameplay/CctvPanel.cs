using System;
using CoOpHorror.Localization;
using CoOpHorror.Player;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // The ship's CCTV desk. Its monitor shows a live feed; clicking it switches the local player into the
    // full-screen CCTV view (handled by the UI layer through OpenView). Nothing here is replicated.
    public sealed class CctvPanel : Interactable
    {
        public static CctvPanel Instance { get; private set; }

        // Set by the CCTV view (UI layer) so gameplay code does not depend on it.
        public static Func<bool> OpenView;

        public Renderer Screen;

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

        public override InteractKind Describe(PlayerInteractor who, out string prompt)
        {
            prompt = GameText.Format("interact.cctvPanel", CctvCamera.OnlineCount, CctvCamera.All.Count);
            return InteractKind.Press;
        }

        public override bool OwnerUse(PlayerInteractor who)
        {
            OpenView?.Invoke();
            return true;
        }
    }
}
