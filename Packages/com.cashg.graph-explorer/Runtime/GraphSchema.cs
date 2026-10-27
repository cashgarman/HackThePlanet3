using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace Cashg.GraphExplorer
{
	/// <summary>
	///     Presentation metadata for one discrete value of a categorical <see cref="GraphFieldDefinition" />.
	///     Keys in <see cref="GraphFieldDefinition.Categories" /> are the raw stored property values;
	///     lookup uses <see cref="GraphValue.ToString" /> (see <see cref="GraphPresentationProfile.CategoryColor" />).
	/// </summary>
	public sealed class GraphCategoryDefinition
	{
		/// <summary>Creates a category definition for schema registration.</summary>
		/// <param name="label">Display name; null becomes empty.</param>
		/// <param name="color">Color used for category-based node coloring.</param>
		/// <param name="badge">Optional compact label; null becomes empty.</param>
		/// <param name="order">Sort key for ordered category lists; default is zero.</param>
		public GraphCategoryDefinition(string label, Color color, string badge = "", int order = 0)
		{
			Label = label ?? "";
			Color = color;
			Badge = badge ?? "";
			Order = order;
		}

		/// <summary>Human-readable name for legends, filters and inspectors.</summary>
		public string Label { get; }

		/// <summary>Node color when <see cref="ColorMapping.Category" /> is active for the owning field.</summary>
		public Color Color { get; }

		/// <summary>Optional short glyph or abbreviation for compact UI; empty when omitted.</summary>
		public string Badge { get; }

		/// <summary>Relative sort order when listing categories (lower values first).</summary>
		public int Order { get; }
	}

	/// <summary>
	///     Schema entry for a node or edge property key. Documents may carry undeclared properties;
	///     registration enables validation, search or filter policy, inspector visibility, and presentation helpers.
	/// </summary>
	public sealed class GraphFieldDefinition
	{
		/// <summary>Registers a field definition for schema lookup and validation.</summary>
		/// <param name="id">Non-empty property key.</param>
		/// <param name="label">Display label; null falls back to <paramref name="id" />.</param>
		/// <param name="type">Expected value type for declared properties.</param>
		/// <param name="searchable">Whether search UI should include this field.</param>
		/// <param name="inspectable">Whether the inspector should list this field.</param>
		/// <param name="unit">Optional unit suffix; null becomes empty.</param>
		/// <param name="partitionBy">Optional partition property id; null becomes empty.</param>
		/// <param name="categories">Optional categorical value map; null yields an empty read-only dictionary.</param>
		public GraphFieldDefinition(string id, string label, GraphValueType type = GraphValueType.Text,
									bool searchable = true, bool inspectable = true, string unit = "",
									string partitionBy = "",
									IDictionary<string, GraphCategoryDefinition> categories = null)
		{
			Id = GraphRecordCopy.Required(id, nameof(id));
			Label = label ?? id;
			Type = type;
			Searchable = searchable;
			Inspectable = inspectable;
			Unit = unit ?? "";
			PartitionBy = partitionBy ?? "";
			Categories = new ReadOnlyDictionary<string, GraphCategoryDefinition>(categories == null
					? new Dictionary<string, GraphCategoryDefinition>(StringComparer.Ordinal)
					: new Dictionary<string, GraphCategoryDefinition>(categories, StringComparer.Ordinal));
		}

		/// <summary>
		///     Stable property key; matches keys in <see cref="GraphNodeRecord.Properties" /> and
		///     <see cref="GraphEdgeRecord.Properties" />.
		/// </summary>
		public string Id { get; }

		/// <summary>Human-readable caption; defaults to <see cref="Id" /> when omitted.</summary>
		public string Label { get; }

		/// <summary>Expected <see cref="GraphValue.Type" /> for non-null values; checked by <see cref="GraphSchema.Validate" />.</summary>
		public GraphValueType Type { get; }

		/// <summary>When false, hosts should omit this field from search indexing and query UI.</summary>
		public bool Searchable { get; }

		/// <summary>When false, the stock inspector skips this property (see presenter field gating).</summary>
		public bool Inspectable { get; }

		/// <summary>Optional display suffix for numeric values (for example a currency or distance unit).</summary>
		public string Unit { get; }

		/// <summary>
		///     When set, names another property whose value partitions metric scaling for
		///     <see cref="GraphPresentationProfile.TryMetric" />.
		/// </summary>
		public string PartitionBy { get; }

		/// <summary>Optional map from stored categorical values to <see cref="GraphCategoryDefinition" /> metadata.</summary>
		public IReadOnlyDictionary<string, GraphCategoryDefinition> Categories { get; }
	}

	/// <summary>
	///     Presentation and default sizing for a node <see cref="GraphNodeRecord.TypeId" />.
	///     Unknown types may still render via <see cref="GraphPresentationProfile.Type" /> fallbacks.
	/// </summary>
	public sealed class GraphNodeTypeDefinition
	{
		/// <summary>Creates a node type definition for schema registration.</summary>
		/// <param name="id">Non-empty type id.</param>
		/// <param name="label">Display label; null falls back to <paramref name="id" />.</param>
		/// <param name="shapeId">Renderer shape id; null becomes <c>default</c>.</param>
		/// <param name="color">Type color; null becomes gray.</param>
		/// <param name="defaultRadius">Positive default radius; NaN, infinity, or non-positive values throw.</param>
		/// <exception cref="ArgumentOutOfRangeException"><paramref name="defaultRadius" /> is not finite and positive.</exception>
		public GraphNodeTypeDefinition(string id, string label, string shapeId = "default", Color? color = null,
									   float defaultRadius = .65f)
		{
			Id = GraphRecordCopy.Required(id, nameof(id));
			Label = label ?? id;
			ShapeId = shapeId ?? "default";
			if (float.IsNaN(defaultRadius) || float.IsInfinity(defaultRadius) || defaultRadius <= 0)
				throw new ArgumentOutOfRangeException(nameof(defaultRadius));
			Color = color ?? Color.gray;
			DefaultRadius = defaultRadius;
		}

		/// <summary>Stable type id stored on each node record.</summary>
		public string Id { get; }

		/// <summary>Human-readable type name; defaults to <see cref="Id" /> when omitted.</summary>
		public string Label { get; }

		/// <summary>
		///     Built-in mesh id for instanced rendering (for example <c>Building</c>, <c>Person</c>); unknown ids fall back
		///     at draw time.
		/// </summary>
		public string ShapeId { get; }

		/// <summary>Node color when <see cref="ColorMapping.Type" /> is active.</summary>
		public Color Color { get; }

		/// <summary>Default world-space radius multiplier before metric or degree sizing overrides.</summary>
		public float DefaultRadius { get; }
	}

	/// <summary>
	///     Presentation and layout metadata for an edge <see cref="GraphEdgeRecord.TypeId" />.
	/// </summary>
	public sealed class GraphRelationDefinition
	{
		/// <summary>Creates a relation definition for schema registration.</summary>
		/// <param name="id">Non-empty relation id.</param>
		/// <param name="label">Display label; null falls back to <paramref name="id" />.</param>
		/// <param name="color">Edge color; null becomes gray.</param>
		/// <param name="isHierarchy">Whether this relation denotes a hierarchy.</param>
		/// <param name="parentIsFrom">Parent endpoint when hierarchical; ignored when not hierarchical.</param>
		/// <param name="springStrength">Non-negative layout spring strength; NaN or infinity throw.</param>
		/// <exception cref="ArgumentOutOfRangeException"><paramref name="springStrength" /> is not finite and non-negative.</exception>
		public GraphRelationDefinition(string id, string label, Color? color = null, bool isHierarchy = false,
									   bool parentIsFrom = true, float springStrength = .35f)
		{
			Id = GraphRecordCopy.Required(id, nameof(id));
			Label = label ?? id;
			Color = color ?? Color.gray;
			IsHierarchy = isHierarchy;
			ParentIsFrom = parentIsFrom;
			if (float.IsNaN(springStrength) || float.IsInfinity(springStrength) || springStrength < 0)
				throw new ArgumentOutOfRangeException(nameof(springStrength));
			SpringStrength = springStrength;
		}

		/// <summary>Stable relation id stored on each edge record.</summary>
		public string Id { get; }

		/// <summary>Human-readable relation name; defaults to <see cref="Id" /> when omitted.</summary>
		public string Label { get; }

		/// <summary>Edge line color in the renderer when the relation is registered on the scene schema.</summary>
		public Color Color { get; }

		/// <summary>When true, marks a parent/child hierarchy relation for host tree or grouping logic.</summary>
		public bool IsHierarchy { get; }

		/// <summary>
		///     When <see cref="IsHierarchy" /> is true, parent direction: true means the <c>From</c> endpoint is the parent;
		///     false means the <c>To</c> endpoint is the parent.
		/// </summary>
		public bool ParentIsFrom { get; }

		/// <summary>Force-layout spring weight for this relation; unregistered relations use a built-in default.</summary>
		public float SpringStrength { get; }
	}

	/// <summary>
	///     Host-supplied meaning and presentation for a graph. <see cref="GraphDocument" /> instances remain open:
	///     they may contain types, relations, and properties not declared here.
	/// </summary>
	public sealed class GraphSchema
	{
		private readonly Dictionary<string, GraphFieldDefinition> fields = new(StringComparer.Ordinal);
		private readonly Dictionary<string, GraphRelationDefinition> relations = new(StringComparer.Ordinal);
		private readonly Dictionary<string, GraphNodeTypeDefinition> types = new(StringComparer.Ordinal);

		/// <summary>Creates an empty schema; populate via <see cref="Register(GraphNodeTypeDefinition)" /> and related overloads.</summary>
		public GraphSchema()
		{
			NodeTypes = new ReadOnlyDictionary<string, GraphNodeTypeDefinition>(types);
			Relations = new ReadOnlyDictionary<string, GraphRelationDefinition>(relations);
			Fields = new ReadOnlyDictionary<string, GraphFieldDefinition>(fields);
		}

		/// <summary>Registered node types keyed by id (ordinal comparison).</summary>
		public IReadOnlyDictionary<string, GraphNodeTypeDefinition> NodeTypes { get; }

		/// <summary>Registered relation types keyed by id (ordinal comparison).</summary>
		public IReadOnlyDictionary<string, GraphRelationDefinition> Relations { get; }

		/// <summary>Registered field definitions keyed by property id (ordinal comparison).</summary>
		public IReadOnlyDictionary<string, GraphFieldDefinition> Fields { get; }

		/// <summary>Adds a node type. Duplicate ids throw rather than replace.</summary>
		/// <param name="definition">Type to register.</param>
		/// <exception cref="ArgumentNullException"><paramref name="definition" /> is null.</exception>
		/// <exception cref="ArgumentException">
		///     A type with the same <see cref="GraphNodeTypeDefinition.Id" /> is already
		///     registered.
		/// </exception>
		public void Register(GraphNodeTypeDefinition definition)
		{
			if (definition == null) throw new ArgumentNullException(nameof(definition));
			types.Add(definition.Id, definition);
		}

		/// <summary>Adds a relation type. Duplicate ids throw rather than replace.</summary>
		/// <param name="definition">Relation to register.</param>
		/// <exception cref="ArgumentNullException"><paramref name="definition" /> is null.</exception>
		/// <exception cref="ArgumentException">
		///     A relation with the same <see cref="GraphRelationDefinition.Id" /> is already
		///     registered.
		/// </exception>
		public void Register(GraphRelationDefinition definition)
		{
			if (definition == null) throw new ArgumentNullException(nameof(definition));
			relations.Add(definition.Id, definition);
		}

		/// <summary>Adds a field definition. Duplicate ids throw rather than replace.</summary>
		/// <param name="definition">Field to register.</param>
		/// <exception cref="ArgumentNullException"><paramref name="definition" /> is null.</exception>
		/// <exception cref="ArgumentException">A field with the same <see cref="GraphFieldDefinition.Id" /> is already registered.</exception>
		public void Register(GraphFieldDefinition definition)
		{
			if (definition == null) throw new ArgumentNullException(nameof(definition));
			fields.Add(definition.Id, definition);
		}

		/// <summary>
		///     Returns non-fatal validation messages for a document against this schema.
		///     Checks registered node and relation ids and value types for declared fields; undeclared properties are allowed.
		/// </summary>
		/// <param name="document">Document to inspect.</param>
		/// <returns>Human-readable issue strings; empty when no problems are found.</returns>
		/// <exception cref="ArgumentNullException"><paramref name="document" /> is null.</exception>
		public IReadOnlyList<string> Validate(GraphDocument document)
		{
			if (document == null) throw new ArgumentNullException(nameof(document));
			var issues = new List<string>();

			// Check node types and declared fields independently from relation definitions.
			foreach (var node in document.Nodes.Values)
			{
				if (!types.ContainsKey(node.TypeId)) issues.Add("Unregistered node type: " + node.TypeId);
				ValidateFields(node.Id, node.Properties, issues);
			}

			foreach (var edge in document.Edges.Values)
			{
				if (!relations.ContainsKey(edge.TypeId)) issues.Add("Unregistered relation type: " + edge.TypeId);
				ValidateFields(edge.Id, edge.Properties, issues);
			}

			return issues.AsReadOnly();
		}

		private void ValidateFields(string owner, IReadOnlyDictionary<string, GraphValue> properties,
									List<string> issues)
		{
			// Undeclared and explicit-null fields are allowed; report only mismatches against known scalar types.
			foreach (var pair in properties)
				if (fields.TryGetValue(pair.Key, out var field) && pair.Value.Type != GraphValueType.Null &&
					pair.Value.Type != field.Type)
					issues.Add(owner + ": field " + pair.Key + " expects " + field.Type);
		}
	}
}