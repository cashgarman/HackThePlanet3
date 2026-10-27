using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static Cashg.GraphExplorer.GraphUi;

namespace Cashg.GraphExplorer
{
	/// <summary>Represents graph shell controller.</summary>
	public sealed class GraphShellController : IDisposable
	{
		private readonly List<GraphShellDrawer> customDrawers = new();
		private readonly List<Action> detach = new();
		private readonly IGraphShellHost host;
		private readonly List<VisualElement> owned = new();
		private bool disposed;
		private Toggle timelineEnabledToggle;

		/// <summary>Creates shell controls for a host.</summary>
		/// <param name="host">Host that owns shell state and elements.</param>
		public GraphShellController(IGraphShellHost host)
		{
			this.host = host ?? throw new ArgumentNullException(nameof(host));
		}

		/// <summary>Gets the timeline controls created for the host, when available.</summary>
		public GraphTimelineControls TimelineControls { get; private set; }

		/// <summary>Detaches callbacks, disposes timeline controls, and removes owned UI.</summary>
		public void Dispose()
		{
			// Stop scheduled interactions and remove only UI elements and callbacks owned by this controller.
			if (disposed) return;
			disposed = true;
			EndShellResize();
			host.shellSearchSchedule?.Pause();
			TimelineControls?.Dispose();
			foreach (var action in detach) action();
			detach.Clear();
			foreach (var element in owned) element.RemoveFromHierarchy();
			owned.Clear();
			host.shellReady = false;
		}

		private void RegisterOn<T>(VisualElement target, EventCallback<T> callback,
								   TrickleDown phase = TrickleDown.NoTrickleDown) where T : EventBase<T>, new()
		{
			target.RegisterCallback(callback, phase);
			detach.Add(() => target.UnregisterCallback(callback, phase));
		}

		private void RegisterRoot<T>(EventCallback<T> callback, TrickleDown phase = TrickleDown.NoTrickleDown)
			where T : EventBase<T>, new()
		{
			RegisterOn(host.root, callback, phase);
		}

		/// <summary>Builds the shell header and its navigation controls.</summary>
		public void BuildShellHeader()
		{
			// Register the header as owned UI, then compose navigation, search, and workspace controls.
			host.shellHeader = Element("header");
			host.shellHeader.name = "shell-header";
			host.root.Add(host.shellHeader);
			owned.Add(host.shellHeader);
			var brand = Element("brand");
			brand.Add(Label(host.BrandMark, "brand-mark"));
			brand.Add(Label(host.Title, "app-title"));
			host.shellHeader.Add(brand);
			var historyControls = Element("header-actions");
			host.shellHeader.Add(historyControls);
			foreach (var direction in new[] { -1, 1 })
			{
				var step = direction;
				var button = HeaderButton(step < 0 ? "Back" : "Forward", step < 0 ? "back" : "forward",
										  () => host.History(step));
				button.AddToClassList("shell-icon-only");
				historyControls.Add(button);
			}

			var overview = HeaderButton("Overview", "overview", () => host.Overview());
			overview.name = "shell-overview";
			host.shellHeader.Add(overview);
			host.shellSearchHost = Element("shell-search-host");
			host.shellHeader.Add(host.shellSearchHost);
			var headerRight = Element("shell-header-right");
			host.shellHeader.Add(headerRight);
			var progress = Element("shell-header-progress");
			progress.name = "shell-header-progress";
			var progressFill = Element("shell-header-progress-fill");
			progressFill.name = "shell-header-progress-fill";
			progress.Add(progressFill);
			headerRight.Add(progress);
			host.shellSourceStatus = Label("Connecting", "shell-source-status");
			headerRight.Add(host.shellSourceStatus);
			var source = HeaderButton("Source", "web", () => ShowShellDrawer("Source"));
			source.name = "shell-source-button";
			headerRight.Add(source);
			host.shellWorkspaceButton = HeaderButton("Workspace", "save", () => ToggleShellWorkspace());
			host.shellWorkspaceButton.name = "shell-workspace-button";
			headerRight.Add(host.shellWorkspaceButton);
		}

		/// <summary>Completes shell construction after the host's search controls are ready.</summary>
		public void CompleteShell()
		{
			// Attach navigation rail controls to the host's existing drawer content.
			host.BuildNodeSearch();
			var drawerHead = Element("shell-drawer-head");
			host.shellDrawerTitle = Label("Filters", "shell-drawer-title");
			drawerHead.Add(host.shellDrawerTitle);
			var closeDrawer = Button("Close", () => ShowShellDrawer(""));
			closeDrawer.name = "shell-close-drawer";
			drawerHead.Add(closeDrawer);
			host.sidebar.Insert(0, drawerHead);
			owned.Add(drawerHead);
			host.shellRail = Element("shell-rail");
			host.shellRail.name = "shell-rail";
			host.root.Add(host.shellRail);
			owned.Add(host.shellRail);
			foreach (var entry in new[]
					 {
						 new[] { "Browse", "list" }, new[] { "Filters", "filter" }, new[] { "Favorites", "star" },
						 new[] { "View", "eye" }, new[] { "Source", "web" }
					 })
			{
				var name = entry[0];
				var button = HeaderButton(name, entry[1], () => ShowShellDrawer(name));
				button.name = "shell-rail-" + name.ToLowerInvariant();
				button.AddToClassList("shell-rail-button");
				host.shellRail.Add(button);
				host.shellRailButtons[name] = button;
			}

			// Insert host-provided drawers beside built-ins while enforcing unique rail identities.
			if (host is IGraphCustomDrawerShellHost customHost)
				foreach (var drawer in customHost.CustomDrawers)
				{
					if (host.shellRailButtons.ContainsKey(drawer.Id))
						throw new ArgumentException("Duplicate drawer ID: " + drawer.Id);
					customDrawers.Add(drawer);
					if (drawer.Content.parent == null) host.shellViewBody.parent.Add(drawer.Content);
					var button = HeaderButton(drawer.Title, drawer.Icon, () => ShowShellDrawer(drawer.Id));
					button.name = "shell-rail-" + drawer.Id.ToLowerInvariant();
					button.AddToClassList("shell-rail-button");
					host.shellRail.Insert(
						host.shellRail.IndexOf(host.shellRailButtons["View"]) + 1 + customDrawers.Count - 1, button);
					host.shellRailButtons[drawer.Id] = button;
				}

			var help = HeaderButton("Help", "help", host.ShowHelp);

			// Compose the shared inspect/queue dock before applying persisted view preferences.
			help.AddToClassList("shell-rail-button");
			help.AddToClassList("shell-rail-help");
			host.shellRail.Add(help);
			host.shellDockHeader = Element("shell-dock-head");
			host.rightColumn.Insert(0, host.shellDockHeader);
			owned.Add(host.shellDockHeader);
			host.shellInspectTab = host.LabeledIcon("search", "Inspect", () => ShowShellDock("Inspect"),
													"Inspect. Details and relationships for the selected entity.");
			host.shellInspectTab.AddToClassList("icon-tab");
			host.shellInspectTab.name = "shell-inspect-tab";
			host.shellQueueTab = host.LabeledIcon("list", "Queue", () => ShowShellDock("Queue"),
												  "Task queue. Draft tasks, run progress, and history.");
			host.shellQueueTab.AddToClassList("icon-tab");
			host.shellQueueTab.name = "shell-queue-tab";
			host.shellQueueBadge = Label("0", "tab-badge");
			host.shellQueueBadge.pickingMode = PickingMode.Ignore;
			host.shellQueueTab.Add(host.shellQueueBadge);
			var closeDock = host.IconButton("close", () => ShowShellDock(""), "Close the dock.");
			closeDock.name = "shell-close-dock";
			host.shellDockHeader.Add(host.shellInspectTab);
			host.shellDockHeader.Add(host.shellQueueTab);
			host.shellDockHeader.Add(closeDock);
			AddShellDockResize();
			host.shellScaleField = new DropdownField("UI scale", new List<string> { "100%", "125%", "150%" },
													 Mathf.RoundToInt((host.shellPreferences.uiScale - 1) * 4));
			host.shellScaleField.name = "shell-ui-scale";
			host.shellViewBody.Add(host.shellScaleField);
			owned.Add(host.shellScaleField);
			RegisterOn<ChangeEvent<string>>(host.shellScaleField,
											e => SetShellScale(
												e.newValue == "150%" ? 1.5f : e.newValue == "125%" ? 1.25f : 1));
			if (host.shellPreferencesLoaded) host.reducedMotion = host.shellPreferences.reducedMotion;
			else host.shellPreferences.reducedMotion = host.reducedMotion;
			host.ApplyReducedMotion(host.reducedMotion);
			var motion = new Toggle("Reduced motion") { value = host.reducedMotion, name = "shell-reduced-motion" };
			UseShellCheckbox(motion);
			host.shellViewBody.Add(motion);
			owned.Add(motion);
			RegisterOn<ChangeEvent<bool>>(motion, e =>
			{
				host.reducedMotion = host.shellPreferences.reducedMotion = e.newValue;
				host.ApplyReducedMotion(host.reducedMotion);
				host.root.EnableInClassList("reduced-motion", host.reducedMotion);
				host.SaveShellPreferences();
				if (host.ActionsAvailable) host.SaveCommandWorkspace();
			});
			var scaleNote =
				Label("Scale changes the whole interface. Larger scales use the compact layout when needed.", "note");
			host.shellViewBody.Add(scaleNote);
			owned.Add(scaleNote);
			if (host is IGraphTimelineShellHost timelineHost)
			{
				// Add timeline controls only for hosts that expose history operations.
				timelineEnabledToggle = new Toggle("Graph change timeline")
					{ value = host.shellPreferences.timelineEnabled, name = "shell-timeline-enabled" };
				UseShellCheckbox(timelineEnabledToggle);
				host.shellViewBody.Add(timelineEnabledToggle);
				owned.Add(timelineEnabledToggle);
				RegisterOn<ChangeEvent<bool>>(timelineEnabledToggle, e =>
				{
					TimelineControls?.SetEnabled(e.newValue);
					host.shellPreferences.timelineEnabled = e.newValue;
				});
				TimelineControls = new GraphTimelineControls(host, timelineHost);
				TimelineControls.Refresh();
			}

			if (host.ActionsAvailable)
			{
				// Supply a compact tool selector when the command belt cannot show its full hotbar.
				var beltRow = host.commandBelt.Q(className: "hotbar-row");
				beltRow.pickingMode = PickingMode.Position;
				host.taskNowLabel.parent.pickingMode = PickingMode.Position;
				host.shellToolField = new DropdownField { name = "shell-compact-tool" };
				host.shellToolField.AddToClassList("shell-tool-field");
				host.shellToolField.tooltip = "Equipped tool. Keys 1–8 also equip tools; 0 returns to Inspect.";
				beltRow.Insert(1, host.shellToolField);
				owned.Add(host.shellToolField);
				RegisterOn<ChangeEvent<string>>(host.shellToolField, e =>
				{
					if (host.shellUpdatingTools) return;
					var index = host.shellToolLabels.IndexOf(e.newValue);
					host.EquipToolAt(index);
				});
			}

			// The queue and host.inspector share one dock. Old floating-queue saves remain readable.
			host.commandFloating = false;
			host.shellWorkspaceMenu = Element("shell-workspace-menu");
			host.root.Add(host.shellWorkspaceMenu);
			owned.Add(host.shellWorkspaceMenu);
			foreach (var item in host.WorkspaceActions)
			{
				var action = item.Value;
				host.shellWorkspaceMenu.Add(Button(item.Key, () =>
				{
					CloseShellWorkspace();
					action();
				}));
			}

			RegisterRoot<GeometryChangedEvent>(_ => RefreshShellLayout());
			RegisterRoot<KeyDownEvent>(e =>
			{
				if (((e.ctrlKey || e.commandKey) && e.keyCode == KeyCode.F) ||
					(e.keyCode == KeyCode.Slash && !host.IsEditingText()))
				{
					host.nodeSearchField.Focus();
					e.StopImmediatePropagation();
					e.PreventDefault();
				}
			}, TrickleDown.TrickleDown);
			DisableWasdUiNavigation();
			RegisterRoot<PointerDownEvent>(e =>
			{
				if (!host.shellWorkspaceOpen) return;
				var hit = e.target as VisualElement;
				if (hit != null &&
					(host.shellWorkspaceMenu.Contains(hit) || host.shellWorkspaceButton.Contains(hit))) return;
				CloseShellWorkspace();
				host.shellConsumedPointerFrame = Time.frameCount;
				e.StopImmediatePropagation();
			}, TrickleDown.TrickleDown);
			host.shellReady = true;
			if (host.ActionsAvailable) host.RefreshShellToolChoice();
			RefreshShellLayout();
			host.RefreshShellSource();
			// Keep modal surfaces above the shell; world labels never receive empty-area input.
			host.tooltip.BringToFront();
			host.uiTip.BringToFront();
			host.hotbarTip?.BringToFront();
			host.loadingOverlay.BringToFront();
		}

		/// <summary>Opens a shell drawer by identifier.</summary>
		/// <param name="name">Drawer identifier.</param>
		public void ShowShellDrawer(string name)
		{
			// Toggle the requested drawer and release competing dock focus on narrower layouts.
			if (!host.shellReady) return;
			EndShellResize();
			CloseShellWorkspace();
			var next = host.shellDrawer == name ? "" : name;
			if (next.Length > 0)
			{
				if (host.root.resolvedStyle.width < 1400)
				{
					ReleaseShellFocus(host.rightColumn);
					host.commandPanelOpen = false;
					host.shellInspectorOpen = false;
				}
			}
			else
			{
				ReleaseShellFocus(host.sidebar);
			}

			host.shellDrawer = next;
			RefreshShellLayout();
		}

		/// <summary>Opens a shell dock tab.</summary>
		/// <param name="tab">Dock tab identifier.</param>
		public void ShowShellDock(string tab)
		{
			// Transfer focus away from hidden panels before activating the inspect or queue tab.
			if (tab == "Queue" && !host.ActionsAvailable) return;
			if (!host.shellReady) return;
			EndShellResize();
			CloseShellWorkspace();
			if (host.commandPanelOpen && tab != "Queue") ReleaseShellFocus(host.commandPanel);
			if (host.shellInspectorOpen && tab != "Inspect") ReleaseShellFocus(host.inspector);
			if (tab.Length > 0 && host.root.resolvedStyle.width < 1400)
			{
				ReleaseShellFocus(host.sidebar);
				host.shellDrawer = "";
			}

			if (tab.Length == 0) ReleaseShellFocus(host.rightColumn);
			host.shellInspectorOpen = tab == "Inspect";
			host.commandPanelOpen = tab == "Queue";
			if (host.commandPanelOpen) host.RefreshCommandPanel();
			// Hosts rebuild queue contents; the shell owns dock visibility and tab state.
			RefreshShellLayout();
		}

		/// <summary>Updates the header progress indicator.</summary>
		/// <param name="fraction">Optional progress fraction.</param>
		/// <param name="indeterminate">Whether to show indeterminate progress.</param>
		public void SetHeaderProgress(float? fraction, bool indeterminate = false)
		{
			// Switch between hidden, animated indeterminate, and bounded percentage progress states.
			var bar = host.root?.Q("shell-header-progress");
			var fill = bar?.Q("shell-header-progress-fill");
			if (bar == null || fill == null) return;
			var active = indeterminate || fraction.HasValue;
			bar.EnableInClassList("is-active", active);
			bar.EnableInClassList("is-indeterminate", indeterminate);
			if (!active) return;
			if (indeterminate)
			{
				fill.style.width = StyleKeyword.Null;
				bar.tooltip = "Working";
				return;
			}

			var clamped = Mathf.Clamp01(fraction.Value);
			fill.style.width = Length.Percent(clamped * 100f);
			bar.tooltip = Mathf.RoundToInt(clamped * 100f) + "%";
		}

		/// <summary>Toggles the action queue surface.</summary>
		public void ToggleShellQueue()
		{
			ShowShellDock(host.commandPanelOpen ? "" : "Queue");
		}

		/// <summary>Closes the active shell surface.</summary>
		/// <returns><see langword="true" /> when a surface was closed.</returns>
		public bool CloseShellSurface()
		{
			// Dismiss the most immediate open surface first so repeated escape actions unwind the shell.
			if (host.shellWorkspaceOpen)
			{
				CloseShellWorkspace();
				return true;
			}

			if (host.shellDrawer.Length > 0)
			{
				ShowShellDrawer("");
				return true;
			}

			if (host.commandPanelOpen || host.shellInspectorOpen)
			{
				ShowShellDock("");
				return true;
			}

			return false;
		}

		/// <summary>Releases graph focus after interaction with a shell container.</summary>
		/// <param name="container">Container whose pointer interaction ended.</param>
		public void ReleaseShellFocus(VisualElement container)
		{
			var focused = host.root.focusController?.focusedElement as VisualElement;
			if (focused == null || !(focused == container || container.Contains(focused))) return;
			focused.Blur();
			// Return to a visible rail control instead of trapping focus inside a hidden panel.
			host.shellRailButtons["Browse"].Focus();
		}

		/// <summary>Toggles the workspace menu.</summary>
		public void ToggleShellWorkspace()
		{
			// Place keyboard focus on the first workspace action when opening the menu.
			host.shellWorkspaceOpen = !host.shellWorkspaceOpen;
			RefreshShellLayout();
			if (host.shellWorkspaceOpen) host.shellWorkspaceMenu.Q<Button>()?.Focus();
		}

		/// <summary>Closes the workspace menu.</summary>
		public void CloseShellWorkspace()
		{
			if (!host.shellWorkspaceOpen) return;
			host.shellWorkspaceOpen = false;
			ReleaseShellFocus(host.shellWorkspaceMenu);
			host.shellWorkspaceMenu.style.display = DisplayStyle.None;
		}

		/// <summary>Applies and persists a shell UI scale.</summary>
		/// <param name="scale">Requested scale.</param>
		public void SetShellScale(float scale)
		{
			// Apply a finite supported panel scale and synchronize its field without recursive change events.
			host.shellPreferences.uiScale = float.IsFinite(scale) ? Mathf.Clamp(scale, 1, 1.5f) : 1;
			host.runtimePanelSettings.scale = host.shellPreferences.uiScale;
			host.shellScaleField?.SetValueWithoutNotify(Mathf.RoundToInt(host.shellPreferences.uiScale * 100) + "%");
			RefreshShellLayout();
			host.SaveShellPreferences();
		}

		/// <summary>Refreshes timeline controls when host timeline state changes.</summary>
		public void RefreshTimeline()
		{
			if (timelineEnabledToggle != null)
				timelineEnabledToggle.SetValueWithoutNotify(host.shellPreferences.timelineEnabled);
			TimelineControls?.Refresh();
		}

		/// <summary>Registers shell dock resizing interactions.</summary>
		public void AddShellDockResize()
		{
			// Capture the initial width for dragging and provide equivalent bounded keyboard resizing.
			var handle = Element("shell-resize");
			handle.name = "shell-dock-resize";
			handle.focusable = true;
			handle.tabIndex = 0;
			handle.tooltip =
				"Resize dock: drag, or focus and use Left/Right arrows. Home resets to 360. Range 320–480.";
			host.rightColumn.Add(handle);
			owned.Add(handle);
			float origin = 0, width = 360;
			RegisterOn<PointerDownEvent>(handle, e =>
			{
				if (e.button != 0) return;
				host.shellResizing = true;
				origin = e.position.x;
				width = host.shellPreferences.dockWidth;
				handle.CapturePointer(e.pointerId);
				handle.Focus();
				e.StopPropagation();
			});
			RegisterOn<PointerMoveEvent>(handle, e =>
			{
				if (!host.shellResizing) return;
				host.shellPreferences.dockWidth = Mathf.Clamp(width + origin - e.position.x, 320, 480);
				RefreshShellLayout();
				e.StopPropagation();
			});
			RegisterOn<PointerUpEvent>(handle, e =>
			{
				if (!host.shellResizing) return;
				host.shellResizing = false;
				handle.ReleasePointer(e.pointerId);
				host.SaveShellPreferences();
				e.StopPropagation();
			});
			RegisterOn<PointerCaptureOutEvent>(handle, _ =>
			{
				if (host.shellResizing)
				{
					host.shellResizing = false;
					host.SaveShellPreferences();
				}
			});
			RegisterOn<KeyDownEvent>(handle, e =>
			{
				if (e.keyCode != KeyCode.LeftArrow && e.keyCode != KeyCode.RightArrow &&
					e.keyCode != KeyCode.Home) return;
				host.shellPreferences.dockWidth = e.keyCode == KeyCode.Home
					? 360
					: Mathf.Clamp(host.shellPreferences.dockWidth + (e.keyCode == KeyCode.LeftArrow ? 16 : -16), 320,
								  480);
				RefreshShellLayout();
				host.SaveShellPreferences();
				e.StopImmediatePropagation();
				e.PreventDefault();
			});
		}

		/// <summary>Ends a shell dock resize operation.</summary>
		public void EndShellResize()
		{
			// Release any active drag before hiding or switching the dock and persist its final width.
			if (!host.shellResizing) return;
			host.shellResizing = false;
			var handle = host.root?.Q<VisualElement>("shell-dock-resize");
			if (handle != null && handle.HasPointerCapture(PointerId.mousePointerId))
				handle.ReleasePointer(PointerId.mousePointerId);
			host.SaveShellPreferences();
		}

		/// <summary>Recomputes shell layout after size or visibility changes.</summary>
		public void RefreshShellLayout()
		{
			// Recalculate reserved regions after dock, drawer, or scale changes.
			if (!host.shellReady || host.root.panel == null) return;
			float width = host.root.resolvedStyle.width, height = host.root.resolvedStyle.height;
			if (!float.IsFinite(width) || width < 100 || !float.IsFinite(height)) return;
			if (!host.ActionsAvailable) host.commandPanelOpen = false;

			// Keep drawers and the dock mutually exclusive when both would crowd the graph viewport.
			var dockOpen = host.commandPanelOpen || host.shellInspectorOpen;
			if (width < 1400 && dockOpen && host.shellDrawer.Length > 0)
			{
				ReleaseShellFocus(host.sidebar);
				host.shellDrawer = "";
			}

			host.root.EnableInClassList("shell-compact", width < 1000);
			host.root.EnableInClassList("reduced-motion", host.reducedMotion);

			// Refresh drawer visibility and active rail state independently from the dock tab contents.
			TimelineControls?.Refresh();
			host.sidebar.style.display = host.shellDrawer.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
			host.shellDrawerTitle.text = host.shellDrawer;
			foreach (var drawer in customDrawers)
			{
				drawer.Content.style.display = host.shellDrawer == drawer.Id ? DisplayStyle.Flex : DisplayStyle.None;
				if (host.shellDrawer == drawer.Id) host.shellDrawerTitle.text = drawer.Title;
			}

			host.shellSourceBody.style.display = host.shellDrawer == "Source" ? DisplayStyle.Flex : DisplayStyle.None;
			host.shellFiltersBody.style.display = host.shellDrawer == "Filters" ? DisplayStyle.Flex : DisplayStyle.None;
			host.shellViewBody.style.display = host.shellDrawer == "View" ? DisplayStyle.Flex : DisplayStyle.None;
			host.shellBrowseBody.style.display = host.shellDrawer == "Browse" ? DisplayStyle.Flex : DisplayStyle.None;
			host.shellFavoritesBody.style.display =
				host.shellDrawer == "Favorites" ? DisplayStyle.Flex : DisplayStyle.None;
			if (host.favoritesList != null) host.favoritesList.style.height = Mathf.Max(140, height - 190);
			host.resultList.style.height = Mathf.Max(140, height - 190);
			foreach (var entry in host.shellRailButtons)
				entry.Value.EnableInClassList("is-active", entry.Key == host.shellDrawer);
			host.rightColumn.style.display = dockOpen ? DisplayStyle.Flex : DisplayStyle.None;
			host.rightColumn.style.width = host.shellPreferences.dockWidth;
			host.inspector.style.display = host.shellInspectorOpen && !host.commandPanelOpen
				? DisplayStyle.Flex
				: DisplayStyle.None;
			if (host.commandPanel != null)
				host.commandPanel.style.display = host.commandPanelOpen ? DisplayStyle.Flex : DisplayStyle.None;
			host.shellQueueTab.style.display = host.ActionsAvailable ? DisplayStyle.Flex : DisplayStyle.None;
			host.shellInspectTab.EnableInClassList("is-active", !host.commandPanelOpen);
			host.shellQueueTab.EnableInClassList("is-active", host.commandPanelOpen);
			if (host.shellQueueBadge != null) host.shellQueueBadge.text = host.DraftCount.ToString();
			host.shellWorkspaceMenu.style.display = host.shellWorkspaceOpen ? DisplayStyle.Flex : DisplayStyle.None;
			if (host.shellWorkspaceOpen) host.shellWorkspaceMenu.BringToFront();
			var area = ShellGraphRect();

			// Align command and count overlays with the graph area left after visible side panels.
			if (host.commandBelt != null)
				host.commandBelt.style.display = host.ActionsAvailable ? DisplayStyle.Flex : DisplayStyle.None;
			if (host.ActionsAvailable)
			{
				host.commandBelt.style.left = area.xMin;
				host.commandBelt.style.right = Mathf.Max(12, width - area.xMax);
				var compactTools = area.width < 600;
				host.hotbar.style.display = compactTools ? DisplayStyle.None : DisplayStyle.Flex;
				host.shellToolField.style.display = compactTools && host.shellToolLabels.Count > 1
					? DisplayStyle.Flex
					: DisplayStyle.None;
			}

			host.shellCounts.style.left = area.xMin;
			host.shellCounts.style.right = Mathf.Max(12, width - area.xMax);
			if (host.ActionsAvailable) host.RefreshShellTaskHud();
		}

		/// <summary>Gets the screen-space rectangle reserved for the graph.</summary>
		/// <returns>The graph display rectangle.</returns>
		public Rect ShellGraphRect()
		{
			// Reserve side-panel widths with screen-size fallbacks before root geometry becomes available.
			float width = host.root?.resolvedStyle.width ?? Screen.width,
				  height = host.root?.resolvedStyle.height ?? Screen.height;
			if (!float.IsFinite(width) || width < 1) width = Screen.width;
			if (!float.IsFinite(height) || height < 1) height = Screen.height;
			float left = host.shellReady && host.shellDrawer.Length > 0 ? 392 : 84;
			var right = width - (host.shellReady && (host.commandPanelOpen || host.shellInspectorOpen)
				? host.shellPreferences.dockWidth + 32
				: 20);
			return Rect.MinMaxRect(left, 100, Mathf.Max(left + 160, right), Mathf.Max(180, height - 116));
		}

		/// <summary>Queues a delayed node search query.</summary>
		/// <param name="query">Search query to submit.</param>
		public void QueueShellSearch(string query)
		{
			// Debounce filter application while retaining only the latest pending query.
			host.shellPendingSearch = query;
			if (host.root == null) return;
			if (host.shellSearchSchedule == null)
				host.shellSearchSchedule = host.root.schedule.Execute(() =>
				{
					host.Query = host.shellPendingSearch;
					host.ApplyFilters();
				}).StartingIn(150);
			else host.shellSearchSchedule.ExecuteLater(150);
		}

		/// <summary>Cancels the pending node search update.</summary>
		public void CancelShellSearch()
		{
			host.shellSearchSchedule?.Pause();
			host.shellPendingSearch = host.Query ?? "";
		}

		private void DisableWasdUiNavigation()
		{
			// WASD is reserved for RMB flight. Arrow keys step the timeline and must not move UI focus.
			RegisterRoot<KeyDownEvent>(e =>
			{
				if (e.keyCode is not (KeyCode.W or KeyCode.A or KeyCode.S or KeyCode.D)) return;
				if (host.IsEditingText()) return;
				e.StopImmediatePropagation();
				e.PreventDefault();
			}, TrickleDown.TrickleDown);
			RegisterRoot<NavigationMoveEvent>(e =>
			{
				if (e.direction is NavigationMoveEvent.Direction.Left or NavigationMoveEvent.Direction.Right
					or NavigationMoveEvent.Direction.Up or NavigationMoveEvent.Direction.Down)
				{
					e.StopImmediatePropagation();
					e.PreventDefault();
					return;
				}

				if (host.IsEditingText()) return;
				var keyboard = Keyboard.current;
				if (keyboard == null) return;
				if (!keyboard.wKey.isPressed && !keyboard.aKey.isPressed && !keyboard.sKey.isPressed &&
					!keyboard.dKey.isPressed) return;
				e.StopImmediatePropagation();
				e.PreventDefault();
			}, TrickleDown.TrickleDown);
		}
	}
}