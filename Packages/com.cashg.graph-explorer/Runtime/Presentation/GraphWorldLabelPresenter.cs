using UnityEngine;
using UnityEngine.UIElements;
using static Cashg.GraphExplorer.GraphUi;

namespace Cashg.GraphExplorer
{
	/// <summary>Represents graph world node label.</summary>
	public class GraphWorldNodeLabel
	{
		/// <summary>Gets or sets optional step chip elements.</summary>
		public VisualElement[] Chips;
		/// <summary>Gets or sets the root label element.</summary>
		public VisualElement Root;
		/// <summary>Gets or sets the workflow steps container.</summary>
		public VisualElement Steps;
		/// <summary>Gets or sets the display states for workflow steps.</summary>
		public string[] StepStates;
		/// <summary>Gets or sets the node type caption element.</summary>
		public Label TypeHeader;
		/// <summary>Gets or sets the node name element.</summary>
		public Label Name;
		/// <summary>Gets or sets the node metadata element.</summary>
		public Label Meta;
	}

	/// <summary>Represents graph world label presenter.</summary>
	public static class GraphWorldLabelPresenter
	{
		private const float NodeLabelInkSize = 11f, NodeLabelLeading = 4f;

		/// <summary>Determines whether two label rectangles overlap with spacing.</summary>
		/// <param name="a">First label rectangle.</param>
		/// <param name="b">Second label rectangle.</param>
		/// <param name="padding">Minimum spacing between rectangles.</param>
		/// <returns><see langword="true"/> when the rectangles overlap after spacing.</returns>
		public static bool LabelRectsOverlap(Rect a, Rect b, float padding)
		{
			return a.xMax + padding > b.x && b.xMax + padding > a.x && a.yMax + padding > b.y && b.yMax + padding > a.y;
		}

		/// <summary>Estimates the rendered width of text using the presenter's glyph metrics.</summary>
		/// <param name="text">Text to measure.</param>
		/// <returns>Estimated width in reference pixels.</returns>
		public static float LabelInk(string text)
		{
			// Estimate text width from narrow, normal, and wide glyph classes without allocating layout elements.
			if (string.IsNullOrEmpty(text)) return 0f;
			var width = 0f;
			for (var i = 0; i < text.Length; i++)
			{
				var c = text[i];
				if (c == ' ' || c == '.' || c == ',' || c == '\'' || c == 'i' || c == 'l' || c == 'I' || c == 'j' ||
					c == 't') width += 3.8f;
				else if (char.IsUpper(c) || c == 'm' || c == 'w') width += 8.4f;
				else width += 6.5f;
			}

			return width;
		}

		/// <summary>Scales reference label metrics to a font size.</summary>
		/// <param name="fontSize">Requested font size.</param>
		/// <returns>Scale relative to the reference font size.</returns>
		public static float LabelScale(float fontSize)
		{
			return fontSize / NodeLabelInkSize;
		}

		// Width of the whole panel, padding included. Short text sets the width; long names are capped at MaxWidth and wrap.
		/// <summary>Calculates the full label panel width, including padding.</summary>
		/// <param name="s">Label settings.</param>
		/// <param name="typeName">Node type caption.</param>
		/// <param name="name">Node name.</param>
		/// <param name="meta">Metadata text.</param>
		/// <param name="nameFontSize">Optional name font size override.</param>
		/// <param name="extraWidth">Additional width reserved by the host.</param>
		/// <returns>Panel width clamped to the configured limits.</returns>
		public static float NodeLabelBlockWidth(GraphNodeLabelSettings s, string typeName, string name, string meta,
												float nameFontSize = 0f, float extraWidth = 0f)
		{
			if (nameFontSize <= 0f) nameFontSize = s.NameFontSize;
			var text = Mathf.Max(LabelInk(name) * LabelScale(nameFontSize),
								 LabelInk(meta) * LabelScale(s.MetaFontSize));
			var typeWidth = LabelInk(typeName) * LabelScale(s.TypeFontSize) +
							Mathf.Max(0, (typeName == null ? 0 : typeName.Length) - 1);
			return Mathf.Clamp(Mathf.Max(text, typeWidth) + s.Padding * 2f + extraWidth, s.MinWidth, s.MaxWidth);
		}

		// The full name always stays visible: it wraps word by word inside the panel instead of being clipped.
		/// <summary>Counts wrapped lines needed to display a node name within the available width.</summary>
		/// <param name="s">Label settings.</param>
		/// <param name="name">Node name.</param>
		/// <param name="available">Available text width in pixels.</param>
		/// <param name="nameFontSize">Optional name font size override.</param>
		/// <returns>Number of lines required by the name.</returns>
		public static int NodeNameLineCount(GraphNodeLabelSettings s, string name, float available,
											float nameFontSize = 0f)
		{
			if (string.IsNullOrEmpty(name)) return 1;
			if (nameFontSize <= 0f) nameFontSize = s.NameFontSize;
			var scale = LabelScale(nameFontSize);
			var space = LabelInk(" ") * scale;
			var limit = Mathf.Max(1f, available);
			var lines = 1;
			var column = 0f;
			foreach (var word in name.Split(' '))
			{
				// Wrap at word boundaries while leaving a single oversized word on its own line.
				var wordWidth = LabelInk(word) * scale;
				if (column > 0f && column + space + wordWidth > limit)
				{
					lines++;
					column = wordWidth;
				}
				else
				{
					column += (column > 0f ? space : 0f) + wordWidth;
				}
			}

			return lines;
		}

		/// <summary>Applies the configured background, padding, and corner radius to a label panel.</summary>
		/// <param name="panel">Panel element to style.</param>
		/// <param name="s">Settings that define the panel style.</param>
		public static void ApplyLabelPanel(VisualElement panel, GraphNodeLabelSettings s)
		{
			panel.style.backgroundColor = s.BackgroundColor;
			var padding = Mathf.Max(0f, s.Padding);
			panel.style.paddingLeft = padding;
			panel.style.paddingRight = padding;
			panel.style.paddingTop = padding;
			panel.style.paddingBottom = padding;
			var radius = Mathf.Max(0f, s.CornerRadius);
			panel.style.borderTopLeftRadius = radius;
			panel.style.borderTopRightRadius = radius;
			panel.style.borderBottomLeftRadius = radius;
			panel.style.borderBottomRightRadius = radius;
		}

		/// <summary>Creates and attaches the standard world-node label elements to a model.</summary>
		/// <typeparam name="T">Label model type.</typeparam>
		/// <param name="parent">Element that receives the label.</param>
		/// <param name="model">Label model to populate.</param>
		/// <returns>The populated label model.</returns>
		public static T Create<T>(VisualElement parent, T model) where T : GraphWorldNodeLabel
		{
			// Create a reusable noninteractive label block so world labels never intercept graph input.
			var block = Element("node-label");
			block.pickingMode = PickingMode.Ignore;
			block.style.display = DisplayStyle.None;
			var typeHeader = Label("", "node-type-header");
			typeHeader.pickingMode = PickingMode.Ignore;
			var nameLabel = Label("", "node-name");
			nameLabel.pickingMode = PickingMode.Ignore;
			var meta = Label("", "node-meta");
			meta.pickingMode = PickingMode.Ignore;
			meta.style.display = DisplayStyle.None;
			var steps = Element("node-steps");
			steps.pickingMode = PickingMode.Ignore;
			steps.style.display = DisplayStyle.None;
			block.Add(typeHeader);
			block.Add(nameLabel);
			block.Add(meta);
			block.Add(steps);
			model.Root = block;
			model.TypeHeader = typeHeader;
			model.Name = nameLabel;
			model.Meta = meta;
			model.Steps = steps;
			parent.Add(block);
			return model;
		}
	}
}
