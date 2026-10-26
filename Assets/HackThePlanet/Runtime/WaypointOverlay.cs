using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace HackThePlanet
{
    // Uses full-network positions without adding undiscovered systems to the rendered scene.
    internal sealed class WaypointOverlay : VisualElement
    {
        private readonly Camera camera;
        private readonly Dictionary<string,Vector3> positions;
        private readonly List<(NetworkWaypoint waypoint,Label marker)> markers=new();
        private readonly IReadOnlyList<string> path;
        private readonly Color routeColor;
        private float textScale=1;
        public IEnumerable<Vector3> Anchors => markers.Select(m=>positions[m.waypoint.nodeId]).Concat(path.Select(id=>positions[id]));

        public WaypointOverlay(Camera camera,List<NetNode> nodes,List<NetLink> links,IReadOnlyList<NetworkWaypoint> waypoints,int tier,string entry)
        {
            this.camera=camera;positions=nodes.ToDictionary(n=>n.id,n=>n.position);
            style.position=Position.Absolute;style.left=style.right=style.top=style.bottom=0;pickingMode=PickingMode.Ignore;
            foreach(var waypoint in waypoints.Where(w=>positions.ContainsKey(w.nodeId)))
            {
                var marker=new Label("W"+(markers.Count+1)+(tier>=3&&!string.IsNullOrEmpty(waypoint.label)?" / "+waypoint.label:"")){enableRichText=false,pickingMode=PickingMode.Ignore};
                marker.name="waypoint-marker-"+waypoint.nodeId;
                marker.style.position=Position.Absolute;marker.style.color=GameSimulation.WaypointColors[tier>=2?waypoint.color:0];
                marker.style.backgroundColor=new Color(.02f,.05f,.06f,.92f);marker.style.fontSize=12;
                marker.style.paddingLeft=marker.style.paddingRight=5;marker.style.paddingTop=marker.style.paddingBottom=3;
                markers.Add((waypoint,marker));Add(marker);
            }
            var target=tier>=4?waypoints.FirstOrDefault(w=>w.showPath&&positions.ContainsKey(w.nodeId)):null;
            path=target==null?System.Array.Empty<string>():GameSimulation.WaypointPath(nodes,links,entry,target.nodeId).NodeIds;
            routeColor=target==null?Color.white:GameSimulation.WaypointColors[target.color];
            generateVisualContent+=DrawPath;
        }

        public void Reposition(float scale)
        {
            textScale=scale;
            foreach(var item in markers)
            {
                var p=camera.WorldToViewportPoint(positions[item.waypoint.nodeId]);
                item.marker.style.display=p.z>camera.nearClipPlane?DisplayStyle.Flex:DisplayStyle.None;
                // Keep the marker at the edge when its node lies outside the current camera frame.
                item.marker.style.fontSize=12*scale;
                float width=item.marker.resolvedStyle.width;if(float.IsNaN(width))width=70;
                item.marker.style.left=Mathf.Clamp(p.x*contentRect.width-width/2,0,Mathf.Max(0,contentRect.width-Mathf.Max(70,width)));
                item.marker.style.top=Mathf.Clamp((1-p.y)*contentRect.height-42*scale,0,Mathf.Max(0,contentRect.height-25*scale));
            }
            MarkDirtyRepaint();
        }

        private void DrawPath(MeshGenerationContext context)
        {
            var painter=context.painter2D;painter.strokeColor=routeColor;painter.lineWidth=3;
            for(int i=1;i<path.Count;i++)
            {
                var a=camera.WorldToViewportPoint(positions[path[i-1]]);var b=camera.WorldToViewportPoint(positions[path[i]]);
                if(a.z<=camera.nearClipPlane||b.z<=camera.nearClipPlane)continue;
                painter.BeginPath();painter.MoveTo(new Vector2(a.x*contentRect.width,(1-a.y)*contentRect.height));
                painter.LineTo(new Vector2(b.x*contentRect.width,(1-b.y)*contentRect.height));painter.Stroke();
            }
            foreach(var item in markers)
            {
                var p=camera.WorldToViewportPoint(positions[item.waypoint.nodeId]);if(p.z<=camera.nearClipPlane)continue;
                var anchor=new Vector2(p.x*contentRect.width,(1-p.y)*contentRect.height);
                painter.fillColor=item.marker.resolvedStyle.color;painter.BeginPath();
                painter.MoveTo(anchor+new Vector2(-5,-17)*textScale);painter.LineTo(anchor+new Vector2(5,-17)*textScale);
                painter.LineTo(anchor+new Vector2(0,-8)*textScale);painter.ClosePath();painter.Fill();
            }
        }
    }
}
