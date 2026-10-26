using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cashg.GraphExplorer
{
	/// <summary>Versioned JSON storage. Unknown/corrupt existing documents are never overwritten.</summary>
	public sealed class GraphStorage
	{
		private readonly string directory;

		/// <summary>Creates versioned storage scoped to a host, dataset, and provider.</summary>
		/// <param name="root">Absolute root directory for stored data.</param>
		/// <param name="hostId">Host identity.</param>
		/// <param name="datasetId">Dataset identity.</param>
		/// <param name="providerId">Provider identity.</param>
		public GraphStorage(string root, string hostId, string datasetId, string providerId)
		{
			if (string.IsNullOrWhiteSpace(root))
				throw new ArgumentException("An explicit storage root is required.", nameof(root));
			directory = Path.Combine(Path.GetFullPath(root), Key(hostId), Key(datasetId), Key(providerId));
		}

		private static string Key(string identity)
		{
			if (string.IsNullOrWhiteSpace(identity)) throw new ArgumentException("A storage identity is required.");
			using var hash = SHA256.Create();
			return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "")
							   .ToLowerInvariant();
		}

		/// <summary>Gets the hashed storage path for a document name.</summary>
		/// <param name="name">Document name to scope.</param>
		/// <returns>Full path to the versioned JSON file.</returns>
		public string PathFor(string name)
		{
			return Path.Combine(directory, Key(name) + ".json");
		}

		private static int Version(string json)
		{
			using var document = JsonDocument.Parse(json);
			return document.RootElement.TryGetProperty("version", out var version) &&
				   version.TryGetInt32(out var number)
				? number
				: -1;
		}

		/// <summary>Reads a document when its stored version matches the supported version.</summary>
		/// <param name="name">Document name.</param>
		/// <param name="supportedVersion">Version accepted by the caller.</param>
		/// <returns>Stored JSON, or null when missing or unsupported.</returns>
		public string Read(string name, int supportedVersion)
		{
			var path = PathFor(name);
			if (!File.Exists(path)) return null;

			// Return contents only when their stored format matches the caller's supported version.
			var json = File.ReadAllText(path, Encoding.UTF8);
			return Version(json) == supportedVersion ? json : null;
		}

		/// <summary>Writes JSON without overwriting an existing corrupt or unsupported document.</summary>
		/// <param name="name">Document name.</param>
		/// <param name="supportedVersion">Version required in the JSON document.</param>
		/// <param name="json">Document content to write.</param>
		/// <returns><see langword="true"/> when the document was written.</returns>
		public bool Write(string name, int supportedVersion, string json)
		{
			// Validate the new payload before inspecting or replacing any existing file.
			if (Version(json) != supportedVersion)
				throw new ArgumentException("Document version does not match the writer.", nameof(json));
			var path = PathFor(name);
			if (File.Exists(path))
				try
				{
					if (Version(File.ReadAllText(path, Encoding.UTF8)) != supportedVersion) return false;
				}
				catch (JsonException)
				{
					return false;
				}

			Directory.CreateDirectory(directory);
			var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
			try
			{

				// Stage the complete file before replacing the destination.
				File.WriteAllText(temporary, json, new UTF8Encoding(false));
				if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
				else File.Move(temporary, path);
				return true;
			}
			finally
			{
				if (File.Exists(temporary)) File.Delete(temporary);
			}
		}

		/// <summary>Host-invoked, copy-only migration. Original and already migrated documents survive untouched.</summary>
		public bool ImportLegacy(string name, string legacyPath)
		{
			var destination = PathFor(name);
			if (!File.Exists(legacyPath) || File.Exists(destination)) return false;
			Directory.CreateDirectory(directory);
			File.Copy(legacyPath, destination, false);
			return true;
		}
	}
}
