using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace HackThePlanet
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed partial class WorkstationApp : MonoBehaviour
    {
        public static WorkstationApp Instance { get; private set; }
        public GameSimulation State { get; private set; }
        public string CurrentView => view;
        public VisualElement Root => root;
        public SignalVisuals Visuals { get; private set; }
        public bool Testing { get; set; }
        public const string TextSizePreference = "HackThePlanet.TextSize";
        public const string SelectWithoutCameraFocusPreference = "HackThePlanet.SelectWithoutCameraFocus";
        public const string HackedConnectionColorPreference = "HackThePlanet.HackedConnectionColor";
        public const string MutePreference = "HackThePlanet.Mute";
        private static readonly Color DefaultHackedConnectionColor = new Color(.77f, .96f, .42f, 1f);
        public static Color HackedConnectionColor { get; private set; } = new Color(.77f, .96f, .42f, 1f);
        public int TextSizePercent { get; private set; } = 200;
        public bool SelectWithoutCameraFocus { get; private set; } = true;
        private readonly Dictionary<TextElement, float> baseTextSizes = new Dictionary<TextElement, float>();
        private const float TooltipDelay=.6f;
        // Owners with this class show their tooltip immediately, styled like the graph node intel card.
        public const string IntelTooltipClass="intel-tooltip";
        private Label tooltipLabel;
        private VisualElement tooltipOwner;
        private Vector2 pointerPosition;
        private float tooltipAt;
        private VisualElement root, overlay;
        private Label traceNumber, traceState;
        private Label opPower, opCooling, opCompute, opBandwidth, opStorage, opMemory, opReclaim;
        private VisualElement traceFill;
        private GraphViewport canvas;
        private Contract selectedJob;
        private string selectedNode, selectedTech, view="network", modal;
        private readonly Dictionary<string, Action> uiActions=new Dictionary<string, Action>();
        private readonly GraphCameraState networkCamera=new GraphCameraState(), researchCamera=new GraphCameraState();
        private bool dirty=true, muted, expandedBrief;
        private float speed=1, saveTimer;
        private AudioSource audioSource;
        private AudioClip clickSound, successSound, warningSound;
        private int traceBand;
        private bool sessionActive;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<WorkstationApp>()!=null) return;
            new GameObject("Hack The Planet // Workstation").AddComponent<WorkstationApp>();
        }

        private void Awake()
        {
            if (GameData.ValidationLaunch) { enabled=false; return; }
            Instance=this; Application.targetFrameRate=60; Application.runInBackground=true;
            Exception initializationError=null;
            try {
            State=new GameSimulation(CareerStorage.Load());
            selectedJob=State.Active?State.Job:State.Catalog.Contracts(State.Career).FirstOrDefault(c=>!State.Career.completed.Contains(c.id)&&State.Career.completed.Count>=c.requiredWins)??State.Catalog.Contracts(State.Career).First(c=>!c.recurring);
            State.Changed+=()=>{dirty=true;if(!Testing)CareerStorage.Save(State.Career);};
            selectedNode=null;
            sessionActive=State.Active;
            selectedTech=null;
            } catch(Exception ex) { initializationError=ex;State=null; }
            var camera=Camera.main;
            if(camera==null)camera=new GameObject("Workstation Camera").AddComponent<Camera>();
            if(FindAnyObjectByType<AudioListener>()==null)camera.gameObject.AddComponent<AudioListener>();
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Palette.Hex("080E13");camera.cullingMask=0;
            var panel=ScriptableObject.CreateInstance<PanelSettings>();
            panel.scaleMode=PanelScaleMode.ScaleWithScreenSize;panel.referenceResolution=new Vector2Int(1680,960);
            panel.screenMatchMode=PanelScreenMatchMode.MatchWidthOrHeight;panel.match=.5f;
            panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("HackThePlanet/Theme");
            var document=gameObject.AddComponent<UIDocument>();document.panelSettings=panel;
            root=document.rootVisualElement;root.name="workstation";root.AddToClassList("workstation");
            root.focusable=true;
            root.RegisterCallback<KeyDownEvent>(HandleShortcut,TrickleDown.TrickleDown);
            root.styleSheets.Add(Resources.Load<StyleSheet>("HackThePlanet/Workstation"));
            root.styleSheets.Add(Resources.Load<StyleSheet>("HackThePlanet/GhostFrequency"));
            root.styleSheets.Add(Resources.Load<StyleSheet>("HackThePlanet/Readability"));
            TextSizePercent=Mathf.Clamp(PlayerPrefs.GetInt(TextSizePreference,200),100,300);
            SelectWithoutCameraFocus=PlayerPrefs.GetInt(SelectWithoutCameraFocusPreference,1)!=0;
            string hackedColor=PlayerPrefs.GetString(HackedConnectionColorPreference,ColorUtility.ToHtmlStringRGBA(DefaultHackedConnectionColor));
            if(ColorUtility.TryParseHtmlString("#"+hackedColor,out var parsedHackedColor))HackedConnectionColor=parsedHackedColor;
            muted=PlayerPrefs.GetInt(MutePreference,0)!=0;
            root.RegisterCallback<GeometryChangedEvent>(_=>UpdateReadableLayout());
            root.RegisterCallback<PointerMoveEvent>(evt=>{
                pointerPosition=root.WorldToLocal(evt.position);UpdateRigDrag(evt.position);
                var target=evt.target as VisualElement;
                while(target!=null&&target!=root&&string.IsNullOrEmpty(target.tooltip))target=target.parent;
                if(target==root||target!=null&&!target.enabledInHierarchy)target=null;
                if(target!=tooltipOwner){HideTooltip();tooltipOwner=target;tooltipAt=Time.unscaledTime+(target!=null&&target.ClassListContains(IntelTooltipClass)?0:TooltipDelay);}
            });
            root.RegisterCallback<PointerUpEvent>(EndRigDrag,TrickleDown.TrickleDown);
            root.RegisterCallback<WheelEvent>(evt=>{
                if(dragRigId==null||view!="rig"||modal!=null||Mathf.Abs(evt.delta.y)<.01f)return;
                RotateRigDrag();
                evt.StopImmediatePropagation();
            },TrickleDown.TrickleDown);
            root.RegisterCallback<PointerDownEvent>(_=>HideTooltip(),TrickleDown.TrickleDown);
            root.RegisterCallback<PointerLeaveEvent>(_=>HideTooltip());
            root.RegisterCallback<TooltipEvent>(evt=>{
                evt.tooltip=string.Empty;evt.StopImmediatePropagation();
            },TrickleDown.TrickleDown);
            if(initializationError!=null){Text(root,"GAMEPLAY INITIALIZATION FAILED","large-heading");Text(root,initializationError.Message,"body-copy amber");Debug.LogException(initializationError);return;}
            Visuals=new SignalVisuals();
            audioSource=gameObject.AddComponent<AudioSource>();audioSource.playOnAwake=false;audioSource.volume=.12f;
            clickSound=Tone(640,.045f);successSound=Tone(880,.22f);warningSound=Tone(210,.15f);
            if(CareerStorage.Warning!=null)State.Career.news.Insert(0,CareerStorage.Warning);
            Build();
        }

        private static AudioClip Tone(float hz,float seconds)
        {
            int count=(int)(22050*seconds);var data=new float[count];
            for(int i=0;i<count;i++){float t=i/22050f;data[i]=Mathf.Sin(t*hz*Mathf.PI*2)*Mathf.Sin(Mathf.PI*i/count)*.4f;}
            var clip=AudioClip.Create("terminal",count,1,22050,false);clip.SetData(data,0);return clip;
        }
        private void Beep(bool success=false) { if(!muted&&audioSource!=null)audioSource.PlayOneShot(success?successSound:clickSound); }

        private void Update()
        {
            if(State==null)return;
            State.Tick(Time.unscaledDeltaTime*speed);
            bool active=State.Active;
            if(sessionActive&&!active){selectedNode=null;networkCamera.Reset();}
            sessionActive=active;
            int band=active?(State.Op.trace>=State.TraceDanger?2:State.Op.trace>=State.TraceWarning?1:0):0;
            if(band>traceBand&&!muted&&audioSource!=null)audioSource.PlayOneShot(warningSound,band==2?1:.5f);
            traceBand=band;
            if(!State.Active&&State.LastReport!=null&&modal!="report"&&State.LastReport!=lastShownReport)
            {lastShownReport=State.LastReport;modal="report";dirty=true;Beep(State.LastReport.success);}
            if(State.Active&&Time.unscaledTime>=nextResourceRefresh){nextResourceRefresh=Time.unscaledTime+.5f;if(view=="network")RefreshResourceActions();}
            // Rebuilding mid-drag would call CancelRigDrag and snap a lifted part back into the rack.
            if(dirty&&dragRigId==null)Build();
            if(State.Active&&view=="network")RefreshActionBar();
            if(tooltipOwner!=null&&tooltipLabel!=null&&Time.unscaledTime>=tooltipAt)
            {
                tooltipLabel.text=GameplayRichText.Format(tooltipOwner.tooltip);
                tooltipLabel.EnableInClassList(IntelTooltipClass,tooltipOwner.ClassListContains(IntelTooltipClass));
                tooltipLabel.style.display=DisplayStyle.Flex;
                PositionTooltip();
            }
            UpdateMeters();
            canvas?.Tick(State,Visuals.Mode);
            Visuals.Tick(State,view=="research");
            root.EnableInClassList("trace-alert",State.Active&&State.Op.trace>=State.TraceWarning);
            saveTimer+=Time.unscaledDeltaTime;
            if(saveTimer>=5){saveTimer=0;if(State.Active&&!Testing)CareerStorage.Save(State.Career);}
        }
        private float nextResourceRefresh;
        private OperationReport lastShownReport;
        private void HandleShortcut(KeyDownEvent evt)
        {
            if(State==null)return;
            if(view=="rig"&&modal==null&&evt.keyCode==KeyCode.R){RotateRigSelection();evt.StopImmediatePropagation();return;}
            if(evt.keyCode==KeyCode.Escape&&dragRigId!=null){CancelRigDrag();dirty=true;evt.StopImmediatePropagation();return;}
            if(evt.keyCode==KeyCode.Escape)
            {
                if(modal=="settings")ResumeFromSettings();
                else if(modal=="reset-save")CancelResetSave();
                else if(modal=="debug"){modal="settings";settingsError=null;dirty=true;}
                else if(modal=="help"){modal=null;if(State.Active)State.SetPaused(pausedBeforeHelp);dirty=true;}
                else ShowSettings();
            }
            else if(modal=="help"&&evt.keyCode==KeyCode.F1)
            {
                modal=null;if(State.Active)State.SetPaused(pausedBeforeHelp);dirty=true;
            }
            else if(modal!=null)return;
            else if(evt.keyCode==KeyCode.F1)ShowHelp();
            else if(evt.keyCode==KeyCode.Space&&State.Active)State.SetManualPause(!State.Op.paused);
            else if(view=="network"&&State.Active)
            {
                var action=State.AvailableActions(selectedNode).FirstOrDefault(a=>a.shortcut!=KeyCode.None&&a.shortcut==evt.keyCode);
                if(action==null)action=State.AvailableActions().FirstOrDefault(a=>a.shortcut!=KeyCode.None&&a.shortcut==evt.keyCode);
                if(action==null)return;Run(action.id,action.operatorTarget?null:selectedNode);
            }
            else return;
            evt.StopImmediatePropagation();
        }
        private void OnApplicationQuit(){if(State!=null&&!Testing)CareerStorage.Save(State.Career);}
        private void OnApplicationPause(bool paused){if(paused&&State!=null&&!Testing)CareerStorage.Save(State.Career);}
        private void OnDestroy(){canvas?.Dispose();Visuals?.Dispose();if(Instance==this)Instance=null;}

        private void Build()
        {
            CancelRigDrag();baseTextSizes.Clear();tooltipOwner=null;
            dirty=false;canvas?.Dispose();uiActions.Clear();root.Clear();traceFill=null;traceNumber=null;traceState=null;opPower=opCooling=opCompute=opBandwidth=opStorage=opMemory=opReclaim=null;canvas=null;
            BuildHeader();
            var body=El(root,"desktop-body");
            BuildRail(body);
            var content=El(body,"desktop-content");
            var workspace=El(content,"workspace-row");
            if(view=="network")BuildNetwork(workspace);
            else if(view=="research")BuildResearch(workspace);
            else if(view=="rig")BuildRig(workspace);
            else BuildIntel(workspace);
            var footer=El(root,"footer");Text(footer,"GHOST FREQUENCY / HTP TERMINAL 1.0","footer-brand");
            Text(footer,State.Active?"ENCRYPTED SESSION  /  "+(State.Op.paused?"PAUSED":"LIVE"):"LOCAL SESSION  /  PRIVATE GATEWAY","footer-center");
            Text(footer,CareerStorage.Warning!=null?"SAVE UNAVAILABLE":"AUTOSAVE ACTIVE  /  LOCAL STORAGE","footer-save");
            if(modal=="settings"||modal=="reset-save"||modal=="debug"||modal=="refund-research")
                foreach(var child in root.Children())child.SetEnabled(false);
            if(modal=="refund-research")BuildRefundModal();else if(modal!=null)BuildModal();
            Visuals.AttachGlass(root);
            tooltipLabel=Text(root,"","readable-tooltip");tooltipLabel.enableRichText=true;tooltipLabel.pickingMode=PickingMode.Ignore;
            tooltipLabel.RegisterCallback<GeometryChangedEvent>(_=>PositionTooltip());
            // Snapshot authored fonts after USS resolves, before assigning any scaled
            // fonts. This includes inline graph fonts and inherited control text.
            root.Q(className:"header").RegisterCallbackOnce<GeometryChangedEvent>(_=>{
                foreach(var text in root.Query<TextElement>().ToList())
                    baseTextSizes[text]=text.resolvedStyle.fontSize;
                ApplyTextSize();
            });
            UpdateReadableLayout();
            var modalFocus=modal=="settings"?root.Q<Button>("resume"):modal=="reset-save"?root.Q<Button>("cancel-reset"):modal=="debug"?root.Q<Button>("debug-back"):null;
            if(modalFocus!=null)modalFocus.Focus();else root.Focus();
        }

        private void BuildHeader()
        {
            var header=El(root,"header");var summary=El(header,"header-summary");var logo=El(summary,"logo");logo.Add(new DeviceGlyph(NodeKind.Gateway,Palette.Lime,42));
            var brand=El(logo,"brand");Text(brand,"HACK THE PLANET","brand-name");Text(brand,"GHOST FREQUENCY  /  INDEPENDENT OPERATOR","brand-sub");
            var wallet=El(summary,"wallet");
            var credits=El(wallet,"wallet-cell");Text(credits,"CREDITS","eyebrow");Text(credits,State.Career.credits.ToString("N0")+" cr","credit-amount");
            var xp=El(wallet,"wallet-cell");Text(xp,"RESEARCH XP","eyebrow");Text(xp,State.Career.xp.ToString("N0")+" XP","xp-amount");
            var identity=El(summary,"identity");Text(identity,"OP / ZERO","identity-name");Text(identity,"DAY "+State.Career.day.ToString("00")+"  /  "+Rank(),"identity-day");
            var tabs=El(header,"header-tabs");
            Tab(tabs,"network","OPERATIONS");Tab(tabs,"research","RESEARCH");Tab(tabs,"rig","YOUR RIG");Tab(tabs,"intel","INTELLIGENCE");
        }

        public void SetTextSize(int percent)
        {
            TextSizePercent=Mathf.Clamp(percent,100,300);
            if(!Testing){PlayerPrefs.SetInt(TextSizePreference,TextSizePercent);PlayerPrefs.Save();}
            var value=root.Q<Label>("text-size-value");if(value!=null)value.text="TEXT SIZE / "+TextSizePercent+"%";
            root.Q<SliderInt>("text-size")?.SetValueWithoutNotify(TextSizePercent);
            ApplyTextSize();
        }

        public void SetSelectWithoutCameraFocus(bool value)
        {
            SelectWithoutCameraFocus=value;
            if(!Testing){PlayerPrefs.SetInt(SelectWithoutCameraFocusPreference,value?1:0);PlayerPrefs.Save();}
        }
        public void SetHackedConnectionColor(Color value)
        {
            value.a=1f;HackedConnectionColor=value;
            if(!Testing){PlayerPrefs.SetString(HackedConnectionColorPreference,ColorUtility.ToHtmlStringRGBA(value));PlayerPrefs.Save();}
            canvas?.RefreshConnectionColors();
        }
        private void SetMuted(bool value)
        {
            muted=value;
            if(!Testing){PlayerPrefs.SetInt(MutePreference,value?1:0);PlayerPrefs.Save();}
            dirty=true;
        }
        private void BuildDisplayEffectToggles(VisualElement parent)
        {
            var fieldToggle=new Toggle("Signal field"){name="effect-signal-field",tooltip="Procedural background on network and research maps."};
            fieldToggle.SetValueWithoutNotify(Visuals.SignalFieldEnabled);
            fieldToggle.RegisterValueChangedCallback(evt=>{Visuals.SetSignalFieldEnabled(evt.newValue,!Testing);dirty=true;Beep();});
            parent.Add(fieldToggle);
            var glassToggle=new Toggle("Phosphor glass"){name="effect-phosphor-glass",tooltip="Screen-wide glass treatment over the workstation UI."};
            glassToggle.SetValueWithoutNotify(Visuals.PhosphorGlassEnabled);
            glassToggle.RegisterValueChangedCallback(evt=>{Visuals.SetPhosphorGlassEnabled(evt.newValue,!Testing);dirty=true;Beep();});
            parent.Add(glassToggle);
            var animateToggle=new Toggle("Animate display effects"){name="effect-animate",tooltip="When off, enabled layers stay visible but motion freezes."};
            animateToggle.SetValueWithoutNotify(Visuals.AnimateDisplayEffects);
            animateToggle.RegisterValueChangedCallback(evt=>{Visuals.SetAnimateDisplayEffects(evt.newValue,!Testing);dirty=true;Beep();});
            parent.Add(animateToggle);
        }

        private void ApplyTextSize()
        {
            float scale=TextSizePercent/100f;
            foreach(var pair in baseTextSizes)pair.Key.style.fontSize=pair.Value*scale;
            if(tooltipLabel!=null)tooltipLabel.style.fontSize=12*scale;
            canvas?.SetTextScale(scale);
            UpdateReadableLayout();HideTooltip();
        }

        private void UpdateReadableLayout()
        {
            // Font size stays independent of viewport size; the layout absorbs growth.
            root.EnableInClassList("large-text",TextSizePercent>225);
            root.EnableInClassList("compact-width",root.contentRect.width>0&&root.contentRect.width<1400);
        }

        private void HideTooltip()
        {tooltipOwner=null;if(tooltipLabel!=null)tooltipLabel.style.display=DisplayStyle.None;}

        private void PositionTooltip()
        {
            if(tooltipLabel==null||tooltipOwner==null||tooltipLabel.resolvedStyle.display==DisplayStyle.None)return;
            tooltipLabel.style.left=Mathf.Clamp(pointerPosition.x+14,0,Mathf.Max(0,root.contentRect.width-tooltipLabel.layout.width-8));
            tooltipLabel.style.top=Mathf.Clamp(pointerPosition.y+20,0,Mathf.Max(0,root.contentRect.height-tooltipLabel.layout.height-8));
        }

        private void BuildRail(VisualElement body)
        {
            var rail=El(body,"rail");
            El(rail,"flex-spacer");
            RailCircleButton(rail,"\u2699",ShowSettings,"settings","rail-help settings-button").tooltip="Settings / Escape";
            RailCircleButton(rail,"?",ShowHelp,"help","rail-help").tooltip="Field guide / F1";
        }

        private void Tab(VisualElement parent,string target,string label)
        {var b=Btn(parent,label,()=>Navigate(target),"tab-"+target,"tab");if(view==target)b.AddToClassList("active");}
        public void Navigate(string target)
        { if(State.Active&&target!="network")State.SetPaused(true);view=target;modal=null;dirty=true;Beep(); }

        private void BuildNetwork(VisualElement workspace)
        {
            var left=Scroll(workspace,"left-panel");
            var lh=El(left,"panel-heading");Text(lh,State.Active?"ACTIVE CONTRACT":"CONTRACTS","large-heading panel-title");
            if(!State.Active)
            {
                var list=El(left,"contract-list");
                foreach(var job in State.Catalog.Contracts(State.Career))
                {
                    bool done=State.Career.completed.Contains(job.id),locked=State.Career.completed.Count<job.requiredWins;
                    var card=Btn(list,"",()=>{selectedJob=job;selectedNode=null;networkCamera.Reset();dirty=true;Beep();},"contract-"+job.id,"contract-card");
                    if(selectedJob.id==job.id)card.AddToClassList("active");if(locked)card.AddToClassList("locked");
                    var meta=El(card,"row-between");Text(meta,done?"DELIVERED":locked?"ENCRYPTED":"AVAILABLE","contract-state"+(done?" lime":locked?" muted":" cyan"));Text(meta,job.recurring?"R":"0"+(State.Catalog.Contracts(State.Career).Where(c=>!c.recurring).Select(c=>c.id).ToList().IndexOf(job.id)+1),"micro muted");
                    Text(card,job.title,"contract-title");Text(card,job.company,"micro muted");
                    Text(card,job.DifficultyLabel+" / "+(job.Intel==ContractIntel.Full?"FULL INTEL":job.Intel==ContractIntel.Topology?"TOPOLOGY ONLY":"NO INTEL"),"contract-difficulty micro "+(job.Intel==ContractIntel.None?"amber":"cyan"));
                    var reward=El(card,"contract-reward");Text(reward,job.credits.ToString("N0")+" cr","small lime");Text(reward,"+"+job.xp+" XP","small muted");
                }
            }
            else
            {
                var brief=El(left,"active-brief");Text(brief,State.Job.tag,"micro cyan");Text(brief,State.Job.title,"mission-title");Text(brief,State.Job.client,"micro muted");
                Btn(brief,expandedBrief?"HIDE BRIEF ▴":"READ BRIEF ▾",()=>{expandedBrief=!expandedBrief;dirty=true;},"toggle-brief","brief-toggle");if(expandedBrief)RichText(brief,State.Job.description,"body-copy");Text(brief,"OBJECTIVES","eyebrow section-space");
                foreach(var n in State.Op.nodes.Where(n=>n.objective))
                {var b=Btn(brief,(State.ObjectiveDone(n)?"[+] ":"[ ] ")+n.label,()=>SelectNode(n.id),"objective-"+n.id,"objective-row");if(State.ObjectiveDone(n))b.AddToClassList("lime");}
                Text(brief,"REWARD CONDITIONS","eyebrow section-space");Text(brief,State.Job.credits+" cr + "+State.Job.xp+" XP on delivery","small lime");
                RichText(brief,State.RewardDescription,"small muted");

            }
            var loadout=El(left,"loadout");Text(loadout,"EQUIPPED ABILITIES  /  "+State.Career.equipped.Count+" OF "+State.Slots,"eyebrow");
            if(State.Career.equipped.Count==0)Text(loadout,"Equip abilities in Research between contracts.","small muted");
            foreach(var id in State.Career.equipped)
            {
                var tech=FindTech(id);
                var entry=Btn(loadout,tech.title+" / "+(State.Active?"IN ACTION BAR":"EQUIPPED"),()=>{if(!State.Active){selectedTech=id;Navigate("research");}},"ability-"+id,"ability-button");entry.SetEnabled(!State.Active);
            }
            var mid=El(workspace,"network-panel");var map=El(mid,"map-content");var mh=El(map,"map-heading");var title=El(mh,"map-title-block");
            var intel=State.Active?ContractIntel.Full:selectedJob.Intel;
            Text(title,State.Active?"LIVE NETWORK":"NETWORK INTELLIGENCE","eyebrow cyan");Text(title,(State.Active?State.Job:selectedJob).company,"map-title");
            var state=El(mh,"map-status");            Text(state,State.Active?(State.Op.paused?"||  SIMULATION PAUSED":"●  ENCRYPTED CONNECTION"):"○  "+selectedJob.DifficultyLabel+" / "+(intel==ContractIntel.Full?"FULL INTEL":intel==ContractIntel.Topology?"TOPOLOGY ONLY":"NO INTEL"),"micro "+(State.Active?"lime":"muted"));
            if(State.Active)Text(state,State.Op.nodes.Count(n=>n.known)+" / "+State.Op.nodes.Count+" SYSTEMS DISCOVERED","micro muted");
            if(State.Active)BuildTraceBar(map);
            var nodes=new List<NetNode>();
            if(intel==ContractIntel.None)
            {
                var blocked=El(map,"graph-viewport no-intel-view");blocked.name="no-intel-view";
                Visuals.AttachField(blocked,true);
                var message=El(blocked,"no-intel-message");
                Text(message,"NO INTEL AVAILABLE","no-intel-title");
            }
            else
            {
                var preview=State.Active?null:NetworkGenerator.Generate(selectedJob,State.Career,State.Catalog);
                nodes=State.Active?State.Op.nodes:preview.nodes;
                var links=State.Active?State.Op.links:preview.links;
                var route=State.Active?State.Route(selectedNode,true):null;
                var job=State.Active?State.Job:selectedJob;
                var entryId=State.Active?State.Op.entryNode:State.Catalog.Network(job.networkId).entryNode;
                var objectiveId=job.Intel!=ContractIntel.None?nodes.FirstOrDefault(n=>n.objective&&!n.collected)?.id:null;
                canvas=new GraphViewport(nodes,links,selectedNode,!State.Active,SelectNode,route,networkCamera,intel,job.Intel,State.Catalog,entryId,objectiveId,State.Waypoints(job),State.WaypointTier);map.Add(canvas);Visuals.AttachField(canvas);Visuals.AttachIntelCard(canvas.IntelStatic);
            }
            if(State.Active)BuildActionBar(map);
            var mapFooter=El(map,"map-footer");
            var legend=El(mapFooter,"legend");foreach(var kind in State.Catalog.NodeTypes.Where(t=>intel==ContractIntel.Full&&nodes.Any(n=>n.typeId==t.id)&&t.glyph!=NodeKind.Gateway))
            {var key=El(legend,"legend-key");var dot=El(key,"legend-dot");dot.style.backgroundColor=kind.color;Text(key,kind.title,"micro muted");}
            if(intel!=ContractIntel.Full)Text(legend,intel==ContractIntel.Topology?"UNKNOWN SYSTEMS / CONNECTIONS ONLY":"NETWORK SIGNAL UNAVAILABLE","micro muted");
            if(intel!=ContractIntel.None)
            {var controls=El(mapFooter,"map-controls");Btn(controls,"−",()=>canvas.Zoom(1.15f),"zoom-out","map-button");Btn(controls,"+",()=>canvas.Zoom(.85f),"zoom-in","map-button");Btn(controls,"FIT",()=>canvas.ResetView(),"fit","map-button");}
            if(State.Active)BuildOperationBar(mid);
            var right=El(workspace,"right-panel");
            if(State.Active)BuildTarget(right,State.Node(selectedNode));
            else BuildBrief(right);
        }

        public void SelectNode(string id){selectedNode=id;networkCamera.FocusedId=string.IsNullOrEmpty(id)?null:id;dirty=true;Beep();}
        public void SelectTech(string id){selectedTech=id;researchCamera.FocusedId=string.IsNullOrEmpty(id)?null:id;dirty=true;Beep();}

        private void BuildBrief(VisualElement right)
        {
            var scroll=new ScrollView(ScrollViewMode.Vertical);scroll.AddToClassList("inspector-scroll");scroll.AddToClassList("contract-brief");right.Add(scroll);
            Text(scroll,"CONTRACT BRIEF","eyebrow");Text(scroll,selectedJob.tag,"eyebrow cyan section-space");Text(scroll,selectedJob.title,"mission-title");
            Text(scroll,selectedJob.client,"eyebrow muted");RichText(scroll,selectedJob.description,"body-copy");
            Text(scroll,selectedJob.DifficultyLabel+" / NETWORK INTEL","eyebrow section-space");RichText(scroll,selectedJob.IntelDescription,"body-copy");
            Text(scroll,"THE ASK","eyebrow section-space");RichText(scroll,selectedJob.objective,"body-copy");
            BuildStorageBrief(scroll);BuildWaypointPlanner(scroll);
            Text(scroll,"ESCROW PAYOUT","eyebrow section-space");Text(scroll,selectedJob.credits.ToString("N0")+" cr  /  "+selectedJob.xp+" XP","payout");
            Text(scroll,selectedJob.RewardMultiplier.ToString("0.#")+"× "+selectedJob.DifficultyLabel+" PAYOUT / CREDITS + XP","micro cyan");
            RichText(scroll,selectedJob.condition,"small muted");Text(scroll,"OPTIONAL ARCHIVES","eyebrow section-space");RichText(scroll,State.ArchiveDescription,"small muted");
            Text(scroll,"OPERATOR NOTE","eyebrow section-space");RichText(scroll,State.Career.jobs==0?"Your gateway is already controlled. Hack a connected relay to get inside, or scan it to discover more routes. Pause whenever you need to think.":"Scanners add pressure while you work near them. A captured hub weakens the systems it protects. Your old successes may have changed these defenses.","body-copy note");
            var bottom=El(right,"inspector-bottom");bool done=State.Career.completed.Contains(selectedJob.id),locked=State.Career.completed.Count<selectedJob.requiredWins,rigBlocked=!State.RigValid;
            var start=Btn(bottom,done?"CONTRACT DELIVERED":locked?"COMPLETE EARLIER CONTRACTS":"CONNECT TO NETWORK  →",StartSelected,"connect","primary-button");start.SetEnabled(!done&&!locked&&!rigBlocked);
            if(rigBlocked)Text(bottom,string.Join("\n",State.RigProblems),"small connect-block-warning center-text");
        }

        private void BuildWaypointPlanner(VisualElement parent)
        {
            int tier=State.WaypointTier;
            Text(parent,"WAYPOINTS / "+tier+" SLOTS","eyebrow section-space");
            if(tier==0){Text(parent,"Research Waypoints I in RECON to mark systems before connecting.","small muted");return;}
            if(selectedJob.Intel==ContractIntel.None){RichText(parent,"No network intel: waypoint placement is unavailable for this contract.","small muted");return;}
            var network=NetworkGenerator.Generate(selectedJob,State.Career,State.Catalog);
            var points=State.Waypoints(selectedJob);
            var selected=network.nodes.FirstOrDefault(n=>n.id==selectedNode);
            bool canPlan=!State.Career.completed.Contains(selectedJob.id)&&State.Career.completed.Count>=selectedJob.requiredWins;
            Text(parent,"Select a system on the map, then place a marker. Markers stay visible during the run, even before discovery.","small muted");
            var add=Btn(parent,"PLACE ON SELECTED SYSTEM ("+points.Count+" / "+tier+")",()=>State.PlaceWaypoint(selectedJob,selectedNode),"place-waypoint","secondary-button");
            add.SetEnabled(canPlan&&selected!=null&&points.Count<tier&&!points.Any(w=>w.nodeId==selectedNode));
            foreach(var waypoint in points)
            {
                int number=points.ToList().IndexOf(waypoint)+1;
                var row=El(parent,"section-space");
                var heading=Text(row,"W"+number+" / "+(selectedJob.Intel==ContractIntel.Full?network.nodes.First(n=>n.id==waypoint.nodeId).label:"UNKNOWN SYSTEM"),"small");
                heading.style.color=GameSimulation.WaypointColors[tier>=2?waypoint.color:0];
                Btn(row,"LOCATE",()=>SelectNode(waypoint.nodeId),"locate-waypoint-"+number,"secondary-button");
                if(tier>=2)
                {
                    var colors=El(row,"row-between");
                    string[] names={"CYAN","AMBER","VIOLET","PINK","WHITE"};
                    for(int i=0;i<names.Length;i++)
                    {
                        int color=i;var button=Btn(colors,names[i].Substring(0,1),()=>State.EditWaypoint(selectedJob,waypoint.nodeId,color,waypoint.label,waypoint.showPath),"waypoint-"+number+"-color-"+i,"map-button");button.tooltip=names[i];
                        button.style.color=GameSimulation.WaypointColors[i];button.SetEnabled(canPlan);
                    }
                }
                if(tier>=3)
                {
                    var label=new TextField("Label (40 characters)"){name="waypoint-label-"+number,value=waypoint.label,maxLength=40,isDelayed=true};
                    label.SetEnabled(canPlan);label.RegisterValueChangedCallback(evt=>State.EditWaypoint(selectedJob,waypoint.nodeId,waypoint.color,evt.newValue,waypoint.showPath));row.Add(label);
                }
                if(tier>=4)
                {
                    var route=Btn(row,waypoint.showPath?"HIDE GATEWAY ROUTE":"SHOW SHORTEST GATEWAY ROUTE",()=>State.EditWaypoint(selectedJob,waypoint.nodeId,waypoint.color,waypoint.label,!waypoint.showPath),"waypoint-route-"+number,"secondary-button");route.SetEnabled(canPlan);
                }
                Btn(row,"REMOVE",()=>State.RemoveWaypoint(selectedJob,waypoint.nodeId),"remove-waypoint-"+number,"secondary-button").SetEnabled(canPlan);
            }
            if(tier>=4)Text(parent,"One route at a time, from the gateway through discovered and undiscovered systems.","small muted");
        }

        public void StartSelected()
        {
            if(State.Start(selectedJob)){selectedNode=null;view="network";networkCamera.Reset();Beep();dirty=true;}
        }

        private void BuildTarget(VisualElement right,NetNode node)
        {
            var scroll=new ScrollView(ScrollViewMode.Vertical);scroll.AddToClassList("inspector-scroll");right.Add(scroll);
            Text(scroll,"TARGET INSPECTOR","eyebrow");
            if(node==null)return;
            if(!node.known)
            {
                Text(scroll,"UNDISCOVERED SYSTEM","mission-title");Text(scroll,"Scan a reachable system to discover its neighbors. Blueprint research reveals objective regions at the start of a contract.","body-copy muted");
                var exit=El(right,"inspector-bottom");Btn(exit,"DISCONNECT",DisconnectClicked,"disconnect","danger-button");return;
            }
            var nodeType=State.Catalog.NodeTypes.First(t=>t.id==node.typeId);
            var type=El(scroll,"target-type");type.Add(new DeviceGlyph(node.kind,nodeType.color,42));
            var status=El(type,"target-status");Text(status,nodeType.title.ToUpperInvariant(),"micro cyan");Text(status,node.controlled?"ACCESS ESTABLISHED":State.Reachable(node)?"WITHIN REACH":"ROUTE REQUIRED","micro "+(node.controlled?"lime":"muted"));
            Text(scroll,node.label,"target-title");Text(scroll,"ADDR / "+node.id.ToUpperInvariant(),"micro muted");
            var stats=El(scroll,"target-stats");Text(stats,"SECURITY","eyebrow");Text(stats,State.EffectiveSecurity(node).ToString("00")+" / 10","small");
            var bars=El(scroll,"security-bars");for(int i=0;i<10;i++){var bar=El(bars,"security-bar");if(i<State.EffectiveSecurity(node))bar.style.backgroundColor=nodeType.color;}
            if(node.objective)Text(scroll,State.ObjectiveDone(node)?"[+] CONTRACT OBJECTIVE SECURED":"[*] CONTRACT OBJECTIVE","objective-tag");
            if(node.bonus)Text(scroll,node.collected?"[+] OPTIONAL FILES SECURED":"[+] OPTIONAL ARCHIVE / "+State.ArchiveDescription,"micro lime section-space");
            if(node.suppressed>0)Text(scroll,State.Op.reservations.Any(r=>r.target==node.id)?"SUPPRESSED / RAM RESERVED":"SUPPRESSED / "+Mathf.CeilToInt(node.suppressed)+"s remaining","micro lime section-space");
            if(node.scanned)Text(scroll,"INTEL VERIFIED / INTRUSION PREPARED","micro cyan section-space");
            var researchHint=State.ActionResearchHint(node.id);if(researchHint!=null)Text(scroll,researchHint,"small muted section-space");
            if(node.storage>0)RichText(scroll,"FILE SIZE / "+node.storage.ToString("0")+" GB STORAGE","small cyan");
            string description=string.IsNullOrWhiteSpace(node.detail)?nodeType.description:node.detail;
            if(!string.IsNullOrWhiteSpace(description))Text(scroll,description,"body-copy muted");
            if(node.bonus&&node.collected)
            {
                var fragment=LoreContent.Find(State.Job,node.loreId);
                if(fragment!=null){Text(scroll,fragment.title,"eyebrow lime section-space");Text(scroll,fragment.text,"body-copy");Text(scroll,"Extract to preserve this fragment.","small muted");}
            }
            var route=State.Route(node.id,true);
            Text(scroll,"ACCESS ROUTE","eyebrow section-space");
            Text(scroll,route!=null&&route.IsReachable?string.Join(" > ",route.NodeIds.Select(id=>id==State.Op.entryNode?"LOCAL":State.Node(id).label)):"Extend your controlled route to this system.","small muted");
            var bottom=El(right,"inspector-bottom");
            Btn(bottom,State.ObjectiveComplete?"EXTRACT & DELIVER  →":"DISCONNECT",DisconnectClicked,"disconnect",State.ObjectiveComplete?"primary-button":"danger-button");
            Text(bottom,State.ObjectiveCount+" / "+State.ObjectiveTotal+" objectives secured","micro muted center-text");
        }

        private void ActionButton(VisualElement parent,ActionInfo action,bool primary)
        {
            var wrapper=El(parent,"action-slot");wrapper.AddToClassList(IntelTooltipClass);
            var button=Btn(wrapper,"",()=>Run(action.id,action.operatorTarget?null:selectedNode),"action-"+action.id,"action-button");
            var content=El(button,"action-card-content");
            var icon=new ActionGlyph(action.id,Palette.Cyan,26);content.Add(icon);
            var details=El(content,"action-card-details");
            var row=El(details,"action-title-row");Text(row,"","action-title");if(!string.IsNullOrEmpty(action.shortcutLabel))Text(row,action.shortcutLabel,"keycap");
            Text(details,"","action-estimate");
            Text(details,"","action-cost");
            string description=action.description;
            if(string.IsNullOrWhiteSpace(description))description=State.Catalog.Technologies.FirstOrDefault(t=>t.grants.Contains(action.id))?.description??"Equipped special ability.";
            actionButtons[action.id]=new ActionButtonParts(action,button,wrapper,icon,description);
            UpdateActionButton(actionButtons[action.id],primary);
            for(int i=0;i<button.childCount;i++)IgnorePointerHits(button[i]);
        }

        private void UpdateActionButton(ActionButtonParts parts,bool primary=false)
        {
            var action=parts.action;var button=parts.button;string target=action.operatorTarget?null:selectedNode;
            var preview=State.Preview(action.id,target);bool toggleOff=State.IsAbilityActive(action.id,target);
            bool queue=State.Op.task!=null&&!action.Definition.instant&&!toggleOff&&!action.operatorTarget;
            bool canQueue=queue&&State.CanQueue(action.id,target)&&action.Definition.memoryCost<=State.MemoryFree;
            bool blockedByRunningTask=State.Op.task!=null&&!toggleOff&&!queue&&!(action.Definition.instant&&action.operatorTarget);
            bool enabled=toggleOff?preview.allowed:queue?canQueue:preview.allowed&&!blockedByRunningTask;
            bool alreadyQueued=queue&&State.Op.queue.Any(q=>q.actionId==action.id&&q.target==target);
            string reason=blockedByRunningTask?"Another action is running.":toggleOff?preview.reason:queue&&!canQueue
                ?!action.Definition.queueable?"This action cannot be queued while another action is running."
                :State.Op.queue.Count>=State.QueueCapacity?"Queue full ("+State.Op.queue.Count+" / "+State.QueueCapacity+"). Clear queued actions or wait for the current action."
                :alreadyQueued?"This action is already in the queue."
                :action.Definition.memoryCost>State.MemoryFree?"Cannot queue: not enough free RAM."
                :State.Op.task.actionId==action.id&&State.Op.task.target==target?"This action is already running on the selected target."
                :"Queue requirements are not met for this target.":preview.reason;
            bool memoryBlocked=!toggleOff&&action.Definition.memoryCost>State.MemoryFree&&preview.reason.StartsWith("Not enough free RAM",StringComparison.Ordinal);
            float ramWait=memoryBlocked&&State.MemoryReclaim>0?Mathf.Max(0,action.Definition.memoryCost-State.MemoryFree)/State.MemoryReclaim:0;
            if(memoryBlocked&&State.MemoryFree+State.MemoryReclaiming+0.001f<action.Definition.memoryCost)reason="RAM HELD BY ACTIVE ABILITIES. Disable one to free memory.";
            else if(memoryBlocked&&ramWait>0)reason="RAM reclaiming. Ready in "+ramWait.ToString("0.0")+"s.";
            else if(memoryBlocked&&State.MemoryReclaim<=0)reason="RAM cannot be reclaimed with the current rig.";
            parts.wrapper.tooltip=action.title+"\n"+parts.description+(action.Definition.memoryCost>0?"\nRAM COST / "+action.Definition.memoryCost.ToString("0.#")+" GB":"")+Environment.NewLine+reason;
            button.tooltip=parts.wrapper.tooltip;
            button.SetEnabled(enabled);
            button.EnableInClassList("available-action",enabled);
            button.EnableInClassList("suggested-action",primary&&enabled);
            button.EnableInClassList("risky-action",!queue&&preview.allowed&&preview.willIsolate);
            parts.icon.SetTint(!enabled?Palette.Hex("65818A"):preview.willIsolate?Palette.Amber:ActionGlyph.ColorFor(action.id));
            button.Q<Label>(className:"action-title").text=toggleOff?"OFF / "+action.title:action.title;
            var remaining=State.UsesRemaining(action.id,target);
            string estimate=memoryBlocked?(ramWait>0?"READY IN "+ramWait.ToString("0.0")+"s":reason.StartsWith("RAM HELD",StringComparison.Ordinal)?"RAM HELD":"NO RAM")
                :blockedByRunningTask?"BUSY":queue?canQueue?"QUEUE":State.Op.queue.Any(q=>q.actionId==action.id&&q.target==target)?"IN QUEUE":State.Op.queue.Count>=State.QueueCapacity?"QUEUE FULL":"WAIT":preview.allowed?"READY":"LOCKED";
            if(remaining.HasValue)estimate+=" / "+remaining.Value+" LEFT";
            button.Q<Label>(className:"action-estimate").text=estimate;
            string cost=string.Empty;
            if(toggleOff&&action.Definition.memoryCost>0)cost="FREES "+action.Definition.memoryCost.ToString("0.#")+" GB RAM";
            else if(enabled)
            {
                cost="+"+preview.trace.ToString("0.#")+" TRACE";
                if(action.Definition.memoryCost>0)cost+=" / "+action.Definition.memoryCost.ToString("0.#")+" GB RAM";
            }
            button.Q<Label>(className:"action-cost").text=cost;
        }
        private void Run(string actionId,string targetId)
        {
            if(!State.Execute(actionId,targetId))return;
            Beep();
            networkCamera.FocusedId=null;
            HideTooltip();
            dirty=true;
        }
        private Technology FindTech(string id)=>State.Catalog.Technologies.FirstOrDefault(t=>t.id==id);
        private void DisconnectClicked(){if(State.ObjectiveComplete)State.Disconnect();else{State.SetPaused(true);modal="abort";dirty=true;}}

        private void BuildTraceBar(VisualElement parent)
        {
            var bar=El(parent,"trace-banner");
            Text(bar,"TRACE","eyebrow trace-banner-label");
            traceNumber=Text(bar,"00.0%","trace-number trace-banner-number");
            var track=El(bar,"meter-track trace-banner-track");traceFill=El(track,"meter-fill");
            traceState=Text(bar,"STABLE","micro trace-banner-state");
        }

        private void BuildOperationBar(VisualElement parent)
        {
            // Fixed slots: every control and readout exists for the whole contract, so nothing appears or
            // disappears when a tool runs or the queue changes. Built only while a contract is active.
            var bar=El(parent,"operation-bar");
            var stats=El(bar,"operation-stats");
            var topRow=El(stats,"operation-stat-row");
            var bottomRow=El(stats,"operation-stat-row");
            var tint=Palette.Hex("A8C2BC");
            var cyan=Palette.Hex("60D8D0");
            opPower=OperationStat(topRow,"power",tint,"","micro");
            opCooling=OperationStat(topRow,"cooling",tint,"","micro");
            opCompute=OperationStat(topRow,"processing",cyan,"","micro cyan");
            opBandwidth=OperationStat(topRow,"transfer",cyan,"","micro cyan");
            opStorage=OperationStat(bottomRow,"storage",cyan,"","micro cyan");
            opMemory=OperationStat(bottomRow,"memory",cyan,"","micro cyan");
            opReclaim=OperationStat(bottomRow,"reclaim",cyan,"","micro cyan");
            var controls=El(bar,"time-controls");
            var pause=Btn(controls,State.Active&&State.Op.paused?"▶":"Ⅱ",()=>{if(State.Active)State.SetManualPause(!State.Op.paused);},"pause","time-button");pause.SetEnabled(State.Active);pause.tooltip="Pause / Space";
            var stop=Btn(controls,"STOP",()=>{State.StopTask();dirty=true;},"stop-tool","time-button stop-tool");stop.SetEnabled(State.CanStopTask);stop.tooltip="Stop the current tool and clear its queue. Existing trace remains.";
            var clear=Btn(controls,"CLEAR",()=>{State.ClearQueue();dirty=true;},"clear-queue","time-button clear-tools");clear.SetEnabled(State.Active&&State.Op.queue.Count>0);clear.tooltip="Clear queued tools.";
            foreach(float rate in State.SimulationSpeeds){float captured=rate;var b=Btn(controls,rate+"x",()=>{speed=captured;dirty=true;},"speed-"+rate,"speed-button");if(Mathf.Approximately(speed,rate))b.AddToClassList("active");}
        }

        private void UpdateMeters()
        {
            UpdateOperationStats();
            RefreshResourceRadials();
            if(traceNumber==null)return;
            float trace=State.Active?State.Op.trace:0;Color color=trace>=State.TraceDanger?Palette.Red:trace>=State.TraceWarning?Palette.Amber:Palette.Lime;
            traceNumber.text=trace.ToString("00.0")+"%";traceNumber.style.color=color;traceFill.style.width=Length.Percent(State.TraceFraction*100);traceFill.style.backgroundColor=color;
            string status=!State.Active?"DISCONNECTED":State.Op.decoyRemaining>0?"DECOY "+Mathf.CeilToInt(State.Op.decoyRemaining)+"s":trace>=State.TraceDanger?"LOCKDOWN IMMINENT":State.Scanners>0?State.Scanners+" SCANNER"+(State.Scanners>1?"S":""):"STABLE";
            if(traceState!=null){traceState.text=status;traceState.style.color=color;}
        }

        private void UpdateOperationStats()
        {
            if(opPower==null)return;
            opPower.text=GameplayRichText.Format("POWER / "+State.PowerUsed+" / "+State.PowerLimit);
            opCooling.text=GameplayRichText.Format("COOL / "+State.HeatUsed+" / "+State.CoolingLimit);
            opCompute.text=GameplayRichText.Format("COMPUTE / "+State.Compute.ToString("0.00")+" GFLOP/s");
            opBandwidth.text=GameplayRichText.Format("BW / "+State.Bandwidth.ToString("0.00")+" GB/s");
            opStorage.text=GameplayRichText.Format("STORE / "+State.StorageUsed.ToString("0")+" / "+State.StorageCapacity.ToString("0")+" GB");
            opMemory.text=GameplayRichText.Format("RAM / "+State.MemoryFree.ToString("0.#")+" / "+State.MemoryPool.ToString("0.#")+" GB / RES "+State.MemoryReserved.ToString("0.#")+" GB");
            opReclaim.text=GameplayRichText.Format("RECLAIM / "+State.MemoryReclaiming.ToString("0.#")+" GB / "+State.MemoryReclaim.ToString("0.#")+" GB/s");
        }

        // Branch key shown under the research graph. Hover text describes what each route does.
        private static readonly (string branch, string title, string tip)[] ResearchLegend =
        {
            ("RECON", "RECON", "Recon: see options before committing. Reveals more topology, forecasts objectives and exploits scanned weaknesses."),
            ("INTRUSION", "INTRUSION", "Intrusion: execute a plan efficiently. Faster hacks, longer queues and a shorter main recovery."),
            ("STEALTH", "STEALTH", "Stealth: keep room for optional work. Lower trace, quiet relay entry and route recovery."),
            ("CONTROL", "CONTROL", "Control: change defenses before advancing. Suppress scanners and hubs to open a brief safe window."),
            ("CROSSOVER", "CROSSOVER", "Crossover: requires BOTH connected parent technologies. Combines two branches."),
        };

        // Keeps the inspector and focused node on a technology the player can currently see.
        private string ResearchFocus(string id)
        {
            if(string.IsNullOrEmpty(id))return null;
            if(State.ResearchVisible(id))return id;
            var catalog=State.Catalog;
            return catalog.Technologies.FirstOrDefault(t=>State.ResearchVisible(t.id)&&!State.Has(t.id))?.id
                ?? catalog.Technologies.FirstOrDefault(t=>State.ResearchVisible(t.id))?.id
                ?? catalog.Technologies.First().id;
        }

        private void BuildResearch(VisualElement workspace)
        {
            selectedTech=ResearchFocus(selectedTech);
            var leftShell=El(workspace,"left-panel");
            var loadout=El(leftShell,"research-loadout");
            Text(loadout,"ABILITY LOADOUT","eyebrow");Text(loadout,State.Career.equipped.Count+" / "+State.Slots+" SLOTS USED","small muted section-space");
            if(State.Career.equipped.Count==0)Text(loadout,"Select an unlocked ability on the network to equip it.","small muted");
            foreach(string id in State.Career.equipped.ToArray())
            {
                var loadoutTech=FindTech(id);if(loadoutTech==null)continue;
                var row=El(loadout,"research-loadout-row");
                Btn(row,loadoutTech.title,()=>{selectedTech=id;researchCamera.FocusedId=id;dirty=true;},"loadout-select-"+id,"research-loadout-select");
                Btn(row,"×",()=>{if(State.ToggleEquip(id))Beep();},"loadout-remove-"+id,"research-loadout-remove").tooltip="Unequip "+loadoutTech.title;
            }
            El(leftShell,"flex-spacer");
            var refundArea=El(leftShell,"loadout");
            var refund=Btn(refundArea,"REFUND ALL RESEARCH",()=>{modal="refund-research";dirty=true;},"refund","secondary-button");refund.SetEnabled(!State.Active&&State.ResearchCount>0);Text(refundArea,"Between contracts: "+(State.RefundFraction*100).ToString("0")+"% of research XP is returned.","micro muted");
            // Same column structure as the operations map so the GraphViewport gets a flex-sized render target.
            var mid=El(workspace,"network-panel");var map=El(mid,"map-content");var mh=El(map,"map-heading");var titles=El(mh,"map-title-block");Text(titles,"CONNECTED KNOWLEDGE","eyebrow cyan");Text(titles,"RESEARCH NETWORK","map-title");Text(mh,(State.ResearchCount)+" / "+(State.ResearchTotal)+" TECHNOLOGIES UNLOCKED","micro muted");
            canvas=new GraphViewport(State.Career,selectedTech,SelectTech,researchCamera,State.Catalog);map.Add(canvas);Visuals.AttachField(canvas);Visuals.AttachIntelCard(canvas.IntelStatic);
            var footer=El(map,"map-footer");var legend=El(footer,"legend");
            foreach(var (branch,title,tip) in ResearchLegend){var key=El(legend,"legend-key");key.tooltip=tip;var dot=El(key,"legend-dot");dot.style.backgroundColor=Palette.Branch(branch);Text(key,title,"micro").style.color=Palette.Branch(branch);}
            var controls=El(footer,"map-controls");Btn(controls,"−",()=>canvas.Zoom(1.15f),"zoom-out","map-button");Btn(controls,"+",()=>canvas.Zoom(.85f),"zoom-in","map-button");Btn(controls,"FIT",()=>canvas.ResetView(),"fit","map-button");
            var right=El(workspace,"right-panel");var info=Scroll(right,"tech-inspector");
            Text(info,"TECHNOLOGY INSPECTOR","eyebrow");
            if(string.IsNullOrEmpty(selectedTech))
            {
                Text(info,"Select a technology on the research network.","body-copy muted section-space");
                Text(info,State.Career.equipped.Count+" / "+State.Slots+" special ability slots occupied","small muted section-space");
                if(State.Active)Text(info,"Research changes are available between contracts. Your active operation has been paused.","body-copy amber");
                return;
            }
            var tech=FindTech(selectedTech);if(tech==null)return;
            Text(info,tech.branch+(tech.special?" / EQUIPPED ABILITY":" / PASSIVE"),"micro section-space").style.color=Palette.Branch(tech.branch);
            Text(info,tech.title,"mission-title");Text(info,tech.description,"body-copy");
            Text(info,State.Has(tech.id)?"UNLOCKED":tech.cost+" XP","research-price");
            Text(info,"PREREQUISITES","eyebrow section-space");
            if(tech.parents.Length==0)Text(info,"Included with your gateway.","small muted");
            foreach(var parent in tech.parents)Text(info,(State.Has(parent)?"[+] ":"[ ] ")+FindTech(parent).title,"small "+(State.Has(parent)?"lime":"muted"));
            if(tech.parents.Length>1)Text(info,"BOTH parent technologies required.","micro amber section-space");
            Text(info,"LOADOUT","eyebrow section-space");Text(info,State.Career.equipped.Count+" / "+State.Slots+" special ability slots occupied","small muted");
            foreach(var id in State.Career.equipped)Btn(info,FindTech(id).title+"  ×",()=>State.ToggleEquip(id),"unequip-"+id,"ability-button").SetEnabled(!State.Active);
            El(info,"flex-spacer");
            if(State.Active)Text(info,"Research changes are available between contracts. Your active operation has been paused.","body-copy amber");
            var bottom=El(right,"inspector-bottom");
            if(!State.Has(tech.id))
            {var unlock=Btn(bottom,"UNLOCK / "+tech.cost+" XP",()=>{if(State.BuyTech(tech.id))Beep(true);},"unlock","primary-button");unlock.SetEnabled(!State.Active&&State.Career.xp>=tech.cost&&tech.parents.All(State.Has));}
            else if(tech.special)
            {bool equipped=State.Equipped(tech.id);var equip=Btn(bottom,equipped?"UNEQUIP ABILITY":"EQUIP ABILITY",()=>{State.ToggleEquip(tech.id);Beep();},"equip","primary-button");equip.SetEnabled(!State.Active&&(equipped||State.Career.equipped.Count<State.Slots));}
            else Text(bottom,"[+] ALWAYS ACTIVE","eyebrow lime center-text");
        }

        private void BuildIntel(VisualElement workspace)
        {
            var page=Scroll(workspace,"full-page");Text(page,"THE NETWORK REMEMBERS","eyebrow cyan");Text(page,"Follow the signal.","large-heading");Text(page,"Your contracts leave a mark on the world. Read the consequences, review a run, and connect the dots.","body-copy muted");
            var columns=El(page,"intel-columns");var news=El(columns,"news-feed");Text(news,"WORLD FEED","eyebrow section-space");
            foreach(var item in State.Career.news){var card=El(news,"news-card");Text(card,item.StartsWith("DAY")?"ENCRYPTED BULLETIN":"EXCHANGE DISPATCH","micro cyan");Text(card,item,"body-copy");}
            var reports=El(columns,"reports-feed");Text(reports,"OPERATION ARCHIVE","eyebrow section-space");
            if(State.Career.reports.Count==0)Text(reports,"Your story starts with the first connection. Complete a contract to archive its operation timeline.","body-copy muted");
            foreach(var report in State.Career.reports)
            {var card=Btn(reports,"",()=>{reportToShow=report;modal="archive";dirty=true;},"report-"+State.Career.reports.IndexOf(report),"report-card");Text(card,"DAY "+report.day.ToString("00")+" / "+report.verdict,"micro "+(report.success?"lime":"amber"));Text(card,report.title,"upgrade-name");Text(card,report.trace.ToString("0.0")+"% TRACE  /  "+report.credits+" cr  /  "+report.xp+" XP","small muted");}
            Text(reports,"RECOVERED INTELLIGENCE / "+State.Career.lore.Count,"eyebrow section-space");
            if(State.Career.lore.Count==0)Text(reports,"Recover optional archives and extract successfully to preserve their stories here.","body-copy muted");
            foreach(var fragment in State.Career.lore)
            {
                var card=El(reports,"news-card");Text(card,fragment.company+" / "+fragment.contract,"micro cyan");
                Text(card,fragment.title,"upgrade-name");Text(card,fragment.text,"body-copy");
            }
            var stats=El(page,"career-stats");Text(stats,State.Career.jobs+" CONTRACTS DELIVERED","eyebrow lime");Text(stats,State.Career.totalEarnedXp+" TOTAL XP EARNED","eyebrow cyan");Text(stats,(State.ResearchCount)+" TECHNOLOGIES","eyebrow violet");
        }

        private OperationReport reportToShow;
        private bool pausedBeforeHelp;
        private void ShowHelp(){pausedBeforeHelp=State.Active&&State.Op.paused;if(State.Active)State.SetPaused(true);modal="help";dirty=true;}
        private bool pausedBeforeSettings;
        private string modalBeforeSettings, modalBeforeResetSave="settings", settingsError;
        private void ShowSettings()
        {
            if(modal=="settings"||modal=="reset-save"||modal=="debug"||modal=="refund-research")return;
            modalBeforeSettings=modal;pausedBeforeSettings=State.Active&&State.Op.paused;
            if(State.Active)State.SetPaused(true);
            modal="settings";settingsError=null;dirty=true;
        }
        private void ResumeFromSettings(bool resumeOperation=false)
        {
            modal=modalBeforeSettings;modalBeforeSettings=null;settingsError=null;
            if(State.Active)
            {
                if(resumeOperation)State.SetManualPause(false);
                else State.SetPaused(pausedBeforeSettings);
            }
            dirty=true;
        }
        private void OpenResetSaveConfirm(string returnModal="settings")
        {
            modalBeforeResetSave=returnModal;modal="reset-save";settingsError=null;dirty=true;
        }
        private void CancelResetSave()
        {
            modal=modalBeforeResetSave;modalBeforeResetSave="settings";settingsError=null;dirty=true;
        }
        private void ResetGameSave()
        {
            if(!Testing&&!CareerStorage.Reset())
            {settingsError="The save could not be reset. Check that local storage is writable, then try again.";dirty=true;return;}
            State=new GameSimulation(new Career());
            State.Changed+=()=>{dirty=true;if(!Testing)CareerStorage.Save(State.Career);};
            sessionActive=State.Active;
            selectedJob=State.Catalog.Contracts(State.Career).First(c=>!c.recurring);selectedNode=null;selectedTech=null;view="network";
            networkCamera.Reset();researchCamera.Reset();
            modal=modalBeforeSettings=settingsError=null;modalBeforeResetSave="settings";lastShownReport=reportToShow=null;
            speed=1;saveTimer=0;traceBand=0;dirty=true;
        }
        private void QuitGame()
        {
            if(!Testing&&!CareerStorage.Save(State.Career))
            {settingsError="The save could not be written. Check that local storage is writable, then try again.";dirty=true;return;}
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#else
            Application.Quit();
#endif
        }
        private void BuildModal()
        {
            overlay=El(root,"modal-overlay");var box=Scroll(overlay,"modal-box");
            if(modal=="settings")
            {
                box.AddToClassList("settings-menu");
                Text(box,"WORKSTATION / ESC","eyebrow cyan");Text(box,"Settings","large-heading");
                Text(box,"TEXT SIZE / "+TextSizePercent+"%","eyebrow cyan").name="text-size-value";
                var textSize=new SliderInt("Text size",100,300){name="text-size",showInputField=true};
                textSize.AddToClassList("text-size-slider");textSize.SetValueWithoutNotify(TextSizePercent);box.Add(textSize);
                textSize.RegisterValueChangedCallback(evt=>SetTextSize(evt.newValue));
                Text(box,"100%–300%. Default: 200% (double size). Applies immediately to every screen and is saved automatically.","small muted");
                Text(box,"GRAPH","eyebrow cyan section-space");
                var selectWithoutFocus=new Toggle("Single-click selection does not move the camera"){name="select-without-camera-focus"};
                selectWithoutFocus.SetValueWithoutNotify(SelectWithoutCameraFocus);
                selectWithoutFocus.RegisterValueChangedCallback(evt=>SetSelectWithoutCameraFocus(evt.newValue));
                box.Add(selectWithoutFocus);
                Text(box,"When on, single-click selects and opens detailed intel without moving the camera; double-click also frames the view. When off, single-click selects and frames immediately.","small muted");
                Text(box,"HACKED CONNECTION COLOR","eyebrow cyan section-space");
                var hackedConnectionColor=new TextField("Hacked node links (#RRGGBB)"){name="hacked-connection-color",value="#"+ColorUtility.ToHtmlStringRGB(HackedConnectionColor)};box.Add(hackedConnectionColor);
                var hackedColorFeedback=Text(box,"Links touching systems you control use this color and a stronger stroke.","small muted");
                hackedConnectionColor.RegisterValueChangedCallback(evt=>
                {
                    if(ColorUtility.TryParseHtmlString(evt.newValue,out var color)){SetHackedConnectionColor(color);hackedColorFeedback.text="Saved locally. Links touching systems you control use this color and a stronger stroke.";}
                    else hackedColorFeedback.text="Enter a valid color such as #C5F56B.";
                });
                Text(box,"DISPLAY","eyebrow cyan section-space");
                BuildDisplayEffectToggles(box);
                Text(box,"Toggle each layer independently. Turn off animation to freeze motion while keeping shaders visible. Preferences are saved locally.","small muted");
                Text(box,"AUDIO","eyebrow cyan section-space");
                var soundToggle=new Toggle("Interface sounds"){name="interface-sounds"};
                soundToggle.SetValueWithoutNotify(!muted);
                soundToggle.RegisterValueChangedCallback(evt=>SetMuted(!evt.newValue));
                box.Add(soundToggle);
                Text(box,"UI clicks and trace warnings. Saved locally.","small muted");
                Btn(box,"Resume",()=>ResumeFromSettings(true),"resume","primary-button");
                Btn(box,"Cheats / Debug",()=>{modal="debug";dirty=true;},"debug-menu","secondary-button");
                Btn(box,"Reset Game Save",()=>OpenResetSaveConfirm(),"reset-save","secondary-button");
                Btn(box,"Quit",QuitGame,"quit","danger-button");
                if(settingsError!=null)Text(box,settingsError,"body-copy amber");
                Text(box,"ESC / RESUME","micro muted section-space center-text");
            }
            else if(modal=="debug")
            {
                box.AddToClassList("settings-menu");
                Text(box,"WORKSTATION / DEBUG","eyebrow amber");Text(box,"Cheats / Debug","large-heading");
                Text(box,"Changes apply immediately and autosave to this career. An active operation stays paused while this menu is open.","body-copy muted");
                Text(box,State.Career.credits.ToString("N0")+" CREDITS / "+State.Career.xp.ToString("N0")+" XP","eyebrow cyan section-space");
                Text(box,State.ResearchCount+" / "+State.ResearchTotal+" TECHNOLOGIES UNLOCKED","small muted");
                Btn(box,"Add 1,000,000 credits",()=>{State.DebugAddCredits();Beep();},"debug-credits","secondary-button");
                Btn(box,"Add 1,000,000 XP",()=>{State.DebugAddXp();Beep();},"debug-xp","secondary-button");
                var unlock=Btn(box,"Unlock whole tech tree",()=>{State.DebugUnlockAllTech();Beep();},"debug-unlock-tech","secondary-button");
                unlock.SetEnabled(State.ResearchCount<State.ResearchTotal);
                Text(box,"Technology unlocks are free. Equip special abilities in Research between contracts; loadout slots still apply.","small muted");
                Btn(box,"Reset Game",()=>OpenResetSaveConfirm("debug"),"debug-reset-game","danger-button");
                Text(box,"Same as Settings → Reset Game Save: wipes this career, clears any active operation, and writes a fresh save.","small muted");
                Btn(box,"Back to Settings",()=>{modal="settings";dirty=true;},"debug-back","primary-button");
                Text(box,"ESC / BACK TO SETTINGS","micro muted section-space center-text");
            }
            else if(modal=="reset-save")
            {
                box.AddToClassList("settings-menu");
                Text(box,"RESET GAME SAVE?","eyebrow amber");Text(box,"Start a new career?","large-heading");
                Text(box,"This permanently resets all contracts, credits, research, hardware, reports, and your current operation. This cannot be undone.","body-copy muted");
                Btn(box,"Cancel",CancelResetSave,"cancel-reset","primary-button");
                Btn(box,"Reset Game Save",ResetGameSave,"confirm-reset","danger-button");
                if(settingsError!=null)Text(box,settingsError,"body-copy amber");
            }
            else if(modal=="help")
            {
                Text(box,"OPERATOR FIELD GUIDE","eyebrow cyan");Text(box,"The planet is a network.\nFind your way through.","large-heading");
                string[] guide={"01 / CONNECT — Pick a contract. Gold markers show required objectives. Green means a system is yours.","02 / EXPLORE — Select a reachable system. Scan [1] maps connections and prepares a faster, quieter intrusion. Hack [2] takes control and extends your route.","03 / RECOVER — Control each required target, then recover its files [3]. Record jobs need both copies; investigations need all three records.","04 / MANAGE TRACE — Tools create trace. Check the forecast before committing. STOP cancels a tool; existing trace stays. Planning is free. Space pauses; "+string.Join(" / ",State.SimulationSpeeds.Select(x=>x+"x"))+" changes speed.","05 / GET PAID — Extract & deliver after all objectives are secured. Optional archives pay extra. At "+State.TraceLimit+"% trace the attempt ends; gear and research stay yours.","06 / GROW — Credits buy hardware. XP purchases connected research. Crossovers require both parents. Equip abilities in limited slots. Research refunds are free between jobs."};
                foreach(var line in guide)Text(box,line,"guide-line");
                Text(box,"GRAPH CONTROLS / Click to select and inspect · Double-click to frame the camera (when single-click inspect is enabled) · Scroll to zoom · Left-drag to orbit · Right- or middle-drag to pan · FIT to recenter\nSETTINGS / Text size, graph inspect behavior, display effects, interface sounds, save reset, and quit.\nYour career and active operation autosave locally. Reopened operations start paused.","small muted section-space");
                Btn(box,"BACK TO THE WORKSTATION  →",()=>{modal=null;if(State.Active)State.SetPaused(pausedBeforeHelp);dirty=true;},"close-help","primary-button");
            }
            else if(modal=="abort")
            {
                Text(box,"END THIS CONNECTION?","eyebrow amber");Text(box,"The job isn't secured yet.","large-heading");Text(box,"Disconnecting now ends this attempt without payment. The contract stays available, and your equipment and research remain yours.","body-copy muted");
                Btn(box,"CONTINUE OPERATION",()=>{modal=null;State.SetManualPause(false);dirty=true;},"cancel-abort","primary-button");Btn(box,"DISCONNECT WITHOUT REWARD",()=>{modal=null;State.Disconnect();dirty=true;},"confirm-abort","danger-button");
            }
            else
            {
                var report=modal=="archive"?reportToShow:State.LastReport;if(report==null){modal=null;return;}
                Text(box,"OPERATION / "+report.verdict,"eyebrow "+(report.success?"lime":"amber"));Text(box,report.title,"large-heading");Text(box,report.company+" / DAY "+report.day.ToString("00"),"micro muted");
                var rewards=El(box,"result-rewards");Text(rewards,report.success?"+"+report.credits.ToString("N0")+" cr":"NO REWARD","result-credit");Text(rewards,report.success?"+"+report.xp+" XP":"RETRY AVAILABLE","result-xp");Text(rewards,report.trace.ToString("0.0")+"% TRACE","result-trace");
                ReportBonuses(box,report);
                Text(box,report.narrative,"body-copy");
                Text(box,"POST-OPERATION TIMELINE","eyebrow section-space");
                var timeline=new ScrollView(ScrollViewMode.Vertical);timeline.AddToClassList("result-timeline");box.Add(timeline);foreach(var line in report.log)Text(timeline,line,"terminal-line muted");
                if(report.success&&State.StoryComplete)Text(box,"STORY COMPLETE / Freelance contracts remain available. Keep building your network.","body-copy lime");
                var actions=El(box,"modal-actions");
                bool archived=modal=="archive";
                Btn(actions,archived?"CLOSE ARCHIVE":"BACK TO EXCHANGE",()=>{modal=null;view=archived?"intel":"network";SelectNextContract();dirty=true;},"close-report","secondary-button");
                if(report.success&&modal!="archive")Btn(actions,"OPEN RESEARCH  →",()=>{modal=null;SelectNextContract();Navigate("research");},"report-research","primary-button");
            }
        }

        private void ReportBonuses(VisualElement box,OperationReport report)
        {
            var rewards=State.Catalog.Data.rewards;
            int archiveTotal=report.bonusArchivesTotal>0?report.bonusArchivesTotal:Math.Max(report.bonusFiles,0);
            bool showArchives=archiveTotal>0;
            bool showQuiet=report.quietLimit>0;
            if(!showArchives&&!showQuiet)return;
            Text(box,"CONTRACT BONUSES","eyebrow section-space");
            if(showQuiet)
            {
                string payout="+"+rewards.quietCredits+" cr / +"+rewards.quietXp+" XP";
                string requirement="Below "+report.quietLimit.ToString("0")+"% trace";
                string status;
                if(!report.success)status="NOT PAID  "+requirement+" — "+payout;
                else if(report.quietBonusEarned)status="ACHIEVED  "+requirement+" — "+payout;
                else status="MISSED  "+report.trace.ToString("0.0")+"% trace (need "+requirement+") — "+payout;
                Text(box,"QUIET COMPLETION / "+status,"result-bonus "+(report.success&&report.quietBonusEarned?"lime":"amber"));
            }
            if(showArchives)
            {
                int earnedCr=report.bonusFiles*rewards.archiveCredits;
                int earnedXp=Mathf.Min(rewards.archiveXpCap,report.bonusFiles*rewards.archiveXp);
                int maxCr=archiveTotal*rewards.archiveCredits;
                int maxXp=Mathf.Min(rewards.archiveXpCap,archiveTotal*rewards.archiveXp);
                string payout=report.success&&report.bonusFiles>0?"+"+earnedCr+" cr / +"+earnedXp+" XP":"Up to +"+maxCr+" cr / +"+maxXp+" XP";
                string progress=report.bonusFiles+"/"+archiveTotal+" secured";
                string status=report.success?(report.bonusFiles>0?"ACHIEVED  "+progress+" — "+payout:"MISSED  "+progress+" — "+payout):("NOT PAID  "+progress+" — "+payout);
                Text(box,"OPTIONAL ARCHIVES / "+status,"result-bonus "+(report.success&&report.bonusFiles>0?"lime":"amber"));
            }
        }

        private void SelectNextContract(){selectedJob=State.Catalog.Contracts(State.Career).FirstOrDefault(c=>!State.Career.completed.Contains(c.id)&&State.Career.completed.Count>=c.requiredWins)??State.Catalog.Contracts(State.Career).Last();selectedNode=null;networkCamera.Reset();}
        private string Rank()=>State.Rank;

        private static VisualElement El(VisualElement parent,string classes)
        {var el=new VisualElement();foreach(var c in classes.Split(' '))if(c.Length>0)el.AddToClassList(c);parent.Add(el);return el;}
        private static ScrollView Scroll(VisualElement parent,string classes)
        {var scroll=new ScrollView(ScrollViewMode.Vertical);foreach(var c in classes.Split(' '))if(c.Length>0)scroll.AddToClassList(c);parent.Add(scroll);return scroll;}
        private static Label Text(VisualElement parent,string text,string classes)
        {var label=new Label(text);foreach(var c in classes.Split(' '))if(c.Length>0)label.AddToClassList(c);parent.Add(label);return label;}
        private static Label RichText(VisualElement parent,string text,string classes)
        {var label=new Label(GameplayRichText.Format(text)){enableRichText=true};foreach(var c in classes.Split(' '))if(c.Length>0)label.AddToClassList(c);parent.Add(label);return label;}
        private static void IgnorePointerHits(VisualElement element)
        {
            element.pickingMode=PickingMode.Ignore;
            for(int i=0;i<element.childCount;i++)IgnorePointerHits(element[i]);
        }
        private Button Btn(VisualElement parent,string text,Action action,string name,string classes)
        {
            var button=new Button(action){text=text,name=name};foreach(var c in classes.Split(' '))if(c.Length>0)button.AddToClassList(c);parent.Add(button);uiActions[name]=action;return button;
        }
        private Button RailCircleButton(VisualElement parent,string glyph,Action action,string name,string classes)
        {
            var button=new Button(action){text="",name=name};foreach(var c in classes.Split(' '))if(c.Length>0)button.AddToClassList(c);
            var icon=Text(button,glyph,"rail-help-glyph");icon.pickingMode=PickingMode.Ignore;parent.Add(button);uiActions[name]=action;return button;
        }
        // Used by the editor's in-player smoke test to exercise the actual wired UI callbacks.
        public bool ActivateVisibleControl(string name)
        {
            if(dirty)Build();
            var toggle=root.Q<Toggle>(name);
            if(toggle!=null&&toggle.enabledInHierarchy){toggle.value=!toggle.value;return true;}
            var control=root.Q<Button>(name);if(control==null||!control.enabledInHierarchy)return false;
            if(uiActions.TryGetValue(name,out var action)){action();return true;}return canvas?.ActivateControl(name)==true;
        }
        public void UseTestCareer(Career career)=>UseTestCatalog(GameCatalog.Default,career);
        public void UseTestCatalog(GameCatalog catalog,Career career=null,string contractId=null)
        {
            Testing=true;State=new GameSimulation(catalog,career);State.Changed+=()=>dirty=true;
            sessionActive=State.Active;
            selectedJob=catalog.Contracts(State.Career).First(c=>contractId==null?!c.recurring:c.id==contractId);
            selectedNode=null;selectedTech=null;
            view="network";modal=null;networkCamera.Reset();researchCamera.Reset();dirty=true;
            modalBeforeSettings=settingsError=null;lastShownReport=reportToShow=null;
        }
    }
}
