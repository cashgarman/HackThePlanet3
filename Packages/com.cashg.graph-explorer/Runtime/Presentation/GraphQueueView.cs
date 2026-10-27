using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using static Cashg.GraphExplorer.GraphUi;

namespace Cashg.GraphExplorer
{
	/// <summary>Queue visuals and editing gestures; providers retain review/execution authority.</summary>
	public static class GraphQueueView
	{
		/// <summary>Creates a queue row with a title, accent, and action container.</summary>
		/// <param name="parent">Element that receives the row.</param>
		/// <param name="id">Stable row identifier.</param>
		/// <param name="position">Displayed queue position.</param>
		/// <param name="title">Row title.</param>
		/// <param name="accent">Accent color.</param>
		/// <param name="highlighted">Whether to apply the highlighted appearance.</param>
		/// <returns>References to the created row elements.</returns>
		public static Row CreateRow(VisualElement parent, string id, int position, string title, Color accent,
									bool highlighted = false)
		{
			// Keep title and action containers separate so host-specific controls can be appended to the shared card.
			var card = Element("queue-card");
			card.name = id;
			card.focusable = true;
			card.EnableInClassList("queue-highlight", highlighted);
			TintSlot(card, accent);
			parent.Add(card);
			var line = Element("queue-line");
			card.Add(line);
			var index = Label(position.ToString(), "queue-index");
			index.style.color = accent;
			index.pickingMode = PickingMode.Ignore;
			line.Add(index);
			var heading = Label(title, "queue-entry-title");
			Ellipsis(heading);
			line.Add(heading);
			var actions = Element("queue-actions");
			line.Add(actions);
			return new Row { Card = card, Line = line, Actions = actions, Title = heading };
		}

		/// <summary>Adds a progress meter to a queue card.</summary>
		/// <param name="card">Card that receives the meter.</param>
		/// <param name="progress">Progress fraction, clamped to zero through one.</param>
		/// <param name="color">Progress fill color.</param>
		public static void AddProgress(VisualElement card, float progress, Color color)
		{
			var meter = Element("queue-meter");
			var fill = Element("queue-meter-fill");
			fill.style.width = Length.Percent(Mathf.Clamp01(progress) * 100f);
			fill.style.backgroundColor = color;
			meter.Add(fill);
			card.Add(meter);
		}

		/// <summary>Creates a status chip with the supplied caption and color.</summary>
		/// <param name="caption">Status text.</param>
		/// <param name="color">Status color.</param>
		/// <returns>The created chip.</returns>
		public static Label StatusChip(string caption, Color color)
		{
			var chip = Label(caption, "queue-chip");
			chip.style.color = color;
			chip.style.backgroundColor = new Color(color.r, color.g, color.b, .18f);
			return chip;
		}

		/// <summary>Binds pointer and keyboard gestures for reordering queue entries.</summary>
		/// <typeparam name="T">Queue entry type.</typeparam>
		/// <param name="handle">Element used to begin a drag.</param>
		/// <param name="card">Focusable card for keyboard reordering.</param>
		/// <param name="rows">Container that determines the drop target.</param>
		/// <param name="entries">Mutable queue entries.</param>
		/// <param name="entry">Entry represented by this row.</param>
		/// <param name="identity">Returns an entry's stable identifier.</param>
		/// <param name="locked">Returns whether reordering is currently disabled.</param>
		/// <param name="getDragged">Gets the identifier currently being dragged.</param>
		/// <param name="setDragged">Updates the identifier currently being dragged.</param>
		/// <param name="focus">Focuses the entry after it moves.</param>
		/// <param name="changed">Notifies the host after reordering.</param>
		public static void BindReorder<T>(VisualElement handle, VisualElement card, ScrollView rows, IList<T> entries,
										  T entry, Func<T, string> identity, Func<bool> locked, Func<string> getDragged,
										  Action<string> setDragged,
										  Action<string> focus, Action changed)
		{
			handle.RegisterCallback<PointerDownEvent>(e =>
			{
				// Capture the dragged request identity, since list positions may change before pointer release.
				if (e.button != 0 || locked()) return;
				setDragged(identity(entry));
				handle.CapturePointer(e.pointerId);
				card.Focus();
				e.StopPropagation();
			});
			handle.RegisterCallback<PointerUpEvent>(e =>
			{
				if (getDragged() == null || !handle.HasPointerCapture(e.pointerId)) return;
				handle.ReleasePointer(e.pointerId);
				var draggedId = getDragged();
				setDragged(null);
				if (locked()) return;

				// Resolve the current source and drop target against live entries before moving the request.
				var source = -1;
				for (var i = 0; i < entries.Count; i++)
					if (identity(entries[i]) == draggedId)
					{
						source = i;
						break;
					}

				var target = rows.contentContainer.Children()
								 .FirstOrDefault(row => row.worldBound.Contains(e.position));
				if (source < 0 || target == null) return;
				var destination = -1;
				for (var i = 0; i < entries.Count; i++)
					if ("task-" + identity(entries[i]) == target.name)
					{
						destination = i;
						break;
					}

				if (destination < 0) return;
				var dragged = entries[source];
				entries.RemoveAt(source);
				entries.Insert(destination, dragged);
				focus(draggedId);
				changed();
			});
			card.RegisterCallback<KeyDownEvent>(e =>
			{
				// Alt-arrow reordering follows the same lock and bounds rules as dragging and restores row focus.
				if (!e.altKey || locked()) return;
				int source = entries.IndexOf(entry), destination = source;
				if (e.keyCode == KeyCode.UpArrow) destination--;
				else if (e.keyCode == KeyCode.DownArrow) destination++;
				else return;
				if (source < 0 || destination < 0 || destination >= entries.Count) return;
				entries.RemoveAt(source);
				entries.Insert(destination, entry);
				focus(identity(entry));
				changed();
				rows.Q<VisualElement>("task-" + identity(entry))?.Focus();
				e.StopPropagation();
			});
		}

		/// <summary>Applies an accent border and background tint to an element.</summary>
		/// <param name="element">Element to tint.</param>
		/// <param name="accent">Accent color.</param>
		public static void TintSlot(VisualElement element, Color accent)
		{
			element.style.borderLeftColor = accent;
			element.style.backgroundColor =
				new Color(accent.r * .22f + .05f, accent.g * .16f + .07f, accent.b * .14f + .1f, 1f);
		}

		/// <summary>Configures a label to truncate overflowing text with an ellipsis.</summary>
		/// <param name="label">Label to configure.</param>
		public static void Ellipsis(Label label)
		{
			label.style.whiteSpace = WhiteSpace.NoWrap;
			label.style.overflow = Overflow.Hidden;
			label.style.textOverflow = TextOverflow.Ellipsis;
			label.style.minWidth = 0;
			label.style.flexShrink = 1;
		}

		/// <summary>Maps a task status identifier to a compact display label.</summary>
		/// <param name="status">Task status identifier.</param>
		/// <returns>Short status text suitable for a queue chip.</returns>
		public static string ShortStatus(string status)
		{
			switch (status)
			{
				case "running":    return "RUN";
				case "completed":  return "OK";
				case "failed":     return "FAIL";
				case "blocked":    return "BLOCK";
				case "cancelled":  return "STOP";
				case "skipped":    return "SKIP";
				case "pausing":    return "PAUSE";
				case "paused":     return "HOLD";
				case "waiting":    return "WAIT";
				case "cancelling": return "STOP";
				default:
					return string.IsNullOrEmpty(status) ? "NEXT" :
						status.Length <= 5 ? status.ToUpperInvariant() : status.Substring(0, 4).ToUpperInvariant();
			}
		}

		/// <summary>Represents row.</summary>
		public sealed class Row
		{
			/// <summary>Gets or sets the row's action container.</summary>
			public VisualElement Actions;

			/// <summary>Gets or sets the row card element.</summary>
			public VisualElement Card;

			/// <summary>Gets or sets the row's main content line.</summary>
			public VisualElement Line;

			/// <summary>Gets or sets the row title label.</summary>
			public Label Title;
		}
	}
}