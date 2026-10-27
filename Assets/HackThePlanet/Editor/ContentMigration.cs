using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cashg.GraphExplorer;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HackThePlanet.Editor
{
	/// <summary>One-time export of legacy gameplay assets into authoritative JSON under the data directory.</summary>
	public static class ContentMigration
	{
		/// <summary>Exports validated JSON definitions from Resources gameplay assets when data does not yet exist.</summary>
		/// <exception cref="InvalidOperationException">The data directory already exists.</exception>
		/// <exception cref="InvalidDataException">A round-trip reload does not match exported JSON.</exception>
		[MenuItem("Hack The Planet/JSON/Export Original Assets (once)")]
		public static void Export()
		{
			// Read the authoritative data directory path.
			var directory = GameData.DirectoryPath;

			// Refuse to overwrite data that already exists.
			if (Directory.Exists(directory))
				throw new InvalidOperationException("data already exists; export refuses to overwrite authored JSON.");

			// Load and validate the legacy root game definition.
			var root = Resources.Load<GameDefinition>("HackThePlanet/Gameplay/Game");
			GameDefinitionValidator.ThrowIfInvalid(root);

			// Create the manifest entry list and the expected file map.
			var entries = new List<ContentEntry>();
			var expected = new Dictionary<string, string>();

			// Local helper that encodes one object and registers its manifest entry.
			void Add(ScriptableObject obj, string category, string id)
			{
				// Derive the category folder and the relative file path.
				var folder = category.ToLowerInvariant().Replace(' ', '-');
				var file = folder + "/" + id + ".json";

				// Create the manifest entry and register it.
				var entry = new ContentEntry
					{ key = Guid.NewGuid().ToString("N"), category = category, type = obj.GetType().Name, file = file };
				entries.Add(entry);

				// Store the encoded JSON under its file path.
				expected[file] = ContentJson.Encode(obj);
			}

			// Export each gameplay definition under its matching category.
			foreach (var definition in GameDefinitionValidator.Definitions(root))
				Add(definition, ContentJson.Categories.First(c => ContentJson.CategoryType(c) == definition.GetType()),
					definition.id);

			// Export the gameplay text palette.
			Add(Resources.Load<GameplayTextPalette>("HackThePlanet/GameplayTextPalette"), "Text Palette",
				"text-palette");

			// Export the research graph visual settings.
			Add(GraphResources.Load<GraphVisualSettings>("GraphVisualSettings"), "Graph Visuals", "research");

			// Create a temporary copy of the graph visual settings for the network visuals.
			var network = Object.Instantiate(GraphResources.Load<GraphVisualSettings>("GraphVisualSettings"));

			// Apply the network visual tuning values.
			network.ShadowBrightness = .7f;
			network.SurfaceEmission = .24f;
			network.BloomIntensity = .04f;
			network.OutlinePixels = 1;
			network.OutlineOpacity = .8f;
			network.SelectionGlowIntensity = .6f;
			network.SelectionRimIntensity = .3f;

			// Export the network visuals and destroy the temporary copy.
			Add(network, "Network Visuals", "network");
			Object.DestroyImmediate(network);

			// Export the graph node label settings.
			Add(GraphResources.Load<GraphNodeLabelSettings>("GraphNodeLabelSettings"), "Graph Labels", "labels");

			// Encode the manifest that lists every exported entry.
			expected["manifest.json"] = ContentJson.Encode(new ContentManifest { entries = entries.ToArray() });

			// Validate before creating the authoritative directory.
			using (var loaded = ContentJson.Load(directory, expected))
			{
				// Compare each reloaded object against its exported JSON.
				foreach (var entry in entries)
					if (ContentJson.Encode(loaded.Objects[entry.key]) != expected[entry.file])
						throw new InvalidDataException("Migration round-trip mismatch: " + entry.file);
			}

			// Write every validated file under the data directory.
			foreach (var file in expected)
			{
				// Resolve the safe file path, create its folder, and write the JSON.
				var path = ContentJson.SafePath(directory, file.Key);
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.WriteAllText(path, file.Value);
			}

			// Write the migration pass report under Artifacts.
			Directory.CreateDirectory("Artifacts");
			File.WriteAllText("Artifacts/json-migration.txt",
							  "PASS / " + entries.Count +
							  " definitions exported and compared field-for-field after reload.\n");

			// Log the number of exported definitions.
			Debug.Log("JSON migration passed: " + entries.Count + " definitions.");
		}

		/// <summary>Loads and validates JSON from <see cref="GameData.DirectoryPath" /> without modifying files.</summary>
		[MenuItem("Hack The Planet/JSON/Validate Data")]
		public static void Validate()
		{
			// Load the authoritative JSON and validate it without writing files.
			using (var bundle = ContentJson.Load(GameData.DirectoryPath))
			{
			}

			// Report that validation passed.
			Debug.Log("JSON content validation passed.");
		}
	}

	/// <summary>Player build hook that snapshots validated JSON beside the built executable.</summary>
	[NoAutoStaticsCleanup]
	public sealed class ContentBuildPipeline : IPreprocessBuildWithReport, IPostprocessBuildWithReport
	{
		// Holds the validated JSON snapshot captured before the build and consumed after it.
		private static Dictionary<string, string> validated;

		/// <summary>Stages validated JSON next to the build output and atomically replaces any prior data folder.</summary>
		/// <param name="report">Completed build report supplying the output directory.</param>
		/// <exception cref="BuildFailedException">Staging failed or the validated snapshot was missing.</exception>
		public void OnPostprocessBuild(BuildReport report)
		{
			// Fail when the preprocess snapshot was not captured.
			if (validated == null)
				throw new BuildFailedException("Missing validated JSON build snapshot.");

			// Resolve the build output folder and the staging and destination paths.
			var output = Path.GetFullPath(Path.GetDirectoryName(report.summary.outputPath));
			string destination = Path.Combine(output, "data"),
				   staged = Path.Combine(output, "data-staging-" + Guid.NewGuid().ToString("N"));

			// Stage the JSON and publish it, cleaning up on failure.
			try
			{
				// Only our generated sibling data directory may be replaced; never target source data.
				if (string.Equals(destination, Path.GetFullPath(GameData.DirectoryPath),
								  StringComparison.OrdinalIgnoreCase))
					throw new IOException("Build output cannot replace project data.");

				// Write each validated snapshot file into the staging folder.
				foreach (var file in validated)
				{
					// Resolve the staged path, create its folder, and write the snapshot text.
					var path = ContentJson.SafePath(staged, file.Key);
					Directory.CreateDirectory(Path.GetDirectoryName(path));
					File.WriteAllText(path, file.Value);
				}

				// Verify the staged JSON loads before publishing it.
				using (var check = ContentJson.Load(staged))
				{
				}

				// Replace any previous data folder with the staged copy.
				if (Directory.Exists(destination))
					Directory.Delete(destination, true);
				Directory.Move(staged, destination);
			}
			catch (Exception ex)
			{
				// Wrap the failure in a build error that keeps the original message.
				throw new BuildFailedException("Could not package JSON data: " + ex.Message);
			}
			finally
			{
				// Clear the snapshot so it is never reused.
				validated = null;
			}
		}

		/// <summary>Gets the preprocess callback order relative to other build hooks (runs after lower values).</summary>
		public int callbackOrder => 10;

		/// <summary>Captures a validated copy of project JSON before the player build proceeds.</summary>
		/// <param name="report">Build report for the upcoming player build.</param>
		/// <exception cref="BuildFailedException">Project JSON failed to load or validate.</exception>
		public void OnPreprocessBuild(BuildReport report)
		{
			// Load the project JSON and capture its validated file map.
			try
			{
				using (var bundle = ContentJson.Load(GameData.DirectoryPath))
				{
					// Copy the validated files into the snapshot.
					validated = new Dictionary<string, string>(bundle.Files);
				}
			}
			catch (Exception ex)
			{
				// Convert any load or validation failure into a build failure.
				throw new BuildFailedException(ex.Message);
			}
		}
	}
}