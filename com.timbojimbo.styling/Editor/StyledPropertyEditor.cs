using System;
using TimboJimbo.Core;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Styling;
using TimboJimboEditor.Core;
using TimboJimboEditor.PropertyBindings.Utility;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.Styling
{
	[CustomEditor(typeof(StyledProperty))]
	public sealed class StyledPropertyEditor : Editor
	{
		private static readonly FoldoutGUI.SessionBool StyleValuesFoldoutExpanded = new FoldoutGUI.SessionBool("StyledPropertyStyleValues");
		private static readonly FoldoutGUI.SessionBool TransitionFoldoutExpanded = new FoldoutGUI.SessionBool("StyledPropertyTransition");

		private StyledProperty _styled;
		private SerializedProperty _propertyProp;
		private SerializedProperty _baselineProp;
		private SerializedProperty _baselineThemeKeyProp;
		private SerializedProperty _styleValuesProp;
		private SerializedProperty _transitionProp;

		private BindableProperty _lastSeenProperty;
		private StylingOverrideScope _previewOverride;
		private bool _isPreviewing;
		private string _previewStyleName;

		private void OnEnable()
		{
			_styled = (StyledProperty)target;
			_propertyProp = serializedObject.FindProperty("_property");
			_baselineProp = serializedObject.FindProperty("_baseline");
			_baselineThemeKeyProp = serializedObject.FindProperty("_baselineThemeKey");
			_styleValuesProp = serializedObject.FindProperty("_styleValues");
			_transitionProp = serializedObject.FindProperty("_transition");
			_lastSeenProperty = _styled.Property;
		}

		private void OnDisable()
		{
			EndPreview();
		}

		public override void OnInspectorGUI()
		{
			if (_isPreviewing && Event.current.rawType == EventType.MouseUp && GUIUtility.hotControl == 0)
			{
				EndPreview();
				Repaint();
			}

			serializedObject.Update();

			EditorGUILayout.PropertyField(_propertyProp);

			// The picker commits its selection on the next OnGUI, which is a Layout event. Apply and reseed
			// here so the sections below lay out and repaint against the same property kind; doing it at
			// the end left Layout drawing the old kind's transition controls and Repaint the new kind's.
			serializedObject.ApplyModifiedProperties();
			DetectPropertyChange();
			serializedObject.Update();

			EditorGUILayout.Space(EditorGUIUtility.standardVerticalSpacing);

			DrawStyleValuesSection();
			DrawTransitionSection();

			if (serializedObject.ApplyModifiedProperties() && _styled.IsTransitioning)
				_styled.CompleteTransitionImmediate();
		}

		public override bool RequiresConstantRepaint() => _isPreviewing;

		// ── Property changes ─────────────────────────────────────────────────────

		/// <summary>
		/// Picking a different property invalidates everything the component stored, so re-seed the
		/// baseline from the live value, drop the style values and reset the transition to the default.
		/// </summary>
		private void DetectPropertyChange()
		{
			var current = _styled.Property;
			if (current.Equals(_lastSeenProperty))
				return;

			_lastSeenProperty = current;

			serializedObject.Update();
			_styleValuesProp.ClearArray();
			_baselineThemeKeyProp.stringValue = string.Empty;

			if (current.Target != null && current.Kind != ValueKind.Invalid)
			{
				WriteTransition(StylePropertyTransition.GetDefault(current));
				_baselineProp.boxedValue = TryReadLiveValue(current, out var liveValue)
					? liveValue
					: ValueContainer.FromDefault(current.Kind);
			}
			else
			{
				WriteTransition(StylePropertyTransition.Instant);
			}

			serializedObject.ApplyModifiedProperties();
		}

		private bool TryReadLiveValue(BindableProperty property, out ValueContainer value)
		{
			value = default;
			if (_styled == null || property.Target == null || property.Kind == ValueKind.Invalid)
				return false;

			using var binding = PropertyBindingCollection.Bind(_styled.gameObject, new[] { property });
			return binding.TryRead(property, out value);
		}

		// ── Style values ─────────────────────────────────────────────────────────

		// Sentinel key used to identify the baseline preview (cannot collide with style names).
		private const string BaselinePreviewKey = "\0__baseline__";

		/// <summary>The "no style active" value, drawn as the first row of the Styles list like the sheet inspector does.</summary>
		private void DrawBaselineRow()
		{
			using (new GUILayout.HorizontalScope())
			{
				using (new EditorGUI.DisabledScope(true))
					EditorGUILayout.TextField(string.Empty, GUILayout.MinWidth(50f));
				var nameRect = GUILayoutUtility.GetLastRect();
				EditorGUI.LabelField(nameRect, new GUIContent("Baseline", "The value used when no style is active."), StylingEditorGUI.Styles.ItalicLabel);

				var cellRect = GUILayoutUtility.GetRect(50f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
				DrawValueCell(cellRect, _baselineProp, _baselineThemeKeyProp, includePullFromScene: true);

				StylingEditorGUI.ButtonGroupHoldButton(
					new GUIContent("Preview", "Hold to preview the baseline (no styles active)"),
					buttonIndex: 0,
					buttonCount: 2,
					onHoldStart: () => BeginPreview(BaselinePreviewKey),
					onHoldEnd: EndPreview,
					options: GUILayout.Width(64f)
				);

				using (new EditorGUI.DisabledScope(true))
				{
					StylingEditorGUI.ButtonGroupButton(
						content: new GUIContent("✕", "The baseline cannot be removed"),
						buttonIndex: 1,
						buttonCount: 2,
						options: GUILayout.Width(24f));
				}
			}
		}

		private void DrawStyleValuesSection()
		{
			FoldoutGUI.Draw(
				expanded: StyleValuesFoldoutExpanded.Get(serializedObject),
				drawContent: () =>
				{
					FoldoutGUI.Title("Styles");
					GUILayout.FlexibleSpace();
					if (StylingEditorGUI.AddButton())
						AddStyleValue();
				},
				onToggle: value => StyleValuesFoldoutExpanded.Set(serializedObject, value)
			);

			if (!StyleValuesFoldoutExpanded.Get(serializedObject))
				return;

			FoldoutGUI.BeginContent();

			int removeAt = -1;

			StylingEditorGUI.BeginHoldButtonGroup();

			DrawBaselineRow();

			for (int i = 0; i < _styleValuesProp.arraySize; i++)
			{
				if (DrawStyleValueRow(i))
					removeAt = i;
			}

			StylingEditorGUI.EndHoldButtonGroup();

			if (removeAt >= 0)
			{
				EndPreview();
				_styleValuesProp.DeleteArrayElementAtIndex(removeAt);
			}

			FoldoutGUI.EndContent();
		}

		private bool DrawStyleValueRow(int index)
		{
			var element = _styleValuesProp.GetArrayElementAtIndex(index);
			var nameProp = element.FindPropertyRelative(nameof(StyledProperty.StyleValue.StyleName));
			var valueProp = element.FindPropertyRelative(nameof(StyledProperty.StyleValue.Value));
			var themeKeyProp = element.FindPropertyRelative(nameof(StyledProperty.StyleValue.ThemeKey));

			bool requestDelete = false;

			using (new GUILayout.HorizontalScope())
			{
				EditorGUILayout.PropertyField(nameProp, GUIContent.none, GUILayout.MinWidth(50f));

				var cellRect = GUILayoutUtility.GetRect(50f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
				DrawValueCell(cellRect, valueProp, themeKeyProp, includePullFromScene: false);

				var styleName = nameProp.stringValue;
				using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(styleName)))
				{
					StylingEditorGUI.ButtonGroupHoldButton(
						new GUIContent("Preview", "Hold to preview this style"),
						buttonIndex: 0,
						buttonCount: 2,
						onHoldStart: () => BeginPreview(styleName),
						onHoldEnd: EndPreview,
						options: GUILayout.Width(64f)
					);
				}

				if (StylingEditorGUI.ButtonGroupButton(
						content: new GUIContent("✕", "Remove this style value"),
						buttonIndex: 1,
						buttonCount: 2,
						options: GUILayout.Width(24f)))
				{
					requestDelete = true;
				}
			}

			return requestDelete;
		}

		private void AddStyleValue()
		{
			int index = _styleValuesProp.arraySize;
			_styleValuesProp.arraySize++;

			var element = _styleValuesProp.GetArrayElementAtIndex(index);
			element.FindPropertyRelative(nameof(StyledProperty.StyleValue.StyleName)).stringValue = string.Empty;
			element.FindPropertyRelative(nameof(StyledProperty.StyleValue.Value)).boxedValue = (ValueContainer)_baselineProp.boxedValue;
			element.FindPropertyRelative(nameof(StyledProperty.StyleValue.ThemeKey)).stringValue = string.Empty;
		}

		// ── Value cells ──────────────────────────────────────────────────────────

		private void DrawValueCell(Rect rect, SerializedProperty valueProp, SerializedProperty themeKeyProp, bool includePullFromScene)
		{
			var property = _styled.Property;
			var literal = (ValueContainer)valueProp.boxedValue;
			var themeKey = themeKeyProp.stringValue;
			var theme = ResolveTheme();

			// The menu check runs before the control is drawn: a colour field has its own right-click
			// copy/paste handler that would otherwise consume the event first.
			if (StyleCellEditorGUI.IsContextMenuEvent(Event.current, rect))
				ShowValueMenu(valueProp, themeKeyProp, includePullFromScene);

			if (!string.IsNullOrEmpty(themeKey))
			{
				StyleCellEditorGUI.DrawThemedCell(rect, theme, property, themeKey, literal);
				return;
			}

			var linkable = property.Kind != ValueKind.Invalid;
			var chipRect = linkable ? StyleCellEditorGUI.SplitLinkChip(ref rect) : default;

			EditorGUI.BeginChangeCheck();
			var newValue = PropertyBindingsEditorGUI.ValueContainerField(rect, property, literal);
			if (EditorGUI.EndChangeCheck())
				valueProp.boxedValue = newValue;

			if (linkable && StyleCellEditorGUI.DrawUnlinkedChip(chipRect, theme))
				ShowValueMenu(valueProp, themeKeyProp, includePullFromScene);
		}

		private void ShowValueMenu(SerializedProperty valueProp, SerializedProperty themeKeyProp, bool includePullFromScene)
		{
			var valuePath = valueProp.propertyPath;
			var themeKeyPath = themeKeyProp.propertyPath;

			var kind = _styled.Property.Kind;
			var menu = new GenericMenu();

			// Same menu shape as the sheet table: Copy/Paste, then theme linking, then cell-specific extras.
			StyleCellEditorGUI.AddClipboardMenuItems(
				menu,
				kind,
				hasValue: kind != ValueKind.Invalid,
				currentValue: (ValueContainer)valueProp.boxedValue,
				paste: value => SetValue(valuePath, value));

			StyleCellEditorGUI.AddThemeMenuItems(
				menu,
				ResolveTheme(),
				kind,
				themeKeyProp.stringValue,
				key => SetThemeKey(themeKeyPath, key));

			if (includePullFromScene)
			{
				menu.AddSeparator(string.Empty);

				if (_styled.Property.Target != null && _styled.Property.Kind != ValueKind.Invalid)
					menu.AddItem(new GUIContent("Pull from Scene"), false, () => PullFromScene(valuePath));
				else
					menu.AddDisabledItem(new GUIContent("Pull from Scene"));
			}

			menu.ShowAsContext();
			Event.current.Use();
		}

		private void SetThemeKey(string themeKeyPath, string key)
		{
			serializedObject.Update();
			var prop = serializedObject.FindProperty(themeKeyPath);
			if (prop == null)
				return;

			prop.stringValue = key ?? string.Empty;
			serializedObject.ApplyModifiedProperties();

			if (_styled != null && _styled.IsTransitioning)
				_styled.CompleteTransitionImmediate();
		}

		private void PullFromScene(string valuePath)
		{
			if (TryReadLiveValue(_styled.Property, out var liveValue))
				SetValue(valuePath, liveValue);
		}

		private void SetValue(string valuePath, ValueContainer value)
		{
			serializedObject.Update();
			var prop = serializedObject.FindProperty(valuePath);
			if (prop == null)
				return;

			prop.boxedValue = value;
			serializedObject.ApplyModifiedProperties();

			if (_styled != null && _styled.IsTransitioning)
				_styled.CompleteTransitionImmediate();
		}

		private StyleTheme ResolveTheme() => _styled != null ? StyleThemeSource.Resolve(_styled.gameObject) : null;

		// ── Transition ───────────────────────────────────────────────────────────

		private void DrawTransitionSection()
		{
			FoldoutGUI.Draw(
				expanded: TransitionFoldoutExpanded.Get(serializedObject),
				drawContent: () =>
				{
					FoldoutGUI.Title("Transition");
					GUILayout.FlexibleSpace();
				},
				onToggle: value => TransitionFoldoutExpanded.Set(serializedObject, value)
			);

			if (!TransitionFoldoutExpanded.Get(serializedObject))
				return;

			FoldoutGUI.BeginContent();

			var durationProp = _transitionProp.FindPropertyRelative(nameof(StylePropertyTransition.Duration));
			var easeProp = _transitionProp.FindPropertyRelative(nameof(StylePropertyTransition.EaseType));
			var interpolationProp = _transitionProp.FindPropertyRelative(nameof(StylePropertyTransition.Interpolation));
			var discreteProp = _transitionProp.FindPropertyRelative(nameof(StylePropertyTransition.DiscreteValueSelection));

			EditorGUILayout.PropertyField(durationProp, new GUIContent("Duration", "0 applies the value instantly."));
			if (durationProp.floatValue < 0f)
				durationProp.floatValue = 0f;

			var kind = _styled.Property.Kind;
			var continuous = IsContinuousKind(kind);

			using (new EditorGUI.DisabledScope(durationProp.floatValue <= 0f))
			{
				if (continuous)
				{
					var easeRect = EditorGUI.PrefixLabel(
						EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight),
						new GUIContent("Ease Type"));

					CoreEditorGUI.EaseTypePopup(easeRect, (EaseType)easeProp.enumValueIndex, newEase =>
					{
						serializedObject.Update();
						easeProp.enumValueIndex = (int)newEase;
						serializedObject.ApplyModifiedProperties();
					});
				}

				DrawInterpolationField(kind, interpolationProp, discreteProp);
			}

			FoldoutGUI.EndContent();
		}

		private void DrawInterpolationField(ValueKind kind, SerializedProperty interpolationProp, SerializedProperty discreteProp)
		{
			switch (kind)
			{
				case ValueKind.Color:
				{
					var colorProp = interpolationProp.FindPropertyRelative(nameof(InterpolationConfig.Color));
					var rect = EditorGUI.PrefixLabel(
						EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight),
						new GUIContent("Interpolation"));

					CoreEditorGUI.ColorInterpolationModePopup(rect, (ColorInterpolationMode)colorProp.enumValueIndex, newMode =>
					{
						serializedObject.Update();
						colorProp.enumValueIndex = (int)newMode;
						serializedObject.ApplyModifiedProperties();
					});
					break;
				}

				case ValueKind.Vector2:
					EditorGUILayout.PropertyField(
						interpolationProp.FindPropertyRelative(nameof(InterpolationConfig.Vector2)),
						new GUIContent("Interpolation"));
					break;

				case ValueKind.Vector3:
					EditorGUILayout.PropertyField(
						interpolationProp.FindPropertyRelative(nameof(InterpolationConfig.Vector3)),
						new GUIContent("Interpolation"));
					break;

				case ValueKind.Quaternion:
					EditorGUILayout.PropertyField(
						interpolationProp.FindPropertyRelative(nameof(InterpolationConfig.Rotation)),
						new GUIContent("Interpolation"));
					break;

				case ValueKind.Invalid:
					break;

				default:
				{
					var rect = EditorGUI.PrefixLabel(
						EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight),
						new GUIContent("Value Selection", "Which side of the blend wins while a discrete value transitions."));

					CoreEditorGUI.DiscreteValueSelectionModePopup(rect, (DiscreteValueSelectionMode)discreteProp.enumValueIndex, newMode =>
					{
						serializedObject.Update();
						discreteProp.enumValueIndex = (int)newMode;
						serializedObject.ApplyModifiedProperties();
					});
					break;
				}
			}
		}

		private static bool IsContinuousKind(ValueKind kind)
		{
			switch (kind)
			{
				case ValueKind.Float:
				case ValueKind.Vector2:
				case ValueKind.Vector3:
				case ValueKind.Vector4:
				case ValueKind.Color:
				case ValueKind.Quaternion:
					return true;
				default:
					return false;
			}
		}

		// ── Preview ──────────────────────────────────────────────────────────────

		private void BeginPreview(string styleName)
		{
			if (string.IsNullOrWhiteSpace(styleName) || _styled == null)
			{
				EndPreview();
				return;
			}

			if (_isPreviewing && _previewStyleName == styleName)
				return;

			_previewOverride?.Dispose();
			_previewOverride = StylingSystem.StylingOverrideScope(
				_styled.gameObject,
				styleName == BaselinePreviewKey ? Array.Empty<string>() : new[] { styleName });
			_isPreviewing = true;
			_previewStyleName = styleName;

			SceneView.RepaintAll();
		}

		private void EndPreview()
		{
			if (!_isPreviewing && _previewOverride == null)
				return;

			_previewOverride?.Dispose();
			_previewOverride = null;
			_isPreviewing = false;
			_previewStyleName = null;

			SceneView.RepaintAll();
		}

		// ── Helpers ──────────────────────────────────────────────────────────────

		private void WriteTransition(StylePropertyTransition transition)
		{
			_transitionProp.FindPropertyRelative(nameof(StylePropertyTransition.Duration)).floatValue = transition.Duration;
			_transitionProp.FindPropertyRelative(nameof(StylePropertyTransition.EaseType)).enumValueIndex = (int)transition.EaseType;
			_transitionProp.FindPropertyRelative(nameof(StylePropertyTransition.DiscreteValueSelection)).enumValueIndex = (int)transition.DiscreteValueSelection;

			var interpolationProp = _transitionProp.FindPropertyRelative(nameof(StylePropertyTransition.Interpolation));
			interpolationProp.FindPropertyRelative(nameof(InterpolationConfig.Rotation)).enumValueIndex = (int)transition.Interpolation.Rotation;
			interpolationProp.FindPropertyRelative(nameof(InterpolationConfig.Color)).enumValueIndex = (int)transition.Interpolation.Color;
			interpolationProp.FindPropertyRelative(nameof(InterpolationConfig.Vector2)).enumValueIndex = (int)transition.Interpolation.Vector2;
			interpolationProp.FindPropertyRelative(nameof(InterpolationConfig.Vector3)).enumValueIndex = (int)transition.Interpolation.Vector3;
		}
	}
}
