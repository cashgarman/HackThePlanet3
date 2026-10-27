using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static HackThePlanet.Editor.DefaultGameplayAssets;

namespace HackThePlanet.Editor
{
	/// <summary>Authored balance tweaks applied directly to generated gameplay ScriptableObjects.</summary>
	public static class BalanceRefinement
	{
		/// <summary>Updates tools, research, rewards, and hardware pricing then saves gameplay assets.</summary>
		public static void Apply()
		{
			// Load the root game definition that holds every gameplay asset.
			var root = Resources.Load<GameDefinition>("HackThePlanet/Gameplay/Game");

			ActionDefinition Tool(string id)
			{
				return root.actions.Single(a => a.id == id);
			}

			TechnologyDefinition Tech(string id)
			{
				return root.technologies.Single(t => t.id == id);
			}

			// Fetch the three core actions that are tuned below.
			var scan = Tool("scan");
			var hack = Tool("hack");
			var download = Tool("download");

			// Set the presentation order so the core actions sort first.
			scan.presentationPriority = 30;
			hack.presentationPriority = 20;
			download.presentationPriority = 0;

			// Give the bridge and backdoor actions a mid presentation priority.
			foreach (var id in new[] { "bridge", "backdoor" })
				Tool(id).presentationPriority = 10;

			// Give the disrupt and blackout actions a higher presentation priority.
			foreach (var id in new[] { "disrupt", "blackout" })
				Tool(id).presentationPriority = 15;

			// Make scanning faster and cheaper in trace, and hide it once a system is scanned.
			scan.duration = N(1.6f);
			scan.baseTrace = N(.6f);
			scan.visible = Not(State(NodeState.Scanned));

			// Scale hack duration with target security and the compute stat.
			hack.duration = B(Arithmetic.Divide,
							  B(Arithmetic.Add, N(2.2f), B(Arithmetic.Multiply, I(NumberInput.Security), N(1.15f))),
							  S("compute"));

			// Hide hack once the system is already controlled.
			hack.visible = Not(State(NodeState.Controlled));

			// Scale download duration with node storage, using objective or bonus defaults otherwise.
			download.duration = B(Arithmetic.Divide,
								  Choose(Compare(I(NumberInput.NodeStorage), Comparison.Greater, N(0)),
										 I(NumberInput.NodeStorage), Choose(State(NodeState.Objective), N(5.5f), N(4))),
								  S("bandwidth"));

			// Show download only on uncollected objective or bonus nodes that are not security hubs.
			download.visible = All(Any(State(NodeState.Objective), State(NodeState.Bonus)),
								   Not(State(NodeState.Collected)), Not(Tag("securityhub")));

			// Build a modifier that shortens hack time once the target has been scanned.
			var preparationTime = Mod("duration", Arithmetic.Multiply, N(.85f),
									  All(Action(hack), State(NodeState.Scanned)));

			// Give the preparation time modifier a stable id.
			preparationTime.id = "scan-preparation-time";

			// Build a modifier that lowers hack trace once the target has been scanned.
			var preparationTrace = Mod("baseTrace", Arithmetic.Multiply, N(.8f),
									   All(Action(hack), State(NodeState.Scanned)));

			// Give the preparation trace modifier a stable id.
			preparationTrace.id = "scan-preparation-trace";

			// Replace any older scan-preparation modifiers with the two new ones.
			root.modifiers = root.modifiers
								 .Where(m => m.id != "scan-preparation-time" && m.id != "scan-preparation-trace")
								 .Concat(new[] { preparationTime, preparationTrace }).ToArray();

			// Describe the scan bonus in the scan tooltip.
			scan.description =
				"Reveal nearby routes and prepare this target: scanned systems take 15% less hack time and 20% less hack trace.";

			// Update the hack tooltip.
			hack.description = "Take control to extend your access. Scan first for a cleaner, faster intrusion.";

			// Update the download tooltip.
			download.description =
				"Secure the selected objective or optional archive, then extract to bank its rewards.";

			// Tune disrupt and blackout, which share the same timing and trace cost.
			foreach (var id in new[] { "disrupt", "blackout" })
			{
				// Set the duration and base trace for this action.
				var action = Tool(id);
				action.duration = N(1.2f);
				action.baseTrace = N(1);

				// Extend each status effect to 45 seconds.
				foreach (var status in action.effects.OfType<ApplyStatusEffect>())
					status.duration = N(45);

				// Show disrupt on scanners and blackout on security hubs, once their defense is active.
				action.visible = All(Tag(id == "disrupt" ? "scanner" : "securityhub"), State(NodeState.DefenseActive));
			}

			// Set the requirements and failure messages for using blackout.
			Tool("blackout").requirements = new[]
			{
				// Require a discovered system, an active defense, and a nearby controlled route.
				Req(State(NodeState.Known), "Scan to discover this hub."),
				Req(State(NodeState.DefenseActive), "This defense is already offline."),
				Req(Compare(I(NumberInput.ControlledDistance), Comparison.LessOrEqual, N(3)),
					"Requires a discovered route within three hops of controlled access.")
			};

			// Set up the bridge action with its two charges and short duration.
			var bridge = Tool("bridge");
			bridge.duration = N(1.2f);
			bridge.useLimit = N(2);

			// Show bridge only on uncontrolled firewalls once the research is equipped.
			bridge.visible = All(Tag("firewall"), Not(State(NodeState.Controlled)),
								 new ResearchCondition { technology = Tech("bridge"), equipped = true });

			// Set the requirements and failure messages for using bridge.
			bridge.requirements = new[]
			{
				// Require a discovered, reachable, scanned firewall with a controlled neighbor.
				Req(State(NodeState.Known), "Discover this firewall first."),
				Req(State(NodeState.Reachable), "Control a connected neighbor first."),
				Req(Not(State(NodeState.Controlled)), "You already control this system."),
				Req(State(NodeState.Scanned), "Scan this firewall to prepare a bridge.")
			};

			// Set up the backdoor action with its shorter duration.
			var backdoor = Tool("backdoor");
			backdoor.duration = N(.8f);

			// Show backdoor only on uncontrolled workstations or relays once the research is equipped.
			backdoor.visible = All(Any(Tag("workstation"), Tag("relay")), Not(State(NodeState.Controlled)),
								   new ResearchCondition { technology = Tech("backdoor"), equipped = true });

			// Set the requirements and failure messages for using backdoor.
			backdoor.requirements = new[]
			{
				// Require a discovered system within three hops of controlled access.
				Req(State(NodeState.Known), "Discover this system first."),
				Req(Not(State(NodeState.Controlled)), "You already control this system."),
				Req(Compare(I(NumberInput.ControlledDistance), Comparison.LessOrEqual, N(3)),
					"Requires a discovered route within three hops of controlled access.")
			};

			// Tune the decoy effects: trace reduction and the protection duration.
			foreach (var effect in Tool("decoy").effects)
			{
				// Reduce trace by 12 when decoy is used.
				if (effect is TraceEffect trace)
					trace.amount = N(-12);

				// Protect against trace growth for 10 seconds.
				if (effect is ApplyStatusEffect protection)
					protection.duration = N(10);
			}

			// List every technology id in the same order as the cost table below.
			string[] ids =
			{
				"wide", "efficient", "quiet", "disrupt", "blueprint", "signal", "chain", "selective", "ghost",
				"backdoor", "blackout", "decoy", "passive", "weakpoint", "cascade", "bridge", "buffer"
			};

			// Set the research cost of each technology, matched by index.
			int[] costs = { 45, 45, 45, 45, 90, 60, 85, 85, 70, 100, 85, 95, 120, 130, 140, 140, 135 };
			for (var i = 0; i < ids.Length; i++)
				Tech(ids[i]).cost = costs[i];

			// Tune the efficient and quiet hack modifiers.
			Tech("efficient").modifiers[0].value = N(.8f);
			Tech("quiet").modifiers[0].value = N(.8f);

			// Build the signal modifier that lowers trace once a target is prepared by a scan.
			var signalPreparation = Mod("baseTrace", Arithmetic.Multiply, N(.85f),
										All(Action(hack), State(NodeState.Scanned)));

			// Give the signal preparation modifier a stable id.
			signalPreparation.id = "signal-prepared-trace";

			// Replace the signal modifiers with the updated preparation modifier.
			Tech("signal").modifiers = Tech("signal").modifiers.Where(m => m.id != "signal-prepared-trace")
													 .Concat(new[] { signalPreparation }).ToArray();

			// Tune the selective, weakpoint, and buffer modifiers.
			Tech("selective").modifiers[0].value = N(.7f);
			Tech("weakpoint").modifiers[0].value = N(.75f);
			Tech("buffer").modifiers[0].value = N(.9f);

			// Make bridge depend on both wide scan and efficient hack.
			Tech("bridge").parents = new[] { Tech("wide"), Tech("efficient") };

			// Rewrite the research descriptions so they match the new numbers.
			Tech("signal").description =
				"Scans take half the usual time and produce no base trace. Prepared hacks generate another {modifier:2:reduction}% less trace. Active sensors can still detect the scan.";

			Tech("bridge").description =
				"Two charges per operation. Scan a reachable firewall, then cross it in 1.2 seconds with zero trace. Requires Wide scan and Efficient hack.";

			Tech("backdoor").description =
				"Once per operation, reclaim a discovered workstation or relay within three mapped hops of controlled access. Takes 0.8 seconds and produces no trace.";

			Tech("disrupt").description =
				"Suppress a reachable scanner for 45 seconds. Takes 1.2 seconds with 1 base trace. Reusable while equipped.";

			Tech("blackout").description =
				"Remotely suppress a discovered hub within three mapped hops for 45 seconds. Its protected defenses immediately lose their bonus.";

			Tech("decoy").description =
				"Once per operation, remove 12 trace and prevent trace growth for 10 seconds. Best used before committing to a noisy tool.";

			// Set the reward amounts for archives, quiet completions, and the archive XP cap.
			root.rewards.archiveXpCap = 45;
			root.rewards.archiveCredits = 100;
			root.rewards.quietCredits = 100;
			root.rewards.quietXp = 20;

			// Set the quiet-trace limit for each story contract in order.
			float[] quiet = { 60, 60, 65, 60, 65, 70 };
			var story = 0;

			// Apply the quiet limit to story contracts and update the condition text.
			foreach (var job in root.contracts)
			{
				// Recurring contracts keep their own limit.
				if (!job.recurring)
					job.quietLimit = quiet[story++];

				// Describe the quiet bonus and archive rewards in the contract condition.
				job.condition = "Below " + job.quietLimit +
								"% trace: +100 cr / +20 XP. Each optional archive: +100 cr / +15 XP; all three pay XP.";
			}

			// Price each hardware tier and gate the memory expansion behind an ability.
			foreach (var hardware in root.hardware)
			{
				// Use the memory-specific prices, or the standard prices for other hardware.
				var prices = hardware.id == "memory" ? new[] { 450, 1000 } : new[] { 350, 800, 1450 };

				// Apply each tier price by index.
				for (var i = 0; i < hardware.tiers.Length; i++)
					hardware.tiers[i].price = prices[i];

				// Require at least one unlocked ability before memory can be expanded.
				if (hardware.id == "memory")
					hardware.requirements = new[]
					{
						Req(Compare(I(NumberInput.UnlockedAbilities), Comparison.Greater, N(0)),
							"Research an ability before expanding its loadout.")
					};
			}

			// Allow task cancellation on the career, then validate the definitions.
			root.career.allowTaskCancellation = true;
			GameDefinitionValidator.ThrowIfInvalid(root);

			// Mark every definition dirty and save the assets.
			foreach (var definition in GameDefinitionValidator.Definitions(root))
				EditorUtility.SetDirty(definition);
			AssetDatabase.SaveAssets();

			// Clear the cached default catalog so the new values load.
			GameCatalog.InvalidateDefault();

			// Write the refinement pass result to the artifact log.
			File.WriteAllText("Artifacts/balance-refinement.txt",
							  "PASS / Faster tools, scan preparation, affordable research, useful bounded abilities, full archive XP, and guarded hardware progression authored in gameplay assets.");
		}
	}
}