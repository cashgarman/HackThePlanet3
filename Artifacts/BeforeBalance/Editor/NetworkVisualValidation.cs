using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Cashg.GraphExplorer;
using UnityEngine;

namespace HackThePlanet.Editor
{
    public static class NetworkVisualValidation
    {
        private static T Field<T>(GraphViewport viewport,string name) => (T)typeof(GraphViewport).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(viewport);

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
                    SaveTexture(texture,"node-models-"+view+".png");
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
