using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Cashg.GraphExplorer
{
	/// <summary>Shared pointer hover and primary-click handling for graph hosts.</summary>
	public static class GraphSceneInput
	{
		/// <summary>Gets the default time window used to recognize a double click.</summary>
		public const float DefaultDoubleClickSeconds = 0.35f;

		/// <summary>Determines whether a screen position is over blocking UI.</summary>
		/// <param name="root">Panel root to inspect.</param>
		/// <param name="screenPosition">Pointer position in screen coordinates.</param>
		/// <returns><see langword="true" /> when UI blocks graph input.</returns>
		public static bool IsUiBlockingGraph(VisualElement root, Vector2 screenPosition)
		{
			// Convert to panel coordinates before hit testing; the root itself leaves the graph interactive.
			if (root?.panel == null) return true;
			var point = RuntimePanelUtils.ScreenToPanel(
				root.panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
			var hit = root.panel.Pick(point);
			return hit != null && hit != root;
		}

		/// <summary>Samples the node or edge under a screen position.</summary>
		/// <param name="root">Panel root used to detect UI blocking.</param>
		/// <param name="graph">Scene to sample.</param>
		/// <param name="edgeFilter">Filter applied while picking edges.</param>
		/// <param name="renderer">Renderer used to pick nodes and edges.</param>
		/// <param name="camera">Camera used for projection.</param>
		/// <param name="screenPosition">Pointer position in screen coordinates.</param>
		/// <returns>The hover sample at the requested position.</returns>
		public static HoverSample Sample(
		VisualElement root,
		IGraphScene graph,
		IGraphEdgeFilter edgeFilter,
		GraphRenderer renderer,
		Camera camera,
		Vector2 screenPosition)
		{
			if (IsUiBlockingGraph(root, screenPosition))
				return new HoverSample(true, null, null);
			return new HoverSample(
				false,
				renderer.Pick(graph, camera, screenPosition),
				renderer.PickEdge(graph, edgeFilter, camera, screenPosition));
		}

		/// <summary>Stop camera follow and clear persisted view selection.</summary>
		public static void ReleaseCameraFocus(GraphViewState view, GraphNavigation navigation)
		{
			navigation?.StopTracking();
			if (view != null) view.SelectedId = "";
		}

		/// <summary>Advance double-click tracking; returns true when the same node was clicked twice within the window.</summary>
		/// <summary>Tracks a click and reports whether it completes a same-node double click.</summary>
		/// <param name="state">State retained between primary clicks.</param>
		/// <param name="node">Clicked node.</param>
		/// <param name="time">Current unscaled time.</param>
		/// <param name="doubleClickSeconds">Maximum interval between clicks.</param>
		/// <returns><see langword="true" /> when the click is a double click.</returns>
		public static bool RegisterPrimaryClick(
		ref PrimaryClickState state,
		IGraphNode node,
		float time,
		float doubleClickSeconds = DefaultDoubleClickSeconds)
		{
			if (node == null)
			{
				// An empty-space click breaks the same-node double-click sequence.
				state = default;
				return false;
			}

			var doubleClick = node.Id == state.LastNodeId && time - state.LastTime < doubleClickSeconds;

			// Consume a successful pair so a third click starts a fresh sequence.
			if (doubleClick)
			{
				state = default;
			}
			else
			{
				state.LastNodeId = node.Id;
				state.LastTime = time;
			}

			return doubleClick;
		}

		/// <summary>Handles a primary click, selecting nodes or clearing focus on empty space.</summary>
		/// <param name="sample">Current hover sample.</param>
		/// <param name="mouse">Input mouse device.</param>
		/// <param name="clickState">State retained for double-click detection.</param>
		/// <param name="navigation">Camera navigation controller.</param>
		/// <param name="view">Graph view state.</param>
		/// <param name="select">Callback that updates node selection.</param>
		/// <param name="doubleClickSeconds">Maximum interval between clicks.</param>
		public static void HandlePrimaryClick(
		HoverSample sample,
		Mouse mouse,
		ref PrimaryClickState clickState,
		GraphNavigation navigation,
		GraphViewState view,
		Action<IGraphNode> select,
		float doubleClickSeconds = DefaultDoubleClickSeconds)
		{
			if (sample.Blocked || mouse == null || !mouse.leftButton.wasPressedThisFrame || select == null) return;

			// Select immediately, using a repeated click on the same node to begin camera tracking.
			if (sample.Node != null)
			{
				select(sample.Node);
				if (RegisterPrimaryClick(ref clickState, sample.Node, Time.unscaledTime, doubleClickSeconds))
					navigation?.Focus(sample.Node);
				return;
			}

			clickState = default;

			// Empty graph space clears both selection and any camera tracking lock.
			ReleaseCameraFocus(view, navigation);
			select(null);
		}

		/// <summary>Consume a graph node click when the path panel is waiting for an endpoint.</summary>
		public static bool HandlePathPick(HoverSample sample, GraphPathState path)
		{
			return !sample.Blocked && path != null && path.PendingPick.HasValue && sample.Node != null &&
				   path.AcceptPickedNode(sample.Node.Id);
		}

		/// <summary>Represents primary click state.</summary>
		public struct PrimaryClickState
		{
			/// <summary>Gets or sets the time of the previous primary click.</summary>
			public float LastTime;

			/// <summary>Gets or sets the identifier of the node from the previous click.</summary>
			public string LastNodeId;
		}

		/// <summary>Represents hover sample.</summary>
		public readonly struct HoverSample
		{
			/// <summary>Gets whether UI blocked the pointer position.</summary>
			public bool Blocked { get; }

			/// <summary>Gets the node under the pointer, when any.</summary>
			public IGraphNode Node { get; }

			/// <summary>Gets the edge under the pointer, when any.</summary>
			public IGraphEdge Edge { get; }

			/// <summary>Creates a hover sample.</summary>
			/// <param name="blocked">Whether UI blocked graph input.</param>
			/// <param name="node">Node under the pointer.</param>
			/// <param name="edge">Edge under the pointer.</param>
			public HoverSample(bool blocked, IGraphNode node, IGraphEdge edge)
			{
				Blocked = blocked;
				Node = node;
				Edge = edge;
			}
		}
	}
}