using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cashg.GraphExplorer
{
	/// <summary>Host-independent step appearance shared by inspectors, labels and hover cards.</summary>
	public static class GraphWorkflowView
	{
		/// <summary>Applies workflow state classes and an optional glyph image.</summary>
		/// <param name="element">Element whose classes are updated.</param>
		/// <param name="glyph">Optional glyph element to decorate.</param>
		/// <param name="icon">Optional icon texture.</param>
		/// <param name="complete">Whether the step is complete.</param>
		/// <param name="confirm">Whether the step needs confirmation.</param>
		/// <param name="interactive">Whether the step can be queued.</param>
		public static void Paint(VisualElement element, VisualElement glyph, Texture2D icon, bool complete,
								 bool confirm, bool interactive)
		{
			element.EnableInClassList("done", complete);
			element.EnableInClassList("confirm", confirm);
			element.EnableInClassList("pending", !complete);
			element.EnableInClassList("queueable", interactive);
			if (glyph != null && icon != null)
			{
				glyph.style.backgroundImage = new StyleBackground(icon);
				glyph.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
			}
		}

		/// <summary>Gets the presentation color for a workflow state.</summary>
		/// <param name="state">Workflow state.</param>
		/// <returns>The state color.</returns>
		public static Color Color(GraphWorkflowState state)
		{
			return state == GraphWorkflowState.Complete ? new Color(.35f, .92f, .65f) :
				state == GraphWorkflowState.Failed ? new Color(1, .35f, .35f) :
				state == GraphWorkflowState.NotApplicable ? new Color(.45f, .48f, .52f) :
				state == GraphWorkflowState.NeedsReview ? new Color(1, .65f, .25f) : new Color(.65f, .78f, .9f);
		}

		/// <summary>Creates a button for a workflow step.</summary>
		/// <param name="step">Step definition.</param>
		/// <param name="state">Current state of the step.</param>
		/// <param name="clicked">Optional click action.</param>
		/// <param name="compact">Whether to use the compact appearance.</param>
		/// <param name="confirm">Whether the action requires confirmation.</param>
		/// <returns>The configured step button.</returns>
		public static Button Step(GraphWorkflowStep step, GraphWorkflowState state, Action clicked = null,
								  bool compact = false, bool confirm = false)
		{
			// Explain the available queue or rerun action while adapting the same state to full and compact buttons.
			var complete = state == GraphWorkflowState.Complete;
			var action = clicked == null ? "" :
				confirm ? "\nClick the checkmark to queue a rerun." :
				complete ? "\nClick to prepare a rerun." : "\nAdd this step to the queue.";
			var button = new Button(clicked)
			{
				name = "workflow-" + step.Id,
				tooltip = Title(step.Id) + " · " + GraphWorkflow.Label(state) + "\n" + step.Description + action
			};
			button.AddToClassList(compact ? "workflow-mini" : "workflow-step");
			button.EnableInClassList("confirm", !compact && confirm);
			button.Add(GraphUi.Glyph(Icon(step.Id)));
			if (!compact)
			{
				button.Add(GraphUi.Label(Title(step.Id), "workflow-step-title"));
				var status = GraphUi.Label(GraphWorkflow.Label(state), "workflow-step-state");
				status.style.color = Color(state);
				button.Add(status);
				if (clicked != null) button.Add(GraphUi.Glyph(confirm ? "check" : "plus"));
			}
			else
			{
				button.style.borderBottomColor = Color(state);
			}

			button.SetEnabled(state != GraphWorkflowState.NotApplicable);
			return button;
		}

		/// <summary>Gets the display title for a workflow step identifier.</summary>
		/// <param name="id">Step identifier.</param>
		/// <returns>The display title.</returns>
		public static string Title(string id)
		{
			return id == "format" ? "Format" :
				id == "xml_comments" ? "XML docs" :
				id == "logical_blocks" ? "Sections" :
				id == "block_comments" ? "Comments" : "Markdown";
		}

		/// <summary>Gets the icon identifier for a workflow step.</summary>
		/// <param name="id">Step identifier.</param>
		/// <returns>The icon identifier.</returns>
		public static string Icon(string id)
		{
			return id == "format" ? "align" :
				id == "xml_comments" ? "page" :
				id == "logical_blocks" ? "list" :
				id == "block_comments" ? "pen" : "page";
		}
	}
}
