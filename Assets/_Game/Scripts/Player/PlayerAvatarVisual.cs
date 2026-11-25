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
            handColor = Color.Lerp(bodyColor, new Color(1f, 0.85f, 0.8f), 0.45f);
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

            // Jelly squash and stretch from the actual movement (works for remote players too).
            var dt = Mathf.Max(Time.deltaTime, 0.0001f);
            var velocity = (transform.position - lastPosition) / dt;
            lastPosition = transform.position;
            var flat = new Vector2(velocity.x, velocity.z).magnitude;
            Vector3 target;
            if (motor != null && motor.IsSpawned && motor.Crouched.Value) target = Crouched;
            else
            {
                // Soft but firm: a little give when landing and jumping, a faint wobble when running.
                var stretch = Mathf.Clamp(velocity.y * 0.02f, -0.1f, 0.1f);
                var wobble = Mathf.Sin(Time.time * 11f) * 0.015f * Mathf.Clamp01(flat / 4f);
                target = new Vector3(1f - stretch * 0.5f - wobble, 1f + stretch + wobble, 1f - stretch * 0.5f - wobble);
            }
            squash = Vector3.SmoothDamp(squash, target, ref squashVelocity, 0.07f, 40f, dt);
            BodyRoot.localScale = squash;
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
            Tint(LeftFist != null ? LeftFist.GetComponent<Renderer>() : null, paw);
            Tint(RightFist != null ? RightFist.GetComponent<Renderer>() : null, paw);
        }

        private void Tint(Renderer r, Color c)
        {
            if (r == null) return;
            block.SetColor("_BaseColor", c);
            r.SetPropertyBlock(block);
        }
    }
}
