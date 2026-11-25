using System.Collections.Generic;
using CoOpHorror.Player;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.AI;

namespace CoOpHorror.Gameplay
{
    // Placeholder monster, host only: patrols waypoints on the NavMesh, chases the nearest player it can see,
    // walks over to loud noises it hears, and body-slams players it touches.
    // The NavMeshAgent runs on the host only; a server-authority NetworkTransform replicates it.
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class TempMonster : NetworkBehaviour
    {
        public const float PatrolSpeed = 1.8f;
        public const float InvestigateSpeed = 2.8f;
        public const float ChaseSpeed = 3.3f;   // slower than walking (4) so players can escape
        public const float SightRange = 14f;
        public const float LoseTargetAfter = 4f;
        public const float LingerAtNoise = 3f;  // seconds spent looking around where it heard something
        public const float AttackRange = 1.6f;
        public const float AttackCooldown = 1.2f;
        public const int AttackDamage = 20;

        public static TempMonster Instance { get; private set; }

        public Transform[] Waypoints;
        public Renderer[] Eyes;

        public readonly NetworkVariable<bool> Chasing = new(false);

        private NavMeshAgent agent;
        private NetworkTransform sync;
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
        private MaterialPropertyBlock block;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        private void Awake()
        {
            Instance = this;
            agent = GetComponent<NavMeshAgent>();
            agent.enabled = false; // host enables it after the spawn teleport
            sync = GetComponent<NetworkTransform>();
            homePosition = transform.position;
            homeRotation = transform.rotation;
            block = new MaterialPropertyBlock();
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Noise.Server -= OnNoise;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            target = null;
            investigating = false;
            waypoint = 0;
            nextAttack = 0f;
            Chasing.Value = false;
            resetPending = true; // teleport home once the NetworkTransform can commit
            Noise.Server += OnNoise;
        }

        public override void OnNetworkDespawn()
        {
            Noise.Server -= OnNoise;
            if (agent != null) agent.enabled = false;
        }

        private void OnNoise(Vector3 position, float radius)
        {
            if (target != null) return; // already chasing someone
            if ((position - transform.position).sqrMagnitude > radius * radius) return;
            investigating = true;
            noisePosition = position;
            lingerUntil = 0f;
        }

        private void Update()
        {
            UpdateEyes();
            if (!IsServer || !IsSpawned) return;

            if (resetPending)
            {
                if (sync != null && !sync.CanCommitToTransform) return;
                transform.SetPositionAndRotation(homePosition, homeRotation);
                if (sync != null) sync.Teleport(homePosition, homeRotation, transform.localScale);
                agent.enabled = true;
                agent.Warp(homePosition);
                resetPending = false;
            }
            if (!agent.enabled || !agent.isOnNavMesh) return;

            if (Time.time >= nextScan)
            {
                nextScan = Time.time + 0.15f;
                var seen = FindVisiblePlayer();
                if (seen != null)
                {
                    target = seen;
                    lastSeenTime = Time.time;
                    lastSeenPosition = seen.transform.position;
                    investigating = false;
                }
            }
            if (target != null && (!target.IsSpawned || target.IsDead.Value || Time.time - lastSeenTime > LoseTargetAfter))
            {
                // Lost them: check where they were last seen before going back to patrol.
                if (target != null && !target.IsDead.Value) { investigating = true; noisePosition = lastSeenPosition; lingerUntil = 0f; }
                target = null;
            }
            if (Chasing.Value != (target != null)) Chasing.Value = target != null;

            if (target != null)
            {
                agent.speed = ChaseSpeed;
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
            else if (Waypoints != null && Waypoints.Length > 0)
            {
                agent.speed = PatrolSpeed;
                MoveTo(Waypoints[waypoint].position);
                if (Arrived(1.2f)) waypoint = (waypoint + 1) % Waypoints.Length;
            }

            TryAttack();
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

        private PlayerHealth FindVisiblePlayer()
        {
            PlayerHealth best = null;
            var bestDistance = SightRange * SightRange;
            var eye = transform.position + Vector3.up * 0.8f;
            foreach (var p in PlayerHealth.All)
            {
                if (p == null || !p.IsSpawned || p.IsDead.Value) continue;
                var look = p.transform.position + Vector3.up * 0.6f;
                var distance = (look - eye).sqrMagnitude;
                if (distance > bestDistance) continue;
                // The ray starts inside the monster's own capsule, which raycasts ignore.
                if (Physics.Linecast(eye, look, out var hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && hit.collider.GetComponentInParent<PlayerHealth>() != p) continue;
                best = p;
                bestDistance = distance;
            }
            return best;
        }

        // Body slam.
        private void TryAttack()
        {
            if (Time.time < nextAttack) return;
            foreach (var p in PlayerHealth.All)
            {
                if (p == null || !p.IsSpawned || p.IsDead.Value) continue;
                var offset = p.transform.position - transform.position;
                if (Flat(offset).magnitude > AttackRange || Mathf.Abs(offset.y) > 1.6f) continue;
                var push = Flat(offset).normalized * 6f + Vector3.up * 3f;
                p.ServerApplyDamage(AttackDamage, push);
                nextAttack = Time.time + AttackCooldown;
                return;
            }
        }

        private void UpdateEyes()
        {
            if (Eyes == null) return;
            var chasing = IsSpawned && Chasing.Value;
            var color = chasing ? new Color(1f, 0.1f, 0.05f) * (1f + Mathf.PingPong(Time.time * 5f, 0.6f)) : new Color(0.55f, 0.08f, 0.05f);
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
