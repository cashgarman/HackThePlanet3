using System;
using System.Collections.Generic;
using System.Linq;
using Cashg.GraphExplorer;
using UnityEngine;
using UnityEngine.UIElements;

namespace HackThePlanet
{
    // Vector icons for rig parts, stat readouts and inventory tabs. Drawn like DeviceGlyph so they stay crisp at any text scale.
    // The key is a module id for parts with their own drawing, otherwise a family name. tier > 0 adds one pip per tier to power and cooling icons.
    public sealed class RigGlyph : VisualElement
    {
        private static readonly HashSet<string> BespokeModules=new HashSet<string>{"uplink","recon-cache","drive","storage-array","accelerator","ram-bank","quiet-filter"};
        private readonly string category;
        private readonly Color tint;
        private readonly int tier;
        public RigGlyph(string category,Color tint,float size=20,int tier=0)
        {
            this.category=category??""; this.tint=tint; this.tier=Mathf.Clamp(tier,0,5); style.width=size; style.height=size; style.flexShrink=0;
            pickingMode=PickingMode.Ignore; generateVisualContent+=Draw;
        }
        // Parts without a bespoke icon (including mod parts) use their family icon, so every module gets one.
        public static RigGlyph ForModule(RigModuleDefinition model,Color tint,float size=20)
        {
            bool tiered=model.family=="power"||model.family=="cooling";
            return new RigGlyph(BespokeModules.Contains(model.id)?model.id:model.family,tint,size,tiered?model.tier:0);
        }
        private void Draw(MeshGenerationContext context)
        {
            var p=context.painter2D; p.strokeColor=tint; p.fillColor=tint; p.lineWidth=1.5f;
            float w=contentRect.width, h=contentRect.height;
            // Tiered icons shrink the artwork upward to leave a row of pips along the bottom edge.
            float scale=tier>0?.8f:1f;
            Vector2 P(float x,float y) => new Vector2(x*w,y*h*scale);
            void Line(float x,float y,float a,float b) { p.BeginPath(); p.MoveTo(P(x,y)); p.LineTo(P(a,b)); p.Stroke(); }
            void Box(float x,float y,float a,float b) { p.BeginPath(); p.MoveTo(P(x,y)); p.LineTo(P(a,y)); p.LineTo(P(a,b)); p.LineTo(P(x,b)); p.ClosePath(); p.Stroke(); }
            void Poly(params float[] v) { p.BeginPath(); p.MoveTo(P(v[0],v[1])); for(int i=2;i<v.Length;i+=2) p.LineTo(P(v[i],v[i+1])); p.ClosePath(); p.Stroke(); }
            void Chip(float x,float y,float a,float b) { p.BeginPath(); p.MoveTo(P(x,y)); p.LineTo(P(a,y)); p.LineTo(P(a,b)); p.LineTo(P(x,b)); p.ClosePath(); p.Fill(); }
            switch(category)
            {
                case "ALL":
                    Box(.12f,.12f,.46f,.46f); Box(.54f,.12f,.88f,.46f); Box(.12f,.54f,.46f,.88f); Box(.54f,.54f,.88f,.88f); break;
                case "power":
                    Poly(.58f,.06f, .22f,.56f, .48f,.56f, .40f,.94f, .78f,.42f, .52f,.42f, .58f,.06f); break;
                case "cooling":
                    for(int i=0;i<3;i++)
                    {
                        float a=i*60f*Mathf.Deg2Rad, dx=Mathf.Cos(a)*.38f, dy=Mathf.Sin(a)*.38f;
                        Line(.5f-dx,.5f-dy,.5f+dx,.5f+dy);
                    }
                    p.BeginPath(); p.Arc(P(.5f,.5f),w*.07f,0f,360f); p.Fill(); break;
                case "processing":
                    Box(.22f,.22f,.78f,.78f); Box(.38f,.38f,.62f,.62f);
                    Line(.36f,.08f,.36f,.22f); Line(.64f,.08f,.64f,.22f); Line(.36f,.78f,.36f,.92f); Line(.64f,.78f,.64f,.92f);
                    Line(.08f,.36f,.22f,.36f); Line(.08f,.64f,.22f,.64f); Line(.78f,.36f,.92f,.36f); Line(.78f,.64f,.92f,.64f); break;
                case "transfer":
                    Line(.10f,.36f,.84f,.36f); Line(.84f,.36f,.70f,.24f); Line(.84f,.36f,.70f,.48f);
                    Line(.90f,.64f,.16f,.64f); Line(.16f,.64f,.30f,.52f); Line(.16f,.64f,.30f,.76f); break;
                case "storage":
                    Box(.16f,.10f,.84f,.34f); Box(.16f,.40f,.84f,.64f); Box(.16f,.70f,.84f,.94f);
                    Line(.26f,.22f,.40f,.22f); Line(.26f,.52f,.40f,.52f); Line(.26f,.82f,.40f,.82f); break;
                case "memory":
                    Box(.08f,.30f,.92f,.66f);
                    Line(.20f,.66f,.20f,.80f); Line(.35f,.66f,.35f,.80f); Line(.50f,.66f,.50f,.80f); Line(.65f,.66f,.65f,.80f); Line(.80f,.66f,.80f,.80f);
                    Box(.20f,.40f,.36f,.56f); Line(.46f,.42f,.80f,.42f); Line(.46f,.54f,.80f,.54f); break;
                case "stealth":
                    p.BeginPath(); p.MoveTo(P(.08f,.5f)); p.BezierCurveTo(P(.28f,.20f),P(.72f,.20f),P(.92f,.5f));
                    p.BezierCurveTo(P(.72f,.80f),P(.28f,.80f),P(.08f,.5f)); p.Stroke();
                    p.BeginPath(); p.Arc(P(.5f,.5f),w*.12f,0f,360f); p.Stroke();
                    Line(.14f,.90f,.86f,.10f); break;
                case "reclaim":
                    p.BeginPath(); p.Arc(P(.5f,.5f),w*.34f,30f,300f); p.Stroke();
                    float end=300f*Mathf.Deg2Rad, ex=.5f+Mathf.Cos(end)*.34f, ey=.5f+Mathf.Sin(end)*.34f;
                    Line(ex,ey,ex+.16f,ey-.02f); Line(ex,ey,ex-.02f,ey-.16f); break;
                case "slots":
                    Box(.1f,.1f,.9f,.9f); Line(.1f,.37f,.9f,.37f); Line(.1f,.63f,.9f,.63f); Line(.37f,.1f,.37f,.9f); Line(.63f,.1f,.63f,.9f); break;
                case "uplink":
                    Line(.5f,.56f,.5f,.92f); Line(.34f,.92f,.66f,.92f);
                    p.BeginPath(); p.Arc(P(.5f,.5f),w*.14f,200f,340f); p.Stroke();
                    p.BeginPath(); p.Arc(P(.5f,.5f),w*.27f,200f,340f); p.Stroke();
                    p.BeginPath(); p.Arc(P(.5f,.5f),w*.05f,0f,360f); p.Fill(); break;
                case "recon-cache":
                    Box(.14f,.46f,.86f,.86f); Line(.5f,.1f,.5f,.6f); Line(.36f,.46f,.5f,.6f); Line(.64f,.46f,.5f,.6f); break;
                case "drive":
                    Box(.1f,.12f,.9f,.88f);
                    p.BeginPath(); p.Arc(P(.42f,.5f),w*.22f,0f,360f); p.Stroke();
                    p.BeginPath(); p.Arc(P(.76f,.26f),w*.04f,0f,360f); p.Fill(); break;
                case "storage-array":
                    Box(.08f,.08f,.92f,.92f);
                    foreach(float fx in new[]{.3f,.7f}) foreach(float fy in new[]{.3f,.7f}) { p.BeginPath(); p.Arc(P(fx,fy),w*.1f,0f,360f); p.Stroke(); }
                    break;
                case "accelerator":
                    Box(.18f,.18f,.82f,.82f);
                    Line(.06f,.36f,.18f,.36f); Line(.06f,.64f,.18f,.64f); Line(.82f,.36f,.94f,.36f); Line(.82f,.64f,.94f,.64f);
                    Line(.36f,.06f,.36f,.18f); Line(.64f,.06f,.64f,.18f); Line(.36f,.82f,.36f,.94f); Line(.64f,.82f,.64f,.94f);
                    Line(.34f,.58f,.5f,.42f); Line(.5f,.42f,.66f,.58f); Line(.34f,.74f,.5f,.58f); Line(.5f,.58f,.66f,.74f); break;
                case "ram-bank":
                    Box(.08f,.14f,.92f,.44f); Box(.08f,.56f,.92f,.86f);
                    foreach(float cx in new[]{.22f,.4f,.6f,.78f}) { Chip(cx-.04f,.22f,cx+.04f,.36f); Chip(cx-.04f,.64f,cx+.04f,.78f); }
                    break;
                case "quiet-filter":
                    Poly(.12f,.14f, .88f,.14f, .58f,.5f, .58f,.86f, .42f,.76f, .42f,.5f); break;
                default:
                    p.BeginPath(); p.Arc(P(.5f,.5f),w*.38f,0f,360f); p.Stroke();
                    Line(.12f,.5f,.88f,.5f); break;
            }
            for(int i=0;i<tier;i++){float cx=.5f+(i-(tier-1)*.5f)*.16f;Chip(cx-.05f,.84f,cx+.05f,.92f);}
        }
    }

    public sealed partial class WorkstationApp
    {
        private readonly GraphCameraState rigCamera=new GraphCameraState();
        private static readonly string[] RigCategoryOrder={"power","cooling","processing","transfer","storage","memory","stealth","reclaim"};
        private string selectedRigId,dragRigId,rigMessage,rigCategory="ALL";
        private bool dragRotated,dragMoved,dragLifted;
        private int dragPointer,dragRestoreRow=-1,dragRestoreCol=-1;
        private bool dragRestoreRot;
        private Vector2 dragOrigin,lastRigDragPanelPos;
        private Button dragGhost;
        private VisualElement inventoryDropZone;
        private readonly List<Button> dragValidSlots=new List<Button>();

        private void BuildRig(VisualElement workspace)
        {
            if(State.Part(selectedRigId)==null)selectedRigId=null;
            var categories=RigCategories().ToList();
            if(rigCategory!="ALL"&&!categories.Contains(rigCategory))rigCategory="ALL";
            var left=El(workspace,"left-panel");var shop=Scroll(left,"rig-shop");
            Text(shop,"MODULE EXCHANGE","eyebrow cyan");Text(shop,"Buy copies. Pack your signal.","small muted");
            foreach(var branch in State.Modules.GroupBy(m=>m.branch))
            {
                Text(shop,branch.Key,"eyebrow section-space").style.color=Palette.Branch(branch.Key);
                foreach(var model in branch)
                {
                    var card=El(shop,"rig-shop-card");card.tooltip=ModuleTooltip(model)+"\n"+State.PreviewModule(model.id).reason;
                    var shopHead=El(card,"rig-shop-head");shopHead.Add(RigGlyph.ForModule(model,Palette.Branch(model.branch),18));
                    Text(shopHead,model.title+" / T"+model.tier,"small rig-part-title").style.color=Palette.Branch(model.branch);
                    RichText(card,model.width+"x"+model.height+" / "+model.price+" cr / "+model.powerDemand+" POWER / "+model.heatOutput+" HEAT","micro muted");
                    RichText(card,model.description,"small");
                    var preview=State.PreviewModule(model.id);
                    var buy=Btn(card,"BUY COPY / "+model.price+" cr",()=>{var part=State.BuyModule(model.id);if(part!=null){selectedRigId=part.instanceId;State.PlaceFirstFree(part.instanceId);Beep(true);}},"buy-"+model.id,"secondary-button");buy.SetEnabled(preview.allowed);
                    if(!preview.allowed)Text(card,preview.reason,"micro amber");
                }
            }
            var middle=El(workspace,"network-panel");var map=El(middle,"map-content");BuildRigIssues(map);var head=El(map,"map-heading");
            var titles=El(head,"map-title-block");
            var titleRow=El(titles,"rig-title-row");
            Text(titleRow,State.Chassis.title.ToUpperInvariant()+" / "+State.Chassis.rows+"x"+State.Chassis.columns,"map-title");
            Text(titleRow,"DRAG TO PLACE / R OR WHEEL ROTATES","micro muted");
            BuildRigBudgets(head);
            canvas=new GraphViewport(BuildRackScene(),SlotClicked,rigCamera,true);map.Add(canvas);
            const float cellPitch=4f,cellInset=.32f;
            foreach(var node in canvas.Query<Button>().ToList())
            {
                if(!node.name.StartsWith("slot-"))continue;
                int index=int.Parse(node.name.Substring(5)),row=index/State.Chassis.columns,column=index%State.Chassis.columns;
                if(State.PartAt(row,column)!=null){node.style.display=DisplayStyle.None;continue;}
                node.AddToClassList("rig-cell");
                canvas.ConfigureFlatNode(node.name,Vector2.one*(cellPitch-cellInset),"Empty cell "+(row+1)+", "+(column+1));
                node.SetEnabled(!State.Active);
            }
            foreach(var part in State.InstalledParts)
            {
                var model=State.Module(part.modelId);var size=State.PartSize(part);
                var footprint=new Vector2(size.x*cellPitch-cellInset,size.y*cellPitch-cellInset);
                canvas.ConfigureFlatNode("part-"+part.instanceId,footprint,ModuleIntel(model),module:true);
                var module=canvas.Q<Button>("part-"+part.instanceId);
                module?.AddToClassList("rig-module");module?.AddToClassList(IntelTooltipClass);
                if(module!=null)SetPartContent(module,model,model.title,18);
                module?.RegisterCallback<PointerDownEvent>(evt=>BeginRigDrag(part.instanceId,evt),TrickleDown.TrickleDown);
            }
            var footer=El(map,"map-footer");
            Btn(footer,"-",()=>canvas.Zoom(1.15f),"rig-zoom-out","map-button");Btn(footer,"+",()=>canvas.Zoom(.85f),"rig-zoom-in","map-button");Btn(footer,"FIT",()=>canvas.ResetView(),"rig-fit","map-button");
            var right=El(workspace,"right-panel");
            var details=Scroll(right,"rig-details");
            var inventory=El(right,"rig-inventory");inventoryDropZone=inventory;
            BuildRigInventory(inventory,categories);
            BuildRigDetails(details);
        }
        // Shown above the rack on every edit. Red entries block connecting; amber entries are advisory.
        private void BuildRigIssues(VisualElement map)
        {
            var issues=State.RigIssues;
            var panel=El(map,"rig-issues");panel.name="rig-issues";
            panel.style.display=issues.Count==0?DisplayStyle.None:DisplayStyle.Flex;
            foreach(var issue in issues)
            {
                bool error=issue.severity==RigIssueSeverity.Error;
                Text(panel,(error?"ERROR / ":"WARNING / ")+issue.message,"small rig-issue "+(error?"rig-issue-error":"rig-issue-warning"));
            }
        }
        private void BuildRigBudgets(VisualElement head)
        {
            var budgets=El(head,"rig-budgets");
            var stats=El(budgets,"rig-stat-line");
            RigStat(stats,"power",Palette.Hex("A8C2BC"),"POWER / "+State.PowerUsed+" / "+State.PowerLimit,"small");
            RigStat(stats,"cooling",Palette.Hex("A8C2BC"),"COOLING / "+State.HeatUsed+" / "+State.CoolingLimit,"small");
            RigStat(stats,"processing",Palette.Hex("60D8D0"),"COMPUTE / "+State.Compute.ToString("0.00")+" GFLOP/s","small cyan");
            RigStat(stats,"transfer",Palette.Hex("60D8D0"),"BANDWIDTH / "+State.Bandwidth.ToString("0.00")+" GB/s","small cyan");
            RigStat(stats,"storage",Palette.Hex("60D8D0"),"STORAGE / "+State.StorageCapacity.ToString("0")+" GB","micro cyan");
            RigStat(stats,"memory",Palette.Hex("60D8D0"),"RAM / "+State.MemoryPool.ToString("0.#")+" GB","micro cyan");
            RigStat(stats,"reclaim",Palette.Hex("60D8D0"),"RECLAIM / "+State.MemoryReclaim.ToString("0.#")+" GB/s","micro cyan");
            RigStat(stats,"slots",Palette.Hex("60D8D0"),"SLOTS / "+State.Slots,"micro cyan");
            var actions=El(budgets,"rig-budget-actions");
            Text(actions,State.RigValid?"BUILD READY":"BUILD INVALID / CONNECTION BLOCKED",State.RigValid?"micro lime":"micro amber");
            if(State.NextChassis!=null)
            {
                var preview=State.PreviewChassis();
                var upgrade=Btn(actions,"EXPAND CHASSIS / "+State.NextChassis.title+" / "+State.NextChassis.price+" cr",()=>{State.UpgradeChassis();rigCamera.Reset();},"upgrade-chassis","secondary-button");upgrade.SetEnabled(preview.allowed);
                Text(actions,preview.reason,"micro muted");
            }
        }
        // Each readout pairs its value with the icon of the thing it measures.
        private static void RigStat(VisualElement line,string glyph,Color tint,string text,string classes)
        {
            OperationStat(line,glyph,tint,text,classes);
        }
        private static Label OperationStat(VisualElement line,string glyph,Color tint,string text,string classes)
        {
            var stat=El(line,"rig-stat");
            var icon=new RigGlyph(glyph,tint,14);icon.AddToClassList("rig-stat-icon");stat.Add(icon);
            return RichText(stat,text,classes);
        }
        // Rig parts show their icon beside the title. The button text is cleared so both share one row.
        private static void SetPartContent(Button button,RigModuleDefinition model,string title,float iconSize)
        {
            button.text="";
            var content=new VisualElement();content.AddToClassList("rig-part-content");
            content.Add(RigGlyph.ForModule(model,Palette.Branch(model.branch),iconSize));
            var label=new Label(title){pickingMode=PickingMode.Ignore};label.AddToClassList("rig-part-title");content.Add(label);
            button.Add(content);
        }
        private void BuildRigDetails(VisualElement info)
        {
            Text(info,"SELECTED COMPONENT","eyebrow cyan");
            var selected=State.Part(selectedRigId);
            if(selected==null)Text(info,"Select a component in the rack or the inventory.","body-copy muted section-space");
            else
            {
                var model=State.Module(selected.modelId);var branch=Palette.Branch(model.branch);
                Text(info,model.title,"mission-title section-space").style.color=branch;
                Text(info,model.branch+" / "+(model.family??"").ToUpperInvariant()+" / "+(selected.Installed?"INSTALLED":"INVENTORY"),"micro section-space").style.color=branch;
                RichText(info,ModuleSpecs(model),"small");
                Text(info,"ROTATION / "+(selected.rotated?"90":"0")+" DEGREES","micro muted");
                Btn(info,"PLACE IN FIRST FREE SPACE",()=>{if(!State.PlaceFirstFree(selected.instanceId,selected.rotated)){rigMessage="No valid space for this footprint.";dirty=true;}},"rig-place-first","secondary-button").SetEnabled(!State.Active);
                Btn(info,"ROTATE / R",RotateRigSelection,"rig-rotate","secondary-button").SetEnabled(!State.Active&&model.rotatable&&State.Catalog.Data.rig.allowRotation);
                Btn(info,"REMOVE TO INVENTORY",()=>State.RemoveModule(selected.instanceId),"rig-remove","secondary-button").SetEnabled(!State.Active&&selected.Installed);
                Btn(info,"SELL / "+State.ModuleSalePrice(selected.instanceId)+" cr",()=>State.SellModule(selected.instanceId),"rig-sell","danger-button").SetEnabled(!State.Active);
            }
            if(rigMessage!=null)Text(info,rigMessage,"small amber section-space");
            Text(info,State.Active?"Disconnect before changing hardware.":"Drag parts between the rack and the inventory, or select a part and click a cell. Sales return "+(State.Catalog.Data.rig.saleFraction*100).ToString("0")+"% of the paid price.","small muted section-space");
            // Extension hardware remains authorable for mods; the shipped catalog uses rack modules.
            foreach(var h in State.Catalog.Hardware)
            {
                var button=Btn(info,h.title+" / "+State.UpgradePrice(h.id)+" cr",()=>State.Upgrade(h.id),"upgrade-"+h.id,"secondary-button");button.SetEnabled(State.PreviewUpgrade(h.id).allowed);
            }
        }
        private GraphScene BuildRackScene()
        {
            var document=new GraphDocument("rig","Rig rack");var schema=new GraphSchema();
            schema.Register(new GraphNodeTypeDefinition("empty","Empty","default",Palette.Hex("243A40"),.2f));
            foreach(var branch in State.Modules.Select(m=>m.branch).Distinct())schema.Register(new GraphNodeTypeDefinition(branch,branch,"default",Palette.Branch(branch),.6f));
            for(int row=0;row<State.Chassis.rows;row++)for(int col=0;col<State.Chassis.columns;col++)
            {
                int index=row*State.Chassis.columns+col;
                document.UpsertNode(new GraphNodeRecord("slot-"+index,"empty",""));
            }
            foreach(var part in State.InstalledParts)document.UpsertNode(new GraphNodeRecord("part-"+part.instanceId,State.Module(part.modelId).branch,State.Module(part.modelId).title));
            var scene=new GraphScene(document,schema);
            for(int row=0;row<State.Chassis.rows;row++)for(int col=0;col<State.Chassis.columns;col++){var node=scene.Nodes["slot-"+(row*State.Chassis.columns+col)];node.Position=new Vector3(col*4,-row*4,0);node.Pinned=true;}
            foreach(var part in State.InstalledParts){var size=State.PartSize(part);var node=scene.Nodes["part-"+part.instanceId];node.Position=new Vector3((part.column+(size.x-1)*.5f)*4,-(part.row+(size.y-1)*.5f)*4,0);node.Pinned=true;}
            return scene;
        }
        private string ModuleTooltip(RigModuleDefinition model)=>model.title+" / "+ModuleSpecs(model);
        // Same layout as the graph node intel card: lime title over light body text.
        private string ModuleIntel(RigModuleDefinition model)=>"<color=#9BFACB><b>"+model.title+"</b></color>\n"+ModuleSpecs(model);
        private string ModuleSpecs(RigModuleDefinition model)=>"TIER "+model.tier+" / "+model.width+"x"+model.height+"\n"+model.description+"\n"+model.powerDemand+" POWER / "+model.heatOutput+" HEAT / +"+model.powerCapacity+" POWER LIMIT / +"+model.coolingCapacity+" COOLING LIMIT\n"+model.synergy+"\n"+model.flavour;
        private IEnumerable<string> RigCategories()=>State.Modules.Select(m=>m.family).Where(f=>!string.IsNullOrEmpty(f)).Distinct()
            .OrderBy(f=>{int i=Array.IndexOf(RigCategoryOrder,f);return i<0?RigCategoryOrder.Length:i;}).ThenBy(f=>f,StringComparer.Ordinal);
        private static string RigCategoryTitle(string category)=>category=="ALL"?"ALL PARTS":category.ToUpperInvariant();
        private void BuildRigInventory(VisualElement parent,List<string> categories)
        {
            Text(parent,"INVENTORY","eyebrow cyan");
            var tabs=El(parent,"rig-category-tabs");
            var tabKeys=new List<string>{"ALL"};tabKeys.AddRange(categories);
            foreach(var category in tabKeys)
            {
                var key=category;bool active=rigCategory==key;
                var tab=Btn(tabs,"",()=>{rigCategory=key;dirty=true;Beep();},"rig-category-"+key,"rig-category-tab");
                tab.EnableInClassList("active",active);tab.tooltip=RigCategoryTitle(key);
                tab.Add(new RigGlyph(key,active?Palette.Lime:Palette.Hex("7C919D"),20));
            }
            var parts=State.Career.rigParts.Where(p=>!p.Installed&&(rigCategory=="ALL"||State.Module(p.modelId).family==rigCategory)).ToList();
            Text(parent,RigCategoryTitle(rigCategory)+" / "+parts.Count+" SPARE","eyebrow section-space");
            var list=Scroll(parent,"rig-inventory-list");
            if(parts.Count==0)Text(list,"No spare parts here. Buy copies in the module exchange, or drag installed parts into this panel.","small muted");
            foreach(var part in parts)
            {
                var model=State.Module(part.modelId);
                var row=Btn(list,"",()=>{selectedRigId=part.instanceId;rigMessage=null;dirty=true;},"owned-"+part.instanceId,"ability-button rig-inventory-row");
                SetPartContent(row,model,(part.instanceId==selectedRigId?"> ":"")+model.title,20);
                row.tooltip=ModuleIntel(model);row.AddToClassList(IntelTooltipClass);row.style.color=Palette.Branch(model.branch);
                row.RegisterCallback<PointerDownEvent>(evt=>BeginRigDrag(part.instanceId,evt),TrickleDown.TrickleDown);
            }
        }
        private void SlotClicked(string id)
        {
            if(State.Active)return;
            if(id.StartsWith("part-")){selectedRigId=id.Substring(5);rigMessage=null;dirty=true;return;}
            if(!id.StartsWith("slot-"))return;int index=int.Parse(id.Substring(5));int row=index/State.Chassis.columns,column=index%State.Chassis.columns;
            var part=State.Part(selectedRigId);
            if(part!=null&&State.TryPlaceModule(part.instanceId,row,column,part.rotated)){rigMessage=null;return;}
            var occupied=State.PartAt(row,column);if(occupied!=null)selectedRigId=occupied.instanceId;
            else rigMessage="The selected footprint does not fit here.";dirty=true;
        }
        private void RotateRigSelection()
        {
            if(dragRigId!=null){RotateRigDrag();return;}
            if(selectedRigId!=null&&!State.RotateModule(selectedRigId)){rigMessage="Rotation is blocked. Remove the part to inventory or choose another cell.";dirty=true;}
        }
        internal void RotateRigDrag()
        {
            if(dragRigId==null)return;
            var model=State.Module(State.Part(dragRigId).modelId);
            if(!model.rotatable||!State.Catalog.Data.rig.allowRotation)return;
            dragRotated=!dragRotated;
            if(dragMoved)RefreshRigDragGhost();
        }
        private void BeginRigDrag(string instance,PointerDownEvent evt)
        {
            if(State.Active||evt.button!=0)return;
            var part=State.Part(instance);
            dragRigId=selectedRigId=instance;dragPointer=evt.pointerId;dragRotated=part.rotated;dragMoved=false;dragLifted=false;
            dragRestoreRow=part.row;dragRestoreCol=part.column;dragRestoreRot=part.rotated;
            dragOrigin=lastRigDragPanelPos=evt.position;root.CapturePointer(evt.pointerId);evt.StopImmediatePropagation();
        }
        private bool TryNearestRigAnchor(Vector2 panelPosition,Vector2Int size,out int row,out int column)
        {
            row=column=0;
            if(canvas==null||size.x<=0||size.y<=0)return false;
            int cols=State.Chassis.columns,rows=State.Chassis.rows;
            if(!TryGetRigGridBounds(out var grid,out var cellW,out var cellH))return false;
            if(!grid.Contains(panelPosition))return false;
            int nearestRow=0,nearestCol=0;float nearestDist=float.MaxValue;
            for(int r=0;r<rows;r++)for(int c=0;c<cols;c++)
            {
                if(!TrySlotPanelCenter(r,c,out var center))continue;
                float dist=(center-panelPosition).sqrMagnitude;
                if(dist<nearestDist){nearestDist=dist;nearestRow=r;nearestCol=c;}
            }
            row=Mathf.Clamp(nearestRow,0,rows-size.y);
            column=Mathf.Clamp(nearestCol,0,cols-size.x);
            return true;
        }
        private bool TryRigGridMetrics(out Rect originCell,out float cellWidth,out float cellHeight)
        {
            originCell=default;cellWidth=cellHeight=0;
            if(canvas==null)return false;
            int cols=State.Chassis.columns,rows=State.Chassis.rows;
            if(!TrySlotButtonRect(0,0,out originCell))return false;
            cellWidth=originCell.width;cellHeight=originCell.height;
            if(cols>1&&TrySlotButtonRect(0,1,out var nextColumn))cellWidth=nextColumn.xMin-originCell.xMin;
            if(rows>1&&TrySlotButtonRect(1,0,out var nextRow))cellHeight=nextRow.yMin-originCell.yMin;
            return cellWidth>1&&cellHeight>1;
        }
        private bool TryGetRigGridBounds(out Rect grid,out float cellWidth,out float cellHeight)
        {
            grid=default;
            if(!TryRigGridMetrics(out var origin,out cellWidth,out cellHeight))return false;
            int cols=State.Chassis.columns,rows=State.Chassis.rows;
            grid=new Rect(origin.xMin,origin.yMin,cellWidth*cols,cellHeight*rows);
            return grid.width>1&&grid.height>1;
        }
        private bool TrySlotButtonRect(int row,int column,out Rect rect)
        {
            rect=default;
            if(canvas==null)return false;
            int cols=State.Chassis.columns;
            var slot=canvas.Q<Button>("slot-"+(row*cols+column));
            if(slot==null)return false;
            rect=slot.worldBound;
            return rect.width>1&&rect.height>1;
        }
        private bool TrySlotPanelCenter(int row,int column,out Vector2 center)
        {
            center=default;
            if(!TrySlotPanelRect(row,column,out var rect))return false;
            center=new Vector2(rect.center.x,rect.center.y);
            return true;
        }
        private bool TrySlotPanelRect(int row,int column,out Rect rect)
        {
            rect=default;
            if(!TryRigGridMetrics(out var origin,out var cellW,out var cellH))return false;
            rect=new Rect(origin.xMin+column*cellW,origin.yMin+row*cellH,cellW,cellH);
            return true;
        }
        private void UpdateRigDrag(Vector2 position)
        {
            if(dragRigId==null)return;
            lastRigDragPanelPos=position;
            dragMoved|=Vector2.Distance(position,dragOrigin)>5;
            if(!dragMoved)return;
            if(!dragLifted)LiftRigDragPart();
            RefreshRigDragGhost();
            inventoryDropZone?.EnableInClassList("rig-drop-zone",IsOverRigInventory(position));
        }
        private bool IsOverRigInventory(Vector2 panelPosition)=>inventoryDropZone!=null&&inventoryDropZone.worldBound.Contains(panelPosition);
        private void LiftRigDragPart()
        {
            dragLifted=true;
            var part=State.Part(dragRigId);
            if(part.Installed)State.RemoveModule(dragRigId);
            RevealEmptyRigSlots();
            var lifted=canvas?.Q<Button>("part-"+dragRigId);
            if(lifted!=null)lifted.style.display=DisplayStyle.None;
            EnsureRigDragGhost(State.Module(part.modelId));
        }
        private void RestoreRigDragPart()
        {
            if(!dragLifted||dragRestoreRow<0)return;
            State.TryPlaceModule(dragRigId,dragRestoreRow,dragRestoreCol,dragRestoreRot);
        }
        private void RevealEmptyRigSlots()
        {
            if(canvas==null)return;
            int cols=State.Chassis.columns;
            foreach(var btn in canvas.Query<Button>().ToList())
            {
                if(!btn.name.StartsWith("slot-"))continue;
                int index=int.Parse(btn.name.Substring(5));
                if(State.PartAt(index/cols,index%cols)==null)btn.style.display=DisplayStyle.Flex;
            }
        }
        private void EnsureRigDragGhost(RigModuleDefinition model)
        {
            if(dragGhost!=null)return;
            var branch=Palette.Branch(model.branch);
            dragGhost=new Button{ name="rig-drag-ghost",pickingMode=PickingMode.Ignore};
            dragGhost.AddToClassList("rig-module");dragGhost.AddToClassList("rig-drag-ghost");
            SetPartContent(dragGhost,model,model.title,18);
            dragGhost.style.position=Position.Absolute;
            dragGhost.style.backgroundColor=new Color(branch.r*.22f,branch.g*.22f,branch.b*.22f,.94f);
            dragGhost.style.borderBottomColor=dragGhost.style.borderTopColor=branch;
            dragGhost.style.borderLeftColor=dragGhost.style.borderRightColor=branch;
            root.Add(dragGhost);
        }
        private void RefreshRigDragGhost()
        {
            var part=State.Part(dragRigId);if(part==null)return;
            var model=State.Module(part.modelId);EnsureRigDragGhost(model);
            var size=GameSimulation.PartSize(model,dragRotated);
            ClearDragDropHighlights();
            int row=0,column=0;
            bool overRack=TryNearestRigAnchor(lastRigDragPanelPos,size,out row,out column);
            bool valid=overRack&&State.CanPlace(dragRigId,row,column,dragRotated);
            if(overRack)HighlightFootprint(row,column,size,valid);
            dragGhost.EnableInClassList("rig-drag-invalid",overRack&&!valid);
            PositionRigDragGhost(size);
        }
        private void ClearDragDropHighlights()
        {
            foreach(var slot in dragValidSlots)
            {
                slot.RemoveFromClassList("rig-drop-valid");
                slot.RemoveFromClassList("rig-drop-invalid");
            }
            dragValidSlots.Clear();
        }
        private void HighlightFootprint(int row,int column,Vector2Int size,bool valid)
        {
            int cols=State.Chassis.columns;
            string cls=valid?"rig-drop-valid":"rig-drop-invalid";
            for(int y=row;y<row+size.y;y++)for(int x=column;x<column+size.x;x++)
            {
                var slot=canvas?.Q<Button>("slot-"+(y*cols+x));
                if(slot==null)continue;
                slot.style.display=DisplayStyle.Flex;
                slot.AddToClassList(cls);
                dragValidSlots.Add(slot);
            }
        }
        private Vector2 RigCellPanelSize()
        {
            var cell=canvas?.Q<Button>(className:"rig-cell");
            if(cell!=null&&cell.worldBound.width>1)return new Vector2(cell.worldBound.width,cell.worldBound.height);
            return new Vector2(72,48);
        }
        // The ghost always follows the cursor, RPG-inventory style. Grid cells show the landing footprint instead of snapping the ghost.
        private void PositionRigDragGhost(Vector2Int size)
        {
            var cell=RigCellPanelSize();
            float width=cell.x*size.x,height=cell.y*size.y;
            var pointer=root.WorldToLocal(lastRigDragPanelPos);
            dragGhost.style.left=pointer.x-width*.5f;dragGhost.style.top=pointer.y-height*.5f;
            dragGhost.style.width=width;dragGhost.style.height=height;
        }
        private void EndRigDrag(PointerUpEvent evt)
        {
            if(dragRigId==null||evt.pointerId!=dragPointer)return;
            bool placed=false;
            if(dragMoved&&IsOverRigInventory(evt.position))
            {
                // Dropping on the inventory keeps a lifted part out of the rack (it is already unplaced).
                placed=true;rigMessage=null;
            }
            else if(dragMoved)
            {
                var part=State.Part(dragRigId);
                var size=GameSimulation.PartSize(State.Module(part.modelId),dragRotated);
                if(TryNearestRigAnchor(evt.position,size,out int row,out int column))
                    placed=State.TryPlaceModule(dragRigId,row,column,dragRotated);
                if(!placed&&TryNearestRigAnchor(evt.position,size,out _,out _))rigMessage="That footprint does not fit here.";
            }
            CancelRigDrag(placed);dirty=true;evt.StopPropagation();
        }
        private void CancelRigDrag(bool placed=false)
        {
            if(dragLifted&&!placed)RestoreRigDragPart();
            if(dragRigId!=null&&root!=null&&root.HasPointerCapture(dragPointer))root.ReleasePointer(dragPointer);
            ClearDragDropHighlights();
            inventoryDropZone?.RemoveFromClassList("rig-drop-zone");
            dragRigId=null;dragLifted=false;dragRestoreRow=-1;
            dragGhost?.RemoveFromHierarchy();dragGhost=null;
        }
        private void RefreshResourceActions()
        {
            if(!State.Active)return;
            foreach(var action in State.AvailableActions())
            {
                var tech=State.Catalog.Technologies.FirstOrDefault(t=>t.grants.Contains(action.id));if(tech==null)continue;
                var button=root.Q<Button>("ability-"+tech.id);button?.SetEnabled(State.Preview(action.id).allowed);
            }
            foreach(var action in State.AvailableActions(selectedNode))
            {
                var button=root.Q<Button>("action-"+action.id);if(button==null)continue;
                var preview=State.Preview(action.id,selectedNode);bool queued=State.Op.task!=null&&!action.Definition.instant&&!State.IsAbilityActive(action.id,selectedNode);
                button.SetEnabled(queued?State.CanQueue(action.id,selectedNode):preview.allowed);
                var estimate=button.Q<Label>(className:"action-estimate");if(estimate!=null&&!queued)estimate.text=preview.allowed?preview.duration.ToString("0.0")+"s / +"+preview.trace.ToString("0.0")+" trace / "+action.Definition.memoryCost+" GB":preview.reason;
            }
        }
        private void BuildStorageBrief(VisualElement parent)
        {
            var network=NetworkGenerator.Generate(selectedJob,State.Career,State.Catalog);var definition=State.Catalog.Contract(selectedJob.id);
            Text(parent,"STORAGE PLAN","eyebrow section-space");
            foreach(var objective in definition.objectives)
            {
                var node=network.nodes.First(n=>n.id==objective.nodeId);
                RichText(parent,(string.IsNullOrEmpty(objective.label)?node.label:objective.label)+": "+(objective.payload?node.storage.ToString("0")+" GB":"REMOTE / NO STORAGE"),"small");
            }
            int index=0;foreach(var archive in network.nodes.Where(n=>n.bonus))Text(parent,(selectedJob.Intel==ContractIntel.Full?archive.label:"ARCHIVE "+(++index))+": "+archive.storage.ToString("0")+" GB","small muted");
            float required=network.nodes.Where(n=>n.payload).Sum(n=>n.storage);RichText(parent,"NEEDS "+required.ToString("0")+" GB / YOUR STORAGE "+State.StorageCapacity.ToString("0")+" GB","small cyan");
            var track=El(parent,"meter-track");var fill=El(track,"meter-fill");fill.style.width=Length.Percent(Mathf.Clamp01(required/Mathf.Max(1,State.StorageCapacity))*100);fill.style.backgroundColor=required<=State.StorageCapacity?Palette.Cyan:Palette.Red;
        }
        private void BuildRefundModal()
        {
            overlay=El(root,"modal-overlay");var box=Scroll(overlay,"modal-box");Text(box,"REFUND RESEARCH","large-heading");
            Text(box,State.PreviewResearchRefund().description,"body-copy");
            Btn(box,"CONFIRM REFUND",()=>{State.RefundResearch();modal=null;dirty=true;},"confirm-research-refund","primary-button").SetEnabled(!State.Active);
            Btn(box,"CANCEL",()=>{modal=null;dirty=true;},"cancel-research-refund","secondary-button");
        }
    }
}
