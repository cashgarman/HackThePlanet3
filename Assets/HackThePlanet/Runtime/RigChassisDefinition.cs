using UnityEngine;

namespace HackThePlanet
{
	/// <summary>Scriptable rig chassis with grid size, capacities, and unlock requirements.</summary>
	[CreateAssetMenu(menuName = "Hack The Planet/Rig Chassis")]
	public sealed class RigChassisDefinition : GameplayDefinition
	{
		/// <summary>
		///     Gets or sets chassis tier, grid rows/columns (defaults 2×3), price, jobs required to unlock, and module slot
		///     count (default 2).
		/// </summary>
		public int tier = 1, rows = 2, columns = 3, price, requiredJobs, slots = 2;

		/// <summary>Gets or sets storage (default 80), memory pool (40), power (12), and cooling (8) capacities.</summary>
		public float storageCapacity = 80, memoryPool = 40, powerCapacity = 12, coolingCapacity = 8;
	}
}