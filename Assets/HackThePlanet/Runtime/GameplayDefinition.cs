using System;
using UnityEngine;

namespace HackThePlanet
{
	/// <summary>Base scriptable asset for gameplay data entries identified by <see cref="id" /> and <see cref="title" />.</summary>
	public abstract class GameplayDefinition : ScriptableObject
	{
		/// <summary>Gets or sets the stable identifier for this definition.</summary>
		public string id, title;

		/// <summary>Gets or sets the long-form description shown in UI.</summary>
		[TextArea(2, 6)] public string description;
	}

	/// <summary>Serializable pair of a stat identifier and numeric value.</summary>
	[Serializable]
	public sealed class NamedStat
	{
		/// <summary>Gets or sets the stat key.</summary>
		public string id;

		/// <summary>Gets or sets the stat magnitude.</summary>
		public float value;

		/// <summary>Initializes a new instance with default values.</summary>
		public NamedStat()
		{
		}

		/// <summary>Initializes a new instance with the given key and value.</summary>
		/// <param name="key">Stat identifier.</param>
		/// <param name="v">Stat magnitude.</param>
		public NamedStat(string key, float v)
		{
			// Store the stat key.
			id = key;

			// Store the stat magnitude.
			value = v;
		}
	}

	/// <summary>Gate that must pass before an action or purchase is allowed.</summary>
	[Serializable]
	public sealed class Requirement
	{
		/// <summary>Gets or sets the condition evaluated for this requirement.</summary>
		[SerializeReference] public Condition condition = new AlwaysCondition();

		/// <summary>Gets or sets the message shown when the requirement fails.</summary>
		public string message;
	}

	/// <summary>Stat adjustment applied when its <see cref="when" /> condition is satisfied.</summary>
	[Serializable]
	public sealed class Modifier
	{
		/// <summary>Gets or sets the modifier identifier.</summary>
		public string id;

		/// <summary>Gets or sets the target stat name.</summary>
		public string stat;

		/// <summary>Gets or sets the evaluation order; higher values apply later.</summary>
		public int priority;

		/// <summary>Gets or sets how <see cref="value" /> combines with the current stat.</summary>
		public Arithmetic operation = Arithmetic.Multiply;

		/// <summary>Gets or sets the condition that must pass for this modifier to apply.</summary>
		[SerializeReference] public Condition when = new AlwaysCondition();

		/// <summary>Gets or sets the operand used with <see cref="operation" />.</summary>
		[SerializeReference] public Number value = new ConstantNumber(1);
	}

	/// <summary>Gameplay moments that can fire <see cref="Trigger" /> rules.</summary>
	public enum RuleEvent
	{
		/// <summary>An operation has started.</summary>
		OperationStarted,

		/// <summary>A player action has finished.</summary>
		ActionCompleted,

		/// <summary>Control of a node has been established.</summary>
		ControlEstablished,

		/// <summary>A timed status has expired.</summary>
		StatusExpired
	}

	/// <summary>Rule that runs <see cref="effects" /> when a <see cref="RuleEvent" /> occurs and <see cref="when" /> is true.</summary>
	[Serializable]
	public sealed class Trigger
	{
		/// <summary>Gets or sets the event that activates this trigger.</summary>
		public RuleEvent on;

		/// <summary>Gets or sets an optional key so the trigger runs at most once per key.</summary>
		public string onceKey;

		/// <summary>Gets or sets the additional condition required to fire.</summary>
		[SerializeReference] public Condition when = new AlwaysCondition();

		/// <summary>Gets or sets the effects executed when the trigger fires.</summary>
		[SerializeReference] public Effect[] effects = Array.Empty<Effect>();
	}

	/// <summary>Passive modifier source that may apply to nearby or stacked targets.</summary>
	[Serializable]
	public sealed class Aura
	{
		/// <summary>Gets or sets the aura identifier.</summary>
		public string id;

		/// <summary>Gets or sets whether the aura only affects adjacent nodes; <see cref="stack" /> controls stacking.</summary>
		public bool adjacentOnly, stack;

		/// <summary>Gets or sets the condition on the source node for the aura to be active.</summary>
		[SerializeReference] public Condition source = new AlwaysCondition();

		/// <summary>Gets or sets the modifier applied by this aura.</summary>
		public Modifier modifier = new();
	}

	/// <summary>Selects network nodes by scope, optional id, and filter condition.</summary>
	[Serializable]
	public sealed class NodeSelector
	{
		/// <summary>Gets or sets how candidate nodes are chosen.</summary>
		public SelectionScope scope;

		/// <summary>Gets or sets the node id when <see cref="scope" /> is <see cref="SelectionScope.Named" />.</summary>
		public string nodeId;

		/// <summary>Gets or sets the filter condition applied to candidates.</summary>
		[SerializeReference] public Condition filter = new AlwaysCondition();
	}

	/// <summary>Node selection strategies for <see cref="NodeSelector" />.</summary>
	public enum SelectionScope
	{
		/// <summary>The current action target node.</summary>
		Target,

		/// <summary>All nodes in the network.</summary>
		All,

		/// <summary>Neighbors of the target node.</summary>
		Neighbors,

		/// <summary>The network entry node.</summary>
		Entry,

		/// <summary>A single node identified by <see cref="NodeSelector.nodeId" />.</summary>
		Named
	}
}