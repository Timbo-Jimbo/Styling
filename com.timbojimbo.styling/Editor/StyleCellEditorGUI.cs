using System;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Styling;
using TimboJimboEditor.PropertyBindings.Utility;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.Styling
{
	/// <summary>
	/// Shared drawing and context-menu helpers for style value cells. Used by both the style sheet
	/// property table and the <see cref="StyledProperty"/> inspector so the two behave identically.
	/// </summary>
	internal static class StyleCellEditorGUI
	{
		private const float ChipIconSize = 16f; // built-in icons are 16px; drawing at native size keeps them crisp
		private const float ChipPaddingX = 4f;
		private const float ChipIconTextGap = 2f;
		private const float MinControlWidthForKeyLabel = 60f;

		private const float IconChipWidth = ChipPaddingX + ChipIconSize + ChipPaddingX;

		private static GUIStyle s_chipTextStyle;
		private static Texture s_linkIcon;
		private static Texture s_unlinkIcon;
		private static Texture s_warnIcon;

		/// <summary>
		/// Splits an icon-only chip off the right edge of a cell, shrinking <paramref name="rect"/> to the
		/// control beside it. Pair with <see cref="DrawUnlinkedChip"/>.
		/// </summary>
		public static Rect SplitLinkChip(ref Rect rect)
		{
			var chip = new Rect(rect.xMax - IconChipWidth, rect.y, IconChipWidth, rect.height);
			rect.width = Mathf.Max(0f, rect.width - IconChipWidth);
			return chip;
		}

		/// <summary>
		/// Draws the "not linked" chip beside an ordinary value control, so a linkable cell does not read
		/// as a plain field. Dimmed when no theme is in the hierarchy. Returns true on a left click, which
		/// the caller should answer with the same menu as a right-click on the cell.
		/// </summary>
		public static bool DrawUnlinkedChip(Rect chipRect, StyleTheme theme)
		{
			s_unlinkIcon ??= LoadIcon("d_Unlinked", "Unlinked");

			var evt = Event.current;
			bool clicked = evt.type == EventType.MouseDown && evt.button == 0 && chipRect.Contains(evt.mousePosition);

			DrawChipFrame(chipRect);

			var iconRect = new Rect(chipRect.x + ChipPaddingX, chipRect.y + (chipRect.height - ChipIconSize) * 0.5f, ChipIconSize, ChipIconSize);
			var previousColor = GUI.color;
			GUI.color = theme != null ? previousColor : new Color(previousColor.r, previousColor.g, previousColor.b, previousColor.a * 0.4f);
			if (s_unlinkIcon != null)
				GUI.DrawTexture(iconRect, s_unlinkIcon, ScaleMode.ScaleToFit, true);
			else
				EditorGUI.LabelField(iconRect, "∅", s_chipTextStyle ?? EditorStyles.miniLabel);
			GUI.color = previousColor;

			string tooltip = theme != null
				? $"Not linked to a theme. Click or right-click to link to a value in {theme.name}."
				: "Can be linked to a theme value, but no StyleThemeSource is in the hierarchy.";
			EditorGUI.LabelField(chipRect, new GUIContent(string.Empty, tooltip));
			EditorGUIUtility.AddCursorRect(chipRect, MouseCursor.Link);

			return clicked;
		}

		// Chip background butts straight up against the control so they read as one unit.
		private static void DrawChipFrame(Rect chipRect)
		{
			bool pro = EditorGUIUtility.isProSkin;
			EditorGUI.DrawRect(chipRect, pro ? new Color(0.26f, 0.26f, 0.26f, 1f) : new Color(0.78f, 0.78f, 0.78f, 1f));
			EditorGUI.DrawRect(new Rect(chipRect.x, chipRect.y, 1f, chipRect.height), pro ? new Color(0.13f, 0.13f, 0.13f, 1f) : new Color(0.6f, 0.6f, 0.6f, 1f));
		}

		/// <summary>
		/// Draws a theme-linked cell as the ordinary value control, locked (disabled) and showing the
		/// resolved value, with a "link chip" (icon + key) attached flush to its right edge so the two
		/// read as one control. Unresolved keys show the literal fallback and a warning icon.
		/// </summary>
		public static void DrawThemedCell(Rect rect, StyleTheme theme, BindableProperty property, string themeKey, ValueContainer literalValue)
		{
			s_chipTextStyle ??= new GUIStyle(EditorStyles.miniLabel)
			{
				alignment = TextAnchor.MiddleLeft,
				clipping = TextClipping.Clip,
				padding = new RectOffset(0, 0, 0, 0)
			};
			s_linkIcon ??= LoadIcon("d_Linked", "Linked");
			s_warnIcon ??= LoadIcon("d_console.warnicon.sml", "console.warnicon.sml");

			var themed = default(ValueContainer);
			bool resolved = theme != null && theme.TryGetValue(themeKey, out themed) && themed.Kind == property.Kind;
			var shown = resolved ? themed : literalValue;
			var icon = resolved ? s_linkIcon : s_warnIcon;

			string tooltip = resolved
				? $"Linked to '{themeKey}' in {theme.name}. Right-click to unlink."
				: $"Linked to '{themeKey}' but no theme in the hierarchy provides it; showing the literal fallback.";

			// Chip width: icon + key if there is room for a usable control beside it, otherwise icon only.
			float textWidth = s_chipTextStyle.CalcSize(new GUIContent(themeKey)).x;
			float chipWidth = ChipPaddingX + ChipIconSize + ChipIconTextGap + textWidth + ChipPaddingX;
			chipWidth = Mathf.Min(chipWidth, rect.width * 0.5f);
			bool showText = rect.width - chipWidth >= MinControlWidthForKeyLabel;
			if (!showText)
				chipWidth = ChipPaddingX + ChipIconSize + ChipPaddingX;

			var controlRect = new Rect(rect.x, rect.y, Mathf.Max(0f, rect.width - chipWidth), rect.height);
			var chipRect = new Rect(controlRect.xMax, rect.y, chipWidth, rect.height);

			using (new EditorGUI.DisabledScope(true))
				PropertyBindingsEditorGUI.ValueContainerField(controlRect, property, shown);

			DrawChipFrame(chipRect);

			var iconRect = new Rect(chipRect.x + ChipPaddingX, chipRect.y + (chipRect.height - ChipIconSize) * 0.5f, ChipIconSize, ChipIconSize);
			if (icon != null)
				GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
			else
				EditorGUI.LabelField(iconRect, resolved ? "✦" : "⚠", s_chipTextStyle);

			if (showText)
			{
				var textRect = new Rect(iconRect.xMax + ChipIconTextGap, chipRect.y, chipRect.xMax - ChipPaddingX - (iconRect.xMax + ChipIconTextGap), chipRect.height);
				EditorGUI.LabelField(textRect, themeKey, s_chipTextStyle);
			}

			// One tooltip for the whole cell.
			EditorGUI.LabelField(rect, new GUIContent(string.Empty, tooltip));
		}

		private static Texture LoadIcon(string proName, string lightName)
		{
			var content = EditorGUIUtility.IconContent(EditorGUIUtility.isProSkin ? proName : lightName);
			if (content?.image == null)
				content = EditorGUIUtility.IconContent(lightName);
			return content?.image;
		}

		/// <summary>Copy / Paste items. Paste is enabled only when the clipboard holds a value of the cell's kind.</summary>
		public static void AddClipboardMenuItems(GenericMenu menu, ValueKind kind, bool hasValue, ValueContainer currentValue, Action<ValueContainer> paste)
		{
			if (hasValue)
				menu.AddItem(new GUIContent("Copy"), false, () => StyleSheetPropertyTableClipboard.SetValueClipboard(currentValue));
			else
				menu.AddDisabledItem(new GUIContent("Copy"));

			if (StyleSheetPropertyTableClipboard.TryGetClipboardValue(kind, out var clipboardValue))
				menu.AddItem(new GUIContent("Paste"), false, () => paste(clipboardValue));
			else
				menu.AddDisabledItem(new GUIContent("Paste"));
		}

		/// <summary>
		/// Appends the "Link to Theme ▸ ..." / "Unlink from Theme" items for a cell.
		/// <paramref name="setThemeKey"/> receives the new key, or null to unlink.
		/// </summary>
		public static void AddThemeMenuItems(GenericMenu menu, StyleTheme theme, ValueKind kind, string currentKey, Action<string> setThemeKey)
		{
			menu.AddSeparator(string.Empty);

			if (theme == null)
			{
				menu.AddDisabledItem(new GUIContent("Link to Theme/(no StyleThemeSource in hierarchy)"));
			}
			else
			{
				bool any = false;
				foreach (var entry in theme.Entries)
				{
					if (entry.Value.Kind != kind) continue;
					any = true;
					var key = entry.Key;
					menu.AddItem(new GUIContent($"Link to Theme/{key}"), key == currentKey, () => setThemeKey(key));
				}
				if (!any)
					menu.AddDisabledItem(new GUIContent($"Link to Theme/(no {kind} entries in {theme.name})"));
			}

			if (!string.IsNullOrEmpty(currentKey))
				menu.AddItem(new GUIContent("Unlink from Theme"), false, () => setThemeKey(null));
		}

		public static bool IsContextMenuEvent(Event evt, Rect rect)
		{
			if (!rect.Contains(evt.mousePosition))
				return false;

			return evt.type == EventType.ContextClick
				|| (evt.type == EventType.MouseDown && evt.button == 1);
		}
	}
}
