using System;
using UnityEngine;

namespace HackThePlanet
{
    [CreateAssetMenu(menuName="Hack The Planet/Status")]
    public sealed class StatusDefinition : GameplayDefinition
    {
        public bool disablesDefense, preventsTrace;
    }
}
