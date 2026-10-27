using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Cashg.GraphExplorer
{
	// The host owns durable drafts and pending identities; the provider owns execution.
	// No submission may leave this boundary before the identity has been persisted.
	/// <summary>Represents graph queue coordinator.</summary>
	public sealed class GraphQueueCoordinator
	{
		private readonly IGraphActionProvider provider;

		/// <summary>Creates a coordinator for serialized action operations.</summary>
		/// <param name="provider">Provider that performs action requests.</param>
		public GraphQueueCoordinator(IGraphActionProvider provider)
		{
			this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
		}

		/// <summary>Gets whether an operation is currently running.</summary>
		public bool Busy { get; private set; }

		private IEnumerator Perform(Func<IEnumerator> operation, Action<Exception> failed)
		{
			// Serialize provider operations and release the busy flag even when a coroutine fails or is disposed.
			if (Busy)
			{
				failed(new InvalidOperationException("A queue operation is already in progress."));
				yield break;
			}

			Busy = true;
			try
			{
				yield return operation();
			}
			finally
			{
				Busy = false;
			}
		}

		/// <summary>Reviews a stable copy of a draft action queue.</summary>
		/// <param name="draft">Requests to review, in proposed execution order.</param>
		/// <param name="cancellation">Token that requests cancellation.</param>
		/// <param name="completed">Receives the review result.</param>
		/// <param name="failed">Receives an operation failure.</param>
		/// <returns>The coroutine that performs the review.</returns>
		public IEnumerator Review(IReadOnlyList<GraphActionRequest> draft, CancellationToken cancellation,
								  Action<GraphActionRun> completed, Action<Exception> failed)
		{
			// Snapshot ordering before yielding: edits cannot reorder an in-flight review.
			var ordered = draft.ToArray();
			return Perform(() => provider.Review(ordered, cancellation, completed, failed), failed);
		}

		/// <summary>Persists a pending identity before submitting the reviewed queue.</summary>
		/// <param name="review">Review authorizing execution.</param>
		/// <param name="persistPending">Persists the pending submission identity.</param>
		/// <param name="cancellation">Token that requests cancellation.</param>
		/// <param name="completed">Receives the updated run state.</param>
		/// <param name="failed">Receives a persistence or execution failure.</param>
		/// <returns>The coroutine that submits the queue.</returns>
		public IEnumerator Submit(GraphActionRun review, Func<bool> persistPending, CancellationToken cancellation,
								  Action<GraphActionRun> completed, Action<Exception> failed)
		{
			return Perform(() => SubmitCore(review, persistPending, cancellation, completed, failed), failed);
		}

		private IEnumerator SubmitCore(GraphActionRun review, Func<bool> persistPending, CancellationToken cancellation,
									   Action<GraphActionRun> completed, Action<Exception> failed)
		{
			if (cancellation.IsCancellationRequested)
			{
				failed(new OperationCanceledException());
				yield break;
			}

			if (review == null || string.IsNullOrEmpty(review.Id) || string.IsNullOrEmpty(review.ReviewToken) ||
				review.Entries.Any(entry => entry.Errors.Count > 0))
			{
				// Reject missing review authority or entry errors before saving or sending a request.
				failed(new InvalidOperationException("A valid reviewed queue is required."));
				yield break;
			}

			// Persist the exact request identity before the provider can begin external execution.
			if (!persistPending())
			{
				failed(new InvalidOperationException("Pending submission could not be saved."));
				yield break;
			}

			yield return provider.Execute(review.Id, review, cancellation, completed, failed);
		}

		/// <summary>Recovers a request whose execution result is uncertain.</summary>
		/// <param name="requestId">Stable request identifier.</param>
		/// <param name="cancellation">Token that requests cancellation.</param>
		/// <param name="completed">Receives the recovered run state.</param>
		/// <param name="failed">Receives a recovery failure.</param>
		/// <returns>The coroutine that recovers the request.</returns>
		public IEnumerator Recover(string requestId, CancellationToken cancellation, Action<GraphActionRun> completed,
								   Action<Exception> failed)
		{
			return Perform(() => provider.Recover(requestId, cancellation, completed, failed), failed);
		}

		/// <summary>Applies a provider control operation to a run entry.</summary>
		/// <param name="runId">Run to control.</param>
		/// <param name="operation">Provider-defined operation name.</param>
		/// <param name="entryId">Entry affected by the operation.</param>
		/// <param name="cancellation">Token that requests cancellation.</param>
		/// <param name="completed">Receives the updated run state.</param>
		/// <param name="failed">Receives a control failure.</param>
		/// <returns>The coroutine that applies the operation.</returns>
		public IEnumerator Control(string runId, string operation, string entryId, CancellationToken cancellation,
								   Action<GraphActionRun> completed, Action<Exception> failed)
		{
			return Perform(() => provider.Control(runId, operation, entryId, cancellation, completed, failed), failed);
		}
	}
}