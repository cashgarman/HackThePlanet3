using UnityEditor;

namespace HackThePlanet.Editor
{
    public sealed class NetworkModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith("Assets/HackThePlanet/Resources/HackThePlanet/NodeModels/")) return;
            var importer = (ModelImporter)assetImporter;
            importer.isReadable = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = false;
            importer.addCollider = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
        }
    }
}
