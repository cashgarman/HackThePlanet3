using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace HackThePlanet
{
	public sealed partial class WorkstationApp
	{
		private readonly List<PlaytestBatch> labBatches = new();
		private readonly GraphCameraState labCamera = new();
		private PlaytestConfig labConfig = new();
		private PlaytestBatch labBatch;
		private PlaytestRunner labBatchRunner, labReplayRunner;
		private PlaytestResult labReplayExpected;
		private Label labProgress, labLiveStatus;
		private string labSelectedNode, labExportMessage, labReplayMessage;
		private int labSelectedResult, labReplayRevision = -1;
		private float labReplayAccumulator, labReplaySpeed = 1;
		private bool labReplayPaused = true;
		private PlaytestDropdown labOpenDropdown;

		private PlaytestDropdown LabDropdown(string label, List<string> choices, int index) =>
			new(label, choices, index, field =>
			{
				labOpenDropdown?.Close();
				labOpenDropdown = field;
			}, () => labOpenDropdown = null);

		private void TickPlaytestLab()
		{
			if (labBatchRunner != null)
			{
				var start = Time.realtimeSinceStartup;
				for (var i = 0; i < 200 && labBatchRunner != null &&
					Time.realtimeSinceStartup - start < .008f; i++)
				{
					labBatchRunner.Step();
					if (!labBatchRunner.Finished) continue;
					labBatch.results.Add(labBatchRunner.Result);
					labBatchRunner = null;
					if (labBatch.results.Count < labBatch.config.seedCount)
						labBatchRunner = new PlaytestRunner(State.Catalog, labBatch.config,
							labBatch.config.startSeed + labBatch.results.Count);
					else
					{
						PlaytestReports.Assess(labBatch, labBatches);
						foreach (var prior in labBatches) PlaytestReports.Assess(prior, labBatches);
						dirty = true;
					}
				}
				if (labProgress != null)
					labProgress.text = labBatchRunner == null ? "BATCH COMPLETE" :
						"RUNNING / " + labBatch.results.Count + " OF " + labBatch.config.seedCount + " COMPLETE";
			}
			if (labReplayRunner == null) return;
			if (!labReplayPaused && !labReplayRunner.Finished)
			{
				labReplayAccumulator += Time.unscaledDeltaTime * labReplaySpeed * 2;
				var steps = Mathf.Min(100, Mathf.FloorToInt(labReplayAccumulator));
				labReplayAccumulator -= steps;
				for (var i = 0; i < steps && !labReplayRunner.Finished; i++) labReplayRunner.Step();
			}
			if (labReplayRunner.Finished && labReplayMessage == null)
			{
				labReplayMessage = labReplayRunner.Result.signature == labReplayExpected?.signature
					? "REPLAY VERIFIED / outcome and decisions match"
					: "REPLAY MISMATCH / content or policy may have changed";
				labReplayPaused = true;
				dirty = true;
			}
			if (labReplayRunner.Simulation.Active && labReplayRunner.Simulation.Revision != labReplayRevision)
			{
				labReplayRevision = labReplayRunner.Simulation.Revision;
				if (CurrentView == "playtest") dirty = true;
			}
			if (labLiveStatus != null)
			{
				var sim = labReplayRunner.Simulation;
				labLiveStatus.text = sim.Active
					? sim.Job.title + " / " + sim.Op.elapsed.ToString("0.0") + "s / " +
						sim.Op.trace.ToString("0.0") + "% TRACE / " + sim.ObjectiveCount + "/" +
						sim.ObjectiveTotal + " OBJECTIVES"
					: labReplayMessage ?? "REPLAY COMPLETE";
			}
		}

		private void StartPlaytestBatch()
		{
			labConfig.seedCount = Mathf.Clamp(labConfig.seedCount, 1, 100);
			labConfig.startSeed = Mathf.Clamp(labConfig.startSeed, 0, int.MaxValue - 100);
			labBatch = new PlaytestBatch { config = labConfig.Copy(),
				catalogFingerprint = PlaytestReports.Fingerprint(State.Catalog), unityVersion = Application.unityVersion };
			labBatches.Add(labBatch);
			labBatchRunner = new PlaytestRunner(State.Catalog, labBatch.config, labBatch.config.startSeed);
			labReplayRunner = null;
			labReplayMessage = labExportMessage = null;
			labSelectedResult = 0;
			dirty = true;
		}

		private void StartPlaytestReplay(PlaytestResult expected)
		{
			if (labBatch == null || labBatchRunner != null) return;
			labReplayExpected = expected;
			labReplayRunner = new PlaytestRunner(State.Catalog, labBatch.config, expected.seed);
			labReplayPaused = true;
			labReplaySpeed = 1;
			labReplayAccumulator = 0;
			labReplayRevision = -1;
			labReplayMessage = null;
			labSelectedNode = null;
			labCamera.Reset();
			dirty = true;
		}

		private void BuildPlaytestLab(VisualElement workspace)
		{
			var setup = Scroll(workspace, "lab-setup");
			Text(setup, "PLAYTEST LAB", "eyebrow cyan");
			Text(setup, "Scenario", "body-copy");
			Text(setup, "Isolated simulations using the current gameplay catalog.", "small muted");
			var mode = LabDropdown("Mode", new List<string> { "Single contract", "Fresh career" },
				labConfig.career ? 1 : 0);
			mode.name = "lab-mode";
			mode.RegisterValueChangedCallback(e => { labConfig.career = e.newValue == "Fresh career"; dirty = true; });
			setup.Add(mode);
			if (!labConfig.career)
			{
				var contracts = State.Catalog.Data.contracts.Select(c => c.title + " / " + c.id).ToList();
				var definitions = State.Catalog.Data.contracts;
				var selected = Array.FindIndex(definitions, c => c.id == labConfig.contractId);
				var contract = LabDropdown("Contract", contracts, Mathf.Max(0, selected));
			contract.name = "lab-contract";
				contract.RegisterValueChangedCallback(e =>
				{
					var index = contracts.IndexOf(e.newValue);
					if (index >= 0) labConfig.contractId = definitions[index].id;
				});
				setup.Add(contract);
				var difficulty = LabDropdown("Difficulty", new List<string> { "Easy", "Medium", "Hard" },
					Mathf.Clamp(labConfig.difficulty - 1, 0, 2));
			difficulty.name = "lab-difficulty";
				difficulty.RegisterValueChangedCallback(e => labConfig.difficulty =
					e.newValue == "Easy" ? 1 : e.newValue == "Medium" ? 2 : 3);
				setup.Add(difficulty);
				var build = LabDropdown("Build", PlaytestRunner.Builds.ToList(),
					Mathf.Max(0, Array.IndexOf(PlaytestRunner.Builds, labConfig.build)));
			build.name = "lab-build";
				build.RegisterValueChangedCallback(e => { labConfig.build = e.newValue; dirty = true; });
				setup.Add(build);
				if (labConfig.build == "Custom")
				{
				AddLabText(setup, "Research IDs, ordered", labConfig.customTech, "lab-tech",
						value => labConfig.customTech = value);
				AddLabText(setup, "Equipped special IDs", labConfig.customEquipped, "lab-equipped",
						value => labConfig.customEquipped = value);
				AddLabText(setup, "Module IDs", labConfig.customModules, "lab-modules",
						value => labConfig.customModules = value);
				Text(setup, "Separate IDs with commas. Research prerequisites must appear first.", "small muted");
				}
			}
			var strategy = LabDropdown("Strategy", PlaytestRunner.Strategies.ToList(),
				Mathf.Max(0, Array.IndexOf(PlaytestRunner.Strategies, labConfig.strategy)));
			strategy.name = "lab-strategy";
			strategy.RegisterValueChangedCallback(e => { labConfig.strategy = e.newValue; dirty = true; });
			setup.Add(strategy);
			if (labConfig.career)
				Text(setup, "BUILD PLAN / " + CareerUpgradePlanner.Name(labConfig.strategy) +
					" / upgrades purchased with earned resources", "small muted");
			AddLabNumber(setup, "Starting seed", labConfig.startSeed, "lab-seed", v => labConfig.startSeed = Mathf.Max(0, v));
			AddLabNumber(setup, "Seed count (1–100)", labConfig.seedCount, "lab-count",
				v => labConfig.seedCount = Mathf.Clamp(v, 1, 100));
			Btn(setup, "RUN SEED SET", StartPlaytestBatch, "lab-run", "primary-button")
				.SetEnabled(labBatchRunner == null);
			if (labBatchRunner != null)
				Btn(setup, "STOP BATCH", () =>
				{
					labBatchRunner = null;
					PlaytestReports.Assess(labBatch, labBatches);
					labBatch.findings.Add("Batch stopped; results contain completed runs only.");
					dirty = true;
				}, "lab-stop", "secondary-button");
			labProgress = Text(setup, labBatchRunner == null ? "READY" :
				"RUNNING / " + labBatch.results.Count + " OF " + labBatch.config.seedCount + " COMPLETE", "small cyan");
			if (labBatch != null && labBatch.results.Count > 0)
			{
				Btn(setup, "EXPORT JSON + CSV", () =>
				{
					try { labExportMessage = "EXPORTED / " + PlaytestReports.Export(labBatch); }
					catch (Exception ex) { labExportMessage = "EXPORT FAILED / " + ex.Message; }
					dirty = true;
				}, "lab-export", "secondary-button").SetEnabled(labBatchRunner == null);
				if (labExportMessage != null) Text(setup, labExportMessage, "small muted");
			}
			Text(setup, "No player save or content assets are changed.", "small muted");

			var center = El(workspace, "lab-center");
			var head = El(center, "lab-heading");
			Text(head, "SPECTATOR / RESULTS", "eyebrow cyan");
			labLiveStatus = Text(head, labReplayRunner == null ? "Select a completed run to watch." :
				labReplayRunner.Simulation.Active ? labReplayRunner.Simulation.Job.title :
				labReplayMessage ?? "REPLAY COMPLETE", "small lime");
			if (labReplayRunner?.Simulation.Active == true)
			{
				var sim = labReplayRunner.Simulation;
				canvas = new GraphViewport(sim.Op.nodes, sim.Op.links, labSelectedNode, false,
					id => { labSelectedNode = id; labCamera.FocusedId = id; dirty = true; }, null,
					labCamera, ContractIntel.Full, sim.Job.Intel, sim.Catalog);
				center.Add(canvas);
				Visuals.AttachField(canvas);
				Visuals.AttachIntelCard(canvas.IntelStatic);
				var controls = El(center, "lab-controls");
				Btn(controls, labReplayPaused ? "PLAY" : "PAUSE", () =>
				{
					labReplayPaused = !labReplayPaused; dirty = true;
				}, "lab-play-pause", "map-button");
				Btn(controls, "STEP 0.5s", () =>
				{
					labReplayPaused = true;
					labReplayRunner.Step(); dirty = true;
				}, "lab-step", "map-button");
				foreach (var rate in new[] { 1, 2, 4, 8 })
				{
					var chosen = rate;
					Btn(controls, rate + "×", () => { labReplaySpeed = chosen; dirty = true; },
						"lab-speed-" + rate, "map-button" + (labReplaySpeed == rate ? " active" : ""));
				}
				Btn(controls, "FIT", () => canvas.ResetView(), "lab-fit", "map-button");
				Text(center, "CURRENT DECISION / " + (labReplayRunner.CurrentReason ?? "Starting run") +
					(labReplayRunner.CurrentTarget == null ? "" : " / " + labReplayRunner.CurrentTarget), "small muted");
			}
			else
			{
				var summary = Scroll(center, "lab-summary");
				if (labBatch != null) BuildLabSummary(summary);
				else Text(summary, "Configure a scenario and run a seed set to compare outcomes.", "body-copy muted");
			}

			var results = Scroll(workspace, "lab-results");
			Text(results, "RUNS / INSIGHTS", "eyebrow cyan");
			if (labBatches.Count > 1)
			{
				var labels = labBatches.Select((b, i) => "SET " + (i + 1) + " / " +
					(b.config.career ? "CAREER / " + CareerUpgradePlanner.Name(b.config.strategy) :
						b.config.contractId + " / " + b.config.build)).ToList();
				var index = Mathf.Max(0, labBatches.IndexOf(labBatch));
				var picker = LabDropdown("History", labels, index);
			picker.name = "lab-history";
				picker.SetEnabled(labBatchRunner == null);
				picker.RegisterValueChangedCallback(e =>
				{
					labBatch = labBatches[labels.IndexOf(e.newValue)]; labSelectedResult = 0; dirty = true;
				});
				results.Add(picker);
			}
			if (labBatch != null)
			{
				foreach (var finding in labBatch.findings) Text(results, "⚑ " + finding, "small amber lab-finding");
				if (labBatch.results.Count == 0) Text(results, "Seed set is running.", "small muted");
				for (var i = 0; i < labBatch.results.Count; i++)
				{
					var index = i;
					var r = labBatch.results[i];
					var card = Btn(results, "SEED " + r.seed + " / " + (r.success ? "PASS" : "FAIL") +
						" / " + r.seconds.ToString("0.0") + "s / " + r.peakTrace.ToString("0.0") + "% TRACE",
						() => { labSelectedResult = index; StartPlaytestReplay(r); }, "lab-result-" + i,
						"lab-result" + (labSelectedResult == i ? " active" : ""));
					card.SetEnabled(labBatchRunner == null);
					if (!r.success) Text(results, r.failureExplanation ?? r.failure, "small amber");
					if (labBatch.config.career)
					{
						Text(results, r.attempts.Count(a => a.success) + " successful contracts / " +
							r.attempts.Count(a => !a.success) + " failed attempts", "small cyan");
						var details = new Foldout { text = "Career details", value = false, name = "lab-career-details-" + i };
						results.Add(details);
						BuildCareerDetails(details, r);
					}
				}
			}
			if (labReplayRunner != null)
			{
				Text(results, "LIVE RUN", "eyebrow section-space");
				var sim = labReplayRunner.Simulation;
				if (sim.Active)
				{
					Text(results, sim.Career.credits + " credits / " + sim.Career.xp + " XP / " + sim.Chassis.title,
						"small cyan");
					Text(results, "Installed: " + string.Join(", ", sim.Career.rigParts.Where(p => p.Installed)
						.Select(p => sim.Module(p.modelId).title)), "small muted");
					Text(results, sim.ObjectiveCount + "/" + sim.ObjectiveTotal + " objectives", "small lime");
					Text(results, "RAM " + sim.MemoryFree.ToString("0.0") + " GB / STORAGE " +
						sim.FreeStorage.ToString("0.0") + " GB", "small muted");
				}
				foreach (var e in labReplayRunner.Result.events.TakeLast(10).Reverse())
					Text(results, e.description ?? e.reason,
						"small muted lab-event");
			}
		}

		private void BuildLabSummary(VisualElement parent)
		{
			var runs = labBatch.results;
			Text(parent, runs.Count + " / " + labBatch.config.seedCount + " RUNS", "large-heading");
			if (runs.Count == 0) return;
			var won = runs.Where(r => r.success).OrderBy(r => r.seconds).ToArray();
			Text(parent, "SUCCESS RATE / " + won.Length + " OF " + runs.Count, "body-copy lime");
			if (won.Length > 0)
			{
				var p90 = won[Mathf.Clamp(Mathf.CeilToInt(won.Length * .9f) - 1, 0, won.Length - 1)];
				Text(parent, "MEDIAN / " + won[won.Length / 2].seconds.ToString("0.0") + "s", "body-copy");
				Text(parent, "P90 / " + p90.seconds.ToString("0.0") + "s", "body-copy");
				Text(parent, "MEAN PEAK TRACE / " + won.Average(r => r.peakTrace).ToString("0.0") + "%",
					"body-copy");
				Text(parent, "MEAN REWARDS / " + won.Average(r => r.credits).ToString("0") + " cr + " +
					won.Average(r => r.xp).ToString("0") + " XP", "body-copy");
			}
			Text(parent, "Select a run on the right to watch its decisions on the network.", "small muted");
			if (labReplayMessage != null) Text(parent, labReplayMessage, "small cyan");
		}

		private static string LabDuration(double seconds) => seconds < 60
			? seconds.ToString("0.00") + "s" : (int)(seconds / 60) + "m " + (seconds % 60).ToString("0.0") + "s";

		private void BuildCareerDetails(VisualElement parent, PlaytestResult r)
		{
			parent.AddToClassList("lab-career-details");
			var catalog = State.Catalog;
			string Tech(string id) => catalog.Technology(id)?.title ?? id;
			string Module(string id) => catalog.Data.modules.FirstOrDefault(m => m.id == id)?.title ?? id;
			string Chassis(string id) => catalog.Data.chassis.FirstOrDefault(c => c.id == id)?.title ?? id;
			void Section(string title) => Text(parent, title, "eyebrow lab-detail-heading");
			void Row(VisualElement container, string label, string value, string tip = null)
			{
				var row = El(container, "lab-detail-row");
				Text(row, label, "small lab-detail-label");
				Text(row, value, "small lab-detail-value");
				if (tip != null) row.tooltip = tip;
			}
			void Items(string heading, IEnumerable<string> ids, Func<string, string> title)
			{
				Section(heading);
				var groups = ids.GroupBy(id => id).ToArray();
				if (groups.Length == 0) Text(parent, "None", "small muted lab-detail-empty");
				foreach (var group in groups) Row(parent, title(group.Key), "×" + group.Count());
			}

			Section("CAREER SUMMARY");
			Row(parent, "Play style", r.strategy);
			Row(parent, "Build", r.build);
			Row(parent, "Story contracts completed", r.storyStages.ToString());
			Row(parent, "Freelance contracts completed", r.attempts.Count(a => a.freelance && a.success).ToString());
			Row(parent, "Failed attempts", r.attempts.Count(a => !a.success).ToString());
			Row(parent, "Total attempts", r.attempts.Count.ToString());
			Section("CREDITS");
			Row(parent, "Earned", r.credits.ToString("N0"));
			Row(parent, "Spent", r.creditsSpent.ToString("N0"));
			Row(parent, "Remaining", r.finalCredits.ToString("N0"));
			Section("RESEARCH XP");
			Row(parent, "Earned", r.xp.ToString("N0"));
			Row(parent, "Spent", r.xpSpent.ToString("N0"));
			Row(parent, "Remaining", r.finalXp.ToString("N0"));
			Section("TIME");
			Row(parent, "Simulated game time", LabDuration(r.seconds));
			Row(parent, "Wall-clock elapsed", LabDuration(r.wallSeconds),
				"Actual elapsed time for this automated run, including pauses. Replay timing is separate.");
			Items("RESEARCH PURCHASED", r.researchPurchased, Tech);
			Items("FINAL EQUIPPED ABILITIES", r.equippedResearch, Tech);
			Items("CHASSIS PURCHASED", r.chassisPurchased, Chassis);
			Row(parent, "Final chassis", Chassis(r.finalChassis));
			Items("MODULES PURCHASED", r.modulesPurchased, Module);
			Items("FINAL INSTALLED MODULES · INCLUDES STARTER PARTS", r.installedModules, Module);
			Section("CONTRACT HISTORY");
			for (var i = 0; i < r.attempts.Count; i++)
			{
				var attempt = r.attempts[i];
				var entry = El(parent, "lab-contract-detail");
				Text(entry, (i + 1) + ". " + attempt.title, "small cyan lab-contract-detail-title");
				Row(entry, "Type", attempt.freelance ? "Freelance" : "Story");
				Row(entry, "Outcome", attempt.success ? "Completed" : "Failed");
				Row(entry, "Game time", LabDuration(attempt.seconds));
				Row(entry, "Final trace", attempt.trace.ToString("0.0") + "%");
				Row(entry, "Credits earned", attempt.credits.ToString("N0"));
				Row(entry, "XP earned", attempt.xp.ToString("N0"));
				if (!attempt.success)
				{
					Text(entry, "Reason", "small muted lab-contract-detail-title");
					Text(entry, attempt.failure, "small amber lab-detail-reason");
				}
			}
		}
		private static void AddLabText(VisualElement parent, string label, string value, string name,
			Action<string> save)
		{
			var field = new TextField(label) { name = name, value = value, isDelayed = true };
			field.RegisterValueChangedCallback(e => save(e.newValue));
			parent.Add(field);
		}

		private static void AddLabNumber(VisualElement parent, string label, int value, string name,
			Action<int> save)
		{
			var field = new IntegerField(label) { name = name, value = value, isDelayed = true };
			field.RegisterValueChangedCallback(e => save(e.newValue));
			parent.Add(field);
		}
	}
}
