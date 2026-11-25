using System;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // Procedural animation for the placeholder low-poly creatures: limbs swing around their axes in a gait
    // cycle driven by how fast the creature actually moves (works on every peer from the replicated
    // transform), the body bobs, and spinners (fan blades) turn. Faster and twitchier while chasing.
    public sealed class CreatureAnimator : MonoBehaviour
    {
        [Serializable]
        public struct Limb
        {
            public Transform Bone;
            public Vector3 Axis;     // local swing axis
            public float Phase;      // radians
            public float Swing;      // degrees at full stride
        }

        public Limb[] Limbs;
        public Transform Body;
        public float BobAmount = 0.05f;
        public float StrideRate = 5f;   // gait radians per metre
        public Transform[] Spinners;
        public float SpinSpeed = 900f;

        private Quaternion[] rest;
        private Vector3 bodyRest;
        private Vector3 last;
        private float cycle;
        private float speed;
        private TempMonster brain;

        private void Awake()
        {
            brain = GetComponent<TempMonster>();
            rest = new Quaternion[Limbs != null ? Limbs.Length : 0];
            for (var i = 0; i < rest.Length; i++) rest[i] = Limbs[i].Bone != null ? Limbs[i].Bone.localRotation : Quaternion.identity;
            if (Body != null) bodyRest = Body.localPosition;
            last = transform.position;
        }

        private void Update()
        {
            var dt = Mathf.Max(Time.deltaTime, 0.0001f);
            var moved = transform.position - last;
            last = transform.position;
            var flat = new Vector2(moved.x, moved.z).magnitude / dt;
            speed = Mathf.Lerp(speed, flat, 1f - Mathf.Exp(-8f * dt));
            var chasing = brain != null && brain.IsSpawned && brain.Chasing.Value;
            cycle += (speed * StrideRate + (chasing ? 2f : 0.6f)) * dt;
            var stride = Mathf.Clamp01(speed / 2f) + 0.08f;

            for (var i = 0; i < rest.Length; i++)
            {
                var limb = Limbs[i];
                if (limb.Bone == null) continue;
                var twitch = chasing ? Mathf.Sin(Time.time * 31f + i) * 3f : 0f;
                limb.Bone.localRotation = rest[i] * Quaternion.AngleAxis(Mathf.Sin(cycle + limb.Phase) * limb.Swing * stride + twitch, limb.Axis);
            }
            if (Body != null) Body.localPosition = bodyRest + Vector3.up * (Mathf.Abs(Mathf.Sin(cycle)) * BobAmount * stride);
            if (Spinners != null)
                foreach (var s in Spinners)
                    if (s != null) s.Rotate(0f, 0f, SpinSpeed * (chasing ? 1.6f : 1f) * dt, Space.Self);
        }
    }
}
