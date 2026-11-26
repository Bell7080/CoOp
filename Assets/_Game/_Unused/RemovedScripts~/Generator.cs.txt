using System.Collections.Generic;
using CoOpHorror.Localization;
using CoOpHorror.Player;
using Unity.Netcode;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // Hold the left button to crank. Each cranking player adds power to the shared grid.
    // Skill checks appear while cranking; a miss blows the generator up for a moment and makes noise.
    public sealed class Generator : SkillCheckedInteractable
    {
        public const float CrankRatePerPlayer = 12f; // power per second per player
        public const float FailStall = 3f;           // seconds the generator stops after a miss
        public const float FailNoiseRadius = 14f;    // only nearby monsters hear it
        public const float GreatBonus = 3f;

        public Transform Rotor;
        public Light Lamp;
        public Transform Body;       // engine model: chugs while it runs
        private Vector3 bodyRest;
        private bool bodyRestKnown;
        private float chugPhase;
        private float nextChug;

        public readonly NetworkVariable<int> Crankers = new(0);

        private readonly HashSet<PlayerInteractor> cranking = new();
        private float spinSpeed;

        public override InteractKind Describe(PlayerInteractor who, out string prompt)
        {
            var grid = PowerGrid.Instance;
            if (grid != null && grid.IsFull)
            {
                prompt = GameText.Get("interact.generatorFull");
                return InteractKind.None;
            }
            prompt = GameText.Get(IsStalled ? "interact.generatorStalled" : "interact.generator");
            return InteractKind.Hold;
        }

        public override bool ServerHold(PlayerInteractor who, float dt)
        {
            var grid = PowerGrid.Instance;
            if (grid == null) return false;
            cranking.Add(who);
            if (!IsStalled) grid.ServerAdd(CrankRatePerPlayer * dt);
            return true;
        }

        public override void ServerHoldEnded(PlayerInteractor who) => cranking.Remove(who);

        public override void ServerSkillCheck(PlayerInteractor who, SkillCheckResult result)
        {
            if (IsStalled) return;
            if (result == SkillCheckResult.Miss) ServerFail(FailStall, FailNoiseRadius);
            else if (result == SkillCheckResult.Great && PowerGrid.Instance != null) PowerGrid.Instance.ServerAdd(GreatBonus);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer) { cranking.Clear(); Crankers.Value = 0; }
        }

        private void Update()
        {
            if (IsServer && IsSpawned && Crankers.Value != cranking.Count) Crankers.Value = cranking.Count;

            var stalled = IsStalled;
            var crankers = IsSpawned && !stalled ? Crankers.Value : 0;
            spinSpeed = Mathf.MoveTowards(spinSpeed, crankers * 360f, (stalled ? 2000f : 720f) * Time.deltaTime);
            if (Rotor != null) Rotor.Rotate(spinSpeed * Time.deltaTime, 0f, 0f, Space.Self);
            Chug();
            if (Lamp == null) return;
            if (stalled)
            {
                // Sparking after a blow-up.
                Lamp.color = new Color(1f, 0.25f, 0.1f);
                Lamp.intensity = Random.value < 0.35f ? 4f : 0.2f;
                return;
            }
            var grid = PowerGrid.Instance;
            var fraction = grid != null && grid.IsSpawned ? grid.Fraction : 0f;
            Lamp.color = new Color(1f, 0.65f, 0.25f);
            if (CrashIntro.Blackout) { Lamp.intensity = 0f; return; }
            // Always a small glow so the generator can be found in the dark ship.
            Lamp.intensity = 2.5f + fraction * 1.5f + (crankers > 0 ? 0.6f + Mathf.PingPong(Time.time * 6f, 0.5f) : 0f);
        }

        // Chug-chug: the engine block bounces with each stroke while cranked, and idles faintly once charged.
        private void Chug()
        {
            if (Body == null) return;
            if (!bodyRestKnown) { bodyRest = Body.localPosition; bodyRestKnown = true; }
            var grid = PowerGrid.Instance;
            var charged = grid != null && grid.IsSpawned ? grid.Fraction : 0f;
            var rate = spinSpeed / 360f * 4f + (charged > 0.3f ? 1.2f : 0f); // strokes per second
            chugPhase += rate * Time.deltaTime;
            var amount = Mathf.Clamp01(spinSpeed / 360f) * 0.025f + (charged > 0.3f ? 0.004f : 0f);
            var stroke = Mathf.Pow(Mathf.Abs(Mathf.Sin(chugPhase * Mathf.PI)), 6f);
            Body.localPosition = bodyRest + Vector3.up * stroke * amount + Random.insideUnitSphere * amount * 0.15f;
            if (spinSpeed > 60f && Time.time >= nextChug)
            {
                nextChug = Time.time + 0.7f;
                ProtoAudio.PlayPitched(ProtoSound.GeneratorCrank, transform.position, 0.22f, 0.95f + Random.value * 0.1f);
            }
        }
    }
}
