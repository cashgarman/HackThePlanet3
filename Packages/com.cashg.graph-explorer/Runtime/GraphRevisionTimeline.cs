using System;
using System.Collections.Generic;
using System.Linq;

namespace Cashg.GraphExplorer
{
	/// <summary>A discrete revision from a source control DAG. Timestamps are descriptive, not seek coordinates.</summary>
	public sealed class GraphRevision
	{
		/// <summary>Creates a revision node in a revision history.</summary>
		/// <param name="id">Stable revision identifier.</param>
		/// <param name="parentIds">Identifiers of the revisions this revision follows.</param>
		/// <param name="timestampUtc">Optional revision timestamp.</param>
		/// <param name="label">Display label for the revision.</param>
		public GraphRevision(string id, IEnumerable<string> parentIds, DateTimeOffset? timestampUtc, string label)
		{
			if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Revision ID is required.", nameof(id));
			Id = id;
			ParentIds = Array.AsReadOnly((parentIds ?? Array.Empty<string>()).ToArray());
			TimestampUtc = timestampUtc?.ToUniversalTime();
			Label = label ?? "";
			LaneGlyphs = "●";
		}

		/// <summary>Gets the revision identifier.</summary>
		public string Id { get; }
		/// <summary>Gets the parent revision identifiers.</summary>
		public IReadOnlyList<string> ParentIds { get; }
		/// <summary>Gets the optional revision timestamp.</summary>
		public DateTimeOffset? TimestampUtc { get; }
		/// <summary>Gets the display label.</summary>
		public string Label { get; }
		/// <summary>Gets the assigned lane in the revision visualization.</summary>
		public int Lane { get; internal set; }
		/// <summary>Gets lane glyphs used to draw revision links.</summary>
		public string LaneGlyphs { get; internal set; }
		/// <summary>Gets the number of lanes occupied by this revision's links.</summary>
		public int LaneCount { get; internal set; }

		/// <summary>Gets the links between this revision and its parent lanes.</summary>
		public IReadOnlyList<GraphRevisionLaneLink> LaneLinks { get; internal set; } =
			Array.Empty<GraphRevisionLaneLink>();

		/// <summary>Gets whether revision changes have been computed.</summary>
		public bool Ready { get; internal set; }
		/// <summary>Gets the summary of changes from the parent revision.</summary>
		public GraphRevisionChangeSummary Changes { get; internal set; }
	}

	/// <summary>Represents graph revision lane link.</summary>
	public readonly struct GraphRevisionLaneLink
	{
		/// <summary>Gets the source lane index.</summary>
		public int FromLane { get; }
		/// <summary>Gets the destination lane index.</summary>
		public int ToLane { get; }

		/// <summary>Creates a link between two revision lanes.</summary>
		/// <param name="fromLane">Source lane index.</param>
		/// <param name="toLane">Destination lane index.</param>
		public GraphRevisionLaneLink(int fromLane, int toLane)
		{
			FromLane = fromLane;
			ToLane = toLane;
		}
	}

	/// <summary>Represents graph revision change summary.</summary>
	public sealed class GraphRevisionChangeSummary
	{
		/// <summary>Creates a summary of node and edge changes.</summary>
		/// <param name="nodesCreated">Number of created nodes.</param>
		/// <param name="nodesModified">Number of modified nodes.</param>
		/// <param name="nodesDeleted">Number of deleted nodes.</param>
		/// <param name="edgesCreated">Number of created edges.</param>
		/// <param name="edgesModified">Number of modified edges.</param>
		/// <param name="edgesDeleted">Number of deleted edges.</param>
		public GraphRevisionChangeSummary(int nodesCreated, int nodesModified, int nodesDeleted,
										  int edgesCreated, int edgesModified, int edgesDeleted)
		{
			NodesCreated = nodesCreated;
			NodesModified = nodesModified;
			NodesDeleted = nodesDeleted;
			EdgesCreated = edgesCreated;
			EdgesModified = edgesModified;
			EdgesDeleted = edgesDeleted;
		}

		/// <summary>Gets the number of created nodes.</summary>
		public int NodesCreated { get; }
		/// <summary>Gets the number of modified nodes.</summary>
		public int NodesModified { get; }
		/// <summary>Gets the number of deleted nodes.</summary>
		public int NodesDeleted { get; }
		/// <summary>Gets the number of created edges.</summary>
		public int EdgesCreated { get; }
		/// <summary>Gets the number of modified edges.</summary>
		public int EdgesModified { get; }
		/// <summary>Gets the number of deleted edges.</summary>
		public int EdgesDeleted { get; }
		/// <summary>Gets the identifiers of created nodes.</summary>
		public IReadOnlyList<string> CreatedNodeIds { get; private set; } = Array.Empty<string>();
		/// <summary>Gets the identifiers of modified nodes.</summary>
		public IReadOnlyList<string> ModifiedNodeIds { get; private set; } = Array.Empty<string>();
		/// <summary>Gets the identifiers of deleted nodes.</summary>
		public IReadOnlyList<string> DeletedNodeIds { get; private set; } = Array.Empty<string>();
		/// <summary>Gets the identifiers of created edges.</summary>
		public IReadOnlyList<string> CreatedEdgeIds { get; private set; } = Array.Empty<string>();
		/// <summary>Gets the identifiers of modified edges.</summary>
		public IReadOnlyList<string> ModifiedEdgeIds { get; private set; } = Array.Empty<string>();
		/// <summary>Gets the identifiers of deleted edges.</summary>
		public IReadOnlyList<string> DeletedEdgeIds { get; private set; } = Array.Empty<string>();

		/// <summary>Compares two snapshots and summarizes their node and edge changes.</summary>
		/// <param name="before">Earlier snapshot.</param>
		/// <param name="after">Later snapshot.</param>
		/// <returns>The change summary between the snapshots.</returns>
		public static GraphRevisionChangeSummary Between(GraphSnapshot before, GraphSnapshot after)
		{
			if (after == null) throw new ArgumentNullException(nameof(after));

			// Index both revisions by stable identity before classifying additions, edits, and deletions.
			var oldNodes =
				(before?.Nodes ?? Array.Empty<GraphNodeRecord>()).ToDictionary(node => node.Id, StringComparer.Ordinal);
			var newNodes = after.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
			var oldEdges =
				(before?.Edges ?? Array.Empty<GraphEdgeRecord>()).ToDictionary(edge => edge.Id, StringComparer.Ordinal);
			var newEdges = after.Edges.ToDictionary(edge => edge.Id, StringComparer.Ordinal);

			bool NodeChanged(GraphNodeRecord oldNode, GraphNodeRecord newNode)
			{
				// Source movement and line shifts are metadata; content and semantic edges report actual code changes.
				if (oldNode.Properties.TryGetValue("content_hash", out var prior) &&
					newNode.Properties.TryGetValue("content_hash", out var next)) return !prior.Equals(next);
				return oldNode.TypeId != newNode.TypeId || oldNode.Label != newNode.Label;
			}

			bool EdgeChanged(GraphEdgeRecord a, GraphEdgeRecord b)
			{
				return a.From != b.From || a.To != b.To ||
					   a.TypeId != b.TypeId || a.Status != b.Status || a.Confidence != b.Confidence ||
					   a.Properties.Count != b.Properties.Count || a.Properties.Any(pair =>
							   !b.Properties.TryGetValue(pair.Key, out var value) || !pair.Value.Equals(value));
			}

			var createdNodes = newNodes.Keys.Where(id => !oldNodes.ContainsKey(id))
									   .OrderBy(id => id, StringComparer.Ordinal).ToArray();
			var modifiedNodes = newNodes.Values
										.Where(node => oldNodes.TryGetValue(node.Id, out var old) &&
													   NodeChanged(old, node)).Select(node => node.Id)
										.OrderBy(id => id, StringComparer.Ordinal).ToArray();
			var deletedNodes = oldNodes.Keys.Where(id => !newNodes.ContainsKey(id))
									   .OrderBy(id => id, StringComparer.Ordinal).ToArray();
			var createdEdges = newEdges.Keys.Where(id => !oldEdges.ContainsKey(id))
									   .OrderBy(id => id, StringComparer.Ordinal).ToArray();
			var modifiedEdges = newEdges.Values
										.Where(edge => oldEdges.TryGetValue(edge.Id, out var old) &&
													   EdgeChanged(old, edge)).Select(edge => edge.Id)
										.OrderBy(id => id, StringComparer.Ordinal).ToArray();
			var deletedEdges = oldEdges.Keys.Where(id => !newEdges.ContainsKey(id))
									   .OrderBy(id => id, StringComparer.Ordinal).ToArray();
			return new GraphRevisionChangeSummary(createdNodes.Length, modifiedNodes.Length, deletedNodes.Length,
												  createdEdges.Length, modifiedEdges.Length, deletedEdges.Length)
			{
				CreatedNodeIds = Array.AsReadOnly(createdNodes), ModifiedNodeIds = Array.AsReadOnly(modifiedNodes),
				DeletedNodeIds = Array.AsReadOnly(deletedNodes),
				CreatedEdgeIds = Array.AsReadOnly(createdEdges), ModifiedEdgeIds = Array.AsReadOnly(modifiedEdges),
				DeletedEdgeIds = Array.AsReadOnly(deletedEdges)
			};
		}
	}

	/// <summary>Revision ordering and branch lanes; snapshot loading is owned by the host.</summary>
	public sealed class GraphRevisionTimeline
	{
		private readonly Dictionary<string, GraphRevision> byId = new(StringComparer.Ordinal);
		private readonly List<GraphRevision> revisions = new();
		/// <summary>Gets the ordered revisions in the timeline.</summary>
		public IReadOnlyList<GraphRevision> Revisions => revisions;
		/// <summary>Gets the currently selected revision.</summary>
		public GraphRevision Selected { get; private set; }
		/// <summary>Occurs when timeline content or selection changes.</summary>
		public event Action Changed;
		/// <summary>Occurs when a revision selection is requested.</summary>
		public event Action<GraphRevision> SelectionRequested;

		/// <summary>Replaces the revisions in timeline order.</summary>
		/// <param name="ordered">Revisions to install.</param>
		public void Replace(IEnumerable<GraphRevision> ordered)
		{
			var incoming = (ordered ?? Array.Empty<GraphRevision>()).ToArray();

			// Validate the complete input before discarding the currently displayed revision sequence.
			if (incoming.Any(revision => revision == null) ||
				incoming.Select(revision => revision.Id).Distinct(StringComparer.Ordinal).Count() != incoming.Length)
				throw new ArgumentException("Duplicate or null revision.", nameof(ordered));
			revisions.Clear();
			byId.Clear();
			Selected = null;
			foreach (var revision in incoming)
			{
				revisions.Add(revision);
				byId.Add(revision.Id, revision);
			}

			BuildLanes();
			Changed?.Invoke();
		}

		/// <summary>Attempts to find a revision by identifier.</summary>
		/// <param name="id">Revision identifier.</param>
		/// <param name="revision">Receives the revision when found.</param>
		/// <returns><see langword="true"/> when the identifier exists.</returns>
		public bool TryGet(string id, out GraphRevision revision)
		{
			return byId.TryGetValue(id ?? "", out revision);
		}

		/// <summary>Marks a revision ready and assigns its change summary.</summary>
		/// <param name="id">Revision identifier.</param>
		/// <param name="changes">Computed change summary.</param>
		public void MarkReady(string id, GraphRevisionChangeSummary changes)
		{
			if (!byId.TryGetValue(id, out var revision)) throw new ArgumentException("Unknown revision.", nameof(id));
			revision.Ready = true;
			revision.Changes = changes;
			Changed?.Invoke();
		}

		/// <summary>Requests selection of the revision with the supplied identifier.</summary>
		/// <param name="id">Revision identifier.</param>
		public void RequestSelection(string id)
		{
			if (!byId.TryGetValue(id ?? "", out var revision))
				throw new ArgumentException("Unknown revision.", nameof(id));
			SelectionRequested?.Invoke(revision);
		}

		/// <summary>Confirms the selection of a revision.</summary>
		/// <param name="id">Revision identifier.</param>
		public void ConfirmSelection(string id)
		{
			if (!byId.TryGetValue(id ?? "", out var revision))
				throw new ArgumentException("Unknown revision.", nameof(id));
			Selected = revision;
			Changed?.Invoke();
		}

		private void BuildLanes()
		{
			// Walk newest to oldest: a commit occupies its child's lane and spawns lanes for merge parents.
			var active = new List<string>();
			for (var i = revisions.Count - 1; i >= 0; i--)
			{
				var revision = revisions[i];
				var lane = active.IndexOf(revision.Id);
				if (lane < 0)
				{
					lane = active.Count;
					active.Add(revision.Id);
				}

				var before = active.ToArray();
				revision.Lane = lane;
				revision.LaneCount = active.Count;
				revision.LaneGlyphs = string.Concat(Enumerable.Range(0, active.Count)
															  .Select(index => index == lane ? "● " : "│ "));
				active.RemoveAt(lane);

				// Reserve lanes for merge parents without duplicating parents already tracked by another branch.
				for (var p = revision.ParentIds.Count - 1; p >= 0; p--)
					if (!active.Contains(revision.ParentIds[p]))
						active.Insert(Math.Min(lane, active.Count), revision.ParentIds[p]);
				var links = new List<GraphRevisionLaneLink>();

				// Connect old lane positions to their new parent or continuing-branch positions.
				for (var oldLane = 0; oldLane < before.Length; oldLane++)
					if (oldLane == lane)
						foreach (var parent in revision.ParentIds)
							links.Add(new GraphRevisionLaneLink(lane, active.IndexOf(parent)));
					else links.Add(new GraphRevisionLaneLink(oldLane, active.IndexOf(before[oldLane])));
				revision.LaneLinks = links.AsReadOnly();
			}
		}
	}
}
