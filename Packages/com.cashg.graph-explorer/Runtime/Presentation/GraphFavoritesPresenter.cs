using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using static Cashg.GraphExplorer.GraphUi;

namespace Cashg.GraphExplorer
{
	/// <summary>Represents graph favorites options.</summary>
	public sealed class GraphFavoritesOptions<TNode> where TNode : class, IGraphNode
	{
		/// <summary>Gets or sets the callback that activates a favorite node.</summary>
		public Action<TNode> Activate;
		/// <summary>Gets or sets the callback that returns a node's color.</summary>
		public Func<TNode, Color> Color;
		/// <summary>Gets or sets the favorite star color.</summary>
		public Color FavoriteColor;
		/// <summary>Gets or sets the persisted favorite node identifiers.</summary>
		public HashSet<string> FavoriteIds;
		/// <summary>Gets or sets the callback that reports whether only favorites are shown.</summary>
		public Func<bool> FavoritesOnly;
		/// <summary>Gets or sets the callback that returns nodes keyed by identifier.</summary>
		public Func<IReadOnlyDictionary<string, TNode>> Nodes;
		/// <summary>Gets or sets the scroll view that hosts the favorites drawer.</summary>
		public ScrollView Parent;
		/// <summary>Gets or sets the callback that clears filters before activating a hidden favorite.</summary>
		public Action ResetFilters;
		/// <summary>Gets or sets the mutable favorite result list.</summary>
		public List<TNode> Results;
		/// <summary>Gets or sets the callback that changes favorite-only filtering.</summary>
		public Action<bool> SetFavoritesOnly;
		/// <summary>Gets or sets the callback that returns a node's type label.</summary>
		public Func<TNode, string> TypeLabel;
	}

	/// <summary>Represents graph favorites presenter.</summary>
	public sealed class GraphFavoritesPresenter<TNode> : IDisposable where TNode : class, IGraphNode
	{
		private readonly GraphFavoritesOptions<TNode> options;
		/// <summary>Gets the favorite status label.</summary>
		public Label favoriteStatus;
		/// <summary>Gets the favorites list view.</summary>
		public ListView favoritesList;
		/// <summary>Gets the favorites-only toggle.</summary>
		public Toggle favoritesOnlyToggle;
		/// <summary>Gets the favorites drawer content element.</summary>
		public VisualElement shellFavoritesBody;

		/// <summary>Creates a favorites presenter with host-provided callbacks.</summary>
		/// <param name="options">Callbacks and collections used by the favorites view.</param>
		public GraphFavoritesPresenter(GraphFavoritesOptions<TNode> options)
		{
			this.options = options ?? throw new ArgumentNullException(nameof(options));
		}

		private ScrollView filterScroll => options.Parent;

		/// <summary>Removes the favorites drawer content.</summary>
		public void Dispose()
		{
			shellFavoritesBody?.RemoveFromHierarchy();
		}

		/// <summary>Builds the favorites drawer and list controls.</summary>
		public void BuildFavoritesDrawer()
		{
			// Share the host's favorites filter while virtualizing the independent favorites navigation list.
			shellFavoritesBody = Element("drawer-section");
			filterScroll.Add(shellFavoritesBody);
			Section(shellFavoritesBody, "FAVORITES");
			favoriteStatus = Label("", "note");
			shellFavoritesBody.Add(favoriteStatus);
			favoritesOnlyToggle = new Toggle("Show only favorites")
				{ name = "shell-favorites-only", value = options.FavoritesOnly() };
			UseShellCheckbox(favoritesOnlyToggle);
			favoritesOnlyToggle.RegisterValueChangedCallback(e => options.SetFavoritesOnly(e.newValue));
			shellFavoritesBody.Add(favoritesOnlyToggle);
			favoritesList = new ListView
			{
				name = "shell-favorites-list", itemsSource = options.Results, fixedItemHeight = 48,
				selectionType = SelectionType.Single, virtualizationMethod = CollectionVirtualizationMethod.FixedHeight
			};
			favoritesList.style.flexGrow = 1;
			favoritesList.style.minHeight = 120;
			HideHorizontalScroller(favoritesList);
			favoritesList.makeItem = () =>
			{
				var row = Element("result-row");
				row.Add(Label("", "result-name"));
				row.Add(Label("", "result-meta"));
				row.Add(GraphContentUi.FavoriteStar(14, options.FavoriteColor));
				return row;
			};
			favoritesList.bindItem = (element, index) =>
			{
				var node = options.Results[index];
				var title = element.Q<Label>(className: "result-name");
				title.text = node.Label;
				title.style.color = options.Color(node);
				element.Q<Label>(className: "result-meta").text = options.TypeLabel(node) + " · " +
																  node.Neighbors.Count + " connections" +
																  (node.Visible ? "" : " · hidden by filters");
				GraphContentUi.BindFavoriteRow(element, node);
			};
			favoritesList.selectionChanged += items =>
			{
				var node = items.OfType<TNode>().FirstOrDefault();
				if (node == null) return;
				// Same rule as global search: a hidden favorite must be revealed, not silently left off-screen.
				if (!node.Visible) options.ResetFilters();
				options.Activate(node);
			};
			shellFavoritesBody.Add(favoritesList);
		}

		// Favorites are listed by name regardless of filters, so a hidden favorite can still be found and revealed.
		/// <summary>Refreshes the list and status for the current graph favorites.</summary>
		public void RefreshFavorites()
		{
			if (favoritesList == null || favoriteStatus == null || options.Nodes() == null) return;
			options.Results.Clear();
			options.Results.AddRange(options.Nodes().Values.Where(n => n.Favorite)
											.OrderBy(n => n.Label, StringComparer.OrdinalIgnoreCase)
											.ThenBy(n => n.Id, StringComparer.Ordinal));
			favoritesList.Rebuild();
			if (options.FavoriteIds.Count == 0)
			{
				favoriteStatus.text =
					"No favorites yet. Select an entity and press its star in Overview. Favorites rank first in search and stay saved between sessions.";
				return;
			}

			var missing = options.FavoriteIds.Count(id => !options.Nodes().ContainsKey(id));

			// Report saved favorites absent from this loaded graph without dropping their persisted identities.
			var summary = options.Results.Count + (options.Results.Count == 1 ? " favorite" : " favorites") +
						  " in this graph";
			if (missing > 0) summary += " · " + missing + " not in the loaded graph";
			favoriteStatus.text = summary;
		}
	}
}
