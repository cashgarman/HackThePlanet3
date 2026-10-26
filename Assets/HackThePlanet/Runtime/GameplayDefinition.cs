using System;
using UnityEngine;

namespace HackThePlanet
{
    public abstract class GameplayDefinition : ScriptableObject
    {
        public string id, title;
        [TextArea(2,6)] public string description;
    }
    [Serializable] public sealed class NamedStat { public string id; public float value; public NamedStat(){} public NamedStat(string key,float v){id=key;value=v;} }
    [Serializable] public sealed class Requirement { [SerializeReference] public Condition condition = new AlwaysCondition(); public string message; }
    [Serializable] public sealed class Modifier
    {
        public string id;
        public string stat;
        public int priority;
        public Arithmetic operation = Arithmetic.Multiply;
        [SerializeReference] public Condition when = new AlwaysCondition();
        [SerializeReference] public Number value = new ConstantNumber(1);
    }
    public enum RuleEvent { OperationStarted, ActionCompleted, ControlEstablished, StatusExpired }
    [Serializable] public sealed class Trigger
    {
        public RuleEvent on;
        public string onceKey;
        [SerializeReference] public Condition when = new AlwaysCondition();
        [SerializeReference] public Effect[] effects = Array.Empty<Effect>();
    }
    [Serializable] public sealed class Aura
    {
        public string id;
        public bool adjacentOnly, stack;
        [SerializeReference] public Condition source = new AlwaysCondition();
        public Modifier modifier = new Modifier();
    }
    [Serializable] public sealed class NodeSelector
    {
        public SelectionScope scope;
        public string nodeId;
        [SerializeReference] public Condition filter = new AlwaysCondition();
    }
    public enum SelectionScope { Target, All, Neighbors, Entry, Named }

}
