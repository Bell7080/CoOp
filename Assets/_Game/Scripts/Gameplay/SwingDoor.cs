using CoOpHorror.Player;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // An ordinary hinged door, single leaf or a double door. Opened by hand the R.E.P.O. way (DragOpenable):
    // hold the left button and the nose bites the leaf, then drag it with your view; let go and it swings on
    // its own momentum. Bodies still push it open just by walking into it (players and monsters alike, so a
    // creature barges through). One-way doors only open towards +Z of the door. Host-authoritative angles;
    // every peer animates the leaves (their colliders block players). Monsters' paths ignore the leaves.
    public sealed class SwingDoor : DragOpenable
    {
        public const float MaxAngle = 100f;
        public const float PushStrength = 140f;   // degrees per second per m/s of walking into it
        public const float GustSpeed = 420f;      // degrees per second a gust flings it

        public Transform LeafA, LeafB;            // pivots at the hinges; leaves extend along -SideA*X / -SideB*X
        public float SideA = 1f, SideB = -1f;     // which jamb each hinge is on (+X or -X)
        public float LeafWidth = 1.5f;
        public bool OneWay;

        public static readonly System.Collections.Generic.List<SwingDoor> All = new();

        // Walk-in wardrobes (jangnong) are swing doors too, built as "Wardrobe_NN" (ElecMoleDoors).
        public const float WardrobeWidth = 1.6f, WardrobeDepth = 1.4f;
        public bool IsWardrobe => name.Contains("Wardrobe_");

        protected override void Awake()
        {
            // the leaves become DragOpenable parts before the base caches their rest poses
            var parts = new System.Collections.Generic.List<DragPart> { Leaf(LeafA, SideA) };
            if (LeafB != null) parts.Add(Leaf(LeafB, SideB));
            Parts = parts.ToArray();
            NameKey = IsWardrobe ? "openable.wardrobe" : "openable.door";
            base.Awake();
            All.Add(this);
        }

        private DragPart Leaf(Transform pivot, float side)
        {
            var open = OpenAngle(side, 1f);
            return new DragPart
            {
                Pivot = pivot,
                Kind = DragPartKind.Hinge,
                Min = OneWay ? (open > 0f ? 0f : -MaxAngle) : -MaxAngle,
                Max = OneWay ? (open > 0f ? MaxAngle : 0f) : MaxAngle,
                Mass = IsWardrobe ? 1.1f : 1.6f,
                Friction = 1.1f,
                LatchBelow = 2f,
            };
        }

        public override void OnDestroy()
        {
            All.Remove(this);
            base.OnDestroy();
        }

        // Host: someone standing inside a wardrobe with its door shut (muffles the clock spider's alarm).
        public static bool InClosedWardrobe(Vector3 position)
        {
            foreach (var door in All)
            {
                if (door == null || !door.IsSpawned || !door.IsWardrobe || door.IsOpen) continue;
                var local = door.transform.InverseTransformPoint(position);
                if (Mathf.Abs(local.x) < WardrobeWidth / 2f && Mathf.Abs(local.z) < WardrobeDepth / 2f && local.y < 2.5f) return true;
            }
            return false;
        }

        // Host: a gust (the fan crawler) flings it open.
        public void ServerBlow(Vector3 from)
        {
            if (!IsServer || IsOpen) return;
            var away = Vector3.Dot(from - transform.position, transform.forward) > 0f ? -1f : 1f;
            if (OneWay) away = 1f;
            ServerKick(0, Mathf.Sign(OpenAngle(SideA, away)) * GustSpeed);
            if (LeafB != null) ServerKick(1, Mathf.Sign(OpenAngle(SideB, away)) * GustSpeed);
        }

        // Rotating a leaf by +angle about Y turns its free end towards +Z for a hinge on +X (towards -Z for -X).
        private static float OpenAngle(float side, float towardsZ) => side * towardsZ * 90f;

        // Host: bodies walking into a leaf shove it (degrees per second), or NaN.
        protected override float ServerShove(int part)
        {
            var leaf = part == 0 ? LeafA : LeafB;
            var side = part == 0 ? SideA : SideB;
            if (leaf == null) return float.NaN;
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
                if (p != null && p.IsSpawned && !p.IsDead.Value && p.TryGetComponent<PlayerInteractor>(out var pi) && !GrabbedBy(pi))
                    Pusher(p.transform.position + Vector3.down * 0.5f, pi.MotionVelocity);
            foreach (var m in TempMonster.All)
                if (m != null && m.IsSpawned && m.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var agent))
                    Pusher(m.transform.position, agent.velocity * 1.6f + (agent.velocity.sqrMagnitude > 0.1f ? agent.velocity.normalized : Vector3.zero));
            return Mathf.Abs(push) > 0.01f ? push : float.NaN;
        }
    }
}
