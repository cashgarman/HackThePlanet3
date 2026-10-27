using UnityEngine;
namespace HackThePlanet
{
    [CreateAssetMenu(menuName="Hack The Planet/Rig Chassis")]
    public sealed class RigChassisDefinition : GameplayDefinition
    {
        public int tier=1, rows=2, columns=3, price, requiredJobs, slots=2;
        public float storageCapacity=80, memoryPool=40, powerCapacity=12, coolingCapacity=8;
    }
}
