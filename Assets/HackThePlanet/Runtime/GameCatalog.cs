using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

namespace HackThePlanet
{
    public sealed class ActionInfo
    {
        internal readonly ActionDefinition Definition;
        internal ActionInfo(ActionDefinition definition){Definition=definition;}
        public string id=>Definition.id;
        public string title=>Definition.title;
        public string description=>Definition.description;
        public KeyCode shortcut=>Definition.shortcut;
        public string shortcutLabel=>Definition.shortcutLabel;
        public bool operatorTarget=>Definition.operatorTarget;
        public int priority=>Definition.presentationPriority;
    }
    public sealed class HardwareInfo
    {
        internal readonly HardwareDefinition Definition;
        internal HardwareInfo(HardwareDefinition definition){Definition=definition;}
        public string id=>Definition.id;
        public string title=>Definition.title;
        public string description=>Definition.description;
        public string category=>Definition.category;
        public string statLabel=>Definition.statLabel;
        public string displayedStat=>Definition.displayedStat;
        public int maxLevel=>Definition.tiers.Length;
    }
    public sealed class NodeTypeInfo
    {
        internal readonly NodeTypeDefinition Definition;
        internal NodeTypeInfo(NodeTypeDefinition definition){Definition=definition;}
        public string id=>Definition.id;
        public string title=>Definition.title;
        public string description=>Definition.description;
        public NodeKind glyph=>Definition.glyph;
        public Color color=>Definition.color;
    }

    // Compiles a private, detached object graph. Public APIs expose immutable metadata
    // or disposable descriptors; the simulation never changes these definitions.
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class GameCatalog : IDisposable
    {
        private static GameCatalog defaultCatalog;
        private static readonly List<GameCatalog> retiredCatalogs=new List<GameCatalog>();
        public static GameCatalog Default => defaultCatalog??=new GameCatalog(Resources.Load<GameDefinition>("HackThePlanet/Gameplay/Game"));
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDefault(){defaultCatalog?.Dispose();defaultCatalog=null;foreach(var old in retiredCatalogs)old.Dispose();retiredCatalogs.Clear();}
        internal static void InvalidateDefault()
        {
            // Existing simulations retain their frozen catalog until the session ends.
            if(defaultCatalog!=null)retiredCatalogs.Add(defaultCatalog);defaultCatalog=null;
        }
        internal readonly GameDefinition Data;
        private readonly Dictionary<string,ActionDefinition> actions;
        private readonly Dictionary<string,TechnologyDefinition> technologies;
        private readonly Dictionary<string,NodeTypeDefinition> nodeTypes;
        private readonly Dictionary<string,NetworkDefinition> networks;
        private readonly Dictionary<string,ContractDefinition> contracts;
        private readonly List<GameplayDefinition> clones=new List<GameplayDefinition>();
        public IReadOnlyList<ActionInfo> Actions { get; }
        public IReadOnlyList<HardwareInfo> Hardware { get; }
        public IReadOnlyList<NodeTypeInfo> NodeTypes { get; }
        public IReadOnlyList<string> Companies { get; }
        public IEnumerable<Technology> Technologies=>Data.technologies.Select(DescribeTechnology);
        public int StoryCount=>Data.contracts.Count(c=>!c.recurring);

        public GameCatalog(GameDefinition root)
        {
            if(root==null)throw new InvalidOperationException("Missing gameplay root asset: Resources/HackThePlanet/Gameplay/Game.asset");
            GameDefinitionValidator.ThrowIfInvalid(root);
            var map=new Dictionary<GameplayDefinition,GameplayDefinition>();
            GameplayDefinition Copy(GameplayDefinition source)
            {
                if(source==null)return null;
                if(map.TryGetValue(source,out var existing))return existing;
                var copy=UnityEngine.Object.Instantiate(source);copy.name=source.name;copy.hideFlags=HideFlags.HideAndDontSave;
                map.Add(source,copy);clones.Add(copy);
                Remap(copy,new HashSet<object>(),Copy);return copy;
            }
            Data=(GameDefinition)Copy(root);
            actions=Data.actions.ToDictionary(x=>x.id);technologies=Data.technologies.ToDictionary(x=>x.id);
            nodeTypes=Data.nodeTypes.ToDictionary(x=>x.id);networks=Data.networks.ToDictionary(x=>x.id);contracts=Data.contracts.ToDictionary(x=>x.id);
            Actions=Array.AsReadOnly(Data.actions.Select(x=>new ActionInfo(x)).ToArray());
            Hardware=Array.AsReadOnly(Data.hardware.Select(x=>new HardwareInfo(x)).ToArray());
            NodeTypes=Array.AsReadOnly(Data.nodeTypes.Select(x=>new NodeTypeInfo(x)).ToArray());
            Companies=Array.AsReadOnly(Data.companies.Select(x=>x.title).ToArray());
        }
        private static void Remap(object obj,HashSet<object> visited,Func<GameplayDefinition,GameplayDefinition> copy)
        {
            if(obj==null||obj is string||obj.GetType().IsValueType||!visited.Add(obj))return;
            if(obj is IList list)
            {
                for(int i=0;i<list.Count;i++)if(list[i] is GameplayDefinition d)list[i]=copy(d);else Remap(list[i],visited,copy);
                return;
            }
            foreach(var f in obj.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public))
            {
                var value=f.GetValue(obj);
                if(value is GameplayDefinition d)f.SetValue(obj,copy(d));else Remap(value,visited,copy);
            }
        }
        internal ActionDefinition Action(string id)=>id!=null&&actions.TryGetValue(id,out var value)?value:null;
        internal TechnologyDefinition Technology(string id)=>id!=null&&technologies.TryGetValue(id,out var value)?value:null;
        internal NodeTypeDefinition NodeType(string id)=>id!=null&&nodeTypes.TryGetValue(id,out var value)?value:throw new InvalidOperationException("Unknown node type: "+id);
        internal NetworkDefinition Network(string id)=>id!=null&&networks.TryGetValue(id,out var value)?value:throw new InvalidOperationException("Unknown network: "+id);
        internal ContractDefinition Contract(string id)=>id!=null&&contracts.TryGetValue(id,out var value)?value:null;
        internal float BaseStat(string id)=>Data.career.stats.FirstOrDefault(x=>x.id==id)?.value??0;
        internal string CompanyFor(string contractId)=>Contract(contractId)?.company.title;
        public ActionInfo FindAction(string id)=>Actions.FirstOrDefault(x=>x.id==id);
        internal RuleContext Context(Career career,Contract contract=null)=>new RuleContext {Catalog=this,Career=career,Contract=contract};

        public IReadOnlyList<Contract> Contracts(Career career)
        {
            return Array.AsReadOnly(Data.contracts.Where(d=>!d.recurring||career.completed.Count>=d.requiredWins).Select(d=>DescribeContract(d,career)).ToArray());
        }
        internal Contract DescribeContract(ContractDefinition d,Career career)
        {
            var context=Context(career);
            var job=new Contract {id=d.id,title=d.title,description=d.description,client=d.client,company=d.company.title,tag=d.tag,objective=d.objective,condition=d.condition,aftermath=d.aftermath,
                kind=d.displayKind,requiredWins=d.requiredWins,quietLimit=d.quietLimit,recurring=d.recurring,networkId=d.network.id,
                credits=Mathf.RoundToInt(d.credits.Evaluate(context)),xp=Mathf.RoundToInt(d.xp.Evaluate(context)),difficulty=Mathf.RoundToInt(d.difficulty.Evaluate(context)),seed=Mathf.RoundToInt(d.seed.Evaluate(context))};
            job.credits=Mathf.RoundToInt(job.credits*job.RewardMultiplier);
            job.xp=Mathf.RoundToInt(job.xp*job.RewardMultiplier);
            job.lore=d.lore.Select(x=>new LoreFragment {id=x.id,title=x.title,text=x.text,company=job.company,contract=job.title}).ToArray();
            return job;
        }
        internal Technology DescribeTechnology(TechnologyDefinition d)=>new Technology(d.id,d.title,d.branch,d.cost,d.position.x,d.position.y,FormatTechnology(d),d.special,d.parents.Select(x=>x.id).ToArray()) { grants=d.grants.Select(x=>x.id).ToArray() };
        private string FormatTechnology(TechnologyDefinition d)
        {
            return Regex.Replace(d.description??"",@"\{modifier:(\d+):(value|reduction|percent)\}",match=>
            {
                int index=int.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture);if(index>=d.modifiers.Length)return match.Value;
                float value=d.modifiers[index].value.Evaluate(Context(new Career()));
                switch(match.Groups[2].Value){case "reduction":value=(1-value)*100;break;case "percent":value*=100;break;}
                return value.ToString("0.##",CultureInfo.InvariantCulture);
            });
        }
        internal string Format(string text,RuleContext c)
        {
            return (text??"").Replace("{target}",c.Node?.label??"operator").Replace("{archiveCredits}",Data.rewards.archiveCredits.ToString())
                .Replace("{archiveXp}",Data.rewards.archiveXp.ToString()).Replace("{archiveXpCap}",Data.rewards.archiveXpCap.ToString())
                .Replace("{aftermath}",c.Contract?.aftermath??string.Empty).Replace("{quietCredits}",Data.rewards.quietCredits.ToString()).Replace("{quietXp}",Data.rewards.quietXp.ToString());
        }
        public Career CreateCareer()
        {
            var r=Data.career;
            return new Career { initialized=true,researchSpent=0,credits=r.credits,xp=r.xp,day=r.day,unlocked=r.initialTechnologies.Select(x=>x.id).ToList(),news=new List<string>{r.welcome} };
        }
        public void Dispose()
        {
            foreach(var clone in clones)if(clone!=null){if(Application.isPlaying)UnityEngine.Object.Destroy(clone);else UnityEngine.Object.DestroyImmediate(clone);}
            clones.Clear();
        }
    }
}
