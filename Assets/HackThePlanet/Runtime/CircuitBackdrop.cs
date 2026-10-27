using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Random = System.Random;

namespace HackThePlanet
{
	// Procedural circuit-board backdrop for the flat rig rack. Traces are generated once per panel size from a fixed seed,
	// so rebuilding the rig UI never reshuffles the board. Pulses race along the traces and blinks light the pads.
	// FX+ animates; FX- and FX0 keep the board static, matching the other display effects.
	/// <summary>Deterministic procedural circuit-board backdrop for the flat rig graph viewport.</summary>
	public sealed class CircuitBackdrop : VisualElement
	{
		// Generation seed, grid tuning, and the trace and pulse palette.
		// Fixed seed that keeps trace layout stable for a given panel size.
		private const int Seed = 0x5EED;

		// Grid cell size, pad radius, and pulse animation frame interval.
		private const float Cell = 16f, PadSize = 2.6f, FrameInterval = 1f / 30f;

		// Target trace count scales as one trace per this many grid cells.
		private const int TraceDensity = 14;

		// Orthogonal step directions used when walking trace paths on the grid.
		private static readonly Vector2Int[] Cardinal = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

		// Dim base color for static trace strokes and pad fills.
		private static readonly Color TraceColor = new(.40f, .85f, .92f, .07f);

		// Body color for animated pulses traveling along traces.
		private static readonly Color PulseColor = new(.40f, .90f, .96f, .55f);

		// Bright tip color drawn at the head of each moving pulse.
		private static readonly Color PulseHead = new(.85f, 1f, 1f, .7f);

		// Solder pad positions placed at trace endpoints and corners.
		private readonly List<Vector2> pads = new();

		// Generated trace polylines cached until the panel size changes.
		private readonly List<Trace> traces = new();

		// Whether pulses animate and whether a repaint was requested since the last frame.
		private bool animating, repaintRequested = true;

		// Panel dimensions the current trace board was generated for.
		private int builtWidth = -1, builtHeight = -1;

		// Next unscaled time when an animated board may repaint again.
		private float nextFrame;

		/// <summary>Creates a non-interactive full-bleed backdrop that regenerates traces when geometry changes.</summary>
		public CircuitBackdrop()
		{
			// Cover the parent without intercepting input.
			style.position = Position.Absolute;
			style.left = style.right = style.top = style.bottom = 0;
			pickingMode = PickingMode.Ignore;

			// Draw the board and request a repaint when the layout changes.
			generateVisualContent += Draw;
			RegisterCallback<GeometryChangedEvent>(_ => repaintRequested = true);
		}

		// Called from the viewport's Tick so the backdrop shares the workstation's frame loop.
		/// <summary>Repaints trace pulses when full FX animation is enabled, or after layout changes.</summary>
		/// <param name="effects">Display tier; only <see cref="SignalEffects.Full" /> animates pulses.</param>
		public void Tick(SignalEffects effects)
		{
			// Full FX animates; any other tier keeps the board static.
			var animate = effects == SignalEffects.Full;

			// Request a repaint when the animation mode changes.
			if (animate != animating)
			{
				animating = animate;
				repaintRequested = true;
			}

			// Static boards only repaint when requested.
			if (!animate && !repaintRequested)
				return;

			// Animated boards repaint at the frame interval.
			if (animate && Time.unscaledTime < nextFrame)
				return;

			// Schedule the next frame and queue the repaint.
			nextFrame = Time.unscaledTime + FrameInterval;
			repaintRequested = false;
			MarkDirtyRepaint();
		}

		// Paints traces, pads, blinking pads, and moving pulses for the current panel size.
		private void Draw(MeshGenerationContext context)
		{
			// Measure the panel and skip drawing when it has no size yet.
			float w = contentRect.width, h = contentRect.height;
			if (w <= 1 || h <= 1)
				return;

			// Rebuild the board when the panel size changes.
			int width = Mathf.RoundToInt(w), height = Mathf.RoundToInt(h);
			if (width != builtWidth || height != builtHeight)
				Generate(width, height);

			// Stroke every trace in the static board color.
			var p = context.painter2D;
			p.lineWidth = 1f;
			p.strokeColor = TraceColor;
			foreach (var trace in traces)
			{
				p.BeginPath();
				p.MoveTo(trace.points[0]);
				for (var i = 1; i < trace.points.Length; i++)
					p.LineTo(trace.points[i]);
				p.Stroke();
			}

			// Fill every pad with the trace color.
			var time = Time.unscaledTime;
			p.fillColor = TraceColor;
			foreach (var pad in pads)
			{
				p.BeginPath();
				p.Arc(pad, PadSize, 0f, 360f);
				p.Fill();
			}

			// Static boards stop here.
			if (!animating)
				return;

			// Blinking pads: a hash-picked subset of pads breathes on its own phase.
			for (var i = 0; i < pads.Count; i++)
			{
				// Skip the pads that do not blink.
				var h1 = Hash((uint)i, 3u);
				if (h1 % 3u != 0u)
					continue;

				// Give each blinking pad its own speed and phase.
				var phase = h1 % 997u * .01f;
				var pulse = .5f + .5f * Mathf.Sin(time * (.8f + h1 % 5u * .15f) + phase);

				// Fill the pad with a breathing alpha.
				p.fillColor = new Color(TraceColor.r, TraceColor.g, TraceColor.b, .03f + .14f * pulse * pulse);
				p.BeginPath();
				p.Arc(pads[i], PadSize * 1.3f, 0f, 360f);
				p.Fill();
			}

			// Pulses: each one walks a trace, then hops to another trace chosen by its lap number.
			if (traces.Count == 0)
				return;

			// Scale the pulse count with the trace count, clamped to 8..30.
			var pulseCount = Mathf.Clamp(traces.Count / 4, 8, 30);
			for (var i = 0; i < pulseCount; i++)
			{
				// Each pulse runs at its own rate and starts at its own offset.
				var rate = .10f + Hash((uint)i, 7u) % 100u * .0022f;
				var lap = time * rate + Hash((uint)i, 11u) % 1000u * .001f;
				var cycle = (uint)Mathf.FloorToInt(lap);
				var fraction = lap - Mathf.Floor(lap);

				// Pick the trace for this lap and draw the pulse along it.
				var trace = traces[(int)(Hash((uint)i * 31u + cycle, 17u) % (uint)traces.Count)];
				DrawPulse(p, trace, fraction * trace.length);
			}
		}

		// Draws a fading pulse tail and bright head along a trace at the given distance.
		private static void DrawPulse(Painter2D p, Trace trace, float head)
		{
			// Tail fades from dim to bright toward the head.
			Span(p, trace, head - 120f, head - 60f, WithAlpha(PulseColor, .06f), 1f);
			Span(p, trace, head - 60f, head - 22f, WithAlpha(PulseColor, .18f), 1.4f);
			Span(p, trace, head - 22f, head, PulseColor, 2f);

			// Mark the bright head point.
			p.fillColor = PulseHead;
			p.BeginPath();
			p.Arc(PointAt(trace, Mathf.Clamp(head, 0f, trace.length)), 1.8f, 0f, 360f);
			p.Fill();
		}

		// Strokes a colored segment of a trace between two arc-length distances.
		private static void Span(Painter2D p, Trace trace, float from, float to, Color color, float width)
		{
			// Clamp the span to the trace length.
			from = Mathf.Clamp(from, 0f, trace.length);
			to = Mathf.Clamp(to, 0f, trace.length);

			// Skip spans too short to draw.
			if (to - from <= .5f)
				return;

			// Stroke the span through the trace points that fall inside it.
			p.strokeColor = color;
			p.lineWidth = width;
			p.BeginPath();
			p.MoveTo(PointAt(trace, from));
			for (var i = 1; i < trace.points.Length; i++)
				if (trace.lengths[i] > from && trace.lengths[i] < to)
					p.LineTo(trace.points[i]);
			p.LineTo(PointAt(trace, to));
			p.Stroke();
		}

		// Interpolates a panel position along a trace at the given arc-length distance.
		private static Vector2 PointAt(Trace trace, float distance)
		{
			// Find the segment that contains the distance.
			var i = 1;
			while (i < trace.points.Length - 1 && trace.lengths[i] < distance) i++;

			// Interpolate inside that segment.
			var segment = trace.lengths[i] - trace.lengths[i - 1];
			var t = segment > 0 ? Mathf.Clamp01((distance - trace.lengths[i - 1]) / segment) : 0f;

			return Vector2.Lerp(trace.points[i - 1], trace.points[i], t);
		}

		// Returns the same RGB with a replaced alpha channel.
		private static Color WithAlpha(Color color, float alpha)
		{
			return new Color(color.r, color.g, color.b, alpha);
		}

		// Random walk on a grid with orthogonal turns, chamfered into 45 degree corners like PCB traces.
		// Rebuilds traces and pads for a new panel size using the fixed generation seed.
		private void Generate(int width, int height)
		{
			// Record the size this board was built for and clear the old board.
			builtWidth = width;
			builtHeight = height;
			traces.Clear();
			pads.Clear();

			// Seed the generator so the board is stable for a given size.
			var random = new Random(Seed);

			// Size the grid in cells and track which cells are taken.
			var cols = Mathf.Max(2, Mathf.CeilToInt(width / Cell));
			var rows = Mathf.Max(2, Mathf.CeilToInt(height / Cell));
			var used = new bool[cols, rows];

			// Stop once enough traces exist for this grid.
			var target = Mathf.Max(8, cols * rows / TraceDensity);

			// Try random start cells until the target trace count is reached.
			for (var attempt = 0; traces.Count < target && attempt < target * 14; attempt++)
			{
				// Skip start cells that are already taken.
				int startX = random.Next(cols), startY = random.Next(rows);
				if (used[startX, startY])
					continue;

				// Start the trace at this cell and pick an initial direction.
				var cells = new List<Vector2Int> { new(startX, startY) };
				used[startX, startY] = true;
				var direction = Cardinal[random.Next(4)];
				var segments = random.Next(4, 10);

				// Walk the trace one straight run at a time.
				for (var s = 0; s < segments; s++)
				{
					// Sometimes turn 90 degrees, left or right.
					if (random.NextDouble() < .5)
						direction = random.Next(2) == 0
							? new Vector2Int(direction.y, direction.x)
							: new Vector2Int(-direction.y, -direction.x);

					// Walk up to the chosen number of free cells in this direction.
					int steps = random.Next(1, 6), moved = 0;
					var current = cells[cells.Count - 1];
					for (var k = 0; k < steps; k++)
					{
						// Stop the run at the grid edge or at a used cell.
						var next = current + direction;
						if (next.x < 0 || next.y < 0 || next.x >= cols || next.y >= rows || used[next.x, next.y])
							break;

						// Claim the cell and advance.
						used[next.x, next.y] = true;
						current = next;
						moved++;
					}

					// End the walk when the run could not move.
					if (moved == 0)
						break;
					cells.Add(current);
				}

				// Discard traces too short to look like a route.
				if (cells.Count < 3)
					continue;

				// Convert the cells to panel points at their centers.
				var corners = new List<Vector2>(cells.Count);
				foreach (var cell in cells)
					corners.Add(new Vector2((cell.x + .5f) * Cell, (cell.y + .5f) * Cell));

				// Chamfer each interior corner into two points for a 45 degree bend.
				var polyline = new List<Vector2> { corners[0] };
				var chamfer = Cell * .5f;
				for (var i = 1; i < corners.Count - 1; i++)
				{
					var corner = corners[i];
					polyline.Add(corner + (corners[i - 1] - corner).normalized * chamfer);
					polyline.Add(corner + (corners[i + 1] - corner).normalized * chamfer);
				}

				// Finish the polyline at the last corner and store the trace.
				polyline.Add(corners[corners.Count - 1]);
				traces.Add(BuildTrace(polyline));

				// Place pads at both ends and at random interior corners.
				pads.Add(corners[0]);
				pads.Add(corners[corners.Count - 1]);
				for (var i = 1; i < corners.Count - 1; i++)
					if (random.NextDouble() < .15)
						pads.Add(corners[i]);
			}
		}

		// Builds a trace with cumulative arc-lengths from a polyline of panel points.
		private static Trace BuildTrace(List<Vector2> polyline)
		{
			// Create the trace with its points and an empty length table.
			var trace = new Trace { points = polyline.ToArray(), lengths = new float[polyline.Count] };

			// Accumulate the distance from the start to each point.
			for (var i = 1; i < polyline.Count; i++)
				trace.lengths[i] = trace.lengths[i - 1] + Vector2.Distance(polyline[i - 1], polyline[i]);

			// Store the total length of the trace.
			trace.length = trace.lengths[polyline.Count - 1];

			return trace;
		}

		// Mixes two integers into a deterministic pseudo-random hash for pulse timing.
		private static uint Hash(uint a, uint b)
		{
			unchecked
			{
				// Mix the inputs with a multiplicative hash.
				var h = (a * 0x9E3779B1u) ^ (b + 0x7F4A7C15u);
				h ^= h >> 15;
				h *= 0x85EBCA77u;
				h ^= h >> 13;

				return h;
			}
		}

		private sealed class Trace
		{
			public float length;
			public float[] lengths;
			public Vector2[] points;
		}
	}
}