using System;
using UnityEngine;

namespace HackThePlanet
{
	/// <summary>Scriptable rig module placed on the chassis grid with power, heat, and stat modifiers.</summary>
	[CreateAssetMenu(menuName = "Hack The Planet/Rig Module")]
	public sealed class RigModuleDefinition : GameplayDefinition
	{
		/// <summary>Gets or sets module family, research branch (default INTRUSION), flavour text, and synergy id.</summary>
		public string family, branch = "INTRUSION", flavour, synergy;

		/// <summary>Gets or sets tier, footprint width/height (default 1×1), and purchase price.</summary>
		public int tier = 1, width = 1, height = 1, price;

		/// <summary>Gets or sets whether the module can be rotated on the grid (default true).</summary>
		public bool rotatable = true;

		/// <summary>Gets or sets power demand, heat output, and optional power/cooling capacity contributions.</summary>
		public float powerDemand, heatOutput, powerCapacity, coolingCapacity;

		/// <summary>Gets or sets prerequisites to install or purchase.</summary>
		public Requirement[] requirements = Array.Empty<Requirement>();

		/// <summary>Gets or sets stat modifiers while the module is installed.</summary>
		public Modifier[] modifiers = Array.Empty<Modifier>();
	}
}