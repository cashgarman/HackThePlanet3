using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using static Cashg.GraphExplorer.GraphUi;

namespace Cashg.GraphExplorer
{
	/// <summary>Represents graph content ui.</summary>
	public static class GraphContentUi
	{
		/// <summary>Creates a property row or disclosure for long values.</summary>
		/// <param name="parent">Element that receives the property.</param>
		/// <param name="key">Property key used as the caption.</param>
		/// <param name="value">Property value to display.</param>
		/// <param name="decorate">Callback that decorates a disclosure when one is created.</param>
		/// <param name="bindTip">Callback that binds original text as a tooltip when formatting changes.</param>
		public static void AddProperty(VisualElement parent, string key, string value, Action<Foldout> decorate,
									   Action<VisualElement, string> bindTip)
		{
			var original = value;
			var timestampKey = key.IndexOf("date", StringComparison.OrdinalIgnoreCase) >= 0 ||
							   key.IndexOf("time", StringComparison.OrdinalIgnoreCase) >= 0 ||
							   key == "discovered" || key == "created" || key == "updated" ||
							   key.EndsWith("_at", StringComparison.OrdinalIgnoreCase);
			// An offset-free backend timestamp is a wall-clock value; do not invent a timezone.
			if (timestampKey && DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
												  out var timestamp))
				value = timestamp.ToString("MMM d, yyyy · HH:mm:ss", CultureInfo.InvariantCulture) +
						(timestamp.Kind == DateTimeKind.Utc
							? " UTC"
							: timestamp.Kind == DateTimeKind.Local
								? timestamp.ToString(" zzz", CultureInfo.InvariantCulture)
								: "");
			var caption = key.Replace('_', ' ');
			if (caption.Length > 0) caption = char.ToUpperInvariant(caption[0]) + caption.Substring(1);

			// Move multiline or long values into a disclosure so the property list remains scannable.
			if (value.Length > 160 || value.Contains('\n'))
			{
				var disclosure = new Foldout { text = caption, value = false };
				disclosure.AddToClassList("inspector-disclosure");
				decorate(disclosure);
				disclosure.Add(Label(value, "property-value"));
				parent.Add(disclosure);
				return;
			}

			var row = Element("inspector-property");
			row.Add(Label(caption, "inspector-property-key"));
			var content = Label(value, "inspector-property-value");

			// Preserve the raw timestamp as hover text whenever its displayed value was reformatted.
			if (value != original) bindTip(content, original);
			row.Add(content);
			parent.Add(row);
		}

		/// <summary>Creates a decorative favorite star.</summary>
		/// <param name="size">Star width and height.</param>
		/// <param name="color">Star color.</param>
		/// <returns>The star element.</returns>
		public static VisualElement FavoriteStar(float size, Color color)
		{
			var star = new VisualElement { pickingMode = PickingMode.Ignore };
			star.AddToClassList("favorite-star");
			star.style.width = size;
			star.style.height = size;
			star.style.flexShrink = 0;
			star.generateVisualContent += ctx => HeaderIcons.Paint(ctx.painter2D, "star", star.contentRect, color);
			star.RegisterCallback<GeometryChangedEvent>(_ => star.MarkDirtyRepaint());
			return star;
		}


		/// <summary>Updates favorite styling for a row representing a node.</summary>
		/// <param name="row">Row element to update.</param>
		/// <param name="node">Node represented by the row.</param>
		public static void BindFavoriteRow(VisualElement row, IGraphNode node)
		{
			row.EnableInClassList("is-favorite", node.Favorite);
			var star = row.Q(className: "favorite-star");
			if (star != null) star.style.display = node.Favorite ? DisplayStyle.Flex : DisplayStyle.None;
		}
	}
}