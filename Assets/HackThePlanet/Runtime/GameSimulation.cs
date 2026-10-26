using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cashg.GraphExplorer;
using UnityEngine;

namespace HackThePlanet
{
    [Serializable] public sealed class HardwareLevel { public string id; public int level; }
    [Serializable] public sealed class CounterState { public string key; public int value; }
    [Serializable] public sealed class StatusState
    {
        public string id,target;
        public float remaining;
        public bool preventsTrace,disablesDefense;
    }
    [Serializable] public sealed class Career
    {
        public int version=2, credits=-1, xp, day=1, totalEarnedXp, jobs;
        public bool initialized;
        public int researchSpent = -1;
        public List<HardwareLevel> hardware=new List<HardwareLevel>();
        public List<string> completed=new List<string>(), unlocked=new List<string>(), equipped=new List<string>(), news=new List<string>();
        public List<OperationReport> reports=new List<OperationReport>();
        public List<LoreFragment> lore=new List<LoreFragment>();
        public List<NetworkWaypoint> waypoints=new List<NetworkWaypoint>();
        public Operation operation;
        [NonSerialized] public bool saveBlocked;
        public int Level(string id)=>hardware?.FirstOrDefault(x=>x.id==id)?.level??0;
        public void SetLevel(string id,int level){hardware??=new List<HardwareLevel>();var item=hardware.FirstOrDefault(x=>x.id==id);if(item==null)hardware.Add(new HardwareLevel{id=id,level=level});else item.level=level;}
        // Compatibility helpers for existing editor fixtures. Runtime hardware uses definition IDs.
        public int cpu { get=>Level("cpu");set=>SetLevel("cpu",value); }
        public int bandwidth { get=>Level("bandwidth");set=>SetLevel("bandwidth",value); }
        public int memory { get=>Level("memory");set=>SetLevel("memory",value); }
    }
    [Serializable] public sealed class QueuedTool
    {
        public string target,actionId;
        public ToolKind kind {get=>Enum.TryParse<ToolKind>(actionId,true,out var value)?value:ToolKind.Scan;set=>actionId=value.ToString().ToLowerInvariant();}
        public QueuedTool(){}
        public QueuedTool(ToolKind tool,string id){kind=tool;target=id;}
        public QueuedTool(string action,string id){actionId=action;target=id;}
    }
    [Serializable] public sealed class ToolTask
    {
        public string target,actionId;
        public float duration,elapsed,trace;
        public ToolKind kind {get=>Enum.TryParse<ToolKind>(actionId,true,out var value)?value:ToolKind.Scan;set=>actionId=value.ToString().ToLowerInvariant();}
    }
    [Serializable] public sealed class Operation
    {
        public string contractId,entryNode,lastControlled;
        public int seed,generationVersion;
        public List<NetNode> nodes;
        public List<NetLink> links;
        public List<string> log=new List<string>();
        public List<QueuedTool> queue=new List<QueuedTool>();
        public List<CounterState> counters=new List<CounterState>();
        public List<StatusState> statuses=new List<StatusState>();
        public ToolTask task;
        public float trace,elapsed;
        public bool paused;
        public float decoyRemaining=>statuses?.Where(s=>s.preventsTrace).Select(s=>s.remaining).DefaultIfEmpty(0).Max()??0;
    }
    [Serializable] public sealed class OperationReport
    {
        public string title,company,verdict,narrative;
        public bool success;
        public float trace;
        public int credits,xp,day,bonusFiles;
        public List<string> log=new List<string>();
    }
    public sealed class ToolPreview
    {
        public bool allowed, willIsolate, losesQuietBonus;
        public string reason;
        public float duration,trace,projectedTrace;
    }

    public sealed partial class GameSimulation
    {
        public GameCatalog Catalog {get;}
        public Career Career {get;private set;}
        public Operation Op=>Career.operation;
        public Contract Job {get;private set;}
        public GraphDocument KnownGraph {get;private set;}
        public GraphScene Scene {get;private set;}
        public OperationReport LastReport {get;private set;}
        public int Revision {get;private set;}
        public event Action Changed;
        private int effectDepth,effectCount;
        public bool Active=>Op!=null;
        public bool CanStopTask=>Active&&Op.task!=null&&Catalog.Data.career.allowTaskCancellation;
        public int ResearchCount=>Career.unlocked.Except(Catalog.Data.career.initialTechnologies.Select(t=>t.id)).Count();
        public int ResearchTotal=>Catalog.Data.technologies.Length-Catalog.Data.career.initialTechnologies.Length;
        public float RefundFraction=>Catalog.Data.career.refundFraction;
        public int Slots=>Mathf.Max(0,Mathf.FloorToInt(Stat("slots")));
        public int QueueCapacity=>Mathf.Max(0,Mathf.FloorToInt(Stat("queueCapacity")));
        public float TraceLimit=>Catalog.Data.career.traceLimit;
        public float TraceWarning=>Catalog.Data.career.traceWarning;
        public float TraceDanger=>Catalog.Data.career.traceDanger;
        public float TraceFraction=>Active?Op.trace/TraceLimit:0;
        public float[] SimulationSpeeds=>Catalog.Data.career.simulationSpeeds.ToArray();
        public bool StoryComplete=>Catalog.Contracts(Career).Where(x=>!x.recurring).All(x=>Career.completed.Contains(x.id));
        public string Rank=>Catalog.Data.career.ranks.Where(x=>Career.jobs>=x.jobs).OrderBy(x=>x.jobs).LastOrDefault()?.title??string.Empty;
        public bool Has(string id)=>Career.unlocked.Contains(id);
        public bool Equipped(string id)=>Has(id)&&Career.equipped.Contains(id);
        public NetNode Node(string id)=>Op?.nodes.FirstOrDefault(n=>n.id==id);
        public IEnumerable<NetNode> Neighbors(NetNode node)=>node==null||Op==null?Enumerable.Empty<NetNode>():Op.links.Where(l=>l.a==node.id||l.b==node.id).Select(l=>Node(l.a==node.id?l.b:l.a));
        public bool Reachable(NetNode node)=>node!=null&&node.known&&(node.controlled||Neighbors(node).Any(n=>n.controlled));
        public int ControlledDistance(NetNode target)
        {
            if(!Active||target==null||!target.known)return int.MaxValue;
            var visited=new HashSet<string>{target.id};var queue=new Queue<(NetNode node,int distance)>();queue.Enqueue((target,0));
            while(queue.Count>0)
            {
                var item=queue.Dequeue();if(item.node.controlled)return item.distance;
                foreach(var neighbor in Neighbors(item.node))if(neighbor.known&&visited.Add(neighbor.id))queue.Enqueue((neighbor,item.distance+1));
            }
            return int.MaxValue;
        }
        private ContractDefinition Definition=>Catalog.Contract(Job?.id);
        public int ObjectiveCount=>!Active?0:Definition.objectives.Count(o=>o.complete.Test(Context(Node(o.nodeId))));
        public int ObjectiveTotal=>Active?Definition.objectives.Length:0;
        public bool ObjectiveComplete=>Active&&ObjectiveTotal>0&&ObjectiveCount==ObjectiveTotal;
        public bool ObjectiveDone(NetNode node)=>Active&&Definition.objectives.Where(o=>o.nodeId==node?.id).All(o=>o.complete.Test(Context(node)));
        public float Progress=>Op?.task==null?0:Mathf.Clamp01(Op.task.elapsed/Op.task.duration);
        public int Scanners=>!Active?0:Op.nodes.Count(n=>Catalog.NodeType(n.typeId).auras.Any(a=>a.modifier.stat=="tracePressure"&&a.source.Test(Context(n))));
        public GameSimulation(Career career=null):this(GameCatalog.Default,career){}
        public GameSimulation(GameCatalog catalog,Career career=null)
        {
            Catalog=catalog??throw new ArgumentNullException(nameof(catalog));Career=career??catalog.CreateCareer();Normalize();ValidateCareer(Career,Catalog);
            if(Active){Job=Catalog.DescribeContract(Catalog.Contract(Op.contractId),Career);Job.seed=Op.seed;Op.paused=true;SyncStatuses();RefreshGraph();}
        }
        private void Normalize()
        {
            Career.completed??=new List<string>();Career.unlocked??=new List<string>();Career.equipped??=new List<string>();Career.news??=new List<string>();
            Career.reports??=new List<OperationReport>();Career.lore??=new List<LoreFragment>();Career.hardware??=new List<HardwareLevel>();
            Career.waypoints??=new List<NetworkWaypoint>();
            if(!Career.initialized)
            {
                var defaults=Catalog.Data.career;if(Career.credits<0)Career.credits=defaults.credits;if(Career.xp==0)Career.xp=defaults.xp;if(Career.day==1)Career.day=defaults.day;
                foreach(var tech in defaults.initialTechnologies)if(!Has(tech.id))Career.unlocked.Add(tech.id);
                if(Career.news.Count==0)Career.news.Add(defaults.welcome);Career.initialized=true;
            }
            if(Career.researchSpent<0)
            {
                var purchased=Career.unlocked.Except(Catalog.Data.career.initialTechnologies.Select(t=>t.id)).ToArray();
                int earnedInvestment=Career.totalEarnedXp+Catalog.Data.career.xp-Career.xp;
                Career.researchSpent=purchased.Length==0?0:earnedInvestment>0?earnedInvestment:purchased.Sum(id=>Catalog.Technology(id)?.cost??0);
            }
            if(Op!=null&&string.IsNullOrEmpty(Op.contractId)&&(Op.nodes==null||Op.nodes.Count==0)&&(Op.links==null||Op.links.Count==0)&&Op.trace==0&&Op.elapsed==0)Career.operation=null;
            if(Active)
            {
                Op.queue??=new List<QueuedTool>();Op.log??=new List<string>();Op.counters??=new List<CounterState>();Op.statuses??=new List<StatusState>();
                if(Op.task!=null&&string.IsNullOrEmpty(Op.task.actionId)&&Op.task.duration==0)Op.task=null;
            }
        }
        public static void ValidateCareer(Career career,GameCatalog catalog)
        {
            void Require(bool pass,string message){if(!pass)throw new InvalidDataException("Career v2: "+message+". The save has not been overwritten.");}
            Require(career.version==2,"unsupported save version");Require(career.credits>=0&&career.xp>=0&&career.day>=1&&career.researchSpent>=0,"invalid economy values");
            Require(career.completed.All(id=>catalog.Contract(id)!=null&&!catalog.Contract(id).recurring),"unknown completed contract");
            Require(career.unlocked.All(id=>catalog.Technology(id)!=null)&&career.unlocked.Distinct().Count()==career.unlocked.Count,"unknown or duplicate technology");
            Require(career.equipped.All(id=>career.unlocked.Contains(id)&&catalog.Technology(id).special)&&career.equipped.Distinct().Count()==career.equipped.Count,"invalid equipped technology");
            Require(career.hardware.All(h=>catalog.Data.hardware.Any(d=>d.id==h.id&&h.level>=0&&h.level<=d.tiers.Length))&&career.hardware.Select(h=>h.id).Distinct().Count()==career.hardware.Count,"unknown hardware or invalid tier");
            Require(career.waypoints!=null&&career.waypoints.All(w=>w!=null&&catalog.Contract(w.contractId)!=null&&!string.IsNullOrEmpty(w.nodeId)&&w.color>=0&&w.color<WaypointColors.Length&&(w.label?.Length??0)<=40),"invalid waypoint");
            Require(career.waypoints.GroupBy(w=>(w.contractId,w.seed)).All(g=>g.Count()<=4&&g.Select(w=>w.nodeId).Distinct().Count()==g.Count()&&g.Count(w=>w.showPath)<=1),"invalid waypoint capacity or route");
            var op=career.operation;if(op==null)return;
            Require(catalog.Contract(op.contractId)!=null,"unknown active contract "+op.contractId);
            Require(op.nodes!=null&&op.nodes.Count>0&&op.links!=null,"missing active network");var ids=new HashSet<string>(op.nodes.Select(n=>n.id));
            Require(ids.Count==op.nodes.Count&&op.nodes.All(n=>catalog.Data.nodeTypes.Any(t=>t.id==n.typeId)),"unknown or duplicate network node");
            Require(ids.Contains(op.entryNode)&&ids.Contains(op.lastControlled)&&op.links.All(l=>ids.Contains(l.a)&&ids.Contains(l.b)),"missing network reference");
            Require(catalog.Contract(op.contractId).objectives.All(o=>ids.Contains(o.nodeId)),"missing objective target");
            Require(op.task==null||(catalog.Action(op.task.actionId)!=null&&(catalog.Action(op.task.actionId).operatorTarget||ids.Contains(op.task.target))&&op.task.duration>0&&Finite(op.task.duration)&&Finite(op.task.elapsed)&&Finite(op.task.trace)&&op.task.trace>=0&&op.task.elapsed>=0&&op.task.elapsed<=op.task.duration),"invalid running action");
            Require(op.queue.All(q=>catalog.Action(q.actionId)!=null&&(catalog.Action(q.actionId).operatorTarget||ids.Contains(q.target))),"unknown queued action or target");
            Require(op.statuses.All(s=>catalog.Data.statuses.Any(d=>d.id==s.id)&&(string.IsNullOrEmpty(s.target)||ids.Contains(s.target))&&s.remaining>0&&Finite(s.remaining)),"unknown or invalid status");
            Require(Finite(op.trace)&&op.trace>=0&&Finite(op.elapsed)&&op.elapsed>=0,"invalid operation time or trace");
        }
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        internal RuleContext Context(NetNode node=null,ActionDefinition action=null)=>new RuleContext{Catalog=Catalog,Simulation=this,Career=Career,Contract=Job,Node=node,Action=action};
        public bool Start(Contract job)
        {
            var definition=Catalog.Contract(job?.id);
            if(Active||definition==null||Career.completed.Count<definition.requiredWins||(!definition.recurring&&Career.completed.Contains(definition.id)))return false;
            Job=JsonUtility.FromJson<Contract>(JsonUtility.ToJson(job));Job.networkId=definition.network.id;Job.lore=Catalog.DescribeContract(definition,Career).lore;
            var network=NetworkGenerator.Generate(Job,Career,Catalog);
            Career.operation=new Operation{contractId=Job.id,seed=Job.seed,nodes=network.nodes,links=network.links,generationVersion=NetworkGenerator.Version,entryNode=definition.network.entryNode,lastControlled=definition.network.entryNode};
            foreach(var goal in definition.objectives)Node(goal.nodeId).objective=true;
            Dispatch(RuleEvent.OperationStarted,Context());Log("TUNNEL OPEN // "+Job.company+". Gateway secured.");Log("CLIENT // "+Job.objective);RefreshGraph();Touch();return true;
        }
        public void SetPaused(bool paused){if(!Active)return;Op.paused=paused;Touch();}
        public string PreferredNode(Contract job,bool preview=false){var n=Catalog.Network(job.networkId??Catalog.Contract(job.id).network.id);return preview?n.previewNode:n.preferredNode;}
        public float Stat(string stat)=>Stat(stat,Context());
        internal float Stat(string stat,RuleContext c)=>ApplyModifiers(stat,stat=="security"?(c.Node?.security??0):Catalog.BaseStat(stat),c);
        private IEnumerable<Modifier> Modifiers(string stat,RuleContext context)
        {
            foreach(var m in Catalog.Data.modifiers)if(m.stat==stat)yield return m;
            foreach(var t in Catalog.Data.technologies.Where(t=>Has(t.id)&&(!t.special||Equipped(t.id))))foreach(var m in t.modifiers)if(m.stat==stat)yield return m;
            foreach(var h in Catalog.Data.hardware)for(int i=0;i<Career.Level(h.id);i++)foreach(var m in h.tiers[i].modifiers)if(m.stat==stat)yield return m;
            if(!Active)yield break;var seen=new HashSet<string>();
            foreach(var source in Op.nodes)foreach(var aura in Catalog.NodeType(source.typeId).auras)
            {
                if(aura.modifier.stat!=stat||!aura.source.Test(context.At(source)))continue;
                if(aura.adjacentOnly&&(context.Node==null||!Neighbors(source).Any(n=>n.id==context.Node.id)))continue;
                if(!aura.modifier.when.Test(context))continue;if(!aura.stack&&!seen.Add(aura.id))continue;yield return aura.modifier;
            }
        }
        private float ApplyModifiers(string stat,float basis,RuleContext c)
        {
            c.Enter();try{foreach(var m in Modifiers(stat,c).OrderBy(m=>m.priority))if(m.when.Test(c))basis=RuleMath.Apply(m.operation,basis,m.value.Evaluate(c));return basis;}finally{c.Leave();}
        }
        public int EffectiveSecurity(NetNode node)=>node==null?0:Mathf.Max(Mathf.RoundToInt(Stat("minimumSecurity")),Mathf.RoundToInt(Stat("security",Context(node))));
        private bool Granted(ActionDefinition action)=>action.initiallyAvailable||Catalog.Data.technologies.Any(t=>Has(t.id)&&(!t.special||Equipped(t.id))&&t.grants.Any(a=>a.id==action.id));
        public IReadOnlyList<ActionInfo> AvailableActions(string targetId=null)
        {
            if(!Active)return Array.Empty<ActionInfo>();var node=Node(targetId);
            if(targetId!=null&&node==null)return Array.Empty<ActionInfo>();
            return Catalog.Actions.Where(a=>a.operatorTarget==(node==null)&&Granted(a.Definition)&&a.Definition.visible.Test(Context(node,a.Definition))).OrderBy(a=>a.priority).ToArray();
        }
        public string ActionResearchHint(string targetId)
        {
            if(!Active)return null;var node=Node(targetId);
            var locked=Catalog.Data.actions.Where(a=>!a.operatorTarget&&!Granted(a)&&a.visible.Test(Context(node,a))).ToArray();
            var tech=Catalog.Data.technologies.FirstOrDefault(t=>t.grants.Any(a=>locked.Contains(a)));
            return tech==null?null:(Has(tech.id)?"Equip ":"Research ")+tech.title+" between contracts for another approach.";
        }
        private IEnumerable<Requirement> Requirements(ActionDefinition action,NetNode node)
        {
            foreach(var r in action.requirements)yield return r;
            foreach(var rule in Definition.actionRules.Where(r=>r.action.id==action.id))foreach(var r in rule.requirements)yield return r;
            foreach(var goal in Definition.objectives.Where(o=>o.nodeId==node?.id&&o.action?.id==action.id))foreach(var r in goal.actionRequirements)yield return r;
        }
        public string ActionTitle(string actionId,string targetId=null)
        {
            var action=Catalog.Action(actionId);var objective=Active?Definition.objectives.FirstOrDefault(o=>o.nodeId==targetId&&o.action?.id==actionId):null;
            return string.IsNullOrEmpty(objective?.actionLabel)?action?.title:objective.actionLabel;
        }
        public ToolPreview Preview(string actionId,string targetId=null)
        {
            var p=new ToolPreview();var a=Catalog.Action(actionId);var node=Node(targetId);
            if(!Active||a==null||(!a.operatorTarget&&node==null)){p.reason="Connect to a contract and select a valid action target.";return p;}
            var c=Context(a.operatorTarget?null:node,a);
            if(!a.visible.Test(c)){p.reason="This action does not apply to this target.";return p;}
            if(!Granted(a)){p.reason="Research and equip "+string.Join(" / ",Catalog.Data.technologies.Where(t=>t.grants.Any(x=>x.id==a.id)).Select(t=>t.title))+".";return p;}
            float limit=a.useLimit.Evaluate(c);if(limit>0&&Counter("uses:"+a.id)>=limit){p.reason="This ability has no uses remaining in this operation.";return p;}
            foreach(var r in Requirements(a,node))if(!r.condition.Test(c)){p.reason=Catalog.Format(r.message,c);return p;}
            p.duration=ApplyModifiers("duration",a.duration.Evaluate(c),c);c.Duration=p.duration;
            float trace=ApplyModifiers("baseTrace",a.baseTrace.Evaluate(c),c);p.trace=ApplyModifiers("trace",trace+p.duration*Stat("tracePressure",c),c);
            if(a.traceFree||Stat("traceFree",c)>0)p.trace=0;
            if(!Finite(p.duration)||!Finite(p.trace)||p.duration<0||(!a.operatorTarget&&p.duration==0)||p.trace<0)throw new InvalidOperationException("Invalid duration/trace in action "+a.id);
            // A conservative forecast: protection can lower this, never increase it.
            float protectedSeconds=Mathf.Max(0,Op.decoyRemaining-Catalog.Data.career.maxTick);
            float exposedFraction=p.duration<=0?1:Mathf.Clamp01((p.duration-protectedSeconds)/p.duration);
            p.projectedTrace=Op.trace+p.trace*exposedFraction*Mathf.Max(0,Stat("traceGrowth"));
            p.willIsolate=p.projectedTrace>=TraceLimit;p.losesQuietBonus=Op.trace<Job.quietLimit&&p.projectedTrace>=Job.quietLimit;
            p.allowed=true;p.reason=Op.task!=null&&a.queueable?"Queue after the current task.":"Ready to execute.";return p;
        }
        public int? UsesRemaining(string actionId,string targetId=null)
        {
            var action=Catalog.Action(actionId);if(!Active||action==null)return null;
            float limit=action.useLimit.Evaluate(Context(Node(targetId),action));return limit<=0?(int?)null:Mathf.Max(0,Mathf.CeilToInt(limit)-Counter("uses:"+action.id));
        }
        public bool CanQueue(string actionId,string targetId)
        {
            var a=Catalog.Action(actionId);var n=Node(targetId);
            if(!Active||Op.task==null||a==null||!a.queueable||a.operatorTarget||n==null||Op.queue.Count>=QueueCapacity)return false;
            if(Op.queue.Any(q=>q.actionId==actionId&&q.target==targetId)||(Op.task.actionId==actionId&&Op.task.target==targetId))return false;
            var c=Context(n,a);if(!Granted(a)||!a.visible.Test(c)||!a.queueRequirements.All(r=>r.condition.Test(c)))return false;
            float limit=a.useLimit.Evaluate(c);
            int reserved=(Op.task.actionId==actionId?1:0)+Op.queue.Count(q=>q.actionId==actionId);
            if(limit>0&&Counter("uses:"+actionId)+reserved>=limit)return false;
            if(a.futureNeighborQueue&&!Reachable(n)&&!(Stat("futureQueue",c)>0&&Neighbors(Node(Op.task.target)).Any(x=>x.id==targetId)))return false;return true;
        }
        public bool Execute(string actionId,string targetId=null)
        {
            var action=Catalog.Action(actionId);if(!Active||action==null)return false;
            if(Op.task!=null&&action.queueable&&!action.operatorTarget)
            {
                if(!CanQueue(actionId,targetId))return false;Op.queue.Add(new QueuedTool(actionId,targetId));Log("QUEUED // "+ActionTitle(actionId,targetId)+" : "+Node(targetId).label);Touch();return true;
            }
            var p=Preview(actionId,targetId);if(!p.allowed)return false;
            var task=new ToolTask{actionId=actionId,target=action.operatorTarget?null:targetId,duration=p.duration,trace=p.trace};
            if(action.operatorTarget&&p.duration==0){Op.trace=Mathf.Min(TraceLimit,Op.trace+p.trace);CompleteTask(task);RefreshGraph();Touch();return true;}
            if(Op.task!=null)return false;Op.task=task;Log("RUN // "+ActionTitle(actionId,targetId)+" : "+(Node(targetId)?.label??"operator"));Touch();return true;
        }
        private string LegacyAction(ToolKind kind,string target)
        {
            string group=kind.ToString().ToLowerInvariant();var n=Node(target);return Catalog.Data.actions.FirstOrDefault(a=>a.legacyGroup==group&&a.visible.Test(Context(n,a)))?.id??group;
        }
        public ToolPreview Preview(ToolKind kind,string id)=>Preview(LegacyAction(kind,id),id);
        public bool Execute(ToolKind kind,string id)=>Execute(LegacyAction(kind,id),id);
        public bool Decoy()=>Execute("decoy");
        public void ClearQueue(){if(!Active)return;Op.queue.Clear();Touch();}
        public bool StopTask()
        {
            if(!CanStopTask)return false;
            Log("TOOL STOPPED // "+ActionTitle(Op.task.actionId,Op.task.target)+". Existing trace remains.");
            Op.task=null;Op.queue.Clear();Touch();return true;
        }
        public void Tick(float delta)
        {
            if(!Active||Op.paused||delta<=0||!Finite(delta))return;
            delta=Mathf.Min(delta,Catalog.Data.career.maxTick);Op.elapsed+=delta;bool expired=false;
            foreach(var status in Op.statuses.ToArray())
            {
                status.remaining=Mathf.Max(0,status.remaining-delta);if(status.remaining>0)continue;
                Op.statuses.Remove(status);expired=true;SyncStatuses();
                Log("STATUS EXPIRED // "+Catalog.Data.statuses.First(s=>s.id==status.id).title+" : "+(Node(status.target)?.label??"operator"));Dispatch(RuleEvent.StatusExpired,Context(Node(status.target)));
            }
            SyncStatuses();if(expired)Touch();var task=Op.task;
            if(task==null){if(!Op.statuses.Any(s=>s.preventsTrace))Op.trace+=delta*Stat("idleTracePerSecond");CheckTrace();return;}
            float step=Mathf.Min(delta,task.duration-task.elapsed);
            if(!Op.statuses.Any(s=>s.preventsTrace))Op.trace+=step/task.duration*task.trace*Stat("traceGrowth");task.elapsed+=step;
            if(CheckTrace())return;if(task.elapsed+.0001f<task.duration)return;
            CompleteTask(task);Op.task=null;RefreshGraph();Touch();
            while(Active&&Op.task==null&&Op.queue.Count>0)
            {
                var next=Op.queue[0];Op.queue.RemoveAt(0);if(!Execute(next.actionId,next.target))Log("SKIPPED // Queued action no longer meets its requirements.");
            }
        }
        private bool CheckTrace(){if(Op.trace<TraceLimit)return false;Op.trace=TraceLimit;Disconnect(true);return true;}
        private void CompleteTask(ToolTask task)
        {
            var action=Catalog.Action(task.actionId);var c=Context(Node(task.target),action);c.Duration=task.duration;
            AddCounter("uses:"+action.id,1);ApplyEffects(action.effects,c);Dispatch(RuleEvent.ActionCompleted,c);
            if(ObjectiveComplete)Log("ALL OBJECTIVES SECURED // Disconnect to deliver and collect your reward. Optional archives remain available.");
        }
        internal void ApplyEffects(IEnumerable<Effect> effects,RuleContext c)
        {
            if(effectDepth++==0)effectCount=0;
            try{if(effectDepth>64)throw new InvalidOperationException("Gameplay trigger depth limit exceeded.");foreach(var effect in effects){if(++effectCount>4096)throw new InvalidOperationException("Gameplay effect execution budget exceeded.");effect.Apply(c);}}
            finally{effectDepth--;}
        }
        private void Dispatch(RuleEvent evt,RuleContext c)
        {
            var triggers=Catalog.Data.triggers.Concat(Catalog.Data.technologies.Where(t=>Has(t.id)&&(!t.special||Equipped(t.id))).SelectMany(t=>t.triggers)).ToArray();
            foreach(var t in triggers)
            {
                if(t.on!=evt||(!string.IsNullOrEmpty(t.onceKey)&&Counter(t.onceKey)>0)||!t.when.Test(c))continue;
                if(!string.IsNullOrEmpty(t.onceKey))AddCounter(t.onceKey,1);ApplyEffects(t.effects,c);
            }
        }
        public int Counter(string key)=>Op?.counters.FirstOrDefault(x=>x.key==key)?.value??0;
        internal void AddCounter(string key,int amount){var item=Op.counters.FirstOrDefault(x=>x.key==key);if(item==null)Op.counters.Add(new CounterState{key=key,value=amount});else item.value+=amount;}
        public float StatusRemaining(string id,string target=null)=>Op?.statuses.Where(s=>s.id==id&&s.target==target).Select(s=>s.remaining).DefaultIfEmpty(0).Max()??0;
        internal void ApplyStatus(StatusDefinition status,string target,float duration)
        {
            if(duration<=0||!Finite(duration))throw new InvalidOperationException("Invalid status duration for "+status.id);
            var state=Op.statuses.FirstOrDefault(s=>s.id==status.id&&s.target==target);
            if(state==null){state=new StatusState{id=status.id,target=target,preventsTrace=status.preventsTrace,disablesDefense=status.disablesDefense};Op.statuses.Add(state);}state.remaining=duration;SyncStatuses();
        }
        private void SyncStatuses(){foreach(var n in Op.nodes)n.suppressed=Op.statuses.Where(s=>s.target==n.id&&s.disablesDefense).Select(s=>s.remaining).DefaultIfEmpty(0).Max();}
        internal IEnumerable<NetNode> Select(NodeSelector selector,RuleContext c)
        {
            IEnumerable<NetNode> nodes;
            switch(selector.scope)
            {
                case SelectionScope.All:nodes=Op.nodes;break;
                case SelectionScope.Neighbors:nodes=Neighbors(c.Node);break;
                case SelectionScope.Entry:nodes=new[]{Node(Op.entryNode)};break;
                case SelectionScope.Named:nodes=new[]{Node(selector.nodeId)};break;
                default:nodes=new[]{c.Node};break;
            }
            return nodes.Where(n=>n!=null&&selector.filter.Test(c.At(n)));
        }
        internal void SetState(NetNode node,NodeState state,bool value,RuleContext context)
        {
            switch(state)
            {
                case NodeState.Known:node.known=value;break;
                case NodeState.Scanned:node.scanned=value;break;
                case NodeState.Collected:node.collected=value;break;
                case NodeState.Objective:node.objective=value;break;
                case NodeState.Bonus:node.bonus=value;break;
                case NodeState.Controlled:
                    bool changed=node.controlled!=value;node.controlled=value;if(value&&changed){Op.lastControlled=node.id;Dispatch(RuleEvent.ControlEstablished,context.At(node));}break;
                default:throw new InvalidOperationException("This state is computed and cannot be assigned: "+state);
            }
        }
        internal void Reveal(NetNode source,int radius)
        {
            if(source==null)return;var visited=new HashSet<string>{source.id};var frontier=new List<NetNode>{source};source.known=true;
            for(int i=0;i<radius;i++){var next=new List<NetNode>();foreach(var n in frontier)foreach(var neighbor in Neighbors(n))if(visited.Add(neighbor.id)){neighbor.known=true;next.Add(neighbor);}frontier=next;}
        }
        public void RefreshGraph()
        {
            if(!Active)return;KnownGraph=new GraphDocument("operation-"+Op.contractId,Job.title);
            foreach(var n in Op.nodes.Where(n=>n.known))KnownGraph.UpsertNode(new GraphNodeRecord(n.id,n.kind.ToString(),n.label,new Dictionary<string,GraphValue>{["controlled"]=GraphValue.From(n.controlled),["objective"]=GraphValue.From(n.objective)}));
            foreach(var link in Op.links.Where(l=>Node(l.a).known&&Node(l.b).known))
            {
                KnownGraph.UpsertEdge(new GraphEdgeRecord(link.a+">"+link.b,link.a,link.b,"network"));KnownGraph.UpsertEdge(new GraphEdgeRecord(link.b+">"+link.a,link.b,link.a,"network"));
            }
            Scene=new GraphScene(KnownGraph,new GraphSchema());foreach(var n in Op.nodes.Where(n=>n.known)){Scene.Nodes[n.id].Position=n.position;Scene.Nodes[n.id].Pinned=true;}
        }
        public GraphPathResult Route(string id,bool controlledOnly=false)=>!Active||Scene==null?null:GraphPathfinder.Find(Scene,Op.entryNode,id,controlledOnly?e=>Node(e.From).controlled&&(Node(e.To).controlled||e.To==id):(Func<IGraphEdge,bool>)null);
        public OperationReport Disconnect(bool caught=false)
        {
            if(!Active)return null;bool success=!caught&&ObjectiveComplete;
            var outcome=caught?OperationOutcome.Caught:success?OperationOutcome.Delivered:OperationOutcome.Aborted;
            var policy=Catalog.Data.career.outcomes.Single(p=>p.outcome==outcome);
            var report=new OperationReport{title=Job.title,company=Job.company,trace=Op.trace,success=success,day=Career.day,verdict=policy.verdict,narrative=Catalog.Format(policy.narrative,Context()),log=new List<string>(Op.log),bonusFiles=Op.nodes.Count(n=>n.bonus&&n.collected)};
            if(policy.bankRewards)
            {
                var c=Context();foreach(var reward in Catalog.Data.rewards.rules)if(reward.when.Test(c)){report.credits+=Mathf.RoundToInt(reward.credits.Evaluate(c));report.xp+=Mathf.RoundToInt(reward.xp.Evaluate(c));}
                Career.credits+=report.credits;Career.xp+=report.xp;Career.totalEarnedXp+=report.xp;
            }
            if(policy.bankLore)foreach(var n in Op.nodes.Where(n=>n.bonus&&n.collected)){var fragment=LoreContent.Find(Job,n.loreId);if(fragment!=null&&!Career.lore.Any(f=>f.id==fragment.id))Career.lore.Add(fragment);}
            if(policy.recordCompletion)
            {
                Career.jobs++;
                if(!Job.recurring&&!Career.completed.Contains(Job.id))Career.completed.Add(Job.id);
                Career.news.Insert(0,"DAY "+Career.day.ToString("00")+" // "+Job.aftermath);
            }
            Career.day+=policy.dayAdvance;
            Career.reports.Insert(0,report);if(Career.reports.Count>Catalog.Data.career.reportLimit)Career.reports.RemoveRange(Catalog.Data.career.reportLimit,Career.reports.Count-Catalog.Data.career.reportLimit);
            LastReport=report;Career.operation=null;KnownGraph=null;Scene=null;Touch();return report;
        }
        public const int DebugGrantAmount=1_000_000;
        private static int DebugGrant(int current)=>(int)Math.Min(int.MaxValue,(long)current+DebugGrantAmount);
        public void DebugAddCredits(){Career.credits=DebugGrant(Career.credits);Touch();}
        public void DebugAddXp()
        {
            Career.xp=DebugGrant(Career.xp);Career.totalEarnedXp=DebugGrant(Career.totalEarnedXp);Touch();
        }
        public void DebugUnlockAllTech()
        {
            foreach(var tech in Catalog.Technologies)if(!Has(tech.id))Career.unlocked.Add(tech.id);
            Touch();
        }
        public bool BuyTech(string id)
        {
            var tech=Catalog.Technology(id);
            if((Active&&!Catalog.Data.career.researchDuringOperations)||tech==null||Has(id)||Career.xp<tech.cost||!tech.parents.All(t=>Has(t.id)))return false;
            Career.xp-=tech.cost;Career.researchSpent+=tech.cost;Career.unlocked.Add(id);if(tech.special&&Career.equipped.Count<Slots)Career.equipped.Add(id);Touch();return true;
        }
        public bool ToggleEquip(string id)
        {
            if((Active&&!Catalog.Data.career.equipDuringOperations)||!Has(id)||Catalog.Technology(id)?.special!=true)return false;
            if(Career.equipped.Contains(id))Career.equipped.Remove(id);else if(Career.equipped.Count<Slots)Career.equipped.Add(id);else return false;Touch();return true;
        }
        public bool RefundResearch()
        {
            if(Active&&!Catalog.Data.career.researchDuringOperations)return false;var initial=Catalog.Data.career.initialTechnologies.Select(t=>t.id).ToArray();
            Career.xp+=Mathf.FloorToInt(Career.researchSpent*Catalog.Data.career.refundFraction);Career.researchSpent=0;Career.unlocked=initial.ToList();Career.equipped.Clear();Career.waypoints.Clear();Touch();return true;
        }
        public int UpgradePrice(string id){var h=Catalog.Data.hardware.FirstOrDefault(x=>x.id==id);var level=Career.Level(id);return h!=null&&level<h.tiers.Length?h.tiers[level].price:0;}
        public ToolPreview PreviewUpgrade(string id)
        {
            var p=new ToolPreview();var h=Catalog.Data.hardware.FirstOrDefault(x=>x.id==id);
            if(h==null){p.reason="Unknown hardware.";return p;}
            if(Active&&!Catalog.Data.career.hardwareDuringOperations){p.reason="Install hardware between contracts.";return p;}
            if(Career.Level(id)>=h.tiers.Length){p.reason="Maximum tier installed.";return p;}
            foreach(var r in h.requirements)if(!r.condition.Test(Context())){p.reason=Catalog.Format(r.message,Context());return p;}
            if(Career.credits<UpgradePrice(id)){p.reason="Need "+(UpgradePrice(id)-Career.credits)+" more credits.";return p;}
            p.allowed=true;p.reason="Ready to install.";return p;
        }
        public bool Upgrade(string id)
        {
            var h=Catalog.Data.hardware.FirstOrDefault(x=>x.id==id);
            if(!PreviewUpgrade(id).allowed)return false;
            Career.credits-=UpgradePrice(id);Career.SetLevel(id,Career.Level(id)+1);Touch();return true;
        }
        public int UpgradePrice(int index)=>index>=0&&index<Catalog.Hardware.Count?UpgradePrice(Catalog.Hardware[index].id):0;
        public bool Upgrade(int index)=>index>=0&&index<Catalog.Hardware.Count&&Upgrade(Catalog.Hardware[index].id);
        public string RewardDescription=>Catalog.Format("Below "+Job?.quietLimit+"% trace: +{quietCredits} cr / +{quietXp} XP\nOptional archives: +{archiveCredits} cr / +{archiveXp} XP each (XP capped at +{archiveXpCap})",Context());
        public string ArchiveDescription=>Catalog.Format("+{archiveCredits} cr / +{archiveXp} XP per archive; up to +{archiveXpCap} archive XP.",Context());
        public void Log(string message){if(!Active)return;int min=(int)Op.elapsed/60,sec=(int)Op.elapsed%60;Op.log.Add(min.ToString("00")+":"+sec.ToString("00")+"  "+message);if(Op.log.Count>Catalog.Data.career.logLimit)Op.log.RemoveAt(0);}
        private void Touch(){Revision++;Changed?.Invoke();}
    }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class CareerStorage
    {
        public static string SavePath=>Path.Combine(Application.persistentDataPath,"career-v2.json");
        public static string Warning {get;private set;}
        [Serializable] private sealed class SaveHeader { public int version; }
        public static Career Load(string path=null,GameCatalog catalog=null)
        {
            catalog??=GameCatalog.Default;path??=SavePath;Warning=null;if(!File.Exists(path))return catalog.CreateCareer();
            Career Read(string p)
            {
                string json=File.ReadAllText(p);var header=JsonUtility.FromJson<SaveHeader>(json);
                var career=JsonUtility.FromJson<Career>(json);if(career==null||header==null||header.version!=2)throw new InvalidDataException("Unsupported career save.");return new GameSimulation(catalog,career).Career;
            }
            try{return Read(path);}
            catch(InvalidDataException ex){Warning=ex.Message;throw;}
            catch(Exception ex)
            {
                Warning="The career save could not be read. "+ex.Message;
                try{File.Copy(path,path+".unreadable-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss"),true);}catch{}
                if(File.Exists(path+".bak"))try{return Read(path+".bak");}catch(Exception backup){Warning+=" Backup: "+backup.Message;}
                var fresh=catalog.CreateCareer();fresh.saveBlocked=true;return fresh;
            }
        }
        public static bool Reset(string path=null){var career=GameCatalog.Default.CreateCareer();return Save(career,path)&&Save(career,path);}
        public static bool Save(Career career,string path=null,GameCatalog catalog=null)
        {
            path??=SavePath;if(career.saveBlocked){Warning="Autosave is disabled to preserve an unreadable career. Use Reset Save to start a new one.";return false;}
            try
            {
                GameSimulation.ValidateCareer(career,catalog??GameCatalog.Default);Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));File.WriteAllText(path+".tmp",JsonUtility.ToJson(career,true));
                if(File.Exists(path))File.Replace(path+".tmp",path,path+".bak");else File.Move(path+".tmp",path);Warning=null;return true;
            }
            catch(Exception ex){Warning="Autosave could not write: "+ex.Message;Debug.LogWarning(Warning);return false;}
        }
    }
}
