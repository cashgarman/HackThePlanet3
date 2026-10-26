using System;
using UnityEngine;

namespace HackThePlanet
{
    [CreateAssetMenu(menuName="Hack The Planet/Technology")]
    public sealed class TechnologyDefinition : GameplayDefinition
    {
        public string branch;
        public int cost;
        [Range(0,4)] public int waypointTier;
        public bool special;
        public Vector2 position;
        public TechnologyDefinition[] parents = Array.Empty<TechnologyDefinition>();
        public ActionDefinition[] grants = Array.Empty<ActionDefinition>();
        public Modifier[] modifiers = Array.Empty<Modifier>();
        public Trigger[] triggers = Array.Empty<Trigger>();
    }
}
