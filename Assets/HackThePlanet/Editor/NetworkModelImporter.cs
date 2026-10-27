using UnityEditor;

namespace HackThePlanet.Editor
{
	/// <summary>Import settings for graph node meshes under Resources node models.</summary>
	public sealed class NetworkModelImporter : AssetPostprocessor
	{
		// Applies node-model import settings before Unity imports meshes under NodeModels.
		private void OnPreprocessModel()
		{
			// Skip assets outside the node models folder.
			if (!assetPath.StartsWith("Assets/HackThePlanet/Resources/HackThePlanet/NodeModels/"))
				return;

			// Get the model importer for this asset.
			var importer = (ModelImporter)assetImporter;

			// Read the mesh, skip materials and animation, and disable colliders and compression.
			importer.isReadable = true;
			importer.materialImportMode = ModelImporterMaterialImportMode.None;
			importer.importAnimation = false;
			importer.addCollider = false;
			importer.meshCompression = ModelImporterMeshCompression.Off;
		}
	}
}