using System;
using System.Collections.Generic;
using System.Linq;

namespace Cashg.GraphExplorer
{
	/// <summary>Lists graph path endpoint.</summary>
	public enum GraphPathEndpoint
	{
		/// <summary>The starting node of a path.</summary>
		Start,
		/// <summary>The destination node of a path.</summary>
		End
	}

	/// <summary>Unweighted, directed shortest paths over the currently projected scene.</summary>
	public static class GraphPathfinder
	{
		/// <summary>Finds a shortest path between two nodes using the scene's edges.</summary>
		/// <param name="scene">Scene to search.</param>
		/// <param name="startId">Starting node identifier.</param>
		/// <param name="endId">Destination node identifier.</param>
		/// <param name="edgeAllowed">Optional predicate that excludes edges from traversal.</param>
		/// <returns>The path result, which is unreachable when no path exists.</returns>
		public static GraphPathResult Find(IGraphScene scene, string startId, string endId,
										   Func<IGraphEdge, bool> edgeAllowed = null)
		{
			if (scene == null) throw new ArgumentNullException(nameof(scene));
			if (string.IsNullOrEmpty(startId) || string.IsNullOrEmpty(endId) ||
				!scene.Nodes.ContainsKey(startId) || !scene.Nodes.ContainsKey(endId))
				return GraphPathResult.Unreachable(startId, endId);
			if (startId == endId) return new GraphPathResult(startId, endId, new[] { startId }, Array.Empty<string>());

			// Build deterministic outgoing adjacency from the permitted directed edges.
			var outgoing = new Dictionary<string, List<IGraphEdge>>(StringComparer.Ordinal);
			foreach (var edge in scene.Edges.Values)
			{
				if (edgeAllowed != null && !edgeAllowed(edge)) continue;
				if (!outgoing.TryGetValue(edge.From, out var list))
					outgoing.Add(edge.From, list = new List<IGraphEdge>());
				list.Add(edge);
			}

			foreach (var list in outgoing.Values)
				list.Sort((a, b) =>
				{
					var c = string.CompareOrdinal(a.To, b.To);
					return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
				});

			var previousNode = new Dictionary<string, string>(StringComparer.Ordinal);
			var previousEdge = new Dictionary<string, string>(StringComparer.Ordinal);
			var visited = new HashSet<string>(StringComparer.Ordinal) { startId };
			var queue = new Queue<string>();
			queue.Enqueue(startId);

			// Breadth-first traversal discovers the shortest path and records predecessors.
			while (queue.Count > 0)
			{
				var current = queue.Dequeue();
				if (!outgoing.TryGetValue(current, out var edges)) continue;
				foreach (var edge in edges)
				{
					if (!visited.Add(edge.To)) continue;
					previousNode[edge.To] = current;
					previousEdge[edge.To] = edge.Id;
					if (edge.To == endId)
					{
						// Walk predecessors backward, then restore source-to-destination order.
						var nodes = new List<string> { endId };
						var edgeIds = new List<string>();
						var cursor = endId;
						while (cursor != startId)
						{
							edgeIds.Add(previousEdge[cursor]);
							cursor = previousNode[cursor];
							nodes.Add(cursor);
						}

						nodes.Reverse();
						edgeIds.Reverse();
						return new GraphPathResult(startId, endId, nodes, edgeIds);
					}

					queue.Enqueue(edge.To);
				}
			}

			return GraphPathResult.Unreachable(startId, endId);
		}
	}

	/// <summary>Represents graph path result.</summary>
	public sealed class GraphPathResult
	{
		/// <summary>Creates a result for a path search.</summary>
		/// <param name="startId">Starting node identifier.</param>
		/// <param name="endId">Destination node identifier.</param>
		/// <param name="nodeIds">Node identifiers in path order.</param>
		/// <param name="edgeIds">Edge identifiers in path order.</param>
		public GraphPathResult(string startId, string endId, IEnumerable<string> nodeIds, IEnumerable<string> edgeIds)
		{
			StartId = startId;
			EndId = endId;
			NodeIds = Array.AsReadOnly(nodeIds.ToArray());
			EdgeIds = Array.AsReadOnly(edgeIds.ToArray());
		}

		/// <summary>Gets the starting node identifier.</summary>
		public string StartId { get; }
		/// <summary>Gets the destination node identifier.</summary>
		public string EndId { get; }
		/// <summary>Gets node identifiers in path order.</summary>
		public IReadOnlyList<string> NodeIds { get; }
		/// <summary>Gets edge identifiers in path order.</summary>
		public IReadOnlyList<string> EdgeIds { get; }
		/// <summary>Gets whether a path was found.</summary>
		public bool IsReachable => NodeIds.Count > 0;
		/// <summary>Gets the number of edges in the path.</summary>
		public int Jumps => EdgeIds.Count;

		internal static GraphPathResult Unreachable(string startId, string endId)
		{
			return new GraphPathResult(startId, endId, Array.Empty<string>(), Array.Empty<string>());
		}
	}

	/// <summary>Persistent path selection state. Call Synchronize after scene topology changes.</summary>
	public sealed class GraphPathState
	{
		private readonly Func<IGraphEdge, bool> edgeAllowed;
		private int revision = int.MinValue;
		private IGraphScene scene;

		/// <summary>Creates path-selection state for a graph scene.</summary>
		/// <param name="scene">Scene used to resolve nodes and edges.</param>
		/// <param name="edgeAllowed">Optional predicate that excludes edges from path searches.</param>
		public GraphPathState(IGraphScene scene, Func<IGraphEdge, bool> edgeAllowed = null)
		{
			this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
			this.edgeAllowed = edgeAllowed;
		}

		/// <summary>Gets whether path highlighting is active.</summary>
		public bool IsActive { get; private set; }
		/// <summary>Gets the current path result.</summary>
		public GraphPathResult Result { get; private set; }
		/// <summary>Gets the selected starting node identifier.</summary>
		public string StartId { get; private set; }
		/// <summary>Gets the selected destination node identifier.</summary>
		public string EndId { get; private set; }
		/// <summary>Gets the endpoint currently awaiting a node pick.</summary>
		public GraphPathEndpoint? PendingPick { get; private set; }
		/// <summary>Occurs when path-selection state changes.</summary>
		public event Action<GraphPathState> Changed;
		/// <summary>Occurs when the user should pick a node for an endpoint.</summary>
		public event Action<GraphPathEndpoint> PickRequested;

		/// <summary>Binds the state to a scene and synchronizes its endpoints.</summary>
		/// <param name="value">Scene to use for path selection.</param>
		public void Bind(IGraphScene value)
		{
			scene = value ?? throw new ArgumentNullException(nameof(value));
			Synchronize(true);
		}

		/// <summary>Sets both path endpoints and computes the path.</summary>
		/// <param name="startId">Starting node identifier.</param>
		/// <param name="endId">Destination node identifier.</param>
		/// <returns>The computed path result.</returns>
		public GraphPathResult SetEndpoints(string startId, string endId)
		{
			// Finish any pending pick and compute against the current scene revision before notifying listeners.
			PendingPick = null;
			StartId = startId;
			EndId = endId;
			IsActive = true;
			revision = scene.Revision;
			Result = GraphPathfinder.Find(scene, startId, endId, edgeAllowed);
			Changed?.Invoke(this);
			return Result;
		}

		/// <summary>Sets both endpoints and activates path highlighting.</summary>
		/// <param name="startId">Starting node identifier.</param>
		/// <param name="endId">Destination node identifier.</param>
		/// <returns>The computed path result.</returns>
		public GraphPathResult Show(string startId, string endId)
		{
			return SetEndpoints(startId, endId);
		}

		/// <summary>Begins picking one endpoint from the scene.</summary>
		/// <param name="endpoint">Endpoint to select.</param>
		public void BeginPick(GraphPathEndpoint endpoint)
		{
			PendingPick = endpoint;
			PickRequested?.Invoke(endpoint);
			Changed?.Invoke(this);
		}

		/// <summary>Uses a picked node as the pending endpoint.</summary>
		/// <param name="nodeId">Picked node identifier.</param>
		/// <returns><see langword="true"/> when a pending endpoint accepted the node.</returns>
		public bool AcceptPickedNode(string nodeId)
		{
			// Reject unrelated clicks until a valid scene node can satisfy the pending endpoint pick.
			if (!PendingPick.HasValue || string.IsNullOrEmpty(nodeId) || !scene.Nodes.ContainsKey(nodeId)) return false;
			var endpoint = PendingPick.Value;
			PendingPick = null;
			SetEndpoint(endpoint, nodeId);
			return true;
		}

		/// <summary>Cancels the current endpoint pick.</summary>
		public void CancelPick()
		{
			if (!PendingPick.HasValue) return;
			PendingPick = null;
			Changed?.Invoke(this);
		}

		/// <summary>Sets one path endpoint to a node.</summary>
		/// <param name="endpoint">Endpoint to set.</param>
		/// <param name="nodeId">Node identifier to assign.</param>
		public void SetEndpoint(GraphPathEndpoint endpoint, string nodeId)
		{
			if (string.IsNullOrEmpty(nodeId) || !scene.Nodes.ContainsKey(nodeId))
				throw new ArgumentException("Endpoint must identify a node in the bound scene.", nameof(nodeId));
			PendingPick = null;
			if (endpoint == GraphPathEndpoint.Start) StartId = nodeId;
			else EndId = nodeId;
			if (!string.IsNullOrEmpty(StartId) && !string.IsNullOrEmpty(EndId))
			{
				// Activate a route only after both endpoint identities have been chosen.
				IsActive = true;
				revision = scene.Revision;
				Result = GraphPathfinder.Find(scene, StartId, EndId, edgeAllowed);
			}

			Changed?.Invoke(this);
		}

		/// <summary>Clears both endpoints and the current path.</summary>
		public void Clear()
		{
			IsActive = false;
			Result = null;
			StartId = EndId = null;
			PendingPick = null;
			revision = scene.Revision;
			Changed?.Invoke(this);
		}

		/// <summary>Recomputes the path when the bound scene has changed.</summary>
		/// <param name="force">When true, recomputes even if the scene revision is unchanged.</param>
		/// <returns><see langword="true"/> when the path result changed.</returns>
		public bool Synchronize(bool force = false)
		{
			// Keep unreachable selections active and recompute only after topology changes or an explicit refresh.
			if (!IsActive || (!force && revision == scene.Revision)) return false;
			revision = scene.Revision;
			Result = GraphPathfinder.Find(scene, StartId, EndId, edgeAllowed);
			Changed?.Invoke(this);
			return true;
		}
	}
}
