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
        public Renderer FirstPersonArms; // the body's own arms cut out of its mesh, shown to the owner only
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
            noseColor = bodyColor; // one nose, the same skin as the head it grows from (no colour break)
            noseColor.a = 1f;
            // First person shows the real model's own nose and arms (the arms are the body's triangles skinned
            // to the arm bones, so they move exactly as the body does); the rest of the body only casts its
            // shadow, because from inside the head the scanned mesh shows its inside.
            var mode = ownerView ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            if (Skin != null) foreach (var r in Skin) if (r != null) r.shadowCastingMode = mode;
            if (Details != null) foreach (var r in Details) if (r != null) r.shadowCastingMode = mode;
            if (Nose != null) foreach (var r in Nose) if (r != null) r.shadowCastingMode = BodyRoot != null ? ShadowCastingMode.On : mode;
            if (FirstPersonArms != null)
            {
                FirstPersonArms.enabled = ownerView;
                FirstPersonArms.shadowCastingMode = ShadowCastingMode.Off;
            }
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
            if (FirstPersonArms != null) FirstPersonArms.enabled = !dead && isOwnerView;
            if (!dead) ShowPaws(true);
        }

        // The old stand-in first-person paws: only for the primitive fallback body (the rigged model shows its
        // own arms in first person too).
        private void ShowPaws(bool alive)
        {
            foreach (var paw in new[] { LeftFist, RightFist })
                if (paw != null)
                    foreach (var r in paw.GetComponentsInChildren<Renderer>(true)) r.enabled = alive && BodyRoot == null;
        }

        public void Flash() => flashUntil = Time.time + 0.18f;

        public Color NoseColor => noseColor;

        // While power flows through the nose (PlayerNose) it glows in this colour, pulsing.
        public Color? NoseFlowTint { get; set; }

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
                // firm jelly: it keeps its shape, only a hint of give
                // firm, viscous jelly: the body keeps its shape while moving (only crouching squeezes it)
                var stretch = 0f;
                var wobble = 0f;
                var jolt = 0f;
                target = new Vector3(1f - stretch * 0.5f - wobble + jolt, 1f + stretch + wobble - jolt, 1f - stretch * 0.5f - wobble + jolt);
            }
            squash = Vector3.SmoothDamp(squash, target, ref squashVelocity, 0.05f, 40f, dt);
            BodyRoot.localScale = squash;

            // Lean away from the acceleration (start, stop, sidestep, turn), damped spring back.
            var localAccel = transform.InverseTransformDirection(new Vector3(smoothedAccel.x, 0f, smoothedAccel.z));
            var leanTarget = new Vector3(Mathf.Clamp(-localAccel.z * 0.015f, -0.5f, 0.5f), 0f, Mathf.Clamp(localAccel.x * 0.015f, -0.5f, 0.5f));
            lean = Vector3.SmoothDamp(lean, leanTarget, ref leanVelocity, 0.08f, 200f, dt);
            BodyRoot.localRotation = Quaternion.Euler(lean.x, 0f, lean.z);
        }

        private void ApplyColors(bool flashing)
        {
            if (block == null) block = new MaterialPropertyBlock();
            var red = new Color(1f, 0.15f, 0.1f);
            var skin = flashing ? red : bodyColor;
            if (Skin != null) foreach (var r in Skin) Tint(r, skin);
            Tint(FirstPersonArms, skin);
            var nose = noseColor;
            if (NoseFlowTint.HasValue) nose = Color.Lerp(noseColor, NoseFlowTint.Value, 0.35f + 0.25f * Mathf.Sin(Time.time * 18f));
            if (Nose != null) foreach (var r in Nose) Tint(r, flashing ? red : nose);
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
