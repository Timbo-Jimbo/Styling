using System;
using System.Collections.Generic;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Styling;
using TimboJimboEditor.PropertyBindings.Utility;
using UnityEditor;
using UnityEngine;
using UnityEngine.Pool;
using Object = UnityEngine.Object;

namespace TimboJimboEditor.Styling
{
	[CustomEditor(typeof(StyleSheet))]
	public sealed class StyleSheetEditor : Editor
    {
		private StyleSheet _sheet;
		private StylingOverrideScope _previewOverride;

		private StyleSheetValuesTable _valuesTable;
		private StyleSheetTransitionsTable _transitionsTable;
		private readonly List<Rect> _previewRects = new List<Rect>();
		private readonly List<string> _previewRectNames = new List<string>();
		
		private bool _propertyTableExpanded;
		private bool _isPreviewing;
		private string _previewStyleName;
		private static GUIStyle PreviewChipText;

		private static bool StylesFoldoutExpanded
		{
			get => SessionState.GetBool("StyleSheetEditor.StylesFoldoutExpanded", true);
			set => SessionState.SetBool("StyleSheetEditor.StylesFoldoutExpanded", value);
		}

		private static bool PropertyTableFoldoutExpanded
		{
			get => SessionState.GetBool("StyleSheetEditor.PropertyTableFoldoutExpanded", true);
			set => SessionState.SetBool("StyleSheetEditor.PropertyTableFoldoutExpanded", value);
		}

		private static bool ShowTransitions 
		{
			get => SessionState.GetBool("StyleSheetEditor.ShowTransitions", false);
			set => SessionState.SetBool("StyleSheetEditor.ShowTransitions", value);
		}


		private void OnEnable()
		{
			_sheet = (StyleSheet)target;
		}

		private void OnDisable()
		{
			EndPreview();
			DisposeTables();
		}

		public override void OnInspectorGUI()
		{
			if (_isPreviewing && Event.current.rawType == EventType.MouseUp && GUIUtility.hotControl == 0)
			{
				EndPreview();
				Repaint();
			}

			serializedObject.Update();

			DrawStylesSection();
			DrawPropertyTable();

			serializedObject.ApplyModifiedProperties();
		}

		private void DrawStylesSection()
		{
			var stylesProp = serializedObject.FindProperty("_styles");
			if (stylesProp == null)
				return;
			
			StylingEditorGUI.DrawFoldout(
				expanded: StylesFoldoutExpanded,
				drawContent: () =>
				{
					EditorGUILayout.LabelField("Styles", EditorStyles.boldLabel);
					GUILayout.FlexibleSpace();
					using (new EditorGUI.DisabledScope(StyleSheetRecordingSession.IsRecording))
					{
						if (StylingEditorGUI.AddButton())
						{
							StyleSheetRecordingSession.StartCreating(_sheet);
						}
					}

				},
				onToggle: value => StylesFoldoutExpanded = value
			);
			
			if (!StylesFoldoutExpanded)
				return;
			
			EditorGUILayout.Space(EditorGUIUtility.standardVerticalSpacing * 2f);

			bool isAnyRecording = StyleSheetRecordingSession.IsRecording;
			bool isThisRecording = isAnyRecording && StyleSheetRecordingSession.Target == _sheet;

			if (isThisRecording)
			{
				string label = StyleSheetRecordingSession.IsEditingBaseline
					? "Editing baseline"
					: (StyleSheetRecordingSession.IsCreatingNew ? "Creating" : "Editing")
					  + $" '{StyleSheetRecordingSession.EditingStyleName}'";
				EditorGUILayout.HelpBox($"{label} — make changes in the Scene View.", MessageType.Info);
			}
			else if (isAnyRecording)
			{
				var recordingTargetName = StyleSheetRecordingSession.Target != null
					? StyleSheetRecordingSession.Target.name
					: "(unknown)";
				EditorGUILayout.HelpBox($"Currently editing '{recordingTargetName}'. Finish that session first.", MessageType.Warning);
			}

			DrawStyleControlsList(isAnyRecording);
			EditorGUILayout.Space(EditorGUIUtility.standardVerticalSpacing * 2f);
		}

		private void DrawStyleControlsList(bool isAnyRecording)
		{
			// Baseline row.
			using (new GUILayout.HorizontalScope())
			{
				using (new EditorGUI.DisabledScope(true))
					EditorGUILayout.TextField("", GUILayout.ExpandWidth(true));
				var rect = GUILayoutUtility.GetLastRect();
				EditorGUI.LabelField(rect, "Baseline", StylingEditorGUI.Styles.ItalicLabel);

				using (new EditorGUI.DisabledScope(isAnyRecording))
				{
					if (GUILayout.Button("Edit", GUILayout.Width(50f)))
						StyleSheetRecordingSession.StartEditingBaseline(_sheet);
						
					using (new EditorGUI.DisabledScope(true))
						GUILayout.Button("Delete", GUILayout.Width(60f));
				}
			}

			int removeAt = -1;
			var styles = _sheet.Styles;
			for (int i = 0; i < styles.Count; i++)
			{
				if (DrawStyleControlsRow(styles[i], i, isAnyRecording))
					removeAt = i;
			}

			if (removeAt >= 0)
			{
				var styleName = styles[removeAt].Name;
				if (_previewStyleName == styleName)
					EndPreview();

				Undo.RecordObject(_sheet, "Delete Style");
				_sheet.DeleteStyle(styleName);
				EditorUtility.SetDirty(_sheet);
			}
		}

		private bool DrawStyleControlsRow(Style style, int styleIndex, bool isAnyRecording)
		{
			bool requestDelete = false;

			using (new GUILayout.HorizontalScope())
			{
				using (new EditorGUI.DisabledScope(isAnyRecording))
				{
					EditorGUI.BeginChangeCheck();
					string newName = EditorGUILayout.TextField(style.Name, GUILayout.ExpandWidth(true));
					if (EditorGUI.EndChangeCheck())
					{
						newName = newName.Trim();
						if (!string.IsNullOrWhiteSpace(newName)
							&& newName != style.Name
							&& !StyleNameExists(newName))
						{
							Undo.RecordObject(_sheet, "Rename Style");
							_sheet.RenameStyle(style.Name, newName);
							EditorUtility.SetDirty(_sheet);
						}
					}

					if (GUILayout.Button("Edit", GUILayout.Width(50f)))
						StyleSheetRecordingSession.StartEditing(_sheet, styleIndex);

					if (GUILayout.Button("Delete", GUILayout.Width(60f)))
						requestDelete = true;
				}
			}

			return requestDelete;
		}

		// Sentinel key used to identify the baseline preview button (cannot collide with style names).
		private const string BaselinePreviewKey = "\0__baseline__";

		private void DrawPropertyTable()
		{
			EnsureTables();
			if (_transitionsTable == null)
				return;

			StylingEditorGUI.DrawFoldout(
				expanded: PropertyTableFoldoutExpanded,
				drawContent: () =>
				{
					EditorGUILayout.LabelField("Properties", EditorStyles.boldLabel);
					GUILayout.FlexibleSpace();

					StylingEditorGUILayout.SegmentedControl(
						selected: ShowTransitions ? 1 : 0, 
						labels: new[] { "Values", "Transitions" }, 
						onSelected: i =>
						{
							PropertyTableFoldoutExpanded = true;
							var wantsToShowTransitions = i == 1;
							if (wantsToShowTransitions != ShowTransitions)
								ShowTransitions = wantsToShowTransitions;
						});
				},
				onToggle: value => PropertyTableFoldoutExpanded = value
			);

			if (!PropertyTableFoldoutExpanded)
				return;

			EditorGUILayout.Space(EditorGUIUtility.standardVerticalSpacing * 2f);
			StyleSheetPropertyTable table = ShowTransitions ? _transitionsTable : _valuesTable;
			if (table == null) return;	

			using (new EditorGUI.DisabledScope(StyleSheetRecordingSession.IsRecording))
			{
				GUILayout.Space(4f);
				float treeHeight = Mathf.Max(table.TreeView.totalHeight, EditorGUIUtility.singleLineHeight);
				treeHeight += EditorGUIUtility.singleLineHeight; // extra space for when the vertical scroll bar appears
				var rect = GUILayoutUtility.GetRect(0f, 10000f, treeHeight, treeHeight);
				table.TreeView.OnGUI(rect);
			}

			EditorGUILayout.Space(EditorGUIUtility.standardVerticalSpacing * 2f);
		}


		private void EnsureTables()
		{
			int signature = ComputeTableSignature();

			if (_valuesTable == null || _valuesTable.Signature != signature)
			{
				_valuesTable?.Dispose();
				_valuesTable = StyleSheetValuesTable.Create(_sheet, signature);
			}

			if (_transitionsTable == null || _transitionsTable.Signature != signature)
			{
				_transitionsTable?.Dispose();
				_transitionsTable = StyleSheetTransitionsTable.Create(_sheet, signature);
			}
		}

		private int ComputeTableSignature()
		{
			unchecked
			{
				int hash = 17;
				var baseline = _sheet.BaselineValues;
				hash = hash * 31 + baseline.Count;
				for (int i = 0; i < baseline.Count; i++)
					hash = hash * 31 + baseline[i].Property.GetHashCode();

				var styles = _sheet.Styles;
				hash = hash * 31 + styles.Count;
				for (int s = 0; s < styles.Count; s++)
				{
					hash = hash * 31 + (styles[s].Name?.GetHashCode() ?? 0);
					hash = hash * 31 + styles[s].PropertyValues.Count;
					for (int p = 0; p < styles[s].PropertyValues.Count; p++)
						hash = hash * 31 + styles[s].PropertyValues[p].Property.GetHashCode();
				}
				return hash;
			}
		}

		private void DisposeTables()
		{
			_valuesTable?.Dispose();
			_valuesTable = null;

			_transitionsTable?.Dispose();
			_transitionsTable = null;
		}

		private bool StyleNameExists(string name)
		{
			for (int i = 0; i < _sheet.Styles.Count; i++)
			{
				if (_sheet.Styles[i].Name == name)
					return true;
			}

			return false;
		}

		public override bool HasPreviewGUI() => _sheet != null;

		public override GUIContent GetPreviewTitle() => new GUIContent("Styling");

		public override bool RequiresConstantRepaint() => _isPreviewing;

		public override void OnInteractivePreviewGUI(Rect rect, GUIStyle background)
		{
			DrawPreviewArea(rect, background);
		}

		private void DrawPreviewArea(Rect rect, GUIStyle background)
		{
			if (Event.current.type == EventType.Repaint)
				background?.Draw(rect, false, false, false, false);

			_previewRects.Clear();
			_previewRectNames.Clear();

			const float padding = 6f;
			const float sectionSpacing = 8f;
			const float labelSpacing = 3f;

			var contentRect = new Rect(
				rect.x + padding,
				rect.y + padding,
				Mathf.Max(0f, rect.width - padding * 2f),
				Mathf.Max(0f, rect.height - padding * 2f));

			if (contentRect.width <= 0f || contentRect.height <= 0f)
				return;

			float y = contentRect.y;

			EditorGUI.LabelField(
				new Rect(contentRect.x, y, contentRect.width, EditorGUIUtility.singleLineHeight),
				"Hold to Preview",
				EditorStyles.boldLabel);
			y += EditorGUIUtility.singleLineHeight + labelSpacing;

			if (StyleSheetRecordingSession.IsRecording)
			{
				EditorGUI.LabelField(
					new Rect(contentRect.x, y, contentRect.width, EditorGUIUtility.singleLineHeight),
					"Preview is disabled while recording.",
					EditorStyles.wordWrappedMiniLabel);
				y += EditorGUIUtility.singleLineHeight;
			}
			else if (TryGetPreviewStyleNames(out var styleNames))
			{
				y = DrawPreviewButtons(contentRect, y, styleNames);
			}
			else
			{
				EditorGUI.LabelField(
					new Rect(contentRect.x, y, contentRect.width, EditorGUIUtility.singleLineHeight),
					"No previewable styles found.",
					EditorStyles.wordWrappedMiniLabel);
				y += EditorGUIUtility.singleLineHeight;
			}

			y += sectionSpacing;
			DrawGameObjectScopeSection(contentRect, y);

			if (!StyleSheetRecordingSession.IsRecording)
				HandlePreviewEvents();
		}

		private float DrawPreviewButtons(Rect contentRect, float startY, IReadOnlyList<string> styleNames)
		{
			const float verticalSpacing = 4f;
			const float minItemWidth = 72f;
			const float itemHeight = 24f;

			float x = contentRect.x;
			float y = startY;
			float maxX = contentRect.xMax;

			for (int i = 0; i < styleNames.Count; i++)
			{
				var styleName = styleNames[i];
				var size = EditorStyles.miniButton.CalcSize(new GUIContent(styleName));
				float width = Mathf.Max(minItemWidth, size.x + 16f);

				if (x > contentRect.x && x + width > maxX)
				{
					x = contentRect.x;
					y += itemHeight + verticalSpacing;
				}

				var itemRect = new Rect(x, y, Mathf.Min(width, contentRect.width), itemHeight);
				_previewRects.Add(itemRect);
				_previewRectNames.Add(styleName);
				DrawPreviewChip(itemRect, styleName, _isPreviewing && _previewStyleName == styleName);

				x = itemRect.xMax + verticalSpacing;
			}

			return y + itemHeight;
		}

		private static void DrawPreviewChip(Rect rect, string styleName, bool isActive)
		{
			if (Event.current.type != EventType.Repaint)
				return;

			var fill = isActive
				? GUI.skin.settings.selectionColor
				: new Color(0.24f, 0.24f, 0.24f, 1f);
			var border = isActive
				? GUI.skin.settings.selectionColor * new Color(0.85f, 0.85f, 0.85f, 1f)
				: new Color(0.17f, 0.17f, 0.17f, 1f);
			border.a = 1f;

			EditorGUI.DrawRect(rect, border);
			var innerRect = new Rect(rect.x + 1f, rect.y + 1f, Mathf.Max(0f, rect.width - 2f), Mathf.Max(0f, rect.height - 2f));
			EditorGUI.DrawRect(innerRect, fill);

			if (PreviewChipText == null)
			{
				PreviewChipText = new GUIStyle(EditorStyles.label)
				{
					alignment = TextAnchor.MiddleCenter
				};
			}

			GUI.Label(innerRect, styleName, PreviewChipText);
		}

		private void DrawGameObjectScopeSection(Rect contentRect, float startY)
		{
			if (_sheet == null || _sheet.gameObject == null)
			{
				EditorGUI.LabelField(
					new Rect(contentRect.x, startY, contentRect.width, EditorGUIUtility.singleLineHeight),
					"No GameObject context available.",
					EditorStyles.wordWrappedMiniLabel);
				return;
			}

			using (ListPool<string>.Get(out var subtreeStyleNames))
			using (ListPool<StyleActivation>.Get(out var resolvedActivations))
			{
				StylingSystem.GetSupportedStyleNames(_sheet.gameObject, subtreeStyleNames, includeChildren: true);
				StylingSystem.GetStyleActivations(_sheet.gameObject, resolvedActivations);

				float y = startY;
				y += DrawInfoCard(
					contentRect,
					y,
					"Styles in Subtree",
					subtreeStyleNames.Count > 0 ? string.Join(", ", subtreeStyleNames) : "None");
				y += 4f;
				DrawInfoCard(
					contentRect,
					y,
					"Resolved Activations",
					FormatActivationSummary(resolvedActivations));
			}
		}

		private static float DrawInfoCard(Rect contentRect, float startY, string title, string body)
		{
			const float innerPadding = 0f;
			const float lineSpacing = 2f;

			float bodyWidth = Mathf.Max(20f, contentRect.width - innerPadding * 2f);
			float bodyHeight = EditorStyles.wordWrappedLabel.CalcHeight(new GUIContent(body), bodyWidth);
			float cardHeight = innerPadding * 2f + EditorGUIUtility.singleLineHeight + lineSpacing + bodyHeight;

			var cardRect = new Rect(contentRect.x, startY, contentRect.width, cardHeight);

			var headerRect = new Rect(
				cardRect.x + innerPadding,
				cardRect.y + innerPadding,
				cardRect.width - innerPadding * 2f,
				EditorGUIUtility.singleLineHeight);
			EditorGUI.LabelField(headerRect, title, EditorStyles.boldLabel);

			var bodyRect = new Rect(
				cardRect.x + innerPadding,
				headerRect.yMax + lineSpacing,
				cardRect.width - innerPadding * 2f,
				cardRect.height - innerPadding * 2f - EditorGUIUtility.singleLineHeight - lineSpacing);
			EditorGUI.LabelField(bodyRect, body, EditorStyles.wordWrappedLabel);

			return cardHeight;
		}

		private static string FormatActivationSummary(List<StyleActivation> activations)
		{
			if (activations == null || activations.Count == 0)
				return "None";

			using (ListPool<string>.Get(out var parts))
			{
				for (int i = 0; i < activations.Count; i++)
				{
					var activation = activations[i];
					parts.Add($"{activation.Name}: {(activation.Active ? "Active" : "Inactive")}");
				}

				return string.Join(", ", parts);
			}
		}

		private bool TryGetPreviewStyleNames(out List<string> styleNames)
		{
			styleNames = new List<string>();
			if (_sheet == null || _sheet.gameObject == null)
				return false;

			StylingSystem.GetSupportedStyleNames(_sheet.gameObject, styleNames, includeChildren: true);
			return styleNames.Count > 0;
		}

		private void HandlePreviewEvents()
		{
			int controlId = GUIUtility.GetControlID(FocusType.Passive);
			var evt = Event.current;

			switch (evt.GetTypeForControl(controlId))
			{
				case EventType.MouseDown:
					if (evt.button == 0)
					{
						var hit = HitTestPreviewRects(evt.mousePosition);
						if (hit != null)
						{
							GUIUtility.hotControl = controlId;
							BeginPreview(hit);
							evt.Use();
						}
					}
					break;

				case EventType.MouseDrag:
					if (GUIUtility.hotControl == controlId)
					{
						var hit = HitTestPreviewRects(evt.mousePosition);
						if (hit != _previewStyleName)
						{
							if (hit != null)
								BeginPreview(hit);
							else
								EndPreview();

							Repaint();
						}

						evt.Use();
					}
					break;

				case EventType.MouseUp:
					if (GUIUtility.hotControl == controlId && evt.button == 0)
					{
						GUIUtility.hotControl = 0;
						EndPreview();
						evt.Use();
						Repaint();
					}
					break;
			}
		}

		private string HitTestPreviewRects(Vector2 mousePos)
		{
			if (_previewRects.Count == 0)
				return null;

			Rect bounds = default;
			for (int i = 0; i < _previewRects.Count; i++)
			{
				if (i == 0)
				{
					bounds = _previewRects[i];
				}
				else
				{
					bounds = Rect.MinMaxRect(
						Mathf.Min(bounds.xMin, _previewRects[i].xMin),
						Mathf.Min(bounds.yMin, _previewRects[i].yMin),
						Mathf.Max(bounds.xMax, _previewRects[i].xMax),
						Mathf.Max(bounds.yMax, _previewRects[i].yMax));
				}
			}

			if (!bounds.Contains(mousePos))
				return null;

			int hit = -1;
			float hitDistance = float.MaxValue;
			for (int i = 0; i < _previewRects.Count; i++)
			{
				if (_previewRects[i].Contains(mousePos))
				{
					hit = i;
					break;
				}

				var closestPoint = new Vector2(
					Mathf.Clamp(mousePos.x, _previewRects[i].xMin, _previewRects[i].xMax),
					Mathf.Clamp(mousePos.y, _previewRects[i].yMin, _previewRects[i].yMax));
				float distance = Vector2.Distance(mousePos, closestPoint);
				if (distance < hitDistance)
				{
					hitDistance = distance;
					hit = i;
				}
			}

			return hit >= 0 ? _previewRectNames[hit] : null;
		}

		internal void BeginPreview(string styleName)
		{
			if (string.IsNullOrWhiteSpace(styleName) && styleName != BaselinePreviewKey)
			{
				EndPreview();
				return;
			}

			if (_isPreviewing && _previewStyleName == styleName)
				return;

			_previewOverride?.Dispose();
			var activeStyles = styleName == BaselinePreviewKey
				? System.Array.Empty<string>()
				: new[] { styleName };
			_previewOverride = StylingSystem.StylingOverrideScope(_sheet.gameObject, activeStyles);
			_isPreviewing = true;
			_previewStyleName = styleName;

			SceneView.RepaintAll();
		}

		internal void EndPreview()
		{
			if (!_isPreviewing && _previewOverride == null)
				return;

			_previewOverride?.Dispose();
			_previewOverride = null;
			_isPreviewing = false;
			_previewStyleName = null;

			SceneView.RepaintAll();
		}

	}
}
