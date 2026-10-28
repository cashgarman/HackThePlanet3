using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace HackThePlanet
{
	/// <summary>A lab choice list whose popup stays attached until an explicit selection or dismissal.</summary>
	public sealed class PlaytestDropdown : VisualElement
	{
		private readonly List<string> choices;
		private readonly Button trigger;
		private readonly Action<PlaytestDropdown> opened;
		private readonly Action closed;
		private VisualElement popup;
		private string selected;

		public PlaytestDropdown(string label, List<string> choices, int index,
			Action<PlaytestDropdown> opened, Action closed)
		{
			this.choices = choices;
			this.opened = opened;
			this.closed = closed;
			selected = choices[Mathf.Clamp(index, 0, choices.Count - 1)];
			AddToClassList("lab-choice");
			Add(new Label(label) { pickingMode = PickingMode.Ignore });
			trigger = new Button(() => { if (popup == null) Open(); else Close(); });
			trigger.AddToClassList("lab-choice-trigger");
			trigger.text = selected + "  ▾";
			Add(trigger);
			// UI navigation can retain a held direction across Game-view focus changes. Mouse selection
			// and explicit key presses are handled here instead of forwarding those repeated events.
			RegisterCallback<NavigationMoveEvent>(e => e.StopImmediatePropagation(), TrickleDown.TrickleDown);
			RegisterCallback<NavigationSubmitEvent>(e => e.StopImmediatePropagation(), TrickleDown.TrickleDown);
			trigger.RegisterCallback<KeyDownEvent>(e =>
			{
				if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.Space) return;
				if (popup == null) Open(); else Close();
				e.StopImmediatePropagation();
			}, TrickleDown.TrickleDown);
			RegisterCallback<DetachFromPanelEvent>(_ => Close());
		}

		public void RegisterValueChangedCallback(EventCallback<ChangeEvent<string>> callback) => RegisterCallback(callback);

		public void Close()
		{
			if (popup == null) return;
			popup.RemoveFromHierarchy();
			popup = null;
			closed();
		}

		private void Open()
		{
			if (panel == null) return;
			opened(this);
			var root = panel.visualTree;
			popup = new VisualElement { name = "lab-choice-overlay", focusable = true };
			popup.AddToClassList("lab-choice-overlay");
			// The overlay is a sibling of the UIDocument so menu placement is independent of scrolling.
			// Copy its styles explicitly; document styles do not cascade to panel-root siblings.
			var document = parent;
			while (document != null && document.name != "workstation") document = document.parent;
			if (document != null)
				for (var i = 0; i < document.styleSheets.count; i++) popup.styleSheets.Add(document.styleSheets[i]);
			root.Add(popup);
			var menu = new ScrollView(ScrollViewMode.Vertical);
			menu.AddToClassList("lab-choice-menu");
			var bounds = trigger.worldBound;
			var height = Mathf.Min(360, root.layout.height - 24);
			menu.style.left = Mathf.Clamp(bounds.xMin, 12, Mathf.Max(12, root.layout.width - bounds.width - 12));
			menu.style.top = Mathf.Clamp(bounds.yMax, 12, Mathf.Max(12, root.layout.height - height - 12));
			menu.style.width = Mathf.Min(bounds.width, root.layout.width - 24);
			menu.style.maxHeight = height;
			popup.Add(menu);
			popup.RegisterCallback<PointerDownEvent>(e =>
			{
				if (e.target != popup) return;
				Close(); e.StopImmediatePropagation();
			});
			popup.RegisterCallback<NavigationMoveEvent>(e => e.StopImmediatePropagation(), TrickleDown.TrickleDown);
			popup.RegisterCallback<NavigationSubmitEvent>(e => e.StopImmediatePropagation(), TrickleDown.TrickleDown);
			var items = new List<Button>();
			var cursor = choices.IndexOf(selected);
			foreach (var option in choices)
			{
				var choice = option;
				var button = new Button(() => Select(choice)) { text = option };
				button.AddToClassList("lab-choice-option");
				button.style.fontSize = trigger.resolvedStyle.fontSize;
				button.EnableInClassList("active", option == selected);
				menu.Add(button);
				items.Add(button);
			}
			popup.RegisterCallback<KeyDownEvent>(e =>
			{
				if (e.keyCode == KeyCode.Escape) { Close(); trigger.Focus(); e.StopImmediatePropagation(); }
				else if (e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.DownArrow)
				{
					items[cursor].RemoveFromClassList("active");
					cursor = Mathf.Clamp(cursor + (e.keyCode == KeyCode.DownArrow ? 1 : -1), 0, items.Count - 1);
					items[cursor].AddToClassList("active");
					menu.ScrollTo(items[cursor]);
					e.StopImmediatePropagation();
				}
				else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.Space)
				{ Select(choices[cursor]); trigger.Focus(); e.StopImmediatePropagation(); }
			}, TrickleDown.TrickleDown);
			popup.Focus();
		}

		private void Select(string choice)
		{
			var previous = selected;
			Close();
			if (previous == choice) return;
			selected = choice;
			trigger.text = choice + "  ▾";
			using var change = ChangeEvent<string>.GetPooled(previous, selected);
			change.target = this;
			SendEvent(change);
		}
	}
}
