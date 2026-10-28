using System;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace HackThePlanet.Editor
{
	/// <summary>Executable checks for the isolated designer playtest runner.</summary>
	public static class PlaytestValidation
	{
		[MenuItem("Hack The Planet/Balance/Validate Playtest Lab")]
		public static void Run()
		{
			var catalog = GameCatalog.Default;
			var catalogBefore = PlaytestReports.Fingerprint(catalog);
			var saveBefore = File.Exists(CareerStorage.SavePath) ? File.ReadAllText(CareerStorage.SavePath) : null;
			ValidateArchetypes(catalog);
			var config = new PlaytestConfig { contractId = "quiet-copy", difficulty = 1,
				strategy = "Direct", build = "Stock", seedCount = 1 };
			var first = new PlaytestRunner(catalog, config, 0);
			if (!first.Simulation.Active) throw new InvalidOperationException("Stock trial did not start.");
			var visible = PlaytestObservation.Capture(first.Simulation);
			if (visible.nodes.Count != first.Simulation.Op.nodes.Count ||
				visible.nodes.Any(n => first.Simulation.Node(n.id).objective && !n.objective))
				throw new InvalidOperationException("Easy observation omitted public pre-contract intel.");
			var hiddenConfig = config.Copy();
			hiddenConfig.difficulty = 3;
			var hiddenRun = new PlaytestRunner(catalog, hiddenConfig, 0);
			var hidden = PlaytestObservation.Capture(hiddenRun.Simulation);
			if (hidden.nodes.Any(n => !hiddenRun.Simulation.Node(n.id).known))
				throw new InvalidOperationException("Hard observation exposed undiscovered nodes.");
			Finish(first);
			Debug.Log("Playtest stock / " + first.Result.success + " / " + first.Result.failure +
				" / " + first.Result.seconds.ToString("0.0") + "s / " + first.Result.actions + " actions");
			if (!first.Result.success) Debug.Log(string.Join("\n", first.Result.events.Select(e =>
				e.action + " / " + e.target + " / " + e.trace.ToString("0.0") + " / " + e.reason)));
			if (!first.Result.success) throw new InvalidOperationException("Starter trial failed: " + first.Result.failure);
			var replay = new PlaytestRunner(catalog, config, 0);
			Finish(replay);
			if (first.Result.signature != replay.Result.signature)
				throw new InvalidOperationException("Replay diverged from the batch result.");
			var invalid = new PlaytestRunner(catalog, new PlaytestConfig
				{ contractId = "quiet-copy", build = "Custom", customTech = "missing-tech" }, 0);
			if (!invalid.Finished || invalid.Result.success || !invalid.Result.failure.Contains("missing-tech"))
				throw new InvalidOperationException("Invalid custom build was not reported.");
			var career = new PlaytestRunner(catalog, new PlaytestConfig
				{ career = true, strategy = "Cautious" }, 0);
			Finish(career);
			Debug.Log("Playtest career / " + career.Result.success + " / " + career.Result.failure +
				" / story " + career.Result.storyStages + " / freelance " + career.Result.freelanceJobs);
			if (!career.Result.success) Debug.Log(string.Join("\n", career.Result.events.TakeLast(60).Select(e =>
				e.contractId + " / " + e.action + " / " + e.target + " / " +
				e.trace.ToString("0.0") + " / " + e.reason)));
			if (career.Result.freelanceJobs > 2 * catalog.StoryCount)
				throw new InvalidOperationException("Career exceeded the freelance cap.");
			foreach (var build in PlaytestRunner.Builds.Where(b => b != "Custom"))
			{
				var preset = new PlaytestRunner(catalog, new PlaytestConfig { build = build }, 1);
				if (preset.Finished) throw new InvalidOperationException("Preset setup failed: " + build + " / " + preset.Result.failure);
			}
			if (saveBefore != (File.Exists(CareerStorage.SavePath) ? File.ReadAllText(CareerStorage.SavePath) : null))
				throw new InvalidOperationException("Playtest changed the player save.");
			if (PlaytestReports.Fingerprint(catalog) != catalogBefore)
				throw new InvalidOperationException("Playtest mutated catalog content.");
			Debug.Log("Playtest Lab validation passed.");
		}

		private static int uiPhase, uiFrames;
		private static string uiCareer;
		private static bool uiCareerMode;
		private static RenderTexture uiTexture;
		public static void StartUi(bool career = false)
		{
			uiCareerMode = career;
			uiPhase = uiFrames = 0;
			EditorApplication.update -= PollUi;
			EditorApplication.update += PollUi;
			EditorApplication.isPlaying = true;
		}

		private static void PollUi()
		{
			if (!EditorApplication.isPlaying || WorkstationApp.Instance == null) return;
			try
			{
				if (++uiFrames > 3000) throw new InvalidOperationException("UI validation timed out.");
				var app = WorkstationApp.Instance;
				if (uiPhase == 0)
				{
					app.UseTestCatalog(GameCatalog.Default);
					app.State.Start(app.State.Catalog.Contracts(app.State.Career).First());
					uiTexture = new RenderTexture(1680, 960, 24);
					uiTexture.Create();
					app.GetComponent<UIDocument>().panelSettings.targetTexture = uiTexture;
					if (!app.ActivateVisibleControl("tab-playtest")) throw new Exception("Lab tab missing");
					CheckDropdownStability(app);
					if (uiCareerMode)
					{
						typeof(PlaytestDropdown).GetMethod("Select", System.Reflection.BindingFlags.Instance |
							System.Reflection.BindingFlags.NonPublic).Invoke(app.Root.Q<PlaytestDropdown>("lab-mode"),
							new object[] { "Fresh career" });
						app.SendMessage("Update");
					}
					uiCareer = JsonUtility.ToJson(app.State.Career);
					app.SetTextSize(100);
					app.ActivateVisibleControl("lab-run");
					uiPhase++;
				}
				else if (uiPhase == 1 && app.Root.Q<Button>("lab-result-0")?.enabledInHierarchy == true)
				{
					if (uiCareer != JsonUtility.ToJson(app.State.Career)) throw new Exception("Batch changed player career");
					if (!app.ActivateVisibleControl("lab-export")) throw new Exception("Export control missing");
					app.ActivateVisibleControl("lab-result-0");
					uiPhase++;
				}
				else if (uiPhase == 2 && app.Root.Q<Button>("lab-step") != null)
				{
					app.ActivateVisibleControl("lab-step");
					app.SetTextSize(200);
					app.SendMessage("Update");
					if (uiCareerMode) app.Root.Q<Foldout>("lab-career-details-0").value = true;
					uiPhase++;
					uiFrames = 0;
				}
				else if (uiPhase == 3 && uiFrames > 15)
				{
					CaptureUi("Artifacts/playtest-lab-200.png");
					app.SetTextSize(300);
					uiPhase++;
					uiFrames = 0;
				}
				else if (uiPhase == 4 && uiFrames > 15)
				{
					CaptureUi("Artifacts/playtest-lab-300.png");
					var field = app.Root.Q<PlaytestDropdown>("lab-mode");
					typeof(PlaytestDropdown).GetMethod("Open", System.Reflection.BindingFlags.Instance |
						System.Reflection.BindingFlags.NonPublic).Invoke(field, null);
					uiPhase++;
					uiFrames = 0;
				}
				else if (uiPhase == 5 && uiFrames > 15)
				{
					CaptureUi("Artifacts/playtest-dropdown.png");
					app.Root.Q<PlaytestDropdown>("lab-mode").Close();
					if (uiCareer != JsonUtility.ToJson(app.State.Career)) throw new Exception("Replay changed player career");
					app.SetTextSize(200);
					File.WriteAllText("Temp/HackThePlanet.status", "PASS / playtest-ui");
					EditorApplication.update -= PollUi;
					app.GetComponent<UIDocument>().panelSettings.targetTexture = null;
					uiTexture.Release();
					UnityEngine.Object.DestroyImmediate(uiTexture);
				}
			}
			catch (Exception ex)
			{
				EditorApplication.update -= PollUi;
				File.WriteAllText("Temp/HackThePlanet.status", "FAIL / playtest-ui / " + ex);
				Debug.LogException(ex);
			}
		}

		private static void CaptureUi(string path)
		{
			var previous = RenderTexture.active;
			RenderTexture.active = uiTexture;
			var image = new Texture2D(uiTexture.width, uiTexture.height, TextureFormat.RGBA32, false);
			image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0);
			image.Apply();
			Directory.CreateDirectory("Artifacts");
			File.WriteAllBytes(path, image.EncodeToPNG());
			UnityEngine.Object.DestroyImmediate(image);
			RenderTexture.active = previous;
		}

		private static void CheckDropdownStability(WorkstationApp app)
		{
			app.SendMessage("Update");
			var field = app.Root.Q<PlaytestDropdown>("lab-mode");
			var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
			typeof(PlaytestDropdown).GetMethod("Open", flags).Invoke(field, null);
			var overlay = app.Root.panel.visualTree.Q("lab-choice-overlay");
			if (overlay == null) throw new Exception("Lab dropdown did not open");
			using (var navigation = NavigationSubmitEvent.GetPooled())
			{
				navigation.target = overlay;
				overlay.SendEvent(navigation);
			}
			typeof(WorkstationApp).GetField("dirty", flags).SetValue(app, true);
			app.SendMessage("Update");
			if (app.Root.Q<PlaytestDropdown>("lab-mode") != field || overlay.panel == null)
				throw new Exception("A background rebuild dismissed the open dropdown");
			field.Close();
			app.SendMessage("Update");
			if (app.Root.Q<PlaytestDropdown>("lab-mode") == field)
				throw new Exception("Deferred rebuild did not resume after dropdown dismissal");
		}

		private static void Finish(PlaytestRunner runner)
		{
			for (var i = 0; i < 120000 && !runner.Finished; i++) runner.Step();
			if (!runner.Finished) throw new InvalidOperationException("Playtest runner did not terminate.");
		}

		private static void ValidateArchetypes(GameCatalog catalog)
		{
			foreach (var style in PlaytestRunner.Strategies)
			{
				for (var seed = 0; seed < 2; seed++)
				{
					var run = new PlaytestRunner(catalog, new PlaytestConfig { career = true, strategy = style }, seed);
					Finish(run);
					var replay = new PlaytestRunner(catalog, new PlaytestConfig { career = true, strategy = style }, seed);
					Finish(replay);
					if (run.Result.attempts.Count(a => a.success && !a.freelance) != run.Result.storyStages ||
						run.Result.attempts.Sum(a => a.credits) != run.Result.credits ||
						run.Result.attempts.Sum(a => a.xp) != run.Result.xp || run.Result.wallSeconds < 0 ||
						run.Result.finalCredits != catalog.Data.career.credits + run.Result.credits - run.Result.creditsSpent ||
						run.Result.finalXp != run.Result.xp - run.Result.xpSpent ||
						!run.Result.installedModules.SequenceEqual(run.Simulation.Career.rigParts.Where(p => p.Installed).Select(p => p.modelId)))
						throw new Exception("Career summary does not match simulation state: " + style);
					if (run.Result.signature != replay.Result.signature || run.Result.freelanceJobs > 2 * catalog.StoryCount ||
						run.Result.failure?.StartsWith("Simulation error") == true || !run.Simulation.RigValid ||
						(!run.Result.success && string.IsNullOrWhiteSpace(run.Result.failureExplanation)) ||
						run.Simulation.Career.xp < 0 || run.Simulation.Career.credits < 0)
						throw new Exception("Career archetype invariant failed: " + style + " / " + run.Result.failure);
				}
				var sim = new GameSimulation(catalog);
				var events = new System.Collections.Generic.List<string>();
				void Record(string action, string target, string reason) => events.Add(action + ":" + target);
				CareerUpgradePlanner.Prepare(sim, style, Record);
				if (!events.Any(e => e.StartsWith("save-xp:"))) throw new Exception("Missing affordability event");
				if (sim.Chassis.tier != 1 || sim.Career.credits != catalog.Data.career.credits)
					throw new Exception("Opening career bypassed its chassis unlock or wasted starting credits");
				// A real early-career budget can buy the expansion before it can buy the next module.
				sim.Career.jobs = 1;
				sim.Career.credits = 1250;
				CareerUpgradePlanner.Prepare(sim, style, Record);
				if (sim.Chassis.tier != 2 || sim.Career.credits != 50)
					throw new Exception("AI did not use the affordable unlocked chassis upgrade: " + style);
				CareerUpgradePlanner.Prepare(sim, style, Record);
				if (sim.Career.credits != 50) throw new Exception("AI repeated an expansion while saving for its module");
				sim.Career.credits += 300;
				CareerUpgradePlanner.Prepare(sim, style, Record);
				var early = style == "Direct" ? "processor" : style == "Cautious" ? "ram" : "uplink";
				if (sim.Career.rigParts.Count(p => p.modelId == early && p.Installed) != 2)
					throw new Exception("AI did not install its early style upgrade after earning enough: " + style);
				// Fund the isolated fixture to exercise late upgrades even when an exploration policy loses an early job.
				sim.Career.xp = 10000;
				sim.Career.credits = 100000;
				sim.Career.jobs = 6;
				CareerUpgradePlanner.Prepare(sim, style, Record);
				var module = style == "Direct" ? "spoof-asic" : style == "Cautious" ? "veil-array" : "decoder-board";
				if (!sim.Career.rigParts.Any(p => p.modelId == module && p.Installed) || !sim.RigValid ||
					sim.Career.equipped.Count > sim.Slots)
					throw new Exception("Archetype rig or loadout failed: " + style + " / " + string.Join(",", events));
				var credits = sim.Career.credits;
				var xp = sim.Career.xp;
				CareerUpgradePlanner.Prepare(sim, style, Record);
				if (credits != sim.Career.credits || xp != sim.Career.xp)
					throw new Exception("Archetype repeated a purchase: " + style);
				Debug.Log("Career archetype verified / " + style + " / " + string.Join(",", events));
			}
		}
	}
}
