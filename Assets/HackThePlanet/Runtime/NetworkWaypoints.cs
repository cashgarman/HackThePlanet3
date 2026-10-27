using System;
using System.Collections.Generic;
using System.Linq;
using Cashg.GraphExplorer;
using UnityEngine;

namespace HackThePlanet
{
	/// <summary>Player-placed planning marker on a specific contract network instance.</summary>
	[Serializable]
	public sealed class NetworkWaypoint
	{
		/// <summary>Contract id and target node id this marker annotates.</summary>
		public string contractId, nodeId, label = "";

		/// <summary>Contract seed and palette index into <see cref="GameSimulation.WaypointColors" />.</summary>
		public int seed, color;

		/// <summary>When true, route preview highlights the path to this node.</summary>
		public bool showPath;
	}

	public sealed partial class GameSimulation
	{
		// Fixed palette so each waypoint tier has a stable marker color.
		/// <summary>Palette colors selectable for waypoint markers by tier.</summary>
		public static readonly Color[] WaypointColors =
			{ new(0.2f, 1f, .9f), new(1f, .65f, .2f), new(.85f, .5f, 1f), new(1f, .4f, .6f), Color.white };

		/// <summary>Gets the maximum waypoint features unlocked from owned research tiers (0–4).</summary>
		public int WaypointTier =>
			Mathf.Clamp(
				Catalog.Data.technologies.Where(t => Has(t.id)).Select(t => t.waypointTier).DefaultIfEmpty(0).Max(), 0,
				4);

		/// <summary>Lists waypoints placed on the given contract instance up to <see cref="WaypointTier" />.</summary>
		/// <param name="job">Contract whose id and seed scope stored markers.</param>
		/// <returns>Matching waypoints in career storage order.</returns>
		public IReadOnlyList<NetworkWaypoint> Waypoints(Contract job)
		{
			// Filter stored markers to this contract instance and seed, capped at the unlocked tier.
			return Career.waypoints.Where(w => w.contractId == job.id && w.seed == job.seed).Take(WaypointTier)
						 .ToArray();
		}

		// Planning is tied to the exact generated network, including recurring-contract seeds.
		/// <summary>Places a waypoint on a node when planning is allowed and capacity remains.</summary>
		/// <param name="job">Contract being planned; must have intel and meet win requirements.</param>
		/// <param name="nodeId">Target node on the generated network for this seed.</param>
		/// <returns>True when a new marker was stored.</returns>
		public bool PlaceWaypoint(Contract job, string nodeId)
		{
			// Reject planning during an operation, with no unlocked tier, or for an unknown or intel-less contract.
			if (Active || WaypointTier == 0 || job == null || job.Intel == ContractIntel.None ||
				Catalog.Contract(job.id) == null)
				return false;

			// Reject contracts already completed or without enough prior wins.
			if (Career.completed.Contains(job.id) || Career.completed.Count < job.requiredWins)
				return false;

			// Generate the exact network for this seed so the node id can be validated.
			var network = NetworkGenerator.Generate(job, Career, Catalog);

			// Reject node ids that are not on the generated network.
			if (!network.nodes.Any(n => n.id == nodeId))
				return false;

			// Collect existing markers for this contract instance and seed.
			var existing = Career.waypoints.Where(w => w.contractId == job.id && w.seed == job.seed).ToArray();

			// Reject when the tier's marker capacity is full or this node is already marked.
			if (existing.Length >= WaypointTier || existing.Any(w => w.nodeId == nodeId))
				return false;

			// Replace plans for an expired recurring network rather than accumulating them forever.
			Career.waypoints.RemoveAll(w => w.contractId == job.id && w.seed != job.seed);

			// Store the new marker for this node.
			Career.waypoints.Add(new NetworkWaypoint { contractId = job.id, seed = job.seed, nodeId = nodeId });

			// Notify listeners that the career changed.
			Touch();

			return true;
		}

		/// <summary>Removes a waypoint from the contract instance when not in an active operation.</summary>
		/// <param name="job">Contract whose id and seed scope the marker.</param>
		/// <param name="nodeId">Node id to clear.</param>
		/// <returns>True when a marker was removed.</returns>
		public bool RemoveWaypoint(Contract job, string nodeId)
		{
			// Reject removal during an operation or for a missing contract.
			if (Active || job == null)
				return false;

			// Remove the marker matching this contract instance and node.
			var removed =
				Career.waypoints.RemoveAll(w => w.contractId == job.id && w.seed == job.seed && w.nodeId == nodeId) > 0;

			// Notify listeners only when a marker was actually removed.
			if (removed)
				Touch();

			return removed;
		}

		/// <summary>Updates waypoint appearance and path preview according to unlocked waypoint tier.</summary>
		/// <param name="job">Contract instance owning the marker.</param>
		/// <param name="nodeId">Node id of the marker to edit.</param>
		/// <param name="color">Palette index; requires tier 2+ to change from the current color.</param>
		/// <param name="label">Custom label trimmed to 40 characters; requires tier 3+ to change.</param>
		/// <param name="showPath">Path highlight flag; requires tier 4 to enable (only one path shown at a time).</param>
		/// <returns>True when the marker was updated.</returns>
		public bool EditWaypoint(Contract job, string nodeId, int color, string label, bool showPath)
		{
			// Reject edits during an operation, for missing contracts, or for out-of-range palette indices.
			if (Active || job == null || color < 0 || color >= WaypointColors.Length)
				return false;

			// Find the marker on this node within the contract instance.
			var waypoint = Waypoints(job).FirstOrDefault(w => w.nodeId == nodeId);

			// Reject when no marker exists on this node.
			if (waypoint == null)
				return false;

			// Reject changes that require a waypoint tier not yet unlocked.
			if ((WaypointTier < 2 && color != waypoint.color) ||
				(WaypointTier < 3 && (label ?? "") != waypoint.label) ||
				(WaypointTier < 4 && showPath != waypoint.showPath))
				return false;

			// Store the palette color and the trimmed, length-limited label.
			waypoint.color = color;
			waypoint.label = (label ?? "").Trim();

			if (waypoint.label.Length > 40)
				waypoint.label = waypoint.label.Substring(0, 40);

			// Clear the path flag on other markers so only one route is previewed at a time.
			if (showPath)
				foreach (var other in Career.waypoints.Where(w => w.contractId == job.id && w.seed == job.seed))
					other.showPath = false;

			// Apply the path flag to this marker.
			waypoint.showPath = showPath;

			// Notify listeners that the career changed.
			Touch();

			return true;
		}

		/// <summary>Finds a shortest path between two nodes on an undirected link graph for waypoint routing.</summary>
		/// <param name="nodes">Network nodes to include.</param>
		/// <param name="links">Links treated as bidirectional.</param>
		/// <param name="entry">Start node id (typically the gateway).</param>
		/// <param name="target">Destination node id.</param>
		/// <returns>Path result from GraphExplorer pathfinding.</returns>
		public static GraphPathResult WaypointPath(List<NetNode> nodes, List<NetLink> links, string entry,
												   string target)
		{
			// Create the route document and an empty schema for system nodes and connection relations.
			var document = new GraphDocument("waypoint-route", "Waypoint route");
			var schema = new GraphSchema();

			// Register the system node type and the connection relation.
			schema.Register(new GraphNodeTypeDefinition("system", "System", "default", Color.white));
			schema.Register(new GraphRelationDefinition("link", "Connection", Color.white));

			// Add every network node to the route graph.
			foreach (var node in nodes)
				document.UpsertNode(new GraphNodeRecord(node.id, "system", ""));

			// Add each link to the route graph.
			foreach (var link in links)
			{
				// Add forward and reverse edges so routing treats links as undirected.
				document.UpsertEdge(new GraphEdgeRecord(link.a + ">" + link.b, link.a, link.b, "link"));
				document.UpsertEdge(new GraphEdgeRecord(link.b + ">" + link.a, link.b, link.a, "link"));
			}

			// Run shortest-path search from the entry node to the target.
			return GraphPathfinder.Find(new GraphScene(document, schema), entry, target);
		}
	}
}