using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cashg.GraphExplorer
{
	/// <summary>Lists layout mode.</summary>
	public enum LayoutMode
	{
		/// <summary>Positions nodes in a three-dimensional force layout.</summary>
		Spatial = 0,
		/// <summary>Positions nodes on a flat plane.</summary>
		Flat = 1,
		/// <summary>Arranges nodes in groups using domain grouping policies.</summary>
		Groups = 2,
		/// <summary>Arranges nodes along lanes using domain lane policies.</summary>
		Lanes = 3
	}

	/// <summary>Lists size mapping.</summary>
	public enum SizeMapping
	{
		/// <summary>Uses one size for every node.</summary>
		Uniform = 0,
		/// <summary>Maps node size to its connection count.</summary>
		Connections = 1,
		/// <summary>Maps node size to a numeric metric.</summary>
		Metric = 2
	}

	/// <summary>Lists color mapping.</summary>
	public enum ColorMapping
	{
		/// <summary>Colors nodes by their declared type.</summary>
		Type = 0,
		/// <summary>Colors nodes by connection count.</summary>
		Connections = 1,
		/// <summary>Colors nodes by categorical property values.</summary>
		Category = 2,
		/// <summary>Colors nodes by a numeric metric.</summary>
		Metric = 3
	}

	/// <summary>Lists tool emphasis.</summary>
	public enum ToolEmphasis
	{
		/// <summary>No tool-specific emphasis is applied.</summary>
		None,
		/// <summary>The node represents completed work.</summary>
		Done,
		/// <summary>The node is the next item of work.</summary>
		Next
	}

	/// <summary>A live projection, not a second semantic document. Hosts may implement richer facades.</summary>
	public interface IGraphNode
	{
		/// <summary>Gets the stable node identifier.</summary>
		string Id { get; }
		/// <summary>Gets the semantic node record.</summary>
		GraphNodeRecord Record { get; }
		/// <summary>Gets the node's display label.</summary>
		string Label { get; }
		/// <summary>Gets or sets the node's world position.</summary>
		Vector3 Position { get; set; }
		/// <summary>Gets or sets the node's layout velocity.</summary>
		Vector3 Velocity { get; set; }
		/// <summary>Gets or sets whether layout should keep this node fixed.</summary>
		bool Pinned { get; set; }
		/// <summary>Gets or sets whether the node is visible.</summary>
		bool Visible { get; set; }
		/// <summary>Gets or sets whether the node matches active filters.</summary>
		bool Matches { get; set; }
		/// <summary>Gets or sets whether the node is marked as a favorite.</summary>
		bool Favorite { get; set; }
		/// <summary>Gets or sets the node's tool-specific emphasis.</summary>
		ToolEmphasis Emphasis { get; set; }
		/// <summary>Gets identifiers of adjacent nodes.</summary>
		GraphIdSet Neighbors { get; }
		/// <summary>Gets edges incident to the node.</summary>
		IReadOnlyList<IGraphEdge> Edges { get; }
	}

	/// <summary>Defines graph edge.</summary>
	public interface IGraphEdge
	{
		/// <summary>Gets the stable edge identifier.</summary>
		string Id { get; }
		/// <summary>Gets the semantic edge record.</summary>
		GraphEdgeRecord Record { get; }
		/// <summary>Gets the source node identifier.</summary>
		string From { get; }
		/// <summary>Gets the destination node identifier.</summary>
		string To { get; }
		/// <summary>Gets the relationship type identifier.</summary>
		string Relation { get; }
		/// <summary>Gets the relationship status.</summary>
		string Status { get; }
	}

	/// <summary>Defines graph scene.</summary>
	public interface IGraphScene
	{
		/// <summary>Gets the graph document projected by the scene.</summary>
		GraphDocument Document { get; }
		/// <summary>Gets the scene's mutable view state.</summary>
		GraphViewState View { get; }
		/// <summary>Gets the schema used to interpret graph records.</summary>
		GraphSchema Schema { get; }
		/// <summary>Gets visible nodes keyed by identifier.</summary>
		IReadOnlyDictionary<string, IGraphNode> Nodes { get; }
		/// <summary>Gets visible edges keyed by identifier.</summary>
		IReadOnlyDictionary<string, IGraphEdge> Edges { get; }
		/// <summary>Gets the revision used to detect scene changes.</summary>
		int Revision { get; }
	}

	/// <summary>Defines graph edge filter.</summary>
	public interface IGraphEdgeFilter
	{
		/// <summary>Determines whether an edge passes the active filter.</summary>
		/// <param name="edge">Edge to evaluate.</param>
		/// <returns><see langword="true"/> when the edge is included.</returns>
		bool EdgeMatches(IGraphEdge edge);
	}

	/// <summary>Optional domain policies. Missing policies give ordinary type/degree exploration.</summary>
	public sealed class GraphPresentationProfile
	{
		private readonly Dictionary<string, GraphNodeTypeDefinition> fallbackTypes = new(StringComparer.Ordinal);
		/// <summary>Optional predicate controlling which nodes use category coloring.</summary>
		public Func<IGraphNode, bool> CategoryApplies;
		/// <summary>Optional fallback color for nodes without a category color.</summary>
		public Func<IGraphNode, Color?> CategoryFallback;
		/// <summary>Optional function that returns a node's grouping identifier.</summary>
		public Func<IGraphNode, string> GroupId;
		/// <summary>Optional function that returns the node's grouping strength.</summary>
		public Func<IGraphNode, float> GroupStrength;
		/// <summary>Optional predicate identifying nodes that anchor a group.</summary>
		public Func<IGraphNode, bool> IsGroupAnchor;
		/// <summary>Optional function that returns a node's lane coordinate.</summary>
		public Func<IGraphNode, float?> Lane;
		/// <summary>Gets or sets the center coordinate used for lanes.</summary>
		public float LaneCenter;
		/// <summary>Gets or sets the distance between adjacent lanes.</summary>
		public float LaneSpacing = 16;
		/// <summary>Optional predicate controlling which nodes use metric mapping.</summary>
		public Func<IGraphNode, bool> MetricApplies;
		/// <summary>Gets or sets the function that maps a normalized metric to a color.</summary>
		public Func<float, Color> MetricColor = Spectrum;
		/// <summary>Gets or sets the numeric field used for metric mapping.</summary>
		public string MetricField = "";
		/// <summary>Gets or sets the categorical field used for category mapping.</summary>
		public string CategoryField = "";

		/// <summary>Gets a node type definition, creating a fallback for undeclared types.</summary>
		/// <param name="scene">Scene whose schema is queried.</param>
		/// <param name="node">Node whose type is resolved.</param>
		/// <returns>The declared or fallback node type definition.</returns>
		public GraphNodeTypeDefinition Type(IGraphScene scene, IGraphNode node)
		{
			var id = node.Record.TypeId;
			if (scene.Schema.NodeTypes.TryGetValue(id, out var type)) return type;

			// Cache neutral fallback definitions without assigning domain meaning to unknown types.
			if (!fallbackTypes.TryGetValue(id, out type))
				fallbackTypes.Add(id, type = new GraphNodeTypeDefinition(id, id));
			return type;
		}

		/// <summary>Reads the configured numeric metric and its optional partition key.</summary>
		/// <param name="scene">Scene whose schema is queried.</param>
		/// <param name="node">Node whose metric is read.</param>
		/// <param name="value">Receives the finite numeric value.</param>
		/// <param name="partition">Receives the partition key, when configured.</param>
		/// <returns><see langword="true"/> when a valid metric is available.</returns>
		public bool TryMetric(IGraphScene scene, IGraphNode node, out float value, out string partition)
		{
			value = 0;
			partition = "";

			// Accept only applicable, finite numeric values from the configured metric field.
			if (MetricApplies != null && !MetricApplies(node)) return false;
			if (string.IsNullOrEmpty(MetricField) ||
				!node.Record.Properties.TryGetValue(MetricField, out var property) ||
				property.Type != GraphValueType.Number) return false;
			value = (float)property.Number;
			if (float.IsNaN(value) || float.IsInfinity(value)) return false;

			// Partition normalization by the schema's companion field when one is declared.
			if (scene.Schema.Fields.TryGetValue(MetricField, out var field) &&
				!string.IsNullOrEmpty(field.PartitionBy) &&
				node.Record.Properties.TryGetValue(field.PartitionBy, out var key)) partition = key.ToString();
			return true;
		}

		/// <summary>Gets the category color for a node, using fallback colors when needed.</summary>
		/// <param name="scene">Scene whose schema is queried.</param>
		/// <param name="node">Node whose category is resolved.</param>
		/// <param name="fallback">Color used when category mapping does not provide one.</param>
		/// <returns>The resolved category or fallback color.</returns>
		public Color CategoryColor(IGraphScene scene, IGraphNode node, Color fallback)
		{
			// Prefer a registered category color, then the host fallback, for applicable nodes.
			if (CategoryApplies != null && !CategoryApplies(node)) return fallback;
			return scene.Schema.Fields.TryGetValue(CategoryField, out var field) &&
				   node.Record.Properties.TryGetValue(CategoryField, out var value) &&
				   field.Categories.TryGetValue(value.ToString(), out var category)
				? category.Color
				: CategoryFallback?.Invoke(node) ?? fallback;
		}

		/// <summary>Maps a normalized fraction to a color in the metric spectrum.</summary>
		/// <param name="fraction">Metric fraction, clamped to the zero-to-one range.</param>
		/// <returns>The corresponding spectrum color.</returns>
		public static Color Spectrum(float fraction)
		{
			var color = Color.HSVToRGB(Mathf.Clamp01(fraction) / 3f, .82f, .96f);
			return new Color(Mathf.GammaToLinearSpace(color.r), Mathf.GammaToLinearSpace(color.g),
							 Mathf.GammaToLinearSpace(color.b));
		}
	}

	/// <summary>Host-owned progress. A fraction of one does not imply validated completion.</summary>
	public sealed class GraphNodeProgressVisual
	{
		/// <summary>Gets or sets the current progress fraction.</summary>
		public float Fraction { get; set; }
		/// <summary>Gets or sets whether progress is complete.</summary>
		public bool Complete { get; set; }

		/// <summary>Returns a valid progress fraction clamped to zero through one.</summary>
		/// <param name="fraction">Receives the normalized fraction when valid.</param>
		/// <returns><see langword="true"/> when the fraction is finite.</returns>
		public bool TryFraction(out float fraction)
		{
			fraction = Complete ? 1 : Fraction;
			if (float.IsNaN(fraction) || float.IsInfinity(fraction)) return false;
			fraction = Math.Max(0, Math.Min(1, fraction));
			return true;
		}
	}

	/// <summary>Represents graph task visual.</summary>
	public sealed class GraphTaskVisual
	{
		/// <summary>Gets or sets the total number of tasks.</summary>
		public int Count;
		/// <summary>Gets or sets the number of completed tasks.</summary>
		public int Done;
		/// <summary>Gets or sets the optional task completion fraction.</summary>
		public float? Progress;
		/// <summary>Gets or sets the task status identifier.</summary>
		public string Status = "draft";
		/// <summary>Gets or sets the display text for the task.</summary>
		public string Text = "";

		/// <summary>Maps a task status to its presentation color.</summary>
		/// <param name="status">Status identifier.</param>
		/// <returns>The color associated with the status.</returns>
		public static Color Color(string status)
		{
			return status == "failed" || status == "blocked" ? new Color(1, .35f, .35f) :
				status == "completed" || status == "skipped" ? new Color(.35f, .92f, .65f) :
				status == "running" ? new Color(.3f, .78f, 1) :
				status == "cancelled" ? new Color(.6f, .64f, .7f) : new Color(1, .73f, .32f);
		}
	}
}
