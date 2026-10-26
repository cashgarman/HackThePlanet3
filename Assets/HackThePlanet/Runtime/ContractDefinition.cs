using System;
using UnityEngine;

namespace HackThePlanet
{
    [Serializable] public sealed class ContractActionRule
    {
        public ActionDefinition action;
        public Requirement[] requirements = Array.Empty<Requirement>();
    }
    [Serializable] public sealed class ObjectiveDefinition
    {
        public string nodeId, label, actionLabel;
        public ActionDefinition action;
        [SerializeReference] public Condition complete = new NodeStateCondition { state=NodeState.Collected };
        public Requirement[] actionRequirements = Array.Empty<Requirement>();
    }
    [CreateAssetMenu(menuName="Hack The Planet/Contract")]
    public sealed class ContractDefinition : GameplayDefinition
    {
        public CompanyDefinition company;
        public NetworkDefinition network;
        public string client, tag, objective, condition, aftermath;
        public JobKind displayKind;
        public bool recurring;
        public int requiredWins;
        public float quietLimit = 75;
        [SerializeReference] public Number credits = new ConstantNumber(600);
        [SerializeReference] public Number xp = new ConstantNumber(50);
        [SerializeReference] public Number difficulty = new ConstantNumber(1);
        [SerializeReference] public Number seed = new ConstantNumber(114);
        public ObjectiveDefinition[] objectives = Array.Empty<ObjectiveDefinition>();
        public ContractActionRule[] actionRules = Array.Empty<ContractActionRule>();
        public LoreFragment[] lore = Array.Empty<LoreFragment>();
    }
}
