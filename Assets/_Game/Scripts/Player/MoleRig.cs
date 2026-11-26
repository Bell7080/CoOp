using CoOpHorror.Gameplay;
using UnityEngine;

namespace CoOpHorror.Player
{
    // Drives the mole's own skeleton for what it does with its body, so everything happens on the real model:
    // - the nose: the deep-rigged segment bones (ElecMoleNoseRig) are laid along a curve from the face to
    //   whatever the nose reaches for, stretching the real nose mesh (thinner the longer it gets), with bulges
    //   running along it while power flows;
    // - the arms: while lifting a body with telekinesis, both arms reach toward it.
    // Runs after the clips and RigAnimator's spring chains. Purely presentational; never writes gameplay state.
    [DefaultExecutionOrder(200)]
    public sealed class MoleRig : MonoBehaviour
    {
        public Transform[] NoseSegments;    // root first; children of the original nose bones
        public Transform NoseTip;           // end of the nose (the bulb's front)
        public Transform ArmR1, ArmR2, ArmL1, ArmL2;

        // Set every frame by PlayerNose.
        [HideInInspector] public float NoseOut;        // 0 = resting, 1 = fully on its goal
        [HideInInspector] public Vector3 NoseGoal;     // where the tip should touch
        [HideInInspector] public float Pulse;          // travelling bulges (phase)
        [HideInInspector] public int PulseDirection;   // +1 toward the player (drinking), -1 outward, 0 none

        private Vector3[] homePosition;
        private Quaternion[] homeRotation;
        private Vector3[] rest, curve;
        private Quaternion[] restRotation;
        private PlayerInteractor interactor;
        private float armReach;

        public Vector3 NoseRoot => NoseSegments != null && NoseSegments.Length > 0 && NoseSegments[0] != null ? NoseSegments[0].position : transform.position;

        private void Awake()
        {
            interactor = GetComponent<PlayerInteractor>();
            if (NoseSegments == null) return;
            var n = NoseSegments.Length;
            homePosition = new Vector3[n];
            homeRotation = new Quaternion[n];
            rest = new Vector3[n];
            curve = new Vector3[n];
            restRotation = new Quaternion[n];
            for (var i = 0; i < n; i++)
            {
                if (NoseSegments[i] == null) continue;
                homePosition[i] = NoseSegments[i].localPosition;
                homeRotation[i] = NoseSegments[i].localRotation;
            }
        }

        private void LateUpdate()
        {
            StretchNose();
            ReachArms();
        }

        private void StretchNose()
        {
            if (NoseSegments == null || homePosition == null) return;
            var n = NoseSegments.Length;
            // back to the bind offsets on their (animated, springy) nose bones: that is the resting nose
            for (var i = 0; i < n; i++)
            {
                var s = NoseSegments[i];
                if (s == null) return;
                s.localPosition = homePosition[i];
                s.localRotation = homeRotation[i];
                s.localScale = Vector3.one;
            }
            if (NoseOut <= 0.001f && PulseDirection == 0) return;

            for (var i = 0; i < n; i++) { rest[i] = NoseSegments[i].position; restRotation[i] = NoseSegments[i].rotation; }
            var tipEnd = NoseTip != null ? NoseTip.position : rest[n - 1] + restRotation[n - 1] * Vector3.up * 0.1f;
            var tipLength = Vector3.Distance(rest[n - 1], tipEnd);
            var restLength = tipLength;
            for (var i = 0; i < n - 1; i++) restLength += Vector3.Distance(rest[i], rest[i + 1]);

            // a soft curve: leaves the face along the resting nose, bends round to arrive straight at the goal.
            // The first few segments (the thin root hidden inside the snout) stay where they are.
            var anchor = Mathf.Clamp(n / 8, 0, n - 3);
            var start = rest[anchor];
            var toGoal = NoseGoal - start;
            var distance = Mathf.Max(0.01f, toGoal.magnitude);
            var startDir = (rest[anchor + 1] - rest[anchor]).normalized;
            var control = start + Vector3.Slerp(startDir, toGoal / distance, 0.55f) * (distance * 0.45f);
            var endDir = (NoseGoal - control).normalized;
            var last = NoseGoal - endDir * tipLength;
            var stretch = Mathf.Clamp01((distance / Mathf.Max(0.01f, restLength) - 1f) / 3f);
            for (var i = 0; i < n; i++)
            {
                if (i < anchor) { curve[i] = rest[i]; continue; }
                var u = (i - anchor) / (float)(n - 1 - anchor);
                var v = 1f - u;
                curve[i] = v * v * start + 2f * v * u * control + u * u * last;
            }
            // Orientations by parallel transport along the curve (each segment turns from the previous one's
            // tangent to its own, keeping the resting twist between them), so the nose never twists flat.
            Quaternion previous = Quaternion.identity;
            var previousTangent = Vector3.forward;
            var previousRestTangent = Vector3.forward;
            for (var i = 0; i < n; i++)
            {
                var restTangent = i < n - 1 ? (rest[i + 1] - rest[i]).normalized : (tipEnd - rest[i]).normalized;
                var u = i / (float)(n - 1);
                var tangent = i < n - 1 ? (curve[i + 1] - curve[i]).normalized : endDir;
                Quaternion bent;
                if (i < anchor) bent = restRotation[i];
                else if (i == anchor) bent = Quaternion.FromToRotation(restTangent, tangent) * restRotation[i];
                else
                {
                    // the resting twist of this segment relative to the previous one
                    var restCarried = Quaternion.FromToRotation(previousRestTangent, restTangent) * restRotation[i - 1];
                    var twist = Quaternion.Inverse(restCarried) * restRotation[i];
                    bent = Quaternion.FromToRotation(previousTangent, tangent) * previous * twist;
                }
                previous = bent;
                previousTangent = tangent;
                previousRestTangent = restTangent;
                var s = NoseSegments[i];
                s.SetPositionAndRotation(Vector3.Lerp(rest[i], curve[i], NoseOut), Quaternion.Slerp(restRotation[i], bent, NoseOut));
                // a long nose gets a little thinner in the middle (not at the root or the tip bulb); power
                // flowing bulges it in waves
                var middle = i <= anchor ? 0f : Mathf.Sin((i - anchor) / (float)(n - 1 - anchor) * Mathf.PI);
                var thickness = i == n - 1 ? 1f : Mathf.Lerp(1f, 0.8f, stretch * NoseOut * middle);
                if (PulseDirection != 0 && i > 0)
                {
                    var along = PulseDirection > 0 ? 1f - u : u;
                    var wave = Mathf.Repeat(along - Pulse, 0.34f) / 0.34f;
                    thickness *= 1f + 0.5f * Mathf.Exp(-Mathf.Pow((wave - 0.5f) * 6f, 2f));
                }
                s.localScale = new Vector3(thickness, 1f, thickness);
            }
        }

        // Both arms reach toward a body being lifted with telekinesis (every peer sees the holders list).
        private void ReachArms()
        {
            var held = interactor != null && interactor.IsSpawned ? interactor.HeldGrabbable : null;
            armReach = Mathf.MoveTowards(armReach, held != null ? 1f : 0f, Time.deltaTime * 5f);
            if (armReach <= 0.001f || held == null) return;
            var goal = held.transform.position + Vector3.up * 0.25f;
            Aim(ArmR1, ArmR2, goal);
            Aim(ArmL1, ArmL2, goal);
        }

        private void Aim(Transform upper, Transform lower, Vector3 goal)
        {
            if (upper == null) return;
            var tip = lower != null ? (lower.childCount > 0 ? lower.GetChild(0).position : lower.position + lower.up * 0.15f) : upper.position + upper.up * 0.3f;
            var from = tip - upper.position;
            var to = goal - upper.position;
            if (from.sqrMagnitude < 1e-6f || to.sqrMagnitude < 1e-6f) return;
            upper.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(from, to), armReach) * upper.rotation;
            if (lower == null) return;
            // straighten the forearm along the same line
            var lowerTip = lower.childCount > 0 ? lower.GetChild(0).position : lower.position + lower.up * 0.15f;
            var f2 = lowerTip - lower.position;
            var t2 = goal - lower.position;
            if (f2.sqrMagnitude > 1e-6f && t2.sqrMagnitude > 1e-6f)
                lower.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(f2, t2), armReach) * lower.rotation;
        }

        // Where the right paw is (telekinesis beams start there).
        public Vector3 RightPaw
        {
            get
            {
                if (ArmR2 == null) return transform.position + transform.forward * 0.3f;
                return ArmR2.childCount > 0 ? ArmR2.GetChild(0).position : ArmR2.position;
            }
        }
    }
}
