using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEngine;

namespace Cashg.GraphExplorer
{
	/// <summary>A reversible presentation of a source scene. The source document is never changed.</summary>
	public sealed class GraphClusterScene : IGraphScene
	{
		/// <summary>Type identifier assigned to generated cluster records.</summary>
		public const string ClusterType = "graph-explorer:cluster";
		private readonly Dictionary<string, Cluster> clusters = new(StringComparer.Ordinal);
		private readonly Dictionary<string, IGraphEdge> edges = new(StringComparer.Ordinal);
		private readonly HashSet<string> lodSuppressed = new(StringComparer.Ordinal);
		private readonly Dictionary<string, IGraphNode> nodes = new(StringComparer.Ordinal);
		private readonly Dictionary<string, string> parents = new(StringComparer.Ordinal);

		private readonly IGraphScene source;
		private int sourceRevision = -1;

		/// <summary>Creates a cluster view over an existing scene.</summary>
		/// <param name="source">Scene whose nodes and edges are projected.</param>
		public GraphClusterScene(IGraphScene source)
		{
			this.source = source ?? throw new ArgumentNullException(nameof(source));
			Nodes = new ReadOnlyDictionary<string, IGraphNode>(nodes);
			Edges = new ReadOnlyDictionary<string, IGraphEdge>(edges);
			Synchronize();
		}

		/// <summary>Gets or sets whether distance-based level of detail is enabled.</summary>
		public bool LodEnabled { get; set; }
		/// <summary>Gets or sets the distance at which clusters collapse automatically.</summary>
		public float LodEnterDistance { get; set; } = 80;
		/// <summary>Gets or sets the distance below which clusters expand automatically.</summary>
		public float LodExitDistance { get; set; } = 65;
		/// <summary>Gets or sets the minimum spacing between automatically formed neighbors.</summary>
		public float LodNeighborSeparation { get; set; } = 12;
		/// <summary>Gets or sets the minimum number of members for an automatic cluster.</summary>
		public int LodMinimumMembers { get; set; } = 2;
		/// <summary>Gets the source document.</summary>
		public GraphDocument Document => source.Document;
		/// <summary>Gets the source view state.</summary>
		public GraphViewState View => source.View;
		/// <summary>Gets the source schema.</summary>
		public GraphSchema Schema => source.Schema;
		/// <summary>Gets the currently visible nodes, including collapsed clusters.</summary>
		public IReadOnlyDictionary<string, IGraphNode> Nodes { get; }

		/// <summary>Gets the currently visible edges.</summary>
		public IReadOnlyDictionary<string, IGraphEdge> Edges { get; }

		/// <summary>Gets the revision of the projected scene.</summary>
		public int Revision { get; private set; }

		/// <summary>Determines whether an identifier belongs to a cluster.</summary>
		/// <param name="id">Identifier to check.</param>
		/// <returns><see langword="true"/> when the identifier is a cluster.</returns>
		public bool IsCluster(string id)
		{
			return id != null && clusters.ContainsKey(id);
		}

		/// <summary>Determines whether a cluster is currently collapsed.</summary>
		/// <param name="id">Cluster identifier.</param>
		/// <returns><see langword="true"/> when the cluster is collapsed.</returns>
		public bool IsCollapsed(string id)
		{
			return id != null && clusters.TryGetValue(id, out var cluster) && cluster.Collapsed;
		}

		/// <summary>Gets the immediate parent cluster identifier for a member.</summary>
		/// <param name="id">Node or cluster identifier.</param>
		/// <returns>The parent cluster identifier, or null when the member is not nested.</returns>
		public string ParentId(string id)
		{
			return id != null && parents.TryGetValue(id, out var parent) ? parent : null;
		}

		/// <summary>Expand collapsed ancestors so a canonical source node can be selected.</summary>
		/// <summary>Expands collapsed ancestors so a source node can be selected.</summary>
		/// <param name="sourceNodeId">Identifier of the source node to reveal.</param>
		/// <returns><see langword="true"/> when an ancestor was expanded.</returns>
		public bool Reveal(string sourceNodeId)
		{
			if (!source.Nodes.ContainsKey(sourceNodeId)) return false;

			// Collect enclosing groups so the outermost collapsed ancestor is expanded first.
			var ancestors = new List<string>();
			var id = sourceNodeId;
			while (parents.TryGetValue(id, out var parent))
			{
				ancestors.Add(parent);
				id = parent;
			}

			var changed = false;
			for (var i = ancestors.Count - 1; i >= 0; i--) changed |= Expand(ancestors[i]);

			// Keep automatic LOD from immediately hiding the explicitly revealed node again.
			lodSuppressed.Add(sourceNodeId);
			return changed;
		}

		/// <summary>Gets the immediate members of a cluster.</summary>
		/// <param name="id">Cluster identifier.</param>
		/// <returns>Child node and cluster identifiers.</returns>
		public IReadOnlyList<string> Children(string id)
		{
			return clusters.TryGetValue(id, out var cluster)
				? Array.AsReadOnly(cluster.Children)
				: Array.Empty<string>();
		}

		/// <summary>Gets the source-node identifiers contained in a node or cluster.</summary>
		/// <param name="id">Node or cluster identifier.</param>
		/// <returns>The contained source node identifiers.</returns>
		public IReadOnlyCollection<string> LeafIds(string id)
		{
			var result = new HashSet<string>(StringComparer.Ordinal);

			void Visit(string member)
			{
				// Descend through temporary groups and collect only canonical source nodes.
				if (clusters.TryGetValue(member, out var nested))
					foreach (var child in nested.Children)
						Visit(child);
				else if (source.Nodes.ContainsKey(member)) result.Add(member);
			}

			Visit(id);
			return result;
		}

		/// <summary>Capture canonical node positions, including movement of collapsed display clusters.</summary>
		/// <summary>Gets canonical source-node positions, including offsets from moved collapsed clusters.</summary>
		/// <returns>Source node positions keyed by node identifier.</returns>
		public IReadOnlyDictionary<string, Vector3> CanonicalPositions()
		{
			var result = new SortedDictionary<string, Vector3>(StringComparer.Ordinal);
			foreach (var sourceNode in source.Nodes.Values)
			{
				// Accumulate dragged offsets from every collapsed ancestor without modifying source facts.
				var position = sourceNode.Position;
				var child = sourceNode.Id;
				while (parents.TryGetValue(child, out var parent))
				{
					var cluster = clusters[parent];
					if (cluster.Collapsed) position += cluster.View.Position - cluster.LastPosition;
					child = parent;
				}

				result.Add(sourceNode.Id, position);
			}

			return new ReadOnlyDictionary<string, Vector3>(result);
		}

		/// <summary>Restore canonical positions and re-center temporary clusters around their restored members.</summary>
		/// <summary>Applies canonical source-node positions and re-centers clusters.</summary>
		/// <param name="positions">Positions keyed by source node identifier.</param>
		public void ApplyCanonicalPositions(IReadOnlyDictionary<string, Vector3> positions)
		{
			if (positions == null) throw new ArgumentNullException(nameof(positions));

			// Restore source-node motion first, resetting any missing nodes to their stable seed.
			foreach (var node in source.Nodes.Values)
			{
				node.Position = positions.TryGetValue(node.Id, out var position)
					? position
					: GraphViewState.InitialPosition(node.Id);
				node.Velocity = Vector3.zero;
			}

			// Recenter inner groups before their parents so nested centroids use restored positions.
			foreach (var cluster in clusters.Values.OrderByDescending(Depth))
			{
				var members = cluster.Children.Select(child => clusters.TryGetValue(child, out var nested)
														  ? nested.View.Position
														  : source.Nodes.TryGetValue(child, out var node)
															  ? node.Position
															  : (Vector3?)null)
									 .Where(position => position.HasValue).Select(position => position.Value).ToArray();
				if (members.Length == 0) continue;
				cluster.View.Position =
					members.Aggregate(Vector3.zero, (sum, position) => sum + position) / members.Length;
				cluster.View.Velocity = Vector3.zero;
				cluster.LastPosition = cluster.View.Position;
			}
		}

		private int Depth(Cluster cluster)
		{
			var depth = 0;
			var id = cluster.Id;
			while (parents.TryGetValue(id, out var parent))
			{
				depth++;
				id = parent;
			}

			return depth;
		}

		/// <summary>Gets the source records contained in a node or cluster.</summary>
		/// <param name="id">Node or cluster identifier.</param>
		/// <returns>The contained node records.</returns>
		public IReadOnlyList<GraphNodeRecord> MemberRecords(string id)
		{
			return LeafIds(id).Select(member => source.Document.Nodes[member]).ToArray();
		}

		/// <summary>Gets source edges whose endpoints are both contained in a node or cluster.</summary>
		/// <param name="id">Node or cluster identifier.</param>
		/// <returns>The internal edge records.</returns>
		public IReadOnlyList<GraphEdgeRecord> InternalEdges(string id)
		{
			var members = new HashSet<string>(LeafIds(id), StringComparer.Ordinal);
			return source.Document.Edges.Values.Where(edge => members.Contains(edge.From) && members.Contains(edge.To))
						 .ToArray();
		}

		/// <summary>Collapses visible sibling nodes or clusters into a new cluster.</summary>
		/// <param name="memberIds">At least two visible members with the same immediate parent.</param>
		/// <param name="label">Optional display label for the new cluster.</param>
		/// <returns>The identifier of the new cluster.</returns>
		public string Collapse(IEnumerable<string> memberIds, string label = null)
		{
			Synchronize();

			// Only visible siblings can form a group; reject mixed parents before changing membership.
			var members = (memberIds ?? throw new ArgumentNullException(nameof(memberIds)))
						  .Distinct(StringComparer.Ordinal).ToArray();
			if (members.Length < 2 || members.Any(id => !nodes.ContainsKey(id)))
				throw new ArgumentException("Choose at least two currently visible nodes or clusters.",
											nameof(memberIds));
			var enclosing = parents.TryGetValue(members[0], out var owner) ? owner : null;
			if (members.Any(id => (parents.TryGetValue(id, out var parent) ? parent : null) != enclosing))
				throw new ArgumentException("Members must share the same immediate parent.", nameof(memberIds));
			var id = "graph-explorer:cluster:" + Guid.NewGuid().ToString("N");

			// Place the new group at its members' centroid and splice it into the parent hierarchy.
			var cluster = new Cluster
			{
				Id = id, Label = string.IsNullOrWhiteSpace(label) ? members.Length + " nodes" : label,
				Children = members,
				LastPosition =
					members.Select(member => nodes[member].Position).Aggregate(Vector3.zero, (a, b) => a + b) /
					members.Length
			};
			cluster.View.Position = cluster.LastPosition;
			clusters.Add(id, cluster);
			if (enclosing != null)
			{
				var outer = clusters[enclosing];
				var selected = new HashSet<string>(members, StringComparer.Ordinal);
				outer.Children = outer.Children.Where(child => !selected.Contains(child)).Concat(new[] { id })
									  .ToArray();
				parents[id] = enclosing;
			}

			foreach (var member in members) parents[member] = id;
			Rebuild();
			return id;
		}

		/// <summary>Expands a collapsed cluster and restores its members.</summary>
		/// <param name="id">Cluster identifier.</param>
		/// <returns><see langword="true"/> when the cluster was expanded.</returns>
		public bool Expand(string id)
		{
			return Expand(id, true);
		}

		private bool Expand(string id, bool suppressLod)
		{
			if (!clusters.TryGetValue(id, out var cluster) || !cluster.Collapsed) return false;

			// An explicit expansion exempts automatic group members from further distance collapsing.
			if (suppressLod && cluster.Automatic)
				foreach (var member in LeafIds(id))
					lodSuppressed.Add(member);
			// Move members with a dragged cluster while retaining their relative layout.
			var delta = cluster.View.Position - cluster.LastPosition;
			foreach (var child in cluster.Children)
				if (clusters.TryGetValue(child, out var nested)) nested.View.Position += delta;
				else if (source.Nodes.TryGetValue(child, out var node)) node.Position += delta;
			cluster.Collapsed = false;
			Rebuild();
			return true;
		}

		/// <summary>Collapses an expanded cluster around its current members.</summary>
		/// <param name="id">Cluster identifier.</param>
		/// <returns><see langword="true"/> when the cluster was collapsed.</returns>
		public bool Recollapse(string id)
		{
			if (!clusters.TryGetValue(id, out var cluster) || cluster.Collapsed) return false;

			// Reuse the existing membership but recenter it on its currently visible children.
			cluster.View.Position = cluster.Children.Where(child => nodes.ContainsKey(child))
										   .Select(child => nodes[child].Position)
										   .DefaultIfEmpty(cluster.View.Position)
										   .Aggregate(Vector3.zero, (a, b) => a + b) /
									Math.Max(1, cluster.Children.Count(child => nodes.ContainsKey(child)));
			cluster.LastPosition = cluster.View.Position;
			cluster.Collapsed = true;
			Rebuild();
			return true;
		}

		/// <summary>Removes a cluster while preserving its member nodes.</summary>
		/// <param name="id">Cluster identifier.</param>
		/// <returns><see langword="true"/> when the cluster was removed.</returns>
		public bool RemoveCluster(string id)
		{
			if (!clusters.TryGetValue(id, out var cluster)) return false;
			if (cluster.Collapsed) Expand(id, false);

			// Splice children into the removed group's parent rather than losing their nesting.
			if (parents.TryGetValue(id, out var outerId))
			{
				var outer = clusters[outerId];
				outer.Children = outer.Children.Where(child => child != id).Concat(cluster.Children).ToArray();
				foreach (var child in cluster.Children) parents[child] = outerId;
			}
			else
			{
				foreach (var child in cluster.Children) parents.Remove(child);
			}

			parents.Remove(id);
			clusters.Remove(id);
			Rebuild();
			return true;
		}

		/// <summary>Synchronizes the projection with source-scene revisions.</summary>
		public void Synchronize()
		{
			if (sourceRevision == source.Revision) return;
			sourceRevision = source.Revision;
			lodSuppressed.RemoveWhere(id => !source.Nodes.ContainsKey(id));
			// Remove vanished facts from temporary groups. A group reduced to one
			// member dissolves, including when it is nested in another group.
			bool pruned;
			do
			{
				pruned = false;
				foreach (var cluster in clusters.Values.ToArray())
				{
					cluster.Children = cluster.Children
											  .Where(child => source.Nodes.ContainsKey(child) ||
															  clusters.ContainsKey(child)).ToArray();
					if (cluster.Children.Length >= 2) continue;
					var outerId = parents.TryGetValue(cluster.Id, out var parent) ? parent : null;
					if (outerId != null && clusters.TryGetValue(outerId, out var outer))
						outer.Children = outer.Children.Where(child => child != cluster.Id).Concat(cluster.Children)
											  .ToArray();
					foreach (var child in cluster.Children)
						if (outerId != null) parents[child] = outerId;
						else parents.Remove(child);
					parents.Remove(cluster.Id);
					clusters.Remove(cluster.Id);
					pruned = true;
				}
			} while (pruned);

			Rebuild();
		}

		private void Rebuild()
		{
			nodes.Clear();
			edges.Clear();

			string Representative(string id)
			{
				// The outermost collapsed ancestor is the sole visible representative of a source node.
				var representative = id;
				while (parents.TryGetValue(id, out var parent))
				{
					if (clusters[parent].Collapsed) representative = parent;
					id = parent;
				}

				return representative;
			}

			// Project each visible representative once, sharing source view state for uncollapsed nodes.
			foreach (var item in source.Nodes)
			{
				var id = Representative(item.Key);
				if (!nodes.ContainsKey(id))
					nodes.Add(id, new Node(this, id == item.Key ? item.Value : null,
										   id == item.Key ? null : clusters[id]));
			}

			// Retarget crossing edges while omitting connections internal to a collapsed group.
			foreach (var item in source.Edges)
			{
				string from = Representative(item.Value.From), to = Representative(item.Value.To);
				if (from == to && (from != item.Value.From || to != item.Value.To)) continue;
				var edge = new Edge(item.Value, from, to);
				edges.Add(edge.Id, edge);
				((Node)nodes[from]).Link(to, edge);
				((Node)nodes[to]).Link(from, edge);
			}

			Revision++;
		}

		/// <summary>
		///     Update automatic connected clusters from camera distance. Call at a modest interval, then rebind layout on
		///     change.
		/// </summary>
		public void ClearLodOverrides()
		{
			lodSuppressed.Clear();
		}

		/// <summary>Updates automatic cluster collapse and expansion for a camera position.</summary>
		/// <param name="cameraPosition">Current camera position in world space.</param>
		/// <returns><see langword="true"/> when the visible cluster projection changed.</returns>
		public bool UpdateLod(Vector3 cameraPosition)
		{
			Synchronize();
			var changed = false;

			// Expand automatic groups near the camera using a smaller exit threshold to prevent flicker.
			var exitDistance = Mathf.Max(0, Mathf.Min(LodExitDistance, LodEnterDistance - .01f));
			foreach (var cluster in clusters.Values.Where(c => c.Automatic).ToArray())
				if (Vector3.Distance(cameraPosition, cluster.View.Position) < exitDistance)
				{
					RemoveCluster(cluster.Id);
					changed = true;
				}

			if (!LodEnabled)
			{
				// Disabling LOD dissolves only automatic groups, preserving manually created ones.
				foreach (var cluster in clusters.Values.Where(c => c.Automatic).ToArray())
				{
					RemoveCluster(cluster.Id);
					changed = true;
				}

				return changed;
			}

			// Exclude manually revealed members and nearby nodes from new automatic groups.
			var candidates = nodes.Values
								  .Where(node => !(IsCluster(node.Id)
													 ? LeafIds(node.Id).Any(lodSuppressed.Contains)
													 : lodSuppressed.Contains(node.Id)) &&
												 Vector3.Distance(cameraPosition, node.Position) >= LodEnterDistance)
								  .ToDictionary(node => node.Id, node => node, StringComparer.Ordinal);
			var visited = new HashSet<string>(StringComparer.Ordinal);
			foreach (var candidate in candidates.Values.ToArray())
			{
				// Flood connected, closely spaced siblings so unrelated distant nodes remain separate.
				if (!visited.Add(candidate.Id)) continue;
				var enclosing = parents.TryGetValue(candidate.Id, out var owner) ? owner : null;
				var group = new List<string>();
				var queue = new Queue<string>();
				queue.Enqueue(candidate.Id);
				while (queue.Count > 0)
				{
					var id = queue.Dequeue();
					group.Add(id);
					foreach (var neighbor in candidates[id].Neighbors)
						if (candidates.TryGetValue(neighbor, out var other) &&
							(parents.TryGetValue(neighbor, out var parent) ? parent : null) == enclosing &&
							Vector3.Distance(candidates[id].Position, other.Position) <= LodNeighborSeparation &&
							visited.Add(neighbor))
							queue.Enqueue(neighbor);
				}

				if (group.Count < Math.Max(2, LodMinimumMembers)) continue;
				var clusterId = Collapse(group);
				clusters[clusterId].Automatic = true;
				changed = true;
			}

			return changed;
		}

		private sealed class Cluster
		{
			public readonly GraphNodeViewState View = new();
			public bool Automatic, Collapsed = true;
			public string[] Children;
			public string Id, Label;
			public Vector3 LastPosition;
		}

		private sealed class Node : IGraphNode
		{
			private readonly Cluster cluster;
			private readonly List<IGraphEdge> edges = new();
			private readonly GraphClusterScene owner;
			private readonly IGraphNode source;

			public Node(GraphClusterScene owner, IGraphNode source, Cluster cluster)
			{
				this.owner = owner;
				this.source = source;
				this.cluster = cluster;
			}

			public string Id => source?.Id ?? cluster.Id;

			public GraphNodeRecord Record => source?.Record ?? new GraphNodeRecord(Id, ClusterType, cluster.Label,
				new Dictionary<string, GraphValue> { ["memberCount"] = GraphValue.From(owner.LeafIds(Id).Count) });

			public string Label => source?.Label ?? cluster.Label;

			public Vector3 Position
			{
				get => source?.Position ?? cluster.View.Position;
				set
				{
					if (source != null) source.Position = value;
					else cluster.View.Position = value;
				}
			}

			public Vector3 Velocity
			{
				get => source?.Velocity ?? cluster.View.Velocity;
				set
				{
					if (source != null) source.Velocity = value;
					else cluster.View.Velocity = value;
				}
			}

			public bool Pinned
			{
				get => source?.Pinned ?? cluster.View.Pinned;
				set
				{
					if (source != null) source.Pinned = value;
					else cluster.View.Pinned = value;
				}
			}

			public bool Visible
			{
				get => source?.Visible ?? owner.LeafIds(Id).Any(member => owner.source.Nodes[member].Visible);
				set
				{
					if (source != null) source.Visible = value;
				}
			}

			public bool Matches
			{
				get => source?.Matches ?? owner.LeafIds(Id).Any(member => owner.source.Nodes[member].Matches);
				set
				{
					if (source != null) source.Matches = value;
				}
			}

			public bool Favorite
			{
				get => source?.Favorite ?? owner.LeafIds(Id).Any(member => owner.source.Nodes[member].Favorite);
				set
				{
					if (source != null) source.Favorite = value;
				}
			}

			public ToolEmphasis Emphasis
			{
				get => source?.Emphasis ?? ToolEmphasis.None;
				set
				{
					if (source != null) source.Emphasis = value;
				}
			}

			public GraphIdSet Neighbors { get; } = new();

			public IReadOnlyList<IGraphEdge> Edges => edges;

			public void Link(string other, IGraphEdge edge)
			{
				edges.Add(edge);
				if (other != Id) Neighbors.Values.Add(other);
			}
		}

		private sealed class Edge : IGraphEdge
		{
			private readonly IGraphEdge source;

			public Edge(IGraphEdge source, string from, string to)
			{
				this.source = source;
				var fact = source.Record;
				Record = new GraphEdgeRecord(fact.Id, from, to, fact.TypeId, fact.Status, fact.Confidence,
											 fact.Properties, fact.Sources);
			}

			public string Id => source.Id;
			public GraphEdgeRecord Record { get; }

			public string From => Record.From;
			public string To => Record.To;
			public string Relation => Record.TypeId;
			public string Status => Record.Status;
		}
	}
}
