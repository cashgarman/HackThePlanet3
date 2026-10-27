using UnityEngine;

namespace HackThePlanet
{
	/// <summary>Scriptable definition of a status effect applied during operations.</summary>
	[CreateAssetMenu(menuName = "Hack The Planet/Status")]
	public sealed class StatusDefinition : GameplayDefinition
	{
		/// <summary>Gets or sets whether this status disables defensive responses.</summary>
		public bool disablesDefense, preventsTrace;
	}
}