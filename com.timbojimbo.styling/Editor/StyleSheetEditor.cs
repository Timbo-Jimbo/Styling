using System;
using System.Collections.Generic;
using TimboJimbo.PropertyBindings;
using TimboJimbo.PropertyBindings.Editor.Utility;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.Pool;
using Object = UnityEngine.Object;

namespace TimboJimbo.Styling.Editor
{
	[CustomEditor(typeof(StyleSheet))]
	public sealed class StyleSheetEditor : UnityEditor.Editor
	{
		private StyleSheet _sheet;
		private StylingOverride _previewOverride;

		private readonly Dictionary<string, PropertyTable> _styleTables = new Dictionary<string, PropertyTable>();
		private readonly List<Rect> _previewRects = new List<Rect>();
		private readonly List<string> _previewRectNames = new List<string>();

		private bool _isPreviewing;
		private string _previewStyleName;

		private static GUIStyle s_previewChipText;

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
				EditorGUILayout.HelpBox(
					StyleSheetRecordingSession.IsCreatingNew
						? $"Creating '{StyleSheetRecordingSession.EditingStyleName}' — make changes in the Scene View."
						: $"Editing '{StyleSheetRecordingSession.EditingStyleName}' — make changes in the Scene View.",
					MessageType.Info);
			}
			else if (isAnyRecording)
			{
				var recordingTargetName = StyleSheetRecordingSession.Target != null
					? StyleSheetRecordingSession.Target.name
					: "(unknown)";
				EditorGUILayout.HelpBox($"Currently editing '{recordingTargetName}'. Finish that session first.", MessageType.Warning);
			}

			int removeAt = -1;
			var styles = _sheet.Styles;

			if (styles.Count == 0)
			{
				EditorGUILayout.HelpBox("No styles saved yet.", MessageType.None);
			}
			else
			{
				for (int i = 0; i < styles.Count; i++)
				{
					var styleProp = stylesProp.GetArrayElementAtIndex(i);
					if (DrawStyle(styleProp, styles[i], i, isAnyRecording))
						removeAt = i;
				}
			}

			if (removeAt >= 0)
			{
				var styleName = styles[removeAt].Name;
				if (_previewStyleName == styleName)
					EndPreview();

				Undo.RecordObject(_sheet, "Delete Style");
				_sheet.DeleteStyle(styleName);
				_styleTables.Remove(styleName);
				EditorUtility.SetDirty(_sheet);
			}

			using (new EditorGUI.DisabledScope(isAnyRecording))
			{
				if (GUILayout.Button("Create Style", GUILayout.Height(24f)))
					StyleSheetRecordingSession.StartCreating(_sheet);
			}

			PruneStaleTables();
		}

		private bool DrawStyle(SerializedProperty styleProp, Style style, int styleIndex, bool isAnyRecording)
		{
			bool requestDelete = false;

			using (new GUILayout.VerticalScope(EditorStyles.helpBox))
			{
				using (new GUILayout.HorizontalScope())
				{
					var foldoutRect = GUILayoutUtility.GetRect(13f, EditorGUIUtility.singleLineHeight, GUILayout.Width(13f));
                    foldoutRect.x += 13f;
                    
                    Foldouts.Draw(
                        foldoutRect,
                        styleProp,
                        GUIContent.none,
                        toggleOnLabelClick: true
                    );

					using (new EditorGUI.DisabledScope(isAnyRecording))
					{
						EditorGUI.BeginChangeCheck();
						string newName = EditorGUILayout.TextField(style.Name);
						if (EditorGUI.EndChangeCheck())
						{
							newName = newName.Trim();
							if (!string.IsNullOrWhiteSpace(newName)
								&& newName != style.Name
								&& !StyleNameExists(newName))
							{
								Undo.RecordObject(_sheet, "Rename Style");
								var oldName = style.Name;
								_sheet.RenameStyle(oldName, newName);

								if (_styleTables.TryGetValue(oldName, out var table))
								{
									_styleTables.Remove(oldName);
									_styleTables[newName] = table;
								}

								EditorUtility.SetDirty(_sheet);
							}
						}

						DrawStyleHoldPreviewButton(style.Name);

						if (GUILayout.Button("Edit", GUILayout.Width(50f)))
							StyleSheetRecordingSession.StartEditing(_sheet, styleIndex);

						if (GUILayout.Button("Delete", GUILayout.Width(60f)))
							requestDelete = true;
					}
				}

				if (styleProp.isExpanded)
				{
					if (style.PropertyValues.Count == 0)
					{
						EditorGUILayout.HelpBox("This style is empty — it has no property overrides yet.", MessageType.None);
					}
					else
					{
						using (new EditorGUI.DisabledScope(isAnyRecording))
						{
							EnsureStyleTable(style.Name);
							if (_styleTables.TryGetValue(style.Name, out var table))
								DrawTable(table);
						}
					}
				}
			}

			return requestDelete;
		}

		private void DrawStyleHoldPreviewButton(string styleName)
		{
			var rect = GUILayoutUtility.GetRect(new GUIContent("Preview"), GUI.skin.button, GUILayout.Width(60f));

			bool isActive = _isPreviewing && _previewStyleName == styleName;
			if (Event.current.type == EventType.Repaint)
				GUI.Toggle(rect, isActive, "Preview", GUI.skin.button);

			if (!GUI.enabled)
				return;

			int controlId = GUIUtility.GetControlID($"StylePreview:{styleName}".GetHashCode(), FocusType.Passive, rect);
			var evt = Event.current;

			switch (evt.GetTypeForControl(controlId))
			{
				case EventType.MouseDown:
					if (evt.button == 0 && rect.Contains(evt.mousePosition))
					{
						GUIUtility.hotControl = controlId;
						BeginPreview(styleName);
						evt.Use();
					}
					break;

				case EventType.MouseDrag:
					if (GUIUtility.hotControl == controlId)
					{
						if (rect.Contains(evt.mousePosition))
							BeginPreview(styleName);
						else if (_previewStyleName == styleName)
							EndPreview();

						evt.Use();
					}
					break;

				case EventType.MouseUp:
					if (GUIUtility.hotControl == controlId && evt.button == 0)
					{
						GUIUtility.hotControl = 0;
						if (_previewStyleName == styleName)
							EndPreview();
						evt.Use();
					}
					break;
			}
		}

		private void EnsureStyleTable(string styleName)
		{
			int signature = ComputeStyleSignature(styleName);
			if (_styleTables.TryGetValue(styleName, out var existing) && existing.Signature == signature)
				return;

			existing?.Dispose();
			_styleTables[styleName] = PropertyTable.Create(_sheet, styleName, signature);
		}

		private int ComputeStyleSignature(string styleName)
		{
			unchecked
			{
				var style = _sheet.GetStyle(styleName);
				int hash = 17;
				for (int i = 0; i < style.PropertyValues.Count; i++)
					hash = hash * 31 + style.PropertyValues[i].Property.GetHashCode();
				return hash;
			}
		}

		private void PruneStaleTables()
		{
			using (HashSetPool<string>.Get(out var liveNames))
			using (ListPool<string>.Get(out var stale))
			{
				for (int i = 0; i < _sheet.Styles.Count; i++)
					liveNames.Add(_sheet.Styles[i].Name);

				foreach (var key in _styleTables.Keys)
				{
					if (!liveNames.Contains(key))
						stale.Add(key);
				}

				for (int i = 0; i < stale.Count; i++)
				{
					_styleTables[stale[i]].Dispose();
					_styleTables.Remove(stale[i]);
				}
			}
		}

		private void DisposeTables()
		{
			foreach (var table in _styleTables.Values)
				table.Dispose();

			_styleTables.Clear();
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

		private static void DrawTable(PropertyTable table)
		{
			GUILayout.Space(4f);
			float treeHeight = Mathf.Max(table.TreeView.totalHeight, EditorGUIUtility.singleLineHeight);
			var rect = GUILayoutUtility.GetRect(0f, 10000f, treeHeight, treeHeight);
			table.TreeView.OnGUI(rect);
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

			if (s_previewChipText == null)
			{
				s_previewChipText = new GUIStyle(EditorStyles.label)
				{
					alignment = TextAnchor.MiddleCenter
				};
			}

			GUI.Label(innerRect, styleName, s_previewChipText);
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
			if (string.IsNullOrWhiteSpace(styleName))
			{
				EndPreview();
				return;
			}

			if (_isPreviewing && _previewStyleName == styleName)
				return;

			_previewOverride?.Dispose();
			_previewOverride = StylingSystem.StylingOverrideScope(_sheet.gameObject, new[] { styleName });
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

			public static PropertyTable Create(StyleSheet sheet, string styleName, int signature)
			{
				var state = new TreeViewState<int>();
				var columns = new[]
				{
					new MultiColumnHeaderState.Column
					{
						headerContent = new GUIContent("Property"),
						width = 220f,
						minWidth = 120f,
						autoResize = true,
						canSort = false
					},
					new MultiColumnHeaderState.Column
					{
						headerContent = new GUIContent("Value"),
						width = 200f,
						minWidth = 100f,
						autoResize = true,
						canSort = false
					},
					new MultiColumnHeaderState.Column
					{
						headerContent = new GUIContent("Interp"),
						width = 110f,
						minWidth = 60f,
						maxWidth = 180f,
						autoResize = false,
						canSort = false
					}
				};

				var headerState = new MultiColumnHeaderState(columns);
				var header = new MultiColumnHeader(headerState)
				{
					canSort = false,
					height = 22f
				};
				header.ResizeToFit();

				var tree = new StyleSheetTreeView(state, header, sheet, styleName);
				tree.Reload();
				tree.ExpandAll();
				return new PropertyTable(signature, state, headerState, tree);
			}

			public void Dispose()
			{
			}
		}

		internal enum NodeType { GameObject, Component, Property }

		internal struct NodeData
		{
			public NodeType Type;
			public BindableProperty Property;
			public Object Target;
		}

		internal sealed class StyleSheetTreeView : TreeView<int>
		{
			private const int ColName = 0;
			private const int ColValue = 1;
			private const int ColInterp = 2;

			private readonly StyleSheet _sheet;
			private readonly string _styleName;
			private readonly Dictionary<int, NodeData> _nodes = new Dictionary<int, NodeData>();

			public StyleSheetTreeView(TreeViewState<int> state, MultiColumnHeader header, StyleSheet sheet, string styleName)
				: base(state, header)
			{
				_sheet = sheet;
				_styleName = styleName;
				showAlternatingRowBackgrounds = true;
				rowHeight = 22f;
				showBorder = false;
				cellMargin = 2f;
			}

			private IReadOnlyList<BindablePropertyToValue> SourceList() => _sheet.GetStyle(_styleName).PropertyValues;

			protected override TreeViewItem<int> BuildRoot()
			{
				var root = new TreeViewItem<int>(-1, -1, "Root");
				var items = new List<TreeViewItem<int>>();
				_nodes.Clear();

				var source = SourceList();
				if (source.Count == 0)
				{
					SetupParentsAndChildrenFromDepths(root, items);
					return root;
				}

				int id = 0;
				var goOrder = new List<GameObject>();
				var goDirect = new Dictionary<GameObject, List<BindableProperty>>();
				var goComponentOrder = new Dictionary<GameObject, List<Component>>();
				var componentProperties = new Dictionary<Component, List<BindableProperty>>();

				for (int i = 0; i < source.Count; i++)
				{
					var property = source[i].Property;
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

			protected override void ContextClickedItem(int id)
			{
				if (!_nodes.TryGetValue(id, out var data) || data.Type != NodeType.Property)
					return;

				var menu = new GenericMenu();
				menu.AddItem(new GUIContent($"Remove from '{_styleName}'"), false, () =>
				{
					Undo.RecordObject(_sheet, "Remove Style Override");
					_sheet.RemoveStyleValue(_styleName, data.Property);
					EditorUtility.SetDirty(_sheet);
				});
				menu.AddSeparator(string.Empty);
				menu.AddItem(new GUIContent("Remove from all styles & baseline"), false, () =>
				{
					Undo.RecordObject(_sheet, "Remove Property");
					_sheet.DeletePropertyFromAllStyles(data.Property);
					EditorUtility.SetDirty(_sheet);
				});
				menu.ShowAsContext();
				Event.current.Use();
			}

			private void DrawCell(Rect rect, TreeViewItem<int> item, int column, ref NodeData data)
			{
				switch (column)
				{
					case ColName:
						DrawNameCell(rect, item, ref data);
						break;
					case ColValue:
						if (data.Type == NodeType.Property)
							DrawValueCell(rect, data.Property);
						break;
					case ColInterp:
						if (data.Type == NodeType.Property)
							DrawInterpCell(rect, data.Property);
						break;
				}
			}

			private void DrawNameCell(Rect rect, TreeViewItem<int> item, ref NodeData data)
			{
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

			private void DrawValueCell(Rect rect, BindableProperty property)
			{
				var source = SourceList();
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
					EditorGUI.LabelField(rect, "—", EditorStyles.miniLabel);
					return;
				}

				var entry = source[index];
				EditorGUI.BeginChangeCheck();
				var newValue = PropertyBindingsEditorGUI.ValueContainerField(rect, entry.Value);
				if (EditorGUI.EndChangeCheck())
				{
					Undo.RecordObject(_sheet, "Edit Style Value");
					Undo.RegisterFullObjectHierarchyUndo(_sheet.gameObject, "Edit Style Value");
					_sheet.TryUpdateStyleValue(_styleName, property, newValue);
					if (_sheet.IsTransitioning)
						_sheet.CompleteTransitionImmediate();
					EditorUtility.SetDirty(_sheet);
				}
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
	}
}
