using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HackThePlanet.Editor
{
	/// <summary>Play-style assertions for refined tools, research, and career progression after balance changes.</summary>
	public static class BalanceValidation
	{
		/// <summary>Runs data-driven validation plus refined-career simulation checks and writes artifact logs.</summary>
		[MenuItem("Hack The Planet/Balance/Validate Refined Career")]
		public static void Run()
		{
			// Run the data-driven content checks first.
			DataDrivenValidation.Run();

			// Collect a PASS line for every balance check that succeeds.
			var results = new List<string>();

			// Throw on the first failed balance check, otherwise log it as passing.
			void Check(bool condition, string message)
			{
				// Stop the run if the balance condition is false.
				if (!condition)
					throw new InvalidOperationException("BALANCE FAILED / " + message);

				// Record the passing check for the artifact log.
				results.Add("PASS / " + message);
			}

			// Build a simulation with the requested research bought and a contract started.
			GameSimulation Start(params string[] tech)
			{
				// Create a fresh simulation with generous XP and credits.
				var sim = new GameSimulation();
				sim.Career.xp = 10000;
				sim.Career.credits = 10000;

				// Buy each requested technology, checking every purchase succeeds.
				foreach (var id in tech)
					Check(sim.BuyTech(id), "research " + id);

				// Start the first one-off contract from the catalog.
				sim.Start(sim.Catalog.Contracts(sim.Career).First(c => !c.recurring));

				return sim;
			}

			// Run the scenario checks, logging any failure to the artifact before rethrowing.
			try
			{
				// Check that scanning makes the hack faster and cleaner before any research.
				var scout = Start();

				// Capture the raw hack numbers before the scan.
				float rawTime = scout.Preview("hack", "n2").duration, rawTrace = scout.Preview("hack", "n2").trace;

				// Scan the target and finish the scan task.
				scout.Execute("scan", "n2");
				GameValidation.FinishTask(scout);

				// Verify the scan improves the hack and then disappears from the action list.
				Check(scout.Preview("hack", "n2").duration < rawTime && scout.Preview("hack", "n2").trace < rawTrace,
					  "scanning prepares a faster and cleaner hack before any research");

				Check(!scout.AvailableActions("n2").Any(a => a.id == "scan"),
					  "completed scans disappear from the action list");

				// Start a stop-and-replan scenario with efficient and chain research.
				var stop = Start("efficient", "chain");
				stop.Execute("scan", "n2");
				GameValidation.FinishTask(stop);
				stop.Execute("hack", "n2");
				stop.Execute("hack", "n6");
				stop.Tick(.5f);
				var trace = stop.Op.trace;

				// Verify running work can be stopped and that stopping clears pending work.
				Check(trace > 0 && stop.StopTask(), "running work can be stopped");

				Check(
					stop.Op.task == null && stop.Op.queue.Count == 0 && stop.Op.trace == trace &&
					!stop.Node("n2").controlled,
					"stopping clears pending work without refunding trace or granting ownership");

				// Verify stopping again does nothing.
				stop.Tick(.5f);

				Check(stop.Op.trace == trace && !stop.StopTask(),
					  "stopping returns to free planning and is idempotent");

				// Verify the forecast warns when a hack would exceed the trace limit.
				stop.Op.trace = 99;
				var forecast = stop.Preview("hack", "n2");

				Check(forecast.willIsolate && forecast.projectedTrace >= stop.TraceLimit,
					  "action forecast warns before exceeding trace limit");

				// Verify the forecast reports when a hack would lose the quiet bonus.
				stop.Op.trace = stop.Job.quietLimit - 1;
				Check(stop.Preview("hack", "n2").losesQuietBonus, "forecast distinguishes losing the quiet bonus");

				// Start a shielded scenario where decoy is used under high trace.
				var shielded = Start("disrupt", "decoy");
				shielded.Op.trace = 99;
				shielded.Execute("decoy");
				var protectedForecast = shielded.Preview("hack", "n2");

				// Verify the forecast keeps trace pressure while decoy reserves RAM.
				Check(protectedForecast.projectedTrace > shielded.Op.trace && shielded.MemoryReserved == 16,
					  "forecast retains trace pressure while decoy reserves RAM");

				// Start a bridge scenario with a limited number of charges.
				var bridge = Start("wide", "efficient", "bridge");
				bridge.Node("n6").known = true;
				bridge.Node("n6").controlled = true;
				bridge.Node("n10").known = true;

				// Verify the bridge needs prepared intel before it can be used.
				Check(!bridge.Preview("bridge", "n10").allowed, "bridge needs prepared intel");
				bridge.Node("n10").scanned = true;

				Check(bridge.Preview("bridge", "n10").allowed,
					  "one controlled neighbor makes a prepared bridge useful");

				// Spend the first bridge charge and set up the second target.
				bridge.AddCounter("uses:bridge", 1);
				bridge.Execute("bridge", "n10");
				bridge.Node("n14").known = true;
				bridge.Node("n14").controlled = true;
				bridge.Node("n18").known = true;
				bridge.Node("n18").scanned = true;

				// Verify the last charge cannot be reserved twice.
				Check(!bridge.CanQueue("bridge", "n18"), "the last bridge charge cannot be reserved twice");

				// Finish the bridge work, then confirm the charges are used up.
				GameValidation.FinishTask(bridge);

				Check(bridge.UsesRemaining("bridge") == 0 && !bridge.Preview("bridge", "n18").allowed,
					  "bridge charges remain bounded across targets");

				// Start a ranged backdoor scenario along a generated route.
				var ranged = Start("quiet", "backdoor");
				var route = NetworkGenerator.Generate(ranged.Job, ranged.Career).approaches.Last();
				var far = ranged.Node(route[route.Count - 2]);
				far.known = true;

				// Verify a revealed isolated endpoint cannot be skipped to.
				Check(!ranged.Preview("backdoor", far.id).allowed,
					  "a revealed isolated endpoint cannot skip an entire mission");

				// Scan the entry node and finish the work so the close route node is reachable.
				ranged.Execute("scan", "n2");
				GameValidation.FinishTask(ranged);

				// Pick the close route node used for the shortcut check.
				var close = ranged.Node(route[3]);

				// Verify backdoor still reaches a node three hops away.
				Check(ranged.ControlledDistance(close) == 3 && ranged.Preview("backdoor", close.id).allowed,
					  "backdoor keeps its tactical three-hop shortcut");

				// Start a controller scenario that uses disrupt and blackout.
				var controller = Start("disrupt", "blackout");
				controller.Execute("hack", "n2");
				GameValidation.FinishTask(controller);
				controller.Execute("scan", "n6");
				GameValidation.FinishTask(controller);

				// Verify blackout can reach a mapped hub behind a protected firewall.
				Check(!controller.Reachable(controller.Node("n14")) && controller.Preview("blackout", "n14").allowed,
					  "blackout can reach a mapped hub before crossing its protected firewall");

				// Record the security level, then run blackout remotely.
				var security = controller.EffectiveSecurity(controller.Node("n10"));
				controller.Execute("blackout", "n14");
				GameValidation.FinishTask(controller);

				// Verify blackout immediately lowers the security of the target.
				Check(controller.EffectiveSecurity(controller.Node("n10")) < security,
					  "remote blackout produces an immediate tactical benefit");

				// Start a hardware scenario with no research.
				var hardware = new GameSimulation();

				// Verify advanced power modules are locked until researched.
				Check(!hardware.PreviewModule("power-2").allowed, "advanced power modules require research");

				// Give the hardware scenario enough XP and credits to buy research.
				hardware.Career.xp = 100;
				hardware.Career.credits = 1000;

				// Verify infrastructure research unlocks the next module tier.
				Check(hardware.BuyTech("infrastructure-2") && hardware.PreviewModule("power-2").allowed,
					  "infrastructure research unlocks module tiers");

				// Run the rig payload balance checks using this file's Check helper.
				RigValidation.ValidatePayloadBalance(Check);

				// Start a research-spend scenario to verify XP investment tracking.
				var spent = new GameSimulation();
				spent.Career.xp = 100;

				// Verify purchases record the XP that was actually spent.
				Check(spent.BuyTech("quiet") && spent.Career.researchSpent == 45,
					  "new purchases record their actual XP investment");

				// Verify research spend survives a save/load round trip and can be refunded.
				var roundtrip = new GameSimulation(JsonUtility.FromJson<Career>(JsonUtility.ToJson(spent.Career)));

				Check(roundtrip.Career.researchSpent == 45 && roundtrip.RefundResearch() && roundtrip.Career.xp == 100,
					  "paid research investment survives save/load and refund");

				// Work out the XP available from the first two quiet contracts.
				var firstTwoXp = GameContent.Story.Take(2).Sum(c => c.xp) +
								 GameCatalog.Default.Data.rewards.quietXp * 2;

				// Verify each starting build branch costs no more than those two contracts pay.
				foreach (var branch in new[]
						 {
							 new[] { "wide", "signal" }, new[] { "efficient", "selective" }, new[] { "quiet", "ghost" },
							 new[] { "disrupt", "blackout" }
						 })
					Check(branch.Sum(id => GameContent.FindTech(id).cost) <= firstTwoXp,
						  "two successful quiet contracts can establish " + branch[0] +
						  " build without archive grinding");

				// Verify the archive XP cap covers the third archive at full value.
				Check(GameCatalog.Default.Data.rewards.archiveXpCap >= GameCatalog.Default.Data.rewards.archiveXp * 3,
					  "the third archive earns full XP");

				// Capture the measured balance trials for the statistical checks below.
				BalanceLab.CaptureAfter();
				var measured = JsonUtility.FromJson<BalanceReport>(File.ReadAllText("Artifacts/balance-after.json"));

				// Verify the sample size and that every sampled sequence completes.
				Check(measured.trials.Count == 600 && measured.trials.All(t => t.success && t.trace < 100),
					  "600 sampled build, seed and route sequences complete without unavoidable isolation");

				// Compare each loadout and route against the stock baseline at every difficulty.
				foreach (var difficulty in new[] { 1, 2, 3 })
				{
					// Filter the measured trials to one build, route, and difficulty.
					BalanceTrial[] Trials(string build, string routeName)
					{
						return measured.trials
									   .Where(t => t.build == build && t.route == routeName &&
												   t.difficulty == difficulty).ToArray();
					}

					var stockFast = Trials("Stock", "Exposed");
					var stockSafe = Trials("Stock", "Maintenance");

					// Verify the exposed route is faster but noisier than the maintenance route.
					Check(
						stockFast.Average(t => t.seconds) < stockSafe.Average(t => t.seconds) &&
						stockFast.Average(t => t.trace) > stockSafe.Average(t => t.trace),
						"route speed/exposure trade-off at difficulty " + difficulty);

					// Verify intrusion buys speed.
					Check(Trials("Intrusion", "Exposed").Average(t => t.seconds) < stockFast.Average(t => t.seconds),
						  "intrusion buys speed at difficulty " + difficulty);

					// Verify recon preparation buys safety.
					Check(Trials("Recon", "Maintenance").Average(t => t.trace) < stockSafe.Average(t => t.trace),
						  "recon preparation buys safety at difficulty " + difficulty);

					// Verify stealth reduces trace on the low-exposure route.
					Check(Trials("Stealth", "Maintenance").Average(t => t.trace) < stockSafe.Average(t => t.trace),
						  "stealth rewards a low-exposure route at difficulty " + difficulty);

					// Verify control reduces trace on the exposed route.
					Check(Trials("Control", "Exposed").Average(t => t.trace) < stockFast.Average(t => t.trace),
						  "control changes the exposed route at difficulty " + difficulty);
				}

				// Write the total and the full PASS log to the artifact file.
				results.Add("TOTAL / " + results.Count + " balance checks passed.");
				File.WriteAllLines("Artifacts/balance-validation.txt", results);
				Debug.Log(results.Last());
			}
			catch (Exception ex)
			{
				// Log the exception to the artifact file before rethrowing it.
				results.Add(ex.ToString());
				File.WriteAllLines("Artifacts/balance-validation.txt", results);
				throw;
			}
		}

		/// <summary>Applies balance refinements to content, then runs the full refined-career validation suite.</summary>
		public static void ApplyAndValidate()
		{
			// Apply the balance refinements to the gameplay content first.
			BalanceRefinement.Apply();

			// Then run the full refined-career validation suite.
			Run();
		}

		/// <summary>Runs balance validation and launches the headless smoke build to catch regressions.</summary>
		public static void ValidateAndSmoke()
		{
			// Run the balance validation suite.
			Run();

			// Launch the headless smoke build to catch regressions.
			GameBuildTools.StartSmoke();
		}
	}
}