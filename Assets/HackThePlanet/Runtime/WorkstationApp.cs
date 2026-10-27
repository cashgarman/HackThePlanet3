using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace HackThePlanet
{
	/// <summary>Main workstation UI controller: network operations, research, rig editing, and settings.</summary>
	[NoAutoStaticsCleanup]
	public sealed partial class WorkstationApp : MonoBehaviour
	{
		/// <summary>PlayerPrefs key for UI text scale (100–300%).</summary>
		public const string TextSizePreference = "HackThePlanet.TextSize";

		/// <summary>PlayerPrefs key for single-click select without camera framing.</summary>
		public const string SelectWithoutCameraFocusPreference = "HackThePlanet.SelectWithoutCameraFocus";

		/// <summary>PlayerPrefs key for controlled-link highlight color (#RRGGBB).</summary>
		public const string HackedConnectionColorPreference = "HackThePlanet.HackedConnectionColor";

		/// <summary>PlayerPrefs key for muting interface sounds.</summary>
		public const string MutePreference = "HackThePlanet.Mute";

		/// <summary>USS class that applies graph intel-card tooltip styling to an element.</summary>
		public const string IntelTooltipClass = "intel-tooltip";

		// Default controlled-link color used until the player picks one.
		private static readonly Color DefaultHackedConnectionColor = new(.77f, .96f, .42f, 1f);

		// Branch key shown under the research graph. Hover text describes what each route does.
		private static readonly (string branch, string title, string tip)[] ResearchLegend =
		{
			("RECON", "RECON",
			 "Recon: see options before committing. Reveals more topology, forecasts objectives and exploits scanned weaknesses."),
			("INTRUSION", "INTRUSION",
			 "Intrusion: execute a plan efficiently. Faster hacks, longer queues and a shorter main recovery."),
			("STEALTH", "STEALTH",
			 "Stealth: keep room for optional work. Lower trace, quiet relay entry and route recovery."),
			("CONTROL", "CONTROL",
			 "Control: change defenses before advancing. Suppress scanners and hubs to open a brief safe window."),
			("CROSSOVER", "CROSSOVER", "Crossover: requires BOTH connected parent technologies. Combines two branches.")
		};

		// Authored font sizes before text scale is applied to UI labels.
		private readonly Dictionary<TextElement, float> baseTextSizes = new();

		// Saved orbit cameras for the network map and research tree views.
		private readonly GraphCameraState networkCamera = new(), researchCamera = new();

		// Named UI callbacks registered for shortcut and button wiring.
		private readonly Dictionary<string, Action> uiActions = new();

		// Audio source used for interface click and feedback sounds.
		private AudioSource audioSource;

		// Last shell tab id used to avoid rebuilding unchanged views.
		private string builtView;

		// Active GraphExplorer viewport hosting the 3D network or research scene.
		private GraphViewport canvas;

		// Procedural UI sound clips for clicks, success, and warnings.
		private AudioClip clickSound, successSound, warningSound;

		// UI rebuild flag, mute preference, and expanded contract brief state.
		private bool dirty = true, muted, expandedBrief;

		// Whether the shell UI was built and whether it was built during an active op.
		private bool hasBuiltView, builtActive;

		// Last operation report already shown in the results modal.
		private OperationReport lastShownReport;

		// Modal stack state for settings, reset-save flow, and settings error text.
		private string modalBeforeSettings, modalBeforeResetSave = "settings", settingsError;

		// Next time operation resource labels should refresh.
		private float nextResourceRefresh;

		// Operation bar labels for rig power, cooling, compute, bandwidth, storage, and RAM.
		private Label opPower, opCooling, opCompute, opBandwidth, opStorage, opMemory, opReclaim;

		// Full-screen modal overlay root element.
		private VisualElement overlay;

		// Pause state captured before opening help or settings modals.
		private bool pausedBeforeHelp;

		// Returns whether pausedBeforeSettings for the current UI or rig state.
		private bool pausedBeforeSettings;

		// Latest pointer position in panel space for tooltip placement.
		private Vector2 pointerPosition;

		// Operation report queued to open when the modal layer is ready.
		private OperationReport reportToShow;

		// Contract selected in the job browser for preview and connect.
		private Contract selectedJob;

		// Selected network node, research tech, and active modal id.
		private string selectedNode, selectedTech, modal;

		// Whether a gameplay session is running versus title or test mode.
		private bool sessionActive;

		// Simulation speed multiplier and autosave debounce timer.
		private float speed = 1, saveTimer;

		// Floating tooltip label and the element that owns the current tooltip.
		private Label tooltipLabel;

		// Internal member supporting workstation or graph viewport behavior.
		private VisualElement tooltipOwner;

		// Cached trace warning band index for bar coloring.
		private int traceBand;

		// Trace bar fill element and numeric or state labels.
		private VisualElement traceFill;

		// Internal member supporting workstation or graph viewport behavior.
		private Label traceNumber, traceState;

		/// <summary>Gets the live workstation instance created at bootstrap.</summary>
		public static WorkstationApp Instance { get; private set; }

		/// <summary>Gets the gameplay simulation driving this session.</summary>
		public GameSimulation State { get; private set; }

		/// <summary>Gets the active shell tab: network, research, rig, or intel.</summary>
		public string CurrentView { get; private set; } = "network";

		/// <summary>Gets the UI Toolkit root visual element.</summary>
		public VisualElement Root { get; private set; }

		/// <summary>Gets display effect helpers (signal field, glass, motion).</summary>
		public SignalVisuals Visuals { get; private set; }

		/// <summary>Gets or sets whether autosave and PlayerPrefs writes are suppressed for tests.</summary>
		public bool Testing { get; set; }

		/// <summary>Gets the color used for links touching controlled systems on the network map.</summary>
		public static Color HackedConnectionColor { get; private set; } = new(.77f, .96f, .42f, 1f);

		/// <summary>Gets UI text scale as a percentage of authored sizes.</summary>
		public int TextSizePercent { get; private set; } = 100;

		/// <summary>Gets whether single-click selection avoids moving the graph camera.</summary>
		public bool SelectWithoutCameraFocus { get; private set; } = true;

		// Loads the career, builds UI Toolkit, and boots the gameplay simulation.
		private void Awake()
		{
			// Disable the workstation when the build is only validating launch.
			if (GameData.ValidationLaunch)
			{
				enabled = false;
				return;
			}

			// Register this instance and set the frame rate and background behavior.
			Instance = this;
			Application.targetFrameRate = 60;
			Application.runInBackground = true;

			// Prepare to capture any failure while loading the simulation.
			Exception initializationError = null;

			// Load the saved career and create the gameplay simulation.
			try
			{
				State = new GameSimulation(CareerStorage.Load());

				// Pick the active contract, or the first story contract when idle.
				selectedJob = State.Active
					? State.Job
					: State.Catalog.Contracts(State.Career)
						   .FirstOrDefault(c => !State.Career.completed.Contains(c.id) &&
												State.Career.completed.Count >= c.requiredWins) ?? State.Catalog
						.Contracts(State.Career).First(c => !c.recurring);

				// Mark the view dirty and autosave on every simulation change.
				State.Changed += () =>
				{
					dirty = true;

					// Persist the career unless testing.
					if (!Testing)
						CareerStorage.Save(State.Career);
				};

				// Reset the selection state to match the loaded session.
				selectedNode = null;
				sessionActive = State.Active;
				selectedTech = null;
			}
			catch (Exception ex)
			{
				// Keep the error and clear the simulation so the failure screen can show.
				initializationError = ex;
				State = null;
			}

			// Find or create the camera used behind the UI.
			var camera = Camera.main;

			// Create a workstation camera when the scene has none.
			if (camera == null)
				camera = new GameObject("Workstation Camera").AddComponent<Camera>();

			// Make sure the scene has an audio listener.
			if (FindAnyObjectByType<AudioListener>() == null)
				camera.gameObject.AddComponent<AudioListener>();

			// Configure the background camera to draw a solid color and no layers.
			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = Palette.Hex("080E13");
			camera.cullingMask = 0;

			// Configure the UI Toolkit panel scaling and theme.
			var panel = ScriptableObject.CreateInstance<PanelSettings>();
			panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
			panel.referenceResolution = new Vector2Int(1680, 960);
			panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
			panel.match = .5f;
			panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("HackThePlanet/Theme");

			// Attach the UI document and grab its root element.
			var document = gameObject.AddComponent<UIDocument>();
			document.panelSettings = panel;
			Root = document.rootVisualElement;

			// Name and tag the root, and make it focusable for keyboard shortcuts.
			Root.name = "workstation";
			Root.AddToClassList("workstation");
			Root.focusable = true;
			Root.RegisterCallback<KeyDownEvent>(HandleShortcut, TrickleDown.TrickleDown);

			// Load the stylesheets that style the workstation.
			Root.styleSheets.Add(Resources.Load<StyleSheet>("HackThePlanet/Workstation"));
			Root.styleSheets.Add(Resources.Load<StyleSheet>("HackThePlanet/GhostFrequency"));
			Root.styleSheets.Add(Resources.Load<StyleSheet>("HackThePlanet/Readability"));
			Root.styleSheets.Add(Resources.Load<StyleSheet>("HackThePlanet/SignalDeck"));

			// Restore saved preferences for text size, camera focus, and mute.
			TextSizePercent = Mathf.Clamp(PlayerPrefs.GetInt(TextSizePreference, 100), 100, 300);
			SelectWithoutCameraFocus = PlayerPrefs.GetInt(SelectWithoutCameraFocusPreference, 1) != 0;

			// Restore the controlled-link color, keeping the default if the saved value is invalid.
			var hackedColor = PlayerPrefs.GetString(HackedConnectionColorPreference,
													ColorUtility.ToHtmlStringRGBA(DefaultHackedConnectionColor));

			// Apply the saved color when it parses as a valid hex value.
			if (ColorUtility.TryParseHtmlString("#" + hackedColor, out var parsedHackedColor))
				HackedConnectionColor = parsedHackedColor;

			// Restore the mute setting.
			muted = PlayerPrefs.GetInt(MutePreference, 0) != 0;

			// Keep the readable layout in sync with the root size.
			Root.RegisterCallback<GeometryChangedEvent>(_ => UpdateReadableLayout());

			// Track the pointer to position tooltips and find the hovered tooltip owner.
			Root.RegisterCallback<PointerMoveEvent>(evt =>
			{
				pointerPosition = Root.WorldToLocal(evt.position);
				UpdateRigDrag(evt.position);

				// Walk up from the target to the nearest element with a tooltip.
				var target = evt.target as VisualElement;

				// Walk up to the nearest element that has a tooltip.
				while (target != null && target != Root && string.IsNullOrEmpty(target.tooltip)) target = target.parent;

				// Ignore the root and disabled elements.
				if (target == Root || (target != null && !target.enabledInHierarchy))
					target = null;

				// Switch the tooltip owner when the pointer moves to a different element.
				if (target != tooltipOwner)
				{
					HideTooltip();
					tooltipOwner = target;

					// Refresh the tooltip text for the new owner.
					if (target != null)
						RefreshTooltip();
				}
			});

			// End any rig drag on pointer release.
			Root.RegisterCallback<PointerUpEvent>(EndRigDrag, TrickleDown.TrickleDown);

			// Rotate the dragged rig part with the mouse wheel.
			Root.RegisterCallback<WheelEvent>(evt =>
			{
				// Only rotate while a rig part is being dragged.
				if (dragRigId == null || CurrentView != "rig" || modal != null || Mathf.Abs(evt.delta.y) < .01f)
					return;

				// Rotate the dragged part and stop the wheel from scrolling the view.
				RotateRigDrag();
				evt.StopImmediatePropagation();
			}, TrickleDown.TrickleDown);

			// Hide the tooltip on pointer down or when the pointer leaves the workstation.
			Root.RegisterCallback<PointerDownEvent>(_ => HideTooltip(), TrickleDown.TrickleDown);
			Root.RegisterCallback<PointerLeaveEvent>(_ => HideTooltip());

			// Suppress the default system tooltip so the workstation tooltip is used.
			Root.RegisterCallback<TooltipEvent>(evt =>
			{
				evt.tooltip = string.Empty;
				evt.StopImmediatePropagation();
			}, TrickleDown.TrickleDown);

			// Show the initialization failure screen and stop when loading failed.
			if (initializationError != null)
			{
				Text(Root, "GAMEPLAY INITIALIZATION FAILED", "large-heading");
				Text(Root, initializationError.Message, "body-copy amber");
				Debug.LogException(initializationError);
				return;
			}

			// Create the visual effects and the audio source with its sound clips.
			Visuals = new SignalVisuals();
			audioSource = gameObject.AddComponent<AudioSource>();
			audioSource.playOnAwake = false;
			audioSource.volume = .12f;
			clickSound = Tone(640, .045f);
			successSound = Tone(880, .22f);
			warningSound = Tone(210, .15f);

			// Show the save warning in the news feed when storage is unavailable.
			if (CareerStorage.Warning != null)
				State.Career.news.Insert(0, CareerStorage.Warning);

			// Build the first frame of the UI.
			Build();
		}

		// Advances simulation time, refreshes UI, autosaves, and updates tooltips each frame.
		private void Update()
		{
			// Skip the frame until the simulation exists.
			if (State == null)
				return;

			// Advance the simulation by the scaled frame time.
			State.Tick(Time.unscaledDeltaTime * speed);
			var active = State.Active;

			// Clear the selection and camera when the session ends.
			if (sessionActive && !active)
			{
				selectedNode = null;
				networkCamera.Reset();
			}

			// Remember the session state for the next frame.
			sessionActive = active;

			// Compute the trace band: 0 normal, 1 warning, 2 danger.
			var band = active
				? State.Op.trace >= State.TraceDanger ? 2 : State.Op.trace >= State.TraceWarning ? 1 : 0
				: 0;

			// Play a warning sound when the trace band rises, then store the band.
			if (band > traceBand && !muted && audioSource != null)
				audioSource.PlayOneShot(warningSound, band == 2 ? 1 : .5f);
			traceBand = band;

			// Open the report modal once for each newly finished operation.
			if (!State.Active && State.LastReport != null && modal != "report" && State.LastReport != lastShownReport)
			{
				lastShownReport = State.LastReport;
				modal = "report";
				dirty = true;
				Beep(State.LastReport.success);
			}

			// Refresh resource readouts on a timer while the network view is open.
			if (State.Active && Time.unscaledTime >= nextResourceRefresh)
			{
				nextResourceRefresh = Time.unscaledTime + .5f;

				// Refresh the resource actions only on the network view.
				if (CurrentView == "network")
					RefreshResourceActions();
			}

			// Rebuilding mid-drag would call CancelRigDrag and snap a lifted part back into the rack.
			if (dirty && dragRigId == null)
				Build();

			// Refresh the action bar while an operation is running on the network view.
			if (State.Active && CurrentView == "network")
				RefreshActionBar();

			// Keep the tooltip text and position current.
			if (tooltipOwner != null && tooltipLabel != null)
				RefreshTooltip();

			// Update the meters, canvas, and display effects.
			UpdateMeters();
			canvas?.Tick(State, Visuals.Mode);
			Visuals.Tick(State, CurrentView == "research");

			// Sync the motion and trace-alert classes with the current state.
			Root.EnableInClassList("motion-off", !Visuals.Animate);
			Root.EnableInClassList("trace-alert", State.Active && State.Op.trace >= State.TraceWarning);

			// Autosave the career every five seconds while a session is active.
			saveTimer += Time.unscaledDeltaTime;

			// Reset the timer and save once five seconds have passed.
			if (saveTimer >= 5)
			{
				saveTimer = 0;

				// Save only while a session is active and not testing.
				if (State.Active && !Testing)
					CareerStorage.Save(State.Career);
			}
		}

		// Releases UI resources and clears the singleton instance on teardown.
		private void OnDestroy()
		{
			// Release the canvas and visual effects, then clear the static instance.
			canvas?.Dispose();
			Visuals?.Dispose();

			// Clear the static instance if this object still owns it.
			if (Instance == this)
				Instance = null;
		}

		// Autosaves the career when the application pauses in the background.
		private void OnApplicationPause(bool paused)
		{
			// Save the career when the app is paused.
			if (paused && State != null && !Testing)
				CareerStorage.Save(State.Career);
		}

		// Autosaves the career when the player quits the application.
		private void OnApplicationQuit()
		{
			// Save the career when the app quits.
			if (State != null && !Testing)
				CareerStorage.Save(State.Career);
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		// Creates the workstation GameObject and UI document before the first scene loads.
		private static void Bootstrap()
		{
			// Skip creating the workstation when one already exists in the scene.
			if (FindAnyObjectByType<WorkstationApp>() != null)
				return;

			// Create the workstation host object.
			new GameObject("Hack The Planet // Workstation").AddComponent<WorkstationApp>();
		}

		// Synthesizes a short sine-wave clip for procedural UI sounds.
		private static AudioClip Tone(float hz, float seconds)
		{
			// Size the sample buffer for the requested duration.
			var count = (int)(22050 * seconds);
			var data = new float[count];

			// Fill the buffer with a sine tone that fades in and out.
			for (var i = 0; i < count; i++)
			{
				var t = i / 22050f;
				data[i] = Mathf.Sin(t * hz * Mathf.PI * 2) * Mathf.Sin(Mathf.PI * i / count) * .4f;
			}

			// Wrap the samples in an audio clip.
			var clip = AudioClip.Create("terminal", count, 1, 22050, false);
			clip.SetData(data, 0);
			return clip;
		}

		// Plays a click or success UI sound when not muted.
		private void Beep(bool success = false)
		{
			// Play the success or click sound unless muted or audio is unavailable.
			if (!muted && audioSource != null)
				audioSource.PlayOneShot(success ? successSound : clickSound);
		}

		// Routes keyboard shortcuts to simulation actions and UI commands.
		private void HandleShortcut(KeyDownEvent evt)
		{
			// Ignore keys until the simulation exists.
			if (State == null)
				return;

			// Rotate the selected rig part with R while on the rig view with no modal open.
			if (CurrentView == "rig" && modal == null && evt.keyCode == KeyCode.R)
			{
				RotateRigSelection();
				evt.StopImmediatePropagation();
				return;
			}

			// Cancel an in-progress rig drag with Escape.
			if (evt.keyCode == KeyCode.Escape && dragRigId != null)
			{
				CancelRigDrag();
				dirty = true;
				evt.StopImmediatePropagation();
				return;
			}

			// Escape closes the current modal or opens settings.
			if (evt.keyCode == KeyCode.Escape)
			{
				// Close the settings menu and resume.
				if (modal == "settings")
				{
					ResumeFromSettings();
				}

				// Cancel the reset confirmation.
				else if (modal == "reset-save")
				{
					CancelResetSave();
				}

				// Return from the debug menu to settings.
				else if (modal == "debug")
				{
					modal = "settings";
					settingsError = null;
					dirty = true;
				}

				// Close the help guide and restore the pause state.
				else if (modal == "help")
				{
					modal = null;

					// Restore the pause state when the help guide closes.
					if (State.Active)
						State.SetPaused(pausedBeforeHelp);

					// Redraw the view.
					dirty = true;
				}

				// Otherwise open the settings menu.
				else
				{
					ShowSettings();
				}
			}

			// F1 also closes the help guide.
			else if (modal == "help" && evt.keyCode == KeyCode.F1)
			{
				modal = null;

				// Restore the pause state when the help guide closes.
				if (State.Active)
					State.SetPaused(pausedBeforeHelp);

				// Redraw the view.
				dirty = true;
			}

			// Ignore other keys while a modal is open.
			else if (modal != null)
			{
				return;
			}

			// F1 opens the help guide.
			else if (evt.keyCode == KeyCode.F1)
			{
				ShowHelp();
			}

			// Space toggles the manual pause during an active operation.
			else if (evt.keyCode == KeyCode.Space && State.Active)
			{
				State.SetManualPause(!State.Op.paused);
			}

			// Run an ability bound to a shortcut key, preferring the selected node.
			else if (CurrentView == "network" && State.Active)
			{
				var action = State.AvailableActions(selectedNode)
								  .FirstOrDefault(a => a.shortcut != KeyCode.None && a.shortcut == evt.keyCode);

				// Fall back to operator-level actions when no node action matches.
				if (action == null)
					action = State.AvailableActions()
								  .FirstOrDefault(a => a.shortcut != KeyCode.None && a.shortcut == evt.keyCode);

				// Stop when no action uses this key.
				if (action == null)
					return;

				Run(action.id, action.operatorTarget ? null : selectedNode);
			}

			// Let unhandled keys pass through.
			else
			{
				return;
			}

			// Mark the key as handled.
			evt.StopImmediatePropagation();
		}

		// Constructs the full workstation shell and the active tab content.
		private void Build()
		{
			// Reset the cached UI state and clear the old tree before rebuilding.
			CancelRigDrag();
			baseTextSizes.Clear();
			tooltipOwner = null;
			dirty = false;
			canvas?.Dispose();
			uiActions.Clear();
			Root.Clear();
			traceFill = null;
			traceNumber = null;
			traceState = null;
			opPower = opCooling = opCompute = opBandwidth = opStorage = opMemory = opReclaim = null;
			canvas = null;

			// Build the header bar.
			BuildHeader();

			// Create the body containers and the workspace row.
			var body = El(Root, "desktop-body");
			var content = El(body, "desktop-content");
			var workspace = El(content, "workspace-row");

			// Animate the workspace when the view or session state changed.
			var enter = !hasBuiltView || builtView != CurrentView || builtActive != State.Active;
			hasBuiltView = true;
			builtView = CurrentView;
			builtActive = State.Active;

			// Animate the workspace when the view or session changed.
			if (enter && Visuals.Animate)
			{
				workspace.AddToClassList("entering");
				workspace.schedule.Execute(() => workspace.RemoveFromClassList("entering")).ExecuteLater(16);
			}

			// Build the workspace for the active shell tab.
			if (CurrentView == "network")
				BuildNetwork(workspace);
			else if (CurrentView == "research")
				BuildResearch(workspace);
			else if (CurrentView == "rig")
				BuildRig(workspace);
			else
				BuildIntel(workspace);

			// Build the footer with the connection status and save state.
			var footer = El(Root, "footer");
			Text(footer, "GHOST FREQUENCY / HTP TERMINAL 1.0", "footer-brand");
			Text(footer,
				 State.Active
					 ? "ENCRYPTED SESSION  /  " + (State.Op.paused ? "PAUSED" : "LIVE")
					 : "LOCAL SESSION  /  PRIVATE GATEWAY", "footer-center");
			Text(footer, CareerStorage.Warning != null ? "SAVE UNAVAILABLE" : "AUTOSAVE ACTIVE  /  LOCAL STORAGE",
				 "footer-save");

			// Disable the underlying UI while a settings or confirmation modal is open.
			if (modal == "settings" || modal == "reset-save" || modal == "debug" || modal == "refund-research")
				foreach (var child in Root.Children())
					child.SetEnabled(false);

			// Build the open modal, if any.
			if (modal == "refund-research")
				BuildRefundModal();
			else if (modal != null)
				BuildModal();

			// Apply the glass overlay and motion setting to the root.
			Visuals.AttachGlass(Root);
			Root.EnableInClassList("motion-off", !Visuals.Animate);

			// Create the tooltip label that follows the pointer.
			tooltipLabel = Text(Root, "", "readable-tooltip");
			tooltipLabel.enableRichText = true;
			tooltipLabel.pickingMode = PickingMode.Ignore;
			tooltipLabel.RegisterCallback<GeometryChangedEvent>(_ => PositionTooltip());

			// Snapshot authored fonts after USS resolves, before assigning any scaled
			// fonts. This includes inline graph fonts and inherited control text.
			Root.Q(className: "header").RegisterCallbackOnce<GeometryChangedEvent>(_ =>
			{
				foreach (var text in Root.Query<TextElement>().ToList())
					baseTextSizes[text] = text.resolvedStyle.fontSize;
				ApplyTextSize();
			});

			// Refresh the layout classes for the current root size.
			UpdateReadableLayout();

			// Pick the primary button of the open modal to receive focus.
			var modalFocus = modal == "settings" ? Root.Q<Button>("resume") :
				modal == "reset-save" ? Root.Q<Button>("cancel-reset") :
				modal == "debug" ? Root.Q<Button>("debug-back") : null;

			// Focus the modal button, or the root when no modal is open.
			if (modalFocus != null)
				modalFocus.Focus();
			else
				Root.Focus();
		}

		// Builds the top header with career stats, speed controls, and settings entry.
		private void BuildHeader()
		{
			// Build the header container and the logo block.
			var header = El(Root, "header");
			var summary = El(header, "header-summary");
			var logo = El(summary, "logo");
			logo.Add(new DeviceGlyph(NodeKind.Gateway, Palette.Lime, 42));
			var brand = El(logo, "brand");
			Text(brand, "HACK THE PLANET", "brand-name");
			Text(brand, "GHOST FREQUENCY  /  INDEPENDENT OPERATOR", "brand-sub");

			// Show the credits and research XP wallet.
			var wallet = El(summary, "wallet");
			var credits = El(wallet, "wallet-cell");
			Text(credits, "CREDITS", "eyebrow");
			Text(credits, State.Career.credits.ToString("N0") + " cr", "credit-amount");
			var xp = El(wallet, "wallet-cell");
			Text(xp, "RESEARCH XP", "eyebrow");
			Text(xp, State.Career.xp.ToString("N0") + " XP", "xp-amount");

			// Show the operator identity and the current day and rank.
			var identity = El(summary, "identity");
			Text(identity, "OP / ZERO", "identity-name");
			Text(identity, "DAY " + State.Career.day.ToString("00") + "  /  " + Rank(), "identity-day");

			// Add the help and settings buttons.
			var utilities = El(summary, "header-tools");
			RailCircleButton(utilities, "?", ShowHelp, "help", "header-tool").tooltip = "Field guide / F1";
			RailCircleButton(utilities, "⚙", ShowSettings, "settings", "header-tool").tooltip = "Settings / Escape";

			// Add the navigation tabs for each shell view.
			var tabs = El(header, "header-tabs");
			Tab(tabs, "network", "OPERATIONS");
			Tab(tabs, "research", "RESEARCH");
			Tab(tabs, "rig", "YOUR RIG");
			Tab(tabs, "intel", "INTELLIGENCE");
		}

		/// <summary>Sets UI text scale and persists it unless <see cref="Testing" /> is true.</summary>
		/// <param name="percent">Scale from 100 to 300.</param>
		public void SetTextSize(int percent)
		{
			// Clamp the requested scale to the supported range.
			TextSizePercent = Mathf.Clamp(percent, 100, 300);

			// Persist the scale unless testing.
			if (!Testing)
			{
				PlayerPrefs.SetInt(TextSizePreference, TextSizePercent);
				PlayerPrefs.Save();
			}

			// Update the settings label and slider to show the new scale.
			var value = Root.Q<Label>("text-size-value");

			// Update the label text when it exists.
			if (value != null)
				value.text = "TEXT SIZE / " + TextSizePercent + "%";

			// Sync the slider with the new scale.
			Root.Q<SliderInt>("text-size")?.SetValueWithoutNotify(TextSizePercent);

			// Apply the new scale to all text.
			ApplyTextSize();
		}

		/// <summary>Sets graph single-click inspect behavior and persists it unless testing.</summary>
		/// <param name="value">When true, single-click does not frame the camera.</param>
		public void SetSelectWithoutCameraFocus(bool value)
		{
			// Store the camera focus setting.
			SelectWithoutCameraFocus = value;

			// Persist the setting unless testing.
			if (!Testing)
			{
				PlayerPrefs.SetInt(SelectWithoutCameraFocusPreference, value ? 1 : 0);
				PlayerPrefs.Save();
			}
		}

		/// <summary>Sets the controlled-link color and refreshes the network map.</summary>
		/// <param name="value">Opaque RGB color; alpha is forced to 1.</param>
		public void SetHackedConnectionColor(Color value)
		{
			// Force the color to be opaque and store it.
			value.a = 1f;
			HackedConnectionColor = value;

			// Persist the color unless testing.
			if (!Testing)
			{
				PlayerPrefs.SetString(HackedConnectionColorPreference, ColorUtility.ToHtmlStringRGBA(value));
				PlayerPrefs.Save();
			}

			// Recolor the links on the network map.
			canvas?.RefreshConnectionColors();
		}

		// Stores mute preference and updates the audio source volume.
		private void SetMuted(bool value)
		{
			// Store the mute setting.
			muted = value;

			// Persist the setting unless testing.
			if (!Testing)
			{
				PlayerPrefs.SetInt(MutePreference, value ? 1 : 0);
				PlayerPrefs.Save();
			}

			// Mark the view dirty so the settings toggle is redrawn.
			dirty = true;
		}

		// Adds FX toggles for signal field, glass, and animation to a settings panel.
		private void BuildDisplayEffectToggles(VisualElement parent)
		{
			// Create the signal field toggle and sync it with the current setting.
			var fieldToggle = new Toggle("Signal field")
				{ name = "effect-signal-field", tooltip = "Procedural background on network and research maps." };
			fieldToggle.SetValueWithoutNotify(Visuals.SignalFieldEnabled);

			// Apply the signal field change, redraw, and click.
			fieldToggle.RegisterValueChangedCallback(evt =>
			{
				Visuals.SetSignalFieldEnabled(evt.newValue, !Testing);
				dirty = true;
				Beep();
			});
			parent.Add(fieldToggle);

			// Create the phosphor glass toggle and sync it with the current setting.
			var glassToggle = new Toggle("Phosphor glass")
				{ name = "effect-phosphor-glass", tooltip = "Screen-wide glass treatment over the workstation UI." };
			glassToggle.SetValueWithoutNotify(Visuals.PhosphorGlassEnabled);

			// Apply the phosphor glass change, redraw, and click.
			glassToggle.RegisterValueChangedCallback(evt =>
			{
				Visuals.SetPhosphorGlassEnabled(evt.newValue, !Testing);
				dirty = true;
				Beep();
			});
			parent.Add(glassToggle);

			// Create the animation toggle and sync it with the current setting.
			var animateToggle = new Toggle("Animate display effects")
				{ name = "effect-animate", tooltip = "When off, enabled layers stay visible but motion freezes." };
			animateToggle.SetValueWithoutNotify(Visuals.AnimateDisplayEffects);

			// Apply the animation change, redraw, and click.
			animateToggle.RegisterValueChangedCallback(evt =>
			{
				Visuals.SetAnimateDisplayEffects(evt.newValue, !Testing);
				dirty = true;
				Beep();
			});
			parent.Add(animateToggle);
		}

		// Scales registered UI text elements from saved text size preference.
		private void ApplyTextSize()
		{
			// Convert the percent setting into a scale factor.
			var scale = TextSizePercent / 100f;

			// Scale every snapshotted text element from its authored size.
			foreach (var pair in baseTextSizes)
				pair.Key.style.fontSize = pair.Value * scale;

			// Scale the tooltip label.
			if (tooltipLabel != null)
				tooltipLabel.style.fontSize = 12 * scale;

			// Scale the graph canvas text.
			canvas?.SetTextScale(scale);

			// Refresh the layout and hide the tooltip so it reopens at the new size.
			UpdateReadableLayout();
			HideTooltip();
		}

		// Reflows panels that depend on text scale and viewport size.
		private void UpdateReadableLayout()
		{
			// Font size stays independent of viewport size; the layout absorbs growth.
			Root.EnableInClassList("large-text", TextSizePercent > 150);
			Root.EnableInClassList("compact-width", Root.contentRect.width > 0 && Root.contentRect.width < 1400);
		}

		// Handles HideTooltip for the workstation or graph UI.
		private void HideTooltip()
		{
			// Clear the tooltip owner and hide the label.
			tooltipOwner = null;

			// Hide the label when it exists.
			if (tooltipLabel != null)
				tooltipLabel.style.display = DisplayStyle.None;
		}

		// Handles RefreshTooltip for the workstation or graph UI.
		private void RefreshTooltip()
		{
			// Nothing to show without an owner and a label.
			if (tooltipOwner == null || tooltipLabel == null)
				return;

			// Update the text and intel styling from the owner.
			tooltipLabel.text = GameplayRichText.Format(tooltipOwner.tooltip);
			tooltipLabel.EnableInClassList(IntelTooltipClass, tooltipOwner.ClassListContains(IntelTooltipClass));

			// Show the label and place it near the pointer.
			tooltipLabel.style.display = DisplayStyle.Flex;
			PositionTooltip();
		}

		// Handles PositionTooltip for the workstation or graph UI.
		private void PositionTooltip()
		{
			// Skip positioning when there is no visible tooltip.
			if (tooltipLabel == null || tooltipOwner == null ||
				tooltipLabel.resolvedStyle.display == DisplayStyle.None)
				return;

			// Offset from the pointer and clamp the label inside the workstation.
			tooltipLabel.style.left = Mathf.Clamp(pointerPosition.x + 14, 0,
												  Mathf.Max(0, Root.contentRect.width - tooltipLabel.layout.width - 8));
			tooltipLabel.style.top = Mathf.Clamp(pointerPosition.y + 20, 0,
												 Mathf.Max(0, Root.contentRect.height - tooltipLabel.layout.height -
															  8));
		}

		// Builds the Rail section of the workstation UI.
		private void BuildRail(VisualElement body)
		{
			// Build the rail with a spacer that pushes the utility buttons to the bottom.
			var rail = El(body, "rail");
			El(rail, "flex-spacer");

			// Add the settings and help buttons.
			RailCircleButton(rail, "\u2699", ShowSettings, "settings", "rail-help settings-button").tooltip =
				"Settings / Escape";
			RailCircleButton(rail, "?", ShowHelp, "help", "rail-help").tooltip = "Field guide / F1";
		}

		// Handles Tab for the workstation or graph UI.
		private void Tab(VisualElement parent, string target, string label)
		{
			// Create the tab button and mark it active when it matches the current view.
			var b = Btn(parent, label, () => Navigate(target), "tab-" + target, "tab");

			// Highlight the tab for the current view.
			if (CurrentView == target)
				b.AddToClassList("active");
		}

		/// <summary>Switches shell tabs and pauses an active operation when leaving the network view.</summary>
		/// <param name="target">View id: network, research, rig, or intel.</param>
		public void Navigate(string target)
		{
			// Pause the operation when leaving the network view.
			if (State.Active && target != "network")
				State.SetPaused(true);

			// Switch the view and close any modal.
			CurrentView = target;
			modal = null;
			dirty = true;

			// Play the click sound.
			Beep();
		}

		// Builds the Network section of the workstation UI.
		private void BuildNetwork(VisualElement workspace)
		{
			// Create the left panel and its heading.
			var left = Scroll(workspace, "left-panel");
			var lh = El(left, "panel-heading");
			Text(lh, State.Active ? "ACTIVE CONTRACT" : "CONTRACTS", "large-heading panel-title");

			// List every contract when idle, or show the active brief during a run.
			if (!State.Active)
			{
				var list = El(left, "contract-list");

				// Create one card for each contract.
				foreach (var job in State.Catalog.Contracts(State.Career))
				{
					// Work out whether the contract is delivered or still locked.
					bool done = State.Career.completed.Contains(job.id),
						 locked = State.Career.completed.Count < job.requiredWins;

					// Create the contract card that selects this job when clicked.
					var card = Btn(list, "", () =>
					{
						selectedJob = job;
						selectedNode = null;
						networkCamera.Reset();
						dirty = true;
						Beep();
					}, "contract-" + job.id, "contract-card");

					// Mark the selected or locked card.
					if (selectedJob.id == job.id)
						card.AddToClassList("active");

					// Dim locked contract cards.
					if (locked)
						card.AddToClassList("locked");

					// Show the contract status and its number or recurring tag.
					var meta = El(card, "row-between");
					Text(meta, done ? "DELIVERED" : locked ? "ENCRYPTED" : "AVAILABLE",
						 "contract-state" + (done ? " lime" : locked ? " muted" : " cyan"));
					Text(meta, job.recurring
							 ? "R"
							 : "0" + (State.Catalog.Contracts(State.Career).Where(c => !c.recurring).Select(c => c.id)
										   .ToList().IndexOf(job.id) + 1), "micro muted");

					// Show the contract title, client, and difficulty with intel level.
					Text(card, job.title, "contract-title");
					Text(card, job.company, "micro muted");
					Text(card, job.DifficultyLabel + " / " + (job.Intel == ContractIntel.Full ? "FULL INTEL" :
							 job.Intel == ContractIntel.Topology ? "TOPOLOGY ONLY" : "NO INTEL"),
						 "contract-difficulty micro " + (job.Intel == ContractIntel.None ? "amber" : "cyan"));

					// Show the credit and XP reward.
					var reward = El(card, "contract-reward");
					Text(reward, job.credits.ToString("N0") + " cr", "small lime");
					Text(reward, "+" + job.xp + " XP", "small muted");
				}
			}
			else
			{
				// Show the active contract brief: title, client, objectives, and rewards.
				var brief = El(left, "active-brief");
				Text(brief, State.Job.tag, "micro cyan");
				Text(brief, State.Job.title, "mission-title");
				Text(brief, State.Job.client, "micro muted");

				// Add the toggle that expands or collapses the full description.
				Btn(brief, expandedBrief ? "HIDE BRIEF ▴" : "READ BRIEF ▾", () =>
				{
					expandedBrief = !expandedBrief;
					dirty = true;
				}, "toggle-brief", "brief-toggle");

				// Show the full description when expanded.
				if (expandedBrief)
					RichText(brief, State.Job.description, "body-copy");

				// List each objective node and mark the ones already secured.
				SectionTitle(brief, "OBJECTIVES");

				// Add a row for each objective node.
				foreach (var n in State.Op.nodes.Where(n => n.objective))
				{
					var b = Btn(brief, (State.ObjectiveDone(n) ? "[+] " : "[ ] ") + n.label, () => SelectNode(n.id),
								"objective-" + n.id, "objective-row");

					// Mark secured objectives in lime.
					if (State.ObjectiveDone(n))
						b.AddToClassList("lime");
				}

				// Show the reward for delivery and any reward conditions.
				SectionTitle(brief, "REWARD CONDITIONS");
				Text(brief, State.Job.credits + " cr + " + State.Job.xp + " XP on delivery", "small lime");
				RichText(brief, State.RewardDescription, "small muted");
			}

			// Build the loadout list of equipped abilities.
			var loadout = El(left, "loadout");
			Text(loadout, "EQUIPPED ABILITIES  /  " + State.Career.equipped.Count + " OF " + State.Slots, "eyebrow");

			// Show a hint when no abilities are equipped.
			if (State.Career.equipped.Count == 0)
				Text(loadout, "Equip abilities in Research between contracts.", "small muted");

			// Create a button for each equipped ability.
			foreach (var id in State.Career.equipped)
			{
				// Each ability opens its research entry when idle.
				var tech = FindTech(id);
				var entry = Btn(loadout, tech.title + " / " + (State.Active ? "IN ACTION BAR" : "EQUIPPED"), () =>
				{
					if (!State.Active)
					{
						selectedTech = id;
						Navigate("research");
					}
				}, "ability-" + id, "ability-button");
				entry.SetEnabled(!State.Active);
			}

			// Create the central map panel and its heading.
			var mid = El(workspace, "network-panel");
			var map = El(mid, "map-content");
			var mh = El(map, "map-heading");
			var title = El(mh, "map-title-block");

			// Choose the intel level for the map: full while active, otherwise the contract's.
			var intel = State.Active ? ContractIntel.Full : selectedJob.Intel;
			Text(title, State.Active ? "LIVE NETWORK" : "NETWORK INTELLIGENCE", "eyebrow cyan");
			Text(title, (State.Active ? State.Job : selectedJob).company, "map-title");

			// Build the status line for the map heading.
			// Show the connection or contract status line.
			var state = El(mh, "map-status");
			Text(state,
				 State.Active
					 ? State.Op.paused ? "||  SIMULATION PAUSED" : "●  ENCRYPTED CONNECTION"
					 : "○  " + selectedJob.DifficultyLabel + " / " + (intel == ContractIntel.Full ? "FULL INTEL" :
						 intel == ContractIntel.Topology ? "TOPOLOGY ONLY" : "NO INTEL"),
				 "micro " + (State.Active ? "lime" : "muted"));

			// Show how many systems have been discovered during an active run.
			if (State.Active)
				Text(state, State.Op.nodes.Count(n => n.known) + " / " + State.Op.nodes.Count + " SYSTEMS DISCOVERED",
					 "micro muted");

			// Add the trace meter during an active run.
			if (State.Active)
				BuildTraceBar(map);

			// Build the graph, or a blocked panel when no intel is available.
			var nodes = new List<NetNode>();

			// Pick the graph or the blocked view by intel level.
			if (intel == ContractIntel.None)
			{
				// Show a blocked view with no network data.
				var blocked = El(map, "graph-viewport no-intel-view");
				blocked.name = "no-intel-view";
				Visuals.AttachField(blocked, true);
				var message = El(blocked, "no-intel-message");
				Text(message, "NO INTEL AVAILABLE", "no-intel-title");
			}
			else
			{
				// Gather the nodes, links, route, and entry for the graph.
				var preview = State.Active ? null : NetworkGenerator.Generate(selectedJob, State.Career, State.Catalog);
				nodes = State.Active ? State.Op.nodes : preview.nodes;
				var links = State.Active ? State.Op.links : preview.links;
				var route = State.Active ? State.Route(selectedNode, true) : null;
				var job = State.Active ? State.Job : selectedJob;
				var entryId = State.Active ? State.Op.entryNode : State.Catalog.Network(job.networkId).entryNode;

				// Find the objective node to highlight, if the intel allows it.
				var objectiveId = job.Intel != ContractIntel.None
					? nodes.FirstOrDefault(n => n.objective && !n.collected)?.id
					: null;

				// Host the graph and the overlaid task queue strip in one stack.
				var graphHost = El(map, "map-graph-host");
				canvas = new GraphViewport(nodes, links, selectedNode, !State.Active, SelectNode, route, networkCamera,
										   intel, job.Intel, State.Catalog, entryId, objectiveId, State.Waypoints(job),
										   State.WaypointTier);
				graphHost.Add(canvas);
				Visuals.AttachField(canvas);
				Visuals.AttachIntelCard(canvas.IntelStatic);

				// Overlay the queued-task strip on the live network map during a run.
				if (State.Active)
					BuildGraphQueueStrip(graphHost);
			}

			// Add the action and operation bars during an active run.
			if (State.Active)
				BuildActionBar(map);

			// Add the operation bar during an active run.
			if (State.Active)
				BuildOperationBar(map);

			// Build the legend from the node types visible on the map.
			var mapFooter = El(map, "map-footer");
			var legend = El(mapFooter, "legend");

			// Add one legend entry for each visible node type.
			foreach (var kind in State.Catalog.NodeTypes.Where(t => intel == ContractIntel.Full &&
																	nodes.Any(n => n.typeId == t.id) &&
																	t.glyph != NodeKind.Gateway))
			{
				var key = El(legend, "legend-key");
				var dot = El(key, "legend-dot");
				dot.style.backgroundColor = kind.color;
				Text(key, kind.title, "micro muted");
			}

			// Explain what the player can see when intel is partial or missing.
			if (intel != ContractIntel.Full)
				Text(legend,
					 intel == ContractIntel.Topology
						 ? "UNKNOWN SYSTEMS / CONNECTIONS ONLY"
						 : "NETWORK SIGNAL UNAVAILABLE", "micro muted");

			// Add the zoom and fit controls when the graph is visible.
			if (intel != ContractIntel.None)
			{
				var controls = El(mapFooter, "map-controls");
				Btn(controls, "−", () => canvas.Zoom(1.15f), "zoom-out", "map-button");
				Btn(controls, "+", () => canvas.Zoom(.85f), "zoom-in", "map-button");
				Btn(controls, "FIT", () => canvas.ResetView(), "fit", "map-button");
			}

			// Show the target inspector during a run, or the contract brief otherwise.
			var right = El(workspace, "right-panel");

			// Choose the inspector for the current state.
			if (State.Active)
				BuildTarget(right, State.Node(selectedNode));
			else
				BuildBrief(right);
		}

		/// <summary>Selects a network node and updates the graph camera focus id.</summary>
		/// <param name="id">Node id, or null to clear selection.</param>
		public void SelectNode(string id)
		{
			// Store the selection and focus the camera on it.
			selectedNode = id;
			networkCamera.FocusedId = string.IsNullOrEmpty(id) ? null : id;

			// Redraw the view and play the click sound.
			dirty = true;
			Beep();
		}

		/// <summary>Selects a technology on the research graph.</summary>
		/// <param name="id">Technology id, or null to clear selection.</param>
		public void SelectTech(string id)
		{
			// Store the selection and focus the research camera on it.
			selectedTech = id;
			researchCamera.FocusedId = string.IsNullOrEmpty(id) ? null : id;

			// Redraw the view and play the click sound.
			dirty = true;
			Beep();
		}

		// Builds the Brief section of the workstation UI.
		private void BuildBrief(VisualElement right)
		{
			// Create the scrolling brief panel.
			var scroll = new ScrollView(ScrollViewMode.Vertical);
			scroll.AddToClassList("inspector-scroll");
			scroll.AddToClassList("contract-brief");
			right.Add(scroll);

			// Show the contract title, client, and description.
			PanelTitle(scroll, "CONTRACT BRIEF");
			Text(scroll, selectedJob.tag, "eyebrow cyan section-space");
			Text(scroll, selectedJob.title, "mission-title");
			Text(scroll, selectedJob.client, "eyebrow muted");
			RichText(scroll, selectedJob.description, "body-copy");

			// Show the difficulty, intel level, and the objective.
			Text(scroll, selectedJob.DifficultyLabel + " / NETWORK INTEL", "eyebrow section-space");
			RichText(scroll, selectedJob.IntelDescription, "body-copy");
			Text(scroll, "THE ASK", "eyebrow section-space");
			RichText(scroll, selectedJob.objective, "body-copy");

			// Add the storage and waypoint sections.
			BuildStorageBrief(scroll);
			BuildWaypointPlanner(scroll);

			// Show the payout and the reward conditions.
			Text(scroll, "ESCROW PAYOUT", "eyebrow section-space");
			Text(scroll, selectedJob.credits.ToString("N0") + " cr  /  " + selectedJob.xp + " XP", "payout");
			Text(scroll,
				 selectedJob.RewardMultiplier.ToString("0.#") + "× " + selectedJob.DifficultyLabel +
				 " PAYOUT / CREDITS + XP", "micro cyan");
			RichText(scroll, selectedJob.condition, "small muted");

			// Show the optional archives and a note based on the career progress.
			Text(scroll, "OPTIONAL ARCHIVES", "eyebrow section-space");
			RichText(scroll, State.ArchiveDescription, "small muted");
			Text(scroll, "OPERATOR NOTE", "eyebrow section-space");
			RichText(scroll,
					 State.Career.jobs == 0
						 ? "Your gateway is already controlled. Hack a connected relay to get inside, or scan it to discover more routes. Pause whenever you need to think."
						 : "Scanners add pressure while you work near them. A captured hub weakens the systems it protects. Your old successes may have changed these defenses.",
					 "body-copy note");

			// Create the bottom bar with the connect button.
			var bottom = El(right, "inspector-bottom");

			// Work out whether the contract is delivered, locked, or blocked by the rig.
			bool done = State.Career.completed.Contains(selectedJob.id),
				 locked = State.Career.completed.Count < selectedJob.requiredWins,
				 rigBlocked = !State.RigValid;

			// Create the connect button, enabled only when the contract can start.
			var start = Btn(
				bottom, done ? "CONTRACT DELIVERED" : locked ? "COMPLETE EARLIER CONTRACTS" : "CONNECT TO NETWORK  →",
				StartSelected, "connect", "primary-button");
			start.SetEnabled(!done && !locked && !rigBlocked);

			// Explain why the rig blocks connecting.
			if (rigBlocked)
				Text(bottom, string.Join("\n", State.RigProblems), "small connect-block-warning center-text");
		}

		// Builds the WaypointPlanner section of the workstation UI.
		private void BuildWaypointPlanner(VisualElement parent)
		{
			// Read the waypoint slots unlocked by research.
			var tier = State.WaypointTier;
			Text(parent, "WAYPOINTS / " + tier + " SLOTS", "eyebrow section-space");

			// Explain how to unlock waypoints when none are available.
			if (tier == 0)
			{
				Text(parent, "Research Waypoints I in RECON to mark systems before connecting.", "small muted");
				return;
			}

			// Waypoints need network intel to be placed.
			if (selectedJob.Intel == ContractIntel.None)
			{
				RichText(parent, "No network intel: waypoint placement is unavailable for this contract.",
						 "small muted");
				return;
			}

			// Generate the network and find the current waypoints and selection.
			var network = NetworkGenerator.Generate(selectedJob, State.Career, State.Catalog);
			var points = State.Waypoints(selectedJob);
			var selected = network.nodes.FirstOrDefault(n => n.id == selectedNode);

			// Waypoints can only be changed while the contract is open.
			var canPlan = !State.Career.completed.Contains(selectedJob.id) &&
						  State.Career.completed.Count >= selectedJob.requiredWins;

			// Explain how to place a waypoint.
			Text(parent,
				 "Select a system on the map, then place a marker. Markers stay visible during the run, even before discovery.",
				 "small muted");

			// Add the button that places a waypoint on the selected system.
			var add = Btn(parent, "PLACE ON SELECTED SYSTEM (" + points.Count + " / " + tier + ")",
						  () => State.PlaceWaypoint(selectedJob, selectedNode), "place-waypoint", "secondary-button");
			add.SetEnabled(canPlan && selected != null && points.Count < tier &&
						   !points.Any(w => w.nodeId == selectedNode));

			// List each placed waypoint with its controls.
			foreach (var waypoint in points)
			{
				// Number the waypoint and create its row.
				var number = points.ToList().IndexOf(waypoint) + 1;
				var row = El(parent, "section-space");

				// Show the system name, or hide it when intel is partial.
				var heading = Text(row, "W" + number + " / " + (selectedJob.Intel == ContractIntel.Full
									   ? network.nodes.First(n => n.id == waypoint.nodeId).label
									   : "UNKNOWN SYSTEM"), "small");
				heading.style.color = GameSimulation.WaypointColors[tier >= 2 ? waypoint.color : 0];

				// Add the button that centers the camera on this waypoint.
				Btn(row, "LOCATE", () => SelectNode(waypoint.nodeId), "locate-waypoint-" + number, "secondary-button");

				// Add the color swatches at tier 2 and above.
				if (tier >= 2)
				{
					var colors = El(row, "row-between");
					string[] names = { "CYAN", "AMBER", "VIOLET", "PINK", "WHITE" };

					// Create one color swatch for each waypoint color.
					for (var i = 0; i < names.Length; i++)
					{
						// Create one swatch for each waypoint color.
						var color = i;
						var button = Btn(colors, names[i].Substring(0, 1),
										 () => State.EditWaypoint(selectedJob, waypoint.nodeId, color, waypoint.label,
																  waypoint.showPath),
										 "waypoint-" + number + "-color-" + i, "map-button");
						button.tooltip = names[i];
						button.style.color = GameSimulation.WaypointColors[i];
						button.SetEnabled(canPlan);
					}
				}

				// Add the label field at tier 3 and above.
				if (tier >= 3)
				{
					var label = new TextField("Label (40 characters)")
						{ name = "waypoint-label-" + number, value = waypoint.label, maxLength = 40, isDelayed = true };
					label.SetEnabled(canPlan);
					label.RegisterValueChangedCallback(evt => State.EditWaypoint(
														   selectedJob, waypoint.nodeId, waypoint.color, evt.newValue,
														   waypoint.showPath));
					row.Add(label);
				}

				// Add the gateway route toggle at tier 4 and above.
				if (tier >= 4)
				{
					var route = Btn(row, waypoint.showPath ? "HIDE GATEWAY ROUTE" : "SHOW SHORTEST GATEWAY ROUTE",
									() => State.EditWaypoint(selectedJob, waypoint.nodeId, waypoint.color,
															 waypoint.label, !waypoint.showPath),
									"waypoint-route-" + number, "secondary-button");
					route.SetEnabled(canPlan);
				}

				// Add the button that removes this waypoint.
				Btn(row, "REMOVE", () => State.RemoveWaypoint(selectedJob, waypoint.nodeId),
					"remove-waypoint-" + number, "secondary-button").SetEnabled(canPlan);
			}

			// Explain the single-route limit at tier 4.
			if (tier >= 4)
				Text(parent, "One route at a time, from the gateway through discovered and undiscovered systems.",
					 "small muted");
		}

		/// <summary>Connects to the contract currently selected in the exchange list.</summary>
		public void StartSelected()
		{
			// Start the selected contract when the simulation accepts it.
			if (State.Start(selectedJob))
			{
				// Return to the network view with a fresh camera.
				selectedNode = null;
				CurrentView = "network";
				networkCamera.Reset();

				// Play the click sound and redraw.
				Beep();
				dirty = true;
			}
		}

		// Builds the Target section of the workstation UI.
		private void BuildTarget(VisualElement right, NetNode node)
		{
			// Create the scrolling inspector panel.
			var scroll = new ScrollView(ScrollViewMode.Vertical);
			scroll.AddToClassList("inspector-scroll");
			right.Add(scroll);
			PanelTitle(scroll, "TARGET INSPECTOR");

			// Nothing to inspect until a node is selected.
			if (node == null)
				return;

			// Show a limited view for an undiscovered system, with only a disconnect button.
			if (!node.known)
			{
				Text(scroll, "UNDISCOVERED SYSTEM", "mission-title");
				Text(scroll,
					 "Scan a reachable system to discover its neighbors. Blueprint research reveals objective regions at the start of a contract.",
					 "body-copy muted");
				var exit = El(right, "inspector-bottom");
				Btn(exit, "DISCONNECT", DisconnectClicked, "disconnect", "danger-button");
				return;
			}

			// Show the node type, its glyph, and its access status.
			var nodeType = State.Catalog.NodeTypes.First(t => t.id == node.typeId);
			var type = El(scroll, "target-type");
			type.Add(new DeviceGlyph(node.kind, nodeType.color, 42));
			var status = El(type, "target-status");
			Text(status, nodeType.title.ToUpperInvariant(), "micro cyan");
			Text(status,
				 node.controlled ? "ACCESS ESTABLISHED" : State.Reachable(node) ? "WITHIN REACH" : "ROUTE REQUIRED",
				 "micro " + (node.controlled ? "lime" : "muted"));

			// Show the system name and address.
			Text(scroll, node.label, "target-title");
			Text(scroll, "ADDR / " + node.id.ToUpperInvariant(), "micro muted");

			// Show the effective security rating and a bar for each level.
			var stats = El(scroll, "target-stats");
			Text(stats, "SECURITY", "eyebrow");
			Text(stats, State.EffectiveSecurity(node).ToString("00") + " / 10", "small");
			var bars = El(scroll, "security-bars");

			// Create one bar for each security level.
			for (var i = 0; i < 10; i++)
			{
				// Color each bar up to the effective security level.
				var bar = El(bars, "security-bar");

				// Fill this bar when it is within the security level.
				if (i < State.EffectiveSecurity(node))
					bar.style.backgroundColor = nodeType.color;
			}

			// Show objective, bonus, suppression, and scan status tags.
			if (node.objective)
				Text(scroll, State.ObjectiveDone(node) ? "[+] CONTRACT OBJECTIVE SECURED" : "[*] CONTRACT OBJECTIVE",
					 "objective-tag");

			// Show the bonus archive status.
			if (node.bonus)
				Text(scroll,
					 node.collected
						 ? "[+] OPTIONAL FILES SECURED"
						 : "[+] OPTIONAL ARCHIVE / " + State.ArchiveDescription, "micro lime section-space");

			// Show the suppression status when a node is suppressed.
			if (node.suppressed > 0)
				Text(scroll,
					 State.Op.reservations.Any(r => r.target == node.id)
						 ? "SUPPRESSED / RAM RESERVED"
						 : "SUPPRESSED / " + Mathf.CeilToInt(node.suppressed) + "s remaining",
					 "micro lime section-space");

			// Show that the intel is verified when the node has been scanned.
			if (node.scanned)
				Text(scroll, "INTEL VERIFIED / INTRUSION PREPARED", "micro cyan section-space");

			// Show the research hint and the storage size when the node has files.
			var researchHint = State.ActionResearchHint(node.id);

			// Show the research hint when one applies.
			if (researchHint != null)
				Text(scroll, researchHint, "small muted section-space");

			// Show the storage size for nodes that hold files.
			if (node.storage > 0)
				RichText(scroll, "FILE SIZE / " + node.storage.ToString("0") + " GB STORAGE", "small cyan");

			// Show the node description, falling back to the node type description.
			var description = string.IsNullOrWhiteSpace(node.detail) ? nodeType.description : node.detail;

			// Show the description when it has text.
			if (!string.IsNullOrWhiteSpace(description))
				Text(scroll, description, "body-copy muted");

			// Show the recovered lore fragment for a collected archive.
			if (node.bonus && node.collected)
			{
				var fragment = LoreContent.Find(State.Job, node.loreId);

				// Show the fragment when it is found.
				if (fragment != null)
				{
					Text(scroll, fragment.title, "eyebrow lime section-space");
					Text(scroll, fragment.text, "body-copy");
					Text(scroll, "Extract to preserve this fragment.", "small muted");
				}
			}

			// Show the access route from the gateway to this system.
			var route = State.Route(node.id, true);
			Text(scroll, "ACCESS ROUTE", "eyebrow section-space");
			Text(scroll,
				 route != null && route.IsReachable
					 ? string.Join(
						 " > ", route.NodeIds.Select(id => id == State.Op.entryNode ? "LOCAL" : State.Node(id).label))
					 : "Extend your controlled route to this system.", "small muted");

			// Create the bottom bar with the extract or disconnect button and the objective count.
			var bottom = El(right, "inspector-bottom");
			Btn(bottom, State.ObjectiveComplete ? "EXTRACT & DELIVER  →" : "DISCONNECT", DisconnectClicked,
				"disconnect", State.ObjectiveComplete ? "primary-button" : "danger-button");
			Text(bottom, State.ObjectiveCount + " / " + State.ObjectiveTotal + " objectives secured",
				 "micro muted center-text");
		}

		// Handles ActionButton for the workstation or graph UI.
		private void ActionButton(VisualElement parent, ActionInfo action, bool primary)
		{
			// Create the slot wrapper that carries the tooltip styling.
			var wrapper = El(parent, "action-slot");
			wrapper.AddToClassList(IntelTooltipClass);

			// Create the action button that runs this ability when clicked.
			var button = Btn(wrapper, "", () => Run(action.id, action.operatorTarget ? null : selectedNode),
							 "action-" + action.id, "action-button");

			// Build the icon and the text rows inside the button.
			var content = El(button, "action-card-content");
			var icon = new ActionGlyph(action.id, Palette.Cyan, 26);
			content.Add(icon);
			var details = El(content, "action-card-details");
			var row = El(details, "action-title-row");
			Text(row, "", "action-title");

			// Show the keyboard shortcut when there is one.
			if (!string.IsNullOrEmpty(action.shortcutLabel))
				Text(row, action.shortcutLabel, "keycap");
			Text(details, "", "action-estimate");
			Text(details, "", "action-cost");

			// Use the action description, or the granting technology's description as a fallback.
			var description = action.description;

			// Fall back to the technology's description when the action has none.
			if (string.IsNullOrWhiteSpace(description))
				description =
					State.Catalog.Technologies.FirstOrDefault(t => t.grants.Contains(action.id))?.description ??
					"Equipped special ability.";

			// Store the button parts and refresh the button state.
			actionButtons[action.id] = new ActionButtonParts(action, button, wrapper, icon, description);
			UpdateActionButton(actionButtons[action.id], primary);

			// Let pointer events pass through the button's children to the button itself.
			for (var i = 0; i < button.childCount; i++)
				IgnorePointerHits(button[i]);
		}

		// Handles UpdateActionButton for the workstation or graph UI.
		private void UpdateActionButton(ActionButtonParts parts, bool primary = false)
		{
			// Read the action, its button, and the current target.
			var action = parts.action;
			var button = parts.button;
			var target = action.operatorTarget ? null : selectedNode;

			// Preview the action and check whether it is already active.
			var preview = State.Preview(action.id, target);
			var toggleOff = State.IsAbilityActive(action.id, target);

			// Work out whether the action can be queued behind the running task.
			var queue = State.Op.task != null && !action.Definition.instant && !toggleOff && !action.operatorTarget;
			var canQueue = queue && State.CanQueue(action.id, target) &&
						   action.Definition.memoryCost <= State.MemoryFree;

			// Check whether a running task blocks this action.
			var blockedByRunningTask = State.Op.task != null && !toggleOff && !queue &&
									   !(action.Definition.instant && action.operatorTarget);

			// Decide whether the button is enabled and whether it is already queued.
			var enabled = toggleOff ? preview.allowed : queue ? canQueue : preview.allowed && !blockedByRunningTask;
			var alreadyQueued = queue && State.Op.queue.Any(q => q.actionId == action.id && q.target == target);

			// Pick the reason text shown in the tooltip.
			var reason = blockedByRunningTask
				? "Another action is running."
				: toggleOff
					? preview.reason
					: queue && !canQueue
						? !action.Definition.queueable
							? "This action cannot be queued while another action is running."
							: State.Op.queue.Count >= State.QueueCapacity
								? "Queue full (" + State.Op.queue.Count + " / " +
								  State.QueueCapacity +
								  "). Clear queued actions or wait for the current action."
								: alreadyQueued
									? "This action is already in the queue."
									: action.Definition.memoryCost > State.MemoryFree
										? "Cannot queue: not enough free RAM."
										: State.Op.task.actionId == action.id && State.Op.task.target == target
											? "This action is already running on the selected target."
											: "Queue requirements are not met for this target."
						: preview.reason;

			// Work out whether free RAM is blocking the action and how long until it is reclaimed.
			var memoryBlocked = !toggleOff && action.Definition.memoryCost > State.MemoryFree &&
								preview.reason.StartsWith("Not enough free RAM", StringComparison.Ordinal);
			var ramWait = memoryBlocked && State.MemoryReclaim > 0
				? Mathf.Max(0, action.Definition.memoryCost - State.MemoryFree) / State.MemoryReclaim
				: 0;

			// Explain RAM blocks with the reason that matches the situation.
			if (memoryBlocked && State.MemoryFree + State.MemoryReclaiming + 0.001f < action.Definition.memoryCost)
				reason = "RAM HELD BY ACTIVE ABILITIES. Disable one to free memory.";
			else if (memoryBlocked && ramWait > 0)
				reason = "RAM reclaiming. Ready in " + ramWait.ToString("0.0") + "s.";
			else if (memoryBlocked && State.MemoryReclaim <= 0)
				reason = "RAM cannot be reclaimed with the current rig.";

			// Build the tooltip from the title, description, RAM cost, and reason.
			parts.wrapper.tooltip = action.title + "\n" + parts.description +
									(action.Definition.memoryCost > 0
										? "\nRAM COST / " + action.Definition.memoryCost.ToString("0.#") + " GB"
										: "") + Environment.NewLine + reason;

			// Apply the enabled state and style classes to the button.
			button.tooltip = parts.wrapper.tooltip;
			button.SetEnabled(enabled);
			button.EnableInClassList("available-action", enabled);
			button.EnableInClassList("suggested-action", primary && enabled);
			button.EnableInClassList("risky-action", !queue && preview.allowed && preview.willIsolate);

			// Tint the icon by its state and set the title text.
			parts.icon.SetTint(!enabled ? Palette.Hex("65818A") :
							   preview.willIsolate ? Palette.Amber : ActionGlyph.ColorFor(action.id));
			button.Q<Label>(className: "action-title").text = toggleOff ? "OFF / " + action.title : action.title;

			// Work out the estimate line, including remaining uses.
			var remaining = State.UsesRemaining(action.id, target);
			var estimate = memoryBlocked
				? ramWait > 0 ? "READY IN " + ramWait.ToString("0.0") + "s" :
				reason.StartsWith("RAM HELD", StringComparison.Ordinal) ? "RAM HELD" : "NO RAM"
				: blockedByRunningTask
					? "BUSY"
					: queue
						? canQueue ? "QUEUE" :
						State.Op.queue.Any(q => q.actionId == action.id && q.target == target) ? "IN QUEUE" :
						State.Op.queue.Count >= State.QueueCapacity ? "QUEUE FULL" : "WAIT"
						: preview.allowed
							? "READY"
							: "LOCKED";

			// Append the remaining uses and show the estimate.
			if (remaining.HasValue)
				estimate += " / " + remaining.Value + " LEFT";
			button.Q<Label>(className: "action-estimate").text = estimate;

			// Build the cost line: RAM freed when toggling off, or trace and duration when enabled.
			var cost = string.Empty;

			// Pick the cost text for the current state.
			if (toggleOff && action.Definition.memoryCost > 0)
			{
				cost = "FREES " + action.Definition.memoryCost.ToString("0.#") + " GB RAM";
			}
			else if (enabled)
			{
				cost = "+" + preview.trace.ToString("0.#") + " TRACE / " + preview.duration.ToString("0.#") + "s";

				// Add the RAM cost when the action uses memory.
				if (action.Definition.memoryCost > 0)
					cost += " / " + action.Definition.memoryCost.ToString("0.#") + " GB RAM";
			}

			// Show the cost line.
			button.Q<Label>(className: "action-cost").text = cost;
		}

		// Handles Run for the workstation or graph UI.
		private void Run(string actionId, string targetId)
		{
			// Stop when the simulation rejects the action.
			if (!State.Execute(actionId, targetId))
				return;

			// Play the click sound and clear the focus and tooltip.
			Beep();
			networkCamera.FocusedId = null;
			HideTooltip();

			// Redraw the view.
			dirty = true;
		}

		// Produces the id text shown in the UI.
		private Technology FindTech(string id)
		{
			return State.Catalog.Technologies.FirstOrDefault(t => t.id == id);
		}

		// Handles DisconnectClicked for the workstation or graph UI.
		private void DisconnectClicked()
		{
			// Finish the contract when the objective is secured.
			if (State.ObjectiveComplete)
			{
				State.Disconnect();
			}

			// Otherwise pause and ask the player to confirm the abort.
			else
			{
				State.SetPaused(true);
				modal = "abort";
				dirty = true;
			}
		}

		// Builds the TraceBar section of the workstation UI.
		private void BuildTraceBar(VisualElement parent)
		{
			// Create the trace banner with its label and number.
			var bar = El(parent, "trace-banner");
			Text(bar, "TRACE", "eyebrow trace-banner-label");
			traceNumber = Text(bar, "00.0%", "trace-number trace-banner-number");

			// Create the meter fill and the state label.
			var track = El(bar, "meter-track trace-banner-track");
			traceFill = El(track, "meter-fill");
			traceState = Text(bar, "STABLE", "micro trace-banner-state");
		}

		// Builds the OperationBar section of the workstation UI.
		private void BuildOperationBar(VisualElement parent)
		{
			// Fixed slots: every control and readout exists for the whole contract, so nothing appears or
			// disappears when a tool runs or the queue changes. Built only while a contract is active.
			// Create the bar and the hidden rig stats popover with its two rows.
			var bar = El(parent, "operation-bar");
			var stats = El(bar, "operation-stats rig-stats-popover");
			stats.style.display = DisplayStyle.None;
			var topRow = El(stats, "operation-stat-row");
			var bottomRow = El(stats, "operation-stat-row");

			// Create the readouts for power, cooling, compute, transfer, storage, RAM, and reclaim.
			var tint = Palette.Hex("A8C2BC");
			var cyan = Palette.Hex("60D8D0");
			opPower = OperationStat(topRow, "power", tint, "", "micro");
			opCooling = OperationStat(topRow, "cooling", tint, "", "micro");
			opCompute = OperationStat(topRow, "processing", cyan, "", "micro cyan");
			opBandwidth = OperationStat(topRow, "transfer", cyan, "", "micro cyan");
			opStorage = OperationStat(bottomRow, "storage", cyan, "", "micro cyan");
			opMemory = OperationStat(bottomRow, "memory", cyan, "", "micro cyan");
			opReclaim = OperationStat(bottomRow, "reclaim", cyan, "", "micro cyan");

			// Add the button that shows or hides the rig stats popover.
			Btn(bar, "RIG STATUS",
				() =>
				{
					stats.style.display = stats.resolvedStyle.display == DisplayStyle.None
						? DisplayStyle.Flex
						: DisplayStyle.None;
				}, "rig-status", "rig-status-toggle").tooltip =
				"Show power, cooling, compute, transfer, storage, RAM and reclaim details.";

			// Add the speed label and the time control group.
			Text(bar, "SIMULATION SPEED", "micro operation-speed-label");
			var controls = El(bar, "time-controls");

			// Add the pause button, enabled only while a contract is active.
			var pause = Btn(controls, State.Active && State.Op.paused ? "▶" : "Ⅱ", () =>
			{
				if (State.Active)
					State.SetManualPause(!State.Op.paused);
			}, "pause", "time-button");
			pause.SetEnabled(State.Active);
			pause.tooltip = "Pause / Space";

			// Add the stop button that cancels the current tool and clears the queue.
			var stop = Btn(controls, "STOP", () =>
			{
				State.StopTask();
				dirty = true;
			}, "stop-tool", "time-button stop-tool");
			stop.SetEnabled(State.CanStopTask);
			stop.tooltip = "Stop the current tool and clear its queue. Existing trace remains.";

			// Add the clear button that empties the queued tools.
			var clear = Btn(controls, "CLEAR", () =>
			{
				State.ClearQueue();
				dirty = true;
			}, "clear-queue", "time-button clear-tools");
			clear.SetEnabled(State.Active && State.Op.queue.Count > 0);
			clear.tooltip = "Clear queued tools.";

			// Add one speed button for each available simulation rate.
			foreach (var rate in State.SimulationSpeeds)
			{
				// Capture the rate for the click handler.
				var captured = rate;
				var b = Btn(controls, rate + "x", () =>
				{
					speed = captured;
					dirty = true;
				}, "speed-" + rate, "speed-button");

				// Highlight the speed that is currently selected.
				if (Mathf.Approximately(speed, rate))
					b.AddToClassList("active");
			}
		}

		// Handles UpdateMeters for the workstation or graph UI.
		private void UpdateMeters()
		{
			// Refresh the rig stats readouts and the resource radials.
			UpdateOperationStats();
			RefreshResourceRadials();

			// Skip the trace meter when the banner is not on screen.
			if (traceNumber == null)
				return;

			// Read the trace and pick its color from the warning thresholds.
			var trace = State.Active ? State.Op.trace : 0;
			var color = trace >= State.TraceDanger ? Palette.Red :
				trace >= State.TraceWarning ? Palette.Amber : Palette.Lime;

			// Update the trace number and the meter fill.
			traceNumber.text = trace.ToString("00.0") + "%";
			traceNumber.style.color = color;
			traceFill.style.width = Length.Percent(State.TraceFraction * 100);
			traceFill.style.backgroundColor = color;

			// Pick the status label: disconnected, decoy, lockdown, scanners, or stable.
			var status = !State.Active ? "DISCONNECTED" :
				State.Op.decoyRemaining > 0 ? "DECOY " + Mathf.CeilToInt(State.Op.decoyRemaining) + "s" :
				trace >= State.TraceDanger ? "LOCKDOWN IMMINENT" :
				State.Scanners > 0 ? State.Scanners + " SCANNER" + (State.Scanners > 1 ? "S" : "") : "STABLE";

			// Show the status label when it exists.
			if (traceState != null)
			{
				traceState.text = status;
				traceState.style.color = color;
			}
		}

		// Handles UpdateOperationStats for the workstation or graph UI.
		private void UpdateOperationStats()
		{
			// Skip the readouts until the rig stats have been built.
			if (opPower == null)
				return;

			// Update the power, cooling, compute, and transfer readouts.
			opPower.text = GameplayRichText.Format("POWER / " + State.PowerUsed + " / " + State.PowerLimit);
			opCooling.text = GameplayRichText.Format("COOL / " + State.HeatUsed + " / " + State.CoolingLimit);
			opCompute.text = GameplayRichText.Format("COMPUTE / " + State.Compute.ToString("0.00") + " GFLOP/s");
			opBandwidth.text = GameplayRichText.Format("BW / " + State.Bandwidth.ToString("0.00") + " GB/s");

			// Update the storage, RAM, and reclaim readouts.
			opStorage.text = GameplayRichText.Format("STORE / " + State.StorageUsed.ToString("0") + " / " +
													 State.StorageCapacity.ToString("0") + " GB");
			opMemory.text = GameplayRichText.Format("RAM / " + State.MemoryFree.ToString("0.#") + " / " +
													State.MemoryPool.ToString("0.#") + " GB / RES " +
													State.MemoryReserved.ToString("0.#") + " GB");
			opReclaim.text = GameplayRichText.Format("RECLAIM / " + State.MemoryReclaiming.ToString("0.#") + " GB / " +
													 State.MemoryReclaim.ToString("0.#") + " GB/s");
		}

		// Keeps the inspector and focused node on a technology the player can currently see.
		private string ResearchFocus(string id)
		{
			// Nothing to focus when no technology is selected.
			if (string.IsNullOrEmpty(id))
				return null;

			// Keep the selection when it is visible to the player.
			if (State.ResearchVisible(id))
				return id;

			// Fall back to the first visible locked tech, then any visible tech, then the first tech.
			var catalog = State.Catalog;
			return catalog.Technologies.FirstOrDefault(t => State.ResearchVisible(t.id) && !State.Has(t.id))?.id
				   ?? catalog.Technologies.FirstOrDefault(t => State.ResearchVisible(t.id))?.id
				   ?? catalog.Technologies.First().id;
		}

		// Builds the Research section of the workstation UI.
		private void BuildResearch(VisualElement workspace)
		{
			// Keep the selected technology valid for the player's current view.
			selectedTech = ResearchFocus(selectedTech);

			// Build the left column: the ability loadout first.
			var leftShell = El(workspace, "left-panel");
			var loadout = El(leftShell, "research-loadout");
			PanelTitle(loadout, "ABILITY LOADOUT");
			Text(loadout, State.Career.equipped.Count + " / " + State.Slots + " SLOTS USED",
				 "small muted section-space");

			// Show a hint when no abilities are equipped.
			if (State.Career.equipped.Count == 0)
				Text(loadout, "Select an unlocked ability on the network to equip it.", "small muted");

			// List each equipped ability with select and remove buttons.
			foreach (var id in State.Career.equipped.ToArray())
			{
				// Skip abilities whose technology cannot be found.
				var loadoutTech = FindTech(id);

				// Skip the row when its technology is missing.
				if (loadoutTech == null)
					continue;

				// Create the row with the select and remove buttons.
				var row = El(loadout, "research-loadout-row");
				Btn(row, loadoutTech.title, () =>
				{
					selectedTech = id;
					researchCamera.FocusedId = id;
					dirty = true;
				}, "loadout-select-" + id, "research-loadout-select");
				Btn(row, "×", () =>
				{
					if (State.ToggleEquip(id))
						Beep();
				}, "loadout-remove-" + id, "research-loadout-remove").tooltip = "Unequip " + loadoutTech.title;
			}

			// Build the legend that explains each research branch.
			var researchKey = El(leftShell, "research-guide");
			SectionTitle(researchKey, "RESEARCH SIGNALS");
			Text(researchKey, State.ResearchCount + " / " + State.ResearchTotal + " technologies unlocked",
				 "small muted");

			// Add a legend entry for each research branch.
			foreach (var (branch, title, tip) in ResearchLegend)
			{
				// Create the legend entry with its tooltip and branch color.
				var key = El(researchKey, "legend-key");
				key.tooltip = tip;
				var dot = El(key, "legend-dot");
				dot.style.backgroundColor = Palette.Branch(branch);
				Text(key, title, "small").style.color = Palette.Branch(branch);
			}

			// Push the refund button to the bottom of the left column.
			El(leftShell, "flex-spacer");
			var refundArea = El(leftShell, "loadout");
			var refund = Btn(refundArea, "REFUND ALL RESEARCH", () =>
			{
				modal = "refund-research";
				dirty = true;
			}, "refund", "secondary-button");
			refund.SetEnabled(!State.Active && State.ResearchCount > 0);
			Text(refundArea,
				 "Between contracts: " + (State.RefundFraction * 100).ToString("0") + "% of research XP is returned.",
				 "micro muted");

			// Same column structure as the operations map so the GraphViewport gets a flex-sized render target.
			var mid = El(workspace, "network-panel");
			var map = El(mid, "map-content");
			var mh = El(map, "map-heading");
			var titles = El(mh, "map-title-block");
			Text(titles, "CONNECTED KNOWLEDGE", "eyebrow cyan");
			Text(titles, "RESEARCH NETWORK", "map-title");
			Text(mh, State.ResearchCount + " / " + State.ResearchTotal + " TECHNOLOGIES UNLOCKED", "micro muted");

			// Create the research graph and attach the signal field and intel card.
			canvas = new GraphViewport(State.Career, selectedTech, SelectTech, researchCamera, State.Catalog);
			map.Add(canvas);
			Visuals.AttachField(canvas);
			Visuals.AttachIntelCard(canvas.IntelStatic);

			// Build the footer with the zoom and fit controls.
			var footer = El(map, "map-footer");
			Text(footer, "SELECT A TECHNOLOGY TO INSPECT", "micro muted");
			var controls = El(footer, "map-controls");
			Btn(controls, "−", () => canvas.Zoom(1.15f), "zoom-out", "map-button");
			Btn(controls, "+", () => canvas.Zoom(.85f), "zoom-in", "map-button");
			Btn(controls, "FIT", () => canvas.ResetView(), "fit", "map-button");

			// Create the right-hand inspector panel.
			var right = El(workspace, "right-panel");
			var info = Scroll(right, "tech-inspector");
			PanelTitle(info, "TECHNOLOGY INSPECTOR");

			// Prompt the player to select a technology when none is selected.
			if (string.IsNullOrEmpty(selectedTech))
			{
				Text(info, "Select a technology on the research network.", "body-copy muted section-space");
				Text(info, State.Career.equipped.Count + " / " + State.Slots + " special ability slots occupied",
					 "small muted section-space");

				// Explain that research is locked while a run is active.
				if (State.Active)
					Text(info,
						 "Research changes are available between contracts. Your active operation has been paused.",
						 "body-copy amber");

				// Stop here until a technology is selected.
				return;
			}

			// Stop when the selected technology cannot be found.
			var tech = FindTech(selectedTech);

			// Stop when the selected technology cannot be found.
			if (tech == null)
				return;

			// Show the branch and ability type, colored by branch.
			Text(info, tech.branch + (tech.special ? " / EQUIPPED ABILITY" : " / PASSIVE"), "micro section-space").style
				.color = Palette.Branch(tech.branch);

			// Show the technology title, description, and price or unlocked state.
			Text(info, tech.title, "mission-title");
			Text(info, tech.description, "body-copy");
			Text(info, State.Has(tech.id) ? "UNLOCKED" : tech.cost + " XP", "research-price");

			// List the prerequisites and mark which ones are unlocked.
			Text(info, "PREREQUISITES", "eyebrow section-space");

			// Note when the technology has no prerequisites.
			if (tech.parents.Length == 0)
				Text(info, "Included with your gateway.", "small muted");

			// List each prerequisite with its unlock state.
			foreach (var parent in tech.parents)
				Text(info, (State.Has(parent) ? "[+] " : "[ ] ") + FindTech(parent).title,
					 "small " + (State.Has(parent) ? "lime" : "muted"));

			// Warn when both parent technologies are required.
			if (tech.parents.Length > 1)
				Text(info, "BOTH parent technologies required.", "micro amber section-space");

			// Show the loadout and a remove button for each equipped ability.
			Text(info, "LOADOUT", "eyebrow section-space");
			Text(info, State.Career.equipped.Count + " / " + State.Slots + " special ability slots occupied",
				 "small muted");

			// Add a remove button for each equipped ability.
			foreach (var id in State.Career.equipped)
				Btn(info, FindTech(id).title + "  ×", () => State.ToggleEquip(id), "unequip-" + id, "ability-button")
					.SetEnabled(!State.Active);

			// Push the notice and the bottom actions down.
			El(info, "flex-spacer");

			// Show the research lock notice during an active run.
			if (State.Active)
				Text(info, "Research changes are available between contracts. Your active operation has been paused.",
					 "body-copy amber");

			// Create the bottom bar for the unlock, equip, or always-active action.
			var bottom = El(right, "inspector-bottom");

			// Offer the unlock button when the technology is locked.
			if (!State.Has(tech.id))
			{
				// Offer the unlock button when the tech is locked.
				var unlock = Btn(bottom, "UNLOCK / " + tech.cost + " XP", () =>
				{
					if (State.BuyTech(tech.id))
						Beep(true);
				}, "unlock", "primary-button");
				unlock.SetEnabled(!State.Active && State.Career.xp >= tech.cost && tech.parents.All(State.Has));
			}

			// Offer the equip toggle for special abilities that are unlocked.
			else if (tech.special)
			{
				// Equip or unequip the ability, limited by the slot count.
				var equipped = State.Equipped(tech.id);
				var equip = Btn(bottom, equipped ? "UNEQUIP ABILITY" : "EQUIP ABILITY", () =>
				{
					State.ToggleEquip(tech.id);
					Beep();
				}, "equip", "primary-button");
				equip.SetEnabled(!State.Active && (equipped || State.Career.equipped.Count < State.Slots));
			}

			// Show a passive tech as always active.
			else
			{
				Text(bottom, "[+] ALWAYS ACTIVE", "eyebrow lime center-text");
			}
		}

		// Builds the Intel section of the workstation UI.
		private void BuildIntel(VisualElement workspace)
		{
			// Create the scrolling intel page with its header text.
			var page = Scroll(workspace, "full-page");
			Text(page, "THE NETWORK REMEMBERS", "eyebrow cyan");
			Text(page, "Follow the signal.", "large-heading");
			Text(page,
				 "Your contracts leave a mark on the world. Read the consequences, review a run, and connect the dots.",
				 "body-copy muted");

			// Build the world feed column from the career news.
			var columns = El(page, "intel-columns");
			var news = El(columns, "news-feed");
			Text(news, "WORLD FEED", "eyebrow section-space");

			// Add a card for each news item.
			foreach (var item in State.Career.news)
			{
				// Label each news item as a bulletin or a dispatch.
				var card = El(news, "news-card");
				Text(card, item.StartsWith("DAY") ? "ENCRYPTED BULLETIN" : "EXCHANGE DISPATCH", "micro cyan");
				Text(card, item, "body-copy");
			}

			// Build the operation archive column with a card for each report.
			var reports = El(columns, "reports-feed");
			Text(reports, "OPERATION ARCHIVE", "eyebrow section-space");

			// Show a placeholder when there are no reports yet.
			if (State.Career.reports.Count == 0)
				Text(reports,
					 "Your story starts with the first connection. Complete a contract to archive its operation timeline.",
					 "body-copy muted");

			// Add a card for each archived report.
			foreach (var report in State.Career.reports)
			{
				// Each card opens the archived report when clicked.
				var card = Btn(reports, "", () =>
				{
					reportToShow = report;
					modal = "archive";
					dirty = true;
				}, "report-" + State.Career.reports.IndexOf(report), "report-card");

				// Show the day, verdict, title, and results of the report.
				Text(card, "DAY " + report.day.ToString("00") + " / " + report.verdict,
					 "micro " + (report.success ? "lime" : "amber"));
				Text(card, report.title, "upgrade-name");
				Text(card, report.trace.ToString("0.0") + "% TRACE  /  " + report.credits + " cr  /  " + report.xp +
						   " XP", "small muted");
			}

			// Build the recovered intelligence section from collected lore fragments.
			Text(reports, "RECOVERED INTELLIGENCE / " + State.Career.lore.Count, "eyebrow section-space");

			// Show a placeholder when no lore has been recovered.
			if (State.Career.lore.Count == 0)
				Text(reports, "Recover optional archives and extract successfully to preserve their stories here.",
					 "body-copy muted");

			// Add a card for each recovered fragment.
			foreach (var fragment in State.Career.lore)
			{
				// Show the source company, contract, title, and text of the fragment.
				var card = El(reports, "news-card");
				Text(card, fragment.company + " / " + fragment.contract, "micro cyan");
				Text(card, fragment.title, "upgrade-name");
				Text(card, fragment.text, "body-copy");
			}

			// Show the career totals at the bottom of the page.
			var stats = El(page, "career-stats");
			Text(stats, State.Career.jobs + " CONTRACTS DELIVERED", "eyebrow lime");
			Text(stats, State.Career.totalEarnedXp + " TOTAL XP EARNED", "eyebrow cyan");
			Text(stats, State.ResearchCount + " TECHNOLOGIES", "eyebrow violet");
		}

		// Handles ShowHelp for the workstation or graph UI.
		private void ShowHelp()
		{
			// Remember the pause state and pause the operation while help is open.
			pausedBeforeHelp = State.Active && State.Op.paused;

			// Pause the operation while the help guide is open.
			if (State.Active)
				State.SetPaused(true);

			// Open the help modal.
			modal = "help";
			dirty = true;
		}

		// Handles ShowSettings for the workstation or graph UI.
		private void ShowSettings()
		{
			// Do nothing when a settings-related modal is already open.
			if (modal == "settings" || modal == "reset-save" || modal == "debug" || modal == "refund-research")
				return;

			// Remember the previous modal and the pause state, then pause the operation.
			modalBeforeSettings = modal;
			pausedBeforeSettings = State.Active && State.Op.paused;

			// Pause the operation while the settings menu is open.
			if (State.Active)
				State.SetPaused(true);

			// Open the settings menu.
			modal = "settings";
			settingsError = null;
			dirty = true;
		}

		// Handles ResumeFromSettings for the workstation or graph UI.
		private void ResumeFromSettings(bool resumeOperation = false)
		{
			// Return to the modal that was open before settings.
			modal = modalBeforeSettings;
			modalBeforeSettings = null;
			settingsError = null;

			// Resume the operation, or restore its previous pause state.
			if (State.Active)
			{
				if (resumeOperation)
					State.SetManualPause(false);
				else
					State.SetPaused(pausedBeforeSettings);
			}

			// Redraw the view.
			dirty = true;
		}

		// Handles OpenResetSaveConfirm for the workstation or graph UI.
		private void OpenResetSaveConfirm(string returnModal = "settings")
		{
			// Remember which modal to return to and open the confirmation.
			modalBeforeResetSave = returnModal;
			modal = "reset-save";
			settingsError = null;
			dirty = true;
		}

		// Handles CancelResetSave for the workstation or graph UI.
		private void CancelResetSave()
		{
			// Return to the modal that opened the confirmation.
			modal = modalBeforeResetSave;
			modalBeforeResetSave = "settings";
			settingsError = null;
			dirty = true;
		}

		// Handles ResetGameSave for the workstation or graph UI.
		private void ResetGameSave()
		{
			// Wipe the saved career and stop when the reset fails.
			if (!Testing && !CareerStorage.Reset())
			{
				settingsError = "The save could not be reset. Check that local storage is writable, then try again.";
				dirty = true;
				return;
			}

			// Create a fresh simulation and reconnect the autosave hook.
			State = new GameSimulation(new Career());
			State.Changed += () =>
			{
				dirty = true;

				// Persist the career unless testing.
				if (!Testing)
					CareerStorage.Save(State.Career);
			};

			// Reset the selection and session state to the new career.
			sessionActive = State.Active;
			selectedJob = State.Catalog.Contracts(State.Career).First(c => !c.recurring);
			selectedNode = null;
			selectedTech = null;
			CurrentView = "network";
			networkCamera.Reset();
			researchCamera.Reset();

			// Clear all modal and report state.
			modal = modalBeforeSettings = settingsError = null;
			modalBeforeResetSave = "settings";
			lastShownReport = reportToShow = null;

			// Reset the simulation speed, autosave timer, and trace band.
			speed = 1;
			saveTimer = 0;
			traceBand = 0;

			// Redraw the view.
			dirty = true;
		}

		// Handles QuitGame for the workstation or graph UI.
		private void QuitGame()
		{
			// Save the career first and stop when the save fails.
			if (!Testing && !CareerStorage.Save(State.Career))
			{
				settingsError = "The save could not be written. Check that local storage is writable, then try again.";
				dirty = true;
				return;
			}

			// Stop play mode in the editor, or quit the built application.
#if UNITY_EDITOR
			EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
		}

		// Builds the Modal section of the workstation UI.
		private void BuildModal()
		{
			// Create the overlay and the scrolling modal box, then animate it in.
			overlay = El(Root, "modal-overlay");
			var box = Scroll(overlay, "modal-box");
			OpenModalWithMotion(box);

			// Build the settings menu.
			if (modal == "settings")
			{
				// Add the heading and the text size slider.
				box.AddToClassList("settings-menu");
				Text(box, "WORKSTATION / ESC", "eyebrow cyan");
				Text(box, "Settings", "large-heading");
				Text(box, "TEXT SIZE / " + TextSizePercent + "%", "eyebrow cyan").name = "text-size-value";
				var textSize = new SliderInt("Text size", 100, 300) { name = "text-size", showInputField = true };
				textSize.AddToClassList("text-size-slider");
				textSize.SetValueWithoutNotify(TextSizePercent);
				box.Add(textSize);
				textSize.RegisterValueChangedCallback(evt => SetTextSize(evt.newValue));
				Text(
					box,
					"100%–300%. Default: 200% (double size). Applies immediately to every screen and is saved automatically.",
					"small muted");

				// Add the graph inspect toggle.
				Text(box, "GRAPH", "eyebrow cyan section-space");
				var selectWithoutFocus = new Toggle("Single-click selection does not move the camera")
					{ name = "select-without-camera-focus" };
				selectWithoutFocus.SetValueWithoutNotify(SelectWithoutCameraFocus);
				selectWithoutFocus.RegisterValueChangedCallback(evt => SetSelectWithoutCameraFocus(evt.newValue));
				box.Add(selectWithoutFocus);
				Text(
					box,
					"When on, single-click selects and opens detailed intel without moving the camera; double-click also frames the view. When off, single-click selects and frames immediately.",
					"small muted");

				// Add the hacked connection color field and its live feedback.
				Text(box, "HACKED CONNECTION COLOR", "eyebrow cyan section-space");
				var hackedConnectionColor = new TextField("Hacked node links (#RRGGBB)")
				{
					name = "hacked-connection-color", value = "#" + ColorUtility.ToHtmlStringRGB(HackedConnectionColor)
				};
				box.Add(hackedConnectionColor);
				var hackedColorFeedback =
					Text(box, "Links touching systems you control use this color and a stronger stroke.",
						 "small muted");

				// Apply a valid color and show a message, or show an error for an invalid one.
				hackedConnectionColor.RegisterValueChangedCallback(evt =>
				{
					if (ColorUtility.TryParseHtmlString(evt.newValue, out var color))
					{
						SetHackedConnectionColor(color);
						hackedColorFeedback.text =
							"Saved locally. Links touching systems you control use this color and a stronger stroke.";
					}
					else
					{
						hackedColorFeedback.text = "Enter a valid color such as #C5F56B.";
					}
				});

				// Add the display effect toggles.
				Text(box, "DISPLAY", "eyebrow cyan section-space");
				BuildDisplayEffectToggles(box);
				Text(
					box,
					"Toggle each layer independently. Turn off animation to freeze motion while keeping shaders visible. Preferences are saved locally.",
					"small muted");

				// Add the interface sound toggle.
				Text(box, "AUDIO", "eyebrow cyan section-space");
				var soundToggle = new Toggle("Interface sounds") { name = "interface-sounds" };
				soundToggle.SetValueWithoutNotify(!muted);
				soundToggle.RegisterValueChangedCallback(evt => SetMuted(!evt.newValue));
				box.Add(soundToggle);
				Text(box, "UI clicks and trace warnings. Saved locally.", "small muted");

				// Add the resume, debug, reset, and quit buttons.
				Btn(box, "Resume", () => ResumeFromSettings(true), "resume", "primary-button");
				Btn(box, "Cheats / Debug", () =>
				{
					modal = "debug";
					dirty = true;
				}, "debug-menu", "secondary-button");
				Btn(box, "Reset Game Save", () => OpenResetSaveConfirm(), "reset-save", "secondary-button");
				Btn(box, "Quit", QuitGame, "quit", "danger-button");

				// Show any settings error and the close hint.
				if (settingsError != null)
					Text(box, settingsError, "body-copy amber");
				Text(box, "ESC / RESUME", "micro muted section-space center-text");
			}

			// Build the debug menu with cheat buttons.
			else if (modal == "debug")
			{
				// Add the heading and the warning text.
				box.AddToClassList("settings-menu");
				Text(box, "WORKSTATION / DEBUG", "eyebrow amber");
				Text(box, "Cheats / Debug", "large-heading");
				Text(
					box,
					"Changes apply immediately and autosave to this career. An active operation stays paused while this menu is open.",
					"body-copy muted");

				// Show the current credits, XP, and research progress.
				Text(box, State.Career.credits.ToString("N0") + " CREDITS / " + State.Career.xp.ToString("N0") + " XP",
					 "eyebrow cyan section-space");
				Text(box, State.ResearchCount + " / " + State.ResearchTotal + " TECHNOLOGIES UNLOCKED", "small muted");

				// Add the cheat buttons for credits, XP, and the tech tree.
				Btn(box, "Add 1,000,000 credits", () =>
				{
					State.DebugAddCredits();
					Beep();
				}, "debug-credits", "secondary-button");
				Btn(box, "Add 1,000,000 XP", () =>
				{
					State.DebugAddXp();
					Beep();
				}, "debug-xp", "secondary-button");
				var unlock = Btn(box, "Unlock whole tech tree", () =>
				{
					State.DebugUnlockAllTech();
					Beep();
				}, "debug-unlock-tech", "secondary-button");
				unlock.SetEnabled(State.ResearchCount < State.ResearchTotal);

				// Explain the unlock rules and add the reset and back buttons.
				Text(
					box,
					"Technology unlocks are free. Equip special abilities in Research between contracts; loadout slots still apply.",
					"small muted");
				Btn(box, "Reset Game", () => OpenResetSaveConfirm("debug"), "debug-reset-game", "danger-button");
				Text(
					box,
					"Same as Settings → Reset Game Save: wipes this career, clears any active operation, and writes a fresh save.",
					"small muted");
				Btn(box, "Back to Settings", () =>
				{
					modal = "settings";
					dirty = true;
				}, "debug-back", "primary-button");
				Text(box, "ESC / BACK TO SETTINGS", "micro muted section-space center-text");
			}

			// Build the reset confirmation dialog.
			else if (modal == "reset-save")
			{
				// Warn the player that the reset cannot be undone.
				box.AddToClassList("settings-menu");
				Text(box, "RESET GAME SAVE?", "eyebrow amber");
				Text(box, "Start a new career?", "large-heading");
				Text(
					box,
					"This permanently resets all contracts, credits, research, hardware, reports, and your current operation. This cannot be undone.",
					"body-copy muted");

				// Add the cancel and confirm buttons, and any error message.
				Btn(box, "Cancel", CancelResetSave, "cancel-reset", "primary-button");
				Btn(box, "Reset Game Save", ResetGameSave, "confirm-reset", "danger-button");

				// Show any reset error.
				if (settingsError != null)
					Text(box, settingsError, "body-copy amber");
			}

			// Build the operator field guide.
			else if (modal == "help")
			{
				// Add the guide heading.
				Text(box, "OPERATOR FIELD GUIDE", "eyebrow cyan");
				Text(box, "The planet is a network.\nFind your way through.", "large-heading");

				// List the field guide entries.
				string[] guide =
				{
					"01 / CONNECT — Pick a contract. Gold markers show required objectives. Green means a system is yours.",
					"02 / EXPLORE — Select a reachable system. Scan [1] maps connections and prepares a faster, quieter intrusion. Hack [2] takes control and extends your route.",
					"03 / RECOVER — Control each required target, then recover its files [3]. Record jobs need both copies; investigations need all three records.",
					"04 / MANAGE TRACE — Tools create trace. Check the forecast before committing. STOP cancels a tool; existing trace stays. Planning is free. Space pauses; " +
					string.Join(" / ", State.SimulationSpeeds.Select(x => x + "x")) + " changes speed.",
					"05 / GET PAID — Extract & deliver after all objectives are secured. Optional archives pay extra. At " +
					State.TraceLimit + "% trace the attempt ends; gear and research stay yours.",
					"06 / GROW — Credits buy hardware. XP purchases connected research. Crossovers require both parents. Equip abilities in limited slots. Research refunds are free between jobs."
				};

				// Show each guide line.
				foreach (var line in guide)
					Text(box, line, "guide-line");

				// Explain the graph and settings controls.
				Text(
					box,
					"GRAPH CONTROLS / Click to select and inspect · Double-click to frame the camera (when single-click inspect is enabled) · Scroll to zoom · Left-drag to orbit · Right- or middle-drag to pan · FIT to recenter\nSETTINGS / Text size, graph inspect behavior, display effects, interface sounds, save reset, and quit.\nYour career and active operation autosave locally. Reopened operations start paused.",
					"small muted section-space");

				// Add the button that closes the guide and restores the pause state.
				Btn(box, "BACK TO THE WORKSTATION  →", () =>
				{
					modal = null;

					// Restore the pause state when the help guide closes.
					if (State.Active)
						State.SetPaused(pausedBeforeHelp);

					// Redraw the view.
					dirty = true;
				}, "close-help", "primary-button");
			}

			// Build the disconnect confirmation for an unfinished contract.
			else if (modal == "abort")
			{
				// Explain that disconnecting forfeits payment.
				Text(box, "END THIS CONNECTION?", "eyebrow amber");
				Text(box, "The job isn't secured yet.", "large-heading");
				Text(
					box,
					"Disconnecting now ends this attempt without payment. The contract stays available, and your equipment and research remain yours.",
					"body-copy muted");

				// Add the continue button, which resumes the operation.
				Btn(box, "CONTINUE OPERATION", () =>
				{
					modal = null;
					State.SetManualPause(false);
					dirty = true;
				}, "cancel-abort", "primary-button");

				// Add the confirm button, which ends the attempt without reward.
				Btn(box, "DISCONNECT WITHOUT REWARD", () =>
				{
					modal = null;
					State.Disconnect();
					dirty = true;
				}, "confirm-abort", "danger-button");
			}

			// Build the operation report, either from the archive or the latest run.
			else
			{
				// Pick the report to show, and close the modal if there is none.
				var report = modal == "archive" ? reportToShow : State.LastReport;

				// Close the modal when there is no report to show.
				if (report == null)
				{
					modal = null;
					return;
				}

				// Show the verdict, title, company, and day.
				box.AddToClassList("operation-report");
				Text(box, "OPERATION / " + report.verdict, "eyebrow " + (report.success ? "lime" : "amber"));
				Text(box, report.title, "large-heading");
				Text(box, report.company + " / DAY " + report.day.ToString("00"), "micro muted");

				// Show the credits, XP, and trace results.
				var rewards = El(box, "result-rewards");
				Text(rewards, report.success ? "+" + report.credits.ToString("N0") + " cr" : "NO REWARD",
					 "result-credit");
				Text(rewards, report.success ? "+" + report.xp + " XP" : "RETRY AVAILABLE", "result-xp");
				Text(rewards, report.trace.ToString("0.0") + "% TRACE", "result-trace");

				// Show the bonuses and the narrative.
				ReportBonuses(box, report);
				Text(box, report.narrative, "body-copy");

				// Show the operation timeline in a scrolling list.
				Text(box, "POST-OPERATION TIMELINE", "eyebrow section-space");
				var timeline = new ScrollView(ScrollViewMode.Vertical);
				timeline.AddToClassList("result-timeline");
				box.Add(timeline);

				// Add each log line to the timeline.
				foreach (var line in report.log)
					Text(timeline, line, "terminal-line muted");

				// Show the story completion message when the final contract is done.
				if (report.success && State.StoryComplete)
					Text(box, "STORY COMPLETE / Freelance contracts remain available. Keep building your network.",
						 "body-copy lime");

				// Add the close button, and the research shortcut after a successful run.
				var actions = El(box, "modal-actions");
				var archived = modal == "archive";
				Btn(actions, archived ? "CLOSE ARCHIVE" : "BACK TO EXCHANGE", () =>
				{
					modal = null;
					CurrentView = archived ? "intel" : "network";
					SelectNextContract();
					dirty = true;
				}, "close-report", "secondary-button");

				// Offer the research shortcut after a successful run.
				if (report.success && modal != "archive")
					Btn(actions, "OPEN RESEARCH  →", () =>
					{
						modal = null;
						SelectNextContract();
						Navigate("research");
					}, "report-research", "primary-button");
			}
		}

		// Handles ReportBonuses for the workstation or graph UI.
		private void ReportBonuses(VisualElement box, OperationReport report)
		{
			// Read the reward tuning and work out how many archives this contract had.
			var rewards = State.Catalog.Data.rewards;
			var archiveTotal = report.bonusArchivesTotal > 0
				? report.bonusArchivesTotal
				: Math.Max(report.bonusFiles, 0);
			var showArchives = archiveTotal > 0;
			var showQuiet = report.quietLimit > 0;

			// Hide the section when the contract has no bonuses.
			if (!showArchives && !showQuiet)
				return;

			// Show the bonus section heading.
			Text(box, "CONTRACT BONUSES", "eyebrow section-space");

			// Show the quiet completion bonus status.
			if (showQuiet)
			{
				var payout = "+" + rewards.quietCredits + " cr / +" + rewards.quietXp + " XP";
				var requirement = "Below " + report.quietLimit.ToString("0") + "% trace";

				// Pick the status: not paid, achieved, or missed.
				string status;

				// Choose the status text for the outcome.
				if (!report.success)
					status = "NOT PAID  " + requirement + " — " + payout;
				else if (report.quietBonusEarned)
					status = "ACHIEVED  " + requirement + " — " + payout;
				else
					status = "MISSED  " + report.trace.ToString("0.0") + "% trace (need " + requirement + ") — " +
							 payout;
				Text(box, "QUIET COMPLETION / " + status,
					 "result-bonus " + (report.success && report.quietBonusEarned ? "lime" : "amber"));
			}

			// Show the optional archive bonus status.
			if (showArchives)
			{
				// Work out the earned and maximum payouts for the archives.
				var earnedCr = report.bonusFiles * rewards.archiveCredits;
				var earnedXp = Mathf.Min(rewards.archiveXpCap, report.bonusFiles * rewards.archiveXp);
				var maxCr = archiveTotal * rewards.archiveCredits;
				var maxXp = Mathf.Min(rewards.archiveXpCap, archiveTotal * rewards.archiveXp);
				var payout = report.success && report.bonusFiles > 0
					? "+" + earnedCr + " cr / +" + earnedXp + " XP"
					: "Up to +" + maxCr + " cr / +" + maxXp + " XP";
				var progress = report.bonusFiles + "/" + archiveTotal + " secured";

				// Pick the status: not paid, achieved, or missed.
				var status = report.success
					? report.bonusFiles > 0
						? "ACHIEVED  " + progress + " — " + payout
						: "MISSED  " + progress + " — " + payout
					: "NOT PAID  " + progress + " — " + payout;
				Text(box, "OPTIONAL ARCHIVES / " + status,
					 "result-bonus " + (report.success && report.bonusFiles > 0 ? "lime" : "amber"));
			}
		}

		// Handles SelectNextContract for the workstation or graph UI.
		private void SelectNextContract()
		{
			// Pick the first open story contract, or the last contract as a fallback.
			selectedJob =
				State.Catalog.Contracts(State.Career)
					 .FirstOrDefault(c => !State.Career.completed.Contains(c.id) &&
										  State.Career.completed.Count >= c.requiredWins) ??
				State.Catalog.Contracts(State.Career).Last();

			// Clear the node selection and reset the camera.
			selectedNode = null;
			networkCamera.Reset();
		}

		// Produces the Rank text shown in the UI.
		private string Rank()
		{
			return State.Rank;
		}

		// Produces the classes text shown in the UI.
		private static VisualElement El(VisualElement parent, string classes)
		{
			// Create the element and apply each space-separated class name.
			var el = new VisualElement();

			// Apply each class name to the element.
			foreach (var c in classes.Split(' '))
				if (c.Length > 0)
					el.AddToClassList(c);

			// Attach the element to its parent.
			parent.Add(el);
			return el;
		}

		// Produces the classes text shown in the UI.
		private static ScrollView Scroll(VisualElement parent, string classes)
		{
			// Create the scroll view and apply each space-separated class name.
			var scroll = new ScrollView(ScrollViewMode.Vertical);

			// Apply each class name to the scroll view.
			foreach (var c in classes.Split(' '))
				if (c.Length > 0)
					scroll.AddToClassList(c);

			// Attach the scroll view to its parent.
			parent.Add(scroll);
			return scroll;
		}

		// Produces the text text shown in the UI.
		private static Label Text(VisualElement parent, string text, string classes)
		{
			// Create the label and apply each space-separated class name.
			var label = new Label(text);

			// Apply each class name to the label.
			foreach (var c in classes.Split(' '))
				if (c.Length > 0)
					label.AddToClassList(c);

			// Attach the label to its parent.
			parent.Add(label);
			return label;
		}

		// Produces the title text shown in the UI.
		private static Label PanelTitle(VisualElement parent, string title)
		{
			return Text(parent, title, "eyebrow panel-top-title");
		}

		// Produces the title text shown in the UI.
		private static Label SectionTitle(VisualElement parent, string title)
		{
			return Text(parent, title, "eyebrow section-space panel-section-title");
		}

		// Handles OpenModalWithMotion for the workstation or graph UI.
		private void OpenModalWithMotion(VisualElement box)
		{
			// Skip the animation when display effects are frozen.
			if (!Visuals.Animate)
				return;

			// Add the opening class and remove it on the next frame.
			box.AddToClassList("opening");
			box.schedule.Execute(() => box.RemoveFromClassList("opening")).ExecuteLater(16);
		}

		// Produces the text text shown in the UI.
		private static Label RichText(VisualElement parent, string text, string classes)
		{
			// Create the label with formatted rich text and apply each class name.
			var label = new Label(GameplayRichText.Format(text)) { enableRichText = true };

			// Apply each class name to the label.
			foreach (var c in classes.Split(' '))
				if (c.Length > 0)
					label.AddToClassList(c);

			// Attach the label to its parent.
			parent.Add(label);
			return label;
		}

		// Handles IgnorePointerHits for the workstation or graph UI.
		private static void IgnorePointerHits(VisualElement element)
		{
			// Let pointer events pass through this element and all of its children.
			element.pickingMode = PickingMode.Ignore;

			// Recurse into each child element.
			for (var i = 0; i < element.childCount; i++)
				IgnorePointerHits(element[i]);
		}

		// Produces the text text shown in the UI.
		private Button Btn(VisualElement parent, string text, Action action, string name, string classes)
		{
			// Create the button and apply each class name.
			var button = new Button(action) { text = text, name = name };

			// Apply each class name to the button.
			foreach (var c in classes.Split(' '))
				if (c.Length > 0)
					button.AddToClassList(c);

			// Attach the button to its parent and register its action by name.
			parent.Add(button);
			uiActions[name] = action;
			return button;
		}

		// Produces the glyph text shown in the UI.
		private Button RailCircleButton(VisualElement parent, string glyph, Action action, string name, string classes)
		{
			// Create the circular button and apply each class name.
			var button = new Button(action) { text = "", name = name };

			// Apply each class name to the button.
			foreach (var c in classes.Split(' '))
				if (c.Length > 0)
					button.AddToClassList(c);

			// Add the glyph label and make it ignore pointer hits.
			var icon = Text(button, glyph, "rail-help-glyph");
			icon.pickingMode = PickingMode.Ignore;

			// Attach the button to its parent and register its action by name.
			parent.Add(button);
			uiActions[name] = action;
			return button;
		}

		/// <summary>Toggles or activates a named control for editor smoke tests.</summary>
		/// <param name="name">Element name on the current UI tree.</param>
		/// <returns>True when a visible control handled the activation.</returns>
		public bool ActivateVisibleControl(string name)
		{
			// Rebuild first so the lookup sees the current tree.
			if (dirty)
				Build();

			// Toggle a visible, enabled toggle control.
			var toggle = Root.Q<Toggle>(name);

			// Flip the toggle when it is visible and enabled.
			if (toggle != null && toggle.enabledInHierarchy)
			{
				toggle.value = !toggle.value;
				return true;
			}

			// Stop when no visible, enabled button has this name.
			var control = Root.Q<Button>(name);

			// Stop when no visible, enabled button has this name.
			if (control == null || !control.enabledInHierarchy)
				return false;

			// Invoke the registered action for this button.
			if (uiActions.TryGetValue(name, out var action))
			{
				action();
				return true;
			}

			// Fall back to a control on the graph canvas.
			return canvas?.ActivateControl(name) == true;
		}

		/// <summary>Replaces the session with the default catalog and a test career.</summary>
		/// <param name="career">Career state to simulate.</param>
		public void UseTestCareer(Career career)
		{
			UseTestCatalog(GameCatalog.Default, career);
		}

		/// <summary>Replaces the session for automated tests without autosave side effects.</summary>
		/// <param name="catalog">Catalog supplying contracts and tech.</param>
		/// <param name="career">Optional career; a new career is created when null.</param>
		/// <param name="contractId">When set, selects this contract instead of the first story contract.</param>
		public void UseTestCatalog(GameCatalog catalog, Career career = null, string contractId = null)
		{
			// Turn off autosave for the test session.
			Testing = true;

			// Create the simulation from the supplied catalog and career.
			State = new GameSimulation(catalog, career);
			State.Changed += () => dirty = true;
			sessionActive = State.Active;

			// Select the requested contract, or the first story contract.
			selectedJob = catalog.Contracts(State.Career)
								 .First(c => contractId == null ? !c.recurring : c.id == contractId);

			// Reset the selection, view, and camera state.
			selectedNode = null;
			selectedTech = null;
			CurrentView = "network";
			modal = null;
			networkCamera.Reset();
			researchCamera.Reset();

			// Clear the modal and report state, then redraw.
			dirty = true;
			modalBeforeSettings = settingsError = null;
			lastShownReport = reportToShow = null;
		}
	}
}