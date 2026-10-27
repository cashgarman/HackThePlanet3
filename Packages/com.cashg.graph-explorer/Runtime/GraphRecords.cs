using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace Cashg.GraphExplorer
{
	/// <summary>Lists graph value type.</summary>
	public enum GraphValueType
	{
		/// <summary>Represents the absence of a value.</summary>
		Null,

		/// <summary>Represents a text value.</summary>
		Text,

		/// <summary>Represents a numeric value.</summary>
		Number,

		/// <summary>Represents a Boolean value.</summary>
		Boolean
	}

	/// <summary>Portable scalar metadata. Numbers are finite; null differs from missing.</summary>
	public readonly struct GraphValue : IEquatable<GraphValue>
	{
		/// <summary>Gets the representation kind stored by this value.</summary>
		public GraphValueType Type { get; }

		/// <summary>Gets the text payload when <see cref="Type" /> is <see cref="GraphValueType.Text" />.</summary>
		public string Text { get; }

		/// <summary>Gets the numeric payload when <see cref="Type" /> is <see cref="GraphValueType.Number" />.</summary>
		public double Number { get; }

		/// <summary>Gets the Boolean payload when <see cref="Type" /> is <see cref="GraphValueType.Boolean" />.</summary>
		public bool Boolean { get; }

		private GraphValue(GraphValueType type, string text = null, double number = 0, bool boolean = false)
		{
			Type = type;
			Text = text;
			Number = number;
			Boolean = boolean;
		}

		/// <summary>Gets the default null value.</summary>
		public static GraphValue Null => default;

		/// <summary>Creates a text value, treating null as an empty string.</summary>
		/// <param name="value">Text to store.</param>
		/// <returns>A value containing the supplied text.</returns>
		public static GraphValue From(string value)
		{
			return value == null ? Null : new GraphValue(GraphValueType.Text, value);
		}

		/// <summary>Creates a Boolean value.</summary>
		/// <param name="value">Boolean to store.</param>
		/// <returns>A value containing the supplied Boolean.</returns>
		public static GraphValue From(bool value)
		{
			return new GraphValue(GraphValueType.Boolean, boolean: value);
		}

		/// <summary>Creates a numeric value.</summary>
		/// <param name="value">Number to store.</param>
		/// <returns>A value containing the supplied number.</returns>
		public static GraphValue From(double value)
		{
			if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
			return new GraphValue(GraphValueType.Number, number: value);
		}

		/// <summary>Formats the stored payload as text.</summary>
		/// <returns>The payload formatted for display.</returns>
		public override string ToString()
		{
			return Type == GraphValueType.Text ? Text :
				Type == GraphValueType.Number ? Number.ToString("R", CultureInfo.InvariantCulture) :
				Type == GraphValueType.Boolean ? Boolean ? "true" : "false" : "";
		}

		/// <summary>Determines whether another value has the same kind and payload.</summary>
		/// <param name="other">Value to compare.</param>
		/// <returns><see langword="true" /> when both values are equal.</returns>
		public bool Equals(GraphValue other)
		{
			return Type == other.Type && Text == other.Text && Number == other.Number && Boolean == other.Boolean;
		}

		/// <summary>Determines whether the specified object is an equal <see cref="GraphValue" />.</summary>
		/// <param name="obj">Object to compare.</param>
		/// <returns><see langword="true" /> when <paramref name="obj" /> is an equal value.</returns>
		public override bool Equals(object obj)
		{
			return obj is GraphValue other && Equals(other);
		}

		/// <summary>Returns a hash code for this value.</summary>
		/// <returns>A hash code consistent with <see cref="Equals(GraphValue)" />.</returns>
		public override int GetHashCode()
		{
			return HashCode.Combine(Type, Text, Number, Boolean);
		}
	}

	internal static class GraphRecordCopy
	{
		public static string Required(string value, string name)
		{
			return !string.IsNullOrWhiteSpace(value)
				? value
				: throw new ArgumentException("An opaque nonempty ID is required.", name);
		}

		public static Dictionary<string, GraphValue> Properties(IEnumerable<KeyValuePair<string, GraphValue>> values)
		{
			// Copy caller-owned properties with ordinal keys, rejecting empty or duplicate identities.
			var result = new Dictionary<string, GraphValue>(StringComparer.Ordinal);
			if (values != null)
				foreach (var pair in values)
					result.Add(Required(pair.Key, "property"), pair.Value);
			return result;
		}

		public static IReadOnlyList<string> Sources(IEnumerable<string> values)
		{
			// Validate and deduplicate evidence identifiers before exposing an immutable copy.
			return Array.AsReadOnly(
				(values ?? Array.Empty<string>()).Select(value => Required(value, "source"))
												 .Distinct(StringComparer.Ordinal).ToArray());
		}
	}

	/// <summary>Immutable semantic facts. Layout, filters and selection belong to GraphViewState.</summary>
	public sealed class GraphNodeRecord
	{
		/// <summary>Creates an immutable node record.</summary>
		/// <param name="id">Stable node identifier.</param>
		/// <param name="typeId">Node type identifier.</param>
		/// <param name="label">Display label.</param>
		/// <param name="properties">Node property values.</param>
		/// <param name="sources">Source identifiers associated with the node.</param>
		public GraphNodeRecord(string id, string typeId, string label,
							   IEnumerable<KeyValuePair<string, GraphValue>> properties = null,
							   IEnumerable<string> sources = null)
			: this(id, typeId, label, GraphRecordCopy.Properties(properties), GraphRecordCopy.Sources(sources))
		{
		}

		private GraphNodeRecord(string id, string typeId, string label, Dictionary<string, GraphValue> properties,
								IReadOnlyList<string> sources)
		{
			Id = GraphRecordCopy.Required(id, nameof(id));
			TypeId = GraphRecordCopy.Required(typeId, nameof(typeId));
			Label = label ?? "";
			Properties = new ReadOnlyDictionary<string, GraphValue>(properties);
			Sources = sources;
		}

		/// <summary>Gets the stable node identifier.</summary>
		public string Id { get; }

		/// <summary>Gets the node type identifier.</summary>
		public string TypeId { get; }

		/// <summary>Gets the display label.</summary>
		public string Label { get; }

		/// <summary>Gets the node's immutable property map.</summary>
		public IReadOnlyDictionary<string, GraphValue> Properties { get; }

		/// <summary>Gets the source identifiers associated with the node.</summary>
		public IReadOnlyList<string> Sources { get; }

		/// <summary>Creates a copy with a different type identifier or label.</summary>
		/// <param name="typeId">Replacement type identifier.</param>
		/// <param name="label">Replacement display label.</param>
		/// <returns>A new node record with the updated identity fields.</returns>
		public GraphNodeRecord WithIdentity(string typeId, string label)
		{
			return new GraphNodeRecord(Id, typeId, label, GraphRecordCopy.Properties(Properties), Sources);
		}

		/// <summary>Creates a copy with a property added, replaced, or removed.</summary>
		/// <param name="key">Property key to update.</param>
		/// <param name="value">Replacement value, or null to remove the property.</param>
		/// <returns>A new node record containing the updated property map.</returns>
		public GraphNodeRecord WithProperty(string key, GraphValue? value)
		{
			GraphRecordCopy.Required(key, nameof(key));

			// Copy the property map so setting or removing one entry leaves the original record intact.
			var values = GraphRecordCopy.Properties(Properties);
			if (value.HasValue) values[key] = value.Value;
			else values.Remove(key);
			return new GraphNodeRecord(Id, TypeId, Label, values, Sources);
		}

		/// <summary>Creates a copy with a replacement set of source identifiers.</summary>
		/// <param name="sources">Sources to associate with the node.</param>
		/// <returns>A new node record with the supplied sources.</returns>
		public GraphNodeRecord WithSources(IEnumerable<string> sources)
		{
			return new GraphNodeRecord(Id, TypeId, Label, GraphRecordCopy.Properties(Properties),
									   GraphRecordCopy.Sources(sources));
		}
	}

	/// <summary>Represents graph edge record.</summary>
	public sealed class GraphEdgeRecord
	{
		/// <summary>Creates an immutable edge record.</summary>
		/// <param name="id">Stable edge identifier.</param>
		/// <param name="from">Identifier of the source node.</param>
		/// <param name="to">Identifier of the destination node.</param>
		/// <param name="typeId">Relationship type identifier.</param>
		/// <param name="status">Relationship status.</param>
		/// <param name="confidence">Optional confidence value.</param>
		/// <param name="properties">Edge property values.</param>
		/// <param name="sources">Source identifiers associated with the edge.</param>
		public GraphEdgeRecord(string id, string from, string to, string typeId, string status = "observed",
							   double? confidence = null,
							   IEnumerable<KeyValuePair<string, GraphValue>> properties = null,
							   IEnumerable<string> sources = null)
		{
			Id = GraphRecordCopy.Required(id, nameof(id));
			From = GraphRecordCopy.Required(from, nameof(from));
			To = GraphRecordCopy.Required(to, nameof(to));
			TypeId = GraphRecordCopy.Required(typeId, nameof(typeId));
			if (confidence.HasValue && (double.IsNaN(confidence.Value) || double.IsInfinity(confidence.Value)))
				throw new ArgumentOutOfRangeException(nameof(confidence),
													  "Confidence must be absent or finite; its scale belongs to the dataset.");
			Status = status ?? "";
			Confidence = confidence;
			Properties = new ReadOnlyDictionary<string, GraphValue>(GraphRecordCopy.Properties(properties));
			Sources = GraphRecordCopy.Sources(sources);
		}

		/// <summary>Gets the stable edge identifier.</summary>
		public string Id { get; }

		/// <summary>Gets the source node identifier.</summary>
		public string From { get; }

		/// <summary>Gets the destination node identifier.</summary>
		public string To { get; }

		/// <summary>Gets the relationship type identifier.</summary>
		public string TypeId { get; }

		/// <summary>Gets the relationship status.</summary>
		public string Status { get; }

		/// <summary>Gets the optional confidence value.</summary>
		public double? Confidence { get; }

		/// <summary>Gets the edge's immutable property map.</summary>
		public IReadOnlyDictionary<string, GraphValue> Properties { get; }

		/// <summary>Gets the source identifiers associated with the edge.</summary>
		public IReadOnlyList<string> Sources { get; }

		/// <summary>Creates a copy with updated confidence and source evidence.</summary>
		/// <param name="confidence">Replacement confidence value.</param>
		/// <param name="sources">Replacement source identifiers.</param>
		/// <returns>A new edge record with the supplied evidence.</returns>
		public GraphEdgeRecord WithEvidence(double? confidence, IEnumerable<string> sources)
		{
			return new GraphEdgeRecord(Id, From, To, TypeId, Status, confidence, Properties, sources);
		}
	}

	/// <summary>Represents graph snapshot.</summary>
	public sealed class GraphSnapshot
	{
		/// <summary>Creates a dataset snapshot from node and edge records.</summary>
		/// <param name="datasetId">Stable identifier of the dataset.</param>
		/// <param name="displayName">Human-readable dataset name.</param>
		/// <param name="nodes">Node records in the snapshot.</param>
		/// <param name="edges">Edge records in the snapshot.</param>
		public GraphSnapshot(string datasetId, string displayName, IEnumerable<GraphNodeRecord> nodes,
							 IEnumerable<GraphEdgeRecord> edges)
		{
			DatasetId = GraphRecordCopy.Required(datasetId, nameof(datasetId));
			DisplayName = displayName ?? "";
			Nodes = Array.AsReadOnly((nodes ?? throw new ArgumentNullException(nameof(nodes))).ToArray());
			Edges = Array.AsReadOnly((edges ?? throw new ArgumentNullException(nameof(edges))).ToArray());
		}

		/// <summary>Gets the stable dataset identifier.</summary>
		public string DatasetId { get; }

		/// <summary>Gets the human-readable dataset name.</summary>
		public string DisplayName { get; }

		/// <summary>Gets the snapshot's node records.</summary>
		public IReadOnlyList<GraphNodeRecord> Nodes { get; }

		/// <summary>Gets the snapshot's edge records.</summary>
		public IReadOnlyList<GraphEdgeRecord> Edges { get; }
	}

	/// <summary>One atomic update: upserts replace facts; node removal also removes incident edges.</summary>
	public sealed class GraphBatch
	{
		/// <summary>Contains edge records to add or update.</summary>
		public readonly List<GraphEdgeRecord> Edges = new();

		/// <summary>Contains node records to add or update.</summary>
		public readonly List<GraphNodeRecord> Nodes = new();

		/// <summary>Contains edge identifiers to remove.</summary>
		public readonly List<string> RemoveEdges = new();

		/// <summary>Contains node identifiers to remove.</summary>
		public readonly List<string> RemoveNodes = new();
	}
}