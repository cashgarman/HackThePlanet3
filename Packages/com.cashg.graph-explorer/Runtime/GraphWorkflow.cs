using System;
using System.Collections.Generic;
using System.Linq;

namespace Cashg.GraphExplorer
{
	/// <summary>Lists graph workflow state.</summary>
	public enum GraphWorkflowState
	{
		/// <summary>The task has not started.</summary>
		Pending,
		/// <summary>The task is waiting to be dispatched.</summary>
		Queued,
		/// <summary>The task has been handed to an external provider.</summary>
		HandedOff,
		/// <summary>The task is running.</summary>
		Running,
		/// <summary>The task is waiting for an external result to be synchronized.</summary>
		AwaitingReview,
		/// <summary>The task completed successfully.</summary>
		Complete,
		/// <summary>The task result changed and needs review.</summary>
		NeedsReview,
		/// <summary>The task failed.</summary>
		Failed,
		/// <summary>The task was cancelled.</summary>
		Cancelled,
		/// <summary>The task does not apply to this workflow step.</summary>
		NotApplicable
	}

	/// <summary>Lists graph workflow grouping.</summary>
	public enum GraphWorkflowGrouping
	{
		/// <summary>Groups tasks separately for each workflow step.</summary>
		PerStep,
		/// <summary>Groups all requested steps into one workflow task.</summary>
		WholeWorkflow
	}

	/// <summary>Represents graph workflow step.</summary>
	public sealed class GraphWorkflowStep
	{
		/// <summary>Creates a workflow step definition.</summary>
		/// <param name="id">Stable step identifier.</param>
		/// <param name="label">Display label.</param>
		/// <param name="description">Explanation of the step.</param>
		public GraphWorkflowStep(string id, string label, string description)
		{
			Id = id;
			Label = label;
			Description = description;
		}

		/// <summary>Gets the stable step identifier.</summary>
		public string Id { get; }
		/// <summary>Gets the display label.</summary>
		public string Label { get; }
		/// <summary>Gets the step description.</summary>
		public string Description { get; }
	}

	/// <summary>Represents graph workflow validation.</summary>
	public sealed class GraphWorkflowValidation
	{
		/// <summary>Gets or sets the validated node identifier.</summary>
		public string NodeId { get; set; }
		/// <summary>Gets or sets the validated step identifier.</summary>
		public string StepId { get; set; }
		/// <summary>Gets or sets the fingerprint of the validated node state.</summary>
		public string Fingerprint { get; set; }
		/// <summary>Gets or sets the workflow task identifier.</summary>
		public string TaskId { get; set; }
		/// <summary>Gets or sets when the step was accepted.</summary>
		public DateTimeOffset AcceptedUtc { get; set; }
		/// <summary>Gets or sets artifact identifiers associated with the validation.</summary>
		public Dictionary<string, string> Artifacts { get; set; } = new();
	}

	/// <summary>Represents graph workflow target.</summary>
	public sealed class GraphWorkflowTarget
	{
		/// <summary>Gets or sets the target node identifier.</summary>
		public string NodeId { get; set; }
		/// <summary>Gets or sets the fingerprint used to detect target changes.</summary>
		public string Fingerprint { get; set; }
		/// <summary>Gets or sets the steps requested for this target.</summary>
		public List<string> StepIds { get; set; } = new();
		/// <summary>Gets or sets the steps accepted for this target.</summary>
		public List<string> AcceptedSteps { get; set; } = new();
	}

	/// <summary>Represents graph workflow task.</summary>
	public sealed class GraphWorkflowTask
	{
		/// <summary>Gets or sets the stable task identifier.</summary>
		public string Id { get; set; } = Guid.NewGuid().ToString("N");
		/// <summary>Gets or sets the node that initiated the task.</summary>
		public string SourceNodeId { get; set; }
		/// <summary>Gets or sets whether descendant nodes are included as targets.</summary>
		public bool IncludeDescendants { get; set; }
		/// <summary>Gets or sets the task's target nodes and requested steps.</summary>
		public List<GraphWorkflowTarget> Targets { get; set; } = new();
		/// <summary>Gets or sets the task's current state.</summary>
		public GraphWorkflowState State { get; set; } = GraphWorkflowState.Queued;
		/// <summary>Gets or sets the prompt sent to the external task provider.</summary>
		public string Prompt { get; set; }
		/// <summary>Gets or sets the external thread identifier, when handed off.</summary>
		public string ExternalThreadId { get; set; }
		/// <summary>Gets or sets the external turn identifier, when available.</summary>
		public string ExternalTurnId { get; set; }
		/// <summary>Gets or sets the latest status or error message.</summary>
		public string Message { get; set; }
		/// <summary>Gets or sets the output returned by the external task.</summary>
		public string Output { get; set; }
		/// <summary>Gets or sets whether the task was dispatched automatically.</summary>
		public bool Automatic { get; set; }
		/// <summary>Gets or sets the dispatch generation used to reject stale callbacks.</summary>
		public long DispatchGeneration { get; set; }
		/// <summary>Gets or sets when the task was dispatched.</summary>
		public DateTimeOffset? DispatchedUtc { get; set; }
		/// <summary>Gets or sets when the task finished.</summary>
		public DateTimeOffset? FinishedUtc { get; set; }
		/// <summary>Gets whether every requested target step has been accepted.</summary>
		public bool Accepted => Targets.Count > 0 && Targets.All(t => t.StepIds.All(t.AcceptedSteps.Contains));
		/// <summary>Gets whether the task remains open for resolution.</summary>
		public bool Unresolved => State != GraphWorkflowState.Complete && State != GraphWorkflowState.Cancelled;

		/// <summary>Gets a stable key derived from the source and ordered targets.</summary>
		public string Key => SourceNodeId + "|" + IncludeDescendants + "|" + string.Join(
			";", Targets.OrderBy(t => t.NodeId, StringComparer.Ordinal)
						.Select(t => t.NodeId + ":" + string.Join(",", t.StepIds)));
	}

	/// <summary>Optional host capability; acceptance is separate from external execution success.</summary>
	public interface IGraphWorkflowAcceptance
	{
		/// <summary>Determines whether a task step can be accepted.</summary>
		/// <param name="taskId">Task identifier.</param>
		/// <param name="nodeId">Target node identifier.</param>
		/// <param name="stepId">Step identifier.</param>
		/// <param name="reason">Receives the reason acceptance is unavailable.</param>
		/// <returns><see langword="true"/> when the step can be accepted.</returns>
		bool CanAccept(string taskId, string nodeId, string stepId, out string reason);
		/// <summary>Accepts a task step previously approved by <see cref="CanAccept(string, string, string, out string)"/>.</summary>
		/// <param name="taskId">Task identifier.</param>
		/// <param name="nodeId">Target node identifier.</param>
		/// <param name="stepId">Step identifier.</param>
		void Accept(string taskId, string nodeId, string stepId);
	}

	/// <summary>Optional handoff capability for providers whose work continues outside the graph UI.</summary>
	public interface IGraphExternalTaskProvider
	{
		/// <summary>Opens a task in the provider's external workflow.</summary>
		/// <param name="task">Task to hand off.</param>
		void OpenExternalTask(GraphWorkflowTask task);
	}

	/// <summary>Represents graph workflow.</summary>
	public static class GraphWorkflow
	{
		/// <summary>Summarizes workflow step completion as node progress.</summary>
		/// <param name="steps">States of the applicable workflow steps.</param>
		/// <returns>Progress data, or null when no step applies.</returns>
		public static GraphNodeProgressVisual Progress(IEnumerable<GraphWorkflowState> steps)
		{
			// Exclude steps that do not apply before calculating completion.
			var applicable = steps.Where(s => s != GraphWorkflowState.NotApplicable).ToArray();
			if (applicable.Length == 0) return null;
			var done = applicable.Count(s => s == GraphWorkflowState.Complete);
			return new GraphNodeProgressVisual
				{ Fraction = (float)done / applicable.Length, Complete = done == applicable.Length };
		}

		/// <summary>Gets the display label for a workflow state.</summary>
		/// <param name="state">State to label.</param>
		/// <returns>A concise status label.</returns>
		public static string Label(GraphWorkflowState state)
		{
			return state == GraphWorkflowState.NotApplicable ? "N/A" :
				state == GraphWorkflowState.NeedsReview ? "Changed" :
				state == GraphWorkflowState.AwaitingReview ? "Syncing" :
				state == GraphWorkflowState.HandedOff ? "In Codex" :
				state == GraphWorkflowState.Complete ? "Done" : state.ToString();
		}
	}
}
