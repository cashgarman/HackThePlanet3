using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Cashg.GraphExplorer
{
	/// <summary>One reusable low-poly mesh per entity kind, shared by all its instances.</summary>
	public static class GraphNodeModels
	{
		/// <summary>Creates an owned mesh copy for the requested node shape.</summary>
		/// <param name="shapeId">Shape identifier, or "default" for the standard gem.</param>
		/// <param name="resources">Optional resource overrides.</param>
		/// <returns>A mesh instance for the renderer to own and dispose.</returns>
		public static Mesh Create(string shapeId, GraphResourceConfiguration resources = null)
		{
			// Resolve the requested prefab, using Gem as the default and missing-shape fallback.
			var asset = (resources ?? new GraphResourceConfiguration()).Load<GameObject>(
				"GraphNodeModels/" + (shapeId == "default" ? "Gem" : shapeId));
			if (asset == null)
				asset = (resources ?? new GraphResourceConfiguration()).Load<GameObject>("GraphNodeModels/Gem");
			var filter = asset != null ? asset.GetComponentInChildren<MeshFilter>() : null;
			if (filter == null || filter.sharedMesh == null)
				throw new InvalidOperationException("Missing graph node model: " + shapeId);
			// The renderer owns this copy and disposes it; the imported asset stays intact.
			var mesh = Object.Instantiate(filter.sharedMesh);
			mesh.name = "Graph " + shapeId;
			return mesh;
		}
	}
}
