using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace HackThePlanet.Editor
{
	[Serializable]
	public sealed class CareerBalanceTrial
	{
		public string contractId, profile, failure, module;
		public int difficulty, seed, actions, creditsSpent, xpSpent, creditsEarned, xpEarned;
		public bool success, quiet;
		public float seconds, trace, powerHeadroom, coolingHeadroom, ramHeadroom, storageHeadroom;
		public List<string> usedActions = new();
	}

	[Serializable]
	public sealed class CareerBalanceReport
	{
		public int seedCount, policyVersion = 1, generatorVersion = NetworkGenerator.Version;
		public string contentFingerprint, unityVersion;
		public List<CareerBalanceTrial> trials = new();
		public List<CareerBalanceTrial> careerTrials = new();
		public List<string> findings = new();
	}

	/// <summary>Deterministic career, defense, route, and rig balance measurements.</summary>
	public static class BalanceSuite
	{
		private sealed class Profile
		{
			public string name, keyMode, sweepMode, module;
			public string[] tech;
			public Profile(string name, string keyMode, string sweepMode, string module, params string[] tech)
			{
				this.name = name; this.keyMode = keyMode; this.sweepMode = sweepMode;
				this.module = module; this.tech = tech;
			}
		}

		private static readonly Profile[] Profiles =
		{
			new("Stock", "", "", ""),
			new("Recon", "extract", "", "", "wide", "credential-analysis"),
			new("Intrusion", "spoof", "", "", "efficient", "protocol-spoof"),
			new("Recon + Loop", "extract", "loop", "", "wide", "credential-analysis", "quiet", "loop-feed"),
			new("Intrusion + Reset", "spoof", "reset", "", "efficient", "protocol-spoof", "disrupt", "hard-reset"),
			new("Recon + Reset", "extract", "reset", "", "wide", "credential-analysis", "disrupt", "hard-reset"),
			new("Intrusion + Loop", "spoof", "loop", "", "efficient", "protocol-spoof", "quiet", "loop-feed"),
			new("Decoder Rig", "extract", "loop", "decoder-board", "wide", "credential-analysis", "quiet", "loop-feed", "data-engineering"),
			new("Spoof Rig", "spoof", "reset", "spoof-asic", "efficient", "protocol-spoof", "disrupt", "hard-reset", "parallel-engineering"),
			new("Veil Rig", "extract", "loop", "veil-array", "wide", "credential-analysis", "quiet", "loop-feed", "signal-engineering"),
			new("Isolation Rig", "spoof", "reset", "isolation-bank", "efficient", "protocol-spoof", "disrupt", "hard-reset", "infrastructure-2")
		};

		[MenuItem("Hack The Planet/Balance/Run Career Balance Suite")]
		public static void RunMenu() => VesperValidation.Run();

		/// <summary>Unity batch entry; pass -balanceFull for twenty seeds rather than five.</summary>
		public static void RunBatch() => VesperValidation.RunBatch();

		/// <summary>Runs the existing content, simulation, rig, and tool regression suites.</summary>
		public static void ValidateIntegration()
		{
			ContentMigration.Validate();
			ContentValidation.Run();
			GameValidation.Run();
			BalanceValidation.Run();
			RigValidation.Run();
		}

		/// <summary>Completes the shipped story with earned base rewards and returns its career for persistence checks.</summary>
		public static GameSimulation ValidateBaseCareer(Action<bool, string> check)
		{
			var catalog = GameCatalog.Default;
			var career = catalog.CreateCareer();
			var profile = Profiles.First(p => p.name == "Recon + Loop");
			foreach (var definition in catalog.Data.contracts.Where(c => !c.recurring).OrderBy(c => c.requiredWins))
			{
				var job = catalog.DescribeContract(definition, career);
				var trial = Play(catalog, definition, job.difficulty, job.seed, profile, career);
				check(trial.success, "Base-reward career / " + definition.title + " / " + trial.failure);
			}
			return new GameSimulation(catalog, career);
		}

		public static CareerBalanceReport Run(bool full)
		{
			var report = new CareerBalanceReport();
			var catalog = GameCatalog.Default;
			var seeds = full ? 20 : 5;
			report.seedCount = seeds;
			report.unityVersion = Application.unityVersion;
			using (var hash = SHA256.Create())
				report.contentFingerprint = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(
					string.Join("\n", GameDefinitionValidator.Definitions(catalog.Data).OrderBy(d => d.GetType().Name + "/" + d.id)
						.Select(ContentJson.Encode))))).Replace("-", "");
			var contracts = catalog.Data.contracts;
			foreach (var definition in contracts)
			{
				foreach (var difficulty in definition.recurring ? new[] { 1, 2, 3 } :
					new[] { catalog.DescribeContract(definition, catalog.CreateCareer()).difficulty })
					for (var seed = 0; seed < seeds; seed++)
						foreach (var profile in Profiles)
							report.trials.Add(Play(catalog, definition, difficulty, seed, profile));
				Debug.Log("Career balance measured / " + definition.id + " / " + report.trials.Count + " trials");
			}
			for (var seed = 0; seed < seeds; seed++)
				foreach (var profile in Profiles.Where(p => p.name == "Recon + Loop" || p.name == "Intrusion + Reset"))
				{
					var career = catalog.CreateCareer();
					foreach (var definition in contracts.Where(c => !c.recurring).OrderBy(c => c.requiredWins))
					{
						var trial = Play(catalog, definition, catalog.DescribeContract(definition, career).difficulty,
							seed, profile, career);
						report.careerTrials.Add(trial);
						if (!trial.success) break;
					}
				}

			Assess(report, catalog, full);
			try { report.findings.AddRange(UpgradeBalanceAudit.Run(catalog)); }
			catch (Exception ex) { report.findings.Add("FAIL / Upgrade audit: " + ex.Message); }
			Directory.CreateDirectory("Artifacts");
			var json = JsonUtility.ToJson(report, true);
			var summary = Summary(report, full).ToArray();
			File.WriteAllText("Artifacts/career-balance.json", json);
			File.WriteAllLines("Artifacts/career-balance.txt", summary);
			File.WriteAllText("Artifacts/career-balance-" + (full ? "full" : "quick") + ".json", json);
			File.WriteAllLines("Artifacts/career-balance-" + (full ? "full" : "quick") + ".txt", summary);
			if (report.findings.Any(f => f.StartsWith("FAIL / ")))
				throw new InvalidOperationException("Career balance gates failed. See Artifacts/career-balance.txt");
			Debug.Log("Career balance suite passed: " + report.trials.Count + " isolated trials and " +
				report.careerTrials.Count + " base-payout career stages.");
			return report;
		}

		private static CareerBalanceTrial Play(GameCatalog catalog, ContractDefinition definition, int difficulty,
			int seed, Profile profile, Career carriedCareer = null)
		{
			try { return PlayCore(catalog, definition, difficulty, seed, profile, carriedCareer); }
			catch (Exception ex)
			{
				return new CareerBalanceTrial { contractId = definition.id, difficulty = difficulty, seed = seed,
					profile = profile.name, module = profile.module, failure = "Exception: " + ex };
			}
		}

		private static CareerBalanceTrial PlayCore(GameCatalog catalog, ContractDefinition definition, int difficulty,
			int seed, Profile profile, Career carriedCareer)
		{
			var career = carriedCareer ?? catalog.CreateCareer();
			if (carriedCareer == null)
			{
				career.xp = 10000;
				career.credits = 10000;
				var predecessors = catalog.Data.contracts.Where(c => !c.recurring && c.requiredWins < definition.requiredWins)
					.OrderBy(c => c.requiredWins).Select(c => c.id).ToArray();
				career.completed.AddRange(predecessors);
				career.jobs = Math.Max(3, predecessors.Length);
			}
			var startingXp = career.xp;
			var startingCredits = career.credits;
			var sim = new GameSimulation(catalog, career);
			var trial = new CareerBalanceTrial { contractId = definition.id, difficulty = difficulty,
				seed = seed, profile = profile.name, module = profile.module };
			foreach (var id in profile.tech)
				if (!sim.Has(id) && !sim.BuyTech(id) && carriedCareer == null)
				{ trial.failure = "Cannot buy " + id; return trial; }
			if (profile.sweepMode == "reset" && sim.Equipped("disrupt"))
				sim.ToggleEquip("disrupt");
			if (profile.sweepMode == "reset" && !sim.Equipped("hard-reset"))
				sim.ToggleEquip("hard-reset");
			trial.xpSpent = startingXp - career.xp;
			if (!string.IsNullOrEmpty(profile.module))
			{
				if (!sim.UpgradeChassis() || !sim.UpgradeChassis())
					{ trial.failure = "Cannot expand chassis"; return trial; }
				var part = sim.BuyModule(profile.module);
				if (part == null || !sim.PlaceFirstFree(part.instanceId))
					{ trial.failure = "Cannot install " + profile.module; return trial; }
			}
			trial.creditsSpent = startingCredits - career.credits;
			trial.powerHeadroom = sim.PowerLimit - sim.PowerUsed;
			trial.coolingHeadroom = sim.CoolingLimit - sim.HeatUsed;
			var job = catalog.DescribeContract(definition, career);
			job.difficulty = difficulty;
			job.credits = Mathf.RoundToInt(definition.credits.Evaluate(catalog.Context(career)) * job.RewardMultiplier);
			job.xp = Mathf.RoundToInt(definition.xp.Evaluate(catalog.Context(career)) * job.RewardMultiplier);
			job.seed = seed;
			if (!sim.Start(job)) { trial.failure = "Cannot start contract"; return trial; }
			trial.ramHeadroom = sim.MemoryFree;
			trial.storageHeadroom = sim.FreeStorage;

			bool RunAction(string action, string target = null)
			{
				if (!sim.Active) { trial.failure = "Caught by trace"; return false; }
				var preview = sim.Preview(action, target);
				if (!preview.allowed) { trial.failure = action + ": " + preview.reason;
					trial.seconds = sim.Op.elapsed; trial.trace = sim.Op.trace; return false; }
				if (!sim.Execute(action, target)) { trial.failure = "Execute rejected " + action; return false; }
				trial.actions++;
				trial.usedActions.Add(action);
				trial.ramHeadroom = Mathf.Min(trial.ramHeadroom, sim.MemoryFree);
				for (var guard = 0; sim.Active && sim.Op.task != null; guard++)
				{
					if (guard > 10000) { trial.failure = "Task stalled"; return false; }
					trial.seconds = sim.Op.elapsed;
					trial.trace = sim.Op.trace;
					sim.Tick(.5f);
					if (sim.Active)
					{
						trial.ramHeadroom = Mathf.Min(trial.ramHeadroom, sim.MemoryFree);
						trial.storageHeadroom = Mathf.Min(trial.storageHeadroom, sim.FreeStorage);
					}
				}
				if (!sim.Active) { trial.failure = "Caught by trace"; trial.trace = sim.LastReport.trace; trial.seconds += .5f; return false; }
				return true;
			}

			List<NetNode> Route(NetNode goal)
			{
				var pending = new HashSet<string>(sim.Op.nodes.Select(n => n.id));
				var distance = sim.Op.nodes.ToDictionary(n => n.id, n => float.PositiveInfinity);
				var previous = new Dictionary<string, string>();
				distance[goal.id] = 0;
				previous[goal.id] = null;
				while (pending.Count > 0)
				{
					var id = pending.OrderBy(n => distance[n]).First();
					if (float.IsPositiveInfinity(distance[id])) break;
					pending.Remove(id);
					var node = sim.Node(id);
					if (node.controlled)
					{
						var path = new List<NetNode> { node };
						while (path.Last().id != goal.id)
							path.Add(sim.Node(previous[path.Last().id]));
						return path;
					}
					foreach (var neighbor in sim.Neighbors(node))
						if (pending.Contains(neighbor.id))
						{
							var cost = 1 + node.security * .7f +
								(node.kind == NodeKind.Scanner || node.kind == NodeKind.Firewall ? 3 : 0);
							if (distance[id] + cost >= distance[neighbor.id]) continue;
							distance[neighbor.id] = distance[id] + cost;
							previous[neighbor.id] = id;
						}
				}
				return null;
			}

			bool Approach(string target, bool control)
			{
				for (var guard = 0; guard < 100; guard++)
				{
					var goal = sim.Node(target);
					if (goal == null) { trial.failure = "Missing target " + target; return false; }
					if (control ? goal.controlled : goal.known && sim.ControlledDistance(goal) <= 3) return true;
					var path = Route(goal);
					if (path == null || path.Count < 2) { trial.failure = "No route to " + target; return false; }
					var current = path[0]; var next = path[1];
					if (!next.known && sim.Preview("scan", current.id).allowed && !RunAction("scan", current.id)) return false;
					if (!control && !goal.known && next.known && sim.Preview("scan", next.id).allowed &&
						!RunAction("scan", next.id)) return false;
					if (!control && goal.known && sim.ControlledDistance(goal) <= 3) return true;
					if (!next.known) { trial.failure = "Undiscovered route to " + target; return false; }
					if (!next.controlled && !RunAction("hack", next.id)) return false;
				}
				trial.failure = "Route search exhausted";
				return false;
			}

			var active = sim.Defenses.ToArray();
			if (active.Any(d => d.kind == DefenseKind.SecuritySweep) && profile.sweepMode == "loop" && sim.Equipped("loop-feed") &&
				!RunAction("loop-feed")) return trial;
			if (active.Any(d => d.kind == DefenseKind.SecuritySweep) && profile.sweepMode == "reset" && sim.Equipped("hard-reset"))
				foreach (var d in active.Where(d => d.kind == DefenseKind.SecuritySweep))
				{
					if (!Approach(d.sourceNodeId, false)) return trial;
					if (!RunAction("hard-reset", d.sourceNodeId)) return trial;
				}
			if (profile.keyMode == "extract" && sim.Equipped("credential-analysis"))
				foreach (var source in active.Where(d => d.kind == DefenseKind.CredentialLock &&
					(carriedCareer == null || d.targetNodeIds.Any(id => definition.objectives.Any(o => o.nodeId == id))))
					.Select(d => d.sourceNodeId).Distinct())
					if (!Approach(source, true) || !RunAction("extract-key", source)) return trial;

			for (var pass = 0; pass < definition.objectives.Length + 1 && !sim.ObjectiveComplete; pass++)
				foreach (var objective in definition.objectives)
				{
					var node = sim.Node(objective.nodeId);
					if (sim.ObjectiveDone(node)) continue;
					if (!Approach(node.id, true)) return trial;
					if (sim.IsCredentialLocked(node.id) && profile.keyMode == "spoof" &&
						!RunAction("spoof-gate", node.id)) return trial;
					if (sim.Preview(objective.action.id, node.id).allowed &&
						!RunAction(objective.action.id, node.id)) return trial;
				}

			if (!sim.Active) { trial.failure = "Caught by trace"; return trial; }
			trial.seconds = sim.Op.elapsed;
			trial.trace = sim.Op.trace;
			trial.ramHeadroom = Mathf.Min(trial.ramHeadroom, sim.MemoryFree);
			trial.storageHeadroom = Mathf.Min(trial.storageHeadroom, sim.FreeStorage);
			trial.success = sim.ObjectiveComplete;
			if (!trial.success) trial.failure = "Objectives incomplete";
			var result = sim.Disconnect();
			trial.quiet = result.quietBonusEarned;
			trial.creditsEarned = result.credits;
			trial.xpEarned = result.xp;
			if (carriedCareer != null && result.success)
			{
				career.credits -= result.credits - job.credits;
				career.xp -= result.xp - job.xp;
				career.totalEarnedXp -= result.xp - job.xp;
				trial.creditsEarned = job.credits;
				trial.xpEarned = job.xp;
			}
			return trial;
		}

		private static void Assess(CareerBalanceReport report, GameCatalog catalog, bool full)
		{
			var seeds = full ? 20 : 5;
			foreach (var trial in report.trials.Where(t => t.failure != null &&
				(t.failure.StartsWith("Exception:") || t.failure.StartsWith("Cannot "))))
				report.findings.Add("FAIL / Trial setup or execution error / " + trial.contractId + " / " + trial.profile + " / " + trial.failure);
			foreach (var profile in Profiles.Where(p => p.name == "Recon + Loop" || p.name == "Intrusion + Reset"))
				if (report.careerTrials.Count(t => t.profile == profile.name && t.success) != seeds * catalog.StoryCount)
					report.findings.Add("FAIL / Base-payout career cannot complete all story stages with " + profile.name);
			foreach (var id in new[] { "quiet-copy", "paper-trail" })
				if (report.trials.Count(t => t.contractId == id && t.profile == "Stock" && t.success) != seeds)
					report.findings.Add("FAIL / Starter build cannot complete " + id + " across all seeds.");
			foreach (var group in report.trials.Where(t => t.difficulty >= 2 &&
				catalog.Contract(t.contractId).defenses.Any(d => d.minimumDifficulty <= t.difficulty &&
					d.targetNodeIds.Any(id => catalog.Contract(t.contractId).objectives.Any(o => o.nodeId == id))))
				.GroupBy(t => (t.contractId, t.difficulty)))
			{
				var reliable = group.GroupBy(t => t.profile).Where(p => p.Count(t => t.success) == seeds)
					.Select(p => Profiles.First(x => x.name == p.Key)).ToArray();
				if (!reliable.Any(p => p.keyMode == "extract") || !reliable.Any(p => p.keyMode == "spoof"))
					report.findings.Add("FAIL / Key extraction and spoofing must both solve " + group.Key);
				if (catalog.Contract(group.Key.contractId).defenses.Any(d => d.kind == DefenseKind.SecuritySweep &&
					d.minimumDifficulty <= group.Key.difficulty) &&
					(!reliable.Any(p => p.sweepMode == "loop") || !reliable.Any(p => p.sweepMode == "reset")))
					report.findings.Add("FAIL / Loop Feed and Hard Reset must both solve " + group.Key);
			}
			var firstThree = catalog.Data.contracts.Where(c => !c.recurring).OrderBy(c => c.requiredWins).Take(3)
				.Sum(c => catalog.DescribeContract(c, catalog.CreateCareer()).xp);
			var firstFour = catalog.Data.contracts.Where(c => !c.recurring).OrderBy(c => c.requiredWins).Take(4)
				.Sum(c => catalog.DescribeContract(c, catalog.CreateCareer()).xp);
			foreach (var profile in Profiles.Where(p => p.name == "Recon" || p.name == "Intrusion"))
				if (profile.tech.Sum(id => catalog.Technology(id).cost) > firstThree)
					report.findings.Add("FAIL / " + profile.name + " gate counter is unaffordable before Glasshouse.");
			foreach (var profile in Profiles.Where(p => p.name == "Recon + Loop" || p.name == "Intrusion + Reset"))
				if (profile.tech.Sum(id => catalog.Technology(id).cost) > firstFour)
					report.findings.Add("FAIL / " + profile.name + " defense build is unaffordable before Dead Drop.");
			var firstFourCredits = catalog.Data.career.credits + catalog.Data.contracts.Where(c => !c.recurring)
				.OrderBy(c => c.requiredWins).Take(4).Sum(c => catalog.DescribeContract(c, catalog.CreateCareer()).credits);
			var chassisCost = catalog.Data.chassis.Where(c => c.tier == 2 || c.tier == 3).Sum(c => c.price);
			if (firstFourCredits < chassisCost + catalog.Data.modules.Where(m =>
				new[] { "decoder-board", "spoof-asic", "veil-array", "isolation-bank" }.Contains(m.id)).Min(m => m.price))
				report.findings.Add("FAIL / No tier II defense module is affordable with base story credits.");
			foreach (var pair in new[] { (baseline: "Recon + Loop", rig: "Decoder Rig"),
				(baseline: "Intrusion + Reset", rig: "Spoof Rig"),
				(baseline: "Recon + Loop", rig: "Veil Rig"),
				(baseline: "Intrusion + Reset", rig: "Isolation Rig") })
			{
				var baseTrials = report.trials.Where(t => t.profile == pair.baseline && t.success).ToArray();
				var rigTrials = report.trials.Where(t => t.profile == pair.rig && t.success).ToArray();
				if (baseTrials.Length == 0 || rigTrials.Length == 0) continue;
				if (rigTrials.Average(t => t.seconds) >= baseTrials.Average(t => t.seconds) &&
					rigTrials.Average(t => t.trace) >= baseTrials.Average(t => t.trace))
					report.findings.Add("WARN / " + pair.rig + " is dominated by its unmodified build.");
			}
			foreach (var group in report.trials.Where(t => t.success).GroupBy(t => t.difficulty))
			{
				var median = group.OrderBy(t => t.seconds).ElementAt(group.Count() / 2).seconds;
				var lower = group.Key == 1 ? 60 : group.Key == 2 ? 120 : 240;
				var upper = group.Key == 1 ? 120 : group.Key == 2 ? 240 : 300;
				if (median < lower || median > upper)
					report.findings.Add("FAIL / Difficulty " + group.Key + " median " + median.ToString("0") +
						"s is outside " + lower + "-" + upper + "s.");
				if (group.Key >= 3)
				{
					var times = group.Select(t => t.seconds).OrderBy(x => x).ToArray();
					var p90 = times[Math.Min(times.Length - 1, (int)Math.Ceiling(times.Length * .9) - 1)];
					if (p90 > 360) report.findings.Add("FAIL / Hard 90th percentile exceeds six minutes: " + p90.ToString("0") + "s.");
				}
			}
			foreach (var contract in report.trials.GroupBy(t => (t.contractId, t.difficulty)))
			{
				var builds = contract.GroupBy(t => t.profile).Where(g =>
					{
						var profile = Profiles.First(p => p.name == g.Key);
						return g.All(t => t.success) && string.IsNullOrEmpty(g.First().module) &&
							(profile.sweepMode == "" || g.First().usedActions.Contains(profile.sweepMode == "loop" ? "loop-feed" : "hard-reset"));
					})
					.Select(g => new { name = g.Key, xp = g.First().xpSpent, credits = g.First().creditsSpent,
						time = g.Average(t => t.seconds), trace = g.Average(t => t.trace), ram = g.Average(t => t.ramHeadroom) }).ToArray();
				foreach (var build in builds)
					foreach (var other in builds.Where(b => b.name != build.name && b.xp <= build.xp && b.credits <= build.credits &&
						b.time <= build.time && b.trace <= build.trace && b.ram >= build.ram))
						if (other.xp < build.xp || other.credits < build.credits || other.time < build.time || other.trace < build.trace || other.ram > build.ram)
							report.findings.Add("WARN / Build dominance in " + contract.Key + ": " + other.name + " over " + build.name + ".");
			}
		}

		private static IEnumerable<string> Summary(CareerBalanceReport report, bool full)
		{
			yield return "CAREER BALANCE / " + (full ? "FULL" : "QUICK") + " / " + report.trials.Count + " trials";
			yield return "CONTENT / " + report.contentFingerprint + " / POLICY " + report.policyVersion;
			yield return "BASE-PAYOUT CAREERS / " + report.careerTrials.Count(t => t.success) + "/" + report.careerTrials.Count + " stages completed";
			foreach (var tier in report.trials.Where(t => t.success).GroupBy(t => t.difficulty))
			{
				var times = tier.Select(t => t.seconds).OrderBy(x => x).ToArray();
				yield return "PACING / " + tier.Key + " / median " + times[times.Length / 2].ToString("0.0") +
					"s / p90 " + times[Math.Min(times.Length - 1, (int)Math.Ceiling(times.Length * .9) - 1)].ToString("0.0") + "s";
			}
			foreach (var trial in report.careerTrials.Where(t => !t.success))
				yield return "CAREER FAILURE / " + trial.profile + " / " + trial.contractId + " / seed " + trial.seed + " / " + trial.failure;
			foreach (var group in report.trials.GroupBy(t => (t.contractId, t.difficulty, t.profile)))
				yield return group.Key + " / " + group.Count(t => t.success) + "/" + group.Count() +
					" / mean time " + group.Where(t => t.success).Select(t => t.seconds).DefaultIfEmpty().Average().ToString("0.0") +
					"s / mean trace " + group.Where(t => t.success).Select(t => t.trace).DefaultIfEmpty().Average().ToString("0.0") +
					(group.Any(t => !t.success) ? " / failure " + group.First(t => !t.success).failure : "");
			foreach (var finding in report.findings) yield return finding;
		}
	}
}
