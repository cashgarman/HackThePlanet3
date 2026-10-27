using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace HackThePlanet
{
    // Procedural circuit-board backdrop for the flat rig rack. Traces are generated once per panel size from a fixed seed,
    // so rebuilding the rig UI never reshuffles the board. Pulses race along the traces and blinks light the pads.
    // FX+ animates; FX- and FX0 keep the board static, matching the other display effects.
    public sealed class CircuitBackdrop : VisualElement
    {
        private sealed class Trace
        {
            public Vector2[] points;
            public float[] lengths;
            public float length;
        }

        private const int Seed = 0x5EED;
        private const float Cell = 16f, PadSize = 2.6f, FrameInterval = 1f / 30f;
        private const int TraceDensity = 14;
        private static readonly Vector2Int[] Cardinal = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
        private static readonly Color TraceColor = new Color(.40f, .85f, .92f, .07f);
        private static readonly Color PulseColor = new Color(.40f, .90f, .96f, .55f);
        private static readonly Color PulseHead = new Color(.85f, 1f, 1f, .7f);

        private readonly List<Trace> traces = new List<Trace>();
        private readonly List<Vector2> pads = new List<Vector2>();
        private int builtWidth = -1, builtHeight = -1;
        private bool animating, repaintRequested = true;
        private float nextFrame;

        public CircuitBackdrop()
        {
            style.position = Position.Absolute;
            style.left = style.right = style.top = style.bottom = 0;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => repaintRequested = true);
        }

        // Called from the viewport's Tick so the backdrop shares the workstation's frame loop.
        public void Tick(SignalEffects effects)
        {
            bool animate = effects == SignalEffects.Full;
            if (animate != animating) { animating = animate; repaintRequested = true; }
            if (!animate && !repaintRequested) return;
            if (animate && Time.unscaledTime < nextFrame) return;
            nextFrame = Time.unscaledTime + FrameInterval;
            repaintRequested = false;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w <= 1 || h <= 1) return;
            int width = Mathf.RoundToInt(w), height = Mathf.RoundToInt(h);
            if (width != builtWidth || height != builtHeight) Generate(width, height);

            var p = context.painter2D;
            p.lineWidth = 1f;
            p.strokeColor = TraceColor;
            foreach (var trace in traces)
            {
                p.BeginPath();
                p.MoveTo(trace.points[0]);
                for (int i = 1; i < trace.points.Length; i++) p.LineTo(trace.points[i]);
                p.Stroke();
            }

            float time = Time.unscaledTime;
            p.fillColor = TraceColor;
            foreach (var pad in pads)
            {
                p.BeginPath();
                p.Arc(pad, PadSize, 0f, 360f);
                p.Fill();
            }
            if (!animating) return;

            // Blinking pads: a hash-picked subset of pads breathes on its own phase.
            for (int i = 0; i < pads.Count; i++)
            {
                uint h1 = Hash((uint)i, 3u);
                if (h1 % 3u != 0u) continue;
                float phase = (h1 % 997u) * .01f;
                float pulse = .5f + .5f * Mathf.Sin(time * (.8f + (h1 % 5u) * .15f) + phase);
                p.fillColor = new Color(TraceColor.r, TraceColor.g, TraceColor.b, .03f + .14f * pulse * pulse);
                p.BeginPath();
                p.Arc(pads[i], PadSize * 1.3f, 0f, 360f);
                p.Fill();
            }

            // Pulses: each one walks a trace, then hops to another trace chosen by its lap number.
            if (traces.Count == 0) return;
            int pulseCount = Mathf.Clamp(traces.Count / 4, 8, 30);
            for (int i = 0; i < pulseCount; i++)
            {
                float rate = .10f + (Hash((uint)i, 7u) % 100u) * .0022f;
                float lap = time * rate + (Hash((uint)i, 11u) % 1000u) * .001f;
                uint cycle = (uint)Mathf.FloorToInt(lap);
                float fraction = lap - Mathf.Floor(lap);
                var trace = traces[(int)(Hash((uint)i * 31u + cycle, 17u) % (uint)traces.Count)];
                DrawPulse(p, trace, fraction * trace.length);
            }
        }

        private static void DrawPulse(Painter2D p, Trace trace, float head)
        {
            // Tail fades from dim to bright toward the head.
            Span(p, trace, head - 120f, head - 60f, WithAlpha(PulseColor, .06f), 1f);
            Span(p, trace, head - 60f, head - 22f, WithAlpha(PulseColor, .18f), 1.4f);
            Span(p, trace, head - 22f, head, PulseColor, 2f);
            p.fillColor = PulseHead;
            p.BeginPath();
            p.Arc(PointAt(trace, Mathf.Clamp(head, 0f, trace.length)), 1.8f, 0f, 360f);
            p.Fill();
        }

        private static void Span(Painter2D p, Trace trace, float from, float to, Color color, float width)
        {
            from = Mathf.Clamp(from, 0f, trace.length);
            to = Mathf.Clamp(to, 0f, trace.length);
            if (to - from <= .5f) return;
            p.strokeColor = color;
            p.lineWidth = width;
            p.BeginPath();
            p.MoveTo(PointAt(trace, from));
            for (int i = 1; i < trace.points.Length; i++)
                if (trace.lengths[i] > from && trace.lengths[i] < to) p.LineTo(trace.points[i]);
            p.LineTo(PointAt(trace, to));
            p.Stroke();
        }

        private static Vector2 PointAt(Trace trace, float distance)
        {
            int i = 1;
            while (i < trace.points.Length - 1 && trace.lengths[i] < distance) i++;
            float segment = trace.lengths[i] - trace.lengths[i - 1];
            float t = segment > 0 ? Mathf.Clamp01((distance - trace.lengths[i - 1]) / segment) : 0f;
            return Vector2.Lerp(trace.points[i - 1], trace.points[i], t);
        }

        private static Color WithAlpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

        // Random walk on a grid with orthogonal turns, chamfered into 45 degree corners like PCB traces.
        private void Generate(int width, int height)
        {
            builtWidth = width;
            builtHeight = height;
            traces.Clear();
            pads.Clear();

            var random = new System.Random(Seed);
            int cols = Mathf.Max(2, Mathf.CeilToInt(width / Cell));
            int rows = Mathf.Max(2, Mathf.CeilToInt(height / Cell));
            var used = new bool[cols, rows];
            int target = Mathf.Max(8, cols * rows / TraceDensity);

            for (int attempt = 0; traces.Count < target && attempt < target * 14; attempt++)
            {
                int startX = random.Next(cols), startY = random.Next(rows);
                if (used[startX, startY]) continue;
                var cells = new List<Vector2Int> { new Vector2Int(startX, startY) };
                used[startX, startY] = true;
                var direction = Cardinal[random.Next(4)];
                int segments = random.Next(4, 10);
                for (int s = 0; s < segments; s++)
                {
                    if (random.NextDouble() < .5) direction = random.Next(2) == 0 ? new Vector2Int(direction.y, direction.x) : new Vector2Int(-direction.y, -direction.x);
                    int steps = random.Next(1, 6), moved = 0;
                    var current = cells[cells.Count - 1];
                    for (int k = 0; k < steps; k++)
                    {
                        var next = current + direction;
                        if (next.x < 0 || next.y < 0 || next.x >= cols || next.y >= rows || used[next.x, next.y]) break;
                        used[next.x, next.y] = true;
                        current = next;
                        moved++;
                    }
                    if (moved == 0) break;
                    cells.Add(current);
                }
                if (cells.Count < 3) continue;

                var corners = new List<Vector2>(cells.Count);
                foreach (var cell in cells) corners.Add(new Vector2((cell.x + .5f) * Cell, (cell.y + .5f) * Cell));
                var polyline = new List<Vector2> { corners[0] };
                float chamfer = Cell * .5f;
                for (int i = 1; i < corners.Count - 1; i++)
                {
                    var corner = corners[i];
                    polyline.Add(corner + (corners[i - 1] - corner).normalized * chamfer);
                    polyline.Add(corner + (corners[i + 1] - corner).normalized * chamfer);
                }
                polyline.Add(corners[corners.Count - 1]);
                traces.Add(BuildTrace(polyline));

                pads.Add(corners[0]);
                pads.Add(corners[corners.Count - 1]);
                for (int i = 1; i < corners.Count - 1; i++)
                    if (random.NextDouble() < .15) pads.Add(corners[i]);
            }
        }

        private static Trace BuildTrace(List<Vector2> polyline)
        {
            var trace = new Trace { points = polyline.ToArray(), lengths = new float[polyline.Count] };
            for (int i = 1; i < polyline.Count; i++)
                trace.lengths[i] = trace.lengths[i - 1] + Vector2.Distance(polyline[i - 1], polyline[i]);
            trace.length = trace.lengths[polyline.Count - 1];
            return trace;
        }

        private static uint Hash(uint a, uint b)
        {
            unchecked
            {
                uint h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u);
                h ^= h >> 15;
                h *= 0x85EBCA77u;
                h ^= h >> 13;
                return h;
            }
        }
    }
}
