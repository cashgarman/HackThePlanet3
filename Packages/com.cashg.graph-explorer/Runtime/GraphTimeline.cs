using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEngine;

namespace Cashg.GraphExplorer
{
	/// <summary>One timestamped sample of canonical graph-node positions.</summary>
	public sealed class GraphPositionFrame
	{
		/// <summary>Creates a timestamped sample of canonical node positions.</summary>
		/// <param name="timestampUtc">Time associated with the sample.</param>
		/// <param name="positions">Positions keyed by node identifier.</param>
		public GraphPositionFrame(DateTimeOffset timestampUtc, IEnumerable<KeyValuePair<string, Vector3>> positions)
		{
			TimestampUtc = timestampUtc.ToUniversalTime();

			// Copy only valid, finite position samples and reject duplicate node IDs before exposing the frame.
			var copy = new SortedDictionary<string, Vector3>(StringComparer.Ordinal);
			if (positions != null)
				foreach (var pair in positions)
				{
					if (string.IsNullOrWhiteSpace(pair.Key))
						throw new ArgumentException("Position sample node IDs cannot be empty.", nameof(positions));
					if (!Finite(pair.Value.x) || !Finite(pair.Value.y) || !Finite(pair.Value.z))
						throw new ArgumentOutOfRangeException(nameof(positions), "Node positions must be finite.");
					copy.Add(pair.Key, pair.Value);
				}

			Positions = new ReadOnlyDictionary<string, Vector3>(copy);
		}

		/// <summary>Gets the sample timestamp in UTC.</summary>
		public DateTimeOffset TimestampUtc { get; }
		/// <summary>Gets the sampled positions keyed by node identifier.</summary>
		public IReadOnlyDictionary<string, Vector3> Positions { get; }

		private static bool Finite(float value)
		{
			return !float.IsNaN(value) && !float.IsInfinity(value);
		}
	}

	/// <summary>Represents graph timeline change.</summary>
	public sealed class GraphTimelineChange
	{
		/// <summary>Creates a timeline change from before and after record sets.</summary>
		/// <param name="nodesBefore">Node records before the change.</param>
		/// <param name="nodesAfter">Node records after the change.</param>
		/// <param name="edgesBefore">Edge records before the change.</param>
		/// <param name="edgesAfter">Edge records after the change.</param>
		/// <param name="displayNameBefore">Dataset name before the change.</param>
		/// <param name="displayNameAfter">Dataset name after the change.</param>
		public GraphTimelineChange(IEnumerable<GraphNodeRecord> nodesBefore, IEnumerable<GraphNodeRecord> nodesAfter,
								   IEnumerable<GraphEdgeRecord> edgesBefore, IEnumerable<GraphEdgeRecord> edgesAfter,
								   string displayNameBefore, string displayNameAfter)
		{
			NodesBefore = Array.AsReadOnly((nodesBefore ?? Array.Empty<GraphNodeRecord>()).ToArray());
			NodesAfter = Array.AsReadOnly((nodesAfter ?? Array.Empty<GraphNodeRecord>()).ToArray());
			EdgesBefore = Array.AsReadOnly((edgesBefore ?? Array.Empty<GraphEdgeRecord>()).ToArray());
			EdgesAfter = Array.AsReadOnly((edgesAfter ?? Array.Empty<GraphEdgeRecord>()).ToArray());
			DisplayNameBefore = displayNameBefore ?? "";
			DisplayNameAfter = displayNameAfter ?? "";
		}

		/// <summary>Gets the node records before the change.</summary>
		public IReadOnlyList<GraphNodeRecord> NodesBefore { get; }
		/// <summary>Gets the node records after the change.</summary>
		public IReadOnlyList<GraphNodeRecord> NodesAfter { get; }
		/// <summary>Gets the edge records before the change.</summary>
		public IReadOnlyList<GraphEdgeRecord> EdgesBefore { get; }
		/// <summary>Gets the edge records after the change.</summary>
		public IReadOnlyList<GraphEdgeRecord> EdgesAfter { get; }
		/// <summary>Gets the dataset name before the change.</summary>
		public string DisplayNameBefore { get; }
		/// <summary>Gets the dataset name after the change.</summary>
		public string DisplayNameAfter { get; }
	}

	/// <summary>Represents graph timeline entry.</summary>
	public sealed class GraphTimelineEntry
	{
		/// <summary>Creates a sequenced timeline entry.</summary>
		/// <param name="sequence">Entry sequence number.</param>
		/// <param name="timestampUtc">Entry timestamp.</param>
		/// <param name="change">Change represented by the entry.</param>
		public GraphTimelineEntry(long sequence, DateTimeOffset timestampUtc, GraphTimelineChange change)
		{
			Sequence = sequence;
			TimestampUtc = timestampUtc.ToUniversalTime();
			Change = change ?? throw new ArgumentNullException(nameof(change));
		}

		/// <summary>Gets the sequence number of this entry.</summary>
		public long Sequence { get; }
		/// <summary>Gets the entry timestamp in UTC.</summary>
		public DateTimeOffset TimestampUtc { get; }
		/// <summary>Gets the change recorded by this entry.</summary>
		public GraphTimelineChange Change { get; }
	}

	/// <summary>Persistent, dataset-scoped change history. Baseline facts have no invented creation time.</summary>
	public sealed class GraphTimeline
	{
		private readonly List<GraphTimelineEntry> entries = new();
		private readonly List<GraphPositionFrame> positionFrames = new();
		private bool hasPlayhead;
		private DateTimeOffset playheadTimeUtc;
		private bool positionRecordingEnabled;
		private float positionSampleIntervalSeconds = 1f;
		/// <summary>Gets the workspace for cached external timeline events.</summary>
		public GraphTimelineWorkspace Workspace { get; } = new();
		/// <summary>Gets the baseline snapshot for event history.</summary>
		public GraphSnapshot Baseline { get; private set; }
		/// <summary>Gets the baseline timestamp in UTC.</summary>
		public DateTimeOffset BaselineTimeUtc { get; private set; }
		/// <summary>Gets the ordered event history entries.</summary>
		public IReadOnlyList<GraphTimelineEntry> Entries => entries.AsReadOnly();
		/// <summary>Gets the recorded node-position frames.</summary>
		public IReadOnlyList<GraphPositionFrame> PositionFrames => positionFrames.AsReadOnly();
		/// <summary>Gets or sets whether event and position history recording is enabled.</summary>
		public bool Enabled { get; set; } = true;

		/// <summary>Host capability gate. False hides and pauses capture without removing stored frames.</summary>
		/// <summary>Gets or sets whether the host permits position history recording.</summary>
		public bool PositionRecordingAvailable { get; set; } = true;

		/// <summary>Gets or sets whether node-position frames are recorded.</summary>
		public bool PositionRecordingEnabled
		{
			get => positionRecordingEnabled;
			set
			{
				if (positionRecordingEnabled == value) return;
				positionRecordingEnabled = value;
				Changed?.Invoke();
			}
		}

		/// <summary>Gets or sets the minimum interval between position samples.</summary>
		public float PositionSampleIntervalSeconds
		{
			get => positionSampleIntervalSeconds;
			set
			{
				if (float.IsNaN(value) || float.IsInfinity(value) || value < .1f || value > 60f)
					throw new ArgumentOutOfRangeException(nameof(value),
														  "Position sample interval must be between 0.1 and 60 seconds.");
				if (Mathf.Approximately(positionSampleIntervalSeconds, value)) return;
				positionSampleIntervalSeconds = value;
				Changed?.Invoke();
			}
		}

		/// <summary>Gets the selected event-history entry index.</summary>
		public int Cursor { get; internal set; } = -1;
		/// <summary>Gets whether an event-history baseline is available.</summary>
		public bool HasBaseline => Baseline != null;
		/// <summary>Gets whether any position frames have been recorded.</summary>
		public bool HasPositionHistory => positionFrames.Count > 0;
		/// <summary>Gets whether the playhead is earlier than the latest recorded time.</summary>
		public bool IsHistorical => HasBaseline && CurrentTimeUtc < LatestTimeUtc;
		/// <summary>Gets the current playhead time in UTC.</summary>
		public DateTimeOffset CurrentTimeUtc => hasPlayhead ? playheadTimeUtc : LatestTimeUtc;

		/// <summary>Gets the latest timestamp in the event and position history.</summary>
		public DateTimeOffset LatestTimeUtc
		{
			get
			{
				var latest = BaselineTimeUtc;
				if (entries.Count > 0 && entries[entries.Count - 1].TimestampUtc > latest)
					latest = entries[entries.Count - 1].TimestampUtc;
				if (positionFrames.Count > 0 && positionFrames[positionFrames.Count - 1].TimestampUtc > latest)
					latest = positionFrames[positionFrames.Count - 1].TimestampUtc;
				return latest;
			}
		}

		/// <summary>Gets the earliest timestamp available for position history.</summary>
		public DateTimeOffset EarliestTimeUtc =>
			positionFrames.Count > 0 && positionFrames[0].TimestampUtc < BaselineTimeUtc
				? positionFrames[0].TimestampUtc
				: BaselineTimeUtc;

		private DateTimeOffset LastRecordedTimeUtc
		{
			get
			{
				var latest = BaselineTimeUtc;
				if (entries.Count > 0 && entries[entries.Count - 1].TimestampUtc > latest)
					latest = entries[entries.Count - 1].TimestampUtc;
				if (positionFrames.Count > 0 && positionFrames[positionFrames.Count - 1].TimestampUtc > latest)
					latest = positionFrames[positionFrames.Count - 1].TimestampUtc;
				return latest;
			}
		}

		/// <summary>Occurs when timeline state changes.</summary>
		public event Action Changed;

		/// <summary>Establishes a new baseline and clears existing event and position history.</summary>
		/// <param name="snapshot">Snapshot to use as the baseline.</param>
		/// <param name="timestampUtc">Optional baseline timestamp.</param>
		public void EstablishBaseline(GraphSnapshot snapshot, DateTimeOffset? timestampUtc = null)
		{
			// Start both history tracks from the supplied snapshot and reset the playhead.
			Baseline = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
			BaselineTimeUtc = (timestampUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
			entries.Clear();
			positionFrames.Clear();
			Cursor = -1;
			playheadTimeUtc = BaselineTimeUtc;
			hasPlayhead = true;
		}

		internal void Append(GraphTimelineChange change, DateTimeOffset? timestampUtc = null)
		{
			if (!Enabled || !HasBaseline || change == null) return;

			// Discard entries beyond the cursor and keep new timestamps monotonic across both tracks.
			if (Cursor < entries.Count - 1) entries.RemoveRange(Cursor + 1, entries.Count - Cursor - 1);
			var timestamp = (timestampUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
			if (timestamp < LastRecordedTimeUtc) timestamp = LastRecordedTimeUtc;
			entries.Add(new GraphTimelineEntry(entries.Count + 1L, timestamp, change));
			Cursor = entries.Count - 1;
			playheadTimeUtc = timestamp;
			hasPlayhead = true;
		}

		/// <summary>Records a position frame when recording is enabled and the playhead is live.</summary>
		/// <param name="positions">Positions keyed by node identifier.</param>
		/// <param name="timestampUtc">Optional frame timestamp.</param>
		public void RecordPositionFrame(IEnumerable<KeyValuePair<string, Vector3>> positions,
										DateTimeOffset? timestampUtc = null)
		{
			if (!Enabled || !PositionRecordingAvailable || !PositionRecordingEnabled || !HasBaseline ||
				IsHistorical) return;

			// Append motion only in the live view and advance its playhead with the new sample.
			var wasHistorical = IsHistorical;
			var timestamp = (timestampUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
			if (timestamp < LastRecordedTimeUtc) timestamp = LastRecordedTimeUtc;
			positionFrames.Add(new GraphPositionFrame(timestamp, positions));
			if (!wasHistorical)
			{
				playheadTimeUtc = timestamp;
				hasPlayhead = true;
			}

			Changed?.Invoke();
		}

		/// <summary>Finds the latest event entry at or before a timestamp.</summary>
		/// <param name="timeUtc">Timestamp to query.</param>
		/// <returns>The matching entry index, or -1 when none precedes the timestamp.</returns>
		public int EntryIndexAt(DateTimeOffset timeUtc)
		{
			// Binary-search for the final semantic entry at or before the requested timestamp.
			var ticks = timeUtc.ToUniversalTime().UtcDateTime.Ticks;
			int low = 0, high = entries.Count - 1, result = -1;
			while (low <= high)
			{
				var middle = low + (high - low) / 2;
				if (entries[middle].TimestampUtc.UtcDateTime.Ticks <= ticks)
				{
					result = middle;
					low = middle + 1;
				}
				else
				{
					high = middle - 1;
				}
			}

			return result;
		}

		/// <summary>Interpolates recorded positions at a timestamp.</summary>
		/// <param name="timeUtc">Timestamp to query.</param>
		/// <returns>Positions keyed by node identifier.</returns>
		public IReadOnlyDictionary<string, Vector3> PositionsAt(DateTimeOffset timeUtc)
		{
			var result = new SortedDictionary<string, Vector3>(StringComparer.Ordinal);
			if (positionFrames.Count == 0) return new ReadOnlyDictionary<string, Vector3>(result);

			// Find the bracketing samples, using an endpoint frame outside the recorded range.
			var ticks = timeUtc.ToUniversalTime().UtcDateTime.Ticks;
			var right = 0;
			while (right < positionFrames.Count &&
				   positionFrames[right].TimestampUtc.UtcDateTime.Ticks < ticks) right++;
			var before = right > 0 ? positionFrames[right - 1] : null;
			var after = right < positionFrames.Count ? positionFrames[right] : null;
			if (before == null) before = after;
			if (after == null) after = before;
			if (before == null) return new ReadOnlyDictionary<string, Vector3>(result);
			var span = after.TimestampUtc.UtcDateTime.Ticks - before.TimestampUtc.UtcDateTime.Ticks;
			var amount = span <= 0
				? 0
				: Mathf.Clamp01((float)((ticks - before.TimestampUtc.UtcDateTime.Ticks) / (double)span));
			foreach (var pair in before.Positions) result[pair.Key] = pair.Value;

			// Interpolate shared nodes and retain positions that appear in only one frame.
			foreach (var pair in after.Positions)
				result[pair.Key] = before.Positions.TryGetValue(pair.Key, out var start)
					? Vector3.Lerp(start, pair.Value, amount)
					: pair.Value;
			return new ReadOnlyDictionary<string, Vector3>(result);
		}

		/// <summary>Removes recorded position frames.</summary>
		public void ClearPositionHistory()
		{
			if (positionFrames.Count == 0) return;
			positionFrames.Clear();
			if (hasPlayhead)
			{
				// Clamp the playhead to the remaining semantic history after removing motion samples.
				playheadTimeUtc = playheadTimeUtc < BaselineTimeUtc ? BaselineTimeUtc :
					playheadTimeUtc > LatestTimeUtc ? LatestTimeUtc : playheadTimeUtc;
				Cursor = EntryIndexAt(playheadTimeUtc);
			}

			Changed?.Invoke();
		}

		internal void ClearEventHistory(GraphSnapshot current, DateTimeOffset? timestampUtc = null,
										bool clearPositions = false)
		{
			// Promote the current graph to the baseline and optionally preserve the independent motion track.
			Baseline = current ?? throw new ArgumentNullException(nameof(current));
			BaselineTimeUtc = (timestampUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
			if (!clearPositions && positionFrames.Count > 0 &&
				positionFrames[positionFrames.Count - 1].TimestampUtc > BaselineTimeUtc)
				BaselineTimeUtc = positionFrames[positionFrames.Count - 1].TimestampUtc;
			entries.Clear();
			Cursor = -1;
			if (clearPositions) positionFrames.Clear();
			playheadTimeUtc = BaselineTimeUtc;
			hasPlayhead = true;
			Changed?.Invoke();
		}

		internal void SetPlayhead(DateTimeOffset timestampUtc)
		{
			playheadTimeUtc = timestampUtc.ToUniversalTime();
			hasPlayhead = true;
			Cursor = EntryIndexAt(playheadTimeUtc);
		}

		internal void Restore(GraphSnapshot baseline, DateTimeOffset baselineTimeUtc,
							  IEnumerable<GraphTimelineEntry> history, int cursor,
							  IEnumerable<GraphPositionFrame> positionHistory = null,
							  bool positionRecordingEnabled = false, float sampleIntervalSeconds = 1f,
							  DateTimeOffset? currentTimeUtc = null)
		{
			Baseline = baseline;
			BaselineTimeUtc = baselineTimeUtc.ToUniversalTime();

			// Restore each track in its own ordering before rebuilding the bounded cursor and playhead.
			entries.Clear();
			if (history != null) entries.AddRange(history.OrderBy(item => item.Sequence));
			positionFrames.Clear();
			if (positionHistory != null) positionFrames.AddRange(positionHistory.OrderBy(item => item.TimestampUtc));
			this.positionRecordingEnabled = positionRecordingEnabled;
			positionSampleIntervalSeconds = Mathf.Clamp(sampleIntervalSeconds, .1f, 60f);
			Cursor = Math.Max(-1, Math.Min(cursor, entries.Count - 1));
			playheadTimeUtc =
				(currentTimeUtc ?? (Cursor == entries.Count - 1 ? LatestTimeUtc :
					Cursor < 0 ? BaselineTimeUtc : entries[Cursor].TimestampUtc)).ToUniversalTime();
			hasPlayhead = true;
		}
	}
}
