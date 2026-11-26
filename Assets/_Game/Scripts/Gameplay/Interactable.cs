using CoOpHorror.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // PressOrHold: a click uses it (e.g. hit a body), keeping the button down grabs it.
    public enum InteractKind : byte { None, Press, Hold, PressOrHold }

    // Something a player can use with the left mouse button. Clients only read replicated state to show a
    // prompt; the host validates range/state and applies every change (one writer per value).
    public abstract class Interactable : NetworkBehaviour
    {
        public const float UseRange = 3f;           // client look ray length
        public const float ServerRangeSlack = 1.5f; // host tolerance for interpolation lag

        private Collider[] colliders;

        protected virtual void Awake() => colliders = GetComponentsInChildren<Collider>(true);

        // Whether looking at this collider targets the interactable (e.g. a door only through its buttons).
        // Other colliders of the object then just block the view like a wall.
        public virtual bool TargetedBy(Collider c) => true;

        // Server clock shared by every peer (clients use their estimate of the host's clock).
        protected double NetTime => NetworkManager != null ? NetworkManager.ServerTime.Time : 0d;

        // Client: what the left button does for this player right now and the prompt to show.
        public abstract InteractKind Describe(PlayerInteractor who, out string prompt);

        // Client: a short description shown under the crosshair while looking at this (what it is, what it is
        // worth). Empty for things that need no explanation.
        public virtual string HoverInfo(PlayerInteractor who) => string.Empty;

        // Owner: handle a press locally (e.g. open a view). Return true to skip the server request.
        public virtual bool OwnerUse(PlayerInteractor who) => false;

        // Server: one-shot use (left button pressed).
        public virtual void ServerUse(PlayerInteractor who) { }

        // Server: every frame while the player holds the left button on this. Return false to end the hold.
        public virtual bool ServerHold(PlayerInteractor who, float dt) => false;

        public virtual void ServerHoldEnded(PlayerInteractor who) { }

        // Holding this shows timing skill checks to the holder.
        public virtual bool UsesSkillChecks => false;

        // Right mouse button (the nose pumps power OUT of the tank into this): what it does for this player
        // right now and the prompt. Holding it calls ServerHoldRight on the host like ServerHold.
        public virtual InteractKind DescribeRight(PlayerInteractor who, out string prompt)
        {
            prompt = string.Empty;
            return InteractKind.None;
        }

        // Server: every frame while the player holds the right button on this. Return false to end the hold.
        public virtual bool ServerHoldRight(PlayerInteractor who, float dt) => false;

        // A sticky hold (telekinesis, a latched nose) continues while the button is down even when the player looks away.
        public virtual bool StickyHold => false;

        // Host-side reach while holding (sticky holds swing the object around).
        public virtual float HoldRange => UseRange;

        // Server: the holder reported a skill check result.
        public virtual void ServerSkillCheck(PlayerInteractor who, SkillCheckResult result) { }

        public bool ServerInRange(PlayerInteractor who) => ServerInRange(who, UseRange);

        public bool ServerInRange(PlayerInteractor who, float range)
        {
            var eye = who.EyePosition;
            var limit = (range + ServerRangeSlack) * (range + ServerRangeSlack);
            foreach (var c in colliders)
                if (c != null && c.enabled && (c.ClosestPoint(eye) - eye).sqrMagnitude <= limit) return true;
            return false;
        }
    }

    // Charged by holding the button. A missed skill check stalls it for everyone on it and makes noise.
    public abstract class SkillCheckedInteractable : Interactable
    {
        public readonly NetworkVariable<double> StalledUntil = new(0d);

        public bool IsStalled => IsSpawned && NetTime < StalledUntil.Value;

        public override bool UsesSkillChecks => true;

        public override void OnNetworkSpawn()
        {
            if (IsServer) StalledUntil.Value = 0d;
            StalledUntil.OnValueChanged += OnStallChanged;
        }

        public override void OnNetworkDespawn() => StalledUntil.OnValueChanged -= OnStallChanged;

        private void OnStallChanged(double previous, double current)
        {
            if (current > previous && current > NetTime) ProtoAudio.Play(ProtoSound.Bang, transform.position + Vector3.up, 1f);
        }

        // Server: miss penalty shared by every player holding this.
        protected void ServerFail(float stallSeconds, float noiseRadius)
        {
            StalledUntil.Value = NetTime + stallSeconds;
            Noise.Emit(transform.position, noiseRadius);
        }
    }
}
