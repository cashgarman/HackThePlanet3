using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using static Cashg.GraphExplorer.GraphUi;

namespace Cashg.GraphExplorer
{
	/// <summary>Defines an action that can be shown for an inspected graph node.</summary>
	public sealed class GraphInspectorAction
	{
		/// <summary>Creates an inspector action and its visibility and availability rules.</summary>
		/// <param name="id">Stable action identifier.</param>
		/// <param name="label">Text shown on the action button.</param>
		/// <param name="execute">Callback invoked with the selected node.</param>
		/// <param name="isVisible">Optional predicate that controls whether the action is shown.</param>
		/// <param name="isEnabled">Optional predicate that controls whether the action can run.</param>
		/// <param name="disabledReason">Optional text shown when the action is disabled.</param>
		/// <param name="tooltip">Help text shown for the action.</param>
		public GraphInspectorAction(string id, string label, Action<IGraphNode> execute,
									Func<IGraphNode, bool> isVisible = null, Func<IGraphNode, bool> isEnabled = null,
									Func<IGraphNode, string> disabledReason = null, string tooltip = "")
		{
			if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("An action ID is required.", nameof(id));
			if (string.IsNullOrWhiteSpace(label))
				throw new ArgumentException("An action label is required.", nameof(label));
			Id = id;
			Label = label;
			Execute = execute ?? throw new ArgumentNullException(nameof(execute));
			IsVisible = isVisible ?? (_ => true);
			IsEnabled = isEnabled ?? (_ => true);
			DisabledReason = disabledReason;
			Tooltip = tooltip ?? "";
		}

		/// <summary>Gets the stable identifier used to name the action button.</summary>
		public string Id { get; }

		/// <summary>Gets the button label.</summary>
		public string Label { get; }

		/// <summary>Gets the action help text.</summary>
		public string Tooltip { get; }

		/// <summary>Gets the predicate that determines whether the action is shown for a node.</summary>
		public Func<IGraphNode, bool> IsVisible { get; }

		/// <summary>Gets the predicate that determines whether the action is enabled for a node.</summary>
		public Func<IGraphNode, bool> IsEnabled { get; }

		/// <summary>Gets the optional reason displayed when the action is disabled.</summary>
		public Func<IGraphNode, string> DisabledReason { get; }

		/// <summary>Gets the callback invoked when the action is activated.</summary>
		public Action<IGraphNode> Execute { get; }
	}

	/// <summary>Configures optional content and callbacks used by the graph inspector.</summary>
	public sealed class GraphInspectorOptions
	{
		/// <summary>Gets or sets actions available for the inspected node.</summary>
		public IReadOnlyList<GraphInspectorAction> Actions = Array.Empty<GraphInspectorAction>();

		/// <summary>Gets or sets a callback that adds actions to a relationship row.</summary>
		public Action<VisualElement, IGraphEdge> AppendRelationshipActions;

		/// <summary>Gets or sets a callback that adds custom sections to the inspector.</summary>
		public Action<VisualElement, IGraphNode> AppendSections;

		/// <summary>Gets or sets whether custom sections appear before node properties.</summary>
		public bool AppendSectionsBeforeProperties;

		/// <summary>Gets or sets the callback used to bind tooltip text to an element.</summary>
		public Action<VisualElement, string> BindTip = (element, text) => element.tooltip = text;

		/// <summary>Gets or sets whether the properties section starts collapsed.</summary>
		public bool CollapseProperties;

		/// <summary>Gets or sets a callback that decorates disclosure foldouts.</summary>
		public Action<Foldout> Decorate = foldout => { };

		/// <summary>Gets or sets a callback that clears tracked tooltips from an element tree.</summary>
		public Action<VisualElement> ForgetTips = element => { };

		/// <summary>Gets or sets the callback used to open a source reference.</summary>
		public Action<string> OpenSource;

		/// <summary>Gets or sets the callback invoked when a related node is selected.</summary>
		public Action<IGraphNode> Select;
	}

	/// <summary>Provides display data and interaction callbacks for an inspector relationship row.</summary>
	public sealed class GraphRelationRow
	{
		/// <summary>Gets or sets the color associated with the related node type.</summary>
		public Color Color;

		/// <summary>Gets or sets the graph edge represented by this row.</summary>
		public IGraphEdge Edge;

		/// <summary>Gets or sets a factory that creates the related node icon.</summary>
		public Func<VisualElement> Icon;

		/// <summary>Gets or sets the related node label.</summary>
		public string Label;

		/// <summary>Gets or sets the color associated with the relationship type.</summary>
		public Color RelationColor;

		/// <summary>Gets or sets the relationship label.</summary>
		public string RelationLabel;

		/// <summary>Gets or sets the callback invoked when this row is selected.</summary>
		public Action Select;

		/// <summary>Gets or sets the related node type label.</summary>
		public string TypeLabel;
	}

	/// <summary>Builds the graph node inspector and its relationship controls.</summary>
	public static class GraphInspectorPresenter
	{
		private static Color RelationInk(Color color)
		{
			Color.RGBToHSV(color, out var h, out var s, out var v);
			return Color.HSVToRGB(h, Mathf.Clamp01(s * 1.15f), Mathf.Clamp01(Mathf.Max(v, .84f)));
		}

		/// <summary>Applies the graph explorer disclosure styling to a foldout.</summary>
		/// <param name="foldout">Foldout to style.</param>
		public static void StyleDisclosure(Foldout foldout)
		{
			// Remove built-in checkmarks and reserve a consistent slot for the disclosure chevron.
			foldout.AddToClassList("inspector-disclosure");
			foreach (var extra in foldout.Query(className: "unity-foldout__checkmark").ToList())
				extra.style.display = DisplayStyle.None;
			var toggle = foldout.Q<Toggle>(className: "unity-foldout__toggle");
			if (toggle == null) return;
			toggle.style.flexDirection = FlexDirection.Row;
			toggle.style.alignItems = Align.Center;
			var input = toggle.Q(className: "unity-toggle__input");
			if (input != null)
			{
				input.style.backgroundColor = Color.clear;
				input.style.borderTopWidth = 0;
				input.style.borderBottomWidth = 0;
				input.style.borderLeftWidth = 0;
				input.style.borderRightWidth = 0;
				input.style.width = 16;
				input.style.minWidth = 16;
				input.style.height = 16;
				input.style.marginRight = 6;
				input.style.alignItems = Align.Center;
				input.style.justifyContent = Justify.Center;
				toggle.Insert(0, input);
			}

			var checkmark = toggle.Q(className: "unity-toggle__checkmark");
			if (checkmark != null) checkmark.style.display = DisplayStyle.None;
			var foldoutMark = toggle.Q(className: "unity-foldout__checkmark");
			if (foldoutMark != null) foldoutMark.style.display = DisplayStyle.None;
			var glyph = new VisualElement { pickingMode = PickingMode.Ignore };
			glyph.style.width = 12;
			glyph.style.height = 12;
			glyph.style.flexShrink = 0;
			glyph.generateVisualContent += context =>
			{
				// Paint an expanded or collapsed chevron from the foldout's current value.
				var rect = glyph.contentRect;
				var painter = context.painter2D;
				painter.strokeColor = new Color(.96f, .98f, 1f);
				painter.lineWidth = 2.5f;
				painter.BeginPath();
				if (foldout.value)
				{
					painter.MoveTo(new Vector2(rect.x + 1, rect.y + 3));
					painter.LineTo(new Vector2(rect.x + 6, rect.y + 8));
					painter.LineTo(new Vector2(rect.x + 11, rect.y + 3));
				}
				else
				{
					painter.MoveTo(new Vector2(rect.x + 3, rect.y + 1));
					painter.LineTo(new Vector2(rect.x + 8, rect.y + 6));
					painter.LineTo(new Vector2(rect.x + 3, rect.y + 11));
				}

				painter.Stroke();
			};
			glyph.RegisterCallback<GeometryChangedEvent>(_ => glyph.MarkDirtyRepaint());
			foldout.RegisterValueChangedCallback(_ => glyph.MarkDirtyRepaint());
			if (input != null) input.Add(glyph);
			else toggle.Insert(0, glyph);
		}

		/// <summary>Adds a node type, title, and summary header to an inspector element.</summary>
		/// <param name="parent">Element that receives the header.</param>
		/// <param name="type">Node type label.</param>
		/// <param name="label">Node title.</param>
		/// <param name="color">Accent color used for the type label.</param>
		/// <param name="summary">Summary text shown below the title.</param>
		public static void Header(VisualElement parent, string type, string label, Color color, string summary)
		{
			var tag = Label(type.ToUpperInvariant(), "eyebrow");
			tag.AddToClassList("shell-semantic-tag");
			tag.style.borderLeftColor = color;
			parent.Add(tag);
			parent.Add(Label(label, "inspector-title"));
			Note(parent, summary);
		}

		/// <summary>Builds a searchable list of relationship rows for a node.</summary>
		/// <param name="parent">Element that receives the relationship controls.</param>
		/// <param name="nodeId">Identifier of the inspected node.</param>
		/// <param name="total">Total relationship count before filtering.</param>
		/// <param name="query">Returns the current search text.</param>
		/// <param name="setQuery">Stores changed search text.</param>
		/// <param name="rows">Provides the relationship rows to display.</param>
		/// <param name="hover">Updates the currently hovered edge.</param>
		/// <param name="focus">Updates the currently focused edge.</param>
		/// <param name="options">Inspector callbacks and display options.</param>
		public static void Relationships(VisualElement parent, string nodeId, int total, Func<string> query,
										 Action<string> setQuery,
										 Func<IEnumerable<GraphRelationRow>> rows, Action<IGraphEdge> hover,
										 Action<IGraphEdge> focus, GraphInspectorOptions options)
		{
			parent.Add(Label("Search relationships", "eyebrow"));
			var search = new TextField { value = query(), name = "connection-search" };
			search.AddToClassList("nolabel");
			HideFieldLabel(search);
			options.BindTip(search, "Search all loaded relationships by entity, type, or relationship name.");
			parent.Add(search);
			var count = Label("", "note");
			parent.Add(count);
			var connections = Element("connections-list");
			parent.Add(connections);
			IGraphEdge activeHover = null, activeFocus = null;

			void Populate()
			{
				// Clear stale hover/focus highlights and tooltip bindings before rebuilding relationship rows.
				activeHover = null;
				activeFocus = null;
				hover(null);
				focus(null);
				options.ForgetTips(connections);
				connections.Clear();
				var shown = 0;
				foreach (var row in rows())
				{
					// Search entity, type, and relation labels together without changing the graph's filters.
					var text = row.Label + " " + row.TypeLabel + " " + row.Edge.Relation + " " + row.RelationLabel;
					if (!string.IsNullOrWhiteSpace(query()) &&
						text.IndexOf(query().Trim(), StringComparison.OrdinalIgnoreCase) < 0) continue;
					shown++;
					var group = Element("connection");
					connections.Add(group);
					group.RegisterCallback<PointerEnterEvent>(_ =>
					{
						activeHover = row.Edge;
						hover(row.Edge);
					});
					group.RegisterCallback<PointerLeaveEvent>(_ =>
					{
						if (ReferenceEquals(activeHover, row.Edge))
						{
							activeHover = null;
							hover(null);
						}
					});
					group.RegisterCallback<FocusInEvent>(_ =>
					{
						activeFocus = row.Edge;
						focus(row.Edge);
					});
					group.RegisterCallback<FocusOutEvent>(_ =>
					{
						if (ReferenceEquals(activeFocus, row.Edge))
						{
							activeFocus = null;
							focus(null);
						}
					});
					var heading = Element("connection-header");
					var tint = row.Color;
					tint.a = .22f;
					heading.style.backgroundColor = tint;
					if (row.Icon != null) heading.Add(row.Icon());
					heading.Add(Label(row.TypeLabel, "connection-kind"));
					group.Add(heading);
					var target = Element("connection-target");
					var relation = Label(row.RelationLabel, "connection-relation");
					relation.style.color = RelationInk(row.RelationColor);
					target.Add(relation);
					options.BindTip(heading, (row.Edge.From == nodeId ? "OUT → " : "IN ← ") + row.RelationLabel);
					target.Add(Button(row.Label, row.Select, "link"));
					group.Add(target);
					options.AppendRelationshipActions?.Invoke(group, row.Edge);
				}

				count.text = shown + " of " + total +
							 " loaded relationships · hover or focus an entry to highlight its link";
			}

			search.RegisterValueChangedCallback(e =>
			{
				setQuery(e.newValue);
				Populate();
			});
			Populate();
		}

		/// <summary>Rebuilds the inspector contents for the selected graph node.</summary>
		/// <param name="parent">Element whose contents are replaced.</param>
		/// <param name="scene">Graph scene containing the node and schema.</param>
		/// <param name="node">Node to inspect, or <see langword="null" /> to show the empty state.</param>
		/// <param name="options">Optional content and interaction callbacks.</param>
		public static void Render(VisualElement parent, IGraphScene scene, IGraphNode node,
								  GraphInspectorOptions options = null)
		{
			// Dispose old tooltip bindings before rebuilding the selected node or the empty selection prompt.
			options ??= new GraphInspectorOptions();
			options.ForgetTips(parent);
			parent.Clear();
			if (scene == null || node == null)
			{
				Section(parent, "EXPLORE THE CONNECTIONS");
				parent.Add(Label("Select an entity", "inspector-title"));
				Note(parent, "Select a node to inspect its properties, sources, and relationships.");
				return;
			}

			var type = scene.Schema.NodeTypes.TryGetValue(node.Record.TypeId, out var definition) ? definition : null;
			Header(parent, type?.Label ?? node.Record.TypeId, node.Label, type?.Color ?? Color.gray,
				   node.Neighbors.Count + " distinct neighbors · " + node.Edges.Count + " relationships" +
				   (node.Pinned ? " · PINNED" : "") + (node.Favorite ? " · FAVORITE" : ""));
			if (options.AppendSectionsBeforeProperties) options.AppendSections?.Invoke(parent, node);
			var properties = parent;
			if (options.CollapseProperties)
			{
				var disclosure = new Foldout { text = "Properties", value = false };
				StyleDisclosure(disclosure);
				parent.Add(disclosure);
				properties = disclosure;
			}
			else
			{
				Section(parent, "PROPERTIES");
			}

			foreach (var pair in node.Record.Properties.OrderBy(pair => pair.Key, StringComparer.Ordinal))
			{
				// Honor schema inspectability while retaining undeclared properties under their original keys.
				var field = scene.Schema.Fields.TryGetValue(pair.Key, out var known) ? known : null;
				if (field != null && !field.Inspectable) continue;
				GraphContentUi.AddProperty(properties, field?.Label ?? pair.Key, pair.Value.ToString(),
										   options.Decorate, options.BindTip);
			}

			if (node.Edges.Count > 0)
			{
				// Resolve the opposite projected endpoint and schema labels for each loaded relationship.
				var disclosure = new Foldout { text = "Relationships", value = false };
				StyleDisclosure(disclosure);
				parent.Add(disclosure);
				options.Decorate(disclosure);
				var query = "";
				Relationships(disclosure, node.Id, node.Edges.Count, () => query, value => query = value, () => node
								  .Edges.OrderBy(edge => edge.Relation).Select(edge =>
								  {
									  var other = scene.Nodes[edge.From == node.Id ? edge.To : edge.From];
									  var known = scene.Schema.NodeTypes.TryGetValue(other.Record.TypeId, out var kind)
										  ? kind
										  : null;
									  return new GraphRelationRow
									  {
										  Edge = edge, Label = other.Label,
										  TypeLabel = known?.Label ?? other.Record.TypeId,
										  Color = known?.Color ?? Color.gray,
										  RelationLabel =
											  scene.Schema.Relations.TryGetValue(edge.Relation, out var relation)
												  ? relation.Label
												  : edge.Relation,
										  RelationColor = relation?.Color ?? new Color(.62f, .67f, .74f),
										  Select = () => options.Select?.Invoke(other)
									  };
								  }), _ => { }, _ => { }, options);
			}

			var sources = new Foldout { text = "Sources & provenance", value = false };

			// Show stable provenance even when the host does not provide an action for opening sources.
			StyleDisclosure(sources);
			parent.Add(sources);
			options.Decorate(sources);
			Note(sources, "Stable ID: " + node.Id);
			foreach (var source in node.Record.Sources)
				if (options.OpenSource != null)
					sources.Add(Button(source, () => options.OpenSource(source), "source-link"));
				else Note(sources, source);
			if (!options.AppendSectionsBeforeProperties) options.AppendSections?.Invoke(parent, node);
			foreach (var action in options.Actions ?? Array.Empty<GraphInspectorAction>())
			{
				// Check visibility now and recheck enabled state at click time before rebuilding changed node details.
				if (action == null || !action.IsVisible(node)) continue;
				var enabled = action.IsEnabled(node);
				var button = Button(action.Label, () =>
				{
					if (!action.IsEnabled(node)) return;
					action.Execute(node);
					var current = scene.Nodes.TryGetValue(node.Id, out var refreshed) ? refreshed : null;
					Render(parent, scene, current, options);
				}, "inspector-action");
				button.name = action.Id;
				button.SetEnabled(enabled);
				button.tooltip = enabled ? action.Tooltip : action.DisabledReason?.Invoke(node) ?? action.Tooltip;
				parent.Add(button);
			}
		}
	}

	/// <summary>Creates and updates graph node type filter controls.</summary>
	public static class GraphFilterPresenter
	{
		/// <summary>Creates a button that cycles the filter state for a node type.</summary>
		/// <param name="parent">Element that receives the button.</param>
		/// <param name="shape">Icon element shown inside the button.</param>
		/// <param name="cycle">Callback that advances the filter state.</param>
		/// <param name="bindTip">Callback that binds tooltip text to the button.</param>
		/// <param name="tip">Tooltip text for the button.</param>
		/// <returns>The created filter button.</returns>
		public static Button TypeButton(VisualElement parent, VisualElement shape, Action cycle,
										Action<VisualElement, string> bindTip, string tip)
		{
			var button = new Button(cycle) { text = "" };
			button.AddToClassList("kind-toggle");
			Square(button, 30);
			button.Add(shape);
			parent.Add(button);
			bindTip(button, tip);
			return button;
		}

		/// <summary>Updates the visual state classes for an included or hidden type filter.</summary>
		/// <param name="button">Filter button to update.</param>
		/// <param name="included">Whether the type is explicitly included.</param>
		/// <param name="hidden">Whether the type is hidden.</param>
		public static void Mode(VisualElement button, bool included, bool hidden)
		{
			button.EnableInClassList("kind-solo", included);
			button.EnableInClassList("kind-hide", hidden);
		}
	}
}