using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace Cashg.GraphExplorer
{
	/// <summary>World-locked cloud banks and a drifting sky dome. Distant graph nodes fade toward the same haze.</summary>
	public sealed class GraphAtmosphere : IDisposable
	{
		private readonly Mesh bankMesh;
		private readonly Bank[] banks;

		private readonly Camera camera;
		private readonly Material fogMaterial;
		private readonly List<Renderer> renderers = new();
		private readonly GameObject rig;
		private readonly GameObject sky;
		private readonly Material skyMaterial;
		private readonly Mesh skyMesh;
		private float clock;
		private bool disposed;

		/// <summary>Creates sky and fog effects associated with a camera.</summary>
		/// <param name="camera">Camera whose position drives the atmosphere.</param>
		/// <param name="resources">Optional resource overrides.</param>
		public GraphAtmosphere(Camera camera, GraphResourceConfiguration resources = null)
		{
			this.camera = camera != null ? camera : throw new ArgumentNullException(nameof(camera));
			resources ??= new GraphResourceConfiguration();
			var skyShader = resources.Load<Shader>("GraphSky");
			var fogShader = resources.Load<Shader>("GraphFog");
			if (skyShader == null || fogShader == null)
				throw new InvalidOperationException("Missing Resources/GraphSky or Resources/GraphFog shader.");
			var skySource = resources.Load<Material>("GraphSkyMaterial");

			// Clone imported materials and create transient scene objects owned by this atmosphere instance.
			var fogSource = resources.Load<Material>("GraphFogMaterial");
			skyMaterial = skySource != null ? new Material(skySource) : new Material(skyShader);
			fogMaterial = fogSource != null ? new Material(fogSource) : new Material(fogShader);
			skyMaterial.name = "Graph sky";
			fogMaterial.name = "Graph fog";
			skyMaterial.renderQueue = (int)RenderQueue.Background;
			fogMaterial.renderQueue = (int)RenderQueue.Transparent;
			skyMaterial.hideFlags = HideFlags.HideAndDontSave;
			fogMaterial.hideFlags = HideFlags.HideAndDontSave;

			skyMesh = UvSphere(28, 18);
			bankMesh = Quad();
			skyMesh.hideFlags = HideFlags.HideAndDontSave;
			bankMesh.hideFlags = HideFlags.HideAndDontSave;
			sky = new GameObject("Graph Sky") { hideFlags = HideFlags.DontSave };
			sky.transform.localScale = Vector3.one * 2200f;
			renderers.Add(AddRenderer(sky, skyMesh, skyMaterial));
			rig = new GameObject("Graph Fog") { hideFlags = HideFlags.DontSave };
			banks = CreateBanks();
			PlaceSky();
			foreach (var bank in banks) Place(bank);
			RenderPipelineManager.beginCameraRendering += BeginCamera;
		}

		/// <summary>Releases atmosphere objects, materials, meshes, and render callbacks.</summary>
		public void Dispose()
		{
			// Unsubscribe before releasing transient objects, meshes, and material copies.
			if (disposed) return;
			disposed = true;
			RenderPipelineManager.beginCameraRendering -= BeginCamera;
			DestroyObject(sky);
			DestroyObject(rig);
			DestroyObject(skyMaterial);
			DestroyObject(fogMaterial);
			DestroyObject(skyMesh);
			DestroyObject(bankMesh);
		}

		private void BeginCamera(ScriptableRenderContext context, Camera renderingCamera)
		{
			// Hide these shared scene renderers from every camera except the graph camera that owns them.
			foreach (var renderer in renderers) renderer.forceRenderingOff = renderingCamera != camera;
		}

		/// <summary>Advances the sky and cloud animation.</summary>
		/// <param name="reducedMotion">Whether animation should pause.</param>
		public void Tick(bool reducedMotion)
		{
			// Freeze animation time in reduced-motion mode while keeping the sky centered on the camera.
			if (disposed) throw new ObjectDisposedException(nameof(GraphAtmosphere));
			var dt = reducedMotion ? 0f : Time.unscaledDeltaTime;
			clock += dt;
			skyMaterial.SetFloat("_GraphAtmosphereTime", clock);
			fogMaterial.SetFloat("_GraphAtmosphereTime", clock);
			PlaceSky();
			for (var i = 0; i < banks.Length; i++)
			{
				banks[i].Angle += banks[i].Speed * dt;
				Place(banks[i]);
			}
		}

		private void PlaceSky()
		{
			sky.transform.SetPositionAndRotation(camera.transform.position, Quaternion.identity);
		}

		private void Place(Bank bank)
		{
			var y = bank.Height + Mathf.Sin(clock * bank.BobSpeed + bank.Phase) * bank.Bob;
			bank.Transform.localPosition =
				new Vector3(Mathf.Cos(bank.Angle) * bank.Radius, y, Mathf.Sin(bank.Angle) * bank.Radius);
		}

		private Bank[] CreateBanks()
		{
			// Build each atmosphere layer from its own mesh and material configuration.
			var random = new Random(17);
			var created = new Bank[12];
			for (var i = 0; i < created.Length; i++)
			{
				// Use a fixed seed for repeatable near/far fog placement and independent per-renderer shader values.
				var distant = i < 7;
				var radius = Lerp(random, distant ? 250f : 125f, distant ? 460f : 195f);
				var size = Lerp(random, distant ? 150f : 78f, distant ? 240f : 130f);
				var color = Color.Lerp(new Color(0.16f, 0.26f, 0.40f), new Color(0.26f, 0.38f, 0.54f),
									   (float)random.NextDouble());
				if (i % 4 == 0) color = Color.Lerp(color, new Color(0.36f, 0.22f, 0.12f), 0.42f);
				var host = new GameObject(distant ? "Distant fog" : "Near fog") { hideFlags = HideFlags.DontSave };
				host.transform.SetParent(rig.transform, false);
				host.transform.localScale = Vector3.one * size;
				var renderer = AddRenderer(host, bankMesh, fogMaterial);
				renderers.Add(renderer);
				var block = new MaterialPropertyBlock();
				block.SetColor("_FogColor", color);
				block.SetFloat("_Opacity", Lerp(random, distant ? 0.30f : 0.12f, distant ? 0.48f : 0.22f));
				block.SetFloat("_Seed", i * 2.73f + 0.4f);
				block.SetFloat("_Frequency", distant ? 0.0065f : 0.013f);
				renderer.SetPropertyBlock(block);
				created[i] = new Bank
				{
					Transform = host.transform,
					Angle = (float)(random.NextDouble() * Math.PI * 2),
					Speed = Lerp(random, distant ? 0.008f : 0.014f, distant ? 0.02f : 0.032f) * (i % 2 == 0 ? 1f : -1f),
					Radius = radius,
					Height = Lerp(random, -28f, 46f),
					Bob = Lerp(random, 8f, distant ? 26f : 14f),
					BobSpeed = Lerp(random, 0.08f, 0.18f),
					Phase = (float)(random.NextDouble() * Math.PI * 2)
				};
			}

			return created;
		}

		private static MeshRenderer AddRenderer(GameObject host, Mesh mesh, Material material)
		{
			// Atmospheric geometry shares meshes and materials without participating in lighting or motion vectors.
			host.AddComponent<MeshFilter>().sharedMesh = mesh;
			var renderer = host.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = material;
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			renderer.receiveShadows = false;
			renderer.lightProbeUsage = LightProbeUsage.Off;
			renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
			renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
			return renderer;
		}

		private static float Lerp(Random random, float min, float max)
		{
			return Mathf.Lerp(min, max, (float)random.NextDouble());
		}

		private static Mesh Quad()
		{
			var mesh = new Mesh { name = "Graph fog bank" };
			mesh.vertices = new[]
			{
				new Vector3(-0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0),
				new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0)
			};
			mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1) };
			mesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
			mesh.RecalculateBounds();
			return mesh;
		}

		private static Mesh UvSphere(int longitude, int latitude)
		{
			// Generate latitude rings with a repeated seam vertex, then connect neighboring rings into triangles.
			var vertices = new List<Vector3>();
			var triangles = new List<int>();
			for (var y = 0; y <= latitude; y++)
			{
				var v = y / (float)latitude;
				var pitch = (v - 0.5f) * Mathf.PI;
				var ring = Mathf.Cos(pitch);
				var py = Mathf.Sin(pitch);
				for (var x = 0; x <= longitude; x++)
				{
					var yaw = x / (float)longitude * Mathf.PI * 2f;
					vertices.Add(new Vector3(Mathf.Cos(yaw) * ring, py, Mathf.Sin(yaw) * ring));
				}
			}

			var stride = longitude + 1;
			for (var y = 0; y < latitude; y++)
				for (var x = 0; x < longitude; x++)
				{
					var a = y * stride + x;
					var b = a + stride;
					triangles.Add(a);
					triangles.Add(b);
					triangles.Add(a + 1);
					triangles.Add(a + 1);
					triangles.Add(b);
					triangles.Add(b + 1);
				}

			var mesh = new Mesh { name = "Graph sky dome" };
			mesh.SetVertices(vertices);
			mesh.SetTriangles(triangles, 0);
			mesh.RecalculateBounds();
			return mesh;
		}

		private static void DestroyObject(Object value)
		{
			if (value == null) return;
			if (Application.isPlaying) Object.Destroy(value);
			else Object.DestroyImmediate(value);
		}

		private sealed class Bank
		{
			public float Angle, Speed, Radius, Height, Bob, BobSpeed, Phase;
			public Transform Transform;
		}
	}
}