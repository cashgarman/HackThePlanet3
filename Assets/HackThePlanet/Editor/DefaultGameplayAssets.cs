using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HackThePlanet.Editor
{
    // One-time conversion of the captured playable into editable assets. Never used by a player.
    public static class DefaultGameplayAssets
    {
        public const string Folder="Assets/HackThePlanet/Resources/HackThePlanet/Gameplay";
        private static T Asset<T>(string id,string title) where T:GameplayDefinition
        {
            var value=ScriptableObject.CreateInstance<T>();value.id=id;value.title=title;value.name=id;
            AssetDatabase.CreateAsset(value,Folder+"/"+typeof(T).Name+"-"+id+".asset");return value;
        }
        public static Number N(float x)=>new ConstantNumber(x);
        public static Number I(NumberInput x,string key=null)=>new InputNumber{input=x,key=key};
        public static Number S(string key)=>new StatNumber{stat=key};
        public static Number B(Arithmetic op,Number a,Number b)=>new BinaryNumber{operation=op,left=a,right=b};
        public static Condition State(NodeState s)=>new NodeStateCondition{state=s};
        public static Condition Tag(string s)=>new NodeTagCondition{tag=s};
        public static Condition Not(Condition c)=>new NotCondition{condition=c};
        public static Condition All(params Condition[] c)=>new AllCondition{conditions=c};
        public static Condition Any(params Condition[] c)=>new AnyCondition{conditions=c};
        public static Condition Compare(Number a,Comparison op,Number b)=>new CompareCondition{left=a,comparison=op,right=b};
        public static Number Choose(Condition when,Number yes,Number no)=>new ConditionalNumber{when=when,yes=yes,no=no};
        public static Modifier Mod(string stat,Arithmetic op,Number value,Condition when=null,int priority=0)=>new Modifier{stat=stat,operation=op,value=value,when=when??new AlwaysCondition(),priority=priority};
        public static Requirement Req(Condition c,string message)=>new Requirement{condition=c,message=message};
        public static Condition Action(ActionDefinition a)=>new ActionCondition{action=a};
        public static Effect Log(string message)=>new LogEffect{message=message};
        public static NodeSelector Select(SelectionScope scope,Condition filter=null)=>new NodeSelector{scope=scope,filter=filter??new AlwaysCondition()};

        [MenuItem("Hack The Planet/Import Captured Gameplay Assets")]
        public static void Import()
        {
            if(AssetDatabase.LoadAssetAtPath<GameDefinition>(Folder+"/Game.asset")!=null)throw new InvalidOperationException("Game.asset already exists. Edit the existing assets; importing must never overwrite authored content.");
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            var baseline=JsonUtility.FromJson<GameplaySnapshot>(File.ReadAllText("Artifacts/gameplay-baseline.json"));
            var root=ScriptableObject.CreateInstance<GameDefinition>();root.id="game";root.title="Hack The Planet";root.name="Game";AssetDatabase.CreateAsset(root,Folder+"/Game.asset");
            root.companies=baseline.companies.Select((name,i)=>Asset<CompanyDefinition>("company-"+i,name)).ToArray();
            root.nodeTypes=Enum.GetValues(typeof(NodeKind)).Cast<NodeKind>().Select(kind=>
            {
                var type=Asset<NodeTypeDefinition>(kind.ToString().ToLowerInvariant(),kind.ToString());type.glyph=kind;type.color=Palette.Kind(kind);
                type.tags=new[]{type.id};type.description=baseline.prototypes.First(n=>n.kind==kind).detail;
                type.security=kind==NodeKind.Gateway?N(0):kind==NodeKind.Firewall?B(Arithmetic.Add,N(3),I(NumberInput.Difficulty)):kind==NodeKind.Relay?N(1):new RandomNumber{minimum=1,maximum=2};return type;
            }).ToArray();
            NodeTypeDefinition Type(NodeKind glyph)=>root.nodeTypes.First(t=>t.glyph==glyph);
            var offline=Asset<StatusDefinition>("suppression","Defense suppression");offline.disablesDefense=true;
            var protection=Asset<StatusDefinition>("trace-protection","Trace protection");protection.preventsTrace=true;
            root.statuses=new[]{offline,protection};
            var actions=new List<ActionDefinition>();
            ActionDefinition Make(string id,string title,KeyCode key,string keyLabel,float duration,float trace,Condition visible=null)
            {
                var a=Asset<ActionDefinition>(id,title);a.legacyGroup=id;a.shortcut=key;a.shortcutLabel=keyLabel;a.duration=N(duration);a.baseTrace=N(trace);a.visible=visible??new AlwaysCondition();
                a.tags=new[]{id};a.requirements=new[]{Req(State(NodeState.Known),"Scan to discover this system."),Req(State(NodeState.Reachable),"Control a connected neighbor first.")};
                a.queueRequirements=new[]{Req(State(NodeState.Known),"Discover this system first.")};actions.Add(a);return a;
            }
            var scan=Make("scan","Scan connections",KeyCode.Alpha1,"1",3,2);scan.initiallyAvailable=true;
            scan.requirements=scan.requirements.Concat(new[]{Req(Not(State(NodeState.Scanned)),"Already scanned. Its surrounding connections are known.")}).ToArray();
            scan.effects=new Effect[]{new SetStateEffect{state=NodeState.Scanned},new RevealEffect{radius=S("scanRadius")},Log("SCAN COMPLETE // New routes mapped around {target}.")};
            var hack=Make("hack","Hack system",KeyCode.Alpha2,"2",1,1);hack.initiallyAvailable=true;hack.futureNeighborQueue=true;
            hack.requirements=hack.requirements.Concat(new[]{Req(Not(State(NodeState.Controlled)),"You already control this system.")}).ToArray();
            hack.queueRequirements=hack.queueRequirements.Concat(new[]{Req(Not(State(NodeState.Controlled)),"You already control this system.")}).ToArray();
            Number HackCost()=>B(Arithmetic.Add,N(3),B(Arithmetic.Multiply,I(NumberInput.Security),N(1.7f)));
            hack.duration=B(Arithmetic.Divide,HackCost(),S("hackSpeed"));hack.baseTrace=HackCost();
            Effect[] ControlEffects()=>new Effect[]{new SetStateEffect{state=NodeState.Controlled},new RevealEffect{radius=S("revealRadius")},Log("CONTROL ESTABLISHED // {target}.")};
            hack.effects=ControlEffects();
            var download=Make("download","Download files",KeyCode.Alpha3,"3",1,1,Any(State(NodeState.Objective),State(NodeState.Bonus)));download.initiallyAvailable=true;
            download.requirements=download.requirements.Concat(new[]{Req(State(NodeState.Controlled),"Hack this system before recovering its files."),Req(Not(State(NodeState.Collected)),"Files already secured."),Req(Any(State(NodeState.Objective),State(NodeState.Bonus)),"No contract files or optional archives on this node."),Req(Not(Tag("securityhub")),"Control the hub to satisfy this objective.")}).ToArray();
            download.duration=B(Arithmetic.Divide,Choose(State(NodeState.Objective),N(9),N(7)),S("transferSpeed"));download.baseTrace=Choose(State(NodeState.Objective),N(8),N(6));
            download.effects=new Effect[]{new SetStateEffect{state=NodeState.Collected},new ConditionalEffect{when=State(NodeState.Objective),effects=new[]{Log("OBJECTIVE SECURED // {target}.")}},new ConditionalEffect{when=Not(State(NodeState.Objective)),effects=new[]{Log("OPTIONAL ARCHIVE // +{archiveCredits} cr / +{archiveXp} XP on successful extraction.")}},new RecoverLoreEffect()};
            var disrupt=Make("disrupt","Disrupt scanner",KeyCode.Alpha4,"4",2.5f,2,Tag("scanner"));
            var blackout=Make("blackout","Blackout hub",KeyCode.Alpha4,"4",2.5f,2,Tag("securityhub"));blackout.legacyGroup="disrupt";
            foreach(var a in new[]{disrupt,blackout})
            {
                a.requirements=a.requirements.Concat(new[]{Req(State(NodeState.DefenseActive),"This defense is already offline.")}).ToArray();
                a.effects=new Effect[]{new ApplyStatusEffect{status=offline,duration=N(60)},Log("DEFENSE SUPPRESSED // {target}.")};
            }
            var bridge=Make("bridge","Bridge firewall",KeyCode.B,"B",2,0,Tag("firewall"));bridge.traceFree=true;
            bridge.requirements=bridge.requirements.Concat(new[]{Req(Not(State(NodeState.Controlled)),"You already control this system."),Req(Compare(new InputNumber{input=NumberInput.NeighborCount,filter=State(NodeState.Controlled)},Comparison.GreaterOrEqual,N(2)),"Requires a firewall with two controlled neighbors.")}).ToArray();bridge.effects=ControlEffects();
            var backdoor=Make("backdoor","Use backdoor",KeyCode.R,"R",1,0,Any(Tag("workstation"),Tag("relay")));backdoor.traceFree=true;backdoor.useLimit=N(1);
            backdoor.requirements=new[]{Req(State(NodeState.Known),"Discover this system first."),Req(Not(State(NodeState.Controlled)),"You already control this system.")};backdoor.effects=ControlEffects();
            var decoy=Make("decoy","Decoy process",KeyCode.None,"",0,0);decoy.operatorTarget=true;decoy.queueable=false;decoy.traceFree=true;decoy.useLimit=N(1);decoy.requirements=Array.Empty<Requirement>();
            decoy.effects=new Effect[]{new TraceEffect{amount=N(-15)},new ApplyStatusEffect{operatorStatus=true,status=protection,duration=N(20)},Log("DECOY ACTIVE // Trace reduced. Trace growth suspended.")};
            root.actions=actions.ToArray();
            root.technologies=baseline.technologies.Select(t=>
            {
                var d=Asset<TechnologyDefinition>(t.id,t.title);d.description=t.description;d.branch=t.branch;d.cost=t.cost;d.special=t.special;d.position=t.position;return d;
            }).ToArray();
            TechnologyDefinition Tech(string id)=>root.technologies.First(t=>t.id==id);
            foreach(var t in baseline.technologies)Tech(t.id).parents=t.parents.Select(Tech).ToArray();
            foreach(var pair in new (TechnologyDefinition tech,ActionDefinition action)[]{(Tech("disrupt"),disrupt),(Tech("blackout"),blackout),(Tech("bridge"),bridge),(Tech("backdoor"),backdoor),(Tech("decoy"),decoy)})pair.tech.grants=new[]{pair.action};
            bridge.visible=All(Tag("firewall"),new ResearchCondition{technology=Tech("bridge"),equipped=true});
            backdoor.visible=All(Any(Tag("workstation"),Tag("relay")),new ResearchCondition{technology=Tech("backdoor"),equipped=true});
            decoy.visible=new ResearchCondition{technology=Tech("decoy"),equipped=true};
            Tech("wide").modifiers=new[]{Mod("scanRadius",Arithmetic.Override,N(3))};
            Tech("signal").modifiers=new[]{Mod("duration",Arithmetic.Multiply,N(.5f),Action(scan)),Mod("baseTrace",Arithmetic.Override,N(0),Action(scan))};
            Tech("efficient").modifiers=new[]{Mod("duration",Arithmetic.Multiply,N(.75f),Action(hack))};
            Tech("chain").modifiers=new[]{Mod("queueCapacity",Arithmetic.Override,N(3)),Mod("futureQueue",Arithmetic.Override,N(1))};
            Tech("selective").modifiers=new[]{Mod("duration",Arithmetic.Multiply,N(.55f),All(Action(download),State(NodeState.Objective))),Mod("baseTrace",Arithmetic.Multiply,N(.7f),All(Action(download),State(NodeState.Objective)))};
            Tech("quiet").modifiers=new[]{Mod("baseTrace",Arithmetic.Multiply,N(.7f),Action(hack))};
            Tech("ghost").modifiers=new[]{Mod("traceFree",Arithmetic.Override,N(1),All(Action(hack),Tag("relay"),Compare(I(NumberInput.Counter,"relay-controls"),Comparison.Equal,N(0))))};
            Tech("passive").modifiers=new[]{Mod("revealRadius",Arithmetic.Override,N(3),Tag("relay"))};
            Tech("weakpoint").modifiers=new[]{Mod("baseTrace",Arithmetic.Multiply,N(.6f),All(Action(hack),State(NodeState.Scanned)))};
            Tech("buffer").modifiers=new[]{Mod("trace",Arithmetic.Multiply,N(.85f))};
            Tech("blueprint").triggers=new[]{new Trigger{on=RuleEvent.OperationStarted,effects=new Effect[]{new RevealEffect{targets=Select(SelectionScope.All,State(NodeState.Objective)),radius=N(1)}}}};
            Tech("cascade").triggers=new[]{new Trigger{on=RuleEvent.ActionCompleted,onceKey="cascade",when=All(Tag("securityhub"),Not(State(NodeState.DefenseActive))),effects=new Effect[]{new SecurityEffect{targets=Select(SelectionScope.All,Tag("firewall")),amount=N(-2)},Log("SYSTEM CASCADE // Firewall security reduced across the network.")}}};
            Tech("efficient").description=Tech("efficient").description.Replace("25%","{modifier:0:reduction}%");
            Tech("quiet").description=Tech("quiet").description.Replace("30%","{modifier:0:reduction}%");
            Tech("weakpoint").description=Tech("weakpoint").description.Replace("40%","{modifier:0:reduction}%");
            Tech("buffer").description=Tech("buffer").description.Replace("15%","{modifier:0:reduction}%");
            Tech("selective").description=Tech("selective").description.Replace("55%","{modifier:0:percent}%").Replace("30%","{modifier:1:reduction}%");
            root.triggers=new[]{new Trigger{on=RuleEvent.OperationStarted,effects=new Effect[]{new RevealEffect{targets=Select(SelectionScope.Entry),radius=N(1)}}},new Trigger{on=RuleEvent.ControlEstablished,when=Tag("relay"),effects=new Effect[]{new CounterEffect{key="relay-controls"}}}};
            Type(NodeKind.Scanner).auras=new[]{new Aura{id="scanner-pressure",stack=true,source=All(State(NodeState.DefenseActive),Compare(new InputNumber{input=NumberInput.NeighborCount,filter=State(NodeState.Controlled)},Comparison.Greater,N(0))),modifier=Mod("tracePressure",Arithmetic.Add,N(.18f))}};
            Type(NodeKind.SecurityHub).auras=new[]{new Aura{id="hub-protection",adjacentOnly=true,source=State(NodeState.DefenseActive),modifier=Mod("security",Arithmetic.Add,N(2),Any(Tag("firewall"),Tag("database")))}};
            var network=Asset<NetworkDefinition>("corporate-network","Corporate infrastructure");network.layout=NetworkLayout.ParallelRoutes;network.entryNode="home";network.preferredNode="n2";network.previewNode="n22";
            network.nodes=baseline.prototypes.Select(n=>new NetworkNode{id=n.id,label=n.label,detail=n.kind==NodeKind.Gateway?n.detail:null,type=Type(n.kind),column=n.column,row=n.row,x=n.x,y=n.y,known=n.known,controlled=n.controlled,securityModifiers=n.id=="home"?Array.Empty<Modifier>():new[]{
                Mod("security",Arithmetic.Add,B(Arithmetic.Subtract,I(NumberInput.Difficulty),N(1)),Compare(I(NumberInput.Column),Comparison.GreaterOrEqual,N(4))),
                Mod("security",Arithmetic.Add,N(1),All(Tag("firewall"),Compare(I(NumberInput.CompanyWins),Comparison.Greater,N(0))))}}).ToArray();
            var r=network.routes;r.entrance="n2";r.destination="n22";r.sensorAttachment="n6";r.directRoute=new[]{"n6","n10","n14","n18"};r.sensors=new[]{"n5","n11","watch-a","watch-b"};r.secondaryTargets=new[]{"n9","n19","n20"};r.archives=new[]{"n13","n21","archive-c"};
            r.reserved=new[]{"home","n2","n22","n9","n19","n20","n6","n10","n14","n18","n5","n11","watch-a","watch-b"}.Concat(r.archives).ToArray();
            r.relay=Type(NodeKind.Relay);r.workstation=Type(NodeKind.Workstation);r.scanner=Type(NodeKind.Scanner);r.archive=Type(NodeKind.Database);
            r.extraIds=new[]{"watch-a","watch-b","archive-c"};r.extraTypes=new[]{r.scanner,r.scanner,r.archive};r.extraLabels=new[]{"Ingress sensor","Deep packet sensor","Sealed correspondence"};
            r.tiers=new[]{new RouteTier{nodeCount=32,lengths=new[]{4,6,8},sensorsPerLane=1},new RouteTier{nodeCount=44,lengths=new[]{4,5,6,8},sensorsPerLane=2},new RouteTier{nodeCount=56,lengths=new[]{4,5,6,7,9},sensorsPerLane=2}};
            r.directSecurity=B(Arithmetic.Add,Choose(Compare(I(NumberInput.Step),Comparison.Equal,N(0)),N(1),N(2)),Choose(Tag("firewall"),B(Arithmetic.Add,B(Arithmetic.Subtract,I(NumberInput.Difficulty),N(1)),Choose(Compare(I(NumberInput.CompanyWins),Comparison.Greater,N(0)),N(1),N(0))),N(0)));
            r.routeSecurity=Choose(Compare(I(NumberInput.SafeLane),Comparison.Equal,N(1)),N(1),B(Arithmetic.Add,N(1),B(Arithmetic.Modulo,I(NumberInput.Step),N(2))));
            r.workstationWhen=Compare(B(Arithmetic.Modulo,I(NumberInput.Step),N(3)),Comparison.Equal,N(1));root.networks=new[]{network};
            root.contracts=baseline.contracts.Select((job,index)=>
            {
                var d=Asset<ContractDefinition>(job.id,job.title);d.description=job.description;d.company=root.companies[Array.IndexOf(baseline.companies,job.company)];d.network=network;
                d.client=job.client;d.tag=job.tag;d.objective=job.objective;d.condition=job.condition;d.aftermath=job.aftermath;d.displayKind=job.kind;d.recurring=job.recurring;d.requiredWins=job.requiredWins;d.quietLimit=job.quietLimit;
                d.credits=job.recurring?B(Arithmetic.Add,N(600),B(Arithmetic.Multiply,I(NumberInput.Day),N(35))):N(job.credits);d.xp=N(job.xp);
                d.seed=job.recurring?B(Arithmetic.Add,B(Arithmetic.Multiply,I(NumberInput.Day),N(71)),N((index-6)*37)):N(job.seed);
                d.difficulty=job.recurring?B(Arithmetic.Minimum,N(3),B(Arithmetic.Maximum,N(1),B(Arithmetic.Add,N(1),B(Arithmetic.Floor,B(Arithmetic.Divide,I(NumberInput.Day),N(5)),N(0))))):N(job.difficulty);
                var objectives=new List<ObjectiveDefinition>{new ObjectiveDefinition{nodeId="n22",action=download,actionLabel=job.kind==JobKind.Alter?"Correct record":job.kind==JobKind.Investigate?"Recover evidence":job.kind==JobKind.Disrupt?"Erase targeting index":"Download files",complete=State(NodeState.Collected)}};
                if(job.kind==JobKind.Alter)objectives.Add(new ObjectiveDefinition{nodeId="n20",action=download,actionLabel="Correct record",complete=State(NodeState.Collected)});
                if(job.kind==JobKind.Investigate)foreach(var id in new[]{"n9","n19"})objectives.Add(new ObjectiveDefinition{nodeId=id,action=download,actionLabel="Recover evidence",complete=State(NodeState.Collected)});
                if(job.kind==JobKind.Disrupt)
                {
                    objectives.Add(new ObjectiveDefinition{nodeId="n14",action=hack,complete=State(NodeState.Controlled)});
                    d.actionRules=new[]{new ContractActionRule{action=download,requirements=new[]{Req(new NamedNodeCondition{nodeId="n14",condition=State(NodeState.Controlled)},"Take control of Security hub before erasing the index.")}}};
                }
                d.objectives=objectives.ToArray();d.lore=baseline.lore.Skip(index*3).Take(3).ToArray();return d;
            }).ToArray();
            root.hardware=new[]{Asset<HardwareDefinition>("cpu","Parallel processor"),Asset<HardwareDefinition>("bandwidth","Fiber uplink"),Asset<HardwareDefinition>("memory","Memory expansion")};
            string[] statNames={"hackSpeed","transferSpeed","slots"},categories={"PROCESSING","BANDWIDTH","ABILITY CAPACITY"},labels={"HACK SPEED","TRANSFER SPEED","ABILITY SLOTS"};
            for(int i=0;i<root.hardware.Length;i++)
            {
                var h=root.hardware[i];h.category=categories[i];h.statLabel=labels[i];h.displayedStat=statNames[i];h.description=i==2?"Additional room for equipped special abilities.":"Improves performance for every operation.";
                h.tiers=(i==2?new[]{600,1400}:new[]{400,900,1600}).Select(price=>new HardwareTier{price=price,modifiers=new[]{Mod(statNames[i],Arithmetic.Add,N(i==0?.2f:i==1?.3f:1))}}).ToArray();
            }
            root.rewards=Asset<RewardDefinition>("delivery","Delivery rewards");
            root.rewards.rules=new[]{new RewardRule{credits=I(NumberInput.ContractCredits),xp=I(NumberInput.ContractXp)},new RewardRule{credits=B(Arithmetic.Multiply,I(NumberInput.BonusFiles),I(NumberInput.ArchiveCredits)),xp=B(Arithmetic.Minimum,I(NumberInput.ArchiveXpCap),B(Arithmetic.Multiply,I(NumberInput.BonusFiles),I(NumberInput.ArchiveXp)))},new RewardRule{when=Compare(I(NumberInput.Trace),Comparison.Less,I(NumberInput.QuietLimit)),credits=I(NumberInput.QuietCredits),xp=I(NumberInput.QuietXp)}};
            root.career=Asset<CareerDefinition>("career","Career rules");root.career.initialTechnologies=new[]{Tech("base")};
            root.career.welcome="EXCHANGE ONLINE // A new operator has entered the network. Your first contract is waiting.";
            root.career.stats=new[]{new NamedStat("slots",2),new NamedStat("queueCapacity",1),new NamedStat("hackSpeed",1),new NamedStat("transferSpeed",1),new NamedStat("scanRadius",2),new NamedStat("revealRadius",1),new NamedStat("traceGrowth",1),new NamedStat("minimumSecurity",1),new NamedStat("futureQueue",0),new NamedStat("traceFree",0),new NamedStat("tracePressure",0),new NamedStat("idleTracePerSecond",0)};
            root.career.ranks=new[]{new CareerRank{jobs=0,title="NEW SIGNAL"},new CareerRank{jobs=1,title="FREELANCER"},new CareerRank{jobs=3,title="TRUSTED OPERATOR"},new CareerRank{jobs=6,title="GHOST IN THE NETWORK"}};
            foreach(var asset in GameDefinitionValidator.Definitions(root))EditorUtility.SetDirty(asset);
            GameDefinitionValidator.ThrowIfInvalid(root);AssetDatabase.SaveAssets();File.WriteAllText("Artifacts/gameplay-import.txt","PASS / All default gameplay definitions imported as Unity assets.");
        }
        public static void SaveCatalog()
        {
            var root=Resources.Load<GameDefinition>("HackThePlanet/Gameplay/Game");
            GameDefinitionValidator.ThrowIfInvalid(root);
            foreach(var asset in GameDefinitionValidator.Definitions(root))EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }
    }
}
