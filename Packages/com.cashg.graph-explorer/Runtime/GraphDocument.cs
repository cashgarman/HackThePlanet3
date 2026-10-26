using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEngine;

namespace Cashg.GraphExplorer
{
	/// <summary>Read-only live set, without exposing the mutable adjacency collection.</summary>
	public sealed class GraphIdSet : IReadOnlyCollection<string>
	{
		internal readonly HashSet<string> Values = new(StringComparer.Ordinal);
		/// <summary>Gets the number of identifiers in the set.</summary>
		public int Count => Values.Count;

		/// <summary>Enumerates the identifiers in the set.</summary>
		/// <returns>An enumerator over the identifiers.</returns>
		public IEnumerator<string> GetEnumerator()
		{
			return Values.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		/// <summary>Determines whether the set contains an identifier.</summary>
		/// <param name="id">Identifier to find.</param>
		/// <returns><see langword="true"/> when the identifier is present.</returns>
		public bool Contains(string id)
		{
			return Values.Contains(id);
		}
	}

	/// <summary>Dataset-neutral facts and indexes. Single-threaded; apply provider results on the owner thread.</summary>
	public sealed class GraphDocument
	{
		private readonly Dictionary<string, Adjacency> adjacency = new(StringComparer.Ordinal);
		private readonly Dictionary<string, GraphEdgeRecord> edges = new(StringComparer.Ordinal);
		private readonly Dictionary<string, GraphNodeRecord> nodes = new(StringComparer.Ordinal);
		private bool suppressTimeline;

		/// <summary>Creates an empty document for a dataset.</summary>
		/// <param name="datasetId">Stable identifier of the dataset.</param>
		/// <param name="displayName">Optional human-readable dataset name.</param>
		public GraphDocument(string datasetId, string displayName = "")
		{
			Timeline.Workspace.Changed += () => Changed?.Invoke();
			DatasetId = GraphRecordCopy.Required(datasetId, nameof(datasetId));
			DisplayName = displayName ?? "";
			Nodes = new ReadOnlyDictionary<string, GraphNodeRecord>(nodes);
			Edges = new ReadOnlyDictionary<string, GraphEdgeRecord>(edges);
			Timeline.Changed += OnTimelineChanged;
		}

		/// <summary>Gets the current node records keyed by identifier.</summary>
		public IReadOnlyDictionary<string, GraphNodeRecord> Nodes { get; }
		/// <summary>Gets the current edge records keyed by identifier.</summary>
		public IReadOnlyDictionary<string, GraphEdgeRecord> Edges { get; }
		/// <summary>Gets the stable identifier of this dataset.</summary>
		public string DatasetId { get; }
		/// <summary>Gets the display name of this dataset.</summary>
		public string DisplayName { get; private set; }
		/// <summary>Gets the document revision, including non-topology changes.</summary>
		public int Revision { get; private set; }
		/// <summary>Gets the revision that changes when graph topology changes.</summary>
		public int TopologyRevision { get; private set; }
		/// <summary>Gets the document's event and position timeline.</summary>
		public GraphTimeline Timeline { get; } = new();
		/// <summary>Occurs when document content or timeline state changes.</summary>
		public event Action Changed;

		private void OnTimelineChanged()
		{
			Changed?.Invoke();
		}

		/// <summary>Changes the display name and records the rename in the timeline.</summary>
		/// <param name="displayName">New display name.</param>
		public void Rename(string displayName)
		{
			if (DisplayName == (displayName ?? "")) return;
			EnsureLive();
			var previous = DisplayName;

			// Record the old and new names so seeking can replay the rename.
			DisplayName = displayName ?? "";
			Revision++;
			Timeline.Append(new GraphTimelineChange(null, null, null, null, previous, DisplayName));
			Changed?.Invoke();
		}

		/// <summary>Gets the node identifiers adjacent to a node.</summary>
		/// <param name="id">Node identifier.</param>
		/// <returns>The set of neighboring node identifiers.</returns>
		public GraphIdSet Neighbors(string id)
		{
			return adjacency[id].Neighbors;
		}

		/// <summary>Gets the edge identifiers incident to a node.</summary>
		/// <param name="id">Node identifier.</param>
		/// <returns>The set of incident edge identifiers.</returns>
		public GraphIdSet IncidentEdges(string id)
		{
			return adjacency[id].Edges;
		}

		/// <summary>Adds or replaces a node record and updates document history.</summary>
		/// <param name="node">Node record to apply.</param>
		public void UpsertNode(GraphNodeRecord node)
		{
			if (node == null) throw new ArgumentNullException(nameof(node));
			EnsureLive();

			// Compare before updating the adjacency index so no-op upserts preserve the revision.
			nodes.TryGetValue(node.Id, out var previous);
			if (previous != null && Equivalent(previous, node)) return;
			if (!nodes.ContainsKey(node.Id))
			{
				adjacency.Add(node.Id, new Adjacency());
				TopologyRevision++;
			}
			else if (ReferenceEquals(nodes[node.Id], node))
			{
				return;
			}
			else if (nodes[node.Id].TypeId != node.TypeId)
			{
				TopologyRevision++;
			}

			// Publish the replacement and record its before/after values outside batch replay.
			nodes[node.Id] = node;
			Revision++;
			if (!suppressTimeline)
			{
				Timeline.Append(new GraphTimelineChange(previous == null ? null : new[] { previous }, new[] { node },
														null, null, DisplayName, DisplayName));
				Changed?.Invoke();
			}
		}

		/// <summary>Adds or replaces an edge record and updates document history.</summary>
		/// <param name="edge">Edge record to apply.</param>
		public void UpsertEdge(GraphEdgeRecord edge)
		{
			if (edge == null) throw new ArgumentNullException(nameof(edge));
			EnsureLive();

			// Reject dangling edges before changing records or adjacency indexes.
			if (!nodes.ContainsKey(edge.From) || !nodes.ContainsKey(edge.To))
				throw new ArgumentException("Both edge endpoints must exist.", nameof(edge));
			edges.TryGetValue(edge.Id, out var prior);
			if (prior != null && Equivalent(prior, edge)) return;
			if (edges.TryGetValue(edge.Id, out var previous))
			{
				if (ReferenceEquals(edge, previous)) return;
				if (previous.From != edge.From || previous.To != edge.To)
				{
					// Retargeting must repair both endpoint indexes before replacing the edge.
					Unlink(previous);
					Link(edge);
					TopologyRevision++;
				}
				else if (previous.TypeId != edge.TypeId)
				{
					TopologyRevision++;
				}
			}
			else
			{
				Link(edge);
				TopologyRevision++;
			}

			// Preserve evidence-only updates in history without rebuilding the topology.
			edges[edge.Id] = edge;
			Revision++;
			if (!suppressTimeline)
			{
				Timeline.Append(new GraphTimelineChange(null, null, prior == null ? null : new[] { prior },
														new[] { edge }, DisplayName, DisplayName));
				Changed?.Invoke();
			}
		}

		private void Link(GraphEdgeRecord edge)
		{
			adjacency[edge.From].Edges.Values.Add(edge.Id);
			adjacency[edge.To].Edges.Values.Add(edge.Id);
			if (edge.From == edge.To) return; // Preserve recursive facts, but exclude self from unique-neighbor degree.
			AddNeighbor(edge.From, edge.To);
			AddNeighbor(edge.To, edge.From);
		}

		private void AddNeighbor(string from, string to)
		{
			var index = adjacency[from];

			// Keep duplicate parallel edges counted while exposing each neighbor only once.
			index.Counts.TryGetValue(to, out var count);
			index.Counts[to] = count + 1;
			index.Neighbors.Values.Add(to);
		}

		private void Unlink(GraphEdgeRecord edge)
		{
			adjacency[edge.From].Edges.Values.Remove(edge.Id);
			adjacency[edge.To].Edges.Values.Remove(edge.Id);
			if (edge.From == edge.To) return;
			RemoveNeighbor(edge.From, edge.To);
			RemoveNeighbor(edge.To, edge.From);
		}

		private void RemoveNeighbor(string from, string to)
		{
			var index = adjacency[from];
			var count = index.Counts[to] - 1;
			if (count > 0)
			{
				index.Counts[to] = count;
			}
			else
			{
				// Remove the neighbor only after the last parallel edge has disappeared.
				index.Counts.Remove(to);
				index.Neighbors.Values.Remove(to);
			}
		}

		/// <summary>Removes an edge and updates the document when it exists.</summary>
		/// <param name="id">Edge identifier.</param>
		/// <returns><see langword="true"/> when an edge was removed.</returns>
		public bool RemoveEdge(string id)
		{
			EnsureLive();
			if (!edges.TryGetValue(id, out var edge)) return false;

			// Remove endpoint references before publishing the deletion to history listeners.
			Unlink(edge);
			edges.Remove(id);
			Revision++;
			TopologyRevision++;
			if (!suppressTimeline)
			{
				Timeline.Append(new GraphTimelineChange(null, null, new[] { edge }, null, DisplayName, DisplayName));
				Changed?.Invoke();
			}

			return true;
		}

		/// <summary>Removes a node and its incident edges when it exists.</summary>
		/// <param name="id">Node identifier.</param>
		/// <returns><see langword="true"/> when a node was removed.</returns>
		public bool RemoveNode(string id)
		{
			EnsureLive();
			if (!nodes.ContainsKey(id)) return false;
			var before = Snapshot();

			// Suppress per-edge history entries so removing the node records one coherent change.
			suppressTimeline = true;
			try
			{
				foreach (var edge in adjacency[id].Edges.ToArray()) RemoveEdge(edge);
				adjacency.Remove(id);
				nodes.Remove(id);
				Revision++;
				TopologyRevision++;
			}
			finally
			{
				suppressTimeline = false;
			}

			RecordDifference(before, Snapshot());
			return true;
		}

		/// <summary>Collects nodes reachable within the requested number of hops.</summary>
		/// <param name="id">Starting node identifier.</param>
		/// <param name="depth">Maximum number of hops to traverse.</param>
		/// <returns>Identifiers in the bounded neighborhood.</returns>
		public HashSet<string> Neighborhood(string id, int depth)
		{
			if (depth < 0) throw new ArgumentOutOfRangeException(nameof(depth));
			var found = new HashSet<string>(StringComparer.Ordinal);
			if (id == null || !nodes.ContainsKey(id)) return found;
			var pending = new Queue<(string id, int level)>();
			found.Add(id);
			pending.Enqueue((id, 0));

			// Visit each node once and stop expanding when its hop limit is reached.
			while (pending.Count > 0)
			{
				var current = pending.Dequeue();
				if (current.level >= depth) continue;
				foreach (var neighbor in adjacency[current.id].Neighbors)
					if (found.Add(neighbor))
						pending.Enqueue((neighbor, current.level + 1));
			}

			return found;
		}

		/// <summary>Creates a snapshot of the current document contents.</summary>
		/// <returns>A snapshot containing the current nodes and edges.</returns>
		public GraphSnapshot Snapshot()
		{
			return new GraphSnapshot(DatasetId, DisplayName, nodes.Values, edges.Values);
		}

		/// <summary>Replaces document contents with a snapshot.</summary>
		/// <param name="snapshot">Snapshot to apply.</param>
		public void ReplaceSnapshot(GraphSnapshot snapshot)
		{
			if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
			if (snapshot.DatasetId != DatasetId)
				throw new ArgumentException("A snapshot belongs to a different dataset.", nameof(snapshot));
			EnsureLive();
			var beforeSnapshot = Timeline.HasBaseline ? Snapshot() : null;
			// Validate in a detached document before touching current facts or indexes.
			var incoming = new GraphDocument(DatasetId, snapshot.DisplayName);
			var batch = new GraphBatch();
			batch.Nodes.AddRange(snapshot.Nodes);
			batch.Edges.AddRange(snapshot.Edges);
			incoming.Apply(batch);
			int before = Revision, topologyBefore = TopologyRevision;
			suppressTimeline = true;
			try
			{
				// Install the validated snapshot while exposing one revision for the replacement.
				nodes.Clear();
				edges.Clear();
				adjacency.Clear();
				foreach (var node in incoming.Nodes.Values) UpsertNode(node);
				foreach (var edge in incoming.Edges.Values) UpsertEdge(edge);
				DisplayName = incoming.DisplayName;
				Revision = before + 1;
				TopologyRevision = topologyBefore + 1;
			}
			finally
			{
				suppressTimeline = false;
			}

			if (beforeSnapshot != null) RecordDifference(beforeSnapshot, Snapshot());
		}

		/// <summary>Applies node and edge additions, updates, and removals as one batch.</summary>
		/// <param name="batch">Changes to apply.</param>
		public void Apply(GraphBatch batch)
		{
			if (batch == null) throw new ArgumentNullException(nameof(batch));
			EnsureLive();

			// Freeze the batch and reject conflicting identities before mutating the document.
			var nodeUpdates = batch.Nodes.ToArray();
			var edgeUpdates = batch.Edges.ToArray();
			var removedNodes =
				new HashSet<string>(batch.RemoveNodes.Select(id => GraphRecordCopy.Required(id, "nodeId")),
									StringComparer.Ordinal);
			var removedEdges =
				new HashSet<string>(batch.RemoveEdges.Select(id => GraphRecordCopy.Required(id, "edgeId")),
									StringComparer.Ordinal);
			var updatedNodes = new HashSet<string>(StringComparer.Ordinal);
			var updatedEdges = new HashSet<string>(StringComparer.Ordinal);
			foreach (var node in nodeUpdates)
				if (node == null || !updatedNodes.Add(node.Id) || removedNodes.Contains(node.Id))
					throw new ArgumentException("Null, duplicate or conflicting node update.", nameof(batch));
			foreach (var edge in edgeUpdates)
			{
				if (edge == null || !updatedEdges.Add(edge.Id) || removedEdges.Contains(edge.Id))
					throw new ArgumentException("Null, duplicate or conflicting edge update.", nameof(batch));

				bool Exists(string id)
				{
					return !removedNodes.Contains(id) && (nodes.ContainsKey(id) || updatedNodes.Contains(id));
				}

				// Resolve endpoints against the final batch state, including newly added nodes.
				if (!Exists(edge.From) || !Exists(edge.To))
					throw new ArgumentException("An edge references a missing or removed endpoint.", nameof(batch));
			}

			int before = Revision, topologyBefore = TopologyRevision;
			var beforeSnapshot = Timeline.HasBaseline ? Snapshot() : null;
			suppressTimeline = true;
			try
			{
				// Apply removals first, then install nodes before their referencing edges.
				foreach (var id in removedEdges) RemoveEdge(id);
				foreach (var id in removedNodes) RemoveNode(id);
				foreach (var node in nodeUpdates) UpsertNode(node);
				foreach (var edge in edgeUpdates) UpsertEdge(edge);
				if (Revision != before) Revision = before + 1;
				if (TopologyRevision != topologyBefore) TopologyRevision = topologyBefore + 1;
			}
			finally
			{
				suppressTimeline = false;
			}

			if (Revision != before && beforeSnapshot != null) RecordDifference(beforeSnapshot, Snapshot());
		}

		/// <summary>Records the current state as the baseline for timeline history.</summary>
		/// <param name="timestampUtc">Optional timestamp for the baseline.</param>
		public void EstablishBaseline(DateTimeOffset? timestampUtc = null)
		{
			Timeline.EstablishBaseline(Snapshot(), timestampUtc);
		}

		/// <summary>Record a timestamped sample of canonical graph-node positions.</summary>
		/// <param name="positions">Node positions to record.</param>
		/// <param name="timestampUtc">Optional timestamp for the frame.</param>
		public void RecordPositionFrame(IEnumerable<KeyValuePair<string, Vector3>> positions,
										DateTimeOffset? timestampUtc = null)
		{
			if (positions == null) throw new ArgumentNullException(nameof(positions));

			// Validate the complete frame before admitting any sample to position history.
			var frame = positions.ToArray();
			foreach (var pair in frame)
				if (!nodes.ContainsKey(pair.Key))
					throw new ArgumentException("Position samples must reference nodes in this graph: " + pair.Key,
												nameof(positions));
			Timeline.RecordPositionFrame(frame, timestampUtc);
		}

		/// <summary>Seek graph facts to the latest recorded event at or before the requested time.</summary>
		/// <summary>Moves the document timeline to the state at a timestamp.</summary>
		/// <param name="timeUtc">Timestamp to seek to.</param>
		public void Seek(DateTimeOffset timeUtc)
		{
			if (!Timeline.HasBaseline) EstablishBaseline();
			var time = timeUtc.ToUniversalTime();

			// Clamp the requested timestamp and replay semantic edits through that time.
			if (time < Timeline.EarliestTimeUtc) time = Timeline.EarliestTimeUtc;
			if (time > Timeline.LatestTimeUtc) time = Timeline.LatestTimeUtc;
			var index = Timeline.EntryIndexAt(time);
			var snapshot = Reconstruct(index);
			RestoreSnapshot(snapshot);
			Timeline.SetPlayhead(time);
			Changed?.Invoke();
		}

		/// <summary>Discard position frames while preserving semantic event history.</summary>
		/// <summary>Removes recorded position frames.</summary>
		public void ClearPositionHistory()
		{
			Timeline.ClearPositionHistory();
		}

		/// <summary>Discard event history and establish the current graph as a fresh baseline.</summary>
		/// <summary>Removes event history and optionally records the current state as a baseline.</summary>
		/// <param name="timestampUtc">Optional timestamp for the new baseline.</param>
		public void ClearEventHistory(DateTimeOffset? timestampUtc = null)
		{
			Timeline.ClearEventHistory(Snapshot(), timestampUtc);
		}

		/// <summary>Discard both history streams and establish the current graph as a fresh baseline.</summary>
		/// <summary>Clears both event history and recorded position frames.</summary>
		/// <param name="timestampUtc">Optional timestamp for the new baseline.</param>
		public void ClearHistory(DateTimeOffset? timestampUtc = null)
		{
			Timeline.ClearEventHistory(Snapshot(), timestampUtc, true);
		}

		/// <summary>Moves the event timeline to an entry by index.</summary>
		/// <param name="entryIndex">Index of the timeline entry to seek to.</param>
		public void Seek(int entryIndex)
		{
			if (!Timeline.HasBaseline) EstablishBaseline();
			if (entryIndex < -1 || entryIndex >= Timeline.Entries.Count)
				throw new ArgumentOutOfRangeException(nameof(entryIndex));

			// Restore the baseline or selected entry, then align the timestamp playhead.
			var snapshot = Reconstruct(entryIndex);
			RestoreSnapshot(snapshot);
			Timeline.Cursor = entryIndex;
			Timeline.SetPlayhead(entryIndex < 0 ? Timeline.BaselineTimeUtc : Timeline.Entries[entryIndex].TimestampUtc);
			Changed?.Invoke();
		}

		/// <summary>Returns the document to its live state after timeline inspection.</summary>
		public void ReturnToLive()
		{
			Seek(Timeline.LatestTimeUtc);
		}

		internal void RestoreTimeline(GraphSnapshot baseline, DateTimeOffset baselineTimeUtc,
									  IEnumerable<GraphTimelineEntry> entries, int cursor,
									  IEnumerable<GraphPositionFrame> positionFrames = null,
									  bool positionRecordingEnabled = false, float sampleIntervalSeconds = 1f,
									  DateTimeOffset? currentTimeUtc = null)
		{
			if (baseline == null || baseline.DatasetId != DatasetId)
				throw new ArgumentException("Timeline baseline must belong to this dataset.", nameof(baseline));
			Timeline.Restore(baseline, baselineTimeUtc, entries, cursor, positionFrames, positionRecordingEnabled,
							 sampleIntervalSeconds, currentTimeUtc);
		}

		/// <summary>Copies a compatible document's recorded history into this document.</summary>
		/// <summary>Restores timeline state from another document.</summary>
		/// <param name="source">Document whose timeline state is copied.</param>
		public void RestoreTimelineFrom(GraphDocument source)
		{
			if (source == null) throw new ArgumentNullException(nameof(source));
			if (source.DatasetId != DatasetId)
				throw new ArgumentException("Timeline source belongs to a different dataset.", nameof(source));
			if (!source.Timeline.HasBaseline)
				throw new ArgumentException("Timeline source has no baseline.", nameof(source));

			// Carry portable workspace state alongside semantic edits and position frames.
			Timeline.Workspace.RestoreFrom(source.Timeline.Workspace);
			RestoreTimeline(source.Timeline.Baseline, source.Timeline.BaselineTimeUtc, source.Timeline.Entries,
							source.Timeline.Cursor,
							source.Timeline.PositionFrames, source.Timeline.PositionRecordingEnabled,
							source.Timeline.PositionSampleIntervalSeconds, source.Timeline.CurrentTimeUtc);
		}

		private void EnsureLive()
		{
			if (!suppressTimeline && Timeline.HasBaseline && Timeline.IsHistorical) ReturnToLive();
		}

		private GraphSnapshot Reconstruct(int entryIndex)
		{
			// Replay into a detached document so seeking cannot append new live history.
			var state = new GraphDocument(DatasetId, Timeline.Baseline.DisplayName);
			state.suppressTimeline = true;
			var baseline = new GraphBatch();
			baseline.Nodes.AddRange(Timeline.Baseline.Nodes);
			baseline.Edges.AddRange(Timeline.Baseline.Edges);
			state.Apply(baseline);
			for (var i = 0; i <= entryIndex; i++)
			{
				// Records absent from the after-set are deletions; the remaining after-set is upserted.
				var change = Timeline.Entries[i].Change;
				var batch = new GraphBatch();
				foreach (var node in change.NodesBefore)
					if (!change.NodesAfter.Any(item => item.Id == node.Id))
						batch.RemoveNodes.Add(node.Id);
				foreach (var edge in change.EdgesBefore)
					if (!change.EdgesAfter.Any(item => item.Id == edge.Id))
						batch.RemoveEdges.Add(edge.Id);
				batch.Nodes.AddRange(change.NodesAfter);
				batch.Edges.AddRange(change.EdgesAfter);
				state.Apply(batch);
				if (change.DisplayNameBefore != change.DisplayNameAfter) state.Rename(change.DisplayNameAfter);
			}

			return state.Snapshot();
		}

		private void RestoreSnapshot(GraphSnapshot snapshot)
		{
			// Rebuild facts and adjacency without recording the historical view as a new edit.
			suppressTimeline = true;
			try
			{
				var incoming = new GraphDocument(DatasetId, snapshot.DisplayName);
				var batch = new GraphBatch();
				batch.Nodes.AddRange(snapshot.Nodes);
				batch.Edges.AddRange(snapshot.Edges);
				incoming.Apply(batch);
				nodes.Clear();
				edges.Clear();
				adjacency.Clear();
				foreach (var node in incoming.Nodes.Values) UpsertNode(node);
				foreach (var edge in incoming.Edges.Values) UpsertEdge(edge);
				DisplayName = incoming.DisplayName;
				Revision++;
				TopologyRevision++;
			}
			finally
			{
				suppressTimeline = false;
			}
		}

		private void RecordDifference(GraphSnapshot before, GraphSnapshot after)
		{
			if (!Timeline.HasBaseline) return;

			// Index both snapshots to collect only records that were added, removed, or changed.
			var beforeNodes = before.Nodes.ToDictionary(item => item.Id, StringComparer.Ordinal);
			var afterNodes = after.Nodes.ToDictionary(item => item.Id, StringComparer.Ordinal);
			var beforeEdges = before.Edges.ToDictionary(item => item.Id, StringComparer.Ordinal);
			var afterEdges = after.Edges.ToDictionary(item => item.Id, StringComparer.Ordinal);
			var changedBeforeNodes = beforeNodes.Values
												.Where(item => !afterNodes.TryGetValue(item.Id, out var next) ||
															   !Equivalent(item, next))
												.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
			var changedAfterNodes = afterNodes.Values
											  .Where(item => !beforeNodes.TryGetValue(item.Id, out var prior) ||
															 !Equivalent(item, prior))
											  .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
			var changedBeforeEdges = beforeEdges.Values
												.Where(item => !afterEdges.TryGetValue(item.Id, out var next) ||
															   !Equivalent(item, next))
												.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
			var changedAfterEdges = afterEdges.Values
											  .Where(item => !beforeEdges.TryGetValue(item.Id, out var prior) ||
															 !Equivalent(item, prior))
											  .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
			var changed =
				changedBeforeNodes.Length + changedAfterNodes.Length + changedBeforeEdges.Length +
				changedAfterEdges.Length > 0 || before.DisplayName != after.DisplayName;
			if (!changed) return;

			// Publish a single ordered delta for the completed compound operation.
			Timeline.Append(new GraphTimelineChange(changedBeforeNodes, changedAfterNodes, changedBeforeEdges,
													changedAfterEdges, before.DisplayName, after.DisplayName));
			Changed?.Invoke();
		}

		private static bool Equivalent(GraphNodeRecord left, GraphNodeRecord right)
		{
			return left.TypeId == right.TypeId && left.Label == right.Label &&
				   left.Sources.SequenceEqual(right.Sources, StringComparer.Ordinal) &&
				   left.Properties.Count == right.Properties.Count &&
				   left.Properties.All(pair => right.Properties.TryGetValue(pair.Key, out var value) &&
											   pair.Value.Equals(value));
		}

		private static bool Equivalent(GraphEdgeRecord left, GraphEdgeRecord right)
		{
			return left.From == right.From && left.To == right.To && left.TypeId == right.TypeId &&
				   left.Status == right.Status && left.Confidence == right.Confidence &&
				   left.Sources.SequenceEqual(right.Sources, StringComparer.Ordinal) &&
				   left.Properties.Count == right.Properties.Count &&
				   left.Properties.All(pair => right.Properties.TryGetValue(pair.Key, out var value) &&
											   pair.Value.Equals(value));
		}

		private sealed class Adjacency
		{
			public readonly Dictionary<string, int> Counts = new(StringComparer.Ordinal);
			public readonly GraphIdSet Edges = new(), Neighbors = new();
		}
	}
}
