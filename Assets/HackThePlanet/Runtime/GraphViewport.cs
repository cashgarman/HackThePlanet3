using System;
using System.Collections.Generic;
using System.Linq;
using Cashg.GraphExplorer;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace HackThePlanet
{
	/// <summary>Persisted orbit camera and selection focus for a <see cref="GraphViewport" />.</summary>
	public sealed class GraphCameraState
	{
		/// <summary>Orbit target center in graph world space.</summary>
		public Vector3 Center;

		/// <summary>Camera distance from <see cref="Center" /> along the view axis.</summary>
		public float Distance = 30f;

		/// <summary>Node id showing the intel card; last id clicked for double-click focus.</summary>
		public string FocusedId, LastClickId;

		/// <summary>When true, an initial fit has been applied for the current node count.</summary>
		public bool Initialized;

		/// <summary>Unscaled time of the last node click used for double-click detection.</summary>
		public float LastClickAt = -1;

		/// <summary>Node count when the camera state was saved, used to refit when topology changes.</summary>
		public int NodeCount;

		/// <summary>Orbit pitch in degrees.</summary>
		public float Pitch = 28f;

		/// <summary>When true, the player has panned or zoomed away from the auto fit.</summary>
		public bool UserAdjusted;

		/// <summary>Orbit yaw in degrees.</summary>
		public float Yaw;

		/// <summary>Clears orbit, focus, and initialization flags to defaults.</summary>
		public void Reset()
		{
			// Restore the orbit pose to its defaults.
			Center = Vector3.zero;
			Distance = 30f;
			Yaw = 0;
			Pitch = 28f;

			// Clear the initialization flags and focus state.
			Initialized = false;
			UserAdjusted = false;
			NodeCount = 0;
			FocusedId = null;
			LastClickId = null;
			LastClickAt = -1;
		}
	}

	// A workstation-sized view of the package's actual 3D renderer. The host owns
	// gameplay state and the camera; GraphExplorer owns graph drawing and effects.
	/// <summary>UI Toolkit host for GraphExplorer 3D network or research views with intel cards and picking.</summary>
	public sealed class GraphViewport : VisualElement, IDisposable
	{
		// Guide arrow color highlighting the contract objective node.
		private static readonly Color GuideObjectiveOrange = new(1f, .55f, .15f, 1f);

		// Off-screen camera that renders the graph into a UI texture.
		private readonly Camera camera;

		// Circuit backdrop shown behind flat rig rack views.
		private readonly CircuitBackdrop circuit;

		// Edge visibility filter passed to the GraphExplorer renderer.
		private readonly IGraphEdgeFilter edgeFilter = new AllEdges();

		// When true, uses orthographic flat layout instead of the 3D render target.
		private readonly bool flat;

		// Measured flat node button sizes for label placement.
		private readonly Dictionary<string, Vector2> flatSizes = new();

		// Intel card container shown beside the graph viewport.
		private readonly VisualElement focusedCard;

		// Scroll area holding the focused node or technology details.
		private readonly ScrollView focusedScroll;

		// Title and body labels inside the focused intel card.
		private readonly Label focusedTitle, focusedDetails;

		// Gateway and objective node ids used to draw guide arrows.
		private readonly string guideEntryId, guideObjectiveId;

		// Layer holding screen-space node labels over the render texture.
		private readonly VisualElement labels;

		// Screen-space link overlay drawn above the 3D view.
		private readonly LinkOverlay links;

		// Visual settings asset for network graph rendering.
		private readonly GraphVisualSettings networkSettings;

		// Pickable node buttons keyed by graph node id.
		private readonly Dictionary<string, Button> nodeButtons = new();

		// Preformatted intel strings keyed by node or technology id.
		private readonly Dictionary<string, string> nodeIntel = new();

		// When true, the viewport shows the research tree instead of a network.
		private readonly bool research;

		// Persisted orbit camera restored across viewport rebuilds.
		private readonly GraphCameraState savedCamera;

		// Graph document and topology rendered by GraphExplorer.
		private readonly GraphScene scene;

		// Callback invoked when the player selects a node or technology.
		private readonly Action<string> selectNode;

		// Currently selected node or technology id.
		private readonly string selectedId;

		// Active GraphExplorer rendering session bound to the render texture.
		private readonly GraphRenderingSession session;

		// UI Toolkit image source for the 3D graph render.
		private readonly RenderTexture texture;

		// Optional contract waypoint markers overlaid on the network view.
		private readonly WaypointOverlay waypointOverlay;

		// Node id showing the in-progress tool progress indicator.
		private string activeProgressNodeId;

		// Current and drag-start orbit target centers in graph space.
		private Vector3 center;

		// Internal member supporting workstation or graph viewport behavior.
		private Vector3 centerStart;

		// Whether native graph resources have been released.
		private bool disposed;

		// Camera distance from the orbit center.
		private float distance = 30f;

		// Pointer position when an orbit or pan drag started.
		private Vector2 dragStart;

		// Whether the user is dragging to orbit or pan the camera.
		private bool dragging;

		// Requests an automatic camera fit on the next layout pass.
		private bool fitPending;

		// Whether right-drag is adjusting orbit angles instead of panning.
		private bool orbiting;

		// UI text scale applied to node labels and the intel card.
		private float textScale = 1;

		// Orbit yaw and pitch angles in degrees.
		private float yaw, pitch = 28f;

		// Orbit angles captured at the start of a drag gesture.
		private float yawStart, pitchStart;

		/// <summary>Creates a 3D network viewport from live or preview node and link data.</summary>
		/// <param name="nodes">Simulation nodes supplying positions and intel.</param>
		/// <param name="links">Network topology.</param>
		/// <param name="selected">Selected node id.</param>
		/// <param name="preview">When true, builds a preview graph with optional intel redaction.</param>
		/// <param name="select">Selection callback; null clears selection on background click.</param>
		/// <param name="route">Optional highlighted path edges.</param>
		/// <param name="cameraState">Saved camera; created when null.</param>
		/// <param name="intel">Pre-connect intel tier for preview redaction.</param>
		/// <param name="focusIntel">Intel tier for focused node descriptions; defaults to <paramref name="intel" />.</param>
		/// <param name="catalog">Catalog for node type titles; defaults to <see cref="GameCatalog.Default" />.</param>
		/// <param name="guideEntryId">Optional gateway id for guide arrow rendering.</param>
		/// <param name="guideObjectiveId">Optional objective id for guide arrow rendering.</param>
		/// <param name="waypoints">Optional contract waypoints to overlay.</param>
		/// <param name="waypointTier">Unlocked waypoint feature tier for overlays.</param>
		public GraphViewport(List<NetNode> nodes, List<NetLink> links, string selected, bool preview,
							 Action<string> select, GraphPathResult route = null, GraphCameraState cameraState = null,
							 ContractIntel intel = ContractIntel.Full, ContractIntel? focusIntel = null,
							 GameCatalog catalog = null,
							 string guideEntryId = null, string guideObjectiveId = null,
							 IReadOnlyList<NetworkWaypoint> waypoints = null, int waypointTier = 0)
			: this(BuildNetwork(nodes, links, preview, intel), selected, select, false, null, cameraState, false,
				   preview ? null : new HashSet<string>(nodes.Where(n => n.controlled).Select(n => n.id)),
				   preview
					   ? null
					   : new HashSet<string>(nodes.Where(n => n.known && !n.controlled &&
															  links.Any(l => (l.a == n.id &&
																			  nodes.Any(other => other.id == l.b &&
																					  other.controlled)) ||
																			 (l.b == n.id &&
																			  nodes.Any(other => other.id == l.a &&
																					  other.controlled))))
												  .Select(n => n.id)))
		{
			// Keep the guide arrow ids for rendering.
			this.guideEntryId = guideEntryId;
			this.guideObjectiveId = guideObjectiveId;

			// Add the waypoint overlay when there are waypoints to show.
			if (waypoints != null && waypoints.Count > 0)
			{
				// Create the overlay and add it to the viewport.
				waypointOverlay = new WaypointOverlay(camera, nodes, links, waypoints, waypointTier, guideEntryId);
				Add(waypointOverlay);
			}

			// Describe each node that exists in the scene for its intel card.
			foreach (var node in nodes.Where(n => scene.Nodes.ContainsKey(n.id)))
				nodeIntel[node.id] = DescribeNode(node, focusIntel ?? intel, preview, catalog ?? GameCatalog.Default);

			// Refresh the intel card for the current selection.
			UpdateFocusedCard();
		}

		/// <summary>Creates a research tree viewport with progressive reveal from career state.</summary>
		/// <param name="career">Unlock, XP, and reveal state for technologies.</param>
		/// <param name="selected">Selected technology id.</param>
		/// <param name="select">Selection callback.</param>
		/// <param name="cameraState">Saved camera; created when null.</param>
		/// <param name="catalog">Technology source; defaults to <see cref="GameCatalog.Default" />.</param>
		public GraphViewport(Career career, string selected, Action<string> select, GraphCameraState cameraState = null,
							 GameCatalog catalog = null)
			: this(BuildResearch(career, catalog ?? GameCatalog.Default), selected, select, true, null, cameraState)
		{
			// Index technologies by id.
			var technologies = (catalog ?? GameCatalog.Default).Technologies.ToDictionary(t => t.id);

			// Describe each revealed technology for its intel card.
			foreach (var id in scene.Nodes.Keys)
				nodeIntel[id] = DescribeTechnology(technologies[id], career, technologies);

			// Copy each description into its button tooltip.
			foreach (var pair in nodeButtons)
				if (nodeIntel.TryGetValue(pair.Key, out var intel))
					pair.Value.tooltip = intel;

			// Refresh the intel card for the current selection.
			UpdateFocusedCard();
		}

		/// <summary>Creates a flat orthographic viewport for arbitrary graph scenes (for example the rig rack).</summary>
		/// <param name="graph">Scene to render.</param>
		/// <param name="select">Selection callback for node buttons.</param>
		/// <param name="cameraState">Saved camera; created when null.</param>
		/// <param name="flat">When true, hides the 3D render target and uses orthographic layout.</param>
		public GraphViewport(GraphScene graph, Action<string> select, GraphCameraState cameraState, bool flat)
			: this(graph, null, select, true, null, cameraState, flat)
		{
		}

		// Returns whether research for the current UI or rig state.
		private GraphViewport(GraphScene graph, string selected, Action<string> select, bool research,
							  GraphPathResult route, GraphCameraState cameraState, bool flat = false,
							  HashSet<string> controlledNodes = null,
							  HashSet<string> reachableNodes = null)
		{
			// Store the scene, the flat flag, and the select callback.
			scene = graph;
			this.flat = flat;
			selectNode = select;

			// Flatten the nodes onto the plane for the rig rack.
			if (flat)
				foreach (var node in graph.Nodes.Values)
					node.Position = new Vector3(node.Position.x, node.Position.y, 0);

			// Store the selection and saved camera; refit when the view is new or the topology changed.
			selectedId = selected ?? "";
			savedCamera = cameraState ?? new GraphCameraState();
			fitPending = !savedCamera.Initialized ||
						 (!savedCamera.UserAdjusted && savedCamera.NodeCount != graph.Nodes.Count);
			savedCamera.NodeCount = graph.Nodes.Count;
			this.research = research;

			// Style the root element as a full-size viewport.
			AddToClassList("graph-viewport");
			if (flat)
				AddToClassList("flat-rig");
			style.flexGrow = 1;
			style.minHeight = 0;

			// Create the render texture the 3D view draws into.
			texture = new RenderTexture(1024, 640, 24, RenderTextureFormat.ARGB32)
				{ name = "Workstation GraphExplorer", antiAliasing = 2 };
			texture.Create();

			// Show the texture in a full-size image element that ignores input.
			var image = new Image { image = texture, scaleMode = ScaleMode.StretchToFill };
			image.style.position = Position.Absolute;
			image.style.left = image.style.right = image.style.top = image.style.bottom = 0;
			image.pickingMode = PickingMode.Ignore;
			Add(image);

			// Hide the image in flat mode, where cells draw instead.
			if (flat)
				image.style.display = DisplayStyle.None;

			// The rig rack sits on a circuit board; it stays behind the image, links and cells.
			if (flat)
			{
				circuit = new CircuitBackdrop { name = "rig-circuit-backdrop" };
				Insert(0, circuit);
			}

			// Create a hidden camera object and attach the camera component.
			var cameraObject = new GameObject("Workstation GraphExplorer Camera")
				{ hideFlags = HideFlags.HideAndDontSave };
			camera = cameraObject.AddComponent<Camera>();

			// Render into the texture with a solid background and the lens settings.
			camera.targetTexture = texture;
			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = Palette.Hex("071014");
			camera.cullingMask = 1; // GraphRenderer submits its camera-scoped instances on the default layer.
			camera.fieldOfView = 48;
			camera.orthographic = flat;
			camera.nearClipPlane = .1f;
			camera.farClipPlane = 200;

			// Start the camera from the saved orbit pose and place it.
			center = savedCamera.Center;
			distance = savedCamera.Distance;
			yaw = savedCamera.Yaw;
			pitch = savedCamera.Pitch;
			PlaceCamera();

			// Create the renderer session with the matching appearance resources.
			var resources = GameData.Presentation(false);
			if (!research)
				resources = NetworkAppearance.Create(out networkSettings);
			session = GraphRenderingSession.Initialize(camera, resources);
			session.Layout.Bind(scene);
			session.Layout.Paused = true;

			// Tune the renderer's size mapping and edge widths.
			session.Renderer.Size = SizeMapping.Uniform;
			session.Renderer.EdgeWidthScale = 1.15f;
			session.Renderer.PathEdgeWidth = .07f;

			// The simulation stores both traversal directions; the visual graph draws each link once.
			session.Renderer.ActivePath = route == null
				? null
				: new GraphPathResult(route.StartId, route.EndId, route.NodeIds,
									  route.EdgeIds.Select(id => scene.Edges.ContainsKey(id)
															   ? id
															   : string.Join(">", id.Split('>').Reverse())));

			// Measure the graph for the renderer.
			session.Renderer.UpdateMetrics(scene, edgeFilter);

			// Add the 2D link overlay above the 3D image.
			links = new LinkOverlay(scene, camera, selected, route, research, flat, controlledNodes, reachableNodes);
			Add(links);

			// Create the label layer that holds the node buttons and the intel card.
			labels = new VisualElement();
			labels.style.position = Position.Absolute;
			labels.style.left = labels.style.right = labels.style.top = labels.style.bottom = 0;
			labels.pickingMode = PickingMode.Ignore;
			Add(labels);

			// Create one button per node for picking and labels.
			foreach (var node in scene.Nodes.Values)
			{
				// Create the button with its label and click handler.
				var button = new Button(() => ClickNode(node.Id, select))
				{
					name = flat ? node.Id : (research ? "tech-" : "node-") + node.Id,
					text = node.Label
				};

				// Hide the button until it is positioned, then size and color it.
				button.style.display = DisplayStyle.None;
				button.style.position = Position.Absolute;
				button.style.width = research ? 118 : 104;
				button.style.height = 26;
				button.style.fontSize = 10;
				button.style.unityTextAlign = TextAnchor.MiddleCenter;
				button.style.color = node.Id == selected ? Palette.Lime : Palette.Hex("C8DBD5");
				button.style.backgroundColor = node.Id == selected ? Palette.Hex("254337") : Palette.Hex("12242A");

				// Outline the button in the node's type color.
				button.style.borderBottomWidth = button.style.borderTopWidth = 1;
				button.style.borderLeftWidth = button.style.borderRightWidth = 1;
				var tint = scene.Schema.NodeTypes[node.Record.TypeId].Color;
				button.style.borderBottomColor = button.style.borderTopColor = tint;
				button.style.borderLeftColor = button.style.borderRightColor = tint;

				// Register the button with the labels and the lookup.
				labels.Add(button);
				nodeButtons.Add(node.Id, button);
			}

			// Build the intel card container with its border and background.
			focusedCard = new VisualElement { name = "focused-node-card" };
			focusedCard.style.position = Position.Absolute;
			focusedCard.style.display = DisplayStyle.None;
			focusedCard.style.backgroundColor = Palette.Hex("102025");
			focusedCard.style.borderTopWidth = focusedCard.style.borderBottomWidth = 1;
			focusedCard.style.borderLeftWidth = focusedCard.style.borderRightWidth = 1;
			focusedCard.style.borderTopColor = focusedCard.style.borderBottomColor = Palette.Lime;
			focusedCard.style.borderLeftColor = focusedCard.style.borderRightColor = Palette.Lime;

			// Add the static image that fills the card background.
			IntelStatic = new Image
				{ name = "node-intel-static", pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
			IntelStatic.style.position = Position.Absolute;
			IntelStatic.style.left = IntelStatic.style.right = IntelStatic.style.top = IntelStatic.style.bottom = 0;
			focusedCard.Add(IntelStatic);

			// Build the scrolling body that holds the title and details.
			var scroll = focusedScroll = new ScrollView(ScrollViewMode.Vertical);
			scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
			scroll.style.flexGrow = 1;
			scroll.style.paddingLeft = scroll.style.paddingRight = 10;
			scroll.style.paddingTop = scroll.style.paddingBottom = 8;

			// Style the title and details labels.
			focusedTitle = new Label { name = "focused-node-title" };
			focusedTitle.style.fontSize = 14;
			focusedTitle.style.color = Palette.Lime;
			focusedTitle.style.whiteSpace = WhiteSpace.Normal;
			focusedDetails = new Label { name = "focused-node-intel", enableRichText = true };
			focusedDetails.style.fontSize = 12;
			focusedDetails.style.color = Palette.Hex("DCEAE4");
			focusedDetails.style.whiteSpace = WhiteSpace.Normal;

			// Zero the margins and padding, keep the labels from shrinking, and reposition on resize.
			foreach (var label in new[] { focusedTitle, focusedDetails })
			{
				label.style.marginLeft = label.style.marginRight = label.style.marginTop = label.style.marginBottom = 0;
				label.style.paddingLeft =
					label.style.paddingRight = label.style.paddingTop = label.style.paddingBottom = 0;
				label.style.flexShrink = 0;
				label.RegisterCallback<GeometryChangedEvent>(_ => PositionLabels());
			}

			// Stack the title above the details inside the scroll view.
			focusedTitle.style.marginBottom = 4;
			scroll.Add(focusedTitle);
			scroll.Add(focusedDetails);
			focusedCard.Add(scroll);

			// Stop pointer and wheel events inside the card from reaching the viewport.
			focusedCard.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
			focusedCard.RegisterCallback<WheelEvent>(evt => evt.StopPropagation());

			// Attach the card to the label layer.
			labels.Add(focusedCard);

			// Resize the render target and camera whenever the panel layout changes.
			RegisterCallback<GeometryChangedEvent>(_ =>
			{
				// Skip layout passes before the panel has a size.
				if (disposed || contentRect.width <= 0 || contentRect.height <= 0)
					return;

				// Match the render target to the panel; projection and picking share this aspect.
				var width = Mathf.Clamp(Mathf.RoundToInt(contentRect.width), 256, 2048);
				var height = Mathf.Clamp(Mathf.RoundToInt(contentRect.height), 256, 2048);

				// Resize the render texture when the panel size changed.
				if (texture.width != width || texture.height != height)
				{
					camera.targetTexture = null;
					texture.Release();
					texture.width = width;
					texture.height = height;
					texture.Create();
					camera.targetTexture = texture;
				}

				// Keep the camera aspect in step with the target.
				camera.aspect = (float)width / height;

				// Fit the view once for a new or changed scene.
				if (fitPending)
				{
					ResetView();
					savedCamera.Initialized = true;
					fitPending = false;
				}

				// Place the labels over the new layout.
				PositionLabels();
			});

			// Zoom by a fixed step per wheel notch and stop the event.
			RegisterCallback<WheelEvent>(evt =>
			{
				Zoom(evt.delta.y > 0 ? 1.12f : .89f);
				evt.StopPropagation();
			});

			// Begin a drag on left, middle, or right press, but not on node buttons.
			RegisterCallback<PointerDownEvent>(evt =>
			{
				if (evt.button is not (0 or 1 or 2) || evt.target is Button)
					return;

				// Suppress the context menu on the right button.
				if (evt.button == 1)
					evt.PreventDefault();

				// Record the starting pose; the left button orbits in 3D, other drags pan.
				dragging = true;
				orbiting = !flat && evt.button == 0;
				dragStart = evt.position;
				centerStart = center;
				yawStart = yaw;
				pitchStart = pitch;

				// Capture the pointer so the drag continues outside the panel.
				this.CapturePointer(evt.pointerId);
			});

			// Update the view while dragging.
			RegisterCallback<PointerMoveEvent>(evt =>
			{
				// Ignore moves when no drag is active.
				if (!dragging)
					return;

				// Measure how far the pointer has moved since the press.
				var delta = (Vector2)evt.position - dragStart;

				// Mark the view as user-adjusted once the drag passes a small threshold.
				if (delta.sqrMagnitude > 25)
					savedCamera.UserAdjusted = true;

				// Orbit in 3D; otherwise pan the center across the view plane.
				if (orbiting)
				{
					yaw = yawStart + delta.x * .35f;
					pitch = Mathf.Clamp(pitchStart + delta.y * .3f, -65, 65);
				}
				else
				{
					center = centerStart + (-camera.transform.right * delta.x + camera.transform.up * delta.y) *
						distance / 900f;
				}

				// Update the camera and the labels.
				PlaceCamera();
				PositionLabels();
			});

			// End the drag and treat a short press as a click.
			RegisterCallback<PointerUpEvent>(evt =>
			{
				// Ignore releases when no drag is active.
				if (!dragging)
					return;

				// Treat a short drag as a click and pick the node under the pointer.
				var smallMove = Vector2.Distance(dragStart, evt.position) < 5;
				var hit = evt.button == 0 && smallMove ? PickNode(this.WorldToLocal(evt.position)) : null;

				// End the drag and release the pointer.
				dragging = false;
				this.ReleasePointer(evt.pointerId);

				// Select the picked node, or clear the selection on a background click.
				if (hit != null)
					ClickNode(hit, select);
				else if (evt.button == 0 && smallMove && !string.IsNullOrEmpty(selectedId))
					ClearSelection(select);
			});
		}

		/// <summary>Gets the image element receiving procedural intel-card static from <see cref="SignalVisuals" />.</summary>
		public Image IntelStatic { get; }

		/// <summary>Gets the number of nodes drawn in the last render pass.</summary>
		public int VisibleNodes => session.Renderer.VisibleCount;

		/// <summary>Gets the number of edges drawn in the last render pass.</summary>
		public int VisibleEdges => session.Renderer.VisibleEdges;

		/// <summary>Gets the total nodes in the bound graph scene.</summary>
		public int NodeCount => scene.Nodes.Count;

		/// <summary>Persists camera state and releases render textures, session, and hidden camera objects.</summary>
		public void Dispose()
		{
			// Ignore repeat calls.
			if (disposed)
				return;
			disposed = true;

			// Persist the orbit pose to the saved camera.
			savedCamera.Center = center;
			savedCamera.Distance = distance;
			savedCamera.Yaw = yaw;
			savedCamera.Pitch = pitch;

			// Dispose the rendering session.
			session.Dispose();

			// Release the owned settings, the camera target, and the render texture.
			if (networkSettings != null)
				DestroyOwned(networkSettings);
			if (camera != null)
				camera.targetTexture = null;
			if (texture != null)
			{
				texture.Release();
				DestroyOwned(texture);
			}

			// Destroy the hidden camera object.
			if (camera != null)
				DestroyOwned(camera.gameObject);
		}

		/// <summary>Requests a repaint of the 2D link overlay after hacked-connection colors change.</summary>
		public void RefreshConnectionColors()
		{
			// Repaint the link overlay with the new colors.
			links?.MarkDirtyRepaint();
		}

		/// <summary>Configures label, hit target, and styling for a node in flat rig layout mode.</summary>
		/// <param name="id">Graph node id.</param>
		/// <param name="size">World-space footprint used to size the overlay button.</param>
		/// <param name="tooltip">Hover text for module cells.</param>
		/// <param name="decorative">When true, draws a non-interactive decorative cell.</param>
		/// <param name="module">When true, styles the cell as an interactive rig module.</param>
		public void ConfigureFlatNode(string id, Vector2 size, string tooltip, bool decorative = false,
									  bool module = false)
		{
			// Store the footprint used to size the overlay button.
			flatSizes[id] = size;

			// Skip ids that have no button.
			if (!nodeButtons.TryGetValue(id, out var button))
				return;

			// Set the hover text.
			button.tooltip = tooltip;

			// Modules show their label on a background tinted by branch.
			if (module)
			{
				// Make the module label clickable and tint its background.
				button.text = scene.Nodes[id].Label;
				button.pickingMode = PickingMode.Position;
				var branch = scene.Schema.NodeTypes[scene.Nodes[id].Record.TypeId].Color;
				button.style.backgroundColor = new Color(branch.r * .22f, branch.g * .22f, branch.b * .22f, .94f);
				button.style.opacity = 1f;
			}
			else
			{
				// Other cells are blank, dark tiles that still take clicks.
				button.text = "";
				button.pickingMode = PickingMode.Position;
				button.style.backgroundColor = new Color(.03f, .07f, .08f, .88f);
				button.style.opacity = 1f;
			}

			// Decorative cells show their label but ignore input and children.
			if (decorative && !module)
			{
				button.text = scene.Nodes[id].Label;
				button.pickingMode = PickingMode.Ignore;
				button.style.backgroundColor = new Color(.04f, .09f, .1f, .95f);
				foreach (var child in button.Query<VisualElement>().ToList())
					child.pickingMode = PickingMode.Ignore;
			}

			// Reposition the labels for the new layout.
			PositionLabels();
		}

		/// <summary>Simulates a click on the node button with the given UI name when visible.</summary>
		/// <param name="name">Unity UI name of the target button.</param>
		/// <returns>True when a matching enabled button was activated.</returns>
		public bool ActivateControl(string name)
		{
			// Find the enabled button with this name.
			var pair = nodeButtons.FirstOrDefault(p => p.Value.name == name && p.Value.enabledInHierarchy);

			// Fail when no enabled button matches.
			if (pair.Value == null)
				return false;

			// Activate the matching node.
			ClickNode(pair.Key, selectNode);

			return true;
		}

		// Handles ClickNode for the workstation or graph UI.
		private void ClickNode(string id, Action<string> select)
		{
			if (flat)
			{
				// Flat cells only report the selection.
				select(id);
				return;
			}

			// Detect a second click on the same node within 0.4 seconds.
			var doubleClick = savedCamera.LastClickId == id && Time.unscaledTime - savedCamera.LastClickAt <= .4f;
			savedCamera.LastClickId = doubleClick ? null : id;
			savedCamera.LastClickAt = Time.unscaledTime;

			// Focus the camera on a double-click; a single click only opens the intel card, per the setting.
			if (SelectWithoutCameraFocus())
			{
				if (doubleClick)
					FocusNode(id);
				else
					FocusIntel(id);
			}
			else
			{
				// Focus the camera on the node.
				FocusNode(id);
			}

			// Notify the caller of the selection.
			select(id);
		}

		// Returns whether SelectWithoutCameraFocus for the current UI or rig state.
		private static bool SelectWithoutCameraFocus()
		{
			// Prefer the live app setting when the app exists.
			if (WorkstationApp.Instance != null)
				return WorkstationApp.Instance.SelectWithoutCameraFocus;

			// Otherwise read the saved preference, defaulting to on.
			return PlayerPrefs.GetInt(WorkstationApp.SelectWithoutCameraFocusPreference, 1) != 0;
		}

		// Handles ClearSelection for the workstation or graph UI.
		private void ClearSelection(Action<string> select)
		{
			// Forget the focused card and the last click.
			savedCamera.FocusedId = null;
			savedCamera.LastClickId = null;

			// Clear the selection in the caller.
			select(null);
		}

		/// <summary>Frames the camera on a node and opens its intel card.</summary>
		/// <param name="id">Node id to focus.</param>
		public void FocusNode(string id)
		{
			// Skip unknown nodes.
			if (!scene.Nodes.TryGetValue(id, out var node))
				return;

			// Open the intel card and take manual control of the view.
			FocusIntel(id);
			savedCamera.UserAdjusted = true;
			fitPending = false;

			// Frame the node at a distance scaled to its radius.
			distance = Mathf.Clamp(session.Renderer.Radius(node) * 10, 8, 18);

			// Leave space to the right for the expanded intel card.
			center = node.Position + camera.transform.right *
				(distance * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f) * camera.aspect * .45f);

			// Apply the camera move and reposition the labels.
			PlaceCamera();
			PositionLabels();
		}

		// Handles FocusIntel for the workstation or graph UI.
		private void FocusIntel(string id)
		{
			// Skip unknown nodes.
			if (!scene.Nodes.ContainsKey(id))
				return;

			// Show the intel card for this node.
			savedCamera.FocusedId = id;
			UpdateFocusedCard();

			// Reposition the labels.
			PositionLabels();
		}

		// Handles UpdateFocusedCard for the workstation or graph UI.
		private void UpdateFocusedCard()
		{
			// Show the card only for the focused, selected node that is not in progress.
			var focused = !flat && savedCamera.FocusedId == selectedId && scene.Nodes.ContainsKey(selectedId)
						  && activeProgressNodeId != selectedId;
			focusedCard.style.display = focused ? DisplayStyle.Flex : DisplayStyle.None;

			// Hide the card when it is not focused.
			if (!focused)
				return;

			// Fill the card with the node label and its formatted intel.
			focusedTitle.text = scene.Nodes[selectedId].Label;
			var intel = nodeIntel.TryGetValue(selectedId, out var detail) ? detail : "NO INTEL";
			focusedDetails.text = GameplayRichText.Format(intel);
		}

		// Returns whether preview for the current UI or rig state.
		internal static string DescribeNode(NetNode node, ContractIntel intel, bool preview, GameCatalog catalog)
		{
			// Previews without full intel show only the redacted placeholder.
			if (preview && intel != ContractIntel.Full)
				return "NO INTEL\nUnknown system. Connect to discover this network.";

			// Build the status line from the control state.
			var status = node.controlled ? "CONTROLLED" : "DISCOVERED / NOT CONTROLLED";

			// Return the no-intel text when no intel is allowed.
			if (intel == ContractIntel.None)
				return "NO INTEL\n" + status;

			// Resolve the role title from the catalog, falling back to the node kind.
			var type = catalog.NodeTypes.FirstOrDefault(t => t.id == node.typeId)
					   ?? catalog.NodeTypes.FirstOrDefault(t => t.glyph == node.kind);
			var role = type?.title ?? node.kind.ToString();

			// Topology-only intel shows the role but not the security rating.
			if (intel == ContractIntel.Topology)
				return "LIMITED INTEL / " + role + "\n" + status;

			// Full intel includes the security rating.
			return "FULL INTEL / " + role + "\n" + status + " / SECURITY " + node.security;
		}

		// Returns whether preview for the current UI or rig state.
		private static GraphScene BuildNetwork(List<NetNode> nodes, List<NetLink> links, bool preview,
											   ContractIntel intel)
		{
			// Create an empty document and schema for the live network.
			var document = new GraphDocument("workstation-network", "Live network");
			var schema = new GraphSchema();

			// Redact at the graph-document boundary so labels, picking, models and effects
			// all receive the same anonymous topology, without changing simulation nodes.
			if (preview && intel != ContractIntel.Full)
			{
				// Register the anonymous node and connection types.
				schema.Register(
					new GraphNodeTypeDefinition("unknown", "Unknown system", "default", Palette.Cyan, 1.12f));
				schema.Register(new GraphRelationDefinition("network", "Connection", Palette.Cyan));

				// Topology-only previews keep the nodes and links, with anonymous labels.
				if (intel == ContractIntel.Topology)
				{
					// Add every node as an anonymous system.
					foreach (var node in nodes)
						document.UpsertNode(new GraphNodeRecord(node.id, "unknown", "UNKNOWN SYSTEM"));

					// Add every link between those systems.
					foreach (var link in links)
						document.UpsertEdge(new GraphEdgeRecord(link.a + ">" + link.b, link.a, link.b, "network"));
				}

				// Copy the simulation positions onto the redacted scene and pin them.
				var redacted = new GraphScene(document, schema);
				foreach (var node in nodes.Where(n => redacted.Nodes.ContainsKey(n.id)))
				{
					redacted.Nodes[node.id].Position = node.position;
					redacted.Nodes[node.id].Pinned = true;
				}

				return redacted;
			}

			// Register a node type for each role, plus owned and objective variants.
			foreach (var group in nodes.GroupBy(n => string.IsNullOrEmpty(n.typeId) ? n.kind.ToString() : n.typeId))
			{
				var node = group.First();
				var glyph = node.kind.ToString();
				var color = node.color.a > 0 ? node.color : Palette.Kind(node.kind);
				schema.Register(new GraphNodeTypeDefinition(group.Key, group.Key, glyph, color, 1.12f));
				schema.Register(
					new GraphNodeTypeDefinition(group.Key + "-owned", group.Key, glyph, Palette.Lime, 1.12f));
				schema.Register(
					new GraphNodeTypeDefinition(group.Key + "-objective", group.Key, glyph, Palette.Amber, 1.27f));
			}

			// Register the connection relation used by every link.
			schema.Register(new GraphRelationDefinition("network", "Connection", Palette.Cyan));

			// Add each visible node with its type, label, and state flags.
			foreach (var node in nodes.Where(n => preview || n.known))
				document.UpsertNode(new GraphNodeRecord(
										node.id,
										(string.IsNullOrEmpty(node.typeId) ? node.kind.ToString() : node.typeId) +
										(node.controlled ? "-owned" : node.objective ? "-objective" : ""), node.label,
										new Dictionary<string, GraphValue>
										{
											["objective"] = GraphValue.From(node.objective && !node.collected),
											["frontier"] = GraphValue.From(
												!preview && node.known && !node.controlled &&
												links.Any(l => (l.a == node.id &&
																nodes.Any(n => n.id == l.b && n.controlled)) ||
															   (l.b == node.id &&
																nodes.Any(n => n.id == l.a && n.controlled))))
										}));

			// Add each link whose endpoints are both in the document.
			foreach (var link in links.Where(l => document.Nodes.ContainsKey(l.a) && document.Nodes.ContainsKey(l.b)))
				document.UpsertEdge(new GraphEdgeRecord(link.a + ">" + link.b, link.a, link.b, "network"));

			// Build the scene and copy the pinned positions and emphasis onto it.
			var scene = new GraphScene(document, schema);
			foreach (var node in nodes.Where(n => document.Nodes.ContainsKey(n.id)))
			{
				var visual = scene.Nodes[node.id];
				visual.Position = node.position;
				visual.Pinned = true;
				visual.Emphasis = node.objective && !node.collected ? ToolEmphasis.Next : ToolEmphasis.None;
			}

			return scene;
		}

		// Produces the DescribeTechnology text shown in the UI.
		internal static string DescribeTechnology(Technology tech, Career career,
												  IReadOnlyDictionary<string, Technology> all)
		{
			string Title(string id)
			{
				return all.TryGetValue(id, out var parent) ? parent.title : id;
			}

			// Work out ownership and whether every parent is unlocked.
			var owned = career.unlocked.Contains(tech.id);
			var ready = tech.parents.All(career.unlocked.Contains);

			// Pick the status line in priority order: unlocked, locked, available, or still short of XP.
			var status = owned ? "UNLOCKED"
				: !ready ? "LOCKED / REQUIRES " + string.Join(" + ", tech.parents.Select(Title))
				: career.xp >= tech.cost ? "AVAILABLE / " + tech.cost + " XP"
				: tech.cost + " XP / NEED " + (tech.cost - career.xp) + " MORE";

			// Build the lines: branch and kind, status, then the description.
			var lines = new List<string>
				{ tech.branch + (tech.special ? " / ABILITY" : " / PASSIVE"), status, tech.description };

			// Note the dual-parent requirement on unowned nodes.
			if (!owned && tech.parents.Length > 1)
				lines.Add("BOTH parent technologies required.");

			// Join the lines into the intel text.
			return string.Join("\n", lines);
		}

		// Shared helper used by the workstation or graph viewport.
		private static GraphScene BuildResearch(Career career, GameCatalog catalog)
		{
			// Create an empty research document and schema.
			var document = new GraphDocument("workstation-research", "Research network");
			var schema = new GraphSchema();

			// Each branch has a bright type for owned nodes and a dimmed twin for revealed but locked nodes.
			foreach (var branch in catalog.Technologies.Select(t => t.branch).Distinct())
			{
				schema.Register(new GraphNodeTypeDefinition(branch, branch, "default",
															Palette.Branch(branch), .95f));
				schema.Register(new GraphNodeTypeDefinition(LockedType(branch), branch, "default",
															LockedColor(Palette.Branch(branch)), .95f));
			}

			// Register the prerequisite relation drawn between technologies.
			schema.Register(new GraphRelationDefinition("requires", "Requires", Palette.Cyan));

			// Progressive reveal: only discovered technologies (root, owned nodes and their direct
			// children) enter the document. An edge is drawn only when both of its ends are visible.
			var visible = catalog.Technologies.Where(t => career.researchRevealed.Contains(t.id)).ToArray();
			var visibleIds = new HashSet<string>(visible.Select(t => t.id));

			// Add each revealed technology, marking it affordable when it can be bought now.
			foreach (var tech in visible)
				document.UpsertNode(new GraphNodeRecord(
										tech.id,
										career.unlocked.Contains(tech.id) ? tech.branch : LockedType(tech.branch),
										tech.title,
										new Dictionary<string, GraphValue>
										{
											["affordable"] = GraphValue.From(
												!career.unlocked.Contains(tech.id) && tech.cost <= career.xp &&
												tech.parents.All(career.unlocked.Contains))
										}));

			// Add a prerequisite edge from each visible parent to its child.
			foreach (var tech in visible)
				foreach (var parent in tech.parents.Where(visibleIds.Contains))
					document.UpsertEdge(new GraphEdgeRecord(parent + ">" + tech.id, parent, tech.id, "requires"));

			// Build the scene from the document and schema.
			var scene = new GraphScene(document, schema);
			foreach (var tech in visible)
			{
				// Look up the node that represents this technology.
				var node = scene.Nodes[tech.id];

				// Research branches occupy distinct depth bands, with individual nodes
				// staggered inside each band to avoid a single planar layer.
				var branchDepth = tech.branch == "RECON" ? -5f
					: tech.branch == "INTRUSION" ? 5f
					: tech.branch == "STEALTH" ? -2.5f
					: tech.branch == "CONTROL" ? 2.5f : 0f;
				var nodeDepth = branchDepth + (tech.position.y - 310f) / 100f;

				// Place the node on the tree plane and pin it there.
				node.Position = new Vector3((tech.position.x - 500) / 43f,
											(310 - tech.position.y) / 43f, nodeDepth);
				node.Pinned = true;
			}

			return scene;
		}

		// Locked research keeps its branch hue, muted toward the background so ownership still reads at a glance.
		private static string LockedType(string branch)
		{
			return branch + "-locked";
		}

		// Shared helper used by the workstation or graph viewport.
		private static Color LockedColor(Color branch)
		{
			return Color.Lerp(branch, new Color(.07f, .1f, .12f, 1f), .62f);
		}

		/// <summary>Advances rendering, progress rings, guide arrows, and flat backdrop animation for one frame.</summary>
		/// <param name="state">Live simulation for task progress and pause state.</param>
		/// <param name="effects">Display effects tier passed to GraphExplorer and the circuit backdrop.</param>
		public void Tick(GameSimulation state, SignalEffects effects)
		{
			// Stop when the viewport has been disposed.
			if (disposed)
				return;

			// Flat rigs only advance the circuit backdrop.
			if (flat)
			{
				circuit?.Tick(effects);
				return;
			}

			// Reposition overlays and sync the reduced-motion flag with the effects tier.
			waypointOverlay?.Reposition(textScale);
			session.Renderer.ReducedMotion = effects != SignalEffects.Full;

			// Track the node with an active task so its progress ring and card stay in step.
			activeProgressNodeId = state.Active && !research && state.Op.task != null && state.Op.task.duration > 0
				? state.Op.task.target
				: null;
			if (activeProgressNodeId == savedCamera.FocusedId)
				UpdateFocusedCard();

			// Show a progress ring on the active task's node, if any.
			if (state.Active && !research && state.Op.task != null && !string.IsNullOrEmpty(state.Op.task.target))
				session.Renderer.NodeProgress = new Dictionary<string, GraphNodeProgressVisual>
					{ [state.Op.task.target] = new() { Fraction = state.Progress } };
			else
				session.Renderer.NodeProgress = null;

			// Update the guide arrows and draw the frame.
			session.Renderer.GuideArrows = BuildGuideArrows();
			scene.Nodes.TryGetValue(selectedId, out var selected);
			session.Renderer.Draw(scene, edgeFilter, camera, selected, null, null);
		}

		// Cached state used while rendering or interacting with the view.
		private IReadOnlyList<GraphGuideArrow> BuildGuideArrows()
		{
			// Research maps have no guide arrows.
			if (research)
				return null;

			// Start an empty arrow list with room for two arrows.
			var arrows = new List<GraphGuideArrow>(2);

			// Add an arrow to the entry gateway and one to the objective when each exists.
			if (!string.IsNullOrEmpty(guideEntryId) && scene.Nodes.ContainsKey(guideEntryId))
				arrows.Add(new GraphGuideArrow { NodeId = guideEntryId, Color = Palette.Lime });
			if (!string.IsNullOrEmpty(guideObjectiveId) && scene.Nodes.ContainsKey(guideObjectiveId))
				arrows.Add(new GraphGuideArrow { NodeId = guideObjectiveId, Color = GuideObjectiveOrange });

			// Return null when no arrows were added.
			return arrows.Count == 0 ? null : arrows;
		}

		// Handles PositionLabels for the workstation or graph UI.
		private void PositionLabels()
		{
			// Skip layout until the panel has a size.
			if (disposed || contentRect.width <= 0 || contentRect.height <= 0)
				return;

			// Flat rigs hide the intel card and place each cell in panel space.
			if (flat)
			{
				focusedCard.style.display = DisplayStyle.None;
				foreach (var pair in nodeButtons)
				{
					// Measure the cell's world footprint and project its center and far corner.
					var node = scene.Nodes[pair.Key];
					var size = flatSizes.TryGetValue(pair.Key, out var dimensions) ? dimensions : Vector2.one * 3.8f;
					var point = camera.WorldToViewportPoint(node.Position);
					var edge = camera.WorldToViewportPoint(node.Position + new Vector3(size.x / 2, size.y / 2, 0));

					// Convert the footprint into panel pixels.
					float width = Mathf.Abs(edge.x - point.x) * 2 * contentRect.width,
						  height = Mathf.Abs(edge.y - point.y) * 2 * contentRect.height;

					// Show the cell when it is in front of the camera, then place and size it.
					pair.Value.style.display = point.z > 0 ? DisplayStyle.Flex : DisplayStyle.None;
					pair.Value.style.left = point.x * contentRect.width - width / 2;
					pair.Value.style.top = (1 - point.y) * contentRect.height - height / 2;
					pair.Value.style.width = width;
					pair.Value.style.height = height;
					pair.Value.style.whiteSpace = WhiteSpace.Normal;
				}

				// Repaint the link overlay for the new cell positions.
				links.MarkDirtyRepaint();
				return;
			}

			// Reposition the waypoint markers and clear the previous selection frame.
			waypointOverlay?.Reposition(textScale);
			links.SelectionVisible = false;
			var showFocusedCard = false;

			// Hide every button, then show the intel card for the selected node only.
			foreach (var pair in nodeButtons)
			{
				pair.Value.style.display = DisplayStyle.None;

				// Skip other nodes, and the selected node while its progress ring is shown.
				if (pair.Key != selectedId)
					continue;
				if (activeProgressNodeId == selectedId)
					continue;
				var node = scene.Nodes[pair.Key];

				// Skip the node when it is behind the camera or off screen.
				var point = camera.WorldToViewportPoint(node.Position);
				if (point.z <= camera.nearClipPlane || point.x < 0 || point.x > 1 || point.y < 0 ||
					point.y > 1)
					continue;

				// Project the model's bounding sphere into the same panel coordinates as its card.
				var radius = session.Renderer.Radius(node);
				var edge = camera.WorldToViewportPoint(node.Position + camera.transform.up * radius);
				var halfSize = Mathf.Max(10, Mathf.Abs(edge.y - point.y) * contentRect.height + 4);
				var nodeCenter = new Vector2(point.x * contentRect.width, (1 - point.y) * contentRect.height);
				var reticle = new Rect(nodeCenter.x - halfSize, nodeCenter.y - halfSize, halfSize * 2, halfSize * 2);

				// Measure the card and compare the room on each side of the reticle.
				var rightSpace = contentRect.width - reticle.xMax - 20;
				var leftSpace = reticle.xMin - 20;
				var focusedSize = MeasureFocusedCard(Mathf.Max(rightSpace, leftSpace));
				var width = focusedSize.x;
				var height = focusedSize.y;

				// Place the card on the roomier side, or on the left when the right is too tight.
				var beside = rightSpace >= leftSpace || width <= rightSpace + 1
					? reticle.xMax + 12
					: reticle.xMin - width - 12;

				// Clamp the card inside the panel and vertically centered on the node.
				var card = new Rect(Mathf.Clamp(beside, 8, Mathf.Max(8, contentRect.width - width - 8)),
									Mathf.Clamp(nodeCenter.y - height / 2, 8,
												Mathf.Max(8, contentRect.height - height - 8)), width, height);

				// Apply the card size and position, then show the selection frame.
				showFocusedCard = true;
				focusedCard.style.width = width;
				focusedCard.style.height = height;
				focusedCard.style.left = card.x;
				focusedCard.style.top = card.y;
				links.SelectionVisible = true;
				links.SelectionRect = reticle;
				links.CardRect = card;
			}

			// Show the card only when a selected node was placed, then repaint the links.
			focusedCard.style.display = showFocusedCard ? DisplayStyle.Flex : DisplayStyle.None;
			links.MarkDirtyRepaint();
		}

		// Internal member supporting workstation or graph viewport behavior.
		private Vector2 MeasureFocusedCard(float availableWidth)
		{
			// Measure explicit lines at the current font size before imposing viewport limits.
			// Grow sideways first; wrap and scroll only when the graph cannot contain the text.
			float NaturalWidth(Label label)
			{
				return GameplayRichText.Plain(label.text).Split('\n').Max(line =>
																			  label.MeasureTextSize(
																				  line, 0, MeasureMode.Undefined, 0,
																				  MeasureMode.Undefined).x);
			}

			// Size the card to the wider of its title and details, capped by the available space.
			var width = Mathf.Ceil(Mathf.Min(Mathf.Max(NaturalWidth(focusedTitle), NaturalWidth(focusedDetails)) + 24,
											 Mathf.Max(1, Mathf.Min(contentRect.width - 16, availableWidth))));

			// Leave room inside the card padding for the wrapped text.
			var textWidth = Mathf.Max(1, width - 22);

			// Measure the wrapped height of one label at the text width.
			float TextHeight(Label label)
			{
				return label.MeasureTextSize(GameplayRichText.Plain(label.text), textWidth, MeasureMode.Exactly,
											 0, MeasureMode.Undefined).y;
			}

			// Measure the card height and check whether it must scroll to fit the panel.
			var height = Mathf.Ceil(TextHeight(focusedTitle) + TextHeight(focusedDetails) + 22);
			var maxHeight = Mathf.Max(1, contentRect.height - 16);
			var scrolls = height > maxHeight;

			// Show the scroll bar only when the text is taller than the panel allows.
			focusedScroll.verticalScrollerVisibility =
				scrolls ? ScrollerVisibility.AlwaysVisible : ScrollerVisibility.Hidden;

			// Reserve room for the scroll bar when it is shown.
			if (scrolls)
				textWidth = Mathf.Max(1, textWidth - 18);

			// Size the scroll content to the wrapped width.
			focusedScroll.contentContainer.style.width = textWidth;

			// Return the card size, clamped to the panel height.
			return new Vector2(width, Mathf.Min(height, maxHeight));
		}

		/// <summary>Scales overlay label buttons for accessibility settings.</summary>
		/// <param name="scale">Multiplier applied to default label dimensions.</param>
		public void SetTextScale(float scale)
		{
			// Store the scale for overlay drawing.
			textScale = scale;

			// Scale each node button and let its text wrap.
			foreach (var button in nodeButtons.Values)
			{
				button.style.width = Mathf.Min((research ? 118 : 104) * scale, Mathf.Max(1, contentRect.width));
				button.style.height = 26 * scale;
				button.style.whiteSpace = WhiteSpace.Normal;
			}

			// Reposition the buttons at the new size.
			PositionLabels();
		}

		// Produces the PickNode text shown in the UI.
		private string PickNode(Vector2 local)
		{
			// No node can be hit before the panel has a size.
			if (contentRect.width <= 0 || contentRect.height <= 0)
				return null;

			// Convert the panel point to texture pixels and pick the node there.
			return session.Renderer.Pick(scene, camera, new Vector2(local.x / contentRect.width * texture.width,
																	(1 - local.y / contentRect.height) *
																	texture.height))?.Id;
		}

		/// <summary>Multiplies orbit distance by the given factor within clamp limits.</summary>
		/// <param name="factor">Zoom multiplier.</param>
		public void Zoom(float factor)
		{
			// Mark the view as user-adjusted and clamp the orbit distance.
			savedCamera.UserAdjusted = true;
			distance = Mathf.Clamp(distance * factor, 4, 180);

			// Move the camera and labels.
			PlaceCamera();
			PositionLabels();
		}

		/// <summary>Fits the camera to all node positions (and waypoint anchors) and clears focus.</summary>
		public void ResetView()
		{
			// Clear the user adjustment and the focused card.
			savedCamera.UserAdjusted = false;
			savedCamera.FocusedId = null;

			// Nothing to fit in an empty scene.
			if (scene.Nodes.Count == 0)
				return;

			// Collect every node and waypoint position to frame.
			var positions = scene.Nodes.Values.Select(n => n.Position)
								 .Concat(waypointOverlay?.Anchors ?? Enumerable.Empty<Vector3>()).ToArray();

			// Build bounds around every position and center the camera on them.
			var bounds = new Bounds(positions[0], Vector3.one * 3);
			foreach (var position in positions)
				bounds.Encapsulate(new Bounds(position, Vector3.one * 3));
			center = bounds.center;

			// Flat rigs fit by the larger of the height or the width at the panel aspect.
			if (flat)
			{
				distance = Mathf.Max(bounds.size.y, bounds.size.x / camera.aspect) * 1.2f;
				PlaceCamera();
				PositionLabels();
				return;
			}

			// Find the orbit distance at which every point fits in view.
			var rotation = Quaternion.Inverse(Quaternion.Euler(pitch, yaw, 0));
			var tangent = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f);
			distance = 4;
			foreach (var position in positions)
			{
				var point = rotation * (position - center);
				distance = Mathf.Max(distance, Mathf.Max((Mathf.Abs(point.y) + 2) / tangent,
														 (Mathf.Abs(point.x) + 2) / (tangent * camera.aspect)) -
											   point.z);
			}

			// Pad the fit a little more for larger graphs.
			distance *= !research && scene.Nodes.Count < 20 ? 1.0f : 1.12f;

			// Apply the new pose and reposition the labels.
			PlaceCamera();
			PositionLabels();
		}

		// Handles PlaceCamera for the workstation or graph UI.
		private void PlaceCamera()
		{
			// Flat rigs look straight down the z axis with an orthographic size.
			if (flat)
			{
				pitch = yaw = 0;
				center.z = 0;
				camera.orthographicSize = distance / 2;
			}

			// Build the orbit rotation from pitch and yaw.
			var orientation = Quaternion.Euler(pitch, yaw, 0);

			// Apply the rotation and place the camera behind the center at the orbit distance.
			camera.transform.rotation = orientation;
			camera.transform.position = center + orientation * new Vector3(0, 0, -distance);
		}

		// Handles DestroyOwned for the workstation or graph UI.
		private static void DestroyOwned(Object obj)
		{
			// Destroy at runtime, or immediately in edit mode.
			if (Application.isPlaying)
				Object.Destroy(obj);
			else
				Object.DestroyImmediate(obj);
		}

		private sealed class AllEdges : IGraphEdgeFilter
		{
			public bool EdgeMatches(IGraphEdge edge)
			{
				return true;
			}
		}

		private sealed class LinkOverlay : VisualElement
		{
			// Cached state used while rendering or interacting with the view.
			private readonly Camera camera;

			// Cached state used while rendering or interacting with the view.
			private readonly HashSet<string> controlledNodes;

			// Returns whether flat for the current UI or rig state.
			private readonly bool flat;

			// Cached state used while rendering or interacting with the view.
			private readonly HashSet<string> path;

			// Cached state used while rendering or interacting with the view.
			private readonly HashSet<string> reachableNodes;

			// Returns whether research for the current UI or rig state.
			private readonly bool research;

			// Cached state used while rendering or interacting with the view.
			private readonly GraphScene scene;

			// Produces the selected text shown in the UI.
			private readonly string selected;
			public Rect SelectionRect, CardRect;
			public bool SelectionVisible;

			public LinkOverlay(GraphScene scene, Camera camera, string selected, GraphPathResult route, bool research,
							   bool flat = false,
							   HashSet<string> controlledNodes = null, HashSet<string> reachableNodes = null)
			{
				// Store the scene, camera, selection, and display mode.
				this.scene = scene;
				this.camera = camera;
				this.selected = selected;
				this.research = research;
				this.flat = flat;

				// Default the node sets to empty when they are not supplied.
				this.controlledNodes = controlledNodes ?? new HashSet<string>();
				this.reachableNodes = reachableNodes ?? new HashSet<string>();

				// Index both directions of each route edge so either way matches.
				path = new HashSet<string>(
					(route?.EdgeIds ?? Array.Empty<string>()).SelectMany(id => new[]
					{
						id, string.Join(">", id.Split('>').Reverse())
					}));

				// Cover the viewport without intercepting input.
				style.position = Position.Absolute;
				style.left = style.right = style.top = style.bottom = 0;
				pickingMode = PickingMode.Ignore;

				// Draw the overlay on each repaint.
				generateVisualContent += Draw;
			}

			// Handles Draw for the workstation or graph UI.
			private void Draw(MeshGenerationContext context)
			{
				// Overlays are skipped in flat mode and when the camera is gone.
				if (camera == null || flat)
					return;
				var painter = context.painter2D;

				// Draw each edge, colored and weighted by its state.
				foreach (var edge in scene.Edges.Values)
				{
					// Project both endpoints into screen space.
					var a = scene.Nodes[edge.From];
					var b = scene.Nodes[edge.To];
					var start = camera.WorldToScreenPoint(a.Position);
					var end = camera.WorldToScreenPoint(b.Position);

					// Skip edges with an endpoint behind the camera.
					if (start.z <= 0 || end.z <= 0)
						continue;

					// Work out the route, selection, and hacked states for this edge.
					var highlight = path.Contains(edge.Id);
					var related = edge.From == selected || edge.To == selected;
					var hacked = !research && controlledNodes.Contains(edge.From) && controlledNodes.Contains(edge.To);

					// Draw a wide translucent underlay under hacked connections.
					if (hacked)
					{
						var hackedColor = WorkstationApp.HackedConnectionColor;
						hackedColor.a = .28f;
						painter.strokeColor = hackedColor;
						painter.lineWidth = 7f;
						painter.BeginPath();
						painter.MoveTo(new Vector2(start.x / camera.pixelWidth * contentRect.width,
												   (1 - start.y / camera.pixelHeight) * contentRect.height));
						painter.LineTo(new Vector2(end.x / camera.pixelWidth * contentRect.width,
												   (1 - end.y / camera.pixelHeight) * contentRect.height));
						painter.Stroke();
					}

					// Pick the edge tint: hacked color, research branch color, or the default cyan.
					var tint = research ? scene.Schema.NodeTypes[b.Record.TypeId].Color : Palette.Cyan;
					if (hacked)
						tint = WorkstationApp.HackedConnectionColor;
					tint.a = hacked ? highlight ? 1f : related ? .98f : .9f :
						highlight ? .9f :
						related ? .65f :
						research ? .38f : .19f;

					// Pick the stroke width from the same state.
					painter.strokeColor = tint;
					painter.lineWidth = hacked ? highlight ? 3.2f : related ? 2.8f : 2.4f :
						highlight ? 1.15f :
						related ? .9f : .65f;

					// Stroke the edge itself.
					painter.BeginPath();
					painter.MoveTo(new Vector2(start.x / camera.pixelWidth * contentRect.width,
											   (1 - start.y / camera.pixelHeight) * contentRect.height));
					painter.LineTo(new Vector2(end.x / camera.pixelWidth * contentRect.width,
											   (1 - end.y / camera.pixelHeight) * contentRect.height));
					painter.Stroke();
				}

				// Only discovered systems adjacent to the controlled network get a quiet
				// targeting bracket. It guides the next move without exposing hidden nodes.
				painter.strokeColor = new Color(.12f, .94f, .85f, .52f);
				painter.lineWidth = 1.1f;
				foreach (var id in reachableNodes)
				{
					// Skip the selected node and ids that are not in the scene.
					if (id == selected || !scene.Nodes.TryGetValue(id, out var node))
						continue;

					// Skip bracket targets that are behind the camera or off screen.
					var point = camera.WorldToViewportPoint(node.Position);
					if (point.z <= 0 || point.x < 0 || point.x > 1 || point.y < 0 || point.y > 1)
						continue;

					// Center the bracket on the node's panel position.
					float x = point.x * contentRect.width, y = (1 - point.y) * contentRect.height;

					// Draw four L-shaped corner brackets around the node.
					const float outer = 16f, inner = 10f;
					painter.BeginPath();
					painter.MoveTo(new Vector2(x - outer, y - inner));
					painter.LineTo(new Vector2(x - outer, y - outer));
					painter.LineTo(new Vector2(x - inner, y - outer));
					painter.MoveTo(new Vector2(x + inner, y - outer));
					painter.LineTo(new Vector2(x + outer, y - outer));
					painter.LineTo(new Vector2(x + outer, y - inner));
					painter.MoveTo(new Vector2(x - outer, y + inner));
					painter.LineTo(new Vector2(x - outer, y + outer));
					painter.LineTo(new Vector2(x - inner, y + outer));
					painter.MoveTo(new Vector2(x + inner, y + outer));
					painter.LineTo(new Vector2(x + outer, y + outer));
					painter.LineTo(new Vector2(x + outer, y + inner));
					painter.Stroke();
				}

				// Draw the selection frame only when a selected node is on screen.
				if (!SelectionVisible)
					return;
				painter.strokeColor = Palette.Lime;
				painter.lineWidth = 1.5f;

				// Outline the selection rectangle around the node.
				var corners = Corners(SelectionRect);
				painter.BeginPath();
				painter.MoveTo(corners[0]);
				for (var i = 1; i < corners.Length; i++)
					painter.LineTo(corners[i]);
				painter.ClosePath();
				painter.Stroke();

				// Choose the two facing corners on each rectangle, then pair without crossing.
				var cardCorners = Corners(CardRect).OrderBy(p => (p - SelectionRect.center).sqrMagnitude).Take(2)
												   .ToArray();
				var nodeCorners = corners.OrderBy(p => (p - CardRect.center).sqrMagnitude).Take(2).ToArray();
				var direct = (cardCorners[0] - nodeCorners[0]).sqrMagnitude +
							 (cardCorners[1] - nodeCorners[1]).sqrMagnitude;
				var crossed = (cardCorners[0] - nodeCorners[1]).sqrMagnitude +
							  (cardCorners[1] - nodeCorners[0]).sqrMagnitude;

				// Draw the two connector lines from the card corners to the node corners.
				for (var i = 0; i < 2; i++)
				{
					painter.BeginPath();
					painter.MoveTo(cardCorners[i]);
					painter.LineTo(nodeCorners[crossed < direct ? 1 - i : i]);
					painter.Stroke();
				}
			}

			// Shared helper used by the workstation or graph viewport.
			private static Vector2[] Corners(Rect rect)
			{
				return new[]
				{
					new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin),
					new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax)
				};
			}
		}
	}
}