using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HackThePlanet.Editor
{
    public static class NetworkValidation
    {
        public static void Run(Action<bool, string> check)
        {
            var signatures = new HashSet<string>();
            for (int difficulty = 1; difficulty <= 3; difficulty++)
                for (int seed = 0; seed < 100; seed++)
                {
                    var job = new Contract { id = "quiet-copy", title = "Validation", company = GameContent.Companies[0],
                        difficulty = difficulty, seed = seed, kind = JobKind.Copy };
                    var network = NetworkGenerator.Generate(job, new Career());
                    string prefix = "Seed " + seed + " / difficulty " + difficulty + " / ";
                    var ids = new HashSet<string>(network.nodes.Select(n => n.id));
                    check(network.nodes.Count == 20 + difficulty * 12 && ids.Count == network.nodes.Count, prefix + "unique node count");
                    var keys = network.links.Select(l => string.CompareOrdinal(l.a,l.b)<0?l.a+":"+l.b:l.b+":"+l.a).ToArray();
                    check(keys.Distinct().Count() == keys.Length && network.links.All(l => l.a != l.b && ids.Contains(l.a) && ids.Contains(l.b)), prefix + "valid unique edges");
                    var reachable = Reach(network, false);
                    check(reachable.Count == ids.Count, prefix + "connected network");
                    var noArchives = Reach(network, true);
                    check(network.nodes.Where(n => n.objective).All(n => noArchives.Contains(n.id)) && network.nodes.Count(n=>n.bonus)==3,
                        prefix + "archives are optional detours");
                    check(network.approaches.Count == difficulty + 2 && network.approaches.All(route => route.Zip(route.Skip(1),
                        (a,b)=>network.links.Any(l=>l.a==a&&l.b==b||l.a==b&&l.b==a)).All(v=>v)), prefix + "valid alternative routes");
                    check(network.approaches.SelectMany(r=>r.Skip(2).Take(r.Count-3)).Distinct().Count()==network.approaches.Sum(r=>r.Count-3), prefix + "independent approaches");
                    var repeat = NetworkGenerator.Generate(job, new Career());
                    check(JsonUtility.ToJson(new Snapshot(network)) == JsonUtility.ToJson(new Snapshot(repeat)), prefix + "deterministic preview and positions");
                    signatures.Add(string.Join("|", keys));
                    float minimum = float.MaxValue;
                    for (int i=0;i<network.nodes.Count;i++) for(int j=i+1;j<network.nodes.Count;j++)
                        minimum=Mathf.Min(minimum,Vector3.Distance(network.nodes[i].position,network.nodes[j].position));
                    var bounds=new Bounds(network.nodes[0].position,Vector3.zero);
                    foreach(var node in network.nodes)bounds.Encapsulate(node.position);
                    check(minimum>=2.59f && bounds.size.x>20 && bounds.size.y>10 && bounds.size.z>10,prefix+"volumetric layout and spacing");
                    var fast = PlayApproach(job, network.approaches.First());
                    var safe = PlayApproach(job, network.approaches.Last());
                    check(fast.time < safe.time && fast.trace > safe.trace && fast.trace < 100 && safe.trace < 100,
                        prefix + "legal faster/dangerous versus slower/safer routes");
                }
            check(signatures.Count > 250, "Seed sweep produces varied topology");
            var hardenedCareer = new Career { completed = new List<string> { "quiet-copy" } };
            var fresh = NetworkGenerator.Generate(GameContent.Story[3], new Career());
            var hardened = NetworkGenerator.Generate(GameContent.Story[3], hardenedCareer);
            check(hardened.nodes.First(n=>n.id=="n10").security == fresh.nodes.First(n=>n.id=="n10").security + 1,
                "Corporate firewall hardening persists across generated contracts");
            foreach(var job in GameContent.Story)
            {
                var network=NetworkGenerator.Generate(job,new Career());
                check(network.nodes.Where(n=>n.objective).All(n=>Reach(network,true).Contains(n.id)),job.title+" objectives reachable without archives");
                check(network.nodes.Where(n=>n.bonus).Select(n=>LoreContent.Find(job,n.loreId)).All(f=>f!=null&&!string.IsNullOrEmpty(f.text)),job.title+" authored lore fragments");
            }
            ValidateLoreAndSaves(check);
        }

        [Serializable] private sealed class Snapshot
        {
            public List<NetNode> nodes; public List<NetLink> links;
            public Snapshot(GeneratedNetwork network){nodes=network.nodes;links=network.links;}
        }

        private static HashSet<string> Reach(GeneratedNetwork network, bool omitArchives)
        {
            var visited=new HashSet<string>{"home"};var queue=new Queue<string>();queue.Enqueue("home");
            var blocked=new HashSet<string>(network.nodes.Where(n=>omitArchives&&n.bonus).Select(n=>n.id));
            while(queue.Count>0)
            {
                var id=queue.Dequeue();
                foreach(var edge in network.links.Where(l=>l.a==id||l.b==id))
                {var next=edge.a==id?edge.b:edge.a;if(!blocked.Contains(next)&&visited.Add(next))queue.Enqueue(next);}
            }
            return visited;
        }

        private static (float time,float trace) PlayApproach(Contract job,List<string> route)
        {
            var sim=new GameSimulation();sim.Start(job);float duration=0;
            foreach(var id in route.Skip(1))
            {var preview=sim.Preview(ToolKind.Hack,id);duration+=preview.duration;GameValidation.Hack(sim,id);}
            if(!sim.Active)throw new InvalidOperationException("Approach exceeded trace budget");
            return (duration,sim.Op.trace);
        }

        private static GameSimulation EquippedSimulation(Contract job)
        {
            var career=new Career{cpu=3,bandwidth=3,unlocked=new List<string>{"base","quiet","efficient","selective","buffer"}};
            career.completed=GameContent.Story.Take(job.recurring?3:job.requiredWins).Select(c=>c.id).ToList();
            var sim=new GameSimulation(career);sim.Start(job);return sim;
        }

        private static void Download(GameSimulation sim,string id)
        {
            GameValidation.Traverse(sim,id);
            if(!sim.Execute(ToolKind.Download,id))throw new InvalidOperationException("Cannot recover "+id);
            GameValidation.FinishTask(sim);
            if(!sim.Active)throw new InvalidOperationException("Lore run exceeded trace budget");
        }

        private static void ValidateLoreAndSaves(Action<bool,string> check)
        {
            var job=GameContent.Story[0];var sim=EquippedSimulation(job);
            foreach(var id in NetworkGenerator.Generate(job,sim.Career).approaches.Last().Skip(1))GameValidation.Hack(sim,id);
            foreach(var node in sim.Op.nodes.Where(n=>n.bonus).ToArray())Download(sim,node.id);
            check(sim.Career.lore.Count==0&&sim.Op.log.Any(l=>l.Contains("LORE RECOVERED")),"Lore readable before extraction but not banked");
            var positions=sim.Op.nodes.Select(n=>n.position).ToArray();
            var restored=new GameSimulation(JsonUtility.FromJson<Career>(JsonUtility.ToJson(sim.Career)));
            check(restored.Op.nodes.Select(n=>n.position).SequenceEqual(positions)&&restored.Op.paused&&restored.Op.generationVersion==NetworkGenerator.Version,"3D positions survive save/load exactly");
            restored.SetPaused(false);Download(restored,"n22");var report=restored.Disconnect();
            check(report.success&&report.bonusFiles==3&&report.xp==job.xp+45+(report.trace<job.quietLimit?20:0)&&report.credits==job.credits+300+(report.trace<job.quietLimit?100:0),"All three archives pay their authored credit and XP rewards");
            check(report.bonusArchivesTotal==3&&report.quietLimit==job.quietLimit&&report.quietBonusEarned==report.trace<job.quietLimit,"report records optional bonuses and quiet outcome");
            check(restored.Career.lore.Count==3,"Successful extraction banks three permanent fragments");
            var saved=new GameSimulation(JsonUtility.FromJson<Career>(JsonUtility.ToJson(restored.Career)));
            check(saved.Career.lore.Count==3&&saved.Career.lore.All(f=>!string.IsNullOrEmpty(f.contract)&&!string.IsNullOrEmpty(f.company)),"Lore content and provenance survive save/load");
            var aborted=EquippedSimulation(job);Download(aborted,"n13");aborted.Disconnect();
            check(aborted.Career.lore.Count==0,"Abort cannot bank recovered lore");
            var caught=EquippedSimulation(job);Download(caught,"n13");caught.Disconnect(true);
            check(caught.Career.lore.Count==0,"Isolation cannot bank recovered lore");
            // Repeated freelance lore keeps its identity but still awards normal per-run rewards.
            var freelance=new Contract{id="freelance-0",title="Repeat",company=GameContent.Companies[0],difficulty=1,seed=77,recurring=true};
            var repeat=EquippedSimulation(freelance);
            for(int run=0;run<2;run++)
            {
                if(run>0)repeat.Start(freelance);
                Download(repeat,"n13");Download(repeat,"n22");
                var delivered=repeat.Disconnect();check(delivered.success&&delivered.bonusFiles==1,"Freelance archive pays on run "+run);
            }
            check(repeat.Career.lore.Count==1,"Repeated freelance fragments deduplicate");
            for(int i=0;i<25;i++){repeat.Start(freelance);repeat.Disconnect();}
            check(repeat.Career.reports.Count==20&&repeat.Career.lore.Count==1,"Lore collection outlives report retention");
            // V1 saves remain untouched on career-v1.json; v2 deliberately starts fresh.
        }
    }
}
