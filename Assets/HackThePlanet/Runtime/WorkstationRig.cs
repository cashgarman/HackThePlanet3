using System;
using System.Collections.Generic;
using System.Linq;
using Cashg.GraphExplorer;
using UnityEngine;
using UnityEngine.UIElements;

namespace HackThePlanet
{
	/// <summary>
	///     Vector icon for rig modules, stat readouts, and inventory tabs; scales cleanly like <see cref="DeviceGlyph" />
	///     .
	/// </summary>
	public sealed class RigGlyph : VisualElement
	{
		// Module ids that have bespoke artwork instead of the generic family icon.
		// Module ids that use custom vector art instead of the family default icon.
		private static readonly HashSet<string> BespokeModules = new()
			{ "uplink", "recon-cache", "drive", "storage-array", "accelerator", "ram-bank", "quiet-filter" };

		// Icon key selecting module, family, or stat artwork.
		private readonly string category;

		// Tier pip count drawn along the bottom for power and cooling modules.
		private readonly int tier;

		// Stroke and fill color for the rig glyph.
		private readonly Color tint;

		/// <summary>Creates an icon for a module id, family name, or stat category.</summary>
		/// <param name="category">Module id, family (for example <c>power</c>), or stat glyph key.</param>
		/// <param name="tint">Stroke and fill color.</param>
		/// <param name="size">Width and height in layout pixels.</param>
		/// <param name="tier">When positive, draws tier pips along the bottom (power and cooling modules).</param>
		public RigGlyph(string category, Color tint, float size = 20, int tier = 0)
		{
			// Store the icon key, colors, and pip tier.
			this.category = category ?? "";
			this.tint = tint;
			this.tier = Mathf.Clamp(tier, 0, 5);

			// Size the icon and keep flex layouts from shrinking it.
			style.width = size;
			style.height = size;
			style.flexShrink = 0;

			// Ignore pointer input and draw through the painter callback.
			pickingMode = PickingMode.Ignore;
			generateVisualContent += Draw;
		}

		/// <summary>Builds an icon for a module, using a bespoke drawing when available otherwise the module family.</summary>
		/// <param name="model">Authoring definition for the rack module.</param>
		/// <param name="tint">Stroke and fill color.</param>
		/// <param name="size">Width and height in layout pixels.</param>
		/// <returns>A glyph sized for UI lists and rack cells.</returns>
		public static RigGlyph ForModule(RigModuleDefinition model, Color tint, float size = 20)
		{
			// Only power and cooling modules show tier pips.
			var tiered = model.family == "power" || model.family == "cooling";

			// Use bespoke art when available, otherwise the module family icon.
			return new RigGlyph(BespokeModules.Contains(model.id) ? model.id : model.family, tint, size,
								tiered ? model.tier : 0);
		}

		// Draws the vector rig icon for the bound category and optional tier pips.
		private void Draw(MeshGenerationContext context)
		{
			// Set the stroke, fill, and line width for the icon.
			var p = context.painter2D;
			p.strokeColor = tint;
			p.fillColor = tint;
			p.lineWidth = 1.5f;

			// Read the drawable size.
			float w = contentRect.width, h = contentRect.height;

			// Tiered icons shrink the artwork upward to leave a row of pips along the bottom edge.
			var scale = tier > 0 ? .8f : 1f;

			Vector2 P(float x, float y)
			{
				return new Vector2(x * w, y * h * scale);
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

			void Poly(params float[] v)
			{
				// Stroke a closed polygon from the flattened coordinate list.
				p.BeginPath();
				p.MoveTo(P(v[0], v[1]));
				for (var i = 2; i < v.Length; i += 2)
					p.LineTo(P(v[i], v[i + 1]));
				p.ClosePath();
				p.Stroke();
			}

			void Chip(float x, float y, float a, float b)
			{
				// Fill a small rectangle, used for memory and tier pips.
				p.BeginPath();
				p.MoveTo(P(x, y));
				p.LineTo(P(a, y));
				p.LineTo(P(a, b));
				p.LineTo(P(x, b));
				p.ClosePath();
				p.Fill();
			}

			// Pick the artwork for the category, module family, or stat glyph.
			switch (category)
			{
				case "ALL":
					// Draw four quadrant boxes.
					Box(.12f, .12f, .46f, .46f);
					Box(.54f, .12f, .88f, .46f);
					Box(.12f, .54f, .46f, .88f);
					Box(.54f, .54f, .88f, .88f);
					break;
				case "power":
					// Draw the lightning bolt.
					Poly(.58f, .06f, .22f, .56f, .48f, .56f, .40f, .94f, .78f, .42f, .52f, .42f, .58f, .06f); break;
				case "cooling":
					// Draw three spokes through a central hub.
					for (var i = 0; i < 3; i++)
					{
						// Compute the spoke end-point offsets.
						float a = i * 60f * Mathf.Deg2Rad, dx = Mathf.Cos(a) * .38f, dy = Mathf.Sin(a) * .38f;
						Line(.5f - dx, .5f - dy, .5f + dx, .5f + dy);
					}

					// Fill the hub at the center.
					p.BeginPath();
					p.Arc(P(.5f, .5f), w * .07f, 0f, 360f);
					p.Fill();
					break;
				case "processing":
					// Draw the chip body, inner die, and pins.
					Box(.22f, .22f, .78f, .78f);
					Box(.38f, .38f, .62f, .62f);
					Line(.36f, .08f, .36f, .22f);
					Line(.64f, .08f, .64f, .22f);
					Line(.36f, .78f, .36f, .92f);
					Line(.64f, .78f, .64f, .92f);
					Line(.08f, .36f, .22f, .36f);
					Line(.08f, .64f, .22f, .64f);
					Line(.78f, .36f, .92f, .36f);
					Line(.78f, .64f, .92f, .64f);
					break;
				case "transfer":
					// Draw two opposing arrows.
					Line(.10f, .36f, .84f, .36f);
					Line(.84f, .36f, .70f, .24f);
					Line(.84f, .36f, .70f, .48f);
					Line(.90f, .64f, .16f, .64f);
					Line(.16f, .64f, .30f, .52f);
					Line(.16f, .64f, .30f, .76f);
					break;
				case "storage":
					// Draw three stacked drive bays with slot marks.
					Box(.16f, .10f, .84f, .34f);
					Box(.16f, .40f, .84f, .64f);
					Box(.16f, .70f, .84f, .94f);
					Line(.26f, .22f, .40f, .22f);
					Line(.26f, .52f, .40f, .52f);
					Line(.26f, .82f, .40f, .82f);
					break;
				case "memory":
					// Draw the memory stick with contact pins and chips.
					Box(.08f, .30f, .92f, .66f);
					Line(.20f, .66f, .20f, .80f);
					Line(.35f, .66f, .35f, .80f);
					Line(.50f, .66f, .50f, .80f);
					Line(.65f, .66f, .65f, .80f);
					Line(.80f, .66f, .80f, .80f);
					Box(.20f, .40f, .36f, .56f);
					Line(.46f, .42f, .80f, .42f);
					Line(.46f, .54f, .80f, .54f);
					break;
				case "stealth":
					// Draw the eye outline, pupil, and slash.
					p.BeginPath();
					p.MoveTo(P(.08f, .5f));
					p.BezierCurveTo(P(.28f, .20f), P(.72f, .20f), P(.92f, .5f));
					p.BezierCurveTo(P(.72f, .80f), P(.28f, .80f), P(.08f, .5f));
					p.Stroke();
					p.BeginPath();
					p.Arc(P(.5f, .5f), w * .12f, 0f, 360f);
					p.Stroke();
					Line(.14f, .90f, .86f, .10f);
					break;
				case "reclaim":
					// Draw the reclaim arc and its arrowhead at the end point.
					p.BeginPath();
					p.Arc(P(.5f, .5f), w * .34f, 30f, 300f);
					p.Stroke();
					float end = 300f * Mathf.Deg2Rad,
						  ex = .5f + Mathf.Cos(end) * .34f,
						  ey = .5f + Mathf.Sin(end) * .34f;
					Line(ex, ey, ex + .16f, ey - .02f);
					Line(ex, ey, ex - .02f, ey - .16f);
					break;
				case "slots":
					// Draw the 3x3 slot grid.
					Box(.1f, .1f, .9f, .9f);
					Line(.1f, .37f, .9f, .37f);
					Line(.1f, .63f, .9f, .63f);
					Line(.37f, .1f, .37f, .9f);
					Line(.63f, .1f, .63f, .9f);
					break;
				case "uplink":
					// Draw the antenna mast, two signal arcs, and the transmitter dot.
					Line(.5f, .56f, .5f, .92f);
					Line(.34f, .92f, .66f, .92f);
					p.BeginPath();
					p.Arc(P(.5f, .5f), w * .14f, 200f, 340f);
					p.Stroke();
					p.BeginPath();
					p.Arc(P(.5f, .5f), w * .27f, 200f, 340f);
					p.Stroke();
					p.BeginPath();
					p.Arc(P(.5f, .5f), w * .05f, 0f, 360f);
					p.Fill();
					break;
				case "recon-cache":
					// Draw the cache box with a download arrow.
					Box(.14f, .46f, .86f, .86f);
					Line(.5f, .1f, .5f, .6f);
					Line(.36f, .46f, .5f, .6f);
					Line(.64f, .46f, .5f, .6f);
					break;
				case "drive":
					// Draw the drive body, platter, and activity light.
					Box(.1f, .12f, .9f, .88f);
					p.BeginPath();
					p.Arc(P(.42f, .5f), w * .22f, 0f, 360f);
					p.Stroke();
					p.BeginPath();
					p.Arc(P(.76f, .26f), w * .04f, 0f, 360f);
					p.Fill();
					break;
				case "storage-array":
					// Draw the enclosure and its four drive bays.
					Box(.08f, .08f, .92f, .92f);

					// Stroke a bay circle at each grid position.
					foreach (var fx in new[] { .3f, .7f })
						foreach (var fy in new[] { .3f, .7f })
						{
							p.BeginPath();
							p.Arc(P(fx, fy), w * .1f, 0f, 360f);
							p.Stroke();
						}

					break;
				case "accelerator":
					// Draw the chip body, its pins, and the inner circuit.
					Box(.18f, .18f, .82f, .82f);
					Line(.06f, .36f, .18f, .36f);
					Line(.06f, .64f, .18f, .64f);
					Line(.82f, .36f, .94f, .36f);
					Line(.82f, .64f, .94f, .64f);
					Line(.36f, .06f, .36f, .18f);
					Line(.64f, .06f, .64f, .18f);
					Line(.36f, .82f, .36f, .94f);
					Line(.64f, .82f, .64f, .94f);
					Line(.34f, .58f, .5f, .42f);
					Line(.5f, .42f, .66f, .58f);
					Line(.34f, .74f, .5f, .58f);
					Line(.5f, .58f, .66f, .74f);
					break;
				case "ram-bank":
					// Draw two memory banks with chip pips.
					Box(.08f, .14f, .92f, .44f);
					Box(.08f, .56f, .92f, .86f);

					// Fill a pair of chips in each bank column.
					foreach (var cx in new[] { .22f, .4f, .6f, .78f })
					{
						Chip(cx - .04f, .22f, cx + .04f, .36f);
						Chip(cx - .04f, .64f, cx + .04f, .78f);
					}

					break;
				case "quiet-filter":
					// Draw the funnel with its filter stem.
					Poly(.12f, .14f, .88f, .14f, .58f, .5f, .58f, .86f, .42f, .76f, .42f, .5f); break;
				default:
					// Draw a generic ring with a horizontal bar.
					p.BeginPath();
					p.Arc(P(.5f, .5f), w * .38f, 0f, 360f);
					p.Stroke();
					Line(.12f, .5f, .88f, .5f);
					break;
			}

			// Draw the tier pips along the bottom edge, centered on the icon.
			for (var i = 0; i < tier; i++)
			{
				var cx = .5f + (i - (tier - 1) * .5f) * .16f;
				Chip(cx - .05f, .84f, cx + .05f, .92f);
			}
		}
	}

	public sealed partial class WorkstationApp
	{
		// Module family tab order used in the rig shop and inventory filters.
		private static readonly string[] RigCategoryOrder =
			{ "power", "cooling", "processing", "transfer", "storage", "memory", "stealth", "reclaim" };

		// Grid slot buttons highlighted as valid drop targets during a drag.
		private readonly List<Button> dragValidSlots = new();

		// Saved camera for the flat rig rack graph viewport.
		private readonly GraphCameraState rigCamera = new();

		// Floating ghost button following the pointer during rig drags.
		private Button dragGhost;

		// Drag start pointer position and the latest rig drag position in panel space.
		private Vector2 dragOrigin, lastRigDragPanelPos;

		// Captured pointer id and grid anchor to restore when a drag is canceled.
		private int dragPointer, dragRestoreRow = -1, dragRestoreCol = -1;

		// Rotation state to restore when a drag is canceled without placing.
		private bool dragRestoreRot;

		// Drag rotation, movement, and whether the part was lifted from the grid.
		private bool dragRotated, dragMoved, dragLifted;

		// Inventory panel used as the drop target when uninstalling modules.
		private VisualElement inventoryDropZone;

		// Selected module instance, active drag id, status text, and shop filter categories.
		private string selectedRigId, dragRigId, rigMessage, rigCategory = "ALL", shopCategory = "ALL";

		// Builds the Rig section of the workstation UI.
		private void BuildRig(VisualElement workspace)
		{
			// Clear the selection when its part no longer exists.
			if (State.Part(selectedRigId) == null)
				selectedRigId = null;

			// Collect the available families and reset any filter that no longer exists.
			var categories = RigCategories().ToList();

			if (rigCategory != "ALL" && !categories.Contains(rigCategory))
				rigCategory = "ALL";

			if (shopCategory != "ALL" && !categories.Contains(shopCategory))
				shopCategory = "ALL";

			// Build the left panel: the module exchange header and category filters.
			var left = El(workspace, "left-panel");
			var shop = Scroll(left, "rig-shop");
			Text(shop, "MODULE EXCHANGE", "eyebrow cyan");
			Text(shop, "Buy copies. Pack your signal.", "small muted");
			BuildRigShopFilters(shop, categories);

			// List each branch's modules as cards, filtered by the shop category.
			foreach (var branch in State.Modules.Where(m => shopCategory == "ALL" || m.family == shopCategory)
										.GroupBy(m => m.branch))
			{
				// Heading for this branch's module cards.
				Text(shop, branch.Key, "eyebrow section-space").style.color = Palette.Branch(branch.Key);

				// One card per module in this branch.
				foreach (var model in branch)
				{
					// Build the card container with its icon, title, and tooltip.
					var card = El(shop, "rig-shop-card");
					card.tooltip = ModuleTooltip(model) + "\n" + State.PreviewModule(model.id).reason;
					var shopHead = El(card, "rig-shop-head");
					shopHead.Add(RigGlyph.ForModule(model, Palette.Branch(model.branch), 40));
					Text(shopHead, model.title + " / T" + model.tier, "small rig-part-title").style.color =
						Palette.Branch(model.branch);

					// Show the spec line and the module description.
					RichText(card, model.width + "x" + model.height + " / " + model.price + " cr / " +
								   model.powerDemand + " POWER / " + model.heatOutput + " HEAT", "micro muted");
					RichText(card, model.description, "small");

					// Create the buy button, which buys one copy and places it in the first free slot.
					var preview = State.PreviewModule(model.id);
					var buy = Btn(card, "BUY COPY / " + model.price + " cr", () =>
					{
						// Buy a copy from the exchange.
						var part = State.BuyModule(model.id);

						// Select and place the new copy when the purchase succeeds.
						if (part != null)
						{
							// Select the copy, place it, and give audio feedback.
							selectedRigId = part.instanceId;
							State.PlaceFirstFree(part.instanceId);
							Beep(true);
						}
					}, "buy-" + model.id, "secondary-button");

					// Disable the button when the preview blocks the purchase.
					buy.SetEnabled(preview.allowed);

					// Explain why the purchase is blocked.
					if (!preview.allowed)
						Text(card, preview.reason, "micro amber");
				}
			}

			// Create the rack map panel and show any validation issues above it.
			var middle = El(workspace, "network-panel");
			var map = El(middle, "map-content");
			BuildRigIssues(map);

			// Build the heading with the chassis title, budget actions, and budgets.
			var head = El(map, "map-heading rig-map-heading");
			var titles = El(head, "map-title-block");
			var titleRow = El(titles, "rig-title-row");
			var titleLead = El(titleRow, "rig-title-lead");
			Text(titleLead,
				 State.Chassis.title.ToUpperInvariant() + " / " + State.Chassis.rows + "x" + State.Chassis.columns,
				 "map-title");
			Text(titleLead, "DRAG TO PLACE / R OR WHEEL ROTATES", "micro muted");
			BuildRigBudgetActions(titleRow);
			BuildRigBudgets(head);

			// Create the rack viewport over the scene and its cell metrics.
			canvas = new GraphViewport(BuildRackScene(), SlotClicked, rigCamera, true);
			map.Add(canvas);
			const float cellPitch = 4f, cellInset = .32f;

			// Style the empty cells and hide the ones that hold installed parts.
			foreach (var node in canvas.Query<Button>().ToList())
			{
				// Skip buttons that are not grid slots.
				if (!node.name.StartsWith("slot-"))
					continue;

				// Decode the slot index into its row and column.
				int index = int.Parse(node.name.Substring(5)),
					row = index / State.Chassis.columns,
					column = index % State.Chassis.columns;

				// Hide cells that hold an installed part.
				if (State.PartAt(row, column) != null)
				{
					node.style.display = DisplayStyle.None;

					continue;
				}

				// Style the empty cell and set its tooltip.
				node.AddToClassList("rig-cell");
				canvas.ConfigureFlatNode(node.name, Vector2.one * (cellPitch - cellInset),
										 "Empty cell " + (row + 1) + ", " + (column + 1));
				node.SetEnabled(!State.Active);
			}

			// Place a footprint-sized button over each installed part.
			foreach (var part in State.InstalledParts)
			{
				var model = State.Module(part.modelId);
				var size = State.PartSize(part);
				var footprint = new Vector2(size.x * cellPitch - cellInset, size.y * cellPitch - cellInset);
				canvas.ConfigureFlatNode("part-" + part.instanceId, footprint, ModuleIntel(model), module: true);

				// Tag the part button with its styles and intel tooltip.
				var module = canvas.Q<Button>("part-" + part.instanceId);
				module?.AddToClassList("rig-module");
				module?.AddToClassList(IntelTooltipClass);

				// Fill the button with the module icon and title.
				if (module != null)
					SetPartContent(module, model, model.title, 56);

				// Start a rack drag when the part is pressed.
				module?.RegisterCallback<PointerDownEvent>(evt => BeginRigDrag(part.instanceId, evt),
														   TrickleDown.TrickleDown);
			}

			// Add the zoom and fit controls under the rack.
			var footer = El(map, "map-footer");
			Btn(footer, "-", () => canvas.Zoom(1.15f), "rig-zoom-out", "map-button");
			Btn(footer, "+", () => canvas.Zoom(.85f), "rig-zoom-in", "map-button");
			Btn(footer, "FIT", () => canvas.ResetView(), "rig-fit", "map-button");

			// Create the right panel and its scrolling details area.
			var right = El(workspace, "right-panel");
			var details = Scroll(right, "rig-details");

			// Build the inventory list, which doubles as the drop zone.
			var inventory = El(right, "rig-inventory");
			inventoryDropZone = inventory;
			BuildRigInventory(inventory, categories);

			// Fill the details panel for the selected component.
			BuildRigDetails(details);
		}

		// Shown above the rack on every edit. Red entries block connecting; amber entries are advisory.
		private void BuildRigIssues(VisualElement map)
		{
			// Gather the issues and create the panel, hidden when there are none.
			var issues = State.RigIssues;
			var panel = El(map, "rig-issues");
			panel.name = "rig-issues";
			panel.style.display = issues.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;

			// Add one line per issue, styled by its severity.
			foreach (var issue in issues)
			{
				// Pick the label and style from the severity.
				var error = issue.severity == RigIssueSeverity.Error;
				Text(panel, (error ? "ERROR / " : "WARNING / ") + issue.message,
					 "small rig-issue " + (error ? "rig-issue-error" : "rig-issue-warning"));
			}
		}

		// Builds the RigBudgetActions section of the workstation UI.
		private void BuildRigBudgetActions(VisualElement parent)
		{
			// Create the action row and the build-status label.
			var actions = El(parent, "rig-budget-actions");
			Text(actions, State.RigValid ? "BUILD READY" : "BUILD INVALID / CONNECTION BLOCKED",
				 State.RigValid ? "micro lime" : "micro amber");

			// Offer the chassis expansion when a higher tier exists.
			if (State.NextChassis != null)
			{
				// Preview the expansion and create its button.
				var preview = State.PreviewChassis();
				var upgrade = Btn(
					actions, "EXPAND CHASSIS / " + State.NextChassis.title + " / " + State.NextChassis.price + " cr",
					() =>
					{
						// Expand the chassis and recenter the camera on the new grid.
						State.UpgradeChassis();
						rigCamera.Reset();
					}, "upgrade-chassis", "secondary-button");

				// Enable the button only when the upgrade is allowed, and show why not.
				upgrade.SetEnabled(preview.allowed);
				Text(actions, preview.reason, "micro muted");
			}
		}

		// Builds the RigBudgets section of the workstation UI.
		private void BuildRigBudgets(VisualElement head)
		{
			// Create the stat line and its palette colors.
			var budgets = El(head, "rig-budgets");
			var stats = El(budgets, "rig-stat-line");
			var muted = Palette.Hex("A8C2BC");
			var cyan = Palette.Hex("60D8D0");

			// Add one readout per power budget and resource stat.
			RigStat(stats, "power", muted, "POWER / " + State.PowerUsed + " / " + State.PowerLimit, "micro");
			RigStat(stats, "cooling", muted, "COOLING / " + State.HeatUsed + " / " + State.CoolingLimit, "micro");
			RigStat(stats, "processing", cyan, "COMPUTE / " + State.Compute.ToString("0.00") + " GFLOP/s",
					"micro cyan");
			RigStat(stats, "transfer", cyan, "BANDWIDTH / " + State.Bandwidth.ToString("0.00") + " GB/s", "micro cyan");
			RigStat(stats, "storage", cyan, "STORAGE / " + State.StorageCapacity.ToString("0") + " GB", "micro cyan");
			RigStat(stats, "memory", cyan, "RAM / " + State.MemoryPool.ToString("0.#") + " GB", "micro cyan");
			RigStat(stats, "reclaim", cyan, "RECLAIM / " + State.MemoryReclaim.ToString("0.#") + " GB/s", "micro cyan");
			RigStat(stats, "slots", cyan, "SLOTS / " + State.Slots, "micro cyan");
		}

		// Each readout pairs its value with the icon of the thing it measures.
		private static void RigStat(VisualElement line, string glyph, Color tint, string text, string classes)
		{
			OperationStat(line, glyph, tint, text, classes, 22);
		}

		// Produces the glyph text shown in the UI.
		private static Label OperationStat(VisualElement line, string glyph, Color tint, string text, string classes,
										   float iconSize = 36)
		{
			// Build the stat container with its icon.
			var stat = El(line, "rig-stat");
			var icon = new RigGlyph(glyph, tint, iconSize);
			icon.AddToClassList("rig-stat-icon");
			stat.Add(icon);

			// Add the readout text beside the icon.
			return RichText(stat, text, classes);
		}

		// Rig parts show their icon beside the title. The button text is cleared so both share one row.
		private static void SetPartContent(Button button, RigModuleDefinition model, string title, float iconSize)
		{
			// Clear the button text so the icon and title can share one row.
			button.text = "";

			// Build the row with the module icon.
			var content = new VisualElement();
			content.AddToClassList("rig-part-content");
			content.Add(RigGlyph.ForModule(model, Palette.Branch(model.branch), iconSize));

			// Build the title label.
			var label = new Label(title) { pickingMode = PickingMode.Ignore };
			label.AddToClassList("rig-part-title");
			content.Add(label);

			// Attach the row to the button.
			button.Add(content);
		}

		// Builds the RigDetails section of the workstation UI.
		private void BuildRigDetails(VisualElement info)
		{
			// Show the panel title and look up the selected part.
			PanelTitle(info, "SELECTED COMPONENT");
			var selected = State.Part(selectedRigId);

			// Show a prompt when no part is selected.
			if (selected == null)
			{
				Text(info, "Select a component in the rack or the inventory.", "body-copy muted section-space");
			}
			else
			{
				// Show the part name, branch, and install state in its branch color.
				var model = State.Module(selected.modelId);
				var branch = Palette.Branch(model.branch);
				Text(info, model.title, "mission-title section-space").style.color = branch;
				Text(info, model.branch + " / " + (model.family ?? "").ToUpperInvariant() + " / " +
						   (selected.Installed ? "INSTALLED" : "INVENTORY"), "micro section-space").style.color =
					branch;

				// Show the spec sheet and the current rotation.
				RichText(info, ModuleSpecs(model), "small");
				Text(info, "ROTATION / " + (selected.rotated ? "90" : "0") + " DEGREES", "micro muted");

				// Offer the component actions: place, rotate, remove, and sell.
				Btn(info, "PLACE IN FIRST FREE SPACE", () =>
				{
					// Report when the footprint has no free space on the rack.
					if (!State.PlaceFirstFree(selected.instanceId, selected.rotated))
					{
						rigMessage = "No valid space for this footprint.";
						dirty = true;
					}
				}, "rig-place-first", "secondary-button").SetEnabled(!State.Active);
				Btn(info, "ROTATE / R", RotateRigSelection, "rig-rotate", "secondary-button")
					.SetEnabled(!State.Active && model.rotatable && State.Catalog.Data.rig.allowRotation);
				Btn(info, "REMOVE TO INVENTORY", () => State.RemoveModule(selected.instanceId), "rig-remove",
					"secondary-button").SetEnabled(!State.Active && selected.Installed);
				Btn(info, "SELL / " + State.ModuleSalePrice(selected.instanceId) + " cr",
					() => State.SellModule(selected.instanceId), "rig-sell", "danger-button").SetEnabled(!State.Active);
			}

			// Show any pending rig message.
			if (rigMessage != null)
				Text(info, rigMessage, "small amber section-space");

			// Explain the editing rules and the sale return.
			Text(info, State.Active
					 ? "Disconnect before changing hardware."
					 : "Drag parts between the rack and the inventory, or select a part and click a cell. Sales return " +
					   (State.Catalog.Data.rig.saleFraction * 100).ToString("0") + "% of the paid price.",
				 "small muted section-space");

			// Extension hardware remains authorable for mods; the shipped catalog uses rack modules.
			foreach (var h in State.Catalog.Hardware)
			{
				// Create the upgrade button and enable it when the preview allows it.
				var button = Btn(info, h.title + " / " + State.UpgradePrice(h.id) + " cr", () => State.Upgrade(h.id),
								 "upgrade-" + h.id, "secondary-button");
				button.SetEnabled(State.PreviewUpgrade(h.id).allowed);
			}
		}

		// Internal member supporting workstation or graph viewport behavior.
		private GraphScene BuildRackScene()
		{
			// Create the document and register the empty-cell node type.
			var document = new GraphDocument("rig", "Rig rack");
			var schema = new GraphSchema();
			schema.Register(new GraphNodeTypeDefinition("empty", "Empty", "default", Palette.Hex("243A40"), .2f));

			// Register one node type per module branch, colored by branch.
			foreach (var branch in State.Modules.Select(m => m.branch).Distinct())
				schema.Register(new GraphNodeTypeDefinition(branch, branch, "default", Palette.Branch(branch), .6f));

			// Add an empty slot node for every chassis cell.
			for (var row = 0; row < State.Chassis.rows; row++)
				for (var col = 0; col < State.Chassis.columns; col++)
				{
					// Slot nodes are keyed by their row-major index.
					var index = row * State.Chassis.columns + col;
					document.UpsertNode(new GraphNodeRecord("slot-" + index, "empty", ""));
				}

			// Add a node for each installed part.
			foreach (var part in State.InstalledParts)
				document.UpsertNode(new GraphNodeRecord("part-" + part.instanceId, State.Module(part.modelId).branch,
														State.Module(part.modelId).title));

			// Build the scene from the document and its schema.
			var scene = new GraphScene(document, schema);

			// Pin each cell node to its grid position.
			for (var row = 0; row < State.Chassis.rows; row++)
				for (var col = 0; col < State.Chassis.columns; col++)
				{
					var node = scene.Nodes["slot-" + (row * State.Chassis.columns + col)];
					node.Position = new Vector3(col * 4, -row * 4, 0);
					node.Pinned = true;
				}

			// Center each installed part node over its footprint and pin it.
			foreach (var part in State.InstalledParts)
			{
				var size = State.PartSize(part);
				var node = scene.Nodes["part-" + part.instanceId];
				node.Position = new Vector3((part.column + (size.x - 1) * .5f) * 4,
											-(part.row + (size.y - 1) * .5f) * 4, 0);
				node.Pinned = true;
			}

			return scene;
		}

		// Produces the ModuleTooltip text shown in the UI.
		private string ModuleTooltip(RigModuleDefinition model)
		{
			return model.title + " / " + ModuleSpecs(model);
		}

		// Same layout as the graph node intel card: lime title over light body text.
		private string ModuleIntel(RigModuleDefinition model)
		{
			return "<color=#9BFACB><b>" + model.title + "</b></color>\n" + ModuleSpecs(model);
		}

		// Produces the ModuleSpecs text shown in the UI.
		private string ModuleSpecs(RigModuleDefinition model)
		{
			return "TIER " + model.tier + " / " + model.width + "x" + model.height + "\n" + model.description + "\n" +
				   model.powerDemand + " POWER / " + model.heatOutput + " HEAT / +" + model.powerCapacity +
				   " POWER LIMIT / +" + model.coolingCapacity + " COOLING LIMIT\n" + model.synergy + "\n" +
				   model.flavour;
		}

		// Internal member supporting workstation or graph viewport behavior.
		private IEnumerable<string> RigCategories()
		{
			return State.Modules.Select(m => m.family).Where(f => !string.IsNullOrEmpty(f)).Distinct()
						.OrderBy(f =>
						{
							var i = Array.IndexOf(RigCategoryOrder, f);
							return i < 0 ? RigCategoryOrder.Length : i;
						}).ThenBy(f => f, StringComparer.Ordinal);
		}

		// Produces the RigCategoryTitle text shown in the UI.
		private static string RigCategoryTitle(string category)
		{
			return category == "ALL" ? "ALL PARTS" : category.ToUpperInvariant();
		}

		// Builds the RigShopFilters section of the workstation UI.
		private void BuildRigShopFilters(VisualElement parent, List<string> categories)
		{
			// Create the tab row with an ALL tab followed by each category.
			var tabs = El(parent, "rig-category-tabs rig-shop-category-tabs");
			var tabKeys = new List<string> { "ALL" };
			tabKeys.AddRange(categories);

			// Add one tab per category.
			foreach (var category in tabKeys)
			{
				// Capture the category and whether its tab is the active filter.
				var key = category;
				var active = shopCategory == key;

				// Select this category when its tab is clicked.
				var tab = Btn(tabs, "", () =>
				{
					// Filter the shop to this category and redraw.
					shopCategory = key;
					dirty = true;
					Beep();
				}, "shop-category-" + key, "rig-category-tab");

				// Mark the active tab, label it, and add its icon.
				tab.EnableInClassList("active", active);
				tab.tooltip = RigCategoryTitle(key);
				tab.Add(new RigGlyph(key, active ? Palette.Lime : Palette.Hex("7C919D"), 24));
			}
		}

		// Builds the RigInventory section of the workstation UI.
		private void BuildRigInventory(VisualElement parent, List<string> categories)
		{
			// Create the inventory heading and the category tab row.
			Text(parent, "INVENTORY", "eyebrow cyan");
			var tabs = El(parent, "rig-category-tabs");
			var tabKeys = new List<string> { "ALL" };
			tabKeys.AddRange(categories);

			// Add one tab per category.
			foreach (var category in tabKeys)
			{
				// Capture the category and whether its tab is the active filter.
				var key = category;
				var active = rigCategory == key;

				// Select this category when its tab is clicked.
				var tab = Btn(tabs, "", () =>
				{
					// Filter the inventory to this category and redraw.
					rigCategory = key;
					dirty = true;
					Beep();
				}, "rig-category-" + key, "rig-category-tab");

				// Mark the active tab, label it, and add its icon.
				tab.EnableInClassList("active", active);
				tab.tooltip = RigCategoryTitle(key);
				tab.Add(new RigGlyph(key, active ? Palette.Lime : Palette.Hex("7C919D"), 30));
			}

			// Collect the spare parts that match the category filter.
			var parts = State.Career.rigParts
							 .Where(p => !p.Installed &&
										 (rigCategory == "ALL" || State.Module(p.modelId).family == rigCategory))
							 .ToList();

			// Show the spare count and create the scrolling list.
			Text(parent, RigCategoryTitle(rigCategory) + " / " + parts.Count + " SPARE", "eyebrow section-space");
			var list = Scroll(parent, "rig-inventory-list");

			// Show a placeholder row when no spare parts match.
			if (parts.Count == 0)
			{
				var emptyRow = El(list, "rig-inventory-row rig-inventory-empty");
				Text(emptyRow, "EMPTY", "micro muted");
			}

			// One row per spare part; click to select, drag to place.
			foreach (var part in parts)
			{
				// Create the row button that selects this spare part.
				var model = State.Module(part.modelId);
				var row = Btn(list, "", () =>
				{
					// Select this spare part and clear any pending message.
					selectedRigId = part.instanceId;
					rigMessage = null;
					dirty = true;
				}, "owned-" + part.instanceId, "ability-button rig-inventory-row");

				// Fill the row with the icon, title, and branch color.
				SetPartContent(row, model, (part.instanceId == selectedRigId ? "> " : "") + model.title, 52);
				row.tooltip = ModuleIntel(model);
				row.AddToClassList(IntelTooltipClass);
				row.style.color = Palette.Branch(model.branch);

				// Start a rack drag when the row is pressed.
				row.RegisterCallback<PointerDownEvent>(evt => BeginRigDrag(part.instanceId, evt),
													   TrickleDown.TrickleDown);
			}
		}

		// Handles SlotClicked for the workstation or graph UI.
		private void SlotClicked(string id)
		{
			// Ignore clicks while an operation is running.
			if (State.Active)
				return;

			// Select the part that was clicked and stop.
			if (id.StartsWith("part-"))
			{
				selectedRigId = id.Substring(5);
				rigMessage = null;
				dirty = true;

				return;
			}

			// Ignore clicks on anything other than a grid slot.
			if (!id.StartsWith("slot-"))
				return;

			// Decode the slot index into its row and column.
			var index = int.Parse(id.Substring(5));
			int row = index / State.Chassis.columns, column = index % State.Chassis.columns;

			// Try to place the selected part with its anchor at the clicked cell.
			var part = State.Part(selectedRigId);
			if (part != null && State.TryPlaceModule(part.instanceId, row, column, part.rotated))
			{
				rigMessage = null;

				return;
			}

			// Otherwise select the part covering the cell, or explain why the footprint does not fit.
			var occupied = State.PartAt(row, column);
			if (occupied != null)
				selectedRigId = occupied.instanceId;
			else
				rigMessage = "The selected footprint does not fit here.";

			dirty = true;
		}

		// Handles RotateRigSelection for the workstation or graph UI.
		private void RotateRigSelection()
		{
			// Rotate the part being dragged instead when a drag is in progress.
			if (dragRigId != null)
			{
				RotateRigDrag();

				return;
			}

			// Rotate the selected part; explain when rotation is blocked.
			if (selectedRigId != null && !State.RotateModule(selectedRigId))
			{
				rigMessage = "Rotation is blocked. Remove the part to inventory or choose another cell.";
				dirty = true;
			}
		}

		// Handles RotateRigDrag for the workstation or graph UI.
		internal void RotateRigDrag()
		{
			// Nothing to rotate without an active drag.
			if (dragRigId == null)
				return;

			// Look up the module and check that it may rotate.
			var model = State.Module(State.Part(dragRigId).modelId);
			if (!model.rotatable || !State.Catalog.Data.rig.allowRotation)
				return;

			// Toggle the drag rotation and refresh the ghost once it is on screen.
			dragRotated = !dragRotated;
			if (dragMoved)
				RefreshRigDragGhost();
		}

		// Handles BeginRigDrag for the workstation or graph UI.
		private void BeginRigDrag(string instance, PointerDownEvent evt)
		{
			// Ignore drags during an operation or from non-primary buttons.
			if (State.Active || evt.button != 0)
				return;

			// Look up the part and select it for the drag.
			var part = State.Part(instance);
			dragRigId = selectedRigId = instance;

			// Record the pointer and the pre-drag state so a cancel can restore it.
			dragPointer = evt.pointerId;
			dragRotated = part.rotated;
			dragMoved = false;
			dragLifted = false;
			dragRestoreRow = part.row;
			dragRestoreCol = part.column;
			dragRestoreRot = part.rotated;

			// Record where the drag started and capture the pointer.
			dragOrigin = lastRigDragPanelPos = evt.position;
			Root.CapturePointer(evt.pointerId);

			// Consume the event so other handlers do not process it.
			evt.StopImmediatePropagation();
		}

		// Tries to NearestRigAnchor and returns whether it succeeded.
		private bool TryNearestRigAnchor(Vector2 panelPosition, Vector2Int size, out int row, out int column)
		{
			// Start with zeroed outputs.
			row = column = 0;

			// Bail out when there is no canvas or the footprint is empty.
			if (canvas == null || size.x <= 0 || size.y <= 0)
				return false;

			// Read the grid dimensions and its panel-space bounds.
			int cols = State.Chassis.columns, rows = State.Chassis.rows;
			if (!TryGetRigGridBounds(out var grid, out var cellW, out var cellH))
				return false;

			// Reject pointers outside the grid.
			if (!grid.Contains(panelPosition))
				return false;

			// Find the cell whose center is closest to the pointer.
			int nearestRow = 0, nearestCol = 0;
			var nearestDist = float.MaxValue;
			for (var r = 0; r < rows; r++)
				for (var c = 0; c < cols; c++)
				{
					// Skip cells whose panel position cannot be measured.
					if (!TrySlotPanelCenter(r, c, out var center))
						continue;

					// Track the closest center seen so far.
					var dist = (center - panelPosition).sqrMagnitude;
					if (dist < nearestDist)
					{
						nearestDist = dist;
						nearestRow = r;
						nearestCol = c;
					}
				}

			// Clamp the anchor so the whole footprint stays on the grid.
			row = Mathf.Clamp(nearestRow, 0, rows - size.y);
			column = Mathf.Clamp(nearestCol, 0, cols - size.x);

			return true;
		}

		// Tries to RigGridMetrics and returns whether it succeeded.
		private bool TryRigGridMetrics(out Rect originCell, out float cellWidth, out float cellHeight)
		{
			// Start with empty outputs.
			originCell = default;
			cellWidth = cellHeight = 0;

			// Require a live canvas and a measurable origin cell.
			if (canvas == null)
				return false;

			int cols = State.Chassis.columns, rows = State.Chassis.rows;
			if (!TrySlotButtonRect(0, 0, out originCell))
				return false;

			// Start from the origin cell size.
			cellWidth = originCell.width;
			cellHeight = originCell.height;

			// Derive the true pitch from the neighboring cells when they exist.
			if (cols > 1 && TrySlotButtonRect(0, 1, out var nextColumn))
				cellWidth = nextColumn.xMin - originCell.xMin;
			if (rows > 1 && TrySlotButtonRect(1, 0, out var nextRow))
				cellHeight = nextRow.yMin - originCell.yMin;

			// Accept the metrics only when both sizes are usable.
			return cellWidth > 1 && cellHeight > 1;
		}

		// Tries to GetRigGridBounds and returns whether it succeeded.
		private bool TryGetRigGridBounds(out Rect grid, out float cellWidth, out float cellHeight)
		{
			// Start with an empty grid rect.
			grid = default;

			// Measure the cells first; bail out when they are unusable.
			if (!TryRigGridMetrics(out var origin, out cellWidth, out cellHeight))
				return false;

			// Build the panel-space rect that covers the whole grid.
			int cols = State.Chassis.columns, rows = State.Chassis.rows;
			grid = new Rect(origin.xMin, origin.yMin, cellWidth * cols, cellHeight * rows);

			return grid.width > 1 && grid.height > 1;
		}

		// Tries to SlotButtonRect and returns whether it succeeded.
		private bool TrySlotButtonRect(int row, int column, out Rect rect)
		{
			// Start with an empty rect.
			rect = default;

			// Require a live canvas.
			if (canvas == null)
				return false;

			// Find the slot button for this cell.
			var cols = State.Chassis.columns;
			var slot = canvas.Q<Button>("slot-" + (row * cols + column));
			if (slot == null)
				return false;

			// Use its world bounds, provided they are non-degenerate.
			rect = slot.worldBound;

			return rect.width > 1 && rect.height > 1;
		}

		// Tries to SlotPanelCenter and returns whether it succeeded.
		private bool TrySlotPanelCenter(int row, int column, out Vector2 center)
		{
			// Start with an empty center.
			center = default;

			// Fail when the cell rect cannot be measured.
			if (!TrySlotPanelRect(row, column, out var rect))
				return false;

			// Convert the rect center into a vector.
			center = new Vector2(rect.center.x, rect.center.y);

			return true;
		}

		// Tries to SlotPanelRect and returns whether it succeeded.
		private bool TrySlotPanelRect(int row, int column, out Rect rect)
		{
			// Start with an empty rect.
			rect = default;

			// Measure the grid cells first.
			if (!TryRigGridMetrics(out var origin, out var cellW, out var cellH))
				return false;

			// Offset the origin by the cell's row and column.
			rect = new Rect(origin.xMin + column * cellW, origin.yMin + row * cellH, cellW, cellH);

			return true;
		}

		// Handles UpdateRigDrag for the workstation or graph UI.
		private void UpdateRigDrag(Vector2 position)
		{
			// Ignore moves when no drag is active.
			if (dragRigId == null)
				return;

			// Track the pointer position for the ghost and drop targets.
			lastRigDragPanelPos = position;

			// Treat the gesture as a drag only after the pointer moves more than 5 pixels.
			dragMoved |= Vector2.Distance(position, dragOrigin) > 5;
			if (!dragMoved)
				return;

			// Lift the part out of the rack on the first real movement.
			if (!dragLifted)
				LiftRigDragPart();

			// Move the ghost and highlight the inventory drop zone while hovering it.
			RefreshRigDragGhost();
			inventoryDropZone?.EnableInClassList("rig-drop-zone", IsOverRigInventory(position));
		}

		// Returns whether IsOverRigInventory for the current UI or rig state.
		private bool IsOverRigInventory(Vector2 panelPosition)
		{
			return inventoryDropZone != null && inventoryDropZone.worldBound.Contains(panelPosition);
		}

		// Handles LiftRigDragPart for the workstation or graph UI.
		private void LiftRigDragPart()
		{
			// Mark the drag as lifted so a cancel knows to restore the part.
			dragLifted = true;

			// Take an installed part off the grid while it is being dragged.
			var part = State.Part(dragRigId);
			if (part.Installed)
				State.RemoveModule(dragRigId);

			// Reveal the cells the part used to occupy.
			RevealEmptyRigSlots();

			// Hide the part's button; the ghost stands in for it.
			var lifted = canvas?.Q<Button>("part-" + dragRigId);
			if (lifted != null)
				lifted.style.display = DisplayStyle.None;

			// Create the ghost that follows the pointer.
			EnsureRigDragGhost(State.Module(part.modelId));
		}

		// Handles RestoreRigDragPart for the workstation or graph UI.
		private void RestoreRigDragPart()
		{
			// Only restore a part that was actually lifted from a valid position.
			if (!dragLifted || dragRestoreRow < 0)
				return;

			// Put the part back where the drag started.
			State.TryPlaceModule(dragRigId, dragRestoreRow, dragRestoreCol, dragRestoreRot);
		}

		// Handles RevealEmptyRigSlots for the workstation or graph UI.
		private void RevealEmptyRigSlots()
		{
			// Nothing to reveal without a canvas.
			if (canvas == null)
				return;

			// Show each slot whose cell is now empty.
			var cols = State.Chassis.columns;
			foreach (var btn in canvas.Query<Button>().ToList())
			{
				// Skip buttons that are not grid slots.
				if (!btn.name.StartsWith("slot-"))
					continue;

				// Decode the slot index and reveal it when its cell is empty.
				var index = int.Parse(btn.name.Substring(5));
				if (State.PartAt(index / cols, index % cols) == null)
					btn.style.display = DisplayStyle.Flex;
			}
		}

		// Handles EnsureRigDragGhost for the workstation or graph UI.
		private void EnsureRigDragGhost(RigModuleDefinition model)
		{
			// Reuse the existing ghost while a drag is in progress.
			if (dragGhost != null)
				return;

			// Create a non-interactive button styled like the part.
			var branch = Palette.Branch(model.branch);
			dragGhost = new Button { name = "rig-drag-ghost", pickingMode = PickingMode.Ignore };
			dragGhost.AddToClassList("rig-module");
			dragGhost.AddToClassList("rig-drag-ghost");
			SetPartContent(dragGhost, model, model.title, 18);

			// Float the ghost and tint its background and borders with the branch color.
			dragGhost.style.position = Position.Absolute;
			dragGhost.style.backgroundColor = new Color(branch.r * .22f, branch.g * .22f, branch.b * .22f, .94f);
			dragGhost.style.borderBottomColor = dragGhost.style.borderTopColor = branch;
			dragGhost.style.borderLeftColor = dragGhost.style.borderRightColor = branch;

			// Attach the ghost to the root so it draws above the UI.
			Root.Add(dragGhost);
		}

		// Handles RefreshRigDragGhost for the workstation or graph UI.
		private void RefreshRigDragGhost()
		{
			// Look up the dragged part; nothing to refresh without one.
			var part = State.Part(dragRigId);
			if (part == null)
				return;

			// Make sure the ghost exists and measure the footprint at the current rotation.
			var model = State.Module(part.modelId);
			EnsureRigDragGhost(model);
			var size = GameSimulation.PartSize(model, dragRotated);

			// Clear last frame's highlights before computing new ones.
			ClearDragDropHighlights();

			// Find the anchor nearest the pointer and check whether it can be placed.
			int row = 0, column = 0;
			var overRack = TryNearestRigAnchor(lastRigDragPanelPos, size, out row, out column);
			var valid = overRack && State.CanPlace(dragRigId, row, column, dragRotated);

			// Highlight the landing cells and mark the ghost invalid when they cannot take it.
			if (overRack)
				HighlightFootprint(row, column, size, valid);
			dragGhost.EnableInClassList("rig-drag-invalid", overRack && !valid);

			// Move the ghost under the pointer.
			PositionRigDragGhost(size);
		}

		// Handles ClearDragDropHighlights for the workstation or graph UI.
		private void ClearDragDropHighlights()
		{
			// Remove the drop classes from every highlighted slot.
			foreach (var slot in dragValidSlots)
			{
				slot.RemoveFromClassList("rig-drop-valid");
				slot.RemoveFromClassList("rig-drop-invalid");
			}

			// Forget the highlighted slots.
			dragValidSlots.Clear();
		}

		// Handles HighlightFootprint for the workstation or graph UI.
		private void HighlightFootprint(int row, int column, Vector2Int size, bool valid)
		{
			// Pick the highlight class from whether the footprint fits.
			var cols = State.Chassis.columns;
			var cls = valid ? "rig-drop-valid" : "rig-drop-invalid";

			// Highlight every cell the footprint would cover.
			for (var y = row; y < row + size.y; y++)
				for (var x = column; x < column + size.x; x++)
				{
					// Skip cells that have no slot button.
					var slot = canvas?.Q<Button>("slot-" + (y * cols + x));
					if (slot == null)
						continue;

					// Show the slot and apply the highlight class.
					slot.style.display = DisplayStyle.Flex;
					slot.AddToClassList(cls);
					dragValidSlots.Add(slot);
				}
		}

		// Internal member supporting workstation or graph viewport behavior.
		private Vector2 RigCellPanelSize()
		{
			// Measure an empty cell when one is laid out.
			var cell = canvas?.Q<Button>(className: "rig-cell");
			if (cell != null && cell.worldBound.width > 1)
				return new Vector2(cell.worldBound.width, cell.worldBound.height);

			// Fall back to a default size before layout has run.
			return new Vector2(72, 48);
		}

		// The ghost always follows the cursor, RPG-inventory style. Grid cells show the landing footprint instead of snapping the ghost.
		private void PositionRigDragGhost(Vector2Int size)
		{
			// Size the ghost to the footprint at cell scale.
			var cell = RigCellPanelSize();
			float width = cell.x * size.x, height = cell.y * size.y;

			// Center the ghost on the pointer.
			var pointer = Root.WorldToLocal(lastRigDragPanelPos);
			dragGhost.style.left = pointer.x - width * .5f;
			dragGhost.style.top = pointer.y - height * .5f;
			dragGhost.style.width = width;
			dragGhost.style.height = height;
		}

		// Handles EndRigDrag for the workstation or graph UI.
		private void EndRigDrag(PointerUpEvent evt)
		{
			// Ignore releases that do not belong to the active drag.
			if (dragRigId == null || evt.pointerId != dragPointer)
				return;

			// Track whether the drop placed the part somewhere.
			var placed = false;

			// Dropping over the inventory leaves the part unplaced.
			if (dragMoved && IsOverRigInventory(evt.position))
			{
				// Dropping on the inventory keeps a lifted part out of the rack (it is already unplaced).
				placed = true;
				rigMessage = null;
			}
			else if (dragMoved)
			{
				// Snap to the nearest anchor on the rack and try to place the part there.
				var part = State.Part(dragRigId);
				var size = GameSimulation.PartSize(State.Module(part.modelId), dragRotated);
				if (TryNearestRigAnchor(evt.position, size, out var row, out var column))
					placed = State.TryPlaceModule(dragRigId, row, column, dragRotated);

				// Explain the failure when the rack was reachable but the footprint did not fit.
				if (!placed && TryNearestRigAnchor(evt.position, size, out _, out _))
					rigMessage = "That footprint does not fit here.";
			}

			// End the drag, restoring the part if it was not placed, then redraw.
			CancelRigDrag(placed);
			dirty = true;

			// Consume the release so it does not reach the rest of the UI.
			evt.StopPropagation();
		}

		// Handles CancelRigDrag for the workstation or graph UI.
		private void CancelRigDrag(bool placed = false)
		{
			// Put a lifted part back where the drag started unless it was placed.
			if (dragLifted && !placed)
				RestoreRigDragPart();

			// Release the pointer capture taken at drag start.
			if (dragRigId != null && Root != null && Root.HasPointerCapture(dragPointer))
				Root.ReleasePointer(dragPointer);

			// Clear the drop highlights and the inventory drop-zone styling.
			ClearDragDropHighlights();
			inventoryDropZone?.RemoveFromClassList("rig-drop-zone");

			// Reset the drag state.
			dragRigId = null;
			dragLifted = false;
			dragRestoreRow = -1;

			// Remove the ghost from the hierarchy.
			dragGhost?.RemoveFromHierarchy();
			dragGhost = null;
		}

		// Handles RefreshResourceActions for the workstation or graph UI.
		private void RefreshResourceActions()
		{
			RefreshActionBar();
		}

		// Builds the StorageBrief section of the workstation UI.
		private void BuildStorageBrief(VisualElement parent)
		{
			// Generate the job's network and read its contract definition.
			var network = NetworkGenerator.Generate(selectedJob, State.Career, State.Catalog);
			var definition = State.Catalog.Contract(selectedJob.id);

			// Heading for the storage section.
			Text(parent, "STORAGE PLAN", "eyebrow section-space");

			// Show each objective's storage need, or that it is remote.
			foreach (var objective in definition.objectives)
			{
				var node = network.nodes.First(n => n.id == objective.nodeId);
				RichText(parent,
						 (string.IsNullOrEmpty(objective.label) ? node.label : objective.label) + ": " +
						 (objective.payload ? node.storage.ToString("0") + " GB" : "REMOTE / NO STORAGE"), "small");
			}

			// Number the archives in order unless the intel is full.
			var index = 0;

			// List the bonus archives with their storage sizes.
			foreach (var archive in network.nodes.Where(n => n.bonus))
				Text(parent,
					 (selectedJob.Intel == ContractIntel.Full ? archive.label : "ARCHIVE " + ++index) + ": " +
					 archive.storage.ToString("0") + " GB", "small muted");

			// Compare the payload storage needed with the drive capacity.
			var required = network.nodes.Where(n => n.payload).Sum(n => n.storage);
			RichText(parent,
					 "NEEDS " + required.ToString("0") + " GB / YOUR STORAGE " + State.StorageCapacity.ToString("0") +
					 " GB", "small cyan");

			// Draw the storage meter, which turns red when the payloads do not fit.
			var track = El(parent, "meter-track");
			var fill = El(track, "meter-fill");
			fill.style.width = Length.Percent(Mathf.Clamp01(required / Mathf.Max(1, State.StorageCapacity)) * 100);
			fill.style.backgroundColor = required <= State.StorageCapacity ? Palette.Cyan : Palette.Red;
		}

		// Builds the RefundModal section of the workstation UI.
		private void BuildRefundModal()
		{
			// Create the modal overlay and its scrolling box, then animate it open.
			overlay = El(Root, "modal-overlay");
			var box = Scroll(overlay, "modal-box");
			OpenModalWithMotion(box);

			// Heading and the refund summary text.
			Text(box, "REFUND RESEARCH", "large-heading");
			Text(box, State.PreviewResearchRefund().description, "body-copy");

			// Confirm the refund; disabled while an operation is running.
			Btn(box, "CONFIRM REFUND", () =>
			{
				// Apply the refund, close the modal, and redraw.
				State.RefundResearch();
				modal = null;
				dirty = true;
			}, "confirm-research-refund", "primary-button").SetEnabled(!State.Active);

			// Close the modal without refunding.
			Btn(box, "CANCEL", () =>
			{
				// Dismiss the modal and redraw.
				modal = null;
				dirty = true;
			}, "cancel-research-refund", "secondary-button");
		}
	}
}