using System;
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
            ColorInterpolationMode.HSV   => LerpUnclampedHSV(fromColor, toColor, t),
            ColorInterpolationMode.OkLab => LerpUnclampedOkLab(fromColor, toColor, t),
            ColorInterpolationMode.OkLCh => LerpUnclampedOkLCh(fromColor, toColor, t),
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

        // Todo, color sampling logic is internal in PropertyBindings..! So had to duplicate it here. Fix

        private static Color LerpUnclampedHSV(Color from, Color to, float t)
        {
            Color.RGBToHSV(from, out float fromH, out float fromS, out float fromV);
            Color.RGBToHSV(to, out float toH, out float toS, out float toV);

            if (Mathf.Abs(toH - fromH) > 0.5f)
            {
                if (toH > fromH)
                    fromH += 1f;
                else
                    toH += 1f;
            }

            float h = Mathf.LerpUnclamped(fromH, toH, t);
            float s = Mathf.LerpUnclamped(fromS, toS, t);
            float v = Mathf.LerpUnclamped(fromV, toV, t);

            if (h < 0f) h += 1f;
            else if (h > 1f) h -= 1f;

            Color result = Color.HSVToRGB(h, s, v);
            result.a = Mathf.LerpUnclamped(from.a, to.a, t);
            return result;
        }

        private static Color LerpUnclampedOkLab(Color from, Color to, float t)
        {
            RGBToOkLab(from, out float fromL, out float fromA, out float fromB);
            RGBToOkLab(to, out float toL, out float toA, out float toB);

            float l = Mathf.LerpUnclamped(fromL, toL, t);
            float a = Mathf.LerpUnclamped(fromA, toA, t);
            float b = Mathf.LerpUnclamped(fromB, toB, t);

            return OkLabToRGB(l, a, b, withAlpha: Mathf.LerpUnclamped(from.a, to.a, t));
        }

        private static Color LerpUnclampedOkLCh(Color from, Color to, float t)
        {
            RGBToOkLCh(from, out float fromL, out float fromC, out float fromH);
            RGBToOkLCh(to, out float toL, out float toC, out float toH);

            if (Mathf.Abs(toH - fromH) > Mathf.PI)
            {
                if (toH > fromH)
                    fromH += 2f * Mathf.PI;
                else
                    toH += 2f * Mathf.PI;
            }

            float l = Mathf.LerpUnclamped(fromL, toL, t);
            float c = Mathf.LerpUnclamped(fromC, toC, t);
            float h = Mathf.LerpUnclamped(fromH, toH, t);

            if (h > Mathf.PI) h -= 2f * Mathf.PI;
            else if (h < -Mathf.PI) h += 2f * Mathf.PI;

            return OkLChToRGB(l, c, h, withAlpha: Mathf.LerpUnclamped(from.a, to.a, t));
        }

        private static void RGBToOkLab(Color color, out float l, out float a, out float b)
        {
            Vector3 lab = ColorToOkLab(color);
            l = lab.x;
            a = lab.y;
            b = lab.z;
        }

        private static Color OkLabToRGB(float l, float a, float b, float? withAlpha = null)
        {
            return OkLabToColor(new Vector3(l, a, b), withAlpha ?? 1f);
        }

        private static void RGBToOkLCh(Color color, out float l, out float c, out float h)
        {
            Vector3 lab = ColorToOkLab(color);
            Vector3 lch = OkLabToOkLCh(lab);
            l = lch.x;
            c = lch.y;
            h = lch.z;
        }

        private static Color OkLChToRGB(float l, float c, float h, float? withAlpha = null)
        {
            return OkLabToColor(OkLChToOkLab(new Vector3(l, c, h)), withAlpha ?? 1f);
        }

        private static Color EnsureLinear(Color c)
        {
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? c : c.linear;
        }

        private static Color LinearToProjectSpecific(Color c)
        {
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? c : c.gamma;
        }

        private static Vector3 LinearToLMS(Vector3 linear)
        {
            return new Vector3(
                0.4122214708f * linear.x + 0.5363325363f * linear.y + 0.0514459929f * linear.z,
                0.2119034982f * linear.x + 0.6806995451f * linear.y + 0.1073969566f * linear.z,
                0.0883024619f * linear.x + 0.2817188376f * linear.y + 0.6299787005f * linear.z
            );
        }

        private static Vector3 LMSToOkLab(Vector3 lms)
        {
            float l_ = Mathf.Pow(lms.x, 1f / 3f);
            float m_ = Mathf.Pow(lms.y, 1f / 3f);
            float s_ = Mathf.Pow(lms.z, 1f / 3f);

            return new Vector3(
                0.2104542553f * l_ + 0.7936177850f * m_ - 0.0040720468f * s_,
                1.9779984951f * l_ - 2.4285922050f * m_ + 0.4505937099f * s_,
                0.0259040371f * l_ + 0.7827717662f * m_ - 0.8086757660f * s_
            );
        }

        private static Vector3 OkLabToLMS(Vector3 lab)
        {
            float l_ = lab.x + 0.3963377774f * lab.y + 0.2158037573f * lab.z;
            float m_ = lab.x - 0.1055613458f * lab.y - 0.0638541728f * lab.z;
            float s_ = lab.x - 0.0894841775f * lab.y - 1.2914855480f * lab.z;

            return new Vector3(l_ * l_ * l_, m_ * m_ * m_, s_ * s_ * s_);
        }

        private static Vector3 LMSToLinear(Vector3 lms)
        {
            return new Vector3(
                 4.0767416621f * lms.x - 3.3077115913f * lms.y + 0.2309699292f * lms.z,
                -1.2684380046f * lms.x + 2.6097574011f * lms.y - 0.3413193965f * lms.z,
                -0.0041960863f * lms.x - 0.7034186147f * lms.y + 1.7076147010f * lms.z
            );
        }

        private static Vector3 ColorToOkLab(Color c)
        {
            Color linear = EnsureLinear(c);
            return LMSToOkLab(LinearToLMS(new Vector3(linear.r, linear.g, linear.b)));
        }

        private static Color OkLabToColor(Vector3 lab, float alpha)
        {
            Vector3 linear = LMSToLinear(OkLabToLMS(lab));
            Color linearColor = new Color(linear.x, linear.y, linear.z, alpha);
            return LinearToProjectSpecific(linearColor);
        }

        private static Vector3 OkLabToOkLCh(Vector3 lab)
        {
            return new Vector3(
                lab.x,
                Mathf.Sqrt(lab.y * lab.y + lab.z * lab.z),
                Mathf.Atan2(lab.z, lab.y)
            );
        }

        private static Vector3 OkLChToOkLab(Vector3 lch)
        {
            return new Vector3(
                lch.x,
                lch.y * Mathf.Cos(lch.z),
                lch.y * Mathf.Sin(lch.z)
            );
        }
    }
}