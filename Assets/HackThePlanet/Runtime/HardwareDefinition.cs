using System;
using UnityEngine;

namespace HackThePlanet
{
	/// <summary>Upgrade tier for hardware with price and stat modifiers.</summary>
	[Serializable]
	public sealed class HardwareTier
	{
		/// <summary>Gets or sets the purchase price for this tier.</summary>
		public int price;

		/// <summary>Gets or sets modifiers applied when this tier is owned.</summary>
		public Modifier[] modifiers = Array.Empty<Modifier>();
	}

	/// <summary>Scriptable hardware item with category, display stats, requirements, and tiered upgrades.</summary>
	[CreateAssetMenu(menuName = "Hack The Planet/Hardware")]
	public sealed class HardwareDefinition : GameplayDefinition
	{
		/// <summary>Gets or sets the hardware category, stat label, and displayed stat key.</summary>
		public string category, statLabel, displayedStat;

		/// <summary>Gets or sets prerequisites that must pass before purchase.</summary>
		public Requirement[] requirements = Array.Empty<Requirement>();

		/// <summary>Gets or sets purchasable upgrade tiers.</summary>
		public HardwareTier[] tiers = Array.Empty<HardwareTier>();
	}
}