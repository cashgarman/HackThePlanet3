using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cashg.GraphExplorer;
using UnityEditor;
using UnityEngine;

namespace HackThePlanet.Editor
{
    public static class ContentValidation
    {
        [MenuItem("Hack The Planet/JSON/Run Content and History Validation")]
        public static void Run()
        {
            var results = new List<string>();
            void Check(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); results.Add("PASS / " + text); }
            void Reject(Action action, string text) { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, text); }
            string sandbox = Path.GetFullPath("Artifacts/content-validation/" + Guid.NewGuid().ToString("N"));
            string data = Path.Combine(sandbox, "data");
            Directory.CreateDirectory(data);
            using (var source = ContentJson.Load(GameData.DirectoryPath))
            {
                var coverage = ContentFieldHelp.CheckCoverage().ToArray(); Check(coverage.Length == 0, "Descriptions cover every editable field: " + string.Join(", ", coverage));
                foreach (var file in source.Files) { string path = ContentJson.SafePath(data, file.Key); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, file.Value); }
                GameplayAssetGenerator.Generate();
                foreach(var entry in source.Manifest.entries)
                {
                    var generated=AssetDatabase.LoadAssetAtPath<ScriptableObject>(GameplayAssetGenerator.Output+"/"+entry.key+".asset");
                    Check(generated!=null&&ContentJson.Encode(generated)==ContentJson.Encode(source.Objects[entry.key]),"Generated asset parity "+entry.file);
                }
                foreach (var type in ContentJson.RuleTypes)
                {
                    object instance = Activator.CreateInstance(type); string json = ContentJson.Encode(instance); object copy = Activator.CreateInstance(type);
                    ContentJson.Populate(json, copy, source); Check(ContentJson.Encode(copy) == json, "Rule codec " + type.Name);
                }
                var values = source.Files.ToDictionary(p => p.Key, p => p.Value);
                var actionEntry = source.Manifest.entries.First(e => e.category == "Actions"); string actionJson = values[actionEntry.file];
                void Invalid(string json, string text) { var files = new Dictionary<string, string>(values) { [actionEntry.file] = json }; Reject(() => { using (var b = ContentJson.Load(data, files)) { } }, text); }
                Invalid("{", "Malformed JSON rejected");
                Invalid(actionJson.Replace("\"duration\":", "\"unknownField\":"), "Unknown fields rejected");
                Invalid(actionJson.Replace("ConstantNumber", "System.IO.File"), "Unregistered rule type rejected");
                Invalid(actionJson.Replace("\"title\":", "\"id\": \"duplicate-property\", \"title\":"), "Duplicate JSON property rejected");
                var manifest = ContentJson.Decode<ContentManifest>(values["manifest.json"]); manifest.schemaVersion = 100;
                var unsupported = new Dictionary<string, string>(values) { ["manifest.json"] = ContentJson.Encode(manifest) };
                Reject(() => { using (var b = ContentJson.Load(data, unsupported)) { } }, "Unsupported schema rejected");
                Reject(() => ContentJson.SafePath(data, "../outside.json"), "Directory traversal rejected");
                Reject(() => { using (var b = ContentJson.Load(Path.Combine(sandbox, "missing"))) { } }, "Missing data directory rejected without creating files");
                Check(!Directory.Exists(Path.Combine(sandbox, "missing")), "Failed load leaves filesystem untouched");
                values[actionEntry.file] = actionJson;
            }
            var draft = ScriptableObject.CreateInstance<ContentDraft>();
            try
            {
                using (var workspace = new ContentWorkspace(draft, data))
                {
                    var history = new ContentHistory(data); var entry = workspace.Bundle.Manifest.entries.First(e => e.category == "Actions");
                    var action = (ActionDefinition)workspace.Bundle.Objects[entry.key]; string before = draft.Current[entry.file];
                    action.description += " [history validation]"; workspace.Capture();
                    string first = history.Commit(draft.Current, draft.Original, "First checkpoint"); draft.Accept();
                    Check(history.Revisions(entry.key).Count == 2, "First changed save captures original plus new revision");
                    Check(history.Revisions(entry.key).Last().label == "First checkpoint", "Save revision name retained");
                    Check(history.Commit(draft.Current, draft.Original) == null && history.Revisions(entry.key).Count == 2, "Unchanged saves create no revision");
                    var firstRevision = history.Revisions(entry.key).Last(); history.Name(entry.key, firstRevision.id, "Renamed checkpoint");
                    Check(history.Revisions(entry.key).Last().label == "Renamed checkpoint", "Revision labels can be renamed");
                    Check(ContentHistory.Diff(before, ContentJson.Encode(action), false).Contains("$.description"), "Readable property-level history diff");
                    var originalRevision = history.Revisions(entry.key).First(); workspace.Restore(new[] { originalRevision });
                    Check(draft.Current[entry.file] == before, "Restore creates an unsaved original-version draft");
                    history.Commit(draft.Current, draft.Original, "Restored", "Restore"); draft.Accept();
                    Check(history.Revisions(entry.key).Count == 3, "Restore appends history without erasing later revisions");
                    workspace.RenameId(entry.key, "test-renamed-action");
                    using (var renamed = ContentJson.Load(data, draft.Current)) Check(renamed.Game.actions.Any(a => a.id == "test-renamed-action"), "Rename updates root and every object reference");
                    history.Commit(draft.Current, draft.Original, operation: "Rename ID"); draft.Accept();
                    Check(history.Revisions(entry.key).Count == 4, "History follows immutable entry identity through ID rename");
                    var transaction = history.Transactions().Last();
                    var originals = transaction.revisions.GroupBy(r => r.entry.key).Select(g => g.Last()).Where(r => !string.IsNullOrEmpty(r.parent)).Select(r => history.Revisions(r.entry.key).Single(p => p.id == r.parent)).ToArray();
                    workspace.Restore(originals); history.Commit(draft.Current, draft.Original, operation: "Restore transaction"); draft.Accept();
                    Check(((ActionDefinition)workspace.Bundle.Objects[entry.key]).id != "test-renamed-action", "Multi-entry transaction restore preserves consistent references");
                    var duplicate = workspace.Add("Actions", entry.key); string duplicateKey = duplicate.key;
                    history.Commit(draft.Current, draft.Original, "Created copy"); draft.Accept();
                    Check(history.Revisions(duplicateKey).Count == 1, "Duplicate has independent history identity");
                    string oldFile = duplicate.file;
                    workspace.RenameFile(duplicateKey, "actions/renamed-copy.json");
                    history.Commit(draft.Current, draft.Original, operation: "Rename file"); draft.Accept();
                    Check(!File.Exists(ContentJson.SafePath(data, oldFile)) && history.Revisions(duplicateKey).Count == 2, "File rename removes the old path and retains entry history");
                    workspace.Delete(duplicateKey); history.Commit(draft.Current, draft.Original); draft.Accept();
                    Check(history.Revisions(duplicateKey).Last().json == null, "Deletion is retained in entry history");
                    workspace.Restore(new[] { history.Revisions(duplicateKey).First() }); history.Commit(draft.Current, draft.Original); draft.Accept();
                    Check(workspace.Bundle.Objects.ContainsKey(duplicateKey), "Deleted entry restoration restores root registration");
                    var referencedTech = workspace.Bundle.Manifest.entries.First(e => e.category == "Technologies" && workspace.Incoming(e.key).Count > 0);
                    Reject(() => workspace.Delete(referencedTech.key), "Deletion with incoming references is blocked");
                    var localFiles = draft.Current; string externalPath = ContentJson.SafePath(data, entry.file); File.AppendAllText(externalPath, "\n");
                    Reject(() => history.Commit(localFiles, draft.Original), "External edits are detected before overwrite");
                    Check(File.ReadAllText(externalPath).EndsWith("\n\n"), "External conflict leaves disk content intact");
                    history.Commit(localFiles, draft.Original, reviewedOverwrite: true);
                    Check(history.Revisions(entry.key).Any(r => r.operation == "External" && r.json.EndsWith("\n\n")), "Reviewed overwrite preserves the external disk version as a revision");
                    var badCandidate = new Dictionary<string, string>(draft.Current) { [entry.file] = "{" };
                    string validBefore = File.ReadAllText(externalPath);
                    Reject(() => history.Commit(badCandidate, draft.Original), "Invalid draft cannot be committed");
                    Check(File.ReadAllText(externalPath) == validBefore, "Invalid-save rejection preserves authoritative bytes");
                    Undo.RecordObject(draft, "Content draft validation");
                    var undoFiles = draft.Current; undoFiles[entry.file] += " "; draft.Set(undoFiles); Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                    Check(draft.Current[entry.file] == localFiles[entry.file], "Unity Undo restores isolated draft JSON");
                    Undo.PerformRedo(); Check(draft.Current[entry.file].EndsWith(" "), "Unity Redo reapplies draft JSON");
                    draft.Set(localFiles); Undo.ClearUndo(draft);
                    // Simulate an interrupted write after its journal reached disk.
                    string recoveryBefore = File.ReadAllText(externalPath);
                    var pending = new ContentTransaction { id = "999999-recovery-test", files = new[] { new ContentFileChange { file = entry.file, before = recoveryBefore, after = "broken", beforeExists = true, afterExists = true } }, revisions = Array.Empty<ContentRevision>() };
                    ContentHistory.AtomicWrite(Path.Combine(history.HistoryDirectory, "transactions", pending.id + ".json"), JsonUtility.ToJson(pending)); File.WriteAllText(externalPath, "broken");
                    history.Recover(); Check(File.ReadAllText(externalPath) == recoveryBefore, "Interrupted transaction recovery restores previous bytes");
                    Check(history.Transactions().All(t => t.committed), "Aborted transactions are excluded from visible history");
                    string recovery = JsonUtility.ToJson(draft); var recovered = ScriptableObject.CreateInstance<ContentDraft>();
                    JsonUtility.FromJsonOverwrite(recovery, recovered); Check(recovered.Current[entry.file] == draft.Current[entry.file], "Draft recovery round-trip preserves content"); UnityEngine.Object.DestroyImmediate(recovered);
                    string savedDirectory = Environment.CurrentDirectory;
                    try { Environment.CurrentDirectory = sandbox; using (var relocated = ContentJson.Load(data)) Check(relocated.Game != null, "Absolute directory loading is independent of working directory"); }
                    finally { Environment.CurrentDirectory = savedDirectory; }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(draft); }
            Directory.CreateDirectory("Artifacts"); File.WriteAllText("Artifacts/content-validation.txt", string.Join("\n", results) + "\n");
            Debug.Log("Content validation passed: " + results.Count + " checks. Sandbox retained in " + sandbox);
        }
    }
}
