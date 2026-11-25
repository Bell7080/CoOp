using CoOpHorror.Gameplay;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CoOpHorror.Player
{
    // Inventory slots (number keys). Starts with one slot; shop upgrade modules will add more (Slots).
    // While lifting a tiny item alone, its number key stores it; with empty hands the key takes it out into
    // a telekinesis hold (until the next click or Q). Stored items ride along invisibly. Host validates.
    public sealed class PlayerInventory : NetworkBehaviour
    {
        public const int MaxSlots = 4;

        public readonly NetworkVariable<int> Slots = new(1);

        private PlayerInteractor interactor;
        private PlayerHealth health;

        private void Awake()
        {
            interactor = GetComponent<PlayerInteractor>();
            health = GetComponent<PlayerHealth>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) Slots.Value = 1;
        }

        public Appliance InSlot(int slot)
        {
            if (!IsSpawned) return null;
            foreach (var a in Appliance.All)
                if (a != null && a.IsSpawned && a.StoredBy.Value == NetworkObjectId && a.StoredSlot.Value == slot) return a;
            return null;
        }

        // Automation / UI entry point (owner).
        public void UseSlot(int slot)
        {
            if (IsOwner && IsSpawned) SlotRpc(slot);
        }

        private void Update()
        {
            if (!IsOwner || !IsSpawned || !FirstPersonController.GameplayInputReady || (health != null && health.IsDead.Value)) return;
            var k = Keyboard.current;
            if (k == null) return;
            for (var i = 0; i < Slots.Value && i < 9; i++)
                if (k[Key.Digit1 + i].wasPressedThisFrame) SlotRpc(i);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SlotRpc(int slot)
        {
            if (slot < 0 || slot >= Slots.Value || interactor == null || (health != null && health.IsDead.Value)) return;
            var stored = InSlot(slot);
            if (stored != null)
            {
                if (interactor.HandsFull) return;
                stored.ServerTakeOut(interactor);
                interactor.ServerBeginLatchedHold(stored);
                return;
            }
            if (interactor.HeldGrabbable is Appliance held && held.CanStore && held.Holders.Count == 1)
            {
                interactor.ServerForceEndHold(held);
                held.ServerStore(interactor, slot);
            }
        }

        // Death or leaving: everything stored falls out where the player is.
        public void ServerDropAll()
        {
            if (!IsServer || !IsSpawned) return;
            foreach (var a in Appliance.All)
                if (a != null && a.IsSpawned && a.StoredBy.Value == NetworkObjectId) a.ServerTakeOut(interactor);
        }
    }
}
