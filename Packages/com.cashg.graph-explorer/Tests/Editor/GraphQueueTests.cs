using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace Cashg.GraphExplorer.Tests
{
	/// <summary>Represents graph queue tests.</summary>
	public sealed class GraphQueueTests
	{
		private static GraphActionRun Review => new() { Id = "review", ReviewToken = "token" };

		private static void Drain(IEnumerator routine)
		{
			// Execute nested provider iterators and dispose each scope so coordinator finally blocks run.
			using (routine as IDisposable)
			{
				while (routine.MoveNext())
					if (routine.Current is IEnumerator nested)
						Drain(nested);
			}
		}

		private static void Fail(Exception error)
		{
			Assert.Fail(error.ToString());
		}

		/// <summary>Checks that review Snapshots Exact Order Before Yield.</summary>
		[Test]
		public void ReviewSnapshotsExactOrderBeforeYield()
		{
			var provider = new Provider();
			var queue = new GraphQueueCoordinator(provider);
			var draft = new List<GraphActionRequest> { new() { Id = "second" }, new() { Id = "first" } };
			var operation = queue.Review(draft, CancellationToken.None, _ => { }, Fail);

			// Edit the draft before executing the coroutine to verify Review snapshots order at call time.
			draft.Reverse();
			Drain(operation);
			Assert.That(provider.Calls, Is.EqualTo(new[] { "second,first" }));
			Assert.That(queue.Busy, Is.False);
		}

		/// <summary>Checks that submission Persists Before Sending And Blocks On Storage Failure.</summary>
		[Test]
		public void SubmissionPersistsBeforeSendingAndBlocksOnStorageFailure()
		{
			// Assert the pending request is durable before the provider receives it.
			var provider = new Provider();
			var queue = new GraphQueueCoordinator(provider);
			Exception error = null;
			Drain(queue.Submit(Review, () => false, CancellationToken.None, _ => Assert.Fail("sent"),
							   failure => error = failure));
			Assert.That(error, Is.Not.Null);
			Assert.That(provider.Calls, Is.Empty);
			Drain(queue.Submit(Review, () =>
			{
				provider.Calls.Add("save");
				return true;
			}, CancellationToken.None, _ => { }, Fail));
			Assert.That(provider.Calls, Is.EqualTo(new[] { "save", "send:review" }));
		}

		/// <summary>Checks that uncertain Submission Recovers Same Identity And Releases Busy.</summary>
		[Test]
		public void UncertainSubmissionRecoversSameIdentityAndReleasesBusy()
		{
			var provider = new Provider { FailExecute = true };
			var queue = new GraphQueueCoordinator(provider);
			Exception error = null;
			Drain(queue.Submit(Review, () => true, CancellationToken.None, _ => { }, failure => error = failure));
			Assert.That(((GraphProviderException)error).Rejected, Is.False);
			Assert.That(queue.Busy, Is.False);
			Drain(queue.Recover("review", CancellationToken.None, run => Assert.That(run.Status, Is.EqualTo("running")),
								Fail));
			Assert.That(provider.Calls, Is.EqualTo(new[] { "send:review", "recover:review" }));
		}

		/// <summary>Checks that cancelled Or Invalid Review Never Sends.</summary>
		[Test]
		public void CancelledOrInvalidReviewNeverSends()
		{
			var provider = new Provider();
			var queue = new GraphQueueCoordinator(provider);
			var failures = 0;
			Drain(queue.Submit(Review, () => true, new CancellationToken(true), _ => Assert.Fail("sent"),
							   _ => failures++));
			var invalid = Review;

			// An entry-level review error blocks submission just as an already-cancelled token does.
			invalid.Entries.Add(new GraphActionEntryProgress { Errors = new List<string> { "blocked" } });
			Drain(queue.Submit(invalid, () => true, CancellationToken.None, _ => Assert.Fail("sent"), _ => failures++));
			Assert.That(failures, Is.EqualTo(2));
			Assert.That(provider.Calls, Is.Empty);
		}

		/// <summary>Checks that entry Controls And Rejection Classification Remain Explicit.</summary>
		[Test]
		public void EntryControlsAndRejectionClassificationRemainExplicit()
		{
			var provider = new Provider();
			var queue = new GraphQueueCoordinator(provider);
			Drain(queue.Control("review", "retry", "entry", CancellationToken.None, _ => { }, Fail));
			Assert.That(provider.Calls, Is.EqualTo(new[] { "retry:entry" }));
			Assert.That(new GraphProviderException("stale", 409).Rejected, Is.True);
			Assert.That(new GraphProviderException("offline", 503).Rejected, Is.False);
		}

		private sealed class Provider : IGraphActionProvider
		{
			public readonly List<string> Calls = new();
			public bool FailExecute;
			public string Identity => "fixture";
			public IReadOnlyList<GraphActionDefinition> Actions => Array.Empty<GraphActionDefinition>();

			public IReadOnlyList<GraphActionRequest> ResolveTargets(string action, IGraphScene scene, string source)
			{
				return Array.Empty<GraphActionRequest>();
			}

			public IEnumerator Review(IReadOnlyList<GraphActionRequest> draft, CancellationToken cancellation,
									  Action<GraphActionRun> completed, Action<Exception> failed)
			{
				Calls.Add(string.Join(",", draft.Select(entry => entry.Id)));
				completed(new GraphActionRun { Id = "review", ReviewToken = "token" });
				yield break;
			}

			public IEnumerator Execute(string id, GraphActionRun review, CancellationToken cancellation,
									   Action<GraphActionRun> completed, Action<Exception> failed)
			{
				// Simulate losing the response after sending so recovery must reuse the original request identity.
				Calls.Add("send:" + id);
				if (FailExecute) failed(new GraphProviderException("Connection lost", 0));
				else completed(review);
				yield break;
			}

			public IEnumerator Recover(string id, CancellationToken cancellation, Action<GraphActionRun> completed,
									   Action<Exception> failed)
			{
				Calls.Add("recover:" + id);
				completed(new GraphActionRun { Id = id, Status = "running" });
				yield break;
			}

			public IEnumerator Refresh(string id, CancellationToken cancellation, Action<GraphActionRun> completed,
									   Action<Exception> failed)
			{
				return Recover(id, cancellation, completed, failed);
			}

			public IEnumerator Control(string id, string operation, string entry, CancellationToken cancellation,
									   Action<GraphActionRun> completed, Action<Exception> failed)
			{
				Calls.Add(operation + ":" + entry);
				completed(new GraphActionRun { Id = id });
				yield break;
			}
		}
	}
}
