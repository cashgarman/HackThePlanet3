using System;
using System.Collections.Generic;
using System.Linq;
using Cashg.GraphExplorer;
using UnityEngine;
using UnityEngine.UIElements;

namespace HackThePlanet
{
    public sealed partial class WorkstationApp
    {
        private readonly GraphCameraState rigCamera=new GraphCameraState();
        private string selectedRigId,dragRigId,rigMessage;
        private bool dragRotated,dragMoved,dragLifted;
        private int dragPointer,dragRestoreRow=-1,dragRestoreCol=-1;
        private bool dragRestoreRot;
        private Vector2 dragOrigin,lastRigDragPanelPos;
        private Button dragGhost;
        private readonly List<Button> dragValidSlots=new List<Button>();

        private void BuildRig(VisualElement workspace)
        {
            if(State.Part(selectedRigId)==null)selectedRigId=null;
            var left=El(workspace,"left-panel");var shop=Scroll(left,"rig-shop");
            Text(shop,"MODULE EXCHANGE","eyebrow cyan");Text(shop,"Buy copies. Pack your signal.","small muted");
            foreach(var branch in State.Modules.GroupBy(m=>m.branch))
            {
                Text(shop,branch.Key,"eyebrow section-space").style.color=Palette.Branch(branch.Key);
                foreach(var model in branch)
                {
                    var card=El(shop,"rig-shop-card");card.tooltip=ModuleTooltip(model)+"\n"+State.PreviewModule(model.id).reason;
                    Text(card,model.title+" / T"+model.tier,"small").style.color=Palette.Branch(model.branch);
                    RichText(card,model.width+"x"+model.height+" / "+model.price+" cr / "+model.powerDemand+" POWER / "+model.heatOutput+" HEAT","micro muted");
                    RichText(card,model.description,"small");
                    var preview=State.PreviewModule(model.id);
                    var buy=Btn(card,"BUY COPY / "+model.price+" cr",()=>{var part=State.BuyModule(model.id);if(part!=null){selectedRigId=part.instanceId;State.PlaceFirstFree(part.instanceId);Beep(true);}},"buy-"+model.id,"secondary-button");buy.SetEnabled(preview.allowed);
                    if(!preview.allowed)Text(card,preview.reason,"micro amber");
                }
            }
            var middle=El(workspace,"network-panel");var map=El(middle,"map-content");var head=El(map,"map-heading");
            Text(head,State.Chassis.title.ToUpperInvariant()+" / "+State.Chassis.rows+"x"+State.Chassis.columns,"map-title");
            Text(head,"DRAG TO PLACE / R OR WHEEL ROTATES","micro muted");
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
                canvas.ConfigureFlatNode("part-"+part.instanceId,footprint,ModuleTooltip(model),module:true);
                var module=canvas.Q<Button>("part-"+part.instanceId);
                module?.AddToClassList("rig-module");
                module?.RegisterCallback<PointerDownEvent>(evt=>BeginRigDrag(part.instanceId,evt),TrickleDown.TrickleDown);
            }
            var footer=El(map,"map-footer");
            Btn(footer,"-",()=>canvas.Zoom(1.15f),"rig-zoom-out","map-button");Btn(footer,"+",()=>canvas.Zoom(.85f),"rig-zoom-in","map-button");Btn(footer,"FIT",()=>canvas.ResetView(),"rig-fit","map-button");
            var right=El(workspace,"right-panel");var info=Scroll(right,"rig-inspector");
            Text(info,"BUILD BUDGETS","eyebrow cyan");
            RichText(info,"POWER / "+State.PowerUsed+" / "+State.PowerLimit+"\nCOOLING / "+State.HeatUsed+" / "+State.CoolingLimit,"body-copy");
            Text(info,"BASE / "+State.Chassis.powerCapacity+" POWER / "+State.Chassis.coolingCapacity+" COOLING","micro muted");
            RichText(info,"STORAGE / "+State.StorageCapacity+" GB\nRAM / "+State.MemoryPool+" MEM / "+State.MemoryReclaim+" MEM/s\nABILITY SLOTS / "+State.Slots,"small cyan");
            foreach(var problem in State.RigProblems)Text(info,problem,"small amber");
            Text(info,State.RigValid?"BUILD READY":"BUILD INVALID / CONNECTION BLOCKED",State.RigValid?"eyebrow lime":"eyebrow amber");
            if(State.NextChassis!=null)
            {
                Text(info,State.NextChassis.title+" / "+State.NextChassis.rows+"x"+State.NextChassis.columns+" / "+State.NextChassis.price+" cr","small section-space");
                var preview=State.PreviewChassis();var upgrade=Btn(info,"EXPAND CHASSIS",()=>{State.UpgradeChassis();rigCamera.Reset();},"upgrade-chassis","secondary-button");upgrade.SetEnabled(preview.allowed);Text(info,preview.reason,"micro muted");
            }
            Text(info,"OWNED COPIES / "+State.Career.rigParts.Count,"eyebrow section-space");
            foreach(var part in State.Career.rigParts)
            {
                var model=State.Module(part.modelId);var row=Btn(info,(part.instanceId==selectedRigId?"> ":"")+model.title+(part.Installed?" [INSTALLED]":" [INVENTORY]"),()=>{selectedRigId=part.instanceId;dirty=true;},"owned-"+part.instanceId,"ability-button");row.tooltip=ModuleTooltip(model);row.RegisterCallback<PointerDownEvent>(evt=>BeginRigDrag(part.instanceId,evt),TrickleDown.TrickleDown);
            }
            var selected=State.Part(selectedRigId);
            if(selected!=null)
            {
                var model=State.Module(selected.modelId);Text(info,model.title,"mission-title section-space");RichText(info,ModuleTooltip(model),"small");
                Text(info,"ROTATION / "+(selected.rotated?"90":"0")+" DEGREES","micro muted");
                Btn(info,"PLACE IN FIRST FREE SPACE",()=>{if(!State.PlaceFirstFree(selected.instanceId,selected.rotated)){rigMessage="No valid space for this footprint.";dirty=true;}},"rig-place-first","secondary-button").SetEnabled(!State.Active);
                Btn(info,"ROTATE / R",RotateRigSelection,"rig-rotate","secondary-button").SetEnabled(!State.Active&&model.rotatable&&State.Catalog.Data.rig.allowRotation);
                Btn(info,"REMOVE TO INVENTORY",()=>State.RemoveModule(selected.instanceId),"rig-remove","secondary-button").SetEnabled(!State.Active&&selected.Installed);
                Btn(info,"SELL / "+State.ModuleSalePrice(selected.instanceId)+" cr",()=>State.SellModule(selected.instanceId),"rig-sell","danger-button").SetEnabled(!State.Active);
            }
            if(rigMessage!=null)Text(info,rigMessage,"small amber section-space");
            Text(info,State.Active?"Disconnect before changing hardware.":"Select a copy, then click a cell. Removed parts stay in inventory. Sales return "+(State.Catalog.Data.rig.saleFraction*100).ToString("0")+"% of the paid price.","small muted section-space");
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
        private string ModuleTooltip(RigModuleDefinition model)=>model.title+" / TIER "+model.tier+" / "+model.width+"x"+model.height+"\n"+model.description+"\n"+model.powerDemand+" POWER / "+model.heatOutput+" HEAT / +"+model.powerCapacity+" POWER LIMIT / +"+model.coolingCapacity+" COOLING LIMIT\n"+model.synergy+"\n"+model.flavour;
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
        }
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
            dragGhost=new Button{ name="rig-drag-ghost",text=model.title,pickingMode=PickingMode.Ignore};
            dragGhost.AddToClassList("rig-module");dragGhost.AddToClassList("rig-drag-ghost");
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
            dragGhost.text=model.title;
            dragGhost.EnableInClassList("rig-drag-invalid",overRack&&!valid);
            PositionRigDragGhost(row,column,size,overRack);
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
        private void PositionRigDragGhost(int row,int column,Vector2Int size,bool snapToGrid)
        {
            float width,height,left,top;
            if(snapToGrid&&TryFootprintPanelRect(row,column,size,out left,out top,out width,out height))
            {
                dragGhost.style.left=left;dragGhost.style.top=top;
                dragGhost.style.width=width;dragGhost.style.height=height;return;
            }
            var cell=RigCellPanelSize();
            width=cell.x*size.x;height=cell.y*size.y;
            dragGhost.style.left=pointerPosition.x-width*.5f;dragGhost.style.top=pointerPosition.y-height*.5f;
            dragGhost.style.width=width;dragGhost.style.height=height;
        }
        private bool TryFootprintPanelRect(int row,int column,Vector2Int size,out float left,out float top,out float width,out float height)
        {
            left=top=width=height=0;
            if(canvas==null)return false;
            int cols=State.Chassis.columns;
            if(!TrySlotPanelRect(row,column,out var anchorBound))return false;
            if(TrySlotPanelRect(row+size.y-1,column+size.x-1,out var endBound))
            {
                width=endBound.xMax-anchorBound.xMin;height=endBound.yMax-anchorBound.yMin;
            }
            else
            {
                var cell=RigCellPanelSize();
                width=cell.x*size.x;height=cell.y*size.y;
            }
            var origin=root.WorldToLocal(new Vector2(anchorBound.xMin,anchorBound.yMin));
            left=origin.x;top=origin.y;
            return true;
        }
        private void EndRigDrag(PointerUpEvent evt)
        {
            if(dragRigId==null||evt.pointerId!=dragPointer)return;
            bool placed=false;
            if(dragMoved)
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
                var estimate=button.Q<Label>(className:"action-estimate");if(estimate!=null&&!queued)estimate.text=preview.allowed?preview.duration.ToString("0.0")+"s / +"+preview.trace.ToString("0.0")+" trace / "+action.Definition.memoryCost+" MEM":preview.reason;
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
