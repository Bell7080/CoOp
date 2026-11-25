using UnityEngine;

namespace CoOpHorror.Gameplay
{
    public enum SkillCheckResult : byte { Good, Great, Miss }

    // Owner-side timing minigame shown while holding the button on a skill-checked target.
    // A warning beep, then a needle sweeps once around the ring; press Space inside the zone.
    // The owner reports the result and the host applies it (prototype trust, like movement).
    public sealed class SkillCheck
    {
        public const float FirstDelayMin = 2.5f, FirstDelayMax = 5f;
        public const float IntervalMin = 4f, IntervalMax = 8f;
        public const float WarningTime = 0.6f;
        public const float SweepTime = 1.15f;      // seconds for a full 360° turn
        public const float ZoneWidth = 52f;        // degrees
        public const float GreatWidth = 12f;       // degrees at the start of the zone
        public const float ResultShowTime = 0.9f;

        public enum State { Idle, Warning, Running }

        public State Current { get; private set; } = State.Idle;
        public float NeedleAngle { get; private set; }
        public float ZoneStart { get; private set; }
        public SkillCheckResult LastResult { get; private set; }
        public float LastResultTime { get; private set; } = -10f;
        public bool Visible => Current != State.Idle;

        private float nextAt = -1f;
        private float stateStart;

        // Called every frame by the owner. holding: currently holding a skill-checked target.
        // Returns a result on the frame the check resolves.
        public SkillCheckResult? Tick(bool holding, bool pressed)
        {
            var now = Time.time;
            if (!holding)
            {
                Current = State.Idle;
                nextAt = -1f;
                return null;
            }
            if (nextAt < 0f) nextAt = now + Random.Range(FirstDelayMin, FirstDelayMax);

            switch (Current)
            {
                case State.Idle:
                    if (now < nextAt) return null;
                    Current = State.Warning;
                    stateStart = now;
                    ZoneStart = Random.Range(110f, 300f - ZoneWidth);
                    NeedleAngle = 0f;
                    ProtoAudio.Play(ProtoSound.Beep, CameraPosition(), 0.6f);
                    return null;
                case State.Warning:
                    if (now - stateStart < WarningTime) return null;
                    Current = State.Running;
                    stateStart = now;
                    return null;
                default:
                    NeedleAngle = (now - stateStart) / SweepTime * 360f;
                    if (pressed) return Resolve(Judge(NeedleAngle));
                    if (NeedleAngle >= 360f) return Resolve(SkillCheckResult.Miss);
                    return null;
            }
        }

        private SkillCheckResult Judge(float angle)
        {
            if (angle >= ZoneStart && angle <= ZoneStart + GreatWidth) return SkillCheckResult.Great;
            if (angle >= ZoneStart && angle <= ZoneStart + ZoneWidth) return SkillCheckResult.Good;
            return SkillCheckResult.Miss;
        }

        private SkillCheckResult Resolve(SkillCheckResult result)
        {
            Current = State.Idle;
            nextAt = Time.time + Random.Range(IntervalMin, IntervalMax);
            LastResult = result;
            LastResultTime = Time.time;
            ProtoAudio.Play(result == SkillCheckResult.Miss ? ProtoSound.Buzz : ProtoSound.Click, CameraPosition(), 0.5f);
            return result;
        }

        private static Vector3 CameraPosition() => Camera.main != null ? Camera.main.transform.position : Vector3.zero;
    }
}
