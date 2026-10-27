using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace HackThePlanet.Editor
{
	[Serializable]
	public sealed class UpgradeBalanceSample
	{
		public string id, category, family, capabilities;
		public int cost, cells;
		public float power, heat;
		public float[] before, after, delta;
	}

	[Serializable]
	public sealed class UpgradeBalanceReport
	{
		public string[] metrics;
		public List<UpgradeBalanceSample> upgrades = new();
		public List<string> findings = new();
	}

	/// <summary>Measures the marginal resource and tool benefit of every research node and module.</summary>
	public static class UpgradeBalanceAudit
	{
		private static readonly string[] Metrics =
		{
			"compute", "bandwidth", "storage", "RAM", "reclaim", "power capacity", "cooling capacity",
			"hack seconds", "hack trace", "scan seconds", "scan trace", "download seconds", "download trace",
			"queue capacity", "scan radius", "extra spoof uses", "unlocked module models", "waypoint tier",
			"prepared hack trace", "relay reveal radius"
		};

		public static IReadOnlyList<string> Run(GameCatalog catalog)
		{
			var report = new UpgradeBalanceReport { metrics = Metrics };
			foreach (var technology in catalog.Data.technologies.Where(t => t.id != "base"))
			{
				var baseline = Research(catalog, technology.parents);
				var upgraded = Research(catalog, technology.parents.Concat(new[] { technology }));
				var sample = new UpgradeBalanceSample
				{
					id = technology.id, category = "Research", family = technology.branch, cost = technology.cost,
					capabilities = string.Join(", ", technology.grants.Select(a => a.title)) +
						(technology.triggers.Length > 0 ? " / event rules" : ""),
					before = Measure(baseline), after = Measure(upgraded)
				};
				Add(report, sample);
			}
			foreach (var module in catalog.Data.modules)
			{
				var research = module.requirements.Select(r => r.condition).OfType<ResearchCondition>()
					.Select(r => r.technology).Where(t => t != null).ToArray();
				var baseline = Research(catalog, research);
				var upgraded = Research(catalog, research);
				foreach (var sim in new[] { baseline, upgraded })
					while (sim.NextChassis != null)
						if (!sim.UpgradeChassis()) throw new InvalidOperationException("Audit chassis upgrade failed.");
				var part = upgraded.BuyModule(module.id);
				if (part == null || !upgraded.PlaceFirstFree(part.instanceId) || !upgraded.RigValid)
					throw new InvalidOperationException("Upgrade audit cannot legally install " + module.id);
				Add(report, new UpgradeBalanceSample
				{
					id = module.id, category = "Module", family = module.family, cost = module.price,
					cells = module.width * module.height, power = module.powerDemand, heat = module.heatOutput,
					capabilities = module.description, before = Measure(baseline), after = Measure(upgraded)
				});
			}

			foreach (var sample in report.upgrades.Where(s => s.category == "Module"))
				foreach (var other in report.upgrades.Where(s => s.category == "Module" && s.id != sample.id &&
					s.family == sample.family && s.cost <= sample.cost && s.cells <= sample.cells &&
					s.power <= sample.power && s.heat <= sample.heat))
				{
					// Restrict comparisons to the same authored modifier conditions; specialized effects remain distinct.
					var a = catalog.Data.modules.First(m => m.id == sample.id);
					var b = catalog.Data.modules.First(m => m.id == other.id);
					string Signature(RigModuleDefinition m) => string.Join("|", m.modifiers.Select(x =>
						x.stat + ":" + x.operation + ":" + ContentJson.Encode(x.when)).OrderBy(x => x));
					if (Signature(a) != Signature(b)) continue;
					var better = Enumerable.Range(0, Metrics.Length).All(i => Utility(other.delta[i], i) >= Utility(sample.delta[i], i) - .0001f);
					var strict = other.cost < sample.cost || other.cells < sample.cells || other.power < sample.power ||
						other.heat < sample.heat || Enumerable.Range(0, Metrics.Length).Any(i =>
							Utility(other.delta[i], i) > Utility(sample.delta[i], i) + .0001f);
					if (better && strict)
						report.findings.Add("WARN / Possible module dominance: " + other.id + " over " + sample.id +
							"; footprint shape and unmeasured situational rules still affect placement.");
				}
			Directory.CreateDirectory("Artifacts");
			File.WriteAllText("Artifacts/upgrade-balance.json", JsonUtility.ToJson(report, true));
			File.WriteAllLines("Artifacts/upgrade-balance.txt", report.upgrades.Select(s =>
				s.category + " / " + s.id + " / " + s.cost + " / " + string.Join(", ", s.delta.Select((d, i) =>
					Mathf.Abs(d) > .0001f ? Metrics[i] + " " + d.ToString("+0.###;-0.###") : "").Where(x => x.Length > 0)))
				.Concat(report.findings));
			return report.findings;
		}

		private static float Utility(float delta, int metric) => (metric >= 7 && metric <= 12) || metric == 18 ? -delta : delta;

		private static void Add(UpgradeBalanceReport report, UpgradeBalanceSample sample)
		{
			sample.delta = sample.after.Select((value, i) => value - sample.before[i]).ToArray();
			if (sample.category == "Research" && sample.delta.All(d => Mathf.Abs(d) < .0001f) &&
				string.IsNullOrWhiteSpace(sample.capabilities))
				report.findings.Add("WARN / " + sample.id + " has no measured marginal benefit in the standard fixture.");
			report.upgrades.Add(sample);
		}

		private static GameSimulation Research(GameCatalog catalog, IEnumerable<TechnologyDefinition> technologies)
		{
			var sim = new GameSimulation(catalog);
			sim.Career.xp = sim.Career.credits = 100000;
			sim.Career.jobs = 6;
			void Buy(TechnologyDefinition technology)
			{
				if (sim.Has(technology.id)) return;
				foreach (var parent in technology.parents) Buy(parent);
				if (!sim.BuyTech(technology.id)) throw new InvalidOperationException("Audit research failed: " + technology.id);
			}
			foreach (var technology in technologies) Buy(technology);
			return sim;
		}

		private static float[] Measure(GameSimulation sim)
		{
			var unlockedModules = sim.Modules.Count(m => m.requirements.All(r => r.condition.Test(sim.Context())));
			sim.Start(sim.Catalog.Contracts(sim.Career).First(c => !c.recurring));
			sim.Node("n22").known = sim.Node("n22").controlled = true;
			var hack = sim.Preview("hack", "n2");
			var scan = sim.Preview("scan", "n2");
			var download = sim.Preview("download", "n22");
			sim.Node("n2").scanned = true;
			var prepared = sim.Preview("hack", "n2");
			return new[] { sim.Compute, sim.Bandwidth, sim.StorageCapacity, sim.MemoryPool, sim.MemoryReclaim,
				sim.PowerLimit, sim.CoolingLimit, hack.duration, hack.trace, scan.duration, scan.trace,
				download.duration, download.trace, sim.Stat("queueCapacity"), sim.Stat("scanRadius"),
				sim.Stat("spoofBonus"), unlockedModules, sim.WaypointTier, prepared.trace,
				sim.Stat("revealRadius", sim.Context(sim.Node("n2"), sim.Catalog.Action("hack"))) };
		}
	}
}
