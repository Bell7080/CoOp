using System.Collections.Generic;
using CoOpHorror.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace CoOpHorror.Gameplay
{
    // Each creature's own behaviour on top of the common brain (TempMonster.cs).
    //
    // TV walker (light): sees far only into lit places (lamps, flashlights, a drain flare); a flashlight beam
    // on it makes it turn and come. Its attack is a 1 s wind-up (screen flashing, channel static), then a
    // straight charge that cannot steer: everyone in the line is hit, and if it misses into a wall or a shut
    // door it cracks and is stunned (2.5 s / 4 s, drainable). CCTV cameras near it fill with static.
    //
    // Fan crawler (sound): it starts the stage as one more desk fan on a side table, looking and reading like
    // the real 50 W fan, and turns into the creature only when a nose drinks from it (right in your face).
    // Then it hunts by ear: short sight, hears running, walking, doors, radios. Once it has a target it keeps
    // it out of sight range and through its own gust and overheat, and gives up only when the target really
    // hides (behind cover, under furniture, crouched out of view and quiet). After a lunge it blows a 1.5 s
    // gust that shoves players, swings doors open and spills tanks, then overheats for 3 s (drainable). In the
    // blackout it stays put and only listens.
    //
    // Clock spider (time): ticks all the time (louder up close), rings an alarm every 90 s (20 s in the
    // blackout) that calls the others over, sees in the dark and under furniture. It leaps onto a player's
    // head (its legs hang into the victim's first-person view): slowed, no nose, a 5 s countdown, then a loud alarm (muffled inside a shut wardrobe). A teammate
    // holding the left button on it tears it off; alone, shake it off with A / D.
    public sealed partial class TempMonster
    {
        // TV walker
        public const float TvWindup = 1f;
        public const float TvWallStun = 2.5f;
        public const float TvDoorStun = 4f;
        public const float BeamNoticeRange = 20f;
        public const float BeamHalfAngle = 31f;      // the flashlight's spot cone
        public const float LitThreshold = 0.3f;
        public const float JamRange = 10f;           // CCTV static around a TV walker
        public const float JamStrength = 0.7f;
        // Fan crawler
        public const float FanOverheat = 3f;
        public const float GustSeconds = 1.5f;
        public const float GustRange = 6f;
        public const float GustHalfAngle = 45f;
        public const float GustPush = 7f;
        public const float GustLeak = 0.05f;         // share of the tank blown out per gust tick
        public const float AmbushAfter = 1f;         // seconds of drinking before it wakes in your face
        public const float DisguiseRadius = 0.22f, DisguiseHeight = 0.6f; // its collider while it is a desk fan
        // Clock spider
        public const float AlarmEvery = 90f;
        public const float AlarmEveryBlackout = 20f;
        public const float AlarmSeconds = 3f;
        public const float AlarmRadius = 18f;
        public const float LatchSeconds = 5f;
        public const int LatchDamage = 10;
        public const int LatchAlarmDamage = 15;
        public const float LatchAlarmRadius = 20f;
        public const float MuffledAlarmRadius = 6f;  // inside a shut wardrobe
        public const float LatchSlow = 0.55f;
        public const float PullSeconds = 1.5f;       // a teammate tearing it off
        public const int ShakesToEscape = 12;        // alternating A / D presses, alone
        public const float SpiderThrownStun = 4f;

        private CreatureSenses senses;
        private float modeUntil;            // host clock (Time.time) for stunned / asleep / disguised
        private float windupUntil, gustUntil, nextGustTick, ringUntil, nextAlarm;
        private bool gustPending;
        private Vector3 chargeDirection;
        private readonly HashSet<PlayerHealth> chargeHits = new();
        private PlayerHealth victim;
        private float latchEnd, pullProgress;
        private int shakes;
        private float nextTick;
        private bool shakeLeft, shakeAny;
        private static readonly RaycastHit[] CastHits = new RaycastHit[8];

        public CreatureSenses Senses => senses;
        public bool PoweredDown => IsSpawned && (Mode.Value == CreatureMode.Dead || Mode.Value == CreatureMode.Disguised);

        // Death (every peer): it tips over on its side, then sinks and shrinks away.
        public const float DeathFall = 0.6f, DeathLinger = 0.8f, DeathFade = 1.8f;
        private float deathStart = -1f;
        private Transform deathVisual;
        private Vector3 restPosition, restScale;
        private Quaternion restRotation;
        private bool restCached;
        private static bool Blackout
        {
            get
            {
                var stage = StageDirector.Instance;
                return stage != null && stage.IsSpawned && stage.Phase.Value == StagePhase.Launching;
            }
        }

        private static CreatureKind KindFromName(string objectName)
        {
            if (objectName.Contains("TvWalker")) return CreatureKind.TvWalker;
            if (objectName.Contains("FanCrawler")) return CreatureKind.FanCrawler;
            if (objectName.Contains("ClockSpider")) return CreatureKind.ClockSpider;
            return CreatureKind.Generic;
        }

        // The local player has a clock spider on their back (no nose, A / D to shake).
        public static bool LocalPinned
        {
            get
            {
                var me = PlayerInteractor.Local;
                if (me == null || !me.IsSpawned) return false;
                foreach (var m in All)
                    if (m != null && m.IsSpawned && m.Mode.Value == CreatureMode.Latched && m.LatchedTo.Value == me.NetworkObjectId) return true;
                return false;
            }
        }

        // Any peer: how much static a TV walker puts on a camera at this spot (0..JamStrength).
        public static float JamAt(Vector3 position)
        {
            var jam = 0f;
            foreach (var m in All)
            {
                if (m == null || !m.IsSpawned || m.Kind != CreatureKind.TvWalker || m.PoweredDown) continue;
                var d = Vector3.Distance(position, m.transform.position + Vector3.up);
                if (d < JamRange) jam = Mathf.Max(jam, (1f - d / JamRange) * JamStrength);
            }
            return jam;
        }

        // Host: back to a fresh start (new room, quick restart, teleport).
        // "disguised": start (again) as a desk fan on its table (the fan crawler at the start of a stage).
        private void ServerResetCreature(bool disguised)
        {
            var mode = disguised ? CreatureMode.Disguised : CreatureMode.Active;
            if (Mode.Value != mode) Mode.Value = mode;
            Power.Value = Capacity; // full health
            ambushProgress = 0f;
            LatchedTo.Value = NoVictim;
            LatchEndsAt.Value = 0d;
            victim = null;
            windupUntil = gustUntil = ringUntil = 0f;
            gustPending = false;
            nextAlarm = Time.time + AlarmEvery;
            ServerResetSurface();
            if (body != null) body.isTrigger = false;
        }

        // Every peer: a clinging spider does not shove its victim around; the disguised fan looks (and is as
        // big as) a desk fan, and turns into the crawler when it wakes.
        private void OnModeChanged(CreatureMode previous, CreatureMode current)
        {
            var disguised = current == CreatureMode.Disguised;
            CacheRestPose();
            if (current == CreatureMode.Dead) { if (deathStart < 0f) deathStart = Time.time; }
            else if (deathStart >= 0f || previous == CreatureMode.Dead)
            {
                // back for a new stage: stand up again
                deathStart = -1f;
                if (deathVisual != null)
                {
                    deathVisual.localPosition = restPosition;
                    deathVisual.localRotation = restRotation;
                    deathVisual.localScale = restScale;
                    deathVisual.gameObject.SetActive(true);
                }
            }
            if (body != null)
            {
                body.enabled = current != CreatureMode.Dead;
                body.isTrigger = current == CreatureMode.Latched;
                body.radius = disguised && DisguiseVisual != null ? DisguiseRadius : bodyRadius;
                body.height = disguised && DisguiseVisual != null ? DisguiseHeight : bodyHeight;
                body.center = disguised && DisguiseVisual != null ? new Vector3(0f, DisguiseHeight / 2f, 0f) : bodyCenter;
            }
            if (DisguiseVisual != null) DisguiseVisual.SetActive(disguised);
            if (CreatureVisual != null && current != CreatureMode.Dead) CreatureVisual.SetActive(!disguised || DisguiseVisual == null);
            if (previous == CreatureMode.Disguised && !disguised && IsSpawned)
            {
                // the "fan" unfolds: a bang and a burst of the creature's attack animation
                ProtoAudio.Play(ProtoSound.Bang, transform.position + Vector3.up * 0.5f, 0.8f);
                var rig = GetComponentInChildren<RigAnimator>();
                if (rig != null) rig.PlayOnce(rig.AttackState, 0.8f);
            }
        }

        // ---------- busy states (host) ----------

        // Host: everything that keeps the creature from its usual patrol / chase this frame.
        private bool ServerTickBusy()
        {
            switch (Mode.Value)
            {
                case CreatureMode.Stunned: // (draining it does not hold it: it gets up on time)
                    HaltAgent();
                    if (Time.time >= modeUntil) ServerWake(false);
                    return true;
                case CreatureMode.Dead:
                    return true;
                case CreatureMode.Disguised:
                    return true; // just a fan on a table until a nose drinks from it (TempMonster.Power)
            }
            if (Chasing.Value && target == null) Chasing.Value = false;

            // TV: the wind-up before a charge (stands, screen flashing, then goes in a straight line).
            if (windupUntil > 0f)
            {
                HaltAgent();
                if (target != null) FaceTowards(target.transform.position, 360f);
                if (Time.time < windupUntil) return true;
                windupUntil = 0f;
                var aim = target != null ? Flat(target.transform.position - transform.position) : transform.forward;
                chargeDirection = aim.sqrMagnitude > 0.01f ? aim.normalized : transform.forward;
                transform.rotation = Quaternion.LookRotation(chargeDirection);
                chargeHits.Clear();
                lungeUntil = Time.time + LungeSeconds;
                LungeFxRpc();
                return false;
            }

            // Fan: the gust after a lunge, then it overheats.
            if (gustUntil > 0f)
            {
                HaltAgent();
                ServerTickGust();
                if (Time.time >= gustUntil) { gustUntil = 0f; ServerStun(senses.StunSeconds); }
                return true;
            }

            // Spider: the alarm on the hour.
            if (Kind == CreatureKind.ClockSpider)
            {
                if (ringUntil > 0f)
                {
                    HaltAgent();
                    if (Time.time < ringUntil) return true;
                    ringUntil = 0f;
                }
                else if (Time.time >= nextAlarm)
                {
                    nextAlarm = Time.time + (Blackout ? AlarmEveryBlackout : AlarmEvery);
                    ringUntil = Time.time + AlarmSeconds;
                    Noise.Emit(transform.position, AlarmRadius, NoiseKind.Alarm);
                    AlarmFxRpc(false);
                    HaltAgent();
                    return true;
                }
                else if (Blackout && nextAlarm - Time.time > AlarmEveryBlackout) nextAlarm = Time.time + AlarmEveryBlackout;
            }
            return false;
        }

        // ---------- lunges ----------

        private void ServerStartLunge()
        {
            nextLunge = Time.time + LungeCooldown + LungeSeconds;
            if (Kind == CreatureKind.TvWalker)
            {
                windupUntil = Time.time + TvWindup;
                nextLunge += TvWindup;
                WindupFxRpc();
                return;
            }
            lungeUntil = Time.time + LungeSeconds;
            if (Kind == CreatureKind.FanCrawler) gustPending = true;
            LungeFxRpc();
        }

        // Host, TV: the straight charge. Returns true while charging (it moves itself).
        private bool ServerTickCharge()
        {
            if (Kind != CreatureKind.TvWalker || !Lunging) return false;
            if (agent.hasPath) agent.ResetPath();
            agent.isStopped = true;
            var step = chargeDirection * (LungeSpeed * Time.deltaTime);
            if (ChargeBlocked(out var door))
            {
                lungeUntil = 0f;
                ImpactFxRpc();
                ServerStun(door ? TvDoorStun : TvWallStun);
                return true;
            }
            var before = transform.position;
            agent.Move(step);
            transform.rotation = Quaternion.LookRotation(chargeDirection);
            // ran into the edge of the walkable floor (a wall the cast missed)
            if (Flat(transform.position - before).magnitude < step.magnitude * 0.25f && Time.time > lungeUntil - LungeSeconds + 0.15f)
            {
                lungeUntil = 0f;
                ImpactFxRpc();
                ServerStun(TvWallStun);
            }
            nextRepath = 0f;
            return true;
        }

        // Host, TV: something solid (not a player) right ahead of the charge. "door": a shut sliding door.
        private bool ChargeBlocked(out bool door)
        {
            door = false;
            var origin = transform.position + Vector3.up * Mathf.Max(0.6f, EyeHeight * 0.6f);
            var reach = (body != null ? body.radius : 0.45f) + 0.3f;
            var count = Physics.SphereCastNonAlloc(origin, 0.3f, chargeDirection, CastHits, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                var c = CastHits[i].collider;
                if (c == null || c.transform.IsChildOf(transform) || c.GetComponentInParent<PlayerHealth>() != null) continue;
                if (c.GetComponentInParent<SwingDoor>() != null) continue; // it barges through hinged doors
                if (CastHits[i].distance <= 0f && CastHits[i].point == Vector3.zero) continue; // started inside (floor contact)
                door = c.GetComponentInParent<PoweredDoor>() != null;
                return true;
            }
            return false;
        }

        // Host, TV: everyone in the charge line is hit once.
        private void ServerChargeHits()
        {
            foreach (var p in PlayerHealth.All)
            {
                if (p == null || !p.IsSpawned || p.IsDead.Value || chargeHits.Contains(p)) continue;
                var offset = p.transform.position - transform.position;
                if (Flat(offset).magnitude > AttackRange || Mathf.Abs(offset.y) > 1.6f) continue;
                chargeHits.Add(p);
                Slam(p, offset + chargeDirection);
            }
        }

        // Host, fan: a lunge just ended (hit or miss): blow.
        private void ServerAfterMove()
        {
            if (!gustPending || Lunging || Kind != CreatureKind.FanCrawler) return;
            gustPending = false;
            gustUntil = Time.time + GustSeconds;
            nextGustTick = 0f;
            GustFxRpc();
        }

        private void ServerTickGust()
        {
            if (Time.time < nextGustTick) return;
            nextGustTick = Time.time + 0.25f;
            var origin = transform.position + Vector3.up * EyeHeight;
            var forward = Flat(transform.forward).normalized;
            foreach (var p in PlayerHealth.All)
            {
                if (p == null || !p.IsSpawned || p.IsDead.Value) continue;
                var offset = Flat(p.transform.position - transform.position);
                if (offset.magnitude > GustRange || (offset.sqrMagnitude > 0.01f && Vector3.Angle(forward, offset) > GustHalfAngle)) continue;
                if (Physics.Linecast(origin, p.transform.position, out var hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && hit.collider.GetComponentInParent<PlayerHealth>() != p && !hit.collider.transform.IsChildOf(transform)) continue;
                var away = offset.sqrMagnitude > 0.01f ? offset.normalized : forward;
                p.ServerPush(away * GustPush + Vector3.up * 1.2f);
                if (p.TryGetComponent<PlayerNose>(out var tank)) tank.ServerLeak(GustLeak);
            }
            foreach (var door in SwingDoor.All)
            {
                if (door == null || !door.IsSpawned) continue;
                var offset = Flat(door.transform.position - transform.position);
                if (offset.magnitude > GustRange || Vector3.Angle(forward, offset) > GustHalfAngle) continue;
                door.ServerBlow(transform.position);
            }
        }

        // ---------- patrol extras (host) ----------

        // Fan, blackout: stays where it is and only listens.
        private bool ServerHoldStill()
        {
            if (Kind != CreatureKind.FanCrawler || !Blackout) return false;
            HaltAgent();
            transform.Rotate(0f, 25f * Time.deltaTime, 0f);
            return true;
        }

        // Host, waking from the disguise: off its table onto the nearest walkable floor, agent on.
        private void ServerDropToFloor()
        {
            if (agent.enabled) return;
            if (!NavMesh.SamplePosition(transform.position, out var hit, 3f, NavMesh.AllAreas)) return;
            transform.position = hit.position;
            if (sync != null && sync.CanCommitToTransform) sync.Teleport(hit.position, transform.rotation, transform.localScale);
            agent.enabled = true;
            agent.Warp(hit.position);
        }

        // ---------- TV: light ----------

        // Host: is light on this player (their own flashlight, a lamp, an appliance flaring as it is drained)?
        private static bool IsLit(PlayerHealth p)
        {
            if (p.TryGetComponent<PlayerFlashlight>(out var torch) && torch.IsSpawned && torch.On.Value) return true;
            var position = p.transform.position;
            if (PowerLamp.LitByAnyLamp(position) > LitThreshold) return true;
            foreach (var a in Appliance.All)
                if (a != null && a.IsSpawned && a.Draining.Value && (a.transform.position - position).sqrMagnitude < a.DrainRadius * a.DrainRadius * 0.25f) return true;
            return false;
        }

        // Host, TV: a flashlight beam on it from anywhere it can be seen from: it turns and comes for the light.
        private void ServerNoticeBeams()
        {
            if (Kind != CreatureKind.TvWalker || investigating) return;
            var me = transform.position + Vector3.up * EyeHeight;
            foreach (var p in PlayerHealth.All)
            {
                if (p == null || !p.IsSpawned || p.IsDead.Value) continue;
                if (!p.TryGetComponent<PlayerFlashlight>(out var torch) || !torch.IsSpawned || !torch.On.Value) continue;
                if (!p.TryGetComponent<PlayerInteractor>(out var aim)) continue;
                var eye = aim.EyePosition;
                var toMe = me - eye;
                if (toMe.magnitude > BeamNoticeRange || Vector3.Angle(aim.AimForward, toMe) > BeamHalfAngle) continue;
                if (Physics.Linecast(eye, me, out var hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && !hit.collider.transform.IsChildOf(transform) && !hit.collider.transform.IsChildOf(p.transform)) continue;
                investigating = true;
                noisePosition = p.transform.position;
                lingerUntil = 0f;
                return;
            }
        }

        // ---------- spider: clinging ----------

        private void ServerLatch(PlayerHealth p)
        {
            ServerResetSurface(); // (it may land on them from the ceiling)
            victim = p;
            target = null;
            Chasing.Value = false;
            Recognizing.Value = false;
            lungeUntil = 0f;
            HaltAgent();
            agent.enabled = false;
            latchEnd = Time.time + LatchSeconds;
            LatchEndsAt.Value = NetTime + LatchSeconds;
            pullProgress = 0f;
            shakes = 0;
            LatchedTo.Value = p.NetworkObjectId;
            Mode.Value = CreatureMode.Latched;
            transform.SetPositionAndRotation(BackOf(p), p.transform.rotation);
            if (p.TryGetComponent<PlayerInteractor>(out var hands)) hands.ServerLetGoOfEverything();
            if (p.TryGetComponent<FirstPersonController>(out var motor)) motor.SlowRpc(LatchSeconds + 1f, LatchSlow);
            p.ServerApplyDamage(LatchDamage, Vector3.zero);
            LatchFxRpc(p.OwnerClientId);
        }

        private void ServerTickLatched()
        {
            if (victim == null || !victim.IsSpawned || victim.IsDead.Value) { ServerRelease(false, Vector3.zero); return; }
            transform.SetPositionAndRotation(BackOf(victim), victim.transform.rotation);
            if (Time.time < latchEnd) return;
            // Time's up: it rings right on their back.
            var muffled = SwingDoor.InClosedWardrobe(victim.transform.position);
            victim.ServerApplyDamage(LatchAlarmDamage, -victim.transform.forward * 3f + Vector3.up * 2f);
            if (victim.TryGetComponent<PlayerNose>(out var tank)) tank.ServerLeak(PlayerNose.LeakOnHit);
            Noise.Emit(victim.transform.position, muffled ? MuffledAlarmRadius : LatchAlarmRadius, NoiseKind.Alarm);
            AlarmFxRpc(muffled);
            ServerRelease(true, -victim.transform.forward);
        }

        // On top of the victim's head (it grips their skull; the victim sees its legs over their eyes).
        private static Vector3 BackOf(PlayerHealth p) =>
            (p.TryGetComponent<FirstPersonController>(out var m) && m.AimPivot != null ? m.AimPivot.position : p.transform.position + Vector3.up * 0.72f)
            + Vector3.up * 0.1f;

        // Host: off the victim's back, onto the floor (stunned when torn or shaken off, or after ringing).
        private void ServerRelease(bool stun, Vector3 throwDirection)
        {
            var from = victim != null ? victim.transform.position : transform.position;
            if (victim != null && victim.IsSpawned && victim.TryGetComponent<FirstPersonController>(out var motor)) motor.SlowRpc(0f, 1f);
            victim = null;
            LatchedTo.Value = NoVictim;
            LatchEndsAt.Value = 0d;
            Mode.Value = CreatureMode.Active;
            var drop = from + Vector3.down + Flat(throwDirection).normalized * 1.3f;
            if (!NavMesh.SamplePosition(drop, out var hit, 3f, NavMesh.AllAreas) && !NavMesh.SamplePosition(from, out hit, 4f, NavMesh.AllAreas))
                hit.position = homePosition;
            transform.position = hit.position;
            if (sync != null && sync.CanCommitToTransform) sync.Teleport(hit.position, transform.rotation, transform.localScale);
            agent.enabled = true;
            agent.Warp(hit.position);
            nextLunge = Time.time + 3f;
            nextAttack = Time.time + AttackCooldown;
            if (stun) ServerStun(senses.StunSeconds);
        }

        // Victim (owner): alternate A and D to shake it off.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ShakeRpc(RpcParams rpcParams = default)
        {
            if (Mode.Value != CreatureMode.Latched || victim == null || rpcParams.Receive.SenderClientId != victim.OwnerClientId) return;
            if (++shakes < ShakesToEscape) return;
            ServerRelease(true, victim.transform.right * (Random.value < 0.5f ? -1f : 1f));
        }

        // ---------- every peer ----------

        // The spider's ticking (faster and louder on someone's back) and the victim's A / D shaking.
        private void CacheRestPose()
        {
            if (restCached) return;
            deathVisual = CreatureVisual != null ? CreatureVisual.transform : transform.Find("Visual");
            if (deathVisual == null) return;
            restPosition = deathVisual.localPosition;
            restRotation = deathVisual.localRotation;
            restScale = deathVisual.localScale;
            restCached = true;
        }

        // Every peer: the dead creature keels over on its side, lies still a moment, then sinks and shrinks away.
        private void UpdateDeathFx()
        {
            if (!IsSpawned || Mode.Value != CreatureMode.Dead || deathStart < 0f) return;
            CacheRestPose();
            if (deathVisual == null || !deathVisual.gameObject.activeSelf) return;
            var t = Time.time - deathStart;
            var fall = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / DeathFall));
            var fade = Mathf.Clamp01((t - DeathFall - DeathLinger) / DeathFade);
            deathVisual.localRotation = Quaternion.AngleAxis(88f * fall, Vector3.forward) * restRotation;
            deathVisual.localPosition = restPosition + Vector3.down * (0.35f * fade);
            deathVisual.localScale = restScale * Mathf.Lerp(1f, 0.02f, fade * fade);
            if (fade >= 1f) deathVisual.gameObject.SetActive(false);
        }

        private void UpdatePeerFx()
        {
            UpdateDeathFx();
            if (!IsSpawned || Kind != CreatureKind.ClockSpider) return;
            var latched = Mode.Value == CreatureMode.Latched;
            if (PoweredDown) return;
            if (Time.time >= nextTick && Time.time >= silentUntil) // (silent just before it drops from the ceiling)
            {
                var left = latched ? Mathf.Clamp01((float)(LatchEndsAt.Value - NetTime) / LatchSeconds) : 1f;
                nextTick = Time.time + (latched ? Mathf.Lerp(0.08f, 0.45f, left) : 0.6f);
                ProtoAudio.PlayPitched(ProtoSound.Click, transform.position + Vector3.up * 0.8f, latched ? 0.35f : 0.12f, 1.6f);
            }
            var me = PlayerInteractor.Local;
            if (!latched || me == null || LatchedTo.Value != me.NetworkObjectId || !FirstPersonController.GameplayInputReady) return;
            var k = Keyboard.current;
            if (k == null) return;
            var a = k.aKey.wasPressedThisFrame;
            var d = k.dKey.wasPressedThisFrame;
            if (!a && !d) return;
            var pressedLeft = a;
            if (!shakeAny || pressedLeft != shakeLeft) ShakeRpc();
            shakeAny = true;
            shakeLeft = pressedLeft;
        }

        // ---------- effects ----------

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        private void WindupFxRpc() => ProtoAudio.Play(ProtoSound.StingerStatic, transform.position + Vector3.up, 0.8f);

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        private void ImpactFxRpc()
        {
            ProtoAudio.Play(ProtoSound.Shatter, transform.position + Vector3.up, 0.8f);
            ProtoAudio.Play(ProtoSound.Bang, transform.position + Vector3.up, 0.6f);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        private void GustFxRpc()
        {
            ProtoAudio.Play(ProtoSound.StingerBreath, transform.position + Vector3.up, 0.9f);
            ProtoAudio.PlayPitched(ProtoSound.Rumble, transform.position + Vector3.up, 0.6f, 1.4f);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        private void AlarmFxRpc(bool muffled) =>
            ProtoAudio.PlayPitched(ProtoSound.Alarm, transform.position + Vector3.up, muffled ? 0.35f : 1f, muffled ? 0.8f : 1.15f);

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        private void LatchFxRpc(ulong victimClient)
        {
            ProtoAudio.Play(ProtoSound.MonsterHit, transform.position, 0.7f);
            if (NetworkManager != null && victimClient == NetworkManager.LocalClientId) CrashIntro.Scare(0.45f, 0.8f);
        }
    }
}
