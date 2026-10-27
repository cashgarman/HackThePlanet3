using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace HackThePlanet.Tests
{
	public sealed class SecurityLayerTests
	{
		private GameFixture fixture;
		[SetUp] public void SetUp() => fixture = new GameFixture();
		[TearDown] public void TearDown() => fixture.Dispose();

		private GameSimulation Start(string id, params string[] research)
		{
			var sim = fixture.Simulation();
			sim.Career.xp = 1000;
			var definition = fixture.Catalog.Data.contracts.First(c => c.id == id);
			sim.Career.completed.AddRange(fixture.Catalog.Data.contracts.Where(c => !c.recurring &&
				c.requiredWins < definition.requiredWins).Select(c => c.id));
			sim.Career.jobs = sim.Career.completed.Count;
			foreach (var tech in research) Assert.That(sim.BuyTech(tech), Is.True, tech);
			Assert.That(sim.Start(fixture.Catalog.Contracts(sim.Career).First(c => c.id == id)), Is.True);
			return sim;
		}

		private static void Finish(GameSimulation sim)
		{
			for (var i = 0; i < 1000 && sim.Active && sim.Op.task != null; i++) sim.Tick(.5f);
			Assert.That(sim.Active && sim.Op.task == null, Is.True, "Action did not finish safely.");
		}

		[Test]
		public void CredentialCanBeExtractedOrSpoofedAndIsSaved()
		{
			var extract = Start("glasshouse", "wide", "credential-analysis");
			extract.Node("n9").controlled = extract.Node("n9").known = true;
			extract.Node("n22").controlled = extract.Node("n22").known = true;
			Assert.That(extract.Preview("download", "n22").allowed, Is.False);
			Assert.That(extract.Execute("extract-key", "n9"), Is.True);
			Finish(extract);
			Assert.That(extract.IsCredentialLocked("n22"), Is.False);
			Assert.That(extract.Preview("download", "n22").allowed, Is.True);

			var restored = new GameSimulation(fixture.Catalog,
				JsonUtility.FromJson<Career>(JsonUtility.ToJson(extract.Career)));
			Assert.That(restored.IsCredentialLocked("n22"), Is.False);
			Assert.That(restored.Op.paused, Is.True);
			Assert.That(extract.Execute("download", "n22"), Is.True);
			Finish(extract);
			Assert.That(extract.Disconnect().success, Is.True);
			var credits = extract.Career.credits;
			Assert.That(extract.Disconnect(), Is.Null);
			Assert.That(extract.Career.credits, Is.EqualTo(credits));

			var spoof = Start("glasshouse", "efficient", "protocol-spoof");
			spoof.Node("n22").controlled = spoof.Node("n22").known = true;
			Assert.That(spoof.Execute("spoof-gate", "n22"), Is.True);
			Finish(spoof);
			Assert.That(spoof.IsCredentialLocked("n22"), Is.False);
			Assert.That(spoof.Counter("uses:spoof-gate"), Is.EqualTo(1));
		}

		[Test]
		public void SweepPausesAndLoopFeedBlocksBursts()
		{
			var sim = Start("dead-drop", "quiet", "loop-feed");
			var sweep = sim.Defenses.Single(d => d.kind == DefenseKind.SecuritySweep);
			for (var i = 0; i < 60; i++) sim.Tick(.5f);
			Assert.That(sweep.remaining, Is.EqualTo(30).Within(.01f));
			sim.SetManualPause(true);
			for (var i = 0; i < 120; i++) sim.Tick(.5f);
			Assert.That(sweep.remaining, Is.EqualTo(30).Within(.01f));
			sim.SetManualPause(false);
			Assert.That(sim.Execute("loop-feed"), Is.True);
			Finish(sim);
			var trace = sim.Op.trace;
			for (var i = 0; i < 120; i++) sim.Tick(.5f);
			Assert.That(sweep.remaining, Is.EqualTo(30).Within(.01f));
			Assert.That(sim.Op.trace, Is.EqualTo(trace).Within(.01f));
			Assert.That(sim.Execute("loop-feed"), Is.True);
			for (var i = 0; i < 60; i++) sim.Tick(.5f);
			Assert.That(sim.Op.trace, Is.EqualTo(trace + 12).Within(.01f));
		}

		[Test]
		public void OldActiveOperationDoesNotGainNewDefenses()
		{
			var sim = Start("dead-drop");
			sim.Op.defenses = new List<DefenseState>();
			var legacyJson = JsonUtility.ToJson(sim.Career).Replace("\"defenses\":[],", "");
			Assert.That(legacyJson, Does.Not.Contain("\"defenses\""));
			var restored = new GameSimulation(fixture.Catalog,
				JsonUtility.FromJson<Career>(legacyJson));
			Assert.That(restored.Defenses, Is.Empty);
			Assert.That(restored.Preview("download", "n22").reason,
				Does.Not.Contain("Credential lock"));
		}

		[Test]
		public void ForecastIncludesSweepAfterTemporarySuppressionExpires()
		{
			var sim = Start("quiet-copy");
			sim.Op.defenses.Add(new DefenseState(new ContractDefenseDefinition
			{
				id = "forecast", kind = DefenseKind.SecuritySweep, sourceNodeId = "n14"
			}) { remaining = 1 });
			sim.ApplyStatus(fixture.Catalog.Data.statuses.First(s => s.disablesDefense), "n14", 1);
			var preview = sim.Preview("hack", "n2");
			Assert.That(preview.duration, Is.GreaterThan(2));
			Assert.That(preview.projectedTrace, Is.EqualTo(preview.trace + 12).Within(.01f));
		}

		[Test]
		public void HardResetSurvivesSaveAndReleasesOneShotRam()
		{
			var sim = Start("dead-drop", "disrupt", "hard-reset");
			foreach (var node in sim.Op.nodes) node.known = true;
			sim.Node("n10").controlled = true;
			Assert.That(sim.Execute("hard-reset", "n14"), Is.True);
			Finish(sim);
			Assert.That(sim.Defenses.Single(d => d.kind == DefenseKind.SecuritySweep).disabled, Is.True);
			Assert.That(sim.MemoryReclaiming, Is.Zero);
			var restored = new GameSimulation(fixture.Catalog,
				JsonUtility.FromJson<Career>(JsonUtility.ToJson(sim.Career)));
			Assert.That(restored.NextSweepSeconds, Is.Null);
		}

		[Test]
		public void DecoderBoardIsResearchGatedAndImprovesBandwidth()
		{
			var sim = fixture.Simulation();
			Assert.That(sim.PreviewModule("decoder-board").allowed, Is.False);
			sim.Career.xp = sim.Career.credits = 10000;
			sim.Career.jobs = 3;
			Assert.That(sim.BuyTech("wide"), Is.True);
			Assert.That(sim.BuyTech("data-engineering"), Is.True);
			Assert.That(sim.UpgradeChassis() && sim.UpgradeChassis(), Is.True);
			var before = sim.Bandwidth;
			var part = sim.BuyModule("decoder-board");
			Assert.That(part, Is.Not.Null);
			Assert.That(sim.PlaceFirstFree(part.instanceId), Is.True);
			Assert.That(sim.RigValid, Is.True);
			Assert.That(sim.Bandwidth, Is.GreaterThan(before));
		}
	}
}
