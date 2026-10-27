using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Cashg.GraphExplorer
{
	// Type, name and status text shown above each 3D node. Every value here drives the world-space label UI.
	/// <summary>Represents graph node label settings.</summary>
	[CreateAssetMenu(menuName = "Graph Explorer/Node label settings")]
	[MovedFrom(true, "JobHarvester.Graph", "Assembly-CSharp", "GraphNodeLabelSettings")]
	public sealed class GraphNodeLabelSettings : ScriptableObject
	{
		/// <summary>Gets or sets whether mouse proximity controls label visibility.</summary>
		[Header("Mouse proximity reveal")]
		[Tooltip("Reveal labels near the mouse in screen space. Disable to use the host's original label visibility.")]
		public bool MouseProximityReveal = true;

		/// <summary>Gets or sets the screen-distance threshold for full label opacity.</summary>
		[Tooltip("Distance from the projected node center, in screen pixels, at which its label is fully visible.")]
		[Min(0)]
		public float RevealInnerRadius = 40f;

		/// <summary>Gets or sets the screen distance at which proximity-revealed labels are hidden.</summary>
		[Tooltip(
			"Distance in screen pixels beyond which labels are hidden. Must exceed the inner radius for a gradual fade.")]
		[Min(0)]
		public float RevealOuterRadius = 180f;

		/// <summary>Gets or sets the fade duration in seconds.</summary>
		[Tooltip(
			"Seconds to fade to the new proximity opacity. Zero applies it immediately; independent of time scale.")]
		[Min(0)]
		public float RevealFadeSeconds = .15f;

		/// <summary>Gets or sets whether the selected node label stays visible outside the reveal area.</summary>
		[Tooltip("Keep the selected node's title visible even when the mouse moves away or over UI.")]
		public bool KeepSelectedLabelVisible = true;

		/// <summary>Gets or sets the node type caption font size in pixels.</summary>
		[Header("Text size (pixels)")] [Tooltip("Node type header, such as JOB or COMPANY.")] [Range(6, 24)]
		public float TypeFontSize = 11f;

		/// <summary>Gets or sets the node name font size in pixels.</summary>
		[Tooltip("Full node name. Long names wrap instead of being cut off.")] [Range(6, 24)]
		public float NameFontSize = 11f;

		/// <summary>Gets or sets the metadata font size in pixels.</summary>
		[Tooltip("Stage, status and pin line.")] [Range(6, 24)]
		public float MetaFontSize = 9f;

		/// <summary>Gets or sets the label panel background color.</summary>
		[Header("Panel")] [Tooltip("Dark backing behind the text. Set alpha to 0 for no background.")]
		public Color BackgroundColor = new(.02f, .03f, .05f, .5f);

		/// <summary>Gets or sets the panel padding in pixels.</summary>
		[Tooltip("Space between the text and the edge of the panel, in pixels.")] [Range(0, 24)]
		public float Padding = 6f;

		/// <summary>Gets or sets the panel corner radius in pixels.</summary>
		[Range(0, 16)] public float CornerRadius = 4f;

		/// <summary>Gets or sets the minimum panel width in pixels.</summary>
		[Range(48, 400)] public float MinWidth = 72f;

		/// <summary>Gets or sets the maximum panel width in pixels.</summary>
		[Range(96, 600)] public float MaxWidth = 208f;

		/// <summary>Gets or sets the gap between a node and its label in pixels.</summary>
		[Header("Placement")]
		[Tooltip("Gap in pixels between the top of the node and the bottom of its label.")]
		[Range(0, 80)]
		public float AboveNodeGap = 6f;

		/// <summary>Gets or sets the minimum spacing between labels in pixels.</summary>
		[Tooltip("Minimum gap in pixels between two labels.")] [Range(0, 40)]
		public float LabelSpacing = 12f;

		/// <summary>Gets or sets the maximum number of upward stacking rows.</summary>
		[Tooltip("Rows a selected, hovered or tracked label may stack upward when another label is in the way.")]
		[Range(1, 4)]
		public int StackRows = 4;

		/// <summary>Calculates label opacity from mouse distance and interaction state.</summary>
		/// <param name="screenDistance">Distance from the pointer to the projected node center.</param>
		/// <param name="pointerOverGraph">Whether the pointer is over the graph.</param>
		/// <param name="selected">Whether the node is selected.</param>
		/// <returns>Opacity between zero and one.</returns>
		public float ProximityOpacity(float screenDistance, bool pointerOverGraph, bool selected)
		{
			// Handle selection and pointer exit first, then fade smoothly across the configured screen-space radii.
			if (!MouseProximityReveal || (selected && KeepSelectedLabelVisible)) return 1f;
			if (!pointerOverGraph) return 0f;
			var inner = Mathf.Max(0f, RevealInnerRadius);
			var outer = Mathf.Max(inner, RevealOuterRadius);
			if (outer <= inner) return screenDistance <= inner ? 1f : 0f;
			return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner, outer, screenDistance));
		}
	}
}