using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using CompressionLevel = System.IO.Compression.CompressionLevel;
using Object = UnityEngine.Object;

namespace HackThePlanet.Editor
{
	/// <summary>Editor automation for builds, validation commands, and workstation smoke tests.</summary>
	[InitializeOnLoad]
	[NoAutoStaticsCleanup]
	public static class GameBuildTools
	{
		/// <summary>Gets the primary workstation scene included in player builds.</summary>
		public const string ScenePath = "Assets/HackThePlanet/Scenes/Workstation.unity";

		// Next editor time when the Temp command file should be polled again.
		private static double nextPoll;

		// Current workstation smoke phase index and frames waited within that phase.
		private static int smokePhase, smokeFrames;

		// Signal effects mode restored after an automated smoke run finishes.
		private static SignalEffects previousEffects;

		// Saved shader frames used to detect animation freeze during smoke tests.
		private static Color32[] frozenField, frozenGlass;

		// GPU textures reused across UI rebuilds during effects smoke checks.
		private static RenderTexture sharedField, sharedGlass;

		// Operation timing and trace captured when Settings opens for freeze assertions.
		private static float settingsElapsed, settingsTaskElapsed, settingsTrace;

		// Career instance referenced before reset confirmation during Settings smoke.
		private static Career settingsCareer;

		// Text size percent restored after smoke tests change scaling.
		private static int previousTextSize;

		// Baseline font sizes per TextElement at 100% for scaling assertions.
		private static readonly Dictionary<TextElement, float> textSizeBaseline = new();

		// Asset-only catalog fixture torn down when workstation smoke completes.
		private static DataDrivenValidation.Fixture dataFixture;

		// Off-screen UI render target used to capture workstation smoke screenshots.
		private static RenderTexture smokeUiTexture;

		// Registers editor update and play-mode handlers for batch commands and smoke tests.
		static GameBuildTools()
		{
			// Run the poll loop on every editor update.
			EditorApplication.update += Poll;

			// React to play mode changes for the rig preview and smoke tests.
			EditorApplication.playModeStateChanged += state =>
			{
				// Open the rig page in a test career when a rig-play request enters play mode.
				if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("HTP.RigPreview", false))
				{
					// Clear the request flag, then set up the test career and the rig page.
					SessionState.SetBool("HTP.RigPreview", false);
					var app = WorkstationApp.Instance;
					app.UseTestCareer(new Career { credits = 100000, xp = 10000, jobs = 3 });
					app.State.DebugUnlockAllTech();
					app.State.UpgradeChassis();
					app.Navigate("rig");
					Status("PASS / rig-play");
				}

				// Start the smoke test from its first phase when entering play mode.
				if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("HTP.Smoke", false))
				{
					// Reset the smoke phase counters for a new run.
					smokePhase = 1;
					smokeFrames = 0;
				}

				// Finish the waypoint smoke test when returning to edit mode.
				if (state == PlayModeStateChange.EnteredEditMode && smokePhase == 144)
				{
					// Write the waypoint smoke result file.
					File.WriteAllText(Path.Combine(Project, "Artifacts/waypoint-smoke.txt"),
									  "PASS / Real UI placement, colour selection, custom label, shortest route selection, connection, and marker visibility over an undiscovered system. Discovered model count stays unchanged. Screenshots: 21-waypoint-planning.png, 22-waypoint-hidden-route.png.");

					// Release and destroy the waypoint UI render texture.
					if (smokeUiTexture != null)
					{
						smokeUiTexture.Release();
						Object.DestroyImmediate(smokeUiTexture);
						smokeUiTexture = null;
					}

					// Clear the smoke flags and reset the phase for the next run.
					SessionState.SetBool("HTP.Smoke", false);
					SessionState.SetBool("HTP.WaypointSmoke", false);
					smokePhase = 0;
					Status("PASS / waypoint-smoke");
				}

				// Finish the workstation smoke test when returning to edit mode.
				if (state == PlayModeStateChange.EnteredEditMode && smokePhase == 43)
				{
					// Write the workstation smoke result file.
					File.WriteAllText(Path.Combine(Project, "Artifacts/workstation-smoke.txt"),
									  "PASS / Real player UI callbacks: connect, scan, hack, recover, deliver, research purchase, hardware upgrade. Both shaders compile and render nonblank GPU textures; display modes and GPU texture reuse passed. Settings button and Escape, exact menu choices, menu bounds, operation freeze, previous pause state, blocked background controls and shortcuts, reset confirmation, Cancel and Escape cancellation, confirmed fresh career, usable first contract, and Quit passed. Rendered screenshots captured. Asset-only contract, node variant, action button, shortcut, objective, technology and hardware extension rendered and exercised. Tactical queue, clear and stop controls exercised. Easy full intel, Medium anonymous topology and labels, Hard renderer-free centered message, animated GPU static, reduced/off effects, and connection back to discovered systems passed. Test career and effect preference isolated from user saves.");

					// Append the text-size and career-reset results to the same file.
					File.AppendAllText(Path.Combine(Project, "Artifacts/workstation-smoke.txt"),
									   "\nPASS / Text size: 200% compatibility; real slider callbacks at 100% and 300%; every TextElement scales without compounding, including inline graph labels and numeric input; scrollable Settings stays in bounds at 300%, including a 1280 x 720 render; preference persistence and career-reset independence; original user preference restored.\n");

					// Release and destroy the workstation UI render texture.
					if (smokeUiTexture != null)
					{
						smokeUiTexture.Release();
						Object.DestroyImmediate(smokeUiTexture);
						smokeUiTexture = null;
					}

					// Dispose the data fixture and clear the workstation test state.
					dataFixture?.Dispose();
					dataFixture = null;
					settingsCareer = null;
					SessionState.SetBool("HTP.Smoke", false);
					smokePhase = 0;
					Status("PASS / smoke");

					// Quit the editor when running in batch mode.
					if (Application.isBatchMode)
						EditorApplication.Exit(0);
				}
			};
		}

		// Gets the absolute path to the Unity project root folder.
		private static string Project => Directory.GetParent(Application.dataPath).FullName;

		// Gets the Temp file path where external tools write one-shot editor commands.
		private static string CommandPath => Path.Combine(Project, "Temp/HackThePlanet.command");

		// Gets the Temp file path where command progress and results are written.
		private static string StatusPath => Path.Combine(Project, "Temp/HackThePlanet.status");

		// Polls for batch commands or advances the in-play workstation smoke test.
		private static void Poll()
		{
			// Drive the smoke test one step per frame while it is running.
			if (smokePhase > 0)
			{
				SmokeStep();
				return;
			}

			// Throttle polling to twice a second and skip while the editor is busy.
			if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling ||
				EditorApplication.isUpdating)
				return;

			// Schedule the next poll half a second from now.
			nextPoll = EditorApplication.timeSinceStartup + .5;

			// Stop when no command file has been written.
			if (!File.Exists(CommandPath))
				return;

			// Read the command and delete the file so it runs only once.
			var command = File.ReadAllText(CommandPath).Trim();
			File.Delete(CommandPath);

			// Run the requested command and record its result.
			try
			{
				// Mark the command as running.
				Status("RUNNING / " + command);

				// Dispatch the requested command to its handler.
				switch (command)
				{
					case "export-json":      ContentMigration.Export(); break;
					case "validate-json":    ContentMigration.Validate(); break;
					case "content-validate": ContentValidation.Run(); break;
					case "content-editor":   ContentEditorWindow.Open(); break;

					// Legacy asset import is disabled; content is authored through the Content Editor.
					case "import-gameplay":
						throw new InvalidOperationException(
							"Legacy asset import is disabled. Use Content Editor to author JSON.");

					case "refresh":          AssetDatabase.Refresh(); break;
					case "balance-before":   BalanceLab.CaptureBefore(); break;
					case "balance-after":    BalanceLab.CaptureAfter(); break;
					case "data-validate":    DataDrivenValidation.Run(); break;
					case "balance-validate": BalanceValidation.Run(); break;
					case "career-balance": BalanceSuite.Run(false); break;
					case "playtest-validate": PlaytestValidation.Run(); break;
					case "playtest-ui": PlaytestValidation.StartUi(); return;
					case "playtest-career-ui": PlaytestValidation.StartUi(true); return;
					case "build-playtest": BuildWindowsAt("Builds/Windows-Playtest", BuildOptions.Development); break;
					case "setup":            Setup(); break;

					// Save the rig pointer career and capture its preview screenshot.
					case "rig-capture":
						File.WriteAllText(Path.Combine(Project, "Artifacts/rig-pointer-career.json"),
										  JsonUtility.ToJson(WorkstationApp.Instance.State.Career, true));

						CaptureScreenshot(Path.Combine(Project, "Artifacts/rig-pointer-preview.png"));
						break;

					// Write every build warning and error to a text file.
					case "build-notes":
						File.WriteAllLines(Path.Combine(Project, "Artifacts/rig-build-warnings.txt"),
										   BuildReport.GetLatestReport().steps.SelectMany(step => step.messages)
													  .Where(message => message.type == LogType.Warning ||
																		message.type == LogType.Error)
													  .Select(message => message.content)); break;

					// Enter play mode with the rig preview flag set.
					case "rig-play":
						SessionState.SetBool("HTP.RigPreview", true);
						EditorApplication.isPlaying = true;
						return;

					case "rig-validate":     RigValidation.Run(); break;
					case "generate-assets":  GameplayAssetGenerator.Generate(); break;
					case "validate":         GameValidation.Run(); break;
					case "models":           NetworkVisualValidation.Run(); break;
					case "build":            BuildWindows(); break;
					case "build-rig":        BuildWindowsAt("Builds/Windows-Rig"); break;
					case "build-volumetric": BuildWindowsAt("Builds/Windows-Volumetric"); break;
					case "play":             EditorApplication.isPlaying = true; break;
					case "stop":             EditorApplication.isPlaying = false; break;

					// Start the workstation smoke test without the waypoint flag.
					case "smoke":
						SessionState.SetBool("HTP.WaypointSmoke", false);
						StartSmoke();
						return;

					// Start the waypoint smoke test.
					case "waypoint-smoke":
						SessionState.SetBool("HTP.WaypointSmoke", true);
						StartSmoke();
						return;

					case "capture": CaptureScreenshot(Path.Combine(Project, "Artifacts/live-capture.png")); break;

					// Reject any command that is not in the list above.
					default: throw new InvalidOperationException("Unknown local editor command: " + command);
				}

				// Mark the command as passed.
				Status("PASS / " + command);
			}
			catch (Exception ex)
			{
				// Log the exception and record the failure in the status file.
				Debug.LogException(ex);
				Status("FAIL / " + command + "\n" + ex);
			}
		}

		// Writes the latest batch or smoke status text for external tools to read.
		private static void Status(string text)
		{
			// Make sure the Temp folder that holds the status file exists.
			Directory.CreateDirectory(Path.GetDirectoryName(StatusPath));

			// Overwrite the status file with the latest text.
			File.WriteAllText(StatusPath, text);
		}

		/// <summary>Creates the workstation scene if missing and configures standalone player settings.</summary>
		[MenuItem("Hack The Planet/Set Up Game Scene")]
		public static void Setup()
		{
			// Make sure the scenes folder exists.
			Directory.CreateDirectory(Path.Combine(Project, "Assets/HackThePlanet/Scenes"));

			// Create and save an empty workstation scene if it is missing.
			if (!File.Exists(Path.Combine(Project, ScenePath)))
			{
				var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
				EditorSceneManager.SaveScene(scene, ScenePath);
				EditorSceneManager.CloseScene(scene, true);
			}

			// Include only the workstation scene in player builds.
			EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

			// Set the product and company names, and the default windowed size.
			PlayerSettings.productName = "Hack The Planet";
			PlayerSettings.companyName = "Independent Operator";
			PlayerSettings.defaultScreenWidth = 1680;
			PlayerSettings.defaultScreenHeight = 960;
			PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
			PlayerSettings.resizableWindow = true;
			PlayerSettings.runInBackground = true;

			// Use the Mono scripting backend for standalone builds.
			PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);

			// Save the asset changes.
			AssetDatabase.SaveAssets();
		}

		/// <summary>Builds the Windows player and packages Builds/Hack-The-Planet-Windows.zip.</summary>
		[MenuItem("Hack The Planet/Build Windows Player")]
		public static void BuildWindows()
		{
			// Build the player into Builds/Windows first.
			BuildWindowsAt("Builds/Windows");

			// Pick the final archive path and a temporary staging path next to it.
			string archive = Path.Combine(Project, "Builds/Hack-The-Planet-Windows.zip"), staged = archive + ".writing";

			// Remove any leftover staging archive from an earlier run.
			if (File.Exists(staged))
				File.Delete(staged);

			// Package the built player into the staging archive.
			ZipFile.CreateFromDirectory(Path.Combine(Project, "Builds/Windows"), staged, CompressionLevel.Optimal,
										false);

			// Replace the old archive with the new one, or move the staged file into place.
			if (File.Exists(archive))
				File.Replace(staged, archive, null);
			else
				File.Move(staged, archive);
		}

		// Builds the Windows player into the given output folder after validating content.
		private static void BuildWindowsAt(string output, BuildOptions options = BuildOptions.None)
		{
			// Refuse to build while play mode is active.
			if (EditorApplication.isPlaying)
				throw new InvalidOperationException("Stop play mode before building.");

			// Validate the content and scene before building.
			ContentMigration.Validate();
			Setup();

			// Make sure the output and Artifacts folders exist.
			Directory.CreateDirectory(Path.Combine(Project, output));
			Directory.CreateDirectory(Path.Combine(Project, "Artifacts"));

			// Build the Windows 64-bit player from the workstation scene.
			var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
			{
				scenes = new[] { ScenePath }, locationPathName = Path.Combine(Project, output, "Hack The Planet.exe"),
				target = BuildTarget.StandaloneWindows64, options = options
			});

			// Write the build summary to the report file.
			File.WriteAllText(Path.Combine(Project, "Artifacts/build-report.txt"),
							  result.summary.result + "\nErrors: " + result.summary.totalErrors + "\nWarnings: " +
							  result.summary.totalWarnings + "\nBytes: " + result.summary.totalSize + "\nTime: " +
							  result.summary.totalTime);

			// Fail the build if Unity did not report success.
			if (result.summary.result != BuildResult.Succeeded)
				throw new InvalidOperationException("Windows build failed: " + result.summary.result);
		}

		/// <summary>Menu entry that runs <see cref="GameValidation.Run" />.</summary>
		[MenuItem("Hack The Planet/Run Simulation Validation")]
		public static void Validate()
		{
			// Run the simulation validation suite.
			GameValidation.Run();
		}

		/// <summary>Starts or continues the automated workstation UI smoke test in play mode.</summary>
		[MenuItem("Hack The Planet/Run Workstation Smoke Test")]
		public static void StartSmoke()
		{
			// Make sure the Artifacts folder exists for the smoke outputs.
			Directory.CreateDirectory(Path.Combine(Project, "Artifacts"));

			// Flag the smoke test so it runs when play mode is entered.
			SessionState.SetBool("HTP.Smoke", true);

			// Start the smoke phases now if play mode is already running, otherwise enter play mode.
			if (EditorApplication.isPlaying)
			{
				// Begin at phase one with a fresh frame counter.
				smokePhase = 1;
				smokeFrames = 0;
			}
			else
			{
				// Entering play mode triggers the smoke test from the static constructor.
				EditorApplication.isPlaying = true;
			}
		}

		// Runs one frame of the phased workstation or waypoint UI smoke test.
		private static void SmokeStep()
		{
			// Run one smoke phase, guarding against any exception so the run can be reported.
			try
			{
				// Get the live workstation app and wait until it exists.
				var app = WorkstationApp.Instance;

				if (app == null)
					return;

				// Run the next phase only every 18 frames so the UI can settle.
				if (++smokeFrames < 18)
					return;

				// Reset the frame counter for the next phase.
				smokeFrames = 0;

				// Run the handler for the current smoke phase.
				switch (smokePhase)
				{
					case 1:
						// Create the UI render texture and start the test career.
						smokeUiTexture = new RenderTexture(1680, 960, 24, RenderTextureFormat.ARGB32);
						smokeUiTexture.Create();
						app.GetComponent<UIDocument>().panelSettings.targetTexture = smokeUiTexture;

						// Remember the current effects and text size so they can be restored later.
						previousEffects = app.Visuals.Mode;
						previousTextSize = app.TextSizePercent;

						// Set the UI to a fresh test career at 100% text with full effects.
						app.UseTestCareer(new Career());
						app.SetTextSize(100);
						app.Visuals.SetMode(SignalEffects.Full);
						smokePhase = 400;
						return;

					case 400:
						// Verify the debug UI fixture starts an operation and opens Settings.
						Check(app.State.Start(GameContent.Story[0]), "Debug UI fixture starts an operation.");
						Check(app.ActivateVisibleControl("settings"), "Debug fixture opens Settings.");
						smokePhase++;
						return;

					case 401:
						// Verify Settings opens the Cheats / Debug menu.
						Check(app.ActivateVisibleControl("debug-menu"), "Settings opens Cheats / Debug.");
						smokePhase++;
						return;

					case 402:
						// Verify the debug menu pauses the operation and blocks background controls.
						Check(app.State.Op.paused && !app.ActivateVisibleControl("pause"),
							  "Debug menu pauses the operation and blocks background controls.");

						// Verify all three debug buttons run their callbacks.
						Check(
							app.ActivateVisibleControl("debug-credits") && app.ActivateVisibleControl("debug-xp") &&
							app.ActivateVisibleControl("debug-unlock-tech"),
							"All three debug buttons run their wired callbacks.");

						smokePhase++;
						return;

					case 403:
						// Verify the debug grants apply and the full-tree unlock disables itself.
						Check(
							app.State.Career.credits == 600 + GameSimulation.DebugGrantAmount &&
							app.State.Career.xp == GameSimulation.DebugGrantAmount &&
							app.State.ResearchCount == app.State.ResearchTotal,
							"Debug buttons grant currencies and unlock the entire tree.");

						Check(!app.Root.Q<Button>("debug-unlock-tech").enabledInHierarchy,
							  "Full-tree unlock disables after completion.");

						// Verify the debug menu blocks gameplay shortcuts and capture its screenshot.
						SendKey(app, KeyCode.Space);
						Check(app.State.Op.paused, "Debug menu blocks gameplay shortcuts.");
						CaptureScreenshot(Path.Combine(Project, "Artifacts/cheats-debug-menu.png"));

						// Verify the Back button returns to Settings.
						Check(app.ActivateVisibleControl("debug-back"), "Debug Back button returns to Settings.");
						smokePhase++;
						return;

					case 404:
						// Verify the debug submenu can be reopened and jump to the reset check.
						Check(app.ActivateVisibleControl("debug-menu"), "Debug submenu can be reopened.");
						smokePhase = 407;
						return;

					case 407:
						// Verify the debug menu opens the reset confirmation.
						Check(app.ActivateVisibleControl("debug-reset-game"), "Debug menu opens reset confirmation.");
						smokePhase = 408;
						return;

					case 408:
						// Verify the reset confirmation exposes both choices and that Cancel works.
						Check(app.Root.Q<Button>("confirm-reset") != null && app.Root.Q<Button>("cancel-reset") != null,
							  "Debug reset confirmation exposes both choices.");

						Check(app.ActivateVisibleControl("cancel-reset"), "Cancel reset callback runs.");
						smokePhase = 409;
						return;

					case 409:
						// Verify cancelling returns to the debug menu without losing the granted progress.
						Check(app.Root.Q<Button>("debug-back") != null,
							  "Cancel returns to Cheats / Debug without resetting.");

						Check(app.State.Career.credits == 600 + GameSimulation.DebugGrantAmount,
							  "Cancel preserves debug-granted progress.");

						smokePhase = 405;
						return;

					case 405:
						// Press Escape to leave the debug menu.
						SendKey(app, KeyCode.Escape);
						smokePhase++;
						return;

					case 406:
						// Verify Escape returns to Settings and Resume restores the running operation.
						Check(app.ActivateVisibleControl("resume") && !app.State.Op.paused,
							  "Escape from Debug returns to Settings and Resume restores a running operation.");

						// Write the debug menu result file.
						File.WriteAllText(Path.Combine(Project, "Artifacts/debug-menu-smoke.txt"),
										  "PASS / Real UI callbacks grant 1,000,000 credits and XP, unlock every technology, disable completed unlock, expose Reset Game with confirmation that cancels back to debug, block background controls and shortcuts, return using Back and Escape, and restore the previous operation pause state. Test career isolated from player saves.");

						// Reset to a fresh career for the connection and contract-exchange checks.
						app.UseTestCareer(new Career());
						smokePhase = 2;
						return;

					case 2:
						// Verify the contract exchange shows the preview graph with its labels and fit controls.
						Check(app.Root.Q<Button>("connect") != null, "Connect control exists.");
						var preview = app.Root.Q<GraphViewport>();

						// Verify every preview system and link is drawn in the 3D graph.
						Check(
							preview != null && preview.NodeCount == 32 && preview.VisibleNodes == 32 &&
							preview.VisibleEdges > 0,
							"GraphExplorer renders all preview systems and their links in 3D: " + preview?.NodeCount +
							" nodes / " + preview?.VisibleNodes + " drawn / " + preview?.VisibleEdges + " links.");

						Check(app.Root.Q<Button>("node-home") != null, "Projected system labels remain selectable.");

						// The graph host contains the graph; its footer controls belong to the surrounding network panel.
						var mapBounds = app.Root.Q(className: "network-panel").worldBound;

						Check(
							mapBounds.Contains(preview.worldBound.min + Vector2.one) &&
							mapBounds.Contains(preview.worldBound.max - Vector2.one) &&
							mapBounds.Contains(app.Root.Q<Button>("fit").worldBound.center),
							"Default layout keeps the full graph and its controls visible: viewport " + mapBounds +
							", graph " + preview.worldBound + ", fit " + app.Root.Q<Button>("fit").worldBound);

						// Check the signal shaders and the 3D viewport, then capture the exchange screenshot.
						ValidateSignalRendering(app);
						NetworkVisualValidation.CheckViewport(preview);
						CaptureScreenshot(Path.Combine(Project, "Artifacts/01-contract-exchange.png"));

						// Continue to the waypoint planning phase when that smoke is active, otherwise the node intel phase.
						smokePhase = SessionState.GetBool("HTP.WaypointSmoke", false) ? 127 : 100;
						return;

					case 100:
						// Select the preferred starting system and move on to the node intel checks.
						app.SelectNode(app.State.PreferredNode(GameContent.Story[0], true));
						smokePhase = 131;
						return;

					case 131:
						// Double-click the selected system to open its intel card.
						NetworkVisualValidation.DoubleClickSelected(app.Root.Q<GraphViewport>());
						smokePhase = 130;
						return;

					case 130:
						// Verify the focused node intel card, capture it, and orbit the camera.
						NetworkVisualValidation.CheckFocusedCard(app.Root.Q<GraphViewport>());
						CaptureScreenshot(Path.Combine(Project, "Artifacts/20-focused-node-intel.png"));
						NetworkVisualValidation.Orbit(app.Root.Q<GraphViewport>(), 55, 15);
						smokePhase = 101;
						return;

					case 101:
						// Capture the orbited network view.
						CaptureScreenshot(Path.Combine(Project, "Artifacts/network-orbit.png"));
						smokePhase = 102;
						return;

					case 102:
						// Reset the orbit and open the medium contract preview.
						NetworkVisualValidation.Orbit(app.Root.Q<GraphViewport>(), 0, 28);

						Check(
							app.ActivateVisibleControl("contract-" +
													   GameContent.Story.First(c => c.Intel == ContractIntel.Topology)
																  .id), "Medium contract can be previewed.");

						smokePhase = 120;
						return;

					case 120:
						// Verify the medium graph draws the full anonymous topology.
						var mediumGraph = app.Root.Q<GraphViewport>();

						Check(
							mediumGraph != null && mediumGraph.NodeCount == 44 && mediumGraph.VisibleNodes == 44 &&
							mediumGraph.VisibleEdges > 0, "Medium draws the complete anonymous topology.");

						// Verify medium node cards hide system names and tooltips.
						Check(
							mediumGraph.Query<Button>().ToList()
									   .All(b => b.text == "UNKNOWN SYSTEM" && string.IsNullOrEmpty(b.tooltip)),
							"Medium node cards expose no system names or hover tooltips.");

						// Verify medium shows no type legend or sector intel, then capture it.
						Check(
							app.Root.Query<VisualElement>(className: "legend-key").ToList().Count == 0 &&
							app.Root.Q(className: "sectors") == null,
							"Medium contains no type legend or sector intel.");

						CaptureScreenshot(Path.Combine(Project, "Artifacts/16-medium-contract-intel.png"));
						smokePhase = 128;
						return;

					case 128:
						// Build a career that has completed every contract before the hard one.
						var hard = GameContent.Story.First(c => c.Intel == ContractIntel.None);
						var developed = new Career();

						developed.completed =
							GameContent.Story.TakeWhile(c => c.id != hard.id).Select(c => c.id).ToList();

						// Open the hard contract preview in that career.
						app.UseTestCareer(developed);
						Check(app.ActivateVisibleControl("contract-" + hard.id), "Hard contract can be previewed.");
						smokePhase = 121;
						return;

					case 121:
						// Verify the hard contract creates no graph and shows the centered no-intel message.
						Check(app.Root.Q<GraphViewport>() == null && app.Root.Q("no-intel-view") != null,
							  "Hard creates no graph renderer or selectable systems.");

						var blocked = app.Root.Q("no-intel-view");
						var message = app.Root.Q<Label>(className: "no-intel-title");

						Check(
							message?.text == "NO INTEL AVAILABLE" &&
							Vector2.Distance(message.worldBound.center, blocked.worldBound.center) < 3,
							"Hard intel message is centered in the graph view.");

						// Verify the hard view hides the map controls and legend.
						Check(
							app.Root.Q<Button>("fit") == null && app.Root.Q<Button>("zoom-in") == null &&
							app.Root.Query<VisualElement>(className: "legend-key").ToList().Count == 0,
							"Hard hides map controls and system legend.");

						// Check the signal shaders, save the static field frame, and capture the screen.
						ValidateSignalRendering(app);
						frozenField = ReadPixels(app.Visuals.FieldTexture);
						CaptureScreenshot(Path.Combine(Project, "Artifacts/17-hard-contract-no-intel.png"));
						smokePhase++;
						return;

					case 122:
						// Verify the static animates in Full effects mode.
						Check(!frozenField.SequenceEqual(ReadPixels(app.Visuals.FieldTexture)),
							  "Hard static animates in Full effects mode.");

						// Switch to Reduced effects.
						app.Visuals.SetMode(SignalEffects.Reduced);
						smokePhase++;
						return;

					case 123:
						// Save the field frame that should stay still in Reduced mode.
						frozenField = ReadPixels(app.Visuals.FieldTexture);
						smokePhase++;
						return;

					case 124:
						// Verify the static freezes in Reduced effects mode, then switch effects off.
						Check(frozenField.SequenceEqual(ReadPixels(app.Visuals.FieldTexture)),
							  "Hard static freezes in Reduced effects mode.");

						app.Visuals.SetMode(SignalEffects.Off);
						smokePhase++;
						return;

					case 125:
						// Verify Effects Off hides the field but keeps the intel message, then reconnect in Full.
						Check(
							app.Root.Q<Image>("signal-field").resolvedStyle.display == DisplayStyle.None &&
							app.Root.Q<Label>(className: "no-intel-title").resolvedStyle.display != DisplayStyle.None,
							"Effects Off preserves the Hard intel message without animation.");

						app.Visuals.SetMode(SignalEffects.Full);

						// Verify the hard contract connects through its intel screen.
						Check(app.ActivateVisibleControl("connect") && app.State.Active,
							  "Hard connects through its intel screen.");

						smokePhase++;
						return;

					case 126:
						// Verify connecting replaces the static view with the discovered systems only.
						Check(
							app.Root.Q("no-intel-view") == null &&
							app.Root.Q<GraphViewport>()?.NodeCount == app.State.KnownGraph.Nodes.Count &&
							app.State.KnownGraph.Nodes.Count < app.State.Op.nodes.Count,
							"Hard connection replaces the static screen with discovered systems only.");

						// Reset to a fresh career and release the saved field frame.
						app.UseTestCareer(new Career());
						frozenField = null;
						smokePhase++;
						return;

					case 127:
						// Verify returning to Easy restores the full preview.
						Check(app.Root.Q<GraphViewport>()?.NodeCount == 32 && app.Root.Q("no-intel-view") == null,
							  "Returning to Easy restores the full preview.");

						// Grant XP and buy each waypoint research tier for the waypoint smoke.
						app.State.Career.xp = 1000;

						for (var tier = 1; tier <= 4; tier++)
							Check(app.State.BuyTech("waypoints-" + tier), "Waypoint smoke research tier " + tier);

						// Select the n22 system and move on to waypoint planning.
						app.SelectNode("n22");
						smokePhase = 140;
						return;

					case 140:
						// Verify the contract planner places a waypoint on the selected system.
						Check(app.ActivateVisibleControl("place-waypoint"),
							  "Contract planner places a waypoint on the selected system.");

						smokePhase++;
						return;

					case 141:
						// Verify the waypoint colour, label, and route controls work.
						Check(app.ActivateVisibleControl("waypoint-1-color-2"), "Waypoint colour control works.");
						app.Root.Q<TextField>("waypoint-label-1").value = "Recovery target";
						Check(app.ActivateVisibleControl("waypoint-route-1"), "Waypoint route control works.");
						smokePhase++;
						return;

					case 142:
						// Verify the custom label shows above its system, then capture the planning screen.
						Check(app.Root.Q<Label>("waypoint-marker-n22")?.text.Contains("Recovery target") == true,
							  "Custom waypoint label appears above its system in the preview.");

						CaptureScreenshot(Path.Combine(Project, "Artifacts/21-waypoint-planning.png"));

						// Verify the waypoint plan connects through the contract UI.
						Check(app.ActivateVisibleControl("connect"), "Waypoint plan connects through the contract UI.");
						smokePhase++;
						return;

					case 143:
						// Verify the waypoint marker stays visible over the hidden system during the run.
						var marker = app.Root.Q<Label>("waypoint-marker-n22");

						Check(
							!app.State.Node("n22").known && marker != null &&
							marker.resolvedStyle.display == DisplayStyle.Flex,
							"Waypoint stays visible over an undiscovered system during the run.");

						// Verify the waypoint route does not reveal hidden system models.
						Check(app.Root.Q<GraphViewport>().NodeCount == app.State.Op.nodes.Count(n => n.known),
							  "Waypoint path does not reveal hidden system models.");

						// Verify the selected waypoint route survives the connection, then capture it.
						Check(app.State.Waypoints(app.State.Job).Single().showPath,
							  "Selected waypoint route survives connection.");

						CaptureScreenshot(Path.Combine(Project, "Artifacts/22-waypoint-hidden-route.png"));

						// End the waypoint smoke test here, otherwise continue with the full smoke run.
						if (SessionState.GetBool("HTP.WaypointSmoke", false))
						{
							// Restore the text size and effects, then stop play mode.
							app.SetTextSize(previousTextSize);
							app.Visuals.SetMode(previousEffects);
							smokePhase = 144;
							EditorApplication.isPlaying = false;
							return;
						}

						// Start a fresh career for the mission connection checks.
						app.UseTestCareer(new Career());
						smokePhase = 3;
						return;

					case 3:
						// Verify the connect button starts a mission through the UI.
						Check(app.ActivateVisibleControl("connect"), "Connect button works.");
						Check(app.State.Active, "Mission connected through UI.");
						smokePhase++;
						return;

					case 4:
						// Verify the action bar disables Scan until a target is selected.
						var scanBarButton = app.Root.Q<Button>("action-scan");

						Check(scanBarButton != null && !scanBarButton.enabledInHierarchy,
							  "Action bar disables Scan until a graph target is selected.");

						// Select n2 and verify the Scan button is wired.
						app.SelectNode("n2");
						Check(app.ActivateVisibleControl("action-scan"), "Scan button is wired.");

						// Verify the action button decorations ignore pointer hits.
						var scanButton = app.Root.Q<Button>("action-scan");

						Check(
							scanButton != null && scanButton.Query<VisualElement>().Where(v => v != scanButton).ToList()
															.All(v => v.pickingMode == PickingMode.Ignore),
							"Action button decorations ignore pointer hits.");

						// Finish the scan task and reselect n2 for the next phase.
						GameValidation.FinishTask(app.State);
						app.SelectNode("n2");
						smokePhase++;
						return;

					case 5:
						// Verify the hack button is wired, then queue a future scan while a tool is running.
						Check(app.ActivateVisibleControl("action-hack"), "Hack button is wired.");
						app.State.Tick(.25f);
						Check(app.State.Execute("scan", "n6"), "A future reachable scan can be queued.");

						// Pause the simulation so the queue can be inspected.
						app.State.SetManualPause(true);
						smokePhase = 110;
						return;

					case 110:
						// Find the queued action chip and verify Stop and Clear are enabled.
						var queuedChip = app.Root.Query<VisualElement>(className: "action-queue-chip").ToList()
											.FirstOrDefault(c => c.tooltip != null &&
																 c.tooltip.Contains("Scan connections"));

						Check(
							app.Root.Q<Button>("stop-tool")?.enabledInHierarchy == true &&
							app.Root.Q<Button>("clear-queue")?.enabledInHierarchy == true && queuedChip != null,
							"Running tools expose enabled Stop and Clear, and the queued action chip.");

						// Capture the tactical queue screen.
						CaptureScreenshot(Path.Combine(Project, "Artifacts/15-tactical-queue.png"));

						// Verify Clear removes queued work but keeps the current tool running.
						Check(
							app.ActivateVisibleControl("clear-queue") && app.State.Op.task != null &&
							app.State.Op.queue.Count == 0,
							"Clear removes queued work without cancelling the current tool.");

						// Rebuild the queue, then verify Stop cancels work without refunding trace.
						Check(app.State.Execute("scan", "n6"), "Queue can be rebuilt after clearing.");
						var beforeStop = app.State.Op.trace;

						Check(
							app.ActivateVisibleControl("stop-tool") && app.State.Op.task == null &&
							app.State.Op.queue.Count == 0 && app.State.Op.trace == beforeStop,
							"Stop cancels work without refunding generated trace.");

						smokePhase = 111;
						return;

					case 111:
						// Verify the pause control resumes the paused cancellation.
						Check(app.ActivateVisibleControl("pause") && !app.State.Op.paused,
							  "Paused cancellation resumes through the pause control.");

						// Verify a stopped tool can be restarted from the clean action list.
						Check(
							app.Root.Q<Button>("action-hack").enabledInHierarchy &&
							app.ActivateVisibleControl("action-hack"),
							"A stopped tool can be restarted through the clean action list.");

						// Finish the active task, hack two nodes, and select n14 for the next phase.
						GameValidation.FinishTask(app.State);
						GameValidation.Hack(app.State, "n6");
						GameValidation.Hack(app.State, "n10");
						app.SelectNode("n14");
						smokePhase = 6;
						return;

					case 6:
						// Verify the live graph shows only discovered systems and connections.
						var operationGraph = app.Root.Q<GraphViewport>();

						Check(
							operationGraph != null && operationGraph.NodeCount > 2 &&
							operationGraph.NodeCount < app.State.Op.nodes.Count && operationGraph.VisibleEdges > 0,
							"Live GraphExplorer view shows discovered systems and connections only.");

						// Verify the rig status expands without covering the graph.
						Check(
							app.ActivateVisibleControl("rig-status") &&
							app.Root.Q<VisualElement>(className: "rig-stats-popover").resolvedStyle.display ==
							DisplayStyle.Flex,
							"Rig status expands without covering the graph by default.");

						// Verify the rig status collapses back into the utility row.
						Check(
							app.ActivateVisibleControl("rig-status") &&
							app.Root.Q<VisualElement>(className: "rig-stats-popover").resolvedStyle.display ==
							DisplayStyle.None,
							"Rig status collapses back into the utility row.");

						// Capture the live operation, then resize the render texture to 1280 x 720.
						CaptureScreenshot(Path.Combine(Project, "Artifacts/02-live-operation.png"));
						smokeUiTexture.Release();
						smokeUiTexture.width = 1280;
						smokeUiTexture.height = 720;
						smokeUiTexture.Create();
						smokePhase = 61;
						return;

					case 61:
						// Verify the live graph stays usable at 1280 x 720, then capture it.
						var resizedGraph = app.Root.Q<GraphViewport>();

						Check(
							resizedGraph != null && resizedGraph.worldBound.width > 450 &&
							resizedGraph.worldBound.height > 180,
							"The live graph remains usable at 1280 x 720: " + resizedGraph?.worldBound);

						CaptureScreenshot(Path.Combine(Project, "Artifacts/02-live-operation-1280x720.png"));

						// Resize the render texture to 1920 x 1080.
						smokeUiTexture.Release();
						smokeUiTexture.width = 1920;
						smokeUiTexture.height = 1080;
						smokeUiTexture.Create();
						smokePhase = 62;
						return;

					case 62:
						// Verify the live graph expands at 1920 x 1080, then capture it.
						var wideGraph = app.Root.Q<GraphViewport>();

						Check(
							wideGraph != null && wideGraph.worldBound.width > 700 && wideGraph.worldBound.height > 300,
							"The live graph expands at 1920 x 1080: " + wideGraph?.worldBound);

						CaptureScreenshot(Path.Combine(Project, "Artifacts/02-live-operation-1920x1080.png"));

						// Restore the 1680 x 960 render texture and hack the remaining path to n22.
						smokeUiTexture.Release();
						smokeUiTexture.width = 1680;
						smokeUiTexture.height = 960;
						smokeUiTexture.Create();
						GameValidation.Hack(app.State, "n14");
						GameValidation.Hack(app.State, "n18");
						GameValidation.Hack(app.State, "n22");
						app.SelectNode("n22");
						smokePhase = 7;
						return;

					case 7:
						// Verify the recovery button is wired and finish the download task.
						Check(app.ActivateVisibleControl("action-download"), "Recovery button is wired.");
						GameValidation.FinishTask(app.State);
						smokePhase++;
						return;

					case 8:
						// Verify delivery through the disconnect button banks the rewards.
						Check(app.ActivateVisibleControl("disconnect"), "Delivery button works.");
						Check(!app.State.Active && app.State.Career.jobs == 1, "Delivery banked rewards.");
						smokePhase++;
						return;

					case 9:
						// Capture the operation result screen.
						CaptureScreenshot(Path.Combine(Project, "Artifacts/03-operation-result.png"));
						smokePhase++;
						return;

					case 10:
						// Verify the research link on the result screen works.
						Check(app.ActivateVisibleControl("report-research"), "Research result link works.");
						smokePhase++;
						return;

					case 11:
						// Progressive reveal: only discovered technologies (and their direct children) are drawn.
						Check(
							app.Root.Q<GraphViewport>()?.NodeCount == app.State.Career.researchRevealed.Count &&
							app.State.Career.researchRevealed.Count < GameContent.Tech.Length &&
							app.Root.Q<Button>("tech-wide") != null && app.Root.Q("tech-signal") == null &&
							!app.State.ResearchVisible("signal"),
							"GraphExplorer reveals only connected research: " + app.Root.Q<GraphViewport>()?.NodeCount +
							" of " + GameContent.Tech.Length + " technologies drawn.");

						Check(app.ActivateVisibleControl("tech-wide"), "Research node selection works.");
						Check(app.ActivateVisibleControl("unlock"), "Research purchase works.");
						Check(app.State.Has("wide"), "Tech purchased using earned XP.");

						Check(app.State.ResearchVisible("signal"),
							  "Purchasing Wide Scan reveals its connected research.");

						// Preview a developed build for the research screenshot without touching the real career.
						app.State.Career.xp = 360;
						app.State.BuyTech("quiet");
						app.State.BuyTech("efficient");
						app.SelectTech("passive");
						smokePhase++;
						return;

					case 12:
						// Capture the research network screen.
						CaptureScreenshot(Path.Combine(Project, "Artifacts/04-research-network.png"));
						smokePhase++;
						return;

					case 13:
						// Open the rig page.
						app.Navigate("rig");
						smokePhase++;
						return;

					case 14:
						// Verify the module purchase button works and the second CPU copy is owned.
						Check(app.ActivateVisibleControl("buy-processor"), "Module purchase button works.");

						Check(app.State.Career.rigParts.Count(p => p.modelId == "processor") == 2,
							  "Second CPU copy is owned.");

						smokePhase++;
						return;

					case 15:
						// Verify the rack UI, then capture the rig screen.
						RigValidation.CheckRackUi(app);
						CaptureScreenshot(Path.Combine(Project, "Artifacts/05-your-rig.png"));
						smokePhase++;
						return;

					case 16:
						// Isolated display fixture; banking and failure semantics are exercised by NetworkValidation.
						app.State.Career.lore.Add(LoreContent.For(GameContent.Story[0], 0));
						app.Navigate("intel");
						smokePhase++;
						return;

					case 17:
						// Verify the lore collection shows the fragment title, then capture the intel screen.
						Check(app.Root.Query<Label>().ToList().Any(l => l.text == "The missing appendix"),
							  "Permanent lore collection displays fragment titles.");

						CaptureScreenshot(Path.Combine(Project, "Artifacts/06-intelligence.png"));
						smokePhase++;
						return;

					case 18:
						// Start the selected operation.
						app.StartSelected();
						smokePhase++;
						return;

					case 19:
						// Hack up to n10, select n14, and run a real scan to set up the trace warning.
						GameValidation.Hack(app.State, "n2");
						GameValidation.Hack(app.State, "n6");
						GameValidation.Hack(app.State, "n10");
						app.SelectNode("n14");
						app.State.Op.trace = 87;
						Check(app.State.Execute(ToolKind.Scan, "n14"), "Trace-warning fixture can run a real tool.");

						// Advance the scan partway and pause the operation.
						app.State.Tick(app.State.Op.task.duration * .42f);
						app.State.SetPaused(true);
						smokePhase++;
						return;

					case 20:
						// Verify high trace changes the warning treatment, then capture it.
						Check(app.Root.ClassListContains("trace-alert"),
							  "High trace changes the workstation warning treatment.");

						CaptureScreenshot(Path.Combine(Project, "Artifacts/07-trace-warning.png"));

						// Open Settings and verify the animation toggle freezes shader motion.
						Check(app.ActivateVisibleControl("settings"), "Settings opens for display effect controls.");
						Check(app.ActivateVisibleControl("effect-animate"), "Animate display effects toggle is wired.");
						Check(app.Visuals.Mode == SignalEffects.Reduced, "Disabling animation freezes shader motion.");
						smokePhase++;
						return;

					case 21:
						// Verify reduced effects stop UI motion, then save the shader frames for comparison.
						Check(app.Root.ClassListContains("motion-off"), "Reduced display effects also stop UI motion.");
						frozenField = ReadPixels(app.Visuals.FieldTexture);
						frozenGlass = ReadPixels(app.Visuals.GlassTexture);
						sharedField = app.Visuals.FieldTexture;
						sharedGlass = app.Visuals.GlassTexture;
						CaptureScreenshot(Path.Combine(Project, "Artifacts/08-reduced-effects.png"));
						smokePhase++;
						return;

					case 22:
						// Verify the shader textures stay exactly static, then turn both display layers off.
						Check(
							frozenField.SequenceEqual(ReadPixels(app.Visuals.FieldTexture)) &&
							frozenGlass.SequenceEqual(ReadPixels(app.Visuals.GlassTexture)),
							"Reduced shader textures stay exactly static across frames.");

						Check(
							app.ActivateVisibleControl("effect-signal-field") &&
							app.ActivateVisibleControl("effect-phosphor-glass") &&
							app.Visuals.Mode == SignalEffects.Off, "Display layers can be turned off.");

						smokePhase++;
						return;

					case 23:
						// Verify Off mode hides both shader layers, then capture the screen.
						Check(
							app.Root.Q<Image>("signal-field").resolvedStyle.display == DisplayStyle.None &&
							app.Root.Q<Image>("phosphor-glass").resolvedStyle.display == DisplayStyle.None,
							"Off mode hides both shader layers.");

						Check(app.Root.Q<Button>("action-scan") != null, "Action controls survive effects changes.");
						CaptureScreenshot(Path.Combine(Project, "Artifacts/09-effects-off.png"));

						// Turn the layers and animation back on and verify full effects return.
						Check(
							app.ActivateVisibleControl("effect-signal-field") &&
							app.ActivateVisibleControl("effect-phosphor-glass") &&
							app.ActivateVisibleControl("effect-animate") && app.Visuals.Mode == SignalEffects.Full,
							"Display layers and animation restore full effects.");

						smokePhase++;
						return;

					case 24:
						// Verify restoring full effects restores UI motion and reuses the GPU textures.
						Check(!app.Root.ClassListContains("motion-off"), "Restoring full effects restores UI motion.");

						Check(
							ReferenceEquals(sharedField, app.Visuals.FieldTexture) &&
							ReferenceEquals(sharedGlass, app.Visuals.GlassTexture),
							"UI rebuilds reuse both GPU textures.");

						Check(!frozenField.SequenceEqual(ReadPixels(app.Visuals.FieldTexture)),
							  "Full effects animate the signal field.");

						// Restore the previous effects and text size, then remember the operation state for Settings.
						app.Visuals.SetMode(previousEffects);
						frozenField = null;
						frozenGlass = null;
						app.SetTextSize(200);
						settingsCareer = app.State.Career;
						settingsElapsed = app.State.Op.elapsed;
						settingsTaskElapsed = app.State.Op.task.elapsed;
						settingsTrace = app.State.Op.trace;

						// Verify Settings stays open while display controls change.
						Check(app.Root.Q<VisualElement>(className: "settings-menu") != null,
							  "Settings stays open while display controls are changed.");

						smokePhase++;
						return;

					case 25:
						// Verify the Settings menu contents, its size, and that it freezes the operation.
						var menu = app.Root.Q<VisualElement>(className: "settings-menu");

						Check(
							menu != null && menu.Q<Button>("resume")?.text == "Resume" &&
							menu.Q<Button>("reset-save")?.text == "Reset Game Save" &&
							menu.Q<Button>("quit")?.text == "Quit",
							"Settings retains Resume, Reset Game Save, and Quit.");

						Check(app.Root.Q<SliderInt>("text-size")?.value == 200,
							  "Settings reflects the active 200% compatibility size.");

						Check(
							menu.worldBound.width > 300 && menu.worldBound.height > 200 &&
							app.Root.worldBound.Contains(menu.worldBound.min) &&
							app.Root.worldBound.Contains(menu.worldBound.max),
							"Settings menu fits within the rendered viewport.");

						Check(
							app.State.Op.paused && app.State.Op.elapsed == settingsElapsed &&
							app.State.Op.task.elapsed == settingsTaskElapsed && app.State.Op.trace == settingsTrace,
							"Settings freezes operation time, tool progress, and trace across frames.");

						// Verify Settings blocks gameplay and navigation controls underneath, then capture it.
						Check(!app.ActivateVisibleControl("action-scan") && !app.ActivateVisibleControl("tab-research"),
							  "Settings blocks underlying gameplay and navigation controls.");

						CaptureScreenshot(Path.Combine(Project, "Artifacts/10-settings-menu.png"));
						smokePhase = 300;
						return;

					case 300:
						// Set the text size slider to 100%.
						app.Root.Q<SliderInt>("text-size").value = 100;
						smokePhase = 301;
						return;

					case 301:
						// Record the baseline font size of every text element at 100%, then switch to 300%.
						textSizeBaseline.Clear();

						foreach (var text in app.Root.Query<TextElement>().ToList())
							textSizeBaseline[text] = text.resolvedStyle.fontSize;

						app.Root.Q<SliderInt>("text-size").value = 300;
						smokePhase++;
						return;

					case 302:
						// Verify the slider callback set 300% and that every text element scaled by 3x.
						Check(app.TextSizePercent == 300, "Text size changes through the actual slider callback.");

						foreach (var text in app.Root.Query<TextElement>().ToList())
							if (textSizeBaseline.TryGetValue(text, out var baseline))
								Check(Mathf.Abs(text.resolvedStyle.fontSize - baseline * 3) < .1f,
									  "Every text element scales, including graph labels and settings inputs: " +
									  text.name + " / " + text.text + " / " + text.resolvedStyle.fontSize +
									  " expected " + baseline * 3);

						// Verify the Settings menu stays inside the viewport at 300%.
						var largeMenu = app.Root.Q(className: "settings-menu");

						Check(
							app.Root.worldBound.Contains(largeMenu.worldBound.min) &&
							app.Root.worldBound.Contains(largeMenu.worldBound.max),
							"Settings stays within the viewport at 300%.");

						// Scroll to the Quit button, then capture the 300% settings screen.
						var menuScroll = largeMenu as ScrollView;
						menuScroll.ScrollTo(app.Root.Q<Button>("quit"));
						CaptureScreenshot(Path.Combine(Project, "Artifacts/18-settings-300-percent.png"));
						smokePhase++;
						return;

					case 303:
						// Resize the render texture to 1280 x 720 for the small-screen settings check.
						smokeUiTexture.Release();
						smokeUiTexture.width = 1280;
						smokeUiTexture.height = 720;
						smokeUiTexture.Create();
						smokePhase++;
						return;

					case 304:
						// Verify the 300% Settings menu still fits, scroll to Resume, then capture it.
						var smallMenu = app.Root.Q(className: "settings-menu");

						Check(
							app.Root.worldBound.Contains(smallMenu.worldBound.min) &&
							app.Root.worldBound.Contains(smallMenu.worldBound.max),
							"300% settings fits after resizing to 1280 x 720.");

						(smallMenu as ScrollView).ScrollTo(app.Root.Q<Button>("resume"));
						CaptureScreenshot(Path.Combine(Project, "Artifacts/19-settings-1280x720.png"));
						smokePhase++;
						return;

					case 305:
						// Remember the saved text-size preference so it can be restored afterwards.
						var hadSize = PlayerPrefs.HasKey(WorkstationApp.TextSizePreference);
						var savedSize = PlayerPrefs.GetInt(WorkstationApp.TextSizePreference);

						try
						{
							// Change the text size and verify it is saved to preferences, separate from the career.
							app.Testing = false;
							app.SetTextSize(175);

							Check(PlayerPrefs.GetInt(WorkstationApp.TextSizePreference) == 175,
								  "Text size is saved independently of the career.");
						}
						finally
						{
							// Restore the original saved text-size preference and return to 200%.
							app.Testing = true;

							if (hadSize)
								PlayerPrefs.SetInt(WorkstationApp.TextSizePreference, savedSize);
							else
								PlayerPrefs.DeleteKey(WorkstationApp.TextSizePreference);

							PlayerPrefs.Save();
							app.SetTextSize(200);
						}

						// Restore the 1680 x 960 render texture.
						smokeUiTexture.Release();
						smokeUiTexture.width = 1680;
						smokeUiTexture.height = 960;
						smokeUiTexture.Create();
						smokePhase++;
						return;

					case 306:
						// Verify 200% is kept, then Resume restarts the paused operation.
						Check(app.TextSizePercent == 200, "Returning to 200% preserves the chosen text-size setting.");

						Check(app.ActivateVisibleControl("resume") && !app.State.Op.paused,
							  "Resume restarts the operation that Settings paused.");

						// Pause again with Escape and verify Settings opens and pauses the operation.
						app.State.SetPaused(false);
						SendKey(app, KeyCode.Escape);
						Check(app.State.Op.paused, "Escape opens Settings and pauses a running operation.");
						smokePhase = 26;
						return;

					case 26:
						// Verify gameplay shortcuts cannot run behind Settings, and Escape resumes the operation.
						SendKey(app, KeyCode.Space);
						SendKey(app, KeyCode.Alpha2);
						Check(app.State.Op.paused, "Gameplay shortcuts cannot run behind Settings.");
						SendKey(app, KeyCode.Escape);
						Check(!app.State.Op.paused, "Escape resumes a previously running operation.");

						// Pause the operation and reopen Settings for the reset checks.
						app.State.SetPaused(true);
						SendKey(app, KeyCode.Escape);
						smokePhase++;
						return;

					case 27:
						// Verify the reset button opens confirmation without changing the career or operation.
						Check(app.ActivateVisibleControl("reset-save"),
							  "Reset opens confirmation before changing progress.");

						Check(ReferenceEquals(app.State.Career, settingsCareer) && app.State.Active,
							  "Opening reset confirmation preserves the career and operation.");

						smokePhase++;
						return;

					case 28:
						// Verify the confirmation shows both choices, then capture it.
						Check(app.Root.Q<Button>("confirm-reset") != null && app.Root.Q<Button>("cancel-reset") != null,
							  "Reset confirmation exposes both explicit choices.");

						CaptureScreenshot(Path.Combine(Project, "Artifacts/11-reset-save-confirmation.png"));
						smokePhase++;
						return;

					case 29:
						// Press Escape to cancel the reset confirmation.
						SendKey(app, KeyCode.Escape);
						smokePhase++;
						return;

					case 30:
						// Verify Escape returns to Settings with progress intact, then reopen reset.
						Check(
							app.Root.Q<Button>("resume") != null && ReferenceEquals(app.State.Career, settingsCareer) &&
							app.State.Op.paused,
							"Escape cancels reset and returns to Settings without losing progress.");

						Check(app.ActivateVisibleControl("reset-save"), "Reset confirmation can be reopened.");
						smokePhase++;
						return;

					case 31:
						// Verify the Cancel button closes the reset confirmation.
						Check(app.ActivateVisibleControl("cancel-reset"), "Cancel returns from reset confirmation.");
						smokePhase++;
						return;

					case 32:
						// Verify cancelling keeps the existing career, then reopen reset for the confirmed run.
						Check(
							ReferenceEquals(app.State.Career, settingsCareer) &&
							app.ActivateVisibleControl("reset-save"), "Cancel preserves the existing career.");

						smokePhase++;
						return;

					case 33:
						// Confirm the reset and verify it starts a fresh career with no active operation.
						Check(app.ActivateVisibleControl("confirm-reset"), "Confirmed reset callback runs.");
						var fresh = app.State.Career;

						Check(
							!ReferenceEquals(fresh, settingsCareer) && !app.State.Active && fresh.credits == 600 &&
							fresh.xp == 0 && fresh.day == 1 && fresh.jobs == 0 && fresh.completed.Count == 0 &&
							fresh.reports.Count == 0 && fresh.cpu == 0 &&
							fresh.unlocked.SequenceEqual(new[] { "base" }),
							"Confirmed reset starts a fresh career and clears the active operation.");

						smokePhase++;
						return;

					case 34:
						// Verify the reset returns to the first contract and keeps the text size.
						Check(app.CurrentView == "network" && app.Root.Q<Button>("connect").enabledInHierarchy,
							  "Reset returns to the first contract with a usable connection control.");

						Check(app.TextSizePercent == 200, "Career resets preserve text size.");

						// Close Settings with Escape.
						SendKey(app, KeyCode.Escape);
						smokePhase++;
						return;

					case 35:
						// Resume Settings, then build the asset-only fixture career and contract.
						Check(app.ActivateVisibleControl("resume"), "Settings resumes before asset-only fixture.");
						dataFixture = DataDrivenValidation.CreateFixture();
						var fixtureCareer = dataFixture.Catalog.CreateCareer();
						fixtureCareer.credits = 10000;
						app.UseTestCatalog(dataFixture.Catalog, fixtureCareer, "field-contract");
						smokePhase++;
						return;

					case 36:
						// Verify the asset-only contract and research connect through the generic APIs.
						Check(app.Root.Q<Button>("contract-field-contract") != null,
							  "Asset-only contract appears in exchange.");

						Check(app.State.BuyTech("field-analysis") && app.ActivateVisibleControl("connect"),
							  "Asset-only research and contract connect through generic APIs.");

						// Hack the two fixture systems and select beta.
						GameValidation.Hack(app.State, "alpha");
						GameValidation.Hack(app.State, "beta");
						app.SelectNode("beta");
						smokePhase++;
						return;

					case 37:
						// Verify the asset-defined action button and network render, then scroll to the button.
						Check(app.Root.Q<Button>("action-audit") != null && app.Root.Q<GraphViewport>().NodeCount == 4,
							  "Asset-only action button and arbitrary network render.");

						app.Root.Q<Button>("action-audit").GetFirstAncestorOfType<ScrollView>()
						   .ScrollTo(app.Root.Q<Button>("action-audit"));

						smokePhase = 103;
						return;

					case 103:
						// Press the authored shortcut and verify it runs the action and completes the objective.
						SendKey(app, KeyCode.V);

						Check(app.State.Op.task?.actionId == "audit",
							  "Asset-only shortcut executes the authored action.");

						GameValidation.FinishTask(app.State);
						Check(app.State.ObjectiveComplete, "Asset-only objective completes.");
						CaptureScreenshot(Path.Combine(Project, "Artifacts/12-asset-defined-operation.png"));
						smokePhase = 38;
						return;

					case 38:
						// Verify the asset-only objective delivers through the disconnect button.
						Check(app.ActivateVisibleControl("disconnect"), "Asset-only objective delivers through UI.");
						smokePhase++;
						return;

					case 39:
						// Close the report and open the rig page.
						Check(app.ActivateVisibleControl("close-report"), "Asset-only report closes.");
						app.Navigate("rig");
						smokePhase++;
						return;

					case 40:
						// Scroll the rack shop to the authored expansion module.
						app.Root.Q<Button>("buy-expansion").GetFirstAncestorOfType<ScrollView>()
						   .ScrollTo(app.Root.Q<Button>("buy-expansion"));

						smokePhase = 104;
						return;

					case 104:
						// Verify the authored module appears and can be purchased, then capture the rack.
						Check(app.Root.Q<Button>("buy-expansion") != null, "Authored module appears in the rack shop.");

						Check(
							app.ActivateVisibleControl("buy-expansion") &&
							app.State.Career.rigParts.Any(p => p.modelId == "expansion"),
							"Asset-only module purchases through UI.");

						CaptureScreenshot(Path.Combine(Project, "Artifacts/13-asset-defined-hardware.png"));

						// Open the research page.
						app.Navigate("research");
						smokePhase = 41;
						return;

					case 41:
						// Verify the authored technology appears in the research graph, then capture it.
						Check(app.Root.Q<Button>("tech-field-analysis") != null,
							  "Asset-only technology appears in research graph.");

						CaptureScreenshot(Path.Combine(Project, "Artifacts/14-asset-defined-research.png"));

						// Close the research view.
						SendKey(app, KeyCode.Escape);
						smokePhase++;
						return;

					case 42:
						// Restore the text size, then verify the Quit button is wired.
						app.SetTextSize(previousTextSize);
						smokePhase = 43;
						Check(app.ActivateVisibleControl("quit"), "Quit button is wired.");
						return;
				}
			}
			catch (Exception ex)
			{
				// Restore the workstation state, report the failure, and stop play mode.
				if (WorkstationApp.Instance != null)
					WorkstationApp.Instance.SetTextSize(previousTextSize);

				WorkstationApp.Instance?.Visuals?.SetMode(previousEffects);
				frozenField = null;
				frozenGlass = null;
				SessionState.SetBool("HTP.Smoke", false);
				smokePhase = 0;
				Status("FAIL / smoke\n" + ex);
				Debug.LogException(ex);
				EditorApplication.isPlaying = false;

				// Exit with an error code when running in batch mode.
				if (Application.isBatchMode)
					EditorApplication.Exit(1);
			}
		}

		// Captures the smoke UI render texture or screen to a PNG at the given path.
		private static void CaptureScreenshot(string path)
		{
			// Fall back to Unity's screen capture when no render texture is active.
			if (smokeUiTexture == null)
			{
				ScreenCapture.CaptureScreenshot(path);
				return;
			}

			// Read the render texture into a CPU-side texture, keeping the previous active target.
			var previous = RenderTexture.active;
			var pixels = new Texture2D(smokeUiTexture.width, smokeUiTexture.height, TextureFormat.RGBA32, false);

			try
			{
				// Copy the UI render texture pixels into the readback texture.
				RenderTexture.active = smokeUiTexture;
				pixels.ReadPixels(new Rect(0, 0, smokeUiTexture.width, smokeUiTexture.height), 0, 0);
				pixels.Apply();

				// Verify the capture is not blank, then save it as a PNG.
				var data = pixels.GetPixels32();

				Check(data.Any(p => p.g > 40) && data.Select(p => p.g).Distinct().Count() > 20,
					  "Workstation capture contains nonblank rendered UI pixels.");

				File.WriteAllBytes(path, pixels.EncodeToPNG());
			}
			finally
			{
				// Restore the previous render target and free the readback texture.
				RenderTexture.active = previous;
				Object.DestroyImmediate(pixels);
			}
		}

		// Sends a synthetic key-down event to the workstation UI root.
		private static void SendKey(WorkstationApp app, KeyCode key)
		{
			// Send a pooled key-down event to the workstation UI root.
			using (var evt = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = key }))
			{
				app.Root.SendEvent(evt);
			}
		}

		// Asserts signal shaders compile, render visible output, and ignore pointer input.
		private static void ValidateSignalRendering(WorkstationApp app)
		{
			// Verify both signal shaders are supported by the GPU.
			Check(app.Visuals.Available, "Both Ghost Frequency shaders are supported.");

			// Verify each shader compiles without errors.
			foreach (var name in new[] { "SignalField", "PhosphorGlass" })
				Check(!ShaderUtil.ShaderHasError(Resources.Load<Shader>("HackThePlanet/Shaders/" + name)),
					  name + " compiles without errors.");

			// Read back both shader outputs from the GPU.
			var field = ReadPixels(app.Visuals.FieldTexture);
			var glass = ReadPixels(app.Visuals.GlassTexture);

			// Verify the field output is a visible, nonuniform image.
			Check(field.Any(c => c.g > c.r && c.g > 3) && field.Select(c => c.g).Distinct().Count() > 5,
				  "Signal-field GPU output contains a visible nonuniform image.");

			// Verify the glass output has restrained, nonzero transparency.
			Check(glass.All(c => c.a < 65) && glass.Any(c => c.a > 0),
				  "Phosphor-glass GPU output has restrained nonzero transparency.");

			// Verify the shader layers ignore pointer input.
			Check(
				app.Root.Q<Image>("signal-field").pickingMode == PickingMode.Ignore &&
				app.Root.Q<Image>("phosphor-glass").pickingMode == PickingMode.Ignore,
				"Shader decorations do not intercept input.");

			// Verify the UI font is present for standalone players.
			Check(AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/Oxanium-VariableFont_wght.ttf") != null,
				  "Oxanium UI font is available for standalone players.");
		}

		// Reads pixels from a shader render texture for smoke-test image checks.
		private static Color32[] ReadPixels(RenderTexture texture)
		{
			// Verify the render texture exists and has been allocated.
			Check(texture != null && texture.IsCreated(), "Shader render texture is allocated.");

			// Read the texture into a CPU-side texture, keeping the previous active target.
			var previous = RenderTexture.active;
			var readback = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);

			try
			{
				// Copy the render texture pixels and return them.
				RenderTexture.active = texture;
				readback.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
				readback.Apply();
				return readback.GetPixels32();
			}
			finally
			{
				// Restore the previous render target and free the readback texture.
				RenderTexture.active = previous;
				Object.DestroyImmediate(readback);
			}
		}

		// Throws when a smoke-test assertion fails so the run reports the message.
		private static void Check(bool condition, string message)
		{
			// Throw with the message when the smoke-test condition is false.
			if (!condition)
				throw new InvalidOperationException(message);
		}
	}
}
