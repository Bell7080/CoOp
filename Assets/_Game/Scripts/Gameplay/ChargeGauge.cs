using System.Collections.Generic;
using UnityEngine;

namespace CoOpHorror.Gameplay
{
    // Anything charged from the shared grid by holding the left button (appliances, cameras, lamps).
    public interface IChargeGauge
    {
        float GaugeFraction { get; }   // 0..1
        bool GaugeCharging { get; }    // someone is filling it right now (or it just changed)
        Transform transform { get; }
    }

    // Segmented charge gauge floating above an object (REPO-durability style): a row of cells that fill one
    // by one with a little pop and blip while it charges, then fades out once full or left alone.
    // Presentation only; reads the replicated charge.
    public sealed class ChargeGauge : MonoBehaviour
    {
        public const int Segments = 10;
        public static readonly Color FullColor = new(0.35f, 1f, 0.75f);
        public static readonly Color EmptyColor = new(0.08f, 0.12f, 0.12f, 0.85f);

        public float Height = 0.6f;   // above the object's pivot
        public float Width = 0.5f;

        private IChargeGauge source;
        private Transform row;
        private readonly List<Transform> cells = new();
        private readonly List<Renderer> cellRenderers = new();
        private readonly float[] pop = new float[Segments];
        private int filled = -1;
        private float visible;
        private float lastFraction = -1f;
        private float lastChange = -10f;
        private MaterialPropertyBlock block;
        private static Material material;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        private void Awake()
        {
            source = GetComponent<IChargeGauge>();
            block = new MaterialPropertyBlock();
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                material.SetColor(BaseColor, Color.white);
            }
            row = new GameObject("ChargeGauge").transform;
            row.SetParent(transform, false);
            var gap = Width / Segments;
            for (var i = 0; i < Segments; i++)
            {
                var cell = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(cell.GetComponent<Collider>());
                cell.name = "Cell" + i;
                cell.transform.SetParent(row, false);
                cell.transform.localPosition = new Vector3(-Width / 2f + gap * (i + 0.5f), 0f, 0f);
                var r = cell.GetComponent<Renderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                cells.Add(cell.transform);
                cellRenderers.Add(r);
            }
            row.gameObject.SetActive(false);
        }

        public static int FilledCells(float fraction) => Mathf.Clamp(Mathf.FloorToInt(fraction * Segments + 0.0001f), 0, Segments);

        private void LateUpdate()
        {
            if (source == null) return;
            var fraction = Mathf.Clamp01(source.GaugeFraction);
            if (!Mathf.Approximately(fraction, lastFraction)) { lastChange = Time.time; lastFraction = fraction; }
            var show = source.GaugeCharging || (fraction > 0f && Time.time - lastChange < (fraction >= 1f ? 1.2f : 2.5f));
            visible = Mathf.MoveTowards(visible, show ? 1f : 0f, Time.deltaTime * 4f);
            row.gameObject.SetActive(visible > 0.01f);
            if (visible <= 0.01f) { filled = FilledCells(fraction); return; }

            var now = FilledCells(fraction);
            if (filled >= 0 && now > filled)
                for (var i = filled; i < now; i++)
                {
                    pop[i] = 1f;
                    ProtoAudio.PlayPitched(ProtoSound.Pop, transform.position, 0.35f, 0.8f + i * 0.07f);
                }
            filled = now;

            var cam = Camera.main;
            row.position = transform.position + Vector3.up * Height; // world up, even when the item tumbles
            if (cam != null) row.rotation = Quaternion.LookRotation(row.position - cam.transform.position, Vector3.up);
            var gap = Width / Segments;
            for (var i = 0; i < Segments; i++)
            {
                pop[i] = Mathf.MoveTowards(pop[i], 0f, Time.deltaTime * 6f);
                var size = gap * 0.8f * (1f + pop[i] * 0.7f) * visible;
                cells[i].localScale = new Vector3(size, gap * 1.6f * (1f + pop[i] * 0.7f) * visible, 1f);
                block.SetColor(BaseColor, i < now ? Color.Lerp(FullColor, Color.white, pop[i]) : EmptyColor);
                cellRenderers[i].SetPropertyBlock(block);
            }
        }
    }
}
