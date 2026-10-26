using UnityEditor;

namespace Cashg.GraphExplorer.Editor
{
	/// <summary>Graph meshes receive vertex colors at startup, so CPU access is required in players.</summary>
	public sealed class GraphNodeModelImporter : AssetPostprocessor
	{
		private void OnPreprocessModel()
		{
			// Restrict import overrides to the package's graph node models.
			if (!assetPath.StartsWith(
					"Packages/com.cashg.graph-explorer/Runtime/Resources/CashgGraphExplorer/GraphNodeModels/")) return;

			// Keep model data available for runtime coloring without importing unused features.
			var importer = (ModelImporter)assetImporter;
			importer.isReadable = true;
			importer.globalScale = 1f;
			importer.materialImportMode = ModelImporterMaterialImportMode.None;
			importer.importNormals = ModelImporterNormals.Import;
			importer.importTangents = ModelImporterTangents.None;
			importer.importAnimation = false;
			importer.addCollider = false;
		}
	}
}
