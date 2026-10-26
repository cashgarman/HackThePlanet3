using System;
using UnityEngine;

namespace HackThePlanet
{
    [Serializable] public sealed class HardwareTier
    {
        public int price;
        public Modifier[] modifiers = Array.Empty<Modifier>();
    }
    [CreateAssetMenu(menuName="Hack The Planet/Hardware")]
    public sealed class HardwareDefinition : GameplayDefinition
    {
        public string category, statLabel, displayedStat;
        public Requirement[] requirements = Array.Empty<Requirement>();
        public HardwareTier[] tiers = Array.Empty<HardwareTier>();
    }
}
