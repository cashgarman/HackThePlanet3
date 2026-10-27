using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Scripting.APIUpdating;

namespace Cashg.GraphExplorer
{
	/// <summary>Camera-local, node-only outlines and bloom. No scene-wide post-process volume.</summary>
	[ExecuteAlways]
	[RequireComponent(typeof(Camera))]
	[DisallowMultipleComponent]
	[MovedFrom(true, "JobHarvester.Graph", "Assembly-CSharp", "GraphNodeEffects")]
	public sealed class GraphNodeEffects : MonoBehaviour
	{
		/// <summary>Gets or sets the visual settings used by camera effects.</summary>
		public GraphVisualSettings Settings;

		private EffectsPass pass;
		private ProgressPass progressPass;

		/// <summary>Gets or sets the renderer supplying node masks and progress data.</summary>
		public GraphRenderer Renderer { get; set; }

		/// <summary>Gets or sets resource overrides used by the effects.</summary>
		public GraphResourceConfiguration Resources { get; set; } = new();

		private void OnEnable()
		{
			RenderPipelineManager.beginCameraRendering += BeginCamera;
		}

		private void OnDisable()
		{
			// Release render-pass materials immediately when the camera component is disabled or replaced.
			RenderPipelineManager.beginCameraRendering -= BeginCamera;
			pass?.Dispose();
			pass = null;
			progressPass = null;
		}

		private void BeginCamera(ScriptableRenderContext context, Camera camera)
		{
			// Enqueue effects only for this graph camera; progress remains available without glow or outlines.
			if (camera != GetComponent<Camera>() || Renderer == null) return;
			if (Renderer.VisibleProgressIndicators > 0)
			{
				progressPass ??= new ProgressPass();
				progressPass.Renderer = Renderer;
				camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(progressPass);
			}

			if (Settings == null) Settings = Resources.Load<GraphVisualSettings>("GraphVisualSettings");
			if (Settings == null || (!Settings.Outlines && !Settings.Bloom && !Settings.SelectionGlow)) return;
			pass ??= new EffectsPass(Resources);
			pass.Setup(this);
			camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(pass);
		}

		private sealed class ProgressPass : ScriptableRenderPass
		{
			public GraphRenderer Renderer;

			public ProgressPass()
			{
				renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
			}

			public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
			{
				// Draw prepared progress instances into the active color target using the scene's existing depth.
				var resources = frameData.Get<UniversalResourceData>();
				using var builder = graph.AddRasterRenderPass<Data>("Graph node progress", out var data);
				data.Renderer = Renderer;
				builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
				builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
				builder.SetRenderFunc(static (Data d, RasterGraphContext ctx) =>
										  d.Renderer.DrawProgressOverlay(ctx.cmd));
			}

			private sealed class Data
			{
				public GraphRenderer Renderer;
			}
		}

		private sealed class EffectsPass : ScriptableRenderPass
		{
			private static readonly int NormalTexture = Shader.PropertyToID("_GraphNormalsDepth");
			private static readonly int BloomTexture = Shader.PropertyToID("_GraphBloom");
			private static readonly int GlowTexture = Shader.PropertyToID("_GraphSelectionGlow");
			private static readonly int SeedTexture = Shader.PropertyToID("_GraphSeed");
			private readonly Material composite;
			private readonly Material mask;
			private GraphNodeEffects owner;

			public EffectsPass(GraphResourceConfiguration resources)
			{
				mask = new Material(resources.Load<Material>("GraphNodeMaskMaterial"));
				composite = CoreUtils.CreateEngineMaterial(resources.Load<Shader>("GraphNodePost"));
				renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
				requiresIntermediateTexture = true;
				mask.enableInstancing = true;
			}

			public void Setup(GraphNodeEffects value)
			{
				owner = value;
			}

			public void Dispose()
			{
				CoreUtils.Destroy(mask);
				CoreUtils.Destroy(composite);
			}

			public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
			{
				var resources = frameData.Get<UniversalResourceData>();
				if (resources.isActiveTargetBackBuffer) return;

				// Translate visual settings into mask and composite shader parameters for this camera frame.
				var settings = owner.Settings;
				mask.SetVector("_SelectionColor", settings.SelectionColor.linear);
				mask.SetFloat("_BloomIntensity", settings.Bloom ? Mathf.Max(0, settings.BloomIntensity) : 0);
				mask.SetFloat("_SelectionEnabled", settings.SelectionGlow ? 1 : 0);
				composite.SetVector("_OutlineColor", settings.OutlineColor.linear);
				composite.SetVector("_OutlineParams", new Vector4(
										settings.Outlines ? Mathf.Clamp(settings.OutlinePixels, .5f, 4) : 0,
										Mathf.Clamp(settings.OutlineOpacity, 0, 1),
										Mathf.Cos(Mathf.Clamp(settings.CreaseAngle, 5, 120) * Mathf.Deg2Rad),
										Mathf.Max(.001f, settings.DepthThreshold)));
				composite.SetVector("_SelectionColor", settings.SelectionColor.linear);
				composite.SetFloat("_SelectionGlowIntensity",
								   settings.SelectionGlow ? Mathf.Max(0, settings.SelectionGlowIntensity) : 0);

				var source = resources.activeColorTexture;

				// Allocate matching normal/depth and glow seed targets, retaining the depth buffer's sample count.
				var desc = graph.GetTextureDesc(source);
				desc.name = "Graph node normals and depth";
				desc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
				desc.depthBufferBits = DepthBits.None;
				desc.msaaSamples = graph.GetTextureDesc(resources.activeDepthTexture).msaaSamples;
				desc.bindTextureMS = false;
				desc.clearBuffer = true;
				desc.clearColor = Color.clear;
				desc.filterMode = FilterMode.Point;
				var normals = graph.CreateTexture(desc);
				desc.name = "Graph node bloom and selection seed";
				var seed = graph.CreateTexture(desc);
				using (var builder =
					   graph.AddRasterRenderPass<MaskData>("Graph nodes: normal/depth and glow mask", out var data))
				{
					data.Renderer = owner.Renderer;
					data.Material = mask;
					builder.SetRenderAttachment(normals, 0);
					builder.SetRenderAttachment(seed, 1);
					// Reuse the actual opaque depth buffer: hidden nodes never leak glow.
					builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
					builder.SetRenderFunc(static (MaskData d, RasterGraphContext ctx) =>
											  d.Renderer.DrawEffectsMask(ctx.cmd, d.Material));
				}

				// RGB is subtle bloom; alpha is the wider selected-node glow.
				// Prefilter before wide blurs so sparse taps never create visible steps.
				desc.msaaSamples = MSAASamples.None;
				desc.width = Mathf.Max(1, desc.width / 2);
				desc.height = Mathf.Max(1, desc.height / 2);
				desc.filterMode = FilterMode.Bilinear;
				desc.clearBuffer = false;
				var bloom = seed;
				var glow = seed;
				desc.name = "Graph glow prefilter half";
				var halfSeed = graph.CreateTexture(desc);
				Blur(graph, seed, halfSeed, Vector4.zero, 2);
				if (settings.Bloom || settings.SelectionGlow)
					bloom = MakeBlur(graph, halfSeed, desc, Mathf.Clamp(settings.BloomRadius, 1, 16), "Graph bloom");
				if (settings.SelectionGlow)
					glow = MakeBlur(graph, halfSeed, desc, Mathf.Clamp(settings.SelectionGlowRadius, 4, 64),
									"Graph selected glow");
				var outputDesc = graph.GetTextureDesc(source);

				// Composite outlines and filtered glow into a new color target for downstream post processing.
				outputDesc.name = "Graph node outlines and bloom composite";
				outputDesc.msaaSamples = MSAASamples.None;
				outputDesc.clearBuffer = false;
				var output = graph.CreateTexture(outputDesc);
				using (var builder =
					   graph.AddRasterRenderPass<CompositeData>("Graph nodes: outlines and bloom composite",
																out var data))
				{
					data.Source = source;
					data.Normals = normals;
					data.Seed = seed;
					data.Bloom = bloom;
					data.Glow = glow;
					data.Material = composite;
					builder.UseTexture(source);
					builder.UseTexture(normals);
					builder.UseTexture(seed);
					builder.UseTexture(bloom);
					builder.UseTexture(glow);
					builder.SetRenderAttachment(output, 0);
					builder.AllowGlobalStateModification(true);
					builder.SetRenderFunc(static (CompositeData d, RasterGraphContext ctx) =>
					{
						ctx.cmd.SetGlobalTexture(NormalTexture, d.Normals);
						ctx.cmd.SetGlobalTexture(SeedTexture, d.Seed);
						ctx.cmd.SetGlobalTexture(BloomTexture, d.Bloom);
						ctx.cmd.SetGlobalTexture(GlowTexture, d.Glow);
						Blitter.BlitTexture(ctx.cmd, d.Source, new Vector4(1, 1, 0, 0), d.Material, 1);
					});
				}

				resources.cameraColor = output;
			}

			private TextureHandle MakeBlur(RenderGraph graph, TextureHandle halfSeed, TextureDesc desc, float radius,
										   string name)
			{
				// Keep Gaussian sample spacing below ~1.5 texels even at the largest editor radius.
				var levels = Mathf.Clamp(Mathf.CeilToInt(Mathf.Log(Mathf.Max(1, radius * .4f), 2)), 1, 5);
				var filtered = halfSeed;
				for (var level = 2; level <= levels; level++)
				{
					desc.width = Mathf.Max(1, desc.width / 2);
					desc.height = Mathf.Max(1, desc.height / 2);
					desc.name = name + " prefilter " + level;
					var next = graph.CreateTexture(desc);
					Blur(graph, filtered, next, Vector4.zero, 2);
					filtered = next;
				}

				desc.name = name + " horizontal";

				// Apply a separable blur to the chosen prefiltered level.
				var horizontal = graph.CreateTexture(desc);
				desc.name = name + " vertical";
				var vertical = graph.CreateTexture(desc);
				Blur(graph, filtered, horizontal, new Vector4(radius, 0, 0, 0));
				Blur(graph, horizontal, vertical, new Vector4(0, radius, 0, 0));
				return vertical;
			}

			private void Blur(RenderGraph graph, TextureHandle source, TextureHandle destination, Vector4 radius,
							  int shaderPass = 0)
			{
				// Declare source/destination dependencies before recording the matching blur or prefilter shader pass.
				using var builder = graph.AddRasterRenderPass<BlurData>(
					shaderPass == 2 ? "Graph nodes: downsample prefilter" : "Graph nodes: Gaussian blur", out var data);
				data.Source = source;
				data.Material = composite;
				data.Blur = radius;
				data.Pass = shaderPass;
				builder.UseTexture(source);
				builder.SetRenderAttachment(destination, 0);
				builder.AllowGlobalStateModification(true);
				builder.SetRenderFunc(static (BlurData d, RasterGraphContext ctx) =>
				{
					ctx.cmd.SetGlobalVector("_GraphBlurRadius", d.Blur);
					Blitter.BlitTexture(ctx.cmd, d.Source, new Vector4(1, 1, 0, 0), d.Material, d.Pass);
				});
			}

			private sealed class MaskData
			{
				public Material Material;
				public GraphRenderer Renderer;
			}

			private sealed class BlurData
			{
				public Vector4 Blur;
				public Material Material;
				public int Pass;
				public TextureHandle Source;
			}

			private sealed class CompositeData
			{
				public Material Material;
				public TextureHandle Source, Normals, Seed, Bloom, Glow;
			}
		}
	}
}