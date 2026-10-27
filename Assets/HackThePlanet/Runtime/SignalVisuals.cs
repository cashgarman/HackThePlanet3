using System;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace HackThePlanet
{
	/// <summary>Workstation display effects tier controlling animation and shader overlays.</summary>
	public enum SignalEffects
	{
		/// <summary>Full signal field, glass, and animated overlays.</summary>
		Full,

		/// <summary>Static shaders without time-based animation.</summary>
		Reduced,

		/// <summary>Effects disabled; fallback theme only.</summary>
		Off
	}

	// Two shared procedural textures serve the entire workstation, including rebuilt UI trees.
	// The glass is a transparent decoration: text and hit testing never pass through a distortion shader.
	/// <summary>Procedural signal-field and phosphor-glass overlays driven by trace and task state.</summary>
	public sealed class SignalVisuals : IDisposable
	{
		// PlayerPrefs keys for the saved display effect toggles and the legacy combined mode.
		// Legacy PlayerPrefs key for the combined FX mode before per-layer toggles existed.
		private const string LegacyPreferenceKey = "HackThePlanet.SignalEffects";

		// PlayerPrefs key for the viewport signal-field overlay toggle.
		private const string FieldPreferenceKey = "HackThePlanet.SignalField";

		// PlayerPrefs key for the phosphor glass screen overlay toggle.
		private const string GlassPreferenceKey = "HackThePlanet.PhosphorGlass";

		// PlayerPrefs key for time-based shader animation on display effects.
		private const string AnimatePreferenceKey = "HackThePlanet.AnimateDisplayEffects";

		// Procedural shader materials for the field and glass overlays.
		private readonly Material fieldMaterial, glassMaterial;

		// UI images bound to the generated field, glass, and intel textures.
		private Image fieldImage, glassImage, intelImage;

		// Render targets that hold the procedural field, glass, and intel static passes.
		private RenderTexture fieldTexture, glassTexture, intelTexture;

		// Forces a texture redraw after preference or attachment changes.
		private bool invalidated = true;

		// Last shader state vector sent to the display materials.
		private Vector4 lastState;

		// Next unscaled time when an animated frame may render again.
		private float nextFrame;

		// When true, the field shader uses the no-intel variant for this viewport.
		private bool noIntel;

		/// <summary>Loads shaders and player preferences for workstation display effects.</summary>
		public SignalVisuals()
		{
			// Read the saved display effect toggles.
			LoadPreferences();

			// Load both display shaders from Resources.
			var field = Resources.Load<Shader>("HackThePlanet/Shaders/SignalField");
			var glass = Resources.Load<Shader>("HackThePlanet/Shaders/PhosphorGlass");

			// Create materials only when both shaders exist and run on this GPU.
			if (field != null && glass != null && field.isSupported && glass.isSupported)
			{
				// Create one hidden material per shader.
				fieldMaterial = new Material(field) { hideFlags = HideFlags.HideAndDontSave };
				glassMaterial = new Material(glass) { hideFlags = HideFlags.HideAndDontSave };
			}
			else
			{
				// Fall back to the static theme when a shader is missing.
				Debug.LogWarning("Ghost Frequency shaders are unavailable; using the static workstation theme.");
			}
		}

		/// <summary>Gets whether the viewport signal-field overlay is enabled.</summary>
		public bool SignalFieldEnabled { get; private set; }

		/// <summary>Gets whether the full-screen phosphor glass overlay is enabled.</summary>
		public bool PhosphorGlassEnabled { get; private set; }

		/// <summary>Gets whether display shaders animate over time.</summary>
		public bool AnimateDisplayEffects { get; private set; }

		/// <summary>Gets the combined legacy effects mode derived from individual toggles.</summary>
		public SignalEffects Mode => !SignalFieldEnabled && !PhosphorGlassEnabled ? SignalEffects.Off
			: AnimateDisplayEffects ? SignalEffects.Full : SignalEffects.Reduced;

		/// <summary>Gets whether required shaders loaded successfully.</summary>
		public bool Available => fieldMaterial != null && glassMaterial != null;

		/// <summary>Gets the render target bound to the signal-field decoration.</summary>
		public RenderTexture FieldTexture => fieldTexture;

		/// <summary>Gets the render target bound to the phosphor glass decoration.</summary>
		public RenderTexture GlassTexture => glassTexture;

		/// <summary>Gets whether time-based shader animation is active.</summary>
		public bool Animate => AnimateDisplayEffects;

		/// <summary>Gets the compact FX label for the current <see cref="Mode" />.</summary>
		public string Label => LabelFor(Mode);

		/// <summary>Releases render textures and shader materials.</summary>
		public void Dispose()
		{
			// Release render textures and shader materials.
			Release(ref fieldTexture);
			Release(ref glassTexture);
			Release(ref intelTexture);
			if (fieldMaterial != null)
				Object.Destroy(fieldMaterial);
			if (glassMaterial != null)
				Object.Destroy(glassMaterial);
		}

		/// <summary>Gets the workstation FX menu caption for an effects mode.</summary>
		/// <param name="mode">Effects tier.</param>
		/// <returns>Short label such as FX+, FX−, or FX0.</returns>
		public static string LabelFor(SignalEffects mode)
		{
			return mode == SignalEffects.Full ? "FX+" : mode == SignalEffects.Reduced ? "FX−" : "FX0";
		}

		// Reads saved FX toggles from PlayerPrefs, migrating legacy combined mode when needed.
		private void LoadPreferences()
		{
			// Prefer the individual toggles when any of them has been saved.
			if (PlayerPrefs.HasKey(FieldPreferenceKey) || PlayerPrefs.HasKey(GlassPreferenceKey) ||
				PlayerPrefs.HasKey(AnimatePreferenceKey))
			{
				// Read each toggle, defaulting to on.
				SignalFieldEnabled = PlayerPrefs.GetInt(FieldPreferenceKey, 1) != 0;
				PhosphorGlassEnabled = PlayerPrefs.GetInt(GlassPreferenceKey, 1) != 0;
				AnimateDisplayEffects = PlayerPrefs.GetInt(AnimatePreferenceKey, 1) != 0;
				return;
			}

			// Otherwise migrate the legacy combined FX mode.
			ApplyLegacyMode((SignalEffects)Mathf.Clamp(PlayerPrefs.GetInt(LegacyPreferenceKey, 0), 0, 2));
		}

		// Maps a legacy combined FX mode onto the three individual overlay toggles.
		private void ApplyLegacyMode(SignalEffects mode)
		{
			// Map the legacy mode onto the three toggles.
			SignalFieldEnabled = mode != SignalEffects.Off;
			PhosphorGlassEnabled = mode != SignalEffects.Off;
			AnimateDisplayEffects = mode == SignalEffects.Full;
		}

		/// <summary>Applies a legacy combined FX mode to the individual preference toggles.</summary>
		/// <param name="mode">Target effects tier.</param>
		/// <param name="persist">When true, writes PlayerPrefs.</param>
		public void SetMode(SignalEffects mode, bool persist = false)
		{
			// Apply the mode and mark textures for a redraw.
			ApplyLegacyMode(mode);
			invalidated = true;

			// Save only when requested.
			if (persist)
				SavePreferences();

			// Refresh decoration visibility.
			UpdateVisibility();
		}

		/// <summary>Enables or disables the viewport signal-field overlay.</summary>
		/// <param name="enabled">Desired state.</param>
		/// <param name="persist">When true, writes PlayerPrefs.</param>
		public void SetSignalFieldEnabled(bool enabled, bool persist = false)
		{
			// Skip work when the toggle is already in that state.
			if (SignalFieldEnabled == enabled)
				return;

			// Store the new state and mark textures for a redraw.
			SignalFieldEnabled = enabled;
			invalidated = true;

			// Save only when requested.
			if (persist)
				SavePreferences();

			// Refresh decoration visibility.
			UpdateVisibility();
		}

		/// <summary>Enables or disables the phosphor glass screen overlay.</summary>
		/// <param name="enabled">Desired state.</param>
		/// <param name="persist">When true, writes PlayerPrefs.</param>
		public void SetPhosphorGlassEnabled(bool enabled, bool persist = false)
		{
			// Skip work when the toggle is already in that state.
			if (PhosphorGlassEnabled == enabled)
				return;

			// Store the new state and mark textures for a redraw.
			PhosphorGlassEnabled = enabled;
			invalidated = true;

			// Save only when requested.
			if (persist)
				SavePreferences();

			// Refresh decoration visibility.
			UpdateVisibility();
		}

		/// <summary>Enables or disables time-based animation in display shaders.</summary>
		/// <param name="enabled">Desired state.</param>
		/// <param name="persist">When true, writes PlayerPrefs.</param>
		public void SetAnimateDisplayEffects(bool enabled, bool persist = false)
		{
			// Skip work when the toggle is already in that state.
			if (AnimateDisplayEffects == enabled)
				return;

			// Store the new state and mark textures for a redraw.
			AnimateDisplayEffects = enabled;
			invalidated = true;

			// Save only when requested.
			if (persist)
				SavePreferences();

			// Refresh decoration visibility.
			UpdateVisibility();
		}

		// Persists overlay toggles and the legacy combined mode to PlayerPrefs.
		private void SavePreferences()
		{
			// Write each toggle and the legacy combined mode.
			PlayerPrefs.SetInt(FieldPreferenceKey, SignalFieldEnabled ? 1 : 0);
			PlayerPrefs.SetInt(GlassPreferenceKey, PhosphorGlassEnabled ? 1 : 0);
			PlayerPrefs.SetInt(AnimatePreferenceKey, AnimateDisplayEffects ? 1 : 0);
			PlayerPrefs.SetInt(LegacyPreferenceKey, (int)Mode);

			// Flush the preferences to disk.
			PlayerPrefs.Save();
		}

		/// <summary>Inserts the signal-field image behind a viewport and binds <see cref="FieldTexture" />.</summary>
		/// <param name="viewport">Host element receiving the decoration.</param>
		/// <param name="noIntel">When true, uses the no-intel shader variant for that viewport.</param>
		public void AttachField(VisualElement viewport, bool noIntel = false)
		{
			// Remember the shader variant and create the decoration image.
			this.noIntel = noIntel;
			fieldImage = Decoration("signal-field");
			fieldImage.image = fieldTexture;

			// Place the field behind the viewport content.
			viewport.Insert(0, fieldImage);

			// Mark textures for a redraw and refresh visibility.
			invalidated = true;
			UpdateVisibility();
		}

		/// <summary>Binds procedural static rendering to the focused node intel card image.</summary>
		/// <param name="image">Target UI image from <see cref="GraphViewport.IntelStatic" />.</param>
		public void AttachIntelCard(Image image)
		{
			// Bind the intel static image to the shared texture.
			intelImage = image;
			intelImage.image = intelTexture;

			// Mark textures for a redraw and refresh visibility.
			invalidated = true;
			UpdateVisibility();
		}

		/// <summary>Adds the phosphor glass overlay on top of a UI root.</summary>
		/// <param name="root">Workstation root visual element.</param>
		public void AttachGlass(VisualElement root)
		{
			// Create the glass image and place it on top of the root.
			glassImage = Decoration("phosphor-glass");
			glassImage.image = glassTexture;
			root.Add(glassImage);

			// Mark textures for a redraw and refresh visibility.
			invalidated = true;
			UpdateVisibility();
		}

		// Creates a full-bleed, non-interactive decoration image for an overlay layer.
		private static Image Decoration(string name)
		{
			// Create a non-interactive image that fills its parent.
			var image = new Image
				{ name = name, pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
			image.style.position = Position.Absolute;
			image.style.left = image.style.top = image.style.right = image.style.bottom = 0;

			return image;
		}

		// Shows or hides field, glass, and intel images based on toggles and shader availability.
		private void UpdateVisibility()
		{
			// Show the field when its toggle and the shaders are on.
			if (fieldImage != null)
				fieldImage.style.display = Available && SignalFieldEnabled ? DisplayStyle.Flex : DisplayStyle.None;

			// Show the glass when its toggle and the shaders are on.
			if (glassImage != null)
				glassImage.style.display = Available && PhosphorGlassEnabled ? DisplayStyle.Flex : DisplayStyle.None;

			// Show the intel static with the field toggle.
			if (intelImage != null)
				intelImage.style.display = Available && SignalFieldEnabled ? DisplayStyle.Flex : DisplayStyle.None;
		}

		/// <summary>Updates shader parameters and repaints render targets from simulation trace and task state.</summary>
		/// <param name="state">Live simulation; idle when inactive.</param>
		/// <param name="research">When true, uses research-screen shader parameters.</param>
		public void Tick(GameSimulation state, bool research)
		{
			// Skip all work when the shaders failed to load.
			if (!Available)
				return;

			// Read which overlays are enabled.
			var renderField = SignalFieldEnabled;
			var renderGlass = PhosphorGlassEnabled;

			// Nothing to draw when both overlays are off.
			if (!renderField && !renderGlass)
				return;

			// Size the glass target to the screen, capped at 1920x1080.
			int screenWidth = Mathf.Max(1, Screen.width), screenHeight = Mathf.Max(1, Screen.height);
			var scale = Mathf.Min(1, 1920f / screenWidth, 1080f / screenHeight);
			int glassWidth = Mathf.RoundToInt(screenWidth * scale),
				glassHeight = Mathf.RoundToInt(screenHeight * scale);
			var resized = renderGlass && (glassTexture == null || glassTexture.width != glassWidth ||
										  glassTexture.height != glassHeight);

			// Measure the field image and flag any size change.
			var width = fieldImage != null ? fieldImage.contentRect.width : 0;
			var height = fieldImage != null ? fieldImage.contentRect.height : 0;
			var fieldReady = renderField && fieldImage?.panel != null && !float.IsNaN(width) && !float.IsNaN(height) &&
							 width > 0 && height > 0;
			int fieldWidth = Mathf.Clamp(Mathf.RoundToInt(width), 256, 1280),
				fieldHeight = Mathf.Clamp(Mathf.RoundToInt(height), 128, 800);
			resized |= fieldReady && (fieldTexture == null || fieldTexture.width != fieldWidth ||
									  fieldTexture.height != fieldHeight);

			// Build the shader parameters for this frame.
			var live = state.Active && !research;
			var trace = live ? state.TraceFraction : 0;
			float working = live && !state.Op.paused && state.Op.task != null ? 1 : 0;
			var parameters = new Vector4(trace, working, research ? 1 : 0, AnimateDisplayEffects ? 1 : 0);
			var changed = parameters != lastState;

			// Skip redraws when nothing changed and nothing was resized.
			if (!invalidated && !resized)
			{
				// Static effects only redraw when the state changes.
				if (!AnimateDisplayEffects && !changed)
					return;

				// Animated effects redraw at the frame interval.
				if (AnimateDisplayEffects && Time.unscaledTime < nextFrame)
					return;
			}

			// Remember this frame's timing and state.
			nextFrame = Time.unscaledTime + 1f / 30f;
			lastState = parameters;
			invalidated = false;

			// Resize the render targets and bind them to the images.
			if (renderGlass)
				EnsureTexture(ref glassTexture, glassWidth, glassHeight, "Ghost Frequency / Glass");
			if (fieldReady)
				EnsureTexture(ref fieldTexture, fieldWidth, fieldHeight, "Ghost Frequency / Field");
			if (fieldImage != null)
				fieldImage.image = fieldTexture;
			if (glassImage != null)
				glassImage.image = glassTexture;

			// Animated effects use wall time; static effects use a fixed clock.
			var clock = AnimateDisplayEffects ? Time.unscaledTime : 13.75f;

			// Render the field and glass with the same parameters.
			fieldMaterial.SetFloat("_NoIntel", noIntel ? 1 : 0);
			if (fieldReady)
				Render(fieldMaterial, fieldTexture, parameters, clock);
			if (renderGlass)
				Render(glassMaterial, glassTexture, parameters, clock);

			// Render the node intel static from the no-intel field variant.
			if (renderField && intelImage?.panel != null &&
				intelImage.parent.resolvedStyle.display != DisplayStyle.None)
			{
				EnsureTexture(ref intelTexture, 384, 192, "Ghost Frequency / Node intel");
				intelImage.image = intelTexture;
				fieldMaterial.SetFloat("_NoIntel", 1);
				Render(fieldMaterial, intelTexture, parameters, clock);
			}
		}

		// Blits a display material into a render target with trace and animation parameters.
		private static void Render(Material material, RenderTexture target, Vector4 state, float clock)
		{
			// Pass the frame's shader parameters to the material.
			material.SetVector("_SignalState", state);
			material.SetVector("_Resolution",
							   new Vector4(target.width, target.height, 1f / target.width, 1f / target.height));
			material.SetFloat("_SignalTime", clock);

			// Remember the active render texture so it can be restored.
			var previous = RenderTexture.active;

			// Draw the material into the target texture.
			try
			{
				Graphics.Blit(Texture2D.blackTexture, target, material);
			}
			finally
			{
				// Restore the previous render texture even when the blit throws.
				RenderTexture.active = previous;
			}
		}

		// Allocates or reuses a render texture at the requested size and name.
		private static void EnsureTexture(ref RenderTexture texture, int width, int height, string name)
		{
			// Keep the existing texture when its size matches and it is created.
			if (texture != null && texture.width == width && texture.height == height && texture.IsCreated())
				return;

			// Replace the texture with one at the requested size and create it.
			Release(ref texture);
			texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
			{
				name = name, hideFlags = HideFlags.HideAndDontSave,
				filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
				useMipMap = false, autoGenerateMips = false
			};
			texture.Create();
		}

		// Releases GPU memory and destroys a render texture reference.
		private static void Release(ref RenderTexture texture)
		{
			// Skip when there is no texture to release.
			if (texture == null)
				return;

			// Free the GPU surface, destroy the object, and clear the reference.
			texture.Release();
			Object.Destroy(texture);
			texture = null;
		}
	}
}