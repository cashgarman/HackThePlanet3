using System;
using UnityEngine;

namespace HackThePlanet
{
    [CreateAssetMenu(menuName="Hack The Planet/Node Type")]
    public sealed class NodeTypeDefinition : GameplayDefinition
    {
        public NodeKind glyph;
        public Color color = Color.cyan;
        public string[] tags = Array.Empty<string>();
        [SerializeReference] public Number security = new ConstantNumber(1);
        public Aura[] auras = Array.Empty<Aura>();
    }
}
