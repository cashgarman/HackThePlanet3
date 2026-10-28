using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HackThePlanet
{
	/// <summary>Earned-resource career shopping plans, shared by live runs and replay.</summary>
	public static class CareerUpgradePlanner
	{
		private sealed class Plan
		{
			public string name;
			public string[] research, abilities;
			public (string id, int count)[] rig;
		}

		private static Plan For(string style) => style == "Cautious" ? new Plan
		{
			name = "Recon / Loop",
			research = new[] { "wide", "quiet", "credential-analysis", "loop-feed", "signal-engineering", "blueprint", "infrastructure-2", "buffer" },
			abilities = new[] { "credential-analysis", "loop-feed", "decoy", "disrupt" },
			rig = new[] { ("ram", 2), ("veil-array", 1), ("isolation-bank", 1), ("quiet-filter", 1) }
		} : style == "Opportunist" ? new Plan
		{
			name = "Recon / Archive",
			research = new[] { "wide", "blueprint", "credential-analysis", "data-engineering", "quiet", "loop-feed", "efficient", "selective", "infrastructure-2" },
			abilities = new[] { "credential-analysis", "loop-feed", "backdoor" },
			rig = new[] { ("uplink", 2), ("decoder-board", 1), ("storage-array", 1), ("ram-bank", 1) }
		} : new Plan
		{
			name = "Intrusion / Reset",
			research = new[] { "efficient", "protocol-spoof", "disrupt", "hard-reset", "parallel-engineering", "selective", "infrastructure-2", "chain" },
			abilities = new[] { "protocol-spoof", "hard-reset", "chain", "disrupt" },
			rig = new[] { ("processor", 2), ("spoof-asic", 1), ("accelerator", 1) }
		};

		public static string Name(string style) => For(style).name;

		/// <summary>Buys eligible upgrades in order; returns whether an affordable funding target remains.</summary>
		public static bool Prepare(GameSimulation sim, string style, Action<string, string, string> log)
		{
			if (sim.Active) throw new InvalidOperationException("Career shopping requires an idle simulation.");
			var plan = For(style);
			var funding = false;
			var ordered = new List<TechnologyDefinition>();
			var visited = new HashSet<string>();
			void Add(string id)
			{
				if (!visited.Add(id)) return;
				var tech = sim.Catalog.Technology(id);
				if (tech == null) { log("skip-tech", id, "Definition unavailable"); return; }
				foreach (var parent in tech.parents) Add(parent.id);
				ordered.Add(tech);
			}
			foreach (var id in plan.research) Add(id);
			foreach (var tech in ordered.Where(t => !sim.Has(t.id)))
			{
				if (sim.Career.xp < tech.cost)
				{
					log("save-xp", tech.id, plan.name + ": need " + (tech.cost - sim.Career.xp) + " more XP");
					funding = true;
					break;
				}
				if (!sim.BuyTech(tech.id)) { log("skip-tech", tech.id, "Research prerequisites blocked"); break; }
				log("buy-tech", tech.id, plan.name + ": spent " + tech.cost + " XP");
			}

			foreach (var target in plan.rig)
			{
				if (sim.Career.rigParts.Count(p => p.modelId == target.id && p.Installed) >= target.count) continue;
				var model = sim.Catalog.Data.modules.FirstOrDefault(m => m.id == target.id);
				if (model == null) { log("skip-module", target.id, "Definition unavailable"); continue; }
				if (!model.requirements.All(r => r.condition.Test(sim.Catalog.Context(sim.Career))))
				{ log("skip-module", target.id, "Waiting for required research"); continue; }

				// Preview the complete legal transaction on a copy. The copy's funds are used only to measure cost.
				var copy = JsonUtility.FromJson<Career>(JsonUtility.ToJson(sim.Career));
				copy.credits = 1000000;
				var probe = new GameSimulation(sim.Catalog, copy);
				var owned = probe.Career.rigParts.FirstOrDefault(p => p.modelId == target.id && !p.Installed);
				var part = owned ?? probe.BuyModule(target.id);
				var upgrades = 0;
				bool placed = false;
				while (part != null)
				{
					if (TryInstall(probe, part) || Repack(probe, part))
					{ placed = true; break; }
					if (probe.NextChassis == null || !probe.UpgradeChassis()) break;
					upgrades++;
				}
				if (!placed)
				{
					log("skip-module", target.id, "The current rig cannot fit or support this module. " + probe.PreviewChassis().reason);
					continue;
				}
				var cost = 1000000 - probe.Career.credits;
				if (sim.Career.credits < cost)
				{
					// A chassis is a useful upgrade by itself (RAM, storage, slots and capacity).
					// Buy the unlocked expansion needed by the next module even when that module needs more earnings.
					if (upgrades > 0 && sim.PreviewChassis().allowed)
					{
						var next = sim.NextChassis;
						sim.UpgradeChassis();
						cost -= next.price;
						upgrades--;
						log("buy-chassis", next.id, plan.name + ": spent " + next.price + " credits; expanded capacity while saving for " + model.title);
					}
					log("save-credits", target.id, plan.name + ": need " + (cost - sim.Career.credits) +
						" more credits for module and " + upgrades + " chassis upgrade(s)");
					funding = true;
					break;
				}
				for (var i = 0; i < upgrades; i++)
				{
					var next = sim.NextChassis;
					if (!sim.UpgradeChassis()) throw new InvalidOperationException("Planned chassis purchase rejected.");
					log("buy-chassis", next.id, plan.name + ": spent " + next.price + " credits for " + target.id);
				}
				var actual = sim.Career.rigParts.FirstOrDefault(p => p.modelId == target.id && !p.Installed);
				var bought = actual == null;
				actual ??= sim.BuyModule(target.id);
				if (actual == null) throw new InvalidOperationException("Planned module purchase rejected.");
				foreach (var installed in sim.Career.rigParts.Where(p => p.Installed).ToArray()) sim.RemoveModule(installed.instanceId);
				foreach (var placement in probe.Career.rigParts.Where(p => p.Installed))
				{
					var destination = placement.instanceId == part.instanceId ? actual : sim.Part(placement.instanceId);
					if (!sim.TryPlaceModule(destination.instanceId, placement.row, placement.column, placement.rotated))
						throw new InvalidOperationException("Planned module placement rejected.");
				}
				if (!sim.RigValid)
					throw new InvalidOperationException("Planned rig installation rejected.");
				log(bought ? "buy-module" : "install-module", target.id,
					plan.name + (bought ? ": spent " + model.price + " credits; installed" : ": installed owned module"));
			}

			var desired = plan.abilities.Where(id => sim.Has(id) && sim.Catalog.Technology(id)?.special == true)
				.Take(sim.Slots).ToArray();
			foreach (var id in sim.Career.equipped.Except(desired).ToArray())
			{ sim.ToggleEquip(id); log("unequip", id, plan.name + ": higher priority ability available"); }
			foreach (var id in desired.Except(sim.Career.equipped).ToArray())
				if (sim.ToggleEquip(id)) log("equip", id, plan.name + ": ability priority");
			return funding;
		}

		private static bool TryInstall(GameSimulation sim, RigPart part)
		{
			foreach (var rotated in new[] { false, true })
				for (var row = 0; row < sim.Chassis.rows; row++)
					for (var column = 0; column < sim.Chassis.columns; column++)
					{
						if (!sim.TryPlaceModule(part.instanceId, row, column, rotated)) continue;
						if (sim.RigValid) return true;
						sim.RemoveModule(part.instanceId);
					}
			return false;
		}

		private static bool Repack(GameSimulation sim, RigPart addition)
		{
			var parts = sim.Career.rigParts.Where(p => p.Installed || p == addition)
				.OrderByDescending(p => sim.Module(p.modelId).width * sim.Module(p.modelId).height)
				.ThenBy(p => p.modelId, StringComparer.Ordinal).ToArray();
			var saved = parts.Select(p => (p, p.row, p.column, p.rotated)).ToArray();
			foreach (var p in parts) sim.RemoveModule(p.instanceId);
			var budget = 20000;
			bool Pack(int index)
			{
				if (--budget < 0) return false;
				if (index == parts.Length) return sim.RigValid;
				var p = parts[index];
				foreach (var rotation in new[] { false, true })
					for (var row = 0; row < sim.Chassis.rows; row++)
						for (var column = 0; column < sim.Chassis.columns; column++)
						{
							if (!sim.TryPlaceModule(p.instanceId, row, column, rotation)) continue;
							if (Pack(index + 1)) return true;
							sim.RemoveModule(p.instanceId);
						}
				return false;
			}
			if (Pack(0)) return true;
			foreach (var p in parts) sim.RemoveModule(p.instanceId);
			foreach (var old in saved.Where(s => s.row >= 0))
				sim.TryPlaceModule(old.p.instanceId, old.row, old.column, old.rotated);
			return false;
		}
	}
}
