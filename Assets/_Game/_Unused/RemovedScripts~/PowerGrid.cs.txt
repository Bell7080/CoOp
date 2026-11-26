using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // Team-wide shared electricity. Generators add to it, appliances draw from it. Host writes only.
    public sealed class PowerGrid : NetworkBehaviour
    {
        public const float Capacity = 100f;

        public static PowerGrid Instance { get; private set; }

        public readonly NetworkVariable<float> Stored = new(0f);

        public float Fraction => Stored.Value / Capacity;
        public bool IsFull => Stored.Value >= Capacity - 0.01f;
        public bool IsEmpty => Stored.Value <= 0.01f;

        private void Awake() => Instance = this;

        public override void OnNetworkSpawn()
        {
            if (IsServer) Stored.Value = 0f; // every new room starts with an empty grid
        }

        public void ServerAdd(float amount)
        {
            if (!IsServer || amount <= 0f) return;
            Stored.Value = Mathf.Min(Capacity, Stored.Value + amount);
        }

        // Returns how much was actually taken.
        public float ServerTake(float amount)
        {
            if (!IsServer || amount <= 0f) return 0f;
            var taken = Mathf.Min(amount, Stored.Value);
            Stored.Value -= taken;
            return taken;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }
    }
}
