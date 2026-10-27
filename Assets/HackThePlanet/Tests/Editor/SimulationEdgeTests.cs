using System;
using System.Linq;
using NUnit.Framework;

namespace HackThePlanet.Tests
{
    public sealed class SimulationEdgeTests
    {
        private GameFixture fixture;

        [SetUp]
        public void SetUp() => fixture = new GameFixture();

        [TearDown]
        public void TearDown() => fixture?.Dispose();

        private GameSimulation Started()
        {
            var sim = fixture.Simulation();
            Assert.That(sim.Start(fixture.FirstContract(sim)), Is.True);
            return sim;
        }

        private static NetNode ReachableTarget(GameSimulation sim) =>
            sim.Op.nodes.First(n => n.known && !n.controlled && sim.Reachable(n));

        [Test]
        public void PreviewRejectsUnknownActionAndUnavailableTargetThenEnforcesUseLimit()
        {
            var sim = Started();
            Assert.That(sim.Preview("missing-action", null).allowed, Is.False);

            var target = ReachableTarget(sim);
            target.known = false;
            Assert.That(sim.Preview("hack", target.id).allowed, Is.False);
            target.known = true;

            var hack = fixture.Catalog.Data.actions.Single(a => a.id == "hack");
            hack.useLimit = new ConstantNumber(1);
            Assert.That(sim.UsesRemaining("hack", target.id), Is.EqualTo(1));
            Assert.That(sim.Execute("hack", target.id), Is.True);
            sim.SetManualPause(false);
            for (var i = 0; i < 2000 && sim.Op.task != null; i++)
                sim.Tick(.25f);

            Assert.That(sim.Counter("uses:hack"), Is.EqualTo(1));
            Assert.That(sim.UsesRemaining("hack", target.id), Is.Zero);
            Assert.That(sim.Preview("hack", target.id).allowed, Is.False);
        }

        [Test]
        public void QueueCanBeClearedAndStoppingTaskClearsFollowUps()
        {
            var sim = Started();
            var first = ReachableTarget(sim);
            var targets = new[] { first, sim.Op.nodes.First(n => !n.controlled && n.id != first.id) };
            targets[1].known = true;
            fixture.Catalog.Data.actions.Single(a => a.id == "hack").futureNeighborQueue = false;

            Assert.That(sim.Execute("hack", targets[0].id), Is.True);
            Assert.That(sim.CanQueue("hack", targets[1].id), Is.True);
            Assert.That(sim.Execute("hack", targets[1].id), Is.True);
            Assert.That(sim.Op.queue, Has.Count.EqualTo(1));

            sim.ClearQueue();
            Assert.That(sim.Op.queue, Is.Empty);
            Assert.That(sim.CanQueue("hack", targets[1].id), Is.True);
            Assert.That(sim.Execute("hack", targets[1].id), Is.True);
            Assert.That(sim.StopTask(), Is.True);
            Assert.That(sim.Op.task, Is.Null);
            Assert.That(sim.Op.queue, Is.Empty);
        }

        [Test]
        public void TimedStatusCountsDownAndClearsNodeSuppression()
        {
            var sim = Started();
            var target = ReachableTarget(sim);
            var status = fixture.Catalog.Data.statuses.First(s => s.disablesDefense);
            sim.ApplyStatus(status, target.id, 1f);
            Assert.That(sim.StatusRemaining(status.id, target.id), Is.EqualTo(1f));
            Assert.That(target.suppressed, Is.EqualTo(1f));

            sim.Tick(.5f);
            Assert.That(sim.StatusRemaining(status.id, target.id), Is.EqualTo(.5f).Within(.001f));
            sim.Tick(.5f);
            Assert.That(sim.StatusRemaining(status.id, target.id), Is.Zero);
            Assert.That(target.suppressed, Is.Zero);
        }

        [Test]
        public void EffectsEnforceSecurityFloorSetStateAndFormatOperationLog()
        {
            var sim = Started();
            var target = ReachableTarget(sim);
            target.security = 5;
            var context = sim.Context(target);
            sim.ApplyEffects(new Effect[]
            {
                new SecurityEffect { minimum = 2, amount = new ConstantNumber(-20) },
                new SetStateEffect { state = NodeState.Scanned, value = true },
                new LogEffect { message = "TEST // {target}" }
            }, context);

            Assert.That(target.security, Is.EqualTo(2));
            Assert.That(target.scanned, Is.True);
            Assert.That(sim.Op.log.Last(), Does.Contain(target.label));
        }

        [Test]
        public void ModifiersApplyInOrderAndSecurityHonorsMinimumFloor()
        {
            var sim = Started();
            var target = ReachableTarget(sim);
            target.security = 2;
            var baseMinimum = sim.Stat("minimumSecurity");
            fixture.Catalog.Data.modifiers = fixture.Catalog.Data.modifiers.Concat(new[]
            {
                new Modifier { stat = "minimumSecurity", operation = Arithmetic.Add, value = new ConstantNumber(2) },
                new Modifier { stat = "security", operation = Arithmetic.Override, value = new ConstantNumber(0) }
            }).ToArray();

            Assert.That(sim.EffectiveSecurity(target), Is.EqualTo((int)Math.Round(baseMinimum + 2)));
        }

        [Test]
        public void PersistentActionReservesAndReleasesMemoryOnToggle()
        {
            var sim = Started();
            var blackout = fixture.Catalog.Data.actions.Single(a => a.id == "blackout");
            blackout.initiallyAvailable = true;
            blackout.operatorTarget = true;
            blackout.visible = new AlwaysCondition();
            blackout.requirements = Array.Empty<Requirement>();
            blackout.duration = new ConstantNumber(0);
            blackout.instant = true;
            blackout.memoryMode = MemoryMode.Persistent;
            blackout.memoryCost = 3;
            blackout.effects = Array.Empty<Effect>();
            var before = sim.MemoryFree;

            Assert.That(sim.Execute("blackout"), Is.True);
            Assert.That(sim.IsAbilityActive("blackout"), Is.True);
            Assert.That(sim.MemoryFree, Is.EqualTo(before - 3));
            Assert.That(sim.Execute("blackout"), Is.True);
            Assert.That(sim.IsAbilityActive("blackout"), Is.False);
            Assert.That(sim.MemoryFree, Is.EqualTo(before));
        }

        [Test]
        public void ResearchPurchaseRevealsChildrenAndRefundRestoresStarterState()
        {
            var sim = fixture.Simulation();
            var initial = fixture.Catalog.Data.career.initialTechnologies.Select(t => t.id).ToArray();
            var tech = fixture.Catalog.Data.technologies.First(t => !sim.Has(t.id) && t.parents.All(p => sim.Has(p.id)));
            var children = fixture.Catalog.Data.technologies.Where(t => t.parents.Any(p => p.id == tech.id))
                .Select(t => t.id).ToArray();
            sim.Career.xp = tech.cost + 50;
            var startingXp = sim.Career.xp;

            Assert.That(sim.BuyTech(tech.id), Is.True);
            Assert.That(sim.ResearchVisible(tech.id), Is.True);
            Assert.That(children.All(sim.ResearchVisible), Is.True);
            var xpAfterPurchase = sim.Career.xp;
            var preview = sim.PreviewResearchRefund();
            Assert.That(sim.Has(tech.id), Is.True, "Preview must not mutate unlocked research.");
            Assert.That(sim.Career.xp, Is.EqualTo(xpAfterPurchase));

            Assert.That(sim.RefundResearch(), Is.True);
            Assert.That(sim.Career.unlocked, Is.EquivalentTo(initial));
            Assert.That(sim.Career.researchSpent, Is.Zero);
            Assert.That(sim.Career.xp, Is.EqualTo(xpAfterPurchase + preview.xp));
            Assert.That(sim.Career.xp, Is.EqualTo(startingXp - tech.cost + preview.xp));
            Assert.That(sim.Career.equipped, Is.Empty);
        }

        [Test]
        public void SpecialResearchEquipmentRespectsChassisSlots()
        {
            var sim = fixture.Simulation();
            var specials = fixture.Catalog.Data.technologies.Where(t => t.special).Take(2).ToArray();
            Assert.That(specials, Has.Length.EqualTo(2));
            sim.Chassis.slots = 1;
            sim.Career.unlocked = sim.Career.unlocked.Concat(specials.Select(t => t.id)).Distinct().ToList();
            sim.Career.equipped.Clear();

            Assert.That(sim.ToggleEquip(specials[0].id), Is.True);
            Assert.That(sim.ToggleEquip(specials[1].id), Is.False);
            Assert.That(sim.ToggleEquip(specials[0].id), Is.True);
            Assert.That(sim.ToggleEquip(specials[1].id), Is.True);
        }

        [Test]
        public void CaughtDisconnectDoesNotBankProgressOrRewards()
        {
            var sim = Started();
            var credits = sim.Career.credits;
            var xp = sim.Career.xp;
            var jobs = sim.Career.jobs;
            var report = sim.Disconnect(caught: true);

            Assert.That(report.success, Is.False);
            Assert.That(report.verdict, Does.Contain("ISOLATED"));
            Assert.That(sim.Career.credits, Is.EqualTo(credits));
            Assert.That(sim.Career.xp, Is.EqualTo(xp));
            Assert.That(sim.Career.jobs, Is.EqualTo(jobs));
        }
    }
}
