using CoOpHorror.Localization;
using CoOpHorror.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // Creatures are lumps of electricity: their health is power (Power, out of CreatureSenses.Watts, 1000 W for
    // the small clock spider up to 5000 W for the TV walker). A nose can latch onto one at any time (it keeps
    // moving and attacking; a stun is just an easier moment) and drains its power at the usual flat rate, but
    // only a tenth of what it drains reaches the tank. Draining is as loud as any appliance. Drained to nothing,
    // the creature dies: it keels over on its side, fades away and leaves a battery (CreatureBattery) holding
    // a fifth of its power, which noses can only drink from.
    // The disguised fan looks like a 50 W desk fan to the nose; drinking from it turns it into the creature.
    // A teammate holding the left button on a clinging spider tears it off instead.
    public sealed partial class TempMonster : IChargeGauge
    {
        public const float TankShare = 0.1f;      // share of the drained power that reaches the drinker's tank
        public const float BatteryShare = 0.2f;   // share of its power the dead creature leaves in its battery
        public const int DisguiseWatts = 50;      // what the disguised fan claims to hold

        // The hidden battery this creature drops when it dies (built with it).
        public CreatureBattery Battery;

        private float nextDrainNoise, ambushProgress;
        private Collider noseCollider;

        public override bool StickyHold => true;

        public string DisplayName => Kind switch
        {
            CreatureKind.TvWalker => GameText.Get("creature.tv"),
            CreatureKind.FanCrawler => GameText.Get("creature.fan"),
            CreatureKind.ClockSpider => GameText.Get("creature.clock"),
            _ => GameText.Get("creature.generic"),
        };

        public int Capacity => senses != null ? senses.Watts : 1000;
        private int RemainingWatts => Mathf.CeilToInt(Power.Value - 0.01f);
        private int DisguiseRemaining => Mathf.Max(1, Mathf.CeilToInt(DisguiseWatts - ambushProgress * PlayerNose.SuckRate));
        public bool Alive => IsSpawned && Mode.Value != CreatureMode.Dead;

        // The nose finds it (PlayerInteractor's nose assist) while it can be drunk from.
        public bool NoseDrainable => Alive && Power.Value > 0.5f && Mode.Value != CreatureMode.Latched;

        // The HUD gauge under the crosshair: its health (a disguised fan shows a desk fan's 50 W).
        public float GaugeFraction => !IsSpawned ? 0f
            : Mode.Value == CreatureMode.Disguised ? DisguiseRemaining / (float)DisguiseWatts
            : Capacity > 0 ? Mathf.Clamp01(Power.Value / Capacity) : 0f;
        public bool GaugeCharging => false;

        public override string HoverInfo(PlayerInteractor who)
        {
            if (!IsSpawned) return string.Empty;
            switch (Mode.Value)
            {
                case CreatureMode.Disguised: // exactly what a real fan shows
                    return GameText.Get("appliance.fan") + "\n" + GameText.Format("hover.watts", DisguiseRemaining, DisguiseWatts);
                case CreatureMode.Dead:
                    return string.Empty;
                default:
                    return DisplayName + "\n" + GameText.Format("hover.watts", RemainingWatts, Capacity);
            }
        }

        public override InteractKind Describe(PlayerInteractor who, out string prompt)
        {
            prompt = string.Empty;
            if (!Alive) return InteractKind.None;
            if (Mode.Value == CreatureMode.Latched)
            {
                if (LatchedTo.Value == who.NetworkObjectId) return InteractKind.None;
                prompt = GameText.Format("interact.pullOff", DisplayName);
                return InteractKind.Hold;
            }
            if (Power.Value <= 0.5f) return InteractKind.None;
            prompt = Mode.Value == CreatureMode.Disguised
                ? GameText.Format("interact.suck", GameText.Get("appliance.fan"), DisguiseRemaining)
                : GameText.Format("interact.suck", DisplayName, RemainingWatts);
            return InteractKind.Hold;
        }

        public override bool ServerHold(PlayerInteractor who, float dt)
        {
            if (!Alive) return false;
            if (Mode.Value == CreatureMode.Latched)
            {
                if (victim == null || who.gameObject == victim.gameObject) return false;
                pullProgress += dt;
                if (pullProgress < PullSeconds) return true;
                ServerRelease(true, Flat(victim.transform.position - who.transform.position));
                return false;
            }
            if (!ServerDrink(who, dt)) return false;
            if (Power.Value <= 0.5f) { ServerDie(); return false; }
            if (Mode.Value != CreatureMode.Disguised) return true;
            ambushProgress += dt;
            if (ambushProgress < AmbushAfter) return true;
            ServerAmbush(who.GetComponent<PlayerHealth>());
            return false;
        }

        // Host: the nose drains the creature's power; only a tenth of it reaches the tank.
        private bool ServerDrink(PlayerInteractor who, float dt)
        {
            if (Power.Value <= 0.5f) return false;
            var nose = who.GetComponent<PlayerNose>();
            if (nose == null) return false;
            var drained = Mathf.Min(PlayerNose.SuckRate * dt, Power.Value);
            Power.Value = Mathf.Max(0f, Power.Value - drained);
            nose.ServerAdd(drained * TankShare);
            nose.ServerSetFlow(drained > 0f ? PlayerNose.FlowIn : PlayerNose.FlowNone);
            if (drained <= 0f) return true;
            if (Time.time >= nextDrainNoise)
            {
                nextDrainNoise = Time.time + Appliance.NoisePeriod;
                Noise.Emit(transform.position, Noise.DrainRadius(Mathf.Min(Capacity, 1500)), NoiseKind.Drain);
            }
            return true;
        }

        public Vector3 NosePoint(Vector3 from)
        {
            if (noseCollider == null) noseCollider = body != null ? body : GetComponent<Collider>();
            if (noseCollider == null || !noseCollider.enabled) return transform.position + Vector3.up * EyeHeight;
            return Vector3.Lerp(noseCollider.ClosestPoint(from), noseCollider.bounds.center, 0.15f);
        }

        // ---------- mode changes (host) ----------

        // A breather (TV after a wall hit, fan overheated, spider torn off): it stands still; its power is untouched.
        private void ServerStun(float seconds)
        {
            HaltAgent();
            Chasing.Value = false;
            Recognizing.Value = false;
            lungeUntil = windupUntil = gustUntil = 0f;
            gustPending = false;
            modeUntil = Time.time + seconds;
            Mode.Value = CreatureMode.Stunned;
            StunFxRpc();
        }

        // Drained to nothing: it keels over, fades out (every peer, TempMonster.Creatures) and leaves a battery.
        private void ServerDie()
        {
            if (Mode.Value == CreatureMode.Dead) return;
            var dropAt = FloorUnder(transform.position); // (a spider drained on the ceiling falls; its battery lands below)
            if (Mode.Value == CreatureMode.Latched) ServerRelease(false, Vector3.zero);
            if (Mode.Value == CreatureMode.Disguised) ServerDropToFloor();
            HaltAgent();
            if (agent.enabled) agent.enabled = false;
            target = null;
            investigating = false;
            Chasing.Value = false;
            Recognizing.Value = false;
            lungeUntil = windupUntil = gustUntil = ringUntil = 0f;
            gustPending = false;
            Power.Value = 0f;
            Mode.Value = CreatureMode.Dead;
            if (Battery != null) Battery.ServerDrop(dropAt + Flat(transform.right).normalized * 0.6f, Mathf.RoundToInt(Capacity * BatteryShare));
            DeathFxRpc();
        }

        // Back to the usual brain. A target it had before a stun or its gust is still remembered as just seen
        // (a wind-up or an overheat is no reason to forget).
        private void ServerWake(bool loud)
        {
            var wasDisguised = Mode.Value == CreatureMode.Disguised;
            Mode.Value = CreatureMode.Active;
            if (wasDisguised) ServerDropToFloor();
            nextLunge = Time.time + 1f;
            nextRepath = 0f;
            if (target != null && target.IsSpawned && !target.IsDead.Value)
            {
                lastSeenTime = Time.time;
                lastSeenPosition = target.transform.position;
            }
            if (loud) NoticeFxRpc();
        }

        // The disguised fan turns into the creature in the face of whoever drank from it (no recognition pause).
        private void ServerAmbush(PlayerHealth who)
        {
            ServerWake(false);
            if (who == null) return;
            AmbushFxRpc(who.OwnerClientId);
            target = who;
            lastSeenTime = Time.time;
            lastSeenPosition = who.transform.position;
            Recognizing.Value = false;
            Chasing.Value = true;
            nextLunge = 0f;
            transform.rotation = Quaternion.LookRotation(Flat(who.transform.position - transform.position).sqrMagnitude > 0.01f
                ? Flat(who.transform.position - transform.position) : transform.forward);
            NoticeFxRpc();
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        private void AmbushFxRpc(ulong drinker)
        {
            if (NetworkManager != null && drinker == NetworkManager.LocalClientId) CrashIntro.Scare(0.5f, 0.7f);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        private void StunFxRpc() => ProtoAudio.Play(ProtoSound.Buzz, transform.position + Vector3.up, 0.6f);

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        private void DeathFxRpc()
        {
            ProtoAudio.Play(ProtoSound.Zap, transform.position + Vector3.up, 0.9f);
            ProtoAudio.PlayPitched(ProtoSound.StingerStatic, transform.position + Vector3.up, 0.5f, 0.7f);
        }
    }
}
