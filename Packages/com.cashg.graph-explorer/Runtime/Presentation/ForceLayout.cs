using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cashg.GraphExplorer
{
	/// <summary>Bounded, semi-implicit Euler integration with Barnes-Hut octree repulsion.</summary>
	public sealed class ForceLayout
	{
		private readonly Dictionary<string, Vector3> groupAnchors = new();
		private readonly Dictionary<string, int> indices = new();
		private readonly GraphPresentationProfile profile;
		/// <summary>Gets or sets the layout arrangement mode.</summary>
		public LayoutMode Mode;
		/// <summary>Gets or sets whether force integration is paused.</summary>
		public bool Paused;
		private int cellCount;
		private Cell[] cells = new Cell[1024];
		private int[] degrees = Array.Empty<int>();
		private Vector3[] forces = Array.Empty<Vector3>();
		private IGraphScene graph;
		private bool hasPositionSample;
		private float lastPositionSampleInterval = 1f;
		private IGraphNode[] nodes = Array.Empty<IGraphNode>();
		private float positionSampleAccumulator;
		private Vector3[] positions = Array.Empty<Vector3>();
		private int revision = -1;

		/// <summary>Creates a force layout using optional domain presentation policies.</summary>
		/// <param name="profile">Profile supplying grouping and lane policies.</param>
		public ForceLayout(GraphPresentationProfile profile = null)
		{
			this.profile = profile ?? new GraphPresentationProfile();
		}

		/// <summary>Gets the current total layout energy.</summary>
		public float Energy { get; private set; }
		/// <summary>Gets the current integration temperature.</summary>
		public float Temperature { get; private set; } = 1;

		/// <summary>Restores the integration temperature to its initial value.</summary>
		public void Reheat()
		{
			Temperature = 1;
		}

		/// <summary>Start a fresh deterministic layout for a historical graph state.</summary>
		public void ResetPositions()
		{
			if (graph == null) return;

			// Keep pinned coordinates while resetting free nodes and clearing residual motion.
			foreach (var node in graph.Nodes.Values)
			{
				if (!node.Pinned) node.Position = GraphViewState.InitialPosition(node.Id);
				node.Velocity = Vector3.zero;
			}

			revision = -1;
			Bind(graph);
			Energy = 0;
			Paused = false;
			Reheat();
		}

		/// <summary>Rebinds cached layout structures when the scene revision changes.</summary>
		/// <param name="value">Scene to lay out.</param>
		public void Bind(IGraphScene value)
		{
			if (graph != value)
			{
				graph = value;
				revision = -1;
				Reheat();
			}

			if (revision == graph.Revision) return;

			// Rebuild dense simulation arrays and node-ID lookup only when scene topology changes.
			nodes = new List<IGraphNode>(graph.Nodes.Values).ToArray();
			forces = new Vector3[nodes.Length];
			positions = new Vector3[nodes.Length];
			degrees = new int[nodes.Length];
			indices.Clear();
			groupAnchors.Clear();
			var groups = 0;
			foreach (var node in nodes)
				if (profile.IsGroupAnchor?.Invoke(node) == true)
					groups++;
			var groupIndex = 0;

			// Place group anchors around a ring while caching degrees for hub spring normalization.
			for (var i = 0; i < nodes.Length; i++)
			{
				indices[nodes[i].Id] = i;
				degrees[i] = nodes[i].Neighbors.Count;
				if (profile.IsGroupAnchor?.Invoke(nodes[i]) == true)
				{
					var angle = groupIndex++ * Mathf.PI * 2 / Mathf.Max(1, groups);
					groupAnchors[nodes[i].Id] =
						new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * Mathf.Max(14, groups * 3);
				}
			}

			revision = graph.Revision;
			Reheat();
		}

		private int NewCell(Vector3 center, float half)
		{
			if (cellCount == cells.Length) Array.Resize(ref cells, cells.Length * 2);
			var index = cellCount++;
			cells[index] = new Cell { Center = center, Half = half, Body = -1, Children = -1 };
			return index;
		}

		private int Octant(Vector3 point, Vector3 center)
		{
			return (point.x >= center.x ? 1 : 0) | (point.y >= center.y ? 2 : 0) | (point.z >= center.z ? 4 : 0);
		}

		private void Insert(int index, int body, int depth)
		{
			var cell = cells[index];
			cells[index].Count++;
			cells[index].Sum += positions[body];
			if (cell.Count == 0)
			{
				cells[index].Body = body;
				return;
			}

			if (depth >= 18) return; // Coincident points aggregate rather than recurse indefinitely.
			if (cell.Children < 0)
			{
				// Split a populated leaf and reinsert its prior body before routing the new body.
				var first = cellCount;
				for (var octant = 0; octant < 8; octant++)
					NewCell(
						cell.Center + new Vector3((octant & 1) != 0 ? 1 : -1, (octant & 2) != 0 ? 1 : -1,
												  (octant & 4) != 0 ? 1 : -1) * cell.Half * .5f, cell.Half * .5f);
				cells[index].Children = first;
				cells[index].Body = -1;
				Insert(first + Octant(positions[cell.Body], cell.Center), cell.Body, depth + 1);
			}

			Insert(cells[index].Children + Octant(positions[body], cell.Center), body, depth + 1);
		}

		private Vector3 Repulsion(int index, int body)
		{
			var cell = cells[index];
			if (cell.Count == 0 || (cell.Count == 1 && cell.Body == body)) return Vector3.zero;
			var delta = positions[body] - cell.Sum / cell.Count;
			if (delta.sqrMagnitude < .00001f)
				delta = new Vector3(Mathf.Sin(body * 1.7f), Mathf.Cos(body * 3.1f), Mathf.Sin(body * 2.3f)) * .05f;
			var squared = delta.sqrMagnitude + 1.5f; // Softening bounds forces as distance tends to zero.
			var contains = Mathf.Abs(positions[body].x - cell.Center.x) <= cell.Half &&
						   Mathf.Abs(positions[body].y - cell.Center.y) <= cell.Half &&
						   Mathf.Abs(positions[body].z - cell.Center.z) <= cell.Half;

			// Approximate distant cells by their centroid; descend cells containing the body itself.
			if (cell.Children < 0 || (!contains && cell.Half * 2 / Mathf.Sqrt(squared) < .75f))
				return delta / Mathf.Sqrt(squared) * (65 * cell.Count / squared);
			var force = Vector3.zero;
			for (var i = 0; i < 8; i++) force += Repulsion(cell.Children + i, body);
			return force;
		}

		/// <summary>Advances force integration by a bounded timestep.</summary>
		/// <param name="timestep">Elapsed time for the integration step.</param>
		public void Step(float timestep)
		{
			// Refresh graph arrays only when topology or node ordering requires it.
			using var measurement = GraphDiagnostics.Layout.Auto();
			if (graph == null) return;
			Bind(graph);
			var timeline = graph.Document.Timeline;
			if (timeline.IsHistorical || Paused || Temperature < .025f || nodes.Length == 0)
			{
				positionSampleAccumulator = 0;
				hasPositionSample = false;
				return;
			}

			// Position history is optional and must not affect live force-layout
			// behavior. Reset the recorder while unavailable, then keep simulating.
			var canRecordPositions = timeline.Enabled && timeline.PositionRecordingAvailable &&
									 timeline.PositionRecordingEnabled;
			if (!canRecordPositions)
			{
				positionSampleAccumulator = 0;
				hasPositionSample = false;
			}

			if (!Mathf.Approximately(lastPositionSampleInterval, timeline.PositionSampleIntervalSeconds))
			{
				lastPositionSampleInterval = timeline.PositionSampleIntervalSeconds;
				positionSampleAccumulator = 0;
				hasPositionSample = false;
			}

			var dt = Mathf.Clamp(timestep, .001f, 1f / 30);
			// Read the view once per step. Barnes-Hut visits bodies many times; those
			// visits operate on contiguous value arrays rather than host interface calls.
			for (var i = 0; i < nodes.Length; i++) positions[i] = nodes[i].Position;
			var bounds = new Bounds(positions[0], Vector3.one);
			foreach (var position in positions) bounds.Encapsulate(position);
			cellCount = 0;

			// Build a fresh spatial tree from current positions for this step's repulsion forces.
			NewCell(bounds.center, Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z)) + 1);
			for (var i = 0; i < nodes.Length; i++) Insert(0, i, 0);
			for (var i = 0; i < nodes.Length; i++) forces[i] = Repulsion(0, i) - positions[i] * .018f;

			// Add relation-weighted spring forces after repulsion and gentle centering.
			foreach (var edge in graph.Edges.Values)
			{
				int a = indices[edge.From], b = indices[edge.To];
				var delta = positions[b] - positions[a];
				var distance = Mathf.Max(.01f, delta.magnitude);
				var strength = graph.Schema.Relations.TryGetValue(edge.Relation, out var relation)
					? relation.SpringStrength
					: .35f;
				var force = delta / distance * (distance - 7) * strength;
				// Hubs should not collapse their entire neighborhood into a single point.
				forces[a] += force / Mathf.Sqrt(Mathf.Max(1, degrees[a]));
				forces[b] -= force / Mathf.Sqrt(Mathf.Max(1, degrees[b]));
			}

			Energy = 0;

			// Apply mode constraints and integrate damped velocity, leaving pinned nodes stationary.
			for (var i = 0; i < nodes.Length; i++)
			{
				var node = nodes[i];
				var position = positions[i];
				if (node.Pinned)
				{
					node.Velocity = Vector3.zero;
					continue;
				}

				if (Mode == LayoutMode.Flat) forces[i].z -= position.z * 8;
				if (Mode == LayoutMode.Groups)
				{
					var group = profile.GroupId?.Invoke(node);
					if (group != null && groupAnchors.TryGetValue(group, out var anchor))
						forces[i] += (anchor - position) * (profile.GroupStrength?.Invoke(node) ?? 1f);
				}

				if (Mode == LayoutMode.Lanes && profile.Lane?.Invoke(node) is float lane)
				{
					forces[i].x += ((lane - profile.LaneCenter) * profile.LaneSpacing - position.x) * 3;
					forces[i].y -= position.y * .5f;
				}

				node.Velocity =
					Vector3.ClampMagnitude(
						(node.Velocity + Vector3.ClampMagnitude(forces[i], 140) * dt) * Mathf.Exp(-3 * dt), 18);
				position += node.Velocity * dt;
				if (Mode == LayoutMode.Flat && Mathf.Abs(position.z) < .03f) position.z = 0;
				node.Position = position;
				Energy += node.Velocity.sqrMagnitude;
			}

			Energy /= Mathf.Max(1, nodes.Length);
			Temperature *= Mathf.Exp(-dt * .12f);
			if (Temperature < .35f && Energy < .001f) Temperature = 0;
			if (canRecordPositions)
			{
				// Sample canonical positions at the configured cadence, including movement of collapsed groups.
				positionSampleAccumulator += Mathf.Max(0, timestep);
				if (!hasPositionSample || positionSampleAccumulator >= timeline.PositionSampleIntervalSeconds)
				{
					var samples = graph is GraphClusterScene clustered
						? clustered.CanonicalPositions()
						: CaptureCanonicalPositions(graph);
					graph.Document.RecordPositionFrame(samples);
					positionSampleAccumulator = 0;
					hasPositionSample = true;
				}
			}
		}

		private static IReadOnlyDictionary<string, Vector3> CaptureCanonicalPositions(IGraphScene scene)
		{
			var positions = new SortedDictionary<string, Vector3>(StringComparer.Ordinal);
			foreach (var id in scene.Document.Nodes.Keys) positions[id] = scene.View.GetOrCreate(id).Position;
			return positions;
		}

		private struct Cell
		{
			public Vector3 Center, Sum;
			public float Half;
			public int Count, Body, Children;
		}
	}
}
