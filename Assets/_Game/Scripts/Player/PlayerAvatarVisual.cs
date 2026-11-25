using UnityEngine;
using UnityEngine.Rendering;

namespace CoOpHorror.Player
{
    // Simple placeholder character: capsule body, sphere head and two hands under the aim pivot.
    // A dead player lies on the floor as a dark, discharged body that flickers with sparks.
    // Purely presentational; never writes gameplay state.
    public sealed class PlayerAvatarVisual : MonoBehaviour
    {
        public Renderer Body;
        public Renderer Head;
        public Transform AimPivot;
        public Transform LeftFist;
        public Transform RightFist;

        private static readonly Vector3 CorpseBodyPosition = new(0f, -0.58f, 0.1f);
        private static readonly Vector3 CorpseHeadPosition = new(0f, -0.72f, 0.95f);
        private static readonly Color Discharged = new(0.16f, 0.17f, 0.19f);

        private Vector3 bodyHome, headHome;
        private Quaternion bodyRotationHome;
        private float flashUntil;
        private bool dead;
        private Color bodyColor = Color.white, handColor = Color.white;
        private MaterialPropertyBlock block;
        private bool isOwnerView;

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            if (Body != null) { bodyHome = Body.transform.localPosition; bodyRotationHome = Body.transform.localRotation; }
            if (Head != null) headHome = Head.transform.localPosition;
        }

        public void Configure(bool ownerView, ulong ownerId)
        {
            isOwnerView = ownerView;
            bodyColor = Color.HSVToRGB((ownerId * 0.19f + 0.42f) % 1f, 0.6f, 0.9f);
            handColor = Color.Lerp(bodyColor, new Color(0.95f, 0.8f, 0.68f), 0.6f);
            // Owner sees only hands in first person; body/head still cast shadows.
            var mode = ownerView ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            if (Body != null) Body.shadowCastingMode = mode;
            if (Head != null) Head.shadowCastingMode = mode;
            ApplyColors(false);
        }

        public void SetDead(bool value)
        {
            dead = value;
            if (Body != null)
            {
                Body.transform.localPosition = dead ? CorpseBodyPosition : bodyHome;
                Body.transform.localRotation = dead ? Quaternion.Euler(90f, 0f, 0f) : bodyRotationHome;
            }
            if (Head != null) Head.transform.localPosition = dead ? CorpseHeadPosition : headHome;
            if (LeftFist != null) LeftFist.gameObject.SetActive(!dead);
            if (RightFist != null) RightFist.gameObject.SetActive(!dead);
            ApplyColors(false);
        }

        public void Flash() => flashUntil = Time.time + 0.18f;

        private void Update() => ApplyColors(Time.time < flashUntil);

        private void ApplyColors(bool flashing)
        {
            if (block == null) block = new MaterialPropertyBlock();
            var red = new Color(1f, 0.15f, 0.1f);
            var spark = new Color(0.55f, 0.9f, 1f);
            var body = dead ? (flashing ? spark : Random.value < 0.015f ? spark * 0.6f : Discharged) : flashing ? red : bodyColor;
            Tint(Body, body);
            Tint(Head, body);
            Tint(LeftFist != null ? LeftFist.GetComponent<Renderer>() : null, flashing && !isOwnerView ? red : handColor);
            Tint(RightFist != null ? RightFist.GetComponent<Renderer>() : null, flashing && !isOwnerView ? red : handColor);
        }

        private void Tint(Renderer r, Color c)
        {
            if (r == null) return;
            block.SetColor("_BaseColor", c);
            r.SetPropertyBlock(block);
        }
    }
}
