using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace HackThePlanet
{
    public sealed partial class WorkstationApp
    {
        private sealed class ActionButtonParts
        {
            public readonly ActionInfo action;
            public readonly Button button;
            public readonly VisualElement wrapper;
            public readonly ActionGlyph icon;
            public readonly string description;
            public ActionButtonParts(ActionInfo action,Button button,VisualElement wrapper,ActionGlyph icon,string description)
            {this.action=action;this.button=button;this.wrapper=wrapper;this.icon=icon;this.description=description;}
        }

        private readonly Dictionary<string,ActionButtonParts> actionButtons=new Dictionary<string,ActionButtonParts>();
        private RadialResourceMeter memoryRadial,storageRadial;
        private Label actionHeading,queueStatus;
        private VisualElement queueList;
        private string queueSignature;

        private void BuildActionBar(VisualElement parent)
        {
            actionButtons.Clear();queueSignature=null;
            var bar=El(parent,"graph-action-bar");
            memoryRadial=new RadialResourceMeter("RAM / SHARED POOL");bar.Add(memoryRadial);
            var center=El(bar,"graph-action-center");
            var header=El(center,"action-bar-header");
            actionHeading=Text(header,"ACTIONS / SELECT A SYSTEM","action-bar-heading");
            queueStatus=Text(header,"QUEUE EMPTY","action-queue-status");
            queueList=El(header,"action-queue-list");
            var scroll=new ScrollView(ScrollViewMode.Horizontal){name="action-list"};scroll.AddToClassList("graph-action-scroll");center.Add(scroll);
            var list=El(scroll,"graph-action-list");
            var ids=new List<string>{"scan","hack","download"};
            // Mirrors GameSimulation.Granted: owned techs grant their actions, special techs also need to be equipped.
            foreach(var tech in State.Catalog.Technologies)
            {
                if(!State.Has(tech.id)||(tech.special&&!State.Equipped(tech.id)))continue;
                foreach(string actionId in tech.grants)if(!ids.Contains(actionId))ids.Add(actionId);
            }
            bool primary=true;
            foreach(string id in ids)
            {
                var action=State.Catalog.FindAction(id);if(action==null)continue;
                ActionButton(list,action,primary);primary=false;
            }
            storageRadial=new RadialResourceMeter("STORAGE / PAYLOADS");bar.Add(storageRadial);
            RefreshActionBar();RefreshResourceRadials();
        }

        private void RefreshActionBar()
        {
            if(actionButtons==null||State==null||!State.Active)return;
            if(actionHeading!=null)actionHeading.text="ACTIONS / "+(State.Node(selectedNode)?.label??"SELECT A SYSTEM");
            bool first=true;
            foreach(var parts in actionButtons.Values){UpdateActionButton(parts,first);first=false;}
            RefreshQueueStatus();
        }

        private void RefreshQueueStatus()
        {
            if(queueStatus==null||!State.Active)return;
            string current=State.Op.task==null?"IDLE":(State.Op.paused?"PAUSED / ":"RUNNING / ")+State.ActionTitle(State.Op.task.actionId,State.Op.task.target)+" · "+(State.Node(State.Op.task.target)?.label??"OPERATOR");
            queueStatus.text=current+"  /  QUEUE "+State.Op.queue.Count+" / "+State.QueueCapacity;
            queueStatus.tooltip=current+"\nQueue slots used: "+State.Op.queue.Count+" / "+State.QueueCapacity+(State.Op.queue.Count>0?"\nUse CLEAR to remove queued actions.":string.Empty);
            string signature=string.Join(";",State.Op.queue.Select(q=>q.actionId+"@"+q.target));
            if(queueList==null||signature==queueSignature)return;
            queueSignature=signature;queueList.Clear();
            if(State.Op.queue.Count==0)return;
            for(int i=0;i<State.Op.queue.Count;i++)
            {
                var queued=State.Op.queue[i];var chip=El(queueList,"action-queue-chip");
                string title=State.ActionTitle(queued.actionId,queued.target);
                chip.tooltip=(i+1)+" / "+title+" / "+(State.Node(queued.target)?.label??"OPERATOR");
                chip.Add(new ActionGlyph(queued.actionId,ActionGlyph.ColorFor(queued.actionId),12));
                Text(chip,(i+1).ToString(),"action-queue-index");
            }
        }

        private void RefreshResourceRadials()
        {
            if(!State.Active)return;
            if(memoryRadial!=null)
            {
                memoryRadial.SetValues(State.MemoryPool,State.MemoryReserved,State.MemoryReclaiming,State.MemoryFree,Palette.Cyan,new Color(Palette.Cyan.r,Palette.Cyan.g,Palette.Cyan.b,.38f),
                    State.MemoryFree.ToString("0.#"),"GB FREE",
                    "<b>SHARED RAM</b>\n"+State.MemoryReserved.ToString("0.#")+" GB active\n"+State.MemoryReclaiming.ToString("0.#")+" GB reclaiming\n"+State.MemoryFree.ToString("0.#")+" GB free for one-shots / "+State.MemoryPool.ToString("0.#")+" GB total");
            }
            if(storageRadial!=null)
            {
                float used=State.StorageUsed,total=State.StorageCapacity,free=State.FreeStorage;
                storageRadial.SetValues(total,used,free,free,Palette.Lime,new Color(Palette.Lime.r,Palette.Lime.g,Palette.Lime.b,.38f),free.ToString("0.#"),"GB FREE",
                    "<b>PAYLOAD STORAGE</b>\n"+used.ToString("0.#")+" GB used\n"+free.ToString("0.#")+" GB available / "+total.ToString("0.#")+" GB total");
            }
        }
    }

    internal sealed class RadialResourceMeter : VisualElement
    {
        private readonly Label amount,label;
        private float total,primary,secondary;
        private Color primaryColor,secondaryColor;

        public RadialResourceMeter(string title)
        {
            AddToClassList("resource-radial");pickingMode=PickingMode.Position;
            tooltip=title;AddToClassList(WorkstationApp.IntelTooltipClass);
            var ring=new VisualElement();ring.AddToClassList("resource-radial-ring");ring.pickingMode=PickingMode.Ignore;ring.generateVisualContent+=Draw;Add(ring);
            var center=new VisualElement();center.AddToClassList("resource-radial-center");center.pickingMode=PickingMode.Ignore;Add(center);
            amount=new Label("0");amount.AddToClassList("resource-radial-amount");amount.pickingMode=PickingMode.Ignore;center.Add(amount);
            label=new Label("GB FREE");label.AddToClassList("resource-radial-caption");label.pickingMode=PickingMode.Ignore;center.Add(label);
        }

        public void SetValues(float capacity,float first,float second,float available,Color firstColor,Color secondColor,string value,string caption,string tip)
        {
            total=Mathf.Max(0,capacity);primary=Mathf.Clamp(first/Mathf.Max(.001f,total),0,1);
            secondary=Mathf.Clamp(second/Mathf.Max(.001f,total),0,1-primary);
            primaryColor=firstColor;secondaryColor=secondColor;
            amount.text=value;label.text=caption;tooltip=tip;
            this.Q<VisualElement>(className:"resource-radial-ring")?.MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            var p=context.painter2D;float size=Mathf.Min(contentRect.width,contentRect.height);if(size<2)return;
            var center=new Vector2(contentRect.width*.5f,contentRect.height*.5f);float radius=size*.43f;
            p.lineWidth=Mathf.Max(3,size*.095f);p.strokeColor=Palette.Hex("263940");p.BeginPath();p.Arc(center,radius,0,360);p.Stroke();
            if(total<=0)return;
            DrawArc(p,center,radius,0,primary,primaryColor);
            DrawArc(p,center,radius,primary,secondary,secondaryColor);
        }

        private static void DrawArc(Painter2D p,Vector2 center,float radius,float start,float fraction,Color color)
        {
            if(fraction<=.001f)return;
            p.strokeColor=color;p.BeginPath();p.Arc(center,radius,start*360f,(start+fraction)*360f);p.Stroke();
        }
    }

    internal sealed class ActionGlyph : VisualElement
    {
        private readonly string actionId;
        private Color tint;

        public ActionGlyph(string actionId,Color tint,float size)
        {
            this.actionId=actionId;this.tint=tint;
            AddToClassList("action-glyph");style.width=size;style.height=size;style.flexShrink=0;
            pickingMode=PickingMode.Ignore;generateVisualContent+=Draw;
        }

        public static Color ColorFor(string actionId)
        {
            switch(actionId)
            {
                case "scan": return Palette.Cyan;
                case "hack": case "backdoor": return Palette.Lime;
                case "download": case "bridge": return Palette.Amber;
                case "disrupt": return Palette.Red;
                case "blackout": case "decoy": return Palette.Violet;
                default: return Palette.Cyan;
            }
        }

        public void SetTint(Color color){if(tint==color)return;tint=color;MarkDirtyRepaint();}

        private void Draw(MeshGenerationContext context)
        {
            float size=Mathf.Min(contentRect.width,contentRect.height);if(size<2)return;
            var p=context.painter2D;p.strokeColor=tint;p.fillColor=tint;p.lineWidth=Mathf.Max(1.5f,size*.075f);
            Vector2 P(float x,float y)=>new Vector2(x*contentRect.width,y*contentRect.height);
            void Line(float x,float y,float a,float b){p.BeginPath();p.MoveTo(P(x,y));p.LineTo(P(a,b));p.Stroke();}
            void Box(float x,float y,float a,float b){p.BeginPath();p.MoveTo(P(x,y));p.LineTo(P(a,y));p.LineTo(P(a,b));p.LineTo(P(x,b));p.ClosePath();p.Stroke();}
            void Dot(float x,float y,float radius){p.BeginPath();p.Arc(P(x,y),size*radius,0,360);p.Fill();}
            switch(actionId)
            {
                case "scan":
                    p.BeginPath();p.Arc(P(.5f,.5f),size*.32f,0,360);p.Stroke();
                    p.BeginPath();p.Arc(P(.5f,.5f),size*.17f,0,360);p.Stroke();
                    Line(.5f,.5f,.78f,.22f);Dot(.5f,.5f,.055f);break;
                case "hack":
                    Box(.12f,.2f,.88f,.8f);Line(.24f,.4f,.39f,.51f);Line(.39f,.51f,.24f,.62f);Line(.49f,.62f,.72f,.62f);break;
                case "download":
                    Line(.5f,.13f,.5f,.62f);Line(.31f,.44f,.5f,.63f);Line(.69f,.44f,.5f,.63f);
                    Line(.19f,.72f,.19f,.86f);Line(.19f,.86f,.81f,.86f);Line(.81f,.86f,.81f,.72f);break;
                case "backdoor":
                    Box(.21f,.12f,.72f,.88f);Line(.72f,.12f,.82f,.2f);Line(.72f,.88f,.82f,.8f);
                    Line(.55f,.51f,.87f,.51f);Line(.75f,.39f,.87f,.51f);Line(.75f,.63f,.87f,.51f);break;
                case "bridge":
                    Box(.08f,.28f,.27f,.7f);Box(.73f,.28f,.92f,.7f);
                    Line(.27f,.37f,.5f,.22f);Line(.5f,.22f,.73f,.37f);Line(.27f,.63f,.73f,.63f);break;
                case "blackout":
                    Line(.54f,.1f,.3f,.5f);Line(.3f,.5f,.52f,.5f);Line(.52f,.5f,.4f,.9f);Line(.4f,.9f,.75f,.42f);Line(.75f,.42f,.52f,.42f);
                    Line(.15f,.18f,.29f,.27f);Line(.73f,.75f,.86f,.84f);break;
                case "decoy":
                    Box(.12f,.2f,.62f,.7f);Box(.38f,.3f,.88f,.8f);Dot(.63f,.55f,.07f);break;
                case "disrupt":
                    p.BeginPath();p.Arc(P(.5f,.5f),size*.31f,0,360);p.Stroke();
                    Line(.16f,.84f,.84f,.16f);Dot(.5f,.5f,.055f);break;
                default:
                    Box(.2f,.2f,.8f,.8f);Line(.5f,.06f,.5f,.2f);Line(.5f,.8f,.5f,.94f);
                    Dot(.5f,.5f,.09f);break;
            }
        }
    }
}
