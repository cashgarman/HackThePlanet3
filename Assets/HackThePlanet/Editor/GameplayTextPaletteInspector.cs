using Cashg.GraphExplorer;
using UnityEditor;

namespace HackThePlanet.Editor
{
    public abstract class HistoricalContentInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(AssetDatabase.GetAssetPath(target).StartsWith(GameplayAssetGenerator.Output+"/")?"Generated from authoritative JSON. Use Content Editor, save JSON, then Generate Gameplay Assets.":"Historical asset. JSON in /data is authoritative; use Content Editor.", MessageType.Info);
            if (UnityEngine.GUILayout.Button("Open Content Editor")) ContentEditorWindow.Open();
            using (new EditorGUI.DisabledScope(true)) DrawDefaultInspector();
        }
    }
    [CustomEditor(typeof(GameplayTextPalette))]
    public sealed class GameplayTextPaletteInspector : HistoricalContentInspector { }
    [CustomEditor(typeof(GraphVisualSettings))]
    public sealed class ContentGraphVisualInspector : HistoricalContentInspector { }
    [CustomEditor(typeof(GraphNodeLabelSettings))]
    public sealed class ContentGraphLabelInspector : HistoricalContentInspector { }
}
