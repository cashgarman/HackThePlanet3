using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HackThePlanet.Editor
{
	/// <summary>Mirrors authoritative JSON definitions into generated ScriptableObject assets for play mode.</summary>
	public static class GameplayAssetGenerator
	{
		/// <summary>Gets the asset folder where JSON-owned gameplay mirrors are written.</summary>
		public const string Output = "Assets/HackThePlanet/GeneratedGameplay";

		/// <summary>Generates gameplay assets from the project's default data directory.</summary>
		[MenuItem("Hack The Planet/JSON/Generate Gameplay Assets")]
		public static void Generate()
		{
			GenerateFrom(GameData.DirectoryPath);
		}

		/// <summary>Generates or updates mirror assets from JSON loaded at the given directory.</summary>
		/// <param name="directory">Root folder containing manifest and definition JSON.</param>
		/// <exception cref="InvalidOperationException">An existing generated asset changed CLR type for a manifest key.</exception>
		public static void GenerateFrom(string directory)
		{
			// Load every JSON definition from the directory for the duration of generation.
			using (var bundle = ContentJson.Load(directory))
			{
				// Create the output folder and refresh the database so it sees current files.
				Directory.CreateDirectory(Output);
				AssetDatabase.Refresh();

				// Track the source-to-asset map and the asset paths written by this run.
				var map = new Dictionary<Object, Object>();
				var paths = new HashSet<string>();

				// Immutable manifest keys preserve .meta GUIDs across ID, filename and title changes.
				foreach (var entry in bundle.Manifest.entries)
				{
					// Build the asset path from the manifest key and record it as expected.
					var path = Output + "/" + entry.key + ".asset";
					paths.Add(path);

					// Find the source object that backs this manifest entry.
					var source = bundle.Objects[entry.key];

					// Load the existing generated asset at this path, if any.
					var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);

					// Refuse to overwrite an asset whose CLR type no longer matches its source.
					if (asset != null && asset.GetType() != source.GetType())
						throw new InvalidOperationException("Generated asset type changed for " + entry.key);

					// Create the asset when it does not exist yet.
					if (asset == null)
					{
						asset = ScriptableObject.CreateInstance(source.GetType());
						AssetDatabase.CreateAsset(asset, path);
					}

					// Copy the source data into the asset and set its identity.
					EditorUtility.CopySerialized(source, asset);
					asset.hideFlags = HideFlags.None;
					asset.name = entry.key;
					map.Add(source, asset);
				}

				// Re-point object references between generated assets so they link to each other.
				foreach (var asset in map.Values)
				{
					// Open the asset for serialized property access.
					var serialized = new SerializedObject(asset);
					var iterator = serialized.GetIterator();

					// Walk every property and swap source references for their generated copies.
					while (iterator.Next(true))
						if (iterator.propertyType == SerializedPropertyType.ObjectReference &&
							iterator.objectReferenceValue != null &&
							map.TryGetValue(iterator.objectReferenceValue, out var replacement))
							iterator.objectReferenceValue = replacement;

					// Save the rewritten references without recording an undo step.
					serialized.ApplyModifiedPropertiesWithoutUndo();
					EditorUtility.SetDirty(asset);
				}

				// Delete generated .asset files that no longer correspond to a manifest entry.
				foreach (var guid in AssetDatabase.FindAssets("", new[] { Output }))
				{
					// Resolve the GUID to its asset path.
					var path = AssetDatabase.GUIDToAssetPath(guid);

					// Remove stale generated assets that are not in the current manifest.
					if (path.EndsWith(".asset", StringComparison.Ordinal) && !paths.Contains(path))
						AssetDatabase.DeleteAsset(path);
				}

				// Save all asset changes to disk.
				AssetDatabase.SaveAssets();

				// Report how many assets were generated.
				Debug.Log("Generated " + map.Count +
						  " JSON-owned gameplay assets. Runtime mods still load external data/ JSON.");
			}
		}
	}
}