using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Cashg.GraphExplorer.Tests
{
	/// <summary>Represents graph document tests.</summary>
	public sealed class GraphDocumentTests
	{
		private static GraphNodeRecord Node(string id, string type = "symbol")
		{
			return new GraphNodeRecord(id, type, "Label " + id);
		}

		private static GraphEdgeRecord Edge(string id, string from = "A", string to = "a", string type = "calls")
		{
			return new GraphEdgeRecord(id, from, to, type);
		}

		private static GraphDocument Pair()
		{
			var graph = new GraphDocument("repo:fixture", "Fixture");
			graph.UpsertNode(Node("A"));
			graph.UpsertNode(Node("a"));
			return graph;
		}

		/// <summary>Checks that opaque Ids And Custom Types Remain Distinct.</summary>
		[Test]
		public void OpaqueIdsAndCustomTypesRemainDistinct()
		{
			var graph = Pair();
			graph.UpsertNode(Node(" path:with spaces/Ω ", "custom/type"));
			graph.UpsertEdge(Edge("Call:1"));
			graph.UpsertEdge(Edge("call:1", "a", "A"));
			Assert.That(graph.Nodes.Count, Is.EqualTo(3));
			Assert.That(graph.Edges.Count, Is.EqualTo(2));
			Assert.That(graph.Nodes[" path:with spaces/Ω "].TypeId, Is.EqualTo("custom/type"));
			Assert.That(graph.Neighborhood("A", 1), Is.EquivalentTo(new[] { "A", "a" }));
		}

		/// <summary>Checks that input Collections And Snapshots Cannot Mutate Stored Facts.</summary>
		[Test]
		public void InputCollectionsAndSnapshotsCannotMutateStoredFacts()
		{
			var fields = new Dictionary<string, GraphValue> { ["lines"] = GraphValue.From(12d) };
			var sources = new List<string> { "file.cs:1", "file.cs:1" };
			var node = new GraphNodeRecord("A", "file", "File", fields, sources);

			// Mutate the original inputs to verify records copied them before exposing read-only collections.
			fields["lines"] = GraphValue.From(99d);
			sources.Clear();
			Assert.That(node.Properties["lines"].Number, Is.EqualTo(12));
			Assert.That(node.Sources.Count, Is.EqualTo(1));
			Assert.Throws<NotSupportedException>(() => ((IDictionary<string, GraphValue>)node.Properties).Add(
													 "bad", GraphValue.Null));
			Assert.Throws<NotSupportedException>(() => ((IList<string>)node.Sources).Add("bad"));
		}

		/// <summary>Checks that invalid Batch Leaves Facts Indexes And Revisions Untouched.</summary>
		[Test]
		public void InvalidBatchLeavesFactsIndexesAndRevisionsUntouched()
		{
			// Capture the complete observable document state before applying the invalid batch.
			var graph = Pair();
			graph.UpsertEdge(Edge("existing"));
			var before = GraphJson.Serialize(graph);
			int revision = graph.Revision, topology = graph.TopologyRevision;
			var batch = new GraphBatch();
			batch.Nodes.Add(Node("new"));
			batch.RemoveEdges.Add("existing");
			batch.Edges.Add(Edge("bad", "new", "missing"));

			// A dangling endpoint must reject the whole batch, including its otherwise valid removals and additions.
			Assert.Throws<ArgumentException>(() => graph.Apply(batch));
			Assert.That(GraphJson.Serialize(graph), Is.EqualTo(before));
			Assert.That(graph.Revision, Is.EqualTo(revision));
			Assert.That(graph.TopologyRevision, Is.EqualTo(topology));
			Assert.That(graph.Neighbors("A").Contains("a"), Is.True);
		}

		/// <summary>Checks that duplicate And Conflicting Batch Entries Are Rejected Atomically.</summary>
		[Test]
		public void DuplicateAndConflictingBatchEntriesAreRejectedAtomically()
		{
			var graph = Pair();
			var batch = new GraphBatch();
			batch.Nodes.Add(Node("new"));
			batch.Nodes.Add(Node("new"));
			Assert.Throws<ArgumentException>(() => graph.Apply(batch));
			Assert.That(graph.Nodes.ContainsKey("new"), Is.False);
			batch.Nodes.RemoveAt(1);

			// Reuse the draft to test node update/removal conflicts, then equivalent edge conflicts.
			batch.RemoveNodes.Add("new");
			Assert.Throws<ArgumentException>(() => graph.Apply(batch));
			batch.Nodes.Clear();
			batch.RemoveNodes.Clear();
			batch.Edges.Add(Edge("edge"));
			batch.RemoveEdges.Add("edge");
			Assert.Throws<ArgumentException>(() => graph.Apply(batch));
			Assert.That(graph.Edges.Count, Is.Zero);
		}

		/// <summary>Checks that successful Batch Publishes One Revision And Supports Forward Endpoints.</summary>
		[Test]
		public void SuccessfulBatchPublishesOneRevisionAndSupportsForwardEndpoints()
		{
			var graph = Pair();
			int revision = graph.Revision, topology = graph.TopologyRevision;
			var batch = new GraphBatch();
			batch.Nodes.Add(Node("new"));
			batch.Nodes.Add(Node("another"));
			batch.Edges.Add(Edge("edge", "new", "another"));
			graph.Apply(batch);
			Assert.That(graph.Revision, Is.EqualTo(revision + 1));
			Assert.That(graph.TopologyRevision, Is.EqualTo(topology + 1));
			Assert.That(graph.Neighbors("new").Contains("another"), Is.True);
			graph.Apply(new GraphBatch());

			// An empty batch must not create a new document revision.
			Assert.That(graph.Revision, Is.EqualTo(revision + 1));
		}

		/// <summary>Checks that parallel And Reverse Relations Keep Adjacency Until The Last Edge Is Removed.</summary>
		[Test]
		public void ParallelAndReverseRelationsKeepAdjacencyUntilTheLastEdgeIsRemoved()
		{
			var graph = Pair();
			graph.UpsertEdge(Edge("one"));
			graph.UpsertEdge(Edge("two", type: "references"));
			graph.UpsertEdge(Edge("reverse", "a", "A"));
			Assert.That(graph.Neighbors("A").Count, Is.EqualTo(1));
			Assert.That(graph.IncidentEdges("A").Count, Is.EqualTo(3));
			graph.RemoveEdge("one");

			// Neighbor identity survives parallel and reverse edges until the final connection is removed.
			graph.RemoveEdge("reverse");
			Assert.That(graph.Neighbors("A").Contains("a"), Is.True);
			graph.RemoveEdge("two");
			Assert.That(graph.Neighbors("A").Count, Is.Zero);
			Assert.That(graph.Neighbors("a").Count, Is.Zero);
		}

		/// <summary>Checks that self Links Remain Facts Without Double Counting Degree Or Incident Edges.</summary>
		[Test]
		public void SelfLinksRemainFactsWithoutDoubleCountingDegreeOrIncidentEdges()
		{
			var graph = Pair();
			graph.UpsertEdge(Edge("recursive", "A", "A"));
			Assert.That(graph.Edges.Count, Is.EqualTo(1));
			Assert.That(graph.Neighbors("A").Count, Is.Zero);
			Assert.That(graph.IncidentEdges("A").Count, Is.EqualTo(1));
			graph.RemoveNode("A");
			Assert.That(graph.Edges.Count, Is.Zero);
			Assert.That(graph.Nodes.ContainsKey("a"), Is.True);
		}

		/// <summary>Checks that edge Retarget And Node Removal Repair Both Directions.</summary>
		[Test]
		public void EdgeRetargetAndNodeRemovalRepairBothDirections()
		{
			var graph = Pair();
			graph.UpsertNode(Node("C"));
			graph.UpsertEdge(Edge("edge"));
			graph.UpsertEdge(Edge("edge", "A", "C"));

			// Retargeting must detach the old endpoint before a later node removal clears the new connection.
			Assert.That(graph.Neighbors("a").Count, Is.Zero);
			Assert.That(graph.Neighbors("C").Contains("A"), Is.True);
			var batch = new GraphBatch();
			batch.RemoveNodes.Add("C");
			graph.Apply(batch);
			Assert.That(graph.IncidentEdges("A").Count, Is.Zero);
			Assert.That(graph.Neighbors("A").Count, Is.Zero);
			Assert.That(graph.RemoveNode("C"), Is.False);
		}

		/// <summary>Checks that metadata Updates Replace Facts Without Changing Structural Revision.</summary>
		[Test]
		public void MetadataUpdatesReplaceFactsWithoutChangingStructuralRevision()
		{
			var graph = Pair();
			int topology = graph.TopologyRevision, revision = graph.Revision;
			var previous = graph.Nodes["A"];
			graph.UpsertNode(previous.WithProperty("lines", GraphValue.From(34d)));
			Assert.That(graph.Revision, Is.EqualTo(revision + 1));
			Assert.That(graph.TopologyRevision, Is.EqualTo(topology));
			Assert.That(previous.Properties.ContainsKey("lines"), Is.False);
			graph.UpsertNode(graph.Nodes["A"].WithIdentity("method", "Renamed"));

			// Type changes alter topology even though property-only updates do not.
			Assert.That(graph.TopologyRevision, Is.EqualTo(topology + 1));
			Assert.That(graph.Nodes["A"].Label, Is.EqualTo("Renamed"));
		}

		/// <summary>Checks that snapshot Replacement Validates Before Replacing And Retains Read Only Views.</summary>
		[Test]
		public void SnapshotReplacementValidatesBeforeReplacingAndRetainsReadOnlyViews()
		{
			var graph = Pair();
			var nodes = graph.Nodes;
			var invalid = new GraphSnapshot(graph.DatasetId, "Bad", new[] { Node("B") }, new[] { Edge("dangling") });
			Assert.Throws<ArgumentException>(() => graph.ReplaceSnapshot(invalid));
			Assert.That(nodes.Count, Is.EqualTo(2));
			Assert.Throws<ArgumentException>(() => graph.ReplaceSnapshot(
												 new GraphSnapshot("another", "Bad", Array.Empty<GraphNodeRecord>(),
																   Array.Empty<GraphEdgeRecord>())));
			var revision = graph.Revision;
			graph.ReplaceSnapshot(new GraphSnapshot(graph.DatasetId, "New", new[] { Node("B") },
													Array.Empty<GraphEdgeRecord>()));

			// Previously acquired read-only dictionary views must reflect the replacement in place.
			Assert.That(nodes.Keys, Is.EquivalentTo(new[] { "B" }));
			Assert.That(graph.DisplayName, Is.EqualTo("New"));
			Assert.That(graph.Revision, Is.EqualTo(revision + 1));
		}

		/// <summary>Checks that view Changes Do Not Mutate Or Invalidate Facts.</summary>
		[Test]
		public void ViewChangesDoNotMutateOrInvalidateFacts()
		{
			var graph = Pair();
			var view = new GraphViewState(graph.DatasetId);
			var revision = graph.Revision;
			var state = view.GetOrCreate("A");
			state.Position = new Vector3(1, 2, 3);
			state.Pinned = true;
			state.Favorite = true;
			view.SelectedId = "A";
			view.FoldedNodeIds.Add("A");
			graph.UpsertNode(graph.Nodes["A"].WithProperty("lines", GraphValue.From(10d)));
			view.Synchronize(graph);
			Assert.That(view.GetOrCreate("A"), Is.SameAs(state));
			Assert.That(state.Pinned, Is.True);
			Assert.That(state.Position, Is.EqualTo(new Vector3(1, 2, 3)));
			Assert.That(graph.Revision, Is.EqualTo(revision + 1));
			graph.RemoveNode("A");
			view.Synchronize(graph);

			// Removing a fact clears its view, selection, and folded state after synchronization.
			Assert.That(view.Nodes.ContainsKey("A"), Is.False);
			Assert.That(view.SelectedId, Is.Empty);
			Assert.That(view.FoldedNodeIds, Is.Empty);
			Assert.Throws<ArgumentException>(() => view.Synchronize(new GraphDocument("different")));
		}

		/// <summary>Checks that seeded Positions Are Stable And Shared By Independent Views.</summary>
		[Test]
		public void SeededPositionsAreStableAndSharedByIndependentViews()
		{
			var first = new GraphViewState("one");
			var second = new GraphViewState("two");
			Assert.That(first.GetOrCreate("A").Position, Is.EqualTo(second.GetOrCreate("A").Position));
			first.GetOrCreate("A").Pinned = true;
			Assert.That(second.GetOrCreate("A").Pinned, Is.False);
			Assert.That(GraphViewState.InitialPosition("A"), Is.Not.EqualTo(GraphViewState.InitialPosition("a")));
		}

		/// <summary>Checks that json Round Trip Preserves Scalar Types Evidence And Opaque Identity.</summary>
		[Test]
		public void JsonRoundTripPreservesScalarTypesEvidenceAndOpaqueIdentity()
		{
			var graph = Pair();
			graph.UpsertNode(graph.Nodes["A"].WithProperty("text", GraphValue.From("Ω \"quotes\""))
								  .WithProperty("number", GraphValue.From(12.75))
								  .WithProperty("bool", GraphValue.From(true)).WithProperty("null", GraphValue.Null));
			graph.UpsertEdge(new GraphEdgeRecord("CALL", "A", "a", "calls", "inferred", .7,
												 sources: new[] { "file.cs:8", "file.cs:2" }));
			var json = GraphJson.Serialize(graph);
			var parsed = GraphJson.Parse(json);

			// Require stable serialization and exclude transient position and favorite view state.
			Assert.That(GraphJson.Serialize(parsed), Is.EqualTo(json));
			Assert.That(parsed.Nodes["A"].Properties["null"].Type, Is.EqualTo(GraphValueType.Null));
			Assert.That(parsed.Nodes["A"].Properties["bool"].Boolean, Is.True);
			Assert.That(parsed.Edges["CALL"].Confidence, Is.EqualTo(.7));
			Assert.That(json.Contains("Position"), Is.False);
			Assert.That(json.Contains("Favorite"), Is.False);
		}

		/// <summary>Checks that position Frames Round Trip And Interpolate By Timestamp.</summary>
		[Test]
		public void PositionFramesRoundTripAndInterpolateByTimestamp()
		{
			var graph = Pair();
			var baseline = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
			graph.EstablishBaseline(baseline);
			graph.Timeline.PositionRecordingEnabled = true;
			graph.RecordPositionFrame(new Dictionary<string, Vector3> { ["A"] = Vector3.zero, ["a"] = new(2, 0, 0) },
									  baseline.AddSeconds(1));
			graph.RecordPositionFrame(new Dictionary<string, Vector3> { ["A"] = new(10, 0, 0), ["a"] = new(4, 0, 0) },
									  baseline.AddSeconds(3));
			var middle = graph.Timeline.PositionsAt(baseline.AddSeconds(2));

			// The midpoint is determined by timestamps rather than position frame indices.
			Assert.That(middle["A"], Is.EqualTo(new Vector3(5, 0, 0)));
			Assert.That(middle["a"], Is.EqualTo(new Vector3(3, 0, 0)));
			var parsed = GraphJson.Parse(GraphJson.Serialize(graph));
			Assert.That(parsed.Timeline.PositionFrames.Count, Is.EqualTo(2));
			Assert.That(parsed.Timeline.PositionRecordingEnabled, Is.True);
			Assert.That(parsed.Timeline.PositionSampleIntervalSeconds, Is.EqualTo(1));
			Assert.That(parsed.Timeline.PositionFrames[1].TimestampUtc, Is.EqualTo(baseline.AddSeconds(3)));
			Assert.That(GraphJson.Serialize(parsed), Is.EqualTo(GraphJson.Serialize(graph)));
		}

		/// <summary>Checks that disabling Position Recording Does Not Disable Event Recording.</summary>
		[Test]
		public void DisablingPositionRecordingDoesNotDisableEventRecording()
		{
			var graph = Pair();
			graph.EstablishBaseline();
			graph.Timeline.PositionRecordingEnabled = false;
			graph.UpsertNode(Node("new"));
			Assert.That(graph.Timeline.Entries.Count, Is.EqualTo(1));
			Assert.That(graph.Timeline.PositionFrames, Is.Empty);
		}

		/// <summary>Checks that position Recording Off Does Not Stop Live Force Layout.</summary>
		[Test]
		public void PositionRecordingOffDoesNotStopLiveForceLayout()
		{
			var graph = new GraphDocument("position:fixture");
			graph.UpsertNode(Node("only"));
			graph.EstablishBaseline();
			var scene = new GraphScene(graph, new GraphSchema());
			var layout = new ForceLayout();
			layout.Bind(scene);
			layout.Step(1f / 30);
			Assert.That(layout.Temperature, Is.LessThan(1));
			Assert.That(graph.Timeline.PositionFrames, Is.Empty);
		}

		/// <summary>Checks that clear History Apis Preserve The Other History And Current Graph.</summary>
		[Test]
		public void ClearHistoryApisPreserveTheOtherHistoryAndCurrentGraph()
		{
			var graph = Pair();
			graph.EstablishBaseline();
			graph.Timeline.PositionRecordingEnabled = true;
			graph.RecordPositionFrame(new Dictionary<string, Vector3> { ["A"] = Vector3.one, ["a"] = Vector3.zero });
			graph.UpsertNode(Node("new"));
			var eventCount = graph.Timeline.Entries.Count;
			graph.ClearPositionHistory();

			// Clearing motion preserves semantic edits; clearing semantic edits promotes the current graph to baseline.
			Assert.That(graph.Timeline.PositionFrames, Is.Empty);
			Assert.That(graph.Timeline.Entries.Count, Is.EqualTo(eventCount));

			graph.RecordPositionFrame(new Dictionary<string, Vector3>
										  { ["A"] = Vector3.one, ["a"] = Vector3.zero, ["new"] = Vector3.up });
			graph.ClearEventHistory();
			Assert.That(graph.Timeline.Entries, Is.Empty);
			Assert.That(graph.Timeline.PositionFrames.Count, Is.EqualTo(1));
			Assert.That(graph.Timeline.Baseline.Nodes.Any(node => node.Id == "new"), Is.True);
			Assert.That(graph.Nodes.ContainsKey("new"), Is.True);

			graph.UpsertNode(graph.Nodes["new"].WithIdentity("symbol", "Changed"));

			// Clearing both tracks retains the latest graph facts in the new baseline.
			graph.ClearHistory();
			Assert.That(graph.Timeline.Entries, Is.Empty);
			Assert.That(graph.Timeline.PositionFrames, Is.Empty);
			Assert.That(graph.Timeline.Baseline.Nodes.Single(node => node.Id == "new").Label, Is.EqualTo("Changed"));
			Assert.That(graph.Nodes["new"].Label, Is.EqualTo("Changed"));
		}

		/// <summary>Checks that force Layout Samples At Interval And Skips Sleeping Or Paused Physics.</summary>
		[Test]
		public void ForceLayoutSamplesAtIntervalAndSkipsSleepingOrPausedPhysics()
		{
			var graph = new GraphDocument("position:fixture");
			graph.UpsertNode(Node("only"));
			graph.EstablishBaseline();
			graph.Timeline.PositionRecordingEnabled = true;
			graph.Timeline.PositionSampleIntervalSeconds = .1f;
			var scene = new GraphScene(graph, new GraphSchema());
			var layout = new ForceLayout();
			layout.Bind(scene);
			layout.Step(.05f);
			Assert.That(graph.Timeline.PositionFrames.Count, Is.EqualTo(1));
			layout.Step(.05f);
			Assert.That(graph.Timeline.PositionFrames.Count, Is.EqualTo(1));
			layout.Step(.05f);
			Assert.That(graph.Timeline.PositionFrames.Count, Is.EqualTo(2));
			layout.Paused = true;

			// Paused and cooled layouts must not append redundant motion frames.
			for (var i = 0; i < 20; i++) layout.Step(1f / 30);
			Assert.That(graph.Timeline.PositionFrames.Count, Is.EqualTo(2));
			graph.Timeline.PositionSampleIntervalSeconds = 1f;
			layout.Paused = false;
			for (var i = 0; i < 1200 && layout.Temperature >= .025f; i++) layout.Step(1f / 30);
			Assert.That(layout.Temperature, Is.LessThan(.025f));
			var asleepCount = graph.Timeline.PositionFrames.Count;
			for (var i = 0; i < 20; i++) layout.Step(1f / 30);
			Assert.That(graph.Timeline.PositionFrames.Count, Is.EqualTo(asleepCount));
		}

		/// <summary>Checks that timestamp Seek Applies Latest Events And Position Interpolation.</summary>
		[Test]
		public void TimestampSeekAppliesLatestEventsAndPositionInterpolation()
		{
			var graph = Pair();
			var baseline = DateTimeOffset.UtcNow.AddSeconds(-10);
			graph.EstablishBaseline(baseline);
			graph.Timeline.PositionRecordingEnabled = true;
			graph.RecordPositionFrame(new Dictionary<string, Vector3> { ["A"] = Vector3.zero, ["a"] = Vector3.zero },
									  baseline.AddSeconds(1));
			graph.UpsertNode(Node("new"));
			var eventTime = graph.Timeline.Entries[0].TimestampUtc;
			graph.RecordPositionFrame(
				new Dictionary<string, Vector3> { ["A"] = new(10, 0, 0), ["a"] = Vector3.zero, ["new"] = new(4, 0, 0) },
				eventTime.AddSeconds(2));
			graph.RecordPositionFrame(
				new Dictionary<string, Vector3> { ["A"] = new(20, 0, 0), ["a"] = Vector3.zero, ["new"] = new(8, 0, 0) },
				eventTime.AddSeconds(4));
			graph.Seek(eventTime.AddSeconds(3));

			// Seek after the edit with interpolated motion, then cross back to just before node creation.
			Assert.That(graph.Nodes.ContainsKey("new"), Is.True);
			Assert.That(graph.Timeline.Cursor, Is.EqualTo(0));
			Assert.That(graph.Timeline.PositionsAt(eventTime.AddSeconds(3))["A"].x, Is.EqualTo(15).Within(.001));
			graph.Seek(eventTime.AddTicks(-1));
			Assert.That(graph.Nodes.ContainsKey("new"), Is.False);
		}

		/// <summary>Checks that json Rejects Future Versions Duplicates And Dangling References.</summary>
		[Test]
		public void JsonRejectsFutureVersionsDuplicatesAndDanglingReferences()
		{
			var json = GraphJson.Serialize(Pair());
			Assert.Throws<FormatException>(() => GraphJson.Parse(
											   json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2")));
			Assert.Throws<ArgumentException>(() => GraphJson.Parse(
												 "{\"schemaVersion\":1,\"datasetId\":\"x\",\"displayName\":\"\",\"nodes\":[{\"id\":\"A\",\"type\":\"file\",\"label\":\"A\"},{\"id\":\"A\",\"type\":\"file\",\"label\":\"A\"}],\"edges\":[]}"));
			Assert.Throws<ArgumentException>(() => GraphJson.Parse(
												 "{\"schemaVersion\":1,\"datasetId\":\"x\",\"displayName\":\"\",\"nodes\":[],\"edges\":[{\"id\":\"e\",\"from\":\"A\",\"to\":\"a\",\"type\":\"calls\",\"status\":\"resolved\"}]}"));
		}

		/// <summary>Checks that schema Describes Unrelated Datasets And Reports Field Type Mismatch.</summary>
		[Test]
		public void SchemaDescribesUnrelatedDatasetsAndReportsFieldTypeMismatch()
		{
			var schema = new GraphSchema();
			schema.Register(new GraphNodeTypeDefinition("employee", "Employee", "person", Color.blue));
			schema.Register(
				new GraphRelationDefinition("reports_to", "Reports to", isHierarchy: true, parentIsFrom: false));
			schema.Register(new GraphFieldDefinition("headcount", "Headcount", GraphValueType.Number));
			var graph = new GraphDocument("org");
			graph.UpsertNode(Node("ceo", "employee"));
			graph.UpsertNode(Node("staff", "employee").WithProperty("headcount", GraphValue.From(3d)));
			graph.UpsertEdge(Edge("report", "staff", "ceo", "reports_to"));
			Assert.That(schema.Validate(graph), Is.Empty);
			Assert.That(schema.Relations["reports_to"].ParentIsFrom, Is.False);
			graph.UpsertNode(graph.Nodes["staff"].WithProperty("headcount", GraphValue.From("three")));

			// A declared numeric field rejects text while the valid custom domain schema remains unchanged.
			Assert.That(schema.Validate(graph).Count, Is.EqualTo(1));
			Assert.Throws<ArgumentException>(() => schema.Register(
												 new GraphNodeTypeDefinition("employee", "Duplicate")));
		}

		/// <summary>Checks that missing Null And Non Finite Values Have Explicit Behavior.</summary>
		[Test]
		public void MissingNullAndNonFiniteValuesHaveExplicitBehavior()
		{
			var node = Node("A").WithProperty("known", GraphValue.Null);
			Assert.That(node.Properties.ContainsKey("known"), Is.True);
			Assert.That(node.Properties.ContainsKey("missing"), Is.False);
			Assert.That(node.WithProperty("known", null).Properties.ContainsKey("known"), Is.False);
			Assert.Throws<ArgumentOutOfRangeException>(() => GraphValue.From(double.NaN));
			Assert.Throws<ArgumentOutOfRangeException>(() => GraphValue.From(double.PositiveInfinity));
			Assert.Throws<ArgumentOutOfRangeException>(() => new GraphEdgeRecord(
														   "e", "A", "a", "calls", confidence: double.NaN));
		}

		/// <summary>Checks that default Budgets And Invalid Configuration Are Explicit.</summary>
		[Test]
		public void DefaultBudgetsAndInvalidConfigurationAreExplicit()
		{
			var options = new GraphExplorerOptions();
			Assert.That(options.NodeRenderBudget, Is.EqualTo(8000));
			Assert.That(options.NodeLabelBudget, Is.EqualTo(48));
			Assert.Throws<ArgumentOutOfRangeException>(() => new GraphExplorerOptions(nodeLabelBudget: 0));
			Assert.Throws<ArgumentException>(() => new GraphDocument(" "));
			Assert.Throws<ArgumentOutOfRangeException>(() => Pair().Neighborhood("A", -1));
		}

		/// <summary>Checks that large Sparse Document Can Update And Remove Without Dangling Edges.</summary>
		[Test]
		public void LargeSparseDocumentCanUpdateAndRemoveWithoutDanglingEdges()
		{
			var graph = new GraphDocument("stress");
			var batch = new GraphBatch();
			for (var i = 0; i < 1500; i++) batch.Nodes.Add(Node("n:" + i));
			for (var i = 0; i < 5000; i++) batch.Edges.Add(Edge("e:" + i, "n:" + i % 1500, "n:" + (i + 1) % 1500));
			graph.Apply(batch);
			Assert.That(graph.Nodes.Count, Is.EqualTo(1500));
			Assert.That(graph.Edges.Count, Is.EqualTo(5000));
			graph.RemoveNode("n:0");

			// Stress removal must repair every incident edge and surviving neighbor index.
			Assert.That(graph.Edges.Values.All(edge => edge.From != "n:0" && edge.To != "n:0"), Is.True);
			Assert.That(graph.Neighbors("n:1").Contains("n:0"), Is.False);
		}
	}
}