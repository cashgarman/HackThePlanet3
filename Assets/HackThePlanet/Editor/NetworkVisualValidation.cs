using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Cashg.GraphExplorer;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace HackThePlanet.Editor
{
	/// <summary>Captures and asserts 3D graph rendering, intel redaction, and node model gallery output.</summary>
	public static class NetworkVisualValidation
	{
		// Reads a non-public GraphViewport field by name for rendering assertions.
		private static T Field<T>(GraphViewport viewport, string name)
		{
			return (T)typeof(GraphViewport).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
										   .GetValue(viewport);
		}

		/// <summary>Exercises settling, view-only coordinates, redaction, research and the fixed rig layout.</summary>
		public static void RunLayoutChecks()
		{
			var sim = new GameSimulation(new Career());
			var network = NetworkGenerator.Generate(GameContent.Story[0], sim.Career);
			var before = JsonUtility.ToJson(new NodeSnapshot { nodes = network.nodes });
			foreach (var intel in new[] { ContractIntel.Full, ContractIntel.Topology, ContractIntel.None })
			{
				using var viewport = new GraphViewport(network.nodes, network.links, null, true, _ => { }, intel: intel);
				CheckLayout(viewport, sim);
				if (intel == ContractIntel.Full)
				{
					CaptureLayout(viewport, sim, "force-layout-network.png");
					Orbit(viewport, 55, 15);
					CaptureLayout(viewport, sim, "force-layout-orbit.png");
				}
			}

			var career = GameCatalog.Default.CreateCareer();
			career.researchRevealed = GameCatalog.Default.Technologies.Select(t => t.id).ToList();
			using (var research = new GraphViewport(career, null, _ => { }))
			{
				CheckLayout(research, sim);
				CaptureLayout(research, sim, "force-layout-research.png");
			}

			var document = new GraphDocument("layout-rig-test");
			document.UpsertNode(new GraphNodeRecord("slot", "slot", "Slot"));
			var rackSchema = new GraphSchema();
			rackSchema.Register(new GraphNodeTypeDefinition("slot", "Slot", "default", Color.white, 1));
			var rack = new GraphScene(document, rackSchema);
			rack.Nodes["slot"].Position = new Vector3(3, 4, 5);
			using (var rig = new GraphViewport(rack, _ => { }, new GraphCameraState(), true))
			{
				rig.Tick(sim, SignalEffects.Full);
				if (!Field<GraphRenderingSession>(rig, "session").Layout.Paused ||
					!rack.Nodes["slot"].Pinned || rack.Nodes["slot"].Position != new Vector3(3, 4, 0))
					throw new InvalidOperationException("Flat rig moved under force layout.");
			}

			var selected = network.nodes[0].id;
			using (var focused = new GraphViewport(network.nodes, network.links, selected, true, _ => { }))
			{
				focused.FocusNode(selected);
				var camera = Field<Camera>(focused, "camera");
				var distance = Field<float>(focused, "distance");
				for (var frame = 0; frame < 300; frame++)
					StepLayout(focused);
				var p = camera.WorldToViewportPoint(Field<GraphScene>(focused, "scene").Nodes[selected].Position);
				if (Mathf.Abs(p.x - .5f) > .001f || Mathf.Abs(p.y - .5f) > .001f ||
					Field<float>(focused, "distance") != distance)
					throw new InvalidOperationException("Layout lost user focus or refitted a manually adjusted camera.");
			}

			if (before != JsonUtility.ToJson(new NodeSnapshot { nodes = network.nodes }))
				throw new InvalidOperationException("Layout changed simulation coordinates.");
			File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName,
				"Artifacts/force-layout-validation.txt"),
				"PASS / Spatial operation, redacted, empty and research graphs settle and freeze; authored positions and flat rig remain intact.");
		}

		/// <summary>Settles a viewport and verifies motion, freezing, finite coordinates and FIT framing.</summary>
		/// <param name="viewport">Operation or research viewport to check.</param>
		/// <param name="simulation">Simulation supplying rendering state.</param>
		public static void CheckLayout(GraphViewport viewport, GameSimulation simulation)
		{
			var scene = Field<GraphScene>(viewport, "scene");
			var solver = Field<GraphRenderingSession>(viewport, "session").Layout;
			var before = scene.Nodes.ToDictionary(p => p.Key, p => p.Value.Position);
			if (solver.Mode != LayoutMode.Spatial || scene.Nodes.Values.Any(n => n.Pinned))
				throw new InvalidOperationException("Graph is not a free spatial layout.");
			var wasRunning = !solver.Paused;
			for (var frame = 0; frame < 300 && !solver.Paused; frame++)
				StepLayout(viewport);
			if (!solver.Paused || (scene.Nodes.Count > 0 && solver.Temperature >= .025f))
				throw new InvalidOperationException("Spatial layout did not settle within its bounded step budget.");
			if (wasRunning && scene.Nodes.Count > 1 && scene.Nodes.Values.All(n => n.Position == before[n.Id]))
				throw new InvalidOperationException("Forces did not move the warm-start coordinates.");
			var settled = scene.Nodes.ToDictionary(p => p.Key, p => p.Value.Position);
			viewport.Tick(simulation, SignalEffects.Full);
			if (scene.Nodes.Values.Any(n => !float.IsFinite(n.Position.sqrMagnitude) || n.Position != settled[n.Id]))
				throw new InvalidOperationException("Settled graph drifted or produced non-finite coordinates.");
			viewport.ResetView();
			var camera = Field<Camera>(viewport, "camera");
			foreach (var node in scene.Nodes.Values)
			{
				var p = camera.WorldToViewportPoint(node.Position);
				if (p.z <= 0 || p.x < 0 || p.x > 1 || p.y < 0 || p.y > 1)
					throw new InvalidOperationException("Settled FIT clips " + node.Id);
			}
		}

		private static void CaptureLayout(GraphViewport viewport, GameSimulation simulation, string name)
		{
			viewport.Tick(simulation, SignalEffects.Full);
			Field<Camera>(viewport, "camera").Render();
			SaveTexture(Field<RenderTexture>(viewport, "texture"), name);
		}

		// Avoid submitting hundreds of render batches into one editor frame while draining the solver.
		private static void StepLayout(GraphViewport viewport)
		{
			typeof(GraphViewport).GetMethod("AdvanceLayout", BindingFlags.Instance | BindingFlags.NonPublic)
				.Invoke(viewport, null);
		}

		/// <summary>Asserts contract-intel redaction rules for focused cards and preview graphs.</summary>
		/// <param name="check">Callback recording pass/fail assertions.</param>
		public static void CheckIntel(Action<bool, string> check)
		{
			// Create a secret database node that the redaction rules must hide.
			var fixture = new NetNode
			{
				label = "Secret vault", kind = NodeKind.Database, security = 9, detail = "SECRET INTERNAL DATA",
				objective = true
			};

			string Describe(ContractIntel intel, bool preview = false)
			{
				return (string)typeof(GraphViewport)
							   .GetMethod("DescribeNode", BindingFlags.Static | BindingFlags.NonPublic)
							   .Invoke(null, new object[] { fixture, intel, preview, GameCatalog.Default });
			}

			// Verify full-intel focus exposes the node role and security rating.
			check(
				Describe(ContractIntel.Full).Contains("SECURITY 9") &&
				Describe(ContractIntel.Full).Contains("Database"),
				"Easy focused data stores expose role and security intel.");

			// Verify medium focus redacts internal details and metrics.
			check(
				Describe(ContractIntel.Topology).Contains("LIMITED INTEL") &&
				!Describe(ContractIntel.Topology).Contains("SECRET") &&
				!Describe(ContractIntel.Topology).Contains("SECURITY 9"),
				"Medium focused cards redact internal details and metrics.");

			// Verify hard focus exposes only observed state.
			check(
				Describe(ContractIntel.None).StartsWith("NO INTEL") &&
				!Describe(ContractIntel.None).Contains("vault") && !Describe(ContractIntel.None).Contains("SECRET"),
				"Hard focused cards expose only observed state.");

			// Verify anonymous previews never disclose node types.
			check(
				Describe(ContractIntel.Topology, true).StartsWith("NO INTEL") &&
				!Describe(ContractIntel.Topology, true).Contains("Database"),
				"Anonymous preview cards never disclose node types.");

			// Generate a network and snapshot its nodes before any redaction runs.
			var network = NetworkGenerator.Generate(GameContent.Story[0], new Career());
			var before = JsonUtility.ToJson(new NodeSnapshot { nodes = network.nodes });

			GraphScene Project(bool preview, ContractIntel intel)
			{
				return (GraphScene)typeof(GraphViewport)
								   .GetMethod("BuildNetwork", BindingFlags.Static | BindingFlags.NonPublic)
								   .Invoke(null, new object[] { network.nodes, network.links, preview, intel });
			}

			// Project a full-intel preview of the network.
			var full = Project(true, ContractIntel.Full);
			check(
				full.Nodes.Count == network.nodes.Count &&
				full.Nodes.Values.Any(n => n.Record.TypeId.EndsWith("-objective")),
				"Easy previews preserve the full network and objective intel.");

			// Project a medium-intel preview and verify every system and link is kept.
			var topology = Project(true, ContractIntel.Topology);
			check(topology.Nodes.Count == full.Nodes.Count && topology.Edges.Count == full.Edges.Count,
				  "Medium previews preserve every system and connection.");

			// Verify medium graph records carry no types, labels, ownership, objectives or emphasis.
			check(
				topology.Schema.NodeTypes.Count == 1 &&
				topology.Nodes.Values.All(n => n.Record.TypeId == "unknown" && n.Label == "UNKNOWN SYSTEM" &&
											   n.Record.Properties.Count == 0 && n.Emphasis == ToolEmphasis.None),
				"Medium graph records contain no types, labels, ownership, objectives or emphasis.");

			// Verify medium previews preserve exact route geometry.
			check(topology.Nodes.Values.All(n => n.Position == full.Nodes[n.Id].Position),
				  "Medium previews preserve exact route geometry.");

			// Project a hard-intel preview, which must expose nothing.
			var blocked = Project(true, ContractIntel.None);
			check(blocked.Nodes.Count == 0 && blocked.Edges.Count == 0,
				  "Hard previews expose no systems or connections.");

			// Project the live network after connecting and verify discovered systems show normal intel.
			var live = Project(false, ContractIntel.None);
			check(
				live.Nodes.Count == network.nodes.Count(n => n.known) &&
				live.Nodes.Values.All(n => n.Record.TypeId != "unknown"),
				"Connecting restores normal discovered-system intel even on Hard.");

			// Verify redaction never mutates the generated operation.
			check(before == JsonUtility.ToJson(new NodeSnapshot { nodes = network.nodes }),
				  "Preview redaction never mutates the generated operation.");
		}

		/// <summary>Sends a double-click at the selected node to enter focused intel mode.</summary>
		/// <param name="viewport">Graph viewport with a selected node.</param>
		/// <exception cref="InvalidOperationException">Focus did not activate after the synthetic double-click.</exception>
		public static void DoubleClickSelected(GraphViewport viewport)
		{
			// Read the scene, camera, and selected node from the viewport.
			var scene = Field<GraphScene>(viewport, "scene");
			var camera = Field<Camera>(viewport, "camera");
			var selected = Field<string>(viewport, "selectedId");

			// Convert the selected node's world position into viewport-local coordinates.
			var point = camera.WorldToViewportPoint(scene.Nodes[selected].Position);
			var local = new Vector2(point.x * viewport.contentRect.width, (1 - point.y) * viewport.contentRect.height);
			var screen = viewport.LocalToWorld(local);

			// Send a pointer down and up twice to trigger a double-click.
			for (var i = 0; i < 2; i++)
			{
				// Dispatch a pointer-down event at the node's screen position.
				using (var down = PointerDownEvent.GetPooled(new Event
					   {
						   type = EventType.MouseDown, button = 0, mousePosition = screen
					   }))
				{
					down.target = viewport;
					viewport.SendEvent(down);
				}

				// Dispatch the matching pointer-up event to complete the click.
				using (var up = PointerUpEvent.GetPooled(new Event
					   {
						   type = EventType.MouseUp, button = 0, mousePosition = screen
					   }))
				{
					up.target = viewport;
					viewport.SendEvent(up);
				}
			}

			// Verify the viewport focused the picked system.
			if (Field<GraphCameraState>(viewport, "savedCamera").FocusedId != selected)
				throw new InvalidOperationException("Double-click did not focus the picked system.");
		}

		/// <summary>Verifies focused-node card layout, intel text, and camera framing.</summary>
		/// <param name="viewport">Graph viewport in focused intel mode.</param>
		/// <exception cref="InvalidOperationException">Focused presentation failed an assertion.</exception>
		public static void CheckFocusedCard(GraphViewport viewport)
		{
			// Read the saved camera state that the focused view must match.
			var saved = Field<GraphCameraState>(viewport, "savedCamera");

			// Verify the focused node survived the UI rebuild.
			if (saved.FocusedId != Field<string>(viewport, "selectedId") || !saved.UserAdjusted)
				throw new InvalidOperationException("Node focus did not survive UI rebuild.");

			// Look up the focused card and verify it is visible inside the graph bounds.
			var card = viewport.Q("focused-node-card");
			if (card.resolvedStyle.display == DisplayStyle.None || !viewport.worldBound.Contains(card.worldBound.min) ||
				!viewport.worldBound.Contains(card.worldBound.max))
				throw new InvalidOperationException("Focused card is hidden or extends outside the graph.");

			// Verify the card shows full intel and its static background.
			if (!GameplayRichText.Plain(viewport.Q<Label>("focused-node-intel").text).Contains("FULL INTEL") ||
				viewport.IntelStatic.image == null)
				throw new InvalidOperationException("Focused card lacks detailed intel or the static background.");

			// Look up the camera and scene, then project the focused node into the viewport.
			var camera = Field<Camera>(viewport, "camera");
			var scene = Field<GraphScene>(viewport, "scene");
			var point = camera.WorldToViewportPoint(scene.Nodes[saved.FocusedId].Position);

			// Verify the camera centers the node in the graph view.
			if (Mathf.Abs(point.x - .5f) > .01f || Mathf.Abs(point.y - .5f) > .01f)
				throw new InvalidOperationException(
					"Focused camera does not center the selected system in the graph view.");
		}

		/// <summary>Asserts FIT framing, captures an overview PNG, and checks for visible model pixels.</summary>
		/// <param name="viewport">Graph viewport showing a network.</param>
		/// <exception cref="InvalidOperationException">Nodes clip outside the view or rendering is blank.</exception>
		public static void CheckViewport(GraphViewport viewport)
		{
			// Reset the camera to the FIT framing before measuring.
			viewport.ResetView();

			// Look up the camera and scene used for projection.
			var camera = Field<Camera>(viewport, "camera");
			var scene = Field<GraphScene>(viewport, "scene");

			// Verify every node projects inside the viewport.
			foreach (var node in scene.Nodes.Values)
			{
				// Project the node and check it is in front of and inside the camera.
				var point = camera.WorldToViewportPoint(node.Position);
				if (point.z <= 0 || point.x < 0 || point.x > 1 || point.y < 0 || point.y > 1)
					throw new InvalidOperationException("FIT clips " + node.Id);
			}

			// Look up the render target and save an overview image.
			var texture = Field<RenderTexture>(viewport, "texture");
			SaveTexture(texture, "network-overview.png");

			// Read the rendered pixels back for analysis.
			var pixels = Read(texture);

			// Authored models use several hues. Compare against the cleared corner instead of assuming green geometry.
			var background = pixels[0];
			if (pixels.Count(c => Mathf.Abs(c.r - background.r) + Mathf.Abs(c.g - background.g) +
				Mathf.Abs(c.b - background.b) > 30) < 80)
				throw new InvalidOperationException("Network graph has no visible model pixels");
		}

		/// <summary>Sets orbit angles on the viewport and resets the camera.</summary>
		/// <param name="viewport">Graph viewport to reorient.</param>
		/// <param name="yaw">Horizontal orbit angle in degrees.</param>
		/// <param name="pitch">Vertical orbit angle in degrees.</param>
		public static void Orbit(GraphViewport viewport, float yaw, float pitch)
		{
			// Store the yaw angle on the viewport's private field.
			typeof(GraphViewport).GetField("yaw", BindingFlags.Instance | BindingFlags.NonPublic)
								 .SetValue(viewport, yaw);

			// Store the pitch angle on the viewport's private field.
			typeof(GraphViewport).GetField("pitch", BindingFlags.Instance | BindingFlags.NonPublic)
								 .SetValue(viewport, pitch);

			// Reset the camera so the new angles take effect.
			viewport.ResetView();
		}

		/// <summary>Renders every node model variant and writes gallery artifacts under Artifacts/.</summary>
		public static void Run()
		{
			// Resolve the project root so artifacts can be written beside Assets.
			var root = Directory.GetParent(Application.dataPath).FullName;

			// Create the appearance resources and an empty schema and document for the gallery.
			var resources = NetworkAppearance.Create(out var settings);
			var schema = new GraphSchema();
			var document = new GraphDocument("model-gallery", "Network hardware");
			var index = 0;

			// Render and register every node kind as its own model.
			foreach (NodeKind kind in Enum.GetValues(typeof(NodeKind)))
			{
				// Load the authored model prefab for this kind.
				var asset = Resources.Load<GameObject>("HackThePlanet/NodeModels/" + kind);

				// Fail when the model prefab is missing.
				if (asset == null)
					throw new InvalidOperationException("Missing model " + kind);

				// Expose the model to the renderer under its kind name.
				resources.Set("GraphNodeModels/" + kind, asset);

				// Validate that the imported mesh is readable, normalized, and single-material.
				using (var mesh = new OwnedMesh(GraphNodeModels.Create(kind.ToString(), resources)))
				{
					// Reject unreadable, oversized, or multi-submesh models.
					if (!mesh.Value.isReadable || mesh.Value.vertexCount < 8 || mesh.Value.subMeshCount != 1 ||
						mesh.Value.vertices.Any(v => v.magnitude > .501f))
						throw new InvalidOperationException("Invalid imported model " + kind);
				}

				// Register a node type definition for this kind so the schema can describe it.
				schema.Register(new GraphNodeTypeDefinition(kind.ToString(), kind.ToString(), kind.ToString(),
															Palette.Kind(kind), 1.5f));

				// Add a document node that places this kind in the gallery.
				document.UpsertNode(new GraphNodeRecord("model-" + index++, kind.ToString(), kind.ToString()));
			}

			// Build the scene from the gallery document and schema, and reset the layout index.
			var scene = new GraphScene(document, schema);
			index = 0;

			// Lay the models out in a row and pin them in place.
			foreach (var node in scene.Nodes.Values)
			{
				// Space each model along the x axis and pin it.
				node.Position = new Vector3((index++ - 3) * 3.4f, 0, 0);
				node.Pinned = true;
			}

			// Create a temporary camera and render target that capture the gallery.
			var obj = new GameObject("Network model validation camera");
			var camera = obj.AddComponent<Camera>();
			var texture = new RenderTexture(1400, 500, 24) { antiAliasing = 2 };
			texture.Create();

			// Configure the camera for an orthographic, manually rendered gallery shot.
			camera.enabled = false;
			camera.targetTexture = texture;
			camera.aspect = 2.8f;
			camera.fieldOfView = 48;
			camera.orthographic = true;
			camera.orthographicSize = 4.8f;
			camera.cullingMask = 1;
			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = Palette.Hex("071014");

			// Create the rendering session for this camera and resources.
			var session = GraphRenderingSession.Initialize(camera, resources);

			// Render inside try so the session and temporary objects are always released.
			try
			{
				// Bind the scene to the layout and freeze it for the capture.
				session.Layout.Bind(scene);
				session.Layout.Paused = true;

				// Keep every edge and update the renderer metrics for the filter.
				var filter = new EveryEdge();
				session.Renderer.UpdateMetrics(scene, filter);

				// Set the guide arrows that point at the first and sixth models.
				session.Renderer.GuideArrows = new[]
				{
					new GraphGuideArrow { NodeId = "model-0", Color = Palette.Lime },
					new GraphGuideArrow { NodeId = "model-5", Color = new Color(1f, .64f, .22f) }
				};

				// Render the gallery from three camera angles.
				for (var view = 0; view < 3; view++)
				{
					// Orient the camera for this view.
					var rotation = Quaternion.Euler(view == 0 ? 8 : 20, view == 0 ? 0 : view == 1 ? 25 : 155, 0);
					camera.transform.rotation = rotation;
					camera.transform.position = rotation * new Vector3(0, 0, -16);

					// Reduce motion for every view after the first.
					session.Renderer.ReducedMotion = view > 0;

					// Draw the scene and render it into the texture.
					session.Renderer.Draw(scene, filter, camera, null, null, null);
					camera.Render();

					// Verify the renderer submitted all seven models.
					if (session.Renderer.VisibleCount != 7)
						throw new InvalidOperationException("Gallery did not submit seven models");

					// Read the frame back and save it as a PNG.
					var pixels = Read(texture);
					SaveTexture(texture, "node-models-" + view + ".png");

					// Verify the frame contains enough colored model pixels.
					if (pixels.Count(c => c.g > 70 || c.r > 90 || c.b > 90) < 200)
						throw new InvalidOperationException("Blank model gallery");

					// Check that every node is pickable and visibly rendered in this view.
					foreach (var node in scene.Nodes.Values)
					{
						// Project the node to screen space and pick it back to confirm hit testing.
						var screen = camera.WorldToScreenPoint(node.Position);
						if (session.Renderer.Pick(scene, camera, screen)?.Id != node.Id)
							throw new InvalidOperationException("Model picking failed: " + node.Label);

						// Find the node's pixel position and count model-colored samples nearby.
						int x = Mathf.RoundToInt(screen.x), y = Mathf.RoundToInt(screen.y), hits = 0;

						// Scan a 51x51 window centered on the node.
						for (var dy = -25; dy <= 25; dy++)
							for (var dx = -25; dx <= 25; dx++)
							{
								// Compute the texture index for this sample and skip samples off the texture.
								int px = x + dx, py = y + dy;
								if (px < 0 || py < 0 || px >= texture.width || py >= texture.height)
									continue;

								// Count the sample as a hit when its color is model-like.
								var c = pixels[py * texture.width + px];
								if (c.g > 45 || c.r > 45 || c.b > 60)
									hits++;
							}

						// Fail when the node has too few model pixels nearby.
						if (hits < 12)
							throw new InvalidOperationException(
								"No rendered pixels for " + node.Label + " in view " + view + " (" + hits + ")");
					}
				}

				// Write the PASS summary artifact for the gallery run.
				File.WriteAllText(Path.Combine(root, "Artifacts/network-model-validation.txt"),
								  "PASS / Seven original readable normalized meshes, actual graph shader pixels and picking from front, three-quarter and rear views. Reduced motion exercised. Gallery order: Gateway, Workstation, Relay, Firewall, Scanner, Database, SecurityHub.");
			}
			finally
			{
				// Release the rendering session and detach the temporary camera from its target.
				session.Dispose();
				camera.targetTexture = null;
				texture.Release();

				// Destroy the temporary render texture, camera object, and settings.
				Object.DestroyImmediate(texture);
				Object.DestroyImmediate(obj);
				Object.DestroyImmediate(settings);
			}
		}

		// Reads GPU pixels from a render texture for nonblank image checks.
		private static Color32[] Read(RenderTexture texture)
		{
			// Remember the active render texture and create a readable CPU copy.
			var previous = RenderTexture.active;
			var read = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
			try
			{
				// Make the render target active and copy its pixels into the readable texture.
				RenderTexture.active = texture;
				read.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
				read.Apply();

				return read.GetPixels32();
			}
			finally
			{
				// Restore the previous render texture and free the readable copy.
				RenderTexture.active = previous;
				Object.DestroyImmediate(read);
			}
		}

		// Writes a render texture to an Artifacts PNG with the given base name.
		private static void SaveTexture(RenderTexture texture, string name)
		{
			// Remember the active render texture and create a readable CPU copy.
			var previous = RenderTexture.active;
			var read = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
			try
			{
				// Make the render target active and copy its pixels into the readable texture.
				RenderTexture.active = texture;
				read.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
				read.Apply();

				// Ensure the Artifacts folder exists beside Assets.
				var dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Artifacts");
				Directory.CreateDirectory(dir);

				// Encode the texture as a PNG and write it to disk.
				File.WriteAllBytes(Path.Combine(dir, name), read.EncodeToPNG());
			}
			finally
			{
				// Restore the previous render texture and free the readable copy.
				RenderTexture.active = previous;
				Object.DestroyImmediate(read);
			}
		}

		// Minimal node list wrapper for JSON snapshots in visual validation.
		[Serializable]
		private sealed class NodeSnapshot
		{
			public List<NetNode> nodes;
		}

		// Graph edge filter that accepts every edge for full-topology captures.
		private sealed class EveryEdge : IGraphEdgeFilter
		{
			public bool EdgeMatches(IGraphEdge edge)
			{
				return true;
			}
		}

		// Wraps a generated mesh and destroys it when disposed.
		private sealed class OwnedMesh : IDisposable
		{
			public readonly Mesh Value;

			public OwnedMesh(Mesh mesh)
			{
				// Keep the mesh so it can be destroyed when this wrapper is disposed.
				Value = mesh;
			}

			public void Dispose()
			{
				// Destroy the wrapped mesh.
				Object.DestroyImmediate(Value);
			}
		}
	}
}
