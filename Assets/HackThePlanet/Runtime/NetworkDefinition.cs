using System;
using UnityEngine;

namespace HackThePlanet
{
	/// <summary>Whether a network uses fixed authored topology or parallel-route generation.</summary>
	public enum NetworkLayout
	{
		/// <summary>Nodes and conditional links are defined explicitly in data.</summary>
		Authored,

		/// <summary>Routes, tiers, and filler nodes are assembled procedurally from <see cref="ParallelRouteLayout" />.</summary>
		ParallelRoutes
	}

	/// <summary>Node template embedded in a <see cref="NetworkDefinition" /> before runtime generation.</summary>
	[Serializable]
	public sealed class NetworkNode
	{
		/// <summary>Stable id and player-facing label and detail override.</summary>
		public string id, label, detail;

		/// <summary>Node type that supplies glyph, color, and base security formula.</summary>
		public NodeTypeDefinition type;

		/// <summary>Legacy grid coordinates and planar x/y for 2D canvases.</summary>
		public int column, row;

		/// <summary>Planar layout coordinates.</summary>
		public float x, y;

		/// <summary>World position for 3D graph layout.</summary>
		public Vector3 position;

		/// <summary>Initial fog-of-war and control flags on the template.</summary>
		public bool known, controlled, bonus;

		/// <summary>Index into contract lore arrays; negative means no lore binding.</summary>
		public int loreIndex = -1;

		/// <summary>Conditional modifiers applied when evaluating node security at generation time.</summary>
		public Modifier[] securityModifiers = Array.Empty<Modifier>();
	}

	/// <summary>Conditional link between two template nodes in an authored network.</summary>
	[Serializable]
	public sealed class NetworkEdge
	{
		/// <summary>Endpoint node ids.</summary>
		public string a, b;

		/// <summary>Condition that must pass for the link to exist in a generated instance.</summary>
		[SerializeReference] public Condition when = new AlwaysCondition();
	}

	/// <summary>Difficulty tier sizing for parallel-route network generation.</summary>
	[Serializable]
	public sealed class RouteTier
	{
		/// <summary>Total nodes to materialize for this tier, including reserved identities.</summary>
		public int nodeCount;

		/// <summary>Per-lane hop counts; index zero must match the direct route length.</summary>
		public int[] lengths = Array.Empty<int>();

		/// <summary>Scanner nodes placed on each non-direct lane per tier.</summary>
		public int sensorsPerLane = 1;
	}

	/// <summary>Authoring data for procedurally wiring parallel ingress lanes and filler nodes.</summary>
	[Serializable]
	public sealed class ParallelRouteLayout
	{
		/// <summary>Anchor node ids for entrance, objective destination, and sensor attachment.</summary>
		public string entrance, destination, sensorAttachment;

		/// <summary>Ordered ids for the fast direct path, sensors, side targets, and archives.</summary>
		public string[] directRoute = Array.Empty<string>(),
						sensors = Array.Empty<string>(),
						secondaryTargets = Array.Empty<string>(),
						archives = Array.Empty<string>();

		/// <summary>Node ids excluded from the shuffled filler pool.</summary>
		public string[] reserved = Array.Empty<string>();

		/// <summary>Per-difficulty tier definitions controlling lane lengths and pool size.</summary>
		public RouteTier[] tiers = Array.Empty<RouteTier>();

		/// <summary>Node types used when assigning roles along generated lanes.</summary>
		public NodeTypeDefinition relay, workstation, scanner, archive;

		/// <summary>Extra fixed identities merged before filler nodes are added.</summary>
		public string[] extraIds = Array.Empty<string>();

		/// <summary>Labels parallel to <see cref="extraIds" />.</summary>
		public string[] extraLabels = Array.Empty<string>();

		/// <summary>Types parallel to <see cref="extraIds" />.</summary>
		public NodeTypeDefinition[] extraTypes = Array.Empty<NodeTypeDefinition>();

		/// <summary>Prefix and labels for auto-generated relay filler nodes.</summary>
		public string fillerPrefix = "extra-",
					  fillerLabel = "Maintenance relay ",
					  safeLaneLabel = "Maintenance",
					  transitLabel = "Transit",
					  sensorLabel = "Transit sensor";

		/// <summary>Numeric offset applied when numbering filler relay labels.</summary>
		public int fillerLabelOffset = 24;

		/// <summary>World positions for entry gateway, lane entrance, and final destination.</summary>
		public Vector3 entryPosition = new(-19, -1, -2),
					   entrancePosition = new(-14, 0, 0),
					   destinationPosition = new(14, 0, 0);

		/// <summary>Lane geometry, jitter, spacing, and separation tuning for layout.</summary>
		public float radius = 8,
					 laneRadius = 1.5f,
					 jitter = .45f,
					 satelliteRadius = 3.5f,
					 satelliteGrowth = .025f,
					 minimumSpacing = 2.6f,
					 separationSpacing = 2.65f,
					 separationForce = .51f;

		/// <summary>Placement and crossover parameters for lane graph assembly.</summary>
		public int placementAttempts = 1000,
				   separationPasses = 80,
				   crossoverFromLane = 1,
				   crossoverToLane = 2,
				   crossoverStep = 4;

		/// <summary>Security formulas for direct-route nodes versus transit lanes.</summary>
		[SerializeReference] public Number directSecurity = new ConstantNumber(2);

		/// <summary>Security formula for generated workstation and relay hops.</summary>
		[SerializeReference] public Number routeSecurity = new ConstantNumber(1);

		/// <summary>Chooses workstation versus relay typing along non-direct lanes.</summary>
		[SerializeReference] public Condition workstationWhen = new AlwaysCondition();
	}

	/// <summary>Authoring asset describing a mission network template and optional procedural routes.</summary>
	[CreateAssetMenu(menuName = "Hack The Planet/Network")]
	public sealed class NetworkDefinition : GameplayDefinition
	{
		/// <summary>Gateway id, default selection id, and contract preview focus id.</summary>
		public string entryNode, preferredNode, previewNode;

		/// <summary>Whether the network uses fixed links or parallel-route generation.</summary>
		public NetworkLayout layout;

		/// <summary>Storage formulas for payload objectives and bonus archive nodes.</summary>
		[SerializeReference]
		public Number payloadStorage = new ConstantNumber(20), archiveStorage = new ConstantNumber(28);

		/// <summary>Template nodes copied or expanded when a contract instance is built.</summary>
		public NetworkNode[] nodes = Array.Empty<NetworkNode>();

		/// <summary>Authored edges evaluated with <see cref="NetworkEdge.when" /> at generation time.</summary>
		public NetworkEdge[] links = Array.Empty<NetworkEdge>();

		/// <summary>Parallel-route configuration used when <see cref="layout" /> is <see cref="NetworkLayout.ParallelRoutes" />.</summary>
		public ParallelRouteLayout routes = new();
	}
}