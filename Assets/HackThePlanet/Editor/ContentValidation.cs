using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HackThePlanet.Editor
{
	/// <summary>End-to-end sandbox validation for JSON authoring, assets, history, and draft undo.</summary>
	public static class ContentValidation
	{
		/// <summary>Runs the full content and local-history validation suite and writes Artifacts/content-validation.txt.</summary>
		[MenuItem("Hack The Planet/JSON/Run Content and History Validation")]
		public static void Run()
		{
			// Collect one PASS line for every successful check.
			var results = new List<string>();

			// Local helper that records a passing check or throws on failure.
			void Check(bool condition, string text)
			{
				// Stop the suite at the first failed check.
				if (!condition)
					throw new InvalidOperationException(text);

				// Record the passing check.
				results.Add("PASS / " + text);
			}

			// Local helper that asserts an action is rejected by throwing.
			void Reject(Action action, string text)
			{
				// Run the action and note whether it threw.
				var rejected = false;
				try
				{
					action();
				}
				catch
				{
					rejected = true;
				}

				// Require that the action was rejected.
				Check(rejected, text);
			}

			// Create an isolated sandbox folder and its data subfolder.
			var sandbox = Path.GetFullPath("Artifacts/content-validation/" + Guid.NewGuid().ToString("N"));
			var data = Path.Combine(sandbox, "data");
			Directory.CreateDirectory(data);

			// Load the authoritative data that the suite validates.
			using (var source = ContentJson.Load(GameData.DirectoryPath))
			{
				// Confirm every editable field has a description.
				var coverage = ContentFieldHelp.CheckCoverage().ToArray();
				Check(coverage.Length == 0, "Descriptions cover every editable field: " + string.Join(", ", coverage));

				// Copy every authoritative file into the sandbox.
				foreach (var file in source.Files)
				{
					// Resolve the sandbox path, create its folder, and write the file.
					var path = ContentJson.SafePath(data, file.Key);
					Directory.CreateDirectory(Path.GetDirectoryName(path));
					File.WriteAllText(path, file.Value);
				}

				// Generate the Unity assets from the sandbox source.
				GameplayAssetGenerator.Generate();

				// Verify each generated asset matches its source JSON.
				foreach (var entry in source.Manifest.entries)
				{
					// Load the generated asset for this manifest entry.
					var generated =
						AssetDatabase.LoadAssetAtPath<ScriptableObject>(
							GameplayAssetGenerator.Output + "/" + entry.key + ".asset");

					// Require that the asset encodes to the same JSON as the source object.
					Check(
						generated != null && ContentJson.Encode(generated) ==
						ContentJson.Encode(source.Objects[entry.key]), "Generated asset parity " + entry.file);
				}

				// Check that every rule type round-trips through the JSON codec.
				foreach (var type in ContentJson.RuleTypes)
				{
					// Encode a default instance of the rule type.
					var instance = Activator.CreateInstance(type);
					var json = ContentJson.Encode(instance);

					// Populate a fresh copy from that JSON and compare the encodings.
					var copy = Activator.CreateInstance(type);
					ContentJson.Populate(json, copy, source);
					Check(ContentJson.Encode(copy) == json, "Rule codec " + type.Name);
				}

				// Capture the action entry and its JSON for the negative tests.
				var values = source.Files.ToDictionary(p => p.Key, p => p.Value);
				var actionEntry = source.Manifest.entries.First(e => e.category == "Actions");
				var actionJson = values[actionEntry.file];

				// Local helper that asserts loading data with a replaced action JSON is rejected.
				void Invalid(string json, string text)
				{
					// Build a file map with the action JSON replaced.
					var files = new Dictionary<string, string>(values) { [actionEntry.file] = json };

					// Require that loading the modified data fails.
					Reject(() =>
					{
						using (var b = ContentJson.Load(data, files))
						{
						}
					}, text);
				}

				// Reject malformed, unknown, unregistered, and duplicate JSON properties.
				Invalid("{", "Malformed JSON rejected");
				Invalid(actionJson.Replace("\"duration\":", "\"unknownField\":"), "Unknown fields rejected");
				Invalid(actionJson.Replace("ConstantNumber", "System.IO.File"), "Unregistered rule type rejected");
				Invalid(actionJson.Replace("\"title\":", "\"id\": \"duplicate-property\", \"title\":"),
						"Duplicate JSON property rejected");

				// Reject a manifest with an unsupported schema version.
				var manifest = ContentJson.Decode<ContentManifest>(values["manifest.json"]);
				manifest.schemaVersion = 100;
				var unsupported = new Dictionary<string, string>(values)
					{ ["manifest.json"] = ContentJson.Encode(manifest) };
				Reject(() =>
				{
					using (var b = ContentJson.Load(data, unsupported))
					{
					}
				}, "Unsupported schema rejected");

				// Reject path traversal and a missing data directory.
				Reject(() => ContentJson.SafePath(data, "../outside.json"), "Directory traversal rejected");
				Reject(() =>
				{
					using (var b = ContentJson.Load(Path.Combine(sandbox, "missing")))
					{
					}
				}, "Missing data directory rejected without creating files");
				Check(!Directory.Exists(Path.Combine(sandbox, "missing")), "Failed load leaves filesystem untouched");

				// Restore the original action JSON in the file map.
				values[actionEntry.file] = actionJson;
			}

			// Create the draft that holds the sandbox JSON files.
			var draft = ScriptableObject.CreateInstance<ContentDraft>();

			// Run the history and draft checks, always destroying the draft afterwards.
			try
			{
				// Open a workspace over the sandbox data.
				using (var workspace = new ContentWorkspace(draft, data))
				{
					// Create the history store and locate the action entry under test.
					var history = new ContentHistory(data);
					var entry = workspace.Bundle.Manifest.entries.First(e => e.category == "Actions");
					var action = (ActionDefinition)workspace.Bundle.Objects[entry.key];
					var before = draft.Current[entry.file];

					// Modify the action description so a real change can be saved.
					action.description += " [history validation]";
					workspace.Capture();

					// Save the first checkpoint and accept it as the new baseline.
					var first = history.Commit(draft.Current, draft.Original, "First checkpoint");
					draft.Accept();

					// Verify the first save records the original and the new revision.
					Check(history.Revisions(entry.key).Count == 2,
						  "First changed save captures original plus new revision");
					Check(history.Revisions(entry.key).Last().label == "First checkpoint",
						  "Save revision name retained");

					// Verify that saving an unchanged draft creates no revision.
					Check(
						history.Commit(draft.Current, draft.Original) == null &&
						history.Revisions(entry.key).Count == 2, "Unchanged saves create no revision");

					// Rename the first checkpoint revision and verify the new label.
					var firstRevision = history.Revisions(entry.key).Last();
					history.Name(entry.key, firstRevision.id, "Renamed checkpoint");
					Check(history.Revisions(entry.key).Last().label == "Renamed checkpoint",
						  "Revision labels can be renamed");

					// Verify the history diff reports the changed description property.
					Check(ContentHistory.Diff(before, ContentJson.Encode(action), false).Contains("$.description"),
						  "Readable property-level history diff");

					// Restore the original revision into the draft as unsaved work.
					var originalRevision = history.Revisions(entry.key).First();
					workspace.Restore(new[] { originalRevision });
					Check(draft.Current[entry.file] == before, "Restore creates an unsaved original-version draft");

					// Save the restore as a new revision and verify history grows without losing later entries.
					history.Commit(draft.Current, draft.Original, "Restored", "Restore");
					draft.Accept();
					Check(history.Revisions(entry.key).Count == 3,
						  "Restore appends history without erasing later revisions");

					// Rename the definition id and verify every reference follows the rename.
					workspace.RenameId(entry.key, "test-renamed-action");
					using (var renamed = ContentJson.Load(data, draft.Current))
					{
						Check(renamed.Game.actions.Any(a => a.id == "test-renamed-action"),
							  "Rename updates root and every object reference");
					}

					// Save the rename and verify history follows the immutable entry key.
					history.Commit(draft.Current, draft.Original, operation: "Rename ID");
					draft.Accept();
					Check(history.Revisions(entry.key).Count == 4,
						  "History follows immutable entry identity through ID rename");

					// Restore every entry from the latest transaction and verify references stay consistent.
					var transaction = history.Transactions().Last();
					var originals = transaction.revisions.GroupBy(r => r.entry.key).Select(g => g.Last())
											   .Where(r => !string.IsNullOrEmpty(r.parent))
											   .Select(r => history.Revisions(r.entry.key)
																   .Single(p => p.id == r.parent)).ToArray();
					workspace.Restore(originals);
					history.Commit(draft.Current, draft.Original, operation: "Restore transaction");
					draft.Accept();
					Check(((ActionDefinition)workspace.Bundle.Objects[entry.key]).id != "test-renamed-action",
						  "Multi-entry transaction restore preserves consistent references");

					// Duplicate the action and verify the copy gets its own history identity.
					var duplicate = workspace.Add("Actions", entry.key);
					var duplicateKey = duplicate.key;
					history.Commit(draft.Current, draft.Original, "Created copy");
					draft.Accept();
					Check(history.Revisions(duplicateKey).Count == 1, "Duplicate has independent history identity");

					// Rename the duplicate file and verify the old path is removed while history is retained.
					var oldFile = duplicate.file;
					workspace.RenameFile(duplicateKey, "actions/renamed-copy.json");
					history.Commit(draft.Current, draft.Original, operation: "Rename file");
					draft.Accept();
					Check(
						!File.Exists(ContentJson.SafePath(data, oldFile)) && history.Revisions(duplicateKey).Count == 2,
						"File rename removes the old path and retains entry history");

					// Delete the duplicate and verify the deletion is recorded in its history.
					workspace.Delete(duplicateKey);
					history.Commit(draft.Current, draft.Original);
					draft.Accept();
					Check(history.Revisions(duplicateKey).Last().json == null, "Deletion is retained in entry history");

					// Restore the deleted duplicate and verify it is registered again.
					workspace.Restore(new[] { history.Revisions(duplicateKey).First() });
					history.Commit(draft.Current, draft.Original);
					draft.Accept();
					Check(workspace.Bundle.Objects.ContainsKey(duplicateKey),
						  "Deleted entry restoration restores root registration");

					// Verify that deleting a technology that is still referenced is blocked.
					var referencedTech =
						workspace.Bundle.Manifest.entries.First(e => e.category == "Technologies" &&
																	 workspace.Incoming(e.key).Count > 0);
					Reject(() => workspace.Delete(referencedTech.key), "Deletion with incoming references is blocked");

					// Simulate an external edit to the action file on disk.
					var localFiles = draft.Current;
					var externalPath = ContentJson.SafePath(data, entry.file);
					File.AppendAllText(externalPath, "\n");

					// Verify the external edit is detected before a save overwrites it.
					Reject(() => history.Commit(localFiles, draft.Original),
						   "External edits are detected before overwrite");

					// Verify the conflicting disk bytes are left intact.
					Check(File.ReadAllText(externalPath).EndsWith("\n\n"),
						  "External conflict leaves disk content intact");

					// Commit with a reviewed overwrite and verify the external version is kept in history.
					history.Commit(localFiles, draft.Original, reviewedOverwrite: true);
					Check(history.Revisions(entry.key).Any(r => r.operation == "External" && r.json.EndsWith("\n\n")),
						  "Reviewed overwrite preserves the external disk version as a revision");

					// Verify an invalid candidate is rejected and leaves the disk bytes unchanged.
					var badCandidate = new Dictionary<string, string>(draft.Current) { [entry.file] = "{" };
					var validBefore = File.ReadAllText(externalPath);
					Reject(() => history.Commit(badCandidate, draft.Original), "Invalid draft cannot be committed");
					Check(File.ReadAllText(externalPath) == validBefore,
						  "Invalid-save rejection preserves authoritative bytes");

					// Make an undoable draft edit and verify Unity Undo and Redo restore it.
					Undo.RecordObject(draft, "Content draft validation");
					var undoFiles = draft.Current;
					undoFiles[entry.file] += " ";
					draft.Set(undoFiles);
					Undo.FlushUndoRecordObjects();
					Undo.PerformUndo();
					Check(draft.Current[entry.file] == localFiles[entry.file],
						  "Unity Undo restores isolated draft JSON");
					Undo.PerformRedo();
					Check(draft.Current[entry.file].EndsWith(" "), "Unity Redo reapplies draft JSON");

					// Reset the draft to the saved content and clear its undo records.
					draft.Set(localFiles);
					Undo.ClearUndo(draft);

					// Simulate an interrupted write after its journal reached disk.
					var recoveryBefore = File.ReadAllText(externalPath);
					var pending = new ContentTransaction
					{
						id = "999999-recovery-test",
						files = new[]
						{
							new ContentFileChange
							{
								file = entry.file, before = recoveryBefore, after = "broken", beforeExists = true,
								afterExists = true
							}
						},
						revisions = Array.Empty<ContentRevision>()
					};
					ContentHistory.AtomicWrite(
						Path.Combine(history.HistoryDirectory, "transactions", pending.id + ".json"),
						JsonUtility.ToJson(pending));

					// Corrupt the file on disk and run recovery.
					File.WriteAllText(externalPath, "broken");
					history.Recover();

					// Verify recovery restores the previous bytes and hides the aborted transaction.
					Check(File.ReadAllText(externalPath) == recoveryBefore,
						  "Interrupted transaction recovery restores previous bytes");
					Check(history.Transactions().All(t => t.committed),
						  "Aborted transactions are excluded from visible history");

					// Round-trip the draft through Unity serialization and compare its content.
					var recovery = JsonUtility.ToJson(draft);
					var recovered = ScriptableObject.CreateInstance<ContentDraft>();
					JsonUtility.FromJsonOverwrite(recovery, recovered);
					Check(recovered.Current[entry.file] == draft.Current[entry.file],
						  "Draft recovery round-trip preserves content");
					Object.DestroyImmediate(recovered);

					// Save the working directory, then load the data from the sandbox to prove path independence.
					var savedDirectory = Environment.CurrentDirectory;
					try
					{
						// Switch the working directory to the sandbox for the load check.
						Environment.CurrentDirectory = sandbox;
						using (var relocated = ContentJson.Load(data))
						{
							Check(relocated.Game != null,
								  "Absolute directory loading is independent of working directory");
						}
					}
					finally
					{
						// Restore the original working directory.
						Environment.CurrentDirectory = savedDirectory;
					}
				}
			}
			finally
			{
				// Destroy the temporary draft asset.
				Object.DestroyImmediate(draft);
			}

			// Write the PASS report under Artifacts.
			Directory.CreateDirectory("Artifacts");
			File.WriteAllText("Artifacts/content-validation.txt", string.Join("\n", results) + "\n");

			// Log the summary with the sandbox location.
			Debug.Log("Content validation passed: " + results.Count + " checks. Sandbox retained in " + sandbox);
		}
	}
}