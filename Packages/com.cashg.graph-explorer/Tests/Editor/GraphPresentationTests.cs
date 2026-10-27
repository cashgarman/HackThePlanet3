using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Cashg.GraphExplorer.Tests
{
	/// <summary>Represents graph presentation tests.</summary>
	public sealed class GraphPresentationTests
	{
		/// <summary>Checks that label Reveal Uses Screen Distance With Smooth Falloff.</summary>
		[Test]
		public void LabelRevealUsesScreenDistanceWithSmoothFalloff()
		{
			var settings = ScriptableObject.CreateInstance<GraphNodeLabelSettings>();
			try
			{
				settings.RevealInnerRadius = 40f;
				settings.RevealOuterRadius = 180f;

				// Check the fully visible radius, smooth midpoint, and fully hidden outer boundary.
				Assert.That(settings.ProximityOpacity(0f, true, false), Is.EqualTo(1f));
				Assert.That(settings.ProximityOpacity(40f, true, false), Is.EqualTo(1f));
				Assert.That(settings.ProximityOpacity(110f, true, false), Is.EqualTo(.5f).Within(.0001f));
				Assert.That(settings.ProximityOpacity(180f, true, false), Is.Zero);
				Assert.That(settings.ProximityOpacity(1000f, true, false), Is.Zero);
			}
			finally
			{
				Object.DestroyImmediate(settings);
			}
		}

		/// <summary>Checks that label Reveal Handles Pointer Exit Selection And Disabled Mode.</summary>
		[Test]
		public void LabelRevealHandlesPointerExitSelectionAndDisabledMode()
		{
			var settings = ScriptableObject.CreateInstance<GraphNodeLabelSettings>();
			try
			{
				Assert.That(settings.ProximityOpacity(0f, false, false), Is.Zero);
				Assert.That(settings.ProximityOpacity(1000f, false, true), Is.EqualTo(1f));
				settings.KeepSelectedLabelVisible = false;

				// Disabling the selection exemption restores normal pointer and distance gating.
				Assert.That(settings.ProximityOpacity(1000f, true, true), Is.Zero);
				Assert.That(settings.ProximityOpacity(0f, false, true), Is.Zero);
				settings.MouseProximityReveal = false;
				Assert.That(settings.ProximityOpacity(1000f, false, false), Is.EqualTo(1f));
			}
			finally
			{
				Object.DestroyImmediate(settings);
			}
		}

		/// <summary>Checks that label Reveal Handles Collapsed And Invalid Radius Ranges.</summary>
		[Test]
		public void LabelRevealHandlesCollapsedAndInvalidRadiusRanges()
		{
			var settings = ScriptableObject.CreateInstance<GraphNodeLabelSettings>();
			try
			{
				settings.RevealInnerRadius = 40f;
				settings.RevealOuterRadius = 10f;
				Assert.That(settings.ProximityOpacity(40f, true, false), Is.EqualTo(1f));
				Assert.That(settings.ProximityOpacity(41f, true, false), Is.Zero);
				settings.RevealInnerRadius = -40f;

				// Invalid negative radii collapse to a zero-distance step rather than producing an inverted fade.
				settings.RevealOuterRadius = -10f;
				Assert.That(settings.ProximityOpacity(0f, true, false), Is.EqualTo(1f));
				Assert.That(settings.ProximityOpacity(1f, true, false), Is.Zero);
			}
			finally
			{
				Object.DestroyImmediate(settings);
			}
		}

		private static GraphScene Scene()
		{
			// Use domain-specific type names and metric partitions to expose accidental built-in assumptions.
			var document = new GraphDocument("fictional:arbitrary", "Arbitrary types");
			document.UpsertNode(new GraphNodeRecord("opaque:a", "division", "A", new Dictionary<string, GraphValue>
			{
				["headcount"] = GraphValue.From(10d), ["unit"] = GraphValue.From("people"),
				["state"] = GraphValue.From("open")
			}));
			document.UpsertNode(new GraphNodeRecord("opaque:b", "employee", "B", new Dictionary<string, GraphValue>
			{
				["headcount"] = GraphValue.From(50d), ["unit"] = GraphValue.From("people")
			}));
			document.UpsertEdge(new GraphEdgeRecord("opaque:edge", "opaque:a", "opaque:b", "reports-to"));
			var schema = new GraphSchema();
			schema.Register(new GraphNodeTypeDefinition("division", "Division", "Building", Color.cyan, 1));
			schema.Register(new GraphNodeTypeDefinition("employee", "Employee", "Person", Color.magenta));
			schema.Register(new GraphRelationDefinition("reports-to", "Reports to", Color.yellow, springStrength: .9f));
			schema.Register(new GraphFieldDefinition("headcount", "Headcount", GraphValueType.Number,
													 partitionBy: "unit"));
			schema.Register(new GraphFieldDefinition("state", "State",
													 categories: new Dictionary<string, GraphCategoryDefinition>
													 {
														 ["open"] = new("Open", Color.green)
													 }));
			return new GraphScene(document, schema);
		}

		/// <summary>Checks that projection Keeps Facts And View Separate.</summary>
		[Test]
		public void ProjectionKeepsFactsAndViewSeparate()
		{
			var scene = Scene();
			var node = scene.Nodes["opaque:a"];
			var before = GraphJson.Serialize(scene.Document);
			node.Position = Vector3.one;
			node.Pinned = true;
			node.Favorite = true;
			Assert.That(GraphJson.Serialize(scene.Document), Is.EqualTo(before));
			Assert.That(scene.View.Nodes[node.Id].Position, Is.EqualTo(Vector3.one));
			Assert.That(node.Edges[0], Is.SameAs(scene.Edges["opaque:edge"]));
		}

		/// <summary>Checks that projection Synchronizes Additions Updates And Removals Without Replacing Surviving Views.</summary>
		[Test]
		public void ProjectionSynchronizesAdditionsUpdatesAndRemovalsWithoutReplacingSurvivingViews()
		{
			var scene = Scene();
			var survivor = scene.Nodes["opaque:a"];
			scene.Document.UpsertNode(survivor.Record.WithIdentity("division", "Renamed"));
			scene.Document.RemoveNode("opaque:b");
			scene.Synchronize();
			Assert.That(scene.Nodes["opaque:a"], Is.SameAs(survivor));
			Assert.That(survivor.Label, Is.EqualTo("Renamed"));
			Assert.That(scene.Edges, Is.Empty);
			Assert.That(survivor.Neighbors, Is.Empty);
		}

		/// <summary>Checks that projection Rejects View From Another Dataset.</summary>
		[Test]
		public void ProjectionRejectsViewFromAnotherDataset()
		{
			var scene = Scene();
			Assert.Throws<ArgumentException>(() => new GraphScene(scene.Document, scene.Schema,
																  new GraphViewState("other")));
		}

		/// <summary>Checks that presentation Uses Schema Types Categories And Numeric Partitions.</summary>
		[Test]
		public void PresentationUsesSchemaTypesCategoriesAndNumericPartitions()
		{
			var scene = Scene();
			var profile = new GraphPresentationProfile { MetricField = "headcount", CategoryField = "state" };
			Assert.That(profile.Type(scene, scene.Nodes["opaque:a"]).ShapeId, Is.EqualTo("Building"));
			Assert.That(profile.CategoryColor(scene, scene.Nodes["opaque:a"], Color.gray), Is.EqualTo(Color.green));
			Assert.That(profile.TryMetric(scene, scene.Nodes["opaque:a"], out var value, out var partition), Is.True);
			Assert.That(value, Is.EqualTo(10));
			Assert.That(partition, Is.EqualTo("people"));
		}

		/// <summary>Checks that unknown Type Fallback Is Stable And Does Not Invent Domain Meaning.</summary>
		[Test]
		public void UnknownTypeFallbackIsStableAndDoesNotInventDomainMeaning()
		{
			var scene = Scene();
			scene.Document.UpsertNode(new GraphNodeRecord("new", "custom:type", "New"));
			scene.Synchronize();
			var profile = new GraphPresentationProfile();
			Assert.That(profile.Type(scene, scene.Nodes["new"]), Is.SameAs(profile.Type(scene, scene.Nodes["new"])));
			Assert.That(profile.Type(scene, scene.Nodes["new"]).ShapeId, Is.EqualTo("default"));
			Assert.That(profile.TryMetric(scene, scene.Nodes["new"], out _, out _), Is.False);
		}

		/// <summary>Checks that layout Supports Arbitrary Types And Pinned Nodes.</summary>
		[Test]
		public void LayoutSupportsArbitraryTypesAndPinnedNodes()
		{
			var scene = Scene();
			var pinned = scene.Nodes["opaque:a"];
			pinned.Pinned = true;
			var position = pinned.Position;
			var layout = new ForceLayout();
			layout.Bind(scene);
			for (var i = 0; i < 120; i++) layout.Step(1f / 60);
			Assert.That(pinned.Position, Is.EqualTo(position));
			Assert.That(scene.Nodes.Values.All(node => float.IsFinite(node.Position.sqrMagnitude)), Is.True);
		}

		/// <summary>Checks that flat Layout Converges To Plane.</summary>
		[Test]
		public void FlatLayoutConvergesToPlane()
		{
			var scene = Scene();
			var layout = new ForceLayout { Mode = LayoutMode.Flat };
			layout.Bind(scene);
			for (var i = 0; i < 600; i++) layout.Step(1f / 60);
			Assert.That(scene.Nodes.Values.All(node => Mathf.Abs(node.Position.z) < .03f), Is.True);
		}

		/// <summary>Checks that layout Reads External Position Edits On Every Step.</summary>
		[Test]
		public void LayoutReadsExternalPositionEditsOnEveryStep()
		{
			var scene = Scene();
			var layout = new ForceLayout();
			layout.Bind(scene);
			layout.Step(1f / 60);
			var node = scene.Nodes["opaque:a"];
			var draggedTo = new Vector3(500, 500, 500);
			node.Position = draggedTo;
			node.Velocity = Vector3.zero;
			var topology = scene.Revision;

			// Rename without a topology revision to prove the next step reads the externally dragged view position.
			scene.Document.UpsertNode(node.Record.WithIdentity(node.Record.TypeId, "Renamed during drag"));
			Assert.That(scene.Revision, Is.EqualTo(topology));
			layout.Step(1f / 60);
			Assert.That(Vector3.Distance(node.Position, draggedTo), Is.LessThan(1),
						"A cached solver must use the latest view position, including without a topology change.");
		}

		/// <summary>Checks that renderer Uses Configured Metric And Default Budgets.</summary>
		[Test]
		public void RendererUsesConfiguredMetricAndDefaultBudgets()
		{
			var scene = Scene();
			var profile = new GraphPresentationProfile { MetricField = "headcount" };
			using var renderer = new GraphRenderer(profile);
			renderer.UpdateMetrics(scene, new AllEdges());
			renderer.Size = SizeMapping.Metric;
			Assert.That(renderer.MaximumVisible, Is.EqualTo(8000));
			Assert.That(renderer.Radius(scene.Nodes["opaque:a"]), Is.EqualTo(.65f).Within(.0001f));
			Assert.That(renderer.Radius(scene.Nodes["opaque:b"]), Is.EqualTo(1.3f).Within(.0001f));
			renderer.Dispose(); // Repeated disposal is supported.
		}

		/// <summary>Checks that register Primary Click Requires Same Node Within Window.</summary>
		[Test]
		public void RegisterPrimaryClickRequiresSameNodeWithinWindow()
		{
			var state = new GraphSceneInput.PrimaryClickState();
			var nodeA = Scene().Nodes["opaque:a"];
			var nodeB = Scene().Nodes["opaque:b"];
			Assert.That(GraphSceneInput.RegisterPrimaryClick(ref state, nodeA, 1f), Is.False);
			Assert.That(GraphSceneInput.RegisterPrimaryClick(ref state, nodeB, 1.1f), Is.False);
			Assert.That(GraphSceneInput.RegisterPrimaryClick(ref state, nodeA, 1.2f), Is.False);
			Assert.That(GraphSceneInput.RegisterPrimaryClick(ref state, nodeA, 1.25f), Is.True);

			// Consuming a double click resets the sequence so the third click is not another double click.
			Assert.That(GraphSceneInput.RegisterPrimaryClick(ref state, nodeA, 1.3f), Is.False);
		}

		/// <summary>Checks that release Camera Focus Stops Tracking And Clears Selected Id.</summary>
		[Test]
		public void ReleaseCameraFocusStopsTrackingAndClearsSelectedId()
		{
			var go = new GameObject("Graph scene input test");
			try
			{
				var camera = go.AddComponent<Camera>();
				using var navigation = new GraphNavigation(camera);
				var scene = Scene();
				var node = scene.Nodes["opaque:a"];
				navigation.Focus(node);
				scene.View.SelectedId = node.Id;

				// Releasing camera focus must clear both the navigation subject and persisted selected ID.
				GraphSceneInput.ReleaseCameraFocus(scene.View, navigation);
				Assert.That(navigation.Tracking, Is.False);
				Assert.That(scene.View.SelectedId, Is.Empty);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		/// <summary>Checks that focus Follow Keeps The Camera Locked To Node Motion.</summary>
		[Test]
		public void FocusFollowKeepsTheCameraLockedToNodeMotion()
		{
			var go = new GameObject("Graph focus follow test");
			try
			{
				var camera = go.AddComponent<Camera>();
				camera.transform.SetPositionAndRotation(new Vector3(0, 0, -30), Quaternion.identity);
				using var navigation = new GraphNavigation(camera);
				var node = Scene().Nodes["opaque:a"];
				node.Position = Vector3.zero;
				navigation.Focus(node);
				Assert.That(navigation.Tracking, Is.True);
				Assert.That(navigation.Tracked, Is.SameAs(node));
				var origin = camera.transform.position;
				node.Position = new Vector3(4, -2, 1);

				// Following carries the camera with the subject; holding updates the target without a catch-up jump.
				navigation.Follow(false);
				Assert.That(camera.transform.position, Is.EqualTo(origin + new Vector3(4, -2, 1)));
				node.Position = new Vector3(9, -2, 1);
				navigation.Follow(true);
				Assert.That(camera.transform.position, Is.EqualTo(origin + new Vector3(4, -2, 1)));
				navigation.Follow(false);
				Assert.That(camera.transform.position, Is.EqualTo(origin + new Vector3(4, -2, 1)));
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		/// <summary>Checks that navigation Does Not Change Supplied Camera On Construction.</summary>
		[Test]
		public void NavigationDoesNotChangeSuppliedCameraOnConstruction()
		{
			var go = new GameObject("Graph navigation test");
			try
			{
				var camera = go.AddComponent<Camera>();
				camera.transform.SetPositionAndRotation(new Vector3(8, 9, 10), Quaternion.Euler(12, 34, 56));
				var position = camera.transform.position;
				var rotation = camera.transform.rotation;

				// Constructing navigation must observe the borrowed camera pose without reframing it.
				using var navigation = new GraphNavigation(camera);
				Assert.That(camera.transform.position, Is.EqualTo(position));
				Assert.That(camera.transform.rotation, Is.EqualTo(rotation));
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		/// <summary>Checks that rendering Session Releases Owned Resources And Can Initialize Again.</summary>
		[Test]
		public void RenderingSessionReleasesOwnedResourcesAndCanInitializeAgain()
		{
			var go = new GameObject("Graph rendering lifecycle test");
			try
			{
				var camera = go.AddComponent<Camera>();
				camera.enabled = false;
				var position = camera.transform.position;
				var clear = camera.clearFlags;
				var rate = Application.targetFrameRate;
				for (var i = 0; i < 3; i++)
				{
					// Compare native allocations across repeated sessions and reject simultaneous ownership of one camera.
					var ownedBefore = NativeResources();
					var session = GraphRenderingSession.Initialize(camera, new GraphResourceConfiguration());
					Assert.That(camera.transform.position, Is.EqualTo(position));
					Assert.That(camera.clearFlags, Is.EqualTo(clear));
					Assert.That(Application.targetFrameRate, Is.EqualTo(rate));
					var effects = session.Effects;
					Assert.Throws<InvalidOperationException>(() => GraphRenderingSession.Initialize(
																 camera, new GraphResourceConfiguration()));
					session.Dispose();
					session.Dispose();
					Assert.That(effects == null, Is.True);
					Assert.That(
						Object.FindObjectsByType<GraphNodeEffects>(FindObjectsSortMode.None)
							  .Any(item => item.gameObject == go), Is.False);
					Assert.That(NativeResources().SetEquals(ownedBefore), Is.True,
								"Initialization/disposal must release its transient materials and meshes.");
				}
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		/// <summary>Checks that rendering Session Restores Borrowed Effects.</summary>
		[Test]
		public void RenderingSessionRestoresBorrowedEffects()
		{
			var go = new GameObject("Graph borrowed effects test");
			try
			{
				var camera = go.AddComponent<Camera>();
				camera.enabled = false;
				var effects = go.AddComponent<GraphNodeEffects>();
				effects.enabled = false;
				var resources = effects.Resources;
				using (var session = GraphRenderingSession.Initialize(camera, new GraphResourceConfiguration()))
				{
					// Borrow the existing disabled component instead of creating a replacement.
					Assert.That(session.Effects, Is.SameAs(effects));
				}

				Assert.That(effects != null && !effects.enabled && effects.Renderer == null, Is.True);
				Assert.That(effects.Resources, Is.SameAs(resources));
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		private static HashSet<Object> NativeResources()
		{
			// Exclude imported assets so the lifecycle check compares only transient meshes and materials.
			return new HashSet<Object>(
				Resources.FindObjectsOfTypeAll<Material>().Cast<Object>()
						 .Concat(Resources.FindObjectsOfTypeAll<Mesh>())
						 .Where(item => !EditorUtility.IsPersistent(item)));
		}

		private sealed class AllEdges : IGraphEdgeFilter
		{
			public bool EdgeMatches(IGraphEdge edge)
			{
				return true;
			}
		}
	}
}