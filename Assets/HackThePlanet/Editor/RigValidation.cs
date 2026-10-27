using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace HackThePlanet.Editor
{
	/// <summary>Editor validation for rack placement, payloads, modules, and rig UI smoke helpers.</summary>
	[NoAutoStaticsCleanup]
	public static class RigValidation
	{
		// Collects passing rig validation messages for the final artifact report.
		private static readonly List<string> results = new();

		// Records a passing rig assertion or throws with the failure message.
		private static void Check(bool value, string message)
		{
			// Throw on the first failed rig check so the run stops with its message.
			if (!value)
				throw new InvalidOperationException("RIG FAILED / " + message);

			// Record the passing rig check in the results list.
			results.Add("PASS / " + message);
		}

		// Asserts two floats match within a small tolerance.
		private static void Near(float a, float b, string message)
		{
			Check(Mathf.Abs(a - b) < .001f, message + " / " + a);
		}

		// Builds a simulation with max career progress and an empty rack for rig tests.
		private static GameSimulation Rich()
		{
			// Create a simulation with currency, XP, and every technology unlocked.
			var sim = new GameSimulation();
			sim.DebugAddCredits();
			sim.DebugAddXp();
			sim.DebugUnlockAllTech();

			// Mark six contracts as delivered so every chassis is eligible.
			sim.Career.jobs = 6;

			// Upgrade the chassis to its final tier, checking each step.
			while (sim.NextChassis != null) Check(sim.UpgradeChassis(), "chassis progression upgrades sequentially");

			// Remove the starter parts so tests begin from an empty rack.
			foreach (var part in sim.Career.rigParts.ToArray())
				sim.RemoveModule(part.instanceId);

			return sim;
		}

		/// <summary>Runs the full rig validation suite and writes Artifacts/rig-validation.txt.</summary>
		[MenuItem("Hack The Planet/Rig/Validate Complete Rework")]
		public static void Run()
		{
			// Reset the results list and make sure the Artifacts folder exists.
			results.Clear();
			Directory.CreateDirectory("Artifacts");

			// Run the rig suite and write the report, recording any exception before rethrowing.
			try
			{
				// Verify every shipped rig definition loads from external JSON.
				using (var bundle = ContentJson.Load(GameData.DirectoryPath))
				{
					Check(bundle.Game.modules.Length == 16 && bundle.Game.chassis.Length == 4,
						  "all shipped rig definitions load from external JSON");
				}

				// Verify every editable field has authoring help text.
				Check(!ContentFieldHelp.CheckCoverage().Any(), "all editable fields have authoring help");

				// Create the starter simulation and verify the starter kit is a legal rig.
				var starter = new GameSimulation();
				Check(starter.RigValid && starter.Career.rigParts.Count == 4 && starter.InstalledParts.Count() == 4,
					  "starter kit fills a legal 2x3 chassis");

				// Verify the starter power, heat, storage, and RAM totals.
				Near(starter.PowerUsed, 9, "starter power demand");
				Near(starter.HeatUsed, 6, "starter heat");
				Near(starter.StorageCapacity, 100, "starter storage");
				Near(starter.MemoryPool, 50, "starter RAM");

				// Verify chassis and module research gates are enforced.
				Check(!starter.PreviewChassis().allowed && !starter.PreviewModule("power-2").allowed,
					  "chassis career and module research gates are enforced");

				// Fund the starter and buy two processor copies.
				starter.DebugAddCredits();
				var first = starter.BuyModule("processor");
				var second = starter.BuyModule("processor");

				// Verify duplicate purchases create independent inventory copies.
				Check(first != null && second != null && first.instanceId != second.instanceId && !first.Installed,
					  "duplicate purchases create independent inventory copies");

				// Verify placement rules: a full rack, overlaps, rearrangement, and rotation.
				Check(!starter.PlaceFirstFree(first.instanceId), "full rack rejects automatic placement");
				var cpu = starter.Career.rigParts.First(p => p.modelId == "processor");
				Check(!starter.TryPlaceModule(first.instanceId, 0, 0), "overlap is rejected");
				Check(starter.RemoveModule(cpu.instanceId) && starter.TryPlaceModule(first.instanceId, 0, 0),
					  "free rearrangement installs another copy");
				Check(!starter.RotateModule(first.instanceId), "rotation rechecks adjacent occupied cells");

				// Sell the spare copy and verify the refund is 75 percent of the paid price.
				var credits = starter.Career.credits;
				Check(starter.SellModule(second.instanceId) && starter.Career.credits - credits == 225,
					  "voluntary sale returns 75 percent of actual paid price");

				// Build a rich rig with researched infrastructure and verify the stacked budgets.
				var rich = Rich();
				var power = rich.BuyModule("power-1");
				var power2 = rich.BuyModule("power-2");
				var cooler = rich.BuyModule("cooling-2");
				Check(
					rich.TryPlaceModule(power.instanceId, 0, 0) && rich.TryPlaceModule(power2.instanceId, 1, 0) &&
					rich.TryPlaceModule(cooler.instanceId, 3, 0),
					"mixed researched infrastructure tiers fit independently");
				Near(rich.PowerLimit, 88, "power contributions stack");
				Near(rich.CoolingLimit, 58, "cooling contribution stacks");
				Near(rich.PowerUsed, 3, "cooling consumes power");
				Near(rich.HeatUsed, 7, "power supplies produce heat");

				// Verify rotation swaps footprint dimensions and rejects boundary overflow.
				Check(rich.TryPlaceModule(cooler.instanceId, 3, 2, true) && rich.PartSize(cooler).x == 1,
					  "rotated footprints swap dimensions");
				Check(!rich.TryPlaceModule(cooler.instanceId, 4, 5, true), "rotated boundary overflow is rejected");

				// Buy storage parts and rebuild the rack with touching storage modules.
				var storage = rich.BuyModule("storage-array");
				var drive1 = rich.BuyModule("drive");
				var drive2 = rich.BuyModule("drive");
				foreach (var part in rich.InstalledParts.ToArray())
					rich.RemoveModule(part.instanceId);
				rich.TryPlaceModule(storage.instanceId, 0, 0);
				rich.TryPlaceModule(drive1.instanceId, 2, 0);
				rich.TryPlaceModule(drive2.instanceId, 0, 2, true);

				// Verify adjacency counts each touching copy once and adds bounded storage.
				Check(rich.RigNeighborCount(storage.instanceId, "storage") == 2,
					  "large touching edges count each adjacent copy once");
				Near(rich.StorageCapacity, 380, "array adjacency adds bounded storage");

				// Buy a tier-three power module and preview the research refund it would trigger.
				var paid = rich.BuyModule("power-3");
				rich.TryPlaceModule(paid.instanceId, 3, 0);
				var owned = rich.Career.rigParts.Count;
				var refund = rich.PreviewResearchRefund();
				Check(refund.copies >= 3 && refund.credits >= paid.paidPrice,
					  "research refund previews full-price sales of newly locked copies");

				// Apply the research refund and verify it matches the preview atomically.
				credits = rich.Career.credits;
				Check(
					rich.RefundResearch() && rich.Career.credits - credits == refund.credits &&
					rich.Career.rigParts.Count == owned - refund.copies,
					"research refund applies matching sales atomically");

				// Run the limit, connection, storage, payload, and authoring suites.
				LimitsAndBudgetFailures();
				ConnectionIssues();
				StorageAndRam();
				ValidatePayloadBalance(Check);
				Authoring();

				// Regenerate the gameplay assets and verify each matches its JSON source.
				GameplayAssetGenerator.Generate();
				using (var bundle = ContentJson.Load(GameData.DirectoryPath))
				{
					foreach (var entry in bundle.Manifest.entries)
					{
						// Load the generated asset and compare its encoding with the JSON object.
						var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
							GameplayAssetGenerator.Output + "/" + entry.key + ".asset");
						Check(
							asset != null && ContentJson.Encode(asset) == ContentJson.Encode(bundle.Objects[entry.key]),
							"generated asset parity / " + entry.file);
					}
				}

				// Capture the module GUIDs before regenerating them.
				var guids = AssetDatabase.FindAssets("t:RigModuleDefinition", new[] { GameplayAssetGenerator.Output })
										 .OrderBy(x => x).ToArray();

				// Regenerate the assets again.
				GameplayAssetGenerator.Generate();

				// Verify regeneration preserves every generated asset GUID.
				Check(
					guids.SequenceEqual(AssetDatabase
										.FindAssets("t:RigModuleDefinition", new[] { GameplayAssetGenerator.Output })
										.OrderBy(x => x)), "regeneration preserves generated asset GUIDs");

				// Write the summary and the report artifact.
				results.Add("TOTAL / " + results.Count + " rig assertions passed.");
				File.WriteAllLines("Artifacts/rig-validation.txt", results);
				Debug.Log(results.Last());
			}
			catch (Exception ex)
			{
				// Record the exception, write the partial report, and rethrow for the caller.
				results.Add(ex.ToString());
				File.WriteAllLines("Artifacts/rig-validation.txt", results);
				throw;
			}
		}

		// Exercises rack limits, budgets, and placement failures against authored rig rules.
		private static void LimitsAndBudgetFailures()
		{
			// Load a private copy of the shipped content so the edits stay in memory.
			using (var content = ContentJson.Load(GameData.DirectoryPath))
			{
				// Shrink the starter chassis budgets and inflate the processor's compute modifier.
				content.Game.chassis[0].powerCapacity = 9;
				content.Game.chassis[0].coolingCapacity = 6;
				var processor = content.Game.modules.Single(m => m.id == "processor");
				processor.modifiers[0].value = new ConstantNumber(100);

				// Add a trace multiplier of zero to the authored modifiers.
				content.Game.modifiers = content.Game.modifiers
												.Concat(new[]
												{
													new Modifier
													{
														stat = "trace", operation = Arithmetic.Multiply,
														value = new ConstantNumber(0)
													}
												}).ToArray();

				// Compile the modified catalog and run the budget checks against it.
				using (var catalog = new GameCatalog(content.Game))
				{
					// Verify exact budgets are valid and the compute stat is capped.
					var sim = new GameSimulation(catalog);
					Check(sim.RigValid && sim.PowerLimit == sim.PowerUsed && sim.CoolingLimit == sim.HeatUsed,
						  "exact power and cooling budgets are valid");
					Near(sim.Stat("compute"), 3, "compute stacking is capped by authored rig rules");

					// Remove the drive and add a processor so the packing overloads the budgets temporarily.
					var drive = sim.Career.rigParts.First(p => p.modelId == "drive");
					sim.RemoveModule(drive.instanceId);
					var extra = sim.BuyModule("processor");
					Check(extra != null && sim.TryPlaceModule(extra.instanceId, 1, 0) && !sim.RigValid,
						  "packing permits temporary power and cooling budget failures");

					// Verify an overloaded assembly cannot connect.
					Check(!sim.Start(catalog.Contracts(sim.Career).First()), "invalid assembly cannot connect");

					// Remove the excess processor and verify the rig can connect again.
					sim.RemoveModule(extra.instanceId);
					Check(sim.RigValid && sim.Start(catalog.Contracts(sim.Career).First()),
						  "removing excess load restores connection eligibility");

					// Verify trace reductions never drop below the authored minimum fraction.
					Near(sim.Preview("hack", "n2").trace, 4.7f * .2f,
						 "trace reductions retain the authored minimum fraction");
				}
			}
		}

		// Validates uplink, power, and cooling connection error messaging on the rack.
		private static void ConnectionIssues()
		{
			// Remove the uplink and verify it is reported as a blocking error.
			var sim = new GameSimulation();
			var uplink = sim.Career.rigParts.First(p => p.modelId == "uplink");
			sim.RemoveModule(uplink.instanceId);
			Check(
				sim.RigIssues.Any(i => i.severity == RigIssueSeverity.Error && i.message.Contains("uplink")) &&
				sim.RigProblems.Any(p => p.Contains("uplink")), "missing uplink is a blocking rig error");
			Check(!sim.RigValid && !sim.Start(GameContent.Story[0]), "missing uplink blocks connection");

			// Reinstall the uplink and verify connection is restored.
			sim.PlaceFirstFree(uplink.instanceId);
			Check(sim.RigValid && sim.Start(GameContent.Story[0]), "installing uplink restores connection");
			sim.Disconnect();

			// Remove the drive and verify missing storage is only a warning.
			var lean = new GameSimulation();
			lean.RemoveModule(lean.Career.rigParts.First(p => p.modelId == "drive").instanceId);
			Check(
				lean.RigValid &&
				lean.RigIssues.Any(i => i.severity == RigIssueSeverity.Warning && i.message.StartsWith("No storage")),
				"missing storage is a warning that does not block connection");

			// Remove the processor as well and verify the rig still reports only warnings.
			lean.RemoveModule(lean.Career.rigParts.First(p => p.modelId == "processor").instanceId);
			Check(
				lean.RigValid && lean.RigIssues.Count == 2 &&
				lean.RigIssues.All(i => i.severity == RigIssueSeverity.Warning),
				"missing processing is a warning and warnings never block");

			// Verify a warnings-only rig can still connect.
			Check(lean.Start(GameContent.Story[0]), "warnings-only rig connects");
		}

		// Checks storage capacity, RAM reclaim, and module payload interactions.
		private static void StorageAndRam()
		{
			// Create a simulation with the Decoy and disrupt technologies unlocked.
			var sim = new GameSimulation();
			sim.DebugAddXp();
			sim.BuyTech("disrupt");
			sim.BuyTech("decoy");

			// Verify a legal rig connects and the Decoy can be activated.
			Check(sim.Start(GameContent.Story[0]), "legal rig connects");
			Check(sim.Decoy(), "persistent Decoy activates");
			Near(sim.MemoryReserved, 16, "Decoy reserves RAM");

			// Verify disabling the Decoy restores the trace pressure of a hack.
			var trace = sim.Preview("hack", "n2").trace;
			Check(sim.Decoy() && sim.MemoryReserved == 0 && sim.Preview("hack", "n2").trace > trace,
				  "disabling Decoy restores trace pressure");

			// Run a hack with the Decoy off, then turn the Decoy on mid-task.
			sim.Execute("hack", "n2");
			sim.Tick(.25f);
			var unprotected = sim.Op.trace;
			sim.Decoy();
			sim.Tick(.25f);

			// Verify enabling the Decoy reduces trace during a running tool.
			var protectedStep = sim.Op.trace - unprotected;
			Near(protectedStep, unprotected * .65f, "enabling Decoy reduces trace during a running tool");

			// Turn the Decoy off mid-task and verify the remaining trace returns to normal.
			sim.Decoy();
			var beforeDisable = sim.Op.trace;
			sim.Tick(.25f);
			Near(sim.Op.trace - beforeDisable, unprotected,
				 "disabling Decoy restores a running tool's remaining trace");

			// Stop the task and reset the trace before testing one-shot abilities.
			sim.StopTask();
			sim.Op.trace = 0;

			// Mark the disrupt target and its neighbor as known and controlled.
			sim.Node("n5").known = true;
			sim.Node("n6").known = sim.Node("n6").controlled = true;

			// Execute the one-shot and verify it occupies reclaiming RAM.
			Check(sim.Execute("disrupt", "n5"), "one-shot executes with free RAM");
			Near(sim.MemoryReclaiming, 10, "one-shot occupies reclaiming RAM");

			// Pause and verify the RAM reclamation freezes.
			sim.SetPaused(true);
			sim.Tick(.5f);
			Near(sim.MemoryReclaiming, 10, "pause freezes RAM reclamation");

			// Resume and verify RAM reclaims in simulation time.
			sim.SetPaused(false);
			sim.Tick(.5f);
			Near(sim.MemoryReclaiming, 8, "RAM reclaims in simulation time");

			// Cancel the one-shot and verify no RAM is refunded.
			Check(sim.StopTask() && sim.MemoryReclaiming == 8, "cancelling a one-shot does not refund RAM");

			// Fill the reclaiming RAM to the pool limit and verify activation is blocked.
			sim.Op.memoryReclaiming = sim.MemoryPool;
			Check(!sim.Preview("disrupt", "n5").allowed, "insufficient RAM blocks ability activation");
			sim.Op.memoryReclaiming = 0;

			// Collect an archive and verify it occupies storage.
			var archive = sim.Op.nodes.First(n => n.bonus);
			archive.known = archive.controlled = true;
			archive.collected = true;
			Near(sim.StorageUsed, archive.storage, "collected archive occupies storage");

			// Fill the drive with the archive and verify a required payload cannot download.
			var payload = sim.Op.nodes.First(n => n.payload);
			payload.known = payload.controlled = true;
			archive.storage = sim.StorageCapacity;
			Check(!sim.Preview("download", payload.id).allowed, "full drive blocks required download");

			// Verify discard runs instantly alongside another running tool.
			Check(sim.Execute("hack", "n2") && sim.Execute("discard", archive.id) && sim.Op.task != null,
				  "discard is instant alongside another running tool");
			Near(sim.StorageUsed, 0, "discard frees archive storage");
			sim.StopTask();

			// Verify required payloads cannot be discarded.
			Check(!sim.AvailableActions(payload.id).Any(a => a.id == "discard"),
				  "required payloads cannot be discarded");

			// Verify a discarded archive can be downloaded again and occupies storage once.
			archive.storage = 28;
			Check(sim.Execute("download", archive.id), "discarded archive can be downloaded again");
			GameValidation.FinishTask(sim);
			Near(sim.StorageUsed, 28, "redownload occupies storage once");

			// Activate the Decoy and set reclaiming RAM before saving mid-state.
			sim.Decoy();
			sim.Op.memoryReclaiming = 5;

			// Reload the career and verify RAM, files, and reservations survive as a paused operation.
			var restored = new GameSimulation(JsonUtility.FromJson<Career>(JsonUtility.ToJson(sim.Career)));
			Check(
				restored.Op.paused && restored.IsAbilityActive("decoy") && restored.MemoryReclaiming == 5 &&
				restored.StorageUsed == 28, "active save preserves RAM, files and reservations and restores paused");

			// Discard the archive, mark every objective complete, and disconnect.
			sim.Execute("discard", archive.id);
			foreach (var node in sim.Op.nodes.Where(n => n.objective))
				node.known = node.controlled = node.collected = true;
			var report = sim.Disconnect();

			// Verify a discarded archive pays no bonus and banks no lore.
			Check(report.success && report.bonusFiles == 0 && sim.Career.lore.Count == 0,
				  "discarded archive grants no payout or banked lore");

			// Verify delivery clears storage and RAM state.
			Near(sim.StorageUsed, 0, "delivery clears storage");
			Near(sim.MemoryReserved + sim.MemoryReclaiming, 0, "delivery clears RAM state");

			// Verify both abort and isolation reset all resource state.
			foreach (var caught in new[] { false, true })
			{
				// Start a contract, seed reclaiming RAM, and disconnect in the given mode.
				sim.Start(GameContent.Story[1]);
				sim.Op.memoryReclaiming = 10;
				sim.Disconnect(caught);

				// Verify storage, RAM, and the operation are all cleared.
				Check(!sim.Active && sim.StorageUsed == 0 && sim.MemoryReserved == 0 && sim.MemoryReclaiming == 0,
					  "abort/isolation reset resource state");
			}

			// Build a rich rig with the uplink installed and the blackout ability equipped.
			var blackout = Rich();
			blackout.PlaceFirstFree(blackout.Career.rigParts.First(p => p.modelId == "uplink").instanceId);
			blackout.Career.equipped = new List<string> { "blackout" };
			blackout.Start(GameContent.Story[0]);

			// Mark the hub and its neighbor as known, with the neighbor controlled.
			blackout.Node("n14").known = true;
			blackout.Node("n10").known = blackout.Node("n10").controlled = true;

			// Verify a persistent activation reserves RAM immediately.
			Check(blackout.Execute("blackout", "n14") && blackout.MemoryReserved == 18,
				  "persistent targeted activation reserves RAM immediately");

			// Verify cancelling the persistent activation releases the reservation.
			Check(blackout.StopTask() && blackout.MemoryReserved == 0,
				  "cancelling persistent activation releases reservation");

			// Reactivate the blackout and let it run well past its former timed duration.
			blackout.Execute("blackout", "n14");
			GameValidation.FinishTask(blackout);
			for (var i = 0; i < 100; i++)
				blackout.Tick(.5f);

			// Verify the suppression persists and keeps its RAM reserved.
			Check(blackout.Node("n14").suppressed > 0 && blackout.MemoryReserved == 18,
				  "persistent suppression survives its former timed duration");

			// Capture the suppressed hub and verify the reserved RAM is released.
			blackout.SetState(blackout.Node("n14"), NodeState.Controlled, true, blackout.Context());
			Check(blackout.MemoryReserved == 0, "capturing a suppressed hub releases RAM");
		}

		/// <summary>Checks generated payload storage fits starter chassis across contracts, seeds, and difficulties.</summary>
		/// <param name="check">Callback recording pass/fail assertions.</param>
		public static void ValidatePayloadBalance(Action<bool, string> check)
		{
			// Use the default catalog and a fresh career to describe the contracts.
			var catalog = GameCatalog.Default;
			var career = catalog.CreateCareer();

			// Check every shipped contract across three difficulties and twenty seeds.
			foreach (var job in catalog.Data.contracts.Select(d => catalog.DescribeContract(d, career)))
				for (var difficulty = 1; difficulty <= 3; difficulty++)
					for (var seed = 0; seed < 20; seed++)
					{
						// Apply this difficulty and seed to the contract.
						job.difficulty = difficulty;
						job.seed = seed;

						// Generate the network and verify its payloads fit the bare starter chassis.
						var network = NetworkGenerator.Generate(job, career, catalog);
						check(
							network.nodes.Where(n => n.payload).Sum(n => n.storage) <=
							catalog.Data.rig.startingChassis.storageCapacity,
							"bare starter payload fit / " + job.id + " / " + difficulty + " / " + seed);

						// Regenerate the network and verify its storage values are deterministic.
						var again = NetworkGenerator.Generate(job, career, catalog);
						if (!network.nodes.Select(n => n.storage).SequenceEqual(again.nodes.Select(n => n.storage)))
							throw new InvalidOperationException("Nondeterministic storage");
					}
		}

		// Verifies JSON rig authoring helpers and preview placement in the content editor.
		private static void Authoring()
		{
			// Create a unique scratch directory for the authoring round trip.
			var directory = Path.GetFullPath("Artifacts/rig-authoring-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);

			// Copy every shipped JSON file into the scratch directory.
			using (var source = ContentJson.Load(GameData.DirectoryPath))
			{
				foreach (var pair in source.Files)
				{
					// Write each shipped file to its matching path in the scratch directory.
					var file = ContentJson.SafePath(directory, pair.Key);
					Directory.CreateDirectory(Path.GetDirectoryName(file));
					File.WriteAllText(file, pair.Value);
				}
			}

			// Drive the Content Editor's rig authoring and verify the edited values load back.
			var edited = ContentEditorWindow.ExerciseRigAuthoring(directory);
			using (var loaded = ContentJson.Load(directory, edited))
			{
				// Verify the author-controlled power and cooling capacities.
				Check(
					loaded.Game.modules.Single(m => m.id == "power-1").powerCapacity == 9 &&
					loaded.Game.modules.Single(m => m.id == "cooling-1").coolingCapacity == 7,
					"Content Editor controls author power and cooling capacity");

				// Verify the author-controlled RAM, storage, and ability costs.
				Check(
					loaded.Game.rig.memoryReclaim == 5 && loaded.Game.chassis[0].storageCapacity == 81 &&
					loaded.Game.actions.Single(a => a.id == "backdoor").memoryCost == 11,
					"Content Editor authors RAM, storage and ability costs");
			}

			// Write the edited files back to the scratch directory.
			foreach (var pair in edited)
			{
				// Write each edited file to its matching path.
				var file = ContentJson.SafePath(directory, pair.Key);
				Directory.CreateDirectory(Path.GetDirectoryName(file));
				File.WriteAllText(file, pair.Value);
			}

			// Create a content draft and open a workspace over the scratch directory.
			var draft = ScriptableObject.CreateInstance<ContentDraft>();
			try
			{
				using (var workspace = new ContentWorkspace(draft, directory))
				{
					// Find the shipped power-1 module entry to base the modded copy on.
					var power = workspace.Bundle.Manifest.entries.Single(e => e.category == "Modules" &&
																			  ((RigModuleDefinition)workspace.Bundle
																				  .Objects[e.key]).id == "power-1");

					// Create a modded copy of power-1 with new identity and capacity values.
					var added = workspace.Add("Modules", power.key);
					var model = (RigModuleDefinition)workspace.Bundle.Objects[added.key];
					model.id = "modded-power";
					model.tier = 4;
					model.powerCapacity = 17;

					// Gate the modded module behind the infrastructure-2 research.
					model.requirements = new[]
					{
						new Requirement
						{
							condition = new ResearchCondition
							{
								technology = workspace.Bundle.Game.technologies.Single(t => t.id == "infrastructure-2")
							},
							message = "Research infrastructure."
						}
					};

					// Add a storage bonus that only applies when a cooling module is adjacent.
					model.modifiers = new[]
					{
						new Modifier
						{
							stat = "storageCapacity", operation = Arithmetic.Add, value = new ConstantNumber(7),
							when = new RigNeighborCondition { kind = "cooling", minimum = 1 }
						}
					};

					// Capture the workspace edits into the draft.
					workspace.Capture();

					// Commit the draft to the content history and accept it.
					new ContentHistory(directory).Commit(draft.Current, draft.Original, "Rig authoring validation");
					draft.Accept();

					// Reload the edited content and build a catalog from it.
					using (var content = ContentJson.Load(directory))
					{
						using (var catalog = new GameCatalog(content.Game))
						{
							// Start a funded simulation and clear the starter rack.
							var sim = new GameSimulation(catalog);
							sim.DebugAddCredits();
							sim.DebugAddXp();
							sim.Career.jobs = 6;
							while (sim.NextChassis != null) sim.UpgradeChassis();
							foreach (var part in sim.Career.rigParts.ToArray())
								sim.RemoveModule(part.instanceId);

							// Verify the modded module is gated by research and needs no C# changes.
							Check(sim.BuyModule("modded-power") == null && sim.BuyTech("infrastructure-2"),
								  "modded module research gate works without C# changes");

							// Place the modded module beside a cooling module to trigger its adjacency bonus.
							var copy = sim.BuyModule("modded-power");
							var cooling = sim.BuyModule("cooling-1");
							sim.TryPlaceModule(copy.instanceId, 0, 0);
							sim.TryPlaceModule(cooling.instanceId, 1, 0);

							// Verify the JSON-authored power and storage values take effect at runtime.
							Near(sim.PowerLimit, 77, "external JSON changes power capacity without asset regeneration");
							Near(sim.StorageCapacity, 267,
								 "external JSON adjacency effect executes without runtime C# changes");

							// Verify the edits survive save, reload, and detached catalog compilation.
							Check(sim.Catalog.Data.rig.memoryReclaim == 5 && sim.Module("recycler").price == 401,
								  "editor changes survive save, reload and detached catalog compilation");
						}
					}
				}
			}
			finally
			{
				// Destroy the temporary content draft.
				Object.DestroyImmediate(draft);
			}
		}

		/// <summary>Asserts rig view layout and rack controls during workstation smoke tests.</summary>
		/// <param name="app">Workstation application under test.</param>
		public static void CheckRackUi(WorkstationApp app)
		{
			// Locate the rack viewport that hosts the rig layout.
			var viewport = app.Root.Q<GraphViewport>();

			// Verify the inventory rack uses the flat cell layout and installed modules render as grid items.
			Check(viewport != null && app.Root.Q(className: "flat-rig") != null,
				  "inventory rack uses flat cell layout");
			Check(app.Root.Q<Button>(className: "rig-module") != null, "installed modules render as grid items");

			// Verify problems are listed above the rack and packing controls exist.
			Check(app.Root.Q("rig-issues") != null, "rig problems are listed above the rack");
			Check(
				app.Root.Q<Button>("rig-place-first") != null && app.Root.Q<Button>("rig-rotate") != null &&
				app.Root.Q<Button>("rig-remove") != null, "keyboard-accessible packing controls exist");

			// Verify the layout splits details and inventory and provides category tabs.
			Check(
				app.Root.Q(className: "rig-details") != null && app.Root.Q(className: "rig-inventory") != null &&
				app.Root.Q<Button>("rig-category-ALL") != null,
				"rig splits selected details and inventory with category tabs");

			// Verify spare parts appear in the inventory list.
			Check(
				app.State.Career.rigParts.All(p => p.Installed) ||
				app.Root.Q<Button>(className: "rig-inventory-row") != null, "spare parts appear in the inventory list");

			// Find an installed processor and its button to test overlay placement.
			var part = app.State.Career.rigParts.First(p => p.modelId == "processor" && p.Installed);
			var button = app.Root.Q<Button>("part-" + part.instanceId);

			// Verify the module overlay sits inside the rack viewport.
			Check(
				button != null && button.worldBound.width > 0 && button.worldBound.height > 0 &&
				viewport.worldBound.Overlaps(button.worldBound), "module overlays lie inside the rack viewport");

			// Verify the rig displays its assembly budgets.
			Check(app.Root.Query<Label>().ToList().Any(l => l.text.Contains("COOLING")),
				  "rig displays assembly budgets");
		}
	}
}