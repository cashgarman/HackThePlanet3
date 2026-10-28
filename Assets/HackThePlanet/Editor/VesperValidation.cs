using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace HackThePlanet.Editor
{
    public static class VesperValidation
    {
        private static int checks;
        private static readonly List<string> rows = new();
        private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception ex) { Debug.LogException(ex); File.WriteAllText("Artifacts/vesper-validation.txt", "FAIL / " + ex); EditorApplication.Exit(1); }
        }
        public static void Run()
        {
            checks = 0; rows.Clear();
            using var bundle = ContentJson.Load(Path.GetFullPath("data"));
            Check(bundle.Errors.Count == 0, string.Join("\n", bundle.Errors));
            var catalog = new GameCatalog(bundle.Game);
            Check(catalog.Data.contracts.Count(c => !c.recurring && !c.optional) == 30, "30 story contracts");
            Check(catalog.Data.technologies.Length == 29 && catalog.Data.modules.Length == 22, "Research and rig catalog counts");
            var audit = new List<string>();
            foreach (var module in catalog.Data.modules)
            {
                var funded = new GameSimulation(catalog, catalog.CreateCareer()); funded.Career.credits = 100000; funded.Career.jobs = 20;
                funded.DebugUnlockAllTech(); while (funded.UpgradeChassis()) { }
                var before = new[] { funded.Compute, funded.Bandwidth, funded.StorageCapacity, funded.MemoryPool, funded.PowerLimit, funded.CoolingLimit };
                var part = funded.BuyModule(module.id); Check(part != null && funded.PlaceFirstFree(part.instanceId), "Module can be bought and packed: " + module.id);
                Check(funded.RigValid, "Single-copy rig remains legal: " + module.id);
                var after = new[] { funded.Compute, funded.Bandwidth, funded.StorageCapacity, funded.MemoryPool, funded.PowerLimit, funded.CoolingLimit };
                audit.Add(module.id + " / " + module.price + " cr / compute,bandwidth,storage,RAM,power,cooling: " + string.Join(",", before.Zip(after, (a,b) => a.ToString("0.##") + "→" + b.ToString("0.##"))) + " / " + module.description);
            }
            File.WriteAllLines("Artifacts/vesper-upgrade-audit.txt", audit);
            var refund = new GameSimulation(catalog, catalog.CreateCareer()); refund.Career.xp = 500; refund.BuyTech("wide"); refund.BuyTech("signal"); refund.BuyTech("quiet");
            var forecast = refund.PreviewTechnologyRefund("wide"); var wallet = refund.Career.xp;
            Check(forecast.xp == 115 && refund.Has("signal"), "Selective refund preview is exact and non-mutating");
            Check(refund.RefundTechnology("wide") && refund.Career.xp == wallet + forecast.xp && refund.Has("quiet") && !refund.Has("signal"), "Selective refund matches preview");
            var samples = new Dictionary<int, List<float>>();
            foreach (var alliance in new[] { "expose", "shelter", "command" })
            foreach (var ending in new[] { "open", "careful" })
            foreach (var researched in new[] { false, true })
            for (var seed = 0; seed < 20; seed++)
            {
                var sim = new GameSimulation(catalog, catalog.CreateCareer());
                var steps = 0;
                while (!sim.StoryComplete)
                {
                    var pending = sim.PendingChoice;
                    if (pending != null)
                    {
                        var option = pending.choices.FirstOrDefault(o => o.flag == alliance || o.flag == alliance + "-" + ending) ?? pending.choices.First();
                        Check(sim.ChooseStory(option.id), "Story choice " + option.id); continue;
                    }
                    var d = catalog.Data.contracts.FirstOrDefault(c => !c.recurring && !c.optional && sim.ContractAvailable(c));
                    Check(d != null, "No softlock / " + alliance + " / " + string.Join(",", sim.Career.completed));
                    var job = catalog.Contracts(sim.Career).First(c => c.id == d.id); job.seed = seed;
                    Check(sim.Start(job), "Start " + d.id);
                    if (d.defenses.Length > 0) Secure(sim, "n14");
                    if (d.defenses.Any(x => x.kind == DefenseKind.CredentialLock)) { Secure(sim, "n19"); RunAction(sim, "extract-key", "n19"); }
                    foreach (var goal in d.objectives) { Secure(sim, goal.nodeId); if (!sim.ObjectiveDone(sim.Node(goal.nodeId))) RunAction(sim, goal.action.id, goal.nodeId); }
                    if (d.evidence != null && d.evidence.answers.Length > 0)
                    {
                        Check(!sim.SolveEvidence(d.evidence.answers.First(a => !a.correct).id), "Wrong conclusion stays retryable");
                        Check(sim.SolveEvidence(d.evidence.answers.First(a => a.correct).id), "Correct evidence conclusion");
                    }
                    Check(sim.ObjectiveComplete, "Objectives / " + d.id);
                    var seconds = sim.Op.elapsed;
                    if (!samples.ContainsKey(job.difficulty)) samples[job.difficulty] = new List<float>(); samples[job.difficulty].Add(seconds);
                    var report = sim.Disconnect(); Check(report.success, "Delivery " + d.id);
                    Check(sim.Career.lore.Any(l => l.id == d.id + "/revelation"), "Guaranteed plot reveal");
                    Check(++steps <= 13, "13-contract path limit");
                    // Base rewards buy useful choices, without relying on optional loot or freelance income.
                    if (researched)
                    {
                        foreach (var tech in catalog.Data.technologies.Where(t => t.branch == (alliance == "expose" ? "RECON" : alliance == "shelter" ? "STEALTH" : "CONTROL"))) sim.BuyTech(tech.id);
                        if (sim.Career.credits >= 1200 && sim.Career.jobs == 1) sim.UpgradeChassis();
                    }
                }
                Check(steps == 13, "Exactly 13 story jobs per path");
                GameSimulation.ValidateCareer(sim.Career, catalog);
                var path = "Artifacts/vesper-save-test.json"; CareerStorage.Save(sim.Career, path); var restored = CareerStorage.Load(path, catalog);
                Check(restored.storyFlags.Contains(alliance), "Alliance survives save");
                rows.Add(alliance + "/" + ending + "/" + (researched ? "researched" : "stock") + "/" + seed + " / " + sim.Career.credits + " credits / " + sim.Career.xp + " XP / 13 jobs");
            }
            foreach (var pair in samples) { var a = pair.Value.OrderBy(x => x).ToArray();
                var median = a[a.Length / 2]; var p90 = a[(int)(a.Length * .9f)];
                Check(median >= (pair.Key == 1 ? 90 : pair.Key == 2 ? 150 : 240) && median <= (pair.Key == 1 ? 120 : pair.Key == 2 ? 240 : 300), "Pacing median tier " + pair.Key + ": " + median);
                if (pair.Key == 3) Check(p90 <= 360, "Hard p90 pacing");
                rows.Add("PACING / tier " + pair.Key + " median " + a[a.Length / 2].ToString("0.0") + "s / p90 " + a[(int)(a.Length * .9f)].ToString("0.0") + "s"); }
            File.WriteAllLines("Artifacts/vesper-validation.txt", new[] { "PASS / " + checks + " checks / 240 complete careers / 3120 story stages" }.Concat(rows));
            File.Copy("Artifacts/vesper-validation.txt", "Artifacts/career-balance.txt", true);
            Debug.Log("Vesper validation passed / " + checks);
        }
        private static void RunAction(GameSimulation sim, string action, string target)
        {
            var p = sim.Preview(action, target); Check(p.allowed, action + "/" + target + ": " + p.reason);
            Check(sim.Execute(action, target), "Execute " + action);
            for (var guard = 0; sim.Active && sim.Op.task != null; guard++) { Check(guard < 2000, "Task terminates"); sim.Tick(.5f); }
            Check(sim.Active, "Operation survives " + action + "/" + target);
        }
        private static void Secure(GameSimulation sim, string target)
        {
            if (sim.Node(target).controlled) return;
            var queue = new Queue<string>(); var parent = new Dictionary<string,string>();
            foreach (var n in sim.Op.nodes.Where(n => n.controlled)) { queue.Enqueue(n.id); parent[n.id] = null; }
            while (queue.Count > 0 && !parent.ContainsKey(target))
            {
                var id = queue.Dequeue(); foreach (var edge in sim.Op.links.Where(l => l.a == id || l.b == id))
                { var next = edge.a == id ? edge.b : edge.a; if (!parent.ContainsKey(next)) { parent[next] = id; queue.Enqueue(next); } }
            }
            Check(parent.ContainsKey(target), "Connected target " + target);
            var path = new List<string>(); for (var p = target; p != null; p = parent[p]) path.Add(p); path.Reverse();
            foreach (var id in path.Where(id => !sim.Node(id).controlled))
            {
                Check(sim.Node(id).known, "Control reveals legal next hop " + id);
                if (sim.Preview("scan", id).allowed) RunAction(sim, "scan", id);
                RunAction(sim, "hack", id);
            }
        }
    }
}
