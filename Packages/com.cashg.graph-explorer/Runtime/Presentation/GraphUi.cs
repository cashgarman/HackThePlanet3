using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cashg.GraphExplorer
{
	/// <summary>Shared UI Toolkit primitives. Hosts supply policy and callbacks.</summary>
	public static class GraphUi
	{
		/// <summary>Creates a non-rich-text label with an optional style class.</summary>
		/// <param name="text">Label text.</param>
		/// <param name="css">Optional style class.</param>
		/// <returns>The created label.</returns>
		public static Label Label(string text, string css = null)
		{
			var label = new Label(text) { enableRichText = false };
			if (css != null) label.AddToClassList(css);
			return label;
		}

		/// <summary>Creates an element with the specified style class.</summary>
		/// <param name="css">Style class to add.</param>
		/// <returns>The created element.</returns>
		public static VisualElement Element(string css)
		{
			var element = new VisualElement();
			element.AddToClassList(css);
			return element;
		}

		/// <summary>Creates a button with text, an action, and an optional style class.</summary>
		/// <param name="text">Button text.</param>
		/// <param name="action">Action invoked when clicked.</param>
		/// <param name="css">Optional style class.</param>
		/// <returns>The created button.</returns>
		public static Button Button(string text, Action action, string css = null)
		{
			var button = new Button(action) { text = text };
			if (css != null) button.AddToClassList(css);
			return button;
		}

		/// <summary>Creates a header button with a painted icon and caption.</summary>
		/// <param name="text">Caption and tooltip text.</param>
		/// <param name="icon">Header icon identifier.</param>
		/// <param name="action">Action invoked when clicked.</param>
		/// <returns>The created button.</returns>
		public static Button HeaderButton(string text, string icon, Action action)
		{
			// Render the icon and caption as noninteractive children so the button owns their pointer events.
			var button = new Button(action) { text = "", tooltip = text };
			button.AddToClassList("header-button");
			var glyph = new VisualElement { pickingMode = PickingMode.Ignore };
			glyph.AddToClassList("header-icon");
			glyph.generateVisualContent += ctx => HeaderIcons.Paint(ctx.painter2D, icon, glyph.contentRect);
			glyph.RegisterCallback<GeometryChangedEvent>(_ => glyph.MarkDirtyRepaint());
			var caption = Label(text, "header-button-label");
			caption.pickingMode = PickingMode.Ignore;
			button.Add(glyph);
			button.Add(caption);
			return button;
		}

		/// <summary>Compact action with a painted icon, optional caption and a descriptive tooltip.</summary>
		public static Button ActionButton(string icon, string caption, string tooltip, Action action)
		{
			// Use an icon-only class when no caption is supplied, preserving the same tooltip and click target.
			var button = new Button(action) { tooltip = tooltip };
			button.AddToClassList("ux-action");
			button.Add(Glyph(icon));
			if (!string.IsNullOrEmpty(caption))
			{
				var label = Label(caption, "ux-action-caption");
				label.pickingMode = PickingMode.Ignore;
				button.Add(label);
			}
			else
			{
				button.AddToClassList("ux-icon-only");
			}

			return button;
		}

		/// <summary>Creates and adds a section heading to a parent element.</summary>
		/// <param name="parent">Element that receives the heading.</param>
		/// <param name="title">Heading text.</param>
		/// <returns>The created label.</returns>
		public static Label Section(VisualElement parent, string title)
		{
			var label = Label(title, "section-title");
			parent.Add(label);
			return label;
		}

		/// <summary>Adds a note label to a parent element.</summary>
		/// <param name="parent">Element that receives the note.</param>
		/// <param name="text">Note text.</param>
		public static void Note(VisualElement parent, string text)
		{
			parent.Add(Label(text, "note"));
		}

		/// <summary>Hides the built-in label for a UI Toolkit field.</summary>
		/// <param name="field">Field whose label is hidden.</param>
		public static void HideFieldLabel(VisualElement field)
		{
			// Remove the built-in field label's reserved width as well as its visible text.
			var label = field.Q(className: "unity-base-field__label");
			if (label != null)
			{
				label.style.display = DisplayStyle.None;
				label.style.width = 0;
				label.style.minWidth = 0;
				label.style.marginLeft = 0;
				label.style.marginRight = 0;
			}
		}

		/// <summary>Creates a painted icon element.</summary>
		/// <param name="name">Icon identifier.</param>
		/// <returns>The created icon element.</returns>
		public static VisualElement Glyph(string name)
		{
			var icon = new VisualElement { pickingMode = PickingMode.Ignore };
			icon.AddToClassList("glyph");
			icon.AddToClassList("glyph-" + name);
			icon.style.width = 15;
			icon.style.height = 15;
			icon.style.flexShrink = 0;
			icon.generateVisualContent += ctx => HeaderIcons.Paint(ctx.painter2D, name, icon.contentRect);
			icon.RegisterCallback<GeometryChangedEvent>(_ => icon.MarkDirtyRepaint());
			return icon;
		}

		/// <summary>Hides the built-in label and checkmark chrome of a toggle.</summary>
		/// <param name="toggle">Toggle to restyle.</param>
		public static void HideToggleChrome(Toggle toggle)
		{
			// Strip the default label and checkmark before stretching the input over the custom toggle surface.
			var label = toggle.Q(className: "unity-base-field__label");
			if (label != null) label.style.display = DisplayStyle.None;
			var check = toggle.Q(className: "unity-toggle__checkmark");
			if (check != null) check.style.display = DisplayStyle.None;
			var text = toggle.Q(className: "unity-toggle__text");
			if (text != null)
			{
				text.style.display = DisplayStyle.None;
				text.style.width = 0;
				text.style.minWidth = 0;
				text.style.marginLeft = 0;
				text.style.marginRight = 0;
			}

			var input = toggle.Q(className: "unity-toggle__input");
			if (input != null)
			{
				input.style.marginLeft = 0;
				input.style.marginRight = 0;
				input.style.marginTop = 0;
				input.style.marginBottom = 0;
				input.style.paddingLeft = 0;
				input.style.paddingRight = 0;
				input.style.paddingTop = 0;
				input.style.paddingBottom = 0;
				input.style.backgroundColor = Color.clear;
				input.style.position = Position.Absolute;
				input.style.left = 0;
				input.style.right = 0;
				input.style.top = 0;
				input.style.bottom = 0;
				input.style.alignItems = Align.Center;
				input.style.justifyContent = Justify.Center;
			}
		}

		/// <summary>Paints a check mark inside a rectangle.</summary>
		/// <param name="paint">Painter used to draw the check.</param>
		/// <param name="rect">Bounds for the check mark.</param>
		public static void PaintCheck(Painter2D paint, Rect rect)
		{
			if (rect.width < 4f || rect.height < 4f) return;
			paint.strokeColor = new Color(1f, 0.973f, 0.941f);
			paint.lineWidth = 2f;
			paint.lineCap = LineCap.Round;
			paint.lineJoin = LineJoin.Round;
			paint.BeginPath();
			paint.MoveTo(new Vector2(rect.xMin + 3f, rect.yMin + rect.height * 0.52f));
			paint.LineTo(new Vector2(rect.xMin + rect.width * 0.42f, rect.yMax - 3.5f));
			paint.LineTo(new Vector2(rect.xMax - 2.5f, rect.yMin + 3.5f));
			paint.Stroke();
		}

		// Same box as a filter row: light border, orange fill and a painted check when on.
		/// <summary>Applies the shell checkbox style and paints its check mark.</summary>
		/// <param name="toggle">Toggle to style.</param>
		public static void UseShellCheckbox(Toggle toggle)
		{
			toggle.AddToClassList("shell-check");
			var box = toggle.Q(className: "unity-toggle__checkmark");
			if (box == null) return;
			var mark = new VisualElement { name = "shell-check-mark", pickingMode = PickingMode.Ignore };
			mark.AddToClassList("shell-check-mark");
			mark.generateVisualContent += ctx => PaintCheck(ctx.painter2D, mark.contentRect);
			mark.RegisterCallback<GeometryChangedEvent>(_ => mark.MarkDirtyRepaint());
			box.Add(mark);
		}

		/// <summary>Hides the horizontal scroller of a scroll view.</summary>
		/// <param name="view">Scroll view to update.</param>
		public static void HideHorizontalScroller(ScrollView view)
		{
			if (view == null) return;
			view.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
		}

		/// <summary>Disables horizontal scrolling for a vertical collection view.</summary>
		/// <param name="view">Collection view to update.</param>
		public static void HideHorizontalScroller(BaseVerticalCollectionView view)
		{
			if (view == null) return;
			view.horizontalScrollingEnabled = false;
		}

		/// <summary>Applies fixed square dimensions and centered content to an element.</summary>
		/// <param name="element">Element to size.</param>
		/// <param name="size">Width and height in pixels.</param>
		public static void Square(VisualElement element, float size)
		{
			// Fix both layout bounds and flex behavior so shell icon controls retain their square dimensions.
			element.style.width = size;
			element.style.height = size;
			element.style.minWidth = size;
			element.style.maxWidth = size;
			element.style.minHeight = size;
			element.style.maxHeight = size;
			element.style.flexGrow = 0;
			element.style.flexShrink = 0;
			element.style.marginLeft = 1;
			element.style.marginRight = 1;
			element.style.marginTop = 1;
			element.style.marginBottom = 1;
			element.style.paddingLeft = 0;
			element.style.paddingRight = 0;
			element.style.paddingTop = 0;
			element.style.paddingBottom = 0;
			element.style.alignItems = Align.Center;
			element.style.justifyContent = Justify.Center;
			element.style.flexDirection = FlexDirection.Row;
		}
	}
}