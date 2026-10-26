using NUnit.Framework;

namespace Cashg.GraphExplorer.Tests
{
	/// <summary>Represents graph timeline viewport tests.</summary>
	public sealed class GraphTimelineViewportTests
	{
		/// <summary>Checks that zoom Keeps Time Under Cursor.</summary>
		[TestCase(0d)]
		[TestCase(.25d)]
		[TestCase(1d)]
		public void ZoomKeepsTimeUnderCursor(double anchor)
		{
			var view = new GraphTimelineViewport();
			var time = view.FromVisible(anchor);
			view.Zoom(anchor, .5);
			Assert.That(view.End - view.Start, Is.EqualTo(.5).Within(1e-12));
			Assert.That(view.FromVisible(anchor), Is.EqualTo(time).Within(1e-12));
			Assert.That(view.ToVisible(time), Is.EqualTo(anchor).Within(1e-12));
		}

		/// <summary>Checks that markers And Scrubbing Use Visible Window.</summary>
		[Test]
		public void MarkersAndScrubbingUseVisibleWindow()
		{
			var view = new GraphTimelineViewport();
			view.Zoom(.75, .2);
			Assert.That(view.ToVisible(.5), Is.LessThan(0));
			Assert.That(view.ToVisible(.7), Is.EqualTo(.5).Within(1e-12));
			Assert.That(view.ToVisible(.9), Is.GreaterThan(1));
			Assert.That(view.FromVisible(.5), Is.EqualTo(.7).Within(1e-12));
		}

		/// <summary>Checks that zoom Out Clamps To History And Restores Full Range.</summary>
		[Test]
		public void ZoomOutClampsToHistoryAndRestoresFullRange()
		{
			var view = new GraphTimelineViewport();
			view.Zoom(1, .1);
			view.Zoom(0, 2);
			Assert.That(view.Start, Is.EqualTo(.8).Within(1e-12));
			Assert.That(view.End, Is.EqualTo(1));
			view.Zoom(.4, 100);
			Assert.That(view.IsZoomed, Is.False);
			Assert.That(view.Start, Is.Zero);
			Assert.That(view.End, Is.EqualTo(1));
		}

		/// <summary>Checks that deep Zoom Preserves Precision And Has Finite Limit.</summary>
		[Test]
		public void DeepZoomPreservesPrecisionAndHasFiniteLimit()
		{
			var view = new GraphTimelineViewport();
			for (var i = 0; i < 200; i++) view.Zoom(.987, .5);

			// Repeated zoom reaches a finite minimum span and invalid input cannot corrupt its coordinates.
			Assert.That(view.End - view.Start, Is.EqualTo(1e-9).Within(1e-15));
			Assert.That(view.FromVisible(.5), Is.GreaterThan(view.Start).And.LessThan(view.End));
			view.Zoom(double.NaN, .5);
			view.Zoom(.5, double.PositiveInfinity);
			Assert.That(double.IsNaN(view.Start), Is.False);
			view.Reset();
			Assert.That(view.IsZoomed, Is.False);
		}
	}
}
