using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HackThePlanet.Editor
{
	/// <summary>Foldout property drawer for polymorphic gameplay rule references.</summary>
	[CustomPropertyDrawer(typeof(Number), true)]
	[CustomPropertyDrawer(typeof(Condition), true)]
	[CustomPropertyDrawer(typeof(Effect), true)]
	public sealed class GameplayRuleDrawer : PropertyDrawer
	{
		/// <summary>Calculates inspector height including expanded rule fields.</summary>
		/// <param name="property">Serialized rule reference.</param>
		/// <param name="label">Inspector label.</param>
		/// <returns>Total property height in pixels.</returns>
		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			// Start with the height of the foldout line.
			var height = EditorGUIUtility.singleLineHeight;

			// Add the height of the visible child fields when the rule is expanded.
			if (property.isExpanded && property.managedReferenceValue != null)
			{
				// Walk the rule's visible child properties.
				var child = property.Copy();
				var end = child.GetEndProperty();
				var enter = true;

				// Add each child's height plus the spacing after it.
				while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
				{
					height += EditorGUI.GetPropertyHeight(child, true) + EditorGUIUtility.standardVerticalSpacing;
					enter = false;
				}
			}

			return height;
		}

		/// <summary>Draws a type picker and optional expanded fields for the active rule instance.</summary>
		/// <param name="position">Inspector rect.</param>
		/// <param name="property">Serialized rule reference.</param>
		/// <param name="label">Inspector label.</param>
		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			// Begin the property scope so the header and child fields are drawn together.
			EditorGUI.BeginProperty(position, label, property);

			// Lay out the header line with the foldout and the current rule type.
			var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
			property.isExpanded = EditorGUI.Foldout(new Rect(line.x, line.y, line.width * .5f, line.height),
													property.isExpanded, label, true);
			var type = property.managedReferenceValue?.GetType();

			// Draw the type picker button on the right half of the header.
			if (GUI.Button(new Rect(line.x + line.width * .5f, line.y, line.width * .5f, line.height),
						   type == null ? "Choose rule…" : ObjectNames.NicifyVariableName(type.Name),
						   EditorStyles.popup))
			{
				// Pick the base type that candidate rules must derive from.
				var baseType = typeof(Number).IsAssignableFrom(fieldInfo.FieldType) ? typeof(Number) :
					typeof(Condition).IsAssignableFrom(fieldInfo.FieldType) ? typeof(Condition) : typeof(Effect);

				// Use the element type when the field is an array.
				if (fieldInfo.FieldType.IsArray)
					baseType = fieldInfo.FieldType.GetElementType();

				// Capture the serialized target and property path, and create the menu.
				var target = property.serializedObject.targetObject;
				var path = property.propertyPath;
				var menu = new GenericMenu();

				// Add each serializable, concrete rule type to the menu.
				foreach (var choice in TypeCache.GetTypesDerivedFrom(baseType)
												.Where(t => !t.IsAbstract && t.IsSerializable).OrderBy(t => t.Name))
				{
					// Add a menu entry that creates this rule type when chosen.
					var selected = choice;
					menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(choice.Name)), choice == type, () =>
					{
						// Open a fresh serialized copy of the target so the change is applied safely.
						var so = new SerializedObject(target);
						so.Update();
						var p = so.FindProperty(path);

						// Replace the rule with a new instance of the selected type and expand it.
						p.managedReferenceValue = Activator.CreateInstance(selected);
						p.isExpanded = true;

						// Commit the change to the serialized object.
						so.ApplyModifiedProperties();
					});
				}

				// Show the rule type menu at the cursor.
				menu.ShowAsContext();
			}

			// Draw the expanded child fields below the header line.
			if (property.isExpanded && property.managedReferenceValue != null)
			{
				// Indent the child fields under the header.
				EditorGUI.indentLevel++;

				// Start drawing children just below the header line.
				var y = line.yMax + EditorGUIUtility.standardVerticalSpacing;
				var child = property.Copy();
				var end = child.GetEndProperty();
				var enter = true;

				// Draw each visible child field and advance the vertical cursor.
				while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
				{
					// Measure the child and draw it at the current y position.
					var h = EditorGUI.GetPropertyHeight(child, true);
					EditorGUI.PropertyField(new Rect(position.x, y, position.width, h), child, true);

					// Advance the cursor past this field and mark later children as non-entry.
					y += h + EditorGUIUtility.standardVerticalSpacing;
					enter = false;
				}

				// Restore the indent level.
				EditorGUI.indentLevel--;
			}

			// End the property scope opened at the start of the method.
			EditorGUI.EndProperty();
		}
	}

	/// <summary>Read-only inspector for gameplay definitions with JSON catalog validation.</summary>
	[CustomEditor(typeof(GameplayDefinition), true)]
	public sealed class GameplayDefinitionInspector : UnityEditor.Editor
	{
		// Holds the latest JSON catalog validation errors shown in the inspector.
		private string[] errors = Array.Empty<string>();

		// Reloads catalog validation errors when the inspector is enabled.
		private void OnEnable()
		{
			RefreshErrors();
		}

		// Reloads the JSON catalog and stores any load failure message for display.
		private void RefreshErrors()
		{
			// Reload the JSON catalog and record any load failure as an error.
			try
			{
				// Load the catalog; a clean load clears any previous errors.
				using (var data = ContentJson.Load(GameData.DirectoryPath))
				{
					// Clear the error list because the catalog loaded successfully.
					errors = Array.Empty<string>();
				}
			}
			catch (Exception ex)
			{
				// Keep the failure message so the inspector can show it.
				errors = new[] { ex.Message };
			}
		}

		/// <summary>Draws authoring guidance, a disabled default inspector, and optional catalog errors.</summary>
		public override void OnInspectorGUI()
		{
			// Determine whether this asset was generated from JSON.
			var generated = AssetDatabase.GetAssetPath(target)
										 .StartsWith(GameplayAssetGenerator.Output + "/", StringComparison.Ordinal);

			// Show guidance matching the asset's origin.
			EditorGUILayout.HelpBox(
				generated
					? "Generated from authoritative JSON. Edit in the Content Editor, then Generate Gameplay Assets."
					: "Historical asset. Authoritative content is JSON in /data.", MessageType.Info);

			// Offer a shortcut to the Content Editor.
			if (GUILayout.Button("Open Content Editor"))
				ContentEditorWindow.Open();

			// Draw the default inspector as read-only.
			using (new EditorGUI.DisabledScope(true))
			{
				DrawDefaultInspector();
			}

			// Offer a manual catalog validation that refreshes the error list below.
			if (GUILayout.Button("Validate Gameplay Catalog"))
				RefreshErrors();

			// List up to 12 catalog errors.
			foreach (var error in errors.Take(12))
				EditorGUILayout.HelpBox(error, MessageType.Error);

			// Summarize how many more errors exist beyond the 12 shown.
			if (errors.Length > 12)
				EditorGUILayout.HelpBox(errors.Length - 12 + " more catalog errors.", MessageType.Error);
		}

		/// <summary>Menu shortcut that validates authoritative JSON via <see cref="ContentMigration.Validate" />.</summary>
		[MenuItem("Hack The Planet/Validate Gameplay Assets")]
		public static void ValidateAssets()
		{
			ContentMigration.Validate();
		}
	}

	/// <summary>Regenerates JSON-owned gameplay assets before player builds.</summary>
	public sealed class GameplayBuildValidation : IPreprocessBuildWithReport
	{
		/// <summary>Gets the preprocess callback order (runs before higher values).</summary>
		public int callbackOrder => 0;

		/// <summary>Generates gameplay mirror assets from JSON prior to building.</summary>
		/// <param name="report">Upcoming build report.</param>
		/// <exception cref="BuildFailedException">Asset generation failed.</exception>
		public void OnPreprocessBuild(BuildReport report)
		{
			// Regenerate the gameplay mirror assets from JSON before building.
			try
			{
				GameplayAssetGenerator.Generate();
			}
			catch (Exception ex)
			{
				// Abort the build if asset generation fails.
				throw new BuildFailedException(ex.Message);
			}
		}
	}

	/// <summary>Legacy asset postprocessor retained for historical gameplay asset paths.</summary>
	public sealed class GameplayAssetChanges : AssetPostprocessor
	{
		// Retained asset postprocessor hook; historical gameplay paths no longer invalidate snapshots.
		private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved,
												   string[] previous)
		{
			// Historical assets no longer invalidate a launch-loaded content snapshot.
		}
	}

	/// <summary>Editor utilities for seeding the default gameplay text palette asset.</summary>
	public static class GameplayTextPaletteTools
	{
		// Asset path for the default gameplay text palette seeded by InitializePalette.
		private const string Path = "Assets/HackThePlanet/Resources/HackThePlanet/GameplayTextPalette.asset";

		/// <summary>Creates or updates the palette asset with default terms and reloads runtime styling.</summary>
		public static void InitializePalette()
		{
			// Load the existing palette asset from its fixed path.
			var palette = AssetDatabase.LoadAssetAtPath<GameplayTextPalette>(Path);

			// Create the palette asset when it does not exist yet.
			if (palette == null)
			{
				// Create a new palette asset at the fixed path.
				palette = ScriptableObject.CreateInstance<GameplayTextPalette>();
				AssetDatabase.CreateAsset(palette, Path);
			}

			// Replace the palette terms with the defaults.
			palette.terms = GameplayTextPalette.DefaultTerms();

			// Mark the asset dirty and save it.
			EditorUtility.SetDirty(palette);
			AssetDatabase.SaveAssets();

			// Reload runtime styling from the updated palette.
			GameplayRichText.ReloadPalette();
		}
	}
}