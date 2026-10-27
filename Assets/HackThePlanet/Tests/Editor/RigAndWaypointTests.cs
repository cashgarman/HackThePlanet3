using System.Linq;
using NUnit.Framework;

namespace HackThePlanet.Tests
{
    public sealed class RigAndWaypointTests
    {
        private GameFixture fixture;

        [SetUp]
        public void SetUp() => fixture = new GameFixture();

        [TearDown]
        public void TearDown() => fixture?.Dispose();

        [Test]
        public void RotatedFootprintControlsPlacementAtChassisEdges()
        {
            var sim = fixture.Simulation();
            var model = fixture.Catalog.Data.modules.First(m => m.width == 2 && m.height == 1 && m.rotatable);
            model.requirements = System.Array.Empty<Requirement>();
            model.price = 0;
            sim.Career.rigParts.Clear();
            var part = sim.BuyModule(model.id);
            Assert.That(part, Is.Not.Null);
            Assert.That(GameSimulation.PartSize(model, false).x, Is.EqualTo(2));
            Assert.That(GameSimulation.PartSize(model, true).y, Is.EqualTo(2));

            Assert.That(sim.CanPlace(part.instanceId, 0, sim.Chassis.columns - 1, false), Is.False);
            Assert.That(sim.TryPlaceModule(part.instanceId, 0, sim.Chassis.columns - 1, true), Is.True);
            Assert.That(part.rotated, Is.True);
            Assert.That(sim.TryPlaceModule(part.instanceId, -1, 0, true), Is.False);
            Assert.That(sim.TryPlaceModule(part.instanceId, 0, sim.Chassis.columns, true), Is.False);
        }

        [Test]
        public void ModulePurchaseSaleAndChassisUpgradePreserveEconomyAndPlacements()
        {
            var sim = fixture.Simulation();
            var model = fixture.Catalog.Data.modules.First();
            model.requirements = System.Array.Empty<Requirement>();
            model.price = 101;
            fixture.Catalog.Data.rig.saleFraction = .75f;
            sim.Career.credits = 5000;
            var beforePurchase = sim.Career.credits;
            var part = sim.BuyModule(model.id);
            Assert.That(part, Is.Not.Null);
            Assert.That(sim.Career.credits, Is.EqualTo(beforePurchase - 101));
            Assert.That(sim.ModuleSalePrice(part.instanceId), Is.EqualTo(75));
            Assert.That(sim.SellModule(part.instanceId), Is.True);
            Assert.That(sim.Career.credits, Is.EqualTo(beforePurchase - 26));
            Assert.That(sim.Part(part.instanceId), Is.Null);

            var installed = sim.InstalledParts.Select(p => (p.instanceId, p.row, p.column, p.rotated)).ToArray();
            var next = sim.NextChassis;
            sim.Career.jobs = next.requiredJobs;
            sim.Career.credits = next.price;
            Assert.That(sim.UpgradeChassis(), Is.True);
            Assert.That(sim.Career.chassisId, Is.EqualTo(next.id));
            Assert.That(sim.Career.credits, Is.Zero);
            Assert.That(sim.InstalledParts.Select(p => (p.instanceId, p.row, p.column, p.rotated)).ToArray(),
                Is.EqualTo(installed));
        }

        [Test]
        public void RigNeighborRulesCountOnlyOrthogonallyAdjacentMatchingModules()
        {
            var sim = fixture.Simulation();
            var model = fixture.Catalog.Data.modules.First(m => m.width == 1 && m.height == 1);
            var other = fixture.Catalog.Data.modules.First(m => m.width == 1 && m.height == 1 && m.id != model.id);
            model.family = "test-family";
            other.family = "other-family";
            sim.Career.rigParts = new System.Collections.Generic.List<RigPart>
            {
                new RigPart { instanceId = "source", modelId = model.id, row = 0, column = 0 },
                new RigPart { instanceId = "adjacent", modelId = other.id, row = 0, column = 1 },
                new RigPart { instanceId = "diagonal", modelId = other.id, row = 1, column = 1 }
            };
            var context = sim.Context().ForRig("source");

            Assert.That(sim.RigNeighborCount("source", "test-family"), Is.Zero);
            Assert.That(sim.RigNeighborCount("source", "other-family"), Is.EqualTo(1));
            Assert.That(new RigNeighborCondition { kind = "other-family", minimum = 1 }.Test(context), Is.True);
            Assert.That(new RigNeighborCondition { kind = "other-family", minimum = 2 }.Test(context), Is.False);
        }

        [Test]
        public void WaypointsHonorTierCapacityAndScopeByContractSeed()
        {
            var sim = fixture.Simulation();
            var tierFour = fixture.Catalog.Data.technologies.First(t => t.waypointTier == 4);
            sim.Career.unlocked.Add(tierFour.id);
            var job = fixture.Catalog.Contracts(sim.Career).First(c => !c.recurring && c.Intel != ContractIntel.None);
            job.requiredWins = 0;
            var network = NetworkGenerator.Generate(job, sim.Career, fixture.Catalog);
            var targets = network.nodes.Select(n => n.id).Take(5).ToArray();

            Assert.That(sim.WaypointTier, Is.EqualTo(4));
            Assert.That(sim.PlaceWaypoint(job, targets[0]), Is.True);
            Assert.That(sim.PlaceWaypoint(job, targets[0]), Is.False, "Duplicate nodes cannot be marked.");
            Assert.That(sim.PlaceWaypoint(job, targets[1]), Is.True);
            Assert.That(sim.PlaceWaypoint(job, targets[2]), Is.True);
            Assert.That(sim.PlaceWaypoint(job, targets[3]), Is.True);
            Assert.That(sim.PlaceWaypoint(job, targets[4]), Is.False, "Tier four caps the plan at four markers.");
            Assert.That(sim.Waypoints(job).Count, Is.EqualTo(4));

            Assert.That(sim.EditWaypoint(job, targets[0], 1, new string('L', 45), true), Is.True);
            Assert.That(sim.EditWaypoint(job, targets[1], 2, "  Second target  ", true), Is.True);
            Assert.That(sim.Waypoints(job).First(w => w.nodeId == targets[0]).label, Is.EqualTo(new string('L', 40)));
            Assert.That(sim.Waypoints(job).First(w => w.nodeId == targets[1]).label, Is.EqualTo("Second target"));
            Assert.That(sim.Waypoints(job).Single(w => w.nodeId == targets[1]).showPath, Is.True);
            Assert.That(sim.Waypoints(job).Single(w => w.nodeId == targets[0]).showPath, Is.False);

            var otherSeed = new Contract { id = job.id, seed = job.seed + 1 };
            Assert.That(sim.Waypoints(otherSeed), Is.Empty);
            Assert.That(sim.RemoveWaypoint(job, targets[1]), Is.True);
            Assert.That(sim.RemoveWaypoint(job, targets[1]), Is.False);
        }

        [Test]
        public void WaypointPathTreatsLinksAsUndirectedAndReportsDisconnectedTargets()
        {
            var nodes = new System.Collections.Generic.List<NetNode>
            {
                new NetNode { id = "a" }, new NetNode { id = "b" }, new NetNode { id = "c" }, new NetNode { id = "isolated" }
            };
            var links = new System.Collections.Generic.List<NetLink> { new NetLink("b", "a"), new NetLink("b", "c") };

            Assert.That(GameSimulation.WaypointPath(nodes, links, "a", "c").IsReachable, Is.True);
            Assert.That(GameSimulation.WaypointPath(nodes, links, "a", "isolated").IsReachable, Is.False);
        }
    }
}
