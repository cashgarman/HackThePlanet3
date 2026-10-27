using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HackThePlanet
{
    [Serializable] public sealed class RigPart
    {
        public string instanceId,modelId;
        public int paidPrice,row=-1,column=-1;
        public bool rotated;
        public bool Installed=>row>=0&&column>=0;
    }
    [Serializable] public sealed class MemoryReservation
    {
        public string actionId,target;
        public float amount;
    }
    public enum RigIssueSeverity { Error, Warning }
    public sealed class RigIssue
    {
        public readonly RigIssueSeverity severity;
        public readonly string message;
        public RigIssue(RigIssueSeverity severity,string message){this.severity=severity;this.message=message;}
    }
    public sealed class ResearchRefundPreview
    {
        public int xp,credits,copies;
        public string[] soldInstances;
        public string description;
    }
    public sealed partial class GameSimulation
    {
        private string applyingMemoryKey;
        public RigChassisDefinition Chassis=>Catalog.Data.chassis.Single(c=>c.id==Career.chassisId);
        public IReadOnlyList<RigModuleDefinition> Modules=>Catalog.Data.modules;
        public IEnumerable<RigPart> InstalledParts=>Career.rigParts.Where(p=>p.Installed);
        public RigModuleDefinition Module(string id)=>Catalog.Data.modules.FirstOrDefault(m=>m.id==id);
        public RigPart Part(string id)=>Career.rigParts.FirstOrDefault(p=>p.instanceId==id);
        public RigChassisDefinition NextChassis=>Catalog.Data.chassis.OrderBy(c=>c.tier).FirstOrDefault(c=>c.tier>Chassis.tier);
        public float PowerLimit=>Chassis.powerCapacity+InstalledParts.Sum(p=>Module(p.modelId).powerCapacity);
        public float CoolingLimit=>Chassis.coolingCapacity+InstalledParts.Sum(p=>Module(p.modelId).coolingCapacity);
        public float PowerUsed=>InstalledParts.Sum(p=>Module(p.modelId).powerDemand);
        public float HeatUsed=>InstalledParts.Sum(p=>Module(p.modelId).heatOutput);
        public float StorageCapacity=>Mathf.Max(0,Stat("storageCapacity"));
        public float StorageUsed=>Op?.nodes.Where(n=>n.collected).Sum(n=>n.storage)??0;
        public float FreeStorage=>Mathf.Max(0,StorageCapacity-StorageUsed);
        public float MemoryPool=>Mathf.Max(0,Stat("memoryPool"));
        public float MemoryReserved=>Op?.reservations.Sum(r=>r.amount)??0;
        public float MemoryReclaiming=>Op?.memoryReclaiming??0;
        public float MemoryFree=>Mathf.Max(0,MemoryPool-MemoryReserved-MemoryReclaiming);
        public float MemoryReclaim=>Mathf.Max(0,Stat("memoryReclaim"));
        public float Compute=>Mathf.Max(0,Stat("compute"));
        public float Bandwidth=>Mathf.Max(0,Stat("bandwidth"));
        public const string UplinkModelId="uplink",StorageFamily="storage",ProcessingFamily="processing";
        public bool RigValid=>!Catalog.Data.rig.requireValidBuild||RigIssues.All(i=>i.severity!=RigIssueSeverity.Error);
        // Errors block connecting. Warnings describe weaker builds that can still run contracts.
        public IReadOnlyList<string> RigProblems=>RigIssues.Where(i=>i.severity==RigIssueSeverity.Error).Select(i=>i.message).ToArray();
        public IReadOnlyList<RigIssue> RigIssues
        {
            get
            {
                var issues=new List<RigIssue>();var occupied=new HashSet<(int,int)>();
                void Error(string message)=>issues.Add(new RigIssue(RigIssueSeverity.Error,message));
                void Warning(string message)=>issues.Add(new RigIssue(RigIssueSeverity.Warning,message));
                foreach(var part in InstalledParts)
                {
                    var model=Module(part.modelId);var size=PartSize(part);
                    if(part.rotated&&(!model.rotatable||!Catalog.Data.rig.allowRotation))Error(model.title+": rotation is unavailable.");
                    for(int y=part.row;y<part.row+size.y;y++)for(int x=part.column;x<part.column+size.x;x++)
                    {
                        if(x<0||y<0||x>=Chassis.columns||y>=Chassis.rows)Error(model.title+": outside the chassis.");
                        if(!occupied.Add((y,x)))Error(model.title+": overlaps another part.");
                    }
                }
                if(PowerUsed>PowerLimit)Error("Power demand exceeds limit by "+(PowerUsed-PowerLimit).ToString("0.#")+". Install a power supply or remove a part.");
                if(HeatUsed>CoolingLimit)Error("Heat exceeds cooling by "+(HeatUsed-CoolingLimit).ToString("0.#")+". Install cooling or remove a part.");
                if(!InstalledParts.Any(p=>p.modelId==UplinkModelId))Error("No uplink installed. Install an Uplink to connect.");
                if(!InstalledParts.Any(p=>Module(p.modelId).family==StorageFamily))Warning("No storage module installed. Only chassis storage ("+StorageCapacity.ToString("0")+" GB) is available. Add a Drive or Storage Array for more.");
                if(!InstalledParts.Any(p=>Module(p.modelId).family==ProcessingFamily))Warning("No processing module installed. Compute stays at base.");
                return issues.GroupBy(i=>i.severity+"|"+i.message).Select(g=>g.First()).ToArray();
            }
        }
        private void InitializeRig()
        {
            Career.rigParts??=new List<RigPart>();
            if(!string.IsNullOrEmpty(Career.chassisId))return;
            Career.chassisId=Catalog.Data.rig.startingChassis.id;
            foreach(var start in Catalog.Data.rig.startingParts)Career.rigParts.Add(new RigPart{instanceId=Guid.NewGuid().ToString("N"),modelId=start.module.id,row=start.row,column=start.column,rotated=start.rotated});
        }
        internal float RigBaseStat(string stat)
        {
            switch(stat)
            {
                case "storageCapacity":return Chassis.storageCapacity;
                case "memoryPool":return Chassis.memoryPool;
                case "memoryReclaim":return Catalog.Data.rig.memoryReclaim;
                default:return Catalog.BaseStat(stat);
            }
        }
        public Vector2Int PartSize(RigPart part)=>PartSize(Module(part.modelId),part.rotated);
        public static Vector2Int PartSize(RigModuleDefinition model,bool rotated)=>rotated?new Vector2Int(model.height,model.width):new Vector2Int(model.width,model.height);
        public RigPart PartAt(int row,int column)=>InstalledParts.FirstOrDefault(p=>row>=p.row&&column>=p.column&&row<p.row+PartSize(p).y&&column<p.column+PartSize(p).x);
        public bool CanPlace(string instance,int row,int column,bool rotated)
        {
            var part=Part(instance);if(Active||part==null)return false;var model=Module(part.modelId);var size=PartSize(model,rotated);
            return (!rotated||(model.rotatable&&Catalog.Data.rig.allowRotation))&&row>=0&&column>=0&&row+size.y<=Chassis.rows&&column+size.x<=Chassis.columns
                &&!InstalledParts.Any(p=>p!=part&&row<p.row+PartSize(p).y&&row+size.y>p.row&&column<p.column+PartSize(p).x&&column+size.x>p.column);
        }
        // Editing can retain budget failures, but never ambiguous overlapping placements.
        public bool TryPlaceModule(string instance,int row,int column,bool rotated=false)
        {
            if(!CanPlace(instance,row,column,rotated))return false;var part=Part(instance);part.row=row;part.column=column;part.rotated=rotated;Touch();return true;
        }
        public bool PlaceFirstFree(string instance,bool rotated=false)
        {
            for(int row=0;row<Chassis.rows;row++)for(int column=0;column<Chassis.columns;column++)if(TryPlaceModule(instance,row,column,rotated))return true;return false;
        }
        public bool RemoveModule(string instance)
        {
            var part=Part(instance);if(Active||part==null)return false;part.row=part.column=-1;Touch();return true;
        }
        public bool RotateModule(string instance)
        {
            var part=Part(instance);if(Active||part==null)return false;
            if(part.Installed)return TryPlaceModule(instance,part.row,part.column,!part.rotated);
            if(!Module(part.modelId).rotatable||!Catalog.Data.rig.allowRotation)return false;
            part.rotated=!part.rotated;Touch();return true;
        }
        public ToolPreview PreviewModule(string modelId)
        {
            var model=Module(modelId);var preview=new ToolPreview();
            if(model==null){preview.reason="Unknown module model.";return preview;}
            if(Active){preview.reason="Change the rig between contracts.";return preview;}
            foreach(var requirement in model.requirements)if(!requirement.condition.Test(Context())){preview.reason=Catalog.Format(requirement.message,Context());return preview;}
            if(Career.credits<model.price){preview.reason="Need "+(model.price-Career.credits)+" more credits.";return preview;}
            preview.allowed=true;preview.reason="Ready to buy. Stored in inventory; placement is free.";return preview;
        }
        private bool ModuleRequirementsMet(RigModuleDefinition model)=>model.requirements.All(r=>r.condition.Test(Context()));
        public RigPart BuyModule(string modelId)
        {
            if(!PreviewModule(modelId).allowed)return null;var model=Module(modelId);Career.credits-=model.price;
            var part=new RigPart{instanceId=Guid.NewGuid().ToString("N"),modelId=model.id,paidPrice=model.price};Career.rigParts.Add(part);Touch();return part;
        }
        public int ModuleSalePrice(string instance)=>Part(instance) is RigPart part?Mathf.FloorToInt(part.paidPrice*Catalog.Data.rig.saleFraction):0;
        public bool SellModule(string instance)
        {
            var part=Part(instance);if(Active||part==null)return false;Career.credits+=ModuleSalePrice(instance);Career.rigParts.Remove(part);Touch();return true;
        }
        public ToolPreview PreviewChassis()
        {
            var preview=new ToolPreview();var next=NextChassis;
            if(Active)preview.reason="Change the rig between contracts.";
            else if(next==null)preview.reason="Maximum chassis installed.";
            else if(Career.jobs<next.requiredJobs)preview.reason="Requires "+next.requiredJobs+" successful jobs.";
            else if(Career.credits<next.price)preview.reason="Need "+(next.price-Career.credits)+" more credits.";
            else{preview.allowed=true;preview.reason="Expand the chassis; existing placements are preserved.";}return preview;
        }
        public bool UpgradeChassis()
        {
            if(!PreviewChassis().allowed)return false;var next=NextChassis;Career.credits-=next.price;Career.chassisId=next.id;Touch();return true;
        }
        public int RigNeighborCount(string instance,string familyOrBranch)
        {
            var source=Part(instance);if(source==null||!source.Installed)return 0;var a=PartSize(source);
            return InstalledParts.Count(p=>
            {
                if(p==source)return false;var model=Module(p.modelId);
                if(!string.IsNullOrEmpty(familyOrBranch)&&!string.Equals(model.id,familyOrBranch,StringComparison.OrdinalIgnoreCase)&&!string.Equals(model.family,familyOrBranch,StringComparison.OrdinalIgnoreCase)&&!string.Equals(model.branch,familyOrBranch,StringComparison.OrdinalIgnoreCase))return false;
                var b=PartSize(p);
                return ((source.column+a.x==p.column||p.column+b.x==source.column)&&source.row<p.row+b.y&&source.row+a.y>p.row)
                    ||((source.row+a.y==p.row||p.row+b.y==source.row)&&source.column<p.column+b.x&&source.column+a.x>p.column);
            });
        }
        public ResearchRefundPreview PreviewResearchRefund()
        {
            var original=Career.unlocked;var originalEquipped=Career.equipped;var initial=Catalog.Data.career.initialTechnologies.Select(t=>t.id).ToList();
            try
            {
                Career.unlocked=initial;Career.equipped=new List<string>();var sold=Career.rigParts.Where(p=>!ModuleRequirementsMet(Module(p.modelId))).ToArray();
                var preview=new ResearchRefundPreview{xp=Mathf.FloorToInt(Career.researchSpent*RefundFraction),credits=sold.Sum(p=>Mathf.FloorToInt(p.paidPrice*Catalog.Data.rig.researchSaleFraction)),copies=sold.Length,soldInstances=sold.Select(p=>p.instanceId).ToArray()};
                preview.description="Refund "+preview.xp+" XP and sell "+preview.copies+" locked module copies for "+preview.credits+" cr. Placements will be removed; check power and cooling before connecting.";return preview;
            }
            finally{Career.unlocked=original;Career.equipped=originalEquipped;}
        }
        private static string MemoryKey(string action,string target)=>action+":"+(target??"");
        public bool IsAbilityActive(string action,string target=null)=>Op?.reservations.Any(r=>MemoryKey(r.actionId,r.target)==MemoryKey(action,target))==true;
        private void SpendMemory(ActionDefinition action,string target)
        {
            if(action.memoryMode==MemoryMode.OneShot)Op.memoryReclaiming+=action.memoryCost;
            if(action.memoryMode==MemoryMode.Persistent)Op.reservations.Add(new MemoryReservation{actionId=action.id,target=action.operatorTarget?null:target,amount=action.memoryCost});
        }
        private void ReleaseAbility(string action,string target)
        {
            if(!Active)return;
            if(Op.task!=null&&MemoryKey(Op.task.actionId,Op.task.target)==MemoryKey(action,target)){Op.task=null;Op.queue.Clear();}
            Op.reservations.RemoveAll(r=>MemoryKey(r.actionId,r.target)==MemoryKey(action,target));
            string key=MemoryKey(action,target);Op.statuses.RemoveAll(s=>s.memoryKey==key);SyncStatuses();
            Log("ABILITY DISABLED // "+Catalog.Action(action).title+". RAM released.");
        }
        private static void ValidateRigCareer(Career career,GameCatalog catalog,Action<bool,string> require)
        {
            require(catalog.Data.chassis.Any(c=>c.id==career.chassisId),"unknown chassis");
            require(career.rigParts!=null&&career.rigParts.All(p=>p!=null&&!string.IsNullOrEmpty(p.instanceId)&&catalog.Data.modules.Any(m=>m.id==p.modelId)&&p.paidPrice>=0&&((p.row==-1&&p.column==-1)||(p.row>=0&&p.column>=0)))&&career.rigParts.Select(p=>p.instanceId).Distinct().Count()==career.rigParts.Count,"invalid rig inventory");
            var op=career.operation;if(op==null)return;
            require(Finite(op.memoryReclaiming)&&op.memoryReclaiming>=0&&op.reservations!=null&&op.reservations.All(r=>catalog.Action(r.actionId)?.memoryMode==MemoryMode.Persistent&&Finite(r.amount)&&r.amount>=0&&(catalog.Action(r.actionId).operatorTarget?string.IsNullOrEmpty(r.target):op.nodes.Any(n=>n.id==r.target)))&&op.reservations.Select(r=>MemoryKey(r.actionId,r.target)).Distinct().Count()==op.reservations.Count,"invalid RAM state");
            require(op.nodes.All(n=>Finite(n.storage)&&n.storage>=0),"invalid file storage");
        }
    }
}
