using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace HackThePlanet
{
	[Serializable]
	public sealed class PlaytestConfig
	{
		public bool career;
		public string contractId = "ghost-meter", strategy = "Direct", build = "Stock";
		public int difficulty = 1, startSeed, seedCount = 5;
		public string customTech = "", customEquipped = "", customModules = "";
		public PlaytestConfig Copy() => JsonUtility.FromJson<PlaytestConfig>(JsonUtility.ToJson(this));
	}

	[Serializable]
	public sealed class PlaytestEvent
	{
		public string contractId, action, target, reason, description;
		public float seconds, trace;
	}

	[Serializable]
	public sealed class PlaytestAttempt
	{
		public string contractId, title, failure;
		public bool success, freelance;
		public float seconds, trace;
		public int credits, xp;
	}

	[Serializable]
	public sealed class PlaytestResult
	{
		public int seed, actions, credits, xp, bonusFiles, storyStages, freelanceJobs, creditsSpent, xpSpent;
		public string contractId, strategy, build, failure, failureExplanation, signature;
		public bool success;
		public float seconds, peakTrace, finalTrace, minRam, minStorage;
		public List<PlaytestEvent> events = new();
		public List<PlaytestAttempt> attempts = new();
		public List<string> researchPurchased = new(), modulesPurchased = new(), chassisPurchased = new(),
			installedModules = new(), equippedResearch = new();
		public string finalChassis;
		public int finalCredits, finalXp;
		public double wallSeconds;
	}

	[Serializable]
	public sealed class PlaytestBatch
	{
		public PlaytestConfig config;
		public string catalogFingerprint, unityVersion;
		public int policyVersion = 3;
		public List<PlaytestResult> results = new();
		public List<string> findings = new();
	}

	/// <summary>Only information an operator could see while connected.</summary>
	public sealed class PlaytestObservation
	{
		public sealed class Node
		{
			public string id, label, goalAction;
			public bool known, controlled, scanned, objective, objectiveDone, bonus, collected;
			public int security;
			public NodeKind kind;
		}

		public sealed class Option
		{
			public string action, target;
			public float duration, trace;
			public bool allowed, active;
		}

		public readonly List<Node> nodes = new();
		public readonly List<NetLink> links = new();
		public readonly List<Option> options = new();
		public bool objectiveComplete;
		public float trace, traceLimit;
		public ContractIntel intel;

		public static PlaytestObservation Capture(GameSimulation sim)
		{
			var view = new PlaytestObservation
			{
				objectiveComplete = sim.ObjectiveComplete,
				trace = sim.Op.trace,
				traceLimit = sim.TraceLimit,
				intel = sim.Job.Intel
			};
			var definition = sim.Catalog.Contract(sim.Job.id);
			var intel = sim.Job.Intel;
			foreach (var n in sim.Op.nodes.Where(n => n.known || intel != ContractIntel.None))
			{
				var detailed = n.known || intel == ContractIntel.Full;
				view.nodes.Add(new Node
				{
					id = n.id, known = n.known, label = detailed ? n.label : null,
					controlled = n.known && n.controlled, scanned = n.known && n.scanned,
					objective = detailed && n.objective,
					objectiveDone = detailed && n.objective && sim.ObjectiveDone(n),
					goalAction = detailed ? definition.objectives.FirstOrDefault(o => o.nodeId == n.id)?.action?.id : null,
					bonus = detailed && n.bonus, collected = detailed && n.collected,
					security = detailed ? n.security : 0, kind = detailed ? n.kind : NodeKind.Gateway
				});
				if (!n.known) continue;
				foreach (var action in sim.AvailableActions(n.id))
				{
					var preview = sim.Preview(action.id, n.id);
					view.options.Add(new Option { action = action.id, target = n.id,
							duration = preview.duration, trace = preview.trace, allowed = preview.allowed,
							active = sim.IsAbilityActive(action.id, n.id) });
				}
			}
			var visible = new HashSet<string>(view.nodes.Select(n => n.id));
			view.links.AddRange(sim.Op.links.Where(l => visible.Contains(l.a) && visible.Contains(l.b))
				.Select(l => new NetLink(l.a, l.b)));
			foreach (var action in sim.AvailableActions())
			{
				var preview = sim.Preview(action.id);
				view.options.Add(new Option { action = action.id, duration = preview.duration,
					trace = preview.trace, allowed = preview.allowed,
					active = sim.IsAbilityActive(action.id) });
			}
			return view;
		}
	}

	/// <summary>Deterministic policy that receives only a filtered observation.</summary>
	public static class PlaytestPolicy
	{
		public readonly struct Choice
		{
			public readonly string action, target, reason;
			public Choice(string action, string target, string reason)
			{ this.action = action; this.target = target; this.reason = reason; }
		}

		public static Choice Decide(PlaytestObservation view, string strategy)
		{
			if (view.objectiveComplete) return new Choice("disconnect", null, "Required objectives secured");
			var legal = view.options.Where(o => o.allowed && !o.active).ToList();
			Choice Pick(PlaytestObservation.Option option, string reason) =>
				new(option.action, option.target, reason);
			var goals = view.nodes.Where(n => n.objective && !n.objectiveDone).ToList();
			var cautious = strategy == "Cautious";
			var bonus = strategy == "Opportunist";
			var nearLimit = view.trace > view.traceLimit * (cautious ? .65f : .8f);

			if (cautious || nearLimit)
			{
				var recovery = legal.Where(o => o.action == "recover" || o.action == "decoy")
					.OrderBy(o => o.trace).ThenBy(o => o.action, StringComparer.Ordinal).FirstOrDefault();
				if (nearLimit && recovery != null) return Pick(recovery, "Reduce trace before the next action");
			}
			var objectiveAction = legal.Where(o => goals.Any(g => g.id == o.target && g.goalAction == o.action))
				.OrderBy(o => cautious ? o.trace : o.duration).ThenBy(o => o.target, StringComparer.Ordinal).FirstOrDefault();
			if (objectiveAction != null) return Pick(objectiveAction, "Complete a discovered objective");
			if (bonus && view.trace < view.traceLimit * .55f)
			{
				var archive = legal.Where(o => o.action == "download" &&
					view.nodes.Any(n => n.id == o.target && n.bonus && !n.collected))
					.OrderBy(o => o.trace).ThenBy(o => o.target, StringComparer.Ordinal).FirstOrDefault();
				if (archive != null) return Pick(archive, "Collect a low-risk optional archive");
			}
			var counter = legal.Where(o => o.action == "extract-key" || o.action == "spoof-gate" ||
				o.action == "hard-reset" || o.action == "loop-feed")
				.OrderBy(o => o.action == "loop-feed" ? 1 : 0).ThenBy(o => o.trace)
				.ThenBy(o => o.target, StringComparer.Ordinal).FirstOrDefault();
			if (counter != null && (goals.Count > 0 || counter.action == "loop-feed"))
				return Pick(counter, "Disable or bypass a visible defense");

			int Degree(string id) => view.links.Count(l => l.a == id || l.b == id);
			int UnknownNeighbors(string id) => view.links.Count(l => l.a == id &&
				view.nodes.Any(n => n.id == l.b && !n.known) || l.b == id &&
				view.nodes.Any(n => n.id == l.a && !n.known));
			var hack = legal.Where(o => o.action == "hack" &&
				view.nodes.Any(n => n.id == o.target && !n.controlled))
				.OrderBy(o => goals.Count == 0 ? 0 : Distance(view, o.target, goals.Select(g => g.id)) * 10)
				.ThenByDescending(o => goals.Count == 0 ? Degree(o.target) : 0)
				.ThenBy(o => cautious ? o.trace : o.duration)
				.ThenBy(o => view.nodes.First(n => n.id == o.target).security)
				.ThenBy(o => o.target, StringComparer.Ordinal).FirstOrDefault();
			if (hack != null && goals.Count > 0 && Distance(view, hack.target, goals.Select(g => g.id)) < 1000)
				return Pick(hack, "Advance toward a visible objective");

			// Scan each controlled system once. This discovers new nodes without looking at hidden links.
			var scan = legal.Where(o => o.action == "scan" &&
				view.nodes.Any(n => n.id == o.target && n.controlled && !n.scanned) &&
				(view.intel == ContractIntel.None || view.nodes.Any(n => !n.known &&
					Distance(view, o.target, new[] { n.id }) <= 3)))
				.OrderBy(o => goals.Count == 0 ? 0 : Distance(view, o.target, goals.Select(g => g.id)))
				.ThenByDescending(o => UnknownNeighbors(o.target))
				.ThenBy(o => o.trace).ThenBy(o => o.target, StringComparer.Ordinal).FirstOrDefault();
			if (scan != null) return Pick(scan, "Scan an uninspected controlled system");
			if (hack != null) return Pick(hack, "Explore the next reachable system");
			return new Choice(null, null, "No legal progress action remains");
		}

		private static int Distance(PlaytestObservation view, string from, IEnumerable<string> targets)
		{
			var goals = new HashSet<string>(targets);
			var seen = new HashSet<string> { from };
			var queue = new Queue<(string id, int distance)>();
			queue.Enqueue((from, 0));
			while (queue.Count > 0)
			{
				var (id, distance) = queue.Dequeue();
				if (goals.Contains(id)) return distance;
				foreach (var link in view.links)
				{
					var next = link.a == id ? link.b : link.b == id ? link.a : null;
					if (next != null && seen.Add(next)) queue.Enqueue((next, distance + 1));
				}
			}
			return 1000;
		}
	}

	/// <summary>Runs an isolated contract or earned-resource career in fixed simulation steps.</summary>
	public sealed class PlaytestRunner
	{
		public static readonly string[] Strategies = { "Direct", "Cautious", "Opportunist" };
		public static readonly string[] Builds = { "Stock", "Recon", "Intrusion", "Recon + Loop",
			"Intrusion + Reset", "Decoder Rig", "Spoof Rig", "Veil Rig", "Isolation Rig", "Custom" };
		private static readonly Dictionary<string, (string tech, string equip, string modules)> Profiles = new()
		{
			["Stock"] = ("", "", ""),
			["Recon"] = ("wide,credential-analysis", "credential-analysis", ""),
			["Intrusion"] = ("efficient,protocol-spoof", "protocol-spoof", ""),
			["Recon + Loop"] = ("wide,credential-analysis,quiet,loop-feed", "credential-analysis,loop-feed", ""),
			["Intrusion + Reset"] = ("efficient,protocol-spoof,disrupt,hard-reset", "protocol-spoof,hard-reset", ""),
			["Decoder Rig"] = ("wide,credential-analysis,quiet,loop-feed,data-engineering", "credential-analysis,loop-feed", "decoder-board"),
			["Spoof Rig"] = ("efficient,protocol-spoof,disrupt,hard-reset,parallel-engineering", "protocol-spoof,hard-reset", "spoof-asic"),
			["Veil Rig"] = ("wide,credential-analysis,quiet,loop-feed,signal-engineering", "credential-analysis,loop-feed", "veil-array"),
			["Isolation Rig"] = ("efficient,protocol-spoof,disrupt,hard-reset,infrastructure-2", "protocol-spoof,hard-reset", "isolation-bank")
		};
		private readonly GameCatalog catalog;
		private readonly PlaytestConfig config;
		private readonly ContractDefinition[] story;
		private int storyIndex, freelanceAtStage, retryAtStage, ticks, operationActions;
		private bool freelance, missingPurchase;
		private ContractDefinition current;
		private float lastElapsed, lastTrace;
		private int lastObjectives, lastObjectiveTotal;
		private string lastTask;
		private readonly System.Diagnostics.Stopwatch wallClock = System.Diagnostics.Stopwatch.StartNew();
		public GameSimulation Simulation { get; private set; }
		public PlaytestResult Result { get; }
		public bool Finished { get; private set; }
		public string CurrentReason { get; private set; }
		public string CurrentTarget { get; private set; }

		public PlaytestRunner(GameCatalog catalog, PlaytestConfig source, int seed)
		{
			this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
			config = source.Copy();
			story = catalog.Data.contracts.Where(c => !c.recurring && !c.optional).OrderBy(c => c.requiredWins).ToArray();
			Result = new PlaytestResult { seed = seed, contractId = config.career ? "career" : config.contractId,
				strategy = config.strategy, build = config.career ? CareerUpgradePlanner.Name(config.strategy) : config.build,
				minRam = float.MaxValue, minStorage = float.MaxValue };
			Simulation = new GameSimulation(catalog);
			if (!config.career)
			{
				var definition = catalog.Contract(config.contractId);
				if (definition == null) { Fail("Unknown contract"); return; }
				Simulation.Career.xp = 10000;
				Simulation.Career.credits = 10000;
				var predecessors = new HashSet<string>();
                void Include(ContractDefinition d) { foreach (var id in d.prerequisites) if (predecessors.Add(id)) Include(catalog.Contract(id)); }
                Include(definition);
                Simulation.Career.storyFlags.AddRange(definition.requiredFlags);
				Simulation.Career.completed.AddRange(predecessors);
				Simulation.Career.jobs = Math.Max(3, predecessors.Count);
				if (!InstallBuild()) return;
				current = definition;
				StartOperation(definition, false);
			}
			else StartCareerStage();
		}

		private static string[] Ids(string value) => (value ?? "").Split(',')
			.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToArray();

		private bool InstallBuild()
		{
			var startingCredits = Simulation.Career.credits;
			var startingXp = Simulation.Career.xp;
			var profile = config.build == "Custom"
				? (config.customTech, config.customEquipped, config.customModules)
				: Profiles.TryGetValue(config.build, out var preset) ? preset : Profiles["Stock"];
			bool Buy(string id) { var t = catalog.Technology(id); return t != null && (Simulation.Has(id) || (t.parents.All(p => Buy(p.id)) && Simulation.BuyTech(id))); }
            foreach (var id in Ids(profile.Item1))
                if (!Buy(id)) { Fail("Cannot buy research " + id); return false; }
			foreach (var id in Simulation.Career.equipped.ToArray()) Simulation.ToggleEquip(id);
			foreach (var id in Ids(profile.Item2))
				if (!Simulation.ToggleEquip(id)) { Fail("Cannot equip " + id); return false; }
			foreach (var id in Ids(profile.Item3))
			{
				RigPart part = Simulation.BuyModule(id);
				for (var i = 0; part == null && i < 3 && Simulation.UpgradeChassis(); i++) part = Simulation.BuyModule(id);
				var placed = part != null && Simulation.PlaceFirstFree(part.instanceId);
				for (var i = 0; !placed && part != null && i < 3 && Simulation.UpgradeChassis(); i++)
					placed = Simulation.PlaceFirstFree(part.instanceId);
				if (!placed)
				{ Fail("Cannot install module " + id); return false; }
			}
			Result.creditsSpent = startingCredits - Simulation.Career.credits;
			Result.xpSpent = startingXp - Simulation.Career.xp;
			return true;
		}

		private int OperationSeed(ContractDefinition definition, int occurrence)
		{
			unchecked
			{
				var hash = Result.seed;
				foreach (var ch in definition.id) hash = hash * 31 + ch;
				return (hash * 31 + occurrence) & int.MaxValue;
			}
		}

		private bool StartOperation(ContractDefinition definition, bool isFreelance)
		{
			Result.failureExplanation = null;
			freelance = isFreelance;
			current = definition;
			var job = catalog.DescribeContract(definition, Simulation.Career);
			job.seed = config.career ? OperationSeed(definition, Result.storyStages + Result.freelanceJobs + retryAtStage) : Result.seed;
			if (!config.career)
			{
				job.difficulty = Mathf.Clamp(config.difficulty, 1, 3);
				var context = catalog.Context(Simulation.Career);
				job.credits = Mathf.RoundToInt(definition.credits.Evaluate(context) * job.RewardMultiplier);
				job.xp = Mathf.RoundToInt(definition.xp.Evaluate(context) * job.RewardMultiplier);
			}
			if (!Simulation.Start(job)) { Fail("Cannot start " + definition.id); return false; }
			ticks = operationActions = 0;
			lastElapsed = lastTrace = 0;
			lastObjectives = 0;
			lastObjectiveTotal = Simulation.ObjectiveTotal;
			lastTask = null;
			Log("start", null, isFreelance ? "Freelance funding run" : "Start contract");
			return true;
		}

		private void PrepareCareer()
		{
			var credits = Simulation.Career.credits;
			var xp = Simulation.Career.xp;
			missingPurchase = CareerUpgradePlanner.Prepare(Simulation, config.strategy, Log);
			Result.creditsSpent += credits - Simulation.Career.credits;
			Result.xpSpent += xp - Simulation.Career.xp;
		}
		private ContractDefinition AvailableFreelance() => catalog.Data.contracts.Where(c => c.recurring &&
			Simulation.Career.completed.Count >= c.requiredWins)
			.OrderBy(c => catalog.DescribeContract(c, Simulation.Career).difficulty)
			.ThenBy(c => c.id, StringComparer.Ordinal).FirstOrDefault();

		private void StartCareerStage()
		{
			if (Simulation.StoryComplete) { Succeed(); return; }
            while (Simulation.PendingChoice != null) Simulation.ChooseStory(Simulation.PendingChoice.choices.First().id);
            var next = story.FirstOrDefault(Simulation.ContractAvailable);
            if (next == null) { Fail("No available story continuation"); return; }
			PrepareCareer();
			if (missingPurchase && freelanceAtStage < 2 && AvailableFreelance() is { } fund)
			{
				freelanceAtStage++;
				StartOperation(fund, true);
				return;
			}
			if (missingPurchase) Log("skip-purchase", null, AvailableFreelance() == null
				? "Upgrades need more earnings. No freelance jobs are unlocked yet, so the AI will earn rewards from the next story contract."
				: "Upgrades need more earnings. The two freelance jobs allowed for this stage have been used, so the AI will try the story contract with its current build.");
			StartOperation(next, false);
		}

		public void Step()
		{
			try { Advance(); }
			catch (Exception ex) { Fail("Simulation error: " + ex.Message); }
		}

		private void Advance()
		{
			if (Finished || !Simulation.Active) return;
			if (++ticks > 10000 || operationActions > 500) { EndOperation("Action or tick guard reached"); return; }
			if (Simulation.Op.task != null)
			{
				lastObjectives = Simulation.ObjectiveCount;
				lastObjectiveTotal = Simulation.ObjectiveTotal;
				lastTask = Simulation.ActionTitle(Simulation.Op.task.actionId, Simulation.Op.task.target) +
					(Simulation.Op.task.target == null ? "" : " at " + Simulation.Node(Simulation.Op.task.target)?.label);
				lastElapsed = Simulation.Op.elapsed;
				lastTrace = Simulation.Op.trace;
				Simulation.Tick(.5f);
				Measure();
				if (!Simulation.Active) EndOperation("Caught by trace");
				return;
			}
			var evidence = current.evidence;
            if (!Simulation.EvidenceSolved && evidence.clueNodes.All(id => Simulation.Node(id)?.collected == true))
                Simulation.SolveEvidence(evidence.answers.First(a => a.correct).id);
            var observation = PlaytestObservation.Capture(Simulation);
			var choice = PlaytestPolicy.Decide(observation, config.strategy);
			CurrentReason = choice.reason;
			CurrentTarget = choice.target;
			if (choice.action == "disconnect") { EndOperation(null); return; }
			if (choice.action == null)
			{
				if (Simulation.Op.memoryReclaiming > 0)
				{
					lastElapsed = Simulation.Op.elapsed;
					Simulation.Tick(.5f);
					if (!Simulation.Active) EndOperation("Caught while waiting for RAM");
					return;
				}
				EndOperation(choice.reason); return;
			}
			if (!Simulation.Execute(choice.action, choice.target))
			{ EndOperation("Rejected action " + choice.action); return; }
			Result.actions++;
			operationActions++;
			Log(choice.action, choice.target, choice.reason);
			Measure();
		}

		private void Measure()
		{
			if (!Simulation.Active) return;
			Result.peakTrace = Mathf.Max(Result.peakTrace, Simulation.Op.trace);
			Result.minRam = Mathf.Min(Result.minRam, Simulation.MemoryFree);
			Result.minStorage = Mathf.Min(Result.minStorage, Simulation.FreeStorage);
		}

		private void Log(string action, string target, string reason)
		{
			Result.events.Add(new PlaytestEvent { contractId = current?.id, action = action,
				target = target, reason = reason, seconds = Simulation.Active ? Simulation.Op.elapsed : Result.seconds,
				trace = Simulation.Active ? Simulation.Op.trace : 0,
				description = DescribeEvent(action, target, reason) });
		}

		private string DisplayName(string id) => string.IsNullOrEmpty(id) ? "" :
			catalog.Technology(id)?.title ?? catalog.Data.modules.FirstOrDefault(m => m.id == id)?.title ??
			catalog.Data.chassis.FirstOrDefault(c => c.id == id)?.title ?? catalog.Contract(id)?.title ??
			Simulation.Node(id)?.label ?? id;

		private string DescribeEvent(string action, string target, string reason)
		{
			var name = DisplayName(target);
			switch (action)
			{
				case "buy-tech": return "Researched " + name + ". " + reason;
				case "buy-module": return "Bought and installed " + name + ". " + reason;
				case "buy-chassis": return "Expanded the rig to " + name + ". " + reason;
				case "equip": return "Equipped " + name + ". " + reason;
				case "unequip": return "Unequipped " + name + ". " + reason;
				case "install-module": return "Installed owned " + name + ".";
				case "save-xp": return "Saving XP for " + name + ". " + reason;
				case "save-credits": return "Saving credits for " + name + ". " + reason;
				case "skip-module": return "Could not install " + name + ". " + reason;
				case "skip-tech": return "Could not research " + name + ". " + reason;
				case "skip-purchase": return reason;
				case "start": return "Started " + current?.title + ". " + reason;
				case "delivered": return "Completed " + current?.title + " and collected the payout.";
				case "failed": return Result.failureExplanation ?? "Attempt ended on " + current?.title + ": " + reason + ".";
				default: return (catalog.Action(action)?.title ?? action) +
					(string.IsNullOrEmpty(name) ? "" : " at " + name) + ". " + reason;
			}
		}

		private void EndOperation(string failure)
		{
			var wasActive = Simulation.Active;
			var elapsed = wasActive ? Simulation.Op.elapsed : lastElapsed + .5f;
			var finalTrace = wasActive ? Simulation.Op.trace : Simulation.LastReport?.trace ?? lastTrace;
			var won = failure == null && wasActive && Simulation.ObjectiveComplete;
			var objectives = wasActive ? Simulation.ObjectiveCount : lastObjectives;
			var total = wasActive ? Simulation.ObjectiveTotal : lastObjectiveTotal;
			if (!won)
			{
				var cause = failure == "Caught by trace" || failure == "Caught while waiting for RAM"
					? "The network detected the AI because trace reached the limit" +
						(failure == "Caught while waiting for RAM" ? " while it waited for RAM to recover" :
						 string.IsNullOrEmpty(lastTask) ? "" : " while it was using " + lastTask)
					: failure == "No legal progress action remains" ? "The AI could not find an allowed action that would make progress"
					: failure == "Action or tick guard reached" ? "The playtest stopped because the AI took too many steps without finishing"
					: failure?.StartsWith("Rejected action ") == true ? "The game rejected the AI's next action"
					: "The AI ended the attempt before delivering the contract";
				Result.failureExplanation = current.title + ": " + cause + ". It secured " + objectives +
					" of " + total + " objectives in this attempt (" + elapsed.ToString("0.0") + " seconds).";
			}
			var report = wasActive ? Simulation.Disconnect() : Simulation.LastReport;
			Result.seconds += elapsed;
			Result.finalTrace = finalTrace;
			Result.peakTrace = Mathf.Max(Result.peakTrace, finalTrace);
			Result.credits += report?.credits ?? 0;
			Result.xp += report?.xp ?? 0;
			Result.bonusFiles += report?.bonusFiles ?? 0;
			Result.attempts.Add(new PlaytestAttempt { contractId = current.id, title = current.title,
				success = won, freelance = freelance, seconds = elapsed, trace = finalTrace,
				credits = report?.credits ?? 0, xp = report?.xp ?? 0,
				failure = won ? null : Result.failureExplanation });
			Log(won ? "delivered" : "failed", null, won ? "Contract delivered" : failure ?? "Objectives incomplete");
			if (!config.career)
			{
				if (won) Succeed(); else Fail(failure ?? "Objectives incomplete");
				return;
			}
			if (freelance)
			{
				Result.freelanceJobs++;
				if (!won) { Fail("Freelance failed: " + (failure ?? "objectives incomplete")); return; }
				StartCareerStage();
				return;
			}
			if (won)
			{
				Result.storyStages++;
				storyIndex++;
				freelanceAtStage = retryAtStage = 0;
				StartCareerStage();
				return;
			}
			if (retryAtStage == 0 && freelanceAtStage < 2 && AvailableFreelance() is { } retryJob)
			{
				retryAtStage++;
				freelanceAtStage++;
				StartOperation(retryJob, true);
				return;
			}
			Result.failureExplanation += retryAtStage > 0 ? " The career stopped after its one story retry."
				: AvailableFreelance() == null ? " The career stopped because no freelance job was available to fund another attempt."
				: " The career stopped because its limit of two freelance jobs for this stage had been reached.";
			Fail("Story failed at " + current.id + ": " + (failure ?? "objectives incomplete"));
		}

		private void Fail(string reason)
		{
			Result.failure = reason;
			if (string.IsNullOrEmpty(Result.failureExplanation))
			{
				if (reason.StartsWith("Cannot buy research "))
					Result.failureExplanation = "The test build could not research " + DisplayName(reason.Substring(20)) +
						". Check its research order, prerequisites and available XP.";
				else if (reason.StartsWith("Cannot equip "))
					Result.failureExplanation = "The test build could not equip " + DisplayName(reason.Substring(13)) +
						". It must be owned, equippable, and fit within the available ability slots.";
				else if (reason.StartsWith("Cannot install module "))
					Result.failureExplanation = "The test build could not buy and fit " + DisplayName(reason.Substring(22)) +
						". Check its prerequisites, credits and rig space.";
				else if (reason.StartsWith("Simulation error:"))
					Result.failureExplanation = "An unexpected simulation error stopped the test. The technical details are included in the exported report.";
				else Result.failureExplanation = "The run could not continue: " + reason + ".";
			}
			Result.success = false;
			Finished = true;
			FinalizeResult();
		}

		private void Succeed()
		{
			Result.success = true;
			Result.failureExplanation = null;
			Finished = true;
			FinalizeResult();
		}

		private void FinalizeResult()
		{
			wallClock.Stop();
			Result.wallSeconds = wallClock.Elapsed.TotalSeconds;
			Result.finalCredits = Simulation.Career.credits;
			Result.finalXp = Simulation.Career.xp;
			Result.finalChassis = Simulation.Career.chassisId;
			Result.researchPurchased = Result.events.Where(e => e.action == "buy-tech").Select(e => e.target).ToList();
			Result.modulesPurchased = Result.events.Where(e => e.action == "buy-module").Select(e => e.target).ToList();
			Result.chassisPurchased = Result.events.Where(e => e.action == "buy-chassis").Select(e => e.target).ToList();
			Result.installedModules = Simulation.Career.rigParts.Where(p => p.Installed).Select(p => p.modelId).ToList();
			Result.equippedResearch = Simulation.Career.equipped.ToList();
			if (Result.minRam == float.MaxValue) Result.minRam = 0;
			if (Result.minStorage == float.MaxValue) Result.minStorage = 0;
			Result.signature = string.Join("|", Result.events.Select(e => e.contractId + ":" + e.action + ":" + e.target)) +
				"|" + Result.success + "|" + Result.seconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
				"|" + Result.finalTrace.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
				"|" + Result.credits + "|" + Result.xp + "|" + Result.failure;
		}
	}

	/// <summary>Session report calculations and explicit local export.</summary>
	public static class PlaytestReports
	{
		public static string Fingerprint(GameCatalog catalog)
		{
			var json = string.Join("\n", GameDefinitionValidator.Definitions(catalog.Data)
				.OrderBy(d => d.GetType().Name + "/" + d.id).Select(ContentJson.Encode));
			using var hash = System.Security.Cryptography.SHA256.Create();
			return BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(json))).Replace("-", "");
		}

		public static void Assess(PlaytestBatch batch, IEnumerable<PlaytestBatch> history)
		{
			batch.findings.Clear();
			var results = batch.results;
			if (results.Count == 0) return;
			var fails = results.Count(r => !r.success);
			if (fails > 0) batch.findings.Add(fails + "/" + results.Count + " runs failed; inspect failure reasons.");
			var successful = results.Where(r => r.success).OrderBy(r => r.seconds).ToArray();
			if (successful.Length > 0)
			{
				var median = successful[successful.Length / 2].seconds;
				var lower = batch.config.difficulty == 1 ? 60 : batch.config.difficulty == 2 ? 120 : 240;
				var upper = batch.config.difficulty == 1 ? 120 : batch.config.difficulty == 2 ? 240 : 300;
				if (!batch.config.career && (median < lower || median > upper))
					batch.findings.Add("Median " + median.ToString("0") + "s is outside the " + lower + "–" + upper + "s target.");
				if (successful.Count(r => r.peakTrace > 75) * 2 >= successful.Length)
					batch.findings.Add("At least half of successful runs exceeded 75 trace.");
			}
			if (batch.config.career) return;
			foreach (var other in history.Where(h => h != batch && !h.config.career &&
				h.config.contractId == batch.config.contractId && h.config.difficulty == batch.config.difficulty &&
				h.config.startSeed == batch.config.startSeed && h.config.seedCount == batch.config.seedCount &&
				h.config.strategy == batch.config.strategy &&
				h.catalogFingerprint == batch.catalogFingerprint && h.config.build != batch.config.build))
			{
				var pairs = results.Join(other.results, a => a.seed, b => b.seed, (a, b) => (a, b)).ToArray();
				if (pairs.Length == 0 || pairs.Any(p => !p.a.success || !p.b.success)) continue;
				if (pairs.Length == batch.config.seedCount && pairs.All(p =>
					p.b.creditsSpent <= p.a.creditsSpent && p.b.xpSpent <= p.a.xpSpent &&
					p.b.seconds <= p.a.seconds && p.b.peakTrace <= p.a.peakTrace &&
					p.b.minRam >= p.a.minRam) && pairs.Any(p => p.b.seconds < p.a.seconds ||
					p.b.peakTrace < p.a.peakTrace || p.b.minRam > p.a.minRam))
					batch.findings.Add("Candidate dominance: " + other.config.build + " outperformed " +
						batch.config.build + " on every paired seed (time, trace, RAM).");
			}
		}

		public static string Export(PlaytestBatch batch)
		{
			var folder = Path.Combine(Application.persistentDataPath, "Playtests");
			Directory.CreateDirectory(folder);
			var stem = "playtest-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
			File.WriteAllText(Path.Combine(folder, stem + ".json"), JsonUtility.ToJson(batch, true));
			var csv = new List<string> { "seed,success,contract,strategy,build,seconds,peakTrace,finalTrace,actions,credits,xp,bonusFiles,storyStages,freelanceJobs,creditsSpent,xpSpent,failure,failureExplanation,wallSeconds,successfulAttempts,totalAttempts,finalCredits,finalXp,finalChassis,researchPurchased,chassisPurchased,modulesPurchased,installedModules" };
			foreach (var r in batch.results)
			{
				string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
				csv.Add(string.Join(",", r.seed, r.success, Q(r.contractId), Q(r.strategy), Q(r.build),
					r.seconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
					r.peakTrace.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
					r.finalTrace.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
					r.actions, r.credits, r.xp, r.bonusFiles, r.storyStages, r.freelanceJobs,
					r.creditsSpent, r.xpSpent, Q(r.failure), Q(r.failureExplanation),
					r.wallSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
					r.attempts.Count(a => a.success), r.attempts.Count, r.finalCredits, r.finalXp, Q(r.finalChassis),
					Q(string.Join(";", r.researchPurchased)), Q(string.Join(";", r.chassisPurchased)),
					Q(string.Join(";", r.modulesPurchased)), Q(string.Join(";", r.installedModules))));
			}
			File.WriteAllLines(Path.Combine(folder, stem + ".csv"), csv);
			return folder;
		}
	}
}
