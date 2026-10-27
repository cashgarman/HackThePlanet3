using System;
using UnityEngine;

namespace HackThePlanet
{
	/// <summary>How starting an action reserves and releases shared RAM.</summary>
	public enum MemoryMode
	{
		/// <summary>No RAM is reserved; <see cref="ActionDefinition.memoryCost" /> must be zero.</summary>
		None,

		/// <summary>RAM is spent at start and returns gradually through memory reclaim.</summary>
		OneShot,

		/// <summary>RAM stays reserved until the ability is stopped or released.</summary>
		Persistent
	}

	/// <summary>Authoring asset for a workstation tool: targeting, queueing, RAM, trace, and rule effects.</summary>
	[CreateAssetMenu(menuName = "Hack The Planet/Action")]
	public sealed class ActionDefinition : GameplayDefinition
	{
		/// <summary>RAM reservation behavior while the tool runs or persists.</summary>
		public MemoryMode memoryMode;

		/// <summary>RAM in gigabytes required to start the tool; queued actions recheck at execution.</summary>
		[Min(0)] public float memoryCost;

		/// <summary>When true, allows a zero-duration targeted action while another tool is running.</summary>
		public bool instant;

		/// <summary>When true, releases this persistent tool and its RAM when the target becomes permanently controlled.</summary>
		public bool releaseOnTargetControl;

		/// <summary>Stat modifiers applied while a persistent tool holds a RAM reservation.</summary>
		public Modifier[] activeModifiers = Array.Empty<Modifier>();

		/// <summary>Semantic tags matched by action and node conditions.</summary>
		public string[] tags = Array.Empty<string>();

		/// <summary>Keyboard key that invokes this action when it is offered.</summary>
		public KeyCode shortcut;

		/// <summary>Player-facing shortcut caption; should stay consistent with <see cref="shortcut" />.</summary>
		public string shortcutLabel;

		/// <summary>Compatibility grouping for older tool-enum API adapters.</summary>
		public string legacyGroup;

		/// <summary>Workstation ordering priority; lower values appear earlier within the same usability group.</summary>
		public int presentationPriority = 50;

		/// <summary>When true, runs on the operator rather than a selected network node.</summary>
		public bool operatorTarget;

		/// <summary>When true, allows this tool to be queued behind the current action.</summary>
		public bool queueable = true;

		/// <summary>When true, grants the action without purchasing research.</summary>
		public bool initiallyAvailable;

		/// <summary>When true, allows future-neighbor queuing when the futureQueue stat permits it.</summary>
		public bool futureNeighborQueue;

		/// <summary>When true, forces this action's final trace cost to zero.</summary>
		public bool traceFree;

		/// <summary>Condition that decides whether this action is offered for the current target.</summary>
		[SerializeReference] public Condition visible = new AlwaysCondition();

		/// <summary>Requirements checked when execution starts or when an upgrade is purchased.</summary>
		public Requirement[] requirements = Array.Empty<Requirement>();

		/// <summary>Requirements checked when enqueueing; <see cref="requirements" /> are checked again at start.</summary>
		public Requirement[] queueRequirements = Array.Empty<Requirement>();

		/// <summary>Duration in simulation seconds; node-targeted tools require a positive value.</summary>
		[SerializeReference] public Number duration = new ConstantNumber(1);

		/// <summary>Trace contributed by the action before environmental pressure and final modifiers.</summary>
		[SerializeReference] public Number baseTrace = new ConstantNumber(0);

		/// <summary>Per-operation use limit; zero means unlimited uses.</summary>
		[SerializeReference] public Number useLimit = new ConstantNumber(0);

		/// <summary>Ordered mutations applied when this action completes or when a rule runs its effects.</summary>
		[SerializeReference] public Effect[] effects = Array.Empty<Effect>();
	}
}