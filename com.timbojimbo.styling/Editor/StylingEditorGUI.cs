using System;
using System.Collections.Generic;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Styling;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.Styling
{
    public static class StylingEditorGUI
    {
        // ── Shared visual language ───────────────────────────────────────────────
        public static readonly Color PreviewBackground = new Color(0.13f, 0.13f, 0.13f, 1f);
        public static readonly Color PreviewAxis       = new Color(0.227f, 0.227f, 0.227f);

        public static readonly Color AccentBlue  = new Color(0.20f, 0.50f, 1.00f, 1f);
        public static readonly Color AccentWhite = new Color(0.90f, 0.90f, 0.90f, 1f);
        public static readonly Color AccentGreen = new Color(0.10f, 0.85f, 0.45f, 1f);

        public static void EaseTypePopup(Rect rect, EaseType current, Action<EaseType> onSelected)
        {
            if (EditorGUI.DropdownButton(rect, new GUIContent(current.ToString()), FocusType.Passive))
            {
                var popup = new EaseTypePickerPopup(current, onSelected);
                PopupWindow.Show(rect, popup);
            }
        }

        public static void DiscreteValueSelectionModePopup(Rect rect, DiscreteValueSelectionMode current, Action<DiscreteValueSelectionMode> onSelected)
        {
            if (EditorGUI.DropdownButton(rect, new GUIContent(ObjectNames.NicifyVariableName(current.ToString())), FocusType.Passive))
            {
                var popup = new DiscreteValueSelectionModePickerPopup(current, onSelected);
                PopupWindow.Show(rect, popup);
            }
        }

        public static void ColorInterpolationModePopup(Rect rect, ColorInterpolationMode current, Action<ColorInterpolationMode> onSelected)
        {
            if (EditorGUI.DropdownButton(rect, new GUIContent(current.ToString()), FocusType.Passive))
            {
                var popup = new ColorInterpolationModePickerPopup(current, onSelected);
                PopupWindow.Show(rect, popup);
            }
        }

        /// <summary>Direction an arrow pip points toward (its tip).</summary>
        public enum PipDirection { Up, Down, Left, Right }

        /// <summary>
        /// The shared looping animation envelope used by the preview popups: a value travels
        /// from 0→1, holds, then fades out before looping. Keeps motion consistent across tools.
        /// </summary>
        public static void EvaluatePipAnimation(EaseType travelEase, out float t, out float fade)
        {
            const double cycle = 1.5;
            const float travelEnd = 0.75f;
            const float fadeOutStart = 0.85f;

            float phase = (float)(EditorApplication.timeSinceStartup % cycle / cycle);
            t = EaseUtility.Evaluate(Mathf.Clamp01(phase / travelEnd), travelEase);
            fade = EaseUtility.Evaluate(
                1f - Mathf.Clamp01((phase - fadeOutStart) / (1f - fadeOutStart)),
                EaseType.InCubic);
        }

        /// <summary>
        /// Draws a single arrow pip whose tip is anchored at <paramref name="tip"/>, pointing
        /// in <paramref name="direction"/>. The body extends away from the tip.
        /// </summary>
        public static void DrawPip(Vector2 tip, Color color, PipDirection direction, float size = 15f)
        {
            float half = size * 0.5f;
            float h = half * 0.6f;

            // Base shape: tip at origin, body extending toward +X (points Left).
            var p0 = new Vector3(size, -h);
            var p1 = new Vector3(size,  h);
            var p2 = new Vector3(half,  h);
            var p3 = new Vector3(0f,    0f);
            var p4 = new Vector3(half, -h);

            float rot = direction switch
            {
                PipDirection.Left  => 0f,
                PipDirection.Up    => 90f,
                PipDirection.Right => 180f,
                PipDirection.Down  => 270f,
                _ => 0f,
            };

            var prev = (Handles.color, Handles.matrix);
            Handles.color = color;
            Handles.matrix = Matrix4x4.TRS(new Vector3(tip.x, tip.y), Quaternion.Euler(0f, 0f, rot), Vector3.one);
            Handles.DrawAAConvexPolygon(p0, p1, p2, p3, p4);
            (Handles.color, Handles.matrix) = prev;
        }

        public static void DrawHorizontalAxis(float x0, float x1, float y, Color color)
        {
            var prev = Handles.color;
            Handles.color = color;
            float yr = Mathf.RoundToInt(y);
            Handles.DrawLine(new Vector3(x0, yr), new Vector3(x1, yr), 0.5f);
            Handles.color = prev;
        }

        public static void DrawFoldout(
            bool expanded,
            Action drawContent,
            Action<bool> onToggle,
            Action<bool> onGroupToggle = null)
        {
            if (onGroupToggle == null)
                onGroupToggle = onToggle;

            // BeginHorizontal with a styled background paints the row behind the
            // child controls automatically, so the content stays visible.
            // The style's left padding already reserves space for the foldout arrow,
            // so content begins to the right of the arrow.
            using (var scope = new EditorGUILayout.HorizontalScope(Styles.FoldoutRowStyle, GUILayout.ExpandWidth(true)))
            {
                Event evt = Event.current;

                // A fixed oversized bleed reliably covers the full inspector width
                // regardless of sidebars, scroll bars, or currentViewWidth quirks.
                const float BleedAmount = 4000f;
                var rowRect = scope.rect;
                Rect bleedRect = new(
                    rowRect.x - BleedAmount,
                    rowRect.y,
                    rowRect.width + (BleedAmount * 2f),
                    rowRect.height);

                if (evt.type == EventType.Repaint)
                {
                    // Bg
                    EditorGUI.DrawRect(bleedRect, Styles.FoldoutBackgroundColor);

                    // Top border.
                    EditorGUI.DrawRect(
                        new Rect(bleedRect.x, rowRect.y, bleedRect.width, Styles.FoldoutTopBorderThickness),
                        Styles.FoldoutBorderColor);

                    // Foldout arrow vertically centered within the row.
                    float arrowHeight = EditorGUIUtility.singleLineHeight;
                    Rect arrowRect = new(
                        rowRect.x,
                        rowRect.y + ((rowRect.height - arrowHeight) * 0.5f),
                        13f,
                        arrowHeight);
                    arrowRect.x -= 14f;
                    EditorStyles.foldout.Draw(arrowRect, GUIContent.none, false, false, expanded, false);
                }

                using (new GUILayout.HorizontalScope(GUILayout.MinHeight(EditorGUIUtility.singleLineHeight + 2)))
                {
                    drawContent?.Invoke();
                }

                // Detect a click on the row. Inner controls (kebab buttons etc.) get to
                // consume the event first; if they did, evt.type will be Used here.
                bool toggled = false;
                bool wasGroupToggle = false;

                if (evt.type == EventType.MouseDown && evt.button == 0 && bleedRect.Contains(evt.mousePosition))
                {
                    toggled = true;
                    wasGroupToggle = evt.alt;
                    GUI.changed = true;
                    evt.Use();
                }

                if (toggled)
                {
                    expanded = !expanded;

                    if (wasGroupToggle)
                        onGroupToggle?.Invoke(expanded);
                    else
                        onToggle?.Invoke(expanded);
                }
            }
        }
        
        public static bool KebabMenuButton(string label = null, string tooltip = null) => GhostButton("_Menu", label, tooltip);
        public static bool RemoveButton(string label = null, string tooltip = null) => GhostButton("Toolbar Minus", label, tooltip);
        public static bool AddButton(string label = null, string tooltip = null) => GhostButton("Toolbar Plus", label, tooltip);
        public static bool DrawImportButton(string label = null, string tooltip = null) => GhostButton("Download-Available", label, tooltip);
        public static bool DrawRecordButton(string label = null, string tooltip = null) => GhostButton("Animation.Record", label, tooltip);

        /// <summary>
        /// Draws a small icon button with a hover highlight. The icon is looked
        /// up via <see cref="EditorGUIUtility.IconContent(string)"/>. Auto-sizes
        /// to fit the icon (and optional label). Returns true on click.
        /// </summary>
        public static bool GhostButton(string iconName, string label = null, string tooltip = null)
        {
            GUIContent icon = EditorGUIUtility.IconContent(iconName);
            GUIContent content = new GUIContent(label ?? icon.text, icon.image, tooltip ?? icon.tooltip);

            return GUILayout.Button(content, Styles.GhostIconStyle, GUILayout.ExpandWidth(false));
        }

        public static bool ButtonGroupButton(GUIContent content, int buttonIndex, int buttonCount, Action onClick = null, params GUILayoutOption[] options)
        {
            var leftStyle = EditorStyles.miniButtonLeft;
            var midStyle = EditorStyles.miniButtonMid;
            var rightStyle = EditorStyles.miniButtonRight;
            var standaloneStyle = EditorStyles.miniButton;

            GUIStyle style = buttonIndex switch
            {
                0 when buttonCount == 1 => standaloneStyle,
                0 => leftStyle,
                _ when buttonIndex == buttonCount - 1 => rightStyle,
                _ => midStyle,
            };

            if (GUILayout.Button(content, style, options))
            {
                onClick?.Invoke();
                return true;
            }

            return false;
        }

        public static bool ButtonGroupToggleButton(GUIContent content, bool value, int buttonIndex, int buttonCount, Action<bool> onToggle = null, params GUILayoutOption[] options)
        {
            var leftStyle = EditorStyles.miniButtonLeft;
            var midStyle = EditorStyles.miniButtonMid;
            var rightStyle = EditorStyles.miniButtonRight;
            var standaloneStyle = EditorStyles.miniButton;

            GUIStyle style = buttonIndex switch
            {
                0 when buttonCount == 1 => standaloneStyle,
                0 => leftStyle,
                _ when buttonIndex == buttonCount - 1 => rightStyle,
                _ => midStyle,
            };

            bool newValue = GUILayout.Toggle(value, content, style, options);
            if (newValue != value)
                onToggle?.Invoke(newValue);

            return newValue;
        }

        // ── Hold button group ────────────────────────────────────────────────────
        // A group of "hold" buttons that share press-and-drag behavior: press one and drag
        // across its siblings to move the active hold from button to button (used to scrub a
        // live preview across rows). Wrap the buttons with
        // BeginHoldButtonGroup()/EndHoldButtonGroup(); each ButtonGroupHoldButton registers
        // itself, and EndHoldButtonGroup processes the shared press/drag/release.
        private struct HoldButtonEntry
        {
            public Rect Rect;
            public Action OnHoldStart;
            public Action OnHoldEnd;
        }

        private static readonly List<HoldButtonEntry> _holdButtons = new List<HoldButtonEntry>();
        private static bool _holdGroupEnabled;
        private static int _holdGroupControlId = -1;
        private static int _activeHoldButton = -1;

        /// <summary>
        /// Begins a group of hold buttons. Call before drawing the buttons, then
        /// <see cref="EndHoldButtonGroup"/> after. When <paramref name="enabled"/> is false the
        /// buttons still draw, but no press/drag input is processed.
        /// </summary>
        public static void BeginHoldButtonGroup(bool enabled = true)
        {
            _holdButtons.Clear();
            _holdGroupEnabled = enabled;

            if (!enabled)
            {
                _holdGroupControlId = -1;
                _activeHoldButton = -1;
            }
        }

        public static bool ButtonGroupHoldButton(GUIContent content, int buttonIndex, int buttonCount, Action onHoldStart = null, Action onHoldEnd = null, params GUILayoutOption[] options)
        {
            var leftStyle = EditorStyles.miniButtonLeft;
            var midStyle = EditorStyles.miniButtonMid;
            var rightStyle = EditorStyles.miniButtonRight;
            var standaloneStyle = EditorStyles.miniButton;

            GUIStyle style = buttonIndex switch
            {
                0 when buttonCount == 1 => standaloneStyle,
                0 => leftStyle,
                _ when buttonIndex == buttonCount - 1 => rightStyle,
                _ => midStyle,
            };

            var rect = GUILayoutUtility.GetRect(content, style, options);
            var evt = Event.current;

            int index = -1;
            if (_holdGroupEnabled)
            {
                index = _holdButtons.Count;
                _holdButtons.Add(new HoldButtonEntry { Rect = rect, OnHoldStart = onHoldStart, OnHoldEnd = onHoldEnd });
            }

            bool isActive = _holdGroupEnabled && _holdGroupControlId != -1 && _activeHoldButton == index;

            if (evt.type == EventType.Repaint)
            {
                bool isHover = rect.Contains(evt.mousePosition);
                style.Draw(rect, content, isHover, isActive, isActive, false);
            }

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);

            return isActive;
        }

        public static bool HoldButton(GUIContent content, Action onHoldStart = null, Action onHoldEnd = null, params GUILayoutOption[] options)
        {
            return ButtonGroupHoldButton(content, 0, 1, onHoldStart, onHoldEnd, options);
        }

        /// <summary>
        /// Processes press/drag/release for the buttons registered since the last
        /// <see cref="BeginHoldButtonGroup"/>. Dragging across buttons switches which one is held,
        /// invoking the previous button's onHoldEnd and the new button's onHoldStart.
        /// </summary>
        public static void EndHoldButtonGroup()
        {
            if (!_holdGroupEnabled || _holdButtons.Count == 0)
                return;

            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            var evt = Event.current;

            switch (evt.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (evt.button == 0)
                    {
                        int hit = HitTestHoldButtons(evt.mousePosition);
                        if (hit >= 0)
                        {
                            GUIUtility.hotControl = controlId;
                            _holdGroupControlId = controlId;
                            SetActiveHoldButton(hit);
                            evt.Use();
                        }
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlId)
                    {
                        SetActiveHoldButton(HitTestHoldButtons(evt.mousePosition));
                        evt.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlId && evt.button == 0)
                    {
                        GUIUtility.hotControl = 0;
                        _holdGroupControlId = -1;
                        SetActiveHoldButton(-1);
                        evt.Use();
                    }
                    break;
            }
        }

        private static void SetActiveHoldButton(int index)
        {
            if (_activeHoldButton == index)
                return;

            if (_activeHoldButton >= 0 && _activeHoldButton < _holdButtons.Count)
                _holdButtons[_activeHoldButton].OnHoldEnd?.Invoke();

            _activeHoldButton = index;

            if (index >= 0 && index < _holdButtons.Count)
                _holdButtons[index].OnHoldStart?.Invoke();
        }

        // Returns the index of the button under the cursor, snapping to the nearest button when
        // the cursor is between buttons but still within the group's combined bounds; -1 if the
        // cursor is outside the group entirely.
        private static int HitTestHoldButtons(Vector2 mousePos)
        {
            if (_holdButtons.Count == 0)
                return -1;

            Rect bounds = _holdButtons[0].Rect;
            for (int i = 1; i < _holdButtons.Count; i++)
            {
                var r = _holdButtons[i].Rect;
                bounds = Rect.MinMaxRect(
                    Mathf.Min(bounds.xMin, r.xMin),
                    Mathf.Min(bounds.yMin, r.yMin),
                    Mathf.Max(bounds.xMax, r.xMax),
                    Mathf.Max(bounds.yMax, r.yMax));
            }

            if (!bounds.Contains(mousePos))
                return -1;

            int hit = -1;
            float hitDistance = float.MaxValue;
            for (int i = 0; i < _holdButtons.Count; i++)
            {
                var r = _holdButtons[i].Rect;
                if (r.Contains(mousePos))
                    return i;

                var closestPoint = new Vector2(
                    Mathf.Clamp(mousePos.x, r.xMin, r.xMax),
                    Mathf.Clamp(mousePos.y, r.yMin, r.yMax));
                float distance = Vector2.Distance(mousePos, closestPoint);
                if (distance < hitDistance)
                {
                    hitDistance = distance;
                    hit = i;
                }
            }

            return hit;
        }


        // ── Segmented control ────────────────────────────────────────────────────
        /// <summary>
        /// Draws a horizontal segmented control (a row of mutually-exclusive tabs), where the
        /// selected segment is highlighted. Returns the (possibly changed) selected index.
        /// </summary>
        public static int SegmentedControl(Rect rect, int selected, string[] labels, Action<int> onSelected)
        {
            if (labels == null || labels.Length == 0)
                return selected;

            // Outer border + background.
            EditorGUI.DrawRect(rect, Styles.SegmentBorder);
            var inner = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
            EditorGUI.DrawRect(inner, Styles.SegmentBackground);

            float segmentWidth = inner.width / labels.Length;
            var evt = Event.current;
            int result = selected;

            for (int i = 0; i < labels.Length; i++)
            {
                var segRect = new Rect(
                    inner.x + segmentWidth * i,
                    inner.y,
                    i == labels.Length - 1 ? inner.xMax - (inner.x + segmentWidth * i) : segmentWidth,
                    inner.height);

                bool isActive = i == selected;
                var hitRect = segRect;
                //slightly larger
                hitRect.y -= 4f;
                hitRect.height += 8f;
                hitRect.x -= 2f;
                hitRect.width += 4f;

                if (evt.type == EventType.Repaint)
                {
                    if (isActive)
                        EditorGUI.DrawRect(segRect, Styles.SegmentActiveFill);

                    if (i > 0)
                    {
                        var sepRect = new Rect(segRect.x, segRect.y + 2f, 1f, segRect.height - 4f);
                        EditorGUI.DrawRect(sepRect, Styles.SegmentSeparator);
                    }

                    var style = isActive ? Styles.SegmentLabelActive : Styles.SegmentLabel;
                    style.Draw(segRect, new GUIContent(labels[i]), false, false, false, false);
                }
                else if (evt.type == EventType.MouseDown && evt.button == 0 && hitRect.Contains(evt.mousePosition))
                {
                    result = i;
                    onSelected?.Invoke(i);
                    evt.Use();
                }

                EditorGUIUtility.AddCursorRect(hitRect, MouseCursor.Link);
            }

            return result;
        }

        
        internal static class Styles
        {
            public static Color SegmentBackground => new Color(0.17f, 0.17f, 0.17f, 1f);
            public static Color SegmentBorder => new Color(0.10f, 0.10f, 0.10f, 1f);
            public static Color SegmentActiveFill => new Color(0.24f, 0.24f, 0.24f, 1f);
            public static Color SegmentSeparator => new Color(0.10f, 0.10f, 0.10f, 1f);
            private static readonly Color FoldoutBackgroundColorDarkSkin = new(0.19f, 0.19f, 0.19f, 1f);
            private static readonly Color FoldoutBackgroundColorLightSkin = new(0.74f, 0.74f, 0.74f, 1f);
            private static readonly Color FoldoutBorderColorDarkSkin = new(0f, 0f, 0f, 0.38f);
            private static readonly Color FoldoutBorderColorLightSkin = new(0f, 0f, 0f, 0.18f);

            private const float FoldoutContentLeftPadding = 0f;
            private const float FoldoutContentRightPadding = 0f;
            private const float FoldoutVerticalPadding = 0f;
            public const float FoldoutTopBorderThickness = 1f;

            public static GUIStyle GhostIconStyle => _ghostIconStyle ??= new GUIStyle(EditorStyles.iconButton)
            {
                alignment = TextAnchor.MiddleCenter,
                imagePosition = ImagePosition.ImageLeft,
                fontSize = EditorStyles.label.fontSize,
                fixedWidth = 0f,
                fixedHeight = EditorGUIUtility.singleLineHeight,
            };

            public static GUIStyle SegmentLabel => _segmentLabel ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f, 1f) }
            };

            public static GUIStyle SegmentLabelActive => _segmentLabelActive ??= new GUIStyle(SegmentLabel)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.92f, 0.92f, 0.92f, 1f) }
            };

            public static GUIStyle ItalicLabel => _italicLabel ??= new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Italic };

            public static Color FoldoutBackgroundColor => ForCurrentSkin(FoldoutBackgroundColorDarkSkin, FoldoutBackgroundColorLightSkin);
            public static Color FoldoutBorderColor => ForCurrentSkin(FoldoutBorderColorDarkSkin, FoldoutBorderColorLightSkin);

            public static GUIStyle FoldoutRowStyle => _foldoutRowStyle ??= new GUIStyle
            {
                normal = { background = FoldoutBackgroundTexture },
                padding = new RectOffset(
                    (int)FoldoutContentLeftPadding,
                    (int)FoldoutContentRightPadding,
                    (int)(FoldoutTopBorderThickness + FoldoutVerticalPadding),
                    (int)FoldoutVerticalPadding),
                margin = new RectOffset(0, 0, 0, 0),
                stretchWidth = true,
            };

            private static GUIStyle _ghostIconStyle;
            private static GUIStyle _segmentLabel;
            private static GUIStyle _segmentLabelActive;
            private static GUIStyle _foldoutRowStyle;
            private static GUIStyle _italicLabel;
            private static Texture2D _foldoutBackgroundTexture;
            private static bool _stylesUseProSkin;

            private static Texture2D FoldoutBackgroundTexture
            {
                get
                {
                    EnsureFoldoutTexture();
                    return _foldoutBackgroundTexture;
                }
            }

            private static Color ForCurrentSkin(Color darkSkinColor, Color lightSkinColor)
            {
                return EditorGUIUtility.isProSkin ? darkSkinColor : lightSkinColor;
            }

            private static void EnsureFoldoutTexture()
            {
                bool isProSkin = EditorGUIUtility.isProSkin;
                if (_foldoutBackgroundTexture != null && _stylesUseProSkin == isProSkin)
                    return;

                _stylesUseProSkin = isProSkin;

                if (_foldoutBackgroundTexture != null)
                    UnityEngine.Object.DestroyImmediate(_foldoutBackgroundTexture);

                _foldoutBackgroundTexture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                _foldoutBackgroundTexture.SetPixel(0, 0, FoldoutBackgroundColor);
                _foldoutBackgroundTexture.Apply();
            }
        }
    }

    public static class StylingEditorGUILayout
    {
        public static void EaseTypePopup(EaseType current, Action<EaseType> onSelected, params GUILayoutOption[] options)
        {
            var rect = EditorGUILayout.GetControlRect(options);
            StylingEditorGUI.EaseTypePopup(rect, current, onSelected);
        }

        public static void DiscreteValueSelectionModePopup(DiscreteValueSelectionMode current, Action<DiscreteValueSelectionMode> onSelected, params GUILayoutOption[] options)
        {
            var rect = EditorGUILayout.GetControlRect(options);
            StylingEditorGUI.DiscreteValueSelectionModePopup(rect, current, onSelected);
        }

        public static void ColorInterpolationModePopup(ColorInterpolationMode current, Action<ColorInterpolationMode> onSelected, params GUILayoutOption[] options)
        {
            var rect = EditorGUILayout.GetControlRect(options);
            StylingEditorGUI.ColorInterpolationModePopup(rect, current, onSelected);
        }

        public static int SegmentedControl(int selected, string[] labels, Action<int> onSelected, params GUILayoutOption[] options)
        {
            var rect = EditorGUILayout.GetControlRect(false, 20f, options);
            return StylingEditorGUI.SegmentedControl(rect, selected, labels, onSelected);
        }

        public static void BeginVerticalCenter()
        {
            GUILayout.BeginVertical();
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
        }

        public static void EndCenter()
        {
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
        }
    }
}