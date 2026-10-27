using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace HackThePlanet.Editor
{
    public static class GameplayAssetGenerator
    {
        public const string Output="Assets/HackThePlanet/GeneratedGameplay";
        [MenuItem("Hack The Planet/JSON/Generate Gameplay Assets")]
        public static void Generate()=>GenerateFrom(GameData.DirectoryPath);
        public static void GenerateFrom(string directory)
        {
            using(var bundle=ContentJson.Load(directory))
            {
                Directory.CreateDirectory(Output);AssetDatabase.Refresh();
                var map=new Dictionary<Object,Object>();var paths=new HashSet<string>();
                // Immutable manifest keys preserve .meta GUIDs across ID, filename and title changes.
                foreach(var entry in bundle.Manifest.entries)
                {
                    string path=Output+"/"+entry.key+".asset";paths.Add(path);var source=bundle.Objects[entry.key];
                    var asset=AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                    if(asset!=null&&asset.GetType()!=source.GetType())throw new InvalidOperationException("Generated asset type changed for "+entry.key);
                    if(asset==null){asset=ScriptableObject.CreateInstance(source.GetType());AssetDatabase.CreateAsset(asset,path);}
                    EditorUtility.CopySerialized(source,asset);asset.hideFlags=HideFlags.None;asset.name=entry.key;map.Add(source,asset);
                }
                foreach(var asset in map.Values)
                {
                    var serialized=new SerializedObject(asset);var iterator=serialized.GetIterator();
                    while(iterator.Next(true))if(iterator.propertyType==SerializedPropertyType.ObjectReference&&iterator.objectReferenceValue!=null&&map.TryGetValue(iterator.objectReferenceValue,out var replacement))iterator.objectReferenceValue=replacement;
                    serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(asset);
                }
                foreach(string guid in AssetDatabase.FindAssets("",new[]{Output}))
                {
                    string path=AssetDatabase.GUIDToAssetPath(guid);if(path.EndsWith(".asset",StringComparison.Ordinal)&&!paths.Contains(path))AssetDatabase.DeleteAsset(path);
                }
                AssetDatabase.SaveAssets();Debug.Log("Generated "+map.Count+" JSON-owned gameplay assets. Runtime mods still load external data/ JSON.");
            }
        }
    }
}
