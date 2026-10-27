using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace HackThePlanet.Tests
{
    public sealed class SimulationTests
    {
        private GameFixture fixture;

        [SetUp] public void SetUp() => fixture = new GameFixture();
        [TearDown] public void TearDown() => fixture?.Dispose();

        private GameSimulation Started()
        {
            var sim = fixture.Simulation();
            Assert.That(sim.Start(fixture.FirstContract(sim)), Is.True);
            return sim;
        }

        [Test]
        public void SeededNetworkIsStableAndRoutesRemainConnected()
        {
            var sim = fixture.Simulation();
            var job = fixture.FirstContract(sim);
            var a = NetworkGenerator.Generate(job, sim.Career, fixture.Catalog);
            var b = NetworkGenerator.Generate(job, sim.Career, fixture.Catalog);
            Assert.That(a.nodes.Select(n => (n.id, n.security, n.known, n.position)).ToArray(),
                Is.EqualTo(b.nodes.Select(n => (n.id, n.security, n.known, n.position)).ToArray()));
            Assert.That(a.links.Select(l => (l.a, l.b)).ToArray(), Is.EqualTo(b.links.Select(l => (l.a, l.b)).ToArray()));
            Assert.That(a.nodes.Select(n => n.id), Is.Unique);
            Assert.That(a.links.All(l => a.nodes.Any(n => n.id == l.a) && a.nodes.Any(n => n.id == l.b)), Is.True);
            foreach (var node in a.nodes)
                Assert.That(GameSimulation.WaypointPath(a.nodes, a.links, a.nodes.First(n => n.controlled).id,
                    node.id).IsReachable, Is.True, node.id);
        }

        [Test]
        public void ReachabilityAndDistanceFollowKnownControlledLinks()
        {
            var sim = Started();
            var source = sim.Op.nodes.First(n => n.controlled);
            var target = sim.Neighbors(source).First(n => !n.controlled);
            target.known = true;
            Assert.That(sim.Reachable(target), Is.True);
            Assert.That(sim.ControlledDistance(target), Is.EqualTo(1));
            Assert.That(sim.Route(target.id).IsReachable, Is.True);
            target.known = false;
            Assert.That(sim.Reachable(target), Is.False);
            Assert.That(sim.ControlledDistance(target), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void PreviewExecutePauseAndTickCompleteHack()
        {
            var sim = Started();
            var target = sim.Op.nodes.First(n => n.known && !n.controlled && sim.Reachable(n));
            var preview = sim.Preview("hack", target.id);
            Assert.That(preview.allowed, Is.True, preview.reason);
            Assert.That(preview.duration, Is.GreaterThan(0));
            sim.SetManualPause(true);
            Assert.That(sim.Execute("hack", target.id), Is.True);
            Assert.That(sim.Op.paused, Is.True);
            sim.Tick(.25f);
            Assert.That(sim.Op.task.elapsed, Is.Zero);
            sim.SetManualPause(false);
            for (var i = 0; i < 2000 && sim.Active && sim.Op.task != null; i++) sim.Tick(.25f);
            Assert.That(sim.Op.task, Is.Null, "Hack task did not finish.");
            Assert.That(target.controlled, Is.True);
            Assert.That(sim.Counter("uses:hack"), Is.EqualTo(1));
        }

        [Test]
        public void EffectsChangeCountersTraceAndNodeState()
        {
            var sim = Started();
            var node = sim.Op.nodes.First(n => !n.controlled);
            node.known = true;
            var context = sim.Context(node);
            var effects = new Effect[]
            {
                new CounterEffect { key = "test", amount = 3 },
                new ConditionalEffect { when = new NodeStateCondition { state = NodeState.Known },
                    effects = new Effect[] { new TraceEffect { amount = new ConstantNumber(7) } } }
            };
            sim.ApplyEffects(effects, context);
            Assert.That(sim.Counter("test"), Is.EqualTo(3));
            Assert.That(sim.Op.trace, Is.EqualTo(7));
            node.known = false;
            sim.ApplyEffects(effects, context);
            Assert.That(sim.Op.trace, Is.EqualTo(7), "The conditional effect must skip an unknown node.");
            sim.SetState(node, NodeState.Scanned, true, context);
            Assert.That(node.scanned, Is.True);
        }

        [Test]
        public void RigPlacementRejectsOverlapAndKeepsCapacityConsistent()
        {
            var sim = fixture.Simulation();
            Assert.That(sim.RigValid, Is.True);
            Assert.That(sim.CanPlace("missing", 0, 0, false), Is.False);
            var installed = sim.InstalledParts.First();
            Assert.That(sim.PartAt(installed.row, installed.column), Is.Not.Null);
            var bought = sim.BuyModule(sim.Modules.First(m => m.price <= sim.Career.credits).id);
            Assert.That(bought, Is.Not.Null);
            Assert.That(sim.TryPlaceModule(bought.instanceId, installed.row, installed.column), Is.False);
            Assert.That(sim.RemoveModule(bought.instanceId), Is.True);
            Assert.That(sim.PowerUsed, Is.LessThanOrEqualTo(sim.PowerLimit));
            Assert.That(sim.HeatUsed, Is.LessThanOrEqualTo(sim.CoolingLimit));
        }

        [Test]
        public void RemovingAndReplacingRamChangesCapacity()
        {
            var sim = fixture.Simulation();
            var ram = sim.InstalledParts.First(p => p.modelId == "ram");
            var row = ram.row;
            var column = ram.column;
            var before = sim.MemoryPool;
            Assert.That(sim.RemoveModule(ram.instanceId), Is.True);
            Assert.That(sim.MemoryPool, Is.EqualTo(before - 10));
            Assert.That(sim.TryPlaceModule(ram.instanceId, row, column), Is.True);
            Assert.That(sim.MemoryPool, Is.EqualTo(before));
        }

        [Test]
        public void ResearchPurchaseRespectsResources()
        {
            var sim = fixture.Simulation();
            var tech = fixture.Catalog.Data.technologies.First(t => !sim.Has(t.id) && t.parents.All(p => sim.Has(p.id)));
            sim.Career.xp = 0;
            if (tech.cost > 0) Assert.That(sim.BuyTech(tech.id), Is.False);
            sim.Career.xp = tech.cost;
            Assert.That(sim.BuyTech(tech.id), Is.True);
            Assert.That(sim.Has(tech.id), Is.True);
            Assert.That(sim.Career.xp, Is.Zero);
            Assert.That(sim.BuyTech(tech.id), Is.False);
        }

        [Test]
        public void HardwarePurchaseRespectsPriceAndUpdatesLevel()
        {
            var root = UnityEngine.Object.Instantiate(fixture.Catalog.Data);
            var hardware = ScriptableObject.CreateInstance<HardwareDefinition>();
            try
            {
                hardware.id = "test-hardware";
                hardware.title = "Test hardware";
                hardware.category = "test";
                hardware.statLabel = "Compute";
                hardware.displayedStat = "compute";
                hardware.tiers = new[] { new HardwareTier { price = 75 } };
                root.hardware = root.hardware.Concat(new[] { hardware }).ToArray();
                using (var catalog = new GameCatalog(root))
                {
                    var sim = new GameSimulation(catalog);
                    sim.Career.credits = 74;
                    Assert.That(sim.PreviewUpgrade(hardware.id).allowed, Is.False);
                    Assert.That(sim.Upgrade(hardware.id), Is.False);
                    sim.Career.credits = 75;
                    Assert.That(sim.Upgrade(hardware.id), Is.True);
                    Assert.That(sim.Career.Level(hardware.id), Is.EqualTo(1));
                    Assert.That(sim.Career.credits, Is.Zero);
                    Assert.That(sim.Upgrade(hardware.id), Is.False);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(hardware);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void DisconnectWithoutObjectivesDoesNotAwardContract()
        {
            var sim = Started();
            var beforeCredits = sim.Career.credits;
            var beforeJobs = sim.Career.jobs;
            sim.Disconnect();
            Assert.That(sim.Active, Is.False);
            Assert.That(sim.Career.jobs, Is.EqualTo(beforeJobs));
            Assert.That(sim.Career.credits, Is.EqualTo(beforeCredits));
        }

        [Test]
        public void DeliveredObjectiveAwardsCareerAndReportProgress()
        {
            var sim = Started();
            var objective = fixture.Catalog.Contract(sim.Job.id).objectives[0];
            sim.Node(objective.nodeId).collected = true;
            Assert.That(sim.ObjectiveComplete, Is.True);
            var credits = sim.Career.credits;
            var xp = sim.Career.xp;
            var report = sim.Disconnect();
            Assert.That(report.success, Is.True);
            Assert.That(sim.Career.jobs, Is.EqualTo(1));
            Assert.That(sim.Career.credits, Is.EqualTo(credits + report.credits));
            Assert.That(sim.Career.xp, Is.EqualTo(xp + report.xp));
            Assert.That(sim.Career.reports[0], Is.SameAs(report));
            Assert.That(sim.Career.completed, Does.Contain(sim.Job.id));
        }
    }
}
