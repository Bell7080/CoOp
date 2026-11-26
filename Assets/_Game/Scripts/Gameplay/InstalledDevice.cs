using System.Collections.Generic;
using CoOpHorror.Localization;
using CoOpHorror.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // A fixture mounted in the level (CCTV camera, ceiling lamp). Every one is on when the stage starts and
    // holds some power (Watts). Holding the left button on it latches the nose on and drinks that power into
    // the tank at the same flat rate as appliances: the fixture weakens as it drains (a lamp dims, a camera's
    // feed fills with static) and dies when it is empty. Holding the right button pumps power back in from the
    // tank and brings it back. The blackout empties everything still left.
    public abstract class InstalledDevice : Interactable, IChargeGauge, INoseTarget
    {
        public static readonly List<InstalledDevice> Devices = new();

        public int Watts = 60;
        public bool StartsOn = true;      // off fixtures start empty (give them power with the right button)

        public readonly NetworkVariable<float> Power = new(0f);

        public bool Online => IsSpawned && Power.Value > 0.5f;
        public float Fraction => IsSpawned && Watts > 0 ? Mathf.Clamp01(Power.Value / Watts) : 1f;
        public int Remaining => IsSpawned ? Mathf.CeilToInt(Power.Value - 0.01f) : Watts;
        public float GaugeFraction => Fraction;
        public bool GaugeCharging => false;
        public override bool StickyHold => true;   // the nose stays latched while you look around
        public abstract string DisplayName { get; }

        private Collider mainCollider;

        protected override void Awake()
        {
            base.Awake();
            Devices.Add(this);
            foreach (var c in GetComponentsInChildren<Collider>(true)) if (!c.isTrigger) { mainCollider = c; break; }
        }

        public override void OnDestroy()
        {
            Devices.Remove(this);
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) Power.Value = StartsOn ? Watts : 0f;
        }

        public override string HoverInfo(PlayerInteractor who)
        {
            if (!IsSpawned) return string.Empty;
            return DisplayName + "\n" + (Online ? GameText.Format("hover.watts", Remaining, Watts) : GameText.Get("hover.drained"));
        }

        public override InteractKind Describe(PlayerInteractor who, out string prompt)
        {
            if (!Online)
            {
                prompt = GameText.Format("interact.drained", DisplayName);
                return InteractKind.None;
            }
            prompt = GameText.Format("interact.suck", DisplayName, Remaining);
            return InteractKind.Hold;
        }

        // The nose drinks the fixture's power.
        public override bool ServerHold(PlayerInteractor who, float dt)
        {
            if (!Online) return false;
            var nose = who.GetComponent<PlayerNose>();
            if (nose == null) return false;
            var taken = nose.ServerAdd(Mathf.Min(PlayerNose.SuckRate * dt, Power.Value));
            Power.Value = Mathf.Max(0f, Power.Value - taken);
            nose.ServerSetFlow(taken > 0f ? PlayerNose.FlowIn : PlayerNose.FlowNone);
            if (!Online) { Power.Value = 0f; return false; }
            return true;
        }

        // Right button: the nose pumps power back in from the tank (switches it on again).
        public override InteractKind DescribeRight(PlayerInteractor who, out string prompt)
        {
            prompt = string.Empty;
            if (!IsSpawned || Power.Value >= Watts - 0.5f) return InteractKind.None;
            var nose = who.GetComponent<PlayerNose>();
            if (nose == null || nose.Energy.Value < 0.5f) return InteractKind.None;
            prompt = GameText.Format("interact.giveBack", DisplayName, Remaining, Watts);
            return InteractKind.Hold;
        }

        public override bool ServerHoldRight(PlayerInteractor who, float dt)
        {
            var nose = who.GetComponent<PlayerNose>();
            if (nose == null || Power.Value >= Watts - 0.01f) return false;
            var given = nose.ServerTake(Mathf.Min(PlayerNose.SuckRate * dt, Watts - Power.Value));
            Power.Value = Mathf.Min(Watts, Power.Value + given);
            nose.ServerSetFlow(given > 0f ? PlayerNose.FlowOut : PlayerNose.FlowNone);
            return Power.Value < Watts - 0.01f && nose.Energy.Value > 0.01f;
        }

        // Blackout: everything still holding power loses it.
        public void ServerDischarge()
        {
            if (IsServer) Power.Value = 0f;
        }

        public Vector3 NosePoint(Vector3 from)
        {
            if (mainCollider == null || !mainCollider.enabled) return transform.position;
            return Vector3.Lerp(mainCollider.ClosestPoint(from), mainCollider.bounds.center, 0.1f);
        }
    }
}
