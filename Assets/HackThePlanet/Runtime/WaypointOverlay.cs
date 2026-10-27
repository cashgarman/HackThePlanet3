using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace HackThePlanet
{
	/// <summary>Screen-space waypoint markers and optional route preview without mutating the 3D graph scene.</summary>
	internal sealed class WaypointOverlay : VisualElement
	{
		// Camera that projects node world positions into this overlay panel.
		private readonly Camera camera;

		// Waypoint markers paired with their on-screen label elements.
		private readonly List<(NetworkWaypoint waypoint, Label marker)> markers = new();

		// Node ids along the optional tier-4 path preview from the gateway.
		private readonly IReadOnlyList<string> path;

		// World positions keyed by network node id for projection and routing.
		private readonly Dictionary<string, Vector3> positions;

		// Stroke color for the active path preview line.
		private readonly Color routeColor;

		// UI text scale multiplier applied when repositioning markers.
		private float textScale = 1;

		/// <summary>Builds markers for known waypoint nodes and optional path overlay from the entry gateway.</summary>
		/// <param name="camera">Camera used to project node world positions into panel space.</param>
		/// <param name="nodes">Full network nodes supplying positions.</param>
		/// <param name="links">Links used for path preview when tier 4 path display is enabled.</param>
		/// <param name="waypoints">Stored markers for the current contract instance.</param>
		/// <param name="tier">Unlocked waypoint feature tier (0–4).</param>
		/// <param name="entry">Gateway node id used as the path start.</param>
		public WaypointOverlay(Camera camera, List<NetNode> nodes, List<NetLink> links,
							   IReadOnlyList<NetworkWaypoint> waypoints, int tier, string entry)
		{
			// Keep the camera and index node positions by id.
			this.camera = camera;
			positions = nodes.ToDictionary(n => n.id, n => n.position);

			// Cover the parent without intercepting input.
			style.position = Position.Absolute;
			style.left = style.right = style.top = style.bottom = 0;
			pickingMode = PickingMode.Ignore;

			// Create one marker label for each waypoint on a known node.
			foreach (var waypoint in waypoints.Where(w => positions.ContainsKey(w.nodeId)))
			{
				// Number the marker; tier 3 and up append the waypoint label.
				var marker =
					new Label("W" + (markers.Count + 1) +
							  (tier >= 3 && !string.IsNullOrEmpty(waypoint.label) ? " / " + waypoint.label : ""))
						{ enableRichText = false, pickingMode = PickingMode.Ignore };

				// Style the marker label.
				marker.name = "waypoint-marker-" + waypoint.nodeId;
				marker.style.position = Position.Absolute;
				marker.style.color = GameSimulation.WaypointColors[tier >= 2 ? waypoint.color : 0];
				marker.style.backgroundColor = new Color(.02f, .05f, .06f, .92f);
				marker.style.fontSize = 12;
				marker.style.paddingLeft = marker.style.paddingRight = 5;
				marker.style.paddingTop = marker.style.paddingBottom = 3;

				// Register the marker and add it to the overlay.
				markers.Add((waypoint, marker));
				Add(marker);
			}

			// Find the waypoint whose route should be previewed, if any.
			var target = tier >= 4
				? waypoints.FirstOrDefault(w => w.showPath && positions.ContainsKey(w.nodeId))
				: null;
			path = target == null
				? Array.Empty<string>()
				: GameSimulation.WaypointPath(nodes, links, entry, target.nodeId).NodeIds;

			// Use the target's color for the route, or white when there is none.
			routeColor = target == null ? Color.white : GameSimulation.WaypointColors[target.color];

			// Draw the route and marker pointers each frame.
			generateVisualContent += DrawPath;
		}

		/// <summary>Gets world positions for waypoint nodes and nodes on the active path preview.</summary>
		public IEnumerable<Vector3> Anchors =>
			markers.Select(m => positions[m.waypoint.nodeId]).Concat(path.Select(id => positions[id]));

		/// <summary>Updates marker layout and font scale after camera or viewport changes.</summary>
		/// <param name="scale">UI text scale multiplier from the workstation shell.</param>
		public void Reposition(float scale)
		{
			// Store the scale for the pointer drawing.
			textScale = scale;

			// Move each marker to its node's screen position.
			foreach (var item in markers)
			{
				// Show the marker only when its node is in front of the camera.
				var p = camera.WorldToViewportPoint(positions[item.waypoint.nodeId]);
				item.marker.style.display = p.z > camera.nearClipPlane ? DisplayStyle.Flex : DisplayStyle.None;

				// Keep the marker at the edge when its node lies outside the current camera frame.
				item.marker.style.fontSize = 12 * scale;
				var width = item.marker.resolvedStyle.width;
				if (float.IsNaN(width))
					width = 70;
				item.marker.style.left = Mathf.Clamp(p.x * contentRect.width - width / 2, 0,
													 Mathf.Max(0, contentRect.width - Mathf.Max(70, width)));
				item.marker.style.top = Mathf.Clamp((1 - p.y) * contentRect.height - 42 * scale, 0,
													Mathf.Max(0, contentRect.height - 25 * scale));
			}

			// Redraw the route and pointers.
			MarkDirtyRepaint();
		}

		// Paints path segments and pointer triangles for markers in panel space.
		private void DrawPath(MeshGenerationContext context)
		{
			// Set the route stroke style.
			var painter = context.painter2D;
			painter.strokeColor = routeColor;
			painter.lineWidth = 3;

			// Draw each route segment whose ends are in front of the camera.
			for (var i = 1; i < path.Count; i++)
			{
				// Project both ends of the segment.
				var a = camera.WorldToViewportPoint(positions[path[i - 1]]);
				var b = camera.WorldToViewportPoint(positions[path[i]]);

				// Skip segments that reach behind the camera.
				if (a.z <= camera.nearClipPlane || b.z <= camera.nearClipPlane)
					continue;

				// Stroke the segment in panel space.
				painter.BeginPath();
				painter.MoveTo(new Vector2(a.x * contentRect.width, (1 - a.y) * contentRect.height));
				painter.LineTo(new Vector2(b.x * contentRect.width, (1 - b.y) * contentRect.height));
				painter.Stroke();
			}

			// Draw a downward pointer above each visible marker.
			foreach (var item in markers)
			{
				// Project the marker and skip it when it is behind the camera.
				var p = camera.WorldToViewportPoint(positions[item.waypoint.nodeId]);
				if (p.z <= camera.nearClipPlane)
					continue;

				// Fill the pointer triangle with the marker color.
				var anchor = new Vector2(p.x * contentRect.width, (1 - p.y) * contentRect.height);
				painter.fillColor = item.marker.resolvedStyle.color;
				painter.BeginPath();
				painter.MoveTo(anchor + new Vector2(-5, -17) * textScale);
				painter.LineTo(anchor + new Vector2(5, -17) * textScale);
				painter.LineTo(anchor + new Vector2(0, -8) * textScale);
				painter.ClosePath();
				painter.Fill();
			}
		}
	}
}