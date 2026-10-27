using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace HackThePlanet.Editor
{
    [Serializable] public sealed class ContentDraftFile { public string file, text; }
    public sealed class ContentDraft : ScriptableObject
    {
        public ContentDraftFile[] files = Array.Empty<ContentDraftFile>();
        public ContentDraftFile[] originals = Array.Empty<ContentDraftFile>();
        public string operation = "Save";
        public Dictionary<string, string> Current => files.ToDictionary(f => f.file, f => f.text);
        public Dictionary<string, string> Original => originals.ToDictionary(f => f.file, f => f.text);
        public bool Dirty => files.Length != originals.Length || files.Any(f => !Original.TryGetValue(f.file, out var old) || old != f.text);
        public void Set(IDictionary<string, string> values) => files = values.Select(p => new ContentDraftFile { file = p.Key, text = p.Value }).ToArray();
        public void Accept() => originals = files.Select(f => new ContentDraftFile { file = f.file, text = f.text }).ToArray();
    }

    public sealed class ContentWorkspace : IDisposable
    {
        public readonly ContentDraft Draft;
        public readonly string DirectoryPath;
        public ContentBundle Bundle { get; private set; }
        private Dictionary<string, string> encodings;
        public ContentWorkspace(ContentDraft draft, string directory)
        {
            Draft = draft; DirectoryPath = directory;
            if (draft.files.Length == 0)
            {
                using (var bundle = ContentJson.Load(directory, validate: false)) draft.Set(bundle.Files);
                draft.Accept();
            }
            Reload();
        }
        public void Reload()
        {
            Bundle?.Dispose(); Bundle = ContentJson.Load(DirectoryPath, Draft.Current, false);
            encodings = Bundle.Objects.ToDictionary(p => p.Key, p => ContentJson.Encode(p.Value));
        }
        public void Capture()
        {
            var files = Draft.Current;
            var active = new HashSet<string>(Bundle.Manifest.entries.Select(e => e.file)) { "manifest.json" };
            foreach (string file in files.Keys.Where(f => !active.Contains(f)).ToArray()) files.Remove(file);
            foreach (var entry in Bundle.Manifest.entries)
            {
                if (Bundle.InvalidEntries.Contains(entry.key)) continue;
                string next = ContentJson.Encode(Bundle.Objects[entry.key]);
                if (!encodings.TryGetValue(entry.key, out var old) || old != next || !files.ContainsKey(entry.file)) files[entry.file] = next;
                encodings[entry.key] = next;
            }
            string manifestJson = ContentJson.Encode(Bundle.Manifest);
            if (!files.TryGetValue("manifest.json", out var oldManifest) || ContentJson.Encode(ContentJson.Decode<ContentManifest>(oldManifest)) != manifestJson) files["manifest.json"] = manifestJson;
            Draft.Set(files);
        }
        public ContentEntry Add(string category, string copyKey = null)
        {
            if (Singleton(category)) throw new InvalidOperationException("This category has exactly one required definition.");
            var type = ContentJson.CategoryType(category);
            var obj = copyKey == null ? ScriptableObject.CreateInstance(type) : UnityEngine.Object.Instantiate(Bundle.Objects[copyKey]);
            obj.hideFlags = HideFlags.HideAndDontSave;
            var definition = (GameplayDefinition)obj; definition.id = UniqueId(copyKey == null ? "new-" + type.Name.Replace("Definition", "").ToLowerInvariant() : definition.id + "-copy");
            if (copyKey == null) definition.title = "New " + category;
            string folder = category=="Chassis"?"rig/chassis":category=="Modules"?"rig/modules":category.ToLowerInvariant().Replace(' ', '-');
            string stem = System.Text.RegularExpressions.Regex.Replace(definition.id, "[^a-zA-Z0-9_-]", "-");
            string file = folder + "/" + stem + ".json"; int suffix = 2;
            while (Bundle.Manifest.entries.Any(e => string.Equals(e.file, file, StringComparison.OrdinalIgnoreCase)) || File.Exists(ContentJson.SafePath(DirectoryPath, file))) file = folder + "/" + stem + "-" + suffix++ + ".json";
            var entry = new ContentEntry { key = Guid.NewGuid().ToString("N"), category = category, type = type.Name, file = file };
            obj.name = entry.file;
            Bundle.Manifest.entries = Bundle.Manifest.entries.Concat(new[] { entry }).ToArray(); Bundle.Objects.Add(entry.key, obj);
            var rootField = ContentJson.Fields(typeof(GameDefinition)).Single(f => f.FieldType.IsArray && f.FieldType.GetElementType() == type);
            var current = (Array)rootField.GetValue(Bundle.Game); var expanded = Array.CreateInstance(type, current.Length + 1); Array.Copy(current, expanded, current.Length); expanded.SetValue(obj, current.Length); rootField.SetValue(Bundle.Game, expanded);
            Capture(); return entry;
        }
        public static bool Singleton(string category) => new[] { "Game", "Career", "Rewards", "Text Palette", "Graph Visuals", "Network Visuals", "Graph Labels", "Rig Rules" }.Contains(category);
        public string UniqueId(string seed)
        {
            string id = seed; int n = 2; while (Bundle.Objects.Values.OfType<GameplayDefinition>().Any(d => d.id == id)) id = seed + "-" + n++; return id;
        }
        public void RenameId(string key, string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id != id.Trim() || Bundle.Objects.Values.OfType<GameplayDefinition>().Any(d => d != Bundle.Objects[key] && d.id == id)) throw new InvalidOperationException("ID must be nonempty and unique.");
            ((GameplayDefinition)Bundle.Objects[key]).id = id;
            // References are object links in the working graph; serialization writes their new ID in every referencing file.
            Capture(); Draft.operation = "Rename ID";
        }
        public void RenameFile(string key, string file)
        {
            ContentJson.SafePath(DirectoryPath, file);
            if (!file.EndsWith(".json", StringComparison.Ordinal) || file == "manifest.json" || Bundle.Manifest.entries.Any(e => e.key != key && string.Equals(e.file, file, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Choose a unique relative .json filename.");
            var entry = Bundle.Manifest.entries.Single(e => e.key == key);
            if (File.Exists(ContentJson.SafePath(DirectoryPath, file)) && !string.Equals(entry.file, file, StringComparison.OrdinalIgnoreCase)) throw new IOException("Destination already exists on disk.");
            entry.file = file; Capture(); Draft.operation = "Rename file";
        }
        public List<string> Incoming(string key)
        {
            var target = Bundle.Objects[key]; var hits = new List<string>();
            foreach (var entry in Bundle.Manifest.entries.Where(e => e.key != key && e.category != "Game")) Visit(Bundle.Objects[entry.key], entry.file, true, new HashSet<object>());
            return hits;
            void Visit(object obj, string path, bool root, HashSet<object> seen)
            {
                if (ReferenceEquals(obj, target)) { hits.Add(path); return; }
                if (obj == null || obj is string || obj.GetType().IsValueType || (!root && obj is ScriptableObject) || !seen.Add(obj)) return;
                if (obj is System.Collections.IEnumerable items) { int i = 0; foreach (var item in items) Visit(item, path + "[" + i++ + "]", false, seen); }
                else foreach (var field in ContentJson.Fields(obj.GetType())) Visit(field.GetValue(obj), path + "." + field.Name, false, seen);
            }
        }
        public void Delete(string key)
        {
            var entry = Bundle.Manifest.entries.Single(e => e.key == key);
            if (Singleton(entry.category)) throw new InvalidOperationException("Cannot delete a required singleton.");
            var references = Incoming(key); if (references.Count > 0) throw new InvalidOperationException("Reassign/remove incoming references first:\n" + string.Join("\n", references));
            var type = Bundle.Objects[key].GetType();
            var field = ContentJson.Fields(typeof(GameDefinition)).Single(f => f.FieldType.IsArray && f.FieldType.GetElementType() == type);
            var array = ((Array)field.GetValue(Bundle.Game)).Cast<object>().Where(o => !ReferenceEquals(o, Bundle.Objects[key])).ToArray();
            var next = Array.CreateInstance(type, array.Length); for (int i = 0; i < array.Length; i++) next.SetValue(array[i], i); field.SetValue(Bundle.Game, next);
            UnityEngine.Object.DestroyImmediate(Bundle.Objects[key]); Bundle.Objects.Remove(key);
            Bundle.Manifest.entries = Bundle.Manifest.entries.Where(e => e.key != key).ToArray(); Capture(); Draft.operation = "Delete";
        }
        public void Move(string key, int delta)
        {
            var entry = Bundle.Manifest.entries.Single(e => e.key == key); var entries = Bundle.Manifest.entries;
            int index = Array.IndexOf(entries, entry), other = index + delta;
            while (other >= 0 && other < entries.Length && entries[other].category != entry.category) other += delta;
            if (other < 0 || other >= entries.Length) return;
            (entries[index], entries[other]) = (entries[other], entries[index]);
            if (!Singleton(entry.category))
            {
                var type = Bundle.Objects[key].GetType(); var field = ContentJson.Fields(typeof(GameDefinition)).Single(f => f.FieldType.IsArray && f.FieldType.GetElementType() == type);
                var ordered = entries.Where(e => e.category == entry.category).Select(e => Bundle.Objects[e.key]).ToArray();
                var array = Array.CreateInstance(type, ordered.Length); for (int i = 0; i < ordered.Length; i++) array.SetValue(ordered[i], i); field.SetValue(Bundle.Game, array);
            }
            Capture();
        }
        public void Restore(IEnumerable<ContentRevision> revisions)
        {
            var files = Draft.Current; var manifest = ContentJson.Decode<ContentManifest>(files["manifest.json"]); var entries = manifest.entries.ToList();
            foreach (var revision in revisions)
            {
                var existing = entries.SingleOrDefault(e => e.key == revision.entry.key);
                int oldIndex = existing == null ? entries.Count : entries.IndexOf(existing);
                if (existing != null) { files.Remove(existing.file); entries.Remove(existing); }
                if (revision.json == null) continue;
                if (entries.Any(e => string.Equals(e.file, revision.entry.file, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Restore path collision: " + revision.entry.file + ". Rename the conflicting file first.");
                var originalEntries = ContentJson.Decode<ContentManifest>(Draft.Original["manifest.json"]).entries;
                if (File.Exists(ContentJson.SafePath(DirectoryPath, revision.entry.file)) && !originalEntries.Any(e => e.key == revision.entry.key && e.file == revision.entry.file)) throw new InvalidOperationException("An unrelated file occupies " + revision.entry.file + ". Move it outside data or rename it before restoring.");
                entries.Insert(Math.Min(oldIndex, entries.Count), ContentJson.Decode<ContentEntry>(ContentJson.Encode(revision.entry))); files[revision.entry.file] = revision.json;
            }
            var identities = new HashSet<string>();
            foreach (var entry in entries.Where(e => typeof(GameplayDefinition).IsAssignableFrom(ContentJson.CategoryType(e.category))))
                using (var json = System.Text.Json.JsonDocument.Parse(files[entry.file]))
                {
                    string identity = entry.type + ":" + json.RootElement.GetProperty("id").GetString();
                    if (!identities.Add(identity)) throw new InvalidOperationException("Restore ID collision: " + identity + ". Rename the conflicting entry first.");
                }
            manifest.entries = entries.ToArray(); files["manifest.json"] = ContentJson.Encode(manifest);
            // Rebuild only root registration fields in JSON before resolving references. This avoids losing
            // unrelated root fields when a restored definition changes the set of available IDs.
            var rootRegistration = entries.Single(e => e.category == "Game");
            using (var rootJson = System.Text.Json.JsonDocument.Parse(files[rootRegistration.file]))
            using (var stream = new MemoryStream())
            {
                using (var writer = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    foreach (var property in rootJson.RootElement.EnumerateObject())
                    {
                        writer.WritePropertyName(property.Name);
                        var field = ContentJson.Fields(typeof(GameDefinition)).Single(f => f.Name == property.Name);
                        if (field.FieldType.IsArray && typeof(GameplayDefinition).IsAssignableFrom(field.FieldType.GetElementType()))
                        {
                            writer.WriteStartArray();
                            foreach (var e in entries.Where(e => ContentJson.CategoryType(e.category) == field.FieldType.GetElementType()))
                                using (var json = System.Text.Json.JsonDocument.Parse(files[e.file])) writer.WriteStringValue(json.RootElement.GetProperty("id").GetString());
                            writer.WriteEndArray();
                        }
                        else property.Value.WriteTo(writer);
                    }
                    writer.WriteEndObject();
                }
                files[rootRegistration.file] = System.Text.Encoding.UTF8.GetString(stream.ToArray()) + "\n";
            }
            Draft.Set(files); Draft.operation = "Restore"; Reload();
        }
        public void Dispose() => Bundle?.Dispose();
    }
}
