using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cashg.GraphExplorer;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace HackThePlanet.Editor
{
    public sealed class ContentEditorWindow : EditorWindow
    {
        private enum Redraw { None, Form, Window }

        private const string DescriptionsPref = "HackThePlanet.ContentEditor.ShowDescriptions";
        private static readonly (string Name, string[] Items)[] TabGroups =
        {
            ("Setup", new[] { "Game", "Career", "Rewards" }),
            ("Rig", new[] { "Rig Rules", "Chassis", "Modules" }),
            ("Gameplay", new[] { "Actions", "Technologies", "Hardware", "Statuses", "Node Types" }),
            ("World", new[] { "Companies", "Contracts", "Networks" }),
            ("Presentation", new[] { "Text Palette", "Graph Visuals", "Graph Labels" }),
        };
        private static readonly string[] LongTextFields = { "description", "text", "aftermath", "narrative", "welcome" };
        private static readonly string[] ValueFieldNames = { "value", "input", "stat", "operation", "state", "key" };

        [SerializeField] private string category = "Contracts", selectedKey, search = "", revisionName = "";
        [SerializeField] private float splitWidth = 280;
        [SerializeField] private ContentDraft draft;
        private ContentWorkspace workspace;
        private ContentHistory history;
        private ListView list;
        private ScrollView form;
        private VisualElement banner;
        private Button saveAllButton, validateButton, descriptionsButton;
        private Label statusLabel, countLabel, headerUnsaved;
        private readonly List<string> visibleKeys = new List<string>();
        private readonly Dictionary<string, VisualElement> propertyTargets = new Dictionary<string, VisualElement>();
        private readonly HashSet<string> openHelp = new HashSet<string>();
        private readonly List<(Foldout Fold, string Label, object Value)> ruleHeaders = new List<(Foldout Fold, string Label, object Value)>();
        private List<string> lastValidation;
        private DateTime? lastSaved;
        private bool validationMode;
        private string DataDirectory=>workspace?.DirectoryPath??GameData.DirectoryPath;
        private bool rebuilding;
        private string moduleFamily="All",moduleTier="All";
        private bool previewRotated;
        private ContentDraftFile[] savedBaseline;
        private string RecoveryPath => Path.Combine(Path.GetDirectoryName(GameData.DirectoryPath), "Library/ContentEditor/draft.json");
        private static bool ShowDescriptions => EditorPrefs.GetBool(DescriptionsPref, false);

        [MenuItem("Hack The Planet/Content Editor")]
        public static void Open()
        {
            var window = GetWindow<ContentEditorWindow>(); window.titleContent = new GUIContent("Content Editor");
            if (!window.docked)
            {
                var main = EditorGUIUtility.GetMainWindowPosition();
                window.position = new Rect(main.x + 100, main.y + 100, Math.Min(1400, Math.Max(850, main.width - 200)), Math.Min(850, Math.Max(500, main.height - 200)));
            }
            window.Show(); window.Focus();
        }
        private void OnEnable()
        {
            minSize = new Vector2(850, 500);
            Undo.undoRedoPerformed += OnUndo;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }
        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndo; EditorApplication.playModeStateChanged -= OnPlayMode;
            Cache(); workspace?.Dispose(); workspace = null;
        }
        public void CreateGUI()
        {
            if(validationMode)return;
            ContentEditorUI.Attach(rootVisualElement);
            try
            {
                history = new ContentHistory(GameData.DirectoryPath); history.Recover();
                if (draft == null)
                {
                    draft = CreateInstance<ContentDraft>(); draft.hideFlags = HideFlags.HideAndDontSave;
                    if (File.Exists(RecoveryPath)) JsonUtility.FromJsonOverwrite(File.ReadAllText(RecoveryPath), draft);
                }
                workspace?.Dispose(); workspace = new ContentWorkspace(draft, GameData.DirectoryPath);
                savedBaseline = draft.originals;
                DrawWindow();
            }
            catch (Exception ex)
            {
                rootVisualElement.Clear(); rootVisualElement.Add(new HelpBox(ex.Message, HelpBoxMessageType.Error));
                rootVisualElement.Add(new Button(() => EditorUtility.RevealInFinder(GameData.DirectoryPath)) { text = "Open data directory", tooltip = "Open the data folder in Explorer" });
                rootVisualElement.Add(new Button(CreateGUI) { text = "Retry loading", tooltip = "Load the JSON content again" });
            }
        }

        private void DrawWindow()
        {
            rebuilding = true; rootVisualElement.Clear(); countLabel = null; headerUnsaved = null;
            rootVisualElement.AddToClassList("ctp-root");
            var chrome = new VisualElement(); chrome.AddToClassList("ctp-chrome");
            chrome.Add(BuildCategoryTabs()); chrome.Add(BuildToolbar());
            rootVisualElement.Add(chrome);
            banner = new VisualElement(); banner.AddToClassList("ctp-banner"); rootVisualElement.Add(banner);
            form = new ScrollView(); form.AddToClassList("ctp-form"); form.style.flexGrow = 1; form.style.paddingLeft = form.style.paddingRight = 12;
            list = new ListView { itemsSource = visibleKeys, fixedItemHeight = 36, selectionType = SelectionType.Single };
            list.style.flexGrow = 1; list.makeItem = BuildItem; list.bindItem = (element, index) => BindItem(element, visibleKeys[index]);
            list.selectionChanged += values => { if (rebuilding) return; selectedKey = values.OfType<string>().FirstOrDefault(); ShowEntry(false); };
            if (EntriesFor(category).Count() > 1)
            {
                var split = new TwoPaneSplitView(0, splitWidth, TwoPaneSplitViewOrientation.Horizontal); split.style.flexGrow = 1;
                split.Add(BuildSidebar()); split.Add(form); rootVisualElement.Add(split);
            }
            else rootVisualElement.Add(form);
            rootVisualElement.Add(BuildStatusBar());
            RefreshList(); rebuilding = false; ShowEntry(false); RenderBanner(); UpdateStatus();
        }

        private VisualElement BuildCategoryTabs()
        {
            var scroll = new ScrollView(ScrollViewMode.Horizontal); scroll.AddToClassList("ctp-tabs");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Auto;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            var row = new VisualElement(); row.AddToClassList("ctp-tab-row"); scroll.Add(row);
            foreach (var group in TabGroups)
            {
                var box = new VisualElement(); box.AddToClassList("ctp-tab-group"); row.Add(box);
                var title = new Label(group.Name); title.AddToClassList("ctp-group-label"); box.Add(title);
                var strip = new VisualElement(); strip.AddToClassList("ctp-tab-strip"); box.Add(strip);
                foreach (string name in group.Items)
                {
                    string tab = name; int count = EntriesFor(tab).Count();
                    var button = new Button(() => { category = tab; selectedKey = null; DrawWindow(); }) { text = tab, tooltip = tab + " (" + count + (count == 1 ? " entry)" : " entries)") };
                    button.AddToClassList("ctp-tab"); button.EnableInClassList("ctp-tab--active", category == tab);
                    ContentEditorUI.Decorate(button, true, ContentEditorUI.CategoryIconNames(tab));
                    strip.Add(button);
                }
            }
            return scroll;
        }

        private Toolbar BuildToolbar()
        {
            var toolbar = new Toolbar();
            bool singleton = ContentWorkspace.Singleton(category);
            toolbar.Add(ToolButton("New", "New", "Create a new " + category + " definition with a unique ID and file.", () => Mutate(() => { selectedKey = workspace.Add(category).key; }, Redraw.Window), !singleton, "Toolbar Plus"));
            toolbar.Add(ToolButton("Duplicate", "Duplicate", "Copy the selected definition with a new ID and file. Outgoing references are kept.", () => Mutate(() => { selectedKey = workspace.Add(Selected.category, selectedKey).key; }, Redraw.Window), Selected != null && !ContentWorkspace.Singleton(Selected.category), "Clipboard"));
            toolbar.Add(ToolButton("Delete", "Delete", "Delete the selected definition. Blocked while other content still references it.", Delete, Selected != null && !ContentWorkspace.Singleton(Selected.category), "TreeEditor.Trash"));
            toolbar.Add(Separator());
            toolbar.Add(ToolButton("Save", "Save", "Save only the selected entry. Other changed entries stay as drafts.", () => RequestSave(false), true, "SaveAs"));
            saveAllButton = ToolButton("SaveAll", "Save All", "Save every changed entry, including structural edits, and create revisions.", () => RequestSave(true), true, "SaveAs");
            toolbar.Add(saveAllButton);
            toolbar.Add(Separator());
            validateButton = ToolButton("Validate", "Validate", "Check the complete proposed catalog and property descriptions. Click a message to jump to it.", Validate, true, "d_Valid", "Valid");
            toolbar.Add(validateButton);
            toolbar.Add(ToolButton("GenerateAssets","Generate Assets","Generate Unity asset mirrors from saved, validated JSON.",GameplayAssetGenerator.Generate,true,"Refresh"));
            toolbar.Add(ToolButton("History", "History", "Browse saved revisions and restore an entry or a transaction to the draft.", () => ContentHistoryWindow.Show(history, workspace, selectedKey, Restore), true, "d_UnityEditor.HistoryWindow", "UnityEditor.HistoryWindow"));
            toolbar.Add(ToolButton("Reload", "Reload Disk", "Discard every unsaved draft and reload all JSON files from disk.", ReloadDisk, true, "Refresh"));
            toolbar.Add(Separator());
            var more = new ToolbarMenu { text = "More", tooltip = "Less common actions" };
            more.menu.AppendAction("Revert selected entry to saved version", _ => Revert(), _ => Selected != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            toolbar.Add(more);
            var spacer = new VisualElement(); spacer.AddToClassList("ctp-spacer"); toolbar.Add(spacer);
            descriptionsButton = ToolButton("Descriptions", "Descriptions", "Show every property description inline. When off, use the ? button on a property or hover over its label.", ToggleDescriptions, true, "_Help");
            toolbar.Add(descriptionsButton);
            return toolbar;
        }

        private VisualElement BuildSidebar()
        {
            var sidebar = new VisualElement(); sidebar.style.minWidth = 180; sidebar.AddToClassList("ctp-sidebar");
            sidebar.RegisterCallback<GeometryChangedEvent>(e => { if (e.newRect.width > 0) splitWidth = e.newRect.width; });
            var head = new VisualElement(); head.AddToClassList("ctp-sidebar-head"); sidebar.Add(head);
            var title = new Label(category); title.AddToClassList("ctp-sidebar-title"); head.Add(title);
            countLabel = new Label(); countLabel.AddToClassList("ctp-count"); head.Add(countLabel);
            var filter = new ToolbarSearchField { value = search, tooltip = "Search by title, ID, or file path" };
            filter.RegisterValueChangedCallback(e => { search = e.newValue; RefreshList(); });
            sidebar.Add(filter);
            if(category=="Modules")
            {
                var families=new[]{"All"}.Concat(workspace.Bundle.Game.modules.Select(m=>m.family).Distinct()).ToList();
                var family=new PopupField<string>("Family",families,families.Contains(moduleFamily)?moduleFamily:"All");family.RegisterValueChangedCallback(e=>{moduleFamily=e.newValue;RefreshList();});sidebar.Add(family);
                var tiers=new[]{"All"}.Concat(workspace.Bundle.Game.modules.Select(m=>m.tier.ToString()).Distinct()).ToList();
                var tier=new PopupField<string>("Tier",tiers,tiers.Contains(moduleTier)?moduleTier:"All");tier.RegisterValueChangedCallback(e=>{moduleTier=e.newValue;RefreshList();});sidebar.Add(tier);
            }
            sidebar.Add(list);
            var order = new VisualElement(); order.AddToClassList("ctp-order-row"); sidebar.Add(order);
            order.Add(ContentEditorUI.IconButton("↑ Move up", "Move the selected entry earlier in authored order.", () => Mutate(() => workspace.Move(selectedKey, -1), Redraw.Window)));
            order.Add(ContentEditorUI.IconButton("↓ Move down", "Move the selected entry later in authored order.", () => Mutate(() => workspace.Move(selectedKey, 1), Redraw.Window)));
            return sidebar;
        }

        private VisualElement BuildStatusBar()
        {
            var bar = new VisualElement(); bar.AddToClassList("ctp-statusbar");
            statusLabel = new Label(); statusLabel.AddToClassList("ctp-statusbar-text"); bar.Add(statusLabel);
            return bar;
        }

        private VisualElement BuildItem()
        {
            var row = new VisualElement(); row.AddToClassList("ctp-item");
            var status = new VisualElement(); status.AddToClassList("ctp-item-status"); row.Add(status);
            var icon = new Image { name = "warnIcon", image = ContentEditorUI.FindIcon("console.erroricon.sml", "console.erroricon"), pickingMode = PickingMode.Ignore };
            icon.AddToClassList("ctp-icon"); status.Add(icon);
            var glyph = new Label("!") { name = "warnText" }; glyph.AddToClassList("ctp-error-glyph"); status.Add(glyph);
            var count = new Label { name = "issueCount" }; count.AddToClassList("ctp-error-glyph"); status.Add(count);
            var texts = new VisualElement(); texts.AddToClassList("ctp-item-text"); row.Add(texts);
            var title = new Label { name = "title" }; title.AddToClassList("ctp-item-title"); texts.Add(title);
            var sub = new Label { name = "sub" }; sub.AddToClassList("ctp-item-sub"); texts.Add(sub);
            var dirty = new Label("●") { name = "dirty" }; dirty.AddToClassList("ctp-dirty"); row.Add(dirty);
            return row;
        }

        private void BindItem(VisualElement element, string key)
        {
            if(workspace==null)return;
            var entry = workspace.Bundle.Manifest.entries.Single(e => e.key == key);
            var definition = workspace.Bundle.Objects[key] as GameplayDefinition;
            int issues = EntryErrors(entry).Count();
            element.Q<Label>("title").text = definition != null && !string.IsNullOrWhiteSpace(definition.title) ? definition.title : Path.GetFileNameWithoutExtension(entry.file);
            element.Q<Label>("sub").text = definition is RigModuleDefinition module?module.family+" / T"+module.tier+" / "+module.width+"x"+module.height+" / "+module.price+" cr":definition?.id ?? entry.file;
            var icon = element.Q<Image>("warnIcon"); var glyph = element.Q<Label>("warnText");
            bool showIssue = issues > 0;
            icon.style.display = showIssue && icon.image != null ? DisplayStyle.Flex : DisplayStyle.None;
            glyph.style.display = showIssue && icon.image == null ? DisplayStyle.Flex : DisplayStyle.None;
            var count = element.Q<Label>("issueCount");
            count.text = showIssue ? issues.ToString(CultureInfo.InvariantCulture) : "";
            count.style.display = showIssue ? DisplayStyle.Flex : DisplayStyle.None;
            element.Q<Label>("dirty").style.display = Dirty(entry.file) ? DisplayStyle.Flex : DisplayStyle.None;
            element.tooltip = showIssue ? issues + " issue(s). Run Validate for details." : null;
        }

        private ToolbarButton ToolButton(string name, string label, string tooltip, Action action, bool enabled, params string[] icons)
        {
            var button = new ToolbarButton(action) { text = label, name = name, tooltip = tooltip };
            button.SetEnabled(enabled);
            return ContentEditorUI.Decorate(button, true, icons);
        }
        private static VisualElement Separator() { var separator = new VisualElement(); separator.AddToClassList("ctp-sep"); return separator; }
        private ContentEntry Selected => workspace?.Bundle.Manifest.entries.FirstOrDefault(e => e.key == selectedKey);
        private IEnumerable<ContentEntry> EntriesFor(string tab) => workspace.Bundle.Manifest.entries.Where(e => e.category == tab || tab == "Graph Visuals" && e.category == "Network Visuals");
        private bool Dirty(string file) => !draft.Original.TryGetValue(file, out string old) || !draft.Current.TryGetValue(file, out string next) || old != next;
        private IEnumerable<string> EntryErrors(ContentEntry entry) => workspace.Bundle.Errors.Concat(lastValidation ?? new List<string>()).Where(e => e.Contains(entry.file)).Distinct();

        private void RefreshList()
        {
            if (workspace == null) return;
            bool previous = rebuilding; rebuilding = true; visibleKeys.Clear();
            var entries = EntriesFor(category).ToList();
            foreach (var entry in entries)
            {
                var obj = workspace.Bundle.Objects[entry.key] as GameplayDefinition;
                if(obj is RigModuleDefinition module&&((moduleFamily!="All"&&module.family!=moduleFamily)||(moduleTier!="All"&&module.tier.ToString()!=moduleTier)))continue;
                if (string.IsNullOrWhiteSpace(search) || ((obj?.title ?? "") + " " + (obj?.id ?? "") + " " + entry.file).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) visibleKeys.Add(entry.key);
            }
            if (!visibleKeys.Contains(selectedKey)) selectedKey = visibleKeys.FirstOrDefault();
            if (countLabel != null) countLabel.text = visibleKeys.Count == entries.Count ? entries.Count.ToString() : visibleKeys.Count + " of " + entries.Count;
            if (list != null) { list.Rebuild(); if (selectedKey != null) list.SetSelectionWithoutNotify(new[] { visibleKeys.IndexOf(selectedKey) }); }
            rebuilding = previous;
        }

        private void ShowEntry(bool keepScroll = false)
        {
            if (form == null) return;
            float scroll = form.scrollOffset.y;
            form.Clear(); propertyTargets.Clear(); ruleHeaders.Clear(); headerUnsaved = null;
            var entry = Selected;
            bool manageable = entry != null && !ContentWorkspace.Singleton(entry.category);
            rootVisualElement.Q<ToolbarButton>("Duplicate")?.SetEnabled(manageable);
            rootVisualElement.Q<ToolbarButton>("Delete")?.SetEnabled(manageable);
            if (entry == null) { form.Add(new Label("No entries. Create a new definition or clear the search filter.")); return; }
            var obj = workspace.Bundle.Objects[entry.key];
            form.Add(BuildHeader(entry, obj as GameplayDefinition));
            if (workspace.Bundle.InvalidEntries.Contains(entry.key))
            {
                form.Add(new HelpBox(string.Join("\n", workspace.Bundle.Errors.Where(e => e.StartsWith(entry.file + ":", StringComparison.Ordinal))), HelpBoxMessageType.Error));
                form.Add(new Label("This file could not be decoded. Fix it externally, then Reload Disk. Its contents will not be replaced by defaults."));
                return;
            }
            DrawRigPreview(form,obj);DrawObject(form, obj, "$", 0);
            if (keepScroll) form.schedule.Execute(() => form.scrollOffset = new Vector2(0, scroll));
        }

        internal static Dictionary<string,string> ExerciseRigAuthoring(string directory)
        {
            var window=CreateInstance<ContentEditorWindow>();window.validationMode=true;window.Show();
            var draft=CreateInstance<ContentDraft>();
            try
            {
                window.draft=draft;window.workspace=new ContentWorkspace(draft,directory);window.history=new ContentHistory(directory);window.savedBaseline=draft.originals;
                ContentEditorUI.Attach(window.rootVisualElement);
                void Select(string category,string id){window.category=category;window.selectedKey=window.workspace.Bundle.Manifest.entries.Single(e=>e.category==category&&((GameplayDefinition)window.workspace.Bundle.Objects[e.key]).id==id).key;window.DrawWindow();}
                void Float(string field,float value){window.propertyTargets["$."+field].Q<FloatField>().value=value;}
                void Integer(string field,int value){window.propertyTargets["$."+field].Q<IntegerField>().value=value;}
                Select("Modules","power-1");Float("powerCapacity",9);
                Select("Modules","cooling-1");Float("coolingCapacity",7);
                Select("Chassis","chassis-1");Float("storageCapacity",81);
                Select("Actions","backdoor");Float("memoryCost",11);
                Select("Rig Rules","rig");Float("memoryReclaim",5);
                Select("Modules","recycler");Integer("price",401);
                Select("Modules","drive");window.propertyTargets["$.modifiers[0].value.value"].Q<FloatField>().value=21;
                Select("Modules","storage-array");window.propertyTargets["$.modifiers[1].value.left.right.value"].Q<FloatField>().value=3;
                Select("Networks","corporate-network");window.propertyTargets["$.payloadStorage.right.value"].Q<FloatField>().value=25;
                Select("Modules","ram");Integer("width",2);
                // Restore the starter footprint before validating the full draft.
                Integer("width",1);
                window.workspace.Capture();return draft.Current;
            }
            finally{window.hasUnsavedChanges=false;DestroyImmediate(window);DestroyImmediate(draft);}
        }

        private void DrawRigPreview(VisualElement parent,object obj)
        {
            if(obj is RigModuleDefinition module)
            {
                var preview=new VisualElement();preview.style.flexDirection=FlexDirection.Column;parent.Add(preview);
                var size=GameSimulation.PartSize(module,previewRotated);
                for(int y=0;y<size.y;y++){var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;preview.Add(row);for(int x=0;x<size.x;x++){var cell=new Label(" ");cell.style.width=cell.style.height=28;cell.style.marginRight=cell.style.marginBottom=2;cell.style.backgroundColor=Palette.Branch(module.branch);row.Add(cell);}}
                parent.Add(new Button(()=>{previewRotated=!previewRotated;ShowEntry(true);}){text="Rotate footprint preview"});
                parent.Add(new Label("Footprint "+size.x+"x"+size.y+" / "+module.powerDemand+" power / "+module.heatOutput+" heat / +"+module.powerCapacity+" power limit / +"+module.coolingCapacity+" cooling limit"));
            }
            if(obj is RigRulesDefinition rig&&rig.startingChassis!=null)
            {
                var chassis=rig.startingChassis;float power=0,heat=0,pc=chassis.powerCapacity,cc=chassis.coolingCapacity;
                foreach(var part in rig.startingParts.Where(p=>p.module!=null)){power+=part.module.powerDemand;heat+=part.module.heatOutput;pc+=part.module.powerCapacity;cc+=part.module.coolingCapacity;}
                parent.Add(new Label("STARTER LAYOUT / POWER "+power+" / "+pc+" / COOLING "+heat+" / "+cc));
                for(int y=0;y<chassis.rows;y++)
                {
                    var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;parent.Add(row);
                    for(int x=0;x<chassis.columns;x++)
                    {
                        var occupants=rig.startingParts.Where(p=>p.module!=null&&x>=p.column&&y>=p.row&&x<p.column+GameSimulation.PartSize(p.module,p.rotated).x&&y<p.row+GameSimulation.PartSize(p.module,p.rotated).y).ToArray();
                        var cell=new Label(occupants.Length>1?"OVERLAP":occupants.Length==0?"EMPTY":occupants[0].module.title);cell.style.width=100;cell.style.height=42;cell.style.whiteSpace=WhiteSpace.Normal;cell.style.backgroundColor=occupants.Length==1?Palette.Branch(occupants[0].module.branch):Color.gray;cell.style.color=Color.black;cell.style.marginRight=cell.style.marginBottom=2;row.Add(cell);
                    }
                }
            }
        }

        private VisualElement BuildHeader(ContentEntry entry, GameplayDefinition definition)
        {
            var header = new VisualElement(); header.AddToClassList("ctp-header");
            var top = new VisualElement(); top.AddToClassList("ctp-header-top"); header.Add(top);
            string title = definition == null || string.IsNullOrWhiteSpace(definition.title) ? Path.GetFileNameWithoutExtension(entry.file) : definition.title;
            var titleLabel = new Label(title); titleLabel.AddToClassList("ctp-title"); top.Add(titleLabel);
            top.Add(ContentEditorUI.Chip(entry.category, null));
            headerUnsaved = ContentEditorUI.Chip("Unsaved", "warn"); top.Add(headerUnsaved);
            if (workspace.Bundle.InvalidEntries.Contains(entry.key)) top.Add(ContentEditorUI.Chip("Invalid file", "error"));
            var actions = new ToolbarMenu { text = "Actions", tooltip = "File and identity actions for this entry" };
            actions.AddToClassList("ctp-actions");
            actions.menu.AppendAction("Open file in Explorer", _ => EditorUtility.RevealInFinder(ContentJson.SafePath(GameData.DirectoryPath, entry.file)));
            actions.menu.AppendAction("Rename ID and references...", _ => ContentTextPrompt.Show("Rename ID and references", definition.id, value => Mutate(() => workspace.RenameId(entry.key, value), Redraw.Window)), _ => definition != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            actions.menu.AppendAction("Rename JSON file...", _ => ContentTextPrompt.Show("Rename relative JSON file", entry.file, value => Mutate(() => workspace.RenameFile(entry.key, value), Redraw.Window)));
            top.Add(actions);
            var sub = new Label((definition?.id ?? "no ID") + "  ·  " + entry.file); sub.AddToClassList("ctp-subtitle"); header.Add(sub);
            return header;
        }

        private void DrawObject(VisualElement parent, object obj, string path, int depth)
        {
            if (depth > 60) { parent.Add(new HelpBox("Rule nesting limit reached.", HelpBoxMessageType.Error)); return; }
            foreach (var field in ContentJson.Fields(obj.GetType()))
            {
                var container = new VisualElement(); container.AddToClassList("ctp-prop"); parent.Add(container);
                string fieldPath = path + "." + field.Name; propertyTargets[fieldPath] = container;
                string description;
                try { description = ContentFieldHelp.Description(field); } catch (Exception ex) { description = ex.Message; }
                container.tooltip = description;
                object value = field.GetValue(obj);
                bool compound = IsCompound(field.FieldType);
                VisualElement host = container;
                if (!compound) { host = new VisualElement(); host.AddToClassList("ctp-prop-row"); container.Add(host); }
                DrawValue(host, ContentFieldHelp.DisplayName(field.Name), field.FieldType, value, next => field.SetValue(obj, next), fieldPath, depth, field);
                if (field.Name == "id" && obj is GameplayDefinition) host.Q<TextField>()?.SetEnabled(false);

                string helpKey = selectedKey + "|" + fieldPath;
                var help = new Label(description); help.AddToClassList("ctp-help");
                help.style.display = ShowDescriptions || openHelp.Contains(helpKey) ? DisplayStyle.Flex : DisplayStyle.None;
                var toggle = new Button { text = "?", tooltip = ShowDescriptions ? "Descriptions are always shown. Turn them off in the toolbar." : "Show or hide this property's description" };
                toggle.AddToClassList("ctp-help-toggle");
                toggle.clicked += () =>
                {
                    if (ShowDescriptions) return;
                    if (!openHelp.Remove(helpKey)) openHelp.Add(helpKey);
                    help.style.display = openHelp.Contains(helpKey) ? DisplayStyle.Flex : DisplayStyle.None;
                };
                host.Add(toggle);
                container.Add(help);
            }
        }

        private static bool IsCompound(Type type)
        {
            if (type.IsArray || ContentJson.IsRule(type)) return true;
            return !(type == typeof(string) || type == typeof(bool) || type == typeof(int) || type == typeof(float) || type.IsEnum || type == typeof(Color) || type == typeof(Vector2) || type == typeof(Vector3) || typeof(GameplayDefinition).IsAssignableFrom(type));
        }

        private static T Field<T>(VisualElement parent, T element, bool compact = false) where T : VisualElement
        {
            element.AddToClassList("ctp-field"); if (compact) element.AddToClassList("ctp-field--compact");
            parent.Add(element); return element;
        }

        private void DrawValue(VisualElement parent, string label, Type type, object value, Action<object> setter, string path, int depth, FieldInfo field = null)
        {
            void Set(object next, Redraw redraw = Redraw.None) => Mutate(() => setter(next), redraw);
            if (type == typeof(string))
            {
                var line = new VisualElement(); line.AddToClassList("ctp-field-line"); parent.Add(line);
                bool multiline = field?.GetCustomAttribute<TextAreaAttribute>() != null || LongTextFields.Contains(field?.Name);
                var text = Field(line, new TextField(label) { value = (string)value ?? "", multiline = multiline });
                if (multiline) { text.style.minHeight = 60; text.style.whiteSpace = WhiteSpace.Normal; }
                text.isDelayed = !multiline; text.RegisterValueChangedCallback(e => Set(e.newValue));
                var choices = StringChoices(field?.Name).Distinct().ToList();
                if (choices.Count > 0)
                {
                    var picker = new Button(() => ContentChoiceWindow.Show(label, choices.Select(s => new ContentChoice(s, () => { Set(s); text.SetValueWithoutNotify(s); })).ToList())) { text = "Choose...", tooltip = "Pick an existing " + label.ToLowerInvariant() + " value" };
                    picker.AddToClassList("ctp-suffix"); line.Add(picker);
                }
                return;
            }
            if (type == typeof(bool)) { var control = new Toggle(label) { value = value is bool b && b }; control.RegisterValueChangedCallback(e => Set(e.newValue)); parent.Add(control); return; }
            var range = field?.GetCustomAttribute<RangeAttribute>();
            if (type == typeof(int))
            {
                if (range != null) { var slider = Field(parent, new SliderInt(label, (int)range.min, (int)range.max) { value = (int)value, showInputField = true }); slider.RegisterValueChangedCallback(e => Set(e.newValue)); }
                else { var integer = Field(parent, new IntegerField(label) { value = (int)value, isDelayed = true }, true); integer.RegisterValueChangedCallback(e => Set(e.newValue)); }
                return;
            }
            if (type == typeof(float))
            {
                if (range != null) { var slider = Field(parent, new Slider(label, range.min, range.max) { value = (float)value, showInputField = true }); slider.RegisterValueChangedCallback(e => Set(e.newValue)); }
                else { var number = Field(parent, new FloatField(label) { value = (float)value, isDelayed = true }, true); number.RegisterValueChangedCallback(e => Set(e.newValue)); }
                return;
            }
            if (type.IsEnum) { var choice = Field(parent, new EnumField(label, (Enum)value), true); choice.RegisterValueChangedCallback(e => Set(e.newValue)); return; }
            if (type == typeof(Color)) { var color = Field(parent, new ColorField(label) { value = (Color)value, hdr = true, showAlpha = true }, true); color.RegisterValueChangedCallback(e => Set(e.newValue)); return; }
            if (type == typeof(Vector2)) { var vector = Field(parent, new Vector2Field(label) { value = (Vector2)value }, true); vector.RegisterValueChangedCallback(e => Set(e.newValue)); return; }
            if (type == typeof(Vector3)) { var vector = Field(parent, new Vector3Field(label) { value = (Vector3)value }, true); vector.RegisterValueChangedCallback(e => Set(e.newValue)); return; }
            if (typeof(GameplayDefinition).IsAssignableFrom(type))
            {
                var current = value as GameplayDefinition;
                bool optional = field?.DeclaringType == typeof(ActionCondition) || field?.DeclaringType == typeof(ObjectiveDefinition) && field.Name == "action";
                var row = new VisualElement(); row.AddToClassList("ctp-ref-row"); parent.Add(row);
                var caption = new Label(label); caption.AddToClassList("ctp-label"); row.Add(caption);
                var pick = new Button(() =>
                {
                    var choices = workspace.Bundle.Objects.Values.OfType<GameplayDefinition>().Where(type.IsInstanceOfType)
                        .Select(d => new ContentChoice(d.title + " [" + d.id + "]", () => Set(d, Redraw.Form), ContentEditorUI.CategoryIconNames(CategoryOf(d)))).ToList();
                    // Null is meaningful for generic ActionCondition and optional Objective.action only.
                    if (optional) choices.Insert(0, new ContentChoice("None (optional)", () => Set(null, Redraw.Form)));
                    ContentChoiceWindow.Show(label, choices);
                }) { text = current == null ? "Choose " + ContentEditorUI.Humanize(type.Name) + "..." : current.title + "  [" + current.id + "]", tooltip = "Choose a " + ContentEditorUI.Humanize(type.Name).ToLowerInvariant() + " reference" };
                pick.AddToClassList("ctp-ref-pick");
                ContentEditorUI.Decorate(pick, true, "Search Icon");
                row.Add(pick);
                if (current != null)
                {
                    var go = new Button(() => NavigateTo(current)) { text = "Go to", tooltip = "Open " + current.title + " in its category" };
                    go.AddToClassList("ctp-ref-action");
                    ContentEditorUI.Decorate(go, true, "d_forward", "forward");
                    row.Add(go);
                }
                if (optional && current != null) row.Add(ContentEditorUI.IconButton("×", "Clear this optional reference", () => Set(null, Redraw.Form)));
                return;
            }
            if (type.IsArray)
            {
                var array = (Array)value ?? Array.CreateInstance(type.GetElementType(), 0);
                var fold = new Foldout { text = label + " (" + array.Length + ")", value = false, viewDataKey = selectedKey + path }; parent.Add(fold);
                var elementType = type.GetElementType();
                var add = new Button(() => Set(Resize(array, NewValue(elementType)), Redraw.Form)) { text = "Add item", tooltip = "Append a new item to " + label };
                ContentEditorUI.Decorate(add, true, "Toolbar Plus"); fold.Add(add);
                for (int i = 0; i < array.Length; i++)
                {
                    int index = i;
                    var item = new VisualElement(); item.AddToClassList("ctp-array-item"); fold.Add(item);
                    var head = new VisualElement(); head.AddToClassList("ctp-array-head"); item.Add(head);
                    var itemTitle = new Label("Item " + (i + 1)); itemTitle.AddToClassList("ctp-array-title"); head.Add(itemTitle);
                    head.Add(ContentEditorUI.IconButton("↑", "Move this item earlier", () => Set(Reorder(array, index, -1), Redraw.Form)));
                    head.Add(ContentEditorUI.IconButton("↓", "Move this item later", () => Set(Reorder(array, index, 1), Redraw.Form)));
                    head.Add(ContentEditorUI.IconButton("Copy", "Duplicate this item", () => Set(Resize(array, CloneValue(array.GetValue(index), elementType)), Redraw.Form), "Clipboard"));
                    head.Add(ContentEditorUI.IconButton("×", "Remove this item", () => Set(Remove(array, index), Redraw.Form), "TreeEditor.Trash"));
                    DrawValue(item, "Value", elementType, array.GetValue(i), v => array.SetValue(v, index), path + "[" + i + "]", depth + 1, field);
                }
                return;
            }
            var group = new Foldout { text = label, value = false, viewDataKey = selectedKey + path }; group.AddToClassList("ctp-group"); parent.Add(group);
            if (ContentJson.IsRule(type))
            {
                ruleHeaders.Add((group, label, value));
                group.text = label + ": " + RuleSummary(value);
                group.Add(new Button(() => ContentChoiceWindow.Show("Rule type", ContentJson.RuleTypes.Where(type.IsAssignableFrom)
                    .Select(t => new ContentChoice(ContentEditorUI.Humanize(t.Name) + "  (" + t.Name + ")", () => Set(Activator.CreateInstance(t), Redraw.Form))).ToList()))
                { text = "Change rule type...", tooltip = "Replace this rule with another type. Its settings reset to the new type's defaults." });
            }
            else if (value == null) group.Add(new Button(() => Set(NewValue(type), Redraw.Form)) { text = "Create " + label, tooltip = "Create " + label + " with default settings" });
            if (value != null) DrawObject(group, value, path, depth + 1);
        }

        private string CategoryOf(object definition) => workspace.Bundle.Manifest.entries.FirstOrDefault(e => ReferenceEquals(workspace.Bundle.Objects[e.key], definition))?.category ?? "";

        private void NavigateTo(GameplayDefinition target)
        {
            var entry = workspace.Bundle.Manifest.entries.FirstOrDefault(e => ReferenceEquals(workspace.Bundle.Objects[e.key], target));
            if (entry == null) return;
            category = entry.category == "Network Visuals" ? "Graph Visuals" : entry.category;
            selectedKey = entry.key; DrawWindow();
        }

        private static string RuleSummary(object value)
        {
            if (value == null) return "not set";
            string name = ContentEditorUI.Humanize(value.GetType().Name);
            foreach (var field in ContentJson.Fields(value.GetType()))
            {
                if (!ValueFieldNames.Contains(field.Name)) continue;
                object v = field.GetValue(value);
                if (v == null || v is string text && text.Length == 0) continue;
                string shown;
                if (v is float f) shown = f.ToString("0.###", CultureInfo.InvariantCulture);
                else if (v is int || v is bool || v is string || v.GetType().IsEnum) shown = Convert.ToString(v, CultureInfo.InvariantCulture);
                else continue;
                return name + " " + shown;
            }
            return name;
        }

        private IEnumerable<string> StringChoices(string name)
        {
            if (name == "stat" || name == "displayedStat") return workspace.Bundle.Game.career.stats.Select(s => s.id).Concat(new[] { "security", "duration", "baseTrace", "trace" });
            if (name == "tag" || name == "tags") return workspace.Bundle.Game.nodeTypes.SelectMany(t => t.tags).Concat(workspace.Bundle.Game.actions.SelectMany(a => a.tags));
            if (new[] { "nodeId", "a", "b", "entryNode", "preferredNode", "previewNode", "entrance", "destination", "sensorAttachment", "directRoute", "sensors", "secondaryTargets", "archives", "reserved" }.Contains(name))
            {
                var obj = Selected == null ? null : workspace.Bundle.Objects[Selected.key];
                var network = obj as NetworkDefinition ?? (obj as ContractDefinition)?.network;
                return network?.nodes.Select(n => n.id).Concat(network.routes?.extraIds ?? Array.Empty<string>()) ?? Enumerable.Empty<string>();
            }
            if(name=="kind"||name=="family")return workspace.Bundle.Game.modules.Select(m=>m.family).Concat(workspace.Bundle.Game.modules.Select(m=>m.id)).Concat(workspace.Bundle.Game.modules.Select(m=>m.branch)).Distinct();
            if (name == "key") return workspace.Bundle.Game.modules.Select(m=>m.family).Concat(workspace.Bundle.Game.modules.Select(m=>m.id)).Concat(workspace.Bundle.Game.modules.Select(m=>m.branch)).Concat( workspace.Bundle.Game.hardware.Select(h => h.id).Concat(workspace.Bundle.Game.statuses.Select(s => s.id)));
            return Enumerable.Empty<string>();
        }
        private object NewValue(Type type)
        {
            if (type == typeof(string)) return "";
            if (type.IsArray) return Array.CreateInstance(type.GetElementType(), 0);
            if (typeof(GameplayDefinition).IsAssignableFrom(type)) return workspace.Bundle.Objects.Values.FirstOrDefault(type.IsInstanceOfType);
            if (ContentJson.IsRule(type)) return Activator.CreateInstance(ContentJson.RuleTypes.First(type.IsAssignableFrom));
            return Activator.CreateInstance(type);
        }
        private object CloneValue(object value, Type type)
        {
            if (value == null || type.IsValueType || value is string || value is ScriptableObject) return value;
            // Clone nested managed objects without duplicating definition references.
            var wrapper = new CloneBox { item = value }; return CloneManaged(wrapper.item);
        }
        private object CloneManaged(object value)
        {
            if (value == null || value is string || value.GetType().IsValueType || value is ScriptableObject) return value;
            if (value is Array a) { var next = Array.CreateInstance(value.GetType().GetElementType(), a.Length); for (int i = 0; i < a.Length; i++) next.SetValue(CloneManaged(a.GetValue(i)), i); return next; }
            var copy = Activator.CreateInstance(value.GetType()); foreach (var field in ContentJson.Fields(value.GetType())) field.SetValue(copy, CloneManaged(field.GetValue(value))); return copy;
        }
        private sealed class CloneBox { public object item; }
        private static Array Resize(Array source, object value) { var next = Array.CreateInstance(source.GetType().GetElementType(), source.Length + 1); Array.Copy(source, next, source.Length); next.SetValue(value, source.Length); return next; }
        private static Array Remove(Array source, int index) { var next = Array.CreateInstance(source.GetType().GetElementType(), source.Length - 1); int n = 0; for (int i = 0; i < source.Length; i++) if (i != index) next.SetValue(source.GetValue(i), n++); return next; }
        private static Array Reorder(Array source, int index, int delta) { int other = index + delta; var next = (Array)source.Clone(); if (other >= 0 && other < source.Length) { object item = next.GetValue(index); next.SetValue(next.GetValue(other), index); next.SetValue(item, other); } return next; }

        private void Mutate(Action action, Redraw redraw = Redraw.None)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorUtility.DisplayDialog("Stop Play mode", "Content editing is disabled while playing. Changes load on the next launch.", "OK"); return; }
            string before = JsonUtility.ToJson(draft);
            try
            {
                Undo.RecordObject(draft, "Edit JSON content"); action(); workspace.Capture(); EditorUtility.SetDirty(draft); Cache();
                RefreshList();
                if (redraw == Redraw.Window) DrawWindow();
                else if (redraw == Redraw.Form) ShowEntry(true);
                UpdateStatus();
            }
            catch (Exception ex) { JsonUtility.FromJsonOverwrite(before, draft); workspace.Reload(); DrawWindow(); Error(ex); }
        }
        private void UpdateDirty() { hasUnsavedChanges = draft != null && draft.Dirty; saveChangesMessage = "Save all JSON content changes? Each changed save creates per-entry revisions."; }

        /// <summary>Refreshes toolbar counts, status bar, property markers and rule summaries without rebuilding the form.</summary>
        private void UpdateStatus()
        {
            if (draft == null || workspace == null) return;
            UpdateDirty();
            int unsaved = UnsavedCount(), errors = ErrorCount();
            if (saveAllButton != null) saveAllButton.text = unsaved > 0 ? "Save All (" + unsaved + ")" : "Save All";
            if (validateButton != null) validateButton.text = errors > 0 ? "Validate (" + errors + ")" : "Validate";
            if (descriptionsButton != null) descriptionsButton.text = "Descriptions: " + (ShowDescriptions ? "On" : "Off");
            if (headerUnsaved != null) headerUnsaved.style.display = Selected != null && Dirty(Selected.file) ? DisplayStyle.Flex : DisplayStyle.None;
            if (statusLabel != null) statusLabel.text = unsaved + " unsaved file(s)   ·   " + errors + " error(s)   ·   " + (lastSaved.HasValue ? "last saved " + lastSaved.Value.ToString("HH:mm") : "not saved this session");
            ApplyMarkers();
            foreach (var header in ruleHeaders) header.Fold.text = header.Label + ": " + RuleSummary(header.Value);
        }
        private int UnsavedCount()
        {
            var original = draft.Original; var current = draft.Current;
            return current.Keys.Union(original.Keys).Count(f => !original.TryGetValue(f, out var o) || !current.TryGetValue(f, out var c) || o != c);
        }
        private int ErrorCount() => workspace.Bundle.Errors.Concat(lastValidation ?? new List<string>()).Distinct().Count();

        private void ApplyMarkers()
        {
            if (propertyTargets.Count == 0) return;
            var entry = Selected;
            var changed = ChangedPaths(entry);
            var errorKeys = new HashSet<string>();
            if (entry != null && lastValidation != null)
                foreach (var message in lastValidation.Where(m => m.Contains(entry.file)))
                {
                    string key = PropertyFor(message);
                    if (key != null) errorKeys.Add(key);
                }
            foreach (var pair in propertyTargets)
            {
                pair.Value.EnableInClassList("ctp-changed", changed.Contains(pair.Key));
                pair.Value.EnableInClassList("ctp-error", errorKeys.Contains(pair.Key));
            }
        }
        private HashSet<string> ChangedPaths(ContentEntry entry)
        {
            var changed = new HashSet<string>();
            if (entry == null) return changed;
            var original = draft.Original; var current = draft.Current;
            if (!original.TryGetValue(entry.file, out var before) || !current.TryGetValue(entry.file, out var after) || before == after) return changed;
            using (var oldDocument = JsonDocument.Parse(before))
            using (var newDocument = JsonDocument.Parse(after))
                CollectChanges(newDocument.RootElement, oldDocument.RootElement, "$", changed);
            return changed;
        }
        private static void CollectChanges(JsonElement current, JsonElement original, string path, HashSet<string> changed)
        {
            if (current.GetRawText() == original.GetRawText()) return;
            changed.Add(path);
            if (current.ValueKind == JsonValueKind.Object && original.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in current.EnumerateObject())
                {
                    string child = path + "." + property.Name;
                    if (original.TryGetProperty(property.Name, out var previous)) CollectChanges(property.Value, previous, child, changed);
                    else changed.Add(child);
                }
            }
            else if (current.ValueKind == JsonValueKind.Array && original.ValueKind == JsonValueKind.Array)
            {
                int i = 0;
                foreach (var item in current.EnumerateArray())
                {
                    string child = path + "[" + i + "]";
                    if (i < original.GetArrayLength()) CollectChanges(item, original[i], child, changed); else changed.Add(child);
                    i++;
                }
            }
        }
        private string PropertyFor(string message) => propertyTargets.Keys.Where(k => message.Contains(k) || message.Contains(k.Substring(2))).OrderByDescending(k => k.Length).FirstOrDefault();

        private void Cache()
        {
            if (draft == null||validationMode) return;
            if (!draft.Dirty) { if (File.Exists(RecoveryPath)) File.Delete(RecoveryPath); return; }
            ContentHistory.AtomicWrite(RecoveryPath, JsonUtility.ToJson(draft, true));
        }
        private void OnUndo() { if (workspace == null) return; try { draft.originals = savedBaseline; workspace.Reload(); Cache(); DrawWindow(); } catch (Exception ex) { Error(ex); } }
        private void OnPlayMode(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode) return;
            if (draft != null && draft.Dirty)
            {
                int choice = EditorUtility.DisplayDialogComplex("Unsaved JSON content", "Save all changes before entering Play mode?", "Save All", "Cancel", "Discard");
                if (choice == 1 || choice == 0 && !Save(true)) { EditorApplication.isPlaying = false; return; }
                if (choice == 2) DiscardChanges();
            }
            // Runtime initialization handles resetting its own launch snapshot.
        }
        public override void SaveChanges() { if (Save(true)) base.SaveChanges(); }
        public override void DiscardChanges() { if (draft != null) { draft.Set(draft.Original); workspace?.Reload(); Cache(); UpdateDirty(); } base.DiscardChanges(); if (workspace != null) DrawWindow(); }

        private void RequestSave(bool all)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Error(new InvalidOperationException("Stop Play mode before saving content.")); return; }
            if (!draft.Dirty) { Save(all); return; }
            int count = all ? UnsavedCount() : Selected != null && Dirty(Selected.file) ? 1 : 0;
            string scope = all ? "Save all changes" : "Save " + (Selected?.file ?? "selected entry");
            ContentSaveWindow.Show(scope, count, revisionName, name => { revisionName = name ?? ""; Save(all); });
        }

        private bool Save(bool all, bool reviewedOverwrite = false, IDictionary<string, string> reviewedDisk = null)
        {
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode && EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before saving content.");
                var desired = all ? draft.Current : draft.Original;
                if (!all && Selected != null)
                {
                    if (Dirty("manifest.json")) throw new InvalidOperationException("Registration, ordering, ID, or filename changes may span entries. Use Save All for structural edits.");
                    desired[Selected.file] = draft.Current[Selected.file];
                }
                using (var candidate = ContentJson.Load(GameData.DirectoryPath, desired)) { }
                history.Commit(desired, draft.Original, revisionName, draft.operation, reviewedOverwrite, reviewedDisk);
                draft.originals = desired.Select(p => new ContentDraftFile { file = p.Key, text = p.Value }).ToArray();
                savedBaseline = draft.originals;
                draft.operation = "Save"; revisionName = ""; lastSaved = DateTime.Now; lastValidation = new List<string>(); Cache(); DrawWindow(); return true;
            }
            catch (IOException ex)
            {
                if (ex.Message.StartsWith("External edit conflict:", StringComparison.Ordinal))
                {
                    var diffs = new List<string>(); var reviewed = new Dictionary<string, string>();
                    foreach (string file in draft.Original.Keys.Union(draft.Current.Keys))
                    { string path = ContentJson.SafePath(GameData.DirectoryPath, file); string disk = File.Exists(path) ? File.ReadAllText(path) : null; reviewed[file] = disk; draft.Original.TryGetValue(file, out var original); if (disk != original) diffs.Add(file + "\n" + ContentHistory.Diff(original, disk, true)); }
                    ContentReviewWindow.Show("External changes — review before overwrite", string.Join("\n", diffs), "Overwrite reviewed files", () => Save(all, true, reviewed));
                }
                else Error(ex); return false;
            }
            catch (Exception ex) { ShowErrors(new[] { ex.Message }); return false; }
        }
        private void Validate()
        {
            var errors = new List<string>(); try { using (var bundle = ContentJson.Load(GameData.DirectoryPath, draft.Current)) { } } catch (Exception ex) { errors.Add(ex.Message); }
            workspace.Bundle.Errors.Clear(); workspace.Bundle.Errors.AddRange(errors); RefreshList();
            errors.AddRange(ContentFieldHelp.CheckCoverage()); ShowErrors(errors);
        }
        private void ShowErrors(IEnumerable<string> errors)
        {
            lastValidation = errors.ToList();
            RenderBanner(); UpdateStatus();
        }
        private void RenderBanner()
        {
            if (banner == null) return;
            banner.Clear();
            if (lastValidation == null) return;
            if (lastValidation.Count == 0) { var ok = new Label("All JSON content and property descriptions are valid."); ok.AddToClassList("ctp-ok"); banner.Add(ok); return; }
            var lines = lastValidation.SelectMany(m => m.Split('\n')).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            var fold = new Foldout { text = lines.Count + " validation issue(s). Click one to jump to it.", value = true };
            var summary = new ScrollView(); summary.AddToClassList("ctp-banner-list"); fold.Add(summary);
            foreach (var message in lines)
            {
                string captured = message;
                var button = new Button(() => NavigateError(captured)) { text = message, tooltip = "Jump to this issue" };
                button.AddToClassList("ctp-issue"); button.style.whiteSpace = WhiteSpace.Normal; summary.Add(button);
            }
            banner.Add(fold);
        }
        private void NavigateError(string error)
        {
            var entry = workspace.Bundle.Manifest.entries.FirstOrDefault(e => error.Contains(e.file) || error.Contains(workspace.Bundle.Objects[e.key].name));
            if (entry != null) { category = entry.category == "Network Visuals" ? "Graph Visuals" : entry.category; selectedKey = entry.key; DrawWindow(); }
            string key = PropertyFor(error);
            if (key == null || !propertyTargets.TryGetValue(key, out var target)) return;
            for (var p = target.parent; p != null; p = p.parent) if (p is Foldout fold) fold.value = true;
            target.AddToClassList("ctp-error");
            target.Add(new HelpBox(error, HelpBoxMessageType.Error));
            form.ScrollTo(target);
        }
        private void Delete()
        {
            if (Selected == null) return;
            var references = workspace.Incoming(selectedKey);
            if (references.Count > 0) { ContentReviewWindow.Show("Incoming references — deletion blocked", string.Join("\n", references)); return; }
            if (EditorUtility.DisplayDialog("Delete content entry", "Delete " + Selected.file + "? Save All records a recoverable deletion revision.", "Delete", "Cancel")) Mutate(() => { workspace.Delete(selectedKey); selectedKey = null; }, Redraw.Window);
        }
        private void Revert()
        {
            if (Selected == null) return;
            if (Dirty("manifest.json") || draft.operation == "Rename ID") { Error(new InvalidOperationException("Structural changes span entries. Use Reload Disk to discard all drafts.")); return; }
            Mutate(() => { var files = draft.Current; files[Selected.file] = draft.Original[Selected.file]; draft.Set(files); workspace.Reload(); }, Redraw.Window);
        }
        private void ReloadDisk()
        {
            if (draft.Dirty && !EditorUtility.DisplayDialog("Discard all unsaved changes", "Reload every JSON file from disk?", "Discard and reload", "Cancel")) return;
            try { draft.files = Array.Empty<ContentDraftFile>(); workspace.Dispose(); workspace = new ContentWorkspace(draft, GameData.DirectoryPath); savedBaseline = draft.originals; Cache(); DrawWindow(); } catch (Exception ex) { Error(ex); }
        }
        private void ToggleDescriptions()
        {
            EditorPrefs.SetBool(DescriptionsPref, !ShowDescriptions);
            ShowEntry(true); UpdateStatus();
        }
        private void Restore(IEnumerable<ContentRevision> revisions) => Mutate(() => workspace.Restore(revisions), Redraw.Window);
        private static void Error(Exception ex) => EditorUtility.DisplayDialog("Content Editor", ex.Message, "OK");
    }

    /// <summary>Shared visual helpers for the Content Editor windows: icons with text fallback, chips, and stylesheet attachment.</summary>
    internal static class ContentEditorUI
    {
        private const string StyleSheetPath = "Assets/HackThePlanet/Editor/ContentEditor.uss";
        private static readonly Dictionary<string, string[]> CategoryIcons = new Dictionary<string, string[]>
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
            { "Graph Labels", new[] { "Font Icon" } },
        };

        public static string[] CategoryIconNames(string category) => category != null && CategoryIcons.TryGetValue(category, out var names) ? names : Array.Empty<string>();

        public static void Attach(VisualElement root)
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (sheet != null) root.styleSheets.Add(sheet);
        }

        /// <summary>Returns the first built-in Unity icon that resolves, preferring the dark-skin variant when the editor uses it.</summary>
        public static Texture FindIcon(params string[] names)
        {
            if (names == null) return null;
            bool pro = EditorGUIUtility.isProSkin;
            foreach (string name in names)
            {
                string plain=name.StartsWith("d_",StringComparison.Ordinal)?name.Substring(2):name;
                foreach (string candidate in pro ? new[] { "d_" + plain, plain } : new[] { plain, "d_" + plain })
                {
                    var content = EditorGUIUtility.IconContent(candidate);
                    if (content != null && content.image != null) return content.image;
                }
            }
            return null;
        }

        /// <summary>Adds an icon in front of the button label. When keepText is false and an icon resolves, the text is removed (icon-only).</summary>
        public static T Decorate<T>(T button, bool keepText, params string[] icons) where T : Button
        {
            var texture = FindIcon(icons);
            if (texture == null) return button;
            var image = new Image { image = texture, pickingMode = PickingMode.Ignore };
            image.AddToClassList("ctp-icon");
            button.Insert(0, image);
            button.AddToClassList("ctp-btn-with-icon");
            if (!keepText) button.text = "";
            return button;
        }

        public static Button IconButton(string text, string tooltip, Action action, params string[] icons)
        {
            var button = new Button(action) { text = text, tooltip = tooltip };
            button.AddToClassList("ctp-icon-btn");
            return Decorate(button, false, icons);
        }

        public static Label Chip(string text, string modifier)
        {
            var chip = new Label(text); chip.AddToClassList("ctp-chip");
            if (!string.IsNullOrEmpty(modifier)) chip.AddToClassList("ctp-chip--" + modifier);
            return chip;
        }

        /// <summary>Turns a C# type name into a readable caption: ConstantNumber becomes Constant, NodeStateCondition becomes Node State.</summary>
        public static string Humanize(string typeName)
        {
            string name = typeName;
            foreach (string suffix in new[] { "Definition", "Number", "Condition", "Effect", "Rule" })
            {
                if (name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length) { name = name.Substring(0, name.Length - suffix.Length); break; }
            }
            return Regex.Replace(name, "([a-z0-9])([A-Z])", "$1 $2");
        }
    }

    public sealed class ContentChoice
    {
        public readonly string Label; public readonly Action Choose; public readonly string[] Icons;
        public ContentChoice(string label, Action choose, string[] icons = null) { Label = label; Choose = choose; Icons = icons; }
    }
    public sealed class ContentChoiceWindow : EditorWindow
    {
        public static void Show(string title, List<ContentChoice> choices)
        {
            var window = CreateInstance<ContentChoiceWindow>(); window.titleContent = new GUIContent(title); window.minSize = new Vector2(400, 300);
            ContentEditorUI.Attach(window.rootVisualElement);
            var filter = new ToolbarSearchField { tooltip = "Type to filter. Enter picks the first match; Escape closes." }; window.rootVisualElement.Add(filter);
            var scroll = new ScrollView(); scroll.style.flexGrow = 1; window.rootVisualElement.Add(scroll);
            var visible = choices;
            void Draw(string text)
            {
                scroll.Clear();
                visible = choices.Where(c => c.Label.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                foreach (var choice in visible)
                {
                    var picked = choice;
                    var button = new Button(() => { picked.Choose(); window.Close(); }) { text = choice.Label, tooltip = choice.Label };
                    button.AddToClassList("ctp-choice");
                    ContentEditorUI.Decorate(button, true, choice.Icons);
                    scroll.Add(button);
                }
            }
            filter.RegisterValueChangedCallback(e => Draw(e.newValue));
            filter.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Escape) window.Close();
                else if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && visible.Count > 0) { visible[0].Choose(); window.Close(); }
            });
            Draw(""); window.ShowUtility(); filter.Focus();
        }
    }
    public sealed class ContentTextPrompt : EditorWindow
    {
        public static void Show(string title, string initial, Action<string> apply)
        {
            var window = CreateInstance<ContentTextPrompt>(); window.titleContent = new GUIContent(title); window.minSize = new Vector2(500, 120);
            ContentEditorUI.Attach(window.rootVisualElement);
            var field = new TextField("Value") { value = initial, tooltip = "Enter applies to the draft. Escape cancels." };
            window.rootVisualElement.Add(field);
            void Commit() { apply(field.value); window.Close(); }
            window.rootVisualElement.Add(new Button(Commit) { text = "Apply to draft", tooltip = "Apply this change to the draft. Save writes it to disk." });
            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Commit();
                else if (e.keyCode == KeyCode.Escape) window.Close();
            });
            window.ShowUtility(); field.Focus();
        }
    }
    public sealed class ContentSaveWindow : EditorWindow
    {
        public static void Show(string scope, int fileCount, string revisionName, Action<string> save)
        {
            var window = CreateInstance<ContentSaveWindow>(); window.titleContent = new GUIContent("Save content"); window.minSize = new Vector2(460, 180);
            var root = window.rootVisualElement; ContentEditorUI.Attach(root);
            root.Add(new Label(scope) { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            root.Add(new Label(fileCount + " changed file(s) are validated before anything is written. Each changed save creates revisions in .data-history."));
            var name = new TextField("Revision name") { value = revisionName ?? "", tooltip = "Optional label for the revisions this save creates. Leave blank for none." };
            root.Add(name);
            var row = new VisualElement(); row.AddToClassList("ctp-row"); root.Add(row);
            row.Add(new Button(() => window.Close()) { text = "Cancel", tooltip = "Close without saving. Drafts are kept." });
            void Commit() { save(name.value); window.Close(); }
            row.Add(new Button(Commit) { text = "Save", tooltip = "Validate and write the changes, creating revisions." });
            root.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Commit();
                else if (e.keyCode == KeyCode.Escape) window.Close();
            });
            window.ShowUtility(); name.Focus();
        }
    }
    public sealed class ContentReviewWindow : EditorWindow
    {
        public static void Show(string title, string text, string action = null, Action apply = null)
        {
            var window = CreateInstance<ContentReviewWindow>(); window.titleContent = new GUIContent(title); window.minSize = new Vector2(650, 400);
            ContentEditorUI.Attach(window.rootVisualElement);
            var scroll = new ScrollView(); scroll.style.flexGrow = 1; var label = new TextField { multiline = true, value = text, isReadOnly = true }; label.style.whiteSpace = WhiteSpace.Normal; scroll.Add(label); window.rootVisualElement.Add(scroll);
            var row = new VisualElement(); row.AddToClassList("ctp-row"); window.rootVisualElement.Add(row);
            row.Add(new Button(() => window.Close()) { text = "Close", tooltip = "Close without changing anything" });
            if (apply != null) row.Add(new Button(() => { apply(); window.Close(); }) { text = action, tooltip = "Accept the reviewed disk contents and write the draft over them. The previous disk version stays in history." });
            window.ShowUtility();
        }
    }
    public sealed class ContentHistoryWindow : EditorWindow
    {
        public static void Show(ContentHistory history, ContentWorkspace workspace, string selectedKey, Action<IEnumerable<ContentRevision>> restore)
        {
            var window = CreateInstance<ContentHistoryWindow>(); window.titleContent = new GUIContent("Content revision history"); window.minSize = new Vector2(850, 500);
            ContentEditorUI.Attach(window.rootVisualElement);
            window.rootVisualElement.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Escape) window.Close(); });
            var transactions = history.Transactions().Where(t => t.committed).ToList(); var all = transactions.SelectMany(t => t.revisions).ToList();
            var keys = all.Select(r => r.entry.key).Distinct().ToList();
            if (keys.Count == 0)
            {
                window.rootVisualElement.Add(new HelpBox("No saved revisions yet. The first changed save captures the original disk version and your new version. History is stored in .data-history beside data.", HelpBoxMessageType.Info));
                window.ShowUtility(); return;
            }
            if (selectedKey != null) keys = keys.OrderBy(k => k == selectedKey ? 0 : 1).ToList();
            var picker = new PopupField<string>("Entry (including deleted)", keys, keys.FirstOrDefault(), k => all.Last(r => r.entry.key == k).entry.file, k => all.Last(r => r.entry.key == k).entry.file);
            picker.tooltip = "Choose an entry, including deleted entries, to inspect its revisions";
            window.rootVisualElement.Add(picker); var split = new TwoPaneSplitView(0, 300, TwoPaneSplitViewOrientation.Horizontal); split.style.flexGrow = 1; window.rootVisualElement.Add(split);
            var left = new ScrollView(); var right = new ScrollView(); split.Add(left); split.Add(right);
            void Entries(string key)
            {
                left.Clear(); right.Clear(); var revisions = all.Where(r => r.entry.key == key).ToList();
                foreach (var revision in revisions.AsEnumerable().Reverse())
                {
                    bool current = workspace.Draft.Current.TryGetValue(revision.entry.file, out var json) && ContentHistory.Hash(json) == revision.hash;
                    var button = new Button(() => Details(revision, revisions)) { text = (current ? "● " : "") + revision.timestamp + "\n" + revision.operation + " / " + (revision.label ?? ""), tooltip = current ? "Matches the current draft" : "Select to inspect this revision" };
                    button.style.whiteSpace = WhiteSpace.Normal; button.style.minHeight = 50; left.Add(button);
                }
            }
            void Details(ContentRevision revision, List<ContentRevision> revisions)
            {
                right.Clear(); right.Add(new Label(revision.id + "\nParent: " + (revision.parent ?? "none") + "\nSHA256: " + revision.hash));
                var label = new TextField("Revision name") { value = revision.label ?? "", tooltip = "Name this revision. Names survive later edits to the entry." }; right.Add(label);
                right.Add(new Button(() => { history.Name(revision.entry.key, revision.id, label.value); revision.label = label.value; Entries(revision.entry.key); Details(revision, revisions); }) { text = "Save revision name", tooltip = "Store the revision name without creating a new revision" });
                int index = revisions.IndexOf(revision); string previous = index > 0 ? revisions[index - 1].json : null;
                var raw = new Toggle("Raw JSON comparison") { tooltip = "Show the complete before and after JSON instead of property changes" }; right.Add(raw);
                var diff = new TextField { multiline = true, isReadOnly = true, value = ContentHistory.Diff(previous, revision.json, false) }; right.Add(diff);
                raw.RegisterValueChangedCallback(e => diff.value = ContentHistory.Diff(previous, revision.json, e.newValue));
                right.Add(new Button(() => { restore(new[] { revision }); window.Close(); }) { text = revision.json == null ? "Restore deletion to draft" : "Restore entry to draft", tooltip = "Load this revision into the draft. Validate and Save All to make it authoritative." });
                right.Add(new Button(() => { var transaction = transactions.Single(t => t.id == revision.transaction); restore(transaction.revisions.GroupBy(r => r.entry.key).Select(g => g.Last())); window.Close(); }) { text = "Restore transaction to draft", tooltip = "Restore every entry changed by this multi-entry operation, such as an ID rename" });
                right.Add(new HelpBox("Restores are drafts. Validate and Save All to create new revisions. Missing dependencies must be restored from their own entry histories above; existing history remains intact.", HelpBoxMessageType.Info));
            }
            picker.RegisterValueChangedCallback(e => Entries(e.newValue)); if (keys.Count > 0) Entries(keys[0]); else left.Add(new Label("No saved revisions yet. History begins with the first changed save.")); window.ShowUtility();
        }
    }
}
