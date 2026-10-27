using System;
using UnityEngine;

namespace HackThePlanet
{
	/// <summary>Extra requirements applied when a specific action is used on a contract.</summary>
	[Serializable]
	public sealed class ContractActionRule
	{
		/// <summary>Gets or sets the action this rule applies to.</summary>
		public ActionDefinition action;

		/// <summary>Gets or sets requirements that must pass to use the action.</summary>
		public Requirement[] requirements = Array.Empty<Requirement>();
	}

	/// <summary>Single contract objective tied to a network node and completion condition.</summary>
	[Serializable]
	public sealed class ObjectiveDefinition
	{
		/// <summary>Gets or sets the target node id, display label, and action button label.</summary>
		public string nodeId, label, actionLabel;

		/// <summary>Gets or sets the action executed toward this objective.</summary>
		public ActionDefinition action;

		/// <summary>Gets or sets whether this objective counts as payload delivery.</summary>
		public bool payload;

		/// <summary>Gets or sets the condition that marks the objective complete (default collected node state).</summary>
		[SerializeReference] public Condition complete = new NodeStateCondition { state = NodeState.Collected };

		/// <summary>Gets or sets requirements to start the objective action.</summary>
		public Requirement[] actionRequirements = Array.Empty<Requirement>();
	}

	/// <summary>Contract defense that protects downloads or periodically raises trace.</summary>
	[Serializable]
	public sealed class ContractDefenseDefinition
	{
		public string id, sourceNodeId;
		public string[] targetNodeIds = Array.Empty<string>();
		public DefenseKind kind;
		public int minimumDifficulty = 1;
		public float intervalSeconds = 60, traceBurst = 12;
	}

	/// <summary>Types of contract security layer.</summary>
	public enum DefenseKind { CredentialLock, SecuritySweep }

	/// <summary>Scriptable contract with company, network, payouts, objectives, and lore.</summary>
	[CreateAssetMenu(menuName = "Hack The Planet/Contract")]
	public sealed class ContractDefinition : GameplayDefinition
	{
		/// <summary>Gets or sets the employing company.</summary>
		public CompanyDefinition company;

		/// <summary>Gets or sets the network layout for this job.</summary>
		public NetworkDefinition network;

		/// <summary>Gets or sets client name, tag, objective summary, win condition text, and aftermath template.</summary>
		public string client, tag, objective, condition, aftermath;

		/// <summary>Gets or sets how the job is categorized in the UI.</summary>
		public JobKind displayKind;

		/// <summary>Gets or sets whether the contract can be taken repeatedly.</summary>
		public bool recurring;

		/// <summary>Gets or sets prior wins required before this contract is offered.</summary>
		public int requiredWins;

		/// <summary>Gets or sets the quiet-run trace threshold (default 75).</summary>
		public float quietLimit = 75;

		/// <summary>Contract-specific action time adjustment for objective-heavy jobs.</summary>
		public float paceMultiplier = 1;

		/// <summary>Gets or sets evaluated credit, XP, difficulty, and procedural seed rewards (defaults 600, 50, 1, 114).</summary>
		[SerializeReference] public Number credits = new ConstantNumber(600);

		[SerializeReference] public Number xp = new ConstantNumber(50);
		[SerializeReference] public Number difficulty = new ConstantNumber(1);
		[SerializeReference] public Number seed = new ConstantNumber(114);

		/// <summary>Gets or sets ordered objectives that must be completed.</summary>
		public ObjectiveDefinition[] objectives = Array.Empty<ObjectiveDefinition>();

		/// <summary>Gets or sets per-action requirement overrides.</summary>
		public ContractActionRule[] actionRules = Array.Empty<ContractActionRule>();

		/// <summary>Security layers selected when the operation starts.</summary>
		public ContractDefenseDefinition[] defenses = Array.Empty<ContractDefenseDefinition>();

		/// <summary>Gets or sets story fragments unlocked by this contract.</summary>
		public LoreFragment[] lore = Array.Empty<LoreFragment>();
	}
}
