using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace HackThePlanet
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class Palette
    {
        public static Color Lime = Hex("9BFACB"), Cyan = Hex("65D9EB"), Amber = Hex("FFBC70"), Red = Hex("FF786D"), Violet = Hex("BAA1ED");
        public static Color Hex(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var c); return c; }
        public static Color Kind(NodeKind kind)
        {
            switch (kind)
            {
                case NodeKind.Gateway: return Lime;
                case NodeKind.Relay: return Cyan;
                case NodeKind.Firewall: return Amber;
                case NodeKind.Scanner: return Red;
                case NodeKind.SecurityHub: return Violet;
                case NodeKind.Database: return Hex("D9D0B1");
                default: return Hex("92A6B7");
            }
        }
        public static Color Branch(string branch) => branch == "RECON" ? Cyan : branch == "INTRUSION" ? Amber : branch == "STEALTH" ? Violet : branch == "CONTROL" ? Red
            : branch == "CROSSOVER" ? Hex("F59BD0") : branch == "CORE" ? Hex("DCEAE4") : Lime;
    }

    // Small vector glyphs remain crisp at every workstation scale.
    public sealed class DeviceGlyph : VisualElement
    {
        private readonly NodeKind kind;
        private readonly Color tint;
        public DeviceGlyph(NodeKind kind, Color tint, float size = 28)
        {
            this.kind=kind; this.tint=tint; style.width=size; style.height=size; style.flexShrink=0;
            pickingMode=PickingMode.Ignore; generateVisualContent+=Draw;
        }
        private void Draw(MeshGenerationContext context)
        {
            var p=context.painter2D; p.strokeColor=tint; p.lineWidth=1.5f;
            float w=contentRect.width, h=contentRect.height;
            Vector2 P(float x,float y) => new Vector2(x*w,y*h);
            void Line(float x,float y,float a,float b) { p.BeginPath(); p.MoveTo(P(x,y)); p.LineTo(P(a,b)); p.Stroke(); }
            void Box(float x,float y,float a,float b) { p.BeginPath(); p.MoveTo(P(x,y)); p.LineTo(P(a,y)); p.LineTo(P(a,b)); p.LineTo(P(x,b)); p.ClosePath(); p.Stroke(); }
            switch(kind)
            {
                case NodeKind.Workstation:
                    Box(.15f,.18f,.85f,.66f); Line(.5f,.66f,.5f,.83f); Line(.3f,.83f,.7f,.83f); Line(.28f,.34f,.44f,.43f); Line(.44f,.43f,.28f,.53f); Line(.5f,.53f,.68f,.53f); break;
                case NodeKind.Relay:
                    p.BeginPath(); p.Arc(P(.5f,.5f),w*.26f,0,360); p.Stroke();
                    Line(.5f,.06f,.5f,.25f); Line(.5f,.75f,.5f,.94f); Line(.06f,.5f,.25f,.5f); Line(.75f,.5f,.94f,.5f);
                    p.BeginPath(); p.fillColor=tint; p.Arc(P(.5f,.5f),w*.07f,0,360); p.Fill(); break;
                case NodeKind.Firewall:
                    Box(.14f,.15f,.86f,.85f); Line(.14f,.38f,.86f,.38f); Line(.14f,.62f,.86f,.62f); Line(.4f,.15f,.4f,.38f); Line(.66f,.38f,.66f,.62f); Line(.4f,.62f,.4f,.85f); break;
                case NodeKind.Scanner:
                    p.BeginPath(); p.Arc(P(.5f,.5f),w*.36f,0,360); p.Stroke();
                    p.BeginPath(); p.Arc(P(.5f,.5f),w*.2f,0,360); p.Stroke(); Line(.5f,.5f,.8f,.18f); break;
                case NodeKind.SecurityHub:
                    p.BeginPath(); p.MoveTo(P(.5f,.08f)); p.LineTo(P(.86f,.24f)); p.LineTo(P(.8f,.66f)); p.LineTo(P(.5f,.92f)); p.LineTo(P(.2f,.66f)); p.LineTo(P(.14f,.24f)); p.ClosePath(); p.Stroke();
                    Line(.5f,.3f,.5f,.58f); Line(.5f,.7f,.5f,.72f); break;
                case NodeKind.Database:
                    Box(.2f,.12f,.8f,.88f); Line(.2f,.35f,.8f,.35f); Line(.2f,.58f,.8f,.58f); Line(.32f,.24f,.4f,.24f); Line(.32f,.46f,.4f,.46f); Line(.32f,.74f,.4f,.74f); break;
                default:
                    p.BeginPath(); p.Arc(P(.5f,.5f),w*.38f,0,360); p.Stroke();
                    p.BeginPath(); p.MoveTo(P(.5f,.12f)); p.BezierCurveTo(P(.12f,.3f),P(.12f,.7f),P(.5f,.88f)); p.BezierCurveTo(P(.88f,.7f),P(.88f,.3f),P(.5f,.12f)); p.Stroke();
                    Line(.12f,.5f,.88f,.5f); Line(.18f,.32f,.82f,.32f); Line(.18f,.68f,.82f,.68f); break;
            }
        }
    }

    public sealed class CanvasCamera
    {
        public float zoom = 1;
        public Vector2 pan;
    }

    public sealed class NetworkCanvas : VisualElement
    {
        private readonly VisualElement plane;
        private readonly EdgeLayer edges;
        private readonly CanvasCamera camera;
        private readonly float planeHeight;
        private float fit=1;
        private Vector2 dragStart, originalPan;
        private bool dragging;
        public NetworkCanvas(CanvasCamera camera, List<NetNode> nodes, List<NetLink> links, string selected,
            bool preview, Action<string> select, IEnumerable<string> route = null)
        {
            this.camera=camera; planeHeight=460; AddToClassList("graph-viewport");
            plane=new VisualElement { name="graph-plane" }; plane.AddToClassList("graph-plane"); plane.style.height=planeHeight; Add(plane);
            edges=new EdgeLayer(nodes, links, selected, preview, route); plane.Add(edges);
            var known = nodes.Where(n => preview || n.known).ToList();
            foreach (var node in known)
            {
                var button=new Button(() => select(node.id)) { name="node-"+node.id, tooltip=node.label+" / "+node.kind+(node.objective?" / OBJECTIVE":"") };
                button.AddToClassList("net-node");
                if (node.id==selected) button.AddToClassList("selected");
                if (node.controlled && !preview) button.AddToClassList("controlled");
                if (node.objective) button.AddToClassList("objective");
                if (node.collected) button.AddToClassList("collected");
                Color tint=node.objective ? Palette.Amber : Palette.Kind(node.kind);
                button.style.borderLeftColor=tint;
                button.Add(new NodeFrame(tint,node.id==selected,node.objective));
                button.style.left=node.x-58; button.style.top=node.y-32;
                var top=new VisualElement(); top.AddToClassList("node-top"); top.Add(new DeviceGlyph(node.kind,tint,21));
                var code=new Label(node.kind==NodeKind.Gateway ? "LOCAL / 00" : "SEC "+node.security.ToString("00")); code.AddToClassList("node-code"); top.Add(code);
                var mark=new Label(node.collected ? "+" : node.objective ? "*" : !preview&&node.controlled ? "+" : ""); mark.AddToClassList("node-mark"); top.Add(mark); button.Add(top);
                var title=new Label(node.label); title.AddToClassList("node-title"); button.Add(title);
                if (node.suppressed>0) button.AddToClassList("suppressed");
                var readout=new Label(node.collected?"FILE VERIFIED":!preview&&node.controlled?"ROOT ACCESS":node.objective?"CONTRACT TARGET":"ADDR / "+node.id.ToUpperInvariant());
                readout.AddToClassList("node-readout");button.Add(readout);
                plane.Add(button);
            }
            RegisterCallback<GeometryChangedEvent>(_ => Fit());
            RegisterCallback<WheelEvent>(evt => { camera.zoom=Mathf.Clamp(camera.zoom*(evt.delta.y>0?.9f:1.1f),.65f,2.3f); Apply(); evt.StopPropagation(); });
            RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button!=0 || evt.target is Button || (evt.target as VisualElement)?.GetFirstAncestorOfType<Button>()!=null) return;
                dragging=true; dragStart=evt.position; originalPan=camera.pan; this.CapturePointer(evt.pointerId);
            });
            RegisterCallback<PointerMoveEvent>(evt => { if (!dragging) return; camera.pan=originalPan+(new Vector2(evt.position.x,evt.position.y)-dragStart); Apply(); });
            RegisterCallback<PointerUpEvent>(evt => { dragging=false; this.ReleasePointer(evt.pointerId); });
        }
        public NetworkCanvas(CanvasCamera camera, Career career, string selected, Action<string> select)
        {
            this.camera=camera; planeHeight=640; AddToClassList("graph-viewport");
            plane=new VisualElement(); plane.AddToClassList("graph-plane"); Add(plane);
            edges=new EdgeLayer(career, selected); plane.Add(edges);
            foreach(var tech in GameContent.Tech)
            {
                bool owned=career.unlocked.Contains(tech.id), available=tech.parents.All(career.unlocked.Contains);
                var button=new Button(()=>select(tech.id)) { name="tech-"+tech.id, tooltip=tech.description };
                button.AddToClassList("tech-node"); if (owned) button.AddToClassList("owned");
                if (!available) button.AddToClassList("locked"); if (tech.id==selected) button.AddToClassList("selected");
                button.style.left=tech.position.x-73; button.style.top=tech.position.y-40;
                button.style.borderTopColor=Palette.Branch(tech.branch);
                button.Add(new NodeFrame(Palette.Branch(tech.branch),tech.id==selected,false));
                var meta=new Label(tech.branch+(tech.special ? " / ABILITY":"")); meta.AddToClassList("tech-meta"); meta.style.color=Palette.Branch(tech.branch); button.Add(meta);
                var title=new Label(tech.title); title.AddToClassList("tech-title"); button.Add(title);
                var price=new Label(owned ? (career.equipped.Contains(tech.id) ? "EQUIPPED":"UNLOCKED") : tech.cost+" XP"+(tech.parents.Length>1?" / BOTH PARENTS":"")); price.AddToClassList("tech-price"); button.Add(price); plane.Add(button);
            }
            RegisterCallback<GeometryChangedEvent>(_=>Fit());
            RegisterCallback<WheelEvent>(evt=>{camera.zoom=Mathf.Clamp(camera.zoom*(evt.delta.y>0?.9f:1.1f),.65f,2.3f);Apply();evt.StopPropagation();});
            RegisterCallback<PointerDownEvent>(evt=> { if(evt.button!=0 || evt.target is Button || (evt.target as VisualElement)?.GetFirstAncestorOfType<Button>()!=null)return; dragging=true;dragStart=evt.position;originalPan=camera.pan;this.CapturePointer(evt.pointerId); });
            RegisterCallback<PointerMoveEvent>(evt=>{if(dragging){camera.pan=originalPan+(new Vector2(evt.position.x,evt.position.y)-dragStart);Apply();}});
            RegisterCallback<PointerUpEvent>(evt=>{dragging=false;this.ReleasePointer(evt.pointerId);});
        }
        public void ResetView() { camera.pan=Vector2.zero; camera.zoom=1; Fit(); }
        public void Zoom(float amount) { camera.zoom=Mathf.Clamp(camera.zoom*amount,.65f,2.3f); Apply(); }
        public void UpdateSignal(GameSimulation state,SignalEffects effects) { edges.UpdateSignal(state,effects); }
        private void Fit() { fit=Mathf.Min(contentRect.width/1000f,contentRect.height/planeHeight); Apply(); }
        private void Apply()
        {
            float scale=fit*camera.zoom;
            plane.style.transformOrigin=new TransformOrigin(0,0,0);
            plane.style.scale=new Scale(new Vector3(scale,scale,1));
            plane.style.translate=new Translate((contentRect.width-1000*scale)/2+camera.pan.x,(contentRect.height-planeHeight*scale)/2+camera.pan.y);
        }

        private sealed class NodeFrame : VisualElement
        {
            private readonly Color tint;
            private readonly bool selected, objective;
            public NodeFrame(Color tint,bool selected,bool objective)
            {
                this.tint=tint;this.selected=selected;this.objective=objective;
                pickingMode=PickingMode.Ignore;style.position=Position.Absolute;
                style.left=style.top=style.right=style.bottom=0;
                generateVisualContent+=Draw;
            }
            private void Draw(MeshGenerationContext context)
            {
                var p=context.painter2D;float w=contentRect.width,h=contentRect.height;
                var color=tint;color.a=selected?.95f:.42f;p.strokeColor=color;p.lineWidth=selected?1.5f:1;
                foreach(var corner in new[]{new Vector2(3,3),new Vector2(w-3,3),new Vector2(3,h-3),new Vector2(w-3,h-3)})
                {
                    float dx=corner.x<w/2?1:-1,dy=corner.y<h/2?1:-1;
                    p.BeginPath();p.MoveTo(corner+new Vector2(0,7*dy));p.LineTo(corner);p.LineTo(corner+new Vector2(9*dx,0));p.Stroke();
                }
                color.a=selected?.48f:.15f;p.strokeColor=color;p.lineWidth=1;
                for(int x=0;x<5;x++)
                {float a=w-29+x*3;p.BeginPath();p.MoveTo(new Vector2(a,h-12));p.LineTo(new Vector2(a,h-7-(x%3)*2));p.Stroke();}
                if(objective)
                {
                    p.strokeColor=tint;p.BeginPath();p.MoveTo(new Vector2(w-16,0));p.LineTo(new Vector2(w,16));p.Stroke();
                }
            }
        }

        private sealed class EdgeLayer : VisualElement
        {
            private readonly List<NetNode> nodes;
            private readonly List<NetLink> links;
            private readonly string selected;
            private readonly bool preview;
            private readonly HashSet<string> route;
            private readonly Career career;
            private readonly Dictionary<string,NetNode> lookup;
            private float clock,progress;
            private string workingTarget;
            private bool animate;
            private float nextRepaint;
            public EdgeLayer(List<NetNode> nodes,List<NetLink> links,string selected,bool preview,IEnumerable<string> route)
            { this.nodes=nodes;this.links=links;this.selected=selected;this.preview=preview;this.route=new HashSet<string>(route??Array.Empty<string>());lookup=nodes.ToDictionary(n=>n.id);Init(); }
            public EdgeLayer(Career career,string selected) { this.career=career;this.selected=selected;Init(); }
            private void Init() { style.position=Position.Absolute;style.width=1000;style.height=career==null?460:640;pickingMode=PickingMode.Ignore;generateVisualContent+=Draw; }
            public void UpdateSignal(GameSimulation state,SignalEffects effects)
            {
                bool newAnimate=effects==SignalEffects.Full&&(career!=null||!state.Active||!state.Op.paused);
                string target=career==null&&state.Active?state.Op.task?.target:null;
                float newProgress=target!=null?state.Progress:0;
                bool changed=target!=workingTarget||!Mathf.Approximately(newProgress,progress)||animate!=newAnimate;
                workingTarget=target;progress=newProgress;animate=newAnimate;
                clock=effects==SignalEffects.Full?(career!=null||!state.Active?Time.unscaledTime:state.Op.elapsed):3.5f;
                if(changed||(animate&&Time.unscaledTime>=nextRepaint))
                {nextRepaint=Time.unscaledTime+1f/24f;MarkDirtyRepaint();}
            }
            private void Draw(MeshGenerationContext context)
            {
                var p=context.painter2D;
                float height=career==null?460:640;
                // Registration marks stay crisp over the procedural field, including effects-off mode.
                p.lineWidth=1;p.strokeColor=new Color(.38f,.72f,.63f,.16f);
                for(int x=20;x<1000;x+=80)
                    for(int y=20;y<height;y+=80)
                    {p.BeginPath();p.MoveTo(new Vector2(x-2,y));p.LineTo(new Vector2(x+2,y));p.MoveTo(new Vector2(x,y-2));p.LineTo(new Vector2(x,y+2));p.Stroke();}
                if(career!=null)
                {
                    foreach(var t in GameContent.Tech)
                        foreach(var parent in t.parents)
                        {
                            var from=GameContent.FindTech(parent); bool active=career.unlocked.Contains(parent)&&career.unlocked.Contains(t.id);
                            Color color=Palette.Branch(t.branch);color.a=active?.65f:.2f;
                            DrawEdge(p,from.position,t.position,color,active,clock*.12f,animate);
                        }
                    return;
                }
                foreach(var link in links)
                {
                    var a=lookup[link.a];var b=lookup[link.b];
                    if(!preview&&(!a.known||!b.known))continue;
                    bool active=!preview&&a.controlled&&b.controlled;
                    bool highlight=route.Contains(a.id)&&route.Contains(b.id);
                    Color color=highlight?Palette.Lime:active?Palette.Cyan:new Color(.28f,.4f,.43f,.45f);
                    if(highlight||active)color.a=.75f;
                    bool related=a.id==selected||b.id==selected;
                    if(related&&!active&&!highlight)color=new Color(.48f,.64f,.68f,.65f);
                    DrawEdge(p,new Vector2(a.x,a.y),new Vector2(b.x,b.y),color,active||highlight,clock*.17f,animate);
                }
                foreach(var node in nodes)
                {
                    if(!preview&&!node.known)continue;
                    var center=new Vector2(node.x,node.y);
                    if(node.id==selected)
                    {
                        var tint=node.objective?Palette.Amber:Palette.Lime;tint.a=.45f;p.strokeColor=tint;p.lineWidth=1;
                        p.BeginPath();p.MoveTo(center+new Vector2(-67,-24));p.LineTo(center+new Vector2(-67,-38));p.LineTo(center+new Vector2(-48,-38));p.Stroke();
                        p.BeginPath();p.MoveTo(center+new Vector2(48,38));p.LineTo(center+new Vector2(67,38));p.LineTo(center+new Vector2(67,24));p.Stroke();
                    }
                    if(node.id==workingTarget)
                    {
                        p.strokeColor=Palette.Cyan;p.lineWidth=2;
                        p.BeginPath();p.MoveTo(center+new Vector2(-57,35));p.LineTo(center+new Vector2(-57+114*progress,35));p.Stroke();
                    }
                    if(!preview&&node.kind==NodeKind.Scanner&&!node.controlled&&node.suppressed<=0)
                    {
                        float radius=39+(animate?Mathf.Repeat(clock*13+node.x*.04f,35):12);
                        var warning=Palette.Red;warning.a=animate?.13f*(1-(radius-39)/35):.08f;
                        p.strokeColor=warning;p.lineWidth=1;p.BeginPath();p.Arc(center,radius,0,360);p.Stroke();
                    }
                }
            }
            private static void DrawEdge(Painter2D p,Vector2 a,Vector2 b,Color color,bool active,float phase,bool animate)
            {
                float mid=(a.x+b.x)*.5f,bend=Mathf.Min(Mathf.Abs(b.x-a.x)*.4f,Mathf.Abs(b.y-a.y)*.5f),direction=Mathf.Sign(b.x-a.x);
                var c=new Vector2(mid-bend*direction,a.y);var d=new Vector2(mid+bend*direction,b.y);
                void Path(float width,Color tint)
                {p.strokeColor=tint;p.lineWidth=width;p.BeginPath();p.MoveTo(a);p.LineTo(c);p.LineTo(d);p.LineTo(b);p.Stroke();}
                if(active){var halo=color;halo.a=.055f;Path(8,halo);halo.a=.11f;Path(4,halo);}
                Path(active?1.65f:1,color);
                if(!active)return;
                float first=Vector2.Distance(a,c),second=Vector2.Distance(c,d),third=Vector2.Distance(d,b),total=first+second+third;
                Vector2 Point(float fraction)
                {float distance=fraction*total;if(distance<first)return Vector2.Lerp(a,c,distance/Mathf.Max(.001f,first));if(distance<first+second)return Vector2.Lerp(c,d,(distance-first)/Mathf.Max(.001f,second));return Vector2.Lerp(d,b,(distance-first-second)/Mathf.Max(.001f,third));}
                // Static marks in reduced/off mode still identify controlled routes without motion.
                float t=animate?Mathf.Repeat(phase+Mathf.Abs(a.x+a.y)*.001f,1):.42f;
                for(int i=0;i<(animate?4:1);i++)
                {
                    var point=Point(Mathf.Repeat(t-i*.025f,1));var tint=color;tint.a=(1-i*.23f)*.9f;
                    p.fillColor=tint;p.BeginPath();p.MoveTo(point+new Vector2(-3,-2));p.LineTo(point+new Vector2(3,-2));p.LineTo(point+new Vector2(3,2));p.LineTo(point+new Vector2(-3,2));p.ClosePath();p.Fill();
                }
            }
        }
    }
}
