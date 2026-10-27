using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HackThePlanet
{
	/// <summary>Icon glyph assigned to a network node role.</summary>
	public enum NodeKind
	{
		/// <summary>Player entry point on the target network.</summary>
		Gateway,

		/// <summary>General-purpose host along a route.</summary>
		Workstation,

		/// <summary>Routing hop with minimal services.</summary>
		Relay,

		/// <summary>Defensive filter that blocks or slows intrusion.</summary>
		Firewall,

		/// <summary>Sensor that raises trace or reveals activity.</summary>
		Scanner,

		/// <summary>Data store or archive target.</summary>
		Database,

		/// <summary>Central security coordination node.</summary>
		SecurityHub
	}

	/// <summary>High-level contract objective category shown in mission UI.</summary>
	public enum JobKind
	{
		/// <summary>Exfiltrate or duplicate data.</summary>
		Copy,

		/// <summary>Change systems or records on the network.</summary>
		Alter,

		/// <summary>Gather intelligence without primary exfiltration.</summary>
		Investigate,

		/// <summary>Degrade or disable target infrastructure.</summary>
		Disrupt
	}

	/// <summary>Legacy grouping for classic tool families in compatibility adapters.</summary>
	public enum ToolKind
	{
		/// <summary>Discover node properties.</summary>
		Scan,

		/// <summary>Gain control of a system.</summary>
		Hack,

		/// <summary>Transfer payload or archive data.</summary>
		Download,

		/// <summary>Degrade or disable a target.</summary>
		Disrupt,

		/// <summary>Route traffic or link separated segments.</summary>
		Bridge,

		/// <summary>Install persistent access.</summary>
		Backdoor
	}

	/// <summary>How much network detail is visible before connecting on a contract.</summary>
	public enum ContractIntel
	{
		/// <summary>System types and objectives visible pre-connect.</summary>
		Full,

		/// <summary>Topology only; node roles stay hidden until discovery.</summary>
		Topology,

		/// <summary>No pre-connect map intel.</summary>
		None
	}

	/// <summary>Runtime node state on a generated or live network map.</summary>
	[Serializable]
	public sealed class NetNode
	{
		/// <summary>Stable node identifier within the network instance.</summary>
		public string id, label, detail, typeId;

		/// <summary>Presentation glyph derived from the node type.</summary>
		public NodeKind kind;

		/// <summary>Grid column and row for legacy 2D layout views.</summary>
		public int security, column, row;

		/// <summary>Planar layout coordinates and simulation fields.</summary>
		public float x, y, suppressed, storage;

		/// <summary>Discovery and control flags for fog-of-war rendering.</summary>
		public bool known, controlled, scanned, collected;

		/// <summary>Contract objective, bonus archive, or payload sizing markers.</summary>
		public bool objective, bonus, payload;

		/// <summary>World-space position used by the 3D graph viewport.</summary>
		public Vector3 position;

		/// <summary>Accent color copied from the node type definition.</summary>
		public Color color;

		/// <summary>Optional lore fragment id attached to bonus or story nodes.</summary>
		public string loreId;
	}

	/// <summary>Undirected edge between two network nodes identified by id.</summary>
	[Serializable]
	public sealed class NetLink
	{
		/// <summary>Endpoint node ids; order is not significant for traversal.</summary>
		public string a, b;

		/// <summary>Creates a link between two node ids.</summary>
		/// <param name="from">First endpoint id.</param>
		/// <param name="to">Second endpoint id.</param>
		public NetLink(string from, string to)
		{
			// Store both endpoint ids.
			a = from;
			b = to;
		}
	}

	/// <summary>Resolved contract offer with evaluated payouts and intel tier derived from difficulty.</summary>
	[Serializable]
	public sealed class Contract
	{
		/// <summary>Mission identity and presentation copy.</summary>
		public string id, title, client, company, tag, description, objective, condition, aftermath;

		/// <summary>Objective category for UI grouping.</summary>
		public JobKind kind;

		/// <summary>Evaluated rewards, difficulty, and procedural seed.</summary>
		public int credits, xp, difficulty, seed, requiredWins;

		/// <summary>Trace budget before the run fails the quiet limit.</summary>
		public float quietLimit;

		/// <summary>When true, the job can repeat after win requirements are met.</summary>
		public bool recurring;

		/// <summary>Network template id used when generating the operation map.</summary>
		public string networkId;

		/// <summary>Lore entries recovered during or after the contract.</summary>
		public LoreFragment[] lore = Array.Empty<LoreFragment>();

		/// <summary>Gets pre-connect intel tier from <see cref="difficulty" />.</summary>
		public ContractIntel Intel => difficulty <= 1 ? ContractIntel.Full :
			difficulty == 2 ? ContractIntel.Topology : ContractIntel.None;

		/// <summary>Gets the difficulty label shown in contract browsers.</summary>
		public string DifficultyLabel =>
			Intel == ContractIntel.Full ? "EASY" : Intel == ContractIntel.Topology ? "MEDIUM" : "HARD";

		/// <summary>Gets the payout multiplier applied to <see cref="credits" /> and <see cref="xp" /> from intel tier.</summary>
		public float RewardMultiplier => Intel == ContractIntel.Full ? 1f : Intel == ContractIntel.Topology ? 1.5f : 2f;

		/// <summary>Gets player-facing text explaining what intel is available pre-connect.</summary>
		public string IntelDescription => Intel == ContractIntel.Full
			? "Full network intel. System types and objectives are visible before connecting."
			: Intel == ContractIntel.Topology
				? "Topology only. All systems are unknown until you connect and discover them."
				: "No intel available. Connect and discover the network from your gateway.";
	}

	/// <summary>Research node snapshot for the technology tree UI.</summary>
	[Serializable]
	public sealed class Technology
	{
		/// <summary>Technology id, display title, branch, and formatted description.</summary>
		public string id, title, branch, description;

		/// <summary>XP cost to unlock when prerequisites are satisfied.</summary>
		public int cost;

		/// <summary>Parent technology ids required before purchase.</summary>
		public string[] parents;

		/// <summary>Action ids granted when this technology is owned.</summary>
		public string[] grants = Array.Empty<string>();

		/// <summary>When true, the node is an equippable ability rather than a passive.</summary>
		public bool special;

		/// <summary>Layout position on the research graph canvas.</summary>
		public Vector2 position;

		/// <summary>Creates a technology descriptor for tree rendering.</summary>
		/// <param name="id">Stable technology id.</param>
		/// <param name="title">Display title.</param>
		/// <param name="branch">Research branch name used for coloring.</param>
		/// <param name="cost">XP unlock cost.</param>
		/// <param name="x">Canvas X coordinate.</param>
		/// <param name="y">Canvas Y coordinate.</param>
		/// <param name="description">Formatted description text.</param>
		/// <param name="special">Whether the node is an equippable ability.</param>
		/// <param name="parents">Required parent technology ids.</param>
		public Technology(string id, string title, string branch, int cost, float x, float y,
						  string description, bool special = false, params string[] parents)
		{
			// Copy the descriptor fields and layout position.
			this.id = id;
			this.title = title;
			this.branch = branch;
			this.cost = cost;
			position = new Vector2(x, y);
			this.description = description;
			this.special = special;
			this.parents = parents;
		}
	}

	// Compatibility metadata facade. Gameplay uses the catalog injected into its simulation.
	/// <summary>Static facade over <see cref="GameCatalog.Default" /> for legacy callers and editor tooling.</summary>
	public static class GameContent
	{
		/// <summary>Gets the active validated gameplay catalog.</summary>
		public static GameCatalog Catalog => GameCatalog.Default;

		/// <summary>Gets company titles from the active catalog.</summary>
		public static string[] Companies => Catalog.Companies.ToArray();

		/// <summary>Gets non-recurring story contracts for a fresh career.</summary>
		public static Contract[] Story => Catalog.Contracts(new Career()).Where(x => !x.recurring).ToArray();

		/// <summary>Gets all technologies from the active catalog.</summary>
		public static Technology[] Tech => Catalog.Technologies.ToArray();

		/// <summary>Finds a technology by id, or null when missing.</summary>
		/// <param name="id">Technology id to resolve.</param>
		/// <returns>The matching <see cref="Technology" />, or null.</returns>
		public static Technology FindTech(string id)
		{
			return Catalog.Technologies.FirstOrDefault(x => x.id == id);
		}

		/// <summary>Lists contracts available to the given career.</summary>
		/// <param name="career">Player progress for gating and formula evaluation.</param>
		/// <returns>Mutable list of resolved contracts.</returns>
		public static List<Contract> Contracts(Career career)
		{
			return Catalog.Contracts(career).ToList();
		}

		/// <summary>Gets the default description for a node kind from catalog node types.</summary>
		/// <param name="glyph">Node kind to describe.</param>
		/// <returns>The matching type description, or empty when none is registered.</returns>
		public static string Description(NodeKind glyph)
		{
			return Catalog.NodeTypes.FirstOrDefault(x => x.glyph == glyph)?.description ?? string.Empty;
		}
	}
}