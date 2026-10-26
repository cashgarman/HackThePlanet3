using Cashg.GraphExplorer;
using UnityEditor;

namespace HackThePlanet.Editor
{
    public abstract class HistoricalContentInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("Historical asset. JSON in /data is authoritative; use Hack The Planet > Content Editor. Runtime instances are populated at launch.", MessageType.Info);
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
