using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace HackThePlanet
{
	public sealed partial class WorkstationApp
	{
		// Action bar buttons keyed by action id for live preview updates.
		private readonly Dictionary<string, ActionButtonParts> actionButtons = new();

		// Heading and queue summary labels in the operation action bar.
		private Label actionHeading, queueStatus;

		// RAM and payload storage radial meters flanking the action list.
		private RadialResourceMeter memoryRadial, storageRadial;

		// Row of chips showing queued actions in order.
		private VisualElement queueList;

		// Overlaid strip on the network map showing the running task and queue.
		private VisualElement graphQueueStrip, graphQueueRow;

		// Cached queue signature to avoid rebuilding chips when unchanged.
		private string queueSignature, graphQueueSignature;

		// Builds the operation action bar with RAM meter, actions, queue, and storage meter.
		private void BuildActionBar(VisualElement parent)
		{
			// Reset the button registry and the queue signature.
			actionButtons.Clear();
			queueSignature = null;

			// Build the shared RAM meter on the left of the bar.
			var bar = El(parent, "graph-action-bar");
			memoryRadial = new RadialResourceMeter("RAM / SHARED POOL");
			bar.Add(memoryRadial);

			// Build the heading, queue status, and queue chip row.
			var center = El(bar, "graph-action-center");
			var header = El(center, "action-bar-header");
			actionHeading = Text(header, "ACTIONS / SELECT A SYSTEM", "action-bar-heading");
			queueStatus = Text(header, "QUEUE EMPTY", "action-queue-status");
			queueList = El(header, "action-queue-list");

			// Create the horizontal scroller that holds the action buttons.
			var scroll = new ScrollView(ScrollViewMode.Horizontal) { name = "action-list" };
			scroll.AddToClassList("graph-action-scroll");
			center.Add(scroll);
			var list = El(scroll, "graph-action-list");

			// Start with the base actions, then add any actions granted by owned techs.
			var ids = new List<string> { "scan", "hack", "download" };

			// Mirrors GameSimulation.Granted: owned techs grant their actions, special techs also need to be equipped.
			foreach (var tech in State.Catalog.Technologies)
			{
				// Skip techs that are not owned, or special techs that are not equipped.
				if (!State.Has(tech.id) || (tech.special && !State.Equipped(tech.id)))
					continue;

				// Add each granted action once.
				foreach (var actionId in tech.grants)
					if (!ids.Contains(actionId))
						ids.Add(actionId);
			}

			// Create one button per action; the first one is the primary button.
			var primary = true;
			foreach (var id in ids)
			{
				// Skip action ids that the catalog does not define.
				var action = State.Catalog.FindAction(id);
				if (action == null)
					continue;

				// Add the button and clear the primary flag for the rest.
				ActionButton(list, action, primary);
				primary = false;
			}

			// Build the payload storage meter on the right of the bar.
			storageRadial = new RadialResourceMeter("STORAGE / PAYLOADS");
			bar.Add(storageRadial);

			// Populate the meters and buttons with the current state.
			RefreshActionBar();
			RefreshResourceRadials();
		}

		// Refreshes action button previews and the queue summary for the selected node.
		private void RefreshActionBar()
		{
			// Nothing to refresh without an active operation and a built bar.
			if (actionButtons == null || State == null || !State.Active)
				return;

			// Show the selected system's name in the heading.
			if (actionHeading != null)
				actionHeading.text = "ACTIONS / " + (State.Node(selectedNode)?.label ?? "SELECT A SYSTEM");

			// Update every action button, flagging the first one.
			var first = true;
			foreach (var parts in actionButtons.Values)
			{
				UpdateActionButton(parts, first);
				first = false;
			}

			// Refresh the queue summary and chips.
			RefreshQueueStatus();
			RefreshGraphQueueStrip();
		}

		// Builds the horizontal task queue strip overlaid on the network graph.
		private void BuildGraphQueueStrip(VisualElement host)
		{
			graphQueueSignature = null;
			graphQueueStrip = El(host, "graph-task-queue-strip");
			graphQueueRow = El(graphQueueStrip, "graph-task-queue-row");
			RefreshGraphQueueStrip();
		}

		// Updates the graph queue strip when the running task or queue changes.
		private void RefreshGraphQueueStrip()
		{
			if (graphQueueStrip == null || graphQueueRow == null || !State.Active)
				return;

			var task = State.Op.task;
			var hasRunning = task != null;
			var visible = hasRunning || State.Op.queue.Count > 0;
			graphQueueStrip.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
			if (!visible)
				return;

			var runningSig = hasRunning
				? task.actionId + "@" + task.target + (State.Op.paused ? "|paused" : "|run")
				: string.Empty;
			var signature = runningSig + "|" +
							string.Join(";", State.Op.queue.Select(q => q.actionId + "@" + q.target));
			if (signature == graphQueueSignature)
				return;

			graphQueueSignature = signature;
			graphQueueRow.Clear();

			if (hasRunning)
				AddGraphQueueCard(task.actionId, task.target, true, 0);

			for (var i = 0; i < State.Op.queue.Count; i++)
			{
				var queued = State.Op.queue[i];
				AddGraphQueueCard(queued.actionId, queued.target, false, i + 1);
			}
		}

		// Adds one card to the graph queue strip for the running task or a queued slot.
		private void AddGraphQueueCard(string actionId, string targetId, bool running, int queueIndex)
		{
			var card = El(graphQueueRow, "graph-task-queue-card");
			if (running)
				card.AddToClassList("graph-task-queue-card-running");

			card.Add(new ActionGlyph(actionId, ActionGlyph.ColorFor(actionId), 16));
			var text = El(card, "graph-task-queue-card-text");
			var title = State.ActionTitle(actionId, targetId);
			var target = State.Node(targetId)?.label ?? "OPERATOR";
			Text(text, title, "graph-task-queue-action");
			Text(text, target, "graph-task-queue-target");
			Text(card, running ? State.Op.paused ? "PAUSED" : "RUNNING" : queueIndex.ToString(),
				 "graph-task-queue-badge");
			card.tooltip = running
				? (State.Op.paused ? "PAUSED / " : "RUNNING / ") + title + " / " + target
				: queueIndex + " / " + title + " / " + target;
		}

		// Updates queue status text and rebuilds queue chips when the queue changes.
		private void RefreshQueueStatus()
		{
			// Nothing to show without an active operation and a status label.
			if (queueStatus == null || !State.Active)
				return;

			// Describe the running task, paused state, or idle state.
			var current = State.Op.task == null
				? "IDLE"
				: (State.Op.paused ? "PAUSED / " : "RUNNING / ") +
				  State.ActionTitle(State.Op.task.actionId, State.Op.task.target) + " · " +
				  (State.Node(State.Op.task.target)?.label ?? "OPERATOR");

			// Show the status and queue usage in the label, with a tooltip for details.
			queueStatus.text = current + "  /  QUEUE " + State.Op.queue.Count + " / " + State.QueueCapacity;
			queueStatus.tooltip = current + "\nQueue slots used: " + State.Op.queue.Count + " / " +
								  State.QueueCapacity + (State.Op.queue.Count > 0
									  ? "\nUse CLEAR to remove queued actions."
									  : string.Empty);

			// Build a signature of the queue so the chips only rebuild when it changes.
			var signature = string.Join(";", State.Op.queue.Select(q => q.actionId + "@" + q.target));

			// Skip the rebuild when there is no chip row or the queue is unchanged.
			if (queueList == null || signature == queueSignature)
				return;

			// Rebuild the chip row for the new queue.
			queueSignature = signature;
			queueList.Clear();

			// Leave the row empty when nothing is queued.
			if (State.Op.queue.Count == 0)
				return;

			// Add one numbered chip per queued action.
			for (var i = 0; i < State.Op.queue.Count; i++)
			{
				// Look up the queued action and build its chip.
				var queued = State.Op.queue[i];
				var chip = El(queueList, "action-queue-chip");
				var title = State.ActionTitle(queued.actionId, queued.target);
				chip.tooltip = i + 1 + " / " + title + " / " + (State.Node(queued.target)?.label ?? "OPERATOR");
				chip.Add(new ActionGlyph(queued.actionId, ActionGlyph.ColorFor(queued.actionId), 12));
				Text(chip, (i + 1).ToString(), "action-queue-index");
			}
		}

		// Pushes live RAM and payload storage values into the radial meters.
		private void RefreshResourceRadials()
		{
			// Meters only show live values during an active operation.
			if (!State.Active)
				return;

			// Update the shared RAM meter with reserved, reclaiming, and free amounts.
			if (memoryRadial != null)
				memoryRadial.SetValues(State.MemoryPool, State.MemoryReserved, State.MemoryReclaiming, State.MemoryFree,
									   Palette.Cyan, new Color(Palette.Cyan.r, Palette.Cyan.g, Palette.Cyan.b, .38f),
									   State.MemoryFree.ToString("0.#"), "GB FREE",
									   "<b>SHARED RAM</b>\n" + State.MemoryReserved.ToString("0.#") + " GB active\n" +
									   State.MemoryReclaiming.ToString("0.#") + " GB reclaiming\n" +
									   State.MemoryFree.ToString("0.#") + " GB free for one-shots / " +
									   State.MemoryPool.ToString("0.#") + " GB total");

			// Update the payload storage meter with used and free space.
			if (storageRadial != null)
			{
				// Read the storage totals for this frame.
				float used = State.StorageUsed, total = State.StorageCapacity, free = State.FreeStorage;

				// Push the values to the storage meter.
				storageRadial.SetValues(total, used, free, free, Palette.Lime,
										new Color(Palette.Lime.r, Palette.Lime.g, Palette.Lime.b, .38f),
										free.ToString("0.#"), "GB FREE",
										"<b>PAYLOAD STORAGE</b>\n" + used.ToString("0.#") + " GB used\n" +
										free.ToString("0.#") + " GB available / " + total.ToString("0.#") +
										" GB total");
			}
		}

		// Widget bundle for one operation action button and its cached description.
		private sealed class ActionButtonParts
		{
			public readonly ActionInfo action;
			public readonly Button button;
			public readonly string description;
			public readonly ActionGlyph icon;
			public readonly VisualElement wrapper;

			public ActionButtonParts(ActionInfo action, Button button, VisualElement wrapper, ActionGlyph icon,
									 string description)
			{
				// Store the action, its widgets, and its description for later refreshes.
				this.action = action;
				this.button = button;
				this.wrapper = wrapper;
				this.icon = icon;
				this.description = description;
			}
		}
	}

	internal sealed class RadialResourceMeter : VisualElement
	{
		// Center readout labels for the numeric value and caption.
		private readonly Label amount, label;

		// Arc colors for the primary and secondary capacity segments.
		private Color primaryColor, secondaryColor;

		// Total capacity and normalized primary and secondary arc fractions.
		private float total, primary, secondary;

		public RadialResourceMeter(string title)
		{
			// Style the meter and attach its tooltip and intel class.
			AddToClassList("resource-radial");
			pickingMode = PickingMode.Position;
			tooltip = title;
			AddToClassList(WorkstationApp.IntelTooltipClass);

			// Build the ring that draws the arcs; it ignores pointer input.
			var ring = new VisualElement();
			ring.AddToClassList("resource-radial-ring");
			ring.pickingMode = PickingMode.Ignore;
			ring.generateVisualContent += Draw;
			Add(ring);

			// Build the center panel that holds the readout text.
			var center = new VisualElement();
			center.AddToClassList("resource-radial-center");
			center.pickingMode = PickingMode.Ignore;
			Add(center);

			// Build the amount label.
			amount = new Label("0");
			amount.AddToClassList("resource-radial-amount");
			amount.pickingMode = PickingMode.Ignore;
			center.Add(amount);

			// Build the caption label beneath the amount.
			label = new Label("GB FREE");
			label.AddToClassList("resource-radial-caption");
			label.pickingMode = PickingMode.Ignore;
			center.Add(label);
		}

		public void SetValues(float capacity, float first, float second, float available, Color firstColor,
							  Color secondColor, string value, string caption, string tip)
		{
			// Normalize the two arcs against total capacity and store their colors.
			total = Mathf.Max(0, capacity);
			primary = Mathf.Clamp(first / Mathf.Max(.001f, total), 0, 1);
			secondary = Mathf.Clamp(second / Mathf.Max(.001f, total), 0, 1 - primary);
			primaryColor = firstColor;
			secondaryColor = secondColor;

			// Update the readout text and tooltip.
			amount.text = value;
			label.text = caption;
			tooltip = tip;

			// Ask the ring to redraw with the new arcs.
			this.Q<VisualElement>(className: "resource-radial-ring")?.MarkDirtyRepaint();
		}

		// Paints the track ring and primary or secondary capacity arcs for the meter.
		private void Draw(MeshGenerationContext context)
		{
			// Read the painter and the drawable size; skip when there is too little space.
			var p = context.painter2D;
			var size = Mathf.Min(contentRect.width, contentRect.height);
			if (size < 2)
				return;

			// Compute the ring center, radius, and stroke width from the size.
			var center = new Vector2(contentRect.width * .5f, contentRect.height * .5f);
			var radius = size * .43f;
			p.lineWidth = Mathf.Max(3, size * .095f);

			// Draw the empty track ring first.
			p.strokeColor = Palette.Hex("263940");
			p.BeginPath();
			p.Arc(center, radius, 0, 360);
			p.Stroke();

			// Skip the arcs when there is no capacity to fill.
			if (total <= 0)
				return;

			// Draw the primary and secondary arcs over the track.
			DrawArc(p, center, radius, 0, primary, primaryColor);
			DrawArc(p, center, radius, primary, secondary, secondaryColor);
		}

		// Strokes one arc segment of the radial meter ring in the given color.
		private static void DrawArc(Painter2D p, Vector2 center, float radius, float start, float fraction, Color color)
		{
			// Skip arcs too small to see.
			if (fraction <= .001f)
				return;

			// Stroke the arc between its start and end angles in its color.
			p.strokeColor = color;
			p.BeginPath();
			p.Arc(center, radius, start * 360f, (start + fraction) * 360f);
			p.Stroke();
		}
	}

	internal sealed class ActionGlyph : VisualElement
	{
		// Action id that selects which vector glyph artwork to draw.
		private readonly string actionId;

		// Stroke and fill color for the drawn action icon.
		private Color tint;

		public ActionGlyph(string actionId, Color tint, float size)
		{
			// Store the action id and its tint.
			this.actionId = actionId;
			this.tint = tint;

			// Style the glyph and size it so flex layouts do not shrink it.
			AddToClassList("action-glyph");
			style.width = size;
			style.height = size;
			style.flexShrink = 0;

			// Ignore pointer input and draw through the painter callback.
			pickingMode = PickingMode.Ignore;
			generateVisualContent += Draw;
		}

		public static Color ColorFor(string actionId)
		{
			// Map each action to its accent color.
			switch (actionId)
			{
				case "scan": return Palette.Cyan;
				case "hack":
				case "backdoor": return Palette.Lime;
				case "download":
				case "bridge": return Palette.Amber;
				case "disrupt": return Palette.Red;
				case "blackout":
				case "decoy": return Palette.Violet;
				default: return Palette.Cyan;
			}
		}

		public void SetTint(Color color)
		{
			// Skip the repaint when the tint has not changed.
			if (tint == color)
				return;

			// Store the new tint and redraw.
			tint = color;
			MarkDirtyRepaint();
		}

		// Draws the vector icon for the bound action id inside the glyph bounds.
		private void Draw(MeshGenerationContext context)
		{
			// Read the drawable size; skip when there is too little space.
			var size = Mathf.Min(contentRect.width, contentRect.height);
			if (size < 2)
				return;

			// Set the stroke, fill, and line width for the glyph.
			var p = context.painter2D;
			p.strokeColor = tint;
			p.fillColor = tint;
			p.lineWidth = Mathf.Max(1.5f, size * .075f);

			Vector2 P(float x, float y)
			{
				return new Vector2(x * contentRect.width, y * contentRect.height);
			}

			void Line(float x, float y, float a, float b)
			{
				// Stroke a single straight segment.
				p.BeginPath();
				p.MoveTo(P(x, y));
				p.LineTo(P(a, b));
				p.Stroke();
			}

			void Box(float x, float y, float a, float b)
			{
				// Stroke a rectangle outline between two corners.
				p.BeginPath();
				p.MoveTo(P(x, y));
				p.LineTo(P(a, y));
				p.LineTo(P(a, b));
				p.LineTo(P(x, b));
				p.ClosePath();
				p.Stroke();
			}

			void Dot(float x, float y, float radius)
			{
				// Fill a dot at the point with a radius scaled to the glyph.
				p.BeginPath();
				p.Arc(P(x, y), size * radius, 0, 360);
				p.Fill();
			}

			// Draw the artwork for this action.
			switch (actionId)
			{
				case "scan":
					// Draw the radar rings, sweep line, and center dot.
					p.BeginPath();
					p.Arc(P(.5f, .5f), size * .32f, 0, 360);
					p.Stroke();
					p.BeginPath();
					p.Arc(P(.5f, .5f), size * .17f, 0, 360);
					p.Stroke();
					Line(.5f, .5f, .78f, .22f);
					Dot(.5f, .5f, .055f);
					break;
				case "hack":
					// Draw the terminal frame with a prompt chevron.
					Box(.12f, .2f, .88f, .8f);
					Line(.24f, .4f, .39f, .51f);
					Line(.39f, .51f, .24f, .62f);
					Line(.49f, .62f, .72f, .62f);
					break;
				case "download":
					// Draw the down arrow onto a tray.
					Line(.5f, .13f, .5f, .62f);
					Line(.31f, .44f, .5f, .63f);
					Line(.69f, .44f, .5f, .63f);
					Line(.19f, .72f, .19f, .86f);
					Line(.19f, .86f, .81f, .86f);
					Line(.81f, .86f, .81f, .72f);
					break;
				case "backdoor":
					// Draw the door frame with an arrow passing through it.
					Box(.21f, .12f, .72f, .88f);
					Line(.72f, .12f, .82f, .2f);
					Line(.72f, .88f, .82f, .8f);
					Line(.55f, .51f, .87f, .51f);
					Line(.75f, .39f, .87f, .51f);
					Line(.75f, .63f, .87f, .51f);
					break;
				case "bridge":
					// Draw two nodes joined by a bridge arc.
					Box(.08f, .28f, .27f, .7f);
					Box(.73f, .28f, .92f, .7f);
					Line(.27f, .37f, .5f, .22f);
					Line(.5f, .22f, .73f, .37f);
					Line(.27f, .63f, .73f, .63f);
					break;
				case "blackout":
					// Draw the lightning bolt with its spark lines.
					Line(.54f, .1f, .3f, .5f);
					Line(.3f, .5f, .52f, .5f);
					Line(.52f, .5f, .4f, .9f);
					Line(.4f, .9f, .75f, .42f);
					Line(.75f, .42f, .52f, .42f);
					Line(.15f, .18f, .29f, .27f);
					Line(.73f, .75f, .86f, .84f);
					break;
				case "decoy":
					// Draw two offset boxes with a dot marking the decoy.
					Box(.12f, .2f, .62f, .7f);
					Box(.38f, .3f, .88f, .8f);
					Dot(.63f, .55f, .07f);
					break;
				case "disrupt":
					// Draw the ring with a slash through its center.
					p.BeginPath();
					p.Arc(P(.5f, .5f), size * .31f, 0, 360);
					p.Stroke();
					Line(.16f, .84f, .84f, .16f);
					Dot(.5f, .5f, .055f);
					break;
				default:
					// Draw a crosshair for unknown actions.
					Box(.2f, .2f, .8f, .8f);
					Line(.5f, .06f, .5f, .2f);
					Line(.5f, .8f, .5f, .94f);
					Dot(.5f, .5f, .09f);
					break;
			}
		}
	}
}