using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HackThePlanet.Editor
{
	/// <summary>One relative path and JSON text pair stored in a <see cref="ContentDraft" />.</summary>
	[Serializable]
	public sealed class ContentDraftFile
	{
		/// <summary>Gets or sets the relative JSON path under the data directory.</summary>
		public string file;

		/// <summary>Gets or sets the UTF-8 JSON text for that path.</summary>
		public string text;
	}

	/// <summary>Undo-capable in-memory map of draft JSON files compared against last saved originals.</summary>
	public sealed class ContentDraft : ScriptableObject
	{
		/// <summary>Gets or sets the working draft files.</summary>
		public ContentDraftFile[] files = Array.Empty<ContentDraftFile>();

		/// <summary>Gets or sets the last accepted on-disk snapshot per file.</summary>
		public ContentDraftFile[] originals = Array.Empty<ContentDraftFile>();

		/// <summary>Gets or sets the label for the next save operation shown in the UI.</summary>
		public string operation = "Save";

		/// <summary>Gets the draft files as a path-to-text dictionary.</summary>
		public Dictionary<string, string> Current => files.ToDictionary(f => f.file, f => f.text);

		/// <summary>Gets the accepted baseline files as a path-to-text dictionary.</summary>
		public Dictionary<string, string> Original => originals.ToDictionary(f => f.file, f => f.text);

		/// <summary>Gets whether any draft file differs from the accepted baseline.</summary>
		public bool Dirty => files.Length != originals.Length ||
							 files.Any(f => !Original.TryGetValue(f.file, out var old) || old != f.text);

		/// <summary>Replaces the working draft file map.</summary>
		/// <param name="values">Relative path to JSON text entries.</param>
		public void Set(IDictionary<string, string> values)
		{
			// Rebuild the draft file array from the supplied path-to-text map.
			files = values.Select(p => new ContentDraftFile { file = p.Key, text = p.Value }).ToArray();
		}

		/// <summary>Marks the current draft as the new accepted baseline without writing disk.</summary>
		public void Accept()
		{
			// Snapshot the current draft files as the accepted baseline.
			originals = files.Select(f => new ContentDraftFile { file = f.file, text = f.text }).ToArray();
		}
	}

	/// <summary>Loads JSON into a live object graph, tracks edits on a draft, and applies authoring mutations.</summary>
	public sealed class ContentWorkspace : IDisposable
	{
		/// <summary>Gets the absolute path to authoritative JSON on disk.</summary>
		public readonly string DirectoryPath;

		/// <summary>Gets the draft whose file map drives reloads and saves.</summary>
		public readonly ContentDraft Draft;

		// Caches detected text encodings per draft file path for round-trip saves.
		private Dictionary<string, string> encodings;

		/// <summary>Initializes the workspace from disk when the draft is empty, then reloads the bundle.</summary>
		/// <param name="draft">Draft receiving file edits; populated from disk when empty.</param>
		/// <param name="directory">Data directory containing manifest and JSON definitions.</param>
		public ContentWorkspace(ContentDraft draft, string directory)
		{
			// Store the draft and the data directory it edits.
			Draft = draft;
			DirectoryPath = directory;

			// Populate an empty draft from disk and accept it as the baseline.
			if (draft.files.Length == 0)
			{
				// Load the files from disk without validation.
				using (var bundle = ContentJson.Load(directory, validate: false))
				{
					draft.Set(bundle.Files);
				}

				// Accept the disk snapshot as the baseline.
				draft.Accept();
			}

			// Load the bundle from the draft files.
			Reload();
		}

		/// <summary>Gets the loaded content bundle resolved from the current draft files.</summary>
		public ContentBundle Bundle { get; private set; }

		/// <summary>Disposes the underlying <see cref="ContentBundle" />.</summary>
		public void Dispose()
		{
			// Release the loaded bundle's resources.
			Bundle?.Dispose();
		}

		/// <summary>Reloads <see cref="Bundle" /> from <see cref="Draft.Current" /> and refreshes encoded snapshots.</summary>
		public void Reload()
		{
			// Dispose the previous bundle before loading a new one.
			Bundle?.Dispose();

			// Load the bundle from the current draft files.
			Bundle = ContentJson.Load(DirectoryPath, Draft.Current, false);

			// Cache the encoded JSON of every object for change detection.
			encodings = Bundle.Objects.ToDictionary(p => p.Key, p => ContentJson.Encode(p.Value));
		}

		/// <summary>Serializes in-memory objects back into the draft and prunes removed manifest files.</summary>
		public void Capture()
		{
			// Start from the current draft file map.
			var files = Draft.Current;

			// Build the set of files that the manifest keeps active, including the manifest itself.
			var active = new HashSet<string>(Bundle.Manifest.entries.Select(e => e.file)) { "manifest.json" };

			// Remove draft files that no manifest entry references anymore.
			foreach (var file in files.Keys.Where(f => !active.Contains(f)).ToArray())
				files.Remove(file);

			// Encode each loaded object and store it when it changed.
			foreach (var entry in Bundle.Manifest.entries)
			{
				// Skip entries that failed to load.
				if (Bundle.InvalidEntries.Contains(entry.key))
					continue;

				// Encode the object and compare it with its last snapshot.
				var next = ContentJson.Encode(Bundle.Objects[entry.key]);
				if (!encodings.TryGetValue(entry.key, out var old) || old != next || !files.ContainsKey(entry.file))
					files[entry.file] = next;

				// Remember the encoding for the next comparison.
				encodings[entry.key] = next;
			}

			// Encode the manifest and update its draft file when it changed.
			var manifestJson = ContentJson.Encode(Bundle.Manifest);
			if (!files.TryGetValue("manifest.json", out var oldManifest) ||
				ContentJson.Encode(ContentJson.Decode<ContentManifest>(oldManifest)) != manifestJson)
				files["manifest.json"] = manifestJson;

			// Push the updated file map back into the draft.
			Draft.Set(files);
		}

		/// <summary>Creates or duplicates a definition, registers it in the manifest, and captures the draft.</summary>
		/// <param name="category">Authoring category name (for example Actions or Contracts).</param>
		/// <param name="copyKey">Optional manifest key to duplicate; null creates a blank definition.</param>
		/// <returns>The new manifest entry.</returns>
		/// <exception cref="InvalidOperationException">The category allows only one definition.</exception>
		public ContentEntry Add(string category, string copyKey = null)
		{
			// Reject a second definition in a category that allows exactly one.
			if (Singleton(category))
				throw new InvalidOperationException("This category has exactly one required definition.");

			// Create the new object, either blank or as a copy of the source definition.
			var type = ContentJson.CategoryType(category);
			var obj = copyKey == null
				? ScriptableObject.CreateInstance(type)
				: Object.Instantiate(Bundle.Objects[copyKey]);
			obj.hideFlags = HideFlags.HideAndDontSave;

			// Give the definition a unique id derived from its type or source id.
			var definition = (GameplayDefinition)obj;
			definition.id = UniqueId(copyKey == null
										 ? "new-" + type.Name.Replace("Definition", "").ToLowerInvariant()
										 : definition.id + "-copy");

			// Give blank definitions a default title.
			if (copyKey == null)
				definition.title = "New " + category;

			// Derive the category folder and a filesystem-safe file stem from the id.
			var folder = category == "Chassis" ? "rig/chassis" :
				category == "Modules" ? "rig/modules" : category.ToLowerInvariant().Replace(' ', '-');
			var stem = Regex.Replace(definition.id, "[^a-zA-Z0-9_-]", "-");
			var file = folder + "/" + stem + ".json";

			// Find an unused file path by appending a numeric suffix when needed.
			var suffix = 2;
			while (Bundle.Manifest.entries.Any(e => string.Equals(e.file, file, StringComparison.OrdinalIgnoreCase)) ||
				   File.Exists(ContentJson.SafePath(DirectoryPath, file)))
				file = folder + "/" + stem + "-" + suffix++ + ".json";

			// Register the new entry in the manifest under a fresh immutable key.
			var entry = new ContentEntry
				{ key = Guid.NewGuid().ToString("N"), category = category, type = type.Name, file = file };
			obj.name = entry.file;
			Bundle.Manifest.entries = Bundle.Manifest.entries.Concat(new[] { entry }).ToArray();
			Bundle.Objects.Add(entry.key, obj);

			// Find the root array field that stores this definition type.
			var rootField = ContentJson.Fields(typeof(GameDefinition))
									   .Single(f => f.FieldType.IsArray && f.FieldType.GetElementType() == type);

			// Copy the existing root array into a larger one and append the new object.
			var current = (Array)rootField.GetValue(Bundle.Game);
			var expanded = Array.CreateInstance(type, current.Length + 1);
			Array.Copy(current, expanded, current.Length);
			expanded.SetValue(obj, current.Length);

			// Store the grown array on the root game definition.
			rootField.SetValue(Bundle.Game, expanded);

			// Capture the draft and return the new entry.
			Capture();
			return entry;
		}

		/// <summary>Determines whether a category permits at most one manifest entry.</summary>
		/// <param name="category">Authoring category name.</param>
		/// <returns>True for root singleton categories such as Game or Career.</returns>
		public static bool Singleton(string category)
		{
			return new[]
			{
				"Game", "Career", "Rewards", "Text Palette", "Graph Visuals", "Network Visuals", "Graph Labels",
				"Rig Rules"
			}.Contains(category);
		}

		/// <summary>Allocates a gameplay id unique among loaded definitions, suffixing when needed.</summary>
		/// <param name="seed">Preferred id stem.</param>
		/// <returns>A unique id string.</returns>
		public string UniqueId(string seed)
		{
			// Start from the seed id with a counter ready to append.
			var id = seed;
			var n = 2;

			// Append an increasing counter until no loaded definition uses the id.
			while (Bundle.Objects.Values.OfType<GameplayDefinition>().Any(d => d.id == id)) id = seed + "-" + n++;

			return id;
		}

		/// <summary>Renames a definition id and captures referencing files in the draft.</summary>
		/// <param name="key">Manifest key of the definition to rename.</param>
		/// <param name="id">New unique gameplay id.</param>
		/// <exception cref="InvalidOperationException">The id is empty, whitespace, or already used.</exception>
		public void RenameId(string key, string id)
		{
			// Reject empty, padded, or already used ids.
			if (string.IsNullOrWhiteSpace(id) || id != id.Trim() || Bundle.Objects.Values.OfType<GameplayDefinition>()
																		  .Any(d => d != Bundle.Objects[key] &&
																				   d.id == id))
				throw new InvalidOperationException("ID must be nonempty and unique.");

			// Assign the new id to the definition.
			((GameplayDefinition)Bundle.Objects[key]).id = id;

			// References are object links in the working graph; serialization writes their new ID in every referencing file.
			Capture();

			// Label the draft operation for history.
			Draft.operation = "Rename ID";
		}

		/// <summary>Moves a manifest entry to a new relative JSON path.</summary>
		/// <param name="key">Manifest key of the entry.</param>
		/// <param name="file">New relative .json path under the data directory.</param>
		/// <exception cref="InvalidOperationException">The path is invalid or collides with another entry.</exception>
		/// <exception cref="IOException">An unrelated file already exists at the destination.</exception>
		public void RenameFile(string key, string file)
		{
			// Validate that the destination stays inside the data directory.
			ContentJson.SafePath(DirectoryPath, file);

			// Require a unique relative .json path that is not manifest.json.
			if (!file.EndsWith(".json", StringComparison.Ordinal) || file == "manifest.json" ||
				Bundle.Manifest.entries.Any(e => e.key != key &&
												 string.Equals(e.file, file, StringComparison.OrdinalIgnoreCase)))
				throw new InvalidOperationException("Choose a unique relative .json filename.");

			// Locate the entry being moved.
			var entry = Bundle.Manifest.entries.Single(e => e.key == key);

			// Refuse to overwrite an unrelated file already on disk.
			if (File.Exists(ContentJson.SafePath(DirectoryPath, file)) &&
				!string.Equals(entry.file, file, StringComparison.OrdinalIgnoreCase))
				throw new IOException("Destination already exists on disk.");

			// Move the entry to the new path and capture the draft.
			entry.file = file;
			Capture();

			// Label the draft operation for history.
			Draft.operation = "Rename file";
		}

		/// <summary>Finds object-graph paths that reference the given manifest object.</summary>
		/// <param name="key">Manifest key to search for incoming links.</param>
		/// <returns>Human-readable reference paths suitable for error messages.</returns>
		public List<string> Incoming(string key)
		{
			// Resolve the target object and prepare the list of hit paths.
			var target = Bundle.Objects[key];
			var hits = new List<string>();

			// Walk every non-game definition and record the paths that reach the target.
			foreach (var entry in Bundle.Manifest.entries.Where(e => e.key != key && e.category != "Game"))
				Visit(Bundle.Objects[entry.key], entry.file, true, new HashSet<object>());

			return hits;

			// Local helper that walks an object graph and records paths to the target.
			void Visit(object obj, string path, bool root, HashSet<object> seen)
			{
				// Record the path when the target object is reached.
				if (ReferenceEquals(obj, target))
				{
					hits.Add(path);
					return;
				}

				// Stop at nulls, scalars, cycles, and nested assets that are not part of this graph.
				if (obj == null || obj is string || obj.GetType().IsValueType || (!root && obj is ScriptableObject) ||
					!seen.Add(obj))
					return;

				// Walk collections item by item.
				if (obj is IEnumerable items)
				{
					var i = 0;
					foreach (var item in items)
						Visit(item, path + "[" + i++ + "]", false, seen);
				}
				else
				{
					// Walk each serialized field of the object.
					foreach (var field in ContentJson.Fields(obj.GetType()))
						Visit(field.GetValue(obj), path + "." + field.Name, false, seen);
				}
			}
		}

		/// <summary>Removes a definition from the manifest and game root when it has no incoming references.</summary>
		/// <param name="key">Manifest key to delete.</param>
		/// <exception cref="InvalidOperationException">The entry is required or still referenced.</exception>
		public void Delete(string key)
		{
			// Locate the manifest entry being deleted.
			var entry = Bundle.Manifest.entries.Single(e => e.key == key);

			// Refuse to delete a required singleton.
			if (Singleton(entry.category))
				throw new InvalidOperationException("Cannot delete a required singleton.");

			// Refuse to delete while other definitions still reference this one.
			var references = Incoming(key);
			if (references.Count > 0)
				throw new InvalidOperationException("Reassign/remove incoming references first:\n" +
													string.Join("\n", references));

			// Find the root array field and remove the object from it.
			var type = Bundle.Objects[key].GetType();
			var field = ContentJson.Fields(typeof(GameDefinition))
								   .Single(f => f.FieldType.IsArray && f.FieldType.GetElementType() == type);
			var array = ((Array)field.GetValue(Bundle.Game)).Cast<object>()
															.Where(o => !ReferenceEquals(o, Bundle.Objects[key]))
															.ToArray();

			// Copy the remaining objects into a new root array and store it.
			var next = Array.CreateInstance(type, array.Length);
			for (var i = 0; i < array.Length; i++)
				next.SetValue(array[i], i);
			field.SetValue(Bundle.Game, next);

			// Destroy the object and remove it from the loaded bundle and manifest.
			Object.DestroyImmediate(Bundle.Objects[key]);
			Bundle.Objects.Remove(key);
			Bundle.Manifest.entries = Bundle.Manifest.entries.Where(e => e.key != key).ToArray();

			// Capture the draft and label the operation for history.
			Capture();
			Draft.operation = "Delete";
		}

		/// <summary>Reorders a manifest entry within its category and mirrors order on the game root array.</summary>
		/// <param name="key">Manifest key to move.</param>
		/// <param name="delta">Steps to move within the category (-1 up, +1 down).</param>
		public void Move(string key, int delta)
		{
			// Locate the entry and its current position in the manifest.
			var entry = Bundle.Manifest.entries.Single(e => e.key == key);
			var entries = Bundle.Manifest.entries;
			int index = Array.IndexOf(entries, entry), other = index + delta;

			// Find the next entry in the same category in the requested direction.
			while (other >= 0 && other < entries.Length && entries[other].category != entry.category) other += delta;

			// Stop when there is no neighbor in that direction.
			if (other < 0 || other >= entries.Length)
				return;

			// Swap the two manifest entries.
			(entries[index], entries[other]) = (entries[other], entries[index]);

			// Mirror the new order on the root array for non-singleton categories.
			if (!Singleton(entry.category))
			{
				// Find the root array field for this definition type.
				var type = Bundle.Objects[key].GetType();
				var field = ContentJson.Fields(typeof(GameDefinition))
									   .Single(f => f.FieldType.IsArray && f.FieldType.GetElementType() == type);

				// Rebuild the root array from the reordered manifest entries.
				var ordered = entries.Where(e => e.category == entry.category).Select(e => Bundle.Objects[e.key])
									 .ToArray();
				var array = Array.CreateInstance(type, ordered.Length);
				for (var i = 0; i < ordered.Length; i++)
					array.SetValue(ordered[i], i);

				// Store the reordered array on the root game definition.
				field.SetValue(Bundle.Game, array);
			}

			// Capture the reordered draft.
			Capture();
		}

		/// <summary>Rebuilds draft files from history revisions and reloads the bundle.</summary>
		/// <param name="revisions">Revisions whose JSON and registration should be applied.</param>
		/// <exception cref="InvalidOperationException">Restore would collide on paths or definition ids.</exception>
		public void Restore(IEnumerable<ContentRevision> revisions)
		{
			// Start from the current draft file map and decode its manifest.
			var files = Draft.Current;
			var manifest = ContentJson.Decode<ContentManifest>(files["manifest.json"]);
			var entries = manifest.entries.ToList();

			// Apply each requested revision to the manifest and file map.
			foreach (var revision in revisions)
			{
				// Remove the current registration and file for this entry, remembering its position.
				var existing = entries.SingleOrDefault(e => e.key == revision.entry.key);
				var oldIndex = existing == null ? entries.Count : entries.IndexOf(existing);
				if (existing != null)
				{
					files.Remove(existing.file);
					entries.Remove(existing);
				}

				// Skip deleted revisions; they only remove the entry.
				if (revision.json == null)
					continue;

				// Refuse to restore onto a path that another entry is already using.
				if (entries.Any(e => string.Equals(e.file, revision.entry.file, StringComparison.OrdinalIgnoreCase)))
					throw new InvalidOperationException("Restore path collision: " + revision.entry.file +
														". Rename the conflicting file first.");

				// Refuse to overwrite an unrelated file that occupies the restored path on disk.
				var originalEntries = ContentJson.Decode<ContentManifest>(Draft.Original["manifest.json"]).entries;
				if (File.Exists(ContentJson.SafePath(DirectoryPath, revision.entry.file)) &&
					!originalEntries.Any(e => e.key == revision.entry.key && e.file == revision.entry.file))
					throw new InvalidOperationException("An unrelated file occupies " + revision.entry.file +
														". Move it outside data or rename it before restoring.");

				// Re-register the entry at its previous position and store its JSON.
				entries.Insert(Math.Min(oldIndex, entries.Count),
							   ContentJson.Decode<ContentEntry>(ContentJson.Encode(revision.entry)));
				files[revision.entry.file] = revision.json;
			}

			// Prepare the set of type and id pairs to check for collisions.
			var identities = new HashSet<string>();

			// Walk every restored gameplay definition and check for duplicate identities.
			foreach (var entry in entries.Where(e => typeof(GameplayDefinition).IsAssignableFrom(
													ContentJson.CategoryType(e.category))))
				using (var json = JsonDocument.Parse(files[entry.file]))
				{
					// Build the type and id identity for this restored definition.
					var identity = entry.type + ":" + json.RootElement.GetProperty("id").GetString();

					// Reject the restore when two definitions would share an identity.
					if (!identities.Add(identity))
						throw new InvalidOperationException("Restore ID collision: " + identity +
															". Rename the conflicting entry first.");
				}

			// Write the restored manifest back into the file map.
			manifest.entries = entries.ToArray();
			files["manifest.json"] = ContentJson.Encode(manifest);

			// Rebuild only root registration fields in JSON before resolving references. This avoids losing
			// unrelated root fields when a restored definition changes the set of available IDs.
			var rootRegistration = entries.Single(e => e.category == "Game");
			using (var rootJson = JsonDocument.Parse(files[rootRegistration.file]))
			{
				// Create the output stream and the indented JSON writer.
				using (var stream = new MemoryStream())
				{
					using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
					{
						writer.WriteStartObject();

						// Copy each root property, rewriting definition arrays from the restored ids.
						foreach (var property in rootJson.RootElement.EnumerateObject())
						{
							// Write the property name and resolve its field metadata.
							writer.WritePropertyName(property.Name);
							var field = ContentJson.Fields(typeof(GameDefinition)).Single(f => f.Name == property.Name);
							if (field.FieldType.IsArray &&
								typeof(GameplayDefinition).IsAssignableFrom(field.FieldType.GetElementType()))
							{
								// Write the definition id list for this array field.
								writer.WriteStartArray();
								foreach (var e in entries.Where(e => ContentJson.CategoryType(e.category) ==
																	 field.FieldType.GetElementType()))
									using (var json = JsonDocument.Parse(files[e.file]))
									{
										writer.WriteStringValue(json.RootElement.GetProperty("id").GetString());
									}

								writer.WriteEndArray();
							}
							else
							{
								// Copy non-definition values unchanged.
								property.Value.WriteTo(writer);
							}
						}

						writer.WriteEndObject();
					}

					// Encode the rebuilt root JSON back into the draft file map.
					files[rootRegistration.file] = Encoding.UTF8.GetString(stream.ToArray()) + "\n";
				}
			}

			// Push the restored files into the draft, label the operation, and reload the bundle.
			Draft.Set(files);
			Draft.operation = "Restore";
			Reload();
		}
	}
}