using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cashg.GraphExplorer;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HackThePlanet
{
    [Serializable] public sealed class ContentEntry
    {
        public string key, category, file, type;
    }
    [Serializable] public sealed class ContentManifest
    {
        public int schemaVersion = 1;
        public ContentEntry[] entries = Array.Empty<ContentEntry>();
    }

    /// <summary>A fully resolved, owned graph of JSON-populated ScriptableObjects.</summary>
    public sealed class ContentBundle : IDisposable
    {
        public ContentManifest Manifest;
        public readonly Dictionary<string, ScriptableObject> Objects = new Dictionary<string, ScriptableObject>();
        public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
        public readonly List<string> Errors = new List<string>();
        public readonly HashSet<string> InvalidEntries = new HashSet<string>();
        public T Get<T>(string category) where T : ScriptableObject =>
            (T)Objects[Manifest.entries.Single(e => e.category == category).key];
        public GameDefinition Game => Get<GameDefinition>("Game");
        public ScriptableObject Resolve(string id, Type expected) => Objects.Values.OfType<GameplayDefinition>()
            .SingleOrDefault(d => d.id == id && expected.IsInstanceOfType(d));
        public void Dispose() { foreach (var obj in Objects.Values) if (obj != null) { if (Application.isPlaying) Object.Destroy(obj); else Object.DestroyImmediate(obj); } Objects.Clear(); }
    }

    /// <summary>Shared strict codec. Only the explicitly registered content types may be materialized.</summary>
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ContentJson
    {
        public static readonly Type[] DefinitionTypes = {
            typeof(GameDefinition), typeof(CompanyDefinition), typeof(ContractDefinition), typeof(NetworkDefinition),
            typeof(NodeTypeDefinition), typeof(ActionDefinition), typeof(TechnologyDefinition), typeof(HardwareDefinition),
            typeof(StatusDefinition), typeof(CareerDefinition), typeof(RewardDefinition), typeof(GameplayTextPalette),
            typeof(GraphVisualSettings), typeof(GraphNodeLabelSettings), typeof(RigChassisDefinition), typeof(RigModuleDefinition), typeof(RigRulesDefinition)
        };
        public static readonly Type[] RuleTypes = {
            typeof(ConstantNumber), typeof(StatNumber), typeof(InputNumber), typeof(BinaryNumber), typeof(ConditionalNumber), typeof(RandomNumber),
            typeof(AlwaysCondition), typeof(AllCondition), typeof(AnyCondition), typeof(NotCondition), typeof(NodeStateCondition),
            typeof(RigNeighborCondition), typeof(NodeTagCondition), typeof(ActionCondition), typeof(ResearchCondition), typeof(NamedNodeCondition), typeof(CompareCondition),
            typeof(SetStateEffect), typeof(RevealEffect), typeof(TraceEffect), typeof(ApplyStatusEffect), typeof(SecurityEffect),
            typeof(CounterEffect), typeof(ConditionalEffect), typeof(LogEffect), typeof(RecoverLoreEffect)
        };
        private static readonly Type[] ValueTypes = {
            typeof(RigStarterPart), typeof(ContentManifest), typeof(ContentEntry), typeof(NamedStat), typeof(Requirement), typeof(Modifier), typeof(Trigger), typeof(Aura), typeof(NodeSelector),
            typeof(ObjectiveDefinition), typeof(ContractActionRule), typeof(LoreFragment), typeof(HardwareTier), typeof(NetworkNode), typeof(NetworkEdge),
            typeof(RouteTier), typeof(ParallelRouteLayout), typeof(CareerRank), typeof(OutcomeRule), typeof(RewardRule), typeof(GameplayTextPalette.TermStyle),
            typeof(Vector2), typeof(Vector3), typeof(Color)
        };
        public static readonly string[] Categories = { "Game", "Companies", "Contracts", "Networks", "Node Types", "Actions", "Technologies", "Hardware", "Statuses", "Career", "Rewards", "Text Palette", "Graph Visuals", "Network Visuals", "Graph Labels", "Chassis", "Modules", "Rig Rules" };
        public static Type CategoryType(string category)
        {
            int index=Array.IndexOf(Categories,category);
            if(index<0)throw new InvalidDataException("Unknown category: "+category);
            return DefinitionTypes[index>13?index-1:index==13?12:index];
        }
        public static FieldInfo[] Fields(Type type) => type.GetFields(BindingFlags.Instance | BindingFlags.Public)
            .Where(f => !f.IsNotSerialized && (f.DeclaringType == typeof(GameplayDefinition) || DefinitionTypes.Contains(f.DeclaringType) || RuleTypes.Contains(f.DeclaringType) || ValueTypes.Contains(f.DeclaringType)))
            .OrderBy(f => f.DeclaringType == typeof(GameplayDefinition) ? 0 : 1).ThenBy(f => f.MetadataToken).ToArray();
        public static bool IsRule(Type t) => t == typeof(Number) || t == typeof(Condition) || t == typeof(Effect);
        public static string Encode(object value) { using (var stream = new MemoryStream()) { using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true })) Write(writer, value, true, 0); return Encoding.UTF8.GetString(stream.ToArray()) + "\n"; } }
        private static void Write(Utf8JsonWriter w, object value, bool root, int depth)
        {
            if (depth > 64) throw new InvalidDataException("Content graph exceeds 64 levels.");
            if (value == null) { w.WriteNullValue(); return; }
            var type = value.GetType();
            if (!root && value is GameplayDefinition reference) { w.WriteStringValue(reference.id); return; }
            if (value is string str) { w.WriteStringValue(str); return; }
            if (value is bool flag) { w.WriteBooleanValue(flag); return; }
            if (type.IsEnum) { w.WriteStringValue(value.ToString()); return; }
            if (value is int i) { w.WriteNumberValue(i); return; }
            if (value is float f) { w.WriteNumberValue(f); return; }
            if (value is IEnumerable items) { w.WriteStartArray(); foreach (var item in items) Write(w, item, false, depth + 1); w.WriteEndArray(); return; }
            EnsureAllowed(type);
            w.WriteStartObject();
            if (RuleTypes.Contains(type)) w.WriteString("type", type.Name);
            foreach (var field in Fields(type)) { w.WritePropertyName(field.Name); Write(w, field.GetValue(value), false, depth + 1); }
            w.WriteEndObject();
        }
        private static void EnsureAllowed(Type type)
        {
            if (!DefinitionTypes.Contains(type) && !RuleTypes.Contains(type) && !ValueTypes.Contains(type)) throw new InvalidDataException("Unsupported content type: " + type.Name);
        }
        public static T Decode<T>(string json) where T : new()
        {
            using (var doc = JsonDocument.Parse(json)) return (T)Read(doc.RootElement, typeof(T), null, "$", null, 0);
        }
        public static void Populate(string json, object target, ContentBundle bundle)
        {
            using (var doc = JsonDocument.Parse(json)) Read(doc.RootElement, target.GetType(), target, "$", bundle, 0);
        }
        private static object Read(JsonElement e, Type type, object target, string path, ContentBundle bundle, int depth)
        {
            if (depth > 64) throw new InvalidDataException(path + ": exceeds 64 nesting levels");
            if (e.ValueKind == JsonValueKind.Null) { if (type.IsValueType || type.IsArray || IsRule(type) || type != typeof(string) && !typeof(GameplayDefinition).IsAssignableFrom(type)) throw new InvalidDataException(path + ": null value is not permitted; use an empty array or a valid rule/object"); return null; }
            if (target == null && typeof(GameplayDefinition).IsAssignableFrom(type))
            {
                string id = e.GetString(); return bundle?.Resolve(id, type) ?? throw new InvalidDataException(path + ": unknown " + type.Name + " reference '" + id + "'");
            }
            if (type == typeof(string)) return e.GetString();
            if (type == typeof(bool)) return e.GetBoolean();
            if (type == typeof(int)) return e.GetInt32();
            if (type == typeof(float)) { float f = e.GetSingle(); if (float.IsNaN(f) || float.IsInfinity(f)) throw new InvalidDataException(path + ": non-finite number"); return f; }
            if (type.IsEnum) { string name = e.GetString(); if (!Enum.IsDefined(type, name)) throw new InvalidDataException(path + ": unknown " + type.Name + " '" + name + "'"); return Enum.Parse(type, name); }
            if (type.IsArray)
            {
                var array = Array.CreateInstance(type.GetElementType(), e.GetArrayLength()); int n = 0;
                foreach (var item in e.EnumerateArray()) { array.SetValue(Read(item, type.GetElementType(), null, path + "[" + n + "]", bundle, depth + 1), n); n++; } return array;
            }
            if (IsRule(type))
            {
                string name = e.GetProperty("type").GetString();
                type = RuleTypes.SingleOrDefault(t => t.Name == name && type.IsAssignableFrom(t)) ?? throw new InvalidDataException(path + ".type: unsupported rule '" + name + "'");
            }
            EnsureAllowed(type); target ??= Activator.CreateInstance(type);
            var fields = Fields(type).ToDictionary(f => f.Name); var seen = new HashSet<string>();
            foreach (var property in e.EnumerateObject())
            {
                if (!seen.Add(property.Name)) throw new InvalidDataException(path + "." + property.Name + ": duplicate property");
                if (property.Name == "type" && RuleTypes.Contains(type)) continue;
                if (!fields.TryGetValue(property.Name, out var field)) throw new InvalidDataException(path + "." + property.Name + ": unknown property");
                try { field.SetValue(target, Read(property.Value, field.FieldType, null, path + "." + field.Name, bundle, depth + 1)); }
                catch (Exception ex) { throw new InvalidDataException(path + "." + field.Name + ": " + ex.Message, ex); }
            }
            foreach (var field in fields.Values) if (!seen.Contains(field.Name)) throw new InvalidDataException(path + "." + field.Name + ": required property missing");
            return target;
        }
        public static string SafePath(string directory, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains('\\')) throw new InvalidDataException("Invalid data path: " + relative);
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(root, relative));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Data path escapes directory: " + relative);
            // Do not follow symlinks/junctions outside the content tree.
            for (string p = full; p != null && p.Length >= root.Length; p = Path.GetDirectoryName(p))
                if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked data paths are unsupported: " + relative);
            return full;
        }
        public static ContentBundle Load(string directory, IDictionary<string, string> overrides = null, bool validate = true)
        {
            var bundle = new ContentBundle();
            string ReadFile(string file) => overrides != null && overrides.TryGetValue(file, out var text) ? text : File.ReadAllText(SafePath(directory, file));
            try
            {
                bundle.Files["manifest.json"] = ReadFile("manifest.json");
                bundle.Manifest = Decode<ContentManifest>(bundle.Files["manifest.json"]);
                if (bundle.Manifest.schemaVersion != 1 || bundle.Manifest.entries == null) throw new InvalidDataException("manifest.json: unsupported schemaVersion or missing entries");
                var keys = new HashSet<string>(); var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in bundle.Manifest.entries)
                {
                    if (entry == null || !Guid.TryParseExact(entry.key, "N", out _) || !keys.Add(entry.key) || !paths.Add(entry.file)) throw new InvalidDataException("manifest.json: duplicate/invalid key or file");
                    SafePath(directory, entry.file);
                    if (entry.file == "manifest.json" || !entry.file.EndsWith(".json", StringComparison.Ordinal) || !Categories.Contains(entry.category) || CategoryType(entry.category).Name != entry.type) throw new InvalidDataException(entry.file + ": invalid category/type/path");
                    var obj = ScriptableObject.CreateInstance(CategoryType(entry.category)); obj.hideFlags = HideFlags.HideAndDontSave; obj.name = entry.file;
                    bundle.Objects.Add(entry.key, obj);
                    try
                    {
                        string json = ReadFile(entry.file); bundle.Files[entry.file] = json;
                        if (obj is GameplayDefinition definition) using (var document = JsonDocument.Parse(json)) definition.id = document.RootElement.GetProperty("id").GetString();
                    }
                    catch (Exception ex) { bundle.InvalidEntries.Add(entry.key); bundle.Errors.Add(entry.file + ": " + ex.Message); }
                }
                foreach (var entry in bundle.Manifest.entries)
                {
                    if (!bundle.Files.TryGetValue(entry.file, out var json)) continue;
                    try { Populate(json, bundle.Objects[entry.key], bundle); }
                    catch (Exception ex) { bundle.InvalidEntries.Add(entry.key); bundle.Errors.Add(entry.file + ": " + ex.Message); }
                }
                if (bundle.Errors.Count == 0) bundle.Errors.AddRange(Validate(bundle));
                if (validate && bundle.Errors.Count > 0) throw new InvalidDataException(string.Join("\n", bundle.Errors));
                return bundle;
            }
            catch (Exception ex) { bundle.Dispose(); throw new InvalidDataException("Content load failed in " + directory + "\n" + ex.Message, ex); }
        }
        public static IEnumerable<string> Validate(ContentBundle bundle)
        {
            var errors = new List<string>();
            foreach (var category in new[] { "Game", "Career", "Rewards", "Text Palette", "Graph Visuals", "Network Visuals", "Graph Labels", "Rig Rules" })
                if (bundle.Manifest.entries.Count(e => e.category == category) != 1) errors.Add("manifest.json: expected one " + category + " entry");
            if (errors.Count > 0) return errors;
            try { errors.AddRange(GameDefinitionValidator.Validate(bundle.Game)); }
            catch (Exception ex) { errors.Add("Game: " + ex.Message); }
            var registered = new HashSet<GameplayDefinition>(GameDefinitionValidator.Definitions(bundle.Game));
            foreach (var definition in bundle.Objects.Values.OfType<GameplayDefinition>()) if (!registered.Contains(definition)) errors.Add(definition.name + ": not registered in Game root");
            foreach (var obj in bundle.Objects.Values)
                foreach (var field in Fields(obj.GetType()))
                {
                    var range = field.GetCustomAttribute<RangeAttribute>();
                    if (range != null) { float value = Convert.ToSingle(field.GetValue(obj)); if (value < range.min || value > range.max) errors.Add(obj.name + "." + field.Name + ": outside " + range.min + ".." + range.max); }
                    var minimum = field.GetCustomAttribute<MinAttribute>();
                    if (minimum != null && Convert.ToSingle(field.GetValue(obj)) < minimum.min) errors.Add(obj.name + "." + field.Name + ": below minimum " + minimum.min);
                }
            foreach (var term in bundle.Get<GameplayTextPalette>("Text Palette").terms ?? Array.Empty<GameplayTextPalette.TermStyle>())
                if (term == null || string.IsNullOrWhiteSpace(term.phrase)) errors.Add("Text Palette.terms: nonempty phrase required");
                else if (term.regex) try { _ = new Regex(term.phrase, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)); } catch (Exception ex) { errors.Add("Text Palette.terms: " + ex.Message); }
            var labels = bundle.Get<GraphNodeLabelSettings>("Graph Labels");
            if (labels.MinWidth > labels.MaxWidth || labels.RevealInnerRadius < 0 || labels.RevealOuterRadius < labels.RevealInnerRadius || labels.RevealFadeSeconds < 0) errors.Add("Graph Labels: invalid width, fade or proximity ranges");
            return errors;
        }
    }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class GameData
    {
        private static ContentBundle current;
        public static string DirectoryPath => Path.Combine(Path.GetDirectoryName(Application.dataPath), "data");
        public static ContentBundle Current => current ??= ContentJson.Load(DirectoryPath);
        public static bool ValidationLaunch => Environment.GetCommandLineArgs().Contains("--validate-content");
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ValidateLaunch()
        {
            if (!ValidationLaunch) return;
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--validate-content");
            string report = index + 1 < args.Length ? args[index + 1] : null;
            try
            {
                using (var content = ContentJson.Load(DirectoryPath))
                using (var catalog = new GameCatalog(content.Game))
                {
                    var lines = new List<string> { "PASS / " + DirectoryPath, "Definitions: " + content.Manifest.entries.Length };
                    // A read-only diagnostic mode for verifying installed/modded data without opening or writing a career.
                    lines.AddRange(content.Manifest.entries.Select(e => e.file + " " + ContentFingerprint(ContentJson.Encode(content.Objects[e.key]))));
                    if (!string.IsNullOrWhiteSpace(report)) File.WriteAllLines(report, lines); Debug.Log(string.Join("\n", lines));
                }
                Application.Quit(0);
            }
            catch (Exception ex) { if (!string.IsNullOrWhiteSpace(report)) File.WriteAllText(report, "FAIL / " + ex.Message); Debug.LogException(ex); Application.Quit(1); }
        }
        private static string ContentFingerprint(string json)
        { using (var sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-", ""); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset() { current?.Dispose(); current = null; GameplayRichText.ReloadPalette(); }
        public static GraphResourceConfiguration Presentation(bool network)
        {
            var resources = new GraphResourceConfiguration();
            resources.Set("GraphVisualSettings", Current.Get<GraphVisualSettings>(network ? "Network Visuals" : "Graph Visuals"));
            resources.Set("GraphNodeLabelSettings", Current.Get<GraphNodeLabelSettings>("Graph Labels"));
            return resources;
        }
    }
}
