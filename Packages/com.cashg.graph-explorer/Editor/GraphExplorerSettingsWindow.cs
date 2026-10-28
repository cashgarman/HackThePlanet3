using UnityEditor;
using UnityEngine;

namespace Cashg.GraphExplorer.Editor
{
	/// <summary>Editor window for inspecting and editing the Graph Explorer package defaults.</summary>
	public sealed class GraphExplorerSettingsWindow : EditorWindow
	{
		private Vector2 scroll;
		private GraphVisualSettings settings;
		private SerializedObject serializedSettings;

		/// <summary>Opens the Graph Explorer package settings window.</summary>
		[MenuItem("Graph Explorer/Settings", priority = 0)]
		public static void Open()
		{
			var window = GetWindow<GraphExplorerSettingsWindow>();
			window.titleContent = new GUIContent("Graph Explorer Settings");
			window.minSize = new Vector2(420, 360);
			window.Show();
			window.Focus();
		}

		private void OnEnable()
		{
			minSize = new Vector2(420, 360);
			LoadSettings();
		}

		private void OnDisable()
		{
			serializedSettings?.ApplyModifiedProperties();
			serializedSettings = null;
			settings = null;
		}

		private void OnGUI()
		{
			if (settings == null || serializedSettings == null)
				LoadSettings();

			EditorGUILayout.Space(8);
			using (new EditorGUILayout.HorizontalScope())
			{
				EditorGUILayout.LabelField("Graph Explorer", EditorStyles.boldLabel);
				GUILayout.FlexibleSpace();
				if (settings != null && GUILayout.Button("Ping Asset", GUILayout.Width(90)))
					EditorGUIUtility.PingObject(settings);
			}

			EditorGUILayout.HelpBox(
				"These are the package defaults used when a host does not provide an override. " +
				"Host-specific JSON settings can still replace them at runtime.",
				MessageType.Info);

			if (settings == null || serializedSettings == null)
			{
				EditorGUILayout.HelpBox(
					"The Graph Explorer visual settings resource could not be loaded. " +
					"Use Graph Explorer > Validate package resources to diagnose the package installation.",
					MessageType.Error);
				return;
			}

			var assetPath = AssetDatabase.GetAssetPath(settings);
			if (!string.IsNullOrEmpty(assetPath))
				EditorGUILayout.LabelField("Asset", assetPath, EditorStyles.miniLabel);

			serializedSettings.Update();
			scroll = EditorGUILayout.BeginScrollView(scroll);
			var property = serializedSettings.GetIterator();
			var enterChildren = true;
			while (property.NextVisible(enterChildren))
			{
				enterChildren = false;
				if (property.name == "m_Script")
					continue;

				EditorGUILayout.PropertyField(property, true);
			}
			EditorGUILayout.EndScrollView();

			if (serializedSettings.ApplyModifiedProperties())
			{
				EditorUtility.SetDirty(settings);
				AssetDatabase.SaveAssets();
			}
		}

		private void LoadSettings()
		{
			settings = GraphResources.Load<GraphVisualSettings>("GraphVisualSettings");
			serializedSettings = settings != null ? new SerializedObject(settings) : null;
		}
	}
}
