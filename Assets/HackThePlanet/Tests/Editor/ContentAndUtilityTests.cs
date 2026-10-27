using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HackThePlanet.Tests
{
    public sealed class ContentAndUtilityTests
    {
        private GameFixture fixture;
        private GameplayTextPalette originalPalette;
        private GameplayTextPalette testPalette;

        [SetUp]
        public void SetUp()
        {
            fixture = new GameFixture();
            originalPalette = GameplayRichText.Palette;
        }

        [TearDown]
        public void TearDown()
        {
            GameplayRichText.Palette = originalPalette;
            if (testPalette != null)
                Object.DestroyImmediate(testPalette);
            fixture?.Dispose();
        }

        [Test]
        public void ContentLoaderUsesOverridesAndReportsInvalidEntries()
        {
            var dataDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "data"));
            var actionEntry = fixture.Content.Manifest.entries.First(e => e.type == nameof(ActionDefinition));
            var action = (ActionDefinition)fixture.Content.Objects[actionEntry.key];
            var original = fixture.Content.Files[actionEntry.file];
            var titleField = "\"title\": \"" + action.title + "\"";
            Assert.That(original, Does.Contain(titleField));

            var overridden = original.Replace(titleField, "\"title\": \"Fixture override\"");
            using (var loaded = ContentJson.Load(dataDirectory,
                       new Dictionary<string, string> { [actionEntry.file] = overridden }))
                Assert.That(((ActionDefinition)loaded.Objects[actionEntry.key]).title, Is.EqualTo("Fixture override"));

            using (var partial = ContentJson.Load(dataDirectory,
                       new Dictionary<string, string> { [actionEntry.file] = "{" }, validate: false))
            {
                Assert.That(partial.Errors, Is.Not.Empty);
                Assert.That(partial.InvalidEntries, Does.Contain(actionEntry.key));
            }

            Assert.Throws<System.IO.InvalidDataException>(() => ContentJson.Load(dataDirectory,
                new Dictionary<string, string> { [actionEntry.file] = "{" }));
        }

        [Test]
        public void CatalogGatesRecurringContractsAndFormatsRuntimeTokens()
        {
            var career = fixture.Catalog.CreateCareer();
            var recurring = fixture.Catalog.Data.contracts.First(c => c.recurring);
            var initiallyAvailable = fixture.Catalog.Contracts(career).Select(c => c.id).ToArray();
            Assert.That(initiallyAvailable, Does.Not.Contain(recurring.id));

            career.completed = fixture.Catalog.Data.contracts.Where(c => !c.recurring)
                .Take(recurring.requiredWins).Select(c => c.id).ToList();
            var unlocked = fixture.Catalog.Contracts(career).First(c => c.id == recurring.id);
            Assert.That(unlocked.recurring, Is.True);

            var context = fixture.Catalog.Context(career, unlocked);
            context.Node = new NetNode { label = "Test vault" };
            var formatted = fixture.Catalog.Format("{target} / {aftermath} / {archiveCredits}", context);
            Assert.That(formatted, Does.StartWith("Test vault / " + unlocked.aftermath + " / "));
            Assert.That(formatted, Does.EndWith(fixture.Catalog.Data.rewards.archiveCredits.ToString()));
        }

        [Test]
        public void GameplayRichTextResolvesOverlapsRegexAndPlainText()
        {
            testPalette = ScriptableObject.CreateInstance<GameplayTextPalette>();
            testPalette.terms = new[]
            {
                new GameplayTextPalette.TermStyle { phrase = "SECURITY", color = Color.red },
                new GameplayTextPalette.TermStyle { phrase = "SECURITY 9", color = Color.green },
                new GameplayTextPalette.TermStyle { phrase = "TRACE \\d+", regex = true, color = Color.cyan }
            };
            GameplayRichText.Palette = testPalette;

            var rich = GameplayRichText.Format("Security 9 and trace 42");
            Assert.That(Regex.Matches(rich, "<color=").Count, Is.EqualTo(2));
            Assert.That(rich, Does.Contain("SECURITY 9").IgnoreCase);
            Assert.That(rich, Does.Contain("trace 42").IgnoreCase);
            Assert.That(GameplayRichText.Plain(rich), Is.EqualTo("Security 9 and trace 42"));
            Assert.That(GameplayRichText.Format("plain text"), Is.EqualTo("plain text"));
        }

        [Test]
        public void PaletteHexParsesRgbAndFallsBackForInvalidInput()
        {
            var parsed = Palette.Hex("12ABEF");
            Assert.That(parsed.r, Is.EqualTo(0x12 / 255f).Within(.001f));
            Assert.That(parsed.g, Is.EqualTo(0xAB / 255f).Within(.001f));
            Assert.That(parsed.b, Is.EqualTo(0xEF / 255f).Within(.001f));
            Assert.That(Palette.Hex("not-hex"), Is.EqualTo(Color.white));
        }
    }
}
