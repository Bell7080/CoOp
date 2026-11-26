using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoOpHorror.Gameplay
{
    // Owner/host positions drive presentation only. Reused soft billboards drift through each
    // other instead of outlining the object with a solid shell or a visible rope.
    public sealed class TelekinesisAura : MonoBehaviour
    {
        private const int CloudCount = 32, StreamCount = 20, Capacity = 192;
        private static Material mistMaterial;
        private ParticleSystem mist;
        private readonly ParticleSystem.Particle[] particles = new ParticleSystem.Particle[Capacity];
        private readonly List<LineRenderer> sparks = new();
        private readonly Vector3[] sparkPoints = new Vector3[10];
        private Bounds localBounds;
        private float strength;

        public static TelekinesisAura For(Transform target)
        {
            var aura = target.GetComponentInChildren<TelekinesisAura>();
            if (aura != null) return aura;
            var go = new GameObject("TelekinesisAura");
            go.transform.SetParent(target, false);
            return go.AddComponent<TelekinesisAura>();
        }

        private void Awake()
        {
            if (mistMaterial == null)
            {
                // One shared texture/material, generated once; soft irregular edges without new assets.
                var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false) { name = "CurrentMist", wrapMode = TextureWrapMode.Clamp };
                for (var y = 0; y < 64; y++)
                for (var x = 0; x < 64; x++)
                {
                    var uv = new Vector2((x + 0.5f) / 32f - 1f, (y + 0.5f) / 32f - 1f);
                    var fade = Mathf.Pow(Mathf.Clamp01(1f - uv.sqrMagnitude), 3f);
                    var noise = Mathf.PerlinNoise(x * 0.11f, y * 0.11f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, fade * Mathf.Lerp(0.25f, 1f, noise)));
                }
                texture.Apply(false, true);
                mistMaterial = new Material(Resources.Load<Shader>("WorldMist")) { name = "CurrentMist", mainTexture = texture };
            }
            var first = true;
            foreach (var r in transform.parent.GetComponentsInChildren<Renderer>())
            {
                if (r.transform.IsChildOf(transform) || r is LineRenderer) continue;
                var world = r.bounds;
                // Transform all corners: rotated/scaled appliances must still fit their cloud.
                for (var i = 0; i < 8; i++)
                {
                    var corner = world.center + Vector3.Scale(world.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var point = transform.parent.InverseTransformPoint(corner);
                    if (first) { localBounds = new Bounds(point, Vector3.zero); first = false; }
                    else localBounds.Encapsulate(point);
                }
            }
            if (first) localBounds = new Bounds(Vector3.up * 0.25f, Vector3.one * 0.5f);
            mist = gameObject.AddComponent<ParticleSystem>();
            var main = mist.main;
            main.playOnAwake = false;
            main.loop = false;
            main.maxParticles = Capacity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            var emission = mist.emission; emission.enabled = false;
            var shape = mist.shape; shape.enabled = false;
            var renderer = mist.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mistMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            mist.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void Show(IReadOnlyList<Vector3> from, float weightSag)
        {
            strength = Mathf.MoveTowards(strength, from.Count > 0 ? 1f : 0f, Time.deltaTime * (from.Count > 0 ? 5f : 3f));
            var t = Time.time;
            var centre = transform.parent.TransformPoint(localBounds.center);
            var extents = Vector3.Scale(localBounds.extents, transform.parent.lossyScale);
            var size = Mathf.Clamp(extents.magnitude * 0.7f, 0.24f, 0.7f);
            var count = 0;
            if (strength > 0.01f)
            {
                for (var i = 0; i < CloudCount; i++)
                {
                    var phase = i * 2.39996f;
                    var drift = new Vector3(Mathf.Sin(phase + t * 0.73f), Mathf.Sin(phase * 1.7f - t * 0.61f), Mathf.Cos(phase - t * 0.83f));
                    var position = centre + Vector3.Scale(drift, extents + Vector3.one * 0.09f);
                    Puff(count++, position, size * (1.1f + Mathf.Sin(t * 1.9f + phase) * 0.35f), 0.065f, i, t);
                }
                for (var s = 0; s < from.Count && count + StreamCount <= Capacity; s++)
                for (var i = 0; i < StreamCount; i++)
                {
                    var f = Mathf.Repeat(i / (float)StreamCount + t * 0.19f, 1f);
                    var envelope = Mathf.Sin(f * Mathf.PI);
                    var noise = new Vector3(Mathf.Sin(t * 2.1f + i * 1.7f), Mathf.Cos(t * 1.7f + i * 2.3f), Mathf.Sin(t * 1.3f + i));
                    var position = Vector3.Lerp(from[s], centre, f) + (noise * 0.17f + Vector3.down * weightSag * 0.1f) * envelope;
                    Puff(count++, position, size * (0.55f + envelope * 0.5f), 0.07f * envelope, i + s * 23, t);
                }
            }
            mist.SetParticles(particles, count);
            while (sparks.Count < from.Count)
            {
                var go = new GameObject("FaintCurrent");
                go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.sharedMaterial = mistMaterial;
                line.positionCount = sparkPoints.Length;
                line.startWidth = 0.025f; line.endWidth = 0.008f;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                sparks.Add(line);
            }
            for (var s = 0; s < sparks.Count; s++)
            {
                var line = sparks[s];
                line.enabled = s < from.Count && strength > 0.01f && Mathf.Sin(t * 19f + s * 7f) > 0.75f;
                if (!line.enabled) continue;
                for (var i = 0; i < sparkPoints.Length; i++)
                {
                    var f = i / (sparkPoints.Length - 1f);
                    sparkPoints[i] = Vector3.Lerp(from[s], centre, f) + new Vector3(Mathf.Sin(i * 7f + t * 37f), Mathf.Cos(i * 5f - t * 29f), 0f) * (0.06f * Mathf.Sin(f * Mathf.PI));
                }
                line.SetPositions(sparkPoints);
                line.startColor = line.endColor = new Color(0.55f, 0.94f, 1f, 0.22f * strength);
            }
        }

        private void Puff(int index, Vector3 position, float size, float opacity, int seed, float time)
        {
            particles[index] = new ParticleSystem.Particle
            {
                position = position, startSize = size, rotation = seed * 137.5f + time * 12f,
                startColor = new Color(0.43f, 0.9f, 1f, opacity * strength),
                startLifetime = 1f, remainingLifetime = 1f, randomSeed = (uint)(seed + 1)
            };
        }
    }
}
