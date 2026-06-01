using System;
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

        // ── Segmented control ────────────────────────────────────────────────────
        private static readonly Color SegmentBackground = new Color(0.17f, 0.17f, 0.17f, 1f);
        private static readonly Color SegmentBorder     = new Color(0.10f, 0.10f, 0.10f, 1f);
        private static readonly Color SegmentActiveFill  = new Color(0.24f, 0.24f, 0.24f, 1f);
        private static readonly Color SegmentSeparator   = new Color(0.10f, 0.10f, 0.10f, 1f);

        private static GUIStyle s_segmentLabel;
        private static GUIStyle s_segmentLabelActive;

        /// <summary>
        /// Draws a horizontal segmented control (a row of mutually-exclusive tabs), where the
        /// selected segment is highlighted. Returns the (possibly changed) selected index.
        /// </summary>
        public static int SegmentedControl(Rect rect, int selected, params string[] labels)
        {
            if (labels == null || labels.Length == 0)
                return selected;

            EnsureSegmentStyles();

            // Outer border + background.
            EditorGUI.DrawRect(rect, SegmentBorder);
            var inner = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
            EditorGUI.DrawRect(inner, SegmentBackground);

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

                if (evt.type == EventType.Repaint)
                {
                    if (isActive)
                        EditorGUI.DrawRect(segRect, SegmentActiveFill);

                    if (i > 0)
                    {
                        var sepRect = new Rect(segRect.x, segRect.y + 2f, 1f, segRect.height - 4f);
                        EditorGUI.DrawRect(sepRect, SegmentSeparator);
                    }

                    var style = isActive ? s_segmentLabelActive : s_segmentLabel;
                    style.Draw(segRect, new GUIContent(labels[i]), false, false, false, false);
                }
                else if (evt.type == EventType.MouseDown && evt.button == 0 && segRect.Contains(evt.mousePosition))
                {
                    if (i != selected)
                        result = i;

                    evt.Use();
                }

                EditorGUIUtility.AddCursorRect(segRect, MouseCursor.Link);
            }

            return result;
        }

        private static void EnsureSegmentStyles()
        {
            if (s_segmentLabel != null)
                return;

            s_segmentLabel = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f, 1f) }
            };

            s_segmentLabelActive = new GUIStyle(s_segmentLabel)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.92f, 0.92f, 0.92f, 1f) }
            };
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

        public static int SegmentedControl(int selected, string[] labels, params GUILayoutOption[] options)
        {
            var rect = EditorGUILayout.GetControlRect(false, 20f, options);
            return StylingEditorGUI.SegmentedControl(rect, selected, labels);
        }
    }
}