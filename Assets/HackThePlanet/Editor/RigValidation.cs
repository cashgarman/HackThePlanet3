using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
namespace HackThePlanet.Editor
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class RigValidation
    {
        private static readonly List<string> results=new List<string>();
        private static void Check(bool value,string message){if(!value)throw new InvalidOperationException("RIG FAILED / "+message);results.Add("PASS / "+message);}
        private static void Near(float a,float b,string message)=>Check(Mathf.Abs(a-b)<.001f,message+" / "+a);
        private static GameSimulation Rich()
        {
            var sim=new GameSimulation();sim.DebugAddCredits();sim.DebugAddXp();sim.DebugUnlockAllTech();sim.Career.jobs=6;
            while(sim.NextChassis!=null)Check(sim.UpgradeChassis(),"chassis progression upgrades sequentially");
            foreach(var part in sim.Career.rigParts.ToArray())sim.RemoveModule(part.instanceId);return sim;
        }
        [MenuItem("Hack The Planet/Rig/Validate Complete Rework")]
        public static void Run()
        {
            results.Clear();Directory.CreateDirectory("Artifacts");
            try
            {
                using(var bundle=ContentJson.Load(GameData.DirectoryPath))Check(bundle.Game.modules.Length==16&&bundle.Game.chassis.Length==4,"all shipped rig definitions load from external JSON");
                Check(!ContentFieldHelp.CheckCoverage().Any(),"all editable fields have authoring help");
                var starter=new GameSimulation();Check(starter.RigValid&&starter.Career.rigParts.Count==4&&starter.InstalledParts.Count()==4,"starter kit fills a legal 2x3 chassis");
                Near(starter.PowerUsed,9,"starter power demand");Near(starter.HeatUsed,6,"starter heat");Near(starter.StorageCapacity,100,"starter storage");Near(starter.MemoryPool,50,"starter RAM");
                Check(!starter.PreviewChassis().allowed&&!starter.PreviewModule("power-2").allowed,"chassis career and module research gates are enforced");
                starter.DebugAddCredits();var first=starter.BuyModule("processor");var second=starter.BuyModule("processor");
                Check(first!=null&&second!=null&&first.instanceId!=second.instanceId&&!first.Installed,"duplicate purchases create independent inventory copies");
                Check(!starter.PlaceFirstFree(first.instanceId),"full rack rejects automatic placement");
                var cpu=starter.Career.rigParts.First(p=>p.modelId=="processor");Check(!starter.TryPlaceModule(first.instanceId,0,0),"overlap is rejected");
                Check(starter.RemoveModule(cpu.instanceId)&&starter.TryPlaceModule(first.instanceId,0,0),"free rearrangement installs another copy");
                Check(!starter.RotateModule(first.instanceId),"rotation rechecks adjacent occupied cells");
                int credits=starter.Career.credits;Check(starter.SellModule(second.instanceId)&&starter.Career.credits-credits==225,"voluntary sale returns 75 percent of actual paid price");
                var rich=Rich();var power=rich.BuyModule("power-1");var power2=rich.BuyModule("power-2");var cooler=rich.BuyModule("cooling-2");
                Check(rich.TryPlaceModule(power.instanceId,0,0)&&rich.TryPlaceModule(power2.instanceId,1,0)&&rich.TryPlaceModule(cooler.instanceId,3,0),"mixed researched infrastructure tiers fit independently");
                Near(rich.PowerLimit,88,"power contributions stack");Near(rich.CoolingLimit,58,"cooling contribution stacks");Near(rich.PowerUsed,3,"cooling consumes power");Near(rich.HeatUsed,7,"power supplies produce heat");
                Check(rich.TryPlaceModule(cooler.instanceId,3,2,true)&&rich.PartSize(cooler).x==1,"rotated footprints swap dimensions");
                Check(!rich.TryPlaceModule(cooler.instanceId,4,5,true),"rotated boundary overflow is rejected");
                var storage=rich.BuyModule("storage-array");var drive1=rich.BuyModule("drive");var drive2=rich.BuyModule("drive");
                foreach(var part in rich.InstalledParts.ToArray())rich.RemoveModule(part.instanceId);
                rich.TryPlaceModule(storage.instanceId,0,0);rich.TryPlaceModule(drive1.instanceId,2,0);rich.TryPlaceModule(drive2.instanceId,0,2,true);
                Check(rich.RigNeighborCount(storage.instanceId,"storage")==2,"large touching edges count each adjacent copy once");Near(rich.StorageCapacity,380,"array adjacency adds bounded storage");
                var paid=rich.BuyModule("power-3");rich.TryPlaceModule(paid.instanceId,3,0);
                int owned=rich.Career.rigParts.Count;var refund=rich.PreviewResearchRefund();Check(refund.copies>=3&&refund.credits>=paid.paidPrice,"research refund previews full-price sales of newly locked copies");
                credits=rich.Career.credits;Check(rich.RefundResearch()&&rich.Career.credits-credits==refund.credits&&rich.Career.rigParts.Count==owned-refund.copies,"research refund applies matching sales atomically");
                LimitsAndBudgetFailures();StorageAndRam();ValidatePayloadBalance(Check);Authoring();
                GameplayAssetGenerator.Generate();
                using(var bundle=ContentJson.Load(GameData.DirectoryPath))foreach(var entry in bundle.Manifest.entries)
                {
                    var asset=AssetDatabase.LoadAssetAtPath<ScriptableObject>(GameplayAssetGenerator.Output+"/"+entry.key+".asset");
                    Check(asset!=null&&ContentJson.Encode(asset)==ContentJson.Encode(bundle.Objects[entry.key]),"generated asset parity / "+entry.file);
                }
                var guids=AssetDatabase.FindAssets("t:RigModuleDefinition",new[]{GameplayAssetGenerator.Output}).OrderBy(x=>x).ToArray();GameplayAssetGenerator.Generate();Check(guids.SequenceEqual(AssetDatabase.FindAssets("t:RigModuleDefinition",new[]{GameplayAssetGenerator.Output}).OrderBy(x=>x)),"regeneration preserves generated asset GUIDs");
                results.Add("TOTAL / "+results.Count+" rig assertions passed.");File.WriteAllLines("Artifacts/rig-validation.txt",results);Debug.Log(results.Last());
            }
            catch(Exception ex){results.Add(ex.ToString());File.WriteAllLines("Artifacts/rig-validation.txt",results);throw;}
        }
        private static void LimitsAndBudgetFailures()
        {
            using(var content=ContentJson.Load(GameData.DirectoryPath))
            {
                content.Game.chassis[0].powerCapacity=9;content.Game.chassis[0].coolingCapacity=6;
                var processor=content.Game.modules.Single(m=>m.id=="processor");processor.modifiers[0].value=new ConstantNumber(100);
                content.Game.modifiers=content.Game.modifiers.Concat(new[]{new Modifier{stat="trace",operation=Arithmetic.Multiply,value=new ConstantNumber(0)}}).ToArray();
                using(var catalog=new GameCatalog(content.Game))
                {
                    var sim=new GameSimulation(catalog);Check(sim.RigValid&&sim.PowerLimit==sim.PowerUsed&&sim.CoolingLimit==sim.HeatUsed,"exact power and cooling budgets are valid");Near(sim.Stat("hackSpeed"),3,"speed stacking is capped by authored rig rules");
                    var drive=sim.Career.rigParts.First(p=>p.modelId=="drive");sim.RemoveModule(drive.instanceId);var extra=sim.BuyModule("processor");Check(extra!=null&&sim.TryPlaceModule(extra.instanceId,1,0)&&!sim.RigValid,"packing permits temporary power and cooling budget failures");
                    Check(!sim.Start(catalog.Contracts(sim.Career).First()),"invalid assembly cannot connect");sim.RemoveModule(extra.instanceId);Check(sim.RigValid&&sim.Start(catalog.Contracts(sim.Career).First()),"removing excess load restores connection eligibility");
                    Near(sim.Preview("hack","n2").trace,4.7f*.2f,"trace reductions retain the authored minimum fraction");
                }
            }
        }
        private static void StorageAndRam()
        {
            var sim=new GameSimulation();sim.DebugAddXp();sim.BuyTech("disrupt");sim.BuyTech("decoy");
            Check(sim.Start(GameContent.Story[0]),"legal rig connects");Check(sim.Decoy(),"persistent Decoy activates");Near(sim.MemoryReserved,16,"Decoy reserves RAM");
            float trace=sim.Preview("hack","n2").trace;Check(sim.Decoy()&&sim.MemoryReserved==0&&sim.Preview("hack","n2").trace>trace,"disabling Decoy restores trace pressure");
            sim.Execute("hack","n2");sim.Tick(.25f);float unprotected=sim.Op.trace;
            sim.Decoy();sim.Tick(.25f);float protectedStep=sim.Op.trace-unprotected;Near(protectedStep,unprotected*.65f,"enabling Decoy reduces trace during a running tool");
            sim.Decoy();float beforeDisable=sim.Op.trace;sim.Tick(.25f);Near(sim.Op.trace-beforeDisable,unprotected,"disabling Decoy restores a running tool's remaining trace");sim.StopTask();sim.Op.trace=0;
            sim.Node("n5").known=true;sim.Node("n6").known=sim.Node("n6").controlled=true;
            Check(sim.Execute("disrupt","n5"),"one-shot executes with free RAM");Near(sim.MemoryReclaiming,10,"one-shot occupies reclaiming RAM");
            sim.SetPaused(true);sim.Tick(.5f);Near(sim.MemoryReclaiming,10,"pause freezes RAM reclamation");sim.SetPaused(false);sim.Tick(.5f);Near(sim.MemoryReclaiming,8,"RAM reclaims in simulation time");
            Check(sim.StopTask()&&sim.MemoryReclaiming==8,"cancelling a one-shot does not refund RAM");
            sim.Op.memoryReclaiming=sim.MemoryPool;Check(!sim.Preview("disrupt","n5").allowed,"insufficient RAM blocks ability activation");sim.Op.memoryReclaiming=0;
            var archive=sim.Op.nodes.First(n=>n.bonus);archive.known=archive.controlled=true;archive.collected=true;
            Near(sim.StorageUsed,archive.storage,"collected archive occupies storage");
            var payload=sim.Op.nodes.First(n=>n.payload);payload.known=payload.controlled=true;archive.storage=sim.StorageCapacity;
            Check(!sim.Preview("download",payload.id).allowed,"full drive blocks required download");
            Check(sim.Execute("hack","n2")&&sim.Execute("discard",archive.id)&&sim.Op.task!=null,"discard is instant alongside another running tool");Near(sim.StorageUsed,0,"discard frees archive storage");sim.StopTask();
            Check(!sim.AvailableActions(payload.id).Any(a=>a.id=="discard"),"required payloads cannot be discarded");
            archive.storage=28;Check(sim.Execute("download",archive.id),"discarded archive can be downloaded again");GameValidation.FinishTask(sim);Near(sim.StorageUsed,28,"redownload occupies storage once");
            sim.Decoy();sim.Op.memoryReclaiming=5;
            var restored=new GameSimulation(JsonUtility.FromJson<Career>(JsonUtility.ToJson(sim.Career)));
            Check(restored.Op.paused&&restored.IsAbilityActive("decoy")&&restored.MemoryReclaiming==5&&restored.StorageUsed==28,"active save preserves RAM, files and reservations and restores paused");
            sim.Execute("discard",archive.id);foreach(var node in sim.Op.nodes.Where(n=>n.objective)){node.known=node.controlled=node.collected=true;}
            var report=sim.Disconnect();Check(report.success&&report.bonusFiles==0&&sim.Career.lore.Count==0,"discarded archive grants no payout or banked lore");Near(sim.StorageUsed,0,"delivery clears storage");Near(sim.MemoryReserved+sim.MemoryReclaiming,0,"delivery clears RAM state");
            foreach(bool caught in new[]{false,true}){sim.Start(GameContent.Story[1]);sim.Op.memoryReclaiming=10;sim.Disconnect(caught);Check(!sim.Active&&sim.StorageUsed==0&&sim.MemoryReserved==0&&sim.MemoryReclaiming==0,"abort/isolation reset resource state");}
            var blackout=Rich();blackout.Career.equipped=new List<string>{"blackout"};blackout.Start(GameContent.Story[0]);blackout.Node("n14").known=true;blackout.Node("n10").known=blackout.Node("n10").controlled=true;
            Check(blackout.Execute("blackout","n14")&&blackout.MemoryReserved==18,"persistent targeted activation reserves RAM immediately");
            Check(blackout.StopTask()&&blackout.MemoryReserved==0,"cancelling persistent activation releases reservation");
            blackout.Execute("blackout","n14");GameValidation.FinishTask(blackout);for(int i=0;i<100;i++)blackout.Tick(.5f);
            Check(blackout.Node("n14").suppressed>0&&blackout.MemoryReserved==18,"persistent suppression survives its former timed duration");
            blackout.SetState(blackout.Node("n14"),NodeState.Controlled,true,blackout.Context());Check(blackout.MemoryReserved==0,"capturing a suppressed hub releases RAM");
        }
        public static void ValidatePayloadBalance(Action<bool,string> check)
        {
            var catalog=GameCatalog.Default;var career=catalog.CreateCareer();
            foreach(var job in catalog.Data.contracts.Select(d=>catalog.DescribeContract(d,career)))for(int difficulty=1;difficulty<=3;difficulty++)for(int seed=0;seed<20;seed++)
            {
                job.difficulty=difficulty;job.seed=seed;var network=NetworkGenerator.Generate(job,career,catalog);
                check(network.nodes.Where(n=>n.payload).Sum(n=>n.storage)<=catalog.Data.rig.startingChassis.storageCapacity,"bare starter payload fit / "+job.id+" / "+difficulty+" / "+seed);
                var again=NetworkGenerator.Generate(job,career,catalog);if(!network.nodes.Select(n=>n.storage).SequenceEqual(again.nodes.Select(n=>n.storage)))throw new InvalidOperationException("Nondeterministic storage");
            }
        }
        private static void Authoring()
        {
            string directory=Path.GetFullPath("Artifacts/rig-authoring-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            using(var source=ContentJson.Load(GameData.DirectoryPath))foreach(var pair in source.Files){string file=ContentJson.SafePath(directory,pair.Key);Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(file,pair.Value);}
            var edited=ContentEditorWindow.ExerciseRigAuthoring(directory);
            using(var loaded=ContentJson.Load(directory,edited))
            {
                Check(loaded.Game.modules.Single(m=>m.id=="power-1").powerCapacity==9&&loaded.Game.modules.Single(m=>m.id=="cooling-1").coolingCapacity==7,"Content Editor controls author power and cooling capacity");
                Check(loaded.Game.rig.memoryReclaim==5&&loaded.Game.chassis[0].storageCapacity==81&&loaded.Game.actions.Single(a=>a.id=="backdoor").memoryCost==11,"Content Editor authors RAM, storage and ability costs");
            }
            foreach(var pair in edited){string file=ContentJson.SafePath(directory,pair.Key);Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(file,pair.Value);}
            var draft=ScriptableObject.CreateInstance<ContentDraft>();
            try
            {
                using(var workspace=new ContentWorkspace(draft,directory))
                {
                    var power=workspace.Bundle.Manifest.entries.Single(e=>e.category=="Modules"&&((RigModuleDefinition)workspace.Bundle.Objects[e.key]).id=="power-1");
                    var added=workspace.Add("Modules",power.key);var model=(RigModuleDefinition)workspace.Bundle.Objects[added.key];model.id="modded-power";model.tier=4;model.powerCapacity=17;
                    model.requirements=new[]{new Requirement{condition=new ResearchCondition{technology=workspace.Bundle.Game.technologies.Single(t=>t.id=="infrastructure-2")},message="Research infrastructure."}};
                    model.modifiers=new[]{new Modifier{stat="storageCapacity",operation=Arithmetic.Add,value=new ConstantNumber(7),when=new RigNeighborCondition{kind="cooling",minimum=1}}};workspace.Capture();
                    new ContentHistory(directory).Commit(draft.Current,draft.Original,"Rig authoring validation");draft.Accept();
                    using(var content=ContentJson.Load(directory))using(var catalog=new GameCatalog(content.Game))
                    {
                        var sim=new GameSimulation(catalog);sim.DebugAddCredits();sim.DebugAddXp();sim.Career.jobs=6;while(sim.NextChassis!=null)sim.UpgradeChassis();foreach(var part in sim.Career.rigParts.ToArray())sim.RemoveModule(part.instanceId);
                        Check(sim.BuyModule("modded-power")==null&&sim.BuyTech("infrastructure-2"),"modded module research gate works without C# changes");
                        var copy=sim.BuyModule("modded-power");var cooling=sim.BuyModule("cooling-1");sim.TryPlaceModule(copy.instanceId,0,0);sim.TryPlaceModule(cooling.instanceId,1,0);
                        Near(sim.PowerLimit,77,"external JSON changes power capacity without asset regeneration");Near(sim.StorageCapacity,267,"external JSON adjacency effect executes without runtime C# changes");
                        Check(sim.Catalog.Data.rig.memoryReclaim==5&&sim.Module("recycler").price==401,"editor changes survive save, reload and detached catalog compilation");
                    }
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(draft);}
        }
        public static void CheckRackUi(WorkstationApp app)
        {
            var viewport=app.Root.Q<GraphViewport>();Check(viewport!=null&&app.Root.Q(className:"flat-rig")!=null,"inventory rack uses flat cell layout");
            Check(app.Root.Q<Button>(className:"rig-module")!=null,"installed modules render as grid items");
            Check(app.Root.Q<Button>("rig-place-first")!=null&&app.Root.Q<Button>("rig-rotate")!=null&&app.Root.Q<Button>("rig-remove")!=null,"keyboard-accessible packing controls exist");
            var part=app.State.Career.rigParts.First(p=>p.modelId=="processor"&&p.Installed);var button=app.Root.Q<Button>("part-"+part.instanceId);
            Check(button!=null&&button.worldBound.width>0&&button.worldBound.height>0&&viewport.worldBound.Overlaps(button.worldBound),"module overlays lie inside the rack viewport");
            Check(app.Root.Query<Label>().ToList().Any(l=>l.text.Contains("COOLING")),"rig displays assembly budgets");
        }
    }
}
