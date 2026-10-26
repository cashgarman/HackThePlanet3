using System;
using UnityEngine;

namespace HackThePlanet
{
    [CreateAssetMenu(menuName="Hack The Planet/Action")]
    public sealed class ActionDefinition : GameplayDefinition
    {
        public string[] tags = Array.Empty<string>();
        public KeyCode shortcut;
        public string shortcutLabel, legacyGroup;
        public int presentationPriority = 50;
        public bool operatorTarget, queueable = true, initiallyAvailable, futureNeighborQueue, traceFree;
        [SerializeReference] public Condition visible = new AlwaysCondition();
        public Requirement[] requirements = Array.Empty<Requirement>();
        public Requirement[] queueRequirements = Array.Empty<Requirement>();
        [SerializeReference] public Number duration = new ConstantNumber(1);
        [SerializeReference] public Number baseTrace = new ConstantNumber(0);
        [SerializeReference] public Number useLimit = new ConstantNumber(0);
        [SerializeReference] public Effect[] effects = Array.Empty<Effect>();
    }
}
