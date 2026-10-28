using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Cashg.GraphExplorer
{
	/// <summary>Represents graph visual settings.</summary>
	[CreateAssetMenu(menuName = "Graph Explorer/Node visual settings")]
	[MovedFrom(true, "JobHarvester.Graph", "Assembly-CSharp", "GraphVisualSettings")]
	public sealed class GraphVisualSettings : ScriptableObject
	{
		/// <summary>Gets or sets whether a spatial graph is prewarmed before it becomes visible.</summary>
		[Header("Force-directed layout")]
		public bool PrewarmLayoutBeforeRender = true;

		/// <summary>Gets or sets the number of force-layout steps per frame while prewarming.</summary>
		[Range(4, 64)] public int PrewarmLayoutStepsPerFrame = 16;

		/// <summary>Gets or sets the density multiplier for the drifting cloud banks behind the graph.</summary>
		[Header("Atmosphere")]
		[Range(.25f, 3f)] public float CloudDensity = 1.5f;

		/// <summary>Gets or sets how many intermittent data beams are created behind the graph.</summary>
		[Range(0, 32)] public int DataBeamCount = 7;

		/// <summary>Gets or sets how far beam directions spread from the default horizontal direction.</summary>
		[Range(0, 1)] public float DataBeamDirectionRandomness = 1f;

		/// <summary>Gets or sets the deterministic seed used to place and aim data beams.</summary>
		public int DataBeamSeed = 83;

		/// <summary>Gets or sets whether cel shading is applied.</summary>
		[Header("Cel shading")] public bool CelShading = true;

		/// <summary>Gets or sets the number of lighting bands used by cel shading.</summary>
		[Range(2, 5)] public int LightBands = 3;

		/// <summary>Gets or sets the brightness of shaded surfaces.</summary>
		[Range(0, 1)] public float ShadowBrightness = .38f;

		/// <summary>Gets or sets the softness of transitions between lighting bands.</summary>
		[Range(0, .15f)] public float BandSoftness = .015f;

		/// <summary>Gets or sets the world-space direction of the surface light.</summary>
		public Vector3 LightDirection = new(-.4f, .8f, -.5f);

		/// <summary>Gets or sets the surface emission amount.</summary>
		[Range(0, 1)] public float SurfaceEmission = .08f;

		/// <summary>Gets or sets whether node outlines are rendered.</summary>
		[Header("Silhouette and crease detection")]
		public bool Outlines = true;

		/// <summary>Gets or sets the outline color.</summary>
		public Color OutlineColor = new(.012f, .018f, .028f, 1);

		/// <summary>Gets or sets the outline width in screen pixels.</summary>
		[Range(.5f, 4)] public float OutlinePixels = 1.25f;

		/// <summary>Gets or sets the outline opacity.</summary>
		[Range(0, 1)] public float OutlineOpacity = .95f;

		/// <summary>Gets or sets the minimum surface-normal angle that produces a crease.</summary>
		[Tooltip("Minimum angle between adjacent surface normals that produces a crease.")] [Range(5, 120)]
		public float CreaseAngle = 48f;

		/// <summary>Gets or sets the relative depth threshold for edge detection.</summary>
		[Tooltip("Relative view-depth difference that produces an edge.")] [Range(.001f, .2f)]
		public float DepthThreshold = .025f;

		/// <summary>Gets or sets whether subtle node bloom is rendered.</summary>
		[Header("Subtle node bloom")] public bool Bloom = true;

		/// <summary>Gets or sets the bloom intensity.</summary>
		[Range(0, 1)] public float BloomIntensity = .16f;

		/// <summary>Gets or sets the bloom blur radius in screen pixels.</summary>
		[Tooltip("Blur standard deviation in screen pixels at native resolution.")] [Range(1, 16)]
		public float BloomRadius = 3.5f;

		/// <summary>Gets or sets whether selected nodes receive a glow.</summary>
		[Header("Selected node glow")] public bool SelectionGlow = true;

		/// <summary>Gets or sets the selected-node glow color.</summary>
		[ColorUsage(false, true)] public Color SelectionColor = new(.4f, .85f, 1f, 1);

		/// <summary>Gets or sets the selected-node glow intensity.</summary>
		[Range(0, 5)] public float SelectionGlowIntensity = 1.7f;

		/// <summary>Gets or sets the selected-node rim intensity.</summary>
		[Range(0, 4)] public float SelectionRimIntensity = .65f;

		/// <summary>Gets or sets the selected-node glow radius in pixels.</summary>
		[Range(4, 64)] public float SelectionGlowRadius = 18f;

		/// <summary>Gets or sets the selection pulse frequency in hertz.</summary>
		[Range(0, 4)] public float PulseHz = .8f;

		/// <summary>Gets or sets the selection pulse amplitude.</summary>
		[Range(0, .8f)] public float PulseAmount = .22f;

		/// <summary>Gets or sets whether a selection ring is displayed.</summary>
		public bool ShowSelectionRing;

		/// <summary>Gets or sets whether host-provided node progress is displayed.</summary>
		[Header("Host node progress")] public bool ShowNodeProgress = true;

		/// <summary>Gets or sets the node progress color.</summary>
		public Color ProgressColor = new(.25f, .95f, .48f, 1);

		/// <summary>Gets or sets the unfilled node progress track color.</summary>
		public Color ProgressTrackColor = new(.16f, .23f, .20f, 1);

		/// <summary>Gets or sets the node progress stroke width as a fraction of node radius.</summary>
		[Range(.01f, .2f)] public float ProgressWidth = .07f;

		/// <summary>Gets or sets the progress ring radius scale relative to the node.</summary>
		[Range(2.3f, 4f)] public float ProgressRadiusScale = 2.45f;

		/// <summary>Gets or sets the completion badge size relative to the node.</summary>
		[Range(.15f, .8f)] public float CompletionBadgeSize = .4f;

		/// <summary>Calculates the current selection pulse multiplier.</summary>
		/// <param name="seconds">Elapsed time in seconds.</param>
		/// <param name="reducedMotion">Whether motion effects should be minimized.</param>
		/// <returns>The scale multiplier for the selection effect.</returns>
		public float SelectionPulse(float seconds, bool reducedMotion)
		{
			return reducedMotion
				? 1f
				: 1f + Mathf.Clamp(PulseAmount, 0, .8f) * Mathf.Sin(seconds * Mathf.PI * 2f * Mathf.Max(0, PulseHz));
		}

		/// <summary>Applies cel shading and selected-node material properties.</summary>
		/// <param name="material">Material whose shader properties are updated.</param>
		public void ApplySurface(Material material)
		{
			// Clamp serialized settings at runtime and provide a valid light direction before updating shader values.
			material.SetFloat("_CelShading", CelShading ? 1 : 0);
			material.SetFloat("_LightBands", Mathf.Clamp(LightBands, 2, 5));
			material.SetFloat("_ShadowBrightness", Mathf.Clamp01(ShadowBrightness));
			material.SetFloat("_BandSoftness", Mathf.Clamp(BandSoftness, 0, .15f));
			var light = LightDirection.sqrMagnitude > 1e-6f ? LightDirection.normalized : Vector3.up;
			material.SetVector("_LightDirection", light);
			material.SetFloat("_SurfaceEmission", Mathf.Clamp01(SurfaceEmission));
			material.SetVector("_SelectionColor", SelectionColor.linear);
			material.SetFloat("_SelectionRimIntensity", SelectionGlow ? Mathf.Max(0, SelectionRimIntensity) : 0);
		}
	}
}
