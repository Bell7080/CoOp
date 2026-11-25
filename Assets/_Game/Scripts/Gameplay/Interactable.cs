using CoOpHorror.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    public enum InteractKind : byte { None, Press, Hold }

    // Something a player can use with the left mouse button. Clients only read replicated state to show a
    // prompt; the host validates range/state and applies every change (one writer per value).
    public abstract class Interactable : NetworkBehaviour
    {
        public const float UseRange = 3f;           // client look ray length
        public const float ServerRangeSlack = 1.5f; // host tolerance for interpolation lag

        private Collider[] colliders;

        protected virtual void Awake() => colliders = GetComponentsInChildren<Collider>(true);

        // Server clock shared by every peer (clients use their estimate of the host's clock).
        protected double NetTime => NetworkManager != null ? NetworkManager.ServerTime.Time : 0d;

        // Client: what the left button does for this player right now and the prompt to show.
        public abstract InteractKind Describe(PlayerInteractor who, out string prompt);

        // Owner: handle a press locally (e.g. open a view). Return true to skip the server request.
        public virtual bool OwnerUse(PlayerInteractor who) => false;

        // Server: one-shot use (left button pressed).
        public virtual void ServerUse(PlayerInteractor who) { }

        // Server: every frame while the player holds the left button on this. Return false to end the hold.
        public virtual bool ServerHold(PlayerInteractor who, float dt) => false;

        public virtual void ServerHoldEnded(PlayerInteractor who) { }

        // Holding this shows timing skill checks to the holder.
        public virtual bool UsesSkillChecks => false;

        // Server: the holder reported a skill check result.
        public virtual void ServerSkillCheck(PlayerInteractor who, SkillCheckResult result) { }

        public bool ServerInRange(PlayerInteractor who)
        {
            var eye = who.EyePosition;
            var limit = (UseRange + ServerRangeSlack) * (UseRange + ServerRangeSlack);
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
