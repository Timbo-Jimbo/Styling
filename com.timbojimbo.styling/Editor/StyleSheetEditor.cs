using System.Collections.Generic;
using TimboJimbo.Styling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Pool;

namespace TimboJimboEditor.Styling
{
	[CustomEditor(typeof(StyleSheet))]
	public sealed class StyleSheetEditor : Editor
    {
		private StyleSheet _sheet;
		private StylingOverrideScope _previewOverride;

		private StyleSheetValuesTable _valuesTable;
		private StyleSheetTransitionsTable _transitionsTable;
		
		private bool _isPreviewing;
		private string _previewStyleName;

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
			StylingEditorGUI.BeginHoldButtonGroup(enabled: !isAnyRecording);

			// Baseline row.
			using (new GUILayout.HorizontalScope())
			{
				using (new EditorGUI.DisabledScope(true))
					EditorGUILayout.TextField("", GUILayout.ExpandWidth(true));
				var rect = GUILayoutUtility.GetLastRect();
				EditorGUI.LabelField(rect, "Baseline", StylingEditorGUI.Styles.ItalicLabel);

				using (new EditorGUI.DisabledScope(isAnyRecording))
				{
					StylingEditorGUI.ButtonGroupHoldButton(
						new GUIContent("Preview"), 
						buttonIndex: 0, 
						buttonCount: 3,
						onHoldStart: () => BeginPreview(BaselinePreviewKey),
						onHoldEnd: () => EndPreview(), 
						options: GUILayout.Width(92f)
					);

					if(StylingEditorGUI.ButtonGroupButton(new GUIContent("Edit"), buttonIndex: 1, buttonCount: 3, options: GUILayout.Width(64)))
						StyleSheetRecordingSession.StartEditingBaseline(_sheet);

					using (new EditorGUI.DisabledScope(true))
					{
						StylingEditorGUI.ButtonGroupButton(
							content: new GUIContent("✕", tooltip: "Remove this style activation"),
							buttonIndex: 2,
							buttonCount: 3,
							options: GUILayout.Width(24f)
						);
					}
				}
			}

			int removeAt = -1;
			var styles = _sheet.Styles;
			for (int i = 0; i < styles.Count; i++)
			{
				if (DrawStyleControlsRow(styles[i], i, isAnyRecording))
					removeAt = i;
			}

			StylingEditorGUI.EndHoldButtonGroup();

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

					StylingEditorGUI.ButtonGroupHoldButton(
						new GUIContent("Preview"), 
						buttonIndex: 0, 
						buttonCount: 3,
						onHoldStart: () => BeginPreview(style.Name),
						onHoldEnd: () => EndPreview(), 
						options: GUILayout.Width(92)
					);

					if (StylingEditorGUI.ButtonGroupButton(new GUIContent("Edit"), buttonIndex: 1, buttonCount: 3, options: GUILayout.Width(64)))
						StyleSheetRecordingSession.StartEditing(_sheet, styleIndex);

					if(StylingEditorGUI.ButtonGroupButton(
						content: new GUIContent("✕", tooltip: "Remove this style activation"),
						buttonIndex: 2,
						buttonCount: 3,
						options: GUILayout.Width(24f)
					))
					{
						requestDelete = true;
					}
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
						}, 
						GUILayout.Width(180f));
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

			const float padding = 6f;

			var contentRect = new Rect(
				rect.x + padding,
				rect.y + padding,
				Mathf.Max(0f, rect.width - padding * 2f),
				Mathf.Max(0f, rect.height - padding * 2f));

			if (contentRect.width <= 0f || contentRect.height <= 0f)
				return;

			DrawGameObjectScopeSection(contentRect, contentRect.y);
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
