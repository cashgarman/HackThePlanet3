using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cashg.GraphExplorer;
using UnityEngine;

namespace HackThePlanet.Editor
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class GameValidation
    {
        private static readonly List<string> results=new List<string>();
        private static void Check(bool pass,string description)
        { if(!pass)throw new InvalidOperationException("FAILED: "+description);results.Add("PASS / "+description); }
        public static void FinishTask(GameSimulation sim)
        {
            int guard=0;
            while(sim.Active&&sim.Op.task!=null&&guard++<2000)sim.Tick(.25f);
            if(guard>=2000)throw new InvalidOperationException("A task did not terminate.");
        }
        public static void Hack(GameSimulation sim,string id)
        {
            if(!sim.Active)throw new InvalidOperationException("Disconnected before hacking "+id);
            if(sim.Node(id).controlled)return;
            if(!sim.Execute(ToolKind.Hack,id))throw new InvalidOperationException("Cannot hack "+id+" / "+sim.Preview(ToolKind.Hack,id).reason);
            FinishTask(sim);
        }
        public static void Traverse(GameSimulation sim,string target)
        {
            var parent=new Dictionary<string,string>();
            var queue=new Queue<string>();
            foreach(var node in sim.Op.nodes.Where(n=>n.controlled)){queue.Enqueue(node.id);parent[node.id]=null;}
            while(queue.Count>0&&!parent.ContainsKey(target))
            {
                var id=queue.Dequeue();
                foreach(var node in sim.Neighbors(sim.Node(id)))
                    if(!parent.ContainsKey(node.id)){parent[node.id]=id;queue.Enqueue(node.id);}
            }
            if(!parent.ContainsKey(target))throw new InvalidOperationException("No route to "+target);
            var path=new List<string>();
            for(var id=target;id!=null;id=parent[id])path.Add(id);
            path.Reverse();foreach(var id in path)Hack(sim,id);
        }
        private static void Recover(GameSimulation sim,string id)
        {
            if(!sim.Execute(ToolKind.Download,id))throw new InvalidOperationException("Cannot recover "+id+" / "+sim.Preview(ToolKind.Download,id).reason);
            FinishTask(sim);
        }

        public static void Run()
        {
            results.Clear();string dir=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"Artifacts");Directory.CreateDirectory(dir);
            try
            {
                NetworkValidation.Run(Check);
                var sim=new GameSimulation();
                Check(!sim.Start(GameContent.Story[1]),"Locked contracts cannot start.");
                Check(sim.Start(GameContent.Story[0]),"Starting contract connects.");
                Check(sim.Op.nodes.Count==32,"First operation has 32 stable systems.");
                Check(sim.KnownGraph.Nodes.Count==2,"Known Graph Explorer document excludes undiscovered systems.");
                Check(!sim.Route("n22").IsReachable,"Shortest-path queries cannot reveal unknown routes.");
                Check(!sim.Execute(ToolKind.Hack,"n22"),"Remote unknown targets cannot be hacked.");
                Check(!sim.Execute(ToolKind.Download,"n2"),"Recovery requires ownership and an eligible file.");
                float trace=sim.Op.trace;for(int i=0;i<200;i++)sim.Tick(.25f);
                Check(sim.Op.trace==trace,"Idle planning never raises trace.");
                Check(sim.Execute(ToolKind.Scan,"n2"),"Reachable system can be scanned.");
                sim.SetPaused(true);sim.Tick(.5f);Check(sim.Op.task.elapsed==0&&sim.Op.trace==0,"Pause freezes tool work and trace.");
                sim.SetPaused(false);FinishTask(sim);
                Check(sim.Node("n2").scanned&&sim.KnownGraph.Nodes.Count>3,"Recon reveals and projects additional graph facts.");
                Hack(sim,"n2");Check(sim.Route("n6",true).IsReachable,"Controlled-route reachability uses Graph Explorer.");
                Check(!sim.Route("n10",true).IsReachable,"Uncontrolled intermediates do not provide access.");
                Hack(sim,"n6");Check(sim.Scanners>0,"A scanner observes activity adjacent to a foothold.");
                Hack(sim,"n10");int protectedSecurity=sim.EffectiveSecurity(sim.Node("n18"));Hack(sim,"n14");
                Check(sim.EffectiveSecurity(sim.Node("n18"))==protectedSecurity-2,"Taking a hub removes connected defense bonuses.");
                Hack(sim,"n18");Hack(sim,"n22");Recover(sim,"n22");
                Check(sim.ObjectiveComplete,"Main copy objective is secured by recovery.");
                int oldCredits=sim.Career.credits;var report=sim.Disconnect();
                Check(report.success&&report.xp>=50&&sim.Career.credits>oldCredits,"Delivery banks credits and XP.");
                Check(sim.Career.completed.Contains("quiet-copy")&&sim.Career.news.Count==2,"Delivery persists story progress and a world consequence.");
                Check(!sim.Start(GameContent.Story[0]),"A delivered story contract cannot be farmed.");
                Check(sim.BuyTech("quiet"),"One successful contract funds a starting technology.");
                Check(!sim.BuyTech("quiet"),"Purchased technologies cannot charge twice.");
                Check(!sim.BuyTech("passive"),"Crossover enforces both parents and price.");
                int beforeRefund=sim.Career.xp;sim.RefundResearch();Check(sim.Career.xp==beforeRefund+45&&sim.Career.unlocked.Count==1,"Free refund returns the exact XP cost.");
                sim.Career.xp=1000;sim.BuyTech("quiet");sim.BuyTech("wide");Check(sim.BuyTech("passive"),"Crossover accepts both unlocked parents.");
                sim.BuyTech("disrupt");sim.BuyTech("backdoor");sim.BuyTech("decoy");
                Check(sim.Career.equipped.Count==2,"Special abilities respect the two-slot starting loadout.");
                Check(!sim.ToggleEquip("decoy"),"A full loadout rejects another equipped ability.");
                sim.ToggleEquip("backdoor");Check(sim.ToggleEquip("decoy"),"Unequipping frees a slot.");
                Check(sim.Upgrade(0)&&sim.Career.cpu==1,"Credits buy persistent hardware.");
                Check(!sim.Upgrade(9),"Invalid hardware choices cannot mutate the career.");
                sim.Start(GameContent.Story[1]);
                Check(!sim.BuyTech("efficient")&&!sim.RefundResearch()&&!sim.Upgrade(0),"Research, refunds, and hardware changes are locked during operations.");
                Check(sim.Decoy()&&!sim.Decoy(),"Decoy is usable only once per operation.");
                var aborted=sim.Disconnect();Check(!aborted.success&&sim.Career.cpu==1&&sim.Has("quiet"),"Abort preserves equipment and purchased technology.");
                Check(sim.Start(GameContent.Story[1]),"Uncompleted work remains retryable.");
                sim.Op.trace=99.99f;sim.Execute(ToolKind.Hack,"n2");sim.Tick(.5f);
                Check(!sim.Active&&!sim.LastReport.success&&sim.LastReport.verdict=="CONNECTION ISOLATED","100% trace closes the connection without rewards.");

                var abilities=new GameSimulation(new Career{xp=2000});
                abilities.BuyTech("wide");abilities.BuyTech("efficient");abilities.BuyTech("chain");abilities.BuyTech("blueprint");abilities.BuyTech("bridge");abilities.BuyTech("quiet");abilities.BuyTech("ghost");
                abilities.Start(GameContent.Story[0]);var ghost=abilities.Preview(ToolKind.Hack,"n2");Check(ghost.trace==0,"Ghost relay makes the first relay fully trace-free.");
                abilities.Execute(ToolKind.Scan,"n2");FinishTask(abilities);
                abilities.Execute(ToolKind.Hack,"n2");Check(abilities.Execute(ToolKind.Hack,"n6"),"Chain hack queues a future neighbor.");FinishTask(abilities);
                Check(abilities.Node("n2").controlled&&abilities.Node("n6").controlled,"Queued actions execute in order and recheck access.");
                Check(!abilities.Execute(ToolKind.Bridge,"n10"),"Firewall bridge requires a scanned target and controlled access.");
                abilities.Disconnect();

                // Play the full authored campaign with an earned-XP build, using only legal actions.
                var campaign=new GameSimulation();
                foreach(var job in GameContent.Story)
                {
                    Check(campaign.Start(job),"Campaign unlock / "+job.title);
                    var generated=NetworkGenerator.Generate(job,campaign.Career);
                    foreach(var id in generated.approaches.Last().Skip(1))Hack(campaign,id);
                    if(job.kind==JobKind.Disrupt)Traverse(campaign,"n14");
                    foreach(var target in campaign.Op.nodes.Where(n=>n.objective&&n.kind!=NodeKind.SecurityHub).ToArray())
                    {Traverse(campaign,target.id);Recover(campaign,target.id);}
                    Check(campaign.Active&&campaign.ObjectiveComplete,"All required objectives viable / "+job.title);
                    var result=campaign.Disconnect();Check(result.success,"Career delivery / "+job.title+" / "+result.trace.ToString("0.0")+"% trace");
                    if(!campaign.Has("quiet"))campaign.BuyTech("quiet");
                    else if(!campaign.Has("efficient"))campaign.BuyTech("efficient");
                    else if(!campaign.Has("selective"))campaign.BuyTech("selective");
                    if(campaign.Career.cpu<2)campaign.Upgrade(0);
                }
                Check(campaign.Career.completed.Count==6&&campaign.Career.jobs==6,"All six authored contracts form a complete career loop.");
                var repeatable=GameContent.Contracts(campaign.Career).First(c=>c.recurring);
                Check(campaign.Start(repeatable),"Procedural freelance work unlocks after story progress.");
                Check(campaign.Op.nodes.All(n=>n.security>=0)&&campaign.Op.links.All(l=>campaign.Node(l.a)!=null&&campaign.Node(l.b)!=null),"Generated networks have valid security and link endpoints.");
                campaign.Execute(ToolKind.Hack,"n2");campaign.Tick(.5f);
                string save=Path.Combine(dir,"validation-career.json");
                Check(CareerStorage.Save(campaign.Career,save),"Atomic career save succeeds.");
                var restored=new GameSimulation(CareerStorage.Load(save));
                Check(restored.Active&&restored.Op.paused&&restored.Op.task!=null&&restored.Op.task.elapsed>0,"Active tool work resumes from save in a paused operation.");
                Check(restored.Career.completed.Count==6&&restored.Has("quiet")&&restored.Career.cpu==campaign.Career.cpu,"Research, hardware, story, and economy survive a round trip.");
                Check(restored.KnownGraph.Nodes.Count==campaign.KnownGraph.Nodes.Count,"Known graph projection is rebuilt correctly after load.");
                CareerStorage.Save(restored.Career,save);File.WriteAllText(save,"invalid career");
                var backup=CareerStorage.Load(save);Check(backup.completed.Count==6,"A corrupt save recovers from its previous atomic backup.");
                CareerStorage.Save(backup,save);
                string resetSave=Path.Combine(dir,"validation-reset-career.json");
                CareerStorage.Save(campaign.Career,resetSave);
                Check(CareerStorage.Reset(resetSave),"Reset replaces an existing save and recovery backup.");
                var reset=new GameSimulation(CareerStorage.Load(resetSave)).Career;
                Check(reset.credits==600&&reset.xp==0&&reset.day==1&&reset.jobs==0&&reset.totalEarnedXp==0&&reset.operation==null,
                    "Reset clears economy, story statistics, and the active operation.");
                Check(reset.completed.Count==0&&reset.reports.Count==0&&reset.unlocked.SequenceEqual(new[]{"base"})&&reset.equipped.Count==0&&reset.cpu==0&&reset.bandwidth==0&&reset.memory==0,
                    "Reset clears contracts, reports, research, loadout, and hardware.");
                var resetBackup=new GameSimulation(CareerStorage.Load(resetSave+".bak")).Career;
                Check(resetBackup.operation==null&&resetBackup.jobs==0&&resetBackup.completed.Count==0,"Reset also removes old progress from recovery.");
                File.WriteAllText(resetSave,"invalid career");
                Check(CareerStorage.Load(resetSave).completed.Count==0,"Recovery after reset cannot resurrect the previous career.");
                CareerStorage.Save(reset,resetSave);
                results.Add("TOTAL / "+results.Count+" assertions passed.");
                File.WriteAllLines(Path.Combine(dir,"simulation-validation.txt"),results);Debug.Log("Hack The Planet validation: "+results.Last());
            }
            catch(Exception ex){results.Add(ex.ToString());File.WriteAllLines(Path.Combine(dir,"simulation-validation.txt"),results);throw;}
        }
    }
}
