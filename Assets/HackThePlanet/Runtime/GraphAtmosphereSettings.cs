using UnityEngine;

namespace HackThePlanet
{
	/// <summary>Host-owned atmosphere controls for research and network graph views.</summary>
	public sealed class GraphAtmosphereSettings : ScriptableObject
	{
		/// <summary>Gets or sets cloud density behind research graphs.</summary>
		[Range(.25f, 3f)] public float ResearchCloudDensity = 1.5f;
		/// <summary>Gets or sets cloud density behind network graphs.</summary>
		[Range(.25f, 3f)] public float NetworkCloudDensity = 1.8f;
		/// <summary>Gets or sets the research view's data beam count.</summary>
		[Range(0, 32)] public int ResearchDataBeamCount;
		/// <summary>Gets or sets the network view's data beam count.</summary>
		[Range(0, 32)] public int NetworkDataBeamCount = 7;
		/// <summary>Gets or sets how widely data beam directions are randomized.</summary>
		[Range(0, 1)] public float DataBeamDirectionRandomness = 1f;
		/// <summary>Gets or sets the deterministic seed for data beam placement.</summary>
		public int DataBeamSeed = 83;
	}
}
