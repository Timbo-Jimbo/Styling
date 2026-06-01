using System;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Styling;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.Styling
{
    internal sealed class DiscreteValueSelectionModePickerPopup : PopupWindowContent
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

        private static readonly DiscreteValueSelectionMode[] s_modes =
        {
            DiscreteValueSelectionMode.Nearest,
            DiscreteValueSelectionMode.LeftSide,
            DiscreteValueSelectionMode.RightSide,
        };

        // ── Styles ───────────────────────────────────────────────────────────────
        private static GUIStyle s_btn;

        private static void EnsureStyles()
        {
            if (s_btn != null) return;
            s_btn = new GUIStyle(EditorStyles.miniButton) { alignment = TextAnchor.MiddleCenter, fontSize = 10 };
        }

        private DiscreteValueSelectionMode _current;
        private DiscreteValueSelectionMode _hovered;
        private readonly Action<DiscreteValueSelectionMode> _onSelected;

        public DiscreteValueSelectionModePickerPopup(DiscreteValueSelectionMode current, Action<DiscreteValueSelectionMode> onSelected)
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
            foreach (var mode in s_modes)
            {
                DrawModeButton(new Rect(panel.x, y, panel.width, ButtonHeight), mode);
                y += ButtonHeight + RowSpacing;
            }
        }

        private void DrawModeButton(Rect rect, DiscreteValueSelectionMode mode)
        {
            if (rect.Contains(Event.current.mousePosition) && _hovered != mode)
            {
                _hovered = mode;
                editorWindow?.Repaint();
            }

            bool isCurrent = mode == _current;
            string label = ObjectNames.NicifyVariableName(mode.ToString());
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
            float lineY = rect.y + rect.height * 0.62f;
            float shapeY = rect.y + rect.height * 0.34f;

            float leftX  = rect.x + padX;               // travel start (t = 0, value = from)
            float rightX = rect.x + rect.width - padX;  // travel end   (t = 1, value = to)

            Handles.BeginGUI();

            // Horizontal line with end caps ( >--------< ).
            StylingEditorGUI.DrawHorizontalAxis(leftX, rightX, lineY, StylingEditorGUI.PreviewAxis);
            DrawEndCap(leftX, lineY, +1f);
            DrawEndCap(rightX, lineY, -1f);

            // Animated pip: travels left -> right
            StylingEditorGUI.EvaluatePipAnimation(EaseType.InOutCubic, out float t, out float fade);
            float pipX = Mathf.Lerp(leftX, rightX, t);
            // sampled: 0 at left (from), 1 at right (to) — where the pip currently sits.
            float sampled = t;
            bool picksRightSide = ResolvesToRightSide(_hovered, sampled);
            Color pipColor = Color.Lerp(StylingEditorGUI.AccentBlue, StylingEditorGUI.AccentGreen, sampled)
                             * new Color(0.95f, 0.95f, 0.95f, fade);
            StylingEditorGUI.DrawPip(new Vector2(pipX, lineY - 4f), pipColor, StylingEditorGUI.PipDirection.Up, pipSize);

            // Previews on each end: from (blue circle) left, to (green square) right.
            const float previewSizeInactive = 11f;
            const float previewSizeActive = 18f;
            var ghost = new Color(1f, 1f, 1f, 0.28f);
            
            DrawSelectionShape(new Vector2(leftX, shapeY),  picksRightSide ? previewSizeInactive : previewSizeActive, isSquare: false, StylingEditorGUI.AccentBlue * (picksRightSide ? ghost : Color.white));
            DrawSelectionShape(new Vector2(rightX, shapeY), picksRightSide ? previewSizeActive : previewSizeInactive, isSquare: true,  StylingEditorGUI.AccentGreen * (picksRightSide ? Color.white : ghost));

            Handles.EndGUI();
        }

        // sampled: 0 at left (from), 1 at right (to). Returns true when the mode resolves to `to`.
        private static bool ResolvesToRightSide(DiscreteValueSelectionMode mode, float sampled) => mode switch
        {
            DiscreteValueSelectionMode.Nearest   => sampled >= 0.5f,
            DiscreteValueSelectionMode.LeftSide  => false,
            DiscreteValueSelectionMode.RightSide => true,
            _ => false,
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
            // Square = `to` selection, circle = `from` selection.
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
