using System;
using UnityEngine;
namespace HackThePlanet
{
    [CreateAssetMenu(menuName="Hack The Planet/Rig Module")]
    public sealed class RigModuleDefinition : GameplayDefinition
    {
        public string family, branch="INTRUSION", flavour, synergy;
        public int tier=1, width=1, height=1, price;
        public bool rotatable=true;
        public float powerDemand, heatOutput, powerCapacity, coolingCapacity;
        public Requirement[] requirements=Array.Empty<Requirement>();
        public Modifier[] modifiers=Array.Empty<Modifier>();
    }
}
