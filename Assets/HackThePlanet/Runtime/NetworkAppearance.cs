using System;
using Cashg.GraphExplorer;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HackThePlanet
{
	/// <summary>Builds graph presentation resources and visual settings for network maps.</summary>
	public static class NetworkAppearance
	{
		/// <summary>Loads node models and clones graph visual settings for runtime use.</summary>
		/// <param name="settings">
		///     Receives the instantiated <see cref="GraphVisualSettings" /> registered on the returned
		///     configuration.
		/// </param>
		/// <returns>A presentation resource configuration with node models and visual settings bound.</returns>
		/// <exception cref="InvalidOperationException">
		///     A prefab is missing under HackThePlanet/NodeModels for a
		///     <see cref="NodeKind" /> value.
		/// </exception>
		public static GraphResourceConfiguration Create(out GraphVisualSettings settings)
		{
			// Start from the shared presentation resource set.
			var resources = GameData.Presentation(true);

			// Register a node model prefab for every node kind.
			foreach (NodeKind kind in Enum.GetValues(typeof(NodeKind)))
			{
				// Load the node model prefab for this kind.
				var asset = Resources.Load<GameObject>("HackThePlanet/NodeModels/" + kind);

				// Reject the kind when its prefab is missing.
				if (asset == null)
					throw new InvalidOperationException("Missing network model: " + kind);

				// Store the model under its graph resource key.
				resources.Set("GraphNodeModels/" + kind, asset);
			}

			// Clone the graph visual settings so runtime changes stay isolated.
			settings = Object.Instantiate(resources.Load<GraphVisualSettings>("GraphVisualSettings"));

			// Bind the cloned settings on the resource configuration.
			resources.Set("GraphVisualSettings", settings);

			return resources;
		}
	}
}