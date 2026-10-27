using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using UnityEngine;
using static HackThePlanet.Editor.DefaultGameplayAssets;
using Object = UnityEngine.Object;

namespace HackThePlanet.Editor
{
	/// <summary>Validates stat modifiers, rig rules, and asset-only fixtures against the live JSON catalog.</summary>
	[NoAutoStaticsCleanup]
	public static class DataDrivenValidation
	{
		// Collects passing data-driven validation messages for artifact output.
		private static readonly List<string> results = new();

		// Gets the live game definition loaded from the default JSON catalog.
		private static GameDefinition Root => GameData.Current.Game;

		// Records a passing check or throws with the failure message.
		private static void Check(bool pass, string message)
		{
			// Throw on the first failed check so the run stops with its message.
			if (!pass)
				throw new InvalidOperationException("FAILED / " + message);

			// Record the passing check in the results list.
			results.Add("PASS / " + message);
		}

		// Asserts two floats are within tolerance for modifier and stat checks.
		private static void Near(float actual, float expected, string message)
		{
			Check(Mathf.Abs(actual - expected) < .0002f, message + " / " + actual.ToString("0.####"));
		}

		// Completes the active task and asserts the operation remains within trace budget.
		private static void Finish(GameSimulation sim)
		{
			// Let the pending task finish.
			GameValidation.FinishTask(sim);

			// Verify the action stayed inside the trace budget.
			Check(sim.Active, "action remains within trace budget");
		}

		// Starts an operation with the requested technologies researched on a funded career.
		private static GameSimulation Developed(params string[] technologies)
		{
			// Create a funded career so every requested technology is affordable.
			var career = GameCatalog.Default.CreateCareer();
			career.xp = 10000;
			career.credits = 10000;

			// Unlock each requested technology once.
			foreach (var id in technologies)
				if (!career.unlocked.Contains(id))
					career.unlocked.Add(id);

			// Equip the requested special abilities.
			career.equipped = GameContent.Tech.Where(t => t.special && technologies.Contains(t.id)).Select(t => t.id)
										 .ToList();

			// Fixtures keep the starter uplink, since connecting requires one. Every other part is removed so tests start from a bare rack.
			var sim = new GameSimulation(career);
			foreach (var part in sim.Career.rigParts.Where(p => p.modelId != GameSimulation.UplinkModelId).ToArray())
				sim.RemoveModule(part.instanceId);

			// Start the first story contract on the bare rack.
			sim.Start(GameContent.Story[0]);
			return sim;
		}

		/// <summary>Runs catalog-driven simulation checks and writes Artifacts/data-driven-validation.txt.</summary>
		[MenuItem("Hack The Planet/Run Data Driven Validation")]
		public static void Run()
		{
			// Reset the results list and make sure the Artifacts folder exists.
			results.Clear();
			Directory.CreateDirectory("Artifacts");

			// Run every suite and write the report, recording any exception before rethrowing.
			try
			{
				// Confirm the shipped gameplay definitions validate.
				GameDefinitionValidator.ThrowIfInvalid(Root);
				Check(true, "shipped gameplay definitions validate");

				// Load the captured baseline snapshot that network output must match exactly.
				var captured =
					JsonUtility.FromJson<GameplaySnapshot>(File.ReadAllText("Artifacts/gameplay-baseline.json"));

				// Regenerate every captured network and compare its hash with the baseline.
				foreach (var golden in captured.networks)
				{
					// Build the career and contract that match the captured run.
					var career = new Career();
					if (golden.hardened)
						career.completed.Add("quiet-copy");

					var job = new Contract
					{
						id = "quiet-copy", company = captured.companies[0], seed = golden.seed,
						difficulty = golden.difficulty
					};

					// Generate the network and hash it for comparison.
					var actual = NetworkGenerator.Generate(job, career);
					var hash = GameplayBaseline.Hash(actual);

					// Write a mismatch snapshot to help diagnose any drift from the baseline.
					if (hash != golden.hash)
						File.WriteAllText("Artifacts/network-parity-mismatch.json",
										  JsonUtility.ToJson(new NetworkSnapshot(actual), true));

					// Verify the generated network matches the captured hash.
					Check(hash == golden.hash,
						  "exact network parity / " + golden.seed + " / difficulty " + golden.difficulty +
						  " / hardened " + golden.hardened);
				}

				// Run the mechanics, authoring, and asset-extension suites.
				ValidateMechanics();
				ValidateAuthoring();
				ValidateAssetExtension();

				// Run the existing simulation suite and record its pass.
				GameValidation.Run();
				Check(true, "existing simulation, full campaign, network, lore, settings-save validation passes");

				// Write the summary and the report artifact.
				results.Add("TOTAL / " + results.Count + " assertions passed.");
				File.WriteAllLines("Artifacts/data-driven-validation.txt", results);
				Debug.Log(results.Last());
			}
			catch (Exception ex)
			{
				// Record the exception, write the partial report, and rethrow for the caller.
				results.Add(ex.ToString());
				File.WriteAllLines("Artifacts/data-driven-validation.txt", results);
				throw;
			}
		}

		// Runs modifier, trigger, and rig mechanic checks against the live catalog.
		private static void ValidateMechanics()
		{
			// Verify the baseline relay hack duration and trace before any modifiers apply.
			var plain = Developed();
			var hack = plain.Preview("hack", "n2");
			Near(hack.duration, 3.35f, "baseline relay hack duration");
			Near(hack.trace, 4.7f, "baseline relay hack trace");

			// Verify the efficient and quiet technologies scale duration and trace.
			var efficient = Developed("efficient", "quiet");
			Near(efficient.Preview("hack", "n2").duration, 3.35f * .8f, "efficient hack duration");
			Near(efficient.Preview("hack", "n2").trace, 4.7f * .8f, "quiet hack trace");

			// Verify the ghost relay makes the first relay trace-free and consumes its control trigger.
			var ghost = Developed("quiet", "ghost");
			Near(ghost.Preview("hack", "n2").trace, 0, "first relay fully trace free");
			ghost.Execute("hack", "n2");
			Finish(ghost);
			Check(ghost.Counter("relay-controls") == 1, "control trigger consumes first relay");

			// Verify signal halves scan duration, removes base scan trace, and widens scan radius.
			var scan = Developed("wide", "signal");
			Near(scan.Preview("scan", "n2").duration, .8f, "signal halves scan duration");
			Near(scan.Preview("scan", "n2").trace, 0, "signal removes base scan trace");
			scan.Execute("scan", "n2");
			Finish(scan);
			Near(scan.Stat("scanRadius"), 3, "wide scan radius");

			// Verify passive mapping reveals three hops from the relay.
			var passive = Developed("wide", "quiet", "passive");
			passive.Execute("hack", "n2");
			Finish(passive);
			Check(passive.Node("n14").known, "passive mapping reveals three hops from relay");

			// Verify the blueprint trigger reveals the target's neighborhood when an operation starts.
			var blueprint = Developed("wide", "blueprint");
			Check(blueprint.Node("n22").known && blueprint.Neighbors(blueprint.Node("n22")).All(n => n.known),
				  "blueprint operation-start trigger");

			// Verify scan preparation, weak-point, and global buffer modifiers stack multiplicatively.
			var weak = Developed("wide", "efficient", "weakpoint", "buffer");
			weak.Node("n2").scanned = true;
			Near(weak.Preview("hack", "n2").trace, 4.7f * .8f * .75f * .9f,
				 "scan preparation, weak-point and global buffer stack");

			// Verify scanner pressure follows duration modifiers and precedes global trace modifiers.
			var pressure = Developed("efficient", "quiet", "buffer");
			pressure.Node("n6").known = true;
			pressure.Node("n6").controlled = true;
			pressure.Node("n10").known = true;
			var p = pressure.Preview("hack", "n10");
			var basis = 3 + pressure.EffectiveSecurity(pressure.Node("n10")) * 1.7f;
			Near(p.trace, (basis * .8f + p.duration * pressure.Scanners * .18f) * .9f,
				 "scanner pressure follows duration modifiers and precedes global trace modifiers");

			// Verify the selective main-objective download duration and trace.
			var selective = Developed("efficient", "selective");
			selective.Node("n22").known = true;
			selective.Node("n22").controlled = true;
			Near(selective.Preview("download", "n22").duration, selective.Node("n22").storage * .7f / 1.25f,
				 "selective main-objective duration is file size over uplink bandwidth");
			Near(selective.Preview("download", "n22").trace, 8 * .7f, "selective main-objective trace");

			// Verify chain hacks queue future neighboring actions and reject duplicates.
			var chain = Developed("efficient", "chain");
			chain.Execute("scan", "n2");
			Finish(chain);
			Check(chain.Execute("hack", "n2") && chain.Execute("hack", "n6"),
				  "chain permits future neighboring action");
			Check(!chain.Execute("hack", "n6"), "duplicate queue entries rejected");
			Finish(chain);
			Check(chain.Node("n6").controlled, "queued action executes after gaining reachability");

			// Verify queued actions are rechecked and skipped when their requirements are no longer met.
			var invalid = Developed();
			invalid.Execute("hack", "n2");
			invalid.Node("n6").known = true;
			Check(invalid.Execute("scan", "n6"), "queue defers future reachability");
			invalid.Node("n6").scanned = true;
			Finish(invalid);
			Check(invalid.Op.queue.Count == 0 && invalid.Op.log.Any(l => l.Contains("SKIPPED")),
				  "queued requirements rechecked and completed action skipped");

			// Verify the decoy toggles RAM and reduces trace without refunding existing trace.
			var decoy = Developed("disrupt", "decoy");
			decoy.Op.trace = 30;
			Check(decoy.Execute("decoy"), "persistent operator action executes");
			Near(decoy.Op.trace, 30, "decoy does not refund trace");
			Near(decoy.MemoryReserved, 16, "decoy reserves authored RAM");
			var decoyTrace = decoy.Preview("hack", "n2").trace;
			Near(decoyTrace, 4.7f * .65f, "decoy reduces trace by its authored modifier");
			Check(decoy.Execute("decoy") && decoy.MemoryReserved == 0, "operator toggle releases RAM");

			// Verify scanner suppression expires and restores pressure.
			var suppression = Developed("disrupt", "blackout", "efficient", "cascade");
			suppression.Node("n6").known = true;
			suppression.Node("n6").controlled = true;
			suppression.Node("n5").known = true;
			Check(suppression.Execute("disrupt", "n5"), "scanner suppression action available");
			Finish(suppression);
			Near(suppression.Node("n5").suppressed, 45, "suppression uses authored duration");
			var count = suppression.Scanners;

			// Advance time past the suppression duration.
			for (var i = 0; i < 121; i++)
				suppression.Tick(.5f);
			Check(suppression.Node("n5").suppressed == 0 && suppression.Scanners > count,
				  "expiration restores scanner pressure");

			// Verify the hub suppression cascades firewall changes exactly once.
			suppression.Node("n10").known = true;
			suppression.Node("n10").controlled = true;
			suppression.Node("n14").known = true;
			var wall = suppression.Node("n18").security;
			Check(suppression.Execute("blackout", "n14"), "hub suppression action available");
			Finish(suppression);
			Check(suppression.Node("n18").security == Mathf.Max(1, wall - 2) && suppression.Counter("cascade") == 1,
				  "cascade modifies all firewalls once");

			// Capture the suppressed hub and verify the cascade is not applied a second time.
			suppression.Op.trace = 0;
			suppression.Execute("hack", "n14");
			Finish(suppression);
			Check(suppression.Node("n18").security == Mathf.Max(1, wall - 2),
				  "capturing suppressed hub does not apply cascade twice");

			// Prepare a bridge by controlling one neighbor, then verify it removes all trace and applies control.
			var bridge = Developed("wide", "blueprint", "efficient", "chain", "bridge");
			bridge.Node("n10").known = true;
			bridge.Node("n10").scanned = true;
			foreach (var n in bridge.Neighbors(bridge.Node("n10")).Take(1))
			{
				n.known = true;
				n.controlled = true;
			}

			Check(bridge.Preview("bridge", "n10").allowed, "prepared bridge accepts one controlled neighbor");
			Near(bridge.Preview("bridge", "n10").trace, 0, "bridge terminal override removes all trace");
			bridge.Execute("bridge", "n10");
			Finish(bridge);
			Check(bridge.Node("n10").controlled, "bridge applies reusable control effect");

			// Verify the backdoor reaches a mapped three-hop shortcut and is single-use.
			var backdoor = Developed("quiet", "backdoor");
			backdoor.Execute("scan", "n2");
			Finish(backdoor);
			var mappedRoute = NetworkGenerator.Generate(backdoor.Job, backdoor.Career).approaches.Last();
			var remote = backdoor.Node(mappedRoute[3]);
			Check(!backdoor.Reachable(remote) && backdoor.ControlledDistance(remote) == 3,
				  "backdoor finds a mapped three-hop shortcut");
			Check(backdoor.Execute("backdoor", remote.id), "backdoor bypasses direct reachability within its range");
			Finish(backdoor);
			Check(remote.controlled && !backdoor.Preview("backdoor", "n2").allowed, "backdoor one-use counter");

			// Save mid-operation with queued work and verify the restored state matches.
			var saved = Developed("disrupt", "decoy", "efficient", "chain");
			saved.Execute("decoy");
			saved.Execute("scan", "n2");
			saved.Tick(.5f);
			saved.Execute("hack", "n2");
			var restore = new GameSimulation(JsonUtility.FromJson<Career>(JsonUtility.ToJson(saved.Career)));
			Check(
				restore.Op.paused && restore.Op.queue.Count == 1 && restore.Op.task.actionId == "scan" &&
				restore.Op.task.elapsed == .5f && restore.Counter("uses:decoy") == 1 &&
				restore.IsAbilityActive("decoy") && restore.MemoryReserved == 16,
				"v2 round trip preserves task, queue, counters and timed statuses");
		}

		// Validates rig and content authoring coverage helpers used by the editor.
		private static void ValidateAuthoring()
		{
			// Create a working copy of the root definition and add a duplicate action ID.
			var root = Object.Instantiate(Root);
			var action = Object.Instantiate(Root.actions[0]);
			root.actions = root.actions.Concat(new[] { action }).ToArray();

			// Verify duplicate definition IDs are rejected.
			Check(GameDefinitionValidator.Validate(root).Any(e => e.Contains("unique ID")),
				  "duplicate definition IDs rejected");

			// Give the copied action a new ID and a constant division by zero.
			action.id = "invalid-action";
			action.duration = B(Arithmetic.Divide, N(1), N(0));
			Check(GameDefinitionValidator.Validate(root).Any(e => e.Contains("division/modulo")),
				  "constant division by zero rejected");

			// Set a negative duration and verify timing validation rejects it.
			action.duration = N(-1);
			Check(GameDefinitionValidator.Validate(root).Any(e => e.Contains("positive duration")),
				  "invalid timing rejected");

			// Create a condition that references itself and verify the cycle is rejected.
			var cycle = new NotCondition();
			cycle.condition = cycle;
			action.visible = cycle;
			Check(GameDefinitionValidator.Validate(root).Any(e => e.Contains("cyclic")),
				  "cyclic managed rules rejected");

			// Restore a valid condition and duration, then add a self-referencing stat modifier.
			action.visible = new AlwaysCondition();
			action.duration = N(1);
			root.modifiers = new[] { Mod("queueCapacity", Arithmetic.Override, S("queueCapacity")) };
			Check(GameDefinitionValidator.Validate(root).Any(e => e.Contains("cyclic stat dependency")),
				  "cyclic stat formulas rejected");

			// Clear the modifiers and add a technology that depends on itself.
			root.modifiers = Array.Empty<Modifier>();
			var tech = Object.Instantiate(Root.technologies[0]);
			tech.id = "cycle-tech";
			tech.parents = new[] { tech };
			root.technologies = root.technologies.Concat(new[] { tech }).ToArray();
			Check(GameDefinitionValidator.Validate(root).Any(e => e.Contains("research dependency cycle")),
				  "research dependency cycles rejected");

			// Add a contract with no objectives and verify it is rejected.
			var contract = Object.Instantiate(Root.contracts[0]);
			contract.id = "empty-contract";
			contract.objectives = Array.Empty<ObjectiveDefinition>();
			root.contracts = root.contracts.Concat(new[] { contract }).ToArray();
			Check(GameDefinitionValidator.Validate(root).Any(e => e.Contains("at least one objective")),
				  "empty objective contracts rejected");

			// Point the objective at a node that does not exist.
			contract.objectives = new[] { new ObjectiveDefinition { nodeId = "missing" } };
			Check(GameDefinitionValidator.Validate(root).Any(e => e.Contains("missing target")),
				  "impossible objective references rejected");

			// Verify an unknown technology reference in a career is rejected before it is used.
			var saved = GameCatalog.Default.CreateCareer();
			saved.unlocked.Add("removed-technology");
			var failed = false;
			try
			{
				// Build a simulation from the invalid career.
				new GameSimulation(saved);
			}
			catch (InvalidDataException)
			{
				// Record the expected rejection.
				failed = true;
			}

			Check(failed, "unknown v2 references rejected before modifying save");

			// Write the invalid save and confirm loading it leaves the file byte-for-byte intact.
			var path = "Artifacts/invalid-reference-career.json";
			File.WriteAllText(path, JsonUtility.ToJson(saved));
			var before = File.ReadAllText(path);
			failed = false;
			try
			{
				// Attempt to load the invalid save.
				CareerStorage.Load(path);
			}
			catch (InvalidDataException)
			{
				// Record the expected rejection.
				failed = true;
			}

			Check(failed && File.ReadAllText(path) == before, "invalid v2 save stays byte-for-byte intact");

			// Release the temporary definitions and reload the gameplay text palette.
			Object.DestroyImmediate(root);
			Object.DestroyImmediate(action);
			Object.DestroyImmediate(tech);
			Object.DestroyImmediate(contract);
			GameplayRichText.ReloadPalette();

			// Build a scanner node and describe it the way the tooltip does.
			var scanner = new NetNode
			{
				label = "Traffic scanner", kind = NodeKind.Scanner, security = 2, known = true,
				detail = GameCatalog.Default.NodeTypes.First(t => t.glyph == NodeKind.Scanner).description
			};
			var intel = GraphViewport.DescribeNode(scanner, ContractIntel.Full, false, GameCatalog.Default);
			var rich = GameplayRichText.Format(intel);

			// Verify tooltip terms are bolded and colored.
			Check(rich.Contains("<color=") && rich.Contains("<b>") && rich.Contains("DISCOVERED"),
				  "gameplay text palette bolds and colors tooltip terms");
			Check(GameplayRichText.Plain(rich) == intel, "gameplay rich text preserves readable copy");

			// Verify common plurals are colored together with their base term.
			var plural = GameplayRichText.Format("Scanners add pressure.");
			Check(
				GameplayRichText.Plain(plural) == "Scanners add pressure." &&
				plural.IndexOf("<color=", StringComparison.Ordinal) <
				plural.IndexOf("Scanners", StringComparison.Ordinal) &&
				plural.LastIndexOf("Scanners", StringComparison.Ordinal) <
				plural.IndexOf("</color>", StringComparison.Ordinal),
				"gameplay rich text colors common plurals with the base term");
		}

		/// <summary>Builds an isolated in-memory catalog with asset-only contract content for UI smoke tests.</summary>
		/// <returns>A disposable fixture owning temporary ScriptableObject instances.</returns>
		public static Fixture CreateFixture()
		{
			// Create an isolated fixture that owns every asset it creates.
			var fixture = new Fixture();

			// Copy the shipped root definition so the fixture never edits live data.
			var root = Object.Instantiate(Root);
			root.name = "Asset-only validation fixture";
			fixture.Owned.Add(root);
			fixture.Definition = root;

			// Add a trigger that counts expired statuses.
			root.triggers = root.triggers.Concat(new[]
			{
				new Trigger
				{
					on = RuleEvent.StatusExpired,
					effects = new Effect[] { new CounterEffect { key = "expired-statuses" } }
				}
			}).ToArray();

			// Create a definition asset with the given ID and title and track it for disposal.
			T New<T>(string id, string title) where T : GameplayDefinition
			{
				// Create the asset and set its identity.
				var d = ScriptableObject.CreateInstance<T>();
				d.id = id;
				d.title = title;
				d.name = id;

				// Track the asset so the fixture can destroy it later.
				fixture.Owned.Add(d);
				return d;
			}

			// Create the audit terminal node type and register it with the root.
			var nodeType = New<NodeTypeDefinition>("audit-terminal", "Audit terminal");
			nodeType.glyph = NodeKind.Workstation;
			nodeType.color = Color.magenta;
			nodeType.tags = new[] { "audit-target" };
			nodeType.security = N(1);
			root.nodeTypes = root.nodeTypes.Concat(new[] { nodeType }).ToArray();

			// Create the verify action with its timing, shortcut, and visibility.
			var action = New<ActionDefinition>("audit", "Verify sample");
			action.duration = N(2);
			action.baseTrace = N(1);
			action.shortcut = KeyCode.V;
			action.shortcutLabel = "V";
			action.tags = new[] { "analysis" };
			action.visible = Tag("audit-target");

			// Require discovery and control before the action can run.
			action.requirements = new[]
			{
				Req(State(NodeState.Known), "Discover the target."),
				Req(State(NodeState.Controlled), "Control this terminal first.")
			};

			// Require discovery before the action can be queued.
			action.queueRequirements = new[] { Req(State(NodeState.Known), "Discover the target.") };

			// Mark the target collected and log the verification when the action runs.
			action.effects = new[]
				{ new SetStateEffect { state = NodeState.Collected }, Log("SAMPLE VERIFIED // {target}") };
			root.actions = root.actions.Concat(new[] { action }).ToArray();

			// Create a crossover technology that grants the new action.
			var tech = New<TechnologyDefinition>("field-analysis", "Field analysis");
			tech.branch = "CROSSOVER";
			tech.position = new Vector2(920, 600);
			tech.parents = new[] { root.technologies[0] };
			tech.grants = new[] { action };
			root.technologies = root.technologies.Concat(new[] { tech }).ToArray();

			// Create an expansion module that adds memory pool capacity.
			var expansion = New<RigModuleDefinition>("expansion", "Analysis expansion");
			expansion.family = "analysis";
			expansion.branch = "CROSSOVER";
			expansion.price = 1;
			expansion.modifiers = new[] { Mod("memoryPool", Arithmetic.Add, N(2)) };
			root.modules = root.modules.Concat(new[] { expansion }).ToArray();

			// Build the authored field network with its entry, preferred, and preview nodes.
			var network = New<NetworkDefinition>("field-network", "Field network");
			network.layout = NetworkLayout.Authored;
			network.entryNode = "entry";
			network.preferredNode = "alpha";
			network.previewNode = "beta";

			// Define the four authored nodes of the field network.
			network.nodes = new[]
			{
				new NetworkNode
				{
					id = "entry", label = "Entry", type = root.nodeTypes[0], known = true, controlled = true,
					position = new Vector3(-6, 0, 0)
				},
				new NetworkNode
					{ id = "alpha", label = "Access terminal", type = nodeType, known = true, position = Vector3.zero },
				new NetworkNode
				{
					id = "beta", label = "Sample terminal", type = nodeType, known = true,
					position = new Vector3(6, 0, 0)
				},
				new NetworkNode
					{ id = "side", label = "Side terminal", type = nodeType, position = new Vector3(0, 5, 4) }
			};

			// Connect the authored nodes with three links.
			network.links = new[]
			{
				new NetworkEdge { a = "entry", b = "alpha" }, new NetworkEdge { a = "alpha", b = "beta" },
				new NetworkEdge { a = "alpha", b = "side" }
			};
			root.networks = root.networks.Concat(new[] { network }).ToArray();

			// Create the field contract that runs on the authored network.
			var contract = New<ContractDefinition>("field-contract", "Field investigation");
			contract.company = root.companies[0];
			contract.network = network;
			contract.objective = "Verify the sample.";
			contract.credits = N(111);
			contract.xp = N(7);
			contract.seed = N(99);

			// Define the objective, which needs the beta sample verified after alpha is controlled.
			contract.objectives = new[]
			{
				new ObjectiveDefinition
				{
					nodeId = "beta", action = action, actionLabel = "Verify sample",
					complete = State(NodeState.Collected),
					actionRequirements = new[]
					{
						Req(new NamedNodeCondition { nodeId = "alpha", condition = State(NodeState.Controlled) },
							"Control the access terminal first.")
					}
				}
			};
			root.contracts = root.contracts.Concat(new[] { contract }).ToArray();

			// Compile the fixture definitions into a catalog and return the fixture.
			fixture.Catalog = new GameCatalog(root);
			return fixture;
		}

		// Exercises the asset-only fixture contract, actions, and hardware extensions.
		private static void ValidateAssetExtension()
		{
			// Build the asset-only fixture and run the extension checks against it.
			using (var fixture = CreateFixture())
			{
				// Create a simulation on the fixture catalog and fund it.
				var sim = new GameSimulation(fixture.Catalog);
				sim.Career.credits = 10000;

				// Verify the asset-only technology grants its new action.
				Check(sim.BuyTech("field-analysis"), "asset-only technology can grant a new action");

				// Buy the expansion module, then free the RAM slot so the module can be placed.
				var copy = sim.BuyModule("expansion");
				sim.RemoveModule(sim.Career.rigParts.First(p => p.modelId == "ram").instanceId);

				// Verify the placed module changes generic memory stats.
				Check(copy != null && sim.PlaceFirstFree(copy.instanceId) && sim.MemoryPool == 42,
					  "asset-only module affects generic stats");
				Check(sim.BuyModule("expansion") != null, "asset-only module supports repeat copies");

				// Start the asset-only contract and verify its custom layout and node IDs.
				Check(sim.Start(fixture.Catalog.Contracts(sim.Career).First(c => c.id == "field-contract")),
					  "asset-only contract uses a different network layout");
				Check(
					sim.Op.nodes.Count == 4 && sim.Op.entryNode == "entry" &&
					sim.Node("beta").typeId == "audit-terminal", "asset-only node type and arbitrary stable node IDs");

				// Take control of the access and sample terminals.
				GameValidation.Hack(sim, "alpha");
				GameValidation.Hack(sim, "beta");

				// Verify the new action is discoverable and uses its authored duration.
				Check(sim.AvailableActions("beta").Any(a => a.id == "audit" && a.shortcut == KeyCode.V),
					  "new action discoverable with an authored shortcut");
				Near(sim.Preview("audit", "beta").duration, 2, "new action uses authored formula");

				// Edit the authored asset and verify the compiled catalog is not affected.
				fixture.Definition.actions.First(a => a.id == "audit").duration = N(999);
				Near(sim.Preview("audit", "beta").duration, 2,
					 "compiled catalog is isolated from subsequent asset changes");

				// Execute the new action and verify a status expiry runs its authored effect.
				Check(sim.Execute("audit", "beta"), "new composed action executes");
				sim.ApplyStatus(fixture.Catalog.Data.statuses.First(s => s.preventsTrace), null, .25f);
				sim.Tick(.5f);
				Check(sim.Counter("expired-statuses") == 1, "status-expiration event runs authored effects");

				// Apply a long status and verify the generic queue accepts a new action.
				sim.ApplyStatus(fixture.Catalog.Data.statuses.First(s => s.preventsTrace), null, 5);
				Check(sim.Execute("scan", "beta"), "new action supports the generic queue");

				// Save the custom catalog career and reload it with the same catalog.
				const string savePath = "Artifacts/asset-extension-career-v2.json";
				Check(CareerStorage.Save(sim.Career, savePath, fixture.Catalog),
					  "custom catalog career saves with stable IDs");
				var restored = new GameSimulation(fixture.Catalog, CareerStorage.Load(savePath, fixture.Catalog));

				// Verify the reloaded operation keeps its task, queue, counters, statuses, and hardware.
				Check(
					restored.Op.paused && restored.Op.task.actionId == "audit" &&
					restored.Op.queue[0].actionId == "scan" && restored.Counter("expired-statuses") == 1 &&
					restored.Op.statuses.Count == 1 &&
					restored.Career.rigParts.Count(p => p.modelId == "expansion") == 2,
					"custom v2 save restores actions, queue, counters, statuses and extended hardware");

				// Finish the work and verify the authored objective completes.
				Finish(sim);
				Check(sim.ObjectiveComplete, "new action satisfies authored completion condition");

				// Disconnect and verify the contract delivers through the generic career flow.
				var report = sim.Disconnect();
				Check(report.success && sim.Career.completed.Contains("field-contract"),
					  "new objective and contract deliver through the generic career flow");
			}
		}

		/// <summary>Temporary catalog and definitions created for asset-only validation scenarios.</summary>
		public sealed class Fixture : IDisposable
		{
			/// <summary>Gets or sets the catalog backed by the fixture definitions.</summary>
			public GameCatalog Catalog;

			/// <summary>Gets or sets the root game definition assembled for the fixture.</summary>
			public GameDefinition Definition;

			/// <summary>Gets or sets ScriptableObject instances destroyed when the fixture is disposed.</summary>
			public List<GameplayDefinition> Owned = new();

			/// <summary>Disposes the catalog and destroys owned gameplay assets.</summary>
			public void Dispose()
			{
				// Release the compiled catalog first.
				Catalog?.Dispose();

				// Destroy every temporary asset the fixture created.
				foreach (var asset in Owned)
					Object.DestroyImmediate(asset);
			}
		}
	}
}