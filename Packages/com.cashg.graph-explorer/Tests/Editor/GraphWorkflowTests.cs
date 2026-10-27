using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Cashg.GraphExplorer.Codex;
using NUnit.Framework;
using UnityEngine;

namespace Cashg.GraphExplorer.Tests
{
	/// <summary>Represents graph workflow tests.</summary>
	public sealed class GraphWorkflowTests
	{
		/// <summary>Checks that progress Material Preserves Player Instancing Variants.</summary>
		[Test]
		public void ProgressMaterialPreservesPlayerInstancingVariants()
		{
			var material = GraphResources.Load<Material>("GraphNodeProgressMaterial");
			Assert.That(material, Is.Not.Null);
			Assert.That(material.enableInstancing, Is.True);
			Assert.That(material.shader.name, Is.EqualTo("Cashg/GraphExplorer/GraphNodeProgress"));
		}

		/// <summary>Checks that fractions Are Clamped Without Inferring Completion.</summary>
		[TestCase(-1, 0)]
		[TestCase(.4f, .4f)]
		[TestCase(2, 1)]
		public void FractionsAreClampedWithoutInferringCompletion(float input, float expected)
		{
			var progress = new GraphNodeProgressVisual { Fraction = input };
			Assert.That(progress.TryFraction(out var value), Is.True);
			Assert.That(value, Is.EqualTo(expected));
			Assert.That(progress.Complete, Is.False);
		}

		/// <summary>Checks that nonfinite Values Are Ignored Unless Host Explicitly Completes.</summary>
		[Test]
		public void NonfiniteValuesAreIgnoredUnlessHostExplicitlyCompletes()
		{
			foreach (var input in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
			{
				var progress = new GraphNodeProgressVisual { Fraction = input };
				Assert.That(progress.TryFraction(out _), Is.False);
				progress.Complete = true;
				Assert.That(progress.TryFraction(out var value), Is.True);
				Assert.That(value, Is.EqualTo(1));
			}
		}

		/// <summary>Checks that progress Counts Only Validated Applicable Steps.</summary>
		[Test]
		public void ProgressCountsOnlyValidatedApplicableSteps()
		{
			// Exclude nonapplicable steps from the denominator while queued or changed work remains incomplete.
			Assert.That(GraphWorkflow.Progress(new[] { GraphWorkflowState.NotApplicable }), Is.Null);
			var progress = GraphWorkflow.Progress(new[]
			{
				GraphWorkflowState.Complete, GraphWorkflowState.NeedsReview, GraphWorkflowState.Queued,
				GraphWorkflowState.NotApplicable
			});
			Assert.That(progress.Fraction, Is.EqualTo(1f / 3));
			Assert.That(progress.Complete, Is.False);
			Assert.That(
				GraphWorkflow.Progress(new[] { GraphWorkflowState.Complete, GraphWorkflowState.NotApplicable })
							 .Complete, Is.True);
		}

		/// <summary>Checks that desktop Links Encode Workspace And Prompt.</summary>
		[Test]
		public void DesktopLinksEncodeWorkspaceAndPrompt()
		{
			var root = Path.GetTempPath();
			var prompt = "Unicode ✓ & ? #\nsecond line";
			var link = CodexLinks.NewChat(root, prompt);
			Assert.That(link, Does.Contain("&prompt=" + Uri.EscapeDataString(prompt)));
			Assert.That(link, Does.Contain("path=" + Uri.EscapeDataString(Path.GetFullPath(root))));
			Assert.Throws<ArgumentException>(() => CodexLinks.NewChat("relative", prompt));
			Assert.That(CodexLinks.Thread("id/other"), Does.EndWith("id%2Fother"));
		}

		/// <summary>Checks that protocol Handshake Requests Approvals Questions And Disconnect Are Pumped.</summary>
		[Test]
		public void ProtocolHandshakeRequestsApprovalsQuestionsAndDisconnectArePumped()
		{
			var wire = new FakeTransport();
			using var client = new CodexAppServerClient(wire);
			var ready = false;
			string error = null;
			client.Start(() => ready = true, e => error = e);
			wire.Receive(new { id = 1, result = new { } });

			// Transport callbacks queue messages; readiness must change only when the host pumps them.
			Assert.That(ready, Is.False);
			client.Pump();
			Assert.That(ready, Is.True);
			Assert.That(wire.Sent[1], Does.Contain("initialized"));
			var result = false;
			client.Request("thread/start", new { cwd = "fixture" }, r => result = true, e => error = e);
			wire.Receive(new { id = 2, result = new { thread = new { id = "thread" } } });
			client.Pump();
			Assert.That(result, Is.True);
			var methods = new List<string>();

			// Exercise server requests and notifications, then fail outstanding work when the connection closes.
			client.Message += (method, p, id) =>
			{
				methods.Add(method);
				if (id.HasValue) client.Respond(id.Value, new { decision = "decline" });
			};
			foreach (var method in new[]
						 { "item/commandExecution/requestApproval", "item/tool/requestUserInput", "turn/completed" })
				wire.Receive(new { method, id = 90, @params = new { threadId = "thread" } });
			client.Pump();
			Assert.That(methods.Count, Is.EqualTo(3));
			Assert.That(wire.Sent.Last(), Does.Contain("decline"));
			client.Request("thread/resume", new { threadId = "thread" }, _ => Assert.Fail(), e => error = e);
			wire.Close();
			client.Pump();
			Assert.That(error, Is.EqualTo("offline"));
			Assert.That(client.Ready, Is.False);
		}

		/// <summary>Checks that protocol Reports Errors And Does Not Retry Mutating Requests.</summary>
		[Test]
		public void ProtocolReportsErrorsAndDoesNotRetryMutatingRequests()
		{
			// Keep transport failures visible and require explicit reconciliation before retry.
			var wire = new FakeTransport();
			using var client = new CodexAppServerClient(wire);
			string error = null;
			client.Start(() => { }, e => error = e);
			wire.Receive(new { id = 1, result = new { } });
			client.Pump();
			client.Request("turn/start", new { threadId = "thread" }, _ => Assert.Fail(), e => error = e);
			wire.Receive(new { id = 2, error = new { message = "denied" } });
			client.Pump();
			Assert.That(error, Does.Contain("denied"));

			// Pumping again must not resend a mutating request after a protocol failure.
			client.Pump();
			Assert.That(wire.Sent.Count(s => s.Contains("turn/start")), Is.EqualTo(1));
		}

		private sealed class FakeTransport : ICodexTransport
		{
			public readonly List<string> Sent = new();
			public event Action<string> Line;
			public event Action<string> Closed;

			public void Start()
			{
			}

			public void Send(string line)
			{
				Sent.Add(line);
			}

			public void Dispose()
			{
			}

			public void Receive(object message)
			{
				Line?.Invoke(JsonSerializer.Serialize(message));
			}

			public void Close()
			{
				Closed?.Invoke("offline");
			}
		}
	}
}