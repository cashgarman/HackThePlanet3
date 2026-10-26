using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using static Cashg.GraphExplorer.GraphUi;

namespace Cashg.GraphExplorer
{
	/// <summary>Represents graph search options.</summary>
	public sealed class GraphSearchOptions<TNode> where TNode : class, IGraphNode
	{
		/// <summary>Gets or sets the callback that activates a result.</summary>
		public Action<TNode> Activate;
		/// <summary>Gets or sets the favorite icon color.</summary>
		public Color FavoriteColor;
		/// <summary>Gets or sets the callback that creates a search field with ghost text.</summary>
		public Func<TextField, string, VisualElement> Ghost;
		/// <summary>Gets or sets the callback that creates an icon button.</summary>
		public Func<string, Action, string, Button> IconButton;
		/// <summary>Gets or sets the callback that reports whether search is open.</summary>
		public Func<bool> IsOpen;
		/// <summary>Gets or sets the callback that returns searchable nodes.</summary>
		public Func<IEnumerable<TNode>> Nodes;
		/// <summary>Gets or sets the callback that resets filters before activating a hidden node.</summary>
		public Action ResetFilters;
		/// <summary>Gets or sets the mutable result list.</summary>
		public List<TNode> Results;
		/// <summary>Gets or sets the root element used for popup positioning.</summary>
		public VisualElement Root;
		/// <summary>Gets or sets the element that hosts the search control.</summary>
		public VisualElement SearchHost;
		/// <summary>Gets or sets the callback that changes open state.</summary>
		public Action<bool> SetOpen;
		/// <summary>Gets or sets the callback that creates a node shape icon.</summary>
		public Func<TNode, VisualElement> Shape;
		/// <summary>Gets or sets the callback that returns a node's type label.</summary>
		public Func<TNode, string> TypeLabel;
	}

	/// <summary>Represents graph search presenter.</summary>
	public sealed class GraphSearchPresenter<TNode> : IDisposable where TNode : class, IGraphNode
	{
		private readonly List<Action> detach = new();
		private readonly GraphSearchOptions<TNode> options;
		/// <summary>Gets the node search text field.</summary>
		public TextField nodeSearchField;
		/// <summary>Gets the node search results list.</summary>
		public ListView nodeSearchList;
		/// <summary>Gets the node search popup element.</summary>
		public VisualElement nodeSearchPopup;
		/// <summary>Gets the node search status label.</summary>
		public Label nodeSearchStatus;
		private VisualElement searchRow;

		/// <summary>Creates a node search presenter with host-provided callbacks.</summary>
		/// <param name="options">Callbacks and elements used by node search.</param>
		public GraphSearchPresenter(GraphSearchOptions<TNode> options)
		{
			this.options = options ?? throw new ArgumentNullException(nameof(options));
		}

		private List<TNode> nodeSearchResults => options.Results;
		private VisualElement root => options.Root;
		private VisualElement shellSearchHost => options.SearchHost;

		/// <summary>Detaches registered callbacks and removes search UI elements.</summary>
		public void Dispose()
		{
			// Remove root-level callbacks as well as owned UI so rebuilding search cannot duplicate handlers.
			foreach (var action in detach) action();
			detach.Clear();
			nodeSearchPopup?.RemoveFromHierarchy();
			searchRow?.RemoveFromHierarchy();
			options.SetOpen(false);
		}

		private void RegisterOn<T>(VisualElement target, EventCallback<T> callback,
								   TrickleDown phase = TrickleDown.NoTrickleDown) where T : EventBase<T>, new()
		{
			target.RegisterCallback(callback, phase);
			detach.Add(() => target.UnregisterCallback(callback, phase));
		}

		private void RegisterRoot<T>(EventCallback<T> callback, TrickleDown phase = TrickleDown.NoTrickleDown)
			where T : EventBase<T>, new()
		{
			RegisterOn(root, callback, phase);
		}

		/// <summary>Builds the search field, popup, results list, and interaction callbacks.</summary>
		public void BuildNodeSearch()
		{
			// Build a root-level popup with virtualized rows beneath the header's search field.
			var row = searchRow = Element("shell-search-row");
			shellSearchHost.Add(row);
			nodeSearchField = new TextField { name = "graph-node-search" };
			var wrap = options.Ghost(nodeSearchField, "Find any node…");
			wrap.AddToClassList("grow");
			row.Add(wrap);
			nodeSearchField.tooltip =
				"Fuzzy search every loaded node. ↑ / ↓ to browse, Enter to focus, Esc to dismiss. Ctrl+F or / to search.";
			row.Add(options.IconButton("close", () =>
			{
				nodeSearchField.value = "";
				CloseNodeSearch();
			}, "Clear node search."));
			nodeSearchPopup = Element("node-search-popup");
			nodeSearchPopup.name = "graph-node-search-popup";
			nodeSearchPopup.style.display = DisplayStyle.None;
			root.Add(nodeSearchPopup);
			nodeSearchStatus = Label("", "node-search-status");
			nodeSearchPopup.Add(nodeSearchStatus);
			nodeSearchList = new ListView
			{
				name = "graph-node-search-results", itemsSource = nodeSearchResults,
				fixedItemHeight = 48, selectionType = SelectionType.Single,
				virtualizationMethod = CollectionVirtualizationMethod.FixedHeight
			};
			nodeSearchList.makeItem = () =>
			{
				var item = Element("node-search-item");
				item.Add(Element("node-search-icon"));
				var text = Element("node-search-text");
				text.Add(Label("", "node-search-name"));
				text.Add(Label("", "node-search-kind"));
				item.Add(text);
				item.Add(GraphContentUi.FavoriteStar(14, options.FavoriteColor));
				item.RegisterCallback<PointerUpEvent>(e =>
				{
					if (e.button != 0 || !(item.userData is int index)) return;
					nodeSearchList.selectedIndex = index;
					ActivateNodeSearch();
					e.StopPropagation();
				});
				return item;
			};
			nodeSearchList.bindItem = (item, index) =>
			{
				// Rebind recycled rows to current node identity, type, filter state, and favorite marker.
				var node = nodeSearchResults[index];
				item.userData = index;
				var icon = item.Q(className: "node-search-icon");
				icon.Clear();
				icon.Add(options.Shape(node));
				item.Q<Label>(className: "node-search-name").text = node.Label;
				item.Q<Label>(className: "node-search-kind").text =
					options.TypeLabel(node) + (node.Visible ? "" : " · hidden by filters");
				item.tooltip = node.Label + " · " + options.TypeLabel(node) + (node.Favorite ? " · Favorite" : "");
				GraphContentUi.BindFavoriteRow(item, node);
			};
			nodeSearchList.itemsChosen += _ => ActivateNodeSearch();
			nodeSearchPopup.Add(nodeSearchList);
			nodeSearchField.RegisterValueChangedCallback(_ =>
			{
				options.SetOpen(true);
				RefreshNodeSearch();
			});
			nodeSearchField.RegisterCallback<FocusInEvent>(_ =>
			{
				options.SetOpen(true);
				RefreshNodeSearch();
			});
			RegisterRoot<KeyDownEvent>(e =>
			{
				// Restrict result navigation shortcuts to focus inside the search field or popup.
				var target = e.target as VisualElement;
				if (target == null || !(target == nodeSearchField || target == nodeSearchPopup ||
										nodeSearchField.Contains(target) || nodeSearchPopup.Contains(target))) return;
				if (e.keyCode != KeyCode.DownArrow && e.keyCode != KeyCode.UpArrow && e.keyCode != KeyCode.Return &&
					e.keyCode != KeyCode.KeypadEnter) return;
				if (!options.IsOpen())
				{
					options.SetOpen(true);
					RefreshNodeSearch();
				}

				if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
				{
					ActivateNodeSearch();
				}
				else if (nodeSearchResults.Count > 0)
				{
					var index = Mathf.Clamp(nodeSearchList.selectedIndex + (e.keyCode == KeyCode.DownArrow ? 1 : -1), 0,
											nodeSearchResults.Count - 1);
					nodeSearchList.selectedIndex = index;
					nodeSearchList.ScrollToItem(index);
				}

				e.StopImmediatePropagation();
				e.PreventDefault();
			}, TrickleDown.TrickleDown);
			RegisterRoot<PointerDownEvent>(e =>
			{
				var target = e.target as VisualElement;
				if (target != null && !nodeSearchPopup.Contains(target) && !shellSearchHost.Contains(target))
					CloseNodeSearch();
			}, TrickleDown.TrickleDown);
			RegisterRoot<FocusInEvent>(e =>
			{
				var target = e.target as VisualElement;
				if (target != null && !nodeSearchPopup.Contains(target) && !shellSearchHost.Contains(target))
					CloseNodeSearch();
			});
			RegisterRoot<GeometryChangedEvent>(_ => PositionNodeSearch());
			RegisterOn<GeometryChangedEvent>(shellSearchHost, _ => PositionNodeSearch());
		}

		/// <summary>Positions and sizes the result popup under the search field.</summary>
		public void PositionNodeSearch()
		{
			// Anchor the popup under the search host and cap its list height to the available root space.
			if (nodeSearchPopup == null || !options.IsOpen()) return;
			var bounds = shellSearchHost.worldBound;
			var point = root.WorldToLocal(new Vector2(bounds.xMin, bounds.yMax));
			nodeSearchPopup.style.left = point.x;
			nodeSearchPopup.style.top = point.y + 6;
			nodeSearchPopup.style.width = bounds.width;
			nodeSearchList.style.height = Mathf.Min(384, Mathf.Max(0, root.resolvedStyle.height - point.y - 84),
													nodeSearchResults.Count * 48);
		}

		/// <summary>Rebuilds search results for the current query.</summary>
		public void RefreshNodeSearch()
		{
			// Rebuild results from current graph visibility and query state.
			if (nodeSearchPopup == null || !options.IsOpen()) return;
			var selectedId = nodeSearchList.selectedItem is TNode selected ? selected.Id : null;
			var query = (nodeSearchField.value ?? "").Trim().ToLowerInvariant();
			nodeSearchResults.Clear();
			if (query.Length > 0 && options.Nodes() != null)
				nodeSearchResults.AddRange(
					options.Nodes().Select(n => new { Node = n, Score = NodeSearchScore(query, n) })
						   .Where(hit => hit.Score >= 0).OrderByDescending(hit => hit.Node.Favorite)
						   .ThenByDescending(hit => hit.Score)
						   .ThenBy(hit => hit.Node.Label, StringComparer.OrdinalIgnoreCase)
						   .ThenBy(hit => hit.Node.Id, StringComparer.Ordinal)
						   .Select(hit => hit.Node));

			// Preserve selection by node ID after score ordering changes, falling back to the first result.
			nodeSearchList.Rebuild();
			var index = selectedId == null ? -1 : nodeSearchResults.FindIndex(n => n.Id == selectedId);
			nodeSearchList.SetSelectionWithoutNotify(nodeSearchResults.Count == 0
														 ? Array.Empty<int>()
														 : new[] { Mathf.Max(0, index) });
			nodeSearchStatus.text = query.Length == 0 ? "Search all loaded nodes · ↑ ↓ browse · Enter focus" :
				nodeSearchResults.Count == 0 ? "No matching nodes" :
				nodeSearchResults.Count + " nodes · ↑ ↓ browse · Enter focus";
			nodeSearchPopup.style.display = DisplayStyle.Flex;
			nodeSearchPopup.BringToFront();
			PositionNodeSearch();
		}

		/// <summary>Closes the search popup.</summary>
		/// <returns><see langword="true"/> when the popup was open.</returns>
		public bool CloseNodeSearch()
		{
			if (!options.IsOpen()) return false;
			options.SetOpen(false);
			nodeSearchPopup.style.display = DisplayStyle.None;
			return true;
		}

		/// <summary>Activates the selected search result.</summary>
		public void ActivateNodeSearch()
		{
			if (!(nodeSearchList.selectedItem is TNode node)) return;
			// Global navigation must reveal a result even when the filter drawer hid it.
			if (!node.Visible) options.ResetFilters();
			CloseNodeSearch();
			nodeSearchField.Blur();
			options.Activate(node);
		}

		/// <summary>Scores a node against a query for result ranking.</summary>
		/// <param name="query">Normalized search query.</param>
		/// <param name="node">Node to score.</param>
		/// <returns>Non-negative relevance score, or -1 when unmatched.</returns>
		public static int NodeSearchScore(string query, TNode node)
		{
			// Rank literal name matches ahead of token matches across name, type, and opaque identity.
			var name = (node.Label ?? "").ToLowerInvariant();
			if (name == query) return 10000;
			if (name.StartsWith(query, StringComparison.Ordinal)) return 9000 - Mathf.Min(name.Length, 1000);
			if (name.Contains(query)) return 7000 - Mathf.Min(name.Length, 1000);
			var text = name + " " + node.Record.TypeId.ToLowerInvariant() + " " + (node.Id ?? "").ToLowerInvariant();
			var score = 0;
			foreach (var token in query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
			{
				if (text.Contains(token))
				{
					score += 100;
					continue;
				}

				// Match real words with at most one edit; scattered letters are too permissive.
				// Short queries require literal matches to avoid unrelated one-letter variations.
				if (token.Length >= 4 && text.Split(' ', '-', '_', '/', ':').Any(word => OneSearchTypo(token, word)))
				{
					score += 20;
					continue;
				}

				return -1;
			}

			return score;
		}

		private static bool OneSearchTypo(string a, string b)
		{
			// Skip the common prefix, then allow one transposition, insertion, deletion, or substitution.
			if (Math.Abs(a.Length - b.Length) > 1) return false;
			int i = 0, j = 0;
			while (i < a.Length && j < b.Length && a[i] == b[j])
			{
				i++;
				j++;
			}

			if (i == a.Length || j == b.Length) return true;
			if (a.Length == b.Length && i + 1 < a.Length && a[i] == b[j + 1] && a[i + 1] == b[j] &&
				a.Substring(i + 2) == b.Substring(j + 2)) return true;
			if (a.Length >= b.Length) i++;
			if (b.Length >= a.Length) j++;
			while (i < a.Length && j < b.Length && a[i] == b[j])
			{
				i++;
				j++;
			}

			return i == a.Length && j == b.Length;
		}
	}
}
