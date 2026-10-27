using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using NUnit.Framework;

namespace Cashg.GraphExplorer.Tests
{
	/// <summary>Represents graph timeline cache tests.</summary>
	public sealed class GraphTimelineCacheTests
	{
		private static readonly DateTimeOffset Epoch = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

		private static GraphSnapshot Snapshot(string id = "a")
		{
			return new GraphSnapshot("dataset", "Graph", new[] { new GraphNodeRecord(id, "type", id) },
									 Array.Empty<GraphEdgeRecord>());
		}

		private static GraphDocument Document()
		{
			var document = new GraphDocument("dataset");
			document.ReplaceSnapshot(Snapshot());
			document.EstablishBaseline(Epoch);
			return document;
		}

		private static void Drive(IEnumerator routine)
		{
			// Drive nested load iterators while guaranteeing disposal of unfinished work after a failure.
			var stack = new Stack<IEnumerator>();
			stack.Push(routine);
			try
			{
				while (stack.Count > 0)
				{
					var next = stack.Peek();
					if (!next.MoveNext())
						(stack.Pop() as IDisposable)?.Dispose();
					else if (next.Current is IEnumerator child) stack.Push(child);
				}
			}
			finally
			{
				while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
			}
		}

		private static GraphTimelineEventTrack Track(Provider provider)
		{
			var track = new GraphTimelineEventTrack { Provider = provider };
			track.Replace(new[]
			{
				new GraphTimelineEvent("a", Epoch, "A"), new GraphTimelineEvent("b", Epoch.AddHours(1), "B"),
				new GraphTimelineEvent("tie", Epoch.AddHours(1), "Tie"),
				new GraphTimelineEvent("c", Epoch.AddHours(2), "C")
			});
			return track;
		}

		/// <summary>Checks that range Includes Endpoints And Tied Events And Portable States Survive Reload.</summary>
		[Test]
		public void RangeIncludesEndpointsAndTiedEventsAndPortableStatesSurviveReload()
		{
			var provider = new Provider();
			var track = Track(provider);
			var document = Document();
			var workspace = document.Timeline.Workspace;
			workspace.SelectRange(Epoch.AddHours(1), Epoch); // reverse mouse drag
			workspace.SetView(Epoch.AddMinutes(10), Epoch.AddMinutes(50));
			Drive(workspace.CacheSelection(document, track, provider, CancellationToken.None));
			Assert.That(provider.Loads, Is.EqualTo(3));
			Assert.That(workspace.CachedEvents.Count, Is.EqualTo(3));
			var json = GraphJson.Serialize(document);
			var restored = GraphJson.Parse(json);

			// Identical snapshots share one serialized state even when several descriptors reference it.
			using (var parsed = JsonDocument.Parse(json))
			{
				Assert.That(
					parsed.RootElement.GetProperty("timelineWorkspace").GetProperty("snapshots").EnumerateObject()
						  .Count(), Is.EqualTo(1), "Identical states must be stored once.");
			}

			var saved = restored.Timeline.Workspace;
			Assert.That(saved.SelectionStartUtc, Is.EqualTo(Epoch));
			Assert.That(saved.ViewStartUtc, Is.EqualTo(Epoch.AddMinutes(10)));
			Assert.That(saved.ViewEndUtc, Is.EqualTo(Epoch.AddMinutes(50)));
			var offline = new GraphTimelineEventTrack();

			// Restore descriptors and load a cached event with no attached provider.
			offline.RestoreCachedEvents(saved);
			GraphSnapshot loaded = null;
			Drive(offline.LoadEvent(restored, "tie", CancellationToken.None, value => loaded = value,
									error => Assert.Fail(error.Message)));
			Assert.That(loaded.Nodes[0].Id, Is.EqualTo("a"));
			Assert.That(offline.Events.Count, Is.EqualTo(3));
			var copy = Document();
			copy.RestoreTimelineFrom(restored);
			Assert.That(copy.Timeline.Workspace.CachedEvents.Count, Is.EqualTo(3));
		}

		/// <summary>Checks that cache Reuse Is Context Scoped And Failed Ranges Retain Completed Events.</summary>
		[Test]
		public void CacheReuseIsContextScopedAndFailedRangesRetainCompletedEvents()
		{
			// Ensure a provider context change prevents reuse of snapshots from the prior source.
			var provider = new Provider { FailingId = "b" };
			var track = Track(provider);
			var document = Document();
			var workspace = document.Timeline.Workspace;
			workspace.SelectRange(Epoch, Epoch.AddHours(2));
			Drive(workspace.CacheSelection(document, track, provider, CancellationToken.None));
			Assert.That(workspace.IsCaching, Is.False);
			Assert.That(workspace.Error, Does.Contain("fixture failure"));
			Assert.That(workspace.CachedEvents.Count, Is.EqualTo(1));
			provider.FailingId = null;

			// Retry only unfinished events in the same context, then reload all events for a changed context.
			Drive(workspace.CacheSelection(document, track, provider, CancellationToken.None));
			Assert.That(provider.Loads, Is.EqualTo(5));
			provider.CacheContext = "configuration-v2";
			Drive(workspace.CacheSelection(document, track, provider, CancellationToken.None));
			Assert.That(provider.Loads, Is.EqualTo(9));
		}

		/// <summary>Checks that cancellation Retains Finished Events And Does Not Leave Cache Busy.</summary>
		[Test]
		public void CancellationRetainsFinishedEventsAndDoesNotLeaveCacheBusy()
		{
			var provider = new Provider();
			var track = Track(provider);
			var document = Document();
			var workspace = document.Timeline.Workspace;
			workspace.SelectRange(Epoch, Epoch.AddDays(1));
			using var cancellation = new CancellationTokenSource();
			workspace.Changed += () =>
			{
				// Cancel after one completed load to exercise partial cache retention and iterator cleanup.
				if (workspace.Completed == 1) cancellation.Cancel();
			};
			Drive(workspace.CacheSelection(document, track, provider, cancellation.Token));
			Assert.That(provider.Loads, Is.EqualTo(1));
			Assert.That(workspace.CachedEvents.Count, Is.EqualTo(1));
			Assert.That(workspace.IsCaching, Is.False);
		}

		/// <summary>Checks that lru Promotes Recently Used Parents And Evicts Old Snapshots.</summary>
		[Test]
		public void LruPromotesRecentlyUsedParentsAndEvictsOldSnapshots()
		{
			var cache = new GraphSnapshotCache(2);
			cache.Store("a", Snapshot());
			cache.Store("b", Snapshot("b"));
			Assert.That(cache.TryGet("a", out _), Is.True);

			// Promoting a makes b the eviction target when c exceeds the two-entry capacity.
			cache.Store("c", Snapshot("c"));
			Assert.That(cache.TryGet("b", out _), Is.False);
			Assert.That(cache.TryGet("a", out var snapshot), Is.True);
			Assert.That(snapshot.Nodes[0].Id, Is.EqualTo("a"));
		}

		private sealed class Provider : IGraphTimelineEventProvider
		{
			public readonly GraphSnapshot State = Snapshot();
			public string FailingId;
			public int Loads;
			public string CacheContext { get; set; } = "configuration-v1";

			public IEnumerator LoadEvent(string id, CancellationToken cancellation, Action<GraphSnapshot> completed,
										 Action<Exception> failed)
			{
				Loads++;
				yield return null;
				if (id == FailingId) failed(new InvalidOperationException("fixture failure"));
				else completed(State);
			}
		}
	}
}