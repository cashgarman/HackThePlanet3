using System.Linq;
using NUnit.Framework;

namespace Cashg.GraphExplorer.Tests
{
	/// <summary>Represents graph path tests.</summary>
	public sealed class GraphPathTests
	{
		private static GraphScene Scene()
		{
			// Supply equal-length routes and parallel edges so stable tie ordering can be observed.
			var doc = new GraphDocument("paths", "Paths");
			foreach (var id in new[] { "a", "b", "c", "d", "z" }) doc.UpsertNode(new GraphNodeRecord(id, "node", id));
			doc.UpsertEdge(new GraphEdgeRecord("ab-z", "a", "b", "link"));
			doc.UpsertEdge(new GraphEdgeRecord("ab-a", "a", "b", "link"));
			doc.UpsertEdge(new GraphEdgeRecord("ac", "a", "c", "link"));
			doc.UpsertEdge(new GraphEdgeRecord("bd", "b", "d", "link"));
			doc.UpsertEdge(new GraphEdgeRecord("cd", "c", "d", "link"));
			var schema = new GraphSchema();
			schema.Register(new GraphNodeTypeDefinition("node", "Node"));
			return new GraphScene(doc, schema);
		}

		/// <summary>Checks that finds Shortest Directed Path And Uses Stable Tie And Parallel Edge Ordering.</summary>
		[Test]
		public void FindsShortestDirectedPathAndUsesStableTieAndParallelEdgeOrdering()
		{
			var scene = Scene();
			var path = GraphPathfinder.Find(scene, "a", "d");
			Assert.That(path.IsReachable, Is.True);
			Assert.That(path.Jumps, Is.EqualTo(2));
			Assert.That(path.NodeIds, Is.EqualTo(new[] { "a", "b", "d" }));
			Assert.That(path.EdgeIds, Is.EqualTo(new[] { "ab-a", "bd" }));
			Assert.That(GraphPathfinder.Find(scene, "d", "a").IsReachable, Is.False);
		}

		/// <summary>Checks that same Node Is Zero Hop And Disconnected Nodes Have No Path.</summary>
		[Test]
		public void SameNodeIsZeroHopAndDisconnectedNodesHaveNoPath()
		{
			var scene = Scene();
			var same = GraphPathfinder.Find(scene, "a", "a");
			Assert.That(same.IsReachable, Is.True);
			Assert.That(same.Jumps, Is.Zero);
			Assert.That(same.NodeIds, Is.EqualTo(new[] { "a" }));
			Assert.That(GraphPathfinder.Find(scene, "a", "z").IsReachable, Is.False);
		}

		/// <summary>Checks that state Recomputes After Topology Changes And Keeps Panel Active Without Route.</summary>
		[Test]
		public void StateRecomputesAfterTopologyChangesAndKeepsPanelActiveWithoutRoute()
		{
			var scene = Scene();
			var state = new GraphPathState(scene);
			state.Show("a", "d");
			Assert.That(state.Result.Jumps, Is.EqualTo(2));

			// Remove both final hops while keeping endpoint selections active for a now-unreachable route.
			scene.Document.RemoveEdge("bd");
			scene.Document.RemoveEdge("cd");
			scene.Synchronize();
			Assert.That(state.Synchronize(), Is.True);
			Assert.That(state.IsActive, Is.True);
			Assert.That(state.Result.IsReachable, Is.False);
		}

		/// <summary>Checks that graph Pick Sets Chosen Endpoint And Completes Route.</summary>
		[Test]
		public void GraphPickSetsChosenEndpointAndCompletesRoute()
		{
			var scene = Scene();
			var state = new GraphPathState(scene);
			state.SetEndpoint(GraphPathEndpoint.End, "d");
			state.BeginPick(GraphPathEndpoint.Start);
			Assert.That(state.AcceptPickedNode("a"), Is.True);
			Assert.That(state.Result.NodeIds.First(), Is.EqualTo("a"));
			Assert.That(state.Result.NodeIds.Last(), Is.EqualTo("d"));
		}

		/// <summary>Checks that renderer Binding Keeps Active Result And Clears On Dismissal.</summary>
		[Test]
		public void RendererBindingKeepsActiveResultAndClearsOnDismissal()
		{
			var scene = Scene();
			var state = new GraphPathState(scene);
			using var renderer = new GraphRenderer();
			using var binding = GraphPathRendering.Bind(state, renderer);
			state.Show("a", "d");
			Assert.That(renderer.ActivePath, Is.SameAs(state.Result));
			state.Clear();
			Assert.That(renderer.ActivePath, Is.Null);
		}

		/// <summary>Checks that relation Predicate Excludes Structural Shortcuts And Survives Rebinding.</summary>
		[Test]
		public void RelationPredicateExcludesStructuralShortcutsAndSurvivesRebinding()
		{
			var scene = Scene();
			scene.Document.UpsertEdge(new GraphEdgeRecord("shortcut", "a", "d", "contains"));
			scene.Synchronize();
			Assert.That(GraphPathfinder.Find(scene, "a", "d").Jumps, Is.EqualTo(1));
			var state = new GraphPathState(scene, edge => edge.Relation == "link");
			state.Show("a", "d");
			Assert.That(state.Result.Jumps, Is.EqualTo(2));
			foreach (var node in scene.Nodes.Values) node.Visible = false;

			// Display filters do not affect semantic traversal, and rebinding retains the relation predicate.
			state.Synchronize(true);
			Assert.That(state.Result.Jumps, Is.EqualTo(2), "UI visibility must not change semantic traversal.");
			var replacement = Scene();
			replacement.Document.RemoveNode("d");
			replacement.Synchronize();
			state.Bind(replacement);
			Assert.That(state.IsActive, Is.True);
			Assert.That(state.Result.IsReachable, Is.False);
			Assert.That(state.StartId, Is.EqualTo("a"));
			Assert.That(state.EndId, Is.EqualTo("d"));
		}
	}
}