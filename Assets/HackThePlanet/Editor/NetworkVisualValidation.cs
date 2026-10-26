using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Cashg.GraphExplorer;
using UnityEngine;
using UnityEngine.UIElements;

namespace HackThePlanet.Editor
{
    public static class NetworkVisualValidation
    {
        private static T Field<T>(GraphViewport viewport,string name) => (T)typeof(GraphViewport).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(viewport);

        public static void CheckIntel(Action<bool,string> check)
        {
            var fixture = new NetNode { label = "Secret vault", kind = NodeKind.Database, security = 9, detail = "SECRET INTERNAL DATA", objective = true };
            string Describe(ContractIntel intel, bool preview = false) => (string)typeof(GraphViewport)
                .GetMethod("DescribeNode", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { fixture, intel, preview, GameCatalog.Default });
            check(Describe(ContractIntel.Full).Contains("SECURITY 9") && Describe(ContractIntel.Full).Contains("CONTRACT OBJECTIVE"), "Easy focused data stores expose security and objective intel.");
            check(Describe(ContractIntel.Topology).Contains("LIMITED INTEL") && !Describe(ContractIntel.Topology).Contains("SECRET") && !Describe(ContractIntel.Topology).Contains("SECURITY 9"), "Medium focused cards redact internal details and metrics.");
            check(Describe(ContractIntel.None).StartsWith("NO INTEL") && !Describe(ContractIntel.None).Contains("vault") && !Describe(ContractIntel.None).Contains("SECRET"), "Hard focused cards expose only observed state.");
            check(Describe(ContractIntel.Topology, true).StartsWith("NO INTEL") && !Describe(ContractIntel.Topology, true).Contains("Database"), "Anonymous preview cards never disclose node types.");
            var network=NetworkGenerator.Generate(GameContent.Story[0],new Career());
            var before=JsonUtility.ToJson(new NodeSnapshot{nodes=network.nodes});
            GraphScene Project(bool preview,ContractIntel intel)=>(GraphScene)typeof(GraphViewport).GetMethod("BuildNetwork",BindingFlags.Static|BindingFlags.NonPublic)
                .Invoke(null,new object[]{network.nodes,network.links,preview,intel});
            var full=Project(true,ContractIntel.Full);
            check(full.Nodes.Count==network.nodes.Count&&full.Nodes.Values.Any(n=>n.Record.TypeId.EndsWith("-objective")),"Easy previews preserve the full network and objective intel.");
            var topology=Project(true,ContractIntel.Topology);
            check(topology.Nodes.Count==full.Nodes.Count&&topology.Edges.Count==full.Edges.Count,"Medium previews preserve every system and connection.");
            check(topology.Schema.NodeTypes.Count==1&&topology.Nodes.Values.All(n=>n.Record.TypeId=="unknown"&&n.Label=="UNKNOWN SYSTEM"&&n.Record.Properties.Count==0&&n.Emphasis==ToolEmphasis.None),"Medium graph records contain no types, labels, ownership, objectives or emphasis.");
            check(topology.Nodes.Values.All(n=>n.Position==full.Nodes[n.Id].Position),"Medium previews preserve exact route geometry.");
            var blocked=Project(true,ContractIntel.None);
            check(blocked.Nodes.Count==0&&blocked.Edges.Count==0,"Hard previews expose no systems or connections.");
            var live=Project(false,ContractIntel.None);
            check(live.Nodes.Count==network.nodes.Count(n=>n.known)&&live.Nodes.Values.All(n=>n.Record.TypeId!="unknown"),"Connecting restores normal discovered-system intel even on Hard.");
            check(before==JsonUtility.ToJson(new NodeSnapshot{nodes=network.nodes}),"Preview redaction never mutates the generated operation.");
        }
        [Serializable] private sealed class NodeSnapshot { public List<NetNode> nodes; }

        public static void DoubleClickSelected(GraphViewport viewport)
        {
            var scene = Field<GraphScene>(viewport, "scene");
            var camera = Field<Camera>(viewport, "camera");
            var selected = Field<string>(viewport, "selectedId");
            var point = camera.WorldToViewportPoint(scene.Nodes[selected].Position);
            var local = new Vector2(point.x * viewport.contentRect.width, (1 - point.y) * viewport.contentRect.height);
            var screen = viewport.LocalToWorld(local);
            for (int i = 0; i < 2; i++)
            {
                using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = screen }))
                { down.target = viewport; viewport.SendEvent(down); }
                using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = screen }))
                { up.target = viewport; viewport.SendEvent(up); }
            }
            if (Field<GraphCameraState>(viewport, "savedCamera").FocusedId != selected)
                throw new InvalidOperationException("Double-click did not focus the picked system.");
        }

        public static void CheckFocusedCard(GraphViewport viewport)
        {
            var saved = Field<GraphCameraState>(viewport, "savedCamera");
            if (saved.FocusedId != Field<string>(viewport, "selectedId") || !saved.UserAdjusted)
                throw new InvalidOperationException("Node focus did not survive UI rebuild.");
            var card = viewport.Q("focused-node-card");
            if (card.resolvedStyle.display == DisplayStyle.None || !viewport.worldBound.Contains(card.worldBound.min) || !viewport.worldBound.Contains(card.worldBound.max))
                throw new InvalidOperationException("Focused card is hidden or extends outside the graph.");
            if (!GameplayRichText.Plain(viewport.Q<Label>("focused-node-intel").text).Contains("FULL INTEL") || viewport.IntelStatic.image == null)
                throw new InvalidOperationException("Focused card lacks detailed intel or the static background.");
            var camera = Field<Camera>(viewport, "camera");
            var scene = Field<GraphScene>(viewport, "scene");
            var point = camera.WorldToViewportPoint(scene.Nodes[saved.FocusedId].Position);
            if (Mathf.Abs(point.x - .275f) > .01f || Mathf.Abs(point.y - .5f) > .01f)
                throw new InvalidOperationException("Focused camera does not frame the selected system beside its card.");
        }

        public static void CheckViewport(GraphViewport viewport)
        {
            viewport.ResetView();
            var camera=Field<Camera>(viewport,"camera");var scene=Field<GraphScene>(viewport,"scene");
            foreach(var node in scene.Nodes.Values)
            {
                var point=camera.WorldToViewportPoint(node.Position);
                if(point.z<=0||point.x<0||point.x>1||point.y<0||point.y>1)
                    throw new InvalidOperationException("FIT clips "+node.Id);
            }
            var texture=Field<RenderTexture>(viewport,"texture");
            SaveTexture(texture,"network-overview.png");
            var pixels=Read(texture);
            if(pixels.Count(c=>c.g>70&&c.g>c.r*1.2f)<80)throw new InvalidOperationException("Network graph has no visible model pixels");
        }

        public static void Orbit(GraphViewport viewport,float yaw,float pitch)
        {
            typeof(GraphViewport).GetField("yaw",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(viewport,yaw);
            typeof(GraphViewport).GetField("pitch",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(viewport,pitch);
            viewport.ResetView();
        }

        public static void Run()
        {
            string root=Directory.GetParent(Application.dataPath).FullName;
            var resources=NetworkAppearance.Create(out var settings);var schema=new GraphSchema();var document=new GraphDocument("model-gallery","Network hardware");
            int index=0;
            foreach(NodeKind kind in Enum.GetValues(typeof(NodeKind)))
            {
                var asset=Resources.Load<GameObject>("HackThePlanet/NodeModels/"+kind);
                if(asset==null)throw new InvalidOperationException("Missing model "+kind);
                resources.Set("GraphNodeModels/"+kind,asset);
                using(var mesh=new OwnedMesh(GraphNodeModels.Create(kind.ToString(),resources)))
                {
                    if(!mesh.Value.isReadable||mesh.Value.vertexCount<8||mesh.Value.subMeshCount!=1||mesh.Value.vertices.Any(v=>v.magnitude>.501f))
                        throw new InvalidOperationException("Invalid imported model "+kind);
                }
                schema.Register(new GraphNodeTypeDefinition(kind.ToString(),kind.ToString(),kind.ToString(),Palette.Kind(kind),1.5f));
                document.UpsertNode(new GraphNodeRecord("model-"+index++,kind.ToString(),kind.ToString()));
            }
            var scene=new GraphScene(document,schema);index=0;
            foreach(var node in scene.Nodes.Values){node.Position=new Vector3((index++-3)*3.4f,0,0);node.Pinned=true;}
            var obj=new GameObject("Network model validation camera");var camera=obj.AddComponent<Camera>();
            var texture=new RenderTexture(1400,500,24){antiAliasing=2};texture.Create();
            camera.enabled=false;camera.targetTexture=texture;camera.aspect=2.8f;camera.fieldOfView=48;
            camera.orthographic=true;camera.orthographicSize=4.8f;
            camera.cullingMask=1;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Palette.Hex("071014");
            var session=GraphRenderingSession.Initialize(camera,resources);
            try
            {
                session.Layout.Bind(scene);session.Layout.Paused=true;
                var filter=new EveryEdge();session.Renderer.UpdateMetrics(scene,filter);
                session.Renderer.GuideArrows=new[]
                {
                    new GraphGuideArrow { NodeId="model-0", Color=Palette.Lime },
                    new GraphGuideArrow { NodeId="model-5", Color=new Color(1f,.64f,.22f) }
                };
                for(int view=0;view<3;view++)
                {
                    var rotation=Quaternion.Euler(view==0?8:20,view==0?0:view==1?25:155,0);
                    camera.transform.rotation=rotation;camera.transform.position=rotation*new Vector3(0,0,-16);
                    session.Renderer.ReducedMotion=view>0;
                    session.Renderer.Draw(scene,filter,camera,null,null,null);camera.Render();
                    if(session.Renderer.VisibleCount!=7)throw new InvalidOperationException("Gallery did not submit seven models");
                    var pixels=Read(texture);
                    SaveTexture(texture,"node-models-"+view+".png");
                    if(pixels.Count(c=>c.g>70||c.r>90||c.b>90)<200)throw new InvalidOperationException("Blank model gallery");
                    foreach(var node in scene.Nodes.Values)
                    {
                        var screen=camera.WorldToScreenPoint(node.Position);
                        if(session.Renderer.Pick(scene,camera,screen)?.Id!=node.Id)throw new InvalidOperationException("Model picking failed: "+node.Label);
                        int x=Mathf.RoundToInt(screen.x),y=Mathf.RoundToInt(screen.y),hits=0;
                        for(int dy=-25;dy<=25;dy++)for(int dx=-25;dx<=25;dx++)
                        {
                            int px=x+dx,py=y+dy;if(px<0||py<0||px>=texture.width||py>=texture.height)continue;
                            var c=pixels[py*texture.width+px];if(c.g>45||c.r>45||c.b>60)hits++;
                        }
                        if(hits<12)throw new InvalidOperationException("No rendered pixels for "+node.Label+" in view "+view+" ("+hits+")");
                    }
                }
                File.WriteAllText(Path.Combine(root,"Artifacts/network-model-validation.txt"),"PASS / Seven original readable normalized meshes, actual graph shader pixels and picking from front, three-quarter and rear views. Reduced motion exercised. Gallery order: Gateway, Workstation, Relay, Firewall, Scanner, Database, SecurityHub.");
            }
            finally{session.Dispose();camera.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(obj);UnityEngine.Object.DestroyImmediate(settings);}
        }

        private sealed class EveryEdge:IGraphEdgeFilter{public bool EdgeMatches(IGraphEdge edge)=>true;}
        private sealed class OwnedMesh:IDisposable
        {
            public Mesh Value;public OwnedMesh(Mesh mesh){Value=mesh;}public void Dispose()=>UnityEngine.Object.DestroyImmediate(Value);
        }
        private static Color32[] Read(RenderTexture texture)
        {
            var previous=RenderTexture.active;var read=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false);
            try{RenderTexture.active=texture;read.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);read.Apply();return read.GetPixels32();}
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(read);}
        }
        private static void SaveTexture(RenderTexture texture,string name)
        {
            var previous=RenderTexture.active;var read=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false);
            try
            {
                RenderTexture.active=texture;read.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);read.Apply();
                var dir=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"Artifacts");Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir,name),read.EncodeToPNG());
            }
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(read);}
        }
    }
}
