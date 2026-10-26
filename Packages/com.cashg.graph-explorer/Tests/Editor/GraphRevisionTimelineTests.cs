using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Cashg.GraphExplorer.Tests
{
    /// <summary>Represents graph revision timeline tests.</summary>
    public sealed class GraphRevisionTimelineTests
    {
        [Test] public void BranchLanesKeepMergeParentsAndNonMonotonicDates()
        {
            // Preserve ancestry order even when commit timestamps run backward or are absent for the working tree.
            var now = DateTimeOffset.UtcNow;
            var root = new GraphRevision("root", null, now, "Root");
            var left = new GraphRevision("left", new[] { "root" }, now.AddDays(-1), "Left");
            var right = new GraphRevision("right", new[] { "root" }, now.AddDays(1), "Right");
            var merge = new GraphRevision("merge", new[] { "left", "right" }, now, "Merge");
            var working = new GraphRevision("work", new[] { "merge" }, null, "Working Tree");
            var timeline = new GraphRevisionTimeline(); timeline.Replace(new[] { root, left, right, merge, working });
            Assert.That(merge.LaneLinks.Count, Is.EqualTo(2));
            Assert.That(left.Lane, Is.Not.EqualTo(right.Lane));
            Assert.That(left.TimestampUtc, Is.EqualTo(now.AddDays(-1)));
            Assert.That(working.TimestampUtc, Is.Null);
            string requested = null; timeline.SelectionRequested += revision => requested = revision.Id;
            timeline.RequestSelection("left"); Assert.That(requested, Is.EqualTo("left")); Assert.That(timeline.Selected, Is.Null);
            timeline.ConfirmSelection("left"); Assert.That(timeline.Selected, Is.SameAs(left));
            Assert.Throws<ArgumentException>(() => timeline.Replace(new[] { root, root }));
            Assert.That(timeline.Revisions.Count, Is.EqualTo(5), "Invalid imports must retain the previous history.");
        }

        [Test] public void DeltaTracksBodyEditsDeletionsRenamesAndReferenceCounts()
        {
            GraphNodeRecord Symbol(string id, string hash, int line) => new GraphNodeRecord(id, "member", id,
                new Dictionary<string, GraphValue> { ["content_hash"] = GraphValue.From(hash), ["line"] = GraphValue.From(line) });
            var before = new GraphSnapshot("test", "Test", new[] { Symbol("same", "one", 1), Symbol("edit", "old", 2), Symbol("gone", "x", 3) },
                new[] { new GraphEdgeRecord("call", "same", "edit", "calls", properties: new Dictionary<string, GraphValue> { ["count"] = GraphValue.From(1) }) });
            var after = new GraphSnapshot("test", "Test", new[] { Symbol("same", "one", 200), Symbol("edit", "new", 2), Symbol("renamed", "x", 3) },
                new[] { new GraphEdgeRecord("call", "same", "edit", "calls", properties: new Dictionary<string, GraphValue> { ["count"] = GraphValue.From(2) }) });
            var changes = GraphRevisionChangeSummary.Between(before, after);

            // Line shifts are metadata; changed content hashes and relationship counts identify semantic edits.
            Assert.That(changes.CreatedNodeIds, Is.EqualTo(new[] { "renamed" }));
            Assert.That(changes.ModifiedNodeIds, Is.EqualTo(new[] { "edit" }));
            Assert.That(changes.DeletedNodeIds, Is.EqualTo(new[] { "gone" }));
            Assert.That(changes.ModifiedEdgeIds, Is.EqualTo(new[] { "call" }));
            Assert.That(GraphRevisionChangeSummary.Between(null, after).NodesCreated, Is.EqualTo(3));
        }
    }
}
