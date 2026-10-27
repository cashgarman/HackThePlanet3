using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;

namespace Cashg.GraphExplorer
{
	/// <summary>Represents graph provider progress.</summary>
	public sealed class GraphProviderProgress
	{
		/// <summary>Creates a provider progress update.</summary>
		/// <param name="message">Status text to display with the update.</param>
		/// <param name="fraction">Optional completion fraction from zero to one.</param>
		public GraphProviderProgress(string message, double? fraction = null)
		{
			if (fraction.HasValue && (double.IsNaN(fraction.Value) || fraction < 0 || fraction > 1))
				throw new ArgumentOutOfRangeException(nameof(fraction));
			Message = message ?? "";
			Fraction = fraction;
		}

		/// <summary>Gets the status text for this update.</summary>
		public string Message { get; }

		/// <summary>Gets the optional completion fraction.</summary>
		public double? Fraction { get; }
	}

	// Coroutines keep Unity object/document mutation on the owning thread. Providers
	// may perform background work, but invoke completion on that thread exactly once.
	/// <summary>Defines graph data provider.</summary>
	public interface IGraphDataProvider
	{
		/// <summary>Gets the provider's stable identity.</summary>
		string Identity { get; }

		/// <summary>Loads a dataset snapshot and reports completion or failure through callbacks.</summary>
		/// <param name="datasetId">Dataset to load.</param>
		/// <param name="progress">Receives progress updates, when supplied.</param>
		/// <param name="cancellation">Token that requests cancellation.</param>
		/// <param name="completed">Receives the loaded snapshot.</param>
		/// <param name="failed">Receives a load failure.</param>
		/// <returns>The coroutine that performs the load.</returns>
		IEnumerator Load(string datasetId, IProgress<GraphProviderProgress> progress, CancellationToken cancellation,
						 Action<GraphSnapshot> completed, Action<Exception> failed);

		/// <summary>Loads details for one node and reports completion or failure through callbacks.</summary>
		/// <param name="datasetId">Dataset containing the node.</param>
		/// <param name="nodeId">Node whose details are requested.</param>
		/// <param name="progress">Receives progress updates, when supplied.</param>
		/// <param name="cancellation">Token that requests cancellation.</param>
		/// <param name="completed">Receives the resulting batch.</param>
		/// <param name="failed">Receives a load failure.</param>
		/// <returns>The coroutine that performs the load.</returns>
		IEnumerator LoadDetails(string datasetId, string nodeId, IProgress<GraphProviderProgress> progress,
								CancellationToken cancellation, Action<GraphBatch> completed, Action<Exception> failed);
	}

	/// <summary>Represents graph action parameter.</summary>
	public sealed class GraphActionParameter
	{
		/// <summary>Gets or sets the parameter identifier.</summary>
		public string Id { get; set; }

		/// <summary>Gets or sets the display label.</summary>
		public string Label { get; set; }

		/// <summary>Gets or sets explanatory text for the parameter.</summary>
		public string Description { get; set; }

		/// <summary>Gets or sets the expected value type.</summary>
		public GraphValueType Type { get; set; }

		/// <summary>Gets or sets whether callers must provide a value.</summary>
		public bool Required { get; set; }
	}

	/// <summary>Represents graph action definition.</summary>
	public sealed class GraphActionDefinition
	{
		/// <summary>Gets or sets the action identifier.</summary>
		public string Id { get; set; }

		/// <summary>Gets or sets the display label.</summary>
		public string Label { get; set; }

		/// <summary>Gets or sets explanatory text for the action.</summary>
		public string Description { get; set; }

		/// <summary>Gets or sets the node type identifiers accepted as targets.</summary>
		public List<string> AcceptedTypeIds { get; set; } = new();

		/// <summary>Gets or sets the action's input parameters.</summary>
		public List<GraphActionParameter> Parameters { get; set; } = new();

		/// <summary>Gets or sets the provider-owned configuration schema.</summary>
		public JsonElement ConfigurationSchema { get; set; }
	}

	/// <summary>Represents graph action request.</summary>
	public sealed class GraphActionRequest
	{
		/// <summary>Gets or sets the request identifier used for idempotent execution.</summary>
		public string Id { get; set; } = Guid.NewGuid().ToString("N");

		/// <summary>Gets or sets the identifier of the action to invoke.</summary>
		public string ActionId { get; set; }

		/// <summary>Gets or sets the source node identifier.</summary>
		public string SourceNodeId { get; set; }

		/// <summary>Gets or sets the selected target identifier.</summary>
		public string TargetId { get; set; }

		// Opaque provider binding: backend ownership/job identities never enter library policy.
		/// <summary>Gets or sets opaque provider-specific bindings for this request.</summary>
		public Dictionary<string, GraphValue> Binding { get; set; } = new();

		/// <summary>Gets or sets the values supplied for declared action parameters.</summary>
		public Dictionary<string, GraphValue> Parameters { get; set; } = new();

		// Optional provider-owned structured configuration. Clone borrowed JSON before assigning.
		/// <summary>Gets or sets optional provider-owned structured configuration.</summary>
		public JsonElement Payload { get; set; }
	}

	/// <summary>Represents graph action entry progress.</summary>
	public sealed class GraphActionEntryProgress
	{
		/// <summary>Gets or sets the request identifier for this entry.</summary>
		public string RequestId { get; set; }

		/// <summary>Gets or sets the current entry status.</summary>
		public string Status { get; set; }

		/// <summary>Gets or sets status text for this entry.</summary>
		public string Message { get; set; }

		/// <summary>Gets or sets the optional completion fraction.</summary>
		public double? Fraction { get; set; }

		/// <summary>Gets or sets error messages reported for this entry.</summary>
		public List<string> Errors { get; set; } = new();

		/// <summary>Gets or sets warning messages reported for this entry.</summary>
		public List<string> Warnings { get; set; } = new();
	}

	/// <summary>Represents graph action run.</summary>
	public sealed class GraphActionRun
	{
		/// <summary>Gets or sets the run identifier.</summary>
		public string Id { get; set; }

		/// <summary>Gets or sets the token that identifies the reviewed request state.</summary>
		public string ReviewToken { get; set; }

		/// <summary>Gets or sets the current run status.</summary>
		public string Status { get; set; }

		/// <summary>Gets or sets the revision associated with this run.</summary>
		public long Revision { get; set; }

		/// <summary>Gets or sets progress entries for the run.</summary>
		public List<GraphActionEntryProgress> Entries { get; set; } = new();

		/// <summary>Gets or sets provider-owned structured run data.</summary>
		public JsonElement Payload { get; set; }
	}

	/// <summary>Represents graph provider exception.</summary>
	public sealed class GraphProviderException : Exception
	{
		/// <summary>Creates an exception for a provider response.</summary>
		/// <param name="message">Explanation of the provider failure.</param>
		/// <param name="responseCode">Provider response code associated with the failure.</param>
		public GraphProviderException(string message, long responseCode) : base(message)
		{
			ResponseCode = responseCode;
		}

		/// <summary>Gets the provider response code.</summary>
		public long ResponseCode { get; }

		/// <summary>Gets whether the response code indicates a rejected request.</summary>
		public bool Rejected => ResponseCode >= 400 && ResponseCode < 500;
	}

	/// <summary>Defines graph action provider.</summary>
	public interface IGraphActionProvider
	{
		/// <summary>Gets the provider's stable identity.</summary>
		string Identity { get; }

		/// <summary>Gets the actions currently supported by the provider.</summary>
		IReadOnlyList<GraphActionDefinition> Actions { get; }

		/// <summary>Resolves eligible targets for an action and source node.</summary>
		/// <param name="actionId">Action to resolve.</param>
		/// <param name="scene">Scene containing candidate nodes.</param>
		/// <param name="sourceNodeId">Node from which the action is initiated.</param>
		/// <returns>Requests representing the eligible targets.</returns>
		IReadOnlyList<GraphActionRequest> ResolveTargets(string actionId, IGraphScene scene, string sourceNodeId);

		/// <summary>Reviews an ordered draft before execution.</summary>
		/// <param name="orderedDraft">Requests in the order proposed for execution.</param>
		/// <param name="cancellation">Token that requests cancellation.</param>
		/// <param name="completed">Receives the review result.</param>
		/// <param name="failed">Receives a review failure.</param>
		/// <returns>The coroutine that performs the review.</returns>
		IEnumerator Review(IReadOnlyList<GraphActionRequest> orderedDraft, CancellationToken cancellation,
						   Action<GraphActionRun> completed, Action<Exception> failed);

		// Providers must reject changed/stale reviews and make requestId idempotent.
		// Recovery uses that same identity after uncertain completion; the UI never invents authoritative state.
		/// <summary>Executes a request accepted by the supplied review.</summary>
		/// <param name="requestId">Stable identifier used to make execution idempotent.</param>
		/// <param name="review">Provider review authorizing this execution.</param>
		/// <param name="cancellation">Token that requests cancellation.</param>
		/// <param name="completed">Receives the run state after execution.</param>
		/// <param name="failed">Receives an execution failure.</param>
		/// <returns>The coroutine that performs the execution.</returns>
		IEnumerator Execute(string requestId, GraphActionRun review, CancellationToken cancellation,
							Action<GraphActionRun> completed, Action<Exception> failed);

		/// <summary>Recovers the state of a request whose execution result is uncertain.</summary>
		/// <param name="requestId">Stable identifier of the request to recover.</param>
		/// <param name="cancellation">Token that requests cancellation.</param>
		/// <param name="completed">Receives the recovered run state.</param>
		/// <param name="failed">Receives a recovery failure.</param>
		/// <returns>The coroutine that performs recovery.</returns>
		IEnumerator Recover(string requestId, CancellationToken cancellation, Action<GraphActionRun> completed,
							Action<Exception> failed);

		/// <summary>Refreshes the latest state of a run.</summary>
		/// <param name="runId">Run to refresh.</param>
		/// <param name="cancellation">Token that requests cancellation.</param>
		/// <param name="completed">Receives the refreshed run state.</param>
		/// <param name="failed">Receives a refresh failure.</param>
		/// <returns>The coroutine that performs the refresh.</returns>
		IEnumerator Refresh(string runId, CancellationToken cancellation, Action<GraphActionRun> completed,
							Action<Exception> failed);

		/// <summary>Applies a control operation to a run entry.</summary>
		/// <param name="runId">Run to control.</param>
		/// <param name="operation">Provider-defined control operation.</param>
		/// <param name="entryId">Entry affected by the operation, when applicable.</param>
		/// <param name="cancellation">Token that requests cancellation.</param>
		/// <param name="completed">Receives the updated run state.</param>
		/// <param name="failed">Receives a control failure.</param>
		/// <returns>The coroutine that performs the operation.</returns>
		IEnumerator Control(string runId, string operation, string entryId, CancellationToken cancellation,
							Action<GraphActionRun> completed, Action<Exception> failed);
	}

	// Optional history/catalog support; simple providers need only the action interface.
	/// <summary>Defines graph action history provider.</summary>
	public interface IGraphActionHistoryProvider
	{
		/// <summary>Loads the provider's available action catalog.</summary>
		/// <param name="cancellation">Token that requests cancellation.</param>
		/// <param name="completed">Receives the available action definitions.</param>
		/// <param name="failed">Receives a catalog load failure.</param>
		/// <returns>The coroutine that loads the catalog.</returns>
		IEnumerator LoadActions(CancellationToken cancellation, Action<IReadOnlyList<GraphActionDefinition>> completed,
								Action<Exception> failed);

		/// <summary>Lists runs known to the provider.</summary>
		/// <param name="cancellation">Token that requests cancellation.</param>
		/// <param name="completed">Receives the available runs.</param>
		/// <param name="failed">Receives a listing failure.</param>
		/// <returns>The coroutine that lists runs.</returns>
		IEnumerator ListRuns(CancellationToken cancellation, Action<IReadOnlyList<GraphActionRun>> completed,
							 Action<Exception> failed);
	}
}