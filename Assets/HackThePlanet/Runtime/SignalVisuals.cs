using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace HackThePlanet
{
    public enum SignalEffects { Full, Reduced, Off }

    // Two shared procedural textures serve the entire workstation, including rebuilt UI trees.
    // The glass is a transparent decoration: text and hit testing never pass through a distortion shader.
    public sealed class SignalVisuals : IDisposable
    {
        private const string PreferenceKey = "HackThePlanet.SignalEffects";
        private readonly Material fieldMaterial, glassMaterial;
        private RenderTexture fieldTexture, glassTexture;
        private Image fieldImage, glassImage;
        private float nextFrame;
        private bool invalidated = true;
        private bool noIntel;
        private Vector4 lastState;
        public SignalEffects Mode { get; private set; }
        public bool Available => fieldMaterial != null && glassMaterial != null;
        public RenderTexture FieldTexture => fieldTexture;
        public RenderTexture GlassTexture => glassTexture;
        public bool Animate => Mode == SignalEffects.Full;
        public string Label => Mode == SignalEffects.Full ? "FX+" : Mode == SignalEffects.Reduced ? "FX−" : "FX0";

        public SignalVisuals()
        {
            Mode = (SignalEffects)Mathf.Clamp(PlayerPrefs.GetInt(PreferenceKey, 0), 0, 2);
            var field = Resources.Load<Shader>("HackThePlanet/Shaders/SignalField");
            var glass = Resources.Load<Shader>("HackThePlanet/Shaders/PhosphorGlass");
            if (field != null && glass != null && field.isSupported && glass.isSupported)
            {
                fieldMaterial = new Material(field) { hideFlags = HideFlags.HideAndDontSave };
                glassMaterial = new Material(glass) { hideFlags = HideFlags.HideAndDontSave };
            }
            else Debug.LogWarning("Ghost Frequency shaders are unavailable; using the static workstation theme.");
        }

        public void SetMode(SignalEffects mode, bool persist = false)
        {
            Mode = mode;
            invalidated = true;
            if (persist) { PlayerPrefs.SetInt(PreferenceKey, (int)mode); PlayerPrefs.Save(); }
            UpdateVisibility();
        }

        public void AttachField(VisualElement viewport, bool noIntel = false)
        {
            this.noIntel = noIntel;
            fieldImage = Decoration("signal-field");
            fieldImage.image = fieldTexture;
            viewport.Insert(0, fieldImage);
            invalidated = true;
            UpdateVisibility();
        }

        public void AttachGlass(VisualElement root)
        {
            glassImage = Decoration("phosphor-glass");
            glassImage.image = glassTexture;
            root.Add(glassImage);
            invalidated = true;
            UpdateVisibility();
        }

        private static Image Decoration(string name)
        {
            var image = new Image { name = name, pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            image.style.position = Position.Absolute;
            image.style.left = image.style.top = image.style.right = image.style.bottom = 0;
            return image;
        }

        private void UpdateVisibility()
        {
            var display = Available && Mode != SignalEffects.Off ? DisplayStyle.Flex : DisplayStyle.None;
            if (fieldImage != null) fieldImage.style.display = display;
            if (glassImage != null) glassImage.style.display = display;
        }

        public void Tick(GameSimulation state, bool research)
        {
            if (!Available || Mode == SignalEffects.Off) return;
            int screenWidth = Mathf.Max(1, Screen.width), screenHeight = Mathf.Max(1, Screen.height);
            float scale = Mathf.Min(1, 1920f / screenWidth, 1080f / screenHeight);
            int glassWidth = Mathf.RoundToInt(screenWidth * scale), glassHeight = Mathf.RoundToInt(screenHeight * scale);
            bool resized = glassTexture == null || glassTexture.width != glassWidth || glassTexture.height != glassHeight;
            float width = fieldImage != null ? fieldImage.contentRect.width : 0;
            float height = fieldImage != null ? fieldImage.contentRect.height : 0;
            bool fieldReady = fieldImage?.panel != null && !float.IsNaN(width) && !float.IsNaN(height) && width > 0 && height > 0;
            int fieldWidth = Mathf.Clamp(Mathf.RoundToInt(width), 256, 1280), fieldHeight = Mathf.Clamp(Mathf.RoundToInt(height), 128, 800);
            resized |= fieldReady && (fieldTexture == null || fieldTexture.width != fieldWidth || fieldTexture.height != fieldHeight);
            bool live = state.Active && !research;
            float trace = live ? state.TraceFraction : 0;
            float working = live && !state.Op.paused && state.Op.task != null ? 1 : 0;
            var parameters = new Vector4(trace, working, research ? 1 : 0, Mode == SignalEffects.Reduced ? 0 : 1);
            // Reduced mode keeps the art direction but freezes every decorative animation.
            bool changed = parameters != lastState;
            if (!invalidated && !resized)
            {
                if (Mode == SignalEffects.Reduced && !changed) return;
                if (Mode == SignalEffects.Full && Time.unscaledTime < nextFrame) return;
            }
            nextFrame = Time.unscaledTime + 1f / 30f;
            lastState = parameters;
            invalidated = false;

            EnsureTexture(ref glassTexture, glassWidth, glassHeight, "Ghost Frequency / Glass");
            if (fieldReady) EnsureTexture(ref fieldTexture, fieldWidth, fieldHeight, "Ghost Frequency / Field");
            if (fieldImage != null) fieldImage.image = fieldTexture;
            if (glassImage != null) glassImage.image = glassTexture;

            float clock = Mode == SignalEffects.Full ? Time.unscaledTime : 13.75f;
            fieldMaterial.SetFloat("_NoIntel", noIntel ? 1 : 0);
            if (fieldReady) Render(fieldMaterial, fieldTexture, parameters, clock);
            Render(glassMaterial, glassTexture, parameters, clock);
        }

        private static void Render(Material material, RenderTexture target, Vector4 state, float clock)
        {
            material.SetVector("_SignalState", state);
            material.SetVector("_Resolution", new Vector4(target.width, target.height, 1f / target.width, 1f / target.height));
            material.SetFloat("_SignalTime", clock);
            var previous = RenderTexture.active;
            try { Graphics.Blit(Texture2D.blackTexture, target, material); }
            finally { RenderTexture.active = previous; }
        }

        private static void EnsureTexture(ref RenderTexture texture, int width, int height, string name)
        {
            if (texture != null && texture.width == width && texture.height == height && texture.IsCreated()) return;
            Release(ref texture);
            texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = name, hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                useMipMap = false, autoGenerateMips = false
            };
            texture.Create();
        }

        private static void Release(ref RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            UnityEngine.Object.Destroy(texture);
            texture = null;
        }

        public void Dispose()
        {
            Release(ref fieldTexture);
            Release(ref glassTexture);
            if (fieldMaterial != null) UnityEngine.Object.Destroy(fieldMaterial);
            if (glassMaterial != null) UnityEngine.Object.Destroy(glassMaterial);
        }
    }
}
