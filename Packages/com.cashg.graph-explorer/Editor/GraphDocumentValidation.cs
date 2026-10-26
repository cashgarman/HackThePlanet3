using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Cashg.GraphExplorer.Editor
{
	/// <summary>Represents graph document validation.</summary>
	public static class GraphDocumentValidation
	{
		/// <summary>Opens a JSON file picker and validates the selected semantic graph document.</summary>
		[MenuItem("Graph Explorer/Validate semantic graph JSON")]
		public static void ValidateFile()
		{
			// Select the document to validate.
			var path = EditorUtility.OpenFilePanel("Graph document", "", "json");
			if (string.IsNullOrEmpty(path)) return;

			// Parse the document and report validation failures in the editor console.
			try
			{
				var graph = GraphJson.Parse(File.ReadAllText(path));
				Debug.Log("Graph document valid: " + graph.DatasetId + "; " + graph.Nodes.Count + " nodes, " +
						  graph.Edges.Count + " edges.");
			}
			catch (Exception error)
			{
				Debug.LogError("Graph document invalid: " + error.Message);
			}
		}
	}
}
