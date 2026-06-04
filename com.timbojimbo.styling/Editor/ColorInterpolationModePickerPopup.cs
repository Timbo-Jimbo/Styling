using System;
using TimboJimbo.Core.Utility;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Styling;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.Styling
{
    internal sealed class ColorInterpolationModePickerPopup : PopupWindowContent
    {
        // ── Layout ──────────────────────────────────────────────────────────────
        private const float Padding      = 6f;
        private const float ButtonHeight = 18f;
        private const float RowSpacing   = 0f;
        private const float LeftPanelW   = 90f;
        private const float RightPanelW  = 176f;

        private static Vector2 WindowSize => new Vector2(
            Padding + LeftPanelW + Padding + RightPanelW + Padding,
            Padding + (ButtonHeight + RowSpacing) * s_modes.Length - RowSpacing + Padding
        );

        private static readonly ColorInterpolationMode[] s_modes =
        {
            ColorInterpolationMode.RGB,
            ColorInterpolationMode.HSV,
            ColorInterpolationMode.OkLab,
            ColorInterpolationMode.OkLCh,
        };

        private static readonly string[] s_labels =
        {
            "RGB",
            "HSV",
            "OkLab",
            "OkLCh",
        };

        // ── Styles ───────────────────────────────────────────────────────────────
        private static GUIStyle s_btn;

        private static void EnsureStyles()
        {
            if (s_btn != null) return;
            s_btn = new GUIStyle(EditorStyles.miniButton) { alignment = TextAnchor.MiddleCenter, fontSize = 10 };
        }

        private ColorInterpolationMode _current;
        private ColorInterpolationMode _hovered;
        private readonly Action<ColorInterpolationMode> _onSelected;

        public ColorInterpolationModePickerPopup(ColorInterpolationMode current, Action<ColorInterpolationMode> onSelected)
        {
            _current = current;
            _hovered = current;
            _onSelected = onSelected;
        }

        public override Vector2 GetWindowSize() => WindowSize;

        public override void OnOpen()
        {
            editorWindow.wantsMouseMove = true;
            EditorApplication.update += OnUpdate;
        }

        public override void OnClose()
        {
            EditorApplication.update -= OnUpdate;
        }

        private void OnUpdate() => editorWindow?.Repaint();

        public override void OnGUI(Rect rect)
        {
            EnsureStyles();

            float leftX  = rect.x + Padding;
            float topY   = rect.y + Padding;
            float innerH = rect.height - Padding * 2f;

            DrawButtonPanel(new Rect(leftX, topY, LeftPanelW, innerH));

            float rightX = leftX + LeftPanelW + Padding;
            DrawPreviewPanel(new Rect(rightX, topY, RightPanelW, innerH));

            if (Event.current.type == EventType.MouseMove)
                Event.current.Use();
        }

        private void DrawButtonPanel(Rect panel)
        {
            float y = panel.y;
            for (int i = 0; i < s_modes.Length; i++)
            {
                DrawModeButton(new Rect(panel.x, y, panel.width, ButtonHeight), s_modes[i], s_labels[i]);
                y += ButtonHeight + RowSpacing;
            }
        }

        private void DrawModeButton(Rect rect, ColorInterpolationMode mode, string label)
        {
            if (rect.Contains(Event.current.mousePosition) && _hovered != mode)
            {
                _hovered = mode;
                editorWindow?.Repaint();
            }

            bool isCurrent = mode == _current;
            if (GUI.Toggle(rect, isCurrent, label, s_btn) != isCurrent)
            {
                _current = mode;
                _onSelected?.Invoke(mode);
                editorWindow?.Close();
            }
        }

        private void DrawPreviewPanel(Rect rect)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            EditorGUI.DrawRect(rect, StylingEditorGUI.PreviewBackground);

            const float pipSize = 13f;
            float padX = 14f;
            float lineY = rect.y + rect.height * 0.60f;
            float pipY = rect.y + rect.height * 0.33f;

            float leftX  = rect.x + padX;
            float rightX = rect.x + rect.width - padX;

            Color fromColor = new Color(1.00f, 0.38f, 0.12f, 1f);
            Color toColor   = new Color(0.18f, 0.52f, 1.00f, 1f);

            Handles.BeginGUI();

            DrawSampledLine(leftX, rightX, lineY, _hovered, fromColor, toColor);
            DrawEndCap(leftX, lineY, +1f);
            DrawEndCap(rightX, lineY, -1f);

            DrawSelectionShape(new Vector2(leftX, pipY), 11f, isSquare: false, SampleColor(_hovered, fromColor, toColor, 0f));
            DrawSelectionShape(new Vector2(rightX, pipY), 11f, isSquare: true, SampleColor(_hovered, fromColor, toColor, 1f));

            StylingEditorGUI.EvaluatePipAnimation(EaseType.InOutCubic, out float t, out float fade);
            float pipX = Mathf.Lerp(leftX, rightX, t);
            Color pipColor = SampleColor(_hovered, fromColor, toColor, t) * new Color(0.95f, 0.95f, 0.95f, fade);
            StylingEditorGUI.DrawPip(new Vector2(pipX, lineY - 1f), pipColor, StylingEditorGUI.PipDirection.Up, pipSize);

            Handles.EndGUI();
        }

        private static void DrawSampledLine(float x0, float x1, float y, ColorInterpolationMode mode, Color fromColor, Color toColor)
        {
            const int segments = 96;
            var points = new Vector3[segments + 1];
            var colors = new Color[segments + 1];
            float width = x1 - x0;

            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                points[i] = new Vector3(x0 + width * t, Mathf.RoundToInt(y));
                colors[i] = SampleColor(mode, fromColor, toColor, t);
            }

            Handles.DrawAAPolyLine(4f, colors, points);
        }

        private static Color SampleColor(ColorInterpolationMode mode, Color fromColor, Color toColor, float t) => mode switch
        {
            ColorInterpolationMode.RGB   => Color.LerpUnclamped(fromColor, toColor, t),
            ColorInterpolationMode.HSV   => ColorExtra.LerpUnclampedHSV(fromColor, toColor, t),
            ColorInterpolationMode.OkLab => ColorExtra.LerpUnclampedOkLab(fromColor, toColor, t),
            ColorInterpolationMode.OkLCh => ColorExtra.LerpUnclampedOkLCh(fromColor, toColor, t),
            _ => Color.LerpUnclamped(fromColor, toColor, t),
        };

        private static void DrawEndCap(float x, float y, float dir)
        {
            var prev = Handles.color;
            Handles.color = StylingEditorGUI.PreviewAxis;
            const float w = 4f;
            const float h = 5f;
            Handles.DrawAAPolyLine(1.5f,
                new Vector3(x + dir * w, y - h),
                new Vector3(x, y),
                new Vector3(x + dir * w, y + h));
            Handles.color = prev;
        }

        private static void DrawSelectionShape(Vector2 center, float size, bool isSquare, Color color)
        {
            float half = size * 0.5f;

            var prev = Handles.color;
            Handles.color = color;
            if (isSquare)
            {
                Handles.DrawAAConvexPolygon(
                    new Vector3(center.x - half, center.y - half),
                    new Vector3(center.x + half, center.y - half),
                    new Vector3(center.x + half, center.y + half),
                    new Vector3(center.x - half, center.y + half));
            }
            else
            {
                DrawDisc(center, half);
            }
            Handles.color = prev;
        }

        private static void DrawDisc(Vector2 center, float radius)
        {
            const int seg = 32;
            var points = new Vector3[seg];
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                points[i] = new Vector3(center.x + Mathf.Cos(a) * radius, center.y + Mathf.Sin(a) * radius);
            }
            Handles.DrawAAConvexPolygon(points);
        }
    }
}