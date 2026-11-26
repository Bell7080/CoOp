using System;
using CoOpHorror.Player;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // Plays the Blender-made clips (Idle / Walk / Run or Chase / Crouch / Attack) on a rigged model from what
    // every peer can see: how fast the root actually moves, the replicated crouch flag (players) and the
    // replicated chase flag (monsters). Floppy chains (the mole's nose and arms) get spring lag on top of the
    // clip in LateUpdate. Purely presentational; never writes gameplay state.
    public sealed class RigAnimator : MonoBehaviour
    {
        [Serializable]
        public struct FloppyChain
        {
            public Transform[] Bones;   // root first; each bone's child is its tip
            public float Stiffness;     // spring strength (higher = follows the clip more tightly)
            public float Damping;
            public float Gravity;       // extra droop, metres/s²
        }

        public Animator Animator;
        public Transform MotionRoot;    // whose movement drives the clips (defaults to the parent object)
        public string IdleState = "Idle";
        public string MoveState = "Walk";
        public string FastState = "Run";
        public string CrouchState = "";
        public string AttackState = "";
        public float MoveSpeed = 0.25f;     // m/s above which the move clip plays
        public float FastSpeed = 4.5f;      // m/s above which the fast clip plays
        public float ClipSpeedPerMetre = 0.45f; // move clip playback speed per m/s (1 / stride length per cycle)
        public float FastClipSpeedPerMetre = -1f; // the fast clip's own stride; -1 = same as the move clip
        // Steps: a thump per footfall (contacts per clip cycle), e.g. the TV's hops. 0 = silent.
        public int StepsPerCycle;
        public float StepVolume = 0.35f;
        private float stepPhase;
        public float Fade = 0.15f;
        public FloppyChain[] Floppy;

        private FirstPersonController player;
        private TempMonster monster;
        private Vector3 last;
        private float speed;
        private string current;
        private string forced;
        private float forcedUntil;

        // Plays a state for a moment over the movement-driven choice (attack bursts).
        public void PlayOnce(string state, float seconds)
        {
            if (string.IsNullOrEmpty(state) || Animator == null) return;
            forced = state;
            forcedUntil = Time.time + seconds;
            current = state;
            Animator.CrossFadeInFixedTime(state, 0.05f, 0, 0f);
        }

        private Vector3[][] tipPositions;
        private Vector3[][] tipVelocities;

        private void Awake()
        {
            if (MotionRoot == null) MotionRoot = transform.parent != null ? transform.root : transform;
            player = MotionRoot.GetComponent<FirstPersonController>();
            monster = MotionRoot.GetComponent<TempMonster>();
            if (Animator == null) Animator = GetComponentInChildren<Animator>();
            last = MotionRoot.position;
        }

        private void OnEnable()
        {
            current = null;
            tipPositions = null;
        }

        private void Update()
        {
            if (Animator == null || Animator.runtimeAnimatorController == null) return;
            var dt = Mathf.Max(Time.deltaTime, 0.0001f);
            var moved = MotionRoot.position - last;
            last = MotionRoot.position;
            var flat = new Vector2(moved.x, moved.z).magnitude / dt;
            if (flat > 30f) flat = 0f; // teleports
            speed = Mathf.Lerp(speed, flat, 1f - Mathf.Exp(-10f * dt));

            var crouched = player != null && player.IsSpawned && player.Crouched.Value;
            var chasing = monster != null && monster.IsSpawned && monster.Chasing.Value;
            string next;
            if (Time.time < forcedUntil) { Animator.speed = 1f; return; }
            if (crouched && !string.IsNullOrEmpty(CrouchState)) next = CrouchState;
            else if (chasing && speed < MoveSpeed && !string.IsNullOrEmpty(AttackState)) next = AttackState;
            else if (speed < MoveSpeed) next = IdleState;
            else if (speed > FastSpeed || chasing) next = FastState;
            else next = MoveState;

            if (next != current)
            {
                current = next;
                Animator.CrossFadeInFixedTime(next, Fade);
            }
            var moving = next == MoveState || next == FastState;
            // Playback follows the ground speed so hands and feet don't skate.
            var perMetre = next == FastState && FastClipSpeedPerMetre > 0f ? FastClipSpeedPerMetre : ClipSpeedPerMetre;
            Animator.speed = moving ? Mathf.Clamp(speed * perMetre, 0.4f, 2.6f) : crouched ? Mathf.Clamp(0.5f + speed * 0.6f, 0.5f, 2f) : 1f;
            if (StepsPerCycle > 0 && moving)
            {
                var state = Animator.GetCurrentAnimatorStateInfo(0);
                var cycles = state.normalizedTime * StepsPerCycle;
                if (Mathf.Floor(cycles) != Mathf.Floor(stepPhase)) ProtoAudio.PlayPitched(ProtoSound.Clunk, MotionRoot.position, StepVolume, next == FastState ? 1.15f : 0.85f);
                stepPhase = cycles;
            }
        }

        // Spring lag: each bone's tip trails its animated position and the bone turns to follow it.
        private void LateUpdate()
        {
            if (Floppy == null || Floppy.Length == 0) return;
            var dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (dt <= 0f) return;
            if (tipPositions == null || tipPositions.Length != Floppy.Length)
            {
                tipPositions = new Vector3[Floppy.Length][];
                tipVelocities = new Vector3[Floppy.Length][];
            }
            for (var c = 0; c < Floppy.Length; c++)
            {
                var chain = Floppy[c];
                if (chain.Bones == null) continue;
                if (tipPositions[c] == null || tipPositions[c].Length != chain.Bones.Length)
                {
                    tipPositions[c] = new Vector3[chain.Bones.Length];
                    tipVelocities[c] = new Vector3[chain.Bones.Length];
                    for (var i = 0; i < chain.Bones.Length; i++) tipPositions[c][i] = Tip(chain.Bones[i]);
                }
                for (var i = 0; i < chain.Bones.Length; i++)
                {
                    var bone = chain.Bones[i];
                    if (bone == null) continue;
                    var target = Tip(bone);
                    var p = tipPositions[c][i];
                    var v = tipVelocities[c][i];
                    v += ((target - p) * chain.Stiffness + Vector3.down * chain.Gravity) * dt;
                    v *= Mathf.Exp(-chain.Damping * dt);
                    p += v * dt;
                    // keep the bone's length
                    var length = Vector3.Distance(bone.position, target);
                    p = bone.position + (p - bone.position).normalized * length;
                    tipPositions[c][i] = p;
                    tipVelocities[c][i] = v;
                    var from = target - bone.position;
                    var to = p - bone.position;
                    if (from.sqrMagnitude > 1e-8f && to.sqrMagnitude > 1e-8f)
                        bone.rotation = Quaternion.FromToRotation(from, to) * bone.rotation;
                }
            }
        }

        private static Vector3 Tip(Transform bone)
        {
            if (bone.childCount > 0) return bone.GetChild(0).position;
            return bone.position + bone.up * 0.1f; // FBX bones point along local Y
        }
    }
}
