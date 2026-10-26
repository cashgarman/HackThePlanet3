using System;
using Cashg.GraphExplorer;
using UnityEngine;

namespace HackThePlanet
{
    public static class NetworkAppearance
    {
        public static GraphResourceConfiguration Create(out GraphVisualSettings settings)
        {
            var resources = GameData.Presentation(true);
            foreach (NodeKind kind in Enum.GetValues(typeof(NodeKind)))
            {
                var asset = Resources.Load<GameObject>("HackThePlanet/NodeModels/" + kind);
                if (asset == null) throw new InvalidOperationException("Missing network model: " + kind);
                resources.Set("GraphNodeModels/" + kind, asset);
            }
            settings = UnityEngine.Object.Instantiate(resources.Load<GraphVisualSettings>("GraphVisualSettings"));
            resources.Set("GraphVisualSettings", settings);
            return resources;
        }
    }
}
