using System;
using System.Linq;
using NUnit.Framework;

namespace Cashg.GraphExplorer.Tests
{
	/// <summary>Represents graph timeline event track tests.</summary>
	public sealed class GraphTimelineEventTrackTests
	{
		/// <summary>Checks that optional Tooltips Retain Independent Multiline Text Through Selection.</summary>
		[Test]
		public void OptionalTooltipsRetainIndependentMultilineTextThroughSelection()
		{
			var time = DateTimeOffset.UtcNow;
			var track = new GraphTimelineEventTrack();
			var item = new GraphTimelineEvent("commit", time, "Short label", "Selection details",
											  "Commit subject\n\nFull body with <literal> text.");
			track.Replace(new[] { item });
			track.RequestSelection(item.Id);
			track.ConfirmSelection(item.Id);

			// Selection details and multiline hover text remain independent, including null and disabled tooltip states.
			Assert.That(track.Selected.Tooltip, Is.EqualTo("Commit subject\n\nFull body with <literal> text."));
			Assert.That(track.Selected.Details, Is.EqualTo("Selection details"));
			Assert.That(new GraphTimelineEvent("default", time, "Label").Tooltip, Is.Null);
			Assert.That(new GraphTimelineEvent("disabled", time, "Label", tooltip: "").Tooltip, Is.Empty);
		}

		/// <summary>Checks that dates Are Normalized And Sorted Stably Without Ancestry Clamping.</summary>
		[Test]
		public void DatesAreNormalizedAndSortedStablyWithoutAncestryClamping()
		{
			// Equal UTC instants retain discovery order even when ancestry names imply a different date order.
			var track = new GraphTimelineEventTrack();
			var date = DateTimeOffset.Parse("2020-01-01T10:00:00+02:00");
			track.Replace(new[]
			{
				new GraphTimelineEvent("parent", date.AddDays(2), "Parent"),
				new GraphTimelineEvent("child", date, "Child"),
				new GraphTimelineEvent("same-time", date.ToUniversalTime(), "Other branch")
			});
			Assert.That(track.Events.Select(item => item.Id), Is.EqualTo(new[] { "child", "same-time", "parent" }));
			Assert.That(track.Events[0].TimestampUtc.Offset, Is.EqualTo(TimeSpan.Zero));
			Assert.That(track.Events[2].TimestampUtc - track.Events[0].TimestampUtc, Is.EqualTo(TimeSpan.FromDays(2)));
		}

		/// <summary>Checks that pending Failure And Retry Retain Confirmed Selection And Use Identity.</summary>
		[Test]
		public void PendingFailureAndRetryRetainConfirmedSelectionAndUseIdentity()
		{
			var track = new GraphTimelineEventTrack();
			var time = DateTimeOffset.UtcNow;
			track.Replace(new[] { new GraphTimelineEvent("a", time, "A"), new GraphTimelineEvent("b", time, "B") });
			var stops = 0;
			string requested = null;
			track.SelectionStarting += () => stops++;
			track.SelectionRequested += item => requested = item.Id;
			track.RequestSelection("a");
			track.ConfirmSelection("a");
			track.RequestSelection("b");
			Assert.That(requested, Is.EqualTo("b"));
			Assert.That(track.PendingId, Is.EqualTo("b"));
			Assert.That(track.SelectedId, Is.EqualTo("a"));
			track.FailSelection("a", "stale failure");

			// A stale failure must leave b pending and a confirmed until the current request resolves.
			Assert.That(track.PendingId, Is.EqualTo("b"));
			track.FailSelection("b", "retry");
			Assert.That(track.Error, Is.EqualTo("retry"));
			Assert.That(track.SelectedId, Is.EqualTo("a"));
			track.RequestSelection("b", false);
			track.ConfirmSelection("b");
			Assert.That(stops, Is.EqualTo(2));
			Assert.That(track.Selected.Id, Is.EqualTo("b"));
			Assert.That(track.Error, Is.Null);
			track.SelectSession();
			Assert.That(track.Selected, Is.Null);
		}

		/// <summary>Checks that replacement Is Atomic And Clears Old Scope.</summary>
		[Test]
		public void ReplacementIsAtomicAndClearsOldScope()
		{
			var track = new GraphTimelineEventTrack();
			var item = new GraphTimelineEvent("a", DateTimeOffset.UtcNow, "A");
			track.Replace(new[] { item });
			track.RequestSelection("a");
			track.ConfirmSelection("a");
			Assert.Throws<ArgumentException>(() => track.Replace(new[] { item, item }));

			// Invalid replacement preserves selection; a valid empty replacement clears the previous scope.
			Assert.That(track.SelectedId, Is.EqualTo("a"));
			track.Replace(Array.Empty<GraphTimelineEvent>());
			Assert.That(track.SelectedId, Is.Null);
			Assert.That(track.Events, Is.Empty);
			Assert.Throws<ArgumentException>(() => track.RequestSelection("a"));
		}
	}
}