using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace HackThePlanet
{
	/// <summary>Shared workstation accent colors for nodes, research branches, and UI chrome.</summary>
	[NoAutoStaticsCleanup]
	public static class Palette
	{
		/// <summary>Theme accent colors (lime, cyan, amber, red, violet) used across workstation UI.</summary>
		public static Color Lime = Hex("9BFACB"),
							Cyan = Hex("65D9EB"),
							Amber = Hex("FFBC70"),
							Red = Hex("FF786D"),
							Violet = Hex("BAA1ED");

		/// <summary>Parses a six-digit RGB hex string into a <see cref="Color" />.</summary>
		/// <param name="hex">Hex digits without a leading #.</param>
		/// <returns>The parsed color, or default when parsing fails.</returns>
		public static Color Hex(string hex)
		{
			// Parse the hex digits; invalid input leaves the default color.
			ColorUtility.TryParseHtmlString("#" + hex, out var c);

			return c;
		}

		/// <summary>Gets the default map accent for a <see cref="NodeKind" />.</summary>
		/// <param name="kind">Node role glyph.</param>
		/// <returns>Theme color for that kind.</returns>
		public static Color Kind(NodeKind kind)
		{
			// Map each node role to its accent color.
			switch (kind)
			{
				case NodeKind.Gateway:     return Lime;
				case NodeKind.Relay:       return Cyan;
				case NodeKind.Firewall:    return Amber;
				case NodeKind.Scanner:     return Red;
				case NodeKind.SecurityHub: return Violet;
				case NodeKind.Database:    return Hex("D9D0B1");
				default:                   return Hex("92A6B7");
			}
		}

		/// <summary>Gets the research branch accent color for tree and module UI.</summary>
		/// <param name="branch">Branch code such as RECON or STEALTH.</param>
		/// <returns>Branch theme color.</returns>
		public static Color Branch(string branch)
		{
			return branch == "RECON" ? Cyan
				: branch == "INTRUSION" ? Amber
				: branch == "STEALTH" ? Violet
				: branch == "CONTROL" ? Red
				: branch == "CROSSOVER" ? Hex("F59BD0")
				: branch == "CORE" ? Hex("DCEAE4") : Lime;
		}
	}

	// Small vector glyphs remain crisp at every workstation scale.
	/// <summary>Vector-drawn node icon for legacy 2D network buttons.</summary>
	public sealed class DeviceGlyph : VisualElement
	{
		// Node role that selects which vector artwork to draw.
		private readonly NodeKind kind;

		// Stroke and fill accent for the device icon.
		private readonly Color tint;

		/// <summary>Creates a non-interactive glyph for the given node kind and tint.</summary>
		/// <param name="kind">Node role to draw.</param>
		/// <param name="tint">Stroke and fill accent.</param>
		/// <param name="size">Square layout size in pixels.</param>
		public DeviceGlyph(NodeKind kind, Color tint, float size = 28)
		{
			// Store the node role and tint.
			this.kind = kind;
			this.tint = tint;

			// Fix the square size and keep it from shrinking.
			style.width = size;
			style.height = size;
			style.flexShrink = 0;

			// Let clicks pass through and draw on each repaint.
			pickingMode = PickingMode.Ignore;
			generateVisualContent += Draw;
		}

		// Draws the vector icon for the bound node kind inside the glyph bounds.
		private void Draw(MeshGenerationContext context)
		{
			// Set the stroke color and width for the glyph.
			var p = context.painter2D;
			p.strokeColor = tint;
			p.lineWidth = 1.5f;
			float w = contentRect.width, h = contentRect.height;

			// Convert unit coordinates to panel pixels.
			Vector2 P(float x, float y)
			{
				return new Vector2(x * w, y * h);
			}

			// Stroke a single line between two unit points.
			void Line(float x, float y, float a, float b)
			{
				p.BeginPath();
				p.MoveTo(P(x, y));
				p.LineTo(P(a, b));
				p.Stroke();
			}

			// Stroke a closed rectangle between two unit corners.
			void Box(float x, float y, float a, float b)
			{
				p.BeginPath();
				p.MoveTo(P(x, y));
				p.LineTo(P(a, y));
				p.LineTo(P(a, b));
				p.LineTo(P(x, b));
				p.ClosePath();
				p.Stroke();
			}

			// Draw the glyph shape for each node role.
			switch (kind)
			{
				// Workstation: boxed monitor with a stand and keyboard lines.
				case NodeKind.Workstation:
					Box(.15f, .18f, .85f, .66f);
					Line(.5f, .66f, .5f, .83f);
					Line(.3f, .83f, .7f, .83f);
					Line(.28f, .34f, .44f, .43f);
					Line(.44f, .43f, .28f, .53f);
					Line(.5f, .53f, .68f, .53f);
					break;

				// Relay: ring with four spokes and a filled hub.
				case NodeKind.Relay:
					p.BeginPath();
					p.Arc(P(.5f, .5f), w * .26f, 0, 360);
					p.Stroke();
					Line(.5f, .06f, .5f, .25f);
					Line(.5f, .75f, .5f, .94f);
					Line(.06f, .5f, .25f, .5f);
					Line(.75f, .5f, .94f, .5f);
					p.BeginPath();
					p.fillColor = tint;
					p.Arc(P(.5f, .5f), w * .07f, 0, 360);
					p.Fill();
					break;

				// Firewall: brick wall outline with mortar lines.
				case NodeKind.Firewall:
					Box(.14f, .15f, .86f, .85f);
					Line(.14f, .38f, .86f, .38f);
					Line(.14f, .62f, .86f, .62f);
					Line(.4f, .15f, .4f, .38f);
					Line(.66f, .38f, .66f, .62f);
					Line(.4f, .62f, .4f, .85f);
					break;

				// Scanner: two concentric rings with a sweep line.
				case NodeKind.Scanner:
					p.BeginPath();
					p.Arc(P(.5f, .5f), w * .36f, 0, 360);
					p.Stroke();
					p.BeginPath();
					p.Arc(P(.5f, .5f), w * .2f, 0, 360);
					p.Stroke();
					Line(.5f, .5f, .8f, .18f);
					break;

				// Security hub: shield outline with a check mark.
				case NodeKind.SecurityHub:
					p.BeginPath();
					p.MoveTo(P(.5f, .08f));
					p.LineTo(P(.86f, .24f));
					p.LineTo(P(.8f, .66f));
					p.LineTo(P(.5f, .92f));
					p.LineTo(P(.2f, .66f));
					p.LineTo(P(.14f, .24f));
					p.ClosePath();
					p.Stroke();
					Line(.5f, .3f, .5f, .58f);
					Line(.5f, .7f, .5f, .72f);
					break;

				// Database: stacked cylinder bands drawn as boxed rows.
				case NodeKind.Database:
					Box(.2f, .12f, .8f, .88f);
					Line(.2f, .35f, .8f, .35f);
					Line(.2f, .58f, .8f, .58f);
					Line(.32f, .24f, .4f, .24f);
					Line(.32f, .46f, .4f, .46f);
					Line(.32f, .74f, .4f, .74f);
					break;

				// Default: globe with meridian and latitude lines.
				default:
					p.BeginPath();
					p.Arc(P(.5f, .5f), w * .38f, 0, 360);
					p.Stroke();
					p.BeginPath();
					p.MoveTo(P(.5f, .12f));
					p.BezierCurveTo(P(.12f, .3f), P(.12f, .7f), P(.5f, .88f));
					p.BezierCurveTo(P(.88f, .7f), P(.88f, .3f), P(.5f, .12f));
					p.Stroke();
					Line(.12f, .5f, .88f, .5f);
					Line(.18f, .32f, .82f, .32f);
					Line(.18f, .68f, .82f, .68f);
					break;
			}
		}
	}

	/// <summary>Pan and zoom state shared by a <see cref="NetworkCanvas" /> viewport.</summary>
	public sealed class CanvasCamera
	{
		/// <summary>Panel-space pan offset applied after fit scaling.</summary>
		public Vector2 pan;

		/// <summary>User zoom multiplier clamped by the canvas wheel handler.</summary>
		public float zoom = 1;
	}

	/// <summary>Legacy 2D pannable canvas for network nodes or the flat research tree.</summary>
	public sealed class NetworkCanvas : VisualElement
	{
		// Shared pan and zoom state applied to the graph plane transform.
		private readonly CanvasCamera camera;

		// Overlay that paints links, route highlights, and signal animation.
		private readonly EdgeLayer edges;

		// Transformed container holding node buttons and the edge layer.
		private readonly VisualElement plane;

		// Fixed layout height of the graph plane in panel units.
		private readonly float planeHeight;

		// Pointer position and pan offset captured at the start of a drag.
		private Vector2 dragStart, originalPan;

		// Whether the user is currently panning the canvas.
		private bool dragging;

		// Scale factor that fits the plane into the viewport before user zoom.
		private float fit = 1;

		/// <summary>Builds a live or preview network map with selectable node buttons and edge overlay.</summary>
		/// <param name="camera">Shared pan/zoom state.</param>
		/// <param name="nodes">Nodes to render; unknown nodes are omitted unless preview is true.</param>
		/// <param name="links">Topology for the edge layer.</param>
		/// <param name="selected">Currently selected node id.</param>
		/// <param name="preview">When true, shows the full template without fog-of-war.</param>
		/// <param name="select">Callback invoked when a node button is pressed.</param>
		/// <param name="route">Optional node ids highlighting a planned path on edges.</param>
		public NetworkCanvas(CanvasCamera camera, List<NetNode> nodes, List<NetLink> links, string selected,
							 bool preview, Action<string> select, IEnumerable<string> route = null)
		{
			// Store the shared camera and the fixed plane height for this map.
			this.camera = camera;
			planeHeight = 460;

			// Build the scrollable plane that holds the nodes and edges.
			AddToClassList("graph-viewport");
			plane = new VisualElement { name = "graph-plane" };
			plane.AddToClassList("graph-plane");
			plane.style.height = planeHeight;
			Add(plane);

			// Add the edge layer under the node buttons.
			edges = new EdgeLayer(nodes, links, selected, preview, route);
			plane.Add(edges);

			// Keep only the nodes the player can see, unless previewing.
			var known = nodes.Where(n => preview || n.known).ToList();

			// Build one button per visible node.
			foreach (var node in known)
			{
				// Create the button and its tooltip.
				var button = new Button(() => select(node.id))
				{
					name = "node-" + node.id,
					tooltip = node.label + " / " + node.kind + (node.objective ? " / OBJECTIVE" : "")
				};

				// Mark the node state with CSS classes.
				button.AddToClassList("net-node");
				if (node.id == selected)
					button.AddToClassList("selected");
				if (node.controlled && !preview)
					button.AddToClassList("controlled");
				if (node.objective)
					button.AddToClassList("objective");
				if (node.collected)
					button.AddToClassList("collected");

				// Tint the border and add the corner frame.
				var tint = node.objective ? Palette.Amber : Palette.Kind(node.kind);
				button.style.borderLeftColor = tint;
				button.Add(new NodeFrame(tint, node.id == selected, node.objective));

				// Position the button on the map.
				button.style.left = node.x - 58;
				button.style.top = node.y - 32;

				// Build the top row with the device glyph, code, and mark.
				var top = new VisualElement();
				top.AddToClassList("node-top");
				top.Add(new DeviceGlyph(node.kind, tint, 21));
				var code = new Label(node.kind == NodeKind.Gateway
										 ? "LOCAL / 00"
										 : "SEC " + node.security.ToString("00"));
				code.AddToClassList("node-code");
				top.Add(code);
				var mark = new Label(node.collected ? "+" :
									 node.objective ? "*" :
									 !preview && node.controlled ? "+" : "");
				mark.AddToClassList("node-mark");
				top.Add(mark);
				button.Add(top);

				// Add the node title.
				var title = new Label(node.label);
				title.AddToClassList("node-title");
				button.Add(title);

				// Flag suppressed nodes.
				if (node.suppressed > 0)
					button.AddToClassList("suppressed");

				// Add the readout line, then attach the button to the plane.
				var readout = new Label(node.collected ? "FILE VERIFIED" :
										!preview && node.controlled ? "ROOT ACCESS" :
										node.objective ? "CONTRACT TARGET" : "ADDR / " + node.id.ToUpperInvariant());
				readout.AddToClassList("node-readout");
				button.Add(readout);
				plane.Add(button);
			}

			// Refit on layout changes and zoom with the wheel.
			RegisterCallback<GeometryChangedEvent>(_ => Fit());
			RegisterCallback<WheelEvent>(evt =>
			{
				// Zoom in or out, then reapply the transform.
				camera.zoom = Mathf.Clamp(camera.zoom * (evt.delta.y > 0 ? .9f : 1.1f), .65f, 2.3f);
				Apply();
				evt.StopPropagation();
			});

			// Start a pan when the press lands on empty plane, not a button.
			RegisterCallback<PointerDownEvent>(evt =>
			{
				if (evt.button != 0 || evt.target is Button ||
					(evt.target as VisualElement)?.GetFirstAncestorOfType<Button>() != null)
					return;
				dragging = true;
				dragStart = evt.position;
				originalPan = camera.pan;
				this.CapturePointer(evt.pointerId);
			});

			// Move the plane with the pointer while dragging.
			RegisterCallback<PointerMoveEvent>(evt =>
			{
				if (!dragging)
					return;
				camera.pan = originalPan + (new Vector2(evt.position.x, evt.position.y) - dragStart);
				Apply();
			});

			// End the pan when the pointer is released.
			RegisterCallback<PointerUpEvent>(evt =>
			{
				dragging = false;
				this.ReleasePointer(evt.pointerId);
			});
		}

		/// <summary>Builds the research technology tree canvas for a career.</summary>
		/// <param name="camera">Shared pan/zoom state.</param>
		/// <param name="career">Unlock and XP state for node styling.</param>
		/// <param name="selected">Selected technology id.</param>
		/// <param name="select">Callback invoked when a tech node is pressed.</param>
		public NetworkCanvas(CanvasCamera camera, Career career, string selected, Action<string> select)
		{
			// Store the shared camera and the fixed plane height for the tree.
			this.camera = camera;
			planeHeight = 640;

			// Build the scrollable plane that holds the tech nodes and edges.
			AddToClassList("graph-viewport");
			plane = new VisualElement();
			plane.AddToClassList("graph-plane");
			Add(plane);

			// Add the edge layer under the tech buttons.
			edges = new EdgeLayer(career, selected);
			plane.Add(edges);

			// Build one button per technology.
			foreach (var tech in GameContent.Tech)
			{
				// Work out ownership and whether the parents are unlocked.
				bool owned = career.unlocked.Contains(tech.id), available = tech.parents.All(career.unlocked.Contains);

				// Create the button and its tooltip.
				var button = new Button(() => select(tech.id)) { name = "tech-" + tech.id, tooltip = tech.description };

				// Mark the tech state with CSS classes.
				button.AddToClassList("tech-node");
				if (owned)
					button.AddToClassList("owned");
				if (!available)
					button.AddToClassList("locked");
				if (tech.id == selected)
					button.AddToClassList("selected");

				// Position the button and tint it by branch.
				button.style.left = tech.position.x - 73;
				button.style.top = tech.position.y - 40;
				button.style.borderTopColor = Palette.Branch(tech.branch);
				button.Add(new NodeFrame(Palette.Branch(tech.branch), tech.id == selected, false));

				// Add the branch label in the branch color.
				var meta = new Label(tech.branch + (tech.special ? " / ABILITY" : ""));
				meta.AddToClassList("tech-meta");
				meta.style.color = Palette.Branch(tech.branch);
				button.Add(meta);

				// Add the technology title.
				var title = new Label(tech.title);
				title.AddToClassList("tech-title");
				button.Add(title);

				// Add the price line: equipped or unlocked state, otherwise the XP cost.
				var price = new Label(owned
										  ? career.equipped.Contains(tech.id) ? "EQUIPPED" : "UNLOCKED"
										  : tech.cost + " XP" + (tech.parents.Length > 1 ? " / BOTH PARENTS" : ""));
				price.AddToClassList("tech-price");
				button.Add(price);

				// Attach the finished button to the plane.
				plane.Add(button);
			}

			// Refit on layout changes and zoom with the wheel.
			RegisterCallback<GeometryChangedEvent>(_ => Fit());
			RegisterCallback<WheelEvent>(evt =>
			{
				// Zoom in or out, then reapply the transform.
				camera.zoom = Mathf.Clamp(camera.zoom * (evt.delta.y > 0 ? .9f : 1.1f), .65f, 2.3f);
				Apply();
				evt.StopPropagation();
			});

			// Start a pan when the press lands on empty plane, not a button.
			RegisterCallback<PointerDownEvent>(evt =>
			{
				if (evt.button != 0 || evt.target is Button ||
					(evt.target as VisualElement)?.GetFirstAncestorOfType<Button>() != null)
					return;
				dragging = true;
				dragStart = evt.position;
				originalPan = camera.pan;
				this.CapturePointer(evt.pointerId);
			});

			// Move the plane with the pointer while dragging.
			RegisterCallback<PointerMoveEvent>(evt =>
			{
				if (dragging)
				{
					camera.pan = originalPan + (new Vector2(evt.position.x, evt.position.y) - dragStart);
					Apply();
				}
			});

			// End the pan when the pointer is released.
			RegisterCallback<PointerUpEvent>(evt =>
			{
				dragging = false;
				this.ReleasePointer(evt.pointerId);
			});
		}

		/// <summary>Resets pan and zoom and refits the graph plane to the panel.</summary>
		public void ResetView()
		{
			// Clear the pan and zoom, then refit the plane.
			camera.pan = Vector2.zero;
			camera.zoom = 1;
			Fit();
		}

		/// <summary>Multiplies zoom by the given factor within canvas limits.</summary>
		/// <param name="amount">Zoom multiplier applied to <see cref="CanvasCamera.zoom" />.</param>
		public void Zoom(float amount)
		{
			// Clamp the zoom to canvas limits, then reapply the transform.
			camera.zoom = Mathf.Clamp(camera.zoom * amount, .65f, 2.3f);
			Apply();
		}

		/// <summary>Updates animated edge and scanner overlays from simulation progress and FX mode.</summary>
		/// <param name="state">Active simulation supplying task progress.</param>
		/// <param name="effects">Display effects tier controlling animation.</param>
		public void UpdateSignal(GameSimulation state, SignalEffects effects)
		{
			edges.UpdateSignal(state, effects);
		}

		// Recomputes the base fit scale when the canvas layout size changes.
		private void Fit()
		{
			// Fit the plane inside the panel while keeping its aspect ratio.
			fit = Mathf.Min(contentRect.width / 1000f, contentRect.height / planeHeight);

			// Reapply the transform with the new fit.
			Apply();
		}

		// Applies fit, zoom, centering, and pan to the graph plane transform.
		private void Apply()
		{
			// Combine the fit and the user zoom into one scale factor.
			var scale = fit * camera.zoom;

			// Scale from the top-left origin and center the plane, offset by the pan.
			plane.style.transformOrigin = new TransformOrigin(0, 0, 0);
			plane.style.scale = new Scale(new Vector3(scale, scale, 1));
			plane.style.translate = new Translate((contentRect.width - 1000 * scale) / 2 + camera.pan.x,
												  (contentRect.height - planeHeight * scale) / 2 + camera.pan.y);
		}

		// Corner bracket decoration drawn over network and research node buttons.
		private sealed class NodeFrame : VisualElement
		{
			// Whether the parent node is selected or marked as a contract objective.
			private readonly bool selected, objective;

			// Accent color for brackets and the objective slash.
			private readonly Color tint;

			public NodeFrame(Color tint, bool selected, bool objective)
			{
				// Store the frame state.
				this.tint = tint;
				this.selected = selected;
				this.objective = objective;

				// Cover the button without intercepting input.
				pickingMode = PickingMode.Ignore;
				style.position = Position.Absolute;
				style.left = style.top = style.right = style.bottom = 0;

				// Draw the frame on each repaint.
				generateVisualContent += Draw;
			}

			// Paints corner brackets, edge ticks, and an objective slash on the node button.
			private void Draw(MeshGenerationContext context)
			{
				// Set the frame color and width; selected frames are brighter.
				var p = context.painter2D;
				float w = contentRect.width, h = contentRect.height;
				var color = tint;
				color.a = selected ? .95f : .42f;
				p.strokeColor = color;
				p.lineWidth = selected ? 1.5f : 1;

				// Draw the four corner brackets.
				foreach (var corner in new[]
						 {
							 new Vector2(3, 3), new Vector2(w - 3, 3), new Vector2(3, h - 3), new Vector2(w - 3, h - 3)
						 })
				{
					// Point each bracket inward from its corner.
					float dx = corner.x < w / 2 ? 1 : -1, dy = corner.y < h / 2 ? 1 : -1;

					// Draw the L-shaped bracket.
					p.BeginPath();
					p.MoveTo(corner + new Vector2(0, 7 * dy));
					p.LineTo(corner);
					p.LineTo(corner + new Vector2(9 * dx, 0));
					p.Stroke();
				}

				// Draw the tick marks along the bottom edge in a dimmer color.
				color.a = selected ? .48f : .15f;
				p.strokeColor = color;
				p.lineWidth = 1;
				for (var x = 0; x < 5; x++)
				{
					// Place each tick and draw it.
					var a = w - 29 + x * 3;
					p.BeginPath();
					p.MoveTo(new Vector2(a, h - 12));
					p.LineTo(new Vector2(a, h - 7 - x % 3 * 2));
					p.Stroke();
				}

				// Draw the objective slash in the top-right corner.
				if (objective)
				{
					p.strokeColor = tint;
					p.BeginPath();
					p.MoveTo(new Vector2(w - 16, 0));
					p.LineTo(new Vector2(w, 16));
					p.Stroke();
				}
			}
		}

		// Link and research-edge overlay with optional route and task signal animation.
		private sealed class EdgeLayer : VisualElement
		{
			// Career state when rendering the research tree edges.
			private readonly Career career;

			// Network links to draw between node positions.
			private readonly List<NetLink> links;

			// Node positions keyed by id for edge endpoint lookup.
			private readonly Dictionary<string, NetNode> lookup;

			// Network nodes supplying layout coordinates for edges.
			private readonly List<NetNode> nodes;

			// When true, edges ignore fog-of-war visibility rules.
			private readonly bool preview;

			// Endpoint pairs that belong to the highlighted route preview.
			private readonly HashSet<string> route;

			// Currently selected node or technology id for edge styling.
			private readonly string selected;

			// Whether signal animation is enabled for the current FX tier.
			private bool animate;

			// Animation clock and in-progress task fraction for edge pulses.
			private float clock, progress;

			// Next time the edge layer may repaint during animation.
			private float nextRepaint;

			// Node id receiving the active tool task for animated edges.
			private string workingTarget;

			public EdgeLayer(List<NetNode> nodes, List<NetLink> links, string selected, bool preview,
							 IEnumerable<string> route)
			{
				// Store the map data, selection, route set, and a lookup by node id.
				this.nodes = nodes;
				this.links = links;
				this.selected = selected;
				this.preview = preview;
				this.route = new HashSet<string>(route ?? Array.Empty<string>());
				lookup = nodes.ToDictionary(n => n.id);

				// Run the shared setup.
				Init();
			}

			public EdgeLayer(Career career, string selected)
			{
				// Store the research state and selection.
				this.career = career;
				this.selected = selected;

				// Run the shared setup.
				Init();
			}

			// Sizes the edge layer, ignores input, and hooks the paint callback.
			private void Init()
			{
				// Cover the plane at its fixed size without intercepting input.
				style.position = Position.Absolute;
				style.width = 1000;
				style.height = career == null ? 460 : 640;
				pickingMode = PickingMode.Ignore;

				// Draw the edges and overlays on each repaint.
				generateVisualContent += Draw;
			}

			public void UpdateSignal(GameSimulation state, SignalEffects effects)
			{
				// Work out the animation flag, the working target, and its progress for this frame.
				var newAnimate = effects == SignalEffects.Full && (career != null || !state.Active || !state.Op.paused);
				var target = career == null && state.Active ? state.Op.task?.target : null;
				var newProgress = target != null ? state.Progress : 0;
				var changed = target != workingTarget || !Mathf.Approximately(newProgress, progress) ||
							  animate != newAnimate;

				// Store the new values.
				workingTarget = target;
				progress = newProgress;
				animate = newAnimate;

				// Pick the clock: wall time in research or idle, elapsed task time otherwise.
				clock = effects == SignalEffects.Full
					? career != null || !state.Active ? Time.unscaledTime : state.Op.elapsed
					: 3.5f;

				// Repaint on change, or at 24 fps while animating.
				if (changed || (animate && Time.unscaledTime >= nextRepaint))
				{
					nextRepaint = Time.unscaledTime + 1f / 24f;
					MarkDirtyRepaint();
				}
			}

			// Paints grid marks, links, research edges, selection brackets, and scanner rings.
			private void Draw(MeshGenerationContext context)
			{
				// Set up the canvas and the plane height for this layout.
				var p = context.painter2D;
				float height = career == null ? 460 : 640;

				// Registration marks stay crisp over the procedural field, including effects-off mode.
				p.lineWidth = 1;
				p.strokeColor = new Color(.38f, .72f, .63f, .16f);

				// Draw a small cross at each grid point.
				for (var x = 20; x < 1000; x += 80)
					for (var y = 20; y < height; y += 80)
					{
						p.BeginPath();
						p.MoveTo(new Vector2(x - 2, y));
						p.LineTo(new Vector2(x + 2, y));
						p.MoveTo(new Vector2(x, y - 2));
						p.LineTo(new Vector2(x, y + 2));
						p.Stroke();
					}

				// Research mode draws the prerequisite edges and returns early.
				if (career != null)
				{
					foreach (var t in GameContent.Tech)
						foreach (var parent in t.parents)
						{
							// Look up the parent and whether both ends are unlocked.
							var from = GameContent.FindTech(parent);
							var active = career.unlocked.Contains(parent) && career.unlocked.Contains(t.id);

							// Color the edge by branch and draw it.
							var color = Palette.Branch(t.branch);
							color.a = active ? .65f : .2f;
							DrawEdge(p, from.position, t.position, color, active, clock * .12f, animate);
						}

					return;
				}

				// Draw each network link.
				foreach (var link in links)
				{
					// Look up both endpoints.
					var a = lookup[link.a];
					var b = lookup[link.b];

					// Skip links with unknown endpoints unless previewing.
					if (!preview && (!a.known || !b.known))
						continue;

					// Work out whether the link is hacked and whether it is on the route.
					var active = !preview && a.controlled && b.controlled;
					var highlight = route.Contains(a.id) && route.Contains(b.id);

					// Pick the base color, then strengthen it for route or hacked links.
					var color = highlight ? Palette.Lime : active ? Palette.Cyan : new Color(.28f, .4f, .43f, .45f);
					if (highlight || active)
						color.a = .75f;

					// Brighten links that touch the selected node.
					var related = a.id == selected || b.id == selected;
					if (related && !active && !highlight)
						color = new Color(.48f, .64f, .68f, .65f);

					// Draw the link.
					DrawEdge(p, new Vector2(a.x, a.y), new Vector2(b.x, b.y), color, active || highlight, clock * .17f,
							 animate);
				}

				// Draw the per-node overlays.
				foreach (var node in nodes)
				{
					// Skip unknown nodes unless previewing.
					if (!preview && !node.known)
						continue;

					// Center of the node in plane coordinates.
					var center = new Vector2(node.x, node.y);

					// Draw corner brackets around the selected node.
					if (node.id == selected)
					{
						var tint = node.objective ? Palette.Amber : Palette.Lime;
						tint.a = .45f;
						p.strokeColor = tint;
						p.lineWidth = 1;
						p.BeginPath();
						p.MoveTo(center + new Vector2(-67, -24));
						p.LineTo(center + new Vector2(-67, -38));
						p.LineTo(center + new Vector2(-48, -38));
						p.Stroke();
						p.BeginPath();
						p.MoveTo(center + new Vector2(48, 38));
						p.LineTo(center + new Vector2(67, 38));
						p.LineTo(center + new Vector2(67, 24));
						p.Stroke();
					}

					// Draw the progress bar under the working target.
					if (node.id == workingTarget)
					{
						p.strokeColor = Palette.Cyan;
						p.lineWidth = 2;
						p.BeginPath();
						p.MoveTo(center + new Vector2(-57, 35));
						p.LineTo(center + new Vector2(-57 + 114 * progress, 35));
						p.Stroke();
					}

					// Draw a warning ring around unsuppressed, uncontrolled scanners.
					if (!preview && node.kind == NodeKind.Scanner && !node.controlled && node.suppressed <= 0)
					{
						// Animated scanners sweep the ring outward; static ones use a fixed radius.
						var radius = 39 + (animate ? Mathf.Repeat(clock * 13 + node.x * .04f, 35) : 12);
						var warning = Palette.Red;
						warning.a = animate ? .13f * (1 - (radius - 39) / 35) : .08f;
						p.strokeColor = warning;
						p.lineWidth = 1;
						p.BeginPath();
						p.Arc(center, radius, 0, 360);
						p.Stroke();
					}
				}
			}

			// Strokes a bent link segment with optional glow and animated flow dashes.
			private static void DrawEdge(Painter2D p, Vector2 a, Vector2 b, Color color, bool active, float phase,
										 bool animate)
			{
				// Compute the bent control points of the edge.
				float mid = (a.x + b.x) * .5f,
					  bend = Mathf.Min(Mathf.Abs(b.x - a.x) * .4f, Mathf.Abs(b.y - a.y) * .5f),
					  direction = Mathf.Sign(b.x - a.x);
				var c = new Vector2(mid - bend * direction, a.y);
				var d = new Vector2(mid + bend * direction, b.y);

				// Stroke the edge as three segments with the given width and tint.
				void Path(float width, Color tint)
				{
					p.strokeColor = tint;
					p.lineWidth = width;
					p.BeginPath();
					p.MoveTo(a);
					p.LineTo(c);
					p.LineTo(d);
					p.LineTo(b);
					p.Stroke();
				}

				// Draw a soft glow under active edges.
				if (active)
				{
					var halo = color;
					halo.a = .055f;
					Path(8, halo);
					halo.a = .11f;
					Path(4, halo);
				}

				// Draw the main edge line.
				Path(active ? 1.65f : 1, color);

				// Inactive edges need no direction markers.
				if (!active)
					return;

				// Measure the three segments and the total length.
				float first = Vector2.Distance(a, c),
					  second = Vector2.Distance(c, d),
					  third = Vector2.Distance(d, b),
					  total = first + second + third;

				// Find the point at a fraction of the total length along the path.
				Vector2 Point(float fraction)
				{
					// Convert the fraction into a distance along the path.
					var distance = fraction * total;

					// Walk the three segments in order until the distance falls inside one.
					if (distance < first)
						return Vector2.Lerp(a, c, distance / Mathf.Max(.001f, first));
					if (distance < first + second)
						return Vector2.Lerp(c, d, (distance - first) / Mathf.Max(.001f, second));

					// Otherwise the point lies on the final segment.
					return Vector2.Lerp(d, b, (distance - first - second) / Mathf.Max(.001f, third));
				}

				// Static marks in reduced/off mode still identify controlled routes without motion.
				var t = animate ? Mathf.Repeat(phase + Mathf.Abs(a.x + a.y) * .001f, 1) : .42f;

				// Draw a short chain of square markers along the path.
				for (var i = 0; i < (animate ? 4 : 1); i++)
				{
					// Place this marker behind the leading one.
					var point = Point(Mathf.Repeat(t - i * .025f, 1));

					// Fade each marker behind the leading one.
					var tint = color;
					tint.a = (1 - i * .23f) * .9f;

					// Draw the marker as a filled square.
					p.fillColor = tint;
					p.BeginPath();
					p.MoveTo(point + new Vector2(-3, -2));
					p.LineTo(point + new Vector2(3, -2));
					p.LineTo(point + new Vector2(3, 2));
					p.LineTo(point + new Vector2(-3, 2));
					p.ClosePath();
					p.Fill();
				}
			}
		}
	}
}