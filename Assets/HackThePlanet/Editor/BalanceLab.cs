using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HackThePlanet.Editor
{
    [Serializable] public sealed class BalanceTrial
    {
        public string build,route;
        public int difficulty,seed,actions;
        public bool success,quiet;
        public float seconds,trace;
    }
    [Serializable] public sealed class BalanceReport { public List<BalanceTrial> trials=new List<BalanceTrial>(); }

    public static class BalanceLab
    {
        public static void CaptureBefore()=>Capture("before");
        public static void CaptureAfter()=>Capture("after");
        public static void ApplyAndMeasure(){BalanceRefinement.Apply();CaptureAfter();}
        [MenuItem("Hack The Planet/Balance/Measure Route Balance")]
        public static void CaptureAfterMenu()=>CaptureAfter();
        private static void Capture(string suffix)
        {
            var report=new BalanceReport();
            string[][] builds={Array.Empty<string>(),new[]{"wide","signal"},new[]{"efficient","selective"},new[]{"quiet","ghost"},new[]{"disrupt","blackout"}};
            string[] names={"Stock","Recon","Intrusion","Stealth","Control"};
            for(int difficulty=1;difficulty<=3;difficulty++)for(int seed=0;seed<20;seed++)for(int b=0;b<builds.Length;b++)foreach(bool fast in new[]{true,false})
                report.trials.Add(Play(names[b],builds[b],difficulty,seed,fast));
            File.WriteAllText("Artifacts/balance-"+suffix+".json",JsonUtility.ToJson(report,true));
            var lines=new List<string>{"Build | Difficulty | Route | Success | Mean seconds | Mean trace"};
            foreach(var group in report.trials.GroupBy(t=>new{t.build,t.difficulty,t.route}))
                lines.Add(group.Key.build+" | "+group.Key.difficulty+" | "+group.Key.route+" | "+group.Count(t=>t.success)+"/"+group.Count()+" | "+group.Average(t=>t.seconds).ToString("0.0")+" | "+group.Average(t=>t.trace).ToString("0.0"));
            File.WriteAllLines("Artifacts/balance-"+suffix+".txt",lines);Debug.Log("Balance measurement completed: "+report.trials.Count+" legal action sequences.");
        }
        private static BalanceTrial Play(string name,string[] technologies,int difficulty,int seed,bool fast)
        {
            var catalog=GameCatalog.Default;var career=catalog.CreateCareer();career.xp=10000;
            var sim=new GameSimulation(career);foreach(var id in technologies)if(!sim.BuyTech(id))throw new InvalidOperationException("Cannot create balance build "+id);
            var job=catalog.Contracts(sim.Career).First(c=>!c.recurring);job.difficulty=difficulty;job.seed=seed;
            var network=NetworkGenerator.Generate(job,career);sim.Start(job);
            var trial=new BalanceTrial{build=name,difficulty=difficulty,seed=seed,route=fast?"Exposed":"Maintenance"};
            void Run(string action,string id)
            {
                if(!sim.Active)return;var preview=sim.Preview(action,id);if(!preview.allowed)return;
                if(!sim.Execute(action,id))throw new InvalidOperationException("Balance action rejected.");trial.actions++;
                int guard=0;while(sim.Active&&sim.Op.task!=null&&guard++<4000)sim.Tick(.25f);
                if(guard>=4000)throw new InvalidOperationException("Balance task stalled.");
            }
            foreach(var id in (fast?network.approaches.First():network.approaches.Last()).Skip(1))
            {
                if(!sim.Active)break;
                if((name=="Recon"||(name=="Control"&&sim.Node(id).kind==NodeKind.Firewall))&&sim.Preview("scan",id).allowed)Run("scan",id);
                if(name=="Control")foreach(var target in sim.Op.nodes.Where(n=>!n.controlled&&n.suppressed<=0).ToArray())
                {
                    var action=sim.AvailableActions(target.id).FirstOrDefault(a=>a.id=="disrupt"||a.id=="blackout");
                    if(action!=null&&sim.Preview(action.id,target.id).allowed)Run(action.id,target.id);
                }
                Run("hack",id);
            }
            if(sim.Active)foreach(var node in sim.Op.nodes.Where(n=>n.objective).ToArray())Run("download",node.id);
            if(sim.Active){trial.seconds=sim.Op.elapsed;trial.trace=sim.Op.trace;trial.success=sim.ObjectiveComplete;trial.quiet=trial.success&&trial.trace<job.quietLimit;sim.Disconnect();}
            else{trial.trace=sim.LastReport.trace;trial.seconds=0;trial.success=false;}
            return trial;
        }
    }
}
