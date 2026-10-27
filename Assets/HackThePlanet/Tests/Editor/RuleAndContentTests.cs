using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HackThePlanet.Tests
{
    public sealed class RuleAndContentTests
    {
        [TestCase(Arithmetic.Add, 7f, 3f, 10f)]
        [TestCase(Arithmetic.Subtract, 7f, 3f, 4f)]
        [TestCase(Arithmetic.Multiply, 7f, 3f, 21f)]
        [TestCase(Arithmetic.Divide, 7f, 2f, 3.5f)]
        [TestCase(Arithmetic.Minimum, 7f, 3f, 3f)]
        [TestCase(Arithmetic.Maximum, 7f, 3f, 7f)]
        [TestCase(Arithmetic.Override, 7f, 3f, 3f)]
        [TestCase(Arithmetic.Modulo, 7f, 3f, 1f)]
        [TestCase(Arithmetic.Floor, 7.5f, 0f, 7f)]
        public void ArithmeticProducesExpectedValues(Arithmetic operation, float left, float right, float expected)
        {
            Assert.That(RuleMath.Apply(operation, left, right), Is.EqualTo(expected));
        }

        [TestCase(Arithmetic.Divide)]
        [TestCase(Arithmetic.Modulo)]
        public void ArithmeticRejectsZeroDivisors(Arithmetic operation)
        {
            Assert.Throws<InvalidOperationException>(() => RuleMath.Apply(operation, 1, 0));
        }

        [Test]
        public void NumbersAndConditionsEvaluateAgainstContext()
        {
            var c = new RuleContext { Career = new Career { jobs = 4 }, Node = new NetNode { known = true } };
            var jobs = new InputNumber { input = NumberInput.JobsCompleted };
            var sum = new BinaryNumber { operation = Arithmetic.Add, left = jobs, right = new ConstantNumber(3) };
            var known = new NodeStateCondition { state = NodeState.Known };
            var gate = new AllCondition { conditions = new Condition[] { known, new CompareCondition
            {
                comparison = Comparison.Greater, left = sum, right = new ConstantNumber(6)
            } } };
            Assert.That(sum.Evaluate(c), Is.EqualTo(7));
            Assert.That(gate.Test(c), Is.True);
            c.Node.known = false;
            Assert.That(gate.Test(c), Is.False);
            Assert.That(new ConditionalNumber { when = known, yes = new ConstantNumber(10), no = jobs }.Evaluate(c), Is.EqualTo(4));
        }

        [Test]
        public void ConditionsResolveTagsResearchActionsAndNamedNodes()
        {
            using (var fixture = new GameFixture())
            {
                var sim = fixture.Simulation();
                Assert.That(sim.Start(fixture.FirstContract(sim)), Is.True);
                var node = sim.Op.nodes.First(n => !n.controlled);
                var action = fixture.Catalog.Data.actions.First(a => a.id == "hack");
                var tag = fixture.Catalog.NodeType(node.typeId).tags.First();
                var initialTechnology = sim.Career.unlocked.First();
                var context = sim.Context(node, action);

                Assert.That(new NodeTagCondition { tag = tag }.Test(context), Is.True);
                Assert.That(new ActionCondition { action = action, tag = "hack" }.Test(context), Is.True);
                Assert.That(new ResearchCondition
                    { technology = fixture.Catalog.Technology(initialTechnology) }.Test(context), Is.True);
                Assert.That(new NamedNodeCondition
                    { nodeId = node.id, condition = new NodeStateCondition { state = NodeState.Objective } }
                    .Test(context), Is.EqualTo(node.objective));
                Assert.That(new AnyCondition
                    { conditions = new Condition[] { new AlwaysCondition(), new NotCondition() } }.Test(context), Is.True);
            }
        }

        [Test]
        public void RuleRecursionAndNonFiniteValuesAreRejected()
        {
            var c = new RuleContext();
            var cycle = new BinaryNumber { operation = Arithmetic.Add, right = new ConstantNumber(1) };
            cycle.left = cycle;
            Assert.Throws<InvalidOperationException>(() => cycle.Evaluate(c));
            Assert.Throws<InvalidOperationException>(() => new ConstantNumber(float.NaN).Evaluate(c));
            Assert.That(new ConstantNumber(2).Evaluate(c), Is.EqualTo(2), "The recursion guard must recover after failure.");
        }

        [Test]
        public void RandomNumberIsSeededAndRequiresGenerator()
        {
            var number = new RandomNumber { minimum = 2, maximum = 8 };
            Assert.Throws<InvalidOperationException>(() => number.Evaluate(new RuleContext()));
            var a = new RuleContext { Random = new System.Random(42) };
            var b = new RuleContext { Random = new System.Random(42) };
            Assert.That(Enumerable.Range(0, 12).Select(_ => number.Evaluate(a)).ToArray(),
                Is.EqualTo(Enumerable.Range(0, 12).Select(_ => number.Evaluate(b)).ToArray()));
        }

        [Test]
        public void ContentCodecPreservesPolymorphicRulesAndRejectsUnknownFields()
        {
            var source = new Requirement { message = "Enough jobs", condition = new CompareCondition
            {
                comparison = Comparison.GreaterOrEqual,
                left = new InputNumber { input = NumberInput.JobsCompleted }, right = new ConstantNumber(5)
            } };
            var json = ContentJson.Encode(source);
            var copy = ContentJson.Decode<Requirement>(json);
            Assert.That(copy.condition.Test(new RuleContext { Career = new Career { jobs = 5 } }), Is.True);
            Assert.That(copy.condition.Test(new RuleContext { Career = new Career { jobs = 4 } }), Is.False);
            Assert.Throws<InvalidDataException>(() => ContentJson.Decode<Requirement>("{\"condition\":{\"type\":\"AlwaysCondition\"},\"message\":\"ok\",\"surprise\":2}"));
        }

        [Test]
        public void SafePathKeepsContentInsideItsDirectory()
        {
            var root = Path.Combine(Path.GetTempPath(), "htp-test-root");
            Assert.That(ContentJson.SafePath(root, "actions/hack.json"),
                Is.EqualTo(Path.GetFullPath(Path.Combine(root, "actions", "hack.json"))));
            Assert.Throws<InvalidDataException>(() => ContentJson.SafePath(root, "../outside.json"));
            Assert.Throws<InvalidDataException>(() => ContentJson.SafePath(root, "actions\\hack.json"));
            Assert.Throws<InvalidDataException>(() => ContentJson.SafePath(root, ""));
        }

        [Test]
        public void DefinitionValidatorReportsMissingRootAndDuplicateIds()
        {
            Assert.That(GameDefinitionValidator.Validate(null), Is.Not.Empty);
            using (var fixture = new GameFixture())
            {
                var root = Object.Instantiate(fixture.Catalog.Data);
                try
                {
                    root.actions = root.actions.Concat(new[] { root.actions[0] }).ToArray();
                    Assert.That(GameDefinitionValidator.Validate(root), Has.Some.Contains("unique ID"));
                    Assert.Throws<InvalidOperationException>(() => GameDefinitionValidator.ThrowIfInvalid(root));
                }
                finally { Object.DestroyImmediate(root); }
            }
        }

        [Test]
        public void CatalogResolvesKnownIdsAndAvailableContracts()
        {
            using (var fixture = new GameFixture())
            {
                var action = fixture.Catalog.Data.actions.First();
                Assert.That(fixture.Catalog.FindAction(action.id).title, Is.EqualTo(action.title));
                Assert.That(fixture.Catalog.FindAction("missing"), Is.Null);
                Assert.That(fixture.Catalog.Action(action.id), Is.SameAs(action));
                Assert.That(fixture.Catalog.Contracts(fixture.Catalog.CreateCareer()), Is.Not.Empty);
            }
        }

        [Test]
        public void ContentReferencesResolveToLoadedDefinitions()
        {
            using (var fixture = new GameFixture())
            {
                var objective = fixture.Content.Game.contracts[0].objectives[0];
                var copy = new ObjectiveDefinition();
                ContentJson.Populate(ContentJson.Encode(objective), copy, fixture.Content);
                Assert.That(copy.action, Is.SameAs(objective.action));
                Assert.That(fixture.Content.Resolve("missing", typeof(ActionDefinition)), Is.Null);
            }
        }
    }
}
