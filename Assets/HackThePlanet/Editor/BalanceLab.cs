using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HackThePlanet.Editor
{
	/// <summary>One simulated contract run used to compare loadouts and routes.</summary>
	[Serializable]
	public sealed class BalanceTrial
	{
		/// <summary>Gets or sets the loadout name (for example Stock or Stealth).</summary>
		public string build;

		/// <summary>Gets or sets the approach label (Exposed or Maintenance).</summary>
		public string route;

		/// <summary>Gets or sets contract difficulty tier used for the trial.</summary>
		public int difficulty;

		/// <summary>Gets or sets generation seed for the trial contract.</summary>
		public int seed;

		/// <summary>Gets or sets how many actions executed before delivery or failure.</summary>
		public int actions;

		/// <summary>Gets or sets whether required objectives were delivered.</summary>
		public bool success;

		/// <summary>Gets or sets whether delivery finished under the quiet trace limit.</summary>
		public bool quiet;

		/// <summary>Gets or sets elapsed simulation seconds on success.</summary>
		public float seconds;

		/// <summary>Gets or sets final trace percentage reached during the run.</summary>
		public float trace;
	}

	/// <summary>Aggregate balance measurement output written to Artifacts/balance-*.json.</summary>
	[Serializable]
	public sealed class BalanceReport
	{
		/// <summary>Gets or sets individual route trials collected during measurement.</summary>
		public List<BalanceTrial> trials = new();
	}

	/// <summary>Simulates legal action sequences across seeds and loadouts to measure route balance.</summary>
	public static class BalanceLab
	{
		/// <summary>Captures balance metrics to Artifacts/balance-before.json before refinements.</summary>
		public static void CaptureBefore()
		{
			// Write the pre-refinement balance measurement.
			Capture("before");
		}

		/// <summary>Captures balance metrics to Artifacts/balance-after.json.</summary>
		public static void CaptureAfter()
		{
			// Write the post-refinement balance measurement.
			Capture("after");
		}

		/// <summary>Applies <see cref="BalanceRefinement" /> then records an after capture.</summary>
		public static void ApplyAndMeasure()
		{
			// Apply the balance refinements to the gameplay content.
			BalanceRefinement.Apply();

			// Record the balance measurement after the refinements.
			CaptureAfter();
		}

		/// <summary>Menu entry that runs <see cref="CaptureAfter" />.</summary>
		[MenuItem("Hack The Planet/Balance/Measure Route Balance")]
		public static void CaptureAfterMenu()
		{
			// Run the after-refinement capture from the menu.
			CaptureAfter();
		}

		// Simulates balance trials and writes JSON and summary artifacts with the given suffix.
		private static void Capture(string suffix)
		{
			// Start an empty report that will collect every simulated trial.
			var report = new BalanceReport();

			// Define the five loadouts to test, with the research each one needs.
			string[][] builds =
			{
				Array.Empty<string>(), new[] { "wide", "signal" }, new[] { "efficient", "selective" },
				new[] { "quiet", "ghost" }, new[] { "disrupt", "blackout" }
			};

			// Name each loadout in the same order as the builds list.
			string[] names = { "Stock", "Recon", "Intrusion", "Stealth", "Control" };

			// Simulate every difficulty, seed, loadout, and route combination.
			for (var difficulty = 1; difficulty <= 3; difficulty++)
				for (var seed = 0; seed < 20; seed++)
					for (var b = 0; b < builds.Length; b++)
						foreach (var fast in new[] { true, false })
							report.trials.Add(Play(names[b], builds[b], difficulty, seed, fast));

			// Save the raw trial data as JSON.
			File.WriteAllText("Artifacts/balance-" + suffix + ".json", JsonUtility.ToJson(report, true));

			// Start the summary table with a header row.
			var lines = new List<string> { "Build | Difficulty | Route | Success | Mean seconds | Mean trace" };

			// Add one summary row per loadout, difficulty, and route group.
			foreach (var group in report.trials.GroupBy(t => new { t.build, t.difficulty, t.route }))
				lines.Add(group.Key.build + " | " + group.Key.difficulty + " | " + group.Key.route + " | " +
						  group.Count(t => t.success) + "/" + group.Count() + " | " +
						  group.Average(t => t.seconds).ToString("0.0") + " | " +
						  group.Average(t => t.trace).ToString("0.0"));

			// Save the summary table as text.
			File.WriteAllLines("Artifacts/balance-" + suffix + ".txt", lines);

			// Log how many legal action sequences were measured.
			Debug.Log("Balance measurement completed: " + report.trials.Count + " legal action sequences.");
		}

		// Plays one scripted contract approach for a loadout and records timing and trace.
		private static BalanceTrial Play(string name, string[] technologies, int difficulty, int seed, bool fast)
		{
			// Load the default catalog and create a career for this trial.
			var catalog = GameCatalog.Default;
			var career = catalog.CreateCareer();

			// Give the career enough XP to buy the loadout's research.
			career.xp = 10000;

			// Create the simulation and buy each technology in the loadout.
			var sim = new GameSimulation(career);

			foreach (var id in technologies)
				if (!sim.BuyTech(id))
					throw new InvalidOperationException("Cannot create balance build " + id);

			// Pick the first one-off contract and apply this trial's difficulty and seed.
			var job = catalog.Contracts(sim.Career).First(c => !c.recurring);
			job.difficulty = difficulty;
			job.seed = seed;

			// Generate the network for the contract.
			var network = NetworkGenerator.Generate(job, career);

			// Start the operation on that contract.
			sim.Start(job);

			// Create the trial record that will hold the results.
			var trial = new BalanceTrial
				{ build = name, difficulty = difficulty, seed = seed, route = fast ? "Exposed" : "Maintenance" };

			// Execute an action, then tick its task until it finishes.
			void Run(string action, string id)
			{
				// Stop if the operation is no longer active.
				if (!sim.Active)
					return;

				// Skip the action when the preview says it is not allowed.
				var preview = sim.Preview(action, id);

				if (!preview.allowed)
					return;

				// Execute the action and fail the trial if the simulation rejects it.
				if (!sim.Execute(action, id))
					throw new InvalidOperationException("Balance action rejected.");

				// Count the action toward the trial total.
				trial.actions++;

				// Advance the task in quarter-second steps, with a guard against stalls.
				var guard = 0;
				while (sim.Active && sim.Op.task != null && guard++ < 4000) sim.Tick(.25f);

				// Fail the trial if the task never finished.
				if (guard >= 4000)
					throw new InvalidOperationException("Balance task stalled.");
			}

			// Walk the chosen approach route, skipping the starting node.
			foreach (var id in (fast ? network.approaches.First() : network.approaches.Last()).Skip(1))
			{
				// Stop once the operation is no longer active.
				if (!sim.Active)
					break;

				// Scan the node first for recon and control loadouts when the scan is allowed.
				if ((name == "Recon" || (name == "Control" && sim.Node(id).kind == NodeKind.Firewall)) &&
					sim.Preview("scan", id).allowed)
					Run("scan", id);

				// Control loadouts also disrupt or blackout each uncontrolled target.
				if (name == "Control")
					foreach (var target in sim.Op.nodes.Where(n => !n.controlled && n.suppressed <= 0).ToArray())
					{
						// Find a disrupt or blackout action for this target.
						var action = sim.AvailableActions(target.id)
										.FirstOrDefault(a => a.id == "disrupt" || a.id == "blackout");

						// Run it when the preview allows it.
						if (action != null && sim.Preview(action.id, target.id).allowed)
							Run(action.id, target.id);
					}

				// Hack the node on the route.
				Run("hack", id);
			}

			// Download every objective node while the operation is still active.
			if (sim.Active)
				foreach (var node in sim.Op.nodes.Where(n => n.objective).ToArray())
					Run("download", node.id);

			// Record the outcome: a completed run keeps its time and trace, a failed run is marked unsuccessful.
			if (sim.Active)
			{
				// Save the elapsed time, final trace, and whether the objectives were met.
				trial.seconds = sim.Op.elapsed;
				trial.trace = sim.Op.trace;
				trial.success = sim.ObjectiveComplete;
				trial.quiet = trial.success && trial.trace < job.quietLimit;

				// Disconnect from the network now that the trial is recorded.
				sim.Disconnect();
			}
			else
			{
				// Record the trace reached and mark the trial as failed.
				trial.trace = sim.LastReport.trace;
				trial.seconds = 0;
				trial.success = false;
			}

			return trial;
		}
	}
}