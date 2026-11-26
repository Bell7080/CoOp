using CoOpHorror.Localization;
using CoOpHorror.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // An ordinary hinged door, single leaf or a double door. Bodies push it open just by walking into it
    // (players and monsters alike, so a creature barges through), and a click swings it open away from you or
    // shuts it. One-way doors only open towards +Z of the door. Host-authoritative angles; every peer animates
    // the leaves (their colliders block players). Monsters' paths ignore the leaves: they shove them aside.
    public sealed class SwingDoor : Interactable
    {
        public const float MaxAngle = 100f;
        public const float PushStrength = 140f;   // degrees per second per m/s of walking into it
        public const float ClickSpeed = 260f;     // degrees per second when opened/closed by hand

        public Transform LeafA, LeafB;            // pivots at the hinges; leaves extend along -SideA*X / -SideB*X
        public float SideA = 1f, SideB = -1f;     // which jamb each hinge is on (+X or -X)
        public float LeafWidth = 1.5f;
        public bool OneWay;

        public readonly NetworkVariable<float> AngleA = new(0f);
        public readonly NetworkVariable<float> AngleB = new(0f);

        private float targetA = float.NaN, targetB = float.NaN;
        private float shownA, shownB;

        public override InteractKind Describe(PlayerInteractor who, out string prompt)
        {
            prompt = GameText.Get(IsOpen ? "interact.doorSwingClose" : "interact.doorSwingOpen");
            return InteractKind.Press;
        }

        private bool IsOpen => Mathf.Abs(AngleA.Value) > 15f || (LeafB != null && Mathf.Abs(AngleB.Value) > 15f);

        public override void ServerUse(PlayerInteractor who)
        {
            if (IsOpen) { targetA = 0f; targetB = 0f; return; }
            // swing away from whoever opened it
            var away = Vector3.Dot(who.transform.position - transform.position, transform.forward) > 0f ? -1f : 1f;
            if (OneWay) away = 1f;
            targetA = OpenAngle(SideA, away);
            targetB = OpenAngle(SideB, away);
        }

        // Rotating a leaf by +angle about Y turns its free end towards +Z for a hinge on +X (towards -Z for -X).
        private float OpenAngle(float side, float towardsZ) => side * towardsZ * 90f;

        private void FixedUpdate()
        {
            if (!IsServer || !IsSpawned) return;
            var dt = Time.fixedDeltaTime;
            AngleA.Value = Step(LeafA, SideA, AngleA.Value, ref targetA, dt);
            if (LeafB != null) AngleB.Value = Step(LeafB, SideB, AngleB.Value, ref targetB, dt);
        }

        private float Step(Transform leaf, float side, float angle, ref float target, float dt)
        {
            if (leaf == null) return angle;
            var hinge = leaf.position;
            var along = leaf.rotation * new Vector3(-side, 0f, 0f);           // hinge -> free end
            var normal = Vector3.Cross(Vector3.up, along).normalized;
            var push = 0f;
            void Pusher(Vector3 position, Vector3 velocity)
            {
                var r = position - hinge;
                r.y = 0f;
                var s = Vector3.Dot(r, along);
                var n = Vector3.Dot(r, normal);
                if (s < -0.2f || s > LeafWidth + 0.35f || Mathf.Abs(n) > 0.75f) return;
                var into = -Mathf.Sign(n) * Vector3.Dot(velocity, normal);     // speed towards the leaf
                var shove = Mathf.Max(into, 0f) + (Mathf.Abs(n) < 0.4f ? 1.2f : 0f); // pressed against it
                if (shove <= 0f) return;
                // turning direction: away from the pusher's side
                push += -Mathf.Sign(n) * shove * PushStrength / Mathf.Max(0.5f, s + 0.3f);
            }
            foreach (var p in PlayerHealth.All)
                if (p != null && p.IsSpawned && !p.IsDead.Value && p.TryGetComponent<PlayerInteractor>(out var pi))
                    Pusher(p.transform.position + Vector3.down * 0.5f, pi.MotionVelocity);
            foreach (var m in TempMonster.All)
                if (m != null && m.IsSpawned && m.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var agent))
                    Pusher(m.transform.position, agent.velocity * 1.6f + (agent.velocity.sqrMagnitude > 0.1f ? agent.velocity.normalized : Vector3.zero));

            if (Mathf.Abs(push) > 0.01f)
            {
                target = float.NaN; // bodies win over a click in progress
                // a positive leaf rotation (about +Y) moves the free end along +normal
                angle += push * dt;
            }
            else if (!float.IsNaN(target))
            {
                angle = Mathf.MoveTowards(angle, target, ClickSpeed * dt);
                if (Mathf.Approximately(angle, target)) target = float.NaN;
            }
            if (OneWay)
            {
                var open = OpenAngle(side, 1f);
                angle = open > 0f ? Mathf.Clamp(angle, 0f, MaxAngle) : Mathf.Clamp(angle, -MaxAngle, 0f);
            }
            return Mathf.Clamp(angle, -MaxAngle, MaxAngle);
        }

        private void Update()
        {
            var k = 1f - Mathf.Exp(-14f * Time.deltaTime);
            shownA = Mathf.Lerp(shownA, IsSpawned ? AngleA.Value : 0f, k);
            shownB = Mathf.Lerp(shownB, IsSpawned ? AngleB.Value : 0f, k);
            if (LeafA != null) LeafA.localRotation = Quaternion.Euler(0f, shownA, 0f);
            if (LeafB != null) LeafB.localRotation = Quaternion.Euler(0f, shownB, 0f);
        }
    }
}
