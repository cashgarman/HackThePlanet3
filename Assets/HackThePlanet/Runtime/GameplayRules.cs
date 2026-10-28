using System;
using System.Linq;
using UnityEngine;
using Random = System.Random;

namespace HackThePlanet
{
	/// <summary>Binary operations used by gameplay number formulas.</summary>
	public enum Arithmetic
	{
		/// <summary>Sum of left and right operands.</summary>
		Add,

		/// <summary>Left minus right.</summary>
		Subtract,

		/// <summary>Product of operands.</summary>
		Multiply,

		/// <summary>Left divided by right; throws when right is zero.</summary>
		Divide,

		/// <summary>Smaller of the two operands.</summary>
		Minimum,

		/// <summary>Larger of the two operands.</summary>
		Maximum,

		/// <summary>Replaces left with right.</summary>
		Override,

		/// <summary>Floating remainder; throws when right is zero.</summary>
		Modulo,

		/// <summary>Floor of the left operand.</summary>
		Floor
	}

	/// <summary>Relational tests used by compare conditions.</summary>
	public enum Comparison
	{
		/// <summary>True when operands are equal.</summary>
		Equal,

		/// <summary>True when operands differ.</summary>
		NotEqual,

		/// <summary>True when left is strictly less than right.</summary>
		Less,

		/// <summary>True when left is less than or equal to right.</summary>
		LessOrEqual,

		/// <summary>True when left is strictly greater than right.</summary>
		Greater,

		/// <summary>True when left is greater than or equal to right.</summary>
		GreaterOrEqual
	}

	/// <summary>Network node flags and derived reachability states for rules.</summary>
	public enum NodeState
	{
		/// <summary>Node has been discovered on the map.</summary>
		Known,

		/// <summary>Node is under operator control.</summary>
		Controlled,

		/// <summary>Node intel has been verified.</summary>
		Scanned,

		/// <summary>Payload files were recovered from the node.</summary>
		Collected,

		/// <summary>Node is marked as a contract objective.</summary>
		Objective,

		/// <summary>Node holds optional archive files.</summary>
		Bonus,

		/// <summary>Hostile defenses are active (not controlled and not suppressed).</summary>
		DefenseActive,

		/// <summary>Node is known and adjacent to control or itself controlled.</summary>
		Reachable
	}

	/// <summary>Built-in simulation inputs addressable from authored number formulas.</summary>
	public enum NumberInput
	{
		/// <summary>Formula input mapped to Security during rule evaluation.</summary>
		Security,

		/// <summary>Formula input mapped to BaseSecurity during rule evaluation.</summary>
		BaseSecurity,

		/// <summary>Formula input mapped to Day during rule evaluation.</summary>
		Day,

		/// <summary>Formula input mapped to Difficulty during rule evaluation.</summary>
		Difficulty,

		/// <summary>Formula input mapped to Seed during rule evaluation.</summary>
		Seed,

		/// <summary>Formula input mapped to Column during rule evaluation.</summary>
		Column,

		/// <summary>Formula input mapped to Row during rule evaluation.</summary>
		Row,

		/// <summary>Formula input mapped to CompletedContracts during rule evaluation.</summary>
		CompletedContracts,

		/// <summary>Formula input mapped to CompanyWins during rule evaluation.</summary>
		CompanyWins,

		/// <summary>Formula input mapped to BonusFiles during rule evaluation.</summary>
		BonusFiles,

		/// <summary>Formula input mapped to Trace during rule evaluation.</summary>
		Trace,

		/// <summary>Formula input mapped to QuietLimit during rule evaluation.</summary>
		QuietLimit,

		/// <summary>Formula input mapped to ContractCredits during rule evaluation.</summary>
		ContractCredits,

		/// <summary>Formula input mapped to ContractXp during rule evaluation.</summary>
		ContractXp,

		/// <summary>Formula input mapped to Duration during rule evaluation.</summary>
		Duration,

		/// <summary>Formula input mapped to NeighborCount during rule evaluation.</summary>
		NeighborCount,

		/// <summary>Formula input mapped to NodeCount during rule evaluation.</summary>
		NodeCount,

		/// <summary>Formula input mapped to Counter during rule evaluation.</summary>
		Counter,

		/// <summary>Formula input mapped to StatusRemaining during rule evaluation.</summary>
		StatusRemaining,

		/// <summary>Formula input mapped to HardwareLevel during rule evaluation.</summary>
		HardwareLevel,

		/// <summary>Formula input mapped to ArchiveCredits during rule evaluation.</summary>
		ArchiveCredits,

		/// <summary>Formula input mapped to ArchiveXp during rule evaluation.</summary>
		ArchiveXp,

		/// <summary>Formula input mapped to ArchiveXpCap during rule evaluation.</summary>
		ArchiveXpCap,

		/// <summary>Formula input mapped to QuietCredits during rule evaluation.</summary>
		QuietCredits,

		/// <summary>Formula input mapped to QuietXp during rule evaluation.</summary>
		QuietXp,

		/// <summary>Formula input mapped to Step during rule evaluation.</summary>
		Step,

		/// <summary>Formula input mapped to Lane during rule evaluation.</summary>
		Lane,

		/// <summary>Formula input mapped to SafeLane during rule evaluation.</summary>
		SafeLane,

		/// <summary>Formula input mapped to ControlledDistance during rule evaluation.</summary>
		ControlledDistance,

		/// <summary>Formula input mapped to UnlockedAbilities during rule evaluation.</summary>
		UnlockedAbilities,

		/// <summary>Formula input mapped to NodeStorage during rule evaluation.</summary>
		NodeStorage,

		/// <summary>Formula input mapped to StorageUsed during rule evaluation.</summary>
		StorageUsed,

		/// <summary>Formula input mapped to FreeStorage during rule evaluation.</summary>
		FreeStorage,

		/// <summary>Formula input mapped to StorageCapacity during rule evaluation.</summary>
		StorageCapacity,

		/// <summary>Formula input mapped to MemoryFree during rule evaluation.</summary>
		MemoryFree,

		/// <summary>Formula input mapped to MemoryPool during rule evaluation.</summary>
		MemoryPool,

		/// <summary>Formula input mapped to MemoryReserved during rule evaluation.</summary>
		MemoryReserved,

		/// <summary>Formula input mapped to MemoryReclaiming during rule evaluation.</summary>
		MemoryReclaiming,

		/// <summary>Formula input mapped to RigNeighborCount during rule evaluation.</summary>
		RigNeighborCount,

		/// <summary>Formula input mapped to JobsCompleted during rule evaluation.</summary>
		JobsCompleted
	}

	/// <summary>Evaluation context for conditions, numbers, and effects during simulation and authoring tests.</summary>
	public sealed class RuleContext
	{
		// Tracks nested rule evaluation depth to detect runaway recursion.
		private readonly Guard guard = new();

		// Action under evaluation when testing action-scoped rules.
		internal ActionDefinition Action;

		/// <summary>Career state for economy, research, and rig inventory.</summary>
		public Career Career;

		// Catalog supplying definitions and base stat values for this evaluation.
		internal GameCatalog Catalog;

		/// <summary>Active contract description when simulating an operation.</summary>
		public Contract Contract;

		/// <summary>Action or effect duration in seconds supplied by the caller.</summary>
		public float Duration;

		/// <summary>Network node under evaluation; null for operator-scoped rules.</summary>
		public NetNode Node;

		/// <summary>Seeded random for network generation formulas only.</summary>
		public Random Random;

		/// <summary>Rack module instance id when evaluating per-part modifiers.</summary>
		public string RigInstance;

		/// <summary>Reserved rig slot index for stepped rules, or -1 when unused.</summary>
		public int RigSlot = -1;

		/// <summary>When true, lane rules treat the current lane as safe.</summary>
		public bool SafeLane;

		/// <summary>Live simulation providing stats, counters, and graph queries.</summary>
		public GameSimulation Simulation;

		/// <summary>Step and lane indices for multi-lane rule evaluation.</summary>
		public int Step, Lane;

		// Clones the context and binds it to a rack module instance id.
		internal RuleContext ForRig(string instance)
		{
			// Copy the context and bind it to the rig instance.
			var c = (RuleContext)MemberwiseClone();
			c.RigInstance = instance;

			return c;
		}

		// Clones the context and binds it to a specific network node.
		internal RuleContext At(NetNode node)
		{
			// Copy the context and bind it to the node.
			var c = (RuleContext)MemberwiseClone();
			c.Node = node;

			return c;
		}

		// Enters a nested rule evaluation level and throws when depth exceeds 64.
		internal void Enter()
		{
			// Count this evaluation level and reject runaway recursion.
			if (++guard.depth > 64)
			{
				// Undo the level before reporting the cycle.
				guard.depth--;

				// Report the cyclic reference with the depth limit.
				throw new InvalidOperationException(
					"Gameplay rule evaluation exceeds 64 levels. Check cyclic stat/rule references.");
			}
		}

		// Leaves a nested rule evaluation level after a guarded call completes.
		internal void Leave()
		{
			guard.depth--;
		}

		/// <summary>Checks whether the context node's type includes a semantic tag.</summary>
		/// <param name="tag">Tag id from node type authoring.</param>
		/// <returns>False when there is no node or the tag is absent.</returns>
		public bool Tagged(string tag)
		{
			return Node != null && Catalog.NodeType(Node.typeId).tags.Contains(tag);
		}

		/// <summary>Evaluates a <see cref="NodeState" /> flag or derived state on the context node.</summary>
		/// <param name="state">State to test.</param>
		/// <returns>False when there is no node or the state does not hold.</returns>
		public bool State(NodeState state)
		{
			// Operator-scoped rules have no node, so no state holds.
			if (Node == null)
				return false;

			// Map each state flag to its node field or derived check.
			switch (state)
			{
				case NodeState.Known:         return Node.known;
				case NodeState.Controlled:    return Node.controlled;
				case NodeState.Scanned:       return Node.scanned;
				case NodeState.Collected:     return Node.collected;
				case NodeState.Objective:     return Node.objective;
				case NodeState.Bonus:         return Node.bonus;
				case NodeState.DefenseActive: return !Node.controlled && Node.suppressed <= 0;
				case NodeState.Reachable:     return Simulation != null && Simulation.Reachable(Node);
				default:                      return false;
			}
		}

		// Holds the current recursion depth for guarded rule evaluation.
		private sealed class Guard
		{
			public int depth;
		}
	}

	/// <summary>Authoring-time numeric formula evaluated against a <see cref="RuleContext" />.</summary>
	[Serializable]
	public abstract class Number
	{
		/// <summary>Evaluates the formula with recursion guarding and non-finite checks.</summary>
		/// <param name="context">Simulation, node, and career inputs.</param>
		/// <returns>A finite float value.</returns>
		/// <exception cref="InvalidOperationException">The result is NaN or infinity, or recursion depth exceeds 64.</exception>
		public float Evaluate(RuleContext context)
		{
			// Count this evaluation toward the recursion limit.
			context.Enter();

			// Run the formula and always release the recursion level afterward.
			try
			{
				// Compute the raw value.
				var value = Calculate(context);

				// Reject NaN and infinity before returning.
				if (float.IsNaN(value) || float.IsInfinity(value))
					throw new InvalidOperationException("Non-finite gameplay formula: " + GetType().Name);

				return value;
			}
			finally
			{
				// Release the recursion level.
				context.Leave();
			}
		}

		/// <summary>Evaluates the formula value; called from <see cref="Evaluate" /> after recursion guarding.</summary>
		/// <param name="context">Simulation, node, and career inputs.</param>
		/// <returns>Raw formula result before non-finite checks.</returns>
		protected abstract float Calculate(RuleContext context);
	}

	/// <summary>Fixed literal used in formulas.</summary>
	[Serializable]
	public sealed class ConstantNumber : Number
	{
		/// <summary>Literal value returned by the formula.</summary>
		public float value;

		/// <summary>Creates a zero constant for serialization defaults.</summary>
		public ConstantNumber()
		{
		}

		/// <summary>Creates a constant with the given value.</summary>
		/// <param name="value">Literal returned by <see cref="Evaluate" />.</param>
		public ConstantNumber(float value)
		{
			this.value = value;
		}

		protected override float Calculate(RuleContext c)
		{
			return value;
		}
	}

	/// <summary>Reads a named stat from the simulation or catalog base values.</summary>
	[Serializable]
	public sealed class StatNumber : Number
	{
		/// <summary>Stat id resolved through modifiers on the context node or rig.</summary>
		public string stat;

		protected override float Calculate(RuleContext c)
		{
			// Prefer live simulation stats; fall back to catalog base values.
			return c.Simulation != null ? c.Simulation.Stat(stat, c) : c.Catalog.BaseStat(stat);
		}
	}

	/// <summary>Built-in gameplay input selected by <see cref="NumberInput" />.</summary>
	[Serializable]
	public sealed class InputNumber : Number
	{
		/// <summary>Which simulation input to read.</summary>
		public NumberInput input;

		/// <summary>Counter, status, hardware, or rig neighbor key when the input requires one.</summary>
		public string key;

		/// <summary>Filter for neighbor and node count inputs.</summary>
		[SerializeReference] public Condition filter = new AlwaysCondition();

		protected override float Calculate(RuleContext c)
		{
			// Map each input to its live simulation, career, or catalog value.
			switch (input)
			{
				case NumberInput.JobsCompleted:      return c.Career?.jobs ?? 0;
				case NumberInput.NodeStorage:        return c.Node?.storage ?? 0;
				case NumberInput.StorageUsed:        return c.Simulation?.StorageUsed ?? 0;
				case NumberInput.FreeStorage:        return c.Simulation?.FreeStorage ?? 0;
				case NumberInput.StorageCapacity:    return c.Simulation?.StorageCapacity ?? 0;
				case NumberInput.MemoryFree:         return c.Simulation?.MemoryFree ?? 0;
				case NumberInput.MemoryPool:         return c.Simulation?.MemoryPool ?? 0;
				case NumberInput.MemoryReserved:     return c.Simulation?.MemoryReserved ?? 0;
				case NumberInput.MemoryReclaiming:   return c.Simulation?.MemoryReclaiming ?? 0;
				case NumberInput.RigNeighborCount:   return c.Simulation?.RigNeighborCount(c.RigInstance, key) ?? 0;
				case NumberInput.Step:               return c.Step;
				case NumberInput.Lane:               return c.Lane;
				case NumberInput.SafeLane:           return c.SafeLane ? 1 : 0;
				case NumberInput.ControlledDistance: return c.Simulation?.ControlledDistance(c.Node) ?? int.MaxValue;
				case NumberInput.UnlockedAbilities:
					return c.Career?.unlocked.Count(id => c.Catalog.Technology(id)?.special == true) ?? 0;
				case NumberInput.Security: return c.Simulation?.EffectiveSecurity(c.Node) ?? c.Node?.security ?? 0;
				case NumberInput.BaseSecurity: return c.Node?.security ?? 0;
				case NumberInput.Day: return c.Career?.day ?? 1;
				case NumberInput.Difficulty: return c.Contract?.difficulty ?? 1;
				case NumberInput.Seed: return c.Contract?.seed ?? 0;
				case NumberInput.Column: return c.Node?.column ?? 0;
				case NumberInput.Row: return c.Node?.row ?? 0;
				case NumberInput.CompletedContracts: return c.Career?.completed.Count ?? 0;
				case NumberInput.CompanyWins:
					return c.Career?.completed.Count(id => c.Catalog.CompanyFor(id) == c.Contract?.company) ?? 0;
				case NumberInput.BonusFiles: return c.Simulation?.Op?.nodes.Count(n => n.bonus && n.collected) ?? 0;
				case NumberInput.Trace: return c.Simulation?.Op == null ? 0 : Mathf.Max(c.Simulation.Op.peakTrace, c.Simulation.Op.trace);
				case NumberInput.QuietLimit: return c.Contract?.quietLimit ?? 0;
				case NumberInput.ContractCredits: return c.Contract?.credits ?? 0;
				case NumberInput.ContractXp: return c.Contract?.xp ?? 0;
				case NumberInput.Duration: return c.Duration;
				case NumberInput.NeighborCount:
					return c.Node == null || c.Simulation?.Op == null
						? 0
						: c.Simulation.Neighbors(c.Node).Count(n => filter.Test(c.At(n)));
				case NumberInput.NodeCount: return c.Simulation?.Op?.nodes.Count(n => filter.Test(c.At(n))) ?? 0;
				case NumberInput.Counter: return c.Simulation?.Counter(key) ?? 0;
				case NumberInput.StatusRemaining: return c.Simulation?.StatusRemaining(key, c.Node?.id) ?? 0;
				case NumberInput.HardwareLevel: return c.Career?.Level(key) ?? 0;
				case NumberInput.ArchiveCredits: return c.Catalog.Data.rewards.archiveCredits;
				case NumberInput.ArchiveXp: return c.Catalog.Data.rewards.archiveXp;
				case NumberInput.ArchiveXpCap: return c.Catalog.Data.rewards.archiveXpCap;
				case NumberInput.QuietCredits: return c.Catalog.Data.rewards.quietCredits;
				case NumberInput.QuietXp: return c.Catalog.Data.rewards.quietXp;
				default: throw new InvalidOperationException("Unknown gameplay formula input.");
			}
		}
	}

	/// <summary>Combines two sub-formulas with a <see cref="Arithmetic" /> operation.</summary>
	[Serializable]
	public sealed class BinaryNumber : Number
	{
		/// <summary>Operation applied to evaluated operands.</summary>
		public Arithmetic operation;

		/// <summary>Left and right operand formulas.</summary>
		[SerializeReference] public Number left = new ConstantNumber(0), right = new ConstantNumber(1);

		protected override float Calculate(RuleContext c)
		{
			// Evaluate both operands and combine them with the operation.
			return RuleMath.Apply(operation, left.Evaluate(c), right.Evaluate(c));
		}
	}

	/// <summary>Picks one of two sub-formulas based on a condition.</summary>
	[Serializable]
	public sealed class ConditionalNumber : Number
	{
		/// <summary>When true, <see cref="yes" /> is evaluated; otherwise <see cref="no" />.</summary>
		[SerializeReference] public Condition when = new AlwaysCondition();

		/// <summary>Branch formulas when the condition passes or fails.</summary>
		[SerializeReference] public Number yes = new ConstantNumber(1), no = new ConstantNumber(0);

		protected override float Calculate(RuleContext c)
		{
			// Pick the branch from the condition, then evaluate only that branch.
			return (when.Test(c) ? yes : no).Evaluate(c);
		}
	}

	/// <summary>Uniform random integer inclusive range during seeded network generation.</summary>
	[Serializable]
	public sealed class RandomNumber : Number
	{
		/// <summary>Inclusive lower bound.</summary>
		public int minimum = 1;

		/// <summary>Inclusive upper bound.</summary>
		public int maximum = 2;

		protected override float Calculate(RuleContext c)
		{
			// Random formulas need the seeded generator from network generation.
			if (c.Random == null)
				throw new InvalidOperationException("Random formulas are only valid during seeded network generation.");

			// Draw an inclusive random value within the configured bounds.
			return c.Random.Next(minimum, checked(maximum + 1));
		}
	}

	// Applies binary arithmetic operations used by authored number formulas.
	internal static class RuleMath
	{
		public static float Apply(Arithmetic op, float left, float right)
		{
			float result;

			// Compute the result for the requested operation.
			switch (op)
			{
				case Arithmetic.Add:      result = left + right; break;
				case Arithmetic.Subtract: result = left - right; break;
				case Arithmetic.Multiply: result = left * right; break;
				case Arithmetic.Divide:
					// Reject division by zero before dividing.
					if (right == 0)
						throw new InvalidOperationException("Gameplay formula divides by zero.");

					// Divide the operands.
					result = left / right;
					break;
				case Arithmetic.Minimum:  result = Mathf.Min(left, right); break;
				case Arithmetic.Maximum:  result = Mathf.Max(left, right); break;
				case Arithmetic.Override: result = right; break;
				case Arithmetic.Modulo:
					// Reject modulo by zero before taking the remainder.
					if (right == 0)
						throw new InvalidOperationException("Gameplay formula modulo by zero.");

					// Take the floating remainder of the operands.
					result = left % right;
					break;
				case Arithmetic.Floor: result = Mathf.Floor(left); break;
				default:               throw new InvalidOperationException("Unknown formula operation.");
			}

			// Reject results that are NaN or infinity.
			if (float.IsNaN(result) || float.IsInfinity(result))
				throw new InvalidOperationException("Non-finite gameplay arithmetic result.");

			return result;
		}
	}

	/// <summary>Authoring-time predicate evaluated against a <see cref="RuleContext" />.</summary>
	[Serializable]
	public abstract class Condition
	{
		/// <summary>Evaluates the condition with recursion guarding.</summary>
		/// <param name="c">Simulation, node, and career inputs.</param>
		/// <returns>Whether the predicate holds.</returns>
		/// <exception cref="InvalidOperationException">Recursion depth exceeds 64.</exception>
		public bool Test(RuleContext c)
		{
			// Count this evaluation toward the recursion limit.
			c.Enter();

			// Run the predicate and always release the recursion level afterward.
			try
			{
				// Evaluate the predicate for this context.
				return Check(c);
			}
			finally
			{
				// Release the recursion level.
				c.Leave();
			}
		}

		/// <summary>Evaluates the predicate; called from <see cref="Test" /> after recursion guarding.</summary>
		/// <param name="c">Simulation, node, and career inputs.</param>
		/// <returns>Whether the condition holds for the context.</returns>
		protected abstract bool Check(RuleContext c);
	}

	/// <summary>Condition that always passes.</summary>
	[Serializable]
	public sealed class AlwaysCondition : Condition
	{
		protected override bool Check(RuleContext c)
		{
			return true;
		}
	}

	/// <summary>True when every child condition passes.</summary>
	[Serializable]
	public sealed class AllCondition : Condition
	{
		/// <summary>Child conditions that must all pass.</summary>
		[SerializeReference] public Condition[] conditions = Array.Empty<Condition>();

		protected override bool Check(RuleContext c)
		{
			return conditions.All(x => x.Test(c));
		}
	}

	/// <summary>True when any child condition passes.</summary>
	[Serializable]
	public sealed class AnyCondition : Condition
	{
		/// <summary>Child conditions; one success is enough.</summary>
		[SerializeReference] public Condition[] conditions = Array.Empty<Condition>();

		protected override bool Check(RuleContext c)
		{
			return conditions.Any(x => x.Test(c));
		}
	}

	/// <summary>Inverts a child condition.</summary>
	[Serializable]
	public sealed class NotCondition : Condition
	{
		/// <summary>Inner condition whose result is negated.</summary>
		[SerializeReference] public Condition condition = new AlwaysCondition();

		protected override bool Check(RuleContext c)
		{
			return !condition.Test(c);
		}
	}

	/// <summary>Tests a <see cref="NodeState" /> on the context node.</summary>
	[Serializable]
	public sealed class NodeStateCondition : Condition
	{
		/// <summary>State flag or derived state to require.</summary>
		public NodeState state;

		protected override bool Check(RuleContext c)
		{
			return c.State(state);
		}
	}

	/// <summary>Requires a semantic tag on the context node's type.</summary>
	[Serializable]
	public sealed class NodeTagCondition : Condition
	{
		/// <summary>Tag id from node type authoring.</summary>
		public string tag;

		protected override bool Check(RuleContext c)
		{
			return c.Tagged(tag);
		}
	}

	/// <summary>Matches the action under evaluation, optionally requiring a tag.</summary>
	[Serializable]
	public sealed class ActionCondition : Condition
	{
		/// <summary>When set, the running action id must match.</summary>
		public ActionDefinition action;

		/// <summary>When non-empty, the action must include this tag.</summary>
		public string tag;

		protected override bool Check(RuleContext c)
		{
			return c.Action != null && (action == null || c.Action.id == action.id) &&
				   (string.IsNullOrEmpty(tag) || c.Action.tags.Contains(tag));
		}
	}

	/// <summary>Requires a technology to be unlocked or equipped in the loadout.</summary>
	[Serializable]
	public sealed class ResearchCondition : Condition
	{
		/// <summary>Technology that must be owned.</summary>
		public TechnologyDefinition technology;

		/// <summary>When true, the technology must be equipped; otherwise unlocked is enough.</summary>
		public bool equipped;

		protected override bool Check(RuleContext c)
		{
			return technology != null && (equipped
				? c.Simulation?.Equipped(technology.id) == true
				: c.Career?.unlocked.Contains(technology.id) == true);
		}
	}

	/// <summary>Requires adjacent rack modules on the operator rig.</summary>
	[Serializable]
	public sealed class RigNeighborCondition : Condition
	{
		/// <summary>Empty matches all neighbors; otherwise module id, family, or branch name.</summary>
		public string kind;

		/// <summary>Minimum matching neighbor count required.</summary>
		public int minimum = 1;

		protected override bool Check(RuleContext c)
		{
			return (c.Simulation?.RigNeighborCount(c.RigInstance, kind) ?? 0) >= minimum;
		}
	}

	/// <summary>Evaluates a child condition on a specific network node.</summary>
	[Serializable]
	public sealed class NamedNodeCondition : Condition
	{
		/// <summary>Target node id in the active operation.</summary>
		public string nodeId;

		/// <summary>Condition evaluated with the named node as context.</summary>
		[SerializeReference] public Condition condition = new AlwaysCondition();

		protected override bool Check(RuleContext c)
		{
			// Resolve the named node in the active operation.
			var node = c.Simulation?.Node(nodeId);

			// Test the nested condition with the node as context.
			return node != null && condition.Test(c.At(node));
		}
	}

	/// <summary>Compares two numeric formulas with a <see cref="Comparison" /> operator.</summary>
	[Serializable]
	public sealed class CompareCondition : Condition
	{
		/// <summary>Relational operator applied to evaluated operands.</summary>
		public Comparison comparison;

		/// <summary>Left and right formulas to compare.</summary>
		[SerializeReference] public Number left = new ConstantNumber(0), right = new ConstantNumber(0);

		protected override bool Check(RuleContext c)
		{
			// Evaluate both operands before comparing.
			float a = left.Evaluate(c), b = right.Evaluate(c);

			// Apply the requested relational operator.
			switch (comparison)
			{
				case Comparison.Equal:       return a == b;
				case Comparison.NotEqual:    return a != b;
				case Comparison.Less:        return a < b;
				case Comparison.LessOrEqual: return a <= b;
				case Comparison.Greater:     return a > b;
				default:                     return a >= b;
			}
		}
	}

	/// <summary>Gameplay mutation applied when triggers or actions fire.</summary>
	[Serializable]
	public abstract class Effect
	{
		/// <summary>Applies the effect using the supplied context.</summary>
		/// <param name="c">Simulation, node, and action inputs.</param>
		public abstract void Apply(RuleContext c);
	}

	/// <summary>Sets a <see cref="NodeState" /> flag on selected nodes.</summary>
	[Serializable]
	public sealed class SetStateEffect : Effect
	{
		/// <summary>Nodes to update.</summary>
		public NodeSelector targets = new();

		/// <summary>State field to assign.</summary>
		public NodeState state = NodeState.Controlled;

		/// <summary>Value written to the state field.</summary>
		public bool value = true;

		/// <inheritdoc />
		public override void Apply(RuleContext c)
		{
			// Update the state flag on every selected node.
			foreach (var node in c.Simulation.Select(targets, c).ToArray())
				c.Simulation.SetState(node, state, value, c);
		}
	}

	/// <summary>Marks nodes known within a hop radius of each target.</summary>
	[Serializable]
	public sealed class RevealEffect : Effect
	{
		/// <summary>Seed nodes for flood-fill discovery.</summary>
		public NodeSelector targets = new();

		/// <summary>Hop radius evaluated per target node.</summary>
		[SerializeReference] public Number radius = new ConstantNumber(1);

		/// <inheritdoc />
		public override void Apply(RuleContext c)
		{
			// Reveal each selected node's neighborhood within its computed radius.
			foreach (var node in c.Simulation.Select(targets, c).ToArray())
				c.Simulation.Reveal(node, Mathf.Max(0, Mathf.RoundToInt(radius.Evaluate(c.At(node)))));
		}
	}

	/// <summary>Adjusts operation trace, clamped to the career trace limit.</summary>
	[Serializable]
	public sealed class TraceEffect : Effect
	{
		/// <summary>Delta applied to current trace before clamping.</summary>
		[SerializeReference] public Number amount = new ConstantNumber(-15);

		/// <inheritdoc />
		public override void Apply(RuleContext c)
		{
			// Apply the trace delta and clamp the result to the career limit.
			c.Simulation.Op.trace = Mathf.Clamp(c.Simulation.Op.trace + amount.Evaluate(c), 0,
												c.Catalog.Data.career.traceLimit);
		}
	}

	/// <summary>Applies a timed status to the operator or selected nodes.</summary>
	[Serializable]
	public sealed class ApplyStatusEffect : Effect
	{
		/// <summary>Status definition to apply.</summary>
		public StatusDefinition status;

		/// <summary>When true, applies to the operator instead of node targets.</summary>
		public bool operatorStatus;

		/// <summary>Node targets when <see cref="operatorStatus" /> is false.</summary>
		public NodeSelector targets = new();

		/// <summary>Duration in simulation seconds.</summary>
		[SerializeReference] public Number duration = new ConstantNumber(60);

		/// <inheritdoc />
		public override void Apply(RuleContext c)
		{
			// Apply the status to the operator, or to each selected node.
			if (operatorStatus)
				c.Simulation.ApplyStatus(status, null, duration.Evaluate(c));
			else
				foreach (var node in c.Simulation.Select(targets, c).ToArray())
					c.Simulation.ApplyStatus(status, node.id, duration.Evaluate(c.At(node)));
		}
	}

	/// <summary>Adjusts base security on selected nodes with a floor.</summary>
	[Serializable]
	public sealed class SecurityEffect : Effect
	{
		/// <summary>Nodes to modify.</summary>
		public NodeSelector targets = new() { scope = SelectionScope.All };

		/// <summary>Minimum security after the change.</summary>
		public int minimum = 1;

		/// <summary>Rounded delta added to each node's security.</summary>
		[SerializeReference] public Number amount = new ConstantNumber(-2);

		/// <inheritdoc />
		public override void Apply(RuleContext c)
		{
			// Shift security on each selected node, never dropping below the minimum.
			foreach (var node in c.Simulation.Select(targets, c).ToArray())
				node.security = Mathf.Max(minimum, node.security + Mathf.RoundToInt(amount.Evaluate(c.At(node))));
		}
	}

	/// <summary>Increments a named operation counter.</summary>
	[Serializable]
	public sealed class CounterEffect : Effect
	{
		/// <summary>Counter key persisted on the active operation.</summary>
		public string key;

		/// <summary>Delta added to the counter value.</summary>
		public int amount = 1;

		/// <inheritdoc />
		public override void Apply(RuleContext c)
		{
			// Add the delta to the named counter on the operation.
			c.Simulation.AddCounter(key, amount);
		}
	}

	/// <summary>Runs nested effects when a condition passes.</summary>
	[Serializable]
	public sealed class ConditionalEffect : Effect
	{
		/// <summary>Gate condition for nested effects.</summary>
		[SerializeReference] public Condition when = new AlwaysCondition();

		/// <summary>Effects applied when the gate passes.</summary>
		[SerializeReference] public Effect[] effects = Array.Empty<Effect>();

		/// <inheritdoc />
		public override void Apply(RuleContext c)
		{
			// Apply the nested effects only when the gate passes.
			if (when.Test(c))
				c.Simulation.ApplyEffects(effects, c);
		}
	}

	/// <summary>Appends a formatted line to the operation log.</summary>
	[Serializable]
	public sealed class LogEffect : Effect
	{
		/// <summary>Message template expanded with catalog formatting tokens.</summary>
		[TextArea] public string message;

		/// <inheritdoc />
		public override void Apply(RuleContext c)
		{
			// Format the message template and append it to the operation log.
			c.Simulation.Log(c.Catalog.Format(message, c));
		}
	}

	/// <summary>Logs recovered lore for the context node's archive when present.</summary>
	[Serializable]
	public sealed class RecoverLoreEffect : Effect
	{
		/// <inheritdoc />
		public override void Apply(RuleContext c)
		{
			// Find the lore fragment for the node's archive.
			var fragment = LoreContent.Find(c.Contract, c.Node?.loreId);

			// Log the fragment only when one was found.
			if (fragment != null)
				c.Simulation.Log("LORE RECOVERED // " + fragment.title + " // " + fragment.text);
		}
	}
}