using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace HackThePlanet
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class WorkstationApp : MonoBehaviour
    {
        public static WorkstationApp Instance { get; private set; }
        public GameSimulation State { get; private set; }
        public string CurrentView => view;
        public VisualElement Root => root;
        public SignalVisuals Visuals { get; private set; }
        public bool Testing { get; set; }
        private VisualElement root, overlay;
        private Label traceNumber, runningLabel, traceState, timeLabel, queueLabel;
        private VisualElement traceFill, taskFill;
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<WorkstationApp>()!=null) return;
            new GameObject("Hack The Planet // Workstation").AddComponent<WorkstationApp>();
        }

        private void Awake()
        {
            Instance=this; Application.targetFrameRate=60; Application.runInBackground=true;
            Exception initializationError=null;
            try {
            State=new GameSimulation(CareerStorage.Load());
            selectedJob=State.Active?State.Job:State.Catalog.Contracts(State.Career).FirstOrDefault(c=>!State.Career.completed.Contains(c.id)&&State.Career.completed.Count>=c.requiredWins)??State.Catalog.Contracts(State.Career).First(c=>!c.recurring);
            if(State.Active)selectedNode=State.Op.lastControlled;
            State.Changed+=()=>{dirty=true;if(!Testing)CareerStorage.Save(State.Career);};
            selectedNode=State.Active?State.Op.lastControlled:State.PreferredNode(selectedJob,true);
            selectedTech=State.Catalog.Technologies.FirstOrDefault(t=>!State.Has(t.id))?.id??State.Catalog.Technologies.First().id;
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
            int band=State.Active?(State.Op.trace>=State.TraceDanger?2:State.Op.trace>=State.TraceWarning?1:0):0;
            if(band>traceBand&&!muted&&audioSource!=null)audioSource.PlayOneShot(warningSound,band==2?1:.5f);
            traceBand=band;
            if(timeLabel!=null&&State.Active)timeLabel.text="SESSION / "+TimeText(State.Op.elapsed);
            if(!State.Active&&State.LastReport!=null&&modal!="report"&&State.LastReport!=lastShownReport)
            {lastShownReport=State.LastReport;modal="report";dirty=true;Beep(State.LastReport.success);}
            if(dirty)Build();
            UpdateMeters();
            canvas?.Tick(State,Visuals.Mode);
            Visuals.Tick(State,view=="research");
            root.EnableInClassList("trace-alert",State.Active&&State.Op.trace>=State.TraceWarning);
            saveTimer+=Time.unscaledDeltaTime;
            if(saveTimer>=5){saveTimer=0;if(State.Active&&!Testing)CareerStorage.Save(State.Career);}
        }
        private OperationReport lastShownReport;
        private void HandleShortcut(KeyDownEvent evt)
        {
            if(State==null)return;
            if(evt.keyCode==KeyCode.Escape)
            {
                if(modal=="settings")ResumeFromSettings();
                else if(modal=="reset-save"){modal="settings";settingsError=null;dirty=true;}
                else if(modal=="help"){modal=null;if(State.Active)State.SetPaused(pausedBeforeHelp);dirty=true;}
                else ShowSettings();
            }
            else if(modal=="help"&&evt.keyCode==KeyCode.F1)
            {
                modal=null;if(State.Active)State.SetPaused(pausedBeforeHelp);dirty=true;
            }
            else if(modal!=null)return;
            else if(evt.keyCode==KeyCode.F1)ShowHelp();
            else if(evt.keyCode==KeyCode.Space&&State.Active)State.SetPaused(!State.Op.paused);
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
            dirty=false;canvas?.Dispose();uiActions.Clear();root.Clear();traceFill=null;taskFill=null;traceNumber=null;runningLabel=null;traceState=null;timeLabel=null;queueLabel=null;canvas=null;
            BuildHeader();
            var body=El(root,"desktop-body");
            BuildRail(body);
            var content=El(body,"desktop-content");
            var workspace=El(content,"workspace-row");
            if(view=="network")BuildNetwork(workspace);
            else if(view=="research")BuildResearch(workspace);
            else if(view=="rig")BuildRig(workspace);
            else BuildIntel(workspace);
            BuildConsole(content);
            var footer=El(root,"footer");Text(footer,"GHOST FREQUENCY / HTP TERMINAL 1.0","footer-brand");
            Text(footer,State.Active?"ENCRYPTED SESSION  /  "+(State.Op.paused?"PAUSED":"LIVE"):"LOCAL SESSION  /  PRIVATE GATEWAY","footer-center");
            Text(footer,CareerStorage.Warning!=null?"SAVE UNAVAILABLE":"AUTOSAVE ACTIVE  /  LOCAL STORAGE","footer-save");
            if(modal=="settings"||modal=="reset-save")
                foreach(var child in root.Children())child.SetEnabled(false);
            if(modal!=null)BuildModal();
            Visuals.AttachGlass(root);
            var modalFocus=modal=="settings"?root.Q<Button>("resume"):modal=="reset-save"?root.Q<Button>("cancel-reset"):null;
            if(modalFocus!=null)modalFocus.Focus();else root.Focus();
        }

        private void BuildHeader()
        {
            var header=El(root,"header");var logo=El(header,"logo");logo.Add(new DeviceGlyph(NodeKind.Gateway,Palette.Lime,42));
            var brand=El(logo,"brand");Text(brand,"HACK THE PLANET","brand-name");Text(brand,"GHOST FREQUENCY  /  INDEPENDENT OPERATOR","brand-sub");
            var tabs=El(header,"header-tabs");
            Tab(tabs,"network","OPERATIONS");Tab(tabs,"research","RESEARCH");Tab(tabs,"rig","YOUR RIG");Tab(tabs,"intel","INTELLIGENCE");
            var wallet=El(header,"wallet");
            var credits=El(wallet,"wallet-cell");Text(credits,"CREDITS","eyebrow");Text(credits,State.Career.credits.ToString("N0")+" cr","credit-amount");
            var xp=El(wallet,"wallet-cell");Text(xp,"RESEARCH XP","eyebrow");Text(xp,State.Career.xp.ToString("N0")+" XP","xp-amount");
            var identity=El(header,"identity");Text(identity,"OP / ZERO","identity-name");Text(identity,"DAY "+State.Career.day.ToString("00")+"  /  "+Rank(),"identity-day");
        }

        private void BuildRail(VisualElement body)
        {
            var rail=El(body,"rail");Text(rail,"H/P","rail-logo");
            foreach(var item in new[]{("network","01"),("research","02"),("rig","03"),("intel","04")})
            {var b=Btn(rail,item.Item2,()=>Navigate(item.Item1),"rail-"+item.Item1,"rail-button");b.tooltip=item.Item1.ToUpperInvariant();if(view==item.Item1)b.AddToClassList("active");}
            El(rail,"flex-spacer");
            var fx=Btn(rail,Visuals.Label,()=>{Visuals.SetMode((SignalEffects)(((int)Visuals.Mode+1)%3),!Testing);dirty=true;},"effects","rail-small effects-button");
            fx.tooltip="Display effects: "+Visuals.Mode+". Click to cycle Full / Reduced (static) / Off.";
            Btn(rail,muted?"OFF":"SND",()=>{muted=!muted;dirty=true;},"sound","rail-small");
            Btn(rail,"ESC",ShowSettings,"settings","rail-small settings-button").tooltip="Settings / Escape";
            Btn(rail,"?",ShowHelp,"help","rail-help").tooltip="Field guide / F1";
        }

        private void Tab(VisualElement parent,string target,string label)
        {var b=Btn(parent,label,()=>Navigate(target),"tab-"+target,"tab");if(view==target)b.AddToClassList("active");}
        public void Navigate(string target)
        { if(State.Active&&target!="network")State.SetPaused(true);view=target;modal=null;dirty=true;Beep(); }

        private void BuildNetwork(VisualElement workspace)
        {
            var left=El(workspace,"left-panel");
            var lh=El(left,"panel-heading");Text(lh,State.Active?"ACTIVE CONTRACT":"CONTRACT EXCHANGE","eyebrow");Text(lh,"VERIFIED / ESCROW","micro muted");
            if(!State.Active)
            {
                var list=new ScrollView(ScrollViewMode.Vertical);list.AddToClassList("contract-list");left.Add(list);
                foreach(var job in State.Catalog.Contracts(State.Career))
                {
                    bool done=State.Career.completed.Contains(job.id),locked=State.Career.completed.Count<job.requiredWins;
                    var card=Btn(list,"",()=>{canvas?.Dispose();selectedJob=job;selectedNode=State.PreferredNode(selectedJob,true);networkCamera.Reset();dirty=true;Beep();},"contract-"+job.id,"contract-card");
                    if(selectedJob.id==job.id)card.AddToClassList("active");if(locked)card.AddToClassList("locked");
                    var meta=El(card,"row-between");Text(meta,done?"DELIVERED":locked?"ENCRYPTED":"AVAILABLE","contract-state"+(done?" lime":locked?" muted":" cyan"));Text(meta,job.recurring?"R":"0"+(State.Catalog.Contracts(State.Career).Where(c=>!c.recurring).Select(c=>c.id).ToList().IndexOf(job.id)+1),"micro muted");
                    Text(card,job.title,"contract-title");Text(card,job.company,"micro muted");
                    var reward=El(card,"contract-reward");Text(reward,job.credits.ToString("N0")+" cr","small lime");Text(reward,"+"+job.xp+" XP","small muted");
                }
            }
            else
            {
                var brief=El(left,"active-brief");Text(brief,State.Job.tag,"micro cyan");Text(brief,State.Job.title,"mission-title");Text(brief,State.Job.client,"micro muted");
                Btn(brief,expandedBrief?"HIDE BRIEF ▴":"READ BRIEF ▾",()=>{expandedBrief=!expandedBrief;dirty=true;},"toggle-brief","brief-toggle");if(expandedBrief)Text(brief,State.Job.description,"body-copy");Text(brief,"OBJECTIVES","eyebrow section-space");
                foreach(var n in State.Op.nodes.Where(n=>n.objective))
                {var b=Btn(brief,(State.ObjectiveDone(n)?"[+] ":"[ ] ")+n.label,()=>SelectNode(n.id),"objective-"+n.id,"objective-row");if(State.ObjectiveDone(n))b.AddToClassList("lime");}
                Text(brief,"REWARD CONDITIONS","eyebrow section-space");Text(brief,State.Job.credits+" cr + "+State.Job.xp+" XP on delivery","small lime");
                Text(brief,State.RewardDescription,"small muted");

            }
            var loadout=El(left,"loadout");Text(loadout,"EQUIPPED ABILITIES  /  "+State.Career.equipped.Count+" OF "+State.Slots,"eyebrow");
            if(State.Career.equipped.Count==0)Text(loadout,"Research passives for lasting upgrades, or equip an ability for a new approach.","small muted");
            foreach(var id in State.Career.equipped)
            {
                var tech=FindTech(id);
                var operatorActions=State.Active?State.AvailableActions().Where(a=>tech.grants.Contains(a.id)).ToArray():Array.Empty<ActionInfo>();
                if(operatorActions.Length>0)foreach(var action in operatorActions)
                {
                    var abilityPreview=State.Preview(action.id);var button=Btn(loadout,action.title+(abilityPreview.allowed?" / ACTIVATE":" / USED"),()=>Run(action.id,null),"ability-"+id,"ability-button");button.SetEnabled(abilityPreview.allowed);
                }
                else Btn(loadout,tech.title+" / READY",()=>{selectedTech=id;Navigate("research");},"ability-"+id,"ability-button").SetEnabled(!State.Active);
            }
            var mid=El(workspace,"network-panel");var mh=El(mid,"map-heading");var title=El(mh,"map-title-block");
            Text(title,State.Active?"LIVE NETWORK":"NETWORK INTELLIGENCE","eyebrow cyan");Text(title,(State.Active?State.Job:selectedJob).company,"map-title");
            var state=El(mh,"map-status");Text(state,State.Active?(State.Op.paused?"||  SIMULATION PAUSED":"●  ENCRYPTED CONNECTION"):"○  OFFLINE SCHEMATIC","micro "+(State.Active?"lime":"muted"));
            Text(state,State.Active?State.Op.nodes.Count(n=>n.known)+" / "+State.Op.nodes.Count+" SYSTEMS DISCOVERED":"SELECT A CONTRACT TO BEGIN","micro muted");
            var signal=El(mid,"signal-strip");Text(signal,"GHOST / FREQUENCY","micro");Text(signal,State.Active?"RX / ENCRYPTED UPLINK":"RX / PASSIVE INTERCEPT","micro");Text(signal,"CHANNEL 01.337","micro");
            var sectors=El(mid,"sectors");Text(sectors,"01 / PUBLIC EDGE","sector");Text(sectors,"02 / INTERNAL SERVICES","sector");Text(sectors,"03 / SECURE CORE","sector");
            var preview=State.Active?null:NetworkGenerator.Generate(selectedJob,State.Career,State.Catalog);
            var nodes=State.Active?State.Op.nodes:preview.nodes;
            var links=State.Active?State.Op.links:preview.links;
            var route=State.Active?State.Route(selectedNode,true):null;
            canvas=new GraphViewport(nodes,links,selectedNode,!State.Active,SelectNode,route,networkCamera);mid.Add(canvas);Visuals.AttachField(canvas);
            var mapFooter=El(mid,"map-footer");
            var legend=El(mapFooter,"legend");foreach(var kind in State.Catalog.NodeTypes.Where(t=>nodes.Any(n=>n.typeId==t.id)&&t.glyph!=NodeKind.Gateway))
            {var key=El(legend,"legend-key");var dot=El(key,"legend-dot");dot.style.backgroundColor=kind.color;Text(key,kind.title,"micro muted");}
            var controls=El(mapFooter,"map-controls");Btn(controls,"−",()=>canvas.Zoom(1.15f),"zoom-out","map-button");Btn(controls,"+",()=>canvas.Zoom(.85f),"zoom-in","map-button");Btn(controls,"FIT",()=>canvas.ResetView(),"fit","map-button");
            BuildTraceBar(mid);
            var right=El(workspace,"right-panel");
            if(State.Active)BuildTarget(right,State.Node(selectedNode)??State.Node(State.Op.entryNode));
            else BuildBrief(right);
        }

        public void SelectNode(string id){selectedNode=id;dirty=true;Beep();}
        public void SelectTech(string id){selectedTech=id;dirty=true;Beep();}

        private void BuildBrief(VisualElement right)
        {
            var scroll=new ScrollView(ScrollViewMode.Vertical);scroll.AddToClassList("inspector-scroll");right.Add(scroll);
            Text(scroll,"CONTRACT BRIEF","eyebrow");Text(scroll,selectedJob.tag,"micro cyan section-space");Text(scroll,selectedJob.title,"mission-title");
            Text(scroll,selectedJob.client,"micro muted");Text(scroll,selectedJob.description,"body-copy");
            Text(scroll,"THE ASK","eyebrow section-space");Text(scroll,selectedJob.objective,"body-copy");
            Text(scroll,"ESCROW PAYOUT","eyebrow section-space");Text(scroll,selectedJob.credits.ToString("N0")+" cr  /  "+selectedJob.xp+" XP","payout");
            Text(scroll,selectedJob.condition,"small muted");Text(scroll,"OPTIONAL ARCHIVES","eyebrow section-space");Text(scroll,State.ArchiveDescription,"small muted");
            Text(scroll,"OPERATOR NOTE","eyebrow section-space");Text(scroll,State.Career.jobs==0?"Your gateway is already controlled. Hack a connected relay to get inside, or scan it to discover more routes. Pause whenever you need to think.":"Scanners add pressure while you work near them. A captured hub weakens the systems it protects. Your old successes may have changed these defenses.","body-copy note");
            var bottom=El(right,"inspector-bottom");bool done=State.Career.completed.Contains(selectedJob.id),locked=State.Career.completed.Count<selectedJob.requiredWins;
            var start=Btn(bottom,done?"CONTRACT DELIVERED":locked?"COMPLETE EARLIER CONTRACTS":"CONNECT TO NETWORK  →",StartSelected,"connect","primary-button");start.SetEnabled(!done&&!locked);
            Text(bottom,"No time limit. Plan freely. Disconnect to bank rewards.","micro muted center-text");
        }

        public void StartSelected()
        {
            if(State.Start(selectedJob)){canvas?.Dispose();selectedNode=State.PreferredNode(selectedJob);view="network";networkCamera.Reset();Beep();dirty=true;}
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
            if(node.suppressed>0)Text(scroll,"SUPPRESSED / "+Mathf.CeilToInt(node.suppressed)+"s remaining","micro lime section-space");
            if(node.scanned)Text(scroll,"INTEL VERIFIED / INTRUSION PREPARED","micro cyan section-space");
            Text(scroll,"ACTIONS","eyebrow section-space");
            var options=State.AvailableActions(node.id).OrderByDescending(action=>State.Preview(action.id,node.id).allowed||State.CanQueue(action.id,node.id)).ThenBy(action=>action.priority).ToArray();
            bool hasAccess=State.Reachable(node)||options.Any(action=>State.Preview(action.id,node.id).allowed||State.CanQueue(action.id,node.id));
            if(!hasAccess)Text(scroll,"Secure a connected system to reach this target.","body-copy muted");
            else
            {
                bool primary=true;
                foreach(var action in options){ActionButton(scroll,action,primary);if(State.Preview(action.id,node.id).allowed)primary=false;}
                if(options.Length==0)Text(scroll,"This system is secured. Choose a connected target.","body-copy muted");
            }
            var researchHint=State.ActionResearchHint(node.id);if(researchHint!=null)Text(scroll,researchHint,"small muted section-space");
            Text(scroll,node.detail,"body-copy muted");
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
            var preview=State.Preview(action.id,selectedNode);bool queue=State.Op.task!=null;
            var button=Btn(parent,"",()=>Run(action.id,selectedNode),"action-"+action.id,"action-button");
            bool enabled=queue?State.CanQueue(action.id,selectedNode):preview.allowed;
            button.EnableInClassList("suggested-action",primary&&enabled);button.EnableInClassList("risky-action",!queue&&preview.allowed&&preview.willIsolate);
            var row=El(button,"row-between");Text(row,State.ActionTitle(action.id,selectedNode),"action-title");Text(row,action.shortcutLabel,"keycap");
            var remaining=State.UsesRemaining(action.id,selectedNode);
            float displayedTrace=queue?preview.trace:Mathf.Max(0,preview.projectedTrace-State.Op.trace);
            string estimate=preview.allowed?preview.duration.ToString("0.0")+"s  /  +"+displayedTrace.ToString("0.0")+" trace":preview.reason;
            if(remaining.HasValue)estimate+="  /  "+remaining.Value+" left";
            Text(button,estimate,"action-estimate");
            if(!queue&&preview.allowed)
            {
                string forecast=preview.willIsolate?"RISK / TRACE LIMIT":preview.losesQuietBonus?"CROSSES QUIET BONUS": "UP TO "+Mathf.Min(State.TraceLimit,preview.projectedTrace).ToString("0.0")+"% TRACE";
                Text(button,forecast,"action-forecast "+(preview.willIsolate?"amber":preview.losesQuietBonus?"amber":"muted"));
            }
            button.tooltip=action.description+Environment.NewLine+preview.reason;button.SetEnabled(enabled);
        }
        private void Run(string actionId,string targetId){if(State.Execute(actionId,targetId)){Beep();dirty=true;}}
        private Technology FindTech(string id)=>State.Catalog.Technologies.FirstOrDefault(t=>t.id==id);
        private void DisconnectClicked(){if(State.ObjectiveComplete)State.Disconnect();else{State.SetPaused(true);modal="abort";dirty=true;}}

        private void BuildTraceBar(VisualElement parent)
        {
            var bar=El(parent,"operation-bar");
            var trace=El(bar,"trace-block");var heading=El(trace,"row-between");Text(heading,"TRACE LEVEL","eyebrow");traceNumber=Text(heading,"00.0%","trace-number");
            var track=El(trace,"meter-track");traceFill=El(track,"meter-fill");traceState=Text(trace,"No active connection","micro muted");
            var task=El(bar,"task-block");runningLabel=Text(task,"YOUR NEXT CONNECTION STARTS HERE","task-title");var taskTrack=El(task,"task-track");taskFill=El(taskTrack,"task-fill");
            queueLabel=Text(task,"","queue-summary");
            var controls=El(bar,"time-controls");
            var pause=Btn(controls,State.Active&&State.Op.paused?"▶":"Ⅱ",()=>{if(State.Active)State.SetPaused(!State.Op.paused);},"pause","time-button");pause.SetEnabled(State.Active);pause.tooltip="Pause / Space";
            if(State.CanStopTask)Btn(controls,"STOP",()=>State.StopTask(),"stop-tool","time-button stop-tool").tooltip="Stop the current tool and clear its queue. Existing trace remains.";
            if(State.Active&&State.Op.queue.Count>0)Btn(controls,"CLEAR",()=>State.ClearQueue(),"clear-queue","time-button clear-tools").tooltip="Clear queued tools.";
            foreach(float rate in State.SimulationSpeeds){float captured=rate;var b=Btn(controls,rate+"x",()=>{speed=captured;dirty=true;},"speed-"+rate,"speed-button");if(Mathf.Approximately(speed,rate))b.AddToClassList("active");}
        }

        private void UpdateMeters()
        {
            if(traceNumber==null)return;
            if(queueLabel!=null)queueLabel.text=!State.Active||State.Op.queue.Count==0?"PLANNING IS FREE / SPACE TO PAUSE":"NEXT / "+string.Join(" → ",State.Op.queue.Take(2).Select(q=>State.ActionTitle(q.actionId,q.target)+" · "+State.Node(q.target).label))+(State.Op.queue.Count>2?"  +"+(State.Op.queue.Count-2):"");
            float trace=State.Active?State.Op.trace:0;Color color=trace>=State.TraceDanger?Palette.Red:trace>=State.TraceWarning?Palette.Amber:Palette.Lime;
            traceNumber.text=trace.ToString("00.0")+"%";traceNumber.style.color=color;traceFill.style.width=Length.Percent(State.TraceFraction*100);traceFill.style.backgroundColor=color;
            traceState.text=!State.Active?"DISCONNECTED / NO EXPOSURE":State.Op.decoyRemaining>0?"DECOY ACTIVE / "+Mathf.CeilToInt(State.Op.decoyRemaining)+"s":trace>=State.TraceDanger?"LOCKDOWN IMMINENT / FINISH OR DISCONNECT":State.Scanners>0?State.Scanners+" ACTIVE SCANNER"+(State.Scanners>1?"S":"")+" / EXTRA TRACE WHILE TOOLS RUN":"STABLE / PLANNING DOES NOT RAISE TRACE";
            if(State.Active&&State.Op.task!=null){runningLabel.text=(State.Op.paused?"PAUSED / ":"RUNNING / ")+State.ActionTitle(State.Op.task.actionId,State.Op.task.target)+" : "+(State.Node(State.Op.task.target)?.label??"operator")+"  "+(State.Progress*100).ToString("0")+"%";taskFill.style.width=Length.Percent(State.Progress*100);}
            else{runningLabel.text=State.Active?(State.Op.paused?"PAUSED / QUEUE YOUR NEXT MOVE":"IDLE / SELECT A SYSTEM AND CHOOSE AN ACTION"):"YOUR NEXT CONNECTION STARTS HERE";taskFill.style.width=0;}
        }

        private void BuildResearch(VisualElement workspace)
        {
            var leftShell=El(workspace,"left-panel");var left=new ScrollView(ScrollViewMode.Vertical);left.AddToClassList("research-intro");leftShell.Add(left);
            Text(left,"RESEARCH NETWORK","eyebrow cyan");Text(left,"Build your\nown way in.","large-heading");
            Text(left,"Spend contract XP directly on connected technology. Follow a branch or mix tools across the network.","body-copy muted");
            Text(left,State.Career.xp+" XP","research-balance");Text(left,"AVAILABLE TO SPEND","micro muted");
            foreach(var branch in new[]{("RECON","Discover better routes."),("INTRUSION","Work faster. Reach further."),("STEALTH","Leave less of a signal."),("CONTROL","Make defenses work for you.")})
            {var b=El(left,"branch-description");Text(b,branch.Item1,"eyebrow").style.color=Palette.Branch(branch.Item1);Text(b,branch.Item2,"small muted");}
            El(left,"flex-spacer");Text(left,"Crossover technologies require BOTH connected parent nodes. Passives are always active. Abilities use loadout slots.","small muted");
            var refundArea=El(leftShell,"loadout");
            var refund=Btn(refundArea,"REFUND ALL RESEARCH",()=>{State.RefundResearch();Beep();},"refund","secondary-button");refund.SetEnabled(!State.Active&&State.ResearchCount>0);Text(refundArea,"Between contracts: "+(State.RefundFraction*100).ToString("0")+"% of research XP is returned.","micro muted");
            var mid=El(workspace,"network-panel");var mh=El(mid,"map-heading");var titles=El(mh,"map-title-block");Text(titles,"CONNECTED KNOWLEDGE","eyebrow cyan");Text(titles,"RESEARCH NETWORK","map-title");Text(mh,(State.ResearchCount)+" / "+(State.ResearchTotal)+" TECHNOLOGIES UNLOCKED","micro muted");
            canvas=new GraphViewport(State.Career,selectedTech,SelectTech,researchCamera,State.Catalog);mid.Add(canvas);Visuals.AttachField(canvas);
            var footer=El(mid,"map-footer");Text(footer,"SOLID / UNLOCKED    ·    DIM / PREREQUISITE REQUIRED    ·    SELECT A NODE TO INSPECT","micro muted");
            var controls=El(footer,"map-controls");Btn(controls,"−",()=>canvas.Zoom(1.15f),"zoom-out","map-button");Btn(controls,"+",()=>canvas.Zoom(.85f),"zoom-in","map-button");Btn(controls,"FIT",()=>canvas.ResetView(),"fit","map-button");
            var right=El(workspace,"right-panel");var info=El(right,"tech-inspector");var tech=FindTech(selectedTech)??State.Catalog.Technologies.First();
            Text(info,"TECHNOLOGY INSPECTOR","eyebrow");Text(info,tech.branch+(tech.special?" / EQUIPPED ABILITY":" / PASSIVE"),"micro section-space").style.color=Palette.Branch(tech.branch);
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

        private void BuildRig(VisualElement workspace)
        {
            var page=El(workspace,"full-page");var heading=El(page,"page-heading");Text(heading,"YOUR HARDWARE","eyebrow cyan");Text(heading,"Small rig. Big possibilities.","large-heading");Text(heading,"Credits buy room to operate. Research decides how you use it.","body-copy muted");
            var rig=El(page,"rig-layout");var art=El(rig,"rig-art");art.Add(new DeviceGlyph(NodeKind.Workstation,Palette.Lime,136));Text(art,"GATEWAY / ZERO","rig-name");Text(art,"PRIVATE NODE · ENCRYPTED UPLINK","micro muted");
            var specs=El(art,"rig-specs");
            foreach(var hardware in State.Catalog.Hardware)Text(specs,hardware.category+"   "+State.Stat(hardware.displayedStat).ToString("0.##")+" "+hardware.statLabel,"small cyan");
            var upgrades=new ScrollView(ScrollViewMode.Vertical);upgrades.AddToClassList("upgrade-list");upgrades.contentContainer.style.flexGrow=1;rig.Add(upgrades);int index=0;
            foreach(var hardware in State.Catalog.Hardware)
            {
                int level=State.Career.Level(hardware.id),max=hardware.maxLevel;
                var card=El(upgrades,"upgrade-card");card.style.flexShrink=0;var summary=El(card,"upgrade-summary");Text(summary,hardware.category+" / TIER "+level+" OF "+max,"eyebrow cyan");Text(summary,hardware.title,"upgrade-name");Text(summary,hardware.description,"body-copy muted");
                var purchase=El(card,"upgrade-purchase");Text(purchase,level==max?"MAXIMUM":State.UpgradePrice(hardware.id).ToString("N0")+" cr","payout");
                var button=Btn(purchase,level==max?"INSTALLED":"INSTALL UPGRADE",()=>{if(State.Upgrade(hardware.id))Beep(true);},"upgrade-"+index++,"primary-button");var upgradePreview=State.PreviewUpgrade(hardware.id);button.SetEnabled(upgradePreview.allowed);button.tooltip=upgradePreview.reason;
                if(!upgradePreview.allowed&&level<max)Text(summary,upgradePreview.reason,"small muted");
            }
            Text(page,State.Active?"The operation is paused. Hardware can be installed after you disconnect.":"Hardware persists through failed contracts. Every upgrade is immediately available on your next connection.","small muted section-space");
        }

        private void BuildIntel(VisualElement workspace)
        {
            var page=El(workspace,"full-page");Text(page,"THE NETWORK REMEMBERS","eyebrow cyan");Text(page,"Follow the signal.","large-heading");Text(page,"Your contracts leave a mark on the world. Read the consequences, review a run, and connect the dots.","body-copy muted");
            var columns=El(page,"intel-columns");var news=new ScrollView();news.AddToClassList("news-feed");columns.Add(news);Text(news,"WORLD FEED","eyebrow section-space");
            foreach(var item in State.Career.news){var card=El(news,"news-card");Text(card,item.StartsWith("DAY")?"ENCRYPTED BULLETIN":"EXCHANGE DISPATCH","micro cyan");Text(card,item,"body-copy");}
            var reports=new ScrollView();reports.AddToClassList("reports-feed");columns.Add(reports);Text(reports,"OPERATION ARCHIVE","eyebrow section-space");
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

        private void BuildConsole(VisualElement content)
        {
            var console=El(content,"console");var left=El(console,"console-main");var heading=El(left,"console-heading");Text(heading,"SESSION TERMINAL","eyebrow");Text(heading,State.Active?"LIVE FEED":"LOCAL / ZERO","micro cyan");
            var lines=State.Active?State.Op.log:State.Career.reports.FirstOrDefault()?.log;
            if(lines==null||lines.Count==0)
            {Text(left,"00:00  SYSTEM // Gateway online. Identity masked. The exchange is listening.","terminal-line");Text(left,"00:00  TIP // Contracts earn credits + XP. Credits upgrade your rig. XP grows your research network.","terminal-line muted");Text(left,"00:00  READY // Select The Quiet Copy and connect to your first network.","terminal-line lime");}
            else foreach(var line in lines.Skip(Math.Max(0,lines.Count-3)))Text(left,line,"terminal-line"+(line.Contains("OBJECTIVE")?" lime":line.Contains("RUN //")?" cyan":" muted"));
            var side=El(console,"console-side");Text(side,"OPERATOR STATUS","eyebrow");
            timeLabel=Text(side,State.Active?"SESSION / "+TimeText(State.Op.elapsed):"STANDING / "+Rank(),"small cyan");
            if(State.Active){Text(side,"QUEUE / "+State.Op.queue.Count+" ACTIONS","small muted");}
            else Text(side,"UPLINK / STANDBY","small muted");
        }

        private OperationReport reportToShow;
        private bool pausedBeforeHelp;
        private void ShowHelp(){pausedBeforeHelp=State.Active&&State.Op.paused;if(State.Active)State.SetPaused(true);modal="help";dirty=true;}
        private bool pausedBeforeSettings;
        private string modalBeforeSettings, settingsError;
        private void ShowSettings()
        {
            if(modal=="settings"||modal=="reset-save")return;
            modalBeforeSettings=modal;pausedBeforeSettings=State.Active&&State.Op.paused;
            if(State.Active)State.SetPaused(true);
            modal="settings";settingsError=null;dirty=true;
        }
        private void ResumeFromSettings()
        {
            modal=modalBeforeSettings;modalBeforeSettings=null;settingsError=null;
            if(State.Active)State.SetPaused(pausedBeforeSettings);
            dirty=true;
        }
        private void ResetGameSave()
        {
            if(!Testing&&!CareerStorage.Reset())
            {settingsError="The save could not be reset. Check that local storage is writable, then try again.";dirty=true;return;}
            State=new GameSimulation(new Career());
            State.Changed+=()=>{dirty=true;if(!Testing)CareerStorage.Save(State.Career);};
            selectedJob=State.Catalog.Contracts(State.Career).First(c=>!c.recurring);selectedNode=State.PreferredNode(selectedJob,true);selectedTech=State.Catalog.Technologies.FirstOrDefault(t=>!State.Has(t.id))?.id??State.Catalog.Technologies.First().id;view="network";
            canvas?.Dispose();networkCamera.Reset();researchCamera.Reset();
            modal=modalBeforeSettings=settingsError=null;lastShownReport=reportToShow=null;
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
            overlay=El(root,"modal-overlay");var box=El(overlay,"modal-box");
            if(modal=="settings")
            {
                box.AddToClassList("settings-menu");
                Text(box,"WORKSTATION / ESC","eyebrow cyan");Text(box,"Settings","large-heading");
                Btn(box,"Resume",ResumeFromSettings,"resume","primary-button");
                Btn(box,"Reset Game Save",()=>{modal="reset-save";settingsError=null;dirty=true;},"reset-save","secondary-button");
                Btn(box,"Quit",QuitGame,"quit","danger-button");
                if(settingsError!=null)Text(box,settingsError,"body-copy amber");
                Text(box,"ESC / RESUME","micro muted section-space center-text");
            }
            else if(modal=="reset-save")
            {
                box.AddToClassList("settings-menu");
                Text(box,"RESET GAME SAVE?","eyebrow amber");Text(box,"Start a new career?","large-heading");
                Text(box,"This permanently resets all contracts, credits, research, hardware, reports, and your current operation. This cannot be undone.","body-copy muted");
                Btn(box,"Cancel",()=>{modal="settings";settingsError=null;dirty=true;},"cancel-reset","primary-button");
                Btn(box,"Reset Game Save",ResetGameSave,"confirm-reset","danger-button");
                if(settingsError!=null)Text(box,settingsError,"body-copy amber");
            }
            else if(modal=="help")
            {
                Text(box,"OPERATOR FIELD GUIDE","eyebrow cyan");Text(box,"The planet is a network.\nFind your way through.","large-heading");
                string[] guide={"01 / CONNECT — Pick a contract. Gold markers show required objectives. Green means a system is yours.","02 / EXPLORE — Select a reachable system. Scan [1] maps connections and prepares a faster, quieter intrusion. Hack [2] takes control and extends your route.","03 / RECOVER — Control each required target, then recover its files [3]. Record jobs need both copies; investigations need all three records.","04 / MANAGE TRACE — Tools create trace. Check the forecast before committing. STOP cancels a tool; existing trace stays. Planning is free. Space pauses; "+string.Join(" / ",State.SimulationSpeeds.Select(x=>x+"x"))+" changes speed.","05 / GET PAID — Extract & deliver after all objectives are secured. Optional archives pay extra. At "+State.TraceLimit+"% trace the attempt ends; gear and research stay yours.","06 / GROW — Credits buy hardware. XP purchases connected research. Crossovers require both parents. Equip abilities in limited slots. Research refunds are free between jobs."};
                foreach(var line in guide)Text(box,line,"guide-line");
                Text(box,"GRAPH CONTROLS / Click to inspect · Scroll to zoom · Drag empty space to pan · FIT to recenter\nDISPLAY / FX+ on the left rail cycles Full / Reduced (static) / Off. Display preference is saved locally.\nYour career and active operation autosave locally. Reopened operations start paused.","small muted section-space");
                Btn(box,"BACK TO THE WORKSTATION  →",()=>{modal=null;if(State.Active)State.SetPaused(pausedBeforeHelp);dirty=true;},"close-help","primary-button");
            }
            else if(modal=="abort")
            {
                Text(box,"END THIS CONNECTION?","eyebrow amber");Text(box,"The job isn't secured yet.","large-heading");Text(box,"Disconnecting now ends this attempt without payment. The contract stays available, and your equipment and research remain yours.","body-copy muted");
                Btn(box,"CONTINUE OPERATION",()=>{modal=null;State.SetPaused(false);dirty=true;},"cancel-abort","primary-button");Btn(box,"DISCONNECT WITHOUT REWARD",()=>{modal=null;State.Disconnect();dirty=true;},"confirm-abort","danger-button");
            }
            else
            {
                var report=modal=="archive"?reportToShow:State.LastReport;if(report==null){modal=null;return;}
                Text(box,"OPERATION / "+report.verdict,"eyebrow "+(report.success?"lime":"amber"));Text(box,report.title,"large-heading");Text(box,report.company+" / DAY "+report.day.ToString("00"),"micro muted");
                var rewards=El(box,"result-rewards");Text(rewards,report.success?"+"+report.credits.ToString("N0")+" cr":"NO REWARD","result-credit");Text(rewards,report.success?"+"+report.xp+" XP":"RETRY AVAILABLE","result-xp");Text(rewards,report.trace.ToString("0.0")+"% TRACE","result-trace");
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

        private void SelectNextContract(){selectedJob=State.Catalog.Contracts(State.Career).FirstOrDefault(c=>!State.Career.completed.Contains(c.id)&&State.Career.completed.Count>=c.requiredWins)??State.Catalog.Contracts(State.Career).Last();selectedNode=State.PreferredNode(selectedJob,true);}
        private string Rank()=>State.Rank;
        private static string TimeText(float seconds)=>((int)seconds/60).ToString("00")+":"+((int)seconds%60).ToString("00");

        private static VisualElement El(VisualElement parent,string classes)
        {var el=new VisualElement();foreach(var c in classes.Split(' '))if(c.Length>0)el.AddToClassList(c);parent.Add(el);return el;}
        private static Label Text(VisualElement parent,string text,string classes)
        {var label=new Label(text);foreach(var c in classes.Split(' '))if(c.Length>0)label.AddToClassList(c);parent.Add(label);return label;}
        private Button Btn(VisualElement parent,string text,Action action,string name,string classes)
        {
            var button=new Button(action){text=text,name=name};foreach(var c in classes.Split(' '))if(c.Length>0)button.AddToClassList(c);parent.Add(button);uiActions[name]=action;return button;
        }

        // Used by the editor's in-player smoke test to exercise the actual wired UI callbacks.
        public bool ActivateVisibleControl(string name)
        {
            var control=root.Q<Button>(name);if(control==null||!control.enabledInHierarchy||!uiActions.TryGetValue(name,out var action))return false;
            action();return true;
        }
        public void UseTestCareer(Career career)=>UseTestCatalog(GameCatalog.Default,career);
        public void UseTestCatalog(GameCatalog catalog,Career career=null,string contractId=null)
        {
            Testing=true;State=new GameSimulation(catalog,career);State.Changed+=()=>dirty=true;
            selectedJob=catalog.Contracts(State.Career).First(c=>contractId==null?!c.recurring:c.id==contractId);
            selectedNode=State.PreferredNode(selectedJob);selectedTech=catalog.Technologies.FirstOrDefault(t=>!State.Has(t.id))?.id??catalog.Technologies.First().id;
            view="network";modal=null;networkCamera.Reset();researchCamera.Reset();dirty=true;
            modalBeforeSettings=settingsError=null;lastShownReport=reportToShow=null;
        }
    }
}
