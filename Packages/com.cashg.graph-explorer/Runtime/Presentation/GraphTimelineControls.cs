using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace Cashg.GraphExplorer
{
	/// <summary>Shared timeline UI. Hosts provide graph projection synchronization after a document seek.</summary>
	public sealed class GraphTimelineControls : IDisposable
	{
		private const float PanelLeft = 72f;
		private const float PanelInsetFallback = 24f;
		private readonly Stack<IEnumerator> cacheRoutines = new();
		private readonly Label cacheStatus;
		private readonly List<string> eventDescriptions = new();
		private readonly IGraphTimelineEventShellHost eventHost;
		private readonly VisualElement eventTooltip;
		private readonly Label eventTooltipText;
		private readonly GraphTimelineEventTrack eventTrack;
		private readonly Label headerSummary, timeLabel;
		private readonly IGraphTimelineShellHost host;
		private readonly FloatField intervalField;
		private readonly VisualElement panel, body, marks, optionsRow, currentHighlight, playhead;
		private readonly Button playButton, liveButton, minimizeButton, cacheButton;
		private readonly List<TimePoint> points = new();
		private readonly IGraphPositionHistoryShellHost positionHost;
		private readonly Toggle recordingToggle;
		private readonly EventCallback<GeometryChangedEvent> rootGeometryHandler;
		private readonly IGraphShellHost shell;
		private readonly SliderInt slider;
		private readonly Slider timeSlider;
		private readonly EventCallback<KeyDownEvent> timelineKeyHandler;
		private CancellationTokenSource cacheCancellation;
		private IVisualElementScheduledItem cacheSchedule;
		private VisualElement hoveredMarker;
		private GraphTimelineEntry lastMarkerEntry;
		private GraphSnapshot markerBaseline;
		private int markerEntryCount = -1, markerPositionCount = -1;
		private int markerTrackRevision = -1;
		private GraphDocument observedDocument;
		private IVisualElementScheduledItem playback;
		private DateTimeOffset playbackStartedAt, playbackFrom;
		private DateTimeOffset rangeAnchor;
		private int rangePointer;
		private bool selectingRange;
		private bool syncing, playing, disposed;

		/// <summary>Creates timeline controls for a shell and its timeline host.</summary>
		/// <param name="shell">Shell that owns the controls.</param>
		/// <param name="host">Host that supplies timeline state and navigation operations.</param>
		public GraphTimelineControls(IGraphShellHost shell, IGraphTimelineShellHost host)
		{
			this.shell = shell ?? throw new ArgumentNullException(nameof(shell));
			this.host = host ?? throw new ArgumentNullException(nameof(host));
			positionHost = host as IGraphPositionHistoryShellHost;
			eventHost = host as IGraphTimelineEventShellHost;
			eventTrack = eventHost?.TimelineEvents;

			// Subscribe to optional external history before building controls shared with session history.
			if (eventTrack != null)
			{
				eventTrack.Changed += OnExternalChanged;
				eventTrack.SelectionStarting += StopPlayback;
			}

			panel = GraphUi.Element("timeline-panel");
			panel.name = "graph-timeline";
			shell.root.Add(panel);
			panel.style.top = StyleKeyword.Auto;
			panel.style.right = StyleKeyword.Auto;
			panel.style.height = StyleKeyword.Auto;
			panel.style.flexGrow = 0;
			panel.style.flexShrink = 0;
			rootGeometryHandler = _ => SyncPanelBounds();
			shell.root.RegisterCallback(rootGeometryHandler);
			timelineKeyHandler = OnTimelineKey;
			shell.root.RegisterCallback(timelineKeyHandler, TrickleDown.TrickleDown);
			panel.RegisterCallback<GeometryChangedEvent>(_ => SyncReservedSpace());

			// Build the compact header and transport controls around both discrete and continuous sliders.
			var header = GraphUi.Element("timeline-header");
			panel.Add(header);
			var headerTitle = GraphUi.Element("timeline-header-title");
			headerTitle.Add(GraphUi.Label("HISTORY", "timeline-title"));
			headerSummary = GraphUi.Label("", "timeline-header-summary");
			headerTitle.Add(headerSummary);
			header.Add(headerTitle);
			optionsRow = GraphUi.Element("timeline-options");
			header.Add(optionsRow);
			minimizeButton = Icon("down", ToggleMinimized, "Minimize history timeline.");
			minimizeButton.name = "timeline-minimize";
			minimizeButton.AddToClassList("timeline-minimize");
			header.Add(minimizeButton);
			body = GraphUi.Element("timeline-body");
			panel.Add(body);
			var row = GraphUi.Element("timeline-row");
			body.Add(row);
			row.Add(Icon("back", () => Step(-1), "Step to the previous graph state. Left arrow."));
			playButton = Icon("play", TogglePlayback, "Play graph history.");
			row.Add(playButton);
			row.Add(Icon("forward", () => Step(1), "Step to the next graph state. Right arrow."));
			slider = new SliderInt(0, 1)
				{ name = "timeline-slider", tooltip = "Scrub through recorded graph changes." };
			slider.AddToClassList("timeline-slider");
			slider.RegisterValueChangedCallback(evt =>
			{
				if (!syncing) Seek(evt.newValue - 1);
			});
			row.Add(slider);
			timeSlider = new Slider(0, 1)
			{
				name = "timeline-time-slider", tooltip = "Scrub continuously through graph changes and recorded motion."
			};
			timeSlider.AddToClassList("timeline-slider");
			timeSlider.RegisterValueChangedCallback(evt =>
			{
				if (syncing) return;
				if (Continuous) SeekContinuous(FromProgress(evt.newValue));
				else
					Seek(Mathf.RoundToInt((float)Viewport.FromVisible(evt.newValue) * Document.Timeline.Entries.Count) -
						 1);
			});
			row.Add(timeSlider);
			SuppressSliderChrome(slider);
			SuppressSliderChrome(timeSlider);
			slider.RegisterCallback<WheelEvent>(ZoomTimeline, TrickleDown.TrickleDown);
			timeSlider.RegisterCallback<WheelEvent>(ZoomTimeline, TrickleDown.TrickleDown);
			liveButton = Icon("play", ToggleLive, "Play the layout at the present end of the timeline.");
			liveButton.name = "timeline-live";
			row.Add(liveButton);
			cacheButton = GraphUi.Button("Cache Timeline", CacheTimeline, "timeline-cache");
			cacheButton.name = "timeline-cache";
			cacheButton.tooltip = "Shift-drag over the timeline to select a range, then cache every event in it.";
			row.Add(cacheButton);
			cacheStatus = GraphUi.Label("Shift-drag to select a range", "timeline-cache-status");
			body.Add(cacheStatus);
			marks = GraphUi.Element("timeline-marks");

			// Route zoom and range gestures through the same surfaces used for scrubbing and event markers.
			marks.pickingMode = PickingMode.Position;
			marks.RegisterCallback<WheelEvent>(ZoomTimeline, TrickleDown.TrickleDown);
			slider.tooltip += " Hold Ctrl and scroll to zoom at the cursor.";
			timeSlider.tooltip += " Hold Ctrl and scroll to zoom at the cursor.";
			marks.tooltip = "Shift-drag to select a range. Hold Ctrl and scroll to zoom at the cursor.";
			foreach (var surface in new[] { slider, timeSlider, marks })
			{
				surface.RegisterCallback<PointerDownEvent>(BeginRange, TrickleDown.TrickleDown);
				surface.RegisterCallback<PointerMoveEvent>(MoveRange, TrickleDown.TrickleDown);
				surface.RegisterCallback<PointerUpEvent>(EndRange, TrickleDown.TrickleDown);
				surface.RegisterCallback<PointerCaptureOutEvent>(_ => selectingRange = false);
				surface.RegisterCallback<ClickEvent>(evt =>
				{
					if (evt.shiftKey) evt.StopImmediatePropagation();
				}, TrickleDown.TrickleDown);
			}

			marks.generateVisualContent += DrawRange;
			slider.RegisterCallback<GeometryChangedEvent>(_ => RefreshMarkerGeometry());
			timeSlider.RegisterCallback<GeometryChangedEvent>(_ => RefreshMarkerGeometry());
			marks.RegisterCallback<GeometryChangedEvent>(_ => RefreshMarkerGeometry());
			recordingToggle = new Toggle
			{
				text = "Record motion", name = "timeline-position-recording",
				tooltip = "Record node motion for timeline playback."
			};
			recordingToggle.AddToClassList("timeline-position-recording");
			GraphUi.UseShellCheckbox(recordingToggle);
			optionsRow.Add(recordingToggle);
			recordingToggle.RegisterValueChangedCallback(evt =>
			{
				if (Document == null) return;
				Document.Timeline.PositionRecordingEnabled = evt.newValue;
				Refresh();
			});
			intervalField = new FloatField("Every")
			{
				name = "timeline-position-interval", isDelayed = true,
				tooltip = "Motion sample interval in seconds (0.1–60)."
			};
			intervalField.AddToClassList("timeline-position-interval");
			optionsRow.Add(intervalField);
			optionsRow.Add(GraphUi.Label("s", "timeline-interval-unit"));
			intervalField.RegisterValueChangedCallback(evt =>
			{
				// Preserve the prior interval for nonfinite input and clamp finite edits to supported limits.
				if (Document == null) return;
				var interval = float.IsNaN(evt.newValue) || float.IsInfinity(evt.newValue)
					? Document.Timeline.PositionSampleIntervalSeconds
					: Mathf.Clamp(evt.newValue, .1f, 60f);
				Document.Timeline.PositionSampleIntervalSeconds = interval;
				intervalField.SetValueWithoutNotify(interval);
			});
			currentHighlight = GraphUi.Element("timeline-current");
			currentHighlight.pickingMode = PickingMode.Ignore;
			currentHighlight.style.position = Position.Absolute;
			currentHighlight.style.display = DisplayStyle.None;
			body.Add(currentHighlight);
			body.Add(marks);
			playhead = GraphUi.Element("timeline-playhead");
			playhead.pickingMode = PickingMode.Ignore;
			playhead.style.position = Position.Absolute;
			playhead.style.display = DisplayStyle.None;
			body.Add(playhead);
			timeLabel = GraphUi.Label("Baseline", "timeline-time");
			headerTitle.Add(timeLabel);
			eventTooltip = GraphUi.Element("ui-tip");

			// Keep the event tooltip on the shell root so it can extend beyond timeline bounds.
			eventTooltip.name = "timeline-event-tooltip";
			eventTooltip.pickingMode = PickingMode.Ignore;
			eventTooltipText = GraphUi.Label("", "ui-tip-label");
			eventTooltipText.pickingMode = PickingMode.Ignore;
			eventTooltipText.enableRichText = false;
			eventTooltip.Add(eventTooltipText);
			shell.root.Add(eventTooltip);
			eventTooltip.style.display = DisplayStyle.None;
			eventTooltip.RegisterCallback<GeometryChangedEvent>(_ => PositionEventTooltip());
			ApplyMinimized(shell.shellPreferences.timelineMinimized);
			Refresh();
		}

		private GraphTimelineWorkspace Workspace => Document?.Timeline.Workspace;

		/// <summary>Gets the normalized viewport used to zoom the timeline.</summary>
		public GraphTimelineViewport Viewport { get; } = new();

		private GraphDocument Document => host.TimelineDocument;
		private bool HasExternal => eventTrack?.Events.Count > 0;

		private string ActiveEventId =>
			!string.IsNullOrEmpty(eventTrack?.PendingId) ? eventTrack.PendingId : eventTrack?.SelectedId;

		private DateTimeOffset CurrentTime
		{
			get
			{
				var id = ActiveEventId;
				if (!string.IsNullOrEmpty(id))
				{
					var active = eventTrack.Events.FirstOrDefault(item => item.Id == id);
					if (active != null) return active.TimestampUtc;
				}

				return Document.Timeline.CurrentTimeUtc;
			}
		}

		private DateTimeOffset EndTime => HasExternal && eventTrack.Events[eventTrack.Events.Count - 1].TimestampUtc >
			Document.Timeline.LatestTimeUtc
				? eventTrack.Events[eventTrack.Events.Count - 1].TimestampUtc
				: Document.Timeline.LatestTimeUtc;

		private bool Continuous => HasExternal || (Document?.Timeline.HasPositionHistory == true &&
												   positionHost?.PositionHistoryAvailable == true);

		private bool AtPresentEnd => Document != null && Document.Timeline.HasBaseline &&
									 string.IsNullOrEmpty(ActiveEventId) && !Document.Timeline.IsHistorical;

		private bool MotionControlsVisible => !shell.shellPreferences.timelineMinimized &&
											  positionHost?.PositionHistoryAvailable == true && AtPresentEnd;

		private DateTimeOffset TimelineStartTimeUtc
		{
			get
			{
				var start = Document.Timeline.EarliestTimeUtc;
				return HasExternal && eventTrack.Events[0].TimestampUtc < start
					? eventTrack.Events[0].TimestampUtc
					: start;
			}
		}

		/// <summary>Stops playback and caching, detaches callbacks, and removes timeline UI.</summary>
		public void Dispose()
		{
			// Stop scheduled work before detaching document, provider, and root callbacks.
			if (disposed) return;
			disposed = true;
			StopPlayback();
			CancelCaching();
			if (observedDocument != null) observedDocument.Changed -= OnDocumentChanged;
			if (eventTrack != null)
			{
				eventTrack.Changed -= OnExternalChanged;
				eventTrack.SelectionStarting -= StopPlayback;
			}

			if (rootGeometryHandler != null) shell.root.UnregisterCallback(rootGeometryHandler);
			if (timelineKeyHandler != null) shell.root.UnregisterCallback(timelineKeyHandler, TrickleDown.TrickleDown);
			SyncReservedSpace();
			panel.RemoveFromHierarchy();
			eventTooltip.RemoveFromHierarchy();
		}

		/// <summary>Sets or clears the timeline's visible time range.</summary>
		/// <param name="startUtc">Optional range start.</param>
		/// <param name="endUtc">Optional range end.</param>
		public void SetVisibleRange(DateTimeOffset? startUtc, DateTimeOffset? endUtc)
		{
			Workspace?.SetView(startUtc, endUtc);
		}

		/// <summary>Selects an event-time range for snapshot caching.</summary>
		/// <param name="startUtc">One endpoint of the selected range.</param>
		/// <param name="endUtc">The other endpoint of the selected range.</param>
		public void SelectRange(DateTimeOffset startUtc, DateTimeOffset endUtc)
		{
			Workspace?.SelectRange(startUtc, endUtc);
		}

		private void OnTimelineKey(KeyDownEvent evt)
		{
			// Reserve arrows for history stepping only when no text field or resize control owns them.
			if (evt.keyCode != KeyCode.LeftArrow && evt.keyCode != KeyCode.RightArrow) return;
			if (disposed || panel.resolvedStyle.display == DisplayStyle.None) return;
			if (evt.ctrlKey || evt.altKey || evt.commandKey || shell.IsEditingText() ||
				IsTextEntry(evt.target as VisualElement)) return;
			var focused = evt.target as VisualElement;
			if (focused != null &&
				(focused.name == "shell-dock-resize" || focused.ClassListContains("shell-resize"))) return;
			Step(evt.keyCode == KeyCode.LeftArrow ? -1 : 1);
			evt.StopImmediatePropagation();
			evt.PreventDefault();
		}

		private static bool IsTextEntry(VisualElement element)
		{
			for (var current = element; current != null; current = current.parent)
				if (current is TextField || current is FloatField || current is IntegerField ||
					current is DoubleField || current is LongField)
					return true;
			return false;
		}

		private void ToggleMinimized()
		{
			ApplyMinimized(!shell.shellPreferences.timelineMinimized);
			shell.SaveShellPreferences();
		}

		private void ApplyMinimized(bool minimized)
		{
			// Swap the full timeline body for the compact summary while preserving the saved preference.
			if (minimized) HideEventTooltip();
			shell.shellPreferences.timelineMinimized = minimized;
			panel.EnableInClassList("timeline-minimized", minimized);
			body.style.display = minimized ? DisplayStyle.None : DisplayStyle.Flex;
			optionsRow.style.display = MotionControlsVisible ? DisplayStyle.Flex : DisplayStyle.None;
			timeLabel.style.display = minimized ? DisplayStyle.None : DisplayStyle.Flex;
			headerSummary.style.display = minimized ? DisplayStyle.Flex : DisplayStyle.None;
			minimizeButton.Clear();
			minimizeButton.Add(GraphUi.Glyph(minimized ? "up" : "down"));
			minimizeButton.tooltip = minimized ? "Expand history timeline." : "Minimize history timeline.";
		}

		private void SyncPanelBounds()
		{
			// Anchor history beside the rail and above the footer after shell geometry is resolved.
			if (disposed || panel.panel == null) return;
			var rootWidth = shell.root.resolvedStyle.width;
			if (!float.IsFinite(rootWidth) || rootWidth < 100) return;
			var left = PanelLeft;
			panel.style.left = left;
			panel.style.bottom = ResolvePanelBottom();
			panel.style.top = StyleKeyword.Auto;
			panel.style.right = shell.root.ClassListContains("shell-compact") ? 8f : 16f;
			panel.style.width = StyleKeyword.Auto;
			SyncReservedSpace();
		}

		private void SyncReservedSpace()
		{
			// Raise neighboring shell panels above the timeline's actual height and footer inset.
			var visible = !disposed && panel.style.display.value != DisplayStyle.None;
			var height = panel.resolvedStyle.height;
			var inset = ResolvePanelBottom();
			var bottom = visible && float.IsFinite(height) ? inset + height + 8f : 16f;
			if (shell.sidebar != null) shell.sidebar.style.bottom = bottom;
			if (shell.rightColumn != null) shell.rightColumn.style.bottom = bottom;
			if (shell.commandBelt != null) shell.commandBelt.style.bottom = visible ? bottom : inset + 10f;
			if (shell.shellRail != null) shell.shellRail.style.bottom = inset;
		}

		private float ResolvePanelBottom()
		{
			var footer = shell.root?.Q(className: "footer");
			if (footer == null) return 0f;
			var height = footer.resolvedStyle.height;
			return float.IsFinite(height) && height > 0f ? height : PanelInsetFallback;
		}

		private static Button Icon(string glyph, Action action, string tip)
		{
			var button = new Button(action) { tooltip = tip };
			button.AddToClassList("icon-button");
			button.Add(GraphUi.Glyph(glyph));
			return button;
		}

		/// <summary>Refreshes timeline labels, event marks, and viewport state.</summary>
		public void Refresh()
		{
			// Rebuild timeline points and controls from the current document and provider state.
			if (disposed) return;
			var document = Document;
			if (!ReferenceEquals(observedDocument, document))
			{
				// A dataset switch invalidates scheduled cache work, subscriptions, and viewport bounds.
				if (observedDocument != null) observedDocument.Changed -= OnDocumentChanged;
				observedDocument = document;
				CancelCaching();
				Viewport.Reset();
				if (observedDocument != null) observedDocument.Changed += OnDocumentChanged;
			}

			if (document != null) document.Timeline.Enabled = shell.shellPreferences.timelineEnabled;
			if (document != null)
				document.Timeline.PositionRecordingAvailable = positionHost?.PositionHistoryAvailable == true;
			var available = shell.shellPreferences.timelineEnabled && document != null && document.Timeline.HasBaseline;
			panel.style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
			SyncReservedSpace();
			if (!available)
			{
				StopPlayback();
				HideEventTooltip();
				return;
			}

			SyncPanelBounds();
			ApplyMinimized(shell.shellPreferences.timelineMinimized);
			RestoreViewport();
			RefreshCacheUi();
			var timeline = document.Timeline;
			var cursor = timeline.Cursor;
			var continuous = Continuous;
			var useTimeSlider = continuous || Viewport.IsZoomed;

			// Use continuous coordinates for external history, recorded motion, and zoomed entry history.
			slider.style.display = useTimeSlider ? DisplayStyle.None : DisplayStyle.Flex;
			timeSlider.style.display = useTimeSlider ? DisplayStyle.Flex : DisplayStyle.None;
			optionsRow.style.display = MotionControlsVisible ? DisplayStyle.Flex : DisplayStyle.None;
			recordingToggle.SetValueWithoutNotify(timeline.PositionRecordingEnabled);
			intervalField.SetValueWithoutNotify(timeline.PositionSampleIntervalSeconds);
			intervalField.SetEnabled(timeline.PositionRecordingEnabled);
			syncing = true;
			slider.highValue = timeline.Entries.Count;
			slider.SetValueWithoutNotify(cursor + 1);
			syncing = false;
			if (useTimeSlider)
			{
				syncing = true;
				timeSlider.SetValueWithoutNotify(continuous
													 ? ToProgress(CurrentTime)
													 : Mathf.Clamp01(
														 (float)Viewport.ToVisible(
															 (cursor + 1d) / Math.Max(1, timeline.Entries.Count))));
				syncing = false;
			}

			// Recreate marker elements only when underlying history changes; update selection separately.
			if (markerPositionCount != timeline.PositionFrames.Count ||
				markerTrackRevision != (eventTrack?.Revision ?? -1) ||
				!ReferenceEquals(markerBaseline, timeline.Baseline) || markerEntryCount != timeline.Entries.Count ||
				(timeline.Entries.Count > 0 &&
				 !ReferenceEquals(lastMarkerEntry, timeline.Entries[timeline.Entries.Count - 1])))
				RebuildMarkers(timeline);
			foreach (var marker in marks.Children())
				marker.EnableInClassList("is-selected", marker.userData is TimePoint point &&
														(point.External != null
															? point.External.Id == ActiveEventId
															: string.IsNullOrEmpty(ActiveEventId) &&
															  point.Entry == cursor));
			RefreshMarkerGeometry();
			playButton.Clear();
			playButton.Add(GraphUi.Glyph(playing ? "pause" : "play"));
			playButton.tooltip = playing ? "Pause history playback." : "Play graph history.";
			var simulating = AtPresentEnd && positionHost?.SimulationPaused == false;
			liveButton.Clear();
			liveButton.Add(GraphUi.Glyph(simulating ? "pause" : "play"));
			liveButton.tooltip = simulating
				? "Pause the layout simulation."
				: "Play the layout at the present end of the timeline.";
			var historical = !string.IsNullOrEmpty(ActiveEventId) || timeline.IsHistorical;
			var timeText = (historical ? "HISTORICAL · " : "LIVE · ") + CurrentTime.ToLocalTime()
				.ToString("MMM d, yyyy · HH:mm:ss zzz", CultureInfo.InvariantCulture);
			timeLabel.text = timeText;
			timeLabel.tooltip = string.IsNullOrEmpty(eventTrack?.Error) ? timeText : timeText + "\n" + eventTrack.Error;
			headerSummary.text = timeText;
			headerSummary.tooltip = timeLabel.tooltip;
		}

		/// <summary>Enables or disables timeline recording and presentation.</summary>
		/// <param name="enabled">Whether the timeline is enabled.</param>
		public void SetEnabled(bool enabled)
		{
			shell.shellPreferences.timelineEnabled = enabled;
			if (Document != null) Document.Timeline.Enabled = enabled;
			if (!enabled) StopPlayback();
			shell.SaveShellPreferences();
			Refresh();
		}

		private void OnDocumentChanged()
		{
			Refresh();
		}

		private void OnExternalChanged()
		{
			if (!string.IsNullOrEmpty(eventTrack.Error)) StopPlayback();
			Refresh();
		}

		private float ToProgress(DateTimeOffset time)
		{
			// Map UTC ticks into the visible window, retaining double precision until the final slider value.
			var timeline = Document.Timeline;
			long start = TimelineStartTimeUtc.UtcDateTime.Ticks, end = EndTime.UtcDateTime.Ticks;
			return Mathf.Clamp01((float)Viewport.ToVisible(
									 end <= start
										 ? 1
										 : (time.ToUniversalTime().UtcDateTime.Ticks - start) / (double)(end - start)));
		}

		private DateTimeOffset FromProgress(float progress)
		{
			// Undo viewport zoom before mapping a slider fraction back into the full timestamp range.
			long start = TimelineStartTimeUtc.UtcDateTime.Ticks, end = EndTime.UtcDateTime.Ticks;
			return new DateTimeOffset(start + (long)((end - start) * Viewport.FromVisible(progress)), TimeSpan.Zero);
		}

		private void ZoomTimeline(WheelEvent evt)
		{
			if (!evt.ctrlKey || Document == null || !Document.Timeline.HasBaseline || evt.delta.y == 0) return;
			var activeSlider = Continuous || Viewport.IsZoomed ? (VisualElement)timeSlider : slider;
			var bounds = SliderTravel(activeSlider);
			if (!float.IsFinite(bounds.x) || bounds.y <= 0) return;
			double anchor = Mathf.Clamp01((evt.mousePosition.x - bounds.x) / bounds.y);
			// Keep high precision in the viewport: a small session can follow years of repository history.
			Viewport.Zoom(anchor, Math.Pow(1.2, Math.Max(-20, Math.Min(20, evt.delta.y))));
			if (Viewport.IsZoomed && EndTime > TimelineStartTimeUtc)
				Workspace.SetView(TimeAt(Viewport.Start), TimeAt(Viewport.End));
			else Workspace.SetView(null, null);
			Refresh();
			evt.StopImmediatePropagation();
			evt.PreventDefault();
		}

		private double ProgressAt(DateTimeOffset time)
		{
			// Continuous history uses elapsed time; discrete history spaces semantic entries evenly.
			long start = TimelineStartTimeUtc.UtcDateTime.Ticks, end = EndTime.UtcDateTime.Ticks;
			if (Continuous) return end <= start ? 0 : (time.UtcDateTime.Ticks - start) / (double)(end - start);
			var dates = new[] { Document.Timeline.BaselineTimeUtc }
						.Concat(Document.Timeline.Entries.Select(entry => entry.TimestampUtc)).ToArray();
			if (time <= dates[0]) return 0;
			for (var i = 1; i < dates.Length; i++)
				if (time <= dates[i])
					return (i - 1 + (time - dates[i - 1]).Ticks /
						(double)Math.Max(1, (dates[i] - dates[i - 1]).Ticks)) / Math.Max(1, dates.Length - 1);
			return 1;
		}

		private DateTimeOffset TimeAt(double progress)
		{
			// Invert the timeline mapping, interpolating timestamps between equally spaced semantic entries.
			progress = Math.Max(0, Math.Min(1, progress));
			if (Continuous)
				return TimelineStartTimeUtc.AddTicks((long)((EndTime - TimelineStartTimeUtc).Ticks * progress));
			var timeline = Document.Timeline;
			var index = progress * timeline.Entries.Count;
			var lower = (int)Math.Floor(index);
			var from = lower == 0 ? timeline.BaselineTimeUtc : timeline.Entries[lower - 1].TimestampUtc;
			var to = lower >= timeline.Entries.Count ? from : timeline.Entries[lower].TimestampUtc;
			return from.AddTicks((long)((to - from).Ticks * (index - lower)));
		}

		private void RestoreViewport()
		{
			// Persisted bounds are timestamps, so remap them when the history's overall extent changes.
			if (Workspace.ViewStartUtc.HasValue)
				Viewport.SetRange(ProgressAt(Workspace.ViewStartUtc.Value), ProgressAt(Workspace.ViewEndUtc.Value));
			else Viewport.Reset();
		}

		private DateTimeOffset PointerTime(float x)
		{
			// Account for thumb travel and viewport zoom before interpreting pointer position as time.
			var bounds = SliderTravel(Continuous || Viewport.IsZoomed ? timeSlider : slider);
			return TimeAt(Viewport.FromVisible(bounds.y <= 0 ? 0 : (x - bounds.x) / bounds.y));
		}

		private void BeginRange(PointerDownEvent evt)
		{
			if (!evt.shiftKey || evt.button != 0 || Workspace == null) return;

			// Capture this pointer so range selection continues when dragging outside the timeline.
			StopPlayback();
			HideEventTooltip();
			selectingRange = true;
			rangePointer = evt.pointerId;
			rangeAnchor = PointerTime(evt.position.x);
			Workspace.SelectRange(rangeAnchor, rangeAnchor);
			((VisualElement)evt.currentTarget).CapturePointer(evt.pointerId);
			evt.StopImmediatePropagation();
			evt.PreventDefault();
		}

		private void MoveRange(PointerMoveEvent evt)
		{
			// Only the captured pointer can extend the selection from its original anchor.
			if (!selectingRange || evt.pointerId != rangePointer) return;
			Workspace.SelectRange(rangeAnchor, PointerTime(evt.position.x));
			evt.StopImmediatePropagation();
			evt.PreventDefault();
		}

		private void EndRange(PointerUpEvent evt)
		{
			// Commit the final endpoint before releasing capture and suppressing ordinary scrub behavior.
			if (!selectingRange || evt.pointerId != rangePointer) return;
			Workspace.SelectRange(rangeAnchor, PointerTime(evt.position.x));
			selectingRange = false;
			((VisualElement)evt.currentTarget).ReleasePointer(evt.pointerId);
			evt.StopImmediatePropagation();
			evt.PreventDefault();
		}

		private void DrawRange(MeshGenerationContext context)
		{
			// Clip the selected time span to the visible window before painting the range overlay.
			if (Workspace?.HasSelection != true) return;
			double start = Viewport.ToVisible(ProgressAt(Workspace.SelectionStartUtc.Value)),
				   end = Viewport.ToVisible(ProgressAt(Workspace.SelectionEndUtc.Value));
			if (end < 0 || start > 1) return;
			var bounds = SliderTravel(Continuous || Viewport.IsZoomed ? timeSlider : slider);
			var left = marks.WorldToLocal(new Vector2(bounds.x + bounds.y * (float)Math.Max(0, start), 0)).x;
			var right = marks.WorldToLocal(new Vector2(bounds.x + bounds.y * (float)Math.Min(1, end), 0)).x;
			var painter = context.painter2D;
			painter.fillColor = new Color(.2f, .7f, 1, .3f);
			painter.BeginPath();
			painter.MoveTo(new Vector2(left, 0));
			painter.LineTo(new Vector2(right, 0));
			painter.LineTo(new Vector2(right, marks.contentRect.height));
			painter.LineTo(new Vector2(left, marks.contentRect.height));
			painter.ClosePath();
			painter.Fill();
		}

		private void RefreshCacheUi()
		{
			// Combine external descriptors and already-recorded session samples when summarizing the selection.
			var external = Workspace.SelectedEvents(eventTrack?.Events).Count;
			var session = Document.Timeline.Entries.Count(entry => Workspace.Contains(entry.TimestampUtc)) +
						  Document.Timeline.PositionFrames.Count(frame => Workspace.Contains(frame.TimestampUtc));
			cacheButton.text = Workspace.IsCaching ? "Cancel caching" : "Cache Timeline";
			cacheButton.SetEnabled(Workspace.IsCaching || (Workspace.HasSelection && external + session > 0 &&
														   (external == 0 || eventTrack.Provider != null)));
			cacheStatus.text = Workspace.IsCaching
				? "Caching " + Workspace.Completed + "/" + Workspace.Total
				: Workspace.Error ?? (Workspace.HasSelection
					? external + session + " events selected · " + Workspace.CachedEvents.Count + " cached"
					: "Shift-drag to select a range");
			cacheStatus.tooltip = Workspace.HasSelection
				? Workspace.SelectionStartUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") + " → " +
				  Workspace.SelectionEndUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz")
				: "";
			marks.MarkDirtyRepaint();
		}

		/// <summary>Starts caching the selected event snapshots, or cancels an active cache operation.</summary>
		public void CacheTimeline()
		{
			// Start caching only the events in the selected range that are not already available.
			if (Workspace == null) return;
			if (cacheRoutines.Count > 0)
			{
				CancelCaching();
				Refresh();
				return;
			}

			cacheCancellation = new CancellationTokenSource();
			cacheRoutines.Push(
				Workspace.CacheSelection(Document, eventTrack, eventTrack?.Provider, cacheCancellation.Token));
			cacheSchedule = panel.schedule.Execute(TickCache).Every(16);
		}

		private void TickCache()
		{
			try
			{
				// Drive nested provider coroutines within a small UI budget and pause at ordinary yields.
				var started = Stopwatch.StartNew();
				while (cacheRoutines.Count > 0 && started.ElapsedMilliseconds < 5)
				{
					var next = cacheRoutines.Peek();
					if (!next.MoveNext())
					{
						(cacheRoutines.Pop() as IDisposable)?.Dispose();
						continue;
					}

					if (next.Current is IEnumerator child) cacheRoutines.Push(child);
					else break;
				}

				if (cacheRoutines.Count == 0)
				{
					cacheSchedule?.Pause();
					cacheCancellation?.Dispose();
					cacheCancellation = null;
				}
			}
			catch (Exception error)
			{
				CancelCaching();
				cacheStatus.text = "Cache failed: " + error.Message;
				Debug.LogException(error);
			}
		}

		private void CancelCaching()
		{
			// Dispose the full coroutine stack so iterator finally blocks clear the workspace's busy state.
			cacheCancellation?.Cancel();
			cacheSchedule?.Pause();
			while (cacheRoutines.Count > 0) (cacheRoutines.Pop() as IDisposable)?.Dispose();
			cacheCancellation?.Dispose();
			cacheCancellation = null;
		}

		private static Vector2 SliderTravel(VisualElement activeSlider)
		{
			// Return the thumb-center origin and usable travel after subtracting thumb width and margins.
			var thumb = activeSlider.Q<VisualElement>(className: "unity-base-slider__dragger");
			var track = activeSlider.Q<VisualElement>(className: "unity-base-slider__drag-container") ??
						thumb?.parent ?? activeSlider;
			var bounds = track.worldBound;
			var thumbWidth = thumb == null ? 0 : thumb.resolvedStyle.width;
			if (!float.IsFinite(thumbWidth) || thumbWidth < 0)
				thumbWidth = thumb == null ? 0 : Mathf.Max(0, thumb.worldBound.width);
			var marginLeft = thumb == null || !float.IsFinite(thumb.resolvedStyle.marginLeft)
				? 0
				: thumb.resolvedStyle.marginLeft;
			var marginRight = thumb == null || !float.IsFinite(thumb.resolvedStyle.marginRight)
				? 0
				: thumb.resolvedStyle.marginRight;
			return new Vector2(bounds.xMin + marginLeft + thumbWidth * .5f,
							   Mathf.Max(0, bounds.width - thumbWidth - marginLeft - marginRight));
		}

		private void Seek(int index)
		{
			// Manual entry scrubbing stops playback and keeps the requested cursor inside recorded history.
			if (!shell.shellPreferences.timelineEnabled || Document == null || !Document.Timeline.HasBaseline) return;
			StopPlayback();
			host.SeekTimeline(Mathf.Clamp(index, -1, Document.Timeline.Entries.Count - 1));
			Refresh();
		}

		private void SeekContinuous(DateTimeOffset timeUtc, bool stopPlayback = true)
		{
			if (!shell.shellPreferences.timelineEnabled || Document == null || !Document.Timeline.HasBaseline ||
				(positionHost == null && eventHost == null)) return;
			if (HasExternal)
			{
				// External descriptors are snapshots; session points can still seek interpolated motion by time.
				if (stopPlayback) StopPlayback();
				var point = points.LastOrDefault(item => item.Time <= timeUtc) ?? points.First();
				if (point.External != null)
				{
					SelectPoint(point, stopPlayback);
				}
				else
				{
					eventHost.SeekSessionTime(timeUtc > Document.Timeline.LatestTimeUtc
												  ? Document.Timeline.LatestTimeUtc
												  : timeUtc);
					Refresh();
				}

				return;
			}

			if (stopPlayback) StopPlayback();
			var time = timeUtc < TimelineStartTimeUtc ? TimelineStartTimeUtc :
				timeUtc > Document.Timeline.LatestTimeUtc ? Document.Timeline.LatestTimeUtc : timeUtc;
			positionHost.SeekTimelineTime(time);
			Refresh();
		}

		private void ToggleLive()
		{
			// Pause an already-live simulation, or return from history and resume simulation at the present.
			if (Document == null) return;
			StopPlayback();
			var simulating = AtPresentEnd && positionHost != null && !positionHost.SimulationPaused;
			if (simulating)
			{
				positionHost.SetSimulationPaused(true);
				Refresh();
				return;
			}

			if (eventHost != null) eventHost.ReturnToLive();
			else if (Continuous) SeekContinuous(Document.Timeline.LatestTimeUtc);
			else Seek(Document.Timeline.Entries.Count - 1);
			positionHost?.SetSimulationPaused(false);
			Refresh();
		}

		private void Step(int direction)
		{
			// Step through external points, motion timestamps, or semantic entries according to available history.
			if (Document == null) return;
			if (HasExternal)
			{
				if (points.Count == 0) return;
				StopPlayback();
				var index = CurrentPointIndex();
				SelectPoint(points[Mathf.Clamp(index + direction, 0, points.Count - 1)], true);
				return;
			}

			if (Continuous)
			{
				var points = new SortedSet<DateTimeOffset> { TimelineStartTimeUtc, Document.Timeline.LatestTimeUtc };
				foreach (var entry in Document.Timeline.Entries) points.Add(entry.TimestampUtc);
				foreach (var frame in Document.Timeline.PositionFrames) points.Add(frame.TimestampUtc);
				var target = direction < 0
					? points.Where(time => time < Document.Timeline.CurrentTimeUtc).DefaultIfEmpty(TimelineStartTimeUtc)
							.Last()
					: points.Where(time => time > Document.Timeline.CurrentTimeUtc)
							.DefaultIfEmpty(Document.Timeline.LatestTimeUtc).First();
				RevealTime(target);
				SeekContinuous(target);
				return;
			}

			var next = Mathf.Clamp(Document.Timeline.Cursor + direction, -1, Document.Timeline.Entries.Count - 1);
			RevealTime(next < 0 ? Document.Timeline.BaselineTimeUtc : Document.Timeline.Entries[next].TimestampUtc);
			Seek(next);
		}

		private void TogglePlayback()
		{
			// Restart completed playback at the beginning and retain a wall-clock origin for continuous motion.
			if (Document == null || (Document.Timeline.Entries.Count == 0 && !Continuous) ||
				!shell.shellPreferences.timelineEnabled) return;
			if (HasExternal)
			{
				if (playing)
				{
					StopPlayback();
					Refresh();
					return;
				}

				if (CurrentPointIndex() >= points.Count - 1) SelectPoint(points[0], false);
				playing = true;
				Refresh();
				ScheduleNext();
				return;
			}

			if (Continuous)
			{
				if (!playing && Document.Timeline.CurrentTimeUtc >= Document.Timeline.LatestTimeUtc)
					SeekContinuous(TimelineStartTimeUtc);
				if (!playing)
				{
					playbackFrom = Document.Timeline.CurrentTimeUtc;
					playbackStartedAt = DateTimeOffset.UtcNow;
				}

				playing = !playing;
				Refresh();
				if (playing) ScheduleNext();
				return;
			}

			if (!playing && Document.Timeline.Cursor >= Document.Timeline.Entries.Count - 1) Seek(-1);
			playing = !playing;
			Refresh();
			if (playing) ScheduleNext();
		}

		private void ScheduleNext()
		{
			// Schedule one tick at a time so pause, asynchronous loads, and history exhaustion can stop playback.
			playback?.Pause();
			playback = panel.schedule.Execute(() =>
			{
				if (!playing || Document == null) return;
				if (HasExternal)
				{
					if (eventTrack.PendingId != null)
					{
						// Wait for the host to confirm the pending snapshot before advancing to another event.
						ScheduleNext();
						return;
					}

					var index = CurrentPointIndex();
					if (index >= points.Count - 1 && CurrentTime >= EndTime)
					{
						StopPlayback();
						Refresh();
						return;
					}

					var frames = Document.Timeline.PositionFrames;
					var motion = eventTrack.SelectedId == null && positionHost?.PositionHistoryAvailable == true &&
								 frames.Count > 1 &&
								 CurrentTime >= frames[0].TimestampUtc &&
								 CurrentTime < frames[frames.Count - 1].TimestampUtc;
					if (motion)
					{
						var target = CurrentTime.AddMilliseconds(33);
						if (index + 1 < points.Count && target >= points[index + 1].Time)
							SelectPoint(points[index + 1], false);
						else
							eventHost.SeekSessionTime(target > frames[frames.Count - 1].TimestampUtc
														  ? frames[frames.Count - 1].TimestampUtc
														  : target);
					}
					else if (index + 1 < points.Count)
					{
						SelectPoint(points[index + 1], false);
					}
					else
					{
						eventHost.SeekSessionTime(Document.Timeline.LatestTimeUtc);
					}

					if (playing) ScheduleNext();
					return;
				}

				if (Continuous)
				{
					var target = playbackFrom + (DateTimeOffset.UtcNow - playbackStartedAt);
					if (target >= Document.Timeline.LatestTimeUtc)
					{
						SeekContinuous(Document.Timeline.LatestTimeUtc, false);
						StopPlayback();
						Refresh();
						return;
					}

					SeekContinuous(target, false);
					if (playing) ScheduleNext();
					return;
				}

				if (Document.Timeline.Cursor >= Document.Timeline.Entries.Count - 1)
				{
					StopPlayback();
					Refresh();
					return;
				}

				host.SeekTimeline(Document.Timeline.Cursor + 1);
				Refresh();
				if (playing) ScheduleNext();
			}).StartingIn(HasExternal
							  ? eventTrack.SelectedId == null && Document.Timeline.HasPositionHistory ? 33 : 650
							  : Continuous
								  ? 33
								  : 650);
		}

		private void StopPlayback()
		{
			playing = false;
			playback?.Pause();
			playback = null;
		}

		private int CurrentPointIndex()
		{
			// Match external states by identity, since several events may share the same timestamp.
			var id = ActiveEventId;
			if (!string.IsNullOrEmpty(id))
			{
				var found = points.FindIndex(point => point.External?.Id == id);
				if (found >= 0) return found;
			}

			return points.FindLastIndex(point => point.Time <= CurrentTime && point.External == null);
		}

		private void SelectPoint(TimePoint point, bool stopPlayback)
		{
			// Dispatch snapshot selection or session seeking through the matching host capability.
			if (stopPlayback)
			{
				StopPlayback();
				RevealTime(point.Time);
			}

			if (point.External != null) eventTrack.RequestSelection(point.External.Id, stopPlayback);
			else if (eventHost != null) eventHost.SeekSessionTime(point.Time);
			else host.SeekTimeline(point.Entry);
			Refresh();
		}

		private void RevealTime(DateTimeOffset time)
		{
			// Pan an existing zoom window to contain a stepped-to point without changing its span.
			if (Document == null || !Document.Timeline.HasBaseline || !Viewport.IsZoomed) return;
			var progress = ProgressAt(time);
			var visible = Viewport.ToVisible(progress);
			if (visible >= -.001 && visible <= 1.001) return;
			var span = Viewport.End - Viewport.Start;
			var start = Math.Max(0, Math.Min(1 - span, progress - span * .5));
			Viewport.SetRange(start, start + span);
			var viewStart = TimeAt(Viewport.Start);
			var viewEnd = TimeAt(Viewport.End);
			if (viewEnd > viewStart) Workspace.SetView(viewStart, viewEnd);
		}

		private void RebuildMarkers(GraphTimeline timeline)
		{
			// Merge external descriptors, semantic edits, and session motion boundaries into stable time order.
			HideEventTooltip();
			marks.Clear();
			eventDescriptions.Clear();
			points.Clear();
			eventDescriptions.AddRange(BuildTimelineDetails(timeline));
			if (HasExternal)
			{
				points.AddRange(eventTrack.Events.Select(item => new TimePoint
															 { Time = item.TimestampUtc, External = item }));
				points.Add(new TimePoint { Time = timeline.BaselineTimeUtc });
			}

			for (var i = 0; i < timeline.Entries.Count; i++)
				points.Add(new TimePoint { Time = timeline.Entries[i].TimestampUtc, Entry = i });
			if (HasExternal && timeline.HasPositionHistory)
			{
				points.Add(new TimePoint
				{
					Time = timeline.PositionFrames[0].TimestampUtc,
					Entry = timeline.EntryIndexAt(timeline.PositionFrames[0].TimestampUtc)
				});
				points.Add(new TimePoint
				{
					Time = timeline.LatestTimeUtc, Entry = timeline.EntryIndexAt(timeline.LatestTimeUtc)
				});
			}

			var ordered = points.OrderBy(point => point.Time).ToArray();
			points.Clear();
			points.AddRange(ordered);
			for (var i = 0; i < points.Count; i++)
			{
				// Bind each marker to its own point and independent hover details.
				var point = points[i];
				var marker = GraphUi.Element("timeline-marker");
				marker.name = point.External == null
					? "timeline-marker-" + point.Entry
					: "timeline-event-" + point.External.Id;
				marker.userData = point;
				marker.tooltip = point.External?.Tooltip ?? PointDetails(point);
				marker.pickingMode = PickingMode.Position;
				marker.RegisterCallback<PointerEnterEvent>(_ => ShowEventTooltip(marker));
				marker.RegisterCallback<PointerLeaveEvent>(_ =>
				{
					if (hoveredMarker == marker) HideEventTooltip();
				});
				marker.RegisterCallback<PointerDownEvent>(evt =>
				{
					HideEventTooltip();
					evt.StopPropagation();
				});
				marker.RegisterCallback<ClickEvent>(_ => SelectPoint(point, true));
				marks.Add(marker);
			}

			markerBaseline = timeline.Baseline;
			markerEntryCount = timeline.Entries.Count;
			markerPositionCount = timeline.PositionFrames.Count;
			markerTrackRevision = eventTrack?.Revision ?? -1;
			lastMarkerEntry = timeline.Entries.Count == 0 ? null : timeline.Entries[timeline.Entries.Count - 1];
		}

		private string PointDetails(TimePoint point)
		{
			return point.External != null
				? point.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") + " · " + point.External.Id + " · " +
				  point.External.Details
				: point.Entry < 0
					? "Session baseline · " + point.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz")
					: eventDescriptions[point.Entry];
		}

		private void ShowEventTooltip(VisualElement marker)
		{
			// Replace the prior tooltip only when this marker supplies nonempty hover text.
			HideEventTooltip();
			if (string.IsNullOrWhiteSpace(marker.tooltip)) return;
			hoveredMarker = marker;
			eventTooltipText.text = marker.tooltip;
			eventTooltip.style.display = DisplayStyle.Flex;
			eventTooltip.BringToFront();
			PositionEventTooltip();
		}

		private void HideEventTooltip()
		{
			hoveredMarker = null;
			if (eventTooltip != null) eventTooltip.style.display = DisplayStyle.None;
		}

		private void PositionEventTooltip()
		{
			// Center the tooltip above its marker and clamp it inside the root's horizontal bounds.
			if (hoveredMarker == null || eventTooltip.panel == null) return;
			var anchor = shell.root.WorldToLocal(hoveredMarker.worldBound.center);
			var rootWidth = shell.root.resolvedStyle.width;
			var width = Mathf.Min(360, Mathf.Max(0, rootWidth - 16));
			eventTooltip.style.maxWidth = width;
			eventTooltip.style.width = width;
			var height = eventTooltip.resolvedStyle.height;
			eventTooltip.style.left = Mathf.Clamp(anchor.x - width * .5f, 8, Mathf.Max(8, rootWidth - width - 8));
			eventTooltip.style.top = Mathf.Max(8, anchor.y - (float.IsFinite(height) ? height : 0) - 12);
		}

		private void RefreshMarkerGeometry()
		{
			// Align custom playhead and marker overlays with the active slider's resolved thumb travel.
			if (playhead == null || currentHighlight == null) return;
			var normalized = SyncThumb();
			var activeSlider = Continuous || Viewport.IsZoomed ? (VisualElement)timeSlider : slider;
			if (float.IsFinite(normalized) && activeSlider.panel != null) PlacePlayhead(activeSlider, normalized);
			else playhead.style.display = DisplayStyle.None;
			if (marks.panel == null || activeSlider.panel == null || marks.childCount == 0)
			{
				currentHighlight.style.display = DisplayStyle.None;
				return;
			}

			var bounds = SliderTravel(activeSlider);
			long startTicks = TimelineStartTimeUtc.UtcDateTime.Ticks, endTicks = EndTime.UtcDateTime.Ticks;
			var selectedCenter = float.NaN;
			for (var i = 0; i < marks.childCount; i++)
			{
				var progress = Continuous
					? endTicks <= startTicks
						? 1
						: (points[i].Time.UtcDateTime.Ticks - startTicks) / (double)(endTicks - startTicks)
					: (points[i].Entry + 1d) / Math.Max(1, Document.Timeline.Entries.Count);
				var fraction = Viewport.ToVisible(progress);
				marks[i].style.display = fraction >= 0 && fraction <= 1 ? DisplayStyle.Flex : DisplayStyle.None;
				if (hoveredMarker == marks[i] && marks[i].style.display.value == DisplayStyle.None) HideEventTooltip();
				var centerWorld = bounds.x + bounds.y * (float)fraction;
				var localX = marks.WorldToLocal(new Vector2(centerWorld, marks.worldBound.center.y)).x;
				marks[i].style.left = localX - 2f;
				marks[i].style.top = StyleKeyword.Null;
				marks[i].style.height = StyleKeyword.Null;
				marks[i].style.width = StyleKeyword.Null;
				if (marks[i].ClassListContains("is-selected") && fraction >= 0 && fraction <= 1)
					selectedCenter = centerWorld;
				marks[i].EnableInClassList("in-cache-range", Workspace.Contains(points[i].Time));
				marks[i].EnableInClassList("is-cached",
										   points[i].External != null && Workspace.IsCached(points[i].External.Id));
			}

			PlaceCurrentHighlight(selectedCenter, activeSlider);
			marks.MarkDirtyRepaint();
			PositionEventTooltip();
		}

		private void PlaceCurrentHighlight(float centerWorldX, VisualElement strip)
		{
			// Span the selected marker's highlight from the slider through the event strip when geometry is valid.
			var stripBounds = strip.worldBound;
			var height = marks.worldBound.yMax - stripBounds.yMin;
			if (!float.IsFinite(centerWorldX) || strip.resolvedStyle.display == DisplayStyle.None ||
				stripBounds.height <= 4f || !float.IsFinite(height) || height <= 8f)
			{
				currentHighlight.style.display = DisplayStyle.None;
				return;
			}

			const float width = 6f;
			var origin = body.WorldToLocal(new Vector2(centerWorldX - width * .5f, stripBounds.yMin));
			currentHighlight.style.left = origin.x;
			currentHighlight.style.top = origin.y;
			currentHighlight.style.width = width;
			currentHighlight.style.height = height;
			currentHighlight.style.display = DisplayStyle.Flex;
		}

		private void PlacePlayhead(VisualElement strip, float normalized)
		{
			// Follow the resolved slider thumb, falling back to a fixed height before its layout settles.
			var drag = strip.Q(className: "unity-base-slider__dragger");
			var travel = SliderTravel(strip);
			var width = drag == null ? 3f : drag.resolvedStyle.width;
			if (!float.IsFinite(width) || width <= 0) width = 3f;
			if (drag == null || travel.y <= 0 || !float.IsFinite(normalized))
			{
				playhead.style.display = DisplayStyle.None;
				return;
			}

			var dragBounds = drag.worldBound;
			var top = dragBounds.yMin;
			var height = dragBounds.height;
			if (!float.IsFinite(top) || !float.IsFinite(height) || height <= 1f)
			{
				top = strip.worldBound.center.y - 7f;
				height = 14f;
			}

			var origin = body.WorldToLocal(new Vector2(travel.x + travel.y * normalized - width * .5f, top));
			playhead.style.left = origin.x;
			playhead.style.top = origin.y;
			playhead.style.width = width;
			playhead.style.height = height;
			playhead.style.display = DisplayStyle.Flex;
			playhead.BringToFront();
		}

		private float SyncThumb()
		{
			// Update the continuous value without triggering seek callbacks, then correct the thumb transform.
			if (Document == null || !Document.Timeline.HasBaseline) return float.NaN;
			var timeBased = Continuous || Viewport.IsZoomed;
			var active = timeBased ? (VisualElement)timeSlider : slider;
			if (active.panel == null) return float.NaN;
			float normalized;
			if (timeBased)
			{
				normalized = Mathf.Clamp01((float)Viewport.ToVisible(ProgressAt(CurrentTime)));
				if (Mathf.Abs(timeSlider.value - normalized) > .0001f)
				{
					syncing = true;
					timeSlider.SetValueWithoutNotify(normalized);
					syncing = false;
				}
			}
			else
			{
				float high = slider.highValue;
				normalized = high <= 0 ? 0 : Mathf.Clamp01(slider.value / high);
			}

			var drag = active.Q(className: "unity-base-slider__dragger");
			var container = active.Q(className: "unity-base-slider__drag-container");
			if (drag == null || container == null) return normalized;
			var thumb = drag.resolvedStyle.width;
			var marginLeft = float.IsFinite(drag.resolvedStyle.marginLeft) ? drag.resolvedStyle.marginLeft : 0f;
			var marginRight = float.IsFinite(drag.resolvedStyle.marginRight) ? drag.resolvedStyle.marginRight : 0f;
			var travel = container.layout.width - thumb - marginLeft - marginRight;
			if (!float.IsFinite(travel) || travel <= 0 || !float.IsFinite(thumb) || thumb <= 0) return normalized;
			var position = new Vector3(normalized * travel, 0, 0);
			if ((drag.transform.position - position).sqrMagnitude > .25f) drag.transform.position = position;
			SuppressSliderChrome(active);
			return normalized;
		}

		private static void SuppressSliderChrome(VisualElement field)
		{
			// Hide the built-in focus border after interactions that would otherwise restore slider chrome.
			var border = field.Q(className: "unity-base-slider__dragger-border");
			if (border == null) return;

			void Hide()
			{
				border.pickingMode = PickingMode.Ignore;
				border.style.display = DisplayStyle.None;
				border.style.visibility = Visibility.Hidden;
				border.style.opacity = 0;
				border.style.width = 0;
				border.style.height = 0;
			}

			Hide();
			if (border.ClassListContains("timeline-slider-chrome-hidden")) return;
			border.AddToClassList("timeline-slider-chrome-hidden");
			field.RegisterCallback<PointerDownEvent>(_ => Hide(), TrickleDown.TrickleDown);
			field.RegisterCallback<PointerMoveEvent>(_ => Hide());
			field.RegisterCallback<FocusInEvent>(_ => Hide());
		}

		private static List<string> BuildTimelineDetails(GraphTimeline timeline)
		{
			// Replay node labels while describing each delta so relationship details use names from that moment.
			var result = new List<string>(timeline.Entries.Count);
			var labels =
				timeline.Baseline.Nodes.ToDictionary(node => node.Id, node => node.Label, StringComparer.Ordinal);
			foreach (var entry in timeline.Entries)
			{
				var change = entry.Change;
				var details = new List<string>();
				foreach (var edge in
						 change.EdgesBefore.Where(item => !change.EdgesAfter.Any(next => next.Id == item.Id)))
					details.Add("Removed connection from " + NodeName(labels, edge.From) + " to " +
								NodeName(labels, edge.To) + " (" + ReadableName(edge.TypeId) + ").");
				foreach (var node in change.NodesAfter)
				{
					// Compare scalar metadata and sources as well as identity labels for updated nodes.
					var before = change.NodesBefore.FirstOrDefault(item => item.Id == node.Id);
					if (before == null)
					{
						details.Add("Added " + ReadableName(node.TypeId) + " ‘" + node.Label + "’." +
									MetadataSentence(node.Properties));
					}
					else
					{
						var fields = new List<string>();
						if (before.Label != node.Label)
							fields.Add("name changed from ‘" + before.Label + "’ to ‘" + node.Label + "’");
						if (before.TypeId != node.TypeId)
							fields.Add("type changed from " + ReadableName(before.TypeId) + " to " +
									   ReadableName(node.TypeId));
						fields.AddRange(before.Properties.Keys.Union(node.Properties.Keys, StringComparer.Ordinal)
											  .Where(key => !before.Properties.TryGetValue(key, out var oldValue) ||
															!node.Properties.TryGetValue(key, out var newValue) ||
															!oldValue.Equals(newValue))
											  .Select(key => ReadableName(key) + " changed from “" +
															 Value(before.Properties, key) + "” to “" +
															 Value(node.Properties, key) + "”"));
						if (!before.Sources.SequenceEqual(node.Sources, StringComparer.Ordinal))
							fields.Add("sources changed");
						if (fields.Count > 0)
							details.Add("Updated ‘" + node.Label + "’: " + string.Join("; ", fields) + ".");
					}
				}

				foreach (var node in
						 change.NodesBefore.Where(item => !change.NodesAfter.Any(next => next.Id == item.Id)))
					details.Add("Removed " + ReadableName(node.TypeId) + " ‘" + node.Label + "’.");
				foreach (var node in change.NodesAfter) labels[node.Id] = node.Label;
				foreach (var node in
						 change.NodesBefore.Where(item => !change.NodesAfter.Any(next => next.Id == item.Id)))
					labels.Remove(node.Id);
				foreach (var edge in change.EdgesAfter)
				{
					// Describe new connections or the changed evidence and endpoints of existing connections.
					var before = change.EdgesBefore.FirstOrDefault(item => item.Id == edge.Id);
					if (before == null)
					{
						details.Add("Added connection from " + NodeName(labels, edge.From) + " to " +
									NodeName(labels, edge.To) + " (“ + ReadableName(edge.TypeId) + ”)." +
									MetadataSentence(edge.Properties));
					}
					else
					{
						var fields = new List<string>();
						if (before.From != edge.From || before.To != edge.To)
							fields.Add("endpoints changed to " + NodeName(labels, edge.From) + " and " +
									   NodeName(labels, edge.To));
						if (before.TypeId != edge.TypeId) fields.Add("type changed to " + ReadableName(edge.TypeId));
						if (before.Status != edge.Status)
							fields.Add("status changed from " + ReadableName(before.Status) + " to " +
									   ReadableName(edge.Status));
						if (before.Confidence != edge.Confidence)
							fields.Add("confidence changed from " +
									   (before.Confidence?.ToString(CultureInfo.InvariantCulture) ?? "none") + " to " +
									   (edge.Confidence?.ToString(CultureInfo.InvariantCulture) ?? "none"));
						fields.AddRange(before.Properties.Keys.Union(edge.Properties.Keys, StringComparer.Ordinal)
											  .Where(key => !before.Properties.TryGetValue(key, out var oldValue) ||
															!edge.Properties.TryGetValue(key, out var newValue) ||
															!oldValue.Equals(newValue))
											  .Select(key => ReadableName(key) + " changed from “" +
															 Value(before.Properties, key) + "” to “" +
															 Value(edge.Properties, key) + "”"));
						if (!before.Sources.SequenceEqual(edge.Sources, StringComparer.Ordinal))
							fields.Add("sources changed");
						if (fields.Count > 0)
							details.Add("Updated connection between " + NodeName(labels, edge.From) + " and " +
										NodeName(labels, edge.To) + ": " + string.Join("; ", fields) + ".");
					}
				}

				if (change.DisplayNameBefore != change.DisplayNameAfter)
					details.Add("Renamed the graph from ‘" + change.DisplayNameBefore + "’ to ‘" +
								change.DisplayNameAfter + "’.");
				var text = string.Join(" ", details);
				result.Add(entry.TimestampUtc.ToLocalTime().ToString("MMM d, HH:mm:ss", CultureInfo.InvariantCulture) +
						   " · " + (text.Length == 0 ? "The graph changed." : text));
			}

			return result;
		}

		private static string NodeName(IReadOnlyDictionary<string, string> labels, string id)
		{
			return labels.TryGetValue(id, out var label) && !string.IsNullOrWhiteSpace(label) ? label : id;
		}

		private static string ReadableName(string value)
		{
			return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
				(value ?? "").Replace('/', ' ').Replace('_', ' ').Replace('-', ' ').Trim());
		}

		private static string MetadataSentence(IReadOnlyDictionary<string, GraphValue> values)
		{
			return values.Count == 0
				? ""
				: " Details: " +
				  string.Join(
					  ", ",
					  values.OrderBy(pair => pair.Key, StringComparer.Ordinal)
							.Select(pair => ReadableName(pair.Key) + " is “" +
											(pair.Value.Type == GraphValueType.Null ? "null" : pair.Value.ToString()) +
											"”")) + ".";
		}

		private static string Value(IReadOnlyDictionary<string, GraphValue> values, string key)
		{
			return values.TryGetValue(key, out var value)
				? value.Type == GraphValueType.Null ? "null" : value.ToString()
				: "no value";
		}

		private sealed class TimePoint
		{
			public int Entry = -1;
			public GraphTimelineEvent External;
			public DateTimeOffset Time;
		}
	}
}