using System;
using System.Collections.Generic;
using System.Reflection;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Styling;
using TimboJimboEditor.PropertyBindings.Utility;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.Pool;
using Object = UnityEngine.Object;

namespace TimboJimboEditor.Styling
{
	[CustomEditor(typeof(StyleSheet))]
	public sealed class StyleSheetEditor : UnityEditor.Editor
	{
		private StyleSheet _sheet;
		private StylingOverrideScope _previewOverride;

		private PropertyTable _table;
		private readonly List<Rect> _previewRects = new List<Rect>();
		private readonly List<string> _previewRectNames = new List<string>();

		private bool _isPreviewing;
		private string _previewStyleName;

		private static GUIStyle PreviewChipText;

		private void OnEnable()
		{
			_sheet = (StyleSheet)target;
		}

		private void OnDisable()
		{
			EndPreview();
			DisposeTable();
		}

		public override void OnInspectorGUI()
		{
			if (_isPreviewing && Event.current.rawType == EventType.MouseUp && GUIUtility.hotControl == 0)
			{
				EndPreview();
				Repaint();
			}

			serializedObject.Update();

			DrawSettingsSection();
			EditorGUILayout.Space(8f);
			DrawStylesSection();

			serializedObject.ApplyModifiedProperties();
		}

		[MenuItem("CONTEXT/StyleSheet/Sync Baseline to Active Values")]
		private static void SyncBaselineToActiveValues(MenuCommand command)
		{
			if (!(command.context is StyleSheet sheet))
				return;

			Undo.RecordObject(sheet, "Sync Baseline to Active Values");
			Undo.RegisterFullObjectHierarchyUndo(sheet.gameObject, "Sync Baseline to Active Values");
			sheet.SyncBaselineToCurrentValues();
			if (sheet.IsTransitioning)
				sheet.CompleteTransitionImmediate();
			EditorUtility.SetDirty(sheet);
		}

		private void DrawSettingsSection()
		{
			EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);

			var enableInterpolationProp = serializedObject.FindProperty("_enableInterpolation");
			var transitionTimeProp = serializedObject.FindProperty("_transitionTime");

			EditorGUILayout.PropertyField(enableInterpolationProp, new GUIContent("Enable Interpolation"));
			if (enableInterpolationProp.boolValue)
			{
				EditorGUI.indentLevel++;
				EditorGUILayout.PropertyField(transitionTimeProp, new GUIContent("Transition Time"));
				EditorGUI.indentLevel--;
			}
		}

		private void DrawStylesSection()
		{
			var stylesProp = serializedObject.FindProperty("_styles");
			if (stylesProp == null)
				return;

			Foldouts.Draw(
				stylesProp,
				new GUIContent($"Styles ({_sheet.Styles.Count})"),
				toggleOnLabelClick: true,
				style: EditorStyles.foldoutHeader
            );

			if (!stylesProp.isExpanded)
				return;

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

			using (new EditorGUI.DisabledScope(isAnyRecording))
			{
				if (GUILayout.Button("Create Style", GUILayout.Height(24f)))
					StyleSheetRecordingSession.StartCreating(_sheet);
			}

			EditorGUILayout.Space(6f);
			DrawUnifiedTable(isAnyRecording);
		}

		private void DrawStyleControlsList(bool isAnyRecording)
		{
			// Baseline row.
			using (new GUILayout.HorizontalScope(EditorStyles.helpBox))
			{
				GUILayout.Label("Baseline", EditorStyles.boldLabel, GUILayout.Width(160f));
				GUILayout.FlexibleSpace();

				using (new EditorGUI.DisabledScope(isAnyRecording))
				{
					DrawHoldPreviewButton(BaselinePreviewKey, "Preview");

					if (GUILayout.Button("Sync from current", GUILayout.Width(130f)))
					{
						Undo.RecordObject(_sheet, "Sync Baseline to Active Values");
						Undo.RegisterFullObjectHierarchyUndo(_sheet.gameObject, "Sync Baseline to Active Values");
						_sheet.SyncBaselineToCurrentValues();
						if (_sheet.IsTransitioning)
							_sheet.CompleteTransitionImmediate();
						EditorUtility.SetDirty(_sheet);
					}

					if (GUILayout.Button("Edit", GUILayout.Width(50f)))
						StyleSheetRecordingSession.StartEditingBaseline(_sheet);
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

			using (new GUILayout.HorizontalScope(EditorStyles.helpBox))
			{
				using (new EditorGUI.DisabledScope(isAnyRecording))
				{
					EditorGUI.BeginChangeCheck();
					string newName = EditorGUILayout.TextField(style.Name, GUILayout.Width(160f));
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

					GUILayout.FlexibleSpace();
					DrawHoldPreviewButton(style.Name, "Preview");

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

		private void DrawHoldPreviewButton(string previewKey, string label)
		{
			var rect = GUILayoutUtility.GetRect(new GUIContent(label), GUI.skin.button, GUILayout.Width(60f));

			bool isActive = _isPreviewing && _previewStyleName == previewKey;
			if (Event.current.type == EventType.Repaint)
				GUI.Toggle(rect, isActive, label, GUI.skin.button);

			if (!GUI.enabled)
				return;

			int controlId = GUIUtility.GetControlID($"StylePreview:{previewKey}".GetHashCode(), FocusType.Passive, rect);
			var evt = Event.current;

			switch (evt.GetTypeForControl(controlId))
			{
				case EventType.MouseDown:
					if (evt.button == 0 && rect.Contains(evt.mousePosition))
					{
						GUIUtility.hotControl = controlId;
						BeginPreview(previewKey);
						evt.Use();
					}
					break;

				case EventType.MouseDrag:
					if (GUIUtility.hotControl == controlId)
					{
						if (rect.Contains(evt.mousePosition))
							BeginPreview(previewKey);
						else if (_previewStyleName == previewKey)
							EndPreview();

						evt.Use();
					}
					break;

				case EventType.MouseUp:
					if (GUIUtility.hotControl == controlId && evt.button == 0)
					{
						GUIUtility.hotControl = 0;
						if (_previewStyleName == previewKey)
							EndPreview();
						evt.Use();
					}
					break;
			}
		}

		private void DrawUnifiedTable(bool isAnyRecording)
		{
			EnsureTable();
			if (_table == null)
				return;

			using (new EditorGUI.DisabledScope(isAnyRecording))
			{
				GUILayout.Space(4f);
				float treeHeight = Mathf.Max(_table.TreeView.totalHeight, EditorGUIUtility.singleLineHeight);
				var rect = GUILayoutUtility.GetRect(0f, 10000f, treeHeight, treeHeight);
				_table.TreeView.OnGUI(rect);
			}
		}

		private void EnsureTable()
		{
			int signature = ComputeTableSignature();
			if (_table != null && _table.Signature == signature)
				return;

			_table?.Dispose();
			_table = PropertyTable.Create(_sheet, signature);
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

		private void DisposeTable()
		{
			_table?.Dispose();
			_table = null;
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

		internal sealed class PropertyTable : IDisposable
		{
			public readonly int Signature;
			public readonly TreeViewState<int> State;
			public readonly MultiColumnHeaderState HeaderState;
			public readonly StyleSheetTreeView TreeView;

			private PropertyTable(int signature, TreeViewState<int> state, MultiColumnHeaderState headerState, StyleSheetTreeView treeView)
			{
				Signature = signature;
				State = state;
				HeaderState = headerState;
				TreeView = treeView;
			}

			public static PropertyTable Create(StyleSheet sheet, int signature)
			{
				var state = new TreeViewState<int>();

				// Build column targets: [Property] [Baseline] [Style0] [Style1] ... [Interp]
				var styles = sheet.Styles;
				var targets = new ColumnTarget[1 + 1 + styles.Count + 1];
				targets[0] = new ColumnTarget { Kind = ColumnTargetKind.Property };
				targets[1] = new ColumnTarget { Kind = ColumnTargetKind.Baseline };
				for (int i = 0; i < styles.Count; i++)
					targets[2 + i] = new ColumnTarget { Kind = ColumnTargetKind.Style, StyleName = styles[i].Name };
				targets[targets.Length - 1] = new ColumnTarget { Kind = ColumnTargetKind.Interp };

				var columns = new MultiColumnHeaderState.Column[targets.Length];
				columns[0] = new MultiColumnHeaderState.Column
				{
					headerContent = new GUIContent("Property"),
					width = 220f,
					minWidth = 120f,
					autoResize = true,
					canSort = false
				};
				columns[1] = new MultiColumnHeaderState.Column
				{
					headerContent = new GUIContent("Baseline"),
					width = 140f,
					minWidth = 80f,
					autoResize = false,
					canSort = false
				};
				for (int i = 0; i < styles.Count; i++)
				{
					columns[2 + i] = new MultiColumnHeaderState.Column
					{
						headerContent = new GUIContent(styles[i].Name),
						width = 140f,
						minWidth = 80f,
						autoResize = false,
						canSort = false
					};
				}
				columns[columns.Length - 1] = new MultiColumnHeaderState.Column
				{
					headerContent = new GUIContent("Interp"),
					width = 110f,
					minWidth = 60f,
					maxWidth = 180f,
					autoResize = false,
					canSort = false
				};

				var headerState = new MultiColumnHeaderState(columns);
				var header = new StyleTableMultiColumnHeader(headerState, baselineColumnIndex: 1)
				{
					canSort = false,
					height = 22f
				};
				header.ResizeToFit();

				var tree = new StyleSheetTreeView(state, header, sheet, targets);
				tree.Reload();
				tree.ExpandAll();
				return new PropertyTable(signature, state, headerState, tree);
			}

			public void Dispose()
			{
			}
		}

		private sealed class StyleTableMultiColumnHeader : MultiColumnHeader
		{
			private static GUIStyle s_standardHeader;
			private static GUIStyle s_italicHeader;
			private readonly int _baselineColumnIndex;

			public StyleTableMultiColumnHeader(MultiColumnHeaderState state, int baselineColumnIndex) : base(state)
			{
				_baselineColumnIndex = baselineColumnIndex;
			}

			protected override void ColumnHeaderGUI(MultiColumnHeaderState.Column column, Rect headerRect, int columnIndex)
			{
				if(Event.current.type == EventType.Repaint)
				{
					if (columnIndex == _baselineColumnIndex)
					{
						if (s_italicHeader == null)
						{
							s_italicHeader = new GUIStyle(DefaultStyles.columnHeader)
							{
								fontStyle = FontStyle.Italic,
								alignment = TextAnchor.MiddleCenter
							};
						}

						s_italicHeader.Draw(headerRect, column.headerContent, false, false, false, false);
						return;
					}
					else if(columnIndex > _baselineColumnIndex)
					{
						if (s_standardHeader == null)
						{
							s_standardHeader = new GUIStyle(DefaultStyles.columnHeader)
							{
								fontStyle = FontStyle.Normal,
								alignment = TextAnchor.MiddleCenter
							};
						}

						s_standardHeader.Draw(headerRect, column.headerContent, false, false, false, false);
						return;
					}
				}

				base.ColumnHeaderGUI(column, headerRect, columnIndex);
			}
		}

		internal enum NodeType { GameObject, Component, Property }

		internal struct NodeData
		{
			public NodeType Type;
			public BindableProperty Property;
			public Object Target;
		}

		internal enum ColumnTargetKind { Property, Baseline, Style, Interp }

		internal struct ColumnTarget
		{
			public ColumnTargetKind Kind;
			public string StyleName;
		}

		internal sealed class StyleSheetTreeView : TreeView<int>
		{
			private readonly StyleSheet _sheet;
			private readonly ColumnTarget[] _columnTargets;
			private readonly Dictionary<int, NodeData> _nodes = new Dictionary<int, NodeData>();

			public StyleSheetTreeView(TreeViewState<int> state, MultiColumnHeader header, StyleSheet sheet, ColumnTarget[] columnTargets)
				: base(state, header)
			{
				_sheet = sheet;
				_columnTargets = columnTargets;
				showAlternatingRowBackgrounds = true;
				rowHeight = 22f;
				showBorder = false;
				cellMargin = 2f;
			}

			protected override TreeViewItem<int> BuildRoot()
			{
				var root = new TreeViewItem<int>(-1, -1, "Root");
				var items = new List<TreeViewItem<int>>();
				_nodes.Clear();

				// Union all properties across baseline + styles, preserving discovery order.
				var allProperties = new List<BindableProperty>();
				var seen = new HashSet<BindableProperty>();

				var baseline = _sheet.BaselineValues;
				for (int i = 0; i < baseline.Count; i++)
				{
					if (seen.Add(baseline[i].Property))
						allProperties.Add(baseline[i].Property);
				}

				var styles = _sheet.Styles;
				for (int s = 0; s < styles.Count; s++)
				{
					var pvs = styles[s].PropertyValues;
					for (int p = 0; p < pvs.Count; p++)
					{
						if (seen.Add(pvs[p].Property))
							allProperties.Add(pvs[p].Property);
					}
				}

				if (allProperties.Count == 0)
				{
					SetupParentsAndChildrenFromDepths(root, items);
					return root;
				}

				int id = 0;
				var goOrder = new List<GameObject>();
				var goDirect = new Dictionary<GameObject, List<BindableProperty>>();
				var goComponentOrder = new Dictionary<GameObject, List<Component>>();
				var componentProperties = new Dictionary<Component, List<BindableProperty>>();

				for (int i = 0; i < allProperties.Count; i++)
				{
					var property = allProperties[i];
					var component = property.Target as Component;
					var gameObject = component != null ? component.gameObject : property.Target as GameObject;
					if (gameObject == null)
						continue;

					if (!goDirect.ContainsKey(gameObject))
					{
						goOrder.Add(gameObject);
						goDirect[gameObject] = new List<BindableProperty>();
						goComponentOrder[gameObject] = new List<Component>();
					}

					if (component == null)
					{
						goDirect[gameObject].Add(property);
					}
					else
					{
						if (!componentProperties.TryGetValue(component, out var props))
						{
							props = new List<BindableProperty>();
							componentProperties[component] = props;
							goComponentOrder[gameObject].Add(component);
						}

						props.Add(property);
					}
				}

				for (int i = 0; i < goOrder.Count; i++)
				{
					var gameObject = goOrder[i];
					int goId = id++;
					items.Add(new TreeViewItem<int>(goId, 0, gameObject.name));
					_nodes[goId] = new NodeData { Type = NodeType.GameObject, Target = gameObject };

					var directProperties = goDirect[gameObject];
					for (int j = 0; j < directProperties.Count; j++)
					{
						var property = directProperties[j];
						int propertyId = id++;
						items.Add(new TreeViewItem<int>(propertyId, 1, ObjectNames.NicifyVariableName(property.Path)));
						_nodes[propertyId] = new NodeData { Type = NodeType.Property, Property = property };
					}

					var components = goComponentOrder[gameObject];
					for (int j = 0; j < components.Count; j++)
					{
						var component = components[j];
						var properties = componentProperties[component];
						int componentId = id++;
						var typeName = ObjectNames.NicifyVariableName(component.GetType().Name);
						items.Add(new TreeViewItem<int>(componentId, 1, $"{typeName} ({properties.Count})"));
						_nodes[componentId] = new NodeData { Type = NodeType.Component, Target = component };

						for (int k = 0; k < properties.Count; k++)
						{
							var property = properties[k];
							int propertyId = id++;
							items.Add(new TreeViewItem<int>(propertyId, 2, ObjectNames.NicifyVariableName(property.Path)));
							_nodes[propertyId] = new NodeData { Type = NodeType.Property, Property = property };
						}
					}
				}

				SetupParentsAndChildrenFromDepths(root, items);
				return root;
			}

			protected override void RowGUI(RowGUIArgs args)
			{
				if (!_nodes.TryGetValue(args.item.id, out var data))
					return;

				for (int i = 0; i < args.GetNumVisibleColumns(); i++)
				{
					var cellRect = args.GetCellRect(i);
					var column = args.GetColumn(i);
					CenterRectUsingSingleLineHeight(ref cellRect);
					DrawCell(cellRect, args.item, column, ref data);
				}
			}

			private void DrawCell(Rect rect, TreeViewItem<int> item, int column, ref NodeData data)
			{
				if (column < 0 || column >= _columnTargets.Length)
					return;

				var target = _columnTargets[column];
				switch (target.Kind)
				{
					case ColumnTargetKind.Property:
						DrawNameCell(rect, item, ref data);
						break;
					case ColumnTargetKind.Baseline:
						if (data.Type == NodeType.Property)
							DrawBaselineValueCell(rect, data.Property);
						break;
					case ColumnTargetKind.Style:
						if (data.Type == NodeType.Property)
							DrawStyleValueCell(rect, target.StyleName, data.Property);
						break;
					case ColumnTargetKind.Interp:
						if (data.Type == NodeType.Property)
							DrawInterpCell(rect, data.Property);
						break;
				}
			}

			private void DrawNameCell(Rect rect, TreeViewItem<int> item, ref NodeData data)
			{
				HandleNameCellContextClick(rect, item, data);
				rect.xMin += GetContentIndent(item);

				if (data.Type != NodeType.Property && data.Target != null)
				{
					var type = data.Type == NodeType.GameObject ? typeof(GameObject) : data.Target.GetType();
					var icon = EditorGUIUtility.ObjectContent(data.Target, type).image;
					if (icon != null)
					{
						var iconRect = new Rect(rect.x, rect.y, 16f, rect.height);
						GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
						rect.xMin += 18f;
					}
				}

				var labelStyle = data.Type == NodeType.GameObject ? EditorStyles.boldLabel : EditorStyles.label;
				EditorGUI.LabelField(rect, item.displayName, labelStyle);
			}

			private void HandleNameCellContextClick(Rect rect, TreeViewItem<int> item, NodeData data)
			{
				var evt = Event.current;
				if (!IsContextMenuEvent(evt, rect))
					return;

				if (!TryGetNodeProperties(data, out var propertiesToRemove))
					return;

				var menu = new GenericMenu();
				menu.AddItem(new GUIContent(GetRemoveLabel(item, data)), false, () => RemoveNodeProperties(propertiesToRemove, data));
				menu.ShowAsContext();
				evt.Use();
			}

			private bool TryGetNodeProperties(NodeData data, out BindableProperty[] properties)
			{
				using (ListPool<BindableProperty>.Get(out var buffer))
				{
					switch (data.Type)
					{
						case NodeType.Property:
							buffer.Add(data.Property);
							break;

						case NodeType.Component:
							CollectMatchingProperties(buffer, property => property.Target == data.Target);
							break;

						case NodeType.GameObject:
							CollectMatchingProperties(buffer, property =>
							{
								if (property.Target == data.Target)
									return true;

								return property.Target is Component component && component.gameObject == data.Target;
							});
							break;
					}

					if (buffer.Count == 0)
					{
						properties = null;
						return false;
					}

					properties = buffer.ToArray();
					return true;
				}
			}

			private void CollectMatchingProperties(List<BindableProperty> output, Predicate<BindableProperty> predicate)
			{
				foreach (var node in _nodes.Values)
				{
					if (node.Type == NodeType.Property && predicate(node.Property))
						output.Add(node.Property);
				}
			}

			private string GetRemoveLabel(TreeViewItem<int> item, NodeData data)
			{
				return $"Remove '{GetNodeDisplayName(item, data)}'";
			}

			private string GetNodeDisplayName(TreeViewItem<int> item, NodeData data)
			{
				switch (data.Type)
				{
					case NodeType.GameObject:
						return !string.IsNullOrEmpty(data.Target?.name) ? data.Target.name : item.displayName;

					case NodeType.Component:
						return data.Target != null
							? ObjectNames.NicifyVariableName(data.Target.GetType().Name)
							: item.displayName;

					case NodeType.Property:
					default:
						return item.displayName;
				}
			}

			private void RemoveNodeProperties(BindableProperty[] properties, NodeData data)
			{
				if (properties == null || properties.Length == 0)
					return;

				Undo.RecordObject(_sheet, GetUndoLabel(data));
				for (int i = 0; i < properties.Length; i++)
					_sheet.RemoveProperty(properties[i]);

				EditorUtility.SetDirty(_sheet);
			}

			private string GetUndoLabel(NodeData data)
			{
				switch (data.Type)
				{
					case NodeType.GameObject:
						return "Remove GameObject Properties";
					case NodeType.Component:
						return "Remove Component Properties";
					case NodeType.Property:
					default:
						return "Remove Property";
				}
			}

			private void DrawBaselineValueCell(Rect rect, BindableProperty property)
			{
				var source = _sheet.BaselineValues;
				int index = -1;
				for (int i = 0; i < source.Count; i++)
				{
					if (source[i].Property.Equals(property))
					{
						index = i;
						break;
					}
				}

				if (index < 0)
				{
					DrawMissingCell(rect, property, isBaseline: true, styleName: null);
					return;
				}

				var entry = source[index];
				HandleCellContextClick(rect, property, isBaseline: true, styleName: null, isPresent: true, currentValue: entry.Value);
				EditorGUI.BeginChangeCheck();
				var newValue = PropertyBindingsEditorGUI.ValueContainerField(rect, property, entry.Value);
				if (EditorGUI.EndChangeCheck())
				{
					Undo.RecordObject(_sheet, "Edit Baseline Value");
					Undo.RegisterFullObjectHierarchyUndo(_sheet.gameObject, "Edit Baseline Value");
					_sheet.SetBaselineValue(property, newValue);
					if (_sheet.IsTransitioning)
						_sheet.CompleteTransitionImmediate();
					EditorUtility.SetDirty(_sheet);
				}
			}

			private void DrawStyleValueCell(Rect rect, string styleName, BindableProperty property)
			{
				if (!_sheet.TryGetStyle(styleName, out var style))
					return;

				int index = -1;
				for (int i = 0; i < style.PropertyValues.Count; i++)
				{
					if (style.PropertyValues[i].Property.Equals(property))
					{
						index = i;
						break;
					}
				}

				if (index < 0)
				{
					DrawMissingCell(rect, property, isBaseline: false, styleName: styleName);
					return;
				}

				var entry = style.PropertyValues[index];
				HandleCellContextClick(rect, property, isBaseline: false, styleName: styleName, isPresent: true, currentValue: entry.Value);
				EditorGUI.BeginChangeCheck();
				var newValue = PropertyBindingsEditorGUI.ValueContainerField(rect, property, entry.Value);
				if (EditorGUI.EndChangeCheck())
				{
					Undo.RecordObject(_sheet, "Edit Style Value");
					Undo.RegisterFullObjectHierarchyUndo(_sheet.gameObject, "Edit Style Value");
					var seed = TryGetBaselineValue(property, out var bv) ? bv : ValueContainer.FromDefault(property.Kind);
					_sheet.SetStyleValue(styleName, property, newValue, seed);
					if (_sheet.IsTransitioning)
						_sheet.CompleteTransitionImmediate();
					EditorUtility.SetDirty(_sheet);
				}
			}

			private static GUIStyle s_inheritStyle;
			private static GUIStyle s_missingBaselineStyle;

			private void DrawMissingCell(Rect rect, BindableProperty property, bool isBaseline, string styleName)
			{
				if (isBaseline)
				{
					if (s_missingBaselineStyle == null)
					{
						s_missingBaselineStyle = new GUIStyle(EditorStyles.label)
						{
							alignment = TextAnchor.MiddleCenter,
							normal = { textColor = new Color(0.95f, 0.75f, 0.2f, 1f) }
						};
					}

					EditorGUI.LabelField(rect,
						new GUIContent("⚠️ Missing", "Baseline is missing a value for this property. Right-click to add one."),
						s_missingBaselineStyle);
				}
				else
				{
					if (s_inheritStyle == null)
					{
						s_inheritStyle = new GUIStyle(EditorStyles.miniLabel)
						{
							fontStyle = FontStyle.Italic,
							alignment = TextAnchor.MiddleCenter,
							normal = { textColor = new Color(0.6f, 0.6f, 0.6f, 1f) }
						};
					}

					EditorGUI.LabelField(rect, "inherit", s_inheritStyle);
				}

				HandleCellContextClick(rect, property, isBaseline, styleName, isPresent: false, currentValue: default);
			}

			private void HandleCellContextClick(Rect rect, BindableProperty property, bool isBaseline, string styleName, bool isPresent, ValueContainer currentValue)
			{
				var evt = Event.current;
				if (!IsContextMenuEvent(evt, rect))
					return;

				var menu = new GenericMenu();
				//copy paste section
				AddClipboardMenuItems(menu, property, isBaseline, styleName, isPresent, currentValue);

				//add/remove from THIS section
				if (isBaseline)
				{
					if (!isPresent)
					{
						menu.AddSeparator(string.Empty);
						menu.AddItem(new GUIContent("Add to Baseline"), false, () =>
						{
							var defaultValue = ValueContainer.FromDefault(property.Kind);
							Undo.RecordObject(_sheet, "Add Baseline Value");
							_sheet.SetBaselineValue(property, defaultValue);
							EditorUtility.SetDirty(_sheet);
						});
					}
				}
				else
				{
					menu.AddSeparator(string.Empty);

					if(isPresent)
					{
						menu.AddItem(new GUIContent($"Remove from '{styleName}'"), false, () =>
						{
							Undo.RecordObject(_sheet, "Remove Style Property");
							_sheet.RemoveStyleValue(styleName, property);
							EditorUtility.SetDirty(_sheet);
						});
					}
					else
					{
						var name = styleName;
						menu.AddItem(new GUIContent($"Add to '{name}'"), false, () =>
						{
							var value = TryGetBaselineValue(property, out var bv) ? bv : ValueContainer.FromDefault(property.Kind);
							Undo.RecordObject(_sheet, "Add Style Value");
							_sheet.SetStyleValue(name, property, value, value);
							EditorUtility.SetDirty(_sheet);
						});
					}
				}


				menu.ShowAsContext();
				evt.Use();
			}

			private void AddClipboardMenuItems(GenericMenu menu, BindableProperty property, bool isBaseline, string styleName, bool isPresent, ValueContainer currentValue)
			{
				if (isPresent)
				{
					menu.AddItem(new GUIContent("Copy"), false, () => CopyPaste.SetValueClipboard(currentValue));
				}
				else
				{
					menu.AddDisabledItem(new GUIContent("Copy"));
				}

				if (CopyPaste.TryGetClipboardValue(property.Kind, out var clipboardValue))
				{
					menu.AddItem(new GUIContent("Paste"), false, () => ApplyCellValue(property, isBaseline, styleName, clipboardValue));
				}
				else
				{
					menu.AddDisabledItem(new GUIContent("Paste"));
				}
			}

			private void ApplyCellValue(BindableProperty property, bool isBaseline, string styleName, ValueContainer value)
			{
				if (isBaseline)
				{
					Undo.RecordObject(_sheet, "Paste Baseline Value");
					Undo.RegisterFullObjectHierarchyUndo(_sheet.gameObject, "Paste Baseline Value");
					_sheet.SetBaselineValue(property, value);
				}
				else
				{
					Undo.RecordObject(_sheet, "Paste Style Value");
					Undo.RegisterFullObjectHierarchyUndo(_sheet.gameObject, "Paste Style Value");
					var seed = TryGetBaselineValue(property, out var baselineValue) ? baselineValue : ValueContainer.FromDefault(property.Kind);
					_sheet.SetStyleValue(styleName, property, value, seed);
				}

				if (_sheet.IsTransitioning)
					_sheet.CompleteTransitionImmediate();

				EditorUtility.SetDirty(_sheet);
			}

			private static bool IsContextMenuEvent(Event evt, Rect rect)
			{
				if (!rect.Contains(evt.mousePosition))
					return false;

				return evt.type == EventType.ContextClick
					|| (evt.type == EventType.MouseDown && evt.button == 1);
			}

			private bool TryGetBaselineValue(BindableProperty property, out ValueContainer value)
			{
				var baseline = _sheet.BaselineValues;
				for (int i = 0; i < baseline.Count; i++)
				{
					if (baseline[i].Property.Equals(property))
					{
						value = baseline[i].Value;
						return true;
					}
				}

				value = default;
				return false;
			}

			private void DrawInterpCell(Rect rect, BindableProperty property)
			{
				var configs = _sheet.PropertyConfigs;
				int index = -1;
				for (int i = 0; i < configs.Count; i++)
				{
					if (configs[i].Property.Equals(property))
					{
						index = i;
						break;
					}
				}

				if (index < 0)
				{
					EditorGUI.LabelField(rect, "—", EditorStyles.miniLabel);
					return;
				}

				var config = configs[index].Interpolation;

				EditorGUI.BeginChangeCheck();
				switch (property.Kind)
				{
					case ValueKind.Quaternion:
						config.Rotation = (RotationInterpolationMode)EditorGUI.EnumPopup(rect, config.Rotation);
						break;
					case ValueKind.Color:
						config.Color = (ColorInterpolationMode)EditorGUI.EnumPopup(rect, config.Color);
						break;
					case ValueKind.Vector2:
						config.Vector2 = (VectorInterpolationMode)EditorGUI.EnumPopup(rect, config.Vector2);
						break;
					case ValueKind.Vector3:
						config.Vector3 = (VectorInterpolationMode)EditorGUI.EnumPopup(rect, config.Vector3);
						break;
					default:
						EditorGUI.LabelField(rect, "—", EditorStyles.miniLabel);
						return;
				}

				if (EditorGUI.EndChangeCheck())
				{
					Undo.RecordObject(_sheet, "Change Interpolation");
					SetInterpolationViaSerializedProperty(_sheet, index, config);
					EditorUtility.SetDirty(_sheet);
				}
			}

			private static void SetInterpolationViaSerializedProperty(StyleSheet sheet, int configIndex, InterpolationConfig config)
			{
				using var serializedSheet = new SerializedObject(sheet);
				var configsProp = serializedSheet.FindProperty("_propertyConfigs");
				if (configsProp == null || configIndex < 0 || configIndex >= configsProp.arraySize)
					return;

				var entryProp = configsProp.GetArrayElementAtIndex(configIndex);
				var interpolationProp = entryProp.FindPropertyRelative(nameof(StylePropertyConfig.Interpolation));
				interpolationProp.FindPropertyRelative(nameof(InterpolationConfig.Rotation)).enumValueIndex = (int)config.Rotation;
				interpolationProp.FindPropertyRelative(nameof(InterpolationConfig.Color)).enumValueIndex = (int)config.Color;
				interpolationProp.FindPropertyRelative(nameof(InterpolationConfig.Vector2)).enumValueIndex = (int)config.Vector2;
				interpolationProp.FindPropertyRelative(nameof(InterpolationConfig.Vector3)).enumValueIndex = (int)config.Vector3;
				serializedSheet.ApplyModifiedProperties();
			}
		}

        private static class Foldouts 
        {
            public static bool Draw(SerializedProperty property, GUIContent label, bool toggleOnLabelClick = false, GUIStyle style = null)
            {
                bool value = Get(property);
                bool newValue = EditorGUILayout.Foldout(value, label, toggleOnLabelClick, style ?? EditorStyles.foldout);
                if (newValue != value)
                {
                    Set(property, newValue);

                    //if alt was held, set foldout state for all sibling properties to match the new value
                    if (Event.current.alt)
                        SetAllSiblingValues(property, newValue);
                    
                }

                property.isExpanded = newValue;
                
                return newValue;
            }

            public static bool Draw(Rect rect, SerializedProperty property, GUIContent label, bool toggleOnLabelClick = false, GUIStyle style = null)
            {
                bool value = Get(property);
                bool newValue = EditorGUI.Foldout(rect, value, label, toggleOnLabelClick, style ?? EditorStyles.foldout);
                if (newValue != value)
                {
                    Set(property, newValue);

                    //if alt was held, set foldout state for all sibling properties to match the new value
                    if (Event.current.alt)
                        SetAllSiblingValues(property, newValue);
                    
                }
    
                property.isExpanded = newValue;

                return newValue;
            }

            private static void SetAllSiblingValues(SerializedProperty property, bool value)
            {
                var iterator = property.Copy();
                while (iterator.NextVisible(true))
                {
                    if (iterator.propertyPath.StartsWith(property.propertyPath) && (iterator.isArray || iterator.propertyType == SerializedPropertyType.Generic))
                    {
                        iterator.isExpanded = value;
                        Set(iterator, value);
                    }
                }
            }

            private static bool Get(SerializedProperty property)
            {
                return SessionState.GetBool(PropertyToId(property), false);
            }

            private static void Set(SerializedProperty property, bool value)
            {
                SessionState.SetBool(PropertyToId(property), value);
            }

            private static string PropertyToId(SerializedProperty property)
            {
                return property.serializedObject.targetObject.GetInstanceID() + ":" + property.propertyPath;
            }
        }
		
		private static class CopyPaste
		{
			private static ValueContainer _valueClipboard;
			private static bool _hasValueClipboard;

			public static void SetValueClipboard(ValueContainer value)
			{
				_valueClipboard = value;
				_hasValueClipboard = true;
			}

			public static bool TryGetClipboardValue(ValueKind kind, out ValueContainer value)
			{
				if (_hasValueClipboard && _valueClipboard.Kind == kind)
				{
					value = _valueClipboard;
					return true;
				}

				value = default;
				return false;
			}
		}
	}
}
