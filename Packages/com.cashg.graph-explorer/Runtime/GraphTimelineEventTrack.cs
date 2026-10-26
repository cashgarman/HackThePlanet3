using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Cashg.GraphExplorer
{
	/// <summary>An externally loaded state, independent of the document's linear edit history.</summary>
	public sealed class GraphTimelineEvent
	{
		/// <summary>Creates a descriptor for an externally loaded timeline state.</summary>
		/// <param name="id">Stable event identifier.</param>
		/// <param name="timestampUtc">Event timestamp.</param>
		/// <param name="label">Display label.</param>
		/// <param name="details">Optional descriptive text.</param>
		/// <param name="tooltip">Optional hover text.</param>
		public GraphTimelineEvent(string id, DateTimeOffset timestampUtc, string label, string details = null,
								  string tooltip = null)
		{
			if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Event ID is required.", nameof(id));
			Id = id;
			TimestampUtc = timestampUtc.ToUniversalTime();
			Label = label ?? "";
			Details = details ?? Label;
			Tooltip = tooltip;
		}

		/// <summary>Gets the stable event identifier.</summary>
		public string Id { get; }
		/// <summary>Gets the event timestamp in UTC.</summary>
		public DateTimeOffset TimestampUtc { get; }
		/// <summary>Gets the event display label.</summary>
		public string Label { get; }
		/// <summary>Gets the event details.</summary>
		public string Details { get; }

		/// <summary>Optional hover text. Null uses event details; empty disables the tooltip.</summary>
		public string Tooltip { get; }
	}

	/// <summary>Host-owned descriptors and asynchronous selection state. No snapshots are retained here.</summary>
	public sealed class GraphTimelineEventTrack
	{
		/// <summary>Gets the ordered event descriptors.</summary>
		public IReadOnlyList<GraphTimelineEvent> Events { get; private set; } = Array.Empty<GraphTimelineEvent>();

		/// <summary>Gets or sets the provider used to load selected events.</summary>
		public IGraphTimelineEventProvider Provider { get; set; }
		/// <summary>Gets the selected event identifier.</summary>
		public string SelectedId { get; private set; }
		/// <summary>Gets the event identifier whose selection is pending.</summary>
		public string PendingId { get; private set; }
		/// <summary>Gets the latest selection error.</summary>
		public string Error { get; private set; }
		/// <summary>Gets the event-list revision.</summary>
		public int Revision { get; private set; }
		/// <summary>Gets the selected event descriptor, when present.</summary>
		public GraphTimelineEvent Selected => Events.FirstOrDefault(item => item.Id == SelectedId);
		/// <summary>Occurs when event descriptors or selection state changes.</summary>
		public event Action Changed;
		/// <summary>Occurs when an event selection is requested.</summary>
		public event Action<GraphTimelineEvent> SelectionRequested;
		/// <summary>Occurs before selection starts, allowing playback to stop.</summary>
		public event Action SelectionStarting;

		/// <summary>Loads a portable cached snapshot first, otherwise delegates to the attached provider.</summary>
		/// <summary>Loads an event snapshot from cache or the attached provider.</summary>
		/// <param name="document">Document containing the timeline workspace cache.</param>
		/// <param name="id">Event identifier to load.</param>
		/// <param name="token">Token that requests cancellation.</param>
		/// <param name="completed">Receives the loaded snapshot.</param>
		/// <param name="failed">Receives a load failure.</param>
		/// <returns>The coroutine that loads the event.</returns>
		public IEnumerator LoadEvent(GraphDocument document, string id, CancellationToken token,
									 Action<GraphSnapshot> completed, Action<Exception> failed)
		{
			if (token.IsCancellationRequested)
			{
				failed(new OperationCanceledException());
				yield break;
			}

			string context = null;
			Exception error = null;

			// Provider discovery can fail while the document still contains a usable portable cache.
			try
			{
				context = Provider?.CacheContext;
			}
			catch (Exception failure)
			{
				error = failure;
			}

			// Prefer the active provider's cache context to avoid loading an identically named event from another source.
			if (context != null && document.Timeline.Workspace.TryGet(context, id, out var cached))
			{
				completed(cached);
				yield break;
			}

			if (Provider == null || error != null)
			{
				// Without a provider context, accept a portable match only when its identity is unambiguous.
				var portable = document.Timeline.Workspace.CachedEvents.Where(item => item.Event.Id == id).ToArray();
				if (portable.Length == 1)
				{
					completed(portable[0].Snapshot);
					yield break;
				}

				failed(error ?? new InvalidOperationException("No timeline event provider is attached."));
				yield break;
			}

			yield return Provider.LoadEvent(id, token, completed, failed);
		}

		/// <summary>Restores saved descriptors before provider discovery, including when the source is unavailable.</summary>
		/// <summary>Restores event descriptors from the workspace's saved cache.</summary>
		/// <param name="workspace">Workspace containing cached descriptors.</param>
		/// <param name="context">Optional provider context to select.</param>
		public void RestoreCachedEvents(GraphTimelineWorkspace workspace, string context = null)
		{
			// Collapse duplicate event IDs across saved contexts before replacing the descriptor list.
			Replace(workspace.CachedEvents
							 .Where(item => context == null || item.Context == context)
							 .GroupBy(item => item.Event.Id, StringComparer.Ordinal)
							 .Select(group => group.Last().Event));
		}

		/// <summary>Replaces the available event descriptors and clears selection state.</summary>
		/// <param name="incoming">Descriptors to install.</param>
		public void Replace(IEnumerable<GraphTimelineEvent> incoming)
		{
			var copy = (incoming ?? Array.Empty<GraphTimelineEvent>()).ToArray();

			// Validate the replacement before notifying playback or discarding confirmed selection.
			if (copy.Any(item => item == null) ||
				copy.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != copy.Length)
				throw new ArgumentException("Duplicate or null timeline event.", nameof(incoming));
			// OrderBy is stable: equal timestamps preserve the host's discovery order.
			SelectionStarting?.Invoke();
			Events = Array.AsReadOnly(copy.OrderBy(item => item.TimestampUtc).ToArray());
			SelectedId = PendingId = Error = null;
			Revision++;
			Changed?.Invoke();
		}

		/// <summary>Requests selection of an event descriptor.</summary>
		/// <param name="id">Event identifier.</param>
		/// <param name="stopPlayback">Whether to notify listeners that selection is starting.</param>
		public void RequestSelection(string id, bool stopPlayback = true)
		{
			var item = Events.FirstOrDefault(value => value.Id == id) ??
					   throw new ArgumentException("Unknown event.", nameof(id));
			if (stopPlayback) SelectionStarting?.Invoke();

			// Keep the confirmed selection until the host finishes loading this pending request.
			PendingId = id;
			Error = null;
			Changed?.Invoke();
			SelectionRequested?.Invoke(item);
		}

		/// <summary>Confirms the event currently selected by the host.</summary>
		/// <param name="id">Selected event identifier.</param>
		public void ConfirmSelection(string id)
		{
			if (!Events.Any(item => item.Id == id)) throw new ArgumentException("Unknown event.", nameof(id));
			SelectedId = id;
			PendingId = Error = null;
			Changed?.Invoke();
		}

		/// <summary>Finishes a pending selection with an error.</summary>
		/// <param name="id">Identifier of the pending event.</param>
		/// <param name="error">Failure message.</param>
		public void FailSelection(string id, string error)
		{
			if (PendingId != id) return;

			// Ignore stale failures; a current failure clears only the pending request.
			PendingId = null;
			Error = error ?? "Could not load event.";
			Changed?.Invoke();
		}

		/// <summary>Clears event selection to return to the live session.</summary>
		public void SelectSession()
		{
			SelectedId = PendingId = Error = null;
			Changed?.Invoke();
		}

		/// <summary>Cancels pending selection state.</summary>
		/// <param name="stopPlayback">Whether to notify listeners that selection is starting.</param>
		public void CancelPending(bool stopPlayback = false)
		{
			if (stopPlayback) SelectionStarting?.Invoke();
			PendingId = Error = null;
			Changed?.Invoke();
		}
	}
}
