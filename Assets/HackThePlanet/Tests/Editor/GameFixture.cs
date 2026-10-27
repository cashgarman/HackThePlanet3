using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace HackThePlanet.Tests
{
    internal sealed class GameFixture : IDisposable
    {
        private readonly ContentBundle content;
        internal ContentBundle Content => content;
        internal readonly GameCatalog Catalog;
        internal readonly string DirectoryPath;

        internal GameFixture()
        {
            var data = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "data"));
            content = ContentJson.Load(data);
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
