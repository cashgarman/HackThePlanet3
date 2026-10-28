using System;
using System.IO;
using System.Linq;
using System.IO.Compression;
using UnityEngine;

namespace HackThePlanet.Tests
{
    internal sealed class GameFixture : IDisposable
    {
        private readonly ContentBundle content;
        internal ContentBundle Content => content;
        internal readonly GameCatalog Catalog;
        internal readonly string DirectoryPath;

        internal GameFixture(bool legacy = true)
        {
            var data = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "data"));
            if (legacy)
            {
                data = Path.Combine(Path.GetTempPath(), "HackThePlanet.LegacyFixture", Guid.NewGuid().ToString("N"));
                System.IO.Compression.ZipFile.ExtractToDirectory(Path.Combine(Application.dataPath, "..", "Docs", "LegacyV3", "catalog.zip"), data);
            }
            content = ContentJson.Load(data);
            if (legacy) content.Game.contracts.Last(c => !c.recurring).isEnding = true;
            if (legacy) Directory.Delete(data, true);
            Catalog = new GameCatalog(content.Game);
            DirectoryPath = Path.Combine(Path.GetTempPath(), "HackThePlanet.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
        }

        internal GameSimulation Simulation() => new GameSimulation(Catalog);

        internal Contract FirstContract(GameSimulation sim) => Catalog.Contracts(sim.Career).First();

        internal string SaveFile => Path.Combine(DirectoryPath, "career.json");

        public void Dispose()
        {
            Catalog.Dispose();
            content.Dispose();
            if (Directory.Exists(DirectoryPath))
                Directory.Delete(DirectoryPath, true);
        }
    }
}
