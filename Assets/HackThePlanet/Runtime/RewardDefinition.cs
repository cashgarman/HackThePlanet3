using System;
using UnityEngine;

namespace HackThePlanet
{
	/// <summary>Conditional credit and XP payout evaluated when <see cref="when" /> is true.</summary>
	[Serializable]
	public sealed class RewardRule
	{
		/// <summary>Gets or sets the condition that must pass for this rule to apply.</summary>
		[SerializeReference] public Condition when = new AlwaysCondition();

		/// <summary>Gets or sets the credits awarded when the rule applies.</summary>
		[SerializeReference] public Number credits = new ConstantNumber(0);

		/// <summary>Gets or sets the experience awarded when the rule applies.</summary>
		[SerializeReference] public Number xp = new ConstantNumber(0);
	}

	/// <summary>Scriptable reward table for archive, quiet runs, and custom rules.</summary>
	[CreateAssetMenu(menuName = "Hack The Planet/Rewards")]
	public sealed class RewardDefinition : GameplayDefinition
	{
		/// <summary>Gets or sets default archive payout credits (default 120).</summary>
		public int archiveCredits = 120, archiveXp = 15, archiveXpCap = 30, quietCredits = 150, quietXp = 10;

		/// <summary>Gets or sets additional conditional reward rules.</summary>
		public RewardRule[] rules = Array.Empty<RewardRule>();
	}
}