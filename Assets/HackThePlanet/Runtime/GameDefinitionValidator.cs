using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HackThePlanet
{
    public static class GameDefinitionValidator
    {
        public static IEnumerable<GameplayDefinition> Definitions(GameDefinition root)
        {
            return new GameplayDefinition[]{root,root.career,root.rewards}.Concat(root.companies).Concat(root.contracts).Concat(root.technologies).Concat(root.actions).Concat(root.nodeTypes).Concat(root.networks).Concat(root.hardware).Concat(root.statuses).Concat(root.chassis).Concat(root.modules).Concat(new[]{root.rig});
        }
        public static void ThrowIfInvalid(GameDefinition root)
        {
            var errors=Validate(root);if(errors.Count>0)throw new InvalidOperationException("Invalid gameplay assets:\n"+string.Join("\n",errors));
        }
        public static IReadOnlyList<string> Validate(GameDefinition root)
        {
            var errors=new List<string>();
            if(root==null){errors.Add("Game: missing root asset.");return errors;}
            void Error(GameplayDefinition owner,string field,string message)=>errors.Add((owner==null?"Game":owner.name)+"."+field+": "+message);
            if(root.career==null||root.rewards==null||root.rig==null){Error(root,"career/rewards","both references are required");return errors;}
            if(root.companies==null||root.contracts==null||root.technologies==null||root.actions==null||root.nodeTypes==null||root.networks==null||root.hardware==null||root.statuses==null||root.chassis==null||root.modules==null){Error(root,"definitions","catalog arrays cannot be null");return errors;}
            var all=Definitions(root).ToArray();var registered=new HashSet<GameplayDefinition>(all.Where(x=>x!=null));
            foreach(var group in all.Where(x=>x!=null).GroupBy(x=>x.GetType()))
            {
                var ids=new HashSet<string>();foreach(var d in group)if(string.IsNullOrWhiteSpace(d.id)||!ids.Add(d.id))Error(d,"id","a nonempty, unique ID is required within this definition type");
            }
            foreach(var d in all)
            {
                if(d==null){Error(root,"definitions","missing asset reference");continue;}
                Walk(d,d.name,new HashSet<object>(),0,true,(obj,path)=>
                {
                    if(obj is GameplayDefinition reference&&!registered.Contains(reference))Error(d,path,"reference is not registered in the root catalog");
                    if(obj is float value&&(float.IsNaN(value)||float.IsInfinity(value)))Error(d,path,"value must be finite");
                    if(obj is BinaryNumber binary&&(binary.operation==Arithmetic.Divide||binary.operation==Arithmetic.Modulo)&&binary.right is ConstantNumber number&&number.value==0)Error(d,path,"division/modulo by zero");
                    if(obj is RandomNumber random&&(random.minimum>random.maximum||random.maximum==int.MaxValue))Error(d,path,"invalid inclusive random range");
                    if(obj is ApplyStatusEffect status&&status.status==null)Error(d,path,"status reference is required");
                    if(obj is ApplyStatusEffect timed&&timed.duration is ConstantNumber time&&time.value<=0)Error(d,path,"status duration must be positive");
                    if(obj is RigNeighborCondition adjacent&&(adjacent.minimum<0||(!string.IsNullOrEmpty(adjacent.kind)&&!root.modules.Any(m=>m!=null&&(m.id==adjacent.kind||m.family==adjacent.kind||m.branch==adjacent.kind)))))Error(d,path,"invalid adjacency selector or minimum");
                    if(obj is InputNumber neighbors&&neighbors.input==NumberInput.RigNeighborCount&&!string.IsNullOrEmpty(neighbors.key)&&!root.modules.Any(m=>m!=null&&(m.id==neighbors.key||m.family==neighbors.key||m.branch==neighbors.key)))Error(d,path,"unknown rig neighbor selector");
                    if(obj is ResearchCondition research&&research.technology==null)Error(d,path,"technology reference is required");
                    if(obj is Requirement requirement&&requirement.condition==null)Error(d,path,"requirement condition is required");
                    if(obj is Modifier modifier&&(modifier.value==null||modifier.when==null||string.IsNullOrWhiteSpace(modifier.stat)))Error(d,path,"modifier needs a stat, condition and value");
                    if(obj is SetStateEffect state&&(state.state==NodeState.DefenseActive||state.state==NodeState.Reachable))Error(d,path,"computed states cannot be assigned");
                },path=>Error(d,path,"cyclic or excessively nested rule reference"));
            }
            var statOwners=new Dictionary<string,GameplayDefinition>();var dependencies=new Dictionary<string,HashSet<string>>();
            var validStats=new HashSet<string>(root.career.stats.Select(s=>s.id).Concat(new[]{"security","duration","baseTrace","trace"}));
            foreach(var d in all.Where(d=>d!=null))Walk(d,d.name,new HashSet<object>(),0,true,(obj,path)=>
            {
                if(obj is StatNumber stat&&!validStats.Contains(stat.stat??""))Error(d,path,"unknown stat "+stat.stat);
                if(obj is NotCondition not&&not.condition==null)Error(d,path,"condition is required");
                if(obj is BinaryNumber number&&(number.left==null||number.right==null))Error(d,path,"both formula operands are required");
                if(obj is CompareCondition compare&&(compare.left==null||compare.right==null))Error(d,path,"both comparison operands are required");
                if(obj is ConditionalNumber conditional&&(conditional.when==null||conditional.yes==null||conditional.no==null))Error(d,path,"conditional formula needs all branches");
                if(obj is Modifier modifier&&!string.IsNullOrEmpty(modifier.stat))
                {
                    if(!validStats.Contains(modifier.stat))Error(d,path,"modifier targets an undefined stat "+modifier.stat);
                    if(!dependencies.TryGetValue(modifier.stat,out var edges)){edges=new HashSet<string>();dependencies.Add(modifier.stat,edges);statOwners[modifier.stat]=d;}
                    Walk(modifier,path,new HashSet<object>(),0,false,(child,_)=>{if(child is StatNumber referenced)edges.Add(referenced.stat);if(child is InputNumber input&&input.input==NumberInput.Security)edges.Add("security");},_=>{});
                }
            },_=>{});
            var statVisiting=new HashSet<string>();var statDone=new HashSet<string>();
            void Stat(string key)
            {
                if(statDone.Contains(key))return;if(!statVisiting.Add(key)){Error(statOwners[key],"modifiers","cyclic stat dependency involving "+key);return;}
                if(dependencies.TryGetValue(key,out var edges))foreach(var edge in edges)if(dependencies.ContainsKey(edge))Stat(edge);
                statVisiting.Remove(key);statDone.Add(key);
            }
            foreach(var key in dependencies.Keys)Stat(key);
            foreach(var a in root.actions.Where(x=>x!=null))
            {
                if(a.duration==null||a.baseTrace==null||a.useLimit==null||a.visible==null)Error(a,"formulas","duration, trace, visibility and use limit are required");
                if(a.duration is ConstantNumber duration&&(duration.value<0||(!a.operatorTarget&&!a.instant&&duration.value==0)))Error(a,"duration","targeted actions need positive duration; instant actions must target the operator");
                if(a.memoryCost<0||!Enum.IsDefined(typeof(MemoryMode),a.memoryMode)||(a.memoryMode==MemoryMode.None&&a.memoryCost!=0))Error(a,"memoryCost","invalid RAM cost or mode");
                if(a.memoryMode!=MemoryMode.Persistent&&(a.activeModifiers.Length>0||a.releaseOnTargetControl))Error(a,"memoryMode","sustained effects require Persistent mode");
                if(a.baseTrace is ConstantNumber trace&&trace.value<0)Error(a,"baseTrace","trace cannot be negative");
                if(a.useLimit is ConstantNumber uses&&uses.value<0)Error(a,"useLimit","use limit cannot be negative");
                if(a.requirements==null||a.queueRequirements==null||a.effects==null)Error(a,"rules","rule lists cannot be null");
            }
            var visiting=new HashSet<TechnologyDefinition>();var done=new HashSet<TechnologyDefinition>();
            void Tech(TechnologyDefinition t)
            {
                if(t==null||done.Contains(t))return;if(!visiting.Add(t)){Error(t,"parents","research dependency cycle");return;}
                if(t.waypointTier<0||t.waypointTier>4)Error(t,"waypointTier","waypoint tier must be between zero and four");
                if(t.cost<0)Error(t,"cost","research cost cannot be negative");foreach(var parent in t.parents)Tech(parent);visiting.Remove(t);done.Add(t);
            }
            foreach(var t in root.technologies)Tech(t);
            foreach(var h in root.hardware.Where(x=>x!=null))if(h.tiers==null||h.tiers.Any(t=>t==null||t.price<0))Error(h,"tiers","tiers must exist and prices cannot be negative");
            foreach(var n in root.networks.Where(x=>x!=null))
            {
                if(n.nodes==null||n.nodes.Length==0){Error(n,"nodes","network needs nodes");continue;}
                var ids=new HashSet<string>();foreach(var node in n.nodes)
                {
                    if(node==null||string.IsNullOrWhiteSpace(node.id)||!ids.Add(node.id)){Error(n,"nodes","node IDs must be unique and nonempty");continue;}
                    if(node.type==null)Error(n,"nodes."+node.id,"node type is required");
                }
                if(!ids.Contains(n.entryNode)||!ids.Contains(n.preferredNode)||!ids.Contains(n.previewNode))Error(n,"selection","entry, preferred and preview nodes must exist in the template");
                if(!n.nodes.Any(x=>x!=null&&x.id==n.entryNode&&x.known&&x.controlled))Error(n,"entryNode","entry must start known and controlled");
                if(n.layout==NetworkLayout.Authored)
                {
                    foreach(var edge in n.links)if(!ids.Contains(edge.a)||!ids.Contains(edge.b)||edge.a==edge.b)Error(n,"links","invalid endpoint or self link");
                    var seen=new HashSet<string>{n.entryNode};bool changed=true;
                    while(changed){changed=false;foreach(var edge in n.links)if(seen.Contains(edge.a)||seen.Contains(edge.b)){changed|=seen.Add(edge.a);changed|=seen.Add(edge.b);}}
                    if(seen.Count<ids.Count)Error(n,"links","network contains unreachable nodes");
                }
                else
                {
                    var r=n.routes;
                    if(r==null||r.relay==null||r.workstation==null||r.scanner==null||r.archive==null){Error(n,"routes","route role references are required");continue;}
                    if(r.extraIds.Length!=r.extraTypes.Length||r.extraIds.Length!=r.extraLabels.Length){Error(n,"routes.extraIds","IDs, types and labels need equal lengths");continue;}
                    foreach(var id in r.extraIds)if(!ids.Add(id))Error(n,"routes.extraIds","duplicate identity "+id);
                    foreach(var id in r.reserved.Concat(r.directRoute).Concat(r.sensors).Concat(r.secondaryTargets).Concat(r.archives).Concat(new[]{r.entrance,r.destination,r.sensorAttachment}))if(!ids.Contains(id))Error(n,"routes","missing template node "+id);
                    if(r.tiers.Length==0||r.directSecurity==null||r.routeSecurity==null||r.workstationWhen==null)Error(n,"routes.tiers","tiers and generation formulas are required");
                    foreach(var tier in r.tiers)
                    {
                        if(tier==null||tier.lengths.Length<2||tier.lengths.Any(l=>l<1)||tier.lengths[0]!=r.directRoute.Length){Error(n,"routes.tiers","need at least two routes; first length must match direct route");continue;}
                        int poolNeeded=tier.lengths.Skip(1).Sum()+Math.Max(0,tier.lengths.Length-2)*tier.sensorsPerLane;
                        if(tier.nodeCount<ids.Count||tier.nodeCount-r.reserved.Distinct().Count()<poolNeeded)Error(n,"routes.tiers","insufficient nodes for route and sensor pool");
                    }
                }
            }
            foreach(var c in root.contracts.Where(x=>x!=null))
            {
                if(c.company==null||c.network==null){Error(c,"company/network","both references are required");continue;}
                var ids=new HashSet<string>(c.network.nodes.Where(x=>x!=null).Select(n=>n.id).Concat(c.network.routes?.extraIds??Array.Empty<string>()));
                if(c.objectives==null||c.objectives.Length==0)Error(c,"objectives","contract needs at least one objective");
                else foreach(var goal in c.objectives)if(goal==null||!ids.Contains(goal.nodeId)||goal.complete==null)Error(c,"objectives","missing target node or completion condition");
                Walk(c,"contract",new HashSet<object>(),0,true,(obj,path)=>{if(obj is NamedNodeCondition named&&!ids.Contains(named.nodeId))Error(c,path,"missing prerequisite node "+named.nodeId);},_=>{});
                if(c.credits==null||c.xp==null||c.difficulty==null||c.seed==null)Error(c,"formulas","contract formulas are required");
            }
            var rig=root.rig;
            if(rig.startingChassis==null||root.chassis.Length==0)Error(rig,"startingChassis","a registered starting chassis is required");
            if(rig.saleFraction<0||rig.saleFraction>1||rig.researchSaleFraction<0||rig.researchSaleFraction>1||rig.minimumTraceFraction<0||rig.minimumTraceFraction>1||rig.maximumCompute<=0||rig.maximumBandwidth<=0||rig.memoryReclaim<0)Error(rig,"limits","invalid sale, resource or stacking limits");
            if(root.chassis.GroupBy(c=>c.tier).Any(g=>g.Count()>1))Error(rig,"chassis","tiers must be unique");
            foreach(var chassis in root.chassis)
            {
                if(chassis.rows<1||chassis.columns<1||chassis.tier<1||chassis.price<0||chassis.requiredJobs<0||chassis.slots<0||chassis.storageCapacity<0||chassis.memoryPool<0||chassis.powerCapacity<0||chassis.coolingCapacity<0)Error(chassis,"dimensions/capacities","dimensions must be positive; costs and capacities nonnegative");
            }
            foreach(var module in root.modules)
            {
                if(module.width<1||module.height<1||module.tier<1||module.price<0||module.powerDemand<0||module.heatOutput<0||module.powerCapacity<0||module.coolingCapacity<0)Error(module,"footprint/budgets","invalid footprint, cost, tier or capacity");
                if(!root.chassis.Any(c=>(module.width<=c.columns&&module.height<=c.rows)||(module.rotatable&&rig.allowRotation&&module.height<=c.columns&&module.width<=c.rows)))Error(module,"footprint","module cannot fit any chassis");
                if(string.IsNullOrWhiteSpace(module.family)||!new[]{"RECON","INTRUSION","STEALTH","CONTROL","CROSSOVER"}.Contains(module.branch))Error(module,"family/branch","family and a valid branch are required");
                if(module.requirements==null||module.modifiers==null)Error(module,"rules","rule arrays cannot be null");
            }
            if(root.modules.GroupBy(m=>(m.family,m.tier)).Any(g=>g.Count()>1&&g.Key.family=="power"||g.Count()>1&&g.Key.family=="cooling"))Error(rig,"modules","power and cooling tiers must be unique within their families");
            if(rig.startingChassis!=null)
            {
                var cells=new HashSet<(int,int)>();float power=0,heat=0,pc=rig.startingChassis.powerCapacity,cc=rig.startingChassis.coolingCapacity;
                foreach(var part in rig.startingParts)
                {
                    if(part.module==null){Error(rig,"startingParts","module reference is required");continue;}
                    var size=GameSimulation.PartSize(part.module,part.rotated);
                    if(part.rotated&&(!part.module.rotatable||!rig.allowRotation))Error(rig,"startingParts","rotation is unavailable");
                    for(int y=part.row;y<part.row+size.y;y++)for(int x=part.column;x<part.column+size.x;x++)if(y<0||x<0||y>=rig.startingChassis.rows||x>=rig.startingChassis.columns||!cells.Add((y,x)))Error(rig,"startingParts","overlap or out-of-bounds placement");
                    power+=part.module.powerDemand;heat+=part.module.heatOutput;pc+=part.module.powerCapacity;cc+=part.module.coolingCapacity;
                    if(part.module.requirements.Any(r=>r.condition is ResearchCondition gate&&!root.career.initialTechnologies.Contains(gate.technology)))Error(rig,"startingParts","starter module research must be initially unlocked");
                }
                if(power>pc||heat>cc)Error(rig,"startingParts","starter build exceeds power or cooling");
            }
            var rules=root.career;
            if(rules.credits<0||rules.xp<0||rules.day<1||rules.traceLimit<=0||rules.maxTick<=0||rules.reportLimit<1||rules.logLimit<1||rules.traceWarning<0||rules.traceWarning>=rules.traceDanger||rules.traceDanger>=rules.traceLimit)Error(rules,"limits","invalid career, trace or retention limits");
            if(rules.simulationSpeeds==null||rules.simulationSpeeds.Length==0||rules.simulationSpeeds.Any(s=>s<=0))Error(rules,"simulationSpeeds","speeds must be positive");
            if(rules.stats.GroupBy(s=>s.id).Any(g=>g.Count()>1))Error(rules,"stats","duplicate stat ID");
            if(rules.outcomes==null||rules.outcomes.Length!=Enum.GetValues(typeof(OperationOutcome)).Length||rules.outcomes.Select(o=>o.outcome).Distinct().Count()!=rules.outcomes.Length||rules.outcomes.Any(o=>o.dayAdvance<0||string.IsNullOrEmpty(o.verdict)))Error(rules,"outcomes","each operation outcome needs one policy, a verdict and a nonnegative day advance");
            if(rules.refundFraction<0||rules.refundFraction>1)Error(rules,"refundFraction","refund must be between zero and one");
            if(root.rewards.archiveCredits<0||root.rewards.archiveXp<0||root.rewards.archiveXpCap<0||root.rewards.quietCredits<0||root.rewards.quietXp<0)Error(root.rewards,"rewards","reward values and caps cannot be negative");
            return errors;
        }
        private static void Walk(object obj,string path,HashSet<object> stack,int depth,bool root,Action<object,string> visit,Action<string> cycle)
        {
            if(obj==null)return;visit(obj,path);var type=obj.GetType();
            if(obj is string||type.IsValueType||(!root&&obj is GameplayDefinition))return;
            if(depth>64||!stack.Add(obj)){cycle(path);return;}
            if(obj is IEnumerable items){int i=0;foreach(var item in items)Walk(item,path+"["+i+++ "]",stack,depth+1,false,visit,cycle);}
            else foreach(var field in type.GetFields(BindingFlags.Public|BindingFlags.Instance))Walk(field.GetValue(obj),path+"."+field.Name,stack,depth+1,false,visit,cycle);
            stack.Remove(obj);
        }
    }
}
