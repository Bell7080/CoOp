using System.Collections.Generic;
using CoOpHorror.Player;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.AI;

namespace CoOpHorror.Gameplay
{
    public enum CreatureKind : byte { Generic, TvWalker, FanCrawler, ClockSpider }

    // Active: the usual brain. Stunned: stopped for a moment (TV after a wall hit, fan after its gust, spider
    // after being torn off). Dead: its power (health) drained to nothing. Disguised: the fan sitting on a side
    // table as a desk fan. Latched: the clock spider clinging to a player's back.
    public enum CreatureMode : byte { Active, Stunned, Dead, Disguised, Latched }

    // Monster brain, host only: patrols waypoints on the NavMesh, chases the nearest player it can see,
    // walks over to noises it hears, and body-slams players it touches. The common brain lives in this file;
    // each creature's own senses and gimmicks are in TempMonster.Creatures.cs (TV walker: light and a straight
    // charge; fan crawler: hearing, a floor disguise and a gust; clock spider: an alarm and clinging to backs),
    // and draining a stunned or sleeping creature with the nose is in TempMonster.Power.cs.
    // Players squeezed under furniture are only noticed right next to them (the spider excepted). The
    // NavMeshAgent runs on the host only; a server-authority NetworkTransform replicates it. The root pivot is
    // at the creature's feet.
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed partial class TempMonster : Interactable, INoseTarget
    {
        public const float TuckedSightRange = 2.2f;
        public const float LoseTargetAfter = 3f;
        public const float LoseHiddenAfter = 1.2f;  // crouched out of its view, behind cover, or (TV) in the dark: gives up fast
        public const float ViewHalfAngle = 60f;     // default view cone; each creature's senses may widen it
        public const float SenseRange = 2f;         // right next to it, it notices you whichever way it faces
        public const float LingerAtNoise = 3f;  // seconds spent looking around where it heard something
        public const float AttackCooldown = 1.2f;
        public const float RecognitionSeconds = 0.6f;
        public const ulong NoVictim = ulong.MaxValue;

        // Which creature this brain drives. Scenes built before this field existed infer it from the object's
        // name (Creature_TvWalker, Creature_FanCrawler, Creature_ClockSpider).
        public CreatureKind Kind;

        public float PatrolSpeed = 1.8f;
        public float InvestigateSpeed = 2.8f;
        public float ChaseSpeed = 3.3f;         // below running speed (7) so players can escape
        public float SightRange = 14f;
        public float EyeHeight = 1.2f;
        public float AttackRange = 1.6f;
        public int AttackDamage = 20;
        // Lunge / charge: when it sees its target this close it rushes in a burst (fan: face-first jump scare,
        // TV: head-first ram, spider: a leap onto your back), then needs a breather. Hits send the player flying.
        public float LungeRange = 5f;
        public float LungeSpeed = 7f;
        public float LungeSeconds = 0.8f;
        public float LungeCooldown = 3f;
        public float Knockback = 9f;
        public float KnockUp = 4f;

        public static readonly List<TempMonster> All = new();
        public static TempMonster Instance => All.Count > 0 ? All[0] : null;

        // End-to-end tests freeze the monster so the scripted flow is deterministic (host only).
        public static bool Frozen { get; set; }
        public bool Investigating => investigating;
        public Vector3 InvestigatePoint => noisePosition;
        public Vector3 HomePosition => homePosition;

        public Transform[] Waypoints;
        public Renderer[] Eyes;

        // The fan crawler starts the stage as an ordinary-looking desk fan on a side table (DisguiseVisual shown,
        // CreatureVisual hidden, no NavMeshAgent) and only turns into the creature when a nose drinks from it.
        public bool StartsDisguised;
        public GameObject DisguiseVisual;
        public GameObject CreatureVisual;

        public readonly NetworkVariable<bool> Chasing = new(false);
        public readonly NetworkVariable<bool> Recognizing = new(false);
        public readonly NetworkVariable<CreatureMode> Mode = new(CreatureMode.Active);
        public readonly NetworkVariable<float> Power = new(0f);              // its health, in watts (TempMonster.Power)
        public readonly NetworkVariable<ulong> LatchedTo = new(NoVictim);   // player object the spider clings to
        public readonly NetworkVariable<double> LatchEndsAt = new(0d);      // when its alarm goes off (server time)
        private float recognizeUntil;

        private NavMeshAgent agent;
        private NetworkTransform sync;
        private CapsuleCollider body;
        private float bodyRadius, bodyHeight;
        private Vector3 bodyCenter;
        private Vector3 homePosition;
        private Quaternion homeRotation;
        private bool resetPending;
        private int waypoint;
        private PlayerHealth target;
        private float lastSeenTime;
        private Vector3 lastSeenPosition;
        private bool investigating;
        private Vector3 noisePosition;
        private float lingerUntil;
        private float nextAttack;
        private float nextScan;
        private float nextRepath;
        private Vector3 lastGoal = new(float.MaxValue, 0f, 0f);
        private bool targetHidden;
        private float lungeUntil, nextLunge;
        public bool Lunging => Time.time < lungeUntil;
        private MaterialPropertyBlock block;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        protected override void Awake()
        {
            base.Awake();
            All.Add(this);
            if (Kind == CreatureKind.Generic) Kind = KindFromName(name);
            senses = CreatureSenses.For(Kind);
            senses.Tune(this);
            agent = GetComponent<NavMeshAgent>();
            agent.enabled = false; // host enables it after the spawn teleport
            sync = GetComponent<NetworkTransform>();
            body = GetComponent<CapsuleCollider>();
            if (body != null) { bodyRadius = body.radius; bodyHeight = body.height; bodyCenter = body.center; }
            homePosition = transform.position;
            homeRotation = transform.rotation;
            block = new MaterialPropertyBlock();
        }

        public override void OnDestroy()
        {
            All.Remove(this);
            Noise.Server -= OnNoise;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            Mode.OnValueChanged += OnModeChanged;
            OnModeChanged(CreatureMode.Active, Mode.Value);
            if (!IsServer) return;
            target = null;
            investigating = false;
            waypoint = 0;
            nextAttack = 0f;
            Chasing.Value = false;
            Recognizing.Value = false;
            ServerResetCreature(StartsDisguised);
            resetPending = true; // teleport home once the NetworkTransform can commit
            Noise.Server += OnNoise;
        }

        public override void OnNetworkDespawn()
        {
            Mode.OnValueChanged -= OnModeChanged;
            Noise.Server -= OnNoise;
            if (agent != null) agent.enabled = false;
        }

        // End-to-end tests: put the monster somewhere (host only).
        public void ServerTeleport(Vector3 position)
        {
            if (!IsServer || !IsSpawned) return;
            if (Mode.Value == CreatureMode.Latched) ServerRelease(false, Vector3.zero);
            if (!agent.enabled)
            {
                if (Mode.Value == CreatureMode.Disguised) ServerWake(false); // (drops it to the floor first)
                if (!agent.enabled) return;
            }
            agent.Warp(position);
            if (sync != null && sync.CanCommitToTransform) sync.Teleport(transform.position, transform.rotation, transform.localScale);
            investigating = false;
            target = null;
            Recognizing.Value = false;
            Chasing.Value = false;
            lungeUntil = 0f;
            nextRepath = 0f;
            ServerResetCreature(false);
        }

        // Heard something (a drained appliance crackling, footsteps, a door, a radio, an alarm): walk over to it.
        // How far it hears depends on the kind of noise and the creature (CreatureSenses). Once there it looks
        // around and the usual sight check takes over, so whoever made it gets spotted and chased.
        private void OnNoise(Vector3 position, float radius, NoiseKind kind)
        {
            var scale = senses.Hearing(kind);
            if (scale <= 0f) return;
            radius *= scale;
            if ((position - transform.position).sqrMagnitude > radius * radius) return;
            if (Mode.Value != CreatureMode.Active || ringUntil > Time.time) return; // (a disguised fan only wakes when drunk from)
            if (target != null)
            {
                // A hunter by ear keeps track of a target who is still making noise (running behind a wall is
                // not hiding; crouching is silent).
                if (senses.Persistent && kind != NoiseKind.Drain && kind != NoiseKind.Alarm
                    && (position - target.transform.position).sqrMagnitude < 9f)
                {
                    lastSeenTime = Time.time;
                    lastSeenPosition = target.transform.position;
                }
                return; // already chasing someone
            }
            investigating = true;
            noisePosition = position;
            lingerUntil = 0f;
        }

        private void Update()
        {
            UpdateEyes();
            UpdatePeerFx();
            if (!IsServer || !IsSpawned) return;

            if (resetPending)
            {
                if (sync != null && !sync.CanCommitToTransform) return;
                transform.SetPositionAndRotation(homePosition, homeRotation);
                if (sync != null) sync.Teleport(homePosition, homeRotation, transform.localScale);
                resetPending = false;
                if (Mode.Value == CreatureMode.Disguised) return; // sits on its table until a nose drinks from it
                agent.enabled = true;
                agent.Warp(homePosition);
            }
            if (Mode.Value == CreatureMode.Dead) return;
            if (Mode.Value == CreatureMode.Latched)
            {
                if (!Frozen) ServerTickLatched();
                return;
            }
            if (!agent.enabled || !agent.isOnNavMesh) return;
            agent.isStopped = Frozen;
            if (Frozen) return;
            if (ServerTickBusy()) return; // stunned, asleep, disguised, ringing, winding up, blowing

            if (Time.time >= nextScan)
            {
                nextScan = Time.time + 0.15f;
                var seen = FindVisiblePlayer(out var hidden);
                targetHidden = hidden;
                if (seen != null)
                {
                    if (target == null)
                    {
                        recognizeUntil = Time.time + RecognitionSeconds;
                        Recognizing.Value = true;
                        lungeUntil = 0f;
                        NoticeFxRpc();
                    }
                    target = seen;
                    lastSeenTime = Time.time;
                    lastSeenPosition = seen.transform.position;
                    investigating = false;
                }
                else if (target == null) ServerNoticeBeams();
            }
            if (target != null && (!target.IsSpawned || target.IsDead.Value || Time.time - lastSeenTime > (targetHidden ? senses.HiddenGiveUp : LoseTargetAfter)))
            {
                // Lost them: check where they were last seen before going back to patrol.
                if (target != null && !target.IsDead.Value) { investigating = true; noisePosition = lastSeenPosition; lingerUntil = 0f; }
                target = null;
            }
            // A brief continuous sight cue precedes pursuit; cover can interrupt recognition.
            if (Recognizing.Value && (target == null || Time.time - lastSeenTime > 0.3f))
            {
                Recognizing.Value = false;
                target = null;
            }
            if (Recognizing.Value && Time.time >= recognizeUntil) Recognizing.Value = false;
            var chasing = target != null && !Recognizing.Value;
            if (Chasing.Value != chasing) Chasing.Value = chasing;
            if (Recognizing.Value)
            {
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
                FaceTowards(target.transform.position, 180f);
                return;
            }

            if (ServerTickCharge()) { TryAttack(); return; } // the TV's straight charge steers itself

            if (target != null)
            {
                var gap = (target.transform.position - transform.position).magnitude;
                if (!Lunging && Time.time >= nextLunge && Time.time - lastSeenTime < 0.3f && gap < LungeRange) ServerStartLunge();
                if (windupUntil > 0f) return; // (TV) winding up from the next frame on
                agent.speed = Lunging ? LungeSpeed : ChaseSpeed;
                MoveTo(Time.time - lastSeenTime < 0.3f ? target.transform.position : lastSeenPosition);
            }
            else if (investigating)
            {
                agent.speed = InvestigateSpeed;
                MoveTo(noisePosition);
                if (Arrived(1.5f))
                {
                    if (lingerUntil <= 0f) lingerUntil = Time.time + LingerAtNoise;
                    transform.Rotate(0f, 90f * Time.deltaTime, 0f);
                    if (Time.time >= lingerUntil) investigating = false;
                }
            }
            else if (ServerHoldStill())
            {
                // (the fan in the blackout: stays put and only listens)
            }
            else if (Waypoints != null && Waypoints.Length > 0)
            {
                agent.speed = PatrolSpeed;
                MoveTo(Waypoints[waypoint].position);
                if (Arrived(1.2f)) waypoint = (waypoint + 1) % Waypoints.Length;
            }

            if (Mode.Value == CreatureMode.Active) TryAttack();
            ServerAfterMove();
        }

        private void MoveTo(Vector3 goal)
        {
            if (Time.time < nextRepath && (goal - lastGoal).sqrMagnitude < 0.25f) return;
            nextRepath = Time.time + 0.25f;
            lastGoal = goal;
            if (NavMesh.SamplePosition(goal, out var hit, 2.5f, NavMesh.AllAreas)) agent.SetDestination(hit.position);
            else if (Waypoints != null && Waypoints.Length > 0 && !investigating && target == null) waypoint = (waypoint + 1) % Waypoints.Length;
            else investigating = false; // unreachable noise
        }

        private bool Arrived(float distance) =>
            !agent.pathPending && agent.remainingDistance <= distance;

        private void FaceTowards(Vector3 point, float degreesPerSecond)
        {
            var direction = Flat(point - transform.position);
            if (direction.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), degreesPerSecond * Time.deltaTime);
        }

        // Host: stop where it stands (stunned, asleep, disguised, ringing, winding up).
        private void HaltAgent()
        {
            if (!agent.enabled || !agent.isOnNavMesh) return;
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
            if (agent.hasPath) agent.ResetPath();
            nextRepath = 0f;
        }

        // Sight: line of sight to the player's body, inside its view cone (or right next to it). While it is
        // already chasing someone it keeps track of them outside the cone too, unless they crouch: ducking out
        // of its view or slipping behind a shelf makes it lose them after LoseHiddenAfter. "hidden" reports
        // whether the current target is hiding like that (used for the shorter give-up time).
        // Creature differences (CreatureSenses): the TV only sees far into lit places, the spider looks wider
        // and finds players under furniture.
        private PlayerHealth FindVisiblePlayer(out bool hidden)
        {
            hidden = false;
            PlayerHealth best = null;
            var bestDistance = SightRange * SightRange;
            var eye = transform.position + Vector3.up * EyeHeight;
            foreach (var p in PlayerHealth.All)
            {
                if (p == null || !p.IsSpawned || p.IsDead.Value) continue;
                p.TryGetComponent<FirstPersonController>(out var playerBody);
                var crouched = playerBody != null && playerBody.IsSpawned && playerBody.Crouched.Value;
                // Root sits about 1 m above the feet; aim at the middle of the (crouched) body.
                var look = p.transform.position + Vector3.up * (crouched ? -0.65f : -0.25f);
                var distance = (look - eye).sqrMagnitude;
                var isTarget = p == target;
                // A persistent hunter (the fan) keeps its target beyond its sight range until they really hide.
                var tracking = isTarget && senses.Persistent && distance < senses.TrackRange * senses.TrackRange;
                if (distance > bestDistance && !tracking) continue;
                // The ray starts inside the monster's own capsule, which raycasts ignore.
                if (Physics.Linecast(eye, look, out var hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && hit.collider.GetComponentInParent<PlayerHealth>() != p)
                {
                    if (isTarget) hidden = true; // behind cover
                    continue;
                }
                // Squeezed under furniture: only noticed right next to it (the spider crawls in after you).
                if (!senses.SeesTucked && distance > TuckedSightRange * TuckedSightRange && playerBody != null && playerBody.IsTucked())
                {
                    if (isTarget) hidden = true;
                    continue;
                }
                var near = distance < SenseRange * SenseRange;
                // The TV only makes out players far away when light is on them.
                if (senses.NeedsLight && !near && distance > senses.DarkSight * senses.DarkSight && !IsLit(p))
                {
                    if (isTarget) hidden = true;
                    continue;
                }
                var flat = Flat(look - transform.position);
                var inCone = flat.sqrMagnitude < 0.01f || Vector3.Angle(transform.forward, flat) <= senses.ViewHalfAngle;
                if (!near && !inCone)
                {
                    if (!isTarget) continue;                    // not looking that way
                    if (crouched) { hidden = true; continue; }  // ducked out of its view
                }
                best = p;
                bestDistance = distance;
            }
            if (best != null && best == target) hidden = senses.NeedsLight && !IsLit(best); // the TV loses you fast in the dark
            return best;
        }

        // Body slam (the TV hits everyone in its charge line, the spider clings on instead).
        private void TryAttack()
        {
            if (Kind == CreatureKind.TvWalker && Lunging) { ServerChargeHits(); return; }
            if (!Chasing.Value || Recognizing.Value) return;
            if (Time.time < nextAttack) return;
            foreach (var p in PlayerHealth.All)
            {
                if (p == null || !p.IsSpawned || p.IsDead.Value) continue;
                var offset = p.transform.position - transform.position;
                if (Flat(offset).magnitude > AttackRange || Mathf.Abs(offset.y) > 1.6f) continue;
                if (Kind == CreatureKind.ClockSpider) { ServerLatch(p); return; }
                Slam(p, offset);
                lungeUntil = 0f;
                nextAttack = Time.time + AttackCooldown;
                return;
            }
        }

        private void Slam(PlayerHealth p, Vector3 offset)
        {
            var push = Flat(offset).normalized * Knockback + Vector3.up * KnockUp;
            p.ServerApplyDamage(AttackDamage, push);
            HitFxRpc(p.OwnerClientId);
            // The hit spills part of the victim's energy tank.
            if (p.TryGetComponent<PlayerNose>(out var tank)) tank.ServerLeak(PlayerNose.LeakOnHit);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        private void NoticeFxRpc() => ProtoAudio.Play(ProtoSound.MonsterNotice, transform.position, 0.7f);

        // Every peer: start of a lunge (animation burst + sound).
        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        private void LungeFxRpc()
        {
            ProtoAudio.Play(ProtoSound.MonsterLunge, transform.position, 0.85f);
            var rig = GetComponentInChildren<RigAnimator>();
            if (rig != null) rig.PlayOnce(rig.AttackState, 0.8f);
        }

        // Every peer: the hit; the victim gets the jump-scare (shake, flash, bang).
        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        private void HitFxRpc(ulong victim)
        {
            ProtoAudio.Play(ProtoSound.MonsterHit, transform.position, 0.85f);
            var rig = GetComponentInChildren<RigAnimator>();
            if (rig != null) rig.PlayOnce(rig.AttackState, 0.6f);
            if (NetworkManager != null && victim == NetworkManager.LocalClientId) CrashIntro.Scare(0.35f, 0.6f);
        }

        private void UpdateEyes()
        {
            if (Eyes == null) return;
            var chasing = IsSpawned && Chasing.Value;
            var color = chasing ? new Color(1f, 0.1f, 0.05f) * (1f + Mathf.PingPong(Time.time * 5f, 0.6f)) : new Color(0.55f, 0.08f, 0.05f);
            if (IsSpawned && Recognizing.Value) color = new Color(1f, 0.52f, 0.08f);
            if (PoweredDown) color = new Color(0.03f, 0.01f, 0.01f);
            else if (IsSpawned && Mode.Value == CreatureMode.Stunned) color = new Color(0.55f, 0.08f, 0.05f) * Mathf.PerlinNoise(Time.time * 14f, 0.3f);
            foreach (var eye in Eyes)
            {
                if (eye == null) continue;
                eye.GetPropertyBlock(block);
                block.SetColor(BaseColor, color);
                eye.SetPropertyBlock(block);
            }
        }

        private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);
    }
}
