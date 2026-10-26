using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HackThePlanet
{
    public enum Arithmetic { Add, Subtract, Multiply, Divide, Minimum, Maximum, Override, Modulo, Floor }
    public enum Comparison { Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual }
    public enum NodeState { Known, Controlled, Scanned, Collected, Objective, Bonus, DefenseActive, Reachable }
    public enum NumberInput { Security, BaseSecurity, Day, Difficulty, Seed, Column, Row, CompletedContracts, CompanyWins, BonusFiles, Trace, QuietLimit, ContractCredits, ContractXp, Duration, NeighborCount, NodeCount, Counter, StatusRemaining, HardwareLevel, ArchiveCredits, ArchiveXp, ArchiveXpCap, QuietCredits, QuietXp, Step, Lane, SafeLane }

    public sealed class RuleContext
    {
        internal GameCatalog Catalog;
        public GameSimulation Simulation;
        public Career Career;
        public Contract Contract;
        public NetNode Node;
        internal ActionDefinition Action;
        public float Duration;
        public int Step,Lane;
        public bool SafeLane;
        public System.Random Random;
        private sealed class Guard { public int depth; }
        private Guard guard = new Guard();
        internal RuleContext At(NetNode node) { var c=(RuleContext)MemberwiseClone(); c.Node=node; return c; }
        internal void Enter() { if(++guard.depth>64) { guard.depth--; throw new InvalidOperationException("Gameplay rule evaluation exceeds 64 levels. Check cyclic stat/rule references."); } }
        internal void Leave() { guard.depth--; }
        public bool Tagged(string tag) => Node!=null && Catalog.NodeType(Node.typeId).tags.Contains(tag);
        public bool State(NodeState state)
        {
            if(Node==null)return false;
            switch(state)
            {
                case NodeState.Known:return Node.known;
                case NodeState.Controlled:return Node.controlled;
                case NodeState.Scanned:return Node.scanned;
                case NodeState.Collected:return Node.collected;
                case NodeState.Objective:return Node.objective;
                case NodeState.Bonus:return Node.bonus;
                case NodeState.DefenseActive:return !Node.controlled&&Node.suppressed<=0;
                case NodeState.Reachable:return Simulation!=null&&Simulation.Reachable(Node);
                default:return false;
            }
        }
    }

    [Serializable] public abstract class Number
    {
        public float Evaluate(RuleContext context)
        {
            context.Enter();
            try { float value=Calculate(context); if(float.IsNaN(value)||float.IsInfinity(value))throw new InvalidOperationException("Non-finite gameplay formula: "+GetType().Name); return value; }
            finally { context.Leave(); }
        }
        protected abstract float Calculate(RuleContext context);
    }
    [Serializable] public sealed class ConstantNumber : Number
    {
        public float value;
        public ConstantNumber(){} public ConstantNumber(float value){this.value=value;}
        protected override float Calculate(RuleContext c)=>value;
    }
    [Serializable] public sealed class StatNumber : Number
    {
        public string stat;
        protected override float Calculate(RuleContext c)=>c.Simulation!=null?c.Simulation.Stat(stat,c):c.Catalog.BaseStat(stat);
    }
    [Serializable] public sealed class InputNumber : Number
    {
        public NumberInput input;
        public string key;
        [SerializeReference] public Condition filter=new AlwaysCondition();
        protected override float Calculate(RuleContext c)
        {
            switch(input)
            {
                case NumberInput.Step:return c.Step;
                case NumberInput.Lane:return c.Lane;
                case NumberInput.SafeLane:return c.SafeLane?1:0;
                case NumberInput.Security:return c.Simulation?.EffectiveSecurity(c.Node)??c.Node?.security??0;
                case NumberInput.BaseSecurity:return c.Node?.security??0;
                case NumberInput.Day:return c.Career?.day??1;
                case NumberInput.Difficulty:return c.Contract?.difficulty??1;
                case NumberInput.Seed:return c.Contract?.seed??0;
                case NumberInput.Column:return c.Node?.column??0;
                case NumberInput.Row:return c.Node?.row??0;
                case NumberInput.CompletedContracts:return c.Career?.completed.Count??0;
                case NumberInput.CompanyWins:return c.Career?.completed.Count(id=>c.Catalog.CompanyFor(id)==c.Contract?.company)??0;
                case NumberInput.BonusFiles:return c.Simulation?.Op?.nodes.Count(n=>n.bonus&&n.collected)??0;
                case NumberInput.Trace:return c.Simulation?.Op?.trace??0;
                case NumberInput.QuietLimit:return c.Contract?.quietLimit??0;
                case NumberInput.ContractCredits:return c.Contract?.credits??0;
                case NumberInput.ContractXp:return c.Contract?.xp??0;
                case NumberInput.Duration:return c.Duration;
                case NumberInput.NeighborCount:return c.Node==null||c.Simulation?.Op==null?0:c.Simulation.Neighbors(c.Node).Count(n=>filter.Test(c.At(n)));
                case NumberInput.NodeCount:return c.Simulation?.Op?.nodes.Count(n=>filter.Test(c.At(n)))??0;
                case NumberInput.Counter:return c.Simulation?.Counter(key)??0;
                case NumberInput.StatusRemaining:return c.Simulation?.StatusRemaining(key,c.Node?.id)??0;
                case NumberInput.HardwareLevel:return c.Career?.Level(key)??0;
                case NumberInput.ArchiveCredits:return c.Catalog.Data.rewards.archiveCredits;
                case NumberInput.ArchiveXp:return c.Catalog.Data.rewards.archiveXp;
                case NumberInput.ArchiveXpCap:return c.Catalog.Data.rewards.archiveXpCap;
                case NumberInput.QuietCredits:return c.Catalog.Data.rewards.quietCredits;
                case NumberInput.QuietXp:return c.Catalog.Data.rewards.quietXp;
                default:throw new InvalidOperationException("Unknown gameplay formula input.");
            }
        }
    }
    [Serializable] public sealed class BinaryNumber : Number
    {
        public Arithmetic operation;
        [SerializeReference] public Number left=new ConstantNumber(0),right=new ConstantNumber(1);
        protected override float Calculate(RuleContext c)=>RuleMath.Apply(operation,left.Evaluate(c),right.Evaluate(c));
    }
    [Serializable] public sealed class ConditionalNumber : Number
    {
        [SerializeReference] public Condition when=new AlwaysCondition();
        [SerializeReference] public Number yes=new ConstantNumber(1),no=new ConstantNumber(0);
        protected override float Calculate(RuleContext c)=>(when.Test(c)?yes:no).Evaluate(c);
    }
    [Serializable] public sealed class RandomNumber : Number
    {
        public int minimum=1,maximum=2;
        protected override float Calculate(RuleContext c)
        {
            if(c.Random==null)throw new InvalidOperationException("Random formulas are only valid during seeded network generation.");
            return c.Random.Next(minimum,checked(maximum+1));
        }
    }
    internal static class RuleMath
    {
        public static float Apply(Arithmetic op,float left,float right)
        {
            float result;
            switch(op)
            {
                case Arithmetic.Add:result=left+right;break;
                case Arithmetic.Subtract:result=left-right;break;
                case Arithmetic.Multiply:result=left*right;break;
                case Arithmetic.Divide:if(right==0)throw new InvalidOperationException("Gameplay formula divides by zero.");result=left/right;break;
                case Arithmetic.Minimum:result=Mathf.Min(left,right);break;
                case Arithmetic.Maximum:result=Mathf.Max(left,right);break;
                case Arithmetic.Override:result=right;break;
                case Arithmetic.Modulo:if(right==0)throw new InvalidOperationException("Gameplay formula modulo by zero.");result=left%right;break;
                case Arithmetic.Floor:result=Mathf.Floor(left);break;
                default:throw new InvalidOperationException("Unknown formula operation.");
            }
            if(float.IsNaN(result)||float.IsInfinity(result))throw new InvalidOperationException("Non-finite gameplay arithmetic result.");
            return result;
        }
    }
    [Serializable] public abstract class Condition
    {
        public bool Test(RuleContext c) { c.Enter();try{return Check(c);}finally{c.Leave();} }
        protected abstract bool Check(RuleContext c);
    }
    [Serializable] public sealed class AlwaysCondition : Condition { protected override bool Check(RuleContext c)=>true; }
    [Serializable] public sealed class AllCondition : Condition
    {
        [SerializeReference] public Condition[] conditions=Array.Empty<Condition>();
        protected override bool Check(RuleContext c)=>conditions.All(x=>x.Test(c));
    }
    [Serializable] public sealed class AnyCondition : Condition
    {
        [SerializeReference] public Condition[] conditions=Array.Empty<Condition>();
        protected override bool Check(RuleContext c)=>conditions.Any(x=>x.Test(c));
    }
    [Serializable] public sealed class NotCondition : Condition
    {
        [SerializeReference] public Condition condition=new AlwaysCondition();
        protected override bool Check(RuleContext c)=>!condition.Test(c);
    }
    [Serializable] public sealed class NodeStateCondition : Condition
    {
        public NodeState state;
        protected override bool Check(RuleContext c)=>c.State(state);
    }
    [Serializable] public sealed class NodeTagCondition : Condition
    {
        public string tag;
        protected override bool Check(RuleContext c)=>c.Tagged(tag);
    }
    [Serializable] public sealed class ActionCondition : Condition
    {
        public ActionDefinition action;
        public string tag;
        protected override bool Check(RuleContext c)=>c.Action!=null&&(action==null||c.Action.id==action.id)&&(string.IsNullOrEmpty(tag)||c.Action.tags.Contains(tag));
    }
    [Serializable] public sealed class ResearchCondition : Condition
    {
        public TechnologyDefinition technology;
        public bool equipped;
        protected override bool Check(RuleContext c)=>technology!=null&&(equipped?c.Simulation?.Equipped(technology.id)==true:c.Career?.unlocked.Contains(technology.id)==true);
    }
    [Serializable] public sealed class NamedNodeCondition : Condition
    {
        public string nodeId;
        [SerializeReference] public Condition condition=new AlwaysCondition();
        protected override bool Check(RuleContext c) { var node=c.Simulation?.Node(nodeId);return node!=null&&condition.Test(c.At(node)); }
    }
    [Serializable] public sealed class CompareCondition : Condition
    {
        public Comparison comparison;
        [SerializeReference] public Number left=new ConstantNumber(0),right=new ConstantNumber(0);
        protected override bool Check(RuleContext c)
        {
            float a=left.Evaluate(c),b=right.Evaluate(c);
            switch(comparison)
            {
                case Comparison.Equal:return a==b;
                case Comparison.NotEqual:return a!=b;
                case Comparison.Less:return a<b;
                case Comparison.LessOrEqual:return a<=b;
                case Comparison.Greater:return a>b;
                default:return a>=b;
            }
        }
    }

    [Serializable] public abstract class Effect { public abstract void Apply(RuleContext c); }
    [Serializable] public sealed class SetStateEffect : Effect
    {
        public NodeSelector targets=new NodeSelector();
        public NodeState state=NodeState.Controlled;
        public bool value=true;
        public override void Apply(RuleContext c) { foreach(var node in c.Simulation.Select(targets,c).ToArray())c.Simulation.SetState(node,state,value,c); }
    }
    [Serializable] public sealed class RevealEffect : Effect
    {
        public NodeSelector targets=new NodeSelector();
        [SerializeReference] public Number radius=new ConstantNumber(1);
        public override void Apply(RuleContext c) { foreach(var node in c.Simulation.Select(targets,c).ToArray())c.Simulation.Reveal(node,Mathf.Max(0,Mathf.RoundToInt(radius.Evaluate(c.At(node))))); }
    }
    [Serializable] public sealed class TraceEffect : Effect
    {
        [SerializeReference] public Number amount=new ConstantNumber(-15);
        public override void Apply(RuleContext c)=>c.Simulation.Op.trace=Mathf.Clamp(c.Simulation.Op.trace+amount.Evaluate(c),0,c.Catalog.Data.career.traceLimit);
    }
    [Serializable] public sealed class ApplyStatusEffect : Effect
    {
        public StatusDefinition status;
        public bool operatorStatus;
        public NodeSelector targets=new NodeSelector();
        [SerializeReference] public Number duration=new ConstantNumber(60);
        public override void Apply(RuleContext c)
        {
            if(operatorStatus)c.Simulation.ApplyStatus(status,null,duration.Evaluate(c));
            else foreach(var node in c.Simulation.Select(targets,c).ToArray())c.Simulation.ApplyStatus(status,node.id,duration.Evaluate(c.At(node)));
        }
    }
    [Serializable] public sealed class SecurityEffect : Effect
    {
        public NodeSelector targets=new NodeSelector {scope=SelectionScope.All};
        public int minimum=1;
        [SerializeReference] public Number amount=new ConstantNumber(-2);
        public override void Apply(RuleContext c) { foreach(var node in c.Simulation.Select(targets,c).ToArray())node.security=Mathf.Max(minimum,node.security+Mathf.RoundToInt(amount.Evaluate(c.At(node)))); }
    }
    [Serializable] public sealed class CounterEffect : Effect
    {
        public string key;
        public int amount=1;
        public override void Apply(RuleContext c)=>c.Simulation.AddCounter(key,amount);
    }
    [Serializable] public sealed class ConditionalEffect : Effect
    {
        [SerializeReference] public Condition when=new AlwaysCondition();
        [SerializeReference] public Effect[] effects=Array.Empty<Effect>();
        public override void Apply(RuleContext c) { if(when.Test(c))c.Simulation.ApplyEffects(effects,c); }
    }
    [Serializable] public sealed class LogEffect : Effect
    {
        [TextArea] public string message;
        public override void Apply(RuleContext c)=>c.Simulation.Log(c.Catalog.Format(message,c));
    }
    [Serializable] public sealed class RecoverLoreEffect : Effect
    {
        public override void Apply(RuleContext c)
        {
            var fragment=LoreContent.Find(c.Contract,c.Node?.loreId);
            if(fragment!=null)c.Simulation.Log("LORE RECOVERED // "+fragment.title+" // "+fragment.text);
        }
    }
}
