using System;
using Cashg.GraphExplorer;
using UnityEngine;

namespace HackThePlanet
{
    public static class NetworkAppearance
    {
        public static GraphResourceConfiguration Create(out GraphVisualSettings settings)
        {
            var resources = new GraphResourceConfiguration();
            foreach (NodeKind kind in Enum.GetValues(typeof(NodeKind)))
            {
                var asset = Resources.Load<GameObject>("HackThePlanet/NodeModels/" + kind);
                if (asset == null) throw new InvalidOperationException("Missing network model: " + kind);
                resources.Set("GraphNodeModels/" + kind, asset);
            }
            settings = UnityEngine.Object.Instantiate(resources.Load<GraphVisualSettings>("GraphVisualSettings"));
            settings.ShadowBrightness = .6f;
            settings.SurfaceEmission = .12f;
            settings.BloomIntensity = .04f;
            settings.OutlinePixels = .75f;
            settings.OutlineOpacity = .7f;
            settings.SelectionGlowIntensity = .6f;
            settings.SelectionRimIntensity = .3f;
            resources.Set("GraphVisualSettings", settings);
            return resources;
        }
    }
}
