using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
namespace HackThePlanet.Tests
{
    public sealed class VesperCampaignTests
    {
        private GameFixture f;
        [SetUp] public void Setup() => f = new GameFixture(false);
        [TearDown] public void Cleanup() => f.Dispose();
        private GameSimulation Start(string id)
        {
            var sim = f.Simulation(); var d = f.Catalog.Contract(id);
            void Include(ContractDefinition item) { foreach (var key in item.prerequisites) { if (!sim.Career.completed.Contains(key)) { Include(f.Catalog.Contract(key)); sim.Career.completed.Add(key); } } }
            Include(d); sim.Career.storyFlags.AddRange(d.requiredFlags); sim.RefreshOffers();
            Assert.That(sim.Start(f.Catalog.Contracts(sim.Career).First(c => c.id == id)), Is.True); return sim;
        }
        private static void Finish(GameSimulation s) { for (var i = 0; s.Active && s.Op.task != null && i < 3000; i++) s.Tick(.5f); Assert.That(s.Active, Is.True); Assert.That(s.Op.task, Is.Null); }
        [Test] public void NewCareerUsesV4AndNeverLoadsV3Path()
        { Assert.That(CareerStorage.SavePath, Does.EndWith("career-v4.json")); Assert.That(f.Simulation().Career.version, Is.EqualTo(4)); }
        [Test] public void AllianceChoiceIsExclusiveAndSurvivesReload()
        {
            var s=f.Simulation();s.Career.completed.AddRange(new[]{"ghost-meter","redacted-hours","unlisted-operator","listening-room"});
            Assert.That(s.ChooseStory("shelter"), Is.True);Assert.That(s.ChooseStory("expose"), Is.False);
            var loaded=new GameSimulation(f.Catalog,JsonUtility.FromJson<Career>(JsonUtility.ToJson(s.Career)));
            Assert.That(loaded.ContractAvailable(f.Catalog.Contract("cold-clinic")),Is.True);
            Assert.That(loaded.ContractAvailable(f.Catalog.Contract("margin-notes")),Is.False);
            Assert.That(loaded.StoryComplete,Is.False);
        }
        [Test] public void ExpiredSideOfferCannotCloseMainOrFreelance()
        {
            var s=f.Simulation();s.Career.storyFlags.Add("expose");s.Career.completed.Add("margin-notes");s.RefreshOffers();s.Career.day+=4;
            Assert.That(s.OfferExpired(f.Catalog.Contract("expose-side-1")),Is.True);
            Assert.That(s.ContractAvailable(f.Catalog.Contract("sealed-hearing")),Is.True);
            Assert.That(s.ContractAvailable(f.Catalog.Contract("vesper-freelance-0")),Is.True);
        }
        [Test] public void FailureAdvancesOneDayAndRetainsAllPurchasedProgress()
        {
            var s=Start("ghost-meter");var parts=s.Career.rigParts.Select(p=>p.instanceId).ToArray();var day=s.Career.day;var money=s.Career.credits;
            Assert.That(s.Disconnect(true).success,Is.False);Assert.That(s.Career.day,Is.EqualTo(day+1));Assert.That(s.Career.credits,Is.EqualTo(money));
            Assert.That(s.Career.rigParts.Select(p=>p.instanceId),Is.EqualTo(parts));Assert.That(s.ContractAvailable(f.Catalog.Contract("ghost-meter")),Is.True);
        }
        [Test] public void EvidenceRequiresAllCluesAndWrongAnswerHasNoTracePenalty()
        {
            var s=Start("unlisted-operator");Assert.That(s.SolveEvidence("rehearsal"),Is.False);
            foreach(var id in f.Catalog.Contract("unlisted-operator").evidence.clueNodes)s.Node(id).collected=true;
            var trace=s.Op.trace;Assert.That(s.SolveEvidence("accident"),Is.False);Assert.That(s.Op.trace,Is.EqualTo(trace));
            Assert.That(s.SolveEvidence("rehearsal"),Is.True);
            var restored=new GameSimulation(f.Catalog,JsonUtility.FromJson<Career>(JsonUtility.ToJson(s.Career)));Assert.That(restored.EvidenceSolved,Is.True);
        }
        [Test] public void WatcherForecastIncludesMonitoredIngressAndControllerCaptureCountersIt()
        {
            var s=Start("listening-room");foreach(var n in s.Op.nodes)n.known=true;s.Node("n1").controlled=true;
            var before=s.Preview("hack","n3");s.Node("n14").controlled=true;var after=s.Preview("hack","n3");
            Assert.That(before.trace-after.trace,Is.EqualTo(6).Within(.01));
        }
        [Test] public void PurgeArmsOnLinkedWritePausesAndCanBePermanentlyCountered()
        {
            var s=Start("redacted-hours");foreach(var n in s.Op.nodes)n.known=n.controlled=true;s.Node("n14").controlled=false;
            Assert.That(s.Execute("alter-record","n22"),Is.True);Finish(s);var purge=s.Defenses.Single(d=>d.kind==DefenseKind.PurgeTimer);
            Assert.That(purge.keyAcquired,Is.True);var time=purge.remaining;s.SetManualPause(true);s.Tick(.5f);Assert.That(purge.remaining,Is.EqualTo(time));s.SetManualPause(false);
            s.Node("n14").suppressed=10;s.Tick(.5f);Assert.That(purge.remaining,Is.EqualTo(time));s.Node("n14").controlled=true;s.Tick(.5f);Assert.That(purge.disabled,Is.True);
        }
        [Test] public void UncounteredPurgeEndsAttemptAndKeepsContractRetryable()
        {
            var s=Start("redacted-hours");var d=s.Defenses.Single(d=>d.kind==DefenseKind.PurgeTimer);d.keyAcquired=true;d.remaining=.25f;s.Tick(.5f);
            Assert.That(s.Active,Is.False);Assert.That(s.LastReport.success,Is.False);Assert.That(s.ContractAvailable(f.Catalog.Contract("redacted-hours")),Is.True);
        }
        [Test] public void SelectiveRefundUsesPaidInvestmentAndKeepsOtherBranches()
        {
            var s=f.Simulation();s.Career.xp=500;Assert.That(s.BuyTech("wide")&&s.BuyTech("signal")&&s.BuyTech("quiet"),Is.True);
            var xp=s.Career.xp;f.Catalog.Technology("wide").cost=999;
            Assert.That(s.RefundTechnology("wide"),Is.True);Assert.That(s.Career.xp,Is.EqualTo(xp+115));Assert.That(s.Has("signal"),Is.False);Assert.That(s.Has("quiet"),Is.True);
        }
    }
}
