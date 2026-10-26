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
        private Label traceNumber, runningLabel, traceState, timeLabel;
        private VisualElement traceFill, taskFill;
        private GraphViewport canvas;
        private Contract selectedJob;
        private string selectedNode="n22", selectedTech="wide", view="network", modal;
        private readonly Dictionary<string, Action> uiActions=new Dictionary<string, Action>();
        private readonly GraphCameraState networkCamera=new GraphCameraState(), researchCamera=new GraphCameraState();
        private bool dirty=true, muted;
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
            State=new GameSimulation(CareerStorage.Load());
            selectedJob=State.Active?State.Job:GameContent.Contracts(State.Career).FirstOrDefault(c=>!State.Career.completed.Contains(c.id)&&State.Career.completed.Count>=c.requiredWins)??GameContent.Story[0];
            if(State.Active)selectedNode=State.Op.lastControlled;
            State.Changed+=()=>{dirty=true;if(!Testing)CareerStorage.Save(State.Career);};
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
            int band=State.Active?(State.Op.trace>=85?2:State.Op.trace>=60?1:0):0;
            if(band>traceBand&&!muted&&audioSource!=null)audioSource.PlayOneShot(warningSound,band==2?1:.5f);
            traceBand=band;
            if(timeLabel!=null&&State.Active)timeLabel.text="SESSION / "+TimeText(State.Op.elapsed);
            if(!State.Active&&State.LastReport!=null&&modal!="report"&&State.LastReport!=lastShownReport)
            {lastShownReport=State.LastReport;modal="report";dirty=true;Beep(State.LastReport.success);}
            if(dirty)Build();
            UpdateMeters();
            canvas?.Tick(State,Visuals.Mode);
            Visuals.Tick(State,view=="research");
            root.EnableInClassList("trace-alert",State.Active&&State.Op.trace>=60);
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
                switch(evt.keyCode)
                {
                    case KeyCode.Alpha1:Run(ToolKind.Scan);break;
                    case KeyCode.Alpha2:Run(ToolKind.Hack);break;
                    case KeyCode.Alpha3:Run(ToolKind.Download);break;
                    case KeyCode.Alpha4:Run(ToolKind.Disrupt);break;
                    case KeyCode.B:Run(ToolKind.Bridge);break;
                    case KeyCode.R:Run(ToolKind.Backdoor);break;
                    default:return;
                }
            }
            else return;
            evt.StopImmediatePropagation();
        }
        private void OnApplicationQuit(){if(State!=null&&!Testing)CareerStorage.Save(State.Career);}
        private void OnApplicationPause(bool paused){if(paused&&State!=null&&!Testing)CareerStorage.Save(State.Career);}
        private void OnDestroy(){canvas?.Dispose();Visuals?.Dispose();if(Instance==this)Instance=null;}

        private void Build()
        {
            dirty=false;canvas?.Dispose();uiActions.Clear();root.Clear();traceFill=null;taskFill=null;traceNumber=null;runningLabel=null;traceState=null;timeLabel=null;canvas=null;
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
                foreach(var job in GameContent.Contracts(State.Career))
                {
                    bool done=State.Career.completed.Contains(job.id),locked=State.Career.completed.Count<job.requiredWins;
                    var card=Btn(list,"",()=>{canvas?.Dispose();selectedJob=job;selectedNode="n22";networkCamera.Reset();dirty=true;Beep();},"contract-"+job.id,"contract-card");
                    if(selectedJob.id==job.id)card.AddToClassList("active");if(locked)card.AddToClassList("locked");
                    var meta=El(card,"row-between");Text(meta,done?"DELIVERED":locked?"ENCRYPTED":"AVAILABLE","contract-state"+(done?" lime":locked?" muted":" cyan"));Text(meta,job.recurring?"R":"0"+(Array.IndexOf(GameContent.Story,job)+1),"micro muted");
                    Text(card,job.title,"contract-title");Text(card,job.company,"micro muted");
                    var reward=El(card,"contract-reward");Text(reward,job.credits.ToString("N0")+" cr","small lime");Text(reward,"+"+job.xp+" XP","small muted");
                }
            }
            else
            {
                var brief=El(left,"active-brief");Text(brief,State.Job.tag,"micro cyan");Text(brief,State.Job.title,"mission-title");Text(brief,State.Job.client,"micro muted");
                Text(brief,State.Job.description,"body-copy");Text(brief,"OBJECTIVES","eyebrow section-space");
                foreach(var n in State.Op.nodes.Where(n=>n.objective))
                {var b=Btn(brief,(State.ObjectiveDone(n)?"[+] ":"[ ] ")+n.label,()=>SelectNode(n.id),"objective-"+n.id,"objective-row");if(State.ObjectiveDone(n))b.AddToClassList("lime");}
                Text(brief,"REWARD CONDITIONS","eyebrow section-space");Text(brief,State.Job.credits+" cr + "+State.Job.xp+" XP on delivery","small lime");
                Text(brief,"Below "+State.Job.quietLimit+"% trace: +150 cr / +10 XP","small muted");Text(brief,"Optional archives: +120 cr / +15 XP each (XP capped at +30)","small muted");
                El(brief,"flex-spacer");Text(brief,"YOUR APPROACH","eyebrow section-space");Text(brief,"Front door: hardened defenses.\nMaintenance lane: easier systems, longer route.\nScan to find the connections.","body-copy muted");
            }
            var loadout=El(left,"loadout");Text(loadout,"EQUIPPED ABILITIES  /  "+State.Career.equipped.Count+" OF "+State.Slots,"eyebrow");
            if(State.Career.equipped.Count==0)Text(loadout,"No special abilities yet. Earn XP to build your toolkit.","small muted");
            foreach(var id in State.Career.equipped)
            {
                var tech=GameContent.FindTech(id);
                var b=Btn(loadout,tech.title+(id=="decoy"&&State.Active?(State.Op.decoyUsed?" / USED":" / ACTIVATE"):" / READY"),()=>{if(id=="decoy"&&State.Active){State.Decoy();Beep();}else if(!State.Active){selectedTech=id;Navigate("research");}},"ability-"+id,"ability-button");
                b.SetEnabled(!State.Active||id!="decoy"||!State.Op.decoyUsed);
            }
            var mid=El(workspace,"network-panel");var mh=El(mid,"map-heading");var title=El(mh,"map-title-block");
            Text(title,State.Active?"LIVE NETWORK":"NETWORK INTELLIGENCE","eyebrow cyan");Text(title,(State.Active?State.Job:selectedJob).company,"map-title");
            var state=El(mh,"map-status");Text(state,State.Active?(State.Op.paused?"||  SIMULATION PAUSED":"●  ENCRYPTED CONNECTION"):"○  OFFLINE SCHEMATIC","micro "+(State.Active?"lime":"muted"));
            Text(state,State.Active?State.Op.nodes.Count(n=>n.known)+" / 25 SYSTEMS DISCOVERED":"SELECT A CONTRACT TO BEGIN","micro muted");
            var signal=El(mid,"signal-strip");Text(signal,"GHOST / FREQUENCY","micro");Text(signal,State.Active?"RX / ENCRYPTED UPLINK":"RX / PASSIVE INTERCEPT","micro");Text(signal,"CHANNEL 01.337","micro");
            var sectors=El(mid,"sectors");Text(sectors,"01 / PUBLIC EDGE","sector");Text(sectors,"02 / INTERNAL SERVICES","sector");Text(sectors,"03 / SECURE CORE","sector");
            var nodes=State.Active?State.Op.nodes:GameContent.Network(selectedJob,State.Career);
            var links=State.Active?State.Op.links:GameContent.Links(selectedJob.seed);
            var route=State.Active?State.Route(selectedNode,true):null;
            canvas=new GraphViewport(nodes,links,selectedNode,!State.Active,SelectNode,route,networkCamera);mid.Add(canvas);Visuals.AttachField(canvas);
            var mapFooter=El(mid,"map-footer");
            var legend=El(mapFooter,"legend");foreach(var kind in new[]{NodeKind.Workstation,NodeKind.Relay,NodeKind.Firewall,NodeKind.Scanner,NodeKind.Database,NodeKind.SecurityHub})
            {var key=El(legend,"legend-key");var dot=El(key,"legend-dot");dot.style.backgroundColor=Palette.Kind(kind);Text(key,kind==NodeKind.SecurityHub?"Hub":kind.ToString(),"micro muted");}
            var controls=El(mapFooter,"map-controls");Btn(controls,"−",()=>canvas.Zoom(.85f),"zoom-out","map-button");Btn(controls,"+",()=>canvas.Zoom(1.15f),"zoom-in","map-button");Btn(controls,"FIT",()=>canvas.ResetView(),"fit","map-button");
            BuildTraceBar(mid);
            var right=El(workspace,"right-panel");
            if(State.Active)BuildTarget(right,State.Node(selectedNode)??State.Node("home"));
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
            Text(scroll,selectedJob.condition,"small muted");Text(scroll,"OPTIONAL ARCHIVES","eyebrow section-space");Text(scroll,"+120 cr and +15 XP per archive, up to +30 bonus XP. Secure the main objective before chasing extra files.","small muted");
            Text(scroll,"OPERATOR NOTE","eyebrow section-space");Text(scroll,State.Career.jobs==0?"Your gateway is already controlled. Hack a connected relay to get inside, or scan it to discover more routes. Pause whenever you need to think.":"Scanners add pressure while you work near them. A captured hub weakens the systems it protects. Your old successes may have changed these defenses.","body-copy note");
            var bottom=El(right,"inspector-bottom");bool done=State.Career.completed.Contains(selectedJob.id),locked=State.Career.completed.Count<selectedJob.requiredWins;
            var start=Btn(bottom,done?"CONTRACT DELIVERED":locked?"COMPLETE EARLIER CONTRACTS":"CONNECT TO NETWORK  →",StartSelected,"connect","primary-button");start.SetEnabled(!done&&!locked);
            Text(bottom,"No time limit. Plan freely. Disconnect to bank rewards.","micro muted center-text");
        }

        public void StartSelected()
        {
            if(State.Start(selectedJob)){canvas?.Dispose();selectedNode="n2";view="network";networkCamera.Reset();Beep();dirty=true;}
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
            var type=El(scroll,"target-type");type.Add(new DeviceGlyph(node.kind,Palette.Kind(node.kind),42));
            var status=El(type,"target-status");Text(status,node.kind.ToString().ToUpperInvariant(),"micro cyan");Text(status,node.controlled?"ACCESS ESTABLISHED":State.Reachable(node)?"WITHIN REACH":"ROUTE REQUIRED","micro "+(node.controlled?"lime":"muted"));
            Text(scroll,node.label,"target-title");Text(scroll,"10."+(14+node.column)+"."+(80+node.row)+"."+(node.id=="home"?"1":node.id.Substring(1)),"micro muted");
            Text(scroll,node.detail,"body-copy muted");
            var stats=El(scroll,"target-stats");Text(stats,"SECURITY","eyebrow");Text(stats,State.EffectiveSecurity(node).ToString("00")+" / 10","small");
            var bars=El(scroll,"security-bars");for(int i=0;i<10;i++){var bar=El(bars,"security-bar");if(i<State.EffectiveSecurity(node))bar.style.backgroundColor=Palette.Kind(node.kind);}
            if(node.objective)Text(scroll,State.ObjectiveDone(node)?"[+] CONTRACT OBJECTIVE SECURED":"[*] CONTRACT OBJECTIVE","objective-tag");
            if(node.bonus)Text(scroll,node.collected?"[+] OPTIONAL FILES SECURED":"[+] OPTIONAL ARCHIVE / +120 cr","micro lime section-space");
            if(node.suppressed>0)Text(scroll,"SUPPRESSED / "+Mathf.CeilToInt(node.suppressed)+"s remaining","micro lime section-space");
            Text(scroll,"AVAILABLE ACTIONS","eyebrow section-space");
            ActionButton(scroll,ToolKind.Scan,"1","Scan connections");ActionButton(scroll,ToolKind.Hack,"2","Hack system");
            if(node.objective||node.bonus)ActionButton(scroll,ToolKind.Download,"3",State.Job.kind==JobKind.Alter&&node.objective?"Correct record":State.Job.kind==JobKind.Disrupt&&node.id=="n22"?"Erase targeting index":State.Job.kind==JobKind.Investigate&&node.objective?"Recover evidence":"Download files");
            if(node.kind==NodeKind.Scanner||node.kind==NodeKind.SecurityHub)ActionButton(scroll,ToolKind.Disrupt,"4",node.kind==NodeKind.SecurityHub?"Blackout hub":"Disrupt scanner");
            if(State.Equipped("bridge")&&node.kind==NodeKind.Firewall)ActionButton(scroll,ToolKind.Bridge,"B","Bridge firewall");
            if(State.Equipped("backdoor")&&(node.kind==NodeKind.Workstation||node.kind==NodeKind.Relay))ActionButton(scroll,ToolKind.Backdoor,"R","Use backdoor");
            Text(scroll,"CONNECTION ROUTE","eyebrow section-space");
            var route=State.Route(node.id,true);
            Text(scroll,route!=null&&route.IsReachable?string.Join(" > ",route.NodeIds.Select(id=>id=="home"?"LOCAL":State.Node(id).label)):"No controlled route. Hack a neighboring system to extend your access.","small muted");
            if(node.kind==NodeKind.SecurityHub)Text(scroll,"PROTECTS / "+string.Join(", ",State.Neighbors(node).Where(n=>n.kind==NodeKind.Firewall||n.kind==NodeKind.Database).Select(n=>n.label)),"small amber section-space");
            var bottom=El(right,"inspector-bottom");
            Btn(bottom,State.ObjectiveComplete?"EXTRACT & DELIVER  →":"DISCONNECT",DisconnectClicked,"disconnect",State.ObjectiveComplete?"primary-button":"danger-button");
            Text(bottom,State.ObjectiveCount+" / "+State.ObjectiveTotal+" objectives secured","micro muted center-text");
        }

        private void ActionButton(VisualElement parent,ToolKind kind,string shortcut,string title)
        {
            var preview=State.Preview(kind,selectedNode);bool queuable=State.Op.task!=null&&State.Op.queue.Count<(State.Has("chain")?3:1);
            var b=Btn(parent,"",()=>Run(kind),"action-"+kind.ToString().ToLowerInvariant(),"action-button");
            var row=El(b,"row-between");Text(row,title,"action-title");Text(row,shortcut,"keycap");
            Text(b,preview.allowed?preview.duration.ToString("0.0")+"s  /  +"+preview.trace.ToString("0.0")+" trace"+(State.Op.task!=null?"  / QUEUE":""):preview.reason,"action-estimate");
            b.tooltip=preview.reason;
            // The future-neighbor allowance is exclusive to the Chain hack ability.
            bool futureHack=queuable&&kind==ToolKind.Hack&&State.Has("chain")&&State.Node(selectedNode)?.known==true&&!State.Node(selectedNode).controlled;
            b.SetEnabled(preview.allowed||futureHack);
        }

        private void Run(ToolKind kind) { if(State.Execute(kind,selectedNode)){Beep();dirty=true;} }
        private void DisconnectClicked(){if(State.ObjectiveComplete)State.Disconnect();else{State.SetPaused(true);modal="abort";dirty=true;}}

        private void BuildTraceBar(VisualElement parent)
        {
            var bar=El(parent,"operation-bar");
            var trace=El(bar,"trace-block");var heading=El(trace,"row-between");Text(heading,"TRACE LEVEL","eyebrow");traceNumber=Text(heading,"00.0%","trace-number");
            var track=El(trace,"meter-track");traceFill=El(track,"meter-fill");traceState=Text(trace,"No active connection","micro muted");
            var task=El(bar,"task-block");runningLabel=Text(task,"YOUR NEXT CONNECTION STARTS HERE","task-title");var taskTrack=El(task,"task-track");taskFill=El(taskTrack,"task-fill");
            var controls=El(bar,"time-controls");
            var pause=Btn(controls,State.Active&&State.Op.paused?"▶":"Ⅱ",()=>{if(State.Active)State.SetPaused(!State.Op.paused);},"pause","time-button");pause.SetEnabled(State.Active);pause.tooltip="Pause / Space";
            foreach(float rate in new[]{1f,2f,4f}){float captured=rate;var b=Btn(controls,rate+"x",()=>{speed=captured;dirty=true;},"speed-"+rate,"speed-button");if(Mathf.Approximately(speed,rate))b.AddToClassList("active");}
        }

        private void UpdateMeters()
        {
            if(traceNumber==null)return;
            float trace=State.Active?State.Op.trace:0;Color color=trace>=85?Palette.Red:trace>=60?Palette.Amber:Palette.Lime;
            traceNumber.text=trace.ToString("00.0")+"%";traceNumber.style.color=color;traceFill.style.width=Length.Percent(trace);traceFill.style.backgroundColor=color;
            traceState.text=!State.Active?"DISCONNECTED / NO EXPOSURE":State.Op.decoyRemaining>0?"DECOY ACTIVE / "+Mathf.CeilToInt(State.Op.decoyRemaining)+"s":trace>=85?"LOCKDOWN IMMINENT / FINISH OR DISCONNECT":State.Scanners>0?State.Scanners+" ACTIVE SCANNER"+(State.Scanners>1?"S":"")+" / EXTRA TRACE WHILE TOOLS RUN":"STABLE / PLANNING DOES NOT RAISE TRACE";
            if(State.Active&&State.Op.task!=null){runningLabel.text=(State.Op.paused?"PAUSED / ":"RUNNING / ")+State.Op.task.kind.ToString().ToUpperInvariant()+" : "+State.Node(State.Op.task.target).label+"  "+(State.Progress*100).ToString("0")+"%";taskFill.style.width=Length.Percent(State.Progress*100);}
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
            var refund=Btn(refundArea,"REFUND ALL RESEARCH",()=>{State.RefundResearch();Beep();},"refund","secondary-button");refund.SetEnabled(!State.Active&&State.Career.unlocked.Count>1);Text(refundArea,"Free between contracts. Every XP is returned.","micro muted");
            var mid=El(workspace,"network-panel");var mh=El(mid,"map-heading");var titles=El(mh,"map-title-block");Text(titles,"CONNECTED KNOWLEDGE","eyebrow cyan");Text(titles,"RESEARCH NETWORK","map-title");Text(mh,(State.Career.unlocked.Count-1)+" / "+(GameContent.Tech.Length-1)+" TECHNOLOGIES UNLOCKED","micro muted");
            canvas=new GraphViewport(State.Career,selectedTech,SelectTech,researchCamera);mid.Add(canvas);Visuals.AttachField(canvas);
            var footer=El(mid,"map-footer");Text(footer,"SOLID / UNLOCKED    ·    DIM / PREREQUISITE REQUIRED    ·    SELECT A NODE TO INSPECT","micro muted");
            var controls=El(footer,"map-controls");Btn(controls,"−",()=>canvas.Zoom(.85f),"zoom-out","map-button");Btn(controls,"+",()=>canvas.Zoom(1.15f),"zoom-in","map-button");Btn(controls,"FIT",()=>canvas.ResetView(),"fit","map-button");
            var right=El(workspace,"right-panel");var info=El(right,"tech-inspector");var tech=GameContent.FindTech(selectedTech)??GameContent.Tech[0];
            Text(info,"TECHNOLOGY INSPECTOR","eyebrow");Text(info,tech.branch+(tech.special?" / EQUIPPED ABILITY":" / PASSIVE"),"micro section-space").style.color=Palette.Branch(tech.branch);
            Text(info,tech.title,"mission-title");Text(info,tech.description,"body-copy");
            Text(info,State.Has(tech.id)?"UNLOCKED":tech.cost+" XP","research-price");
            Text(info,"PREREQUISITES","eyebrow section-space");
            if(tech.parents.Length==0)Text(info,"Included with your gateway.","small muted");
            foreach(var parent in tech.parents)Text(info,(State.Has(parent)?"[+] ":"[ ] ")+GameContent.FindTech(parent).title,"small "+(State.Has(parent)?"lime":"muted"));
            if(tech.parents.Length>1)Text(info,"BOTH parent technologies required.","micro amber section-space");
            Text(info,"LOADOUT","eyebrow section-space");Text(info,State.Career.equipped.Count+" / "+State.Slots+" special ability slots occupied","small muted");
            foreach(var id in State.Career.equipped)Btn(info,GameContent.FindTech(id).title+"  ×",()=>State.ToggleEquip(id),"unequip-"+id,"ability-button").SetEnabled(!State.Active);
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
            var specs=El(art,"rig-specs");Text(specs,"CPU   "+(1+State.Career.cpu*.2f).ToString("0.0")+"x HACK SPEED","small lime");Text(specs,"LINK  "+(1+State.Career.bandwidth*.3f).ToString("0.0")+"x TRANSFER SPEED","small cyan");Text(specs,"MEM   "+State.Slots+" ABILITY SLOTS","small violet");
            var upgrades=El(rig,"upgrade-list");
            string[] names={"Parallel processor","Fiber uplink","Memory expansion"};string[] labels={"PROCESSING","BANDWIDTH","ABILITY CAPACITY"};string[] descriptions={"+20% hack speed per tier. Stack with Efficient hack to spend less time exposed.","+30% transfer speed per tier. Get contract data out before the trace closes in.","One additional equipped special ability per tier. Passive research needs no slots."};
            for(int i=0;i<3;i++)
            {
                int type=i,level=i==0?State.Career.cpu:i==1?State.Career.bandwidth:State.Career.memory,max=i==2?2:3;
                var card=El(upgrades,"upgrade-card");var summary=El(card,"upgrade-summary");Text(summary,labels[i]+" / TIER "+level+" OF "+max,"eyebrow cyan");Text(summary,names[i],"upgrade-name");Text(summary,descriptions[i],"body-copy muted");
                var purchase=El(card,"upgrade-purchase");Text(purchase,level==max?"MAXIMUM":State.UpgradePrice(i).ToString("N0")+" cr","payout");
                var b=Btn(purchase,level==max?"INSTALLED":"INSTALL UPGRADE",()=>{if(State.Upgrade(type))Beep(true);},"upgrade-"+i,"primary-button");b.SetEnabled(!State.Active&&level<max&&State.Career.credits>=State.UpgradePrice(i));
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
            var stats=El(page,"career-stats");Text(stats,State.Career.jobs+" CONTRACTS DELIVERED","eyebrow lime");Text(stats,State.Career.totalEarnedXp+" TOTAL XP EARNED","eyebrow cyan");Text(stats,(State.Career.unlocked.Count-1)+" TECHNOLOGIES","eyebrow violet");
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
            if(State.Active){Text(side,"QUEUE / "+State.Op.queue.Count+" ACTIONS","small muted");if(State.Op.queue.Count>0)Btn(side,"CLEAR QUEUE",()=>State.ClearQueue(),"clear-queue","text-button");}
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
            selectedJob=GameContent.Story[0];selectedNode="n22";selectedTech="wide";view="network";
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
                string[] guide={"01 / CONNECT — Pick a contract. Gold markers show required objectives. Green means a system is yours.","02 / EXPLORE — Select a reachable system. Scan [1] reveals nearby connections. Hack [2] takes control and extends your route.","03 / RECOVER — Control each required target, then recover its files [3]. Record jobs need both copies; investigations need all three records.","04 / MANAGE TRACE — Tools create trace. Scanners add pressure and hubs strengthen neighbors. Planning is free. Space pauses; 1x / 2x / 4x changes speed.","05 / GET PAID — Extract & deliver after all objectives are secured. Optional archives pay extra. At 100% trace the attempt ends; gear and research stay yours.","06 / GROW — Credits buy hardware. XP purchases connected research. Crossovers require both parents. Equip abilities in limited slots. Research refunds are free between jobs."};
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
                if(report.success&&State.Career.completed.Count==6)Text(box,"STORY COMPLETE / Freelance contracts remain available. Keep building your network.","body-copy lime");
                var actions=El(box,"modal-actions");
                bool archived=modal=="archive";
                Btn(actions,archived?"CLOSE ARCHIVE":"BACK TO EXCHANGE",()=>{modal=null;view=archived?"intel":"network";SelectNextContract();dirty=true;},"close-report","secondary-button");
                if(report.success&&modal!="archive")Btn(actions,"OPEN RESEARCH  →",()=>{modal=null;SelectNextContract();Navigate("research");},"report-research","primary-button");
            }
        }

        private void SelectNextContract(){selectedJob=GameContent.Contracts(State.Career).FirstOrDefault(c=>!State.Career.completed.Contains(c.id)&&State.Career.completed.Count>=c.requiredWins)??GameContent.Contracts(State.Career).Last();selectedNode="n22";}
        private string Rank()=>State.Career.jobs<1?"NEW SIGNAL":State.Career.jobs<3?"FREELANCER":State.Career.jobs<6?"TRUSTED OPERATOR":"GHOST IN THE NETWORK";
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
        public void UseTestCareer(Career career)
        {
            Testing=true;State=new GameSimulation(career);State.Changed+=()=>dirty=true;selectedJob=GameContent.Story[0];selectedNode="n2";view="network";modal=null;dirty=true;
            modalBeforeSettings=settingsError=null;lastShownReport=reportToShow=null;
        }
    }
}
