using System.IO;
using NUnit.Framework;

namespace HackThePlanet.Tests
{
    public sealed class PersistenceTests
    {
        private GameFixture fixture;

        [SetUp] public void SetUp() => fixture = new GameFixture();
        [TearDown] public void TearDown() => fixture?.Dispose();

        [Test]
        public void SaveAndLoadPreserveCareerProgressAndActiveOperation()
        {
            var sim = fixture.Simulation();
            sim.Career.credits += 41;
            sim.Career.xp += 12;
            Assert.That(sim.Start(fixture.FirstContract(sim)), Is.True);
            var savedSeed = sim.Op.seed;
            Assert.That(CareerStorage.Save(sim.Career, fixture.SaveFile, fixture.Catalog), Is.True);
            var restored = new GameSimulation(fixture.Catalog,
                CareerStorage.Load(fixture.SaveFile, fixture.Catalog));
            Assert.That(restored.Career.credits, Is.EqualTo(sim.Career.credits));
            Assert.That(restored.Career.xp, Is.EqualTo(sim.Career.xp));
            Assert.That(restored.Op.seed, Is.EqualTo(savedSeed));
            Assert.That(restored.Op.paused, Is.True);
            Assert.That(restored.Op.nodes.Count, Is.EqualTo(sim.Op.nodes.Count));
        }

        [Test]
        public void CorruptPrimaryRecoversFromBackupAndBlocksFreshAutosave()
        {
            var sim = fixture.Simulation();
            sim.Career.credits = 100;
            Assert.That(CareerStorage.Save(sim.Career, fixture.SaveFile, fixture.Catalog), Is.True);
            sim.Career.credits = 200;
            Assert.That(CareerStorage.Save(sim.Career, fixture.SaveFile, fixture.Catalog), Is.True);
            File.WriteAllText(fixture.SaveFile, "not JSON");
            var fromBackup = CareerStorage.Load(fixture.SaveFile, fixture.Catalog);
            Assert.That(fromBackup.credits, Is.EqualTo(100));
            Assert.That(CareerStorage.Warning, Is.Not.Empty);

            var isolated = Path.Combine(fixture.DirectoryPath, "no-backup.json");
            File.WriteAllText(isolated, "not JSON");
            var fresh = CareerStorage.Load(isolated, fixture.Catalog);
            Assert.That(fresh.saveBlocked, Is.True);
            Assert.That(CareerStorage.Save(fresh, isolated, fixture.Catalog), Is.False);
            Assert.That(File.ReadAllText(isolated), Is.EqualTo("not JSON"));
        }

        [Test]
        public void UnsupportedVersionIsRejectedWithoutRewritingSave()
        {
            const string unsupported = "{\"version\":2,\"credits\":900}";
            File.WriteAllText(fixture.SaveFile, unsupported);
            Assert.Throws<InvalidDataException>(() => CareerStorage.Load(fixture.SaveFile, fixture.Catalog));
            Assert.That(File.ReadAllText(fixture.SaveFile), Is.EqualTo(unsupported));
        }

        [Test]
        public void InvalidCareerCannotReplaceValidSave()
        {
            var sim = fixture.Simulation();
            Assert.That(CareerStorage.Save(sim.Career, fixture.SaveFile, fixture.Catalog), Is.True);
            var original = File.ReadAllText(fixture.SaveFile);
            sim.Career.version = 2;
            Assert.That(CareerStorage.Save(sim.Career, fixture.SaveFile, fixture.Catalog), Is.False);
            Assert.That(File.ReadAllText(fixture.SaveFile), Is.EqualTo(original));
        }
    }
}
