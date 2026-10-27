using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace HackThePlanet.Editor
{
	/// <summary>UI Toolkit window for editing authoritative JSON content with validation and local history.</summary>
	public sealed class ContentEditorWindow : EditorWindow
	{
		// Editor preference key that stores whether property descriptions are shown inline.
		private const string DescriptionsPref = "HackThePlanet.ContentEditor.ShowDescriptions";

		// Category tab groups shown across the top of the editor, each with its ordered categories.
		private static readonly (string Name, string[] Items)[] TabGroups =
		{
			("Setup", new[] { "Game", "Career", "Rewards" }),
			("Rig", new[] { "Rig Rules", "Chassis", "Modules" }),
			("Gameplay", new[] { "Actions", "Technologies", "Hardware", "Statuses", "Node Types" }),
			("World", new[] { "Companies", "Contracts", "Networks" }),
			("Presentation", new[] { "Text Palette", "Graph Visuals", "Graph Labels" })
		};

		// Property names that use a multi-line text field instead of a single-line one.
		private static readonly string[] LongTextFields =
			{ "description", "text", "aftermath", "narrative", "welcome" };

		// Property names that hold a numeric value, input, or operation field in formulas.
		private static readonly string[] ValueFieldNames = { "value", "input", "stat", "operation", "state", "key" };

		[SerializeField] private string category = "Contracts", selectedKey, search = "", revisionName = "";
		[SerializeField] private float splitWidth = 280;

		[SerializeField] private ContentDraft draft;

		// Field names whose inline help foldouts are expanded in the form.
		private readonly HashSet<string> openHelp = new();

		// Maps validation property paths to highlighted form elements.
		private readonly Dictionary<string, VisualElement> propertyTargets = new();

		// Rule foldout headers pending summary text updates after edits.
		private readonly List<(Foldout Fold, string Label, object Value)> ruleHeaders = new();

		// Manifest keys currently shown in the sidebar list after filtering.
		private readonly List<string> visibleKeys = new();

		// Top banner showing validation errors or save prompts.
		private VisualElement banner;

		// Scrollable inspector for the selected manifest entry.
		private ScrollView form;

		// Local revision history store for the active data directory.
		private ContentHistory history;

		// Timestamp of the last successful save, if any.
		private DateTime? lastSaved;

		// Most recent validation error messages for banner navigation.
		private List<string> lastValidation;

		// Sidebar list of manifest entry keys.
		private ListView list;

		// Active module family and tier filters for the rig shop list.
		private string moduleFamily = "All", moduleTier = "All";

		// Whether the rig preview footprint is shown rotated 90 degrees.
		private bool previewRotated;

		// Guards against recursive UI rebuilds while mutating the draft.
		private bool rebuilding;

		// Toolbar buttons for save-all, validate, and description toggles.
		private Button saveAllButton, validateButton, descriptionsButton;

		// Disk snapshot taken before overwrite review for conflict detection.
		private ContentDraftFile[] savedBaseline;

		// Status bar labels for messages, counts, and unsaved markers.
		private Label statusLabel, countLabel, headerUnsaved;

		// When true, the form highlights validation failures instead of editing freely.
		private bool validationMode;

		// Live JSON workspace backing the draft and bundle.
		private ContentWorkspace workspace;

		// Gets the authoritative JSON directory for the active workspace.
		private string DataDirectory => workspace?.DirectoryPath ?? GameData.DirectoryPath;

		// Gets the Library path where an emergency draft recovery file is stored.
		private string RecoveryPath =>
			Path.Combine(Path.GetDirectoryName(GameData.DirectoryPath), "Library/ContentEditor/draft.json");

		// Gets whether inline field descriptions are shown in the property form.
		private static bool ShowDescriptions => EditorPrefs.GetBool(DescriptionsPref, false);

		// Gets the manifest entry matching the currently selected key, if any.
		private ContentEntry Selected => workspace?.Bundle.Manifest.entries.FirstOrDefault(e => e.key == selectedKey);

		// Builds the UI, workspace, and event subscriptions when the window opens.
		private void OnEnable()
		{
			// Enforce the minimum window size.
			minSize = new Vector2(850, 500);

			// Subscribe to undo and play-mode notifications.
			Undo.undoRedoPerformed += OnUndo;
			EditorApplication.playModeStateChanged += OnPlayMode;
		}

		// Unsubscribes, caches UI state, and disposes the workspace when the window closes.
		private void OnDisable()
		{
			// Unsubscribe from the notifications subscribed in OnEnable.
			Undo.undoRedoPerformed -= OnUndo;
			EditorApplication.playModeStateChanged -= OnPlayMode;

			// Persist the cached UI state and release the workspace.
			Cache();
			workspace?.Dispose();
			workspace = null;
		}

		/// <summary>Builds or rebuilds the editor UI, loading workspace state and recovering interrupted saves.</summary>
		public void CreateGUI()
		{
			// Skip building the UI while headless validation runs.
			if (validationMode)
				return;

			// Attach the shared editor styles to the root element.
			ContentEditorUI.Attach(rootVisualElement);

			// Load the history, recover interrupted saves, and build the window.
			try
			{
				history = new ContentHistory(GameData.DirectoryPath);
				history.Recover();

				// Create the draft on first open and restore any unsaved recovery file.
				if (draft == null)
				{
					draft = CreateInstance<ContentDraft>();
					draft.hideFlags = HideFlags.HideAndDontSave;
					if (File.Exists(RecoveryPath))
						JsonUtility.FromJsonOverwrite(File.ReadAllText(RecoveryPath), draft);
				}

				// Recreate the workspace over the draft and remember the saved baseline.
				workspace?.Dispose();
				workspace = new ContentWorkspace(draft, GameData.DirectoryPath);
				savedBaseline = draft.originals;

				// Draw the full window from the loaded workspace.
				DrawWindow();
			}
			catch (Exception ex)
			{
				// Replace the UI with an error message and recovery actions.
				rootVisualElement.Clear();
				rootVisualElement.Add(new HelpBox(ex.Message, HelpBoxMessageType.Error));
				rootVisualElement.Add(new Button(() => EditorUtility.RevealInFinder(GameData.DirectoryPath))
				{
					text = "Open data directory", tooltip = "Open the data folder in Explorer"
				});
				rootVisualElement.Add(new Button(CreateGUI)
										  { text = "Retry loading", tooltip = "Load the JSON content again" });
			}
		}

		/// <summary>Opens or focuses the Content Editor window.</summary>
		[MenuItem("Hack The Planet/Content Editor")]
		public static void Open()
		{
			// Reuse the existing editor window or create one.
			var window = GetWindow<ContentEditorWindow>();
			window.titleContent = new GUIContent("Content Editor");

			// Position a floating window beside the main Unity window.
			if (!window.docked)
			{
				var main = EditorGUIUtility.GetMainWindowPosition();
				window.position = new Rect(main.x + 100, main.y + 100, Math.Min(1400, Math.Max(850, main.width - 200)),
										   Math.Min(850, Math.Max(500, main.height - 200)));
			}

			// Show and focus the window.
			window.Show();
			window.Focus();
		}

		// Assembles the toolbar, sidebar, form, and status bar for the content editor.
		private void DrawWindow()
		{
			// Suppress selection callbacks while the window is rebuilt.
			rebuilding = true;
			rootVisualElement.Clear();
			countLabel = null;
			headerUnsaved = null;
			rootVisualElement.AddToClassList("ctp-root");

			// Build the top chrome with the category tabs and toolbar.
			var chrome = new VisualElement();
			chrome.AddToClassList("ctp-chrome");
			chrome.style.flexDirection = FlexDirection.Column;
			chrome.style.flexShrink = 0;
			chrome.Add(BuildCategoryTabs());
			var toolbarHost = new VisualElement();
			toolbarHost.AddToClassList("ctp-toolbar-host");
			toolbarHost.Add(BuildToolbar());
			chrome.Add(toolbarHost);
			rootVisualElement.Add(chrome);

			// Create the banner that reports validation and save messages.
			banner = new VisualElement();
			banner.AddToClassList("ctp-banner");
			rootVisualElement.Add(banner);

			// Create the scrollable form that edits the selected entry.
			form = new ScrollView();
			form.AddToClassList("ctp-form");
			form.style.flexGrow = 1;
			form.style.paddingLeft = form.style.paddingRight = 12;

			// Create the entry list and route selection changes to the form.
			list = new ListView
				{ itemsSource = visibleKeys, fixedItemHeight = 36, selectionType = SelectionType.Single };
			list.style.flexGrow = 1;
			list.makeItem = BuildItem;
			list.bindItem = (element, index) => BindItem(element, visibleKeys[index]);
			list.selectionChanged += values =>
			{
				// Ignore selection changes caused by rebuilding the list.
				if (rebuilding)
					return;

				selectedKey = values.OfType<string>().FirstOrDefault();
				ShowEntry();
			};

			// Show the sidebar beside the form only when the category has more than one entry.
			if (EntriesFor(category).Count() > 1)
			{
				// Create a resizable split between the sidebar and the form.
				var split = new TwoPaneSplitView(0, splitWidth, TwoPaneSplitViewOrientation.Horizontal);
				split.style.flexGrow = 1;

				// Place the sidebar and form into the split.
				split.Add(BuildSidebar());
				split.Add(form);
				rootVisualElement.Add(split);
			}
			else
			{
				rootVisualElement.Add(form);
			}

			// Add the status bar at the bottom of the window.
			rootVisualElement.Add(BuildStatusBar());

			// Refresh the list and form, then clear the rebuild flag.
			RefreshList();
			rebuilding = false;
			ShowEntry();
			RenderBanner();
			UpdateStatus();
		}

		// Builds the grouped category tab strip across the top of the editor.
		private VisualElement BuildCategoryTabs()
		{
			// Create the horizontally scrolling tab row.
			var scroll = new ScrollView(ScrollViewMode.Horizontal);
			scroll.AddToClassList("ctp-tabs");
			scroll.style.flexShrink = 0;
			scroll.horizontalScrollerVisibility = ScrollerVisibility.Auto;
			scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
			var row = new VisualElement();
			row.AddToClassList("ctp-tab-row");
			scroll.Add(row);

			// Build one labeled box per tab group.
			foreach (var group in TabGroups)
			{
				var box = new VisualElement();
				box.AddToClassList("ctp-tab-group");
				row.Add(box);

				// Add the group title above its tab strip.
				var title = new Label(group.Name);
				title.AddToClassList("ctp-group-label");
				box.Add(title);
				var strip = new VisualElement();
				strip.AddToClassList("ctp-tab-strip");
				box.Add(strip);

				// Create one tab button per category in the group.
				foreach (var name in group.Items)
				{
					// Capture the tab name and its entry count for the tooltip.
					var tab = name;
					var count = EntriesFor(tab).Count();

					// Selecting a tab switches category, clears the selection, and redraws.
					var button = new Button(() =>
					{
						category = tab;
						selectedKey = null;
						DrawWindow();
					}) { text = tab, tooltip = tab + " (" + count + (count == 1 ? " entry)" : " entries)") };

					// Mark the tab active when it matches the current category.
					button.AddToClassList("ctp-tab");
					button.EnableInClassList("ctp-tab--active", category == tab);
					ContentEditorUI.Decorate(button, true, ContentEditorUI.CategoryIconNames(tab));
					strip.Add(button);
				}
			}

			return scroll;
		}

		// Builds save, validate, search, and navigation controls in the main toolbar.
		private Toolbar BuildToolbar()
		{
			// Create the toolbar and check whether the category allows only one definition.
			var toolbar = new Toolbar();
			var singleton = ContentWorkspace.Singleton(category);

			// Add the New button, disabled for singleton categories.
			toolbar.Add(ToolButton("New", "New", "Create a new " + category + " definition with a unique ID and file.",
								   () => Mutate(() => { selectedKey = workspace.Add(category).key; }, Redraw.Window),
								   !singleton, "Toolbar Plus"));

			// Add the Duplicate button, enabled only when an entry is selected.
			toolbar.Add(ToolButton("Duplicate", "Duplicate",
								   "Copy the selected definition with a new ID and file. Outgoing references are kept.",
								   () => Mutate(
									   () => { selectedKey = workspace.Add(Selected.category, selectedKey).key; },
									   Redraw.Window),
								   Selected != null && !ContentWorkspace.Singleton(Selected.category), "Clipboard"));

			// Add the Delete button, enabled only for a selected non-singleton entry.
			toolbar.Add(ToolButton("Delete", "Delete",
								   "Delete the selected definition. Blocked while other content still references it.",
								   Delete, Selected != null && !ContentWorkspace.Singleton(Selected.category),
								   "TreeEditor.Trash"));

			// Separate the editing buttons from the save buttons.
			toolbar.Add(Separator());

			// Add the Save button for the selected entry only.
			toolbar.Add(ToolButton("Save", "Save",
								   "Save only the selected entry. Other changed entries stay as drafts.",
								   () => RequestSave(false), true, "SaveAs"));

			// Add the Save All button for every changed entry.
			saveAllButton = ToolButton("SaveAll", "Save All",
									   "Save every changed entry, including structural edits, and create revisions.",
									   () => RequestSave(true), true, "SaveAs");
			toolbar.Add(saveAllButton);

			// Separate the save buttons from the validation and asset buttons.
			toolbar.Add(Separator());

			// Add the Validate button that checks the proposed catalog.
			validateButton = ToolButton("Validate", "Validate",
										"Check the complete proposed catalog and property descriptions. Click a message to jump to it.",
										Validate, true, "d_Valid", "Valid");
			toolbar.Add(validateButton);

			// Add the Generate Assets button that mirrors saved JSON into Unity assets.
			toolbar.Add(ToolButton("GenerateAssets", "Generate Assets",
								   "Generate Unity asset mirrors from saved, validated JSON.",
								   GameplayAssetGenerator.Generate, true, "Refresh"));

			// Add the History button that opens the revision browser.
			toolbar.Add(ToolButton("History", "History",
								   "Browse saved revisions and restore an entry or a transaction to the draft.",
								   () => ContentHistoryWindow.Show(history, workspace, selectedKey, Restore), true,
								   "d_UnityEditor.HistoryWindow", "UnityEditor.HistoryWindow"));

			// Add the Reload Disk button that discards unsaved drafts.
			toolbar.Add(ToolButton("Reload", "Reload Disk",
								   "Discard every unsaved draft and reload all JSON files from disk.", ReloadDisk, true,
								   "Refresh"));

			// Separate the reload button from the overflow menu.
			toolbar.Add(Separator());

			// Build the More menu with the less common actions.
			var more = new ToolbarMenu { text = "More", tooltip = "Less common actions" };
			more.menu.AppendAction("Revert selected entry to saved version", _ => Revert(),
								   _ => Selected != null
									   ? DropdownMenuAction.Status.Normal
									   : DropdownMenuAction.Status.Disabled);
			toolbar.Add(more);

			// Add a flexible spacer that pushes the Descriptions toggle to the right.
			var spacer = new VisualElement();
			spacer.AddToClassList("ctp-spacer");
			toolbar.Add(spacer);

			// Add the Descriptions toggle that shows property help inline.
			descriptionsButton = ToolButton("Descriptions", "Descriptions",
											"Show every property description inline. When off, use the ? button on a property or hover over its label.",
											ToggleDescriptions, true, "_Help");
			toolbar.Add(descriptionsButton);

			return toolbar;
		}

		// Builds the searchable manifest list and module filters in the left pane.
		private VisualElement BuildSidebar()
		{
			// Create the sidebar and give it a minimum width.
			var sidebar = new VisualElement();
			sidebar.style.minWidth = 180;
			sidebar.AddToClassList("ctp-sidebar");

			// Remember the sidebar width when the split pane is resized.
			sidebar.RegisterCallback<GeometryChangedEvent>(e =>
			{
				if (e.newRect.width > 0)
					splitWidth = e.newRect.width;
			});

			// Build the sidebar header with the category title and entry count.
			var head = new VisualElement();
			head.AddToClassList("ctp-sidebar-head");
			sidebar.Add(head);
			var title = new Label(category);
			title.AddToClassList("ctp-sidebar-title");
			head.Add(title);

			// Create the label that shows how many entries are visible.
			countLabel = new Label();
			countLabel.AddToClassList("ctp-count");
			head.Add(countLabel);

			// Add the search field that filters the entry list.
			var filter = new ToolbarSearchField { value = search, tooltip = "Search by title, ID, or file path" };
			filter.RegisterValueChangedCallback(e =>
			{
				search = e.newValue;
				RefreshList();
			});
			sidebar.Add(filter);

			// Add the family and tier filters for the Modules category.
			if (category == "Modules")
			{
				// Build the family choices from the loaded modules.
				var families = new[] { "All" }.Concat(workspace.Bundle.Game.modules.Select(m => m.family).Distinct())
											  .ToList();

				// Create the family popup and refresh the list when it changes.
				var family = new PopupField<string>("Family", families,
													families.Contains(moduleFamily) ? moduleFamily : "All");
				family.RegisterValueChangedCallback(e =>
				{
					moduleFamily = e.newValue;
					RefreshList();
				});
				sidebar.Add(family);

				// Build the tier choices from the loaded modules.
				var tiers = new[] { "All" }
							.Concat(workspace.Bundle.Game.modules.Select(m => m.tier.ToString()).Distinct()).ToList();

				// Create the tier popup and refresh the list when it changes.
				var tier = new PopupField<string>("Tier", tiers, tiers.Contains(moduleTier) ? moduleTier : "All");
				tier.RegisterValueChangedCallback(e =>
				{
					moduleTier = e.newValue;
					RefreshList();
				});
				sidebar.Add(tier);
			}

			// Add the entry list below the filters.
			sidebar.Add(list);

			// Create the row of move up and move down buttons that reorder the selected entry.
			var order = new VisualElement();
			order.AddToClassList("ctp-order-row");
			sidebar.Add(order);
			order.Add(ContentEditorUI.IconButton("↑ Move up", "Move the selected entry earlier in authored order.",
												 () => Mutate(() => workspace.Move(selectedKey, -1), Redraw.Window)));
			order.Add(ContentEditorUI.IconButton("↓ Move down", "Move the selected entry later in authored order.",
												 () => Mutate(() => workspace.Move(selectedKey, 1), Redraw.Window)));

			return sidebar;
		}

		// Builds the bottom status row showing counts and save state.
		private VisualElement BuildStatusBar()
		{
			// Create the status bar container.
			var bar = new VisualElement();
			bar.AddToClassList("ctp-statusbar");

			// Create the status text label inside the bar.
			statusLabel = new Label();
			statusLabel.AddToClassList("ctp-statusbar-text");
			bar.Add(statusLabel);

			return bar;
		}

		// Creates one sidebar list row template for a manifest entry key.
		private VisualElement BuildItem()
		{
			// Create the row container for one list entry.
			var row = new VisualElement();
			row.AddToClassList("ctp-item");

			// Build the status column with the warning icon, glyph, and issue count.
			var status = new VisualElement();
			status.AddToClassList("ctp-item-status");
			row.Add(status);
			var icon = new Image
			{
				name = "warnIcon", image = ContentEditorUI.FindIcon("console.erroricon.sml", "console.erroricon"),
				pickingMode = PickingMode.Ignore
			};
			icon.AddToClassList("ctp-icon");
			status.Add(icon);
			var glyph = new Label("!") { name = "warnText" };
			glyph.AddToClassList("ctp-error-glyph");
			status.Add(glyph);
			var count = new Label { name = "issueCount" };
			count.AddToClassList("ctp-error-glyph");
			status.Add(count);

			// Build the text column with the title and subtitle.
			var texts = new VisualElement();
			texts.AddToClassList("ctp-item-text");
			row.Add(texts);
			var title = new Label { name = "title" };
			title.AddToClassList("ctp-item-title");
			texts.Add(title);
			var sub = new Label { name = "sub" };
			sub.AddToClassList("ctp-item-sub");
			texts.Add(sub);

			// Add the unsaved-changes dot on the right edge.
			var dirty = new Label("●") { name = "dirty" };
			dirty.AddToClassList("ctp-dirty");
			row.Add(dirty);

			return row;
		}

		// Binds labels, icons, and dirty markers for one sidebar list row.
		private void BindItem(VisualElement element, string key)
		{
			// Skip binding when no workspace is loaded.
			if (workspace == null)
				return;

			// Resolve the entry, its definition, and its validation issue count.
			var entry = workspace.Bundle.Manifest.entries.Single(e => e.key == key);
			var definition = workspace.Bundle.Objects[key] as GameplayDefinition;
			var issues = EntryErrors(entry).Count();

			// Show the definition title, falling back to the file name.
			element.Q<Label>("title").text = definition != null && !string.IsNullOrWhiteSpace(definition.title)
				? definition.title
				: Path.GetFileNameWithoutExtension(entry.file);

			// Show module specifications for modules, or the ID for other definitions.
			element.Q<Label>("sub").text = definition is RigModuleDefinition module
				? module.family + " / T" + module.tier + " / " + module.width + "x" + module.height + " / " +
				  module.price + " cr"
				: definition?.id ?? entry.file;

			// Show the warning icon when an icon exists, otherwise the text glyph.
			var icon = element.Q<Image>("warnIcon");
			var glyph = element.Q<Label>("warnText");
			var showIssue = issues > 0;
			icon.style.display = showIssue && icon.image != null ? DisplayStyle.Flex : DisplayStyle.None;
			glyph.style.display = showIssue && icon.image == null ? DisplayStyle.Flex : DisplayStyle.None;

			// Show the issue count only when there are issues.
			var count = element.Q<Label>("issueCount");
			count.text = showIssue ? issues.ToString(CultureInfo.InvariantCulture) : "";
			count.style.display = showIssue ? DisplayStyle.Flex : DisplayStyle.None;

			// Show the unsaved dot when the file differs from its saved version, and set the tooltip.
			element.Q<Label>("dirty").style.display = Dirty(entry.file) ? DisplayStyle.Flex : DisplayStyle.None;
			element.tooltip = showIssue ? issues + " issue(s). Run Validate for details." : null;
		}

		// Creates a styled toolbar button wired to an action with an enabled state.
		private ToolbarButton ToolButton(string name, string label, string tooltip, Action action, bool enabled,
										 params string[] icons)
		{
			// Create the toolbar button, set its enabled state, and decorate it with icons.
			var button = new ToolbarButton(action) { text = label, name = name, tooltip = tooltip };
			button.SetEnabled(enabled);

			return ContentEditorUI.Decorate(button, true, icons);
		}

		// Creates a vertical toolbar separator between button groups.
		private static VisualElement Separator()
		{
			// Create a thin divider between toolbar groups.
			var separator = new VisualElement();
			separator.AddToClassList("ctp-sep");
			return separator;
		}

		// Returns manifest entries belonging to the selected category tab.
		private IEnumerable<ContentEntry> EntriesFor(string tab)
		{
			// Return the entries for a tab, folding Network Visuals into Graph Visuals.
			return workspace.Bundle.Manifest.entries.Where(e => e.category == tab ||
																(tab == "Graph Visuals" &&
																 e.category == "Network Visuals"));
		}

		// Returns whether a draft file differs from its accepted baseline.
		private bool Dirty(string file)
		{
			// Compare the draft text with the saved baseline for this file.
			return !draft.Original.TryGetValue(file, out var old) ||
				   !draft.Current.TryGetValue(file, out var next) || old != next;
		}

		// Returns validation messages affecting the given manifest entry.
		private IEnumerable<string> EntryErrors(ContentEntry entry)
		{
			// Gather load and validation messages that mention this entry's file.
			return workspace.Bundle.Errors.Concat(lastValidation ?? new List<string>())
							.Where(e => e.Contains(entry.file)).Distinct();
		}

		// Rebuilds the filtered sidebar list and selection after category or search changes.
		private void RefreshList()
		{
			// Nothing to refresh until a workspace is loaded.
			if (workspace == null)
				return;

			// Suppress selection callbacks while the list is rebuilt.
			var previous = rebuilding;
			rebuilding = true;
			visibleKeys.Clear();

			// Collect the entries for the current category.
			var entries = EntriesFor(category).ToList();

			// Keep only the entries that pass the module filters and the search text.
			foreach (var entry in entries)
			{
				// Read the definition behind this entry.
				var obj = workspace.Bundle.Objects[entry.key] as GameplayDefinition;

				// Skip modules that do not match the family or tier filters.
				if (obj is RigModuleDefinition module && ((moduleFamily != "All" && module.family != moduleFamily) ||
														  (moduleTier != "All" &&
														   module.tier.ToString() != moduleTier)))
					continue;

				// Keep entries whose title, ID, or file path matches the search text.
				if (string.IsNullOrWhiteSpace(search) ||
					((obj?.title ?? "") + " " + (obj?.id ?? "") + " " + entry.file).IndexOf(
						search, StringComparison.OrdinalIgnoreCase) >= 0)
					visibleKeys.Add(entry.key);
			}

			// Select the first visible entry when the current selection is hidden.
			if (!visibleKeys.Contains(selectedKey))
				selectedKey = visibleKeys.FirstOrDefault();

			// Show the visible and total entry counts in the sidebar header.
			if (countLabel != null)
				countLabel.text = visibleKeys.Count == entries.Count
					? entries.Count.ToString()
					: visibleKeys.Count + " of " + entries.Count;

			// Rebuild the list and restore the selected row without firing callbacks.
			if (list != null)
			{
				list.Rebuild();
				if (selectedKey != null)
					list.SetSelectionWithoutNotify(new[] { visibleKeys.IndexOf(selectedKey) });
			}

			// Restore the previous rebuild flag.
			rebuilding = previous;
		}

		// Renders the inspector form for the selected entry and optional rig preview.
		private void ShowEntry(bool keepScroll = false)
		{
			// Nothing to draw until the form exists.
			if (form == null)
				return;

			// Remember the scroll offset and clear the previous form contents.
			var scroll = form.scrollOffset.y;
			form.Clear();
			propertyTargets.Clear();
			ruleHeaders.Clear();
			headerUnsaved = null;

			// Resolve the selected entry and set the Duplicate and Delete buttons to match it.
			var entry = Selected;
			var manageable = entry != null && !ContentWorkspace.Singleton(entry.category);
			rootVisualElement.Q<ToolbarButton>("Duplicate")?.SetEnabled(manageable);
			rootVisualElement.Q<ToolbarButton>("Delete")?.SetEnabled(manageable);

			// Show a placeholder when no entry is selected.
			if (entry == null)
			{
				form.Add(new Label("No entries. Create a new definition or clear the search filter."));
				return;
			}

			// Resolve the selected object and add its header.
			var obj = workspace.Bundle.Objects[entry.key];
			form.Add(BuildHeader(entry, obj as GameplayDefinition));

			// Show decode errors and stop when the file could not be read.
			if (workspace.Bundle.InvalidEntries.Contains(entry.key))
			{
				// Show the decode errors recorded for this file.
				form.Add(new HelpBox(
							 string.Join(
								 "\n",
								 workspace.Bundle.Errors.Where(e => e.StartsWith(entry.file + ":",
																   StringComparison.Ordinal))),
							 HelpBoxMessageType.Error));

				// Explain how to recover without replacing the file with defaults.
				form.Add(new Label(
							 "This file could not be decoded. Fix it externally, then Reload Disk. Its contents will not be replaced by defaults."));
				return;
			}

			// Draw the rig preview, then the property editors for the object.
			DrawRigPreview(form, obj);
			DrawObject(form, obj, "$", 0);

			// Restore the scroll offset when the caller asked to keep it.
			if (keepScroll)
				form.schedule.Execute(() => form.scrollOffset = new Vector2(0, scroll));
		}

		// Runs headless rig authoring mutations in the given directory for validation tests.
		internal static Dictionary<string, string> ExerciseRigAuthoring(string directory)
		{
			// Create a hidden validation window that edits the data in the given directory.
			var window = CreateInstance<ContentEditorWindow>();
			window.validationMode = true;
			window.Show();

			// Create the draft that holds the validation edits.
			var draft = CreateInstance<ContentDraft>();
			try
			{
				// Attach the workspace, history, and styles to the validation window.
				window.draft = draft;
				window.workspace = new ContentWorkspace(draft, directory);
				window.history = new ContentHistory(directory);
				window.savedBaseline = draft.originals;
				ContentEditorUI.Attach(window.rootVisualElement);

				// Local helper that selects an entry by category and gameplay id, then redraws the form.
				void Select(string category, string id)
				{
					// Find the entry whose definition has the requested id.
					window.category = category;
					window.selectedKey = window.workspace.Bundle.Manifest.entries
											   .Single(e => e.category == category &&
															((GameplayDefinition)window.workspace.Bundle.Objects[e.key])
															.id == id).key;

					// Redraw the form for the selected entry.
					window.DrawWindow();
				}

				// Local helper that sets a float property field by name.
				void Float(string field, float value)
				{
					window.propertyTargets["$." + field].Q<FloatField>().value = value;
				}

				// Local helper that sets an integer property field by name.
				void Integer(string field, int value)
				{
					window.propertyTargets["$." + field].Q<IntegerField>().value = value;
				}

				// Edit the power module's capacity.
				Select("Modules", "power-1");
				Float("powerCapacity", 9);

				// Edit the cooling module's capacity.
				Select("Modules", "cooling-1");
				Float("coolingCapacity", 7);

				// Edit the chassis storage capacity.
				Select("Chassis", "chassis-1");
				Float("storageCapacity", 81);

				// Edit the backdoor action's memory cost.
				Select("Actions", "backdoor");
				Float("memoryCost", 11);

				// Edit the rig rules memory reclaim rate.
				Select("Rig Rules", "rig");
				Float("memoryReclaim", 5);

				// Edit the recycler module's price.
				Select("Modules", "recycler");
				Integer("price", 401);

				// Edit the drive module's first modifier value.
				Select("Modules", "drive");
				window.propertyTargets["$.modifiers[0].value.value"].Q<FloatField>().value = 21;

				// Edit the storage array module's nested modifier value.
				Select("Modules", "storage-array");
				window.propertyTargets["$.modifiers[1].value.left.right.value"].Q<FloatField>().value = 3;

				// Edit the corporate network's payload storage formula.
				Select("Networks", "corporate-network");
				window.propertyTargets["$.payloadStorage.right.value"].Q<FloatField>().value = 25;

				// Edit the RAM module's footprint width.
				Select("Modules", "ram");
				Integer("width", 2);

				// Restore the starter footprint before validating the full draft.
				Integer("width", 1);

				// Capture the edited workspace and return the resulting draft files.
				window.workspace.Capture();
				return draft.Current;
			}
			finally
			{
				// Discard the validation window and draft.
				window.hasUnsavedChanges = false;
				DestroyImmediate(window);
				DestroyImmediate(draft);
			}
		}

		// Draws the interactive rack preview for rig rules, chassis, or module entries.
		private void DrawRigPreview(VisualElement parent, object obj)
		{
			// Draw a footprint preview and summary for a rig module.
			if (obj is RigModuleDefinition module)
			{
				// Create the preview column and place it in the parent.
				var preview = new VisualElement();
				preview.style.flexDirection = FlexDirection.Column;
				parent.Add(preview);

				// Draw one row of cells for each footprint row, honoring the rotation toggle.
				var size = GameSimulation.PartSize(module, previewRotated);
				for (var y = 0; y < size.y; y++)
				{
					var row = new VisualElement();
					row.style.flexDirection = FlexDirection.Row;
					preview.Add(row);

					// Draw one tinted cell for each footprint column.
					for (var x = 0; x < size.x; x++)
					{
						var cell = new Label(" ");
						cell.style.width = cell.style.height = 28;
						cell.style.marginRight = cell.style.marginBottom = 2;
						cell.style.backgroundColor = Palette.Branch(module.branch);
						row.Add(cell);
					}
				}

				// Add the button that toggles the footprint rotation and redraws the entry.
				parent.Add(new Button(() =>
				{
					previewRotated = !previewRotated;
					ShowEntry(true);
				}) { text = "Rotate footprint preview" });

				// Add a one-line summary of the footprint and its power and cooling values.
				parent.Add(new Label("Footprint " + size.x + "x" + size.y + " / " + module.powerDemand + " power / " +
									 module.heatOutput + " heat / +" + module.powerCapacity + " power limit / +" +
									 module.coolingCapacity + " cooling limit"));
			}

			// Draw the starter layout grid for a rig rules definition.
			if (obj is RigRulesDefinition rig && rig.startingChassis != null)
			{
				// Total the starter chassis and installed module power and cooling values.
				var chassis = rig.startingChassis;
				float power = 0, heat = 0, pc = chassis.powerCapacity, cc = chassis.coolingCapacity;
				foreach (var part in rig.startingParts.Where(p => p.module != null))
				{
					power += part.module.powerDemand;
					heat += part.module.heatOutput;
					pc += part.module.powerCapacity;
					cc += part.module.coolingCapacity;
				}

				// Show the starter layout totals above the grid.
				parent.Add(
					new Label("STARTER LAYOUT / POWER " + power + " / " + pc + " / COOLING " + heat + " / " + cc));

				// Draw each chassis row as a horizontal strip of cells.
				for (var y = 0; y < chassis.rows; y++)
				{
					var row = new VisualElement();
					row.style.flexDirection = FlexDirection.Row;
					parent.Add(row);

					// Find the starter parts that cover this grid cell.
					for (var x = 0; x < chassis.columns; x++)
					{
						var occupants = rig.startingParts
										   .Where(p => p.module != null && x >= p.column && y >= p.row &&
													   x < p.column + GameSimulation.PartSize(p.module, p.rotated).x &&
													   y < p.row + GameSimulation.PartSize(p.module, p.rotated).y)
										   .ToArray();

						// Label the cell with its module, EMPTY, or OVERLAP.
						var cell = new Label(occupants.Length > 1 ? "OVERLAP" :
											 occupants.Length == 0 ? "EMPTY" : occupants[0].module.title);
						cell.style.width = 100;
						cell.style.height = 42;
						cell.style.whiteSpace = WhiteSpace.Normal;

						// Tint the cell by its module branch, gray for empty or overlapping cells.
						cell.style.backgroundColor = occupants.Length == 1
							? Palette.Branch(occupants[0].module.branch)
							: Color.gray;
						cell.style.color = Color.black;
						cell.style.marginRight = cell.style.marginBottom = 2;
						row.Add(cell);
					}
				}
			}
		}

		// Builds the title row with id, category chips, and navigation shortcuts for an entry.
		private VisualElement BuildHeader(ContentEntry entry, GameplayDefinition definition)
		{
			// Create the header container.
			var header = new VisualElement();
			header.AddToClassList("ctp-header");

			// Create the top row that holds the title, chips, and actions.
			var top = new VisualElement();
			top.AddToClassList("ctp-header-top");
			header.Add(top);

			// Show the definition title, falling back to the file name.
			var title = definition == null || string.IsNullOrWhiteSpace(definition.title)
				? Path.GetFileNameWithoutExtension(entry.file)
				: definition.title;
			var titleLabel = new Label(title);
			titleLabel.AddToClassList("ctp-title");
			top.Add(titleLabel);

			// Add the category chip and the unsaved-changes chip, which is shown while the draft is dirty.
			top.Add(ContentEditorUI.Chip(entry.category, null));
			headerUnsaved = ContentEditorUI.Chip("Unsaved", "warn");
			top.Add(headerUnsaved);

			// Add an error chip when the file failed to decode.
			if (workspace.Bundle.InvalidEntries.Contains(entry.key))
				top.Add(ContentEditorUI.Chip("Invalid file", "error"));

			// Build the Actions menu with file and identity commands.
			var actions = new ToolbarMenu { text = "Actions", tooltip = "File and identity actions for this entry" };
			actions.AddToClassList("ctp-actions");

			// Add the action that reveals the JSON file in Explorer.
			actions.menu.AppendAction("Open file in Explorer",
									  _ => EditorUtility.RevealInFinder(
										  ContentJson.SafePath(GameData.DirectoryPath, entry.file)));

			// Add the action that renames the gameplay id and updates references.
			actions.menu.AppendAction("Rename ID and references...",
									  _ => ContentTextPrompt.Show("Rename ID and references", definition.id,
																  value => Mutate(
																	  () => workspace.RenameId(entry.key, value),
																	  Redraw.Window)),
									  _ => definition != null
										  ? DropdownMenuAction.Status.Normal
										  : DropdownMenuAction.Status.Disabled);

			// Add the action that renames the JSON file path.
			actions.menu.AppendAction("Rename JSON file...",
									  _ => ContentTextPrompt.Show("Rename relative JSON file", entry.file,
																  value => Mutate(
																	  () => workspace.RenameFile(entry.key, value),
																	  Redraw.Window)));
			top.Add(actions);

			// Add the subtitle with the gameplay id and the file path.
			var sub = new Label((definition?.id ?? "no ID") + "  ·  " + entry.file);
			sub.AddToClassList("ctp-subtitle");
			header.Add(sub);

			return header;
		}

		// Recursively renders editable fields for a definition object in the form.
		private void DrawObject(VisualElement parent, object obj, string path, int depth)
		{
			// Stop recursing when the rule nesting becomes too deep.
			if (depth > 60)
			{
				parent.Add(new HelpBox("Rule nesting limit reached.", HelpBoxMessageType.Error));
				return;
			}

			// Draw one editor row for each serialized field of the object.
			foreach (var field in ContentJson.Fields(obj.GetType()))
			{
				// Create the container for this property and register it by its path.
				var container = new VisualElement();
				container.AddToClassList("ctp-prop");
				parent.Add(container);
				var fieldPath = path + "." + field.Name;
				propertyTargets[fieldPath] = container;

				// Resolve the description; a missing description becomes the error text.
				string description;
				try
				{
					description = ContentFieldHelp.Description(field);
				}
				catch (Exception ex)
				{
					description = ex.Message;
				}

				// Use the description as the container tooltip.
				container.tooltip = description;

				// Read the current value and decide whether it needs a compound layout.
				var value = field.GetValue(obj);
				var compound = IsCompound(field.FieldType);
				var host = container;
				if (!compound)
				{
					host = new VisualElement();
					host.AddToClassList("ctp-prop-row");
					container.Add(host);
				}

				// Draw the editor for the value; its setter writes changes back to the object.
				DrawValue(host, ContentFieldHelp.DisplayName(field.Name), field.FieldType, value,
						  next => field.SetValue(obj, next), fieldPath, depth, field);

				// Disable the identity field on definitions so the ID changes only through Rename ID.
				if (field.Name == "id" && obj is GameplayDefinition)
					host.Q<TextField>()?.SetEnabled(false);

				// Create the help label and its toggle, which show the description inline when enabled.
				var helpKey = selectedKey + "|" + fieldPath;
				var help = new Label(description);
				help.AddToClassList("ctp-help");
				help.style.display = ShowDescriptions || openHelp.Contains(helpKey)
					? DisplayStyle.Flex
					: DisplayStyle.None;
				var toggle = new Button
				{
					text = "?",
					tooltip = ShowDescriptions
						? "Descriptions are always shown. Turn them off in the toolbar."
						: "Show or hide this property's description"
				};
				toggle.AddToClassList("ctp-help-toggle");

				// Toggle this property's help state unless descriptions are always shown.
				toggle.clicked += () =>
				{
					if (ShowDescriptions)
						return;

					if (!openHelp.Remove(helpKey))
						openHelp.Add(helpKey);
					help.style.display = openHelp.Contains(helpKey) ? DisplayStyle.Flex : DisplayStyle.None;
				};
				host.Add(toggle);
				container.Add(help);
			}
		}

		// Returns whether a type should be edited as nested fields instead of a scalar control.
		private static bool IsCompound(Type type)
		{
			// Arrays and rule types need a stacked layout, so they count as compound.
			if (type.IsArray || ContentJson.IsRule(type))
				return true;

			return !(type == typeof(string) || type == typeof(bool) || type == typeof(int) || type == typeof(float) ||
					 type.IsEnum || type == typeof(Color) || type == typeof(Vector2) || type == typeof(Vector3) ||
					 typeof(GameplayDefinition).IsAssignableFrom(type));
		}

		// Adds a labeled form field with optional compact layout classes.
		private static T Field<T>(VisualElement parent, T element, bool compact = false) where T : VisualElement
		{
			// Style the element as a form field and add it to the parent.
			element.AddToClassList("ctp-field");
			if (compact)
				element.AddToClassList("ctp-field--compact");

			parent.Add(element);
			return element;
		}

		// Renders one property editor control and wires changes back into the draft.
		private void DrawValue(VisualElement parent, string label, Type type, object value, Action<object> setter,
							   string path, int depth, FieldInfo field = null)
		{
			// Local helper that routes an edited value through Mutate so it is undoable and redraws as requested.
			void Set(object next, Redraw redraw = Redraw.None)
			{
				Mutate(() => setter(next), redraw);
			}

			// Draw a string as a text field, with an optional choice picker for known values.
			if (type == typeof(string))
			{
				// Create the field line and its text field, using a multi-line box for long text.
				var line = new VisualElement();
				line.AddToClassList("ctp-field-line");
				parent.Add(line);
				var multiline = field?.GetCustomAttribute<TextAreaAttribute>() != null ||
								LongTextFields.Contains(field?.Name);
				var text = Field(line, new TextField(label) { value = (string)value ?? "", multiline = multiline });

				// Give multi-line text a taller, wrapping box.
				if (multiline)
				{
					text.style.minHeight = 60;
					text.style.whiteSpace = WhiteSpace.Normal;
				}

				// Delay single-line edits until commit; multi-line edits apply immediately.
				text.isDelayed = !multiline;
				text.RegisterValueChangedCallback(e => Set(e.newValue));

				// Add a Choose button when this field has known values to pick from.
				var choices = StringChoices(field?.Name).Distinct().ToList();
				if (choices.Count > 0)
				{
					var picker = new Button(() => ContentChoiceWindow.Show(
												label, choices.Select(s => new ContentChoice(s, () =>
												{
													Set(s);
													text.SetValueWithoutNotify(s);
												})).ToList()))
						{ text = "Choose...", tooltip = "Pick an existing " + label.ToLowerInvariant() + " value" };
					picker.AddToClassList("ctp-suffix");
					line.Add(picker);
				}

				return;
			}

			// Draw a bool as a toggle.
			if (type == typeof(bool))
			{
				var control = new Toggle(label) { value = value is bool b && b };
				control.RegisterValueChangedCallback(e => Set(e.newValue));
				parent.Add(control);
				return;
			}

			// Read the optional Range attribute that controls slider limits.
			var range = field?.GetCustomAttribute<RangeAttribute>();

			// Draw an int as an integer slider when ranged, otherwise as an integer field.
			if (type == typeof(int))
			{
				if (range != null)
				{
					var slider =
						Field(parent,
							  new SliderInt(label, (int)range.min, (int)range.max)
								  { value = (int)value, showInputField = true });
					slider.RegisterValueChangedCallback(e => Set(e.newValue));
				}
				else
				{
					var integer = Field(parent, new IntegerField(label) { value = (int)value, isDelayed = true }, true);
					integer.RegisterValueChangedCallback(e => Set(e.newValue));
				}

				return;
			}

			// Draw a float as a slider when ranged, otherwise as a float field.
			if (type == typeof(float))
			{
				if (range != null)
				{
					var slider =
						Field(parent,
							  new Slider(label, range.min, range.max) { value = (float)value, showInputField = true });
					slider.RegisterValueChangedCallback(e => Set(e.newValue));
				}
				else
				{
					var number = Field(parent, new FloatField(label) { value = (float)value, isDelayed = true }, true);
					number.RegisterValueChangedCallback(e => Set(e.newValue));
				}

				return;
			}

			// Draw an enum as a dropdown.
			if (type.IsEnum)
			{
				var choice = Field(parent, new EnumField(label, (Enum)value), true);
				choice.RegisterValueChangedCallback(e => Set(e.newValue));
				return;
			}

			// Draw a color as an HDR color field with alpha.
			if (type == typeof(Color))
			{
				var color = Field(parent, new ColorField(label) { value = (Color)value, hdr = true, showAlpha = true },
								  true);
				color.RegisterValueChangedCallback(e => Set(e.newValue));
				return;
			}

			// Draw a Vector2 as a two-component field.
			if (type == typeof(Vector2))
			{
				var vector = Field(parent, new Vector2Field(label) { value = (Vector2)value }, true);
				vector.RegisterValueChangedCallback(e => Set(e.newValue));
				return;
			}

			// Draw a Vector3 as a three-component field.
			if (type == typeof(Vector3))
			{
				var vector = Field(parent, new Vector3Field(label) { value = (Vector3)value }, true);
				vector.RegisterValueChangedCallback(e => Set(e.newValue));
				return;
			}

			// Draw a gameplay definition reference as a row with a picker and a Go to action.
			if (typeof(GameplayDefinition).IsAssignableFrom(type))
			{
				// Read the current reference and whether the field may be cleared.
				var current = value as GameplayDefinition;
				var optional = field?.DeclaringType == typeof(ActionCondition) ||
							   (field?.DeclaringType == typeof(ObjectiveDefinition) && field.Name == "action");

				// Create the reference row with its caption.
				var row = new VisualElement();
				row.AddToClassList("ctp-ref-row");
				parent.Add(row);
				var caption = new Label(label);
				caption.AddToClassList("ctp-label");
				row.Add(caption);

				// Create the picker that lists every loaded definition of the expected type.
				var pick = new Button(() =>
				{
					var choices = workspace.Bundle.Objects.Values.OfType<GameplayDefinition>()
										   .Where(type.IsInstanceOfType)
										   .Select(d => new ContentChoice(d.title + " [" + d.id + "]",
																		  () => Set(d, Redraw.Form),
																		  ContentEditorUI.CategoryIconNames(
																			  CategoryOf(d)))).ToList();

					// Null is meaningful for generic ActionCondition and optional Objective.action only.
					if (optional)
						choices.Insert(0, new ContentChoice("None (optional)", () => Set(null, Redraw.Form)));

					ContentChoiceWindow.Show(label, choices);
				})
				{
					text = current == null
						? "Choose " + ContentEditorUI.Humanize(type.Name) + "..."
						: current.title + "  [" + current.id + "]",
					tooltip = "Choose a " + ContentEditorUI.Humanize(type.Name).ToLowerInvariant() + " reference"
				};
				pick.AddToClassList("ctp-ref-pick");
				ContentEditorUI.Decorate(pick, true, "Search Icon");
				row.Add(pick);

				// Add a Go to button that jumps to the referenced definition.
				if (current != null)
				{
					var go = new Button(() => NavigateTo(current))
						{ text = "Go to", tooltip = "Open " + current.title + " in its category" };
					go.AddToClassList("ctp-ref-action");
					ContentEditorUI.Decorate(go, true, "d_forward", "forward");
					row.Add(go);
				}

				// Add a clear button for optional references that are currently set.
				if (optional && current != null)
					row.Add(ContentEditorUI.IconButton("×", "Clear this optional reference",
													   () => Set(null, Redraw.Form)));
				return;
			}

			// Draw an array as a foldout with add, reorder, copy, and remove controls per item.
			if (type.IsArray)
			{
				// Create the array foldout with its item count, substituting an empty array when null.
				var array = (Array)value ?? Array.CreateInstance(type.GetElementType(), 0);
				var fold = new Foldout
					{ text = label + " (" + array.Length + ")", value = false, viewDataKey = selectedKey + path };
				parent.Add(fold);

				// Add the button that appends a new default item.
				var elementType = type.GetElementType();
				var add = new Button(() => Set(Resize(array, NewValue(elementType)), Redraw.Form))
					{ text = "Add item", tooltip = "Append a new item to " + label };
				ContentEditorUI.Decorate(add, true, "Toolbar Plus");
				fold.Add(add);

				// Draw each item with its own header and editor.
				for (var i = 0; i < array.Length; i++)
				{
					// Capture the item index for the button callbacks.
					var index = i;

					// Create the item container and its header.
					var item = new VisualElement();
					item.AddToClassList("ctp-array-item");
					fold.Add(item);
					var head = new VisualElement();
					head.AddToClassList("ctp-array-head");
					item.Add(head);
					var itemTitle = new Label("Item " + (i + 1));
					itemTitle.AddToClassList("ctp-array-title");
					head.Add(itemTitle);

					// Add the move up, move down, copy, and remove buttons for this item.
					head.Add(ContentEditorUI.IconButton("↑", "Move this item earlier",
														() => Set(Reorder(array, index, -1), Redraw.Form)));
					head.Add(ContentEditorUI.IconButton("↓", "Move this item later",
														() => Set(Reorder(array, index, 1), Redraw.Form)));
					head.Add(ContentEditorUI.IconButton("Copy", "Duplicate this item",
														() => Set(
															Resize(
																array, CloneValue(array.GetValue(index), elementType)),
															Redraw.Form), "Clipboard"));
					head.Add(ContentEditorUI.IconButton("×", "Remove this item",
														() => Set(Remove(array, index), Redraw.Form),
														"TreeEditor.Trash"));

					// Draw the item value itself with a nested editor.
					DrawValue(item, "Value", elementType, array.GetValue(i), v => array.SetValue(v, index),
							  path + "[" + i + "]", depth + 1, field);
				}

				return;
			}

			// Draw any other object as a foldout group whose children are its fields.
			var group = new Foldout { text = label, value = false, viewDataKey = selectedKey + path };
			group.AddToClassList("ctp-group");
			parent.Add(group);

			// Rules get a change-type button and a summary; empty objects get a create button.
			if (ContentJson.IsRule(type))
			{
				ruleHeaders.Add((group, label, value));
				group.text = label + ": " + RuleSummary(value);
				group.Add(new Button(() => ContentChoiceWindow.Show("Rule type", ContentJson.RuleTypes
																		.Where(type.IsAssignableFrom)
																		.Select(t => new ContentChoice(
																				ContentEditorUI.Humanize(t.Name) +
																				"  (" + t.Name + ")",
																				() => Set(Activator.CreateInstance(t),
																					Redraw.Form))).ToList()))
				{
					text = "Change rule type...",
					tooltip = "Replace this rule with another type. Its settings reset to the new type's defaults."
				});
			}
			else if (value == null)
			{
				group.Add(new Button(() => Set(NewValue(type), Redraw.Form))
							  { text = "Create " + label, tooltip = "Create " + label + " with default settings" });
			}

			// Draw the object's own fields when a value exists.
			if (value != null)
				DrawObject(group, value, path, depth + 1);
		}

		// Maps a definition instance to its sidebar category label.
		private string CategoryOf(object definition)
		{
			// Find the category of the definition, or an empty string when none matches.
			return workspace.Bundle.Manifest.entries
							.FirstOrDefault(e => ReferenceEquals(workspace.Bundle.Objects[e.key], definition))
							?.category ?? "";
		}

		// Selects the manifest entry for a definition and scrolls the form into view.
		private void NavigateTo(GameplayDefinition target)
		{
			// Find the manifest entry for the target definition.
			var entry =
				workspace.Bundle.Manifest.entries.FirstOrDefault(e => ReferenceEquals(workspace.Bundle.Objects[e.key],
																	 target));

			// Stop when the target is not registered.
			if (entry == null)
				return;

			// Switch to the entry's category, with Network Visuals shown under Graph Visuals.
			category = entry.category == "Network Visuals" ? "Graph Visuals" : entry.category;
			selectedKey = entry.key;

			// Redraw the window on the target entry.
			DrawWindow();
		}

		// Builds a short human-readable summary for a rule object shown in foldout headers.
		private static string RuleSummary(object value)
		{
			// Describe an unset rule as "not set".
			if (value == null)
				return "not set";

			// Start the summary with the humanized type name.
			var name = ContentEditorUI.Humanize(value.GetType().Name);

			// Append the first summary-worthy value field found on the rule.
			foreach (var field in ContentJson.Fields(value.GetType()))
			{
				// Skip fields that are not summary values or that are empty.
				if (!ValueFieldNames.Contains(field.Name))
					continue;

				var v = field.GetValue(value);
				if (v == null || (v is string text && text.Length == 0))
					continue;

				// Format the value for the summary, skipping values it cannot show.
				string shown;
				if (v is float f)
					shown = f.ToString("0.###", CultureInfo.InvariantCulture);
				else if (v is int || v is bool || v is string || v.GetType().IsEnum)
					shown = Convert.ToString(v, CultureInfo.InvariantCulture);
				else
					continue;

				return name + " " + shown;
			}

			return name;
		}

		// Returns allowed string values for enum-like fields and reference pickers.
		private IEnumerable<string> StringChoices(string name)
		{
			// Offer stat names from the career baseline plus built-in stat keys.
			if (name == "stat" || name == "displayedStat")
				return workspace.Bundle.Game.career.stats.Select(s => s.id)
								.Concat(new[] { "security", "duration", "baseTrace", "trace" });

			// Offer node type tags and action tags.
			if (name == "tag" || name == "tags")
				return workspace.Bundle.Game.nodeTypes.SelectMany(t => t.tags)
								.Concat(workspace.Bundle.Game.actions.SelectMany(a => a.tags));

			// Offer the node IDs of the network currently being edited.
			if (new[]
				{
					"nodeId", "a", "b", "entryNode", "preferredNode", "previewNode", "entrance", "destination",
					"sensorAttachment", "directRoute", "sensors", "secondaryTargets", "archives", "reserved"
				}.Contains(name))
			{
				var obj = Selected == null ? null : workspace.Bundle.Objects[Selected.key];
				var network = obj as NetworkDefinition ?? (obj as ContractDefinition)?.network;
				return network?.nodes.Select(n => n.id).Concat(network.routes?.extraIds ?? Array.Empty<string>()) ??
					   Enumerable.Empty<string>();
			}

			// Offer module families, IDs, and branches.
			if (name == "kind" || name == "family")
				return workspace.Bundle.Game.modules.Select(m => m.family)
								.Concat(workspace.Bundle.Game.modules.Select(m => m.id))
								.Concat(workspace.Bundle.Game.modules.Select(m => m.branch)).Distinct();

			// Offer module, hardware, and status keys.
			if (name == "key")
				return workspace.Bundle.Game.modules.Select(m => m.family)
								.Concat(workspace.Bundle.Game.modules.Select(m => m.id))
								.Concat(workspace.Bundle.Game.modules.Select(m => m.branch))
								.Concat(workspace.Bundle.Game.hardware.Select(h => h.id)
												 .Concat(workspace.Bundle.Game.statuses.Select(s => s.id)));

			// Other fields have no suggested choices.
			return Enumerable.Empty<string>();
		}

		// Creates a default instance for adding a new element to a collection or compound field.
		private object NewValue(Type type)
		{
			// Use an empty string for string fields.
			if (type == typeof(string))
				return "";

			// Use an empty array for array fields.
			if (type.IsArray)
				return Array.CreateInstance(type.GetElementType(), 0);

			// Pick the first loaded definition of the requested type, or null when none exists.
			if (typeof(GameplayDefinition).IsAssignableFrom(type))
				return workspace.Bundle.Objects.Values.FirstOrDefault(type.IsInstanceOfType);

			// Create the first rule type that matches the field.
			if (ContentJson.IsRule(type))
				return Activator.CreateInstance(ContentJson.RuleTypes.First(type.IsAssignableFrom));

			// Create a default instance of any other type.
			return Activator.CreateInstance(type);
		}

		// Deep-copies a value for undo-safe draft mutations.
		private object CloneValue(object value, Type type)
		{
			// Return null, value types, strings, and definitions as they are.
			if (value == null || type.IsValueType || value is string || value is ScriptableObject)
				return value;

			// Clone nested managed objects without duplicating definition references.
			var wrapper = new CloneBox { item = value };
			return CloneManaged(wrapper.item);
		}

		// Deep-copies a managed object graph through JSON serialization.
		private object CloneManaged(object value)
		{
			// Return null, strings, value types, and definitions as they are.
			if (value == null || value is string || value.GetType().IsValueType || value is ScriptableObject)
				return value;

			// Deep-copy each element of an array.
			if (value is Array a)
			{
				var next = Array.CreateInstance(value.GetType().GetElementType(), a.Length);
				for (var i = 0; i < a.Length; i++)
					next.SetValue(CloneManaged(a.GetValue(i)), i);

				return next;
			}

			// Create a fresh instance and copy every field with a deep clone.
			var copy = Activator.CreateInstance(value.GetType());
			foreach (var field in ContentJson.Fields(value.GetType()))
				field.SetValue(copy, CloneManaged(field.GetValue(value)));

			return copy;
		}

		// Returns a new array with one additional element copied from the template value.
		private static Array Resize(Array source, object value)
		{
			// Allocate one more slot and copy the existing items.
			var next = Array.CreateInstance(source.GetType().GetElementType(), source.Length + 1);
			Array.Copy(source, next, source.Length);

			// Append the new value at the end.
			next.SetValue(value, source.Length);

			return next;
		}

		// Returns a new array with the element at the given index removed.
		private static Array Remove(Array source, int index)
		{
			// Allocate a smaller array and track the write position.
			var next = Array.CreateInstance(source.GetType().GetElementType(), source.Length - 1);
			var n = 0;

			// Copy every item except the one at the requested index.
			for (var i = 0; i < source.Length; i++)
				if (i != index)
					next.SetValue(source.GetValue(i), n++);

			return next;
		}

		// Returns a new array with one element moved up or down by delta positions.
		private static Array Reorder(Array source, int index, int delta)
		{
			// Compute the neighbor index and copy the array.
			var other = index + delta;
			var next = (Array)source.Clone();

			// Swap the item with its neighbor when the neighbor is inside the array.
			if (other >= 0 && other < source.Length)
			{
				var item = next.GetValue(index);
				next.SetValue(next.GetValue(other), index);
				next.SetValue(item, other);
			}

			return next;
		}

		// Applies a draft mutation with undo, then refreshes UI per the redraw level.
		private void Mutate(Action action, Redraw redraw = Redraw.None)
		{
			// Block edits during Play mode, since changes only load on the next launch.
			if (EditorApplication.isPlayingOrWillChangePlaymode)
			{
				EditorUtility.DisplayDialog("Stop Play mode",
											"Content editing is disabled while playing. Changes load on the next launch.",
											"OK");
				return;
			}

			// Snapshot the draft so a failed edit can be rolled back.
			var before = JsonUtility.ToJson(draft);
			try
			{
				// Record undo state, apply the edit, and capture the workspace.
				Undo.RecordObject(draft, "Edit JSON content");
				action();
				workspace.Capture();

				// Mark the draft dirty and refresh the cached state and the entry list.
				EditorUtility.SetDirty(draft);
				Cache();
				RefreshList();

				// Redraw the whole window or only the form, depending on what changed.
				if (redraw == Redraw.Window)
					DrawWindow();
				else if (redraw == Redraw.Form)
					ShowEntry(true);

				UpdateStatus();
			}
			catch (Exception ex)
			{
				// Restore the snapshot, reload the workspace, and report the error.
				JsonUtility.FromJsonOverwrite(before, draft);
				workspace.Reload();
				DrawWindow();
				Error(ex);
			}
		}

		// Updates the window dirty flag and save-changes prompt from draft differences.
		private void UpdateDirty()
		{
			// Mark the window dirty when the draft differs from the saved baseline.
			hasUnsavedChanges = draft != null && draft.Dirty;

			// Set the message shown when the user is prompted to save.
			saveChangesMessage = "Save all JSON content changes? Each changed save creates per-entry revisions.";
		}

		/// <summary>Refreshes toolbar counts, status bar, property markers and rule summaries without rebuilding the form.</summary>
		private void UpdateStatus()
		{
			// Nothing to update until the draft and workspace exist.
			if (draft == null || workspace == null)
				return;

			// Refresh the dirty flag and read the unsaved and error counts.
			UpdateDirty();
			int unsaved = UnsavedCount(), errors = ErrorCount();

			// Update the Save All, Validate, and Descriptions labels with their current counts or state.
			if (saveAllButton != null)
				saveAllButton.text = unsaved > 0 ? "Save All (" + unsaved + ")" : "Save All";
			if (validateButton != null)
				validateButton.text = errors > 0 ? "Validate (" + errors + ")" : "Validate";
			if (descriptionsButton != null)
				descriptionsButton.text = "Descriptions: " + (ShowDescriptions ? "On" : "Off");

			// Show the unsaved marker in the header when the selected file is dirty.
			if (headerUnsaved != null)
				headerUnsaved.style.display =
					Selected != null && Dirty(Selected.file) ? DisplayStyle.Flex : DisplayStyle.None;

			// Show the status bar text with unsaved, error, and last-saved information.
			if (statusLabel != null)
				statusLabel.text = unsaved + " unsaved file(s)   ·   " + errors + " error(s)   ·   " +
								   (lastSaved.HasValue
									   ? "last saved " + lastSaved.Value.ToString("HH:mm")
									   : "not saved this session");

			// Refresh the property markers and the rule summaries.
			ApplyMarkers();
			foreach (var header in ruleHeaders)
				header.Fold.text = header.Label + ": " + RuleSummary(header.Value);
		}

		// Counts draft files that differ from their accepted baselines.
		private int UnsavedCount()
		{
			// Count files that differ from the saved baseline, including files only in one side.
			var original = draft.Original;
			var current = draft.Current;
			return current.Keys.Union(original.Keys)
						  .Count(f => !original.TryGetValue(f, out var o) || !current.TryGetValue(f, out var c) ||
									  o != c);
		}

		// Counts validation errors across the current catalog snapshot.
		private int ErrorCount()
		{
			// Count distinct load and validation messages.
			return workspace.Bundle.Errors.Concat(lastValidation ?? new List<string>()).Distinct().Count();
		}

		// Highlights form fields that changed or failed validation since the last save.
		private void ApplyMarkers()
		{
			// Nothing to mark until property fields exist.
			if (propertyTargets.Count == 0)
				return;

			// Collect the changed property paths for the selected entry.
			var entry = Selected;
			var changed = ChangedPaths(entry);

			// Find the property paths that have validation errors.
			var errorKeys = new HashSet<string>();
			if (entry != null && lastValidation != null)
				foreach (var message in lastValidation.Where(m => m.Contains(entry.file)))
				{
					var key = PropertyFor(message);
					if (key != null)
						errorKeys.Add(key);
				}

			// Apply the changed and error classes to every property container.
			foreach (var pair in propertyTargets)
			{
				pair.Value.EnableInClassList("ctp-changed", changed.Contains(pair.Key));
				pair.Value.EnableInClassList("ctp-error", errorKeys.Contains(pair.Key));
			}
		}

		// Collects JSON property paths that differ between draft and baseline for one entry.
		private HashSet<string> ChangedPaths(ContentEntry entry)
		{
			// Start with an empty set and return it directly when there is no entry.
			var changed = new HashSet<string>();
			if (entry == null)
				return changed;

			// Load the saved and draft text for the entry's file.
			var original = draft.Original;
			var current = draft.Current;

			// Stop when the file is missing from either side or is unchanged.
			if (!original.TryGetValue(entry.file, out var before) || !current.TryGetValue(entry.file, out var after) ||
				before == after)
				return changed;

			// Compare the parsed JSON documents and collect the changed property paths.
			using (var oldDocument = JsonDocument.Parse(before))
			{
				using (var newDocument = JsonDocument.Parse(after))
				{
					CollectChanges(newDocument.RootElement, oldDocument.RootElement, "$", changed);
				}
			}

			return changed;
		}

		// Recursively records JSON leaf paths whose values differ between two documents.
		private static void CollectChanges(JsonElement current, JsonElement original, string path,
										   HashSet<string> changed)
		{
			// Stop when the raw JSON is identical; otherwise record this path as changed.
			if (current.GetRawText() == original.GetRawText())
				return;

			changed.Add(path);

			// Recurse into object properties, recording properties that were added.
			if (current.ValueKind == JsonValueKind.Object && original.ValueKind == JsonValueKind.Object)
			{
				foreach (var property in current.EnumerateObject())
				{
					// Compare each property with its original counterpart.
					var child = path + "." + property.Name;
					if (original.TryGetProperty(property.Name, out var previous))
						CollectChanges(property.Value, previous, child, changed);
					else
						changed.Add(child);
				}
			}
			else if (current.ValueKind == JsonValueKind.Array && original.ValueKind == JsonValueKind.Array)
			{
				// Track the array index while comparing items.
				var i = 0;
				foreach (var item in current.EnumerateArray())
				{
					// Compare each item with the original item at the same index.
					var child = path + "[" + i + "]";
					if (i < original.GetArrayLength())
						CollectChanges(item, original[i], child, changed);
					else
						changed.Add(child);
					i++;
				}
			}
		}

		// Extracts a JSON property path referenced by a validation error message.
		private string PropertyFor(string message)
		{
			// Match the message to the longest property path it mentions.
			return propertyTargets.Keys.Where(k => message.Contains(k) || message.Contains(k.Substring(2)))
								  .OrderByDescending(k => k.Length).FirstOrDefault();
		}

		// Persists window layout state and the recovery draft file to Library storage.
		private void Cache()
		{
			// Skip caching when there is no draft or when validating headlessly.
			if (draft == null || validationMode)
				return;

			// Remove the recovery file when the draft matches the saved baseline.
			if (!draft.Dirty)
			{
				if (File.Exists(RecoveryPath))
					File.Delete(RecoveryPath);

				return;
			}

			// Write the draft to the recovery file so unsaved work survives a restart.
			ContentHistory.AtomicWrite(RecoveryPath, JsonUtility.ToJson(draft, true));
		}

		// Reloads the workspace from the draft after an undo or redo operation.
		private void OnUndo()
		{
			// Nothing to reload until a workspace exists.
			if (workspace == null)
				return;

			// Reload the workspace from the restored draft and redraw the window.
			try
			{
				draft.originals = savedBaseline;
				workspace.Reload();
				Cache();
				DrawWindow();
			}
			catch (Exception ex)
			{
				// Report any failure while applying the undo.
				Error(ex);
			}
		}

		// Blocks edits entering play mode and reloads content when returning to edit mode.
		private void OnPlayMode(PlayModeStateChange state)
		{
			// Only act when leaving Edit mode for Play mode.
			if (state != PlayModeStateChange.ExitingEditMode)
				return;

			// Ask the user how to handle unsaved changes before Play mode starts.
			if (draft != null && draft.Dirty)
			{
				var choice = EditorUtility.DisplayDialogComplex("Unsaved JSON content",
																"Save all changes before entering Play mode?",
																"Save All", "Cancel", "Discard");
				// Cancel Play mode when the user cancels or the save fails.
				if (choice == 1 || (choice == 0 && !Save(true)))
				{
					EditorApplication.isPlaying = false;
					return;
				}

				// Discard the draft when the user chooses Discard.
				if (choice == 2)
					DiscardChanges();
			}

			// Runtime initialization handles resetting its own launch snapshot.
		}

		/// <summary>Writes validated draft JSON to disk when Unity prompts to save window changes.</summary>
		public override void SaveChanges()
		{
			// Save the draft and let Unity clear its dirty state only when the save succeeds.
			if (Save(true))
				base.SaveChanges();
		}

		/// <summary>Reverts the draft to accepted originals when Unity prompts to discard window changes.</summary>
		public override void DiscardChanges()
		{
			// Restore the draft to the accepted originals and refresh dependent state.
			if (draft != null)
			{
				draft.Set(draft.Original);
				workspace?.Reload();
				Cache();
				UpdateDirty();
			}

			// Let Unity finish discarding, then redraw the window.
			base.DiscardChanges();
			if (workspace != null)
				DrawWindow();
		}

		// Opens the save confirmation dialog for the current or all dirty files.
		private void RequestSave(bool all)
		{
			// Saving is blocked while Play mode is active.
			if (EditorApplication.isPlayingOrWillChangePlaymode)
			{
				Error(new InvalidOperationException("Stop Play mode before saving content."));
				return;
			}

			// Save immediately when nothing is dirty.
			if (!draft.Dirty)
			{
				Save(all);
				return;
			}

			// Count the files to save and build the confirmation scope label.
			var count = all ? UnsavedCount() : Selected != null && Dirty(Selected.file) ? 1 : 0;
			var scope = all ? "Save all changes" : "Save " + (Selected?.file ?? "selected entry");

			// Ask for an optional revision name, then save.
			ContentSaveWindow.Show(scope, count, revisionName, name =>
			{
				revisionName = name ?? "";
				Save(all);
			});
		}

		// Validates and commits draft changes to disk, optionally after overwrite review.
		private bool Save(bool all, bool reviewedOverwrite = false, IDictionary<string, string> reviewedDisk = null)
		{
			// Attempt the save and report success or failure through the return value.
			try
			{
				// Refuse to save while Play mode is running.
				if (EditorApplication.isPlayingOrWillChangePlaymode && EditorApplication.isPlaying)
					throw new InvalidOperationException("Stop Play mode before saving content.");

				// Choose the files to save: every draft file for Save All, otherwise only the baseline plus the selection.
				var desired = all ? draft.Current : draft.Original;
				if (!all && Selected != null)
				{
					// Structural changes must go through Save All, since they may span several entries.
					if (Dirty("manifest.json"))
						throw new InvalidOperationException(
							"Registration, ordering, ID, or filename changes may span entries. Use Save All for structural edits.");

					desired[Selected.file] = draft.Current[Selected.file];
				}

				// Validate the candidate by loading it before anything is written.
				using (var candidate = ContentJson.Load(GameData.DirectoryPath, desired))
				{
				}

				// Commit the history transaction and accept the saved files as the new baseline.
				history.Commit(desired, draft.Original, revisionName, draft.operation, reviewedOverwrite, reviewedDisk);
				draft.originals = desired.Select(p => new ContentDraftFile { file = p.Key, text = p.Value }).ToArray();
				savedBaseline = draft.originals;
				draft.operation = "Save";

				// Clear the pending revision name and record the save time.
				revisionName = "";
				lastSaved = DateTime.Now;
				lastValidation = new List<string>();

				// Refresh the recovery file and the window.
				Cache();
				DrawWindow();
				return true;
			}
			catch (IOException ex)
			{
				// Offer a diff review when the failure was an external edit conflict.
				if (ex.Message.StartsWith("External edit conflict:", StringComparison.Ordinal))
				{
					// Build the disk diffs for every file that changed outside the editor.
					var diffs = new List<string>();
					var reviewed = new Dictionary<string, string>();
					foreach (var file in draft.Original.Keys.Union(draft.Current.Keys))
					{
						var path = ContentJson.SafePath(GameData.DirectoryPath, file);
						var disk = File.Exists(path) ? File.ReadAllText(path) : null;
						reviewed[file] = disk;
						draft.Original.TryGetValue(file, out var original);
						if (disk != original)
							diffs.Add(file + "\n" + ContentHistory.Diff(original, disk, true));
					}

					// Show the review window and retry the save when the user approves the overwrite.
					ContentReviewWindow.Show("External changes — review before overwrite", string.Join("\n", diffs),
											 "Overwrite reviewed files", () => Save(all, true, reviewed));
				}
				else
				{
					// Report any other I/O failure.
					Error(ex);
				}

				return false;
			}
			catch (Exception ex)
			{
				// Report unexpected failures.
				ShowErrors(new[] { ex.Message });
				return false;
			}
		}

		// Runs catalog validation and shows errors in the banner without entering play mode.
		private void Validate()
		{
			// Collect load errors from a fresh load of the draft.
			var errors = new List<string>();
			try
			{
				using (var bundle = ContentJson.Load(GameData.DirectoryPath, draft.Current))
				{
				}
			}
			catch (Exception ex)
			{
				// Record the load failure as a validation message.
				errors.Add(ex.Message);
			}

			// Replace the bundle errors with the fresh results and refresh the list.
			workspace.Bundle.Errors.Clear();
			workspace.Bundle.Errors.AddRange(errors);
			RefreshList();

			// Add the property description coverage errors and show every result.
			errors.AddRange(ContentFieldHelp.CheckCoverage());
			ShowErrors(errors);
		}

		// Stores validation errors and rebuilds the banner with navigation links.
		private void ShowErrors(IEnumerable<string> errors)
		{
			// Store the messages and refresh the banner and status bar.
			lastValidation = errors.ToList();
			RenderBanner();
			UpdateStatus();
		}

		// Rebuilds the top banner for validation failures or pending save actions.
		private void RenderBanner()
		{
			// Nothing to render without a banner container.
			if (banner == null)
				return;

			// Clear the previous banner content and stop when no validation has run.
			banner.Clear();
			if (lastValidation == null)
				return;

			// Show a success message when there are no validation issues.
			if (lastValidation.Count == 0)
			{
				var ok = new Label("All JSON content and property descriptions are valid.");
				ok.AddToClassList("ctp-ok");
				banner.Add(ok);
				return;
			}

			// Split multi-line messages into one row per issue.
			var lines = lastValidation.SelectMany(m => m.Split('\n')).Where(l => !string.IsNullOrWhiteSpace(l))
									  .ToList();

			// Create the foldout and the scroll list that holds the issue buttons.
			var fold = new Foldout
				{ text = lines.Count + " validation issue(s). Click one to jump to it.", value = true };
			var summary = new ScrollView();
			summary.AddToClassList("ctp-banner-list");
			fold.Add(summary);

			// Create one clickable button per issue.
			foreach (var message in lines)
			{
				// Capture the message for the click handler.
				var captured = message;

				// Create the button that jumps to this issue when clicked.
				var button = new Button(() => NavigateError(captured))
					{ text = message, tooltip = "Jump to this issue" };
				button.AddToClassList("ctp-issue");
				button.style.whiteSpace = WhiteSpace.Normal;
				summary.Add(button);
			}

			// Show the issue list in the banner.
			banner.Add(fold);
		}

		// Selects the entry and field associated with a validation error message.
		private void NavigateError(string error)
		{
			// Find the entry whose file or object name the error mentions.
			var entry = workspace.Bundle.Manifest.entries.FirstOrDefault(e => error.Contains(e.file) ||
																			  error.Contains(
																				  workspace.Bundle.Objects[e.key]
																					  .name));

			// Switch to the matching entry's category and select it.
			if (entry != null)
			{
				category = entry.category == "Network Visuals" ? "Graph Visuals" : entry.category;
				selectedKey = entry.key;
				DrawWindow();
			}

			// Find the property that the error mentions and stop when none is drawn.
			var key = PropertyFor(error);
			if (key == null || !propertyTargets.TryGetValue(key, out var target))
				return;

			// Expand every foldout above the property so it is visible.
			for (var p = target.parent; p != null; p = p.parent)
				if (p is Foldout fold)
					fold.value = true;

			// Mark the property as in error, attach the message, and scroll to it.
			target.AddToClassList("ctp-error");
			target.Add(new HelpBox(error, HelpBoxMessageType.Error));
			form.ScrollTo(target);
		}

		// Removes the selected manifest entry from the draft after confirmation.
		private void Delete()
		{
			// Nothing to delete without a selection.
			if (Selected == null)
				return;

			// Block the deletion and list the references when other content still points at this entry.
			var references = workspace.Incoming(selectedKey);
			if (references.Count > 0)
			{
				ContentReviewWindow.Show("Incoming references — deletion blocked", string.Join("\n", references));
				return;
			}

			// Confirm the deletion before mutating the draft.
			if (EditorUtility.DisplayDialog("Delete content entry",
											"Delete " + Selected.file +
											"? Save All records a recoverable deletion revision.", "Delete", "Cancel"))
				Mutate(() =>
				{
					workspace.Delete(selectedKey);
					selectedKey = null;
				}, Redraw.Window);
		}

		// Discards unsaved edits for the selected entry back to the accepted baseline.
		private void Revert()
		{
			// Nothing to revert without a selection.
			if (Selected == null)
				return;

			// Structural changes span several entries, so only Reload Disk can discard them.
			if (Dirty("manifest.json") || draft.operation == "Rename ID")
			{
				Error(new InvalidOperationException(
						  "Structural changes span entries. Use Reload Disk to discard all drafts."));
				return;
			}

			// Restore the selected file's draft text to its saved version.
			Mutate(() =>
			{
				var files = draft.Current;
				files[Selected.file] = draft.Original[Selected.file];
				draft.Set(files);
				workspace.Reload();
			}, Redraw.Window);
		}

		// Reloads authoritative JSON from disk into the draft, abandoning local edits.
		private void ReloadDisk()
		{
			// Ask for confirmation when there are unsaved changes that would be lost.
			if (draft.Dirty && !EditorUtility.DisplayDialog("Discard all unsaved changes",
															"Reload every JSON file from disk?", "Discard and reload",
															"Cancel"))
				return;

			// Clear the draft and rebuild the workspace from the files on disk.
			try
			{
				draft.files = Array.Empty<ContentDraftFile>();
				workspace.Dispose();
				workspace = new ContentWorkspace(draft, GameData.DirectoryPath);
				savedBaseline = draft.originals;
				Cache();
				DrawWindow();
			}
			catch (Exception ex)
			{
				// Report any failure while reloading.
				Error(ex);
			}
		}

		// Toggles inline field descriptions and rebuilds the property form.
		private void ToggleDescriptions()
		{
			// Flip the persisted preference and refresh the form and toolbar.
			EditorPrefs.SetBool(DescriptionsPref, !ShowDescriptions);
			ShowEntry(true);
			UpdateStatus();
		}

		// Loads selected history revisions into the draft without saving to disk.
		private void Restore(IEnumerable<ContentRevision> revisions)
		{
			// Apply the chosen revisions to the draft as an undoable mutation.
			Mutate(() => workspace.Restore(revisions), Redraw.Window);
		}

		// Shows a modal dialog with an editor exception message.
		private static void Error(Exception ex)
		{
			// Show the error message in a dialog.
			EditorUtility.DisplayDialog("Content Editor", ex.Message, "OK");
		}

		// Describes how much of the window to redraw after an edit.
		private enum Redraw
		{
			None,
			Form,
			Window
		}

		// Holds one value so CloneValue can deep-copy it through a reference.
		private sealed class CloneBox
		{
			public object item;
		}
	}

	/// <summary>
	///     Shared visual helpers for the Content Editor windows: icons with text fallback, chips, and stylesheet
	///     attachment.
	/// </summary>
	internal static class ContentEditorUI
	{
		// Project path to the shared Content Editor USS stylesheet.
		private const string StyleSheetPath = "Assets/HackThePlanet/Editor/ContentEditor.uss";

		// Built-in Unity icon names for each category tab, in preference order.
		private static readonly Dictionary<string, string[]> CategoryIcons = new()
		{
			{ "Game", new[] { "Camera Icon" } },
			{ "Career", new[] { "Avatar Icon" } },
			{ "Rewards", new[] { "Favorite Icon" } },
			{ "Companies", new[] { "Folder Icon" } },
			{ "Contracts", new[] { "TextAsset Icon" } },
			{ "Networks", new[] { "Transform Icon" } },
			{ "Node Types", new[] { "Material Icon" } },
			{ "Actions", new[] { "Animation.Play" } },
			{ "Technologies", new[] { "Light Icon" } },
			{ "Hardware", new[] { "Mesh Icon" } },
			{ "Rig Rules", new[] { "SettingsIcon" } },
			{ "Chassis", new[] { "Mesh Icon" } },
			{ "Modules", new[] { "Mesh Icon" } },
			{ "Statuses", new[] { "Shader Icon" } },
			{ "Text Palette", new[] { "Font Icon" } },
			{ "Graph Visuals", new[] { "Texture Icon" } },
			{ "Network Visuals", new[] { "Texture Icon" } },
			{ "Graph Labels", new[] { "Font Icon" } }
		};

		/// <summary>Gets built-in Unity icon names for a content category tab, or an empty array when unknown.</summary>
		/// <param name="category">Content browser category label.</param>
		/// <returns>Icon name candidates in preference order.</returns>
		public static string[] CategoryIconNames(string category)
		{
			// Look up the icon names for this category, or return none when it is unknown.
			return category != null && CategoryIcons.TryGetValue(category, out var names)
				? names
				: Array.Empty<string>();
		}

		/// <summary>Attaches the Content Editor USS stylesheet to a visual tree root.</summary>
		/// <param name="root">Root element receiving editor styles.</param>
		public static void Attach(VisualElement root)
		{
			// Load the stylesheet asset from the project.
			var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);

			// Add the stylesheet only when it loaded.
			if (sheet != null)
				root.styleSheets.Add(sheet);
		}

		/// <summary>Returns the first built-in Unity icon that resolves, preferring the dark-skin variant when the editor uses it.</summary>
		public static Texture FindIcon(params string[] names)
		{
			// Nothing to resolve without candidate names.
			if (names == null)
				return null;

			// Prefer the dark-skin icon names when the editor uses the pro skin.
			var pro = EditorGUIUtility.isProSkin;
			foreach (var name in names)
			{
				// Strip any dark-skin prefix so both variants can be tried.
				var plain = name.StartsWith("d_", StringComparison.Ordinal) ? name.Substring(2) : name;

				// Try each variant in skin-preferred order and return the first icon that loads.
				foreach (var candidate in pro ? new[] { "d_" + plain, plain } : new[] { plain, "d_" + plain })
				{
					var content = EditorGUIUtility.IconContent(candidate);
					if (content != null && content.image != null)
						return content.image;
				}
			}

			return null;
		}

		/// <summary>
		///     Adds an icon in front of the button label. When keepText is false and an icon resolves, the text is removed
		///     (icon-only).
		/// </summary>
		public static T Decorate<T>(T button, bool keepText, params string[] icons) where T : Button
		{
			// Resolve the first available icon; leave the button unchanged when none exists.
			var texture = FindIcon(icons);
			if (texture == null)
				return button;

			// Insert the icon in front of the label.
			var image = new Image { image = texture, pickingMode = PickingMode.Ignore };
			image.AddToClassList("ctp-icon");
			button.Insert(0, image);
			button.AddToClassList("ctp-btn-with-icon");

			// Clear the label for icon-only buttons.
			if (!keepText)
				button.text = "";

			return button;
		}

		/// <summary>Creates an icon-only toolbar button when a category icon resolves.</summary>
		/// <param name="text">Fallback label when no icon is available.</param>
		/// <param name="tooltip">Hover text.</param>
		/// <param name="action">Click handler.</param>
		/// <param name="icons">Built-in icon name candidates.</param>
		/// <returns>A styled button with optional icon decoration.</returns>
		public static Button IconButton(string text, string tooltip, Action action, params string[] icons)
		{
			// Create the button with the ctp-icon-btn style and decorate it with an icon.
			var button = new Button(action) { text = text, tooltip = tooltip };
			button.AddToClassList("ctp-icon-btn");
			return Decorate(button, false, icons);
		}

		/// <summary>Creates a small status chip label with an optional modifier class.</summary>
		/// <param name="text">Chip caption.</param>
		/// <param name="modifier">Suffix for <c>ctp-chip--</c> styling; ignored when empty.</param>
		/// <returns>A chip label element.</returns>
		public static Label Chip(string text, string modifier)
		{
			// Create the chip with its base style.
			var chip = new Label(text);
			chip.AddToClassList("ctp-chip");

			// Add the modifier style when one was given.
			if (!string.IsNullOrEmpty(modifier))
				chip.AddToClassList("ctp-chip--" + modifier);

			return chip;
		}

		/// <summary>
		///     Turns a C# type name into a readable caption: ConstantNumber becomes Constant, NodeStateCondition becomes Node
		///     State.
		/// </summary>
		public static string Humanize(string typeName)
		{
			// Strip the first known type suffix, if the name has one.
			var name = typeName;
			foreach (var suffix in new[] { "Definition", "Number", "Condition", "Effect", "Rule" })
				if (name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length)
				{
					name = name.Substring(0, name.Length - suffix.Length);
					break;
				}

			// Insert a space at each lower-to-upper case boundary.
			return Regex.Replace(name, "([a-z0-9])([A-Z])", "$1 $2");
		}
	}

	/// <summary>Labeled action offered in a searchable choice dialog.</summary>
	public sealed class ContentChoice
	{
		/// <summary>Gets the callback invoked when the user picks this row.</summary>
		public readonly Action Choose;

		/// <summary>Gets optional toolbar icon names passed to <see cref="ContentEditorUI.Decorate" />.</summary>
		public readonly string[] Icons;

		/// <summary>Gets the display label and filter text for the choice.</summary>
		public readonly string Label;

		/// <summary>Initializes a content editor choice row.</summary>
		/// <param name="label">Visible label and search text.</param>
		/// <param name="choose">Action executed when selected.</param>
		/// <param name="icons">Optional icon resource names.</param>
		public ContentChoice(string label, Action choose, string[] icons = null)
		{
			// Store the label, the callback, and the optional icons.
			Label = label;
			Choose = choose;
			Icons = icons;
		}
	}

	/// <summary>Utility window listing filterable authoring choices.</summary>
	public sealed class ContentChoiceWindow : EditorWindow
	{
		/// <summary>Displays a searchable list of choices and runs the selected action.</summary>
		/// <param name="title">Window title.</param>
		/// <param name="choices">Choices to present.</param>
		public static void Show(string title, List<ContentChoice> choices)
		{
			// Create the utility window with the title and a minimum size.
			var window = CreateInstance<ContentChoiceWindow>();
			window.titleContent = new GUIContent(title);
			window.minSize = new Vector2(400, 300);
			ContentEditorUI.Attach(window.rootVisualElement);

			// Add the filter field and the scroll list that holds the choice buttons.
			var filter = new ToolbarSearchField
				{ tooltip = "Type to filter. Enter picks the first match; Escape closes." };
			window.rootVisualElement.Add(filter);
			var scroll = new ScrollView();
			scroll.style.flexGrow = 1;
			window.rootVisualElement.Add(scroll);
			var visible = choices;

			// Local helper that rebuilds the button list for the current filter text.
			void Draw(string text)
			{
				// Clear the old buttons and keep only the choices that match the filter.
				scroll.Clear();
				visible = choices.Where(c => c.Label.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

				// Create one button per visible choice that runs it and closes the window.
				foreach (var choice in visible)
				{
					var picked = choice;
					var button = new Button(() =>
					{
						picked.Choose();
						window.Close();
					}) { text = choice.Label, tooltip = choice.Label };
					button.AddToClassList("ctp-choice");
					ContentEditorUI.Decorate(button, true, choice.Icons);
					scroll.Add(button);
				}
			}

			// Redraw the list as the filter changes, and handle Escape and Enter keys.
			filter.RegisterValueChangedCallback(e => Draw(e.newValue));
			filter.RegisterCallback<KeyDownEvent>(e =>
			{
				// Escape closes the window without choosing.
				if (e.keyCode == KeyCode.Escape)
				{
					window.Close();
				}
				else if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && visible.Count > 0)
				{
					// Enter runs the first visible match and closes the window.
					visible[0].Choose();
					window.Close();
				}
			});

			// Show the full list, display the window, and focus the filter.
			Draw("");
			window.ShowUtility();
			filter.Focus();
		}
	}

	/// <summary>Single-field prompt for applying a string change to the draft.</summary>
	public sealed class ContentTextPrompt : EditorWindow
	{
		/// <summary>Prompts for text and applies it through the supplied callback.</summary>
		/// <param name="title">Window title.</param>
		/// <param name="initial">Initial field value.</param>
		/// <param name="apply">Called with the entered text when the user applies.</param>
		public static void Show(string title, string initial, Action<string> apply)
		{
			// Create the prompt window with its title and minimum size.
			var window = CreateInstance<ContentTextPrompt>();
			window.titleContent = new GUIContent(title);
			window.minSize = new Vector2(500, 120);
			ContentEditorUI.Attach(window.rootVisualElement);

			// Create the text field that receives the new value.
			var field = new TextField("Value")
				{ value = initial, tooltip = "Enter applies to the draft. Escape cancels." };
			window.rootVisualElement.Add(field);

			// Local helper that applies the entered text and closes the prompt.
			void Commit()
			{
				apply(field.value);
				window.Close();
			}

			// Add the Apply button that commits the text.
			window.rootVisualElement.Add(new Button(Commit)
			{
				text = "Apply to draft", tooltip = "Apply this change to the draft. Save writes it to disk."
			});

			// Enter applies the text; Escape cancels.
			field.RegisterCallback<KeyDownEvent>(e =>
			{
				if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
					Commit();
				else if (e.keyCode == KeyCode.Escape)
					window.Close();
			});

			// Show the prompt and focus the text field.
			window.ShowUtility();
			field.Focus();
		}
	}

	/// <summary>Confirmation dialog for saving validated draft files with an optional revision name.</summary>
	public sealed class ContentSaveWindow : EditorWindow
	{
		/// <summary>Shows save scope, changed file count, and revision name before committing.</summary>
		/// <param name="scope">Summary of what will be saved.</param>
		/// <param name="fileCount">Number of changed files.</param>
		/// <param name="revisionName">Suggested revision label.</param>
		/// <param name="save">Called with the final revision name when the user confirms.</param>
		public static void Show(string scope, int fileCount, string revisionName, Action<string> save)
		{
			// Create the confirmation window with its title and minimum size.
			var window = CreateInstance<ContentSaveWindow>();
			window.titleContent = new GUIContent("Save content");
			window.minSize = new Vector2(460, 180);

			// Add the scope summary and the changed-file count.
			var root = window.rootVisualElement;
			ContentEditorUI.Attach(root);
			root.Add(new Label(scope) { style = { unityFontStyleAndWeight = FontStyle.Bold } });
			root.Add(new Label(fileCount +
							   " changed file(s) are validated before anything is written. Each changed save creates revisions in .data-history."));

			// Add the optional revision name field.
			var name = new TextField("Revision name")
			{
				value = revisionName ?? "",
				tooltip = "Optional label for the revisions this save creates. Leave blank for none."
			};
			root.Add(name);

			// Create the button row for Cancel and Save.
			var row = new VisualElement();
			row.AddToClassList("ctp-row");
			root.Add(row);
			row.Add(new Button(() => window.Close())
						{ text = "Cancel", tooltip = "Close without saving. Drafts are kept." });

			// Local helper that passes the revision name to the save callback and closes the window.
			void Commit()
			{
				save(name.value);
				window.Close();
			}

			// Add the Save button that commits the changes.
			row.Add(new Button(Commit)
						{ text = "Save", tooltip = "Validate and write the changes, creating revisions." });

			// Enter saves and Escape cancels.
			root.RegisterCallback<KeyDownEvent>(e =>
			{
				if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
					Commit();
				else if (e.keyCode == KeyCode.Escape)
					window.Close();
			});

			// Show the window and focus the name field.
			window.ShowUtility();
			name.Focus();
		}
	}

	/// <summary>Read-only review surface for disk diffs and optional overwrite confirmation.</summary>
	public sealed class ContentReviewWindow : EditorWindow
	{
		/// <summary>Displays review text with optional apply action for external edit resolution.</summary>
		/// <param name="title">Window title.</param>
		/// <param name="text">Read-only review body.</param>
		/// <param name="action">Optional primary button caption.</param>
		/// <param name="apply">Optional callback for the primary button.</param>
		public static void Show(string title, string text, string action = null, Action apply = null)
		{
			// Create the review window with its title and minimum size.
			var window = CreateInstance<ContentReviewWindow>();
			window.titleContent = new GUIContent(title);
			window.minSize = new Vector2(650, 400);
			ContentEditorUI.Attach(window.rootVisualElement);

			// Show the review text in a read-only, scrollable field.
			var scroll = new ScrollView();
			scroll.style.flexGrow = 1;
			var label = new TextField { multiline = true, value = text, isReadOnly = true };
			label.style.whiteSpace = WhiteSpace.Normal;
			scroll.Add(label);
			window.rootVisualElement.Add(scroll);

			// Create the button row with Close and the optional apply action.
			var row = new VisualElement();
			row.AddToClassList("ctp-row");
			window.rootVisualElement.Add(row);
			row.Add(new Button(() => window.Close()) { text = "Close", tooltip = "Close without changing anything" });

			// Add the apply button only when a callback was supplied.
			if (apply != null)
				row.Add(new Button(() =>
						{
							apply();
							window.Close();
						})
						{
							text = action,
							tooltip =
								"Accept the reviewed disk contents and write the draft over them. The previous disk version stays in history."
						});
			window.ShowUtility();
		}
	}

	/// <summary>Browser for committed local revisions with restore support.</summary>
	public sealed class ContentHistoryWindow : EditorWindow
	{
		/// <summary>Lists revision history for entries and restores selected revisions into the draft.</summary>
		/// <param name="history">Local history store for the workspace data directory.</param>
		/// <param name="workspace">Active authoring workspace receiving restores.</param>
		/// <param name="selectedKey">Manifest key to prioritize in the entry picker, or null.</param>
		/// <param name="restore">Called with revisions when the user restores.</param>
		public static void Show(ContentHistory history, ContentWorkspace workspace, string selectedKey,
								Action<IEnumerable<ContentRevision>> restore)
		{
			// Create the history window with its title, minimum size, and shared styles.
			var window = CreateInstance<ContentHistoryWindow>();
			window.titleContent = new GUIContent("Content revision history");
			window.minSize = new Vector2(850, 500);
			ContentEditorUI.Attach(window.rootVisualElement);

			// Escape closes the window.
			window.rootVisualElement.RegisterCallback<KeyDownEvent>(e =>
			{
				if (e.keyCode == KeyCode.Escape)
					window.Close();
			});

			// Load the committed transactions and flatten their revisions.
			var transactions = history.Transactions().Where(t => t.committed).ToList();
			var all = transactions.SelectMany(t => t.revisions).ToList();
			var keys = all.Select(r => r.entry.key).Distinct().ToList();

			// Show an informational message when no revisions exist yet.
			if (keys.Count == 0)
			{
				window.rootVisualElement.Add(new HelpBox(
												 "No saved revisions yet. The first changed save captures the original disk version and your new version. History is stored in .data-history beside data.",
												 HelpBoxMessageType.Info));
				window.ShowUtility();
				return;
			}

			// Move the selected entry to the top of the picker when one is selected.
			if (selectedKey != null)
				keys = keys.OrderBy(k => k == selectedKey ? 0 : 1).ToList();

			// Create the entry picker that chooses which entry's revisions are shown.
			var picker = new PopupField<string>("Entry (including deleted)", keys, keys.FirstOrDefault(),
												k => all.Last(r => r.entry.key == k).entry.file,
												k => all.Last(r => r.entry.key == k).entry.file);
			picker.tooltip = "Choose an entry, including deleted entries, to inspect its revisions";
			window.rootVisualElement.Add(picker);

			// Create the two-pane split: revision list on the left, details on the right.
			var split = new TwoPaneSplitView(0, 300, TwoPaneSplitViewOrientation.Horizontal);
			split.style.flexGrow = 1;
			window.rootVisualElement.Add(split);
			var left = new ScrollView();
			var right = new ScrollView();
			split.Add(left);
			split.Add(right);

			// Local helper that lists the revisions of one entry in the left pane.
			void Entries(string key)
			{
				// Clear both panes and collect this entry's revisions.
				left.Clear();
				right.Clear();
				var revisions = all.Where(r => r.entry.key == key).ToList();

				// Add one button per revision, newest first.
				foreach (var revision in revisions.AsEnumerable().Reverse())
				{
					// Mark the revision that matches the current draft.
					var current = workspace.Draft.Current.TryGetValue(revision.entry.file, out var json) &&
								  ContentHistory.Hash(json) == revision.hash;

					// Create a button that shows this revision's details when selected.
					var button = new Button(() => Details(revision, revisions))
					{
						text = (current ? "● " : "") + revision.timestamp + "\n" + revision.operation + " / " +
							   (revision.label ?? ""),
						tooltip = current ? "Matches the current draft" : "Select to inspect this revision"
					};

					// Allow the two-line caption to wrap, then add the button to the list.
					button.style.whiteSpace = WhiteSpace.Normal;
					button.style.minHeight = 50;
					left.Add(button);
				}
			}

			// Local helper that shows one revision's identifiers, name, diff, and restore actions.
			void Details(ContentRevision revision, List<ContentRevision> revisions)
			{
				// Clear the right pane and show the identifiers and parent of this revision.
				right.Clear();
				right.Add(new Label(revision.id + "\nParent: " + (revision.parent ?? "none") + "\nSHA256: " +
									revision.hash));

				// Show an editable name field for this revision.
				var label = new TextField("Revision name")
				{
					value = revision.label ?? "",
					tooltip = "Name this revision. Names survive later edits to the entry."
				};
				right.Add(label);

				// Add the button that stores the new name without creating a revision.
				right.Add(new Button(() =>
						  {
							  history.Name(revision.entry.key, revision.id, label.value);
							  revision.label = label.value;
							  Entries(revision.entry.key);
							  Details(revision, revisions);
						  })
						  {
							  text = "Save revision name",
							  tooltip = "Store the revision name without creating a new revision"
						  });

				// Find the previous revision and build the raw-comparison toggle.
				var index = revisions.IndexOf(revision);
				var previous = index > 0 ? revisions[index - 1].json : null;
				var raw = new Toggle("Raw JSON comparison")
					{ tooltip = "Show the complete before and after JSON instead of property changes" };
				right.Add(raw);

				// Show the diff, updated when the raw toggle changes.
				var diff = new TextField
				{
					multiline = true, isReadOnly = true, value = ContentHistory.Diff(previous, revision.json, false)
				};
				right.Add(diff);
				raw.RegisterValueChangedCallback(e => diff.value =
													 ContentHistory.Diff(previous, revision.json, e.newValue));

				// Add the button that restores this revision into the draft and closes the window.
				right.Add(new Button(() =>
						  {
							  restore(new[] { revision });
							  window.Close();
						  })
						  {
							  text = revision.json == null ? "Restore deletion to draft" : "Restore entry to draft",
							  tooltip =
								  "Load this revision into the draft. Validate and Save All to make it authoritative."
						  });

				// Add the button that restores every entry changed by this transaction.
				right.Add(new Button(() =>
						  {
							  var transaction = transactions.Single(t => t.id == revision.transaction);
							  restore(transaction.revisions.GroupBy(r => r.entry.key).Select(g => g.Last()));
							  window.Close();
						  })
						  {
							  text = "Restore transaction to draft",
							  tooltip =
								  "Restore every entry changed by this multi-entry operation, such as an ID rename"
						  });

				// Explain that restores only change the draft until they are validated and saved.
				right.Add(new HelpBox(
							  "Restores are drafts. Validate and Save All to create new revisions. Missing dependencies must be restored from their own entry histories above; existing history remains intact.",
							  HelpBoxMessageType.Info));
			}

			// Refresh the list when the entry picker changes.
			picker.RegisterValueChangedCallback(e => Entries(e.newValue));

			// Show the first entry's revisions, or a placeholder when none exist.
			if (keys.Count > 0)
				Entries(keys[0]);
			else
				left.Add(new Label("No saved revisions yet. History begins with the first changed save."));

			// Show the window.
			window.ShowUtility();
		}
	}
}