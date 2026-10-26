using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Cashg.GraphExplorer
{
	/// <summary>Defines graph timeline event shell host.</summary>
	public interface IGraphTimelineEventShellHost : IGraphTimelineShellHost
	{
		/// <summary>Gets the external event track exposed by the host.</summary>
		GraphTimelineEventTrack TimelineEvents { get; }
		/// <summary>Seeks to an external event timestamp.</summary>
		/// <param name="timeUtc">Timestamp to seek to.</param>
		void SeekSessionTime(DateTimeOffset timeUtc);
		/// <summary>Returns the host to its live session state.</summary>
		void ReturnToLive();
	}

	/// <summary>Represents graph shell drawer.</summary>
	public sealed class GraphShellDrawer
	{
		/// <summary>Creates a custom shell drawer.</summary>
		/// <param name="id">Stable drawer identifier.</param>
		/// <param name="title">Drawer title.</param>
		/// <param name="icon">Drawer icon identifier.</param>
		/// <param name="content">Drawer content element.</param>
		public GraphShellDrawer(string id, string title, string icon, VisualElement content)
		{
			if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Drawer ID is required.", nameof(id));
			Id = id;
			Title = title ?? id;
			Icon = icon ?? "list";
			Content = content ?? throw new ArgumentNullException(nameof(content));
		}

		/// <summary>Gets the stable drawer identifier.</summary>
		public string Id { get; }
		/// <summary>Gets the drawer title.</summary>
		public string Title { get; }
		/// <summary>Gets the drawer icon identifier.</summary>
		public string Icon { get; }
		/// <summary>Gets the drawer content element.</summary>
		public VisualElement Content { get; }
	}

	/// <summary>Defines graph custom drawer shell host.</summary>
	public interface IGraphCustomDrawerShellHost
	{
		/// <summary>Gets custom drawers to add to the shell.</summary>
		IReadOnlyList<GraphShellDrawer> CustomDrawers { get; }
	}

	/// <summary>Represents graph shell preferences.</summary>
	[Serializable]
	public class GraphShellPreferences
	{
		/// <summary>Gets or sets the preference data version.</summary>
		public int version = 3;
		/// <summary>Gets or sets the shell UI scale.</summary>
		public float uiScale = 1;
		/// <summary>Gets or sets the dock width in pixels.</summary>
		public float dockWidth = 360;
		/// <summary>Gets or sets whether reduced motion is enabled.</summary>
		public bool reducedMotion;
		/// <summary>Gets or sets whether the timeline is enabled.</summary>
		public bool timelineEnabled = true;
		/// <summary>Gets or sets whether the timeline is minimized.</summary>
		public bool timelineMinimized;
	}

	/// <summary>Defines graph timeline shell host.</summary>
	public interface IGraphTimelineShellHost
	{
		/// <summary>Gets the document whose event history is presented.</summary>
		GraphDocument TimelineDocument { get; }
		/// <summary>Seeks to an event-history entry.</summary>
		/// <param name="entryIndex">Timeline entry index.</param>
		void SeekTimeline(int entryIndex);
	}

	/// <summary>Defines graph position history shell host.</summary>
	public interface IGraphPositionHistoryShellHost : IGraphTimelineShellHost
	{
		/// <summary>Gets whether position history can be recorded by the host.</summary>
		bool PositionHistoryAvailable { get; }
		/// <summary>Gets whether the host's simulation is paused.</summary>
		bool SimulationPaused { get; }
		/// <summary>Seeks event and position history to a timestamp.</summary>
		/// <param name="timeUtc">Timestamp to seek to.</param>
		void SeekTimelineTime(DateTimeOffset timeUtc);
		/// <summary>Pauses or resumes the host simulation.</summary>
		/// <param name="paused">Whether simulation should be paused.</param>
		void SetSimulationPaused(bool paused);
	}

	/// <summary>Defines graph shell host.</summary>
	public interface IGraphShellHost
	{
		/// <summary>Gets whether action controls should be available.</summary>
		bool ActionsAvailable { get; }
		/// <summary>Gets the persisted shell preferences.</summary>
		GraphShellPreferences shellPreferences { get; }
		/// <summary>Gets or sets whether preferences have loaded.</summary>
		bool shellPreferencesLoaded { get; set; }
		/// <summary>Gets or sets whether the shell has been built.</summary>
		bool shellReady { get; set; }
		/// <summary>Gets or sets whether the inspector drawer is open.</summary>
		bool shellInspectorOpen { get; set; }
		/// <summary>Gets or sets whether the shell is being resized.</summary>
		bool shellResizing { get; set; }
		/// <summary>Gets or sets whether the workspace menu is open.</summary>
		bool shellWorkspaceOpen { get; set; }
		/// <summary>Gets or sets whether tool controls are being updated.</summary>
		bool shellUpdatingTools { get; set; }
		/// <summary>Gets or sets whether the command panel is open.</summary>
		bool commandPanelOpen { get; set; }
		/// <summary>Gets or sets whether the command panel floats over the graph.</summary>
		bool commandFloating { get; set; }
		/// <summary>Gets or sets whether reduced motion is active.</summary>
		bool reducedMotion { get; set; }
		/// <summary>Gets or sets the currently open shell drawer identifier.</summary>
		string shellDrawer { get; set; }
		/// <summary>Gets or sets the search query waiting to be applied.</summary>
		string shellPendingSearch { get; set; }
		/// <summary>Gets or sets the pointer frame consumed by shell controls.</summary>
		int shellConsumedPointerFrame { get; set; }
		/// <summary>Gets or sets the shell root element.</summary>
		VisualElement root { get; set; }
		/// <summary>Gets or sets the left sidebar element.</summary>
		VisualElement sidebar { get; set; }
		/// <summary>Gets or sets the right column element.</summary>
		VisualElement rightColumn { get; set; }
		/// <summary>Gets or sets the inspector element.</summary>
		VisualElement inspector { get; set; }
		/// <summary>Gets or sets the command panel element.</summary>
		VisualElement commandPanel { get; set; }
		/// <summary>Gets or sets the command belt element.</summary>
		VisualElement commandBelt { get; set; }
		/// <summary>Gets or sets the hotbar element.</summary>
		VisualElement hotbar { get; set; }
		/// <summary>Gets or sets the shell header element.</summary>
		VisualElement shellHeader { get; set; }
		/// <summary>Gets or sets the shell search host element.</summary>
		VisualElement shellSearchHost { get; set; }
		/// <summary>Gets or sets the navigation rail element.</summary>
		VisualElement shellRail { get; set; }
		/// <summary>Gets or sets the dock header element.</summary>
		VisualElement shellDockHeader { get; set; }
		/// <summary>Gets or sets the graph counts element.</summary>
		VisualElement shellCounts { get; set; }
		/// <summary>Gets or sets the source drawer body.</summary>
		VisualElement shellSourceBody { get; set; }
		/// <summary>Gets or sets the filters drawer body.</summary>
		VisualElement shellFiltersBody { get; set; }
		/// <summary>Gets or sets the view drawer body.</summary>
		VisualElement shellViewBody { get; set; }
		/// <summary>Gets or sets the browse drawer body.</summary>
		VisualElement shellBrowseBody { get; set; }
		/// <summary>Gets or sets the favorites drawer body.</summary>
		VisualElement shellFavoritesBody { get; set; }
		/// <summary>Gets or sets the workspace menu element.</summary>
		VisualElement shellWorkspaceMenu { get; set; }
		/// <summary>Gets or sets the shell tooltip element.</summary>
		VisualElement tooltip { get; set; }
		/// <summary>Gets or sets the general UI tip element.</summary>
		VisualElement uiTip { get; set; }
		/// <summary>Gets or sets the hotbar tip element.</summary>
		VisualElement hotbarTip { get; set; }
		/// <summary>Gets or sets the loading overlay element.</summary>
		VisualElement loadingOverlay { get; set; }
		/// <summary>Gets or sets the active drawer title label.</summary>
		Label shellDrawerTitle { get; set; }
		/// <summary>Gets or sets the provider connection status label.</summary>
		Label shellSourceStatus { get; set; }
		/// <summary>Gets or sets the queue count badge.</summary>
		Label shellQueueBadge { get; set; }
		/// <summary>Gets or sets the current task status label.</summary>
		Label taskNowLabel { get; set; }
		/// <summary>Gets or sets the inspector tab button.</summary>
		Button shellInspectTab { get; set; }
		/// <summary>Gets or sets the queue tab button.</summary>
		Button shellQueueTab { get; set; }
		/// <summary>Gets or sets the workspace menu button.</summary>
		Button shellWorkspaceButton { get; set; }
		/// <summary>Gets or sets the UI scale selector.</summary>
		DropdownField shellScaleField { get; set; }
		/// <summary>Gets or sets the active tool selector.</summary>
		DropdownField shellToolField { get; set; }
		/// <summary>Gets or sets the favorites result list.</summary>
		ListView favoritesList { get; set; }
		/// <summary>Gets or sets the search result list.</summary>
		ListView resultList { get; set; }
		/// <summary>Gets or sets the node search field.</summary>
		TextField nodeSearchField { get; set; }
		/// <summary>Gets or sets runtime panel settings for the shell.</summary>
		PanelSettings runtimePanelSettings { get; set; }
		/// <summary>Gets the navigation rail buttons keyed by drawer identifier.</summary>
		Dictionary<string, Button> shellRailButtons { get; }
		/// <summary>Gets the available tool labels.</summary>
		List<string> shellToolLabels { get; }
		/// <summary>Gets or sets the scheduled search update.</summary>
		IVisualElementScheduledItem shellSearchSchedule { get; set; }
		/// <summary>Gets the host's brand mark.</summary>
		string BrandMark { get; }
		/// <summary>Gets the host's display title.</summary>
		string Title { get; }
		/// <summary>Gets the number of draft actions.</summary>
		int DraftCount { get; }
		/// <summary>Gets or sets the current search query.</summary>
		string Query { get; set; }
		/// <summary>Gets host-provided workspace actions.</summary>
		IReadOnlyDictionary<string, Action> WorkspaceActions { get; }
		/// <summary>Moves backward or forward through history.</summary>
		/// <param name="direction">Negative for backward, positive for forward.</param>
		void History(int direction);
		/// <summary>Frames the full graph overview.</summary>
		void Overview();
		/// <summary>Builds the host's node search controls.</summary>
		void BuildNodeSearch();
		/// <summary>Shows host-provided help.</summary>
		void ShowHelp();
		/// <summary>Creates a labeled icon button.</summary>
		/// <param name="icon">Icon identifier.</param>
		/// <param name="text">Button label.</param>
		/// <param name="action">Action invoked by the button.</param>
		/// <param name="tip">Tooltip text.</param>
		/// <returns>The created button.</returns>
		Button LabeledIcon(string icon, string text, Action action, string tip);
		/// <summary>Creates an icon-only button.</summary>
		/// <param name="icon">Icon identifier.</param>
		/// <param name="action">Action invoked by the button.</param>
		/// <param name="tip">Tooltip text.</param>
		/// <returns>The created button.</returns>
		Button IconButton(string icon, Action action, string tip);
		/// <summary>Applies the reduced-motion preference to host controls.</summary>
		/// <param name="value">Whether reduced motion is enabled.</param>
		void ApplyReducedMotion(bool value);
		/// <summary>Persists shell preferences.</summary>
		void SaveShellPreferences();
		/// <summary>Persists the command workspace.</summary>
		void SaveCommandWorkspace();
		/// <summary>Refreshes the available tool selector.</summary>
		void RefreshShellToolChoice();
		/// <summary>Refreshes provider connection status.</summary>
		void RefreshShellSource();
		/// <summary>Refreshes the task status display.</summary>
		void RefreshShellTaskHud();
		/// <summary>Refreshes command panel content.</summary>
		void RefreshCommandPanel();
		/// <summary>Determines whether a text field currently has focus.</summary>
		/// <returns><see langword="true"/> when text editing is active.</returns>
		bool IsEditingText();
		/// <summary>Applies the current graph filters.</summary>
		void ApplyFilters();
		/// <summary>Activates the tool at the specified index.</summary>
		/// <param name="index">Tool index.</param>
		void EquipToolAt(int index);
	}
}
