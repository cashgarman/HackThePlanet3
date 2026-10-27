using System;
using UnityEngine;

namespace HackThePlanet
{
	/// <summary>Root authoring asset listing every gameplay definition bundle for a data pack.</summary>
	[CreateAssetMenu(menuName = "Hack The Planet/Game", fileName = "Game")]
	public sealed class GameDefinition : GameplayDefinition
	{
		/// <summary>Employer organizations referenced by contracts.</summary>
		public CompanyDefinition[] companies = Array.Empty<CompanyDefinition>();

		/// <summary>Missions and recurring jobs offered to the player.</summary>
		public ContractDefinition[] contracts = Array.Empty<ContractDefinition>();

		/// <summary>Research nodes in the technology tree.</summary>
		public TechnologyDefinition[] technologies = Array.Empty<TechnologyDefinition>();

		/// <summary>Workstation tools the player can run against network nodes.</summary>
		public ActionDefinition[] actions = Array.Empty<ActionDefinition>();

		/// <summary>Visual and rule templates for network node roles.</summary>
		public NodeTypeDefinition[] nodeTypes = Array.Empty<NodeTypeDefinition>();

		/// <summary>Authored or procedurally generated network templates.</summary>
		public NetworkDefinition[] networks = Array.Empty<NetworkDefinition>();

		/// <summary>Rig placement rules, starter build, and economy limits.</summary>
		public RigRulesDefinition rig;

		/// <summary>Installable rig frame definitions keyed by tier.</summary>
		public RigChassisDefinition[] chassis = Array.Empty<RigChassisDefinition>();

		/// <summary>Installable rig module definitions.</summary>
		public RigModuleDefinition[] modules = Array.Empty<RigModuleDefinition>();

		/// <summary>Upgradeable hardware lines shown in the shop.</summary>
		public HardwareDefinition[] hardware = Array.Empty<HardwareDefinition>();

		/// <summary>Timed or persistent status effects applied during operations.</summary>
		public StatusDefinition[] statuses = Array.Empty<StatusDefinition>();

		/// <summary>Archive and quiet-run payout tables.</summary>
		public RewardDefinition rewards;

		/// <summary>Starting career stats, trace limits, and simulation tuning.</summary>
		public CareerDefinition career;

		/// <summary>Global stat modifiers wired into the root catalog.</summary>
		public Modifier[] modifiers = Array.Empty<Modifier>();

		/// <summary>Event hooks evaluated by the simulation outside individual actions.</summary>
		public Trigger[] triggers = Array.Empty<Trigger>();
	}
}