using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Cashg.GraphExplorer
{
	/// <summary>Explicit owner of one camera's explorer resources. Does not own the camera or graph.</summary>
	public sealed class GraphRenderingSession : IDisposable
	{
		private readonly bool ownsEffects;
		private readonly bool previousEffectsEnabled;
		private readonly GraphResourceConfiguration previousResources;
		private readonly GraphVisualSettings previousSettings;
		private bool disposed;

		private GraphRenderingSession(Camera camera, GraphResourceConfiguration resources,
									  GraphPresentationProfile profile, GraphExplorerOptions options)
		{
			Camera = camera;
			Effects = camera.GetComponent<GraphNodeEffects>();
			if (Effects != null && Effects.Renderer != null)
				throw new InvalidOperationException("This camera already has an explorer renderer.");
			ownsEffects = Effects == null;

			// Preserve borrowed effect settings so disposing the session can restore the host camera exactly.
			if (!ownsEffects)
			{
				previousSettings = Effects.Settings;
				previousResources = Effects.Resources;
				previousEffectsEnabled = Effects.enabled;
			}

			try
			{
				// Allocate session resources inside the cleanup boundary to release partial initialization on failure.
				Renderer = new GraphRenderer(profile, options, resources);
				Layout = new ForceLayout(profile);
				Navigation = new GraphNavigation(camera);
				Atmosphere = new GraphAtmosphere(camera, resources);
				if (ownsEffects) Effects = camera.gameObject.AddComponent<GraphNodeEffects>();
				Effects.Resources = resources;
				Effects.Settings = Renderer.VisualSettings;
				Effects.Renderer = Renderer;
				Effects.enabled = true;
			}
			catch
			{
				Dispose();
				throw;
			}
		}

		/// <summary>Gets the camera used by this session.</summary>
		public Camera Camera { get; }
		/// <summary>Gets the graph renderer owned by this session.</summary>
		public GraphRenderer Renderer { get; }
		/// <summary>Gets the force layout owned by this session.</summary>
		public ForceLayout Layout { get; }
		/// <summary>Gets the navigation controller owned by this session.</summary>
		public GraphNavigation Navigation { get; }
		/// <summary>Gets the atmosphere controller owned by this session.</summary>
		public GraphAtmosphere Atmosphere { get; }
		/// <summary>Gets the camera effects component used by this session.</summary>
		public GraphNodeEffects Effects { get; }

		/// <summary>Releases session resources and restores reused camera effects state.</summary>
		public void Dispose()
		{
			if (disposed) return;
			disposed = true;
			Navigation?.Dispose();
			if (Effects != null)
			{
				// Disable first so its render subscription and pass materials are released immediately.
				Effects.enabled = false;
				Effects.Renderer = null;
				if (ownsEffects)
				{
					if (Application.isPlaying) Object.Destroy(Effects);
					else Object.DestroyImmediate(Effects);
				}
				else
				{
					Effects.Settings = previousSettings;
					Effects.Resources = previousResources;
					Effects.enabled = previousEffectsEnabled;
				}
			}

			Atmosphere?.Dispose();
			Renderer?.Dispose();
		}

		/// <summary>Validates resources and creates an explorer rendering session for a camera.</summary>
		/// <param name="camera">Camera to render the graph through.</param>
		/// <param name="resources">Graph rendering resources.</param>
		/// <param name="profile">Optional domain presentation profile.</param>
		/// <param name="options">Optional renderer budgets.</param>
		/// <returns>The initialized rendering session.</returns>
		public static GraphRenderingSession Initialize(Camera camera, GraphResourceConfiguration resources,
													   GraphPresentationProfile profile = null,
													   GraphExplorerOptions options = null)
		{
			if (camera == null) throw new ArgumentNullException(nameof(camera));
			if (resources == null) throw new ArgumentNullException(nameof(resources));
			// Validate before allocating meshes, materials, effects or atmosphere objects.
			foreach (var key in new[] { "GraphNodeToon", "GraphEdge", "GraphSky", "GraphFog", "GraphNodePost" })
				if (resources.Load<Shader>(key) == null)
					throw new InvalidOperationException("Missing explorer shader: " + key);
			if (resources.Load<Material>("GraphNodeMaskMaterial") == null)
				throw new InvalidOperationException("Missing node mask material.");
			return new GraphRenderingSession(camera, resources, profile ?? new GraphPresentationProfile(),
											 options ?? new GraphExplorerOptions());
		}
	}
}
