using UnityEngine;
using UnityEngine.Rendering;

namespace CoOpHorror.Player
{
    public enum PunchHand : byte { Left = 0, Right = 1 }

    // Simple placeholder character: capsule body, sphere head and two fists under the aim pivot.
    // Purely presentational; never writes gameplay state.
    public sealed class PlayerAvatarVisual : MonoBehaviour
    {
        public Renderer Body;
        public Renderer Head;
        public Transform AimPivot;
        public Transform LeftFist;
        public Transform RightFist;

        public const float PunchOut = 0.07f;
        public const float PunchBack = 0.13f;
        public const float PunchReach = 0.55f;

        private Vector3 leftHome, rightHome;
        private float leftStart = -10f, rightStart = -10f;
        private float flashUntil;
        private Color bodyColor = Color.white, fistColor = Color.white;
        private MaterialPropertyBlock block;
        private bool isOwnerView;

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            if (LeftFist != null) leftHome = LeftFist.localPosition;
            if (RightFist != null) rightHome = RightFist.localPosition;
        }

        public void Configure(bool ownerView, ulong ownerId)
        {
            isOwnerView = ownerView;
            bodyColor = Color.HSVToRGB((ownerId * 0.19f + 0.42f) % 1f, 0.6f, 0.9f);
            fistColor = Color.Lerp(bodyColor, new Color(0.95f, 0.8f, 0.68f), 0.6f);
            // Owner sees only fists in first person; body/head still cast shadows.
            var mode = ownerView ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            if (Body != null) Body.shadowCastingMode = mode;
            if (Head != null) Head.shadowCastingMode = mode;
            ApplyColors(false);
        }

        public void SetDead(bool dead)
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = !dead;
        }

        public void Flash() => flashUntil = Time.time + 0.18f;

        public void PlayPunch(PunchHand hand)
        {
            if (hand == PunchHand.Left) leftStart = Time.time; else rightStart = Time.time;
        }

        private void Update()
        {
            Animate(LeftFist, leftHome, leftStart);
            Animate(RightFist, rightHome, rightStart);
            ApplyColors(Time.time < flashUntil);
        }

        private static void Animate(Transform fist, Vector3 home, float start)
        {
            if (fist == null) return;
            var t = Time.time - start;
            float extend;
            if (t < 0f || t > PunchOut + PunchBack) extend = 0f;
            else if (t < PunchOut) extend = t / PunchOut;
            else extend = 1f - (t - PunchOut) / PunchBack;
            // Jab toward the aim pivot's centre line so both fists converge on the crosshair.
            var target = new Vector3(home.x * 0.35f, home.y * 0.5f, home.z + PunchReach);
            fist.localPosition = Vector3.Lerp(home, target, Mathf.SmoothStep(0f, 1f, extend));
        }

        private void ApplyColors(bool flashing)
        {
            if (block == null) block = new MaterialPropertyBlock();
            var red = new Color(1f, 0.15f, 0.1f);
            Tint(Body, flashing ? red : bodyColor);
            Tint(Head, flashing ? red : bodyColor);
            Tint(LeftFist != null ? LeftFist.GetComponent<Renderer>() : null, flashing && !isOwnerView ? red : fistColor);
            Tint(RightFist != null ? RightFist.GetComponent<Renderer>() : null, flashing && !isOwnerView ? red : fistColor);
        }

        private void Tint(Renderer r, Color c)
        {
            if (r == null) return;
            block.SetColor("_BaseColor", c);
            r.SetPropertyBlock(block);
        }
    }
}
