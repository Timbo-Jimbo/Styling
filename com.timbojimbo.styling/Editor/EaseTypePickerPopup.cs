using System;
using TimboJimbo.Styling;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.Styling
{
    internal sealed class EaseTypePickerPopup : PopupWindowContent
    {
        // ── Layout ──────────────────────────────────────────────────────────────
        private const float Padding         = 6f;
        private const float ButtonHeight    = 18f;
        private const float RowSpacing      = 0f;
        private const float FamilyLabelW    = 50f;
        private const float LeftPanelW      = 272f;
        private const float RightPanelW     = 176f;
        private const float CurveDisplayMin = -0.2f;
        private const float CurveDisplayMax =  1.2f;

        // Window size derived from content
        private static Vector2 WindowSize => new Vector2(
            Padding + LeftPanelW + Padding + RightPanelW + Padding,
            Padding + (ButtonHeight + RowSpacing) * s_groups.Length - RowSpacing + Padding
        );

        // ── Groups ──────────────────────────────────────────────────────────────
        // Each family entry: (family label or null for Linear, [In, InOut, Out])
        private static readonly (string Family, EaseType[] Types)[] s_groups =
        {
            ("Linear",  new[] { EaseType.Linear }),
            ("Sine",    new[] { EaseType.InSine,    EaseType.InOutSine,    EaseType.OutSine    }),
            ("Quad",    new[] { EaseType.InQuad,    EaseType.InOutQuad,    EaseType.OutQuad    }),
            ("Cubic",   new[] { EaseType.InCubic,   EaseType.InOutCubic,   EaseType.OutCubic   }),
            ("Quart",   new[] { EaseType.InQuart,   EaseType.InOutQuart,   EaseType.OutQuart   }),
            ("Quint",   new[] { EaseType.InQuint,   EaseType.InOutQuint,   EaseType.OutQuint   }),
            ("Expo",    new[] { EaseType.InExpo,    EaseType.InOutExpo,    EaseType.OutExpo    }),
            ("Circ",    new[] { EaseType.InCirc,    EaseType.InOutCirc,    EaseType.OutCirc    }),
            ("Back",    new[] { EaseType.InBack,    EaseType.InOutBack,    EaseType.OutBack    }),
            ("Elastic", new[] { EaseType.InElastic, EaseType.InOutElastic, EaseType.OutElastic }),
            ("Bounce",  new[] { EaseType.InBounce,  EaseType.InOutBounce,  EaseType.OutBounce  }),
        };

        private static readonly string[] s_variantLabels = { "In", "In Out", "Out" };

        // ── Styles ───────────────────────────────────────────────────────────────
        private static GUIStyle s_btnSolo;
        private static GUIStyle s_btnLeft;
        private static GUIStyle s_btnMid;
        private static GUIStyle s_btnRight;
        private static GUIStyle s_familyLabel;

        private static void EnsureStyles()
        {
            if (s_btnSolo != null) return;

            s_btnSolo  = new GUIStyle(EditorStyles.miniButton)      { alignment = TextAnchor.MiddleCenter, fontSize = 10 };
            s_btnLeft  = new GUIStyle(EditorStyles.miniButtonLeft)   { alignment = TextAnchor.MiddleCenter, fontSize = 10 };
            s_btnMid   = new GUIStyle(EditorStyles.miniButtonMid)    { alignment = TextAnchor.MiddleCenter, fontSize = 10 };
            s_btnRight = new GUIStyle(EditorStyles.miniButtonRight)  { alignment = TextAnchor.MiddleCenter, fontSize = 10 };

            s_familyLabel = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize  = 10,
            };
        }

        private EaseType _current;
        private EaseType _hovered;
        private readonly Action<EaseType> _onSelected;

        public EaseTypePickerPopup(EaseType current, Action<EaseType> onSelected)
        {
            _current  = current;
            _hovered  = current;
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

        private void OnUpdate()
        {
            editorWindow?.Repaint();
        }

        public override void OnGUI(Rect rect)
        {
            EnsureStyles();

            float leftX  = rect.x + Padding;
            float topY   = rect.y + Padding;
            float innerH = rect.height - Padding * 2f;

            // Left panel
            DrawButtonPanel(new Rect(leftX, topY, LeftPanelW, innerH));

            // Right panel – curve preview
            float rightX = leftX + LeftPanelW + Padding;
            DrawCurvePanel(new Rect(rightX, topY, RightPanelW, innerH));

            // Consume mouse-move so the popup repaints on hover
            if (Event.current.type == EventType.MouseMove)
                Event.current.Use();
        }

        private void DrawButtonPanel(Rect panel)
        {
            float y = panel.y;

            for (int g = 0; g < s_groups.Length; g++)
            {
                var (family, types) = s_groups[g];

                string rowLabel = family;
                Rect rowRect = new Rect(panel.x, y, panel.width, ButtonHeight);
                bool isHovered = rowRect.Contains(Event.current.mousePosition);

                if (isHovered)
                    EditorGUI.DrawRect(new Rect(panel.x, y, FamilyLabelW, ButtonHeight), new Color(1f, 1f, 1f, 0.1f));

                EditorGUI.LabelField(
                    new Rect(panel.x, y, FamilyLabelW, ButtonHeight),
                    rowLabel, s_familyLabel);

                float btnAreaX = panel.x + FamilyLabelW;
                float btnAreaW = panel.width - FamilyLabelW;

                if (types.Length == 1)
                {
                    DrawEaseButton(new Rect(btnAreaX, y, btnAreaW, ButtonHeight), types[0], "Linear", s_btnSolo);
                }
                else
                {
                    float btnW = btnAreaW / 3f;
                    DrawEaseButton(new Rect(btnAreaX,             y, btnW, ButtonHeight), types[0], s_variantLabels[0], s_btnLeft);
                    DrawEaseButton(new Rect(btnAreaX + btnW,      y, btnW, ButtonHeight), types[1], s_variantLabels[1], s_btnMid);
                    DrawEaseButton(new Rect(btnAreaX + btnW * 2f, y, btnW, ButtonHeight), types[2], s_variantLabels[2], s_btnRight);
                }

                y += ButtonHeight + RowSpacing;
            }
        }

        private void DrawEaseButton(Rect rect, EaseType type, string label, GUIStyle style)
        {
            if (rect.Contains(Event.current.mousePosition) && _hovered != type)
            {
                _hovered = type;
                editorWindow?.Repaint();
            }

            bool isCurrent = type == _current;
            if (GUI.Toggle(rect, isCurrent, label, style) != isCurrent)
            {
                _current = type;
                _onSelected?.Invoke(type);
                editorWindow?.Close();
            }
        }

        private static float GetOvershootPadding(EaseType type) => type switch
        {
            EaseType.InElastic or EaseType.OutElastic => 30f,
            EaseType.InBack or EaseType.OutBack or EaseType.InOutBack or EaseType.InOutElastic => 10f,
            _ => 0f,
        };

        private void DrawCurvePanel(Rect rect)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            var ease = _hovered;
            EditorGUI.DrawRect(rect, StylingEditorGUI.PreviewBackground);

            const float pipSize = 15f;
            float padX = 10f + pipSize;
            float padY = GetOvershootPadding(ease);
            float innerW = rect.width - padX * 2f;
            float innerH = rect.height - padY * 2f;

            float EvalX(float t) => rect.x + padX + t * innerW;
            float EvalY(float v) => rect.y + padY + (1f - (v - CurveDisplayMin) / (CurveDisplayMax - CurveDisplayMin)) * innerH;

            Handles.BeginGUI();

            DrawAxes(EvalX(0f), EvalX(1f), EvalY(0f), EvalY(1f));
            var (c0, c1) = GetCurveColors(ease);
            DrawCurve(ease, c0, c1, EvalX, EvalY);
            DrawPip(ease, c0, c1, EvalX(1f), EvalY, pipSize);

            Handles.EndGUI();
        }

        private static void DrawAxes(float x0, float x1, float y0, float y1)
        {
            StylingEditorGUI.DrawHorizontalAxis(x0, x1, y0, StylingEditorGUI.PreviewAxis);
            StylingEditorGUI.DrawHorizontalAxis(x0, x1, y1, StylingEditorGUI.PreviewAxis);
        }

        private static void DrawCurve(EaseType ease, Color c0, Color c1, Func<float, float> evalX, Func<float, float> evalY)
        {
            const int seg = 80;
            var pts = new Vector3[seg + 1];
            var col = new Color[seg + 1];
            for (int i = 0; i <= seg; i++)
            {
                float t = i / (float)seg;
                float v = EaseUtility.Evaluate(t, ease);
                pts[i] = new Vector3(evalX(t), evalY(v));
                col[i] = Color.Lerp(c0, c1, v);
            }
            Handles.DrawAAPolyLine(3f, col, pts);
        }

        private static void DrawPip(EaseType ease, Color c0, Color c1, float anchorX, Func<float, float> evalY, float size)
        {
            StylingEditorGUI.EvaluatePipAnimation(ease, out float val, out float fade);
            var color = Color.Lerp(c0, c1, val) * new Color(0.9f, 0.9f, 0.9f, fade);
            StylingEditorGUI.DrawPip(new Vector2(anchorX, evalY(val)), color, StylingEditorGUI.PipDirection.Left, size);
        }

        private static (Color start, Color end) GetCurveColors(EaseType type)
        {
            if (type == EaseType.Linear)           return (StylingEditorGUI.AccentBlue,  StylingEditorGUI.AccentGreen);
            string name = type.ToString();
            if (name.StartsWith("InOut"))           return (StylingEditorGUI.AccentBlue,  StylingEditorGUI.AccentGreen);
            if (name.StartsWith("In"))              return (StylingEditorGUI.AccentBlue,  StylingEditorGUI.AccentWhite);
            return (StylingEditorGUI.AccentWhite, StylingEditorGUI.AccentGreen);   // Out*
        }
    }
}