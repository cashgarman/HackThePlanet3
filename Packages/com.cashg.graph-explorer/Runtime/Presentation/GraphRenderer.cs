using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Cashg.GraphExplorer
{
	/// <summary>Instanced node meshes; a single dynamic line mesh for edges. No per-node GameObjects.</summary>
	public sealed class GraphRenderer : IDisposable
	{
		private const float EdgeWidth = .04f;
		private const float EdgeWidthIncident = .075f;

		private const float EdgeWidthHovered = .095f;

		// Pixel radius of the coverage filter. The quad is padded past it so the
		// falloff finishes inside the triangle instead of being cut into stairs.
		private const float EdgeFilterPixels = .8f;
		private const float EdgePadPixels = EdgeFilterPixels + .55f;
		/// <summary>Maximum camera distance at which edge labels are eligible.</summary>
		public const float EdgeLabelNearDistance = 4.5f;
		private static readonly Color Unmapped = new(.5f, .57f, .65f);
		private readonly HashSet<string> activePathEdges = new(StringComparer.Ordinal);
		private readonly HashSet<string> activePathNodes = new(StringComparer.Ordinal);
		private readonly Dictionary<string, List<Batch>> batches = new();
		private readonly Dictionary<string, int> degrees = new();
		private readonly List<Color> edgeColors = new();
		private readonly List<int> edgeIndices = new();
		private readonly Mesh edgeMesh;
		private readonly List<Vector4> edgeUvs = new();
		private readonly List<Vector3> edgeVertices = new();
		private readonly Plane[] frustum = new Plane[6];
		private readonly Mesh haloMesh;
		private readonly Dictionary<string, Vector2> metricRanges = new();
		private readonly Material nodeMaterial, edgeMaterial;

		private readonly GraphPresentationProfile profile;
		private readonly List<Batch> progressBatches = new();
		private readonly HashSet<string> rendered = new();
		private readonly GraphResourceConfiguration resources;
		private readonly Vector4[] selectionColor = new Vector4[1];
		private readonly Matrix4x4[] selectionMatrix = new Matrix4x4[1];
		private readonly MaterialPropertyBlock selectionProperties = new();

		private readonly Dictionary<string, Mesh> shapes = new();

		// Explicit selection reveals context. Hover never expands the filtered graph.
		private readonly HashSet<string> spotlight = new();
		private readonly HashSet<string> spotlightFocus = new();
		/// <summary>Gets or sets the node color mapping mode.</summary>
		public ColorMapping ColorBy = ColorMapping.Type;
		/// <summary>Gets or sets the identifier of the node currently being flashed.</summary>
		public string FlashTarget;
		/// <summary>Gets or sets the time when the current flash ends.</summary>
		public float FlashUntil;
		/// <summary>Gets or sets the maximum number of visible nodes.</summary>
		public int MaximumVisible = 8000;
		/// <summary>Scales relationship ribbon width for compact or high resolution hosts.</summary>
		public float EdgeWidthScale = 1f;
		/// <summary>Gets or sets progress visuals keyed by node identifier.</summary>
		public IReadOnlyDictionary<string, GraphNodeProgressVisual> NodeProgress;
		/// <summary>Gets or sets whether reduced motion is active.</summary>
		public bool ReducedMotion;
		/// <summary>Gets or sets the node size mapping mode.</summary>
		public SizeMapping Size = SizeMapping.Connections;
		/// <summary>Gets or sets task visuals keyed by node identifier.</summary>
		public Dictionary<string, GraphTaskVisual> TaskVisuals;
		/// <summary>Gets or sets whether degree counts include only visible edges.</summary>
		public bool VisibleDegree;
		private bool disposed;
		private Material progressMaterial;
		private Mesh progressMesh;
		private IGraphScene scene;
		private string selectedVisualId, hoveredVisualId;
		private float selectionPulse = 1;

		/// <summary>Creates a graph renderer and its owned meshes and materials.</summary>
		/// <param name="profile">Optional domain presentation profile.</param>
		/// <param name="options">Optional render budgets.</param>
		/// <param name="resources">Optional resource overrides.</param>
		public GraphRenderer(GraphPresentationProfile profile = null, GraphExplorerOptions options = null,
							 GraphResourceConfiguration resources = null)
		{
			this.profile = profile ?? new GraphPresentationProfile();
			this.resources = resources ?? new GraphResourceConfiguration();
			MaximumVisible = (options ?? new GraphExplorerOptions()).NodeRenderBudget;

			// Clone resource materials so renderer-specific properties cannot mutate imported assets.
			var shader = this.resources.Load<Shader>("GraphNodeToon");
			if (shader == null) throw new InvalidOperationException("Missing Resources/GraphNodeToon shader.");
			VisualSettings = this.resources.Load<GraphVisualSettings>("GraphVisualSettings");
			var sourceMaterial = this.resources.Load<Material>("GraphNodeMaterial");
			nodeMaterial = sourceMaterial != null ? new Material(sourceMaterial) : new Material(shader);
			nodeMaterial.shader = shader;
			nodeMaterial.enableInstancing = true;
			nodeMaterial.name = "Graph nodes";
			haloMesh = Ring();
			var edgeShader = this.resources.Load<Shader>("GraphEdge");
			if (edgeShader == null) edgeShader = shader;
			edgeMaterial = new Material(edgeShader) { name = "Graph edges" };
			edgeMaterial.SetColor("_BaseColor", Color.white);
			edgeMaterial.SetFloat("_Filter", EdgeFilterPixels);
			edgeMaterial.renderQueue = (int)RenderQueue.Geometry + 1;
			if (edgeMaterial.HasProperty("_Cull")) edgeMaterial.SetInt("_Cull", (int)CullMode.Off);
			edgeMesh = new Mesh { name = "Graph relationships", indexFormat = IndexFormat.UInt32 };
			edgeMesh.MarkDynamic();
		}

		/// <summary>Gets the number of nodes rendered by the latest draw.</summary>
		public int VisibleCount { get; private set; }
		/// <summary>Gets the number of edges rendered by the latest draw.</summary>
		public int VisibleEdges { get; private set; }
		/// <summary>Gets whether the node rendering budget truncated the graph.</summary>
		public bool Truncated { get; private set; }
		/// <summary>Gets the number of progress indicators rendered by the latest draw.</summary>
		public int VisibleProgressIndicators { get; private set; }

		/// <summary>Set the persistent route shown above transient selection and hover highlights.</summary>
		public GraphPathResult ActivePath { get; set; }

		/// <summary>Gets or sets the visual settings asset used by this renderer.</summary>
		public GraphVisualSettings VisualSettings { get; set; }

		/// <summary>Optional deterministic clock for previews. Null uses real unscaled time.</summary>
		public float? AnimationTimeOverride { get; set; }

		/// <summary>Releases meshes and materials owned by the renderer.</summary>
		public void Dispose()
		{
			if (disposed) return;
			disposed = true;

			// Release every native mesh and material owned by this renderer exactly once.
			foreach (var mesh in shapes.Values) Release(mesh);
			Release(haloMesh);
			Release(edgeMesh);
			Release(nodeMaterial);
			Release(edgeMaterial);
			Release(progressMesh);
			Release(progressMaterial);
		}

		private void EnsureShape(IGraphNode node)
		{
			var shape = profile.Type(scene, node).ShapeId;
			if (shapes.ContainsKey(shape)) return;

			// Initialize the owned model copy with neutral vertex colors for per-instance tinting.
			var mesh = GraphNodeModels.Create(shape, resources);
			var colors = new Color32[mesh.vertexCount];
			for (var i = 0; i < colors.Length; i++) colors[i] = new Color32(255, 255, 255, 255);
			mesh.colors32 = colors;
			shapes[shape] = mesh;
			batches[shape] = new List<Batch>();
		}

		/// <summary>Gets the connection count used for node sizing.</summary>
		/// <param name="node">Node to measure.</param>
		/// <returns>The cached degree, or the node's neighbor count.</returns>
		public int Degree(IGraphNode node)
		{
			return degrees.TryGetValue(node.Id, out var value) ? value : node.Neighbors.Count;
		}

		/// <summary>Calculates the rendered radius for a node.</summary>
		/// <param name="node">Node to size.</param>
		/// <returns>Node radius in world units.</returns>
		public float Radius(IGraphNode node)
		{
			if (Size == SizeMapping.Connections) return .55f + Mathf.Min(1.6f, Mathf.Sqrt(Degree(node)) * .17f);
			if (Size == SizeMapping.Metric && profile.TryMetric(scene, node, out var value, out var partition) &&
				metricRanges.TryGetValue(partition, out var range))
			{
				var fraction = Mathf.Approximately(range.x, range.y) ? .5f : Mathf.InverseLerp(range.x, range.y, value);
				// Bounded relative volume: radius is the cube root of normalized volume.
				return Mathf.Pow(1 + fraction * 7, 1f / 3f) * .65f;
			}

			return profile.Type(scene, node).DefaultRadius;
		}

		/// <summary>Calculates the rendered color for a node.</summary>
		/// <param name="node">Node to color.</param>
		/// <returns>Node color after mapping and tool emphasis are applied.</returns>
		public Color NodeColor(IGraphNode node)
		{
			// Resolve the chosen data mapping before applying filter dimming and workflow emphasis.
			var color = ColorBy == ColorMapping.Metric ? MetricColor(node) :
				ColorBy == ColorMapping.Type ? profile.Type(scene, node).Color :
				ColorBy == ColorMapping.Category ? profile.CategoryColor(scene, node, Unmapped) :
				Color.Lerp(new Color(.2f, .45f, .65f), new Color(1, .65f, .25f),
						   Mathf.Clamp01(Mathf.Log(Degree(node) + 1, 2) / 7));
			if (!node.Matches && !spotlight.Contains(node.Id)) color *= .3f;
			if (node.Emphasis == ToolEmphasis.Done)
			{
				var grey = (color.r + color.g + color.b) / 3f;
				color = Color.Lerp(new Color(grey, grey, grey), new Color(.4f, .42f, .46f), .78f) * .55f;
			}
			else if (node.Emphasis == ToolEmphasis.Next)
			{
				color = Color.Lerp(color, new Color(1f, .9f, .35f), .7f) * 1.45f;
			}

			return color;
		}

		// Normalize only within the schema-defined partition; unrelated units are never compared.
		private Color MetricColor(IGraphNode node)
		{
			if (!profile.TryMetric(scene, node, out var value, out var partition) ||
				!metricRanges.TryGetValue(partition, out var range))
				return Unmapped;
			var fraction = Mathf.Approximately(range.x, range.y) ? .5f : Mathf.InverseLerp(range.x, range.y, value);
			return profile.MetricColor(fraction);
		}

		/// <summary>Recomputes degrees and metric ranges for a scene.</summary>
		/// <param name="graph">Scene whose nodes and edges are measured.</param>
		/// <param name="filter">Edge filter used when visible-degree mode is enabled.</param>
		public void UpdateMetrics(IGraphScene graph, IGraphEdgeFilter filter)
		{
			// Recompute metric ranges from the currently eligible graph partition.
			scene = graph;
			degrees.Clear();
			metricRanges.Clear();
			var neighbors = VisibleDegree ? new Dictionary<string, HashSet<string>>() : null;
			foreach (var node in graph.Nodes.Values)
			{
				if (VisibleDegree) neighbors[node.Id] = new HashSet<string>();
				else degrees[node.Id] = node.Neighbors.Count;
				if (profile.TryMetric(scene, node, out var value, out var partition))
				{
					if (!metricRanges.TryGetValue(partition, out var range)) range = new Vector2(value, value);
					metricRanges[partition] = new Vector2(Mathf.Min(range.x, value), Mathf.Max(range.y, value));
				}
			}

			if (VisibleDegree)
			{
				// Count unique neighbors only across matching edges with two visible endpoints.
				foreach (var edge in graph.Edges.Values)
					if (filter.EdgeMatches(edge) && graph.Nodes[edge.From].Visible && graph.Nodes[edge.To].Visible)
					{
						neighbors[edge.From].Add(edge.To);
						neighbors[edge.To].Add(edge.From);
					}

				foreach (var entry in neighbors) degrees[entry.Key] = entry.Value.Count;
			}
		}

		/// <summary>Draws graph nodes, edges, labels, and selection effects.</summary>
		/// <param name="graph">Scene to draw.</param>
		/// <param name="filter">Filter used to draw edges.</param>
		/// <param name="camera">Camera used for rendering.</param>
		/// <param name="selected">Selected node, when any.</param>
		/// <param name="hovered">Hovered node, when any.</param>
		/// <param name="hoveredEdge">Hovered edge, when any.</param>
		public void Draw(IGraphScene graph, IGraphEdgeFilter filter, Camera camera, IGraphNode selected,
						 IGraphNode hovered, IGraphEdge hoveredEdge)
		{
			using var measurement = GraphDiagnostics.Draw.Auto();
			scene = graph;
			VisualSettings?.ApplySurface(nodeMaterial);
			selectedVisualId = selected?.Id;
			hoveredVisualId = hovered?.Id;
			selectionPulse = VisualSettings != null
				? VisualSettings.SelectionPulse(AnimationTimeOverride ?? Time.unscaledTime, ReducedMotion)
				: 1;
			VisibleCount = 0;
			VisibleEdges = 0;
			Truncated = false;

			// Reuse allocated batches but clear every per-frame visibility and highlight set.
			foreach (var list in batches.Values)
				foreach (var batch in list)
					batch.Count = 0;
			rendered.Clear();
			spotlight.Clear();
			spotlightFocus.Clear();
			activePathNodes.Clear();
			activePathEdges.Clear();
			if (ActivePath != null && ActivePath.IsReachable)
			{
				foreach (var id in ActivePath.NodeIds)
					if (graph.Nodes.ContainsKey(id))
						activePathNodes.Add(id);
				foreach (var id in ActivePath.EdgeIds)
					if (graph.Edges.ContainsKey(id))
						activePathEdges.Add(id);
			}

			CollectSpotlight(selected);
			GeometryUtility.CalculateFrustumPlanes(camera, frustum);

			// Reserve instances for selection, hover, path, and selected neighbors before filling the budget.
			if (selected != null) AddNode(selected);
			if (hovered != null && hovered.Visible) AddNode(hovered);
			foreach (var id in activePathNodes) AddNode(graph.Nodes[id]);
			RevealNeighbors(graph, selected);
			foreach (var node in graph.Nodes.Values)
			{
				if (!node.Visible || rendered.Contains(node.Id) ||
					!GeometryUtility.TestPlanesAABB(frustum, new Bounds(node.Position, Vector3.one * Radius(node) * 4)))
					continue;
				if (VisibleCount >= MaximumVisible)
				{
					Truncated = true;
					continue;
				}

				AddNode(node);
			}

			var parameters = new RenderParams(nodeMaterial)
				{ camera = camera, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };

			// Submit node instances by shape, supplying per-instance colors and selection data.
			foreach (var entry in batches)
				foreach (var batch in entry.Value)
					if (batch.Count > 0)
					{
						batch.Properties.SetVectorArray("_BaseColor", batch.Colors);
						batch.Properties.SetVectorArray("_NodeEffects", batch.Effects);
						parameters.matProps = batch.Properties;
						Graphics.RenderMeshInstanced(parameters, shapes[entry.Key], 0, batch.Matrices, batch.Count);
					}

			using (GraphDiagnostics.Edges.Auto())
			{
				// Build one dynamic ribbon mesh for eligible relationships and task decorations.
				edgeVertices.Clear();
				edgeColors.Clear();
				edgeUvs.Clear();
				edgeIndices.Clear();
				foreach (var edge in graph.Edges.Values)
				{
					var pathEdge = activePathEdges.Contains(edge.Id);
					if (!EdgeShown(edge, filter, graph, pathEdge)) continue;
					var fromDrawn = rendered.Contains(edge.From);
					var toDrawn = rendered.Contains(edge.To);
					if (!fromDrawn && !toDrawn) continue;
					var a = graph.Nodes[edge.From];
					var b = graph.Nodes[edge.To];
					if (!TryVisibleSegment(a, b, out var start, out var end)) continue;
					var emphasized = selected != null && (a == selected || b == selected);
					var incident = emphasized || (hovered != null && (a == hovered || b == hovered));
					var directHover = hoveredEdge != null && hoveredEdge.Id == edge.Id;
					var color = pathEdge
						? new Color(.98f, .76f, .27f, 1f)
						: EdgeColor(edge, a, b, incident, directHover);
					var width = pathEdge ? .16f :
						directHover ? EdgeWidthHovered :
						incident ? EdgeWidthIncident : EdgeWidth;
					// Dim unrelated links only for a selection. Hover brightens the link under the pointer and leaves the rest alone.
					if (!pathEdge && !directHover && selected != null && !emphasized)
					{
						color = new Color(color.r * .22f, color.g * .22f, color.b * .22f, color.a);
						width = EdgeWidth;
					}

					Ribbon(start, end, color, width * .5f * Mathf.Clamp(EdgeWidthScale, .1f, 8f), camera);
					if (pathEdge || directHover || (hoveredEdge == null && emphasized))
					{
						// Add an arrowhead at the destination for emphasized directed relationships.
						var direction = end - start;
						if (direction.sqrMagnitude > 1e-8f)
						{
							direction.Normalize();
							var side = Vector3.Cross(direction, camera.transform.forward);
							if (side.sqrMagnitude < 1e-8f) side = camera.transform.right;
							else side.Normalize();
							side *= .35f;
							var tip = b.Position;
							Ribbon(tip, tip - direction * .85f + side, color, width * .45f, camera);
							Ribbon(tip, tip - direction * .85f - side, color, width * .45f, camera);
						}
					}

					VisibleEdges++;
				}

				DrawTaskRings(graph, camera);
				edgeMesh.Clear();
				if (edgeVertices.Count > 0)
				{
					// Upload the combined geometry and keep its bounds valid while the camera is inside it.
					edgeMesh.SetVertices(edgeVertices);
					edgeMesh.SetColors(edgeColors);
					edgeMesh.SetUVs(0, edgeUvs);
					edgeMesh.SetIndices(edgeIndices, MeshTopology.Triangles, 0);
					edgeMesh.RecalculateBounds();
					var bounds = edgeMesh.bounds;
					bounds.Encapsulate(camera.transform.position);
					bounds.Expand(8f);
					edgeMesh.bounds = bounds;
					Graphics.RenderMesh(
						new RenderParams(edgeMaterial)
							{ camera = camera, worldBounds = bounds, shadowCastingMode = ShadowCastingMode.Off },
						edgeMesh, 0, Matrix4x4.identity);
				}
			}

			if (VisualSettings != null && VisualSettings.ShowSelectionRing)
			{
				if (selected != null && rendered.Contains(selected.Id)) Halo(selected, camera, new Color(1, .85f, .5f));
				if (hovered != null && hovered != selected && rendered.Contains(hovered.Id))
					Halo(hovered, camera, new Color(.65f, .85f, 1));
			}

			foreach (var id in activePathNodes)
				if (graph.Nodes.TryGetValue(id, out var pathNode) && rendered.Contains(id))
					Halo(pathNode, camera, new Color(1f, .72f, .2f));
			DrawNodeProgress(graph, camera);
		}

		/// <summary>Draw the same visible instance batches into the camera's node-only masks.</summary>
		public void DrawEffectsMask(RasterCommandBuffer command, Material material)
		{
			foreach (var entry in batches)
				foreach (var batch in entry.Value)
					if (batch.Count > 0)
						command.DrawMeshInstanced(shapes[entry.Key], 0, material, 0, batch.Matrices, batch.Count,
												  batch.Properties);
		}

		private void CollectSpotlight(IGraphNode focus)
		{
			if (focus == null) return;
			spotlightFocus.Add(focus.Id);
			spotlight.Add(focus.Id);
			foreach (var id in focus.Neighbors) spotlight.Add(id);
		}

		private bool EdgeShown(IGraphEdge edge, IGraphEdgeFilter filter, IGraphScene graph, bool pathEdge = false)
		{
			return pathEdge ||
				   (filter.EdgeMatches(edge) && graph.Nodes[edge.From].Visible && graph.Nodes[edge.To].Visible)
				   || spotlightFocus.Contains(edge.From) || spotlightFocus.Contains(edge.To);
		}

		private void RevealNeighbors(IGraphScene graph, IGraphNode focus)
		{
			if (focus == null) return;

			// Explicit selection reveals adjacent nodes while respecting the remaining node budget.
			foreach (var id in focus.Neighbors)
			{
				if (!graph.Nodes.TryGetValue(id, out var other) || rendered.Contains(other.Id)) continue;
				if (VisibleCount >= MaximumVisible)
				{
					Truncated = true;
					break;
				}

				AddNode(other);
			}
		}

		private void DrawTaskRings(IGraphScene graph, Camera camera)
		{
			// Draw workflow emphasis first, then task-specific progress and status marks for rendered nodes.
			foreach (var node in graph.Nodes.Values)
				if (node.Emphasis == ToolEmphasis.Next && rendered.Contains(node.Id))
					TaskRing(node, camera, new Color(1f, .88f, .3f), 1.9f, 0, 1, .05f);
			if (TaskVisuals != null)
				foreach (var pair in TaskVisuals)
				{
					if (!rendered.Contains(pair.Key) || !graph.Nodes.TryGetValue(pair.Key, out var node)) continue;
					var visual = pair.Value;
					var color = GraphTaskVisual.Color(visual.Status);
					var start = visual.Status == "running" && !visual.Progress.HasValue && !ReducedMotion
						? Time.unscaledTime * 2
						: 0;
					var fraction = visual.Status == "running" ? visual.Progress ?? .25f : 1;
					if (visual.Status == "draft")
						for (var segment = 0; segment < 8; segment++)
							TaskRing(node, camera, color, 1.55f, segment * Mathf.PI / 4, .075f, .05f);
					else TaskRing(node, camera, color, 1.55f, start, 1, .035f);
					if (visual.Status == "running")
						TaskRing(node, camera, color * 1.3f, 1.7f, start, Mathf.Clamp01(fraction), .08f);
					if (visual.Status == "failed" || visual.Status == "blocked" || visual.Status == "needs_input")
					{
						var offset = camera.transform.up * Radius(node) * 2.2f;
						Ribbon(node.Position + offset, node.Position + offset + camera.transform.up * .65f, color, .1f,
							   camera);
						Ribbon(node.Position + offset - camera.transform.up * .25f - camera.transform.right * .06f,
							   node.Position + offset - camera.transform.up * .25f + camera.transform.right * .06f,
							   color, .1f, camera);
					}

					if (visual.Status == "completed")
					{
						var anchor = node.Position + camera.transform.right * Radius(node) * 1.9f;
						var elbow = anchor + camera.transform.right * .25f - camera.transform.up * .25f;
						Ribbon(anchor, elbow, color, .08f, camera);
						Ribbon(elbow, elbow + camera.transform.right * .5f + camera.transform.up * .65f, color, .08f,
							   camera);
					}
				}

			if (!ReducedMotion && Time.unscaledTime < FlashUntil && FlashTarget != null &&
				graph.Nodes.TryGetValue(FlashTarget, out var target) && rendered.Contains(FlashTarget))
			{
				// Briefly connect the camera to the task target after dispatch when motion effects are enabled.
				TaskRing(target, camera, new Color(1, .9f, .65f), 2.1f, 0, 1, .12f);
				var origin = camera.transform.position + camera.transform.forward * 2 + camera.transform.right * .7f -
							 camera.transform.up * .5f;
				Ribbon(origin, target.Position, new Color(.6f, .85f, 1), .04f, camera);
			}
		}

		private void DrawNodeProgress(IGraphScene graph, Camera camera)
		{
			VisibleProgressIndicators = 0;
			if (NodeProgress == null || (VisualSettings != null && !VisualSettings.ShowNodeProgress)) return;
			var color = VisualSettings != null ? VisualSettings.ProgressColor : new Color(.25f, .95f, .48f);
			var track = VisualSettings != null ? VisualSettings.ProgressTrackColor : new Color(.16f, .23f, .20f);
			var scale = VisualSettings != null ? Mathf.Max(2.3f, VisualSettings.ProgressRadiusScale) : 2.45f;
			var width = VisualSettings != null ? VisualSettings.ProgressWidth : .07f;
			var badgeSize = VisualSettings != null ? VisualSettings.CompletionBadgeSize : .4f;
			foreach (var batch in progressBatches) batch.Count = 0;

			// Pack finite progress values into camera-facing instance quads, capped at 1023 per batch.
			foreach (var pair in NodeProgress)
			{
				if (!rendered.Contains(pair.Key) || !graph.Nodes.TryGetValue(pair.Key, out var node) ||
					pair.Value == null || !pair.Value.TryFraction(out var fraction)) continue;
				var index = VisibleProgressIndicators++ / 1023;
				if (index == progressBatches.Count) progressBatches.Add(new Batch());
				var batch = progressBatches[index];
				var radius = Radius(node);
				batch.Matrices[batch.Count] = Matrix4x4.TRS(node.Position - camera.transform.forward * radius * .02f,
															camera.transform.rotation, Vector3.one * radius * scale);
				batch.Effects[batch.Count] = new Vector4(fraction, pair.Value.Complete ? 1 : 0,
														 Mathf.Max(.005f, width) / scale,
														 Mathf.Clamp(badgeSize, .15f, .8f) / scale);
				batch.Count++;
			}

			if (VisibleProgressIndicators == 0) return;
			if (progressMaterial == null)
			{
				// Allocate overlay resources lazily once an eligible progress indicator actually exists.
				var shader = resources.Load<Shader>("GraphNodeProgress");
				if (shader == null) throw new InvalidOperationException("Missing Resources/GraphNodeProgress shader.");
				var source = resources.Load<Material>("GraphNodeProgressMaterial");
				progressMaterial = source != null ? new Material(source) : new Material(shader);
				progressMaterial.shader = shader;
				progressMaterial.name = "Graph node progress";
				progressMaterial.enableInstancing = true;
				progressMesh = new Mesh { name = "Graph progress quad" };
				progressMesh.vertices = new[]
				{
					new Vector3(-1.35f, -1.35f, 0), new Vector3(1.35f, -1.35f, 0), new Vector3(1.35f, 1.35f, 0),
					new Vector3(-1.35f, 1.35f, 0)
				};
				progressMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
				progressMesh.RecalculateBounds();
			}

			progressMaterial.SetColor("_ProgressColor", color);
			progressMaterial.SetColor("_TrackColor", track);
			foreach (var batch in progressBatches)
				if (batch.Count > 0)
				{
					batch.Properties.SetVectorArray("_ProgressData", batch.Effects);
					if (camera.GetComponent<GraphNodeEffects>()?.Renderer != this)
						Graphics.RenderMeshInstanced(
							new RenderParams(progressMaterial)
							{
								camera = camera, matProps = batch.Properties, shadowCastingMode = ShadowCastingMode.Off,
								receiveShadows = false
							}, progressMesh, 0, batch.Matrices, batch.Count);
				}
		}

		/// <summary>URP camera-local overlay, independent of scene labels and node effects.</summary>
		public void DrawProgressOverlay(RasterCommandBuffer command)
		{
			// Reuse the prepared progress batches in the render feature's overlay pass.
			if (VisibleProgressIndicators == 0 || progressMaterial == null || progressMesh == null) return;
			foreach (var batch in progressBatches)
				if (batch.Count > 0)
					command.DrawMeshInstanced(progressMesh, 0, progressMaterial, 0, batch.Matrices, batch.Count,
											  batch.Properties);
		}

		private void TaskRing(IGraphNode node, Camera camera, Color color, float scale, float start, float fraction,
							  float width)
		{
			// Approximate a camera-facing circular arc with the shared ribbon builder.
			var segments = Mathf.Max(1, Mathf.CeilToInt(32 * fraction));
			var radius = Radius(node) * scale;
			for (var i = 0; i < segments; i++)
			{
				float a = start + i / (float)segments * Mathf.PI * 2 * fraction,
					  b = start + (i + 1) / (float)segments * Mathf.PI * 2 * fraction;
				var p = node.Position + (camera.transform.right * Mathf.Cos(a) + camera.transform.up * Mathf.Sin(a)) *
					radius;
				var q = node.Position + (camera.transform.right * Mathf.Cos(b) + camera.transform.up * Mathf.Sin(b)) *
					radius;
				Ribbon(p, q, color, width, camera);
			}
		}

		private void AddNode(IGraphNode node)
		{
			if (!rendered.Add(node.Id)) return;
			VisibleCount++;
			EnsureShape(node);

			// Split instances at Unity's batch limit while preserving independent color and effect data.
			var list = batches[profile.Type(scene, node).ShapeId];
			var index = 0;
			while (index < list.Count && list[index].Count == 1023) index++;
			if (index == list.Count) list.Add(new Batch());
			var batch = list[index];
			var radius = Radius(node);
			batch.Matrices[batch.Count] = Matrix4x4.TRS(node.Position, Quaternion.identity, Vector3.one * radius * 2);
			batch.Colors[batch.Count] = NodeInstanceColor(node);
			batch.Effects[batch.Count] =
				new Vector4(node.Id == selectedVisualId ? 1 : node.Id == hoveredVisualId ? .16f : 0, selectionPulse, 0,
							0);
			batch.Count++;
		}

		// Instanced alpha drives the surface pulse in GraphUnlit. Only a running task sets it.
		private Color NodeInstanceColor(IGraphNode node)
		{
			var color = NodeColor(node);
			color.a = 0f;
			if (TaskVisuals == null || !TaskVisuals.TryGetValue(node.Id, out var task) ||
				task.Status != "running") return color;
			var pulse = RunningPulse(node.Id);
			var glow = GraphTaskVisual.Color(task.Status);
			color = Color.Lerp(color, glow, .12f + .38f * pulse) * (1f + .7f * pulse);
			color.a = pulse;
			return color;
		}

		private float RunningPulse(string id)
		{
			if (ReducedMotion) return .8f;
			var phase = (id.GetHashCode() & 1023) * (Mathf.PI * 2f / 1023f);
			return .18f + .82f * (.5f + .5f * Mathf.Sin(Time.unscaledTime * 4.6f + phase));
		}

		private void Halo(IGraphNode node, Camera camera, Color color)
		{
			selectionMatrix[0] = Matrix4x4.TRS(node.Position, Quaternion.identity, Vector3.one * Radius(node) * 2.8f);
			selectionColor[0] = color;
			selectionColor[0].w = 0f;
			selectionProperties.SetVectorArray("_BaseColor", selectionColor);
			Graphics.RenderMeshInstanced(
				new RenderParams(nodeMaterial)
					{ camera = camera, matProps = selectionProperties, shadowCastingMode = ShadowCastingMode.Off },
				haloMesh, 0, selectionMatrix, 1);
		}

		private Color EdgeColor(IGraphEdge edge, IGraphNode a, IGraphNode b, bool incident, bool directHover)
		{
			// Start with relation semantics, then combine filtering, workflow, selection, and hover brightness.
			var color = scene.Schema.Relations.TryGetValue(edge.Relation, out var relation)
				? relation.Color
				: new Color(.62f, .67f, .74f);
			if (edge.Status == "hypothesis") color = Color.Lerp(color, new Color(.95f, .55f, .28f), .35f);
			var brightness = a.Matches && b.Matches ? .88f : a.Visible && b.Visible ? .52f : .28f;
			if (a.Emphasis == ToolEmphasis.Done && b.Emphasis == ToolEmphasis.Done) brightness *= .4f;
			else if (a.Emphasis == ToolEmphasis.Next || b.Emphasis == ToolEmphasis.Next)
				brightness = Mathf.Max(brightness, 1.15f);
			if (incident) brightness = Mathf.Max(brightness, 1.08f);
			if (directHover) brightness = 1.28f;
			return new Color(Mathf.Min(1f, color.r * brightness), Mathf.Min(1f, color.g * brightness),
							 Mathf.Min(1f, color.b * brightness), 1f);
		}

		/// <summary>Determines whether a node was included in the latest draw.</summary>
		/// <param name="nodeId">Node identifier.</param>
		/// <returns><see langword="true"/> when the node was drawn.</returns>
		public bool IsDrawn(string nodeId)
		{
			return rendered.Contains(nodeId);
		}

		/// <summary>Gets the world-space segment between two nodes when non-degenerate.</summary>
		/// <param name="a">First endpoint node.</param>
		/// <param name="b">Second endpoint node.</param>
		/// <param name="start">Receives the segment start.</param>
		/// <param name="end">Receives the segment end.</param>
		/// <returns><see langword="true"/> when a visible segment exists.</returns>
		public bool TryVisibleSegment(IGraphNode a, IGraphNode b, out Vector3 start, out Vector3 end)
		{
			start = end = default;
			if (a == null || b == null) return false;
			start = a.Position;
			end = b.Position;
			return (end - start).sqrMagnitude >= 1e-8f;
		}

		/// <summary>Projects a world point to screen coordinates and checks whether it is near the viewport.</summary>
		/// <param name="camera">Projection camera.</param>
		/// <param name="world">World-space point.</param>
		/// <param name="screenPoint">Receives the projected panel point.</param>
		/// <returns><see langword="true"/> when the point is in front of and near the viewport.</returns>
		public bool TryProject(Camera camera, Vector3 world, out Vector2 screenPoint)
		{
			// Convert Unity's bottom-origin screen coordinates to panel coordinates with a small offscreen margin.
			var screen = camera.WorldToScreenPoint(world);
			screenPoint = new Vector2(screen.x, Screen.height - screen.y);
			return screen.z > camera.nearClipPlane
				   && screen.x >= -48f && screen.x <= Screen.width + 48f
				   && screen.y >= -48f && screen.y <= Screen.height + 48f;
		}

		/// <summary>Finds the point on an edge nearest to the camera.</summary>
		/// <param name="a">First endpoint node.</param>
		/// <param name="b">Second endpoint node.</param>
		/// <param name="camera">Camera used for the query.</param>
		/// <param name="anchor">Receives the nearest world-space point.</param>
		/// <param name="distance">Receives the camera distance to the point.</param>
		/// <returns><see langword="true"/> when the edge is within the label distance.</returns>
		public bool TryCameraNearEdge(IGraphNode a, IGraphNode b, Camera camera, out Vector3 anchor, out float distance)
		{
			anchor = default;
			distance = float.MaxValue;
			if (!TryVisibleSegment(a, b, out var start, out var end) || !ClipNear(ref start, ref end, camera))
				return false;
			anchor = ClosestWorld(start, end, camera.transform.position, out _);

			// Nearby edge labels follow the closest visible point only while it remains in front of the camera.
			var delta = anchor - camera.transform.position;
			distance = delta.magnitude;
			return Vector3.Dot(delta, camera.transform.forward) > camera.nearClipPlane &&
				   distance <= EdgeLabelNearDistance;
		}

		/// <summary>Finds an anchor point for an edge incident to the selected node.</summary>
		/// <param name="selected">Selected endpoint.</param>
		/// <param name="other">Other edge endpoint.</param>
		/// <param name="camera">Camera used for the query.</param>
		/// <param name="anchor">Receives the world-space anchor.</param>
		/// <param name="distance">Receives the camera distance to the near point.</param>
		/// <returns><see langword="true"/> when a visible edge segment exists.</returns>
		public bool TrySelectedEdgeAnchor(IGraphNode selected, IGraphNode other, Camera camera, out Vector3 anchor,
										  out float distance)
		{
			anchor = default;
			distance = float.MaxValue;
			if (selected == null || other == null || !TryVisibleSegment(selected, other, out var start, out var end) ||
				!ClipNear(ref start, ref end, camera)) return false;
			var near = ClosestWorld(start, end, camera.transform.position, out _);

			// Prefer the nearby segment; otherwise bias the label toward the selected endpoint.
			distance = Vector3.Distance(near, camera.transform.position);
			anchor = distance <= EdgeLabelNearDistance ? near : Vector3.Lerp(start, end, .28f);
			return true;
		}

		private void Ribbon(Vector3 a, Vector3 b, Color color, float halfWidth, Camera camera)
		{
			if (!ClipNear(ref a, ref b, camera)) return;

			// Convert world width to endpoint pixel widths so filtering remains consistent under perspective.
			var span = b - a;
			var length = span.magnitude;
			if (length < 1e-5f) return;
			var direction = span / length;
			var forward = camera.transform.forward;
			var right = camera.transform.right;
			var up = camera.transform.up;
			var origin = camera.transform.position;
			var near = camera.nearClipPlane + .08f;
			var tanHalf = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f);
			if (tanHalf < 1e-4f) return;
			var viewScale = Mathf.Max(1f, camera.pixelHeight) / (2f * tanHalf);
			var depthA = Mathf.Max(Vector3.Dot(a - origin, forward), near);
			var depthB = Mathf.Max(Vector3.Dot(b - origin, forward), near);
			var coreA = halfWidth * viewScale / depthA;
			var coreB = halfWidth * viewScale / depthB;
			var halfA = (coreA + EdgePadPixels) * depthA / viewScale;
			var halfB = (coreB + EdgePadPixels) * depthB / viewScale;
			var lateral = new Vector2(Vector3.Dot(direction, right), Vector3.Dot(direction, up)).magnitude;
			var extendA = CapExtension(lateral, depthA, viewScale, depthA - near, Vector3.Dot(direction, forward));
			var extendB = CapExtension(lateral, depthB, viewScale, depthB - near, -Vector3.Dot(direction, forward));
			var start = a - direction * extendA;
			var end = b + direction * extendB;
			var za = Mathf.Max(Vector3.Dot(a - origin, forward), near);
			var zb = Mathf.Max(Vector3.Dot(b - origin, forward), near);
			var pa = new Vector2(Vector3.Dot(a - origin, right), Vector3.Dot(a - origin, up)) / za;
			var pb = new Vector2(Vector3.Dot(b - origin, right), Vector3.Dot(b - origin, up)) / zb;
			var screenDir = pb - pa;
			var segmentPixels = screenDir.magnitude * viewScale;
			var perp = screenDir.sqrMagnitude < 1e-10f
				? Vector2.right
				: new Vector2(-screenDir.y, screenDir.x).normalized;
			var side = right * perp.x + up * perp.y;
			var capA = extendA * lateral * viewScale / depthA;
			var capB = extendB * lateral * viewScale / depthB;
			var index = edgeVertices.Count;

			// Pad the quad and encode core width and cap distances for shader coverage filtering.
			AddEdgeVertex(start - side * halfA, color,
						  new Vector4(-(coreA + EdgePadPixels), coreA, -capA, segmentPixels));
			AddEdgeVertex(start + side * halfA, color, new Vector4(coreA + EdgePadPixels, coreA, -capA, segmentPixels));
			AddEdgeVertex(end - side * halfB, color,
						  new Vector4(-(coreB + EdgePadPixels), coreB, segmentPixels + capB, segmentPixels));
			AddEdgeVertex(end + side * halfB, color,
						  new Vector4(coreB + EdgePadPixels, coreB, segmentPixels + capB, segmentPixels));
			edgeIndices.Add(index);
			edgeIndices.Add(index + 1);
			edgeIndices.Add(index + 2);
			edgeIndices.Add(index + 1);
			edgeIndices.Add(index + 3);
			edgeIndices.Add(index + 2);
		}

		private void AddEdgeVertex(Vector3 position, Color color, Vector4 uv)
		{
			edgeVertices.Add(position);
			edgeColors.Add(color);
			edgeUvs.Add(uv);
		}

		private static float CapExtension(float lateral, float depth, float viewScale, float depthRoom,
										  float slopeTowardCamera)
		{
			// Add enough world-space padding for the screen filter without extending through the near plane.
			var pixelsPerWorld = Mathf.Max(lateral, .05f) * viewScale / Mathf.Max(depth, 1e-4f);
			var extend = EdgePadPixels / pixelsPerWorld;
			if (slopeTowardCamera > 1e-4f) extend = Mathf.Min(extend, Mathf.Max(0f, depthRoom) / slopeTowardCamera);
			return extend;
		}

		private static bool ClipNear(ref Vector3 a, ref Vector3 b, Camera camera)
		{
			// Reject fully hidden segments and trim crossing segments to a plane just beyond the camera near clip.
			var origin = camera.transform.position + camera.transform.forward * (camera.nearClipPlane + .08f);
			var da = Vector3.Dot(a - origin, camera.transform.forward);
			var db = Vector3.Dot(b - origin, camera.transform.forward);
			if (da < 0f && db < 0f) return false;
			if (da >= 0f && db >= 0f) return true;
			var t = da / (da - db);
			var hit = Vector3.Lerp(a, b, t);
			if (da < 0f) a = hit;
			else b = hit;
			return true;
		}

		private static Vector3 ClosestWorld(Vector3 a, Vector3 b, Vector3 point, out float t)
		{
			var span = b - a;
			var length = span.sqrMagnitude;
			t = length < 1e-8f ? 0f : Mathf.Clamp01(Vector3.Dot(point - a, span) / length);
			return a + span * t;
		}

		/// <summary>Finds the closest visible edge to a screen-space pointer.</summary>
		/// <param name="graph">Scene containing candidate edges.</param>
		/// <param name="filter">Filter applied to candidate edges.</param>
		/// <param name="camera">Camera used for projection.</param>
		/// <param name="mouse">Pointer position in screen coordinates.</param>
		/// <param name="maxPixels">Minimum hit radius in pixels.</param>
		/// <returns>The picked edge, or null when none is close enough.</returns>
		public IGraphEdge PickEdge(IGraphScene graph, IGraphEdgeFilter filter, Camera camera, Vector2 mouse,
								   float maxPixels = 16f)
		{
			using var profile = GraphDiagnostics.PickEdge.Auto();
			IGraphEdge hit = null;
			var nearest = float.MaxValue;
			var tanHalf = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f);
			var pixelHeight = Mathf.Max(1f, camera.pixelHeight);

			// Choose the closest screen-space relationship using a width-aware hit tolerance.
			foreach (var edge in graph.Edges.Values)
			{
				if (!EdgeShown(edge, filter, graph)) continue;
				if (!rendered.Contains(edge.From) && !rendered.Contains(edge.To)) continue;
				if (!TryVisibleSegment(graph.Nodes[edge.From], graph.Nodes[edge.To], out var a, out var b) ||
					!ClipNear(ref a, ref b, camera)) continue;
				var distance = ScreenSegmentDistance(camera, mouse, a, b);
				var depth = Mathf.Max(camera.nearClipPlane,
									  Vector3.Dot((a + b) * .5f - camera.transform.position, camera.transform.forward));
				var pixelHalf = EdgeWidth * .5f * pixelHeight / (2f * depth * tanHalf);
				var limit = Mathf.Clamp(pixelHalf + 12f, maxPixels, 64f);
				if (distance < limit && distance < nearest)
				{
					nearest = distance;
					hit = edge;
				}
			}

			return hit;
		}

		// The hover label is anchored to the midpoint of the full connection (center to center).
		// The midpoint is never taken from a near-clipped piece: clipping would slide the anchor toward
		// whichever endpoint is closest to the camera, so the label would jump onto that node.
		/// <summary>Projects the midpoint of an edge for a hover label.</summary>
		/// <param name="edge">Edge to label.</param>
		/// <param name="graph">Scene containing the edge endpoints.</param>
		/// <param name="camera">Projection camera.</param>
		/// <param name="panelPoint">Receives the projected panel coordinate.</param>
		/// <returns><see langword="true"/> when the midpoint is visible.</returns>
		public bool TryEdgeLabelAnchor(IGraphEdge edge, IGraphScene graph, Camera camera, out Vector2 panelPoint)
		{
			panelPoint = default;
			if (edge == null || graph == null || !graph.Nodes.TryGetValue(edge.From, out var from) ||
				!graph.Nodes.TryGetValue(edge.To, out var to)) return false;
			if (!TryVisibleSegment(from, to, out var a, out var b)) return false;
			return TryProject(camera, (a + b) * .5f, out panelPoint);
		}

		private static float ScreenSegmentDistance(Camera camera, Vector2 mouse, Vector3 a, Vector3 b)
		{
			// Project onto the finite screen segment, including degenerate segments that collapse to one pixel.
			var sa = camera.WorldToScreenPoint(a);
			var sb = camera.WorldToScreenPoint(b);
			if (sa.z <= 0 || sb.z <= 0) return float.MaxValue;
			var p0 = new Vector2(sa.x, sa.y);
			var p1 = new Vector2(sb.x, sb.y);
			var delta = p1 - p0;
			var length = delta.sqrMagnitude;
			if (length < 1e-4f) return Vector2.Distance(mouse, p0);
			var t = Mathf.Clamp01(Vector2.Dot(mouse - p0, delta) / length);
			return Vector2.Distance(mouse, p0 + delta * t);
		}

		/// <summary>Finds the closest visible node to a screen-space pointer.</summary>
		/// <param name="graph">Scene containing candidate nodes.</param>
		/// <param name="camera">Camera used for projection.</param>
		/// <param name="mouse">Pointer position in screen coordinates.</param>
		/// <returns>The picked node, or null when none is close enough.</returns>
		public IGraphNode Pick(IGraphScene graph, Camera camera, Vector2 mouse)
		{
			using var profile = GraphDiagnostics.PickNode.Auto();
			var ray = camera.ScreenPointToRay(mouse);
			IGraphNode hit = null;
			var nearest = float.MaxValue;
			foreach (var node in graph.Nodes.Values)
			{
				if ((!node.Visible && !spotlight.Contains(node.Id)) || !rendered.Contains(node.Id)) continue;
				// All models fit the same radius; a forgiving sphere also keeps thin
				// badges, pins and the globe's holes easy to target from any angle.
				var offset = ray.origin - node.Position;
				var b = Vector3.Dot(offset, ray.direction);
				var radius = Radius(node) * 1.15f;
				var discriminant = b * b - offset.sqrMagnitude + radius * radius;
				if (discriminant < 0) continue;
				var distance = -b - Mathf.Sqrt(discriminant);
				if (distance < 0) distance = -b + Mathf.Sqrt(discriminant);
				if (distance >= 0 && distance < nearest)
				{
					nearest = distance;
					hit = node;
				}
			}

			return hit;
		}

		private static Mesh Ring()
		{
			// Build three perpendicular line circles to keep the selection halo readable from any viewing angle.
			var mesh = new Mesh();
			var vertices = new Vector3[96];
			var indices = new int[192];
			for (var axis = 0; axis < 3; axis++)
				for (var i = 0; i < 32; i++)
				{
					var a = i * Mathf.PI * 2 / 32;
					var p = new Vector3(Mathf.Cos(a) * .5f, Mathf.Sin(a) * .5f, 0);
					vertices[axis * 32 + i] =
						axis == 0 ? p : axis == 1 ? new Vector3(p.x, 0, p.y) : new Vector3(0, p.x, p.y);
					indices[(axis * 32 + i) * 2] = axis * 32 + i;
					indices[(axis * 32 + i) * 2 + 1] = axis * 32 + (i + 1) % 32;
				}

			var colors = new Color32[vertices.Length];
			for (var i = 0; i < colors.Length; i++) colors[i] = new Color32(255, 255, 255, 255);
			mesh.vertices = vertices;
			mesh.colors32 = colors;
			mesh.SetIndices(indices, MeshTopology.Lines, 0);
			return mesh;
		}

		private static void Release(Object resource)
		{
			if (resource == null) return;
			if (Application.isPlaying) Object.Destroy(resource);
			else Object.DestroyImmediate(resource);
		}

		private sealed class Batch
		{
			public readonly Vector4[] Colors = new Vector4[1023];
			public readonly Vector4[] Effects = new Vector4[1023];
			public readonly Matrix4x4[] Matrices = new Matrix4x4[1023];
			public readonly MaterialPropertyBlock Properties = new();
			public int Count;
		}
	}
}
