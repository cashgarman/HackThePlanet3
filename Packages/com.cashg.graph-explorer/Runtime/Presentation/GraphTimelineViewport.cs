using System;

namespace Cashg.GraphExplorer
{
	/// <summary>A cursor-anchored window over normalized timeline coordinates.</summary>
	public sealed class GraphTimelineViewport
	{
		/// <summary>Gets the normalized start of the visible range.</summary>
		public double Start { get; private set; }

		/// <summary>Gets the normalized end of the visible range.</summary>
		public double End { get; private set; } = 1;

		/// <summary>Gets whether the range is zoomed in from the full timeline.</summary>
		public bool IsZoomed => Start > 0 || End < 1;

		/// <summary>Converts normalized timeline progress to the visible range.</summary>
		/// <param name="progress">Progress in full timeline coordinates.</param>
		/// <returns>Progress relative to the visible range.</returns>
		public double ToVisible(double progress)
		{
			return (progress - Start) / (End - Start);
		}

		/// <summary>Converts visible-range progress to normalized timeline coordinates.</summary>
		/// <param name="progress">Progress in visible-range coordinates.</param>
		/// <returns>Progress in full timeline coordinates.</returns>
		public double FromVisible(double progress)
		{
			return Start + Math.Max(0, Math.Min(1, progress)) * (End - Start);
		}

		/// <summary>Sets the visible normalized range, clamped to the timeline.</summary>
		/// <param name="start">Requested range start.</param>
		/// <param name="end">Requested range end.</param>
		public void SetRange(double start, double end)
		{
			// Reject invalid bounds and clamp a nonzero window to normalized history coordinates.
			if (double.IsNaN(start) || double.IsNaN(end) || double.IsInfinity(start) || double.IsInfinity(end) ||
				end <= start) return;
			var span = Math.Max(1e-9, Math.Min(1, end - start));
			Start = Math.Max(0, Math.Min(1 - span, start));
			End = Start + span;
		}

		/// <summary>Restores the full normalized timeline range.</summary>
		public void Reset()
		{
			Start = 0;
			End = 1;
		}

		/// <summary>Zooms around an anchor point in visible-range coordinates.</summary>
		/// <param name="anchor">Anchor position between zero and one.</param>
		/// <param name="factor">Scale factor applied to the current range width.</param>
		public void Zoom(double anchor, double factor)
		{
			// Preserve the time under the cursor while changing the window span, except at history boundaries.
			if (double.IsNaN(anchor) || double.IsInfinity(anchor) || double.IsNaN(factor) ||
				double.IsInfinity(factor) || factor <= 0) return;
			anchor = Math.Max(0, Math.Min(1, anchor));
			var span = Math.Max(1e-9, Math.Min(1, (End - Start) * factor));
			var time = FromVisible(anchor);
			Start = Math.Max(0, Math.Min(1 - span, time - span * anchor));
			End = Start + span;
		}
	}
}