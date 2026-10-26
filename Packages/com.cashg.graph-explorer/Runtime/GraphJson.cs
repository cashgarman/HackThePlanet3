using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnityEngine;

namespace Cashg.GraphExplorer
{
	/// <summary>Versioned semantic document format. View state and application credentials are not included.</summary>
	public static class GraphJson
	{
		/// <summary>Serializes a graph document to JSON.</summary>
		/// <param name="document">Document to serialize.</param>
		/// <returns>JSON representing the document.</returns>
		public static string Serialize(GraphDocument document)
		{
			if (document == null) throw new ArgumentNullException(nameof(document));
			using (var output = new MemoryStream())
			{
				using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true }))
				{
					// Write records in ordinal order so equivalent documents produce stable JSON.

					// The timeline is an optional extension: v1 readers continue to load the semantic snapshot.
					writer.WriteStartObject();
					writer.WriteNumber("schemaVersion", 1);
					writer.WriteString("datasetId", document.DatasetId);
					writer.WriteString("displayName", document.DisplayName);
					writer.WriteStartArray("nodes");
					foreach (var node in document.Nodes.Values.OrderBy(node => node.Id, StringComparer.Ordinal))
						WriteNode(writer, node);
					writer.WriteEndArray();
					writer.WriteStartArray("edges");
					foreach (var edge in document.Edges.Values.OrderBy(edge => edge.Id, StringComparer.Ordinal))
						WriteEdge(writer, edge);
					writer.WriteEndArray();
					WriteTimeline(writer, document.Timeline);
					WriteWorkspace(writer, document.Timeline.Workspace);
					writer.WriteEndObject();
				}

				return Encoding.UTF8.GetString(output.ToArray());
			}
		}

		private static void WriteSources(Utf8JsonWriter writer, IEnumerable<string> sources)
		{
			writer.WriteStartArray("sources");
			foreach (var source in sources.OrderBy(source => source, StringComparer.Ordinal))
				writer.WriteStringValue(source);
			writer.WriteEndArray();
		}

		private static void WriteProperties(Utf8JsonWriter writer, IReadOnlyDictionary<string, GraphValue> properties)
		{
			// Write native JSON scalar values in ordinal key order, retaining explicit null properties.
			writer.WriteStartObject("properties");
			foreach (var pair in properties.OrderBy(pair => pair.Key, StringComparer.Ordinal))
				switch (pair.Value.Type)
				{
					case GraphValueType.Text:    writer.WriteString(pair.Key, pair.Value.Text); break;
					case GraphValueType.Number:  writer.WriteNumber(pair.Key, pair.Value.Number); break;
					case GraphValueType.Boolean: writer.WriteBoolean(pair.Key, pair.Value.Boolean); break;
					default:                     writer.WriteNull(pair.Key); break;
				}

			writer.WriteEndObject();
		}

		private static void WriteNode(Utf8JsonWriter writer, GraphNodeRecord node)
		{
			writer.WriteStartObject();
			writer.WriteString("id", node.Id);
			writer.WriteString("type", node.TypeId);
			writer.WriteString("label", node.Label);
			WriteProperties(writer, node.Properties);
			WriteSources(writer, node.Sources);
			writer.WriteEndObject();
		}

		private static void WriteEdge(Utf8JsonWriter writer, GraphEdgeRecord edge)
		{
			writer.WriteStartObject();
			writer.WriteString("id", edge.Id);
			writer.WriteString("from", edge.From);
			writer.WriteString("to", edge.To);
			writer.WriteString("type", edge.TypeId);
			writer.WriteString("status", edge.Status);
			if (edge.Confidence.HasValue) writer.WriteNumber("confidence", edge.Confidence.Value);
			else writer.WriteNull("confidence");
			WriteProperties(writer, edge.Properties);
			WriteSources(writer, edge.Sources);
			writer.WriteEndObject();
		}

		private static void WriteTimeline(Utf8JsonWriter writer, GraphTimeline timeline)
		{
			writer.WriteStartObject("timeline");
			writer.WriteBoolean("positionRecordingEnabled", timeline.PositionRecordingEnabled);
			writer.WriteNumber("positionSampleIntervalSeconds", timeline.PositionSampleIntervalSeconds);
			writer.WriteString("currentTimeUtc", timeline.CurrentTimeUtc.ToString("O"));

			// Persist the reconstruction baseline separately from timestamped semantic and motion tracks.
			if (timeline.HasBaseline)
			{
				writer.WriteString("baselineTimeUtc", timeline.BaselineTimeUtc.ToString("O"));
				writer.WriteNumber("cursor", timeline.Cursor);
				writer.WriteStartObject("baseline");
				writer.WriteString("datasetId", timeline.Baseline.DatasetId);
				writer.WriteString("displayName", timeline.Baseline.DisplayName);
				writer.WriteStartArray("nodes");
				foreach (var node in timeline.Baseline.Nodes.OrderBy(item => item.Id, StringComparer.Ordinal))
					WriteNode(writer, node);
				writer.WriteEndArray();
				writer.WriteStartArray("edges");
				foreach (var edge in timeline.Baseline.Edges.OrderBy(item => item.Id, StringComparer.Ordinal))
					WriteEdge(writer, edge);
				writer.WriteEndArray();
				writer.WriteEndObject();
			}

			// Position frames store canonical node coordinates independently from semantic edits.
			writer.WriteStartArray("positionFrames");
			foreach (var frame in timeline.PositionFrames)
			{
				writer.WriteStartObject();
				writer.WriteString("timestampUtc", frame.TimestampUtc.ToString("O"));
				writer.WriteStartArray("positions");
				foreach (var pair in frame.Positions.OrderBy(item => item.Key, StringComparer.Ordinal))
				{
					writer.WriteStartObject();
					writer.WriteString("nodeId", pair.Key);
					writer.WriteNumber("x", pair.Value.x);
					writer.WriteNumber("y", pair.Value.y);
					writer.WriteNumber("z", pair.Value.z);
					writer.WriteEndObject();
				}

				writer.WriteEndArray();
				writer.WriteEndObject();
			}

			writer.WriteEndArray();
			writer.WriteStartArray("entries");

			// Keep both record versions so each edit retains its full before/after evidence.
			foreach (var entry in timeline.Entries)
			{
				writer.WriteStartObject();
				writer.WriteNumber("sequence", entry.Sequence);
				writer.WriteString("timestampUtc", entry.TimestampUtc.ToString("O"));
				writer.WriteString("displayNameBefore", entry.Change.DisplayNameBefore);
				writer.WriteString("displayNameAfter", entry.Change.DisplayNameAfter);
				WriteRecordArray(writer, "nodesBefore", entry.Change.NodesBefore, WriteNode);
				WriteRecordArray(writer, "nodesAfter", entry.Change.NodesAfter, WriteNode);
				WriteRecordArray(writer, "edgesBefore", entry.Change.EdgesBefore, WriteEdge);
				WriteRecordArray(writer, "edgesAfter", entry.Change.EdgesAfter, WriteEdge);
				writer.WriteEndObject();
			}

			writer.WriteEndArray();
			writer.WriteEndObject();
		}

		private static void WriteRecordArray<T>(Utf8JsonWriter writer, string name, IEnumerable<T> records,
												Action<Utf8JsonWriter, T> write)
		{
			writer.WriteStartArray(name);
			foreach (var record in records) write(writer, record);
			writer.WriteEndArray();
		}

		/// <summary>Parses JSON into a graph document.</summary>
		/// <param name="json">JSON text containing a graph document.</param>
		/// <returns>The parsed document.</returns>
		public static GraphDocument Parse(string json)
		{
			using (var parsed = JsonDocument.Parse(json))
			{
				var root = parsed.RootElement;

				// Reject unsupported formats before constructing a document from the semantic snapshot.
				var version = root.GetProperty("schemaVersion").GetInt32();
				if (version != 1) throw new FormatException("Unsupported graph document version.");
				var document = new GraphDocument(root.GetProperty("datasetId").GetString(),
												 root.GetProperty("displayName").GetString());
				var batch = new GraphBatch();
				foreach (var node in root.GetProperty("nodes").EnumerateArray()) batch.Nodes.Add(ReadNode(node));
				foreach (var edge in root.GetProperty("edges").EnumerateArray()) batch.Edges.Add(ReadEdge(edge));
				document.Apply(batch);

				// Older documents may omit history; restore it only when a baseline is present.
				if (root.TryGetProperty("timeline", out var timeline) &&
					timeline.TryGetProperty("baseline", out var baseline) && baseline.ValueKind == JsonValueKind.Object)
				{
					var baselineNodes = baseline.GetProperty("nodes").EnumerateArray().Select(ReadNode).ToArray();
					var baselineEdges = baseline.GetProperty("edges").EnumerateArray().Select(ReadEdge).ToArray();
					var baselineSnapshot = new GraphSnapshot(baseline.GetProperty("datasetId").GetString(),
															 baseline.GetProperty("displayName").GetString(),
															 baselineNodes, baselineEdges);

					// Deserialize ordered semantic changes before restoring the saved cursor.
					var entries = new List<GraphTimelineEntry>();
					foreach (var item in timeline.GetProperty("entries").EnumerateArray())
					{
						var change = new GraphTimelineChange(
							item.GetProperty("nodesBefore").EnumerateArray().Select(ReadNode),
							item.GetProperty("nodesAfter").EnumerateArray().Select(ReadNode),
							item.GetProperty("edgesBefore").EnumerateArray().Select(ReadEdge),
							item.GetProperty("edgesAfter").EnumerateArray().Select(ReadEdge),
							item.GetProperty("displayNameBefore").GetString(),
							item.GetProperty("displayNameAfter").GetString());
						entries.Add(new GraphTimelineEntry(item.GetProperty("sequence").GetInt64(),
														   DateTimeOffset.Parse(
															   item.GetProperty("timestampUtc").GetString(),
															   CultureInfo.InvariantCulture, DateTimeStyles.None),
														   change));
					}

					// Load optional motion samples and recording settings with backward-compatible defaults.
					var positionFrames = new List<GraphPositionFrame>();
					if (timeline.TryGetProperty("positionFrames", out var savedFrames))
						foreach (var item in savedFrames.EnumerateArray())
							positionFrames.Add(new GraphPositionFrame(
												   DateTimeOffset.Parse(item.GetProperty("timestampUtc").GetString(),
																		CultureInfo.InvariantCulture,
																		DateTimeStyles.None),
												   item.GetProperty("positions").EnumerateArray()
													   .Select(position => new KeyValuePair<string, Vector3>(
																   position.GetProperty("nodeId").GetString(),
																   new Vector3(
																	   position.GetProperty("x").GetSingle(),
																	   position.GetProperty("y").GetSingle(),
																	   position.GetProperty("z").GetSingle())))));
					var baselineTime = DateTimeOffset.Parse(timeline.GetProperty("baselineTimeUtc").GetString(),
															CultureInfo.InvariantCulture, DateTimeStyles.None);
					var cursor = timeline.TryGetProperty("cursor", out var savedCursor)
						? savedCursor.GetInt32()
						: entries.Count - 1;
					var recording = timeline.TryGetProperty("positionRecordingEnabled", out var savedRecording) &&
									savedRecording.GetBoolean();
					var interval = timeline.TryGetProperty("positionSampleIntervalSeconds", out var savedInterval)
						? savedInterval.GetSingle()
						: 1f;
					var currentTime = timeline.TryGetProperty("currentTimeUtc", out var savedTime)
						? DateTimeOffset.Parse(savedTime.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None)
						: (DateTimeOffset?)null;
					document.RestoreTimeline(baselineSnapshot, baselineTime, entries, cursor, positionFrames, recording,
											 interval, currentTime);
				}

				if (root.TryGetProperty("timelineWorkspace", out var workspace)) ReadWorkspace(workspace, document);
				return document;
			}
		}

		private static void WriteSnapshot(Utf8JsonWriter writer, GraphSnapshot snapshot)
		{
			writer.WriteStartObject();
			writer.WriteString("datasetId", snapshot.DatasetId);
			writer.WriteString("displayName", snapshot.DisplayName);
			WriteRecordArray(writer, "nodes", snapshot.Nodes.OrderBy(item => item.Id, StringComparer.Ordinal),
							 WriteNode);
			WriteRecordArray(writer, "edges", snapshot.Edges.OrderBy(item => item.Id, StringComparer.Ordinal),
							 WriteEdge);
			writer.WriteEndObject();
		}

		private static GraphSnapshot ReadSnapshot(JsonElement value)
		{
			return new GraphSnapshot(value.GetProperty("datasetId").GetString(),
									 value.GetProperty("displayName").GetString(),
									 value.GetProperty("nodes").EnumerateArray().Select(ReadNode),
									 value.GetProperty("edges").EnumerateArray().Select(ReadEdge));
		}

		private static void WriteWorkspace(Utf8JsonWriter writer, GraphTimelineWorkspace workspace)
		{
			writer.WriteStartObject("timelineWorkspace");
			writer.WriteNumber("version", 1);
			if (workspace.HasSelection)
			{
				writer.WriteString("selectionStartUtc", workspace.SelectionStartUtc.Value.ToString("O"));
				writer.WriteString("selectionEndUtc", workspace.SelectionEndUtc.Value.ToString("O"));
			}

			if (workspace.ViewStartUtc.HasValue)
			{
				writer.WriteString("viewStartUtc", workspace.ViewStartUtc.Value.ToString("O"));
				writer.WriteString("viewEndUtc", workspace.ViewEndUtc.Value.ToString("O"));
			}

			var snapshots = new Dictionary<string, string>(StringComparer.Ordinal);

			// Reference cached snapshots by content hash so events sharing a state serialize it once.
			var identities = new Dictionary<GraphSnapshot, string>();
			writer.WriteStartArray("events");
			foreach (var item in workspace.CachedEvents.OrderBy(item => item.Context, StringComparer.Ordinal)
										  .ThenBy(item => item.Event.Id, StringComparer.Ordinal))
			{
				if (!identities.TryGetValue(item.Snapshot, out var identity))
				{
					using var output = new MemoryStream();
					using (var snapshotWriter = new Utf8JsonWriter(output))
					{
						WriteSnapshot(snapshotWriter, item.Snapshot);
					}

					var bytes = output.ToArray();
					using var sha = SHA256.Create();
					identity = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
					snapshots[identity] = Encoding.UTF8.GetString(bytes);
					identities[item.Snapshot] = identity;
				}

				writer.WriteStartObject();
				writer.WriteString("id", item.Event.Id);
				writer.WriteString("timestampUtc", item.Event.TimestampUtc.ToString("O"));
				writer.WriteString("label", item.Event.Label);
				writer.WriteString("details", item.Event.Details);
				writer.WriteString("tooltip", item.Event.Tooltip);
				writer.WriteString("context", item.Context);
				writer.WriteString("snapshot", identity);
				writer.WriteEndObject();
			}

			writer.WriteEndArray();
			writer.WriteStartObject("snapshots");

			// Emit the deduplicated snapshot table after writing event references.
			foreach (var pair in snapshots.OrderBy(pair => pair.Key, StringComparer.Ordinal))
			{
				writer.WritePropertyName(pair.Key);
				using var parsed = JsonDocument.Parse(pair.Value);
				parsed.RootElement.WriteTo(writer);
			}

			writer.WriteEndObject();
			writer.WriteEndObject();
		}

		private static void ReadWorkspace(JsonElement value, GraphDocument document)
		{
			if (value.GetProperty("version").GetInt32() != 1)
				throw new FormatException("Unsupported timeline workspace version.");

			DateTimeOffset Date(string key)
			{
				return DateTimeOffset.Parse(value.GetProperty(key).GetString(), CultureInfo.InvariantCulture,
											DateTimeStyles.None);
			}

			var workspace = document.Timeline.Workspace;

			// Restore portable selection and viewport bounds before reconnecting cached event snapshots.
			if (value.TryGetProperty("selectionStartUtc", out _))
				workspace.SelectRange(Date("selectionStartUtc"), Date("selectionEndUtc"));
			if (value.TryGetProperty("viewStartUtc", out _))
				workspace.SetView(Date("viewStartUtc"), Date("viewEndUtc"));
			var snapshots = value.GetProperty("snapshots").EnumerateObject()
								 .ToDictionary(item => item.Name, item => ReadSnapshot(item.Value),
											   StringComparer.Ordinal);
			foreach (var snapshot in snapshots.Values)
			{
				// Validate dataset ownership and graph consistency before admitting a cached state.
				if (snapshot.DatasetId != document.DatasetId)
					throw new FormatException("Cached timeline state belongs to another dataset.");
				var validation = new GraphDocument(document.DatasetId);
				validation.ReplaceSnapshot(snapshot);
			}

			// Resolve each event's snapshot reference against the validated table.
			foreach (var item in value.GetProperty("events").EnumerateArray())
				workspace.Store(new GraphTimelineEvent(item.GetProperty("id").GetString(),
													   DateTimeOffset.Parse(
														   item.GetProperty("timestampUtc").GetString(),
														   CultureInfo.InvariantCulture, DateTimeStyles.None),
													   item.GetProperty("label").GetString(),
													   item.GetProperty("details").GetString(),
													   item.GetProperty("tooltip").GetString()),
								item.GetProperty("context").GetString(),
								snapshots[item.GetProperty("snapshot").GetString()]);
		}

		private static IEnumerable<string> ReadSources(JsonElement owner)
		{
			return owner.TryGetProperty("sources", out var sources)
				? sources.EnumerateArray().Select(source => source.GetString()).ToArray()
				: Array.Empty<string>();
		}

		private static GraphNodeRecord ReadNode(JsonElement node)
		{
			return new GraphNodeRecord(node.GetProperty("id").GetString(), node.GetProperty("type").GetString(),
									   node.GetProperty("label").GetString(), ReadProperties(node), ReadSources(node));
		}

		private static GraphEdgeRecord ReadEdge(JsonElement edge)
		{
			var confidence = edge.TryGetProperty("confidence", out var value) && value.ValueKind != JsonValueKind.Null
				? value.GetDouble()
				: (double?)null;
			return new GraphEdgeRecord(edge.GetProperty("id").GetString(), edge.GetProperty("from").GetString(),
									   edge.GetProperty("to").GetString(),
									   edge.GetProperty("type").GetString(), edge.GetProperty("status").GetString(),
									   confidence, ReadProperties(edge), ReadSources(edge));
		}

		private static Dictionary<string, GraphValue> ReadProperties(JsonElement owner)
		{
			var values = new Dictionary<string, GraphValue>(StringComparer.Ordinal);
			if (!owner.TryGetProperty("properties", out var properties)) return values;

			// Preserve scalar types and reject nested metadata rather than silently discarding it.
			foreach (var property in properties.EnumerateObject())
				switch (property.Value.ValueKind)
				{
					case JsonValueKind.String:
						values.Add(property.Name, GraphValue.From(property.Value.GetString())); break;
					case JsonValueKind.Number:
						values.Add(property.Name, GraphValue.From(property.Value.GetDouble())); break;
					case JsonValueKind.True: values.Add(property.Name, GraphValue.From(true)); break;
					case JsonValueKind.False: values.Add(property.Name, GraphValue.From(false)); break;
					case JsonValueKind.Null: values.Add(property.Name, GraphValue.Null); break;
					default: throw new FormatException("Graph metadata values must be scalar: " + property.Name);
				}

			return values;
		}
	}
}
