using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnityEngine;

namespace HackThePlanet.Editor
{
	/// <summary>Before and after snapshot of one authoritative JSON file within a history transaction.</summary>
	[Serializable]
	public sealed class ContentFileChange
	{
		/// <summary>Gets or sets the relative path of the JSON file under the data directory.</summary>
		public string file;

		/// <summary>Gets or sets the file text before the transaction, or null when the file did not exist.</summary>
		public string before;

		/// <summary>Gets or sets the file text after the transaction, or null when the file is deleted.</summary>
		public string after;

		/// <summary>Gets or sets whether <see cref="before" /> reflects an existing on-disk file.</summary>
		public bool beforeExists;

		/// <summary>Gets or sets whether <see cref="after" /> reflects a file that should exist after commit.</summary>
		public bool afterExists;
	}

	/// <summary>Immutable revision record for one content entry captured during a save or external change.</summary>
	[Serializable]
	public sealed class ContentRevision
	{
		/// <summary>Gets or sets the unique revision identifier.</summary>
		public string id;

		/// <summary>Gets or sets the parent revision id, or null for the first revision of an entry.</summary>
		public string parent;

		/// <summary>Gets or sets the UTC timestamp when the revision was recorded (ISO 8601).</summary>
		public string timestamp;

		/// <summary>Gets or sets the operation name (for example Save, Delete, or External).</summary>
		public string operation;

		/// <summary>Gets or sets the SHA-256 hash of <see cref="json" /> used for integrity checks.</summary>
		public string hash;

		/// <summary>Gets or sets the optional user-facing revision label.</summary>
		public string label;

		/// <summary>Gets or sets the owning <see cref="ContentTransaction" /> id.</summary>
		public string transaction;

		/// <summary>Gets or sets manifest registration metadata for the entry at this revision.</summary>
		public ContentEntry entry;

		/// <summary>Gets or sets the encoded JSON snapshot, or null when <see cref="deleted" /> is true.</summary>
		public string json;

		/// <summary>Gets or sets whether this revision represents a deliberate entry deletion.</summary>
		public bool deleted;
	}

	/// <summary>Write-ahead journal grouping file changes and entry revisions for one commit attempt.</summary>
	[Serializable]
	public sealed class ContentTransaction
	{
		/// <summary>Gets or sets the unique transaction identifier.</summary>
		public string id;

		/// <summary>Gets or sets the UTC timestamp when the transaction started (ISO 8601).</summary>
		public string timestamp;

		/// <summary>Gets or sets whether all file writes completed successfully.</summary>
		public bool committed;

		/// <summary>Gets or sets the authoritative file deltas included in this transaction.</summary>
		public ContentFileChange[] files;

		/// <summary>Gets or sets entry-level revisions derived from the file changes.</summary>
		public ContentRevision[] revisions;
	}

	/// <summary>Independent local history with write-ahead transactions. No changes to the project's Git repository.</summary>
	public sealed class ContentHistory
	{
		/// <summary>Gets the absolute path to authoritative JSON content.</summary>
		public readonly string DataDirectory;

		/// <summary>Gets the absolute path to the local history store (defaults to a sibling <c>.data-history</c> folder).</summary>
		public readonly string HistoryDirectory;

		/// <summary>Initializes history for the given data directory and optional history folder.</summary>
		/// <param name="dataDirectory">Root folder containing manifest and definition JSON files.</param>
		/// <param name="historyDirectory">Optional override for the transaction journal directory.</param>
		public ContentHistory(string dataDirectory, string historyDirectory = null)
		{
			// Resolve the data directory to an absolute path.
			DataDirectory = Path.GetFullPath(dataDirectory);

			// Default the history folder to a sibling of the data directory.
			HistoryDirectory = historyDirectory ?? Path.Combine(Path.GetDirectoryName(DataDirectory), ".data-history");
		}

		/// <summary>Computes a lowercase SHA-256 hex digest of UTF-8 text, or <c>missing</c> when text is null.</summary>
		/// <param name="text">Snapshot text to hash.</param>
		/// <returns>The digest string used for conflict and integrity checks.</returns>
		public static string Hash(string text)
		{
			// Return the placeholder digest for a missing snapshot.
			if (text == null)
				return "missing";

			// Hash the UTF-8 bytes with SHA-256 and format the digest as lowercase hex.
			using (var sha = SHA256.Create())
			{
				return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "")
								   .ToLowerInvariant();
			}
		}

		/// <summary>Writes or deletes a file atomically via a temporary path and replace/move.</summary>
		/// <param name="path">Destination file path; parent directories are created as needed.</param>
		/// <param name="text">UTF-8 content to write, or null to delete an existing file.</param>
		public static void AtomicWrite(string path, string text)
		{
			// Create the parent folder for the destination file.
			Directory.CreateDirectory(Path.GetDirectoryName(path));

			// Delete the file when the text is null.
			if (text == null)
			{
				// Remove the existing file if there is one.
				if (File.Exists(path))
					File.Delete(path);

				return;
			}

			// Write the text to a temporary file first.
			var temp = path + ".writing";
			File.WriteAllText(temp, text, new UTF8Encoding(false));

			// Replace an existing destination, otherwise move the temporary file into place.
			if (File.Exists(path))
				File.Replace(temp, path, null);
			else
				File.Move(temp, path);
		}

		/// <summary>Loads committed and pending transactions and verifies revision hashes.</summary>
		/// <returns>Transactions ordered by journal file name.</returns>
		/// <exception cref="InvalidDataException">A stored revision hash does not match its JSON snapshot.</exception>
		public List<ContentTransaction> Transactions()
		{
			// Locate the transaction journal folder.
			var folder = Path.Combine(HistoryDirectory, "transactions");

			// Return an empty list when no journal exists yet.
			if (!Directory.Exists(folder))
				return new List<ContentTransaction>();

			// Read every journal file in ordinal file-name order.
			var transactions = Directory.GetFiles(folder, "*.json").OrderBy(p => p, StringComparer.Ordinal)
										.Select(p => JsonUtility.FromJson<ContentTransaction>(File.ReadAllText(p)))
										.ToList();

			// Verify each transaction's file deltas and revision hashes.
			foreach (var transaction in transactions)
			{
				// Clear the before or after text for files that did not exist on that side.
				foreach (var file in transaction.files)
				{
					if (!file.beforeExists)
						file.before = null;
					if (!file.afterExists)
						file.after = null;
				}

				// Normalize deleted and root revisions before checking their hashes.
				foreach (var revision in transaction.revisions)
				{
					if (revision.deleted)
						revision.json = null;
					if (string.IsNullOrEmpty(revision.parent))
						revision.parent = null;

					// Reject any revision whose stored hash does not match its snapshot.
					if (Hash(revision.json) != revision.hash)
						throw new InvalidDataException("History snapshot integrity error: " + revision.id +
													   ". Restore the history store from a backup before saving.");
				}
			}

			return transactions;
		}

		/// <summary>Lists committed revisions for one manifest entry key.</summary>
		/// <param name="key">Immutable manifest key for the content entry.</param>
		/// <returns>Revisions in transaction order for that entry.</returns>
		public List<ContentRevision> Revisions(string key)
		{
			// Gather committed revisions for this entry in transaction order.
			return Transactions().Where(t => t.committed).SelectMany(t => t.revisions).Where(r => r.entry.key == key)
								 .ToList();
		}

		/// <summary>Rolls back uncommitted transactions and marks their journals as aborted.</summary>
		public void Recover()
		{
			foreach (var transaction in Transactions().Where(t => !t.committed))
			{
				// Roll back incomplete transactions. Revision records remain in an aborted record, outside visible history.
				foreach (var change in transaction.files.Reverse())
					AtomicWrite(ContentJson.SafePath(DataDirectory, change.file), change.before);

				// Mark the journal as aborted so it stays out of visible history.
				var source = TransactionPath(transaction.id);
				File.Move(source, source + ".aborted");

				// Log that the interrupted save was recovered.
				Debug.LogWarning("Recovered interrupted content save " + transaction.id +
								 ". Authoritative data restored to its previous state.");
			}
		}

		// Builds the on-disk path for a committed transaction journal file.
		private string TransactionPath(string id)
		{
			return Path.Combine(HistoryDirectory, "transactions", id + ".json");
		}

		/// <summary>Validates, journals, and commits authoritative file changes with per-entry revisions.</summary>
		/// <param name="desired">Candidate file map to write after validation.</param>
		/// <param name="expected">Last-known file map used to detect external edits.</param>
		/// <param name="label">Optional revision label stored on new revisions.</param>
		/// <param name="operation">Operation name recorded on new revisions (default Save).</param>
		/// <param name="reviewedOverwrite">When true, skips conflict checks after the user reviewed disk content.</param>
		/// <param name="reviewedDisk">Disk snapshot approved for overwrite; must still match current bytes when set.</param>
		/// <returns>The committed transaction id, or null when there are no file changes.</returns>
		/// <exception cref="IOException">An external edit conflict is detected before writing.</exception>
		public string Commit(IDictionary<string, string> desired, IDictionary<string, string> expected,
							 string label = "", string operation = "Save", bool reviewedOverwrite = false,
							 IDictionary<string, string> reviewedDisk = null)
		{
			// Roll back any interrupted transaction before comparing files.
			Recover();

			// Build the union of known and desired file paths.
			var all = expected.Keys.Union(desired.Keys).Distinct().ToArray();

			// Collect the files whose disk text differs from the desired text.
			var changes = new List<ContentFileChange>();
			foreach (var file in all)
			{
				// Read the current disk text and the expected and desired text for this file.
				var path = ContentJson.SafePath(DataDirectory, file);
				var actual = File.Exists(path) ? File.ReadAllText(path) : null;
				expected.TryGetValue(file, out var old);
				desired.TryGetValue(file, out var next);

				// Detect external edits against the reviewed disk snapshot or the last known file map.
				if (reviewedDisk != null)
				{
					// Compare the current disk text with the reviewed snapshot.
					reviewedDisk.TryGetValue(file, out var reviewed);
					if (Hash(actual) != Hash(reviewed))
						throw new IOException("External edit conflict: " + file + ". File changed again after review.");
				}
				else if (Hash(actual) != Hash(old) && !reviewedOverwrite)
				{
					// Reject the write when disk changed and the overwrite was not reviewed.
					throw new IOException("External edit conflict: " + file +
										  ". Reload, or review the disk diff before overwriting.");
				}

				// Record the file change when disk text differs from the desired text.
				if (actual != next)
					changes.Add(new ContentFileChange
					{
						file = file, before = actual, after = next, beforeExists = actual != null,
						afterExists = next != null
					});
			}

			// Stop early when no file differs from disk.
			if (changes.Count == 0)
				return null;

			// Validate the candidate before writing either history or data; null marks a deliberate deletion.
			var candidate = desired.ToDictionary(p => p.Key, p => p.Value);
			foreach (var change in changes.Where(c => c.after == null))
				candidate[change.file] = null;

			// Load the candidate to confirm it validates before anything is written.
			using (var bundle = ContentJson.Load(DataDirectory, candidate))
			{
			}

			// Load the manifest as it existed before this commit, falling back to disk when unchanged.
			var beforeText = changes.FirstOrDefault(c => c.file == "manifest.json")?.before;
			if (beforeText == null && File.Exists(ContentJson.SafePath(DataDirectory, "manifest.json")))
				beforeText = File.ReadAllText(ContentJson.SafePath(DataDirectory, "manifest.json"));

			// Decode the before and after manifests for entry comparison.
			var beforeManifest = beforeText != null
				? ContentJson.Decode<ContentManifest>(beforeText)
				: new ContentManifest();
			var afterManifest = ContentJson.Decode<ContentManifest>(desired["manifest.json"]);

			// Create the transaction id and the shared timestamp for this commit.
			var transactionId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + "-" + Guid.NewGuid().ToString("N");
			var now = DateTime.UtcNow.ToString("O");

			// Create the list that collects revisions for changed entries.
			var revisions = new List<ContentRevision>();

			// Compare every entry key that exists before or after this commit.
			foreach (var key in beforeManifest.entries.Select(e => e.key)
											  .Union(afterManifest.entries.Select(e => e.key)))
			{
				// Look up the before and after registrations for this entry.
				var beforeEntry = beforeManifest.entries.FirstOrDefault(e => e.key == key);
				var afterEntry = afterManifest.entries.FirstOrDefault(e => e.key == key);
				var entry = afterEntry ?? beforeEntry;

				// Read the entry's text before this commit, falling back to disk when needed.
				var before = beforeEntry == null
					? null
					: changes.FirstOrDefault(c => c.file == beforeEntry.file)?.before;
				if (beforeEntry != null && before == null &&
					File.Exists(ContentJson.SafePath(DataDirectory, beforeEntry.file)))
					before = File.ReadAllText(ContentJson.SafePath(DataDirectory, beforeEntry.file));

				// Read the entry's text after this commit.
				var after = afterEntry == null ? null : desired[afterEntry.file];

				// Skip entries whose text and registration are both unchanged.
				var registrationChanged = ContentJson.Encode(beforeEntry) != ContentJson.Encode(afterEntry);
				if (before == after && !registrationChanged)
					continue;

				// Find the latest revision recorded for this entry.
				var previous = Revisions(key).LastOrDefault();

				// Record the current disk text when history does not already hold it.
				if (beforeEntry != null && (previous == null || previous.hash != Hash(before)))
				{
					previous = Revision(beforeEntry, before, previous?.id, previous == null ? "Initial" : "External",
										previous == null ? "Original disk version" : "External disk version");
					revisions.Add(previous);
				}

				// Record the committed revision for this entry.
				revisions.Add(Revision(entry, after, previous?.id,
									   afterEntry == null ? "Delete" : beforeEntry == null ? "Create" : operation,
									   label));
			}

			// Local helper that builds one immutable revision record.
			ContentRevision Revision(ContentEntry entry, string json, string parent, string op, string name)
			{
				// Assign a unique id, the hash, and a copy of the entry registration.
				return new ContentRevision
				{
					id = Guid.NewGuid().ToString("N"), parent = parent, timestamp = now, operation = op,
					hash = Hash(json),
					label = name, transaction = transactionId,
					entry = ContentJson.Decode<ContentEntry>(ContentJson.Encode(entry)), json = json,
					deleted = json == null
				};
			}

			// Group the file changes and revisions into one transaction journal entry.
			var transaction = new ContentTransaction
				{ id = transactionId, timestamp = now, files = changes.ToArray(), revisions = revisions.ToArray() };

			// The entire before/after set and immutable revision contents reach disk before any authoritative file is touched.
			AtomicWrite(TransactionPath(transactionId), JsonUtility.ToJson(transaction, true));

			// Apply the file writes and mark the transaction committed, rolling back on failure.
			try
			{
				// Write each changed authoritative file.
				foreach (var change in changes)
					AtomicWrite(ContentJson.SafePath(DataDirectory, change.file), change.after);

				// Mark the journal committed and persist it.
				transaction.committed = true;
				AtomicWrite(TransactionPath(transactionId), JsonUtility.ToJson(transaction, true));

				// Return the id of the committed transaction.
				return transactionId;
			}
			catch
			{
				// Roll back the partial commit and rethrow the original failure.
				Recover();
				throw;
			}
		}

		/// <summary>Renames the label on one saved revision for an entry.</summary>
		/// <param name="key">Manifest entry key owning the revision.</param>
		/// <param name="revisionId">Revision id to rename.</param>
		/// <param name="label">New user-facing label.</param>
		/// <exception cref="InvalidOperationException">No matching committed revision was found.</exception>
		public void Name(string key, string revisionId, string label)
		{
			// Search each committed transaction for the target revision.
			foreach (var transaction in Transactions().Where(t => t.committed))
			{
				// Find the revision matching both the entry key and revision id.
				var revision = transaction.revisions.SingleOrDefault(r => r.entry.key == key && r.id == revisionId);

				// Skip transactions that do not contain the revision.
				if (revision == null)
					continue;

				// Apply the new label and persist the transaction journal.
				revision.label = label;
				AtomicWrite(TransactionPath(transaction.id), JsonUtility.ToJson(transaction, true));

				return;
			}

			// Report that no committed revision matched the request.
			throw new InvalidOperationException("Revision not found.");
		}

		/// <summary>Formats a human-readable diff between two JSON snapshots.</summary>
		/// <param name="before">Previous JSON text, or null for a deletion.</param>
		/// <param name="after">Next JSON text, or null for a deletion.</param>
		/// <param name="raw">When true, emits a line-oriented unified diff; otherwise flattens property paths.</param>
		/// <returns>A diff description, or a no-differences message when snapshots match structurally.</returns>
		public static string Diff(string before, string after, bool raw)
		{
			// Produce a line-oriented unified diff when raw output is requested.
			if (raw)
			{
				// Split both snapshots into normalized lines.
				var a = (before ?? "<deleted>").Replace("\r\n", "\n").Split('\n');
				var b = (after ?? "<deleted>").Replace("\r\n", "\n").Split('\n');

				// Count the unchanged leading and trailing lines.
				int prefix = 0, suffix = 0;
				while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix]) prefix++;
				while (suffix < a.Length - prefix && suffix < b.Length - prefix &&
					   a[a.Length - suffix - 1] == b[b.Length - suffix - 1]) suffix++;

				// Build the unified diff text from the changed middle lines.
				return "--- Previous JSON\n+++ Selected JSON\n" + string.Join("\n", a.Take(prefix).Select(l => " " + l)
																				  .Concat(a.Skip(prefix)
																						  .Take(a.Length - prefix -
																							  suffix)
																						  .Select(l => "-" + l))
																				  .Concat(b.Skip(prefix)
																						  .Take(b.Length - prefix -
																							  suffix)
																						  .Select(l => "+" + l))
																				  .Concat(a.Skip(a.Length - suffix)
																						  .Select(l => " " + l)));
			}

			// Declare the flattened property maps for both snapshots.
			Dictionary<string, string> left, right;

			// Flatten both snapshots into property-path maps.
			try
			{
				left = Flatten(before);
				right = Flatten(after);
			}
			catch (JsonException)
			{
				// Fall back to a raw line comparison when either snapshot is malformed.
				return "One snapshot is malformed JSON. Raw file comparison:\n" + Diff(before, after, true);
			}

			// Collect the property paths whose values differ, in sorted order.
			var lines = new List<string>();
			foreach (var key in left.Keys.Union(right.Keys).OrderBy(k => k, StringComparer.Ordinal))
			{
				// Look up each snapshot's value for this property path.
				left.TryGetValue(key, out var a);
				right.TryGetValue(key, out var b);

				// Record the path only when its values differ.
				if (a != b)
					lines.Add(key + "\n  - " + (a ?? "<missing>") + "\n  + " + (b ?? "<missing>"));
			}

			// Report either the changed properties or a no-differences message.
			return lines.Count == 0 ? "No property differences." : string.Join("\n", lines);
		}

		// Flattens a JSON document into dotted paths mapped to raw leaf values.
		private static Dictionary<string, string> Flatten(string json)
		{
			// Create the flattened map that collects every leaf value.
			var result = new Dictionary<string, string>();

			// Return an empty map when there is no snapshot.
			if (json == null)
				return result;

			// Parse the JSON and walk it from the root.
			using (var document = JsonDocument.Parse(json))
			{
				Visit(document.RootElement, "$");
			}

			return result;

			// Local helper that records each leaf path and its raw JSON text.
			void Visit(JsonElement e, string path)
			{
				// Record empty objects and recurse into each property.
				if (e.ValueKind == JsonValueKind.Object)
				{
					// Record an empty object explicitly so it is not lost.
					if (!e.EnumerateObject().Any())
						result[path] = "{}";

					// Visit every property under its dotted path.
					foreach (var p in e.EnumerateObject())
						Visit(p.Value, path + "." + p.Name);
				}
				else if (e.ValueKind == JsonValueKind.Array)
				{
					// Track the array index and record the array length.
					var i = 0;
					result[path + ".length"] = e.GetArrayLength().ToString();

					// Visit each array item under its indexed path.
					foreach (var item in e.EnumerateArray())
						Visit(item, path + "[" + i++ + "]");
				}
				else
				{
					// Store scalar values as their raw JSON text.
					result[path] = e.GetRawText();
				}
			}
		}
	}
}