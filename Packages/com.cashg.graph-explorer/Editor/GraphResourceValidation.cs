using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Cashg.GraphExplorer.Editor
{
	/// <summary>Represents graph resource validation.</summary>
	public static class GraphResourceValidation
	{
		/// <summary>Checks that the package's required settings, styles, shaders, and materials are available.</summary>
		[MenuItem("Graph Explorer/Validate package resources")]
		public static void Validate()
		{
			// Require the settings and styles used by the graph interface.
			Require(GraphResources.Load<PanelSettings>("GraphPanelSettings"), "serialized panel/text resources");
			Require(GraphResources.Load<GraphVisualSettings>("GraphVisualSettings"), "visual settings");
			Require(GraphResources.Load<GraphNodeLabelSettings>("GraphNodeLabelSettings"), "label settings");
			Require(GraphResources.Load<StyleSheet>("GraphExplorer"), "explorer stylesheet");
			Require(GraphResources.Load<StyleSheet>("GraphShell"), "shell stylesheet");
			foreach (var name in new[]
						 { "GraphNodeToon", "GraphNodeMask", "GraphNodePost", "GraphEdge", "GraphSky", "GraphFog" })
			{
				// Confirm each required shader loads and compiles successfully.
				var shader = GraphResources.Load<Shader>(name);
				Require(shader, name);
				if (ShaderUtil.ShaderHasError(shader))
					throw new InvalidOperationException("Shader import failed: " + name);
			}

			// Confirm the materials used by the renderer are present.
			foreach (var name in new[]
						 { "GraphNodeMaterial", "GraphNodeMaskMaterial", "GraphSkyMaterial", "GraphFogMaterial" })
				Require(GraphResources.Load<Material>(name), name);
		}

		private static void Require(Object asset, string description)
		{
			if (asset == null)
				throw new InvalidOperationException("Missing Graph Explorer package resource: " + description);
		}
	}
}