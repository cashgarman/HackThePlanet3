using System;
using UnityEngine;
namespace HackThePlanet
{
    [Serializable] public sealed class RigStarterPart
    {
        public RigModuleDefinition module;
        public int row,column;
        public bool rotated;
    }
    [CreateAssetMenu(menuName="Hack The Planet/Rig Rules")]
    public sealed class RigRulesDefinition : GameplayDefinition
    {
        public RigChassisDefinition startingChassis;
        public RigStarterPart[] startingParts=Array.Empty<RigStarterPart>();
        [Range(0,1)] public float saleFraction=.75f, researchSaleFraction=1, minimumTraceFraction=.2f;
        public float maximumCompute=3, maximumBandwidth=3, memoryReclaim=4;
        public bool allowRotation=true, requireValidBuild=true;
    }
}
