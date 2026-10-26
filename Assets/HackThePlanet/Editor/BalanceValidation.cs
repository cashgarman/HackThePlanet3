using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HackThePlanet.Editor
{
    public static class BalanceValidation
    {
        [MenuItem("Hack The Planet/Balance/Validate Refined Career")]
        public static void Run()
        {
            DataDrivenValidation.Run();
            var results=new List<string>();
            void Check(bool condition,string message){if(!condition)throw new InvalidOperationException("BALANCE FAILED / "+message);results.Add("PASS / "+message);}
            GameSimulation Start(params string[] tech)
            {
                var sim=new GameSimulation();sim.Career.xp=10000;sim.Career.credits=10000;
                foreach(var id in tech)Check(sim.BuyTech(id),"research "+id);
                sim.Start(sim.Catalog.Contracts(sim.Career).First(c=>!c.recurring));return sim;
            }
            try
            {
                var scout=Start();float rawTime=scout.Preview("hack","n2").duration,rawTrace=scout.Preview("hack","n2").trace;
                scout.Execute("scan","n2");GameValidation.FinishTask(scout);
                Check(scout.Preview("hack","n2").duration<rawTime&&scout.Preview("hack","n2").trace<rawTrace,"scanning prepares a faster and cleaner hack before any research");
                Check(!scout.AvailableActions("n2").Any(a=>a.id=="scan"),"completed scans disappear from the action list");
                var stop=Start("efficient","chain");stop.Execute("scan","n2");GameValidation.FinishTask(stop);stop.Execute("hack","n2");stop.Execute("hack","n6");stop.Tick(.5f);
                float trace=stop.Op.trace;Check(trace>0&&stop.StopTask(),"running work can be stopped");Check(stop.Op.task==null&&stop.Op.queue.Count==0&&stop.Op.trace==trace&&!stop.Node("n2").controlled,"stopping clears pending work without refunding trace or granting ownership");
                stop.Tick(.5f);Check(stop.Op.trace==trace&&!stop.StopTask(),"stopping returns to free planning and is idempotent");
                stop.Op.trace=99;var forecast=stop.Preview("hack","n2");Check(forecast.willIsolate&&forecast.projectedTrace>=stop.TraceLimit,"action forecast warns before exceeding trace limit");
                stop.Op.trace=stop.Job.quietLimit-1;Check(stop.Preview("hack","n2").losesQuietBonus,"forecast distinguishes losing the quiet bonus");
                var shielded=Start("disrupt","decoy");shielded.Op.trace=99;shielded.Execute("decoy");
                var protectedForecast=shielded.Preview("hack","n2");Check(!protectedForecast.willIsolate&&protectedForecast.projectedTrace==shielded.Op.trace,"forecast respects guaranteed trace protection");
                var bridge=Start("wide","efficient","bridge");bridge.Node("n6").known=true;bridge.Node("n6").controlled=true;bridge.Node("n10").known=true;
                Check(!bridge.Preview("bridge","n10").allowed,"bridge needs prepared intel");bridge.Node("n10").scanned=true;Check(bridge.Preview("bridge","n10").allowed,"one controlled neighbor makes a prepared bridge useful");
                bridge.AddCounter("uses:bridge",1);bridge.Execute("bridge","n10");bridge.Node("n14").known=true;bridge.Node("n14").controlled=true;bridge.Node("n18").known=true;bridge.Node("n18").scanned=true;
                Check(!bridge.CanQueue("bridge","n18"),"the last bridge charge cannot be reserved twice");GameValidation.FinishTask(bridge);Check(bridge.UsesRemaining("bridge")==0&&!bridge.Preview("bridge","n18").allowed,"bridge charges remain bounded across targets");
                var ranged=Start("quiet","backdoor");var route=NetworkGenerator.Generate(ranged.Job,ranged.Career).approaches.Last();var far=ranged.Node(route[route.Count-2]);far.known=true;
                Check(!ranged.Preview("backdoor",far.id).allowed,"a revealed isolated endpoint cannot skip an entire mission");ranged.Execute("scan","n2");GameValidation.FinishTask(ranged);
                var close=ranged.Node(route[3]);Check(ranged.ControlledDistance(close)==3&&ranged.Preview("backdoor",close.id).allowed,"backdoor keeps its tactical three-hop shortcut");
                var controller=Start("disrupt","blackout");controller.Execute("hack","n2");GameValidation.FinishTask(controller);controller.Execute("scan","n6");GameValidation.FinishTask(controller);
                Check(!controller.Reachable(controller.Node("n14"))&&controller.Preview("blackout","n14").allowed,"blackout can reach a mapped hub before crossing its protected firewall");
                int security=controller.EffectiveSecurity(controller.Node("n10"));controller.Execute("blackout","n14");GameValidation.FinishTask(controller);Check(controller.EffectiveSecurity(controller.Node("n10"))<security,"remote blackout produces an immediate tactical benefit");
                var hardware=new GameSimulation();Check(!hardware.PreviewUpgrade("memory").allowed&&!hardware.Upgrade("memory"),"empty loadouts cannot accidentally consume starting credits");
                hardware.Career.xp=45;Check(hardware.BuyTech("disrupt")&&hardware.PreviewUpgrade("memory").allowed,"researching an ability unlocks useful capacity upgrades");
                var priorCareer=JsonUtility.FromJson<Career>("{\"version\":2,\"initialized\":true,\"credits\":600,\"xp\":50,\"day\":1,\"totalEarnedXp\":100,\"unlocked\":[\"base\",\"quiet\"]}");
                var prior=new GameSimulation(priorCareer);Check(prior.Career.researchSpent==50&&prior.RefundResearch()&&prior.Career.xp==100,"existing v2 research refunds preserve the XP originally invested before price reductions");
                var spent=new GameSimulation();spent.Career.xp=100;Check(spent.BuyTech("quiet")&&spent.Career.researchSpent==45,"new purchases record their actual XP investment");
                var roundtrip=new GameSimulation(JsonUtility.FromJson<Career>(JsonUtility.ToJson(spent.Career)));Check(roundtrip.Career.researchSpent==45&&roundtrip.RefundResearch()&&roundtrip.Career.xp==100,"paid research investment survives save/load and refund");
                int firstTwoXp=GameContent.Story.Take(2).Sum(c=>c.xp)+GameCatalog.Default.Data.rewards.quietXp*2;
                foreach(var branch in new[]{new[]{"wide","signal"},new[]{"efficient","selective"},new[]{"quiet","ghost"},new[]{"disrupt","blackout"}})
                    Check(branch.Sum(id=>GameContent.FindTech(id).cost)<=firstTwoXp,"two successful quiet contracts can establish "+branch[0]+" build without archive grinding");
                Check(GameCatalog.Default.Data.rewards.archiveXpCap>=GameCatalog.Default.Data.rewards.archiveXp*3,"the third archive earns full XP");
                BalanceLab.CaptureAfter();
                var measured=JsonUtility.FromJson<BalanceReport>(File.ReadAllText("Artifacts/balance-after.json"));
                Check(measured.trials.Count==600&&measured.trials.All(t=>t.success&&t.trace<100),"600 sampled build, seed and route sequences complete without unavoidable isolation");
                foreach(int difficulty in new[]{1,2,3})
                {
                    BalanceTrial[] Trials(string build,string routeName)=>measured.trials.Where(t=>t.build==build&&t.route==routeName&&t.difficulty==difficulty).ToArray();
                    var stockFast=Trials("Stock","Exposed");var stockSafe=Trials("Stock","Maintenance");
                    Check(stockFast.Average(t=>t.seconds)<stockSafe.Average(t=>t.seconds)&&stockFast.Average(t=>t.trace)>stockSafe.Average(t=>t.trace),"route speed/exposure trade-off at difficulty "+difficulty);
                    Check(Trials("Intrusion","Exposed").Average(t=>t.seconds)<stockFast.Average(t=>t.seconds),"intrusion buys speed at difficulty "+difficulty);
                    Check(Trials("Recon","Maintenance").Average(t=>t.trace)<stockSafe.Average(t=>t.trace),"recon preparation buys safety at difficulty "+difficulty);
                    Check(Trials("Stealth","Maintenance").Average(t=>t.trace)<stockSafe.Average(t=>t.trace),"stealth rewards a low-exposure route at difficulty "+difficulty);
                    Check(Trials("Control","Exposed").Average(t=>t.trace)<stockFast.Average(t=>t.trace),"control changes the exposed route at difficulty "+difficulty);
                }
                results.Add("TOTAL / "+results.Count+" balance checks passed.");File.WriteAllLines("Artifacts/balance-validation.txt",results);Debug.Log(results.Last());
            }
            catch(Exception ex){results.Add(ex.ToString());File.WriteAllLines("Artifacts/balance-validation.txt",results);throw;}
        }
        public static void ApplyAndValidate(){BalanceRefinement.Apply();Run();}
        public static void ValidateAndSmoke(){Run();GameBuildTools.StartSmoke();}
    }
}
