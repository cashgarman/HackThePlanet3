using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Cashg.GraphExplorer
{
	/// <summary>Loads exact external event states. Context must change when provider configuration changes.</summary>
	public interface IGraphTimelineEventProvider
	{
		/// <summary>Gets the provider configuration key used to scope cached snapshots.</summary>
		string CacheContext { get; }

		/// <summary>Loads the snapshot associated with an external timeline event.</summary>
		/// <param name="id">Event identifier.</param>
		/// <param name="cancellation">Token that requests cancellation.</param>
		/// <param name="completed">Receives the loaded snapshot.</param>
		/// <param name="failed">Receives a load failure.</param>
		/// <returns>The coroutine that loads the snapshot.</returns>
		IEnumerator LoadEvent(string id, CancellationToken cancellation, Action<GraphSnapshot> completed,
							  Action<Exception> failed);
	}

	/// <summary>Represents graph cached timeline event.</summary>
	public sealed class GraphCachedTimelineEvent
	{
		/// <summary>Creates a cached event descriptor and snapshot pair.</summary>
		/// <param name="item">Event descriptor.</param>
		/// <param name="context">Provider context associated with the snapshot.</param>
		/// <param name="snapshot">Snapshot loaded for the event.</param>
		public GraphCachedTimelineEvent(GraphTimelineEvent item, string context, GraphSnapshot snapshot)
		{
			Event = item ?? throw new ArgumentNullException(nameof(item));
			Context = context ?? "";
			Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
		}

		/// <summary>Gets the cached event descriptor.</summary>
		public GraphTimelineEvent Event { get; }

		/// <summary>Gets the provider context for the cached snapshot.</summary>
		public string Context { get; }

		/// <summary>Gets the cached graph snapshot.</summary>
		public GraphSnapshot Snapshot { get; }
	}

	/// <summary>Dataset-owned timeline selection, absolute zoom window, and portable cached event snapshots.</summary>
	public sealed class GraphTimelineWorkspace
	{
		private readonly Dictionary<string, GraphCachedTimelineEvent> cached = new(StringComparer.Ordinal);
		private readonly HashSet<string> cachedIds = new(StringComparer.Ordinal);

		/// <summary>Gets the cached external event snapshots.</summary>
		public IReadOnlyCollection<GraphCachedTimelineEvent> CachedEvents => cached.Values;

		/// <summary>Gets the start of the selected event range.</summary>
		public DateTimeOffset? SelectionStartUtc { get; private set; }

		/// <summary>Gets the end of the selected event range.</summary>
		public DateTimeOffset? SelectionEndUtc { get; private set; }

		/// <summary>Gets the start of the visible timeline range.</summary>
		public DateTimeOffset? ViewStartUtc { get; private set; }

		/// <summary>Gets the end of the visible timeline range.</summary>
		public DateTimeOffset? ViewEndUtc { get; private set; }

		/// <summary>Gets whether an event range is selected.</summary>
		public bool HasSelection => SelectionStartUtc.HasValue;

		/// <summary>Gets whether selected event snapshots are being cached.</summary>
		public bool IsCaching { get; private set; }

		/// <summary>Gets the number of snapshots cached by the current operation.</summary>
		public int Completed { get; private set; }

		/// <summary>Gets the number of selected events in the current cache operation.</summary>
		public int Total { get; private set; }

		/// <summary>Gets the latest cache operation error, when one occurred.</summary>
		public string Error { get; private set; }

		/// <summary>Determines whether an event snapshot is cached for the specified context.</summary>
		/// <param name="id">Event identifier.</param>
		/// <param name="context">Optional provider context; null checks any cached context.</param>
		/// <returns><see langword="true" /> when the event is cached.</returns>
		public bool IsCached(string id, string context = null)
		{
			return context == null ? cachedIds.Contains(id) : cached.ContainsKey(Key(context, id));
		}

		/// <summary>Occurs when workspace selection, view, or cache state changes.</summary>
		public event Action Changed;

		/// <summary>Selects the inclusive event-time range between two timestamps.</summary>
		/// <param name="start">One endpoint of the selected range.</param>
		/// <param name="end">The other endpoint of the selected range.</param>
		public void SelectRange(DateTimeOffset start, DateTimeOffset end)
		{
			// Normalize reversed drag endpoints to an inclusive UTC selection.
			SelectionStartUtc = (start < end ? start : end).ToUniversalTime();
			SelectionEndUtc = (start > end ? start : end).ToUniversalTime();
			Changed?.Invoke();
		}

		/// <summary>Clears the selected event range.</summary>
		public void ClearSelection()
		{
			SelectionStartUtc = SelectionEndUtc = null;
			Changed?.Invoke();
		}

		/// <summary>Sets or clears the visible timeline time range.</summary>
		/// <param name="start">Optional visible range start.</param>
		/// <param name="end">Optional visible range end.</param>
		public void SetView(DateTimeOffset? start, DateTimeOffset? end)
		{
			if (start.HasValue != end.HasValue || (start.HasValue && start.Value >= end.Value))
				throw new ArgumentException("A view needs an increasing pair of timestamps.");
			ViewStartUtc = start?.ToUniversalTime();
			ViewEndUtc = end?.ToUniversalTime();
			Changed?.Invoke();
		}

		/// <summary>Determines whether a timestamp is inside the selected range.</summary>
		/// <param name="time">Timestamp to check.</param>
		/// <returns><see langword="true" /> when the timestamp is selected.</returns>
		public bool Contains(DateTimeOffset time)
		{
			return HasSelection && time >= SelectionStartUtc.Value && time <= SelectionEndUtc.Value;
		}

		/// <summary>Filters event descriptors to the selected time range.</summary>
		/// <param name="events">Event descriptors to filter.</param>
		/// <returns>Events whose timestamps fall inside the selected range.</returns>
		public IReadOnlyList<GraphTimelineEvent> SelectedEvents(IEnumerable<GraphTimelineEvent> events)
		{
			return (events ?? Array.Empty<GraphTimelineEvent>()).Where(item => Contains(item.TimestampUtc)).ToArray();
		}

		private static string Key(string context, string id)
		{
			// Prefix the context length so concatenated provider contexts and IDs cannot collide.
			return (context ?? "").Length + ":" + context + id;
		}

		/// <summary>Looks up a cached snapshot for a provider context and event.</summary>
		/// <param name="context">Provider context.</param>
		/// <param name="id">Event identifier.</param>
		/// <param name="snapshot">Receives the snapshot when found.</param>
		/// <returns><see langword="true" /> when the snapshot is cached.</returns>
		public bool TryGet(string context, string id, out GraphSnapshot snapshot)
		{
			if (cached.TryGetValue(Key(context, id), out var value))
			{
				snapshot = value.Snapshot;
				return true;
			}

			snapshot = null;
			return false;
		}

		/// <summary>Stores a snapshot for an event in a provider context.</summary>
		/// <param name="item">Event descriptor.</param>
		/// <param name="context">Provider context.</param>
		/// <param name="snapshot">Snapshot to cache.</param>
		public void Store(GraphTimelineEvent item, string context, GraphSnapshot snapshot)
		{
			cached[Key(context, item.Id)] = new GraphCachedTimelineEvent(item, context, snapshot);
			cachedIds.Add(item.Id);
			Changed?.Invoke();
		}

		/// <summary>Removes all cached event snapshots.</summary>
		public void ClearCache()
		{
			cached.Clear();
			cachedIds.Clear();
			Changed?.Invoke();
		}

		/// <summary>Copies portable state; transient progress and errors are deliberately excluded.</summary>
		public void RestoreFrom(GraphTimelineWorkspace source)
		{
			if (ReferenceEquals(this, source)) return;

			// Copy portable bounds and snapshots while leaving transient progress and errors local.
			SelectionStartUtc = source.SelectionStartUtc;
			SelectionEndUtc = source.SelectionEndUtc;
			ViewStartUtc = source.ViewStartUtc;
			ViewEndUtc = source.ViewEndUtc;
			cached.Clear();
			cachedIds.Clear();
			foreach (var item in source.CachedEvents)
			{
				cached.Add(Key(item.Context, item.Event.Id), item);
				cachedIds.Add(item.Event.Id);
			}

			Changed?.Invoke();
		}

		/// <summary>Owner drives this coroutine. Cancellation retains successfully completed events.</summary>
		public IEnumerator CacheSelection(GraphDocument document, GraphTimelineEventTrack track,
										  IGraphTimelineEventProvider provider, CancellationToken token)
		{
			if (IsCaching) throw new InvalidOperationException("A timeline cache operation is already running.");

			// Freeze the selected descriptors so range edits cannot change an in-flight cache operation.
			var selected = SelectedEvents(track?.Events).ToArray();
			IsCaching = true;
			Completed = 0;
			Total = selected.Length;
			Error = null;
			Changed?.Invoke();
			try
			{
				var context = provider?.CacheContext ?? "";
				foreach (var item in selected)
				{
					if (token.IsCancellationRequested) yield break;

					// Reuse completed work in this context; cancellation and later failures keep earlier snapshots.
					if (!TryGet(context, item.Id, out var snapshot))
					{
						if (provider == null)
						{
							Error = "No event loader is attached.";
							yield break;
						}

						Exception failure = null;
						yield return provider.LoadEvent(item.Id, token, value => snapshot = value,
														error => failure = error);
						if (token.IsCancellationRequested) yield break;
						if (failure != null || snapshot == null)
						{
							Error = failure?.Message ?? "The event loader returned no snapshot.";
							yield break;
						}

						if (context != provider.CacheContext)
						{
							// Do not mix snapshots from a provider that changed sources while the load yielded.
							Error = "The event provider changed during caching. Retry the selection.";
							yield break;
						}

						if (snapshot.DatasetId != document.DatasetId)
						{
							Error = "The event belongs to another graph.";
							yield break;
						}

						// Validate before admitting an independently loaded snapshot to the portable cache.
						var validation = new GraphDocument(document.DatasetId);
						validation.ReplaceSnapshot(snapshot);
						Store(item, context, snapshot);
					}

					Completed++;
					Changed?.Invoke();
					yield return null;
				}
				// Session events and motion are already part of GraphJson's baseline/change history.
			}
			finally
			{
				IsCaching = false;
				Changed?.Invoke();
			}
		}
	}

	/// <summary>Bounded LRU for immutable graph snapshots; useful for repeated parent comparisons.</summary>
	public sealed class GraphSnapshotCache
	{
		private readonly int capacity;

		private readonly Dictionary<string, LinkedListNode<KeyValuePair<string, GraphSnapshot>>> entries =
			new(StringComparer.Ordinal);

		private readonly LinkedList<KeyValuePair<string, GraphSnapshot>> order = new();

		/// <summary>Creates a bounded least-recently-used snapshot cache.</summary>
		/// <param name="capacity">Maximum number of snapshots to retain.</param>
		public GraphSnapshotCache(int capacity = 8)
		{
			if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
			this.capacity = capacity;
		}

		/// <summary>Attempts to get a snapshot and marks it as recently used.</summary>
		/// <param name="key">Cache key.</param>
		/// <param name="snapshot">Receives the cached snapshot when found.</param>
		/// <returns><see langword="true" /> when the key is present.</returns>
		public bool TryGet(string key, out GraphSnapshot snapshot)
		{
			if (!entries.TryGetValue(key, out var node))
			{
				snapshot = null;
				return false;
			}

			order.Remove(node);

			// A successful lookup promotes the entry to the front of the eviction order.
			order.AddFirst(node);
			snapshot = node.Value.Value;
			return true;
		}

		/// <summary>Adds or replaces a snapshot and evicts least-recently-used entries if needed.</summary>
		/// <param name="key">Cache key.</param>
		/// <param name="snapshot">Snapshot to cache.</param>
		public void Store(string key, GraphSnapshot snapshot)
		{
			if (entries.TryGetValue(key, out var old))
			{
				order.Remove(old);
				entries.Remove(key);
			}

			entries.Add(key, order.AddFirst(new KeyValuePair<string, GraphSnapshot>(key, snapshot)));

			// Evict from the least-recently-used end until the configured capacity is respected.
			while (entries.Count > capacity)
			{
				entries.Remove(order.Last.Value.Key);
				order.RemoveLast();
			}
		}

		/// <summary>Removes all cached snapshots.</summary>
		public void Clear()
		{
			entries.Clear();
			order.Clear();
		}
	}
}