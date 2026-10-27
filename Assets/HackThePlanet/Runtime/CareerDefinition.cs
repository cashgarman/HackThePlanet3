using System;
using UnityEngine;

namespace HackThePlanet
{
	/// <summary>Career rank title unlocked after completing a number of jobs.</summary>
	[Serializable]
	public sealed class CareerRank
	{
		/// <summary>Gets or sets the completed job count required for this rank.</summary>
		public int jobs;

		/// <summary>Gets or sets the rank title shown in UI.</summary>
		public string title;
	}

	/// <summary>Result of finishing or leaving an operation.</summary>
	public enum OperationOutcome
	{
		/// <summary>Contract objectives were delivered successfully.</summary>
		Delivered,

		/// <summary>The player aborted before completing requirements.</summary>
		Aborted,

		/// <summary>The defender isolated the session (trace caught).</summary>
		Caught
	}

	/// <summary>UI and progression behavior for a specific <see cref="OperationOutcome" />.</summary>
	[Serializable]
	public sealed class OutcomeRule
	{
		/// <summary>Gets or sets which operation outcome this rule describes.</summary>
		public OperationOutcome outcome;

		/// <summary>Gets or sets the headline verdict string.</summary>
		public string verdict;

		/// <summary>Gets or sets the narrative body; may include placeholders such as {aftermath}.</summary>
		[TextArea] public string narrative;

		/// <summary>Gets or sets whether rewards, completion, and lore are banked for this outcome.</summary>
		public bool bankRewards, recordCompletion, bankLore;

		/// <summary>Gets or sets the number of in-game days to advance when this outcome applies.</summary>
		public int dayAdvance;
	}

	/// <summary>Scriptable career progression, trace limits, simulation speeds, and default outcome handling.</summary>
	[CreateAssetMenu(menuName = "Hack The Planet/Career Rules")]
	public sealed class CareerDefinition : GameplayDefinition
	{
		/// <summary>Gets or sets starting credits (default 600), XP, calendar day (default 1), and report/log limits.</summary>
		public int credits = 600, xp, day = 1, reportLimit = 20, logLimit = 100;

		/// <summary>Gets or sets whether research, hardware, and equip are allowed during active operations.</summary>
		public bool researchDuringOperations, hardwareDuringOperations, equipDuringOperations;

		/// <summary>Gets or sets whether in-flight tasks can be cancelled (default true).</summary>
		public bool allowTaskCancellation = true;

		/// <summary>
		///     Gets or sets task refund fraction (default 1), trace limit (100), warning (60), danger (85), and max tick
		///     duration (0.5).
		/// </summary>
		public float refundFraction = 1, traceLimit = 100, traceWarning = 60, traceDanger = 85, maxTick = .5f;

		/// <summary>Gets or sets welcome message shown at career start.</summary>
		public string welcome;

		/// <summary>Gets or sets default outcome rules for delivered, aborted, and caught operations.</summary>
		public OutcomeRule[] outcomes =
		{
			new()
			{
				outcome = OperationOutcome.Delivered, verdict = "CONTRACT COMPLETE", narrative = "{aftermath}",
				bankRewards = true, recordCompletion = true, bankLore = true, dayAdvance = 1
			},
			new()
			{
				outcome = OperationOutcome.Aborted, verdict = "CONTRACT ABORTED",
				narrative =
					"You disconnected before securing every required file. Your equipment and research are safe; the contract remains available."
			},
			new()
			{
				outcome = OperationOutcome.Caught, verdict = "CONNECTION ISOLATED",
				narrative =
					"The defender isolated your session. Equipment and research are safe. The contract can be retried with a different route."
			}
		};

		/// <summary>Gets or sets available simulation speed multipliers (default 1, 2, 4).</summary>
		public float[] simulationSpeeds = { 1, 2, 4 };

		/// <summary>Gets or sets technologies unlocked at career start.</summary>
		public TechnologyDefinition[] initialTechnologies = Array.Empty<TechnologyDefinition>();

		/// <summary>Gets or sets initial named career stats.</summary>
		public NamedStat[] stats = Array.Empty<NamedStat>();

		/// <summary>Gets or sets rank thresholds and titles.</summary>
		public CareerRank[] ranks = Array.Empty<CareerRank>();
	}
}