using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cashg.GraphExplorer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace HackThePlanet.Tests
{
	public sealed class RuntimeHelpersTests
	{
		private GameFixture fixture;

		[SetUp]
		public void SetUp() => fixture = new GameFixture();

		[TearDown]
		public void TearDown() => fixture?.Dispose();

		[Test]
		public void LoreLookupIsScopedToContractAndReturnsNullForUnknownIds()
		{
			var contract = fixture.Catalog.Contracts(fixture.Catalog.CreateCareer()).First(c => c.lore?.Length > 0);
			var first = LoreContent.For(contract, 0);

			Assert.That(first, Is.Not.Null);
			Assert.That(LoreContent.Find(contract, first.id), Is.EqualTo(first));
			Assert.That(LoreContent.For(contract, -1), Is.Null);
			Assert.That(LoreContent.Find(contract, "missing-lore"), Is.Null);
		}

		[Test]
		public void GraphIntelDescriptionsRedactPreviewAndRequireEveryResearchParent()
		{
			var node = new NetNode { id = "vault", kind = NodeKind.Database, label = "Vault", security = 7 };
			Assert.That(GraphViewport.DescribeNode(node, ContractIntel.Topology, true, fixture.Catalog),
				Does.Contain("NO INTEL").And.Not.Contain("Vault").And.Not.Contain("7"));
			Assert.That(GraphViewport.DescribeNode(node, ContractIntel.Topology, false, fixture.Catalog),
				Does.Contain("LIMITED INTEL").And.Not.Contain("SECURITY"));
			Assert.That(GraphViewport.DescribeNode(node, ContractIntel.Full, false, fixture.Catalog),
				Does.Contain("FULL INTEL").And.Contain("SECURITY 7"));

			var techs = fixture.Catalog.Technologies.ToDictionary(t => t.id);
			var crossBranch = techs.Values.First(t => t.parents.Length > 1);
			var career = fixture.Catalog.CreateCareer();
			career.xp = crossBranch.cost;
			var locked = GraphViewport.DescribeTechnology(crossBranch, career, techs);
			Assert.That(locked, Does.Contain("LOCKED / REQUIRES").And.Contain("BOTH parent technologies required."));

			foreach (var parent in crossBranch.parents)
				career.unlocked.Add(parent);
			Assert.That(GraphViewport.DescribeTechnology(crossBranch, career, techs), Does.Contain("AVAILABLE"));
			career.unlocked.Add(crossBranch.id);
			Assert.That(GraphViewport.DescribeTechnology(crossBranch, career, techs), Does.Contain("UNLOCKED"));
		}

		[Test]
		public void NetworkCanvasFiltersUnknownNodesAndClampsZoom()
		{
			var known = new NetNode { id = "known", label = "Known node", kind = NodeKind.Relay, known = true };
			var hidden = new NetNode { id = "hidden", label = "Hidden node", kind = NodeKind.Database, known = false };
			var camera = new CanvasCamera();
			var canvas = new NetworkCanvas(camera, new List<NetNode> { known, hidden },
				new List<NetLink>(), null, false, _ => { });
			try
			{
				Assert.That(canvas.Q<Button>("node-known"), Is.Not.Null);
				Assert.That(canvas.Q<Button>("node-hidden"), Is.Null);
				canvas.Zoom(100);
				Assert.That(camera.zoom, Is.EqualTo(2.3f).Within(.001f));
				canvas.Zoom(0);
				Assert.That(camera.zoom, Is.EqualTo(.65f).Within(.001f));
				canvas.ResetView();
				Assert.That(camera.zoom, Is.EqualTo(1));
				Assert.That(camera.pan, Is.EqualTo(Vector2.zero));
			}
			finally
			{
				canvas.RemoveFromHierarchy();
			}
		}

		[Test]
		public void RuntimeGlyphHelpersChooseStableAccentsAndModuleSymbols()
		{
			Assert.That(ActionGlyph.ColorFor("hack"), Is.EqualTo(Palette.Lime));
			Assert.That(ActionGlyph.ColorFor("unknown-action"), Is.EqualTo(Palette.Cyan));
			Assert.That(GameplayTextPalette.DefaultTerms().Any(term => term.regex && term.phrase == "SECURITY \\d+"),
				Is.True);
			Assert.That(Palette.Kind(NodeKind.Database), Is.EqualTo(Palette.Hex("D9D0B1")));
			Assert.That(Palette.Branch("CROSSOVER"), Is.EqualTo(Palette.Hex("F59BD0")));

			var module = fixture.Catalog.Data.modules.First();
			var glyph = RigGlyph.ForModule(module, Color.magenta, 31);
			Assert.That(glyph, Is.Not.Null);
			Assert.That(glyph.style.width.value.value, Is.EqualTo(31));
			Assert.That(glyph.style.height.value.value, Is.EqualTo(31));
			Assert.That(glyph.pickingMode, Is.EqualTo(PickingMode.Ignore));

			var device = new DeviceGlyph(NodeKind.Firewall, Color.red, 24);
			Assert.That(device.style.width.value.value, Is.EqualTo(24));
			Assert.That(device.style.height.value.value, Is.EqualTo(24));
			Assert.That(device.pickingMode, Is.EqualTo(PickingMode.Ignore));
		}

		[Test]
		public void WaypointOverlayOnlyAddsRouteAnchorsAtUnlockedTierFour()
		{
			var cameraObject = new GameObject("waypoint overlay fixture camera");
			var camera = cameraObject.AddComponent<Camera>();
			var nodes = new List<NetNode>
			{
				new() { id = "entry", position = new Vector3(0, 0, 5) },
				new() { id = "middle", position = new Vector3(0, 0, 10) },
				new() { id = "target", position = new Vector3(0, 0, 15) }
			};
			var links = new List<NetLink> { new("entry", "middle"), new("middle", "target") };
			var waypoint = new NetworkWaypoint { nodeId = "target", color = 1, label = "Vault", showPath = true };
			var waypoints = new[] { waypoint };
			try
			{
				var tierThree = new WaypointOverlay(camera, nodes, links, waypoints, 3, "entry");
				Assert.That(tierThree.Anchors, Is.EqualTo(new[] { nodes[2].position }));
				tierThree.RemoveFromHierarchy();

				var tierFour = new WaypointOverlay(camera, nodes, links, waypoints, 4, "entry");
				Assert.That(tierFour.Q<Label>("waypoint-marker-target"), Is.Not.Null);
				Assert.That(tierFour.Anchors, Is.EquivalentTo(new[]
					{ nodes[0].position, nodes[1].position, nodes[2].position, nodes[2].position }));
				tierFour.RemoveFromHierarchy();
			}
			finally
			{
				Object.DestroyImmediate(cameraObject);
			}
		}

		[Test]
		public void RadialResourceMeterUpdatesAccessibleLabelsAndTooltip()
		{
			var meter = new RadialResourceMeter("RAM meter");
			meter.SetValues(10, 7, 2, 1, Color.green, Color.yellow, "1 / 10", "GB FREE", "RAM remaining");
			Assert.That(meter.Q<Label>(className: "resource-radial-amount").text, Is.EqualTo("1 / 10"));
			Assert.That(meter.Q<Label>(className: "resource-radial-caption").text, Is.EqualTo("GB FREE"));
			Assert.That(meter.tooltip, Is.EqualTo("RAM remaining"));
		}

		[Test]
		public void CircuitBackdropTraceGenerationIsStableForAViewportSize()
		{
			var first = new CircuitBackdrop();
			var second = new CircuitBackdrop();
			var generate = typeof(CircuitBackdrop).GetMethod("Generate", BindingFlags.Instance | BindingFlags.NonPublic);
			var tracesField = typeof(CircuitBackdrop).GetField("traces", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(generate, Is.Not.Null);
			Assert.That(tracesField, Is.Not.Null);

			generate.Invoke(first, new object[] { 320, 180 });
			generate.Invoke(second, new object[] { 320, 180 });
			var firstTraces = (IEnumerable)tracesField.GetValue(first);
			var secondTraces = (IEnumerable)tracesField.GetValue(second);
			Assert.That(firstTraces.Cast<object>().Count(), Is.GreaterThan(0));
			Assert.That(CircuitTraceSignature(firstTraces), Is.EqualTo(CircuitTraceSignature(secondTraces)));
			Assert.That(first.pickingMode, Is.EqualTo(PickingMode.Ignore));
			Assert.That(second.pickingMode, Is.EqualTo(PickingMode.Ignore));
		}

		private static string CircuitTraceSignature(IEnumerable traces)
		{
			return string.Join("||", traces.Cast<object>().Select(trace =>
			{
				var points = (Vector2[])trace.GetType()
					.GetField("points", BindingFlags.Instance | BindingFlags.Public).GetValue(trace);
				return string.Join(";", points.Select(point => point.x.ToString("R") + "," + point.y.ToString("R")));
			}));
		}

		[Test]
		public void NetworkAppearanceRegistersEveryHackThePlanetNodeModel()
		{
			var resources = NetworkAppearance.Create(out var settings);
			try
			{
				Assert.That(settings, Is.Not.Null);
				foreach (NodeKind kind in System.Enum.GetValues(typeof(NodeKind)))
					Assert.That(resources.Load<GameObject>("GraphNodeModels/" + kind), Is.Not.Null, kind.ToString());
				Assert.That(resources.Load<GraphVisualSettings>("GraphVisualSettings"), Is.SameAs(settings));
			}
			finally
			{
				Object.DestroyImmediate(settings);
			}
		}
	}
}
