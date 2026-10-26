using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cashg.GraphExplorer.Tests
{
	/// <summary>Represents graph cluster tests.</summary>
	public sealed class GraphClusterTests
	{
		private static GraphScene MakeScene()
		{
			// Include internal, crossing, parallel, and self edges to exercise each projection rule.
			var facts = new GraphDocument("cluster:test");
			foreach (var id in new[] { "a", "b", "c", "d", "outside" })
				facts.UpsertNode(new GraphNodeRecord(id, "item", id));
			facts.UpsertEdge(new GraphEdgeRecord("ab", "a", "b", "link"));
			facts.UpsertEdge(new GraphEdgeRecord("bc", "b", "c", "link"));
			facts.UpsertEdge(new GraphEdgeRecord("cd", "c", "d", "link"));
			facts.UpsertEdge(new GraphEdgeRecord("in", "outside", "b", "link"));
			facts.UpsertEdge(new GraphEdgeRecord("out", "c", "outside", "link"));
			facts.UpsertEdge(new GraphEdgeRecord("parallel", "outside", "b", "link"));
			facts.UpsertEdge(new GraphEdgeRecord("self", "a", "a", "link"));
			return new GraphScene(facts, new GraphSchema());
		}

		/// <summary>Checks that crossing Edges Keep Identity Direction And Multiplicity.</summary>
		[Test]
		public void CrossingEdgesKeepIdentityDirectionAndMultiplicity()
		{
			var source = MakeScene();
			var before = GraphJson.Serialize(source.Document);
			var view = new GraphClusterScene(source);
			var id = view.Collapse(new[] { "a", "b", "c" }, "Neighborhood");

			// Internal edges disappear from projection while crossing IDs, directions, and parallel facts survive.
			Assert.That(view.Nodes.Keys, Is.EquivalentTo(new[] { id, "d", "outside" }));
			Assert.That(view.Edges.Keys, Is.EquivalentTo(new[] { "cd", "in", "out", "parallel" }));
			Assert.That(view.Edges["in"].From, Is.EqualTo("outside"));
			Assert.That(view.Edges["in"].To, Is.EqualTo(id));
			Assert.That(view.Edges["out"].From, Is.EqualTo(id));
			Assert.That(view.Edges["out"].To, Is.EqualTo("outside"));
			Assert.That(view.Nodes[id].Record.Properties["memberCount"].Number, Is.EqualTo(3));
			Assert.That(view.LeafIds(id), Is.EquivalentTo(new[] { "a", "b", "c" }));
			Assert.That(view.MemberRecords(id).Select(record => record.Id), Is.EquivalentTo(new[] { "a", "b", "c" }));
			Assert.That(view.InternalEdges(id).Select(edge => edge.Id), Is.EquivalentTo(new[] { "ab", "bc", "self" }));
			Assert.That(GraphJson.Serialize(source.Document), Is.EqualTo(before));
			Assert.That(view.Expand(id), Is.True);
			Assert.That(view.Edges.Keys, Is.EquivalentTo(source.Edges.Keys));
			Assert.That(view.Nodes.Keys, Is.EquivalentTo(source.Nodes.Keys));
		}

		/// <summary>Checks that nested Clusters Restore One Level At A Time And Carry Positions.</summary>
		[Test]
		public void NestedClustersRestoreOneLevelAtATimeAndCarryPositions()
		{
			// Verify both hierarchy depth and source-node positions across collapse and expand.
			var source = MakeScene();
			var view = new GraphClusterScene(source);
			var inner = view.Collapse(new[] { "a", "b" }, "Inner");
			var outer = view.Collapse(new[] { inner, "c" }, "Outer");
			Assert.That(view.LeafIds(outer), Is.EquivalentTo(new[] { "a", "b", "c" }));
			var original = source.Nodes["a"].Position;
			view.Nodes[outer].Position += Vector3.right * 10;

			// Expand one level at a time so the outer drag propagates through the still-collapsed inner group.
			view.Expand(outer);
			Assert.That(view.Nodes.ContainsKey(inner), Is.True);
			Assert.That(view.Nodes.ContainsKey("a"), Is.False);
			view.Expand(inner);
			Assert.That(view.Nodes.ContainsKey("a"), Is.True);
			Assert.That(source.Nodes["a"].Position.x, Is.EqualTo(original.x + 10).Within(.001f));
			Assert.That(view.Edges.Keys, Is.EquivalentTo(source.Edges.Keys));
			Assert.That(view.Recollapse(inner), Is.True);
			Assert.That(view.Recollapse(outer), Is.True);
		}

		/// <summary>Checks that position History Uses Canonical Nodes And Tracks Collapsed Cluster Motion.</summary>
		[Test]
		public void PositionHistoryUsesCanonicalNodesAndTracksCollapsedClusterMotion()
		{
			var source = MakeScene();
			var view = new GraphClusterScene(source);
			var initial = source.Nodes["a"].Position;
			var cluster = view.Collapse(new[] { "a", "b", "c" }, "Group");
			view.Nodes[cluster].Position += Vector3.right * 7;

			// Motion history must contain source identities with group displacement, never temporary cluster IDs.
			var positions = view.CanonicalPositions();
			Assert.That(positions.Keys, Is.EquivalentTo(source.Document.Nodes.Keys));
			Assert.That(positions.ContainsKey(cluster), Is.False);
			Assert.That(positions["a"].x, Is.EqualTo(initial.x + 7).Within(.001f));
			view.ApplyCanonicalPositions(positions);
			var restored = view.CanonicalPositions();
			Assert.That(restored["a"], Is.EqualTo(positions["a"]));
			Assert.That(restored["b"], Is.EqualTo(positions["b"]));
		}

		/// <summary>Checks that distance Lod Only Groups Far Connected Neighbors And Releases Near Camera.</summary>
		[Test]
		public void DistanceLodOnlyGroupsFarConnectedNeighborsAndReleasesNearCamera()
		{
			var source = MakeScene();
			source.Nodes["a"].Position = new Vector3(100, 0, 0);
			source.Nodes["b"].Position = new Vector3(101, 0, 0);
			source.Nodes["c"].Position = new Vector3(102, 0, 0);
			source.Nodes["d"].Position = new Vector3(200, 0, 0);
			source.Nodes["outside"].Position = Vector3.zero;
			var view = new GraphClusterScene(source)
				{ LodEnabled = true, LodEnterDistance = 80, LodExitDistance = 65, LodNeighborSeparation = 4 };
			Assert.That(view.UpdateLod(Vector3.zero), Is.True);
			Assert.That(view.Nodes.Count, Is.EqualTo(3));
			var cluster = view.Nodes.Keys.Single(view.IsCluster);
			Assert.That(view.LeafIds(cluster), Is.EquivalentTo(new[] { "a", "b", "c" }));
			Assert.That(view.Edges["in"].To, Is.EqualTo(cluster));
			Assert.That(view.Expand(cluster), Is.True);
			Assert.That(view.UpdateLod(Vector3.zero), Is.False, "User expansion overrides automatic regrouping.");
			view.RemoveCluster(cluster);

			// Clear the manual override to allow regrouping, then approach the group to trigger automatic expansion.
			view.ClearLodOverrides();
			Assert.That(view.UpdateLod(Vector3.zero), Is.True);
			Assert.That(view.UpdateLod(new Vector3(50, 0, 0)), Is.True);
			Assert.That(view.Nodes.Keys, Is.EquivalentTo(source.Nodes.Keys));
		}

		/// <summary>Checks that inspector Relationships Follow Collapsed Clusters On The Projection.</summary>
		[Test]
		public void InspectorRelationshipsFollowCollapsedClustersOnTheProjection()
		{
			var source = MakeScene();
			var view = new GraphClusterScene(source);
			var id = view.Collapse(new[] { "a", "b", "c" }, "Neighborhood");
			var outside = view.Nodes["outside"];
			var missing =
				Assert.Throws<KeyNotFoundException>(() => GraphInspectorPresenter.Render(
														new VisualElement(), source, outside));
			Assert.That(missing.Message, Does.Contain(id));
			var parent = new VisualElement();

			// Render against the projection so retargeted relationships can resolve the generated cluster endpoint.
			GraphInspectorPresenter.Render(parent, view, outside);
			Assert.That(parent.Query<Button>().ToList().Any(button => button.text == "Neighborhood"), Is.True);
		}
	}
}
