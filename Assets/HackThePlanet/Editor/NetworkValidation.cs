using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HackThePlanet.Editor
{
	/// <summary>Editor validation harness for procedural network generation, lore, and save semantics.</summary>
	public static class NetworkValidation
	{
		/// <summary>Runs seed sweeps, story contracts, and lore or save checks through the supplied reporter.</summary>
		/// <param name="check">Callback that records pass/fail assertions with a message.</param>
		public static void Run(Action<bool, string> check)
		{
			// Collect the distinct link topologies produced by the seed sweep.
			var signatures = new HashSet<string>();

			// Sweep three difficulties across one hundred seeds each.
			for (var difficulty = 1; difficulty <= 3; difficulty++)
				for (var seed = 0; seed < 100; seed++)
				{
					// Build a contract for this difficulty and seed.
					var job = new Contract
					{
						id = "quiet-copy", title = "Validation", company = GameContent.Companies[0],
						difficulty = difficulty, seed = seed, kind = JobKind.Copy
					};

					// Generate the network and build the assertion message prefix.
					var network = NetworkGenerator.Generate(job, new Career());
					var prefix = "Seed " + seed + " / difficulty " + difficulty + " / ";
					var ids = new HashSet<string>(network.nodes.Select(n => n.id));

					// Verify the node count is exact and every node id is unique.
					check(network.nodes.Count == 20 + difficulty * 12 && ids.Count == network.nodes.Count,
						  prefix + "unique node count");

					// Build a normalized key for each undirected link.
					var keys = network.links
									  .Select(l => string.CompareOrdinal(l.a, l.b) < 0
												  ? l.a + ":" + l.b
												  : l.b + ":" + l.a).ToArray();

					// Verify links are unique, non-self, and connect known nodes.
					check(
						keys.Distinct().Count() == keys.Length &&
						network.links.All(l => l.a != l.b && ids.Contains(l.a) && ids.Contains(l.b)),
						prefix + "valid unique edges");

					// Check that every node is reachable from home.
					var reachable = Reach(network, false);
					check(reachable.Count == ids.Count, prefix + "connected network");

					// Recompute reachability with archives treated as optional detours.
					var noArchives = Reach(network, true);

					// Verify objectives stay reachable without archives and exactly three bonus archives exist.
					check(
						network.nodes.Where(n => n.objective).All(n => noArchives.Contains(n.id)) &&
						network.nodes.Count(n => n.bonus) == 3,
						prefix + "archives are optional detours");

					// Verify the route count and that every alternative route follows real links.
					check(network.approaches.Count == difficulty + 2 && network.approaches.All(route => route
								  .Zip(route.Skip(1),
									   (a, b) =>
										   network.links.Any(l => (l.a == a && l.b == b) || (l.a == b && l.b == a)))
								  .All(v => v)), prefix + "valid alternative routes");

					// Verify the approaches share no intermediate nodes.
					check(
						network.approaches.SelectMany(r => r.Skip(2).Take(r.Count - 3)).Distinct().Count() ==
						network.approaches.Sum(r => r.Count - 3), prefix + "independent approaches");

					// Generate the network again to confirm the output is deterministic.
					var repeat = NetworkGenerator.Generate(job, new Career());

					// Compare serialized snapshots of both generations.
					check(JsonUtility.ToJson(new Snapshot(network)) == JsonUtility.ToJson(new Snapshot(repeat)),
						  prefix + "deterministic preview and positions");

					// Record this link topology in the signature set.
					signatures.Add(string.Join("|", keys));

					// Start the closest-pair search with the largest possible distance.
					var minimum = float.MaxValue;

					// Measure every pair of nodes and keep the smallest distance.
					for (var i = 0; i < network.nodes.Count; i++)
						for (var j = i + 1; j < network.nodes.Count; j++)
							minimum = Mathf.Min(
								minimum, Vector3.Distance(network.nodes[i].position, network.nodes[j].position));

					// Start the layout bounds at the first node.
					var bounds = new Bounds(network.nodes[0].position, Vector3.zero);

					// Grow the bounds to include every node position.
					foreach (var node in network.nodes)
						bounds.Encapsulate(node.position);

					// Verify the layout is volumetric and nodes keep minimum spacing.
					check(minimum >= 2.59f && bounds.size.x > 20 && bounds.size.y > 10 && bounds.size.z > 10,
						  prefix + "volumetric layout and spacing");

					// Play the first and last approaches to compare their time and trace.
					var fast = PlayApproach(job, network.approaches.First());
					var safe = PlayApproach(job, network.approaches.Last());

					// Verify the faster route is quicker and riskier, and both stay under the trace limit.
					check(fast.time < safe.time && fast.trace > safe.trace && fast.trace < 100 && safe.trace < 100,
						  prefix + "legal faster/dangerous versus slower/safer routes");
				}

			// Verify the seed sweep yields many distinct topologies.
			check(signatures.Count > 250, "Seed sweep produces varied topology");

			// Build a career that has already completed the quiet copy contract.
			var hardenedCareer = new Career { completed = new List<string> { "quiet-copy" } };

			// Generate the story network with a fresh career and with the hardened one.
			var fresh = NetworkGenerator.Generate(GameContent.Story[3], new Career());
			var hardened = NetworkGenerator.Generate(GameContent.Story[3], hardenedCareer);

			// Verify the corporate firewall gains one security level after hardening.
			check(
				hardened.nodes.First(n => n.id == "n10").security == fresh.nodes.First(n => n.id == "n10").security + 1,
				"Corporate firewall hardening persists across generated contracts");

			// Check every authored story contract.
			foreach (var job in GameContent.Story)
			{
				// Generate this contract's network with a fresh career.
				var network = NetworkGenerator.Generate(job, new Career());

				// Verify the objectives can be reached without archives.
				check(network.nodes.Where(n => n.objective).All(n => Reach(network, true).Contains(n.id)),
					  job.title + " objectives reachable without archives");

				// Verify every bonus node has authored lore text.
				check(
					network.nodes.Where(n => n.bonus).Select(n => LoreContent.Find(job, n.loreId))
						   .All(f => f != null && !string.IsNullOrEmpty(f.text)),
					job.title + " authored lore fragments");
			}

			// Validate lore recovery and save/load semantics.
			ValidateLoreAndSaves(check);
		}

		// Returns node ids reachable from home, optionally excluding bonus archive nodes.
		private static HashSet<string> Reach(GeneratedNetwork network, bool omitArchives)
		{
			// Start the breadth-first walk at the home node.
			var visited = new HashSet<string> { "home" };
			var queue = new Queue<string>();
			queue.Enqueue("home");

			// Collect the nodes the walk must avoid, which are the archives when omitted.
			var blocked = new HashSet<string>(network.nodes.Where(n => omitArchives && n.bonus).Select(n => n.id));

			// Expand the frontier until no reachable node remains.
			while (queue.Count > 0)
			{
				// Take the next node off the frontier.
				var id = queue.Dequeue();

				// Visit each neighbor across an incident link.
				foreach (var edge in network.links.Where(l => l.a == id || l.b == id))
				{
					// Find the node on the other end of the link.
					var next = edge.a == id ? edge.b : edge.a;

					// Enqueue the neighbor once when it is not blocked.
					if (!blocked.Contains(next) && visited.Add(next))
						queue.Enqueue(next);
				}
			}

			// Return every node reached from home.
			return visited;
		}

		// Hacks along a route and returns total duration and final trace, or throws when trace exceeds budget.
		private static (float time, float trace) PlayApproach(Contract job, List<string> route)
		{
			// Create a simulation and start the contract for this approach.
			var sim = new GameSimulation();
			sim.Start(job);

			// Accumulate the duration of the route.
			float duration = 0;

			// Hack each node along the route after the home node.
			foreach (var id in route.Skip(1))
			{
				// Preview the hack and add its duration to the route total.
				var preview = sim.Preview(ToolKind.Hack, id);
				duration += preview.duration;

				// Perform the hack on this node.
				GameValidation.Hack(sim, id);
			}

			// Fail when the trace budget was exceeded along the route.
			if (!sim.Active)
				throw new InvalidOperationException("Approach exceeded trace budget");

			// Return the total time and the final trace level.
			return (duration, sim.Op.trace);
		}

		// Starts a career-equipped simulation on the given contract for network validation fixtures.
		private static GameSimulation EquippedSimulation(Contract job)
		{
			// Create a career with the chassis and unlocks needed for the validation runs.
			var career = new Career
			{
				chassisId = "chassis-4",
				unlocked = new List<string> { "base", "quiet", "efficient", "selective", "buffer" }
			};

			// Mark the story contracts this job depends on as completed.
			career.completed = GameContent.Story.Take(job.recurring ? 3 : job.requiredWins).Select(c => c.id).ToList();

			// Create the simulation and start the job.
			var sim = new GameSimulation(career);
			sim.Start(job);

			return sim;
		}

		// Traverses to a node, completes a download, and fails when trace budget is exceeded.
		private static void Download(GameSimulation sim, string id)
		{
			// Walk the simulation to the target node.
			GameValidation.Traverse(sim, id);

			// Execute the download and fail if the recovery is rejected.
			if (!sim.Execute(ToolKind.Download, id))
				throw new InvalidOperationException("Cannot recover " + id);

			// Finish the active task.
			GameValidation.FinishTask(sim);

			// Fail when the trace budget was exceeded during the lore run.
			if (!sim.Active)
				throw new InvalidOperationException("Lore run exceeded trace budget");
		}

		// Asserts lore banking, save migration, and report retention rules through the check callback.
		private static void ValidateLoreAndSaves(Action<bool, string> check)
		{
			// Start an equipped simulation for the first story contract.
			var job = GameContent.Story[0];
			var sim = EquippedSimulation(job);

			// Hack along the last approach to reach the archive network.
			foreach (var id in NetworkGenerator.Generate(job, sim.Career).approaches.Last().Skip(1))
				GameValidation.Hack(sim, id);

			// Download every bonus archive on the network.
			foreach (var node in sim.Op.nodes.Where(n => n.bonus).ToArray())
				Download(sim, node.id);

			// Verify lore is readable mid-run but not yet banked to the career.
			check(sim.Career.lore.Count == 0 && sim.Op.log.Any(l => l.Contains("LORE RECOVERED")),
				  "Lore readable before extraction but not banked");

			// Record node positions and round-trip the career through JSON.
			var positions = sim.Op.nodes.Select(n => n.position).ToArray();
			var restored = new GameSimulation(JsonUtility.FromJson<Career>(JsonUtility.ToJson(sim.Career)));

			// Verify node positions and pause state survive the save/load round trip.
			check(
				restored.Op.nodes.Select(n => n.position).SequenceEqual(positions) && restored.Op.paused &&
				restored.Op.generationVersion == NetworkGenerator.Version, "3D positions survive save/load exactly");

			// Resume the restored simulation and download the remaining archive.
			restored.SetPaused(false);
			Download(restored, "n22");

			// Disconnect to extract and capture the report.
			var report = restored.Disconnect();

			// Verify extraction pays the authored credit and XP rewards.
			check(
				report.success && report.bonusFiles == 3 &&
				report.xp == job.xp + 45 + (report.trace < job.quietLimit ? 20 : 0) &&
				report.credits == job.credits + 300 + (report.trace < job.quietLimit ? 100 : 0),
				"All three archives pay their authored credit and XP rewards");

			// Verify the report records the bonus totals and the quiet outcome.
			check(
				report.bonusArchivesTotal == 3 && report.quietLimit == job.quietLimit &&
				report.quietBonusEarned == report.trace < job.quietLimit,
				"report records optional bonuses and quiet outcome");

			// Verify successful extraction banks the three lore fragments.
			check(restored.Career.lore.Count == 3, "Successful extraction banks three permanent fragments");

			// Save and reload the career to check lore persistence.
			var saved = new GameSimulation(JsonUtility.FromJson<Career>(JsonUtility.ToJson(restored.Career)));

			// Verify lore content and provenance survive the save/load.
			check(
				saved.Career.lore.Count == 3 &&
				saved.Career.lore.All(f => !string.IsNullOrEmpty(f.contract) && !string.IsNullOrEmpty(f.company)),
				"Lore content and provenance survive save/load");

			// Verify an aborted run cannot bank the lore it recovered.
			var aborted = EquippedSimulation(job);
			Download(aborted, "n13");
			aborted.Disconnect();
			check(aborted.Career.lore.Count == 0, "Abort cannot bank recovered lore");

			// Verify an isolated run cannot bank the lore it recovered.
			var caught = EquippedSimulation(job);
			Download(caught, "n13");
			caught.Disconnect(true);
			check(caught.Career.lore.Count == 0, "Isolation cannot bank recovered lore");

			// Repeated freelance lore keeps its identity but still awards normal per-run rewards.
			var freelance = new Contract
			{
				id = "freelance-0", title = "Repeat", company = GameContent.Companies[0], difficulty = 1, seed = 77,
				recurring = true
			};

			// Create the repeatable career from the equipped simulation.
			var repeat = EquippedSimulation(freelance);

			// Run the freelance contract twice and check each run's payout.
			for (var run = 0; run < 2; run++)
			{
				// Restart the contract after the first run.
				if (run > 0)
					repeat.Start(freelance);

				// Download both archives and disconnect to finish the run.
				Download(repeat, "n13");
				Download(repeat, "n22");
				var delivered = repeat.Disconnect();

				// Verify each run pays its bonus archive.
				check(delivered.success && delivered.bonusFiles == 1, "Freelance archive pays on run " + run);
			}

			// Verify repeated runs deduplicate the lore fragment.
			check(repeat.Career.lore.Count == 1, "Repeated freelance fragments deduplicate");

			// Restart and disconnect the contract 25 more times to exercise report retention.
			for (var i = 0; i < 25; i++)
			{
				repeat.Start(freelance);
				repeat.Disconnect();
			}

			// Verify lore survives after report retention trims old reports.
			check(repeat.Career.reports.Count == 20 && repeat.Career.lore.Count == 1,
				  "Lore collection outlives report retention");

			// V1 saves remain untouched on career-v1.json; v2 deliberately starts fresh.
		}

		// JSON-serializable node and link subset used when hashing generated networks.
		[Serializable]
		private sealed class Snapshot
		{
			public List<NetNode> nodes;
			public List<NetLink> links;

			public Snapshot(GeneratedNetwork network)
			{
				// Copy the nodes and links that define the network shape.
				nodes = network.nodes;
				links = network.links;
			}
		}
	}
}