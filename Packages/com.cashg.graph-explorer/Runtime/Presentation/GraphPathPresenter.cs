using System;
using System.Linq;
using UnityEngine.UIElements;

namespace Cashg.GraphExplorer
{
	/// <summary>Represents graph path rendering.</summary>
	public static class GraphPathRendering
	{
		/// <summary>Keep the renderer's persistent path in sync with path state; dispose on teardown.</summary>
		/// <param name="state">Path state to observe.</param>
		/// <param name="renderer">Renderer whose active path is updated.</param>
		/// <returns>A binding that removes the state callback when disposed.</returns>
		public static IDisposable Bind(GraphPathState state, GraphRenderer renderer)
		{
			if (state == null) throw new ArgumentNullException(nameof(state));
			if (renderer == null) throw new ArgumentNullException(nameof(renderer));

			void Apply(GraphPathState _)
			{
				renderer.ActivePath = state.Result;
			}

			state.Changed += Apply;

			// Apply an existing result immediately, then keep highlighting synchronized until the binding is disposed.
			renderer.ActivePath = state.Result;
			return new Binding(state, renderer, Apply);
		}

		private sealed class Binding : IDisposable
		{
			private readonly Action<GraphPathState> apply;
			private GraphRenderer renderer;
			private GraphPathState state;

			public Binding(GraphPathState state, GraphRenderer renderer, Action<GraphPathState> apply)
			{
				this.state = state;
				this.renderer = renderer;
				this.apply = apply;
			}

			public void Dispose()
			{
				// Unsubscribe and clear highlighting so a dismissed panel cannot leave a stale route rendered.
				if (state != null) state.Changed -= apply;
				if (renderer != null) renderer.ActivePath = null;
				state = null;
				renderer = null;
			}
		}
	}

	/// <summary>Compact reusable UI Toolkit path panel. Hosts route pick requests into their graph input.</summary>
	public sealed class GraphPathPresenter : IDisposable
	{
		private readonly Action<IGraphNode> navigate;
		private readonly Func<IGraphNode, string> nodeLabel;
		private readonly Action<GraphPathEndpoint> pick;
		private readonly VisualElement root, rows;
		private readonly IGraphScene scene;
		private readonly VisualElement startChoices, endChoices;
		private readonly TextField startSearch, endSearch;
		private readonly GraphPathState state;
		private readonly Label summary;
		private string startId, endId;

		/// <summary>Creates a path panel bound to graph state.</summary>
		/// <param name="parent">Parent element for the panel.</param>
		/// <param name="scene">Scene used to resolve path nodes.</param>
		/// <param name="state">Path state presented by the panel.</param>
		/// <param name="navigate">Callback that navigates to a selected node.</param>
		/// <param name="beginGraphPick">Callback that begins picking a path endpoint in the graph.</param>
		/// <param name="nodeLabel">Optional function that formats node labels.</param>
		public GraphPathPresenter(VisualElement parent, IGraphScene scene, GraphPathState state,
								  Action<IGraphNode> navigate, Action<GraphPathEndpoint> beginGraphPick,
								  Func<IGraphNode, string> nodeLabel = null)
		{
			this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
			this.state = state ?? throw new ArgumentNullException(nameof(state));
			this.navigate = navigate;
			pick = beginGraphPick;
			this.nodeLabel = nodeLabel ?? (node => node.Label);
			root = GraphUi.Element("path-panel");
			root.name = "graph-path-panel";

			// Compose two endpoint selectors and an ordered route list, then subscribe to shared state changes.
			parent.Add(root);
			var header = GraphUi.Element("path-panel-header");
			root.Add(header);
			header.Add(GraphUi.Label("SHORTEST PATH", "section-title"));
			header.Add(GraphUi.Button("×", state.Clear, "path-dismiss"));
			startSearch = EndpointRow(root, "Start", GraphPathEndpoint.Start, out startChoices);
			endSearch = EndpointRow(root, "End", GraphPathEndpoint.End, out endChoices);
			startSearch.RegisterValueChangedCallback(_ => PopulateChoices(startChoices, GraphPathEndpoint.Start));
			endSearch.RegisterValueChangedCallback(_ => PopulateChoices(endChoices, GraphPathEndpoint.End));
			summary = GraphUi.Label("Choose start and end nodes.", "note");
			root.Add(summary);
			rows = new ScrollView();
			rows.AddToClassList("connections-list");
			rows.AddToClassList("path-nodes");
			root.Add(rows);
			startId = state.StartId;
			endId = state.EndId;
			state.Changed += Refresh;
			Refresh(state);
		}

		/// <summary>Removes the path panel and its state subscription.</summary>
		public void Dispose()
		{
			state.Changed -= Refresh;
			root.RemoveFromHierarchy();
		}

		private TextField EndpointRow(VisualElement parent, string label, GraphPathEndpoint endpoint,
									  out VisualElement choices)
		{
			// Support graph picking through the host callback, falling back to the shared pending-pick state.
			parent.Add(GraphUi.Label(label, "eyebrow"));
			var row = GraphUi.Element("path-endpoint-row");
			parent.Add(row);
			var field = new TextField
				{ name = endpoint == GraphPathEndpoint.Start ? "path-start-search" : "path-end-search" };
			GraphUi.HideFieldLabel(field);
			field.AddToClassList("nolabel");
			row.Add(field);
			row.Add(GraphUi.Button("Pick", () =>
			{
				if (pick != null) pick(endpoint);
				else state.BeginPick(endpoint);
			}, "path-pick"));
			choices = GraphUi.Element("path-choices");
			parent.Add(choices);
			return field;
		}

		private void PopulateChoices(VisualElement choices, GraphPathEndpoint endpoint)
		{
			// Match labels or opaque IDs and bound the deterministic suggestions to eight nodes.
			choices.Clear();
			var query = endpoint == GraphPathEndpoint.Start ? startSearch.value : endSearch.value;
			if (string.IsNullOrWhiteSpace(query)) return;
			foreach (var node in scene.Nodes.Values
									  .Where(n =>
												 n.Label.IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >=
												 0 || n.Id.IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >=
												 0)
									  .OrderBy(n => n.Label, StringComparer.OrdinalIgnoreCase)
									  .ThenBy(n => n.Id, StringComparer.Ordinal).Take(8))
				choices.Add(GraphUi.Button(nodeLabel(node), () => Select(endpoint, node), "link"));
		}

		/// <summary>Sets a path endpoint from a node.</summary>
		/// <param name="endpoint">Path endpoint to assign.</param>
		/// <param name="node">Node to use for the endpoint.</param>
		public void Select(GraphPathEndpoint endpoint, IGraphNode node)
		{
			if (node == null) return;
			startChoices.Clear();
			endChoices.Clear();
			state.SetEndpoint(endpoint, node.Id);
		}

		/// <summary>Sets a path endpoint when the node exists in the scene.</summary>
		/// <param name="endpoint">Path endpoint to assign.</param>
		/// <param name="nodeId">Identifier of the node to select.</param>
		public void SetEndpoint(GraphPathEndpoint endpoint, string nodeId)
		{
			if (scene.Nodes.TryGetValue(nodeId ?? "", out var node)) Select(endpoint, node);
		}

		private void Refresh(GraphPathState changed)
		{
			// Synchronize endpoint fields and choice lists with the current path state.
			startId = changed.StartId;
			endId = changed.EndId;
			startSearch.SetValueWithoutNotify(startId != null && scene.Nodes.TryGetValue(startId, out var startNode)
												  ? nodeLabel(startNode)
												  : startId ?? "");
			endSearch.SetValueWithoutNotify(endId != null && scene.Nodes.TryGetValue(endId, out var endNode)
												? nodeLabel(endNode)
												: endId ?? "");
			rows.Clear();
			if (!changed.IsActive)
			{
				summary.text = changed.PendingPick.HasValue
					? "Click a graph node to set the " + changed.PendingPick.Value.ToString().ToLowerInvariant() +
					  " node · Esc cancels."
					: "Choose start and end nodes.";
				return;
			}

			if (changed.Result == null || !changed.Result.IsReachable)
			{
				summary.text = "No directed path exists between these nodes.";
				return;
			}

			// Show the ordered nodes with each outgoing relationship labeled from the scene schema.
			summary.text = changed.Result.Jumps + (changed.Result.Jumps == 1 ? " jump" : " jumps");
			for (var i = 0; i < changed.Result.NodeIds.Count; i++)
			{
				var id = changed.Result.NodeIds[i];
				if (!scene.Nodes.TryGetValue(id, out var node)) continue;
				var row = GraphUi.Element("connection");
				rows.Add(row);
				row.Add(GraphUi.Label((i + 1).ToString(), "connection-kind"));
				row.Add(GraphUi.Button(nodeLabel(node), () => navigate?.Invoke(node), "link"));
				if (i < changed.Result.EdgeIds.Count &&
					scene.Edges.TryGetValue(changed.Result.EdgeIds[i], out var edge))
					row.Add(GraphUi.Label(
								"→ " + (scene.Schema.Relations.TryGetValue(edge.Relation, out var relation)
									? relation.Label
									: edge.Relation), "connection-relation"));
			}
		}
	}
}
