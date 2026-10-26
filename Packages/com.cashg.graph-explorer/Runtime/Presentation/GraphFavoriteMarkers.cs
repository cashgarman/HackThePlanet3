using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using static Cashg.GraphExplorer.GraphUi;

namespace Cashg.GraphExplorer
{
	/// <summary>Represents graph favorite markers.</summary>
	public static class GraphFavoriteMarkers
	{
		/// <summary>Updates screen-space markers for visible favorite nodes.</summary>
		/// <param name="nodes">Nodes to inspect.</param>
		/// <param name="hasFavorites">Whether any favorites are available.</param>
		/// <param name="root">Panel root used to convert screen coordinates.</param>
		/// <param name="worldLabels">Container for marker elements.</param>
		/// <param name="graphRenderer">Renderer used to test node visibility.</param>
		/// <param name="graphCamera">Camera used to project nodes.</param>
		/// <param name="area">Visible area available to markers.</param>
		/// <param name="favoriteMarks">Reusable marker elements.</param>
		/// <param name="color">Favorite marker color.</param>
		/// <param name="radius">Function that returns the projected node radius.</param>
		public static void Update(IEnumerable<IGraphNode> nodes, bool hasFavorites, VisualElement root,
								  VisualElement worldLabels, GraphRenderer graphRenderer, Camera graphCamera, Rect area,
								  List<VisualElement> favoriteMarks, Color color,
								  Func<IGraphNode, Vector2, float> radius)
		{
			var used = 0;

			// Project rendered favorites and offset each marker diagonally from the node's visible radius.
			if (hasFavorites && nodes != null && root?.panel != null && graphRenderer != null && graphCamera != null &&
				worldLabels != null)
			{
				const float size = 20f;
				var half = size * .5f;
				foreach (var node in nodes)
				{
					if (!node.Favorite || !graphRenderer.IsDrawn(node.Id)) continue;
					var screen = graphCamera.WorldToScreenPoint(node.Position);
					if (screen.z <= graphCamera.nearClipPlane) continue;
					var anchor =
						RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y));
					var offset = radius(node, anchor) * .7071f;
					var center = new Vector2(anchor.x + offset, anchor.y - offset);
					// Markers never sit over the header, rail, or dock. Hidden instead of clamped, so no marker points at the wrong node.
					if (center.x < area.xMin - half || center.x > area.xMax + half || center.y < area.yMin - half ||
						center.y > area.yMax + half) continue;
					var mark = Marker(used++, favoriteMarks, worldLabels, color);
					mark.style.display = DisplayStyle.Flex;
					mark.style.left = Mathf.Round(center.x - half);
					mark.style.top = Mathf.Round(center.y - half);
				}
			}

			// Keep unused markers pooled but hidden when fewer favorites are visible this frame.
			for (var i = used; i < favoriteMarks.Count; i++) favoriteMarks[i].style.display = DisplayStyle.None;
		}

		private static VisualElement Marker(int index, List<VisualElement> favoriteMarks, VisualElement worldLabels,
											Color color)
		{
			// Grow the noninteractive marker pool only when a frame needs more slots than prior frames.
			while (favoriteMarks.Count <= index)
			{
				var mark = Element("favorite-mark");
				mark.pickingMode = PickingMode.Ignore;
				mark.Add(GraphContentUi.FavoriteStar(13, color));
				mark.style.display = DisplayStyle.None;
				favoriteMarks.Add(mark);
				worldLabels.Add(mark);
			}

			return favoriteMarks[index];
		}
	}
}
