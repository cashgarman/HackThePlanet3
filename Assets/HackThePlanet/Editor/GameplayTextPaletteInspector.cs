using Cashg.GraphExplorer;
using UnityEditor;
using UnityEngine;

namespace HackThePlanet.Editor
{
	/// <summary>Read-only inspector that directs authors to JSON and the Content Editor for historical assets.</summary>
	public abstract class HistoricalContentInspector : UnityEditor.Editor
	{
		/// <summary>Draws guidance and a disabled default inspector with a shortcut to the Content Editor.</summary>
		public override void OnInspectorGUI()
		{
			// Show guidance that points authors to the JSON source of truth.
			EditorGUILayout.HelpBox(
				AssetDatabase.GetAssetPath(target).StartsWith(GameplayAssetGenerator.Output + "/")
					? "Generated from authoritative JSON. Use Content Editor, save JSON, then Generate Gameplay Assets."
					: "Historical asset. JSON in /data is authoritative; use Content Editor.", MessageType.Info);

			// Offer a shortcut to the Content Editor.
			if (GUILayout.Button("Open Content Editor"))
				ContentEditorWindow.Open();

			// Draw the default inspector as read-only.
			using (new EditorGUI.DisabledScope(true))
			{
				DrawDefaultInspector();
			}
		}
	}

	/// <summary>Inspector for generated or historical <see cref="GameplayTextPalette" /> assets.</summary>
	[CustomEditor(typeof(GameplayTextPalette))]
	public sealed class GameplayTextPaletteInspector : HistoricalContentInspector
	{
	}

	/// <summary>Inspector for generated or historical graph visual settings assets.</summary>
	[CustomEditor(typeof(GraphVisualSettings))]
	public sealed class ContentGraphVisualInspector : HistoricalContentInspector
	{
	}

	/// <summary>Inspector for generated or historical graph node label settings assets.</summary>
	[CustomEditor(typeof(GraphNodeLabelSettings))]
	public sealed class ContentGraphLabelInspector : HistoricalContentInspector
	{
	}
}