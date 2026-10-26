using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace HackThePlanet.Editor
{
    [Serializable] public sealed class ContentFileChange
    {
        public string file, before, after;
        public bool beforeExists, afterExists;
    }
    [Serializable] public sealed class ContentRevision
    {
        public string id, parent, timestamp, operation, hash, label, transaction;
        public ContentEntry entry;
        public string json;
        public bool deleted;
    }
    [Serializable] public sealed class ContentTransaction
    {
        public string id, timestamp;
        public bool committed;
        public ContentFileChange[] files;
        public ContentRevision[] revisions;
    }

    /// <summary>Independent local history with write-ahead transactions. No changes to the project's Git repository.</summary>
    public sealed class ContentHistory
    {
        public readonly string DataDirectory, HistoryDirectory;
        public ContentHistory(string dataDirectory, string historyDirectory = null)
        {
            DataDirectory = Path.GetFullPath(dataDirectory);
            HistoryDirectory = historyDirectory ?? Path.Combine(Path.GetDirectoryName(DataDirectory), ".data-history");
        }
        public static string Hash(string text)
        {
            if (text == null) return "missing";
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }
        public static void AtomicWrite(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (text == null) { if (File.Exists(path)) File.Delete(path); return; }
            string temp = path + ".writing";
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        public List<ContentTransaction> Transactions()
        {
            string folder = Path.Combine(HistoryDirectory, "transactions");
            if (!Directory.Exists(folder)) return new List<ContentTransaction>();
            var transactions = Directory.GetFiles(folder, "*.json").OrderBy(p => p, StringComparer.Ordinal).Select(p => JsonUtility.FromJson<ContentTransaction>(File.ReadAllText(p))).ToList();
            foreach (var transaction in transactions)
            {
                foreach (var file in transaction.files) { if (!file.beforeExists) file.before = null; if (!file.afterExists) file.after = null; }
                foreach (var revision in transaction.revisions)
                {
                    if (revision.deleted) revision.json = null; if (string.IsNullOrEmpty(revision.parent)) revision.parent = null;
                    if (Hash(revision.json) != revision.hash) throw new InvalidDataException("History snapshot integrity error: " + revision.id + ". Restore the history store from a backup before saving.");
                }
            }
            return transactions;
        }
        public List<ContentRevision> Revisions(string key) => Transactions().Where(t => t.committed).SelectMany(t => t.revisions).Where(r => r.entry.key == key).ToList();
        public void Recover()
        {
            foreach (var transaction in Transactions().Where(t => !t.committed))
            {
                // Roll back incomplete transactions. Revision records remain in an aborted record, outside visible history.
                foreach (var change in transaction.files.Reverse()) AtomicWrite(ContentJson.SafePath(DataDirectory, change.file), change.before);
                string source = TransactionPath(transaction.id);
                File.Move(source, source + ".aborted");
                Debug.LogWarning("Recovered interrupted content save " + transaction.id + ". Authoritative data restored to its previous state.");
            }
        }
        private string TransactionPath(string id) => Path.Combine(HistoryDirectory, "transactions", id + ".json");
        public string Commit(IDictionary<string, string> desired, IDictionary<string, string> expected, string label = "", string operation = "Save", bool reviewedOverwrite = false, IDictionary<string, string> reviewedDisk = null)
        {
            Recover();
            var all = expected.Keys.Union(desired.Keys).Distinct().ToArray();
            var changes = new List<ContentFileChange>();
            foreach (string file in all)
            {
                string path = ContentJson.SafePath(DataDirectory, file);
                string actual = File.Exists(path) ? File.ReadAllText(path) : null;
                expected.TryGetValue(file, out string old); desired.TryGetValue(file, out string next);
                if (reviewedDisk != null) { reviewedDisk.TryGetValue(file, out var reviewed); if (Hash(actual) != Hash(reviewed)) throw new IOException("External edit conflict: " + file + ". File changed again after review."); }
                else if (Hash(actual) != Hash(old) && !reviewedOverwrite) throw new IOException("External edit conflict: " + file + ". Reload, or review the disk diff before overwriting.");
                if (actual != next) changes.Add(new ContentFileChange { file = file, before = actual, after = next, beforeExists = actual != null, afterExists = next != null });
            }
            if (changes.Count == 0) return null;
            // Validate the candidate before writing either history or data; null marks a deliberate deletion.
            var candidate = desired.ToDictionary(p => p.Key, p => p.Value);
            foreach (var change in changes.Where(c => c.after == null)) candidate[change.file] = null;
            using (var bundle = ContentJson.Load(DataDirectory, candidate)) { }
            string beforeText = changes.FirstOrDefault(c => c.file == "manifest.json")?.before;
            if (beforeText == null && File.Exists(ContentJson.SafePath(DataDirectory, "manifest.json"))) beforeText = File.ReadAllText(ContentJson.SafePath(DataDirectory, "manifest.json"));
            var beforeManifest = beforeText != null ? ContentJson.Decode<ContentManifest>(beforeText) : new ContentManifest();
            var afterManifest = ContentJson.Decode<ContentManifest>(desired["manifest.json"]);
            string transactionId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + "-" + Guid.NewGuid().ToString("N");
            string now = DateTime.UtcNow.ToString("O");
            var revisions = new List<ContentRevision>();
            foreach (string key in beforeManifest.entries.Select(e => e.key).Union(afterManifest.entries.Select(e => e.key)))
            {
                var beforeEntry = beforeManifest.entries.FirstOrDefault(e => e.key == key);
                var afterEntry = afterManifest.entries.FirstOrDefault(e => e.key == key);
                var entry = afterEntry ?? beforeEntry;
                string before = beforeEntry == null ? null : changes.FirstOrDefault(c => c.file == beforeEntry.file)?.before;
                if (beforeEntry != null && before == null && File.Exists(ContentJson.SafePath(DataDirectory, beforeEntry.file))) before = File.ReadAllText(ContentJson.SafePath(DataDirectory, beforeEntry.file));
                string after = afterEntry == null ? null : desired[afterEntry.file];
                bool registrationChanged = ContentJson.Encode(beforeEntry) != ContentJson.Encode(afterEntry);
                if (before == after && !registrationChanged) continue;
                var previous = Revisions(key).LastOrDefault();
                if (beforeEntry != null && (previous == null || previous.hash != Hash(before)))
                {
                    previous = Revision(beforeEntry, before, previous?.id, previous == null ? "Initial" : "External", previous == null ? "Original disk version" : "External disk version"); revisions.Add(previous);
                }
                revisions.Add(Revision(entry, after, previous?.id, afterEntry == null ? "Delete" : beforeEntry == null ? "Create" : operation, label));
            }
            ContentRevision Revision(ContentEntry entry, string json, string parent, string op, string name) => new ContentRevision {
                id = Guid.NewGuid().ToString("N"), parent = parent, timestamp = now, operation = op, hash = Hash(json),
                label = name, transaction = transactionId, entry = ContentJson.Decode<ContentEntry>(ContentJson.Encode(entry)), json = json, deleted = json == null
            };
            var transaction = new ContentTransaction { id = transactionId, timestamp = now, files = changes.ToArray(), revisions = revisions.ToArray() };
            // The entire before/after set and immutable revision contents reach disk before any authoritative file is touched.
            AtomicWrite(TransactionPath(transactionId), JsonUtility.ToJson(transaction, true));
            try
            {
                foreach (var change in changes) AtomicWrite(ContentJson.SafePath(DataDirectory, change.file), change.after);
                transaction.committed = true; AtomicWrite(TransactionPath(transactionId), JsonUtility.ToJson(transaction, true));
                return transactionId;
            }
            catch { Recover(); throw; }
        }
        public void Name(string key, string revisionId, string label)
        {
            foreach (var transaction in Transactions().Where(t => t.committed))
            {
                var revision = transaction.revisions.SingleOrDefault(r => r.entry.key == key && r.id == revisionId);
                if (revision == null) continue;
                revision.label = label; AtomicWrite(TransactionPath(transaction.id), JsonUtility.ToJson(transaction, true)); return;
            }
            throw new InvalidOperationException("Revision not found.");
        }
        public static string Diff(string before, string after, bool raw)
        {
            if (raw)
            {
                var a = (before ?? "<deleted>").Replace("\r\n", "\n").Split('\n');
                var b = (after ?? "<deleted>").Replace("\r\n", "\n").Split('\n');
                int prefix = 0, suffix = 0;
                while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix]) prefix++;
                while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[a.Length - suffix - 1] == b[b.Length - suffix - 1]) suffix++;
                return "--- Previous JSON\n+++ Selected JSON\n" + string.Join("\n", a.Take(prefix).Select(l => " " + l)
                    .Concat(a.Skip(prefix).Take(a.Length - prefix - suffix).Select(l => "-" + l))
                    .Concat(b.Skip(prefix).Take(b.Length - prefix - suffix).Select(l => "+" + l))
                    .Concat(a.Skip(a.Length - suffix).Select(l => " " + l)));
            }
            Dictionary<string, string> left, right;
            try { left = Flatten(before); right = Flatten(after); }
            catch (System.Text.Json.JsonException) { return "One snapshot is malformed JSON. Raw file comparison:\n" + Diff(before, after, true); }
            var lines = new List<string>();
            foreach (string key in left.Keys.Union(right.Keys).OrderBy(k => k, StringComparer.Ordinal))
            { left.TryGetValue(key, out var a); right.TryGetValue(key, out var b); if (a != b) lines.Add(key + "\n  - " + (a ?? "<missing>") + "\n  + " + (b ?? "<missing>")); }
            return lines.Count == 0 ? "No property differences." : string.Join("\n", lines);
        }
        private static Dictionary<string, string> Flatten(string json)
        {
            var result = new Dictionary<string, string>(); if (json == null) return result;
            using (var document = System.Text.Json.JsonDocument.Parse(json)) Visit(document.RootElement, "$"); return result;
            void Visit(System.Text.Json.JsonElement e, string path)
            {
                if (e.ValueKind == System.Text.Json.JsonValueKind.Object) { if (!e.EnumerateObject().Any()) result[path] = "{}"; foreach (var p in e.EnumerateObject()) Visit(p.Value, path + "." + p.Name); }
                else if (e.ValueKind == System.Text.Json.JsonValueKind.Array) { int i = 0; result[path + ".length"] = e.GetArrayLength().ToString(); foreach (var item in e.EnumerateArray()) Visit(item, path + "[" + i++ + "]"); }
                else result[path] = e.GetRawText();
            }
        }
    }
}
