using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cashg.GraphExplorer;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HackThePlanet.Editor
{
    public static class ContentMigration
    {
        [MenuItem("Hack The Planet/JSON/Export Original Assets (once)")]
        public static void Export()
        {
            string directory = GameData.DirectoryPath;
            if (Directory.Exists(directory)) throw new InvalidOperationException("data already exists; export refuses to overwrite authored JSON.");
            var root = Resources.Load<GameDefinition>("HackThePlanet/Gameplay/Game");
            GameDefinitionValidator.ThrowIfInvalid(root);
            var entries = new List<ContentEntry>(); var expected = new Dictionary<string, string>();
            void Add(ScriptableObject obj, string category, string id)
            {
                string folder = category.ToLowerInvariant().Replace(' ', '-'); string file = folder + "/" + id + ".json";
                var entry = new ContentEntry { key = Guid.NewGuid().ToString("N"), category = category, type = obj.GetType().Name, file = file };
                entries.Add(entry); expected[file] = ContentJson.Encode(obj);
            }
            foreach (var definition in GameDefinitionValidator.Definitions(root))
                Add(definition, ContentJson.Categories.First(c => ContentJson.CategoryType(c) == definition.GetType()), definition.id);
            Add(Resources.Load<GameplayTextPalette>("HackThePlanet/GameplayTextPalette"), "Text Palette", "text-palette");
            Add(GraphResources.Load<GraphVisualSettings>("GraphVisualSettings"), "Graph Visuals", "research");
            var network = UnityEngine.Object.Instantiate(GraphResources.Load<GraphVisualSettings>("GraphVisualSettings"));
            network.ShadowBrightness = .7f; network.SurfaceEmission = .24f; network.BloomIntensity = .04f; network.OutlinePixels = 1;
            network.OutlineOpacity = .8f; network.SelectionGlowIntensity = .6f; network.SelectionRimIntensity = .3f;
            Add(network, "Network Visuals", "network"); UnityEngine.Object.DestroyImmediate(network);
            Add(GraphResources.Load<GraphNodeLabelSettings>("GraphNodeLabelSettings"), "Graph Labels", "labels");
            expected["manifest.json"] = ContentJson.Encode(new ContentManifest { entries = entries.ToArray() });
            // Validate before creating the authoritative directory.
            using (var loaded = ContentJson.Load(directory, expected))
                foreach (var entry in entries)
                    if (ContentJson.Encode(loaded.Objects[entry.key]) != expected[entry.file]) throw new InvalidDataException("Migration round-trip mismatch: " + entry.file);
            foreach (var file in expected) { string path = ContentJson.SafePath(directory, file.Key); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, file.Value); }
            Directory.CreateDirectory("Artifacts");
            File.WriteAllText("Artifacts/json-migration.txt", "PASS / " + entries.Count + " definitions exported and compared field-for-field after reload.\n");
            Debug.Log("JSON migration passed: " + entries.Count + " definitions.");
        }
        [MenuItem("Hack The Planet/JSON/Validate Data")]
        public static void Validate() { using (var bundle = ContentJson.Load(GameData.DirectoryPath)) { } Debug.Log("JSON content validation passed."); }
    }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class ContentBuildPipeline : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 10;
        private static Dictionary<string, string> validated;
        public void OnPreprocessBuild(BuildReport report)
        {
            try { using (var bundle = ContentJson.Load(GameData.DirectoryPath)) validated = new Dictionary<string, string>(bundle.Files); }
            catch (Exception ex) { throw new BuildFailedException(ex.Message); }
        }
        public void OnPostprocessBuild(BuildReport report)
        {
            if (validated == null) throw new BuildFailedException("Missing validated JSON build snapshot.");
            string output = Path.GetFullPath(Path.GetDirectoryName(report.summary.outputPath));
            string destination = Path.Combine(output, "data"), staged = Path.Combine(output, "data-staging-" + Guid.NewGuid().ToString("N"));
            try
            {
                // Only our generated sibling data directory may be replaced; never target source data.
                if (string.Equals(destination, Path.GetFullPath(GameData.DirectoryPath), StringComparison.OrdinalIgnoreCase)) throw new IOException("Build output cannot replace project data.");
                foreach (var file in validated) { string path = ContentJson.SafePath(staged, file.Key); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, file.Value); }
                using (var check = ContentJson.Load(staged)) { }
                if (Directory.Exists(destination)) Directory.Delete(destination, true);
                Directory.Move(staged, destination);
            }
            catch (Exception ex) { throw new BuildFailedException("Could not package JSON data: " + ex.Message); }
            finally { validated = null; }
        }
    }
}
