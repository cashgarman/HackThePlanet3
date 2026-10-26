using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HackThePlanet.Editor
{
    [CustomPropertyDrawer(typeof(Number),true)]
    [CustomPropertyDrawer(typeof(Condition),true)]
    [CustomPropertyDrawer(typeof(Effect),true)]
    public sealed class GameplayRuleDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property,GUIContent label)
        {
            float height=EditorGUIUtility.singleLineHeight;
            if(property.isExpanded&&property.managedReferenceValue!=null)
            {
                var child=property.Copy();var end=child.GetEndProperty();bool enter=true;
                while(child.NextVisible(enter)&&!SerializedProperty.EqualContents(child,end)){height+=EditorGUI.GetPropertyHeight(child,true)+EditorGUIUtility.standardVerticalSpacing;enter=false;}
            }
            return height;
        }
        public override void OnGUI(Rect position,SerializedProperty property,GUIContent label)
        {
            EditorGUI.BeginProperty(position,label,property);
            var line=new Rect(position.x,position.y,position.width,EditorGUIUtility.singleLineHeight);
            property.isExpanded=EditorGUI.Foldout(new Rect(line.x,line.y,line.width*.5f,line.height),property.isExpanded,label,true);
            var type=property.managedReferenceValue?.GetType();
            if(GUI.Button(new Rect(line.x+line.width*.5f,line.y,line.width*.5f,line.height),type==null?"Choose rule…":ObjectNames.NicifyVariableName(type.Name),EditorStyles.popup))
            {
                Type baseType=typeof(Number).IsAssignableFrom(fieldInfo.FieldType)?typeof(Number):typeof(Condition).IsAssignableFrom(fieldInfo.FieldType)?typeof(Condition):typeof(Effect);
                if(fieldInfo.FieldType.IsArray)baseType=fieldInfo.FieldType.GetElementType();
                var target=property.serializedObject.targetObject;string path=property.propertyPath;var menu=new GenericMenu();
                foreach(var choice in TypeCache.GetTypesDerivedFrom(baseType).Where(t=>!t.IsAbstract&&t.IsSerializable).OrderBy(t=>t.Name))
                {
                    var selected=choice;
                    menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(choice.Name)),choice==type,()=>
                    {
                        var so=new SerializedObject(target);so.Update();var p=so.FindProperty(path);p.managedReferenceValue=Activator.CreateInstance(selected);p.isExpanded=true;so.ApplyModifiedProperties();
                    });
                }
                menu.ShowAsContext();
            }
            if(property.isExpanded&&property.managedReferenceValue!=null)
            {
                EditorGUI.indentLevel++;float y=line.yMax+EditorGUIUtility.standardVerticalSpacing;var child=property.Copy();var end=child.GetEndProperty();bool enter=true;
                while(child.NextVisible(enter)&&!SerializedProperty.EqualContents(child,end))
                {
                    float h=EditorGUI.GetPropertyHeight(child,true);EditorGUI.PropertyField(new Rect(position.x,y,position.width,h),child,true);y+=h+EditorGUIUtility.standardVerticalSpacing;enter=false;
                }
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
        }
    }
    [CustomEditor(typeof(GameplayDefinition),true)]
    public sealed class GameplayDefinitionInspector : UnityEditor.Editor
    {
        private string[] errors=Array.Empty<string>();
        private void OnEnable()=>RefreshErrors();
        private void RefreshErrors()
        {
            var root=AssetDatabase.LoadAssetAtPath<GameDefinition>(DefaultGameplayAssets.Folder+"/Game.asset");
            errors=root==null?Array.Empty<string>():GameDefinitionValidator.Validate(root).ToArray();
        }
        public override void OnInspectorGUI()
        {
            if(DrawDefaultInspector()){GameCatalog.InvalidateDefault();RefreshErrors();}
            if(GUILayout.Button("Validate Gameplay Catalog"))RefreshErrors();
            foreach(var error in errors.Take(12))EditorGUILayout.HelpBox(error,MessageType.Error);
            if(errors.Length>12)EditorGUILayout.HelpBox((errors.Length-12)+" more catalog errors.",MessageType.Error);
        }
        [MenuItem("Hack The Planet/Validate Gameplay Assets")]
        public static void ValidateAssets()
        {
            GameDefinitionValidator.ThrowIfInvalid(Resources.Load<GameDefinition>("HackThePlanet/Gameplay/Game"));Debug.Log("Gameplay asset validation passed.");
        }
    }
    public sealed class GameplayBuildValidation : IPreprocessBuildWithReport
    {
        public int callbackOrder=>0;
        public void OnPreprocessBuild(BuildReport report)
        {
            try{GameDefinitionValidator.ThrowIfInvalid(Resources.Load<GameDefinition>("HackThePlanet/Gameplay/Game"));}
            catch(Exception ex){throw new BuildFailedException(ex.Message);}
        }
    }
    public sealed class GameplayAssetChanges : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported,string[] deleted,string[] moved,string[] previous)
        {
            if(imported.Concat(deleted).Concat(moved).Concat(previous).Any(path=>path.StartsWith(DefaultGameplayAssets.Folder,StringComparison.Ordinal)&&path.EndsWith(".asset",StringComparison.Ordinal)))GameCatalog.InvalidateDefault();
        }
    }
}
