using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Cashg.GraphExplorer.Samples
{
	/// <summary>Self-contained graph authoring and rendering sample.</summary>
	[DisallowMultipleComponent]
	public sealed class GraphExplorerBuilderSample : MonoBehaviour
	{
		/// <summary>Whether nodes can be repositioned by dragging in the sample scene.</summary>
		public bool EnableNodeDragging = true;

		private sealed class AllEdges : IGraphEdgeFilter
		{
			public bool Visible = true;
			public bool EdgeMatches(IGraphEdge edge) => Visible;
		}

		private sealed class SampleDropdown : VisualElement
		{
			private readonly IReadOnlyList<string> choices;
			private readonly Action<SampleDropdown> opened;
			private readonly Action closed;
			private readonly Button trigger;
			private Action<string> changed;
			private VisualElement overlay;
			private string selected;
			public SampleDropdown(string label, IReadOnlyList<string> choices, int index, Action<SampleDropdown> opened, Action closed)
			{
				this.choices=choices; this.opened=opened; this.closed=closed;
				selected=choices[Mathf.Clamp(index,0,choices.Count-1)];
				style.marginTop=3; style.marginBottom=3; Add(new Label(label));
				trigger=new Button(()=>{if(overlay==null) Open(); else Close();}) { text=selected+"  ▾" };
				trigger.style.minHeight=34; trigger.style.backgroundColor=new Color(5/255f,40/255f,50/255f,1);
				trigger.style.color=new Color(233/255f,247/255f,248/255f,1);
				trigger.style.borderTopColor=trigger.style.borderBottomColor=trigger.style.borderLeftColor=trigger.style.borderRightColor=new Color(0,207/255f,196/255f,1);
				trigger.style.borderTopWidth=trigger.style.borderBottomWidth=trigger.style.borderLeftWidth=trigger.style.borderRightWidth=1;
				trigger.style.borderTopLeftRadius=trigger.style.borderTopRightRadius=trigger.style.borderBottomLeftRadius=trigger.style.borderBottomRightRadius=5;
				Add(trigger); RegisterCallback<DetachFromPanelEvent>(_=>Close());
			}
			public string value { get=>selected; set { if(!choices.Contains(value)) return; selected=value; trigger.text=selected+"  ▾"; } }
			public void RegisterValueChangedCallback(Action<string> callback)=>changed+=callback;
			public void Close(){if(overlay==null)return; overlay.RemoveFromHierarchy(); overlay=null; closed?.Invoke();}
			private void Open()
			{
				if(panel==null)return; opened?.Invoke(this); var panelRoot=panel.visualTree;
				overlay=new VisualElement { name="graph-explorer-dropdown-overlay", focusable=true };
				overlay.style.position=Position.Absolute; overlay.style.left=0; overlay.style.top=0;
				overlay.style.width=Length.Percent(100); overlay.style.height=Length.Percent(100);
				overlay.style.backgroundColor=new Color(0,0,0,.001f); panelRoot.Add(overlay);
				var menu=new ScrollView(ScrollViewMode.Vertical); menu.style.position=Position.Absolute;
				menu.style.backgroundColor=new Color(.035f,.055f,.085f,1);
				menu.style.borderTopLeftRadius=menu.style.borderTopRightRadius=menu.style.borderBottomLeftRadius=menu.style.borderBottomRightRadius=7;
				menu.style.borderTopWidth=menu.style.borderBottomWidth=menu.style.borderLeftWidth=menu.style.borderRightWidth=1;
				menu.style.borderTopColor=menu.style.borderBottomColor=menu.style.borderLeftColor=menu.style.borderRightColor=new Color(.15f,.24f,.34f);
				var bounds=trigger.worldBound; var height=Mathf.Min(300,Mathf.Max(120,panelRoot.layout.height-24));
				menu.style.left=Mathf.Clamp(bounds.xMin,12,Mathf.Max(12,panelRoot.layout.width-bounds.width-12));
				menu.style.top=Mathf.Clamp(bounds.yMax,12,Mathf.Max(12,panelRoot.layout.height-height-12));
				menu.style.width=Mathf.Min(bounds.width,panelRoot.layout.width-24); menu.style.maxHeight=height; overlay.Add(menu);
				var cursor=Mathf.Max(0,choices.ToList().IndexOf(selected)); var items=new List<Button>();
				foreach(var choice in choices){var itemValue=choice; var item=new Button(()=>Select(itemValue)){text=itemValue,focusable=false};
					item.style.minHeight=32; item.style.unityTextAlign=TextAnchor.MiddleLeft;
					item.style.backgroundColor=itemValue==selected?new Color(.1f,.25f,.31f):new Color(.035f,.055f,.085f);
					item.style.color=new Color(.88f,.93f,.98f); menu.Add(item); items.Add(item);}
				overlay.RegisterCallback<PointerDownEvent>(evt=>{if(evt.target!=overlay)return;Close();evt.StopImmediatePropagation();});
				overlay.RegisterCallback<NavigationMoveEvent>(evt=>evt.StopImmediatePropagation(),TrickleDown.TrickleDown);
				overlay.RegisterCallback<NavigationSubmitEvent>(evt=>evt.StopImmediatePropagation(),TrickleDown.TrickleDown);
				overlay.RegisterCallback<KeyDownEvent>(evt=>{
					if(evt.keyCode==KeyCode.Escape){Close();trigger.Focus();evt.StopImmediatePropagation();}
					else if(evt.keyCode==KeyCode.DownArrow||evt.keyCode==KeyCode.UpArrow){cursor=Mathf.Clamp(cursor+(evt.keyCode==KeyCode.DownArrow?1:-1),0,items.Count-1);menu.ScrollTo(items[cursor]);evt.StopImmediatePropagation();}
					else if(evt.keyCode==KeyCode.Return||evt.keyCode==KeyCode.Space){Select(choices[cursor]);trigger.Focus();evt.StopImmediatePropagation();}
				},TrickleDown.TrickleDown); overlay.Focus();
			}
			private void Select(string choice){var previous=selected;Close();if(previous==choice)return;selected=choice;trigger.text=selected+"  ▾";changed?.Invoke(selected);}
		}

		private readonly Dictionary<string, string> clusters = new(StringComparer.Ordinal);
		private readonly Dictionary<string, bool> regionBoundariesVisible = new(StringComparer.Ordinal);
		private readonly Dictionary<string, float> regionPadding = new(StringComparer.Ordinal);
		private readonly Dictionary<string, bool> regionContentsVisible = new(StringComparer.Ordinal);
		private readonly Dictionary<string, TextMesh> labels = new(StringComparer.Ordinal);
		private readonly Dictionary<string, TextMesh> regionLabels = new(StringComparer.Ordinal);
		private Camera graphCamera;
		private bool ownsCamera;
		private PanelSettings runtimePanelSettings;
		private GraphClusterScene clusterScene;
		private GraphDocument document;
		private GraphRegionController regions;
		private GraphRenderingSession rendering;
		private GraphScene sourceScene;
		private IGraphNode selected;
		private VisualElement root;
		private ScrollView uiScroll;
		private VisualElement controlPane;
		private Label status;
		private string activeTab = "Graph";
		private Toggle labelsToggle;
		private Slider labelSize;
		private Slider generationCount;
		private Slider generationDensity;
		private Slider generationSeed;
		private TextField nodeId;
		private TextField nodeLabel;
		private TextField nodeType;
		private TextField edgeId;
		private TextField edgeFrom;
		private TextField edgeTo;
		private TextField clusterName;
		private TextField clusterMembers;
		private TextField regionName;
		private TextField regionMembers;
		private SampleDropdown regionParent;
		private SampleDropdown regionSelection;
		private SampleDropdown openDropdown;
		private bool showLabels = true;
		private bool showRegionNames = true;
		private string detectedRegionId;
		private float labelScale = 1f;
		private int nextEdgeId = 1;
		private GraphSceneInput.PrimaryClickState clickState;
		private string lastUiClickKey;
		private float lastUiClickTime;
		private string highlightedNodeId;
		private string highlightedEdgeId;
		private readonly Dictionary<string, VisualElement> nodeListRows = new(StringComparer.Ordinal);
		private readonly Dictionary<string, VisualElement> edgeListRows = new(StringComparer.Ordinal);
		private GraphSceneInput.NodeDragState nodeDragState;
		private GraphTooltipController tooltipController;
		private Label tooltipLabel;

		// Stats panel state. Values are averaged over a short window so the readout is stable.
		private const float StatsWindowSeconds = .5f;
		private readonly Dictionary<string, Label> statValues = new(StringComparer.Ordinal);
		private float statsWindowSeconds;
		private int statsWindowFrames;
		private float statsWorstFrameMs;
		private float statsSimMsSum;
		private float statsScriptMsSum;
		private float statsFps;
		private float statsFrameMs;
		private float statsWorstMsShown;
		private float statsSimMs;
		private float statsScriptMs;

		public GraphExplorerSettings SettingsProfile;
        public string SettingsDirectory;
        private GraphSettingsController settingsController;
        private Label settingsDirty;
        private string settingsError;
        private GraphSettingsData Preferences => settingsController.Active.Data;

        private void InitializeSettings()
        {
            var store = new GraphSettingsStore(string.IsNullOrWhiteSpace(SettingsDirectory) ? null : SettingsDirectory);
            try { settingsController = new GraphSettingsController(store, SettingsProfile); }
            catch(Exception error) { settingsError=error.Message; settingsController=new GraphSettingsController(store); }
            try { if(store.LastSelectedId!=null) settingsController.Select(store.LastSelectedId,GraphSettingsSwitchChoice.Discard); }
            catch(Exception error) { settingsError=error.Message; }
        }
        private void ApplyPreferences()
        {
            rendering.ApplySettings(settingsController.Active);
            var data=Preferences;
            edgeFilter.Visible=data.EdgesVisible; showLabels=data.ShowLabels; labelScale=data.LabelScale;
            EnableNodeDragging=data.EnableNodeDragging; showRegionNames=data.ShowRegionNames;
            regions.RenderingEnabled=data.RegionsVisible;
            foreach(var region in document.Regions.Values) regions.SetRegionPadding(region.Id,regionPadding.TryGetValue(region.Id,out var padding)?padding:data.RegionPadding);
            GraphSettingsJsonForSampleCopy(data.Tooltips,tooltipController.Settings);
            RefreshLabels();
        }
        private static void GraphSettingsJsonForSampleCopy(object source,object target) => JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source),target);
        private void CapturePreferences()
        {
            if(settingsController==null || rendering==null)return;
            var data=Preferences;
            data.NodesVisible=rendering.Renderer.NodesVisible; data.EdgesVisible=edgeFilter.Visible;
            data.ClustersVisible=rendering.Renderer.ClustersVisible; data.RegionsVisible=regions.RenderingEnabled;
            data.ColorBy=rendering.Renderer.ColorBy; data.SizeBy=rendering.Renderer.Size; data.EdgeWidth=rendering.Renderer.EdgeWidthScale;
            data.ShowLabels=showLabels; data.LabelScale=labelScale; data.Background=graphCamera.backgroundColor;
            data.EnableNodeDragging=EnableNodeDragging; data.ShowRegionNames=showRegionNames;
            GraphSettingsJsonForSampleCopy(rendering.Layout,data.Physics);
            GraphSettingsJsonForSampleCopy(tooltipController.Settings,data.Tooltips);
            settingsController.NotifyChanged();
            if(settingsDirty!=null) settingsDirty.text=settingsController.IsDirty ? "Unsaved changes" : "Saved";
        }
        private void BuildProfileControls(VisualElement parent)
        {
            var box=Section(parent,"Settings profile");
            var choices=new List<string>(); var ids=new List<string>();
            choices.Add(settingsController.Active.DisplayName+" ("+settingsController.Active.ProfileId.Substring(0,8)+")"); ids.Add(settingsController.Active.ProfileId);
            foreach(var path in settingsController.Store.List())
            {
                try { var profile=settingsController.Store.Load(System.IO.Path.GetFileNameWithoutExtension(path));
                    if(!ids.Contains(profile.ProfileId)) { ids.Add(profile.ProfileId); choices.Add(profile.DisplayName+" ("+profile.ProfileId.Substring(0,8)+")"); }
                    DestroyOwned(profile);
                } catch(Exception error) { settingsError=error.Message; }
            }
            var picker=CreateDropdown("Profile",choices,0); box.Add(picker);
            var pending=new VisualElement(); box.Add(pending);
            picker.RegisterValueChangedCallback(evt=> {
                var id=ids[choices.IndexOf(evt)];
                if(id==settingsController.Active.ProfileId)return;
                pending.Clear();
                if(!settingsController.IsDirty) { ProfileAction(()=>settingsController.Select(id,GraphSettingsSwitchChoice.Discard)); return; }
                pending.Add(new Label("Save changes before switching?"));
                AddButton(pending,"Save and switch",()=>ProfileAction(()=>settingsController.Select(id,GraphSettingsSwitchChoice.Save)));
                AddButton(pending,"Discard and switch",()=>ProfileAction(()=>settingsController.Select(id,GraphSettingsSwitchChoice.Discard)));
                AddButton(pending,"Cancel",()=>RefreshUi());
            });
            var name=AddText(box,"Name",settingsController.Active.DisplayName);
            name.RegisterValueChangedCallback(evt=> { if(!string.IsNullOrWhiteSpace(evt.newValue)) { settingsController.Active.DisplayName=evt.newValue; settingsDirty.text=settingsController.IsDirty?"Unsaved changes":"Saved"; } });
            var actions=Row(box);
            AddButton(actions,"New",()=>ProfileAction(()=>settingsController.Create(name.value)));
            AddButton(actions,"Save As",()=>ProfileAction(()=>settingsController.Create(name.value)));
            AddButton(actions,"Save",()=>ProfileAction(()=>{settingsController.Active.DisplayName=name.value; settingsController.Save();}));
            AddButton(box,"Revert / Reload JSON",()=>ProfileAction(settingsController.Revert));
            settingsDirty=new Label(settingsController.IsDirty ? "Unsaved changes" : "Saved"); box.Add(settingsDirty);
            if(settingsError!=null) box.Add(new Label(settingsError));
        }
        private void ProfileAction(Action action)
        {
            try { action(); ApplyPreferences(); settingsError=null; RefreshUi(); }
            catch(Exception error) { settingsError=error.Message; SetStatus(error.Message); }
        }

        private void Awake()
		{
			try
			{
				InitializeCamera();
				InitializeSettings();
                CreateGraph(Preferences.NodeCount, Preferences.EdgeDensity, Preferences.Seed, true);
				InitializeRendering();
				InitializeUi();
                ApplyPreferences();
                RefreshUi();
				RefreshLabels();
			}
			catch (Exception exception)
			{
				Debug.LogException(exception, this);
				ShowFailure(exception.Message);
			}
		}

		private void OnDestroy()
		{
            settingsController?.Dispose();
			foreach (var label in labels.Values) DestroyOwned(label.gameObject);
			labels.Clear();
			foreach (var label in regionLabels.Values) DestroyOwned(label.gameObject);
			regionLabels.Clear();
			if (root != null) root.RemoveFromHierarchy();
			regions?.Dispose();
			clusterScene?.Dispose();
			rendering?.Dispose();
			DestroyOwned(runtimePanelSettings);
			if (ownsCamera) DestroyOwned(graphCamera != null ? graphCamera.gameObject : null);
		}

		private void Update()
		{
			if (rendering == null || clusterScene == null) return;
			var scriptStart = System.Diagnostics.Stopwatch.GetTimestamp();
			sourceScene.Synchronize();
			clusterScene.Synchronize();
			rendering.Layout.Bind(clusterScene);
			var simStart = System.Diagnostics.Stopwatch.GetTimestamp();
			rendering.Layout.Step(Time.deltaTime);
			var simMs = ElapsedMs(simStart);

			var mouse = Mouse.current;
			var blocked = root == null || mouse == null ||
				GraphSceneInput.IsUiBlockingGraph(root, mouse.position.ReadValue());
			GraphSceneInput.HoverSample sample = default;
			if (mouse != null)
				sample = GraphSceneInput.Sample(root, clusterScene, edgeFilter, rendering.Renderer,
					graphCamera, mouse.position.ReadValue());
			if (mouse != null)
			{
				if (EnableNodeDragging)
					GraphSceneInput.HandleNodeDrag(sample, mouse, ref nodeDragState, clusterScene, rendering.Layout, graphCamera);
				else if (!string.IsNullOrEmpty(nodeDragState.NodeId))
				{
					rendering.Layout.ReleaseDrag(nodeDragState.NodeId);
					nodeDragState = default;
				}

				GraphSceneInput.HandlePrimaryClick(sample, mouse, ref clickState, rendering.Navigation,
					clusterScene.View, node =>
					{
						selected = node;
						clusterScene.View.SelectedId = node?.Id ?? "";
						if (node != null) FillNodeForm(node);
					}, .35f);
				HandleGraphListDoubleClick(sample, mouse);
			}
			UpdateHoveredRegion(mouse, blocked);
			rendering.Hover.Update(sample, detectedRegionId);
			var tooltipData = !sample.Blocked && sample.Node != null
				? new GraphTooltipData(sample.Node.Id, sample.Node.Label, sample.Node.Position,
					graphCamera.WorldToScreenPoint(sample.Node.Position), mouse.position.ReadValue())
				: default;
			tooltipController.Update(tooltipData, Time.unscaledTime);
			rendering.Navigation.Tick(blocked, selected, GraphNavigation.FrameKeyPressed());
			rendering.Renderer.HoveredRegionId = rendering.Hover.HoveredRegionId;
			rendering.Renderer.Draw(clusterScene, edgeFilter, graphCamera, selected, sample.Node, sample.Edge);
			UpdateLabels();
			UpdateRegionLabels();
			UpdateStats(Time.unscaledDeltaTime, simMs, ElapsedMs(scriptStart));
		}

		private static float ElapsedMs(long startTimestamp) =>
			(float)((System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

		private void UpdateStats(float frameSeconds, float simMs, float scriptMs)
		{
			statsWindowSeconds += frameSeconds;
			statsWindowFrames++;
			statsWorstFrameMs = Mathf.Max(statsWorstFrameMs, frameSeconds * 1000f);
			statsSimMsSum += simMs;
			statsScriptMsSum += scriptMs;
			if (statsWindowSeconds < StatsWindowSeconds || statsWindowFrames == 0) return;

			statsFps = statsWindowFrames / statsWindowSeconds;
			statsFrameMs = statsWindowSeconds * 1000f / statsWindowFrames;
			statsWorstMsShown = statsWorstFrameMs;
			statsSimMs = statsSimMsSum / statsWindowFrames;
			statsScriptMs = statsScriptMsSum / statsWindowFrames;
			statsWindowSeconds = 0;
			statsWindowFrames = 0;
			statsWorstFrameMs = 0;
			statsSimMsSum = 0;
			statsScriptMsSum = 0;
			RefreshStats();
		}

		private void RefreshStats()
		{
			if (statValues.Count == 0 || rendering == null || clusterScene == null) return;
			var layout = rendering.Layout;
			var totalNodes = document.Nodes.Count;
			var totalEdges = document.Edges.Count;
			var visibleNodes = clusterScene.Nodes.Count;
			var awake = layout.AwakeNodeCount;
			var pinned = clusterScene.Nodes.Values.Count(node => node.Pinned);
			var asleep = Math.Max(0, visibleNodes - awake - pinned);
			var collapsed = clusters.Keys.Count(id => clusterScene.IsCollapsed(id));
			var possibleEdges = totalNodes * (totalNodes - 1) / 2.0;
			var density = possibleEdges > 0 ? totalEdges / possibleEdges : 0;
			var averageDegree = totalNodes > 0 ? 2.0 * totalEdges / totalNodes : 0;
			var heapMb = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576.0;
			var state = layout.Paused ? "Paused" : awake == 0 ? "Asleep" : layout.Temperature >= .35f ? "Running" : "Settling";

			Set("fps", $"{statsFps:0.0}");
			if (statValues.TryGetValue("fps", out var fpsLabel))
			{
				fpsLabel.style.color = statsFps >= 55f
					? new Color(.45f, 1f, .82f)
					: statsFps >= 30f
						? new Color(1f, .86f, .38f)
						: new Color(1f, .48f, .42f);
			}
			Set("frame", $"{statsFrameMs:0.00} avg · {statsWorstMsShown:0.0} worst");
			Set("sim", $"{statsSimMs:0.000} ms");
			Set("script", $"{statsScriptMs:0.000} ms");
			Set("heap", $"{heapMb:0.0} MB");
			Set("nodes", $"{totalNodes} total · {visibleNodes} visible");
			Set("edges", $"{totalEdges} total · {rendering.Renderer.VisibleEdges} drawn");
			Set("awake", $"{awake} awake · {asleep} asleep · {pinned} pinned");
			Set("structure", $"{clusters.Count} clusters · {collapsed} collapsed · {document.Regions.Count} regions");
			Set("density", $"avg degree {averageDegree:0.0} · density {density:P1}");
			Set("layout", $"{state} · {layout.Mode}");
			Set("temperature", $"T {layout.Temperature:0.000} · E {layout.Energy:0.0000}");

			void Set(string key, string text)
			{
				if (statValues.TryGetValue(key, out var label)) label.text = text;
			}
		}

		private readonly AllEdges edgeFilter = new();

		private void InitializeCamera()
		{
			graphCamera = Camera.main;
			if (graphCamera == null)
			{
				var cameraObject = new GameObject("Graph Explorer Sample Camera");
				ownsCamera = true;
				cameraObject.tag = "MainCamera";
				graphCamera = cameraObject.AddComponent<Camera>();
				graphCamera.transform.position = new Vector3(0, 0, -46);
				graphCamera.transform.rotation = Quaternion.identity;
			}
			graphCamera.clearFlags = CameraClearFlags.SolidColor;
			graphCamera.backgroundColor = new Color(.025f, .045f, .075f, 1);
			graphCamera.fieldOfView = 48;
			graphCamera.nearClipPlane = .1f;
			graphCamera.farClipPlane = 500;
			if (graphCamera.GetComponent<UniversalAdditionalCameraData>() == null)
				graphCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
		}

		private void InitializeRendering()
		{
			sourceScene = new GraphScene(document, CreateSchema());
			clusterScene = new GraphClusterScene(sourceScene);
			var profile = new GraphPresentationProfile { MetricField = "weight", CategoryField = "group" };
			rendering = GraphRenderingSession.Initialize(graphCamera, new GraphResourceConfiguration(), profile,
				new GraphExplorerOptions(1000, 100));
			regions = new GraphRegionController(clusterScene, rendering.Layout);
                if(settingsController!=null) {
                    regions.RenderingEnabled=Preferences.RegionsVisible;
                    foreach(var region in document.Regions.Values) regions.SetRegionPadding(region.Id,Preferences.RegionPadding);
                }
			rendering.Renderer.Regions = regions;
			rendering.Renderer.UpdateMetrics(clusterScene, edgeFilter);
			tooltipController = new GraphTooltipController();
			tooltipController.TooltipShown += ShowTooltip;
			tooltipController.TooltipHidden += HideTooltip;
			tooltipController.TooltippedNodeMoved += OnTooltippedNodeMoved;
			tooltipController.TooltipPositionChanged += MoveTooltip;
			rendering.Layout.Bind(clusterScene);
			rendering.Navigation.Overview(clusterScene);
		}

		private GraphSchema CreateSchema()
		{
			var schema = new GraphSchema();
			schema.Register(new GraphNodeTypeDefinition("item", "Item", "Gem", new Color(.3f, .82f, 1f), .7f));
			schema.Register(new GraphNodeTypeDefinition("service", "Service", "Gear", new Color(.45f, .95f, .62f), .8f));
			schema.Register(new GraphRelationDefinition("connects", "Connects", new Color(.38f, .75f, .96f), springStrength: .35f));
			schema.Register(new GraphFieldDefinition("weight", "Weight", GraphValueType.Number));
			schema.Register(new GraphFieldDefinition("group", "Group", GraphValueType.Text));
			return schema;
		}

		private void CreateGraph(int count, float density, int seed, bool withStarterRegions)
		{
			if (document != null)
			{
				foreach (var id in document.Regions.Keys.Where(id => document.GetRegionParentId(id) == null).ToArray())
					document.DeleteRegion(id, GraphRegionDeleteMode.PreserveContents);
				foreach (var id in clusters.Keys.ToArray()) clusterScene?.RemoveCluster(id);
				clusters.Clear();
				regionContentsVisible.Clear();
				regionBoundariesVisible.Clear();
				regionPadding.Clear();
				if (document.Nodes.Count > 0)
				{
					var batch = new GraphBatch();
					batch.RemoveNodes.AddRange(document.Nodes.Keys);
					document.Apply(batch);
				}
			}
			document ??= new GraphDocument("graph-explorer-builder", "Graph Explorer Builder");
			var random = new System.Random(seed);
			count = Mathf.Clamp(count, 2, 120);
			for (var i = 0; i < count; i++)
			{
				var id = NodeId(i);
				var type = i % 4 == 0 ? "service" : "item";
				var group = i % 2 == 0 ? "amber" : "cyan";
				var properties = new Dictionary<string, GraphValue>
				{
					["group"] = GraphValue.From(group),
					["weight"] = GraphValue.From(1 + random.NextDouble() * 99)
				};
				document.UpsertNode(new GraphNodeRecord(id, type, $"{(type == "service" ? "Service" : "Node")} {i + 1:00}", properties));
			}

			var possible = count * (count - 1) / 2;
			var target = Mathf.Clamp(Mathf.RoundToInt(possible * Mathf.Clamp01(density)), 0, possible);
			var pairs = new HashSet<long>();
			while (pairs.Count < target)
			{
				var from = random.Next(count);
				var to = random.Next(count - 1);
				if (to >= from) to++;
				var low = Math.Min(from, to);
				var high = Math.Max(from, to);
				var key = (long)low * count + high;
				if (!pairs.Add(key)) continue;
				document.UpsertEdge(new GraphEdgeRecord($"edge-{nextEdgeId++:0000}", NodeId(from), NodeId(to), "connects"));
			}

			if (withStarterRegions && count >= 8)
			{
				var childId = "region-core";
				var parentId = "region-network";
				document.CreateRegion(new GraphRegionRecord(childId, "Core", Enumerable.Range(0, Math.Min(4, count)).Select(NodeId)));
				document.CreateRegion(new GraphRegionRecord(parentId, "Network", Enumerable.Range(4, Math.Min(4, count - 4)).Select(NodeId), new[] { childId }));
			}
			if (sourceScene != null) sourceScene.Synchronize();
			if (clusterScene != null)
			{
				clusterScene.Synchronize();
				if (withStarterRegions && count >= 8)
				{
					var clusterId = clusterScene.Collapse(new[] { NodeId(0), NodeId(1) }, "Starter cluster");
					clusters[clusterId] = "Starter cluster";
				}
			}
			if (rendering != null)
			{
				regions?.Dispose();
				regions = new GraphRegionController(clusterScene, rendering.Layout);
                if(settingsController!=null) {
                    regions.RenderingEnabled=Preferences.RegionsVisible;
                    foreach(var region in document.Regions.Values) regions.SetRegionPadding(region.Id,Preferences.RegionPadding);
                }
				rendering.Renderer.Regions = regions;
				rendering.Layout.Bind(clusterScene);
				rendering.Renderer.UpdateMetrics(clusterScene, edgeFilter);
				rendering.Layout.Reheat();
				rendering.Navigation.Overview(clusterScene);
				selected = null;
				RefreshLabels();
			}
		}

		private static string NodeId(int index) => $"node-{index + 1:000}";

		private void InitializeUi()
		{
			var ui = gameObject.AddComponent<UIDocument>();
			var panel = GraphResources.Load<PanelSettings>("GraphPanelSettings");
			runtimePanelSettings = panel != null ? Instantiate(panel) : ScriptableObject.CreateInstance<PanelSettings>();
			ui.panelSettings = runtimePanelSettings;
			root = ui.rootVisualElement;
			root.style.position = Position.Relative;
			root.style.flexGrow = 1;
			root.style.width = Length.Percent(100);
			root.style.height = Length.Percent(100);
			root.style.flexDirection = FlexDirection.Row;
			root.style.justifyContent = Justify.FlexEnd;
			root.style.alignItems = Align.FlexStart;
			root.style.paddingLeft = 20;
			root.style.paddingRight = 20;
			root.style.paddingTop = 20;
			root.RegisterCallback<NavigationMoveEvent>(evt =>
			{
				// Stop the focus ring's default action as well as preventing the event from reaching controls.
				evt.PreventDefault();
				evt.StopImmediatePropagation();
			}, TrickleDown.TrickleDown);
			var pane = new VisualElement();
			controlPane = pane;
			pane.style.width = 390;
			pane.style.maxHeight = new Length(94, LengthUnit.Percent);
			pane.style.backgroundColor = new Color(.035f, .055f, .085f, .97f);
			pane.style.borderTopLeftRadius = 14;
			pane.style.borderTopRightRadius = 14;
			pane.style.borderBottomLeftRadius = 14;
			pane.style.borderBottomRightRadius = 14;
			pane.style.borderLeftWidth = 1;
			pane.style.borderRightWidth = 1;
			pane.style.borderTopWidth = 1;
			pane.style.borderBottomWidth = 1;
			pane.style.borderLeftColor = new Color(.15f, .24f, .34f);
			pane.style.borderRightColor = new Color(.15f, .24f, .34f);
			pane.style.borderTopColor = new Color(.15f, .24f, .34f);
			pane.style.borderBottomColor = new Color(.15f, .24f, .34f);
			pane.style.paddingLeft = 16;
			pane.style.paddingRight = 16;
			pane.style.paddingTop = 14;
			pane.style.paddingBottom = 14;
			var statsPane = new VisualElement { name = "stats-overlay" };
			statsPane.style.position = Position.Absolute;
			statsPane.style.left = 0;
			statsPane.style.top = 0;
			statsPane.style.maxWidth = 340;
			statsPane.style.width = 340;
			ApplyOverlayChrome(statsPane);
			BuildStats(statsPane);
			root.Add(statsPane);
			var controlsPane = new VisualElement { name = "controls-overlay" };
			controlsPane.style.position = Position.Absolute;
			controlsPane.style.left = 0;
			controlsPane.style.bottom = 0;
			controlsPane.style.maxWidth = 340;
			controlsPane.style.width = 340;
			ApplyOverlayChrome(controlsPane);
			BuildControlsHelp(controlsPane);
			root.Add(controlsPane);
			root.Add(pane);
			tooltipLabel = new Label();
			tooltipLabel.pickingMode = PickingMode.Ignore;
			tooltipLabel.style.position = Position.Absolute;
			tooltipLabel.style.display = DisplayStyle.None;
			tooltipLabel.style.maxWidth = 260;
			tooltipLabel.style.paddingLeft = 10;
			tooltipLabel.style.paddingRight = 10;
			tooltipLabel.style.paddingTop = 7;
			tooltipLabel.style.paddingBottom = 7;
			tooltipLabel.style.backgroundColor = new Color(.035f, .075f, .11f, .97f);
			tooltipLabel.style.color = new Color(.82f, .95f, 1f);
			tooltipLabel.style.borderTopLeftRadius = 6;
			tooltipLabel.style.borderTopRightRadius = 6;
			tooltipLabel.style.borderBottomLeftRadius = 6;
			tooltipLabel.style.borderBottomRightRadius = 6;
			root.Add(tooltipLabel);
			root.RegisterCallback<GeometryChangedEvent>(evt =>
			{
				var width = evt.newRect.width;
				controlPane.style.width = Mathf.Min(390, Mathf.Max(250, width - 36));
				var overlayWidth = Mathf.Min(340, Mathf.Max(240, width * .42f));
				statsPane.style.width = overlayWidth;
				statsPane.style.maxWidth = overlayWidth;
				controlsPane.style.width = overlayWidth;
				controlsPane.style.maxWidth = overlayWidth;
			});
			uiScroll = new ScrollView(ScrollViewMode.Vertical);
			uiScroll.AddToClassList("builder-scroll");
			uiScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
			uiScroll.style.flexGrow = 1;
			uiScroll.style.minHeight = 0;
			uiScroll.style.minWidth = 0;
			uiScroll.RegisterCallback<AttachToPanelEvent>(_ => ApplyThemedScrollbar(uiScroll));
			uiScroll.RegisterCallback<GeometryChangedEvent>(_ => ApplyThemedScrollbar(uiScroll));
			pane.Add(uiScroll);
			BuildControls(uiScroll);
		}

		private void BuildControls(VisualElement parent)
		{
            parent.Clear();
            BuildProfileControls(parent);
			parent.style.flexGrow = 1;
			parent.style.color = new Color(.88f, .93f, .98f);
			var title = new Label("Graph Explorer");
			title.style.unityFontStyleAndWeight = FontStyle.Bold;
			title.style.fontSize = 20;
			parent.Add(title);
			status = new Label($"{document.Nodes.Count} nodes · {document.Edges.Count} edges · {document.Regions.Count} regions");
			status.style.color = new Color(.47f, .82f, 1f);
			status.style.fontSize = 11;
			status.style.marginBottom = 8;
			parent.Add(status);
			var tabs = Row(parent);
			tabs.style.marginBottom = 8;
			var pageNames = new[] { "Graph", "Structure", "View", "Physics" };
			for (var i = 0; i < pageNames.Length; i++)
			{
				var tabName = pageNames[i];
				var button = new Button(() => { activeTab = tabName; RefreshUi(); }) { text = tabName };
				button.style.flexGrow = 1;
				button.style.marginLeft = i == 0 ? 0 : 3;
				button.style.marginRight = 3;
				button.style.minHeight = 30;
				button.style.backgroundColor = activeTab == tabName ? new Color(.1f, .42f, .62f) : new Color(.08f, .13f, .2f);
				button.style.color = activeTab == tabName ? Color.white : new Color(.7f, .79f, .89f);
				tabs.Add(button);
			}
			var pageBuild = CreatePage(parent, activeTab == "Graph");
			var pageStructure = CreatePage(parent, activeTab == "Structure");
			var pageView = CreatePage(parent, activeTab == "View");
			var pagePhysics = CreatePage(parent, activeTab == "Physics");

			var generation = Section(pageBuild, "Generate graph");
			generationCount = new Slider("Nodes", 2, 120) { value = Preferences.NodeCount, showInputField = true };
			generationDensity = new Slider("Edge density", 0, .35f) { value = Preferences.EdgeDensity, showInputField = true };
			generationSeed = new Slider("Seed", 0, 9999) { value = Preferences.Seed, showInputField = true };
			ApplyFieldSkin(generationCount);
			ApplyFieldSkin(generationDensity);
			ApplyFieldSkin(generationSeed);
			generationCount.RegisterValueChangedCallback(evt=>{ Preferences.NodeCount=Mathf.RoundToInt(evt.newValue); CapturePreferences(); });
            generationDensity.RegisterValueChangedCallback(evt=>{ Preferences.EdgeDensity=evt.newValue; CapturePreferences(); });
            generationSeed.RegisterValueChangedCallback(evt=>{ Preferences.Seed=Mathf.RoundToInt(evt.newValue); CapturePreferences(); });
            generation.Add(generationCount);
			generation.Add(generationDensity);
			generation.Add(generationSeed);
			AddButton(generation, "Generate", () =>
			{
				try { CreateGraph(Mathf.RoundToInt(generationCount.value), generationDensity.value, Mathf.RoundToInt(generationSeed.value), false); RefreshUi(); SetStatus("Generated a new seeded graph."); }
				catch (Exception exception) { SetStatus(exception.Message); }
			});

			var nodes = Section(pageBuild, "Nodes");
			nodeListRows.Clear();
			edgeListRows.Clear();
			nodeId = AddText(nodes, "ID", "node-new");
			nodeLabel = AddText(nodes, "Label", "New node");
			nodeType = AddText(nodes, "Type", "item");
			var nodeButtons = Row(nodes);
			AddButton(nodeButtons, "Add node", AddNode);
			AddButton(nodeButtons, "Update selected", UpdateNode);
			var nodeList = new VisualElement();
			nodes.Add(nodeList);
			foreach (var record in document.Nodes.Values.OrderBy(node => node.Id, StringComparer.Ordinal))
			{
				var line = Row(nodeList);
				StyleRecordRow(line);
				if (highlightedNodeId == record.Id) line.style.backgroundColor = new Color(.08f, .25f, .28f, 1);
				nodeListRows[record.Id] = line;
				var select = AddRecordButton(line, record.Id + "  " + record.Label, "Select node " + record.Id, () =>
				{
					highlightedNodeId = record.Id;
					highlightedEdgeId = null;
					ApplyListHighlight();
					if (!clusterScene.Nodes.ContainsKey(record.Id)) clusterScene.Reveal(record.Id);
					selected = sourceScene.Nodes[record.Id];
					clusterScene.View.SelectedId = record.Id;
					FillNodeForm(selected);
				});
			select.style.flexGrow = 1;
			select.style.unityTextAlign = TextAnchor.MiddleLeft;
			AddIconButton(line, "◎", "Focus camera on node " + record.Id, () =>
			{
				if (!clusterScene.Nodes.ContainsKey(record.Id)) clusterScene.Reveal(record.Id);
				selected = sourceScene.Nodes[record.Id];
				clusterScene.View.SelectedId = record.Id;
				FillNodeForm(selected);
				rendering.Navigation.Focus(selected);
			});
				AddIconButton(line, "×", "Remove node " + record.Id, () => RemoveNode(record.Id), true);
			}

			var edges = Section(pageBuild, "Edges");
			edgeId = AddText(edges, "Edge ID", "edge-new");
			edgeFrom = AddText(edges, "From node ID", "node-001");
			edgeTo = AddText(edges, "To node ID", "node-002");
			var edgeButtons = Row(edges);
			AddButton(edgeButtons, "Add / update edge", SaveEdge);
			var edgeList = new VisualElement();
			edges.Add(edgeList);
			foreach (var record in document.Edges.Values.OrderBy(edge => edge.Id, StringComparer.Ordinal).Take(60))
			{
				var line = Row(edgeList);
				StyleRecordRow(line);
				if (highlightedEdgeId == record.Id) line.style.backgroundColor = new Color(.08f, .25f, .28f, 1);
				edgeListRows[record.Id] = line;
				var select = AddRecordButton(line, $"{record.Id}: {record.From} → {record.To}", "Select edge " + record.Id, () =>
				{
					highlightedEdgeId = record.Id;
					highlightedNodeId = null;
					ApplyListHighlight();
					edgeId.value = record.Id; edgeFrom.value = record.From; edgeTo.value = record.To;
				});
				select.style.flexGrow = 1;
				select.style.unityTextAlign = TextAnchor.MiddleLeft;
				AddIconButton(line, "◎", "Focus camera on edge " + record.Id, () => FocusEdge(record));
				AddIconButton(line, "×", "Remove edge " + record.Id, () => { document.RemoveEdge(record.Id); RefreshAfterTopology(); RefreshUi(); }, true);
			}

			var cluster = Section(pageStructure, "Clusters");
			clusterName = AddText(cluster, "Cluster name", "Cluster");
			clusterMembers = AddText(cluster, "Visible member IDs (comma separated)", "node-001,node-002");
			AddButton(cluster, "Create cluster", CreateCluster);
			AddToggle(cluster, "Show cluster representatives", rendering.Renderer.ClustersVisible,
				value => rendering.Renderer.ClustersVisible = value);
			foreach (var item in clusters.ToArray())
			{
				var line = Row(cluster);
				var collapsed = clusterScene.IsCollapsed(item.Key);
				line.Add(new Label(item.Value + (collapsed ? " (collapsed)" : " (expanded)")));
				AddButton(line, collapsed ? "Expand" : "Collapse", () =>
				{
					if (collapsed) clusterScene.Expand(item.Key); else clusterScene.Recollapse(item.Key);
					RefreshAfterTopology(); RefreshUi();
				});
				AddButton(line, "Remove", () => { clusterScene.RemoveCluster(item.Key); clusters.Remove(item.Key); RefreshAfterTopology(); RefreshUi(); });
			}

			var region = Section(pageStructure, "Regions");
			regionName = AddText(region, "Region name", "New region");
			regionMembers = AddText(region, "Node IDs (comma separated)", "node-003,node-004");
			var regionNames = new List<string> { "(root)" };
			regionNames.AddRange(document.Regions.Values.Select(value => value.Name + "  [" + value.Id + "]"));
			regionParent = CreateDropdown("Parent region", regionNames, 0);
			region.Add(regionParent);
			AddButton(region, "Create region", CreateRegion);
			regionSelection = CreateDropdown("Selected region", regionNames, 0);
			region.Add(regionSelection);
			AddButton(region, "Rename selected region", () =>
			{
				try { document.RenameRegion(SelectedRegionId(), Require(regionName.value, "Region name")); RefreshUi(); SetStatus("Region renamed."); }
				catch (Exception exception) { SetStatus(exception.Message); }
			});
			AddButton(region, "Move listed nodes into selected region", () =>
			{
				try { regions.Scene.Document.MoveNodesToRegion(SelectedRegionId(), ParseIds(regionMembers.value)); RefreshAfterTopology(); RefreshUi(); SetStatus("Region membership updated."); }
				catch (Exception exception) { SetStatus(exception.Message); }
			});
			foreach (var record in document.Regions.Values.OrderBy(value => value.Name, StringComparer.Ordinal))
			{
				var line = Row(region);
				line.Add(new Label(record.Name + " · " + document.GetRegionNodeIds(record.Id).Count + " nodes"));
				var contentsVisible = !regionContentsVisible.TryGetValue(record.Id, out var visible) || visible;
				AddButton(line, contentsVisible ? "Hide contents" : "Show contents", () =>
				{
					contentsVisible = !contentsVisible;
					regionContentsVisible[record.Id] = contentsVisible;
					regions.SetRegionContentsVisible(record.Id, contentsVisible);
					RefreshUi();
				});
				AddButton(line, regions.IsRegionCollapsed(record.Id) ? "Expand" : "Collapse", () =>
				{
					if (regions.IsRegionCollapsed(record.Id)) regions.ExpandRegion(record.Id); else regions.CollapseRegion(record.Id);
					RefreshAfterTopology(); RefreshUi();
				});
				AddButton(line, "Delete", () =>
				{
					document.DeleteRegion(record.Id, GraphRegionDeleteMode.PreserveContents);
					regionContentsVisible.Remove(record.Id);
					regionBoundariesVisible.Remove(record.Id);
					regionPadding.Remove(record.Id);
					RefreshAfterTopology(); RefreshUi();
				});
				var boundaryVisible = !regionBoundariesVisible.TryGetValue(record.Id, out var boundary) || boundary;
				AddToggle(region, record.Name + " boundary", boundaryVisible, value =>
				{
					regionBoundariesVisible[record.Id] = value;
					regions.SetRegionBoundaryVisible(record.Id, value);
				});
				var padding = new Slider(record.Name + " padding", 0, 12)
					{ value = regionPadding.TryGetValue(record.Id, out var amount) ? amount : Preferences.RegionPadding, showInputField = true };
				ApplyFieldSkin(padding);
				padding.RegisterValueChangedCallback(evt =>
				{
					regionPadding[record.Id] = evt.newValue;
					regions.SetRegionPadding(record.Id, evt.newValue);
				});
				region.Add(padding);
				AddButton(region, record.Name + " · use automatic bounds", () => regions.UseAutomaticRegionBounds(record.Id));
				AddButton(region, record.Name + " · set manual bounds", () =>
				{
					if (!regions.TryGetRegionBounds(record.Id, out var bounds)) bounds = new GraphRegionBounds(new Bounds(Vector3.zero, Vector3.one * 16));
					var box = bounds.Cuboid;
					box.Expand(4);
					regions.SetRegionManualBounds(record.Id, new GraphRegionBounds(box));
				});
				AddButton(region, record.Name + " · settle", () => regions.SettleRegion(record.Id));
				AddButton(region, record.Name + " · wake", () => regions.WakeRegion(record.Id));
			}

			var render = Section(pageView, "Display");
			var nodeRendering = Section(render, "Nodes");
			AddToggle(nodeRendering, "Show nodes", rendering.Renderer.NodesVisible, value => rendering.Renderer.NodesVisible = value);
			AddDropdown(nodeRendering, "Color by", Enum.GetNames(typeof(ColorMapping)), (int)rendering.Renderer.ColorBy,
				value => rendering.Renderer.ColorBy = (ColorMapping)value);
			AddDropdown(nodeRendering, "Size by", Enum.GetNames(typeof(SizeMapping)), (int)rendering.Renderer.Size,
				value => rendering.Renderer.Size = (SizeMapping)value);
			var edgeRendering = Section(render, "Edges");
			AddToggle(edgeRendering, "Show edges", edgeFilter.Visible, value => edgeFilter.Visible = value);
			var edgeWidth = new Slider("Edge width", .25f, 4) { value = Preferences.EdgeWidth, showInputField = true };
			ApplyFieldSkin(edgeWidth);
			edgeWidth.RegisterValueChangedCallback(evt => { rendering.Renderer.EdgeWidthScale = evt.newValue; CapturePreferences(); });
			edgeRendering.Add(edgeWidth);
			var clusterRendering = Section(render, "Clusters");
			AddToggle(clusterRendering, "Show representatives", rendering.Renderer.ClustersVisible,
				value => rendering.Renderer.ClustersVisible = value);
			var regionRendering = Section(render, "Regions");
            AddSlider(regionRendering,"Default region padding",0,12,Preferences.RegionPadding,value=> {
                Preferences.RegionPadding=value;
                foreach(var region in document.Regions.Values) if(!regionPadding.ContainsKey(region.Id)) regions.SetRegionPadding(region.Id,value);
            });
			AddToggle(regionRendering, "Render region boundaries", regions.RenderingEnabled,
				value => regions.RenderingEnabled = value);
			AddToggle(regionRendering, "Show region names", showRegionNames,
				value => { showRegionNames = value; UpdateRegionLabels(); });
			var tooltipRendering = Section(render, "Tooltips");
			AddToggle(tooltipRendering, "Enable tooltips", tooltipController.Settings.Enabled,
				value => tooltipController.Settings.Enabled = value);
			AddSlider(tooltipRendering, "Show delay", 0, 2, tooltipController.Settings.ShowDelaySeconds,
				value => tooltipController.Settings.ShowDelaySeconds = value);
			AddSlider(tooltipRendering, "Hide delay", 0, 2, tooltipController.Settings.HideDelaySeconds,
				value => tooltipController.Settings.HideDelaySeconds = value);
			AddToggle(tooltipRendering, "Notify when node moves", tooltipController.Settings.NotifyWhenNodeMoves,
				value => tooltipController.Settings.NotifyWhenNodeMoves = value);
			AddDropdown(tooltipRendering, "Follow mode", Enum.GetNames(typeof(GraphTooltipFollowMode)),
				(int)tooltipController.Settings.FollowMode,
				value => tooltipController.Settings.FollowMode = (GraphTooltipFollowMode)value);
			AddSlider(tooltipRendering, "Move threshold", 0, 1, tooltipController.Settings.MoveThreshold,
				value => tooltipController.Settings.MoveThreshold = value);
			AddSlider(tooltipRendering, "Cursor move threshold", 0, 30, tooltipController.Settings.CursorMoveThreshold,
				value => tooltipController.Settings.CursorMoveThreshold = value);
			var labelRendering = Section(render, "Labels");
			labelsToggle = new Toggle("Show labels") { value = showLabels };
			labelsToggle.RegisterValueChangedCallback(evt => { showLabels = evt.newValue; UpdateLabels(); CapturePreferences(); });
			labelRendering.Add(labelsToggle);
			labelSize = new Slider("Label size", .5f, 2.5f) { value = labelScale, showInputField = true };
			ApplyFieldSkin(labelSize);
			labelSize.RegisterValueChangedCallback(evt =>
			{
				labelScale = evt.newValue;
				UpdateLabels();
				UpdateRegionLabels();
			});
			labelRendering.Add(labelSize);
			var background = Section(render, "Background");
			var initialBackground = graphCamera.backgroundColor;
			AddSlider(background, "Red", 0, 1, initialBackground.r, value => SetBackgroundColor(0, value));
			AddSlider(background, "Green", 0, 1, initialBackground.g, value => SetBackgroundColor(1, value));
			AddSlider(background, "Blue", 0, 1, initialBackground.b, value => SetBackgroundColor(2, value));

			var physics = Section(pagePhysics, "Layout physics");
			AddToggle(physics, "Enable node dragging", EnableNodeDragging, value => EnableNodeDragging = value);
			AddToggle(physics, "Pause layout", rendering.Layout.Paused, value => rendering.Layout.Paused = value);
			AddDropdown(physics, "Layout mode", Enum.GetNames(typeof(LayoutMode)), (int)rendering.Layout.Mode,
				value => { rendering.Layout.Mode = (LayoutMode)value; rendering.Layout.Reheat(); });
			AddSlider(physics, "Repulsion", 0, 180, rendering.Layout.RepulsionStrength, value => rendering.Layout.RepulsionStrength = value);
			AddSlider(physics, "Centering", 0, .1f, rendering.Layout.CenteringStrength, value => rendering.Layout.CenteringStrength = value);
			AddSlider(physics, "Spring length", 1, 24, rendering.Layout.SpringRestLength, value => rendering.Layout.SpringRestLength = value);
			AddSlider(physics, "Spring strength", 0, 3, rendering.Layout.SpringStrengthScale, value => rendering.Layout.SpringStrengthScale = value);
			AddSlider(physics, "Damping", 0, 8, rendering.Layout.Damping, value => rendering.Layout.Damping = value);
			AddSlider(physics, "Maximum force", 10, 400, rendering.Layout.MaximumForce, value => rendering.Layout.MaximumForce = value);
			AddSlider(physics, "Maximum speed", 1, 40, rendering.Layout.MaximumSpeed, value => rendering.Layout.MaximumSpeed = value);
			var physicsButtons = Row(physics);
			AddButton(physicsButtons, "Reheat", rendering.Layout.Reheat);
			AddButton(physicsButtons, "Reset layout", () => { rendering.Layout.ResetPositions(); rendering.Navigation.Overview(clusterScene); });
			AddButton(physics, "Reset physics settings", () =>
            {
                Preferences.Physics = new GraphPhysicsSettings();
                ApplyPreferences();
                settingsController.NotifyChanged();
                RefreshUi();
            });
		}

		private static VisualElement CreatePage(VisualElement parent, bool visible)
		{
			var page = new VisualElement();
			page.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
			page.style.flexGrow = 1;
			page.style.minWidth = 0;
			parent.Add(page);
			return page;
		}

		private void RefreshUi()
		{
			if (root == null || root.childCount == 0) return;
			BuildControls(uiScroll);
			RefreshLabels();
		}

		private void RefreshAfterTopology()
        {
            foreach(var region in document.Regions.Values)
                regions.SetRegionPadding(region.Id,regionPadding.TryGetValue(region.Id,out var padding)?padding:Preferences.RegionPadding);
			sourceScene.Synchronize();
			clusterScene.Synchronize();
			foreach (var id in clusters.Keys.Where(id => !clusterScene.IsCluster(id)).ToArray()) clusters.Remove(id);
			rendering.Layout.Bind(clusterScene);
			rendering.Renderer.UpdateMetrics(clusterScene, edgeFilter);
			rendering.Layout.Reheat();
			if (selected != null && !clusterScene.Nodes.ContainsKey(selected.Id)) selected = null;
		}

		private void AddNode()
		{
			try
			{
				var id = Require(nodeId.value, "Node ID");
				if (document.Nodes.ContainsKey(id)) throw new InvalidOperationException("That node ID already exists; select it and use Update selected.");
				document.UpsertNode(new GraphNodeRecord(id, Require(nodeType.value, "Node type"), nodeLabel.value));
				RefreshAfterTopology(); RefreshUi(); SetStatus("Node added.");
			}
			catch (Exception exception) { SetStatus(exception.Message); }
		}

		private void UpdateNode()
		{
			try
			{
				var id = Require(nodeId.value, "Node ID");
				if (!document.Nodes.TryGetValue(id, out var existing)) throw new InvalidOperationException("Select an existing node to update it.");
				document.UpsertNode(existing.WithIdentity(Require(nodeType.value, "Node type"), nodeLabel.value));
				RefreshAfterTopology(); RefreshUi(); SetStatus("Node updated.");
			}
			catch (Exception exception) { SetStatus(exception.Message); }
		}

		private void RemoveNode(string id)
		{
			document.RemoveNode(id);
			RefreshAfterTopology(); RefreshUi();
		}

		private void SaveEdge()
		{
			try
			{
				var id = Require(edgeId.value, "Edge ID");
				var from = Require(edgeFrom.value, "Source node");
				var to = Require(edgeTo.value, "Destination node");
				if (!document.Nodes.ContainsKey(from) || !document.Nodes.ContainsKey(to))
					throw new InvalidOperationException("Both edge endpoints must refer to existing nodes.");
				document.UpsertEdge(new GraphEdgeRecord(id, from, to, "connects"));
				RefreshAfterTopology(); RefreshUi(); SetStatus("Edge added or updated.");
			}
			catch (Exception exception) { SetStatus(exception.Message); }
		}

		private void CreateCluster()
		{
			try
			{
				var id = clusterScene.Collapse(ParseIds(clusterMembers.value), clusterName.value);
				clusters[id] = string.IsNullOrWhiteSpace(clusterName.value) ? "Cluster" : clusterName.value;
				RefreshAfterTopology(); RefreshUi(); SetStatus("Cluster created.");
			}
			catch (Exception exception) { SetStatus(exception.Message); }
		}

		private void CreateRegion()
		{
			try
			{
				var id = "region-" + Guid.NewGuid().ToString("N").Substring(0, 8);
				var parent = ParentRegionId();
				document.CreateRegion(new GraphRegionRecord(id, Require(regionName.value, "Region name")));
				var members = ParseIds(regionMembers.value);
				if (members.Length > 0) document.MoveNodesToRegion(id, members);
				if (!string.IsNullOrEmpty(parent)) document.AddSubregions(parent, new[] { id });
				regionContentsVisible[id] = true;
				RefreshAfterTopology(); RefreshUi(); SetStatus("Region created.");
			}
			catch (Exception exception) { SetStatus(exception.Message); }
		}

		private string ParentRegionId()
		{
			var value = regionParent.value;
			if (string.IsNullOrEmpty(value) || value == "(root)") return null;
			var start = value.LastIndexOf("[", StringComparison.Ordinal);
			var end = value.LastIndexOf("]", StringComparison.Ordinal);
			return start >= 0 && end > start ? value.Substring(start + 1, end - start - 1) : null;
		}

		private string SelectedRegionId()
		{
			var value = regionSelection.value;
			var start = value.LastIndexOf("[", StringComparison.Ordinal);
			var end = value.LastIndexOf("]", StringComparison.Ordinal);
			if (start < 0 || end <= start) throw new InvalidOperationException("Choose a region first.");
			return value.Substring(start + 1, end - start - 1);
		}

		private void FillNodeForm(IGraphNode node)
		{
			nodeId.value = node.Id;
			nodeLabel.value = node.Label;
			nodeType.value = node.Record.TypeId;
		}

		private void ShowTooltip(GraphTooltipData data)
		{
			if (tooltipLabel == null) return;
			tooltipLabel.text = $"{data.Label}  ·  {data.NodeId}";
			tooltipLabel.style.display = DisplayStyle.Flex;
			PositionTooltip(data.PositionFor(tooltipController.Settings.FollowMode));
		}

		private void MoveTooltip(GraphTooltipData data)
		{
			if (tooltipLabel == null) return;
			PositionTooltip(data.PositionFor(tooltipController.Settings.FollowMode));
		}

		private void OnTooltippedNodeMoved(GraphTooltipData data)
		{
			SetStatus($"Tooltip node moved: {data.NodeId}");
		}

		private void HideTooltip(GraphTooltipData data)
		{
			if (tooltipLabel != null) tooltipLabel.style.display = DisplayStyle.None;
		}

		private void PositionTooltip(Vector2 screenPosition)
		{
			if (root?.panel == null || tooltipLabel == null) return;
			var panelPoint = RuntimePanelUtils.ScreenToPanel(root.panel,
				new Vector2(screenPosition.x, Screen.height - screenPosition.y));
			tooltipLabel.style.left = Mathf.Clamp(panelPoint.x + 14, 0, Mathf.Max(0, root.resolvedStyle.width - 270));
			tooltipLabel.style.top = Mathf.Clamp(panelPoint.y - 24, 0, Mathf.Max(0, root.resolvedStyle.height - 50));
		}

		private void RefreshLabels()
		{
			if (clusterScene == null) return;
			var keep = new HashSet<string>(clusterScene.Nodes.Keys, StringComparer.Ordinal);
			foreach (var id in labels.Keys.Where(id => !keep.Contains(id)).ToArray())
			{
				DestroyOwned(labels[id].gameObject);
				labels.Remove(id);
			}
			var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
			foreach (var node in clusterScene.Nodes.Values)
			{
				if (!labels.TryGetValue(node.Id, out var label))
				{
					var obj = new GameObject("Graph label " + node.Id);
					label = obj.AddComponent<TextMesh>();
					label.anchor = TextAnchor.LowerCenter;
					label.alignment = TextAlignment.Center;
					label.color = new Color(.82f, .91f, 1f);
					label.fontSize = 44;
					label.characterSize = .075f;
					if (font != null) { label.font = font; obj.GetComponent<MeshRenderer>().sharedMaterial = font.material; }
					labels.Add(node.Id, label);
				}
				label.text = node.Label;
			}
			UpdateLabels();
		}

		private void UpdateLabels()
		{
			if (clusterScene == null) return;
			foreach (var node in clusterScene.Nodes.Values)
				if (labels.TryGetValue(node.Id, out var label))
				{
					label.transform.position = node.Position + Vector3.up * (rendering.Renderer.Radius(node) * 2 + .25f);
					label.transform.rotation = graphCamera.transform.rotation;
					label.transform.localScale = Vector3.one * labelScale;
                    label.fontSize=Mathf.RoundToInt(Preferences.Labels.NameFontSize * 4);
                    label.transform.position += graphCamera.transform.up * ((Preferences.Labels.AboveNodeGap - 6f) / 24f);
					var hiddenCluster = !rendering.Renderer.ClustersVisible && node.Id.StartsWith("graph-explorer:cluster:", StringComparison.Ordinal);
					label.gameObject.SetActive(showLabels && rendering.Renderer.NodesVisible && !hiddenCluster && node.Visible);
				}
		}

		private void SetStatus(string message)
		{
			if (status != null) status.text = $"{document.Nodes.Count} nodes · {document.Edges.Count} edges · {document.Regions.Count} regions  |  {message}";
			else Debug.Log(message, this);
		}

		private void SetBackgroundColor(int channel, float value)
		{
			var color = graphCamera.backgroundColor;
			if (channel == 0) color.r = value;
			else if (channel == 1) color.g = value;
			else color.b = value;
			graphCamera.backgroundColor = color;
		}

		private static string Require(string value, string label) =>
			string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException(label + " is required.") : value.Trim();

		private static string[] ParseIds(string value) => (value ?? "").Split(',')
			.Select(item => item.Trim()).Where(item => item.Length > 0).Distinct(StringComparer.Ordinal).ToArray();

		private static VisualElement Section(VisualElement parent, string title)
		{
			var foldout = new Foldout { text = title, value = true };
			foldout.style.marginTop = 5;
			foldout.style.marginBottom = 5;
			foldout.style.paddingLeft = 10;
			foldout.style.paddingRight = 10;
			foldout.style.paddingTop = 5;
			foldout.style.paddingBottom = 6;
			foldout.style.backgroundColor = new Color(.055f, .083f, .12f, .92f);
			foldout.style.borderTopLeftRadius = 8;
			foldout.style.borderTopRightRadius = 8;
			foldout.style.borderBottomLeftRadius = 8;
			foldout.style.borderBottomRightRadius = 8;
			parent.Add(foldout);
			return foldout;
		}

		private static void ApplyOverlayChrome(VisualElement pane)
		{
			pane.style.paddingLeft = 12;
			pane.style.paddingRight = 12;
			pane.style.paddingTop = 10;
			pane.style.paddingBottom = 10;
			pane.style.backgroundColor = new Color(5 / 255f, 40 / 255f, 50 / 255f, .97f);
			pane.style.borderTopLeftRadius = 7;
			pane.style.borderTopRightRadius = 7;
			pane.style.borderBottomLeftRadius = 7;
			pane.style.borderBottomRightRadius = 7;
			pane.style.borderLeftWidth = 1;
			pane.style.borderRightWidth = 1;
			pane.style.borderTopWidth = 1;
			pane.style.borderBottomWidth = 1;
			var border = new Color(0, 207 / 255f, 196 / 255f, 1);
			pane.style.borderLeftColor = border;
			pane.style.borderRightColor = border;
			pane.style.borderTopColor = border;
			pane.style.borderBottomColor = border;
		}

		private static void BuildControlsHelp(VisualElement parent)
		{
			var title = new Label("Controls");
			title.style.unityFontStyleAndWeight = FontStyle.Bold;
			title.style.fontSize = 13;
			title.style.color = new Color(.91f, .96f, .98f);
			title.style.marginBottom = 6;
			parent.Add(title);
			AddControlLine(parent, "Left drag", "Orbit camera");
			AddControlLine(parent, "Right / middle drag", "Pan view");
			AddControlLine(parent, "Scroll", "Zoom in and out");
			AddControlLine(parent, "Left click", "Select node");
			AddControlLine(parent, "Hover", "Show node tooltip");
			AddControlLine(parent, "Drag node", "Reposition (Physics → Enable node dragging)");
		}

		private static void AddControlLine(VisualElement parent, string action, string detail)
		{
			var row = new VisualElement();
			row.style.flexDirection = FlexDirection.Row;
			row.style.alignItems = Align.FlexStart;
			row.style.minWidth = 0;
			row.style.marginTop = 2;
			row.style.marginBottom = 2;

			var name = new Label(action);
			name.style.fontSize = 11;
			name.style.color = new Color(0, 207 / 255f, 196 / 255f, 1);
			name.style.flexShrink = 0;
			name.style.width = 118;
			name.style.marginRight = 8;

			var value = new Label(detail);
			value.style.fontSize = 11;
			value.style.color = new Color(.88f, .93f, .98f);
			value.style.whiteSpace = WhiteSpace.Normal;
			value.style.flexGrow = 1;
			value.style.flexShrink = 1;
			value.style.minWidth = 0;

			row.Add(name);
			row.Add(value);
			parent.Add(row);
		}

		private static void ApplyThemedScrollbar(VisualElement scrollRoot)
		{
			var track = new Color(21 / 255f, 32 / 255f, 40 / 255f, 1);
			var thumb = new Color(0, 207 / 255f, 196 / 255f, .72f);
			scrollRoot.Query(className: "unity-scroller--vertical").ForEach(scroller =>
			{
				scroller.style.width = 8;
				scroller.style.marginLeft = 4;
				scroller.style.marginTop = 2;
				scroller.style.marginBottom = 2;
			});
			scrollRoot.Query(className: "unity-scroller__low-button").ForEach(button => button.style.display = DisplayStyle.None);
			scrollRoot.Query(className: "unity-scroller__high-button").ForEach(button => button.style.display = DisplayStyle.None);
			scrollRoot.Query(className: "unity-base-slider__tracker").ForEach(tracker =>
			{
				tracker.style.backgroundColor = track;
				tracker.style.borderTopWidth = 0;
				tracker.style.borderBottomWidth = 0;
				tracker.style.borderLeftWidth = 0;
				tracker.style.borderRightWidth = 0;
				tracker.style.borderTopLeftRadius = 4;
				tracker.style.borderTopRightRadius = 4;
				tracker.style.borderBottomLeftRadius = 4;
				tracker.style.borderBottomRightRadius = 4;
			});
			scrollRoot.Query(className: "unity-base-slider__dragger").ForEach(dragger =>
			{
				dragger.style.backgroundColor = thumb;
				dragger.style.borderTopWidth = 0;
				dragger.style.borderBottomWidth = 0;
				dragger.style.borderLeftWidth = 0;
				dragger.style.borderRightWidth = 0;
				dragger.style.borderTopLeftRadius = 4;
				dragger.style.borderTopRightRadius = 4;
				dragger.style.borderBottomLeftRadius = 4;
				dragger.style.borderBottomRightRadius = 4;
				dragger.style.minWidth = 6;
			});
			scrollRoot.Query(className: "unity-base-slider__dragger-border").ForEach(border => border.style.borderTopWidth = 0);
		}

		private void BuildStats(VisualElement parent)
		{
			var panel = (Foldout)Section(parent, "Stats");
			panel.style.marginTop = 0;
			panel.style.marginBottom = 0;

			AddStat(panel, "fps", "FPS", headline: true);
			AddStat(panel, "frame", "Frame time");
			AddStat(panel, "sim", "Layout step");
			AddStat(panel, "script", "Script update");
			AddStat(panel, "heap", "Managed heap");
			AddStat(panel, "nodes", "Nodes");
			AddStat(panel, "edges", "Edges");
			AddStat(panel, "awake", "Layout nodes");
			AddStat(panel, "structure", "Structure");
			AddStat(panel, "density", "Connectivity");
			AddStat(panel, "layout", "Layout state");
			AddStat(panel, "temperature", "Heat / energy");
		}

		private void AddStat(VisualElement parent, string key, string caption, bool headline = false)
		{
			var row = new VisualElement();
			row.style.flexDirection = FlexDirection.Row;
			row.style.justifyContent = Justify.SpaceBetween;
			row.style.alignItems = headline ? Align.Center : Align.FlexStart;
			row.style.minWidth = 0;
			row.style.marginTop = headline ? 4 : 2;
			row.style.marginBottom = headline ? 10 : 2;
			if (headline)
			{
				row.style.paddingLeft = 8;
				row.style.paddingRight = 8;
				row.style.paddingTop = 8;
				row.style.paddingBottom = 8;
				row.style.backgroundColor = new Color(.04f, .12f, .16f, 1);
				row.style.borderLeftWidth = 3;
				row.style.borderLeftColor = new Color(0, 207 / 255f, 196 / 255f, 1);
				row.style.borderTopLeftRadius = 5;
				row.style.borderTopRightRadius = 5;
				row.style.borderBottomLeftRadius = 5;
				row.style.borderBottomRightRadius = 5;
			}

			var name = new Label(caption);
			name.style.fontSize = headline ? 13 : 11;
			name.style.color = headline ? new Color(.72f, .86f, .95f) : new Color(.58f, .68f, .8f);
			name.style.flexShrink = 0;
			name.style.marginRight = 10;
			if (headline) name.style.unityFontStyleAndWeight = FontStyle.Bold;

			var value = new Label("–");
			value.style.fontSize = headline ? 24 : 11;
			value.style.color = headline ? new Color(0, 207 / 255f, 196 / 255f) : new Color(.88f, .93f, .98f);
			value.style.whiteSpace = WhiteSpace.Normal;
			value.style.unityTextAlign = TextAnchor.UpperRight;
			value.style.flexGrow = 1;
			value.style.flexShrink = 1;
			value.style.minWidth = 0;
			if (headline) value.style.unityFontStyleAndWeight = FontStyle.Bold;

			row.Add(name);
			row.Add(value);
			parent.Add(row);
			statValues[key] = value;
		}

		private static VisualElement Row(VisualElement parent)
		{
			var row = new VisualElement();
			row.style.flexDirection = FlexDirection.Row;
			row.style.alignItems = Align.Center;
			row.style.minWidth = 0;
			row.style.marginBottom = 4;
			parent.Add(row);
			return row;
		}

		private static void StyleRecordRow(VisualElement row)
		{
			row.style.paddingLeft = 5;
			row.style.paddingRight = 5;
			row.style.paddingTop = 3;
			row.style.paddingBottom = 3;
			row.style.backgroundColor = new Color(.055f, .09f, .13f, 1);
			row.style.borderLeftWidth = 2;
			row.style.borderLeftColor = new Color(0, .62f, .61f, .75f);
			row.style.borderTopLeftRadius = 4;
			row.style.borderBottomLeftRadius = 4;
		}

		private static Button AddRecordButton(VisualElement parent, string text, string tooltip, Action callback)
		{
			var button = AddIconButton(parent, text, tooltip, callback);
			button.style.flexGrow = 1;
			button.style.minWidth = 0;
			button.style.justifyContent = Justify.FlexStart;
			button.style.paddingLeft = 8;
			button.style.paddingRight = 8;
			button.style.backgroundColor = Color.clear;
			button.style.borderLeftWidth = 0;
			button.style.borderRightWidth = 0;
			button.style.borderTopWidth = 0;
			button.style.borderBottomWidth = 0;
			button.style.whiteSpace = WhiteSpace.NoWrap;
			button.style.overflow = Overflow.Hidden;
			return button;
		}

		private static Button AddIconButton(VisualElement parent, string icon, string tooltip, Action callback, bool destructive = false)
		{
			var button = new Button(callback) { text = icon, tooltip = tooltip };
			button.style.width = 30;
			button.style.minWidth = 30;
			button.style.height = 28;
			button.style.marginLeft = 3;
			button.style.marginRight = 0;
			button.style.marginTop = 0;
			button.style.marginBottom = 0;
			button.style.paddingLeft = 0;
			button.style.paddingRight = 0;
			button.style.paddingTop = 0;
			button.style.paddingBottom = 0;
			button.style.fontSize = 16;
			button.style.unityFontStyleAndWeight = FontStyle.Bold;
			button.style.color = destructive ? new Color(1f, .53f, .52f) : new Color(.45f, .9f, .88f);
			button.style.backgroundColor = new Color(.08f, .15f, .2f, 1);
			button.style.borderTopLeftRadius = 4;
			button.style.borderTopRightRadius = 4;
			button.style.borderBottomLeftRadius = 4;
			button.style.borderBottomRightRadius = 4;
			parent.Add(button);
			return button;
		}

		private void FocusEdge(GraphEdgeRecord edge)
		{
			if (!sourceScene.Nodes.TryGetValue(edge.From, out var from) ||
				!sourceScene.Nodes.TryGetValue(edge.To, out var to)) return;
			var center = (from.Position + to.Position) * .5f;
			var radius = Mathf.Max(12f, Vector3.Distance(from.Position, to.Position) * .65f);
			rendering.Navigation.Frame(center, radius);
		}

		private void HandleGraphListDoubleClick(GraphSceneInput.HoverSample sample, Mouse mouse)
		{
			if (activeTab != "Graph" || sample.Blocked || mouse == null || !mouse.leftButton.wasPressedThisFrame)
			{
				if (activeTab != "Graph" || sample.Blocked) lastUiClickKey = null;
				return;
			}

			var key = sample.Node != null ? "node:" + sample.Node.Id :
				sample.Edge != null ? "edge:" + sample.Edge.Id : null;
			if (key == null)
			{
				lastUiClickKey = null;
				return;
			}

			var doubleClick = key == lastUiClickKey && Time.unscaledTime - lastUiClickTime <= .35f;
			lastUiClickKey = doubleClick ? null : key;
			lastUiClickTime = Time.unscaledTime;
			if (!doubleClick) return;

			if (sample.Node != null)
			{
				highlightedNodeId = sample.Node.Id;
				highlightedEdgeId = null;
				selected = sample.Node;
				clusterScene.View.SelectedId = sample.Node.Id;
				FillNodeForm(sample.Node);
			}
			else
			{
				var edge = sample.Edge.Record;
				highlightedEdgeId = edge.Id;
				highlightedNodeId = null;
				edgeId.value = edge.Id;
				edgeFrom.value = edge.From;
				edgeTo.value = edge.To;
			}
			ApplyListHighlight();
		}

		private void ApplyListHighlight()
		{
			var normal = new Color(.055f, .09f, .13f, 1);
			var highlighted = new Color(.08f, .25f, .28f, 1);
			foreach (var row in nodeListRows) row.Value.style.backgroundColor = row.Key == highlightedNodeId ? highlighted : normal;
			foreach (var row in edgeListRows) row.Value.style.backgroundColor = row.Key == highlightedEdgeId ? highlighted : normal;
		}

		private void UpdateRegionLabels()
		{
			if (regions == null || document == null || graphCamera == null) return;
			var keep = new HashSet<string>(document.Regions.Keys, StringComparer.Ordinal);
			foreach (var id in regionLabels.Keys.Where(id => !keep.Contains(id)).ToArray())
			{
				DestroyOwned(regionLabels[id].gameObject);
				regionLabels.Remove(id);
			}

			var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
			foreach (var region in document.Regions.Values)
			{
				if (!regionLabels.TryGetValue(region.Id, out var label))
				{
					var obj = new GameObject("Region label " + region.Id);
					label = obj.AddComponent<TextMesh>();
					label.anchor = TextAnchor.LowerCenter;
					label.alignment = TextAlignment.Center;
					label.color = new Color(.34f, .95f, .88f, 1f);
					label.fontSize = 120;
					label.characterSize = .22f;
					label.fontStyle = FontStyle.Bold;
					if (font != null) { label.font = font; obj.GetComponent<MeshRenderer>().sharedMaterial = font.material; }
					regionLabels.Add(region.Id, label);
				}
				if (label.text != region.Name) label.text = region.Name;
			}

			foreach (var pair in regionLabels)
			{
				var label = pair.Value;
				var hasBounds = regions.TryGetRegionBounds(pair.Key, out var bounds);
				label.transform.position = hasBounds
					? new Vector3(bounds.Cuboid.center.x, bounds.Cuboid.max.y + 2.4f, bounds.Cuboid.center.z)
					: Vector3.zero;
				label.transform.rotation = graphCamera.transform.rotation;
				label.transform.localScale = Vector3.one * labelScale;
                    label.fontSize=Mathf.RoundToInt(Preferences.Labels.NameFontSize * 4);
                    label.transform.position += graphCamera.transform.up * ((Preferences.Labels.AboveNodeGap - 6f) / 24f);
				label.gameObject.SetActive(showRegionNames && hasBounds);
			}
		}

		private void UpdateHoveredRegion(Mouse mouse, bool blocked)
		{
			detectedRegionId = null;
			if (mouse == null || blocked || !showRegionNames || regionLabels.Count == 0) return;
			var ray = graphCamera.ScreenPointToRay(mouse.position.ReadValue());
			var nearest = float.PositiveInfinity;
			foreach (var pair in regionLabels)
			{
				if (!pair.Value.gameObject.activeInHierarchy) continue;
				var renderer = pair.Value.GetComponent<Renderer>();
				if (renderer != null && renderer.bounds.IntersectRay(ray, out var distance) && distance < nearest)
				{
					nearest = distance;
					detectedRegionId = pair.Key;
				}
			}
		}

		private static TextField AddText(VisualElement parent, string label, string value)
		{
			var field = new TextField(label) { value = value };
			field.style.marginBottom = 4;
			field.style.minWidth = 0;
			ApplyFieldSkin(field);
			parent.Add(field);
			return field;
		}

		private static void AddButton(VisualElement parent, string text, Action callback)
		{
			var button = new Button(callback) { text = text };
			button.style.marginTop = 3;
			button.style.marginBottom = 3;
			button.style.minHeight = 29;
			button.style.backgroundColor = new Color(.1f, .17f, .25f);
			button.style.color = new Color(.88f, .93f, .98f);
			parent.Add(button);
		}

		private void AddToggle(VisualElement parent, string label, bool value, Action<bool> changed)
		{
			var toggle = new Toggle(label) { value = value };
			toggle.style.marginTop = 3;
			toggle.style.marginBottom = 3;
			toggle.RegisterValueChangedCallback(evt => { changed(evt.newValue); CapturePreferences(); });
			parent.Add(toggle);
		}

		private SampleDropdown CreateDropdown(string label, IReadOnlyList<string> choices, int index) =>
			new SampleDropdown(label, choices, index, opened =>
			{
				if (openDropdown != null && openDropdown != opened) openDropdown.Close();
				openDropdown = opened;
			}, () => openDropdown = null);

		private void AddDropdown(VisualElement parent, string label, IReadOnlyList<string> choices, int index, Action<int> changed)
		{
			var dropdown = CreateDropdown(label, choices, Mathf.Clamp(index, 0, choices.Count - 1));
			dropdown.RegisterValueChangedCallback(value => { changed(choices.ToList().IndexOf(value)); CapturePreferences(); });
			parent.Add(dropdown);
		}

		private void AddSlider(VisualElement parent, string label, float low, float high, float value,
			Action<float> changed)
		{
			var slider = new Slider(label, low, Mathf.Max(high,value)) { value = value, showInputField = true };
			slider.style.marginTop = 2;
			slider.style.marginBottom = 4;
			slider.style.minWidth = 0;
			ApplyFieldSkin(slider);
			slider.RegisterValueChangedCallback(evt => { changed(evt.newValue); CapturePreferences(); });
			parent.Add(slider);
		}

		private static void ApplyFieldSkin(VisualElement control)
		{
			var pending = new Stack<VisualElement>();
			pending.Push(control);
			while (pending.Count > 0)
			{
				var element = pending.Pop();
				if (element.ClassListContains("unity-base-field__input") ||
					element.ClassListContains("unity-base-text-field__input") ||
					element.ClassListContains("unity-text-field__input") ||
					element.ClassListContains("unity-integer-field__input") ||
					element.ClassListContains("unity-base-popup-field__input"))
				{
					element.style.backgroundColor = new Color(5 / 255f, 40 / 255f, 50 / 255f, 1);
					element.style.color = new Color(233 / 255f, 247 / 255f, 248 / 255f, 1);
					element.style.minHeight = 34;
					element.style.paddingLeft = 10;
					element.style.paddingRight = 10;
					element.style.borderTopWidth = 1;
					element.style.borderBottomWidth = 1;
					element.style.borderLeftWidth = 1;
					element.style.borderRightWidth = 1;
					element.style.borderTopColor = new Color(0, 207 / 255f, 196 / 255f, 1);
					element.style.borderBottomColor = new Color(0, 207 / 255f, 196 / 255f, 1);
					element.style.borderLeftColor = new Color(0, 207 / 255f, 196 / 255f, 1);
					element.style.borderRightColor = new Color(0, 207 / 255f, 196 / 255f, 1);
					element.style.borderTopLeftRadius = 5;
					element.style.borderTopRightRadius = 5;
					element.style.borderBottomLeftRadius = 5;
					element.style.borderBottomRightRadius = 5;
				}
				else if (element.ClassListContains("unity-base-popup-field__text"))
					element.style.color = new Color(233 / 255f, 247 / 255f, 248 / 255f, 1);
				else if (element.ClassListContains("unity-base-field__label"))
					element.style.color = new Color(167 / 255f, 190 / 255f, 199 / 255f, 1);

				for (var i = 0; i < element.childCount; i++) pending.Push(element[i]);
			}
		}

		private void ShowFailure(string message)
		{
			if (root == null) return;
			var failure = new Label("Graph Explorer sample could not initialize: " + message);
			failure.style.color = Color.red;
			failure.style.whiteSpace = WhiteSpace.Normal;
			root.Add(failure);
		}

		private static void DestroyOwned(UnityEngine.Object value)
		{
			if (value == null) return;
			if (Application.isPlaying) UnityEngine.Object.Destroy(value);
			else UnityEngine.Object.DestroyImmediate(value);
		}
	}
}

