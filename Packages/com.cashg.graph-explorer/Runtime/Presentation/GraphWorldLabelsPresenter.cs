using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static Cashg.GraphExplorer.GraphWorldLabelPresenter;

namespace Cashg.GraphExplorer
{
	/// <summary>Composes display text and color for a node's world-space label.</summary>
	/// <typeparam name="TNode">Node type.</typeparam>
	/// <param name="node">Node being labeled.</param>
	/// <param name="task">Optional task status for the node.</param>
	/// <param name="name">Receives the node name text.</param>
	/// <param name="meta">Receives the metadata text.</param>
	/// <param name="color">Receives the metadata color.</param>
	public delegate void GraphLabelComposer<TNode>(TNode node, GraphTaskVisual task, out string name, out string meta,
												   out Color color);

	/// <summary>Represents graph world labels options.</summary>
	public sealed class GraphWorldLabelsOptions<TNode, TLabel>
		where TNode : class, IGraphNode where TLabel : GraphWorldNodeLabel
	{
		/// <summary>Gets or sets the camera used to project nodes.</summary>
		public Func<Camera> Camera;

		/// <summary>Gets or sets the callback that composes label text.</summary>
		public GraphLabelComposer<TNode> Compose;

		/// <summary>Gets or sets the callback that creates a label model and its UI.</summary>
		public Action CreateLabel;

		/// <summary>Gets or sets the distance threshold for detailed labels.</summary>
		public Func<float> DetailDistance;

		/// <summary>When set, adds horizontal space during label width measurement (e.g. a type icon).</summary>
		public Func<TNode, bool, bool, float> ExtraLabelWidth;

		/// <summary>Gets or sets the callback that fills workflow step states.</summary>
		public Action<TNode, string[]> FillSteps;

		/// <summary>Host hook after default label fields are bound (featured hover/selection styling, icons, etc.).</summary>
		public Action<TNode, TLabel, bool, bool> FinalizeLabel;

		/// <summary>Gets or sets the predicate indicating whether a node has workflow steps.</summary>
		public Func<TNode, bool, bool> HasSteps;

		/// <summary>Gets or sets the callback that returns the hovered node.</summary>
		public Func<TNode> Hovered;

		/// <summary>Gets or sets the maximum distance at which labels appear.</summary>
		public Func<float> MaxDistance;

		/// <summary>When set, overrides name font size for label measurement and placement.</summary>
		public Func<TNode, bool, bool, GraphNodeLabelSettings, float> NameFontSize;

		/// <summary>Gets or sets the maximum number of label models to create.</summary>
		public int NodeLabelCap;

		/// <summary>Gets or sets the height reserved for a step row.</summary>
		public float NodeStepRowHeight;

		/// <summary>Gets or sets the width reserved for a step row.</summary>
		public float NodeStepRowWidth;

		/// <summary>Gets or sets a callback that returns nodes keyed by identifier.</summary>
		public Func<IReadOnlyDictionary<string, TNode>> Nodes;

		/// <summary>Gets or sets the function that calculates opacity by distance and focus state.</summary>
		public Func<float, bool, float> Opacity;

		/// <summary>Gets or sets the callback that paints workflow step states.</summary>
		public Action<TLabel, string[]> PaintSteps;

		/// <summary>Gets or sets the safe area available for label placement.</summary>
		public Func<Rect> SafeArea;

		/// <summary>Gets or sets the callback that returns the selected node.</summary>
		public Func<TNode> Selected;

		/// <summary>Gets or sets the callback that returns label settings.</summary>
		public Func<GraphNodeLabelSettings> Settings;

		/// <summary>When set, supplies the type caption line and layout height. Return empty to omit the type line.</summary>
		public Func<TNode, bool, bool, string> TypeCaption;

		/// <summary>Gets or sets the function that returns a node's type color.</summary>
		public Func<TNode, Color> TypeColor;

		/// <summary>Gets or sets the function that returns a node's type label.</summary>
		public Func<TNode, string> TypeLabel;

		/// <summary>Gets or sets the renderer used to measure and cull node labels.</summary>
		public GraphRenderer graphRenderer;

		/// <summary>Gets or sets the reusable workflow step state buffer.</summary>
		public string[] jobStepStates;

		/// <summary>Gets or sets the nodes considered for label placement.</summary>
		public List<TNode> labelCandidates;

		/// <summary>Gets or sets the last assigned vertical offset for each node label.</summary>
		public Dictionary<string, int> labelOffsets;

		/// <summary>Gets or sets the node placement order for the current frame.</summary>
		public List<TNode> labelPlacementOrder;

		/// <summary>Gets or sets the reusable list of identifiers to prune.</summary>
		public List<string> labelPrune;

		/// <summary>Gets or sets identifiers already queued for label placement.</summary>
		public HashSet<string> labelQueued;

		/// <summary>Gets or sets smoothed panel positions keyed by node identifier.</summary>
		public Dictionary<string, Vector2> labelSmoothPosition;

		/// <summary>Gets or sets the reusable label models.</summary>
		public List<TLabel> labels;

		/// <summary>Gets or sets nodes labeled during the previous frame.</summary>
		public HashSet<string> lastFrameLabelNodes;

		/// <summary>Gets or sets the assigned label slot for each node.</summary>
		public Dictionary<string, int> nodeLabelSlots;

		/// <summary>Gets or sets panel bounds occupied during the current frame.</summary>
		public List<Rect> occupiedLabels;

		/// <summary>Gets or sets the root panel element.</summary>
		public VisualElement root;

		/// <summary>Gets or sets task presentation data keyed by node identifier.</summary>
		public Dictionary<string, GraphTaskVisual> taskVisuals;

		/// <summary>Gets or sets the container for world-space label elements.</summary>
		public VisualElement worldLabels;
	}

	/// <summary>Represents graph world labels presenter.</summary>
	public sealed class GraphWorldLabelsPresenter<TNode, TLabel>
		where TNode : class, IGraphNode where TLabel : GraphWorldNodeLabel
	{
		private readonly GraphWorldLabelsOptions<TNode, TLabel> options;
		private readonly Dictionary<string, float> revealOpacities = new();
		private Vector3 labelCameraPosition;
		private Quaternion labelCameraRotation;
		private bool labelCameraSampled;

		/// <summary>Creates a world-label presenter using the supplied host callbacks and buffers.</summary>
		/// <param name="options">Callbacks and shared state used to build and position labels.</param>
		public GraphWorldLabelsPresenter(GraphWorldLabelsOptions<TNode, TLabel> options)
		{
			this.options = options ?? throw new ArgumentNullException(nameof(options));
		}

		/// <summary>Determines whether a candidate label overlaps a previously placed label.</summary>
		/// <param name="rect">Candidate label rectangle.</param>
		/// <param name="spacing">Minimum spacing between labels.</param>
		/// <returns><see langword="true" /> when the label is blocked.</returns>
		public bool NodeLabelBlocked(Rect rect, float spacing)
		{
			for (var i = 0; i < options.occupiedLabels.Count; i++)
				if (LabelRectsOverlap(rect, options.occupiedLabels[i], spacing))
					return true;
			return false;
		}

		/// <summary>Finds a panel rectangle for a node label within the safe area.</summary>
		/// <param name="s">Label layout settings.</param>
		/// <param name="id">Node identifier.</param>
		/// <param name="anchor">Projected node anchor.</param>
		/// <param name="nodeRadius">Projected node radius.</param>
		/// <param name="width">Label width.</param>
		/// <param name="height">Label height.</param>
		/// <param name="featured">Whether the label may use stacking rows.</param>
		/// <param name="left">Left safe-area boundary.</param>
		/// <param name="right">Right safe-area boundary.</param>
		/// <param name="top">Top safe-area boundary.</param>
		/// <param name="bottom">Bottom safe-area boundary.</param>
		/// <param name="rect">Receives the available rectangle.</param>
		/// <param name="rowUsed">Receives the stacking row index.</param>
		/// <returns><see langword="true" /> when an unblocked rectangle was found.</returns>
		public bool TryNodeLabelRect(GraphNodeLabelSettings s, string id, Vector2 anchor, float nodeRadius, float width,
									 float height, bool featured, float left, float right, float top, float bottom,
									 out Rect rect, out int rowUsed)
		{
			var rows = featured ? Mathf.Clamp(s.StackRows, 1, 4) : 1;

			// Try the prior stacking row first to keep featured labels from jumping between free positions.
			var start = 0;
			if (featured && options.labelOffsets.TryGetValue(id, out var previous) && (uint)previous < (uint)rows)
				start = previous;
			var baseline = anchor.y - nodeRadius - Mathf.Max(0f, s.AboveNodeGap);
			for (var n = 0; n < rows; n++)
			{
				var row = (start + n) % rows;
				var x = anchor.x - width * .5f;
				var y = baseline - height - row * (height + s.LabelSpacing);
				if (featured)
				{
					// Keep selected and task labels inside the safe area even near screen boundaries.
					x = Mathf.Clamp(x, left, Mathf.Max(left, right - width));
					y = Mathf.Clamp(y, top, Mathf.Max(top, bottom - height));
				}

				rect = new Rect(x, y, width, height);
				if (rect.x < left - .5f || rect.xMax > right + .5f || rect.y < top - .5f ||
					rect.yMax > bottom + .5f) continue;
				if (NodeLabelBlocked(rect, s.LabelSpacing)) continue;
				rowUsed = row;
				return true;
			}

			rect = default;
			rowUsed = -1;
			return false;
		}

		/// <summary>Calculates a node's projected radius in panel pixels.</summary>
		/// <param name="node">Node to measure.</param>
		/// <param name="anchor">Projected node anchor.</param>
		/// <returns>Projected radius in pixels.</returns>
		public float NodeRadiusPixels(TNode node, Vector2 anchor)
		{
			// Project the node's upper radius into panel space so the label clears its visible model.
			if (options.graphRenderer == null) return 0f;
			var top = options.Camera()
							 .WorldToScreenPoint(node.Position +
												 options.Camera().transform.up * options.graphRenderer.Radius(node));
			if (top.z <= 0f) return 0f;
			var topPanel =
				RuntimePanelUtils.ScreenToPanel(options.root.panel, new Vector2(top.x, Screen.height - top.y));
			return Mathf.Abs(anchor.y - topPanel.y);
		}

		/// <summary>Acquires a reusable label slot for a node.</summary>
		/// <param name="node">Node receiving a label.</param>
		/// <param name="usedSlots">Slots already allocated this frame.</param>
		/// <returns>Available slot index, or -1 when no slot is available.</returns>
		public int AcquireLabelSlot(TNode node, HashSet<int> usedSlots)
		{
			// Prefer the node's old slot, then recycle any unused slot before growing the bounded pool.
			if (options.nodeLabelSlots.TryGetValue(node.Id, out var slot) && (uint)slot < (uint)options.labels.Count &&
				!usedSlots.Contains(slot)) return slot;
			for (var i = 0; i < options.labels.Count; i++)
				if (!usedSlots.Contains(i))
					return i;
			if (options.labels.Count >= options.NodeLabelCap || options.worldLabels == null) return -1;
			options.CreateLabel();
			return options.labels.Count - 1;
		}

		/// <summary>Adds a node to the current frame's label placement queue.</summary>
		/// <param name="node">Node to queue.</param>
		public void QueueLabel(TNode node)
		{
			if (node == null || !options.labelQueued.Add(node.Id)) return;
			options.labelPlacementOrder.Add(node);
		}

		/// <summary>Positions and updates world-space labels for the current frame.</summary>
		public void UpdateWorldLabels()
		{
			// Reuse pooled labels while recomputing which nodes fit the current camera view.
			using var profile = GraphDiagnostics.NodeLabels.Auto();
			options.labelPlacementOrder.Clear();
			options.labelQueued.Clear();
			QueueLabel(options.Selected());
			QueueLabel(options.Hovered());

			// Prioritize tasks, then last frame's labels, before admitting new ordinary candidates.
			foreach (var id in options.taskVisuals.Keys)
				if (options.Nodes().TryGetValue(id, out var taskNode))
					QueueLabel(taskNode);
			for (var pass = 0; pass < 2; pass++)
				foreach (var node in options.labelCandidates)
				{
					if (node == options.Selected() || node == options.Hovered() ||
						options.taskVisuals.ContainsKey(node.Id)) continue;
					if (options.lastFrameLabelNodes.Contains(node.Id) != (pass == 0)) continue;
					QueueLabel(node);
				}

			options.occupiedLabels.Clear();
			var usedSlots = new HashSet<int>();
			var placedNodes = new HashSet<string>();
			var panelWidth = options.root.resolvedStyle.width;
			var panelHeight = options.root.resolvedStyle.height;
			if (float.IsNaN(panelWidth) || panelWidth < 1f) panelWidth = Screen.width;
			if (float.IsNaN(panelHeight) || panelHeight < 1f) panelHeight = Screen.height;
			var labelSettings = options.Settings();
			var mouse = Mouse.current;
			var mouseScreen = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
			var pointerOverGraph = false;
			if (labelSettings.MouseProximityReveal && mouse != null && Application.isFocused &&
				options.root.panel != null)
			{
				// Reveal on pointer proximity only inside graph space that is not blocked by shell controls.
				var mousePanel =
					RuntimePanelUtils.ScreenToPanel(options.root.panel,
													new Vector2(mouseScreen.x, Screen.height - mouseScreen.y));
				pointerOverGraph = options.Camera().pixelRect.Contains(mouseScreen)
								   && options.SafeArea().Contains(mousePanel)
								   && !GraphSceneInput.IsUiBlockingGraph(options.root, mouseScreen);
			}

			var labelPadding = Mathf.Max(0f, labelSettings.Padding);
			var typeLine = labelSettings.TypeFontSize + 4f;
			var nameLine = labelSettings.NameFontSize + 4f;
			var metaLine = labelSettings.MetaFontSize + 4f;
			var safeArea = options.SafeArea();
			float leftEdge = safeArea.xMin,
				  rightEdge = safeArea.xMax,
				  topEdge = safeArea.yMin,
				  bottomEdge = safeArea.yMax;
			if (rightEdge - leftEdge < labelSettings.MinWidth)
			{
				leftEdge = 8f;
				rightEdge = Mathf.Max(leftEdge + labelSettings.MinWidth, panelWidth - 8f);
			}

			var cameraPosition = options.Camera().transform.position;
			var cameraRotation = options.Camera().transform.rotation;
			var cameraMoved = !labelCameraSampled
							  || (cameraPosition - labelCameraPosition).sqrMagnitude > 1e-8f
							  || Quaternion.Angle(cameraRotation, labelCameraRotation) > .02f;

			// Camera motion bypasses smoothing so labels stay attached while the user navigates.
			if (cameraMoved)
			{
				labelCameraPosition = cameraPosition;
				labelCameraRotation = cameraRotation;
				labelCameraSampled = true;
			}

			var smooth = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
			foreach (var node in options.labelPlacementOrder)
			{
				var focused = node == options.Selected() || node == options.Hovered();
				if (!node.Visible && !focused) continue;
				var hasTask = options.taskVisuals.TryGetValue(node.Id, out var taskStatus);
				var featured = focused || hasTask;
				var screen = options.Camera().WorldToScreenPoint(node.Position);
				if (screen.z <= 0f) continue;
				if (screen.x < -48f || screen.x > Screen.width + 48f || screen.y < -48f ||
					screen.y > Screen.height + 48f) continue;
				var distance = Vector3.Distance(node.Position, cameraPosition);
				var opacity = options.Opacity(distance, focused);
				if (labelSettings.MouseProximityReveal)
				{
					// Fade each node's reveal independently before combining it with distance-based opacity.
					var target = labelSettings.ProximityOpacity(
						Vector2.Distance(new Vector2(screen.x, screen.y), mouseScreen), pointerOverGraph,
						node == options.Selected());
					revealOpacities.TryGetValue(node.Id, out var reveal);
					reveal = labelSettings.RevealFadeSeconds <= 0f
						? target
						: Mathf.MoveTowards(reveal, target, Time.unscaledDeltaTime / labelSettings.RevealFadeSeconds);
					if (reveal > 0f) revealOpacities[node.Id] = reveal;
					else revealOpacities.Remove(node.Id);
					opacity *= reveal;
				}

				if (opacity <= 0f) continue;
				var detailed = focused || distance <= Mathf.Min(options.DetailDistance(), options.MaxDistance());
				var anchor =
					RuntimePanelUtils.ScreenToPanel(options.root.panel,
													new Vector2(screen.x, Screen.height - screen.y));
				Vector2 smoothPoint;
				if (cameraMoved || !options.labelSmoothPosition.TryGetValue(node.Id, out smoothPoint))
					smoothPoint = anchor;
				else smoothPoint = Vector2.Lerp(smoothPoint, anchor, smooth);
				if ((smoothPoint - anchor).sqrMagnitude > 120f * 120f) smoothPoint = anchor;
				var typeName = options.TypeCaption != null
					? options.TypeCaption(node, detailed, featured)
					: detailed
						? options.TypeLabel(node).ToUpperInvariant()
						: "";
				options.Compose(node, hasTask ? taskStatus : null, out var nameText, out var metaText,
								out var metaColor);
				var jobSteps = detailed && options.HasSteps(node, hasTask);
				if (jobSteps)
				{
					options.FillSteps(node, options.jobStepStates);
					metaText = node.Pinned ? "PINNED" : "";
				}

				if (!detailed)
				{
					typeName = "";
					metaText = "";
				}

				var nameFontSize = options.NameFontSize != null
					? options.NameFontSize(node, detailed, featured, labelSettings)
					: labelSettings.NameFontSize;

				// Measure wrapped text, metadata, and optional step chips before reserving collision space.
				var extraWidth = options.ExtraLabelWidth?.Invoke(node, detailed, featured) ?? 0f;
				var width = NodeLabelBlockWidth(labelSettings, typeName, nameText, metaText, nameFontSize, extraWidth);
				if (jobSteps)
					width = Mathf.Max(
						width, Mathf.Min(labelSettings.MaxWidth, options.NodeStepRowWidth + labelPadding * 2f));
				var column = rightEdge - leftEdge;
				if (width > column) width = Mathf.Max(48f, column);
				var textWidth = width - labelPadding * 2f - extraWidth;
				var nameLineAtSize = nameFontSize + 4f;
				var height = labelPadding * 2f + (typeName.Length > 0 ? typeLine : 0f)
											   + NodeNameLineCount(labelSettings, nameText, textWidth, nameFontSize) *
											   nameLineAtSize
											   + (metaText.Length > 0 ? metaLine : 0f) +
											   (jobSteps ? options.NodeStepRowHeight : 0f);
				var nodeRadius = NodeRadiusPixels(node, smoothPoint);
				var placed = TryNodeLabelRect(labelSettings, node.Id, smoothPoint, nodeRadius, width, height, featured,
											  leftEdge, rightEdge, topEdge, bottomEdge, out var rect, out var rowUsed);
				if (!placed) continue;
				var slot = AcquireLabelSlot(node, usedSlots);
				if (slot < 0) break;

				// Commit placement only after both a collision-free rectangle and a pooled label are available.
				options.labelSmoothPosition[node.Id] = smoothPoint;
				options.labelOffsets[node.Id] = rowUsed;
				options.nodeLabelSlots[node.Id] = slot;
				usedSlots.Add(slot);
				placedNodes.Add(node.Id);
				options.occupiedLabels.Add(rect);
				var label = options.labels[slot];
				label.Root.style.display = DisplayStyle.Flex;
				label.Root.style.width = width;
				label.Root.style.opacity = opacity;
				ApplyLabelPanel(label.Root, labelSettings);
				label.Root.style.left = Mathf.Round(rect.x);
				label.Root.style.top = Mathf.Round(rect.y);
				label.TypeHeader.style.display = typeName.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
				label.TypeHeader.style.fontSize = labelSettings.TypeFontSize;
				if (label.TypeHeader.text != typeName) label.TypeHeader.text = typeName;
				label.TypeHeader.style.color = options.TypeColor(node);
				label.Name.style.fontSize = nameFontSize;
				if (label.Name.text != nameText) label.Name.text = nameText;
				label.Name.style.color =
					node.Matches || focused ? new Color(.84f, .89f, .96f) : new Color(.39f, .44f, .51f);
				var showMeta = metaText.Length > 0;
				label.Meta.style.display = showMeta ? DisplayStyle.Flex : DisplayStyle.None;
				if (showMeta)
				{
					label.Meta.style.fontSize = labelSettings.MetaFontSize;
					if (label.Meta.text != metaText) label.Meta.text = metaText;
					label.Meta.style.color = metaColor;
				}

				label.Steps.style.display = jobSteps ? DisplayStyle.Flex : DisplayStyle.None;
				if (jobSteps) options.PaintSteps(label, options.jobStepStates);
				options.FinalizeLabel?.Invoke(node, label, detailed, featured);
			}

			// Hide unused pool entries and discard smoothing, reveal, and row state for labels no longer placed.
			for (var i = 0; i < options.labels.Count; i++)
				if (!usedSlots.Contains(i))
					options.labels[i].Root.style.display = DisplayStyle.None;
			options.lastFrameLabelNodes.Clear();
			foreach (var id in placedNodes) options.lastFrameLabelNodes.Add(id);
			options.labelPrune.Clear();
			foreach (var id in revealOpacities.Keys)
				if (!options.labelQueued.Contains(id))
					options.labelPrune.Add(id);
			foreach (var id in options.labelPrune) revealOpacities.Remove(id);
			if (!labelSettings.MouseProximityReveal) revealOpacities.Clear();
			options.labelPrune.Clear();
			foreach (var id in options.labelSmoothPosition.Keys)
				if (!placedNodes.Contains(id))
					options.labelPrune.Add(id);
			for (var i = 0; i < options.labelPrune.Count; i++)
			{
				options.labelSmoothPosition.Remove(options.labelPrune[i]);
				options.labelOffsets.Remove(options.labelPrune[i]);
				options.nodeLabelSlots.Remove(options.labelPrune[i]);
			}
		}
	}
}