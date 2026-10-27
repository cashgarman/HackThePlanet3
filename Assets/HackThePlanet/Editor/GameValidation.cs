using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace HackThePlanet.Editor
{
	/// <summary>Simulation and UI validation helpers shared by editor smoke tests and network checks.</summary>
	[NoAutoStaticsCleanup]
	public static class GameValidation
	{
		// Collects passing simulation validation lines for artifact output.
		private static readonly List<string> results = new();

		// Records a passing assertion or throws with the failure description.
		private static void Check(bool pass, string description)
		{
			// Throw on the first failed assertion so the run stops with its description.
			if (!pass)
				throw new InvalidOperationException("FAILED: " + description);

			// Record the passing assertion in the results list.
			results.Add("PASS / " + description);
		}

		/// <summary>Advances simulation ticks until the active task completes or times out.</summary>
		/// <param name="sim">Running operation to advance.</param>
		/// <exception cref="InvalidOperationException">The task did not finish within the tick guard.</exception>
		public static void FinishTask(GameSimulation sim)
		{
			// Start the tick guard counter at zero.
			var guard = 0;

			// Tick the simulation until the task finishes or the guard limit is reached.
			while (sim.Active && sim.Op.task != null && guard++ < 2000) sim.Tick(.25f);

			// Fail when the guard limit was reached before the task finished.
			if (guard >= 2000)
				throw new InvalidOperationException("A task did not terminate.");
		}

		/// <summary>Executes Hack on a node when allowed and waits for completion.</summary>
		/// <param name="sim">Active simulation.</param>
		/// <param name="id">Local node id to control.</param>
		/// <exception cref="InvalidOperationException">The operation is inactive or the hack is rejected.</exception>
		public static void Hack(GameSimulation sim, string id)
		{
			// Fail when the operation has already disconnected.
			if (!sim.Active)
				throw new InvalidOperationException("Disconnected before hacking " + id);

			// Skip the hack when the node is already controlled.
			if (sim.Node(id).controlled)
				return;

			// Execute the hack and fail with the preview reason when it is rejected.
			if (!sim.Execute(ToolKind.Hack, id))
				throw new InvalidOperationException("Cannot hack " + id + " / " +
													sim.Preview(ToolKind.Hack, id).reason);

			// Wait for the hack task to finish.
			FinishTask(sim);
		}

		/// <summary>Hacks along controlled neighbors until <paramref name="target" /> is reached.</summary>
		/// <param name="sim">Active simulation with at least one controlled node.</param>
		/// <param name="target">Destination node id.</param>
		/// <exception cref="InvalidOperationException">No controlled route exists to the target.</exception>
		public static void Traverse(GameSimulation sim, string target)
		{
			// Track each node's predecessor and the frontier of the breadth-first search.
			var parent = new Dictionary<string, string>();
			var queue = new Queue<string>();

			// Seed the search from every controlled node.
			foreach (var node in sim.Op.nodes.Where(n => n.controlled))
			{
				queue.Enqueue(node.id);
				parent[node.id] = null;
			}

			// Expand outward until the target is discovered or the frontier empties.
			while (queue.Count > 0 && !parent.ContainsKey(target))
			{
				// Take the next node off the frontier.
				var id = queue.Dequeue();

				// Record each unvisited neighbor and its predecessor.
				foreach (var node in sim.Neighbors(sim.Node(id)))
					if (!parent.ContainsKey(node.id))
					{
						parent[node.id] = id;
						queue.Enqueue(node.id);
					}
			}

			// Fail when no controlled route reaches the target.
			if (!parent.ContainsKey(target))
				throw new InvalidOperationException("No route to " + target);

			// Walk the predecessor chain from the target back to a controlled node, then reverse it.
			var path = new List<string>();
			for (var id = target; id != null; id = parent[id])
				path.Add(id);
			path.Reverse();

			// Hack each node along the path in order.
			foreach (var id in path)
				Hack(sim, id);
		}

		// Runs a download on the given node and asserts the lore fragment is recovered.
		private static void Recover(GameSimulation sim, string id)
		{
			// Execute the recovery and fail with the preview reason when it is rejected.
			if (!sim.Execute(ToolKind.Download, id))
				throw new InvalidOperationException("Cannot recover " + id + " / " +
													sim.Preview(ToolKind.Download, id).reason);

			// Wait for the recovery task to finish.
			FinishTask(sim);
		}

		// Asserts quiet-run and difficulty-scaled contract rewards match expectations.
		private static void ValidateDifficultyRewards()
		{
			// Copy the game definition and its first contract so the test can edit them safely.
			var source = GameData.Current.Game;
			var root = Object.Instantiate(source);
			var definition = Object.Instantiate(source.contracts[0]);

			// Swap the copied contract into the copied game definition.
			root.contracts = root.contracts.Select(d => d.id == definition.id ? definition : d).ToArray();

			// Set a known base payout for the copied contract.
			definition.credits = new ConstantNumber(600);
			definition.xp = new ConstantNumber(50);
			try
			{
				// Check each difficulty tier against the scaling rules.
				foreach (var tier in new[] { 1, 2, 3 })
				{
					// Apply this tier's difficulty to the copied contract.
					definition.difficulty = new ConstantNumber(tier);
					using (var catalog = new GameCatalog(root))
					{
						// Start a contract from the catalog for this tier.
						var sim = new GameSimulation(catalog);
						var job = catalog.Contracts(sim.Career).First(c => c.id == definition.id);

						// Verify the advertised credits and XP scale with the tier.
						Check(job.credits == 300 * (tier + 1) && job.xp == 25 * (tier + 1),
							  "Difficulty " + tier + " scales advertised credits and XP.");
						Check(sim.Start(job), "Difficulty " + tier + " contract connects.");

						// Restore the active save and verify the payout is not scaled twice.
						var restored =
							new GameSimulation(catalog, JsonUtility.FromJson<Career>(JsonUtility.ToJson(sim.Career)));
						Check(restored.Job.credits == job.credits && restored.Job.xp == job.xp,
							  "Difficulty payout survives active-save restoration without double scaling.");

						// Mark every objective as controlled and collected.
						foreach (var node in restored.Op.nodes.Where(n => n.objective))
						{
							node.controlled = true;
							node.collected = true;
						}

						// Keep the trace at the quiet limit and snapshot the career's totals.
						restored.Op.trace = job.quietLimit;
						int credits = restored.Career.credits, xp = restored.Career.xp;

						// Disconnect and verify the banked payout equals the advertised base.
						var report = restored.Disconnect();
						Check(
							report.success && report.credits == job.credits && report.xp == job.xp &&
							restored.Career.credits - credits == job.credits && restored.Career.xp - xp == job.xp,
							"Difficulty " + tier + " banks exactly the advertised base payout.");
					}
				}
			}
			finally
			{
				// Destroy the copied objects.
				Object.DestroyImmediate(definition);
				Object.DestroyImmediate(root);
			}
		}

		// Validates waypoint tiers, routing, and markers against JSON in the given directory.
		private static void ValidateWaypoints(string dir)
		{
			// Create a simulation with enough XP to unlock the test contracts.
			var sim = new GameSimulation();
			sim.Career.xp = 1000;

			// Pick the first one-off contract and generate its network.
			var job = sim.Catalog.Contracts(sim.Career).First(c => !c.recurring);
			var network = NetworkGenerator.Generate(job, sim.Career, sim.Catalog);

			// Select up to five unknown systems that are at least three jumps from the entry node.
			var targets = network.nodes
								 .Where(n => !n.known && GameSimulation
														 .WaypointPath(network.nodes, network.links,
																	   sim.Catalog.Network(job.networkId).entryNode,
																	   n.id).Jumps >= 3).Take(5).ToArray();

			// Verify waypoints stay locked until research and tier prerequisites are met.
			Check(!sim.PlaceWaypoint(job, targets[0].id) && !sim.BuyTech("waypoints-2"),
				  "Waypoints require research and sequential tier prerequisites.");

			// Walk the four waypoint tiers and verify each tier's capacity and edit rights.
			for (var tier = 1; tier <= 4; tier++)
			{
				// Unlock this tier and verify its capacity.
				Check(sim.BuyTech("waypoints-" + tier) && sim.WaypointTier == tier,
					  "Waypoints tier " + tier + " unlocks its capacity.");
				Check(sim.PlaceWaypoint(job, targets[tier - 1].id) && !sim.PlaceWaypoint(job, targets[4].id),
					  "Tier " + tier + " enforces its waypoint limit.");
				Check(!sim.PlaceWaypoint(job, targets[0].id), "Tier " + tier + " rejects duplicate markers.");

				// Tier I cannot change waypoint colours.
				if (tier == 1)
					Check(!sim.EditWaypoint(job, targets[0].id, 1, "", false),
						  "Tier I cannot change waypoint colours.");

				// Tier II can change colours but cannot add labels.
				if (tier == 2)
					Check(
						sim.EditWaypoint(job, targets[0].id, 1, "", false) &&
						!sim.EditWaypoint(job, targets[0].id, 1, "label", false),
						"Tier II changes colours but cannot add labels.");

				// Tier III can add labels but cannot enable routes.
				if (tier == 3)
					Check(
						sim.EditWaypoint(job, targets[0].id, 1, "Meet here", false) &&
						!sim.EditWaypoint(job, targets[0].id, 1, "Meet here", true),
						"Tier III adds labels but cannot enable routes.");
			}

			// Verify tier IV allows exactly one selected route.
			Check(
				sim.EditWaypoint(job, targets[0].id, 1, "Meet here", true) &&
				sim.EditWaypoint(job, targets[1].id, 2, "Next", true) && sim.Waypoints(job).Count(w => w.showPath) == 1,
				"Tier IV allows exactly one selected route.");

			// Compute the route to the second target through the waypoint graph.
			var route = GameSimulation.WaypointPath(network.nodes, network.links,
													sim.Catalog.Network(job.networkId).entryNode, targets[1].id);

			// Verify the route reaches the target and crosses undiscovered systems.
			Check(
				route.IsReachable && route.NodeIds.Last() == targets[1].id &&
				route.NodeIds.Any(id => !network.nodes.First(n => n.id == id).known),
				"Waypoint routing crosses undiscovered systems.");

			// Build a small chain graph with a shortcut to test shortest-path routing.
			var chain = new List<NetNode>
				{ new() { id = "a" }, new() { id = "b" }, new() { id = "c" }, new() { id = "d" } };
			var edges = new List<NetLink> { new("b", "a"), new("c", "b"), new("d", "c"), new("d", "a") };

			// Verify the shortest path is found regardless of link direction.
			Check(GameSimulation.WaypointPath(chain, edges, "a", "d").Jumps == 1,
				  "Waypoint routing finds the shortest path in either link direction.");

			// Verify plans are isolated per contract.
			var other = sim.Catalog.Contracts(sim.Career).First(c => c.id != job.id);
			Check(sim.Waypoints(other).Count == 0, "Plans are isolated by contract.");

			// Verify starting an operation keeps markers without revealing their systems.
			Check(sim.Start(job) && sim.Waypoints(sim.Job).Count == 4 && !sim.Node(targets[1].id).known,
				  "Starting preserves markers without revealing their systems.");

			// Verify plans cannot be edited while an operation is running.
			Check(!sim.RemoveWaypoint(job, targets[0].id) && !sim.EditWaypoint(job, targets[0].id, 0, "", false),
				  "Plans cannot be edited during operations.");

			// Open a viewport with the waypoints and verify hidden-system markers render.
			using (var viewport = new GraphViewport(sim.Op.nodes, sim.Op.links, sim.Op.entryNode, false, _ => { },
													guideEntryId: sim.Op.entryNode, waypoints: sim.Waypoints(sim.Job),
													waypointTier: sim.WaypointTier))
			{
				// Verify hidden-system markers exist without loading undiscovered models.
				Check(
					viewport.Q<Label>("waypoint-marker-" + targets[1].id) != null &&
					viewport.NodeCount == sim.Op.nodes.Count(n => n.known),
					"Hidden-system markers exist without adding undiscovered models to the viewport.");
			}

			// Save the career with active waypoint plans.
			var save = Path.Combine(dir, "validation-waypoints.json");
			Check(CareerStorage.Save(sim.Career, save), "Waypoint plans save with an active operation.");

			// Reload the saved career and verify the waypoint plans survived.
			var restored = new GameSimulation(CareerStorage.Load(save));
			Check(
				restored.Waypoints(restored.Job).Count == 4 &&
				restored.Waypoints(restored.Job)[0].label == "Meet here" &&
				restored.Waypoints(restored.Job)[1].showPath && restored.Waypoints(restored.Job)[1].color == 2,
				"Waypoint positions, colours, labels and route selection survive save/load.");

			// Verify legacy careers without waypoint data load cleanly.
			var legacy = JsonUtility.FromJson<Career>(JsonUtility.ToJson(sim.Career));
			legacy.waypoints = null;
			Check(new GameSimulation(legacy).Career.waypoints.Count == 0,
				  "Existing careers without waypoint data remain compatible.");

			// Disconnect and verify research refunds clear waypoint plans and capacity.
			sim.Disconnect();
			Check(sim.RefundResearch() && sim.WaypointTier == 0 && sim.Career.waypoints.Count == 0,
				  "Research refunds clear waypoint plans and capacity.");
		}

		// Verifies debug grant and unlock cheats behave and stay isolated from real saves.
		private static void ValidateDebugCheats(string dir)
		{
// Create a simulation and record the starting currency, XP, and change count.
			var sim = new GameSimulation();
			int credits = sim.Career.credits, xp = sim.Career.xp, total = sim.Career.totalEarnedXp, changes = 0;

			// Count change notifications so autosave subscribers can be verified.
			sim.Changed += () => changes++;

			// Apply two credit grants and one XP grant.
			sim.DebugAddCredits();
			sim.DebugAddCredits();
			sim.DebugAddXp();

			// Verify grants repeat, update earned XP, and notify subscribers.
			Check(
				sim.Career.credits == credits + 2 * GameSimulation.DebugGrantAmount &&
				sim.Career.xp == xp + GameSimulation.DebugGrantAmount &&
				sim.Career.totalEarnedXp == total + GameSimulation.DebugGrantAmount && changes == 3,
				"Debug grants repeat, update earned XP, and notify autosave subscribers.");

			// Start a one-off operation to use as the debug fixture.
			Check(sim.Start(sim.Catalog.Contracts(sim.Career).First(c => !c.recurring)),
				  "Debug fixture starts an operation.");

			// Pause the operation and snapshot its state before unlocking everything.
			sim.SetPaused(true);
			var operation = sim.Op;
			var spent = sim.Career.researchSpent;
			var equipped = sim.Career.equipped.ToArray();

			// Unlock every technology twice to confirm the action is idempotent.
			sim.DebugUnlockAllTech();
			sim.DebugUnlockAllTech();

			// Verify every catalog technology is unlocked exactly once.
			Check(
				sim.Catalog.Technologies.All(t => sim.Has(t.id)) &&
				sim.Career.unlocked.Count == sim.Career.unlocked.Distinct().Count(),
				"Debug unlock includes every catalog technology and is idempotent.");

			// Verify the unlock leaves the operation, pause, ledger, and loadout intact.
			Check(
				ReferenceEquals(operation, sim.Op) && sim.Op.paused && sim.Career.researchSpent == spent &&
				sim.Career.equipped.SequenceEqual(equipped),
				"Debug unlock preserves active operation, pause, paid research ledger, and loadout.");

			// Save the debug career and reload it.
			var path = Path.Combine(dir, "validation-debug-career.json");
			Check(CareerStorage.Save(sim.Career, path), "Debug career saves successfully.");
			var restored = new GameSimulation(CareerStorage.Load(path));

			// Verify currency and the full tech tree survive the reload.
			Check(
				restored.Career.credits == sim.Career.credits && restored.Career.xp == sim.Career.xp &&
				restored.Catalog.Technologies.All(t => restored.Has(t.id)),
				"Debug currency and full tech tree survive save restoration.");

			// Push the currency and XP close to the int limit and grant again.
			sim.Career.credits = sim.Career.xp = sim.Career.totalEarnedXp = int.MaxValue - 10;
			sim.DebugAddCredits();
			sim.DebugAddXp();

			// Verify repeated grants saturate at int.MaxValue instead of overflowing.
			Check(
				sim.Career.credits == int.MaxValue && sim.Career.xp == int.MaxValue &&
				sim.Career.totalEarnedXp == int.MaxValue, "Repeated debug grants saturate instead of overflowing.");
		}

		/// <summary>Runs the full gameplay validation suite and writes results under Artifacts/.</summary>
		public static void Run()
		{
			// Reset the results list and make sure the Artifacts folder exists.
			results.Clear();
			var dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Artifacts");
			Directory.CreateDirectory(dir);

			// Run the suite and write a failure report if any step throws.
			try
			{
				// Run each validation module in order.
				NetworkValidation.Run(Check);
				ValidateWaypoints(dir);
				NetworkVisualValidation.CheckIntel(Check);
				ValidateDifficultyRewards();
				ValidateDebugCheats(dir);

				// Start a simulation and verify contract locking and the first operation layout.
				var sim = new GameSimulation();
				Check(!sim.Start(GameContent.Story[1]), "Locked contracts cannot start.");
				Check(sim.Start(GameContent.Story[0]), "Starting contract connects.");
				Check(sim.Op.nodes.Count == 32, "First operation has 32 stable systems.");
				Check(sim.KnownGraph.Nodes.Count == 2, "Known Graph Explorer document excludes undiscovered systems.");

				// Verify unknown routes and targets cannot be reached before discovery.
				Check(!sim.Route("n22").IsReachable, "Shortest-path queries cannot reveal unknown routes.");
				Check(!sim.Execute(ToolKind.Hack, "n22"), "Remote unknown targets cannot be hacked.");
				Check(!sim.Execute(ToolKind.Download, "n2"), "Recovery requires ownership and an eligible file.");

				// Idle for 200 ticks and verify planning alone does not raise trace.
				var trace = sim.Op.trace;
				for (var i = 0; i < 200; i++)
					sim.Tick(.25f);
				Check(sim.Op.trace == trace, "Idle planning never raises trace.");

				// Scan a reachable system.
				Check(sim.Execute(ToolKind.Scan, "n2"), "Reachable system can be scanned.");

				// Pause the operation and verify tool work and trace are frozen.
				sim.SetPaused(true);
				sim.Tick(.5f);
				Check(sim.Op.task.elapsed == 0 && sim.Op.trace == 0, "Pause freezes tool work and trace.");

				// Resume and finish the scan, then verify it reveals more of the graph.
				sim.SetPaused(false);
				FinishTask(sim);
				Check(sim.Node("n2").scanned && sim.KnownGraph.Nodes.Count > 3,
					  "Recon reveals and projects additional graph facts.");

				// Take control of the entry system and verify route reachability through controlled nodes.
				Hack(sim, "n2");
				Check(sim.Route("n6", true).IsReachable, "Controlled-route reachability uses Graph Explorer.");
				Check(!sim.Route("n10", true).IsReachable, "Uncontrolled intermediates do not provide access.");

				// Take a second system and check that a scanner observes the adjacent foothold.
				Hack(sim, "n6");
				Check(sim.Scanners > 0, "A scanner observes activity adjacent to a foothold.");

				// Hack a third system, then take a hub and verify connected defense bonuses are removed.
				Hack(sim, "n10");
				var protectedSecurity = sim.EffectiveSecurity(sim.Node("n18"));
				Hack(sim, "n14");
				Check(sim.EffectiveSecurity(sim.Node("n18")) == protectedSecurity - 2,
					  "Taking a hub removes connected defense bonuses.");

				// Complete the objective by hacking the path and recovering the main copy.
				Hack(sim, "n18");
				Hack(sim, "n22");
				Recover(sim, "n22");
				Check(sim.ObjectiveComplete, "Main copy objective is secured by recovery.");

				// Disconnect to deliver and verify the payout and story consequences.
				var oldCredits = sim.Career.credits;
				var report = sim.Disconnect();
				Check(report.success && report.xp >= 50 && sim.Career.credits > oldCredits,
					  "Delivery banks credits and XP.");
				Check(sim.Career.completed.Contains("quiet-copy") && sim.Career.news.Count == 2,
					  "Delivery persists story progress and a world consequence.");
				Check(!sim.Start(GameContent.Story[0]), "A delivered story contract cannot be farmed.");

				// Verify research purchase rules: a single charge and crossover prerequisites.
				Check(sim.BuyTech("quiet"), "One successful contract funds a starting technology.");
				Check(!sim.BuyTech("quiet"), "Purchased technologies cannot charge twice.");
				Check(!sim.BuyTech("passive"), "Crossover enforces both parents and price.");

				// Refund research and verify the exact XP cost is returned.
				var beforeRefund = sim.Career.xp;
				sim.RefundResearch();
				Check(sim.Career.xp == beforeRefund + 45 && sim.Career.unlocked.Count == 1,
					  "Free refund returns the exact XP cost.");

				// Fund the test research and unlock the crossover parents.
				sim.Career.xp = 1000;
				sim.BuyTech("quiet");
				sim.BuyTech("wide");
				Check(sim.BuyTech("passive"), "Crossover accepts both unlocked parents.");
				sim.BuyTech("disrupt");
				sim.BuyTech("backdoor");
				sim.BuyTech("decoy");

				// Verify the loadout holds two abilities and rejects a third.
				Check(sim.Career.equipped.Count == 2, "Special abilities respect the two-slot starting loadout.");
				Check(!sim.ToggleEquip("decoy"), "A full loadout rejects another equipped ability.");
				sim.ToggleEquip("backdoor");
				Check(sim.ToggleEquip("decoy"), "Unequipping frees a slot.");

				// Buy a module copy and verify an invalid hardware choice is rejected.
				var copies = sim.Career.rigParts.Count;
				Check(sim.BuyModule("processor") != null && sim.Career.rigParts.Count == copies + 1,
					  "Credits buy persistent module copies.");
				Check(!sim.Upgrade(9), "Invalid hardware choices cannot mutate the career.");

				// Start an operation and verify research and hardware changes are locked during it.
				sim.Start(GameContent.Story[1]);
				Check(!sim.BuyTech("efficient") && !sim.RefundResearch() && sim.BuyModule("processor") == null,
					  "Research, refunds, and hardware changes are locked during operations.");
				Check(sim.Decoy() && sim.MemoryReserved == 16 && sim.Decoy() && sim.MemoryReserved == 0,
					  "Decoy toggles its RAM reservation.");

				// Abort the operation and verify equipment and tech survive.
				var aborted = sim.Disconnect();
				Check(!aborted.success && sim.Career.rigParts.Count == copies + 1 && sim.Has("quiet"),
					  "Abort preserves equipment and purchased technology.");

				// Restart the same contract and drive trace to 100% to verify isolation.
				Check(sim.Start(GameContent.Story[1]), "Uncompleted work remains retryable.");
				sim.Op.trace = 99.99f;
				sim.Execute(ToolKind.Hack, "n2");
				sim.Tick(.5f);
				Check(!sim.Active && !sim.LastReport.success && sim.LastReport.verdict == "CONNECTION ISOLATED",
					  "100% trace closes the connection without rewards.");

				// Create a funded career and unlock every ability tech needed for the test.
				var abilities = new GameSimulation(new Career { xp = 2000 });
				abilities.BuyTech("wide");
				abilities.BuyTech("efficient");
				abilities.BuyTech("chain");
				abilities.BuyTech("blueprint");
				abilities.BuyTech("bridge");
				abilities.BuyTech("quiet");
				abilities.BuyTech("ghost");

				// Start a contract and verify the ghost relay makes the first relay trace-free.
				abilities.Start(GameContent.Story[0]);
				var ghost = abilities.Preview(ToolKind.Hack, "n2");
				Check(ghost.trace == 0, "Ghost relay makes the first relay fully trace-free.");

				// Scan and hack the first system, then verify the chain hack queues its neighbor.
				abilities.Execute(ToolKind.Scan, "n2");
				FinishTask(abilities);
				abilities.Execute(ToolKind.Hack, "n2");
				Check(abilities.Execute(ToolKind.Hack, "n6"), "Chain hack queues a future neighbor.");
				FinishTask(abilities);

				// Verify queued actions executed in order and access was rechecked.
				Check(abilities.Node("n2").controlled && abilities.Node("n6").controlled,
					  "Queued actions execute in order and recheck access.");

				// Verify the firewall bridge needs a scanned target and controlled access.
				Check(!abilities.Execute(ToolKind.Bridge, "n10"),
					  "Firewall bridge requires a scanned target and controlled access.");

				// Disconnect the test operation.
				abilities.Disconnect();

				// Play the full authored campaign with an earned-XP build, using only legal actions.
				var campaign = new GameSimulation();

				// Run every authored contract in story order.
				foreach (var job in GameContent.Story)
				{
					// Start the campaign contract and verify it unlocks.
					Check(campaign.Start(job), "Campaign unlock / " + job.title);

					// Hack along the last approach to reach the objectives.
					var generated = NetworkGenerator.Generate(job, campaign.Career);
					foreach (var id in generated.approaches.Last().Skip(1))
						Hack(campaign, id);

					// Disrupt contracts also require traversing the decoy system.
					if (job.kind == JobKind.Disrupt)
						Traverse(campaign, "n14");

					// Find every objective that is not the security hub.
					foreach (var target in campaign.Op.nodes.Where(n => n.objective && n.kind != NodeKind.SecurityHub)
												   .ToArray())
					{
						// Traverse to the objective and recover its file.
						Traverse(campaign, target.id);
						Recover(campaign, target.id);
					}

					// Verify every required objective is viable on this route.
					Check(campaign.Active && campaign.ObjectiveComplete,
						  "All required objectives viable / " + job.title);

					// Disconnect to deliver and verify the contract succeeds.
					var result = campaign.Disconnect();
					Check(result.success,
						  "Career delivery / " + job.title + " / " + result.trace.ToString("0.0") + "% trace");

					// Buy the next starting technology in order, if one is still missing.
					if (!campaign.Has("quiet"))
						campaign.BuyTech("quiet");
					else if (!campaign.Has("efficient"))
						campaign.BuyTech("efficient");
					else if (!campaign.Has("selective"))
						campaign.BuyTech("selective");

					// Upgrade the chassis when the preview allows it.
					if (campaign.PreviewChassis().allowed)
						campaign.UpgradeChassis();
				}

				// Verify the story campaign completed the full career loop.
				Check(campaign.Career.completed.Count == 6 && campaign.Career.jobs == 6,
					  "All six authored contracts form a complete career loop.");

				// Start a procedural freelance contract that unlocks after story progress.
				var repeatable = GameContent.Contracts(campaign.Career).First(c => c.recurring);
				Check(campaign.Start(repeatable), "Procedural freelance work unlocks after story progress.");

				// Verify the generated network has valid security values and link endpoints.
				Check(
					campaign.Op.nodes.All(n => n.security >= 0) &&
					campaign.Op.links.All(l => campaign.Node(l.a) != null && campaign.Node(l.b) != null),
					"Generated networks have valid security and link endpoints.");

				// Start tool work so the save captures an active task.
				campaign.Execute(ToolKind.Hack, "n2");
				campaign.Tick(.5f);

				// Save the career atomically while tool work is active.
				var save = Path.Combine(dir, "validation-career.json");
				Check(CareerStorage.Save(campaign.Career, save), "Atomic career save succeeds.");

				// Reload the career and verify the active operation resumes paused.
				var restored = new GameSimulation(CareerStorage.Load(save));
				Check(restored.Active && restored.Op.paused && restored.Op.task != null && restored.Op.task.elapsed > 0,
					  "Active tool work resumes from save in a paused operation.");

				// Verify research, hardware, story, and economy survive the round trip.
				Check(
					restored.Career.completed.Count == 6 && restored.Has("quiet") &&
					restored.Career.chassisId == campaign.Career.chassisId &&
					restored.Career.rigParts.Count == campaign.Career.rigParts.Count,
					"Research, hardware, story, and economy survive a round trip.");
				Check(restored.KnownGraph.Nodes.Count == campaign.KnownGraph.Nodes.Count,
					  "Known graph projection is rebuilt correctly after load.");

				// Corrupt the save and verify it recovers from the previous atomic backup.
				CareerStorage.Save(restored.Career, save);
				File.WriteAllText(save, "invalid career");
				var backup = CareerStorage.Load(save);
				Check(backup.completed.Count == 6, "A corrupt save recovers from its previous atomic backup.");
				CareerStorage.Save(backup, save);

				// Verify reset replaces an existing save and its recovery backup.
				var resetSave = Path.Combine(dir, "validation-reset-career.json");
				CareerStorage.Save(campaign.Career, resetSave);
				Check(CareerStorage.Reset(resetSave), "Reset replaces an existing save and recovery backup.");

				// Load the reset career and verify the economy and active operation were cleared.
				var reset = new GameSimulation(CareerStorage.Load(resetSave)).Career;
				Check(
					reset.credits == 600 && reset.xp == 0 && reset.day == 1 && reset.jobs == 0 &&
					reset.totalEarnedXp == 0 && reset.operation == null,
					"Reset clears economy, story statistics, and the active operation.");

				// Verify reset also cleared contracts, reports, research, loadout, and hardware.
				Check(
					reset.completed.Count == 0 && reset.reports.Count == 0 &&
					reset.unlocked.SequenceEqual(new[] { "base" }) && reset.equipped.Count == 0 &&
					reset.chassisId == "chassis-1" && reset.rigParts.Count == 4 &&
					reset.rigParts.All(p => p.paidPrice == 0),
					"Reset clears contracts, reports, research, loadout, and hardware.");

				// Verify the recovery backup was also cleared by the reset.
				var resetBackup = new GameSimulation(CareerStorage.Load(resetSave + ".bak")).Career;
				Check(resetBackup.operation == null && resetBackup.jobs == 0 && resetBackup.completed.Count == 0,
					  "Reset also removes old progress from recovery.");

				// Corrupt the reset save and verify recovery cannot bring back the old career.
				File.WriteAllText(resetSave, "invalid career");
				Check(CareerStorage.Load(resetSave).completed.Count == 0,
					  "Recovery after reset cannot resurrect the previous career.");
				CareerStorage.Save(reset, resetSave);

				// Write the summary and the report artifact.
				results.Add("TOTAL / " + results.Count + " assertions passed.");
				File.WriteAllLines(Path.Combine(dir, "simulation-validation.txt"), results);
				Debug.Log("Hack The Planet validation: " + results.Last());
			}
			catch (Exception ex)
			{
				// Record the exception, write the partial report, and rethrow for the caller.
				results.Add(ex.ToString());
				File.WriteAllLines(Path.Combine(dir, "simulation-validation.txt"), results);
				throw;
			}
		}
	}
}