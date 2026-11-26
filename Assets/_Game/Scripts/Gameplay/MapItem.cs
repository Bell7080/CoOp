using CoOpHorror.Localization;
using CoOpHorror.Player;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // Something worth noticing lying in the facility (e.g. the ship's lost generator crank handle). For now it
    // only shows what it is when you look at it; what it is for comes later. Static, no state.
    public sealed class MapItem : Interactable
    {
        public string NameKey = "item.crank";
        public string InfoKey = "item.crankInfo";

        public override string HoverInfo(PlayerInteractor who) => GameText.Get(NameKey) + "\n" + GameText.Get(InfoKey);

        public override InteractKind Describe(PlayerInteractor who, out string prompt)
        {
            prompt = string.Empty;
            return InteractKind.None;
        }
    }
}
