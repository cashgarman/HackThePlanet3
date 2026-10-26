using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HackThePlanet.Editor
{
    [CustomEditor(typeof(GameplayTextPalette))]
    public sealed class GameplayTextPaletteInspector : UnityEditor.Editor
    {
        private const string PreviewSample =
            "FULL INTEL / Scanner\nDISCOVERED / NOT CONTROLLED / SECURITY 9\nDetection / ACTIVE SENSOR";

        private SerializedProperty terms;
        private string filter = "";

        private void OnEnable() => terms = serializedObject.FindProperty("terms");

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "Terms are matched case-insensitively; longer phrases beat shorter ones. " +
                "Enable Regex for patterns such as SECURITY \\d+.",
                MessageType.None);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add term", GUILayout.Width(90)))
                AddTerm();
            if (GUILayout.Button("Reset to defaults", GUILayout.Width(130)))
                ResetDefaults();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Filter", GUILayout.Width(36));
            filter = EditorGUILayout.TextField(filter, GUILayout.MinWidth(80), GUILayout.MaxWidth(220));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            DrawColumnHeader();

            int visible = 0;
            for (int i = 0; i < terms.arraySize; i++)
            {
                var element = terms.GetArrayElementAtIndex(i);
                var phrase = element.FindPropertyRelative("phrase").stringValue ?? "";
                if (!MatchesFilter(phrase)) continue;
                visible++;
                DrawTermRow(i, element);
            }

            if (visible == 0)
                EditorGUILayout.HelpBox(terms.arraySize == 0 ? "No terms yet. Add one or reset to defaults." : "No terms match the filter.", MessageType.Info);

            EditorGUILayout.Space(8);
            DrawPreview();

            if (serializedObject.hasModifiedProperties && serializedObject.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(target);
                GameplayRichText.ReloadPalette();
            }
        }

        private void DrawColumnHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("", GUILayout.Width(22));
            EditorGUILayout.LabelField("Phrase", EditorStyles.miniLabel, GUILayout.MinWidth(120));
            EditorGUILayout.LabelField("Color", EditorStyles.miniLabel, GUILayout.Width(54));
            EditorGUILayout.LabelField("Rx", EditorStyles.miniLabel, GUILayout.Width(22));
            EditorGUILayout.LabelField("Bold", EditorStyles.miniLabel, GUILayout.Width(30));
            EditorGUILayout.LabelField("", GUILayout.Width(44));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTermRow(int index, SerializedProperty element)
        {
            var phrase = element.FindPropertyRelative("phrase");
            var regex = element.FindPropertyRelative("regex");
            var color = element.FindPropertyRelative("color");
            var bold = element.FindPropertyRelative("bold");

            EditorGUILayout.BeginHorizontal();
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(18, 18, GUILayout.Width(18)), color.colorValue);

            phrase.stringValue = EditorGUILayout.TextField(phrase.stringValue, GUILayout.MinWidth(120));
            color.colorValue = EditorGUILayout.ColorField(GUIContent.none, color.colorValue, false, false, false, GUILayout.Width(54));
            regex.boolValue = EditorGUILayout.Toggle(regex.boolValue, GUILayout.Width(22));
            bold.boolValue = EditorGUILayout.Toggle(bold.boolValue, GUILayout.Width(30));

            using (new EditorGUI.DisabledScope(index <= 0))
            {
                if (GUILayout.Button("↑", EditorStyles.miniButtonLeft, GUILayout.Width(20)))
                    terms.MoveArrayElement(index, index - 1);
            }
            using (new EditorGUI.DisabledScope(index >= terms.arraySize - 1))
            {
                if (GUILayout.Button("↓", EditorStyles.miniButtonMid, GUILayout.Width(20)))
                    terms.MoveArrayElement(index, index + 1);
            }
            if (GUILayout.Button("×", EditorStyles.miniButtonRight, GUILayout.Width(20)))
            {
                if (EditorUtility.DisplayDialog("Remove term", "Remove \"" + (phrase.stringValue ?? "") + "\"?", "Remove", "Cancel"))
                    terms.DeleteArrayElementAtIndex(index);
            }

            EditorGUILayout.EndHorizontal();

            if (string.IsNullOrWhiteSpace(phrase.stringValue))
                EditorGUILayout.HelpBox("Phrase is empty; this entry is ignored at runtime.", MessageType.Warning);
        }

        private void DrawPreview()
        {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            if (serializedObject.hasModifiedProperties)
                serializedObject.ApplyModifiedProperties();
            GameplayRichText.Palette = (GameplayTextPalette)target;
            var rich = GameplayRichText.Format(PreviewSample);
            var style = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                richText = true,
                padding = new RectOffset(8, 8, 6, 6),
                normal = { background = EditorGUIUtility.isProSkin ? MakeTex(2, 2, new Color(0.12f, 0.12f, 0.12f)) : MakeTex(2, 2, new Color(0.85f, 0.85f, 0.85f)) }
            };
            EditorGUILayout.LabelField(rich, style);
        }

        private void AddTerm()
        {
            terms.InsertArrayElementAtIndex(terms.arraySize);
            var element = terms.GetArrayElementAtIndex(terms.arraySize - 1);
            element.FindPropertyRelative("phrase").stringValue = "NEW TERM";
            element.FindPropertyRelative("regex").boolValue = false;
            element.FindPropertyRelative("bold").boolValue = true;
            element.FindPropertyRelative("color").colorValue = Palette.Cyan;
        }

        private void ResetDefaults()
        {
            if (!EditorUtility.DisplayDialog("Reset gameplay text palette", "Replace all terms with the built-in defaults?", "Reset", "Cancel"))
                return;
            ((GameplayTextPalette)target).terms = GameplayTextPalette.DefaultTerms();
            EditorUtility.SetDirty(target);
            serializedObject.Update();
            GameplayRichText.ReloadPalette();
        }

        private bool MatchesFilter(string phrase) =>
            string.IsNullOrEmpty(filter) || phrase.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0;

        private static Texture2D MakeTex(int width, int height, Color color)
        {
            var pixels = Enumerable.Repeat(color, width * height).ToArray();
            var texture = new Texture2D(width, height) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
