using System;
using UnityEngine;

namespace HackThePlanet
{
	/// <summary>Initial module placement when starting a rig build.</summary>
	[Serializable]
	public sealed class RigStarterPart
	{
		/// <summary>Gets or sets the module asset to place.</summary>
		public RigModuleDefinition module;

		/// <summary>Gets or sets the grid row and column for placement.</summary>
		public int row, column;

		/// <summary>Gets or sets whether the module is placed rotated.</summary>
		public bool rotated;
	}

	/// <summary>Scriptable global rules for rig building, sales, limits, and starter loadout.</summary>
	[CreateAssetMenu(menuName = "Hack The Planet/Rig Rules")]
	public sealed class RigRulesDefinition : GameplayDefinition
	{
		/// <summary>Gets or sets the default chassis for new rigs.</summary>
		public RigChassisDefinition startingChassis;

		/// <summary>Gets or sets modules pre-placed on the starting chassis.</summary>
		public RigStarterPart[] startingParts = Array.Empty<RigStarterPart>();

		/// <summary>Gets or sets sale refund fractions (0–1) for modules and research, and minimum trace fraction (default 0.2).</summary>
		[Range(0, 1)] public float saleFraction = .75f, researchSaleFraction = 1, minimumTraceFraction = .2f;

		/// <summary>Gets or sets maximum compute and bandwidth (default 3 each) and memory reclaimed per action (default 4).</summary>
		public float maximumCompute = 3, maximumBandwidth = 3, memoryReclaim = 4;

		/// <summary>Gets or sets whether rotation is allowed and builds must be valid before confirming.</summary>
		public bool allowRotation = true, requireValidBuild = true;
	}
}