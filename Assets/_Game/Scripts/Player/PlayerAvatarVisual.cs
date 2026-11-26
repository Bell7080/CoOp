using UnityEngine;
using UnityEngine.Rendering;

namespace CoOpHorror.Player
{
    // Placeholder ElecMole: a squishy, jelly-like mole alien (round body, snout, little ears, glowing antenna)
    // with two paws under the aim pivot for first person. It squashes and stretches with movement, jumps and
    // crouching (squeezed flat to crawl under furniture). A dead player is an invisible ghost (their body is
    // a separate Corpse object). Purely presentational; never writes gameplay state.
    public sealed class PlayerAvatarVisual : MonoBehaviour
    {
        public Transform BodyRoot;      // squash pivot at the feet
        public Renderer[] Skin;         // tinted with the player's colour
        public Renderer[] Details;      // eyes, blush: own colours
        public Renderer[] Nose;         // the player's colour, muted
        public Transform AimPivot;
        public Transform LeftFist;      // paws
        public Transform RightFist;
        // Legacy (v0.2 capsule avatar); unused when BodyRoot is set.
        public Renderer Body;
        public Renderer Head;

        // Hidden under furniture the body is pressed flatter, but it keeps its shape.
        private static readonly Vector3 Crouched = new(1.12f, 0.64f, 1.1f);

        // Player colours, like the concept art: teal, purple, magenta, orange, sky, lime, yellow, coral.
        private static readonly Color[] Palette =
        {
            new(0.3f, 0.78f, 0.68f), new(0.6f, 0.45f, 0.92f), new(0.92f, 0.4f, 0.66f), new(0.98f, 0.62f, 0.3f),
            new(0.42f, 0.68f, 0.98f), new(0.62f, 0.86f, 0.36f), new(0.98f, 0.86f, 0.36f), new(0.98f, 0.48f, 0.45f),
        };

        private float flashUntil;
        private Color bodyColor = Color.white, handColor = Color.white, noseColor = Color.white;
        private MaterialPropertyBlock block;
        private bool isOwnerView;
        private FirstPersonController motor;
        private Vector3 lastPosition;
        private Vector3 squash = Vector3.one;
        private Vector3 squashVelocity;
        private Vector3 lastVelocity, smoothedAccel, lean, leanVelocity;

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            motor = GetComponent<FirstPersonController>();
            lastPosition = transform.position;
        }

        public void Configure(bool ownerView, ulong ownerId)
        {
            isOwnerView = ownerView;
            bodyColor = Palette[(int)(ownerId % (ulong)Palette.Length)];
            handColor = bodyColor; // the first-person hands are the body's own arms
            var grey = bodyColor.grayscale;
            noseColor = Color.Lerp(bodyColor, new Color(grey, grey, grey), 0.5f) * 0.9f;
            noseColor.a = 1f;
            // In first person you only see your paws; the body still casts a shadow.
            var mode = ownerView ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            if (Skin != null) foreach (var r in Skin) if (r != null) r.shadowCastingMode = mode;
            if (Details != null) foreach (var r in Details) if (r != null) r.shadowCastingMode = mode;
            if (Nose != null) foreach (var r in Nose) if (r != null) r.shadowCastingMode = mode;
            if (BodyRoot == null)
            {
                if (Body != null) Body.shadowCastingMode = mode;
                if (Head != null) Head.shadowCastingMode = mode;
            }
            ShowPaws(true);
            ApplyColors(false);
        }

        public void SetDead(bool dead)
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = !dead;
            if (!dead) ShowPaws(true);
        }

        // The first-person paws only exist for their owner; other players see the rigged body's own arms.
        private void ShowPaws(bool alive)
        {
            foreach (var paw in new[] { LeftFist, RightFist })
                if (paw != null)
                    foreach (var r in paw.GetComponentsInChildren<Renderer>(true)) r.enabled = alive && (isOwnerView || BodyRoot == null);
        }

        public void Flash() => flashUntil = Time.time + 0.18f;

        private void Update()
        {
            ApplyColors(Time.time < flashUntil);
            if (BodyRoot == null) return;

            // Firm jelly: the body barely wobbles by itself, but leans and squashes against sudden changes of
            // movement (inertia), then settles quickly without losing the face silhouette. Works for remote players too (from the replicated motion).
            var dt = Mathf.Max(Time.deltaTime, 0.0001f);
            var velocity = (transform.position - lastPosition) / dt;
            lastPosition = transform.position;
            if (velocity.sqrMagnitude > 900f) velocity = lastVelocity; // teleports
            var accel = (velocity - lastVelocity) / dt;
            lastVelocity = velocity;
            smoothedAccel = Vector3.Lerp(smoothedAccel, Vector3.ClampMagnitude(accel, 40f), 1f - Mathf.Exp(-12f * dt));
            var flat = new Vector2(velocity.x, velocity.z).magnitude;
            Vector3 target;
            if (motor != null && motor.IsSpawned && motor.Crouched.Value) target = Crouched;
            else
            {
                var stretch = Mathf.Clamp(velocity.y * 0.002f, -0.014f, 0.014f);
                var wobble = Mathf.Sin(Time.time * 8f) * 0.0015f * Mathf.Clamp01(flat / 4f);
                var jolt = Mathf.Clamp01(new Vector2(smoothedAccel.x, smoothedAccel.z).magnitude / 30f) * 0.012f;
                target = new Vector3(1f - stretch * 0.5f - wobble + jolt, 1f + stretch + wobble - jolt, 1f - stretch * 0.5f - wobble + jolt);
            }
            squash = Vector3.SmoothDamp(squash, target, ref squashVelocity, 0.08f, 40f, dt);
            BodyRoot.localScale = squash;

            // Lean away from the acceleration (start, stop, sidestep, turn), damped spring back.
            var localAccel = transform.InverseTransformDirection(new Vector3(smoothedAccel.x, 0f, smoothedAccel.z));
            var leanTarget = new Vector3(Mathf.Clamp(-localAccel.z * 0.09f, -2.5f, 2.5f), 0f, Mathf.Clamp(localAccel.x * 0.09f, -2.5f, 2.5f));
            lean = Vector3.SmoothDamp(lean, leanTarget, ref leanVelocity, 0.12f, 200f, dt);
            BodyRoot.localRotation = Quaternion.Euler(lean.x, 0f, lean.z);
        }

        private void ApplyColors(bool flashing)
        {
            if (block == null) block = new MaterialPropertyBlock();
            var red = new Color(1f, 0.15f, 0.1f);
            var skin = flashing ? red : bodyColor;
            if (Skin != null) foreach (var r in Skin) Tint(r, skin);
            if (Nose != null) foreach (var r in Nose) Tint(r, flashing ? red : noseColor);
            if (BodyRoot == null)
            {
                Tint(Body, skin);
                Tint(Head, skin);
            }
            var paw = flashing && !isOwnerView ? red : handColor;
            Tint(LeftFist != null ? LeftFist.GetComponentInChildren<Renderer>() : null, paw);
            Tint(RightFist != null ? RightFist.GetComponentInChildren<Renderer>() : null, paw);
        }

        private void Tint(Renderer r, Color c)
        {
            if (r == null) return;
            block.SetColor("_BaseColor", c);
            r.SetPropertyBlock(block);
        }
    }
}
