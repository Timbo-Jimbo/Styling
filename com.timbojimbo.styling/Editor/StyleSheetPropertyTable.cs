using System;
using System.Collections.Generic;
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
	internal abstract class StyleSheetPropertyTable : IDisposable
	{
		public readonly int Signature;
		public readonly TreeViewState<int> State;
		public readonly MultiColumnHeaderState HeaderState;
		public readonly StyleSheetTreeView TreeView;

		protected StyleSheetPropertyTable(int signature, TreeViewState<int> state, MultiColumnHeaderState headerState, StyleSheetTreeView treeView)
		{
			Signature = signature;
			State = state;
			HeaderState = headerState;
			TreeView = treeView;
		}

		public virtual void Dispose()
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

	internal enum ColumnTargetKind { Property, Baseline, Style, TransitionEaseType, TransitionDuration, TransitionInterpolation, TransitionDiscreteValueSelection }

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
				case ColumnTargetKind.TransitionEaseType:
					if (data.Type == NodeType.Property)
						DrawTransitionEaseTypeCell(rect, data.Property);
					break;
				case ColumnTargetKind.TransitionDuration:
					if (data.Type == NodeType.Property)
						DrawTransitionDurationCell(rect, data.Property);
					break;
				case ColumnTargetKind.TransitionInterpolation:
					if (data.Type == NodeType.Property)
						DrawTransitionInterpolationCell(rect, data.Property);
					break;
				case ColumnTargetKind.TransitionDiscreteValueSelection:
					if (data.Type == NodeType.Property)
						DrawTransitionDiscreteValueSelectionCell(rect, data.Property);
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
				menu.AddItem(new GUIContent("Copy"), false, () => StyleSheetPropertyTableClipboard.SetValueClipboard(currentValue));
			}
			else
			{
				menu.AddDisabledItem(new GUIContent("Copy"));
			}

			if (StyleSheetPropertyTableClipboard.TryGetClipboardValue(property.Kind, out var clipboardValue))
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

		private bool TryGetTransition(BindableProperty property, out int index, out StylePropertyTransition transition)
		{
			var configs = _sheet.PropertyConfigs;
			for (int i = 0; i < configs.Count; i++)
			{
				if (configs[i].Property.Equals(property))
				{
					index = i;
					transition = configs[i].Transition;
					return true;
				}
			}

			index = -1;
			transition = default;
			return false;
		}

		private void SetTransitionViaSerializedProperty(int configIndex, StylePropertyTransition transition)
		{
			using var serializedSheet = new SerializedObject(_sheet);
			var configsProp = serializedSheet.FindProperty("_propertyConfigs");
			if (configsProp == null || configIndex < 0 || configIndex >= configsProp.arraySize)
				return;

			var entryProp = configsProp.GetArrayElementAtIndex(configIndex);
			var transitionProp = entryProp.FindPropertyRelative(nameof(StylePropertyConfig.Transition));
			transitionProp.FindPropertyRelative(nameof(StylePropertyTransition.Duration)).floatValue = transition.Duration;
			transitionProp.FindPropertyRelative(nameof(StylePropertyTransition.EaseType)).enumValueIndex = (int)transition.EaseType;
			var interpProp = transitionProp.FindPropertyRelative(nameof(StylePropertyTransition.Interpolation));
			interpProp.FindPropertyRelative(nameof(InterpolationConfig.Rotation)).enumValueIndex = (int)transition.Interpolation.Rotation;
			interpProp.FindPropertyRelative(nameof(InterpolationConfig.Color)).enumValueIndex = (int)transition.Interpolation.Color;
			interpProp.FindPropertyRelative(nameof(InterpolationConfig.Vector2)).enumValueIndex = (int)transition.Interpolation.Vector2;
			interpProp.FindPropertyRelative(nameof(InterpolationConfig.Vector3)).enumValueIndex = (int)transition.Interpolation.Vector3;
			transitionProp.FindPropertyRelative(nameof(StylePropertyTransition.DiscreteValueSelection)).enumValueIndex = (int)transition.DiscreteValueSelection;
			serializedSheet.ApplyModifiedProperties();
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

		private void DrawTransitionEaseTypeCell(Rect rect, BindableProperty property)
		{
			if (!TryGetTransition(property, out var index, out var transition))
			{
				EditorGUI.LabelField(rect, "—", EditorStyles.miniLabel);
				return;
			}

			if (!IsContinuousKind(property.Kind))
			{
				EditorGUI.LabelField(rect, "—", EditorStyles.miniLabel);
				return;
			}

			using (new EditorGUI.DisabledScope(!transition.Animate))
			{
				StylingEditorGUI.EaseTypePopup(rect, transition.EaseType, newEase => 
				{
					Undo.RecordObject(_sheet, "Change Transition Ease");
					transition.EaseType = newEase;
					SetTransitionViaSerializedProperty(index, transition);
					EditorUtility.SetDirty(_sheet);
				});
			}
		}

		private void DrawTransitionDurationCell(Rect rect, BindableProperty property)
		{
			if (!TryGetTransition(property, out var index, out var transition))
			{
				EditorGUI.LabelField(rect, "—", EditorStyles.miniLabel);
				return;
			}

			EditorGUI.BeginChangeCheck();
			float newDuration = EditorGUI.FloatField(rect, transition.Duration);
			if (EditorGUI.EndChangeCheck())
			{
				Undo.RecordObject(_sheet, "Change Transition Duration");
				transition.Duration = Mathf.Max(0f, newDuration);
				SetTransitionViaSerializedProperty(index, transition);
				EditorUtility.SetDirty(_sheet);
			}
		}

		private void DrawTransitionInterpolationCell(Rect rect, BindableProperty property)
		{
			if (!TryGetTransition(property, out var index, out var transition))
			{
				EditorGUI.LabelField(rect, "—", EditorStyles.miniLabel);
				return;
			}

			using (new EditorGUI.DisabledScope(!transition.Animate))
			{
				var config = transition.Interpolation;

				if (property.Kind == ValueKind.Color)
				{
					StylingEditorGUI.ColorInterpolationModePopup(rect, config.Color, newMode =>
					{
						Undo.RecordObject(_sheet, "Change Transition Interpolation");
						config.Color = newMode;
						transition.Interpolation = config;
						SetTransitionViaSerializedProperty(index, transition);
						EditorUtility.SetDirty(_sheet);
					});
					return;
				}

				EditorGUI.BeginChangeCheck();
				switch (property.Kind)
				{
					case ValueKind.Quaternion:
						config.Rotation = (RotationInterpolationMode)EditorGUI.EnumPopup(rect, config.Rotation);
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
					Undo.RecordObject(_sheet, "Change Transition Interpolation");
					transition.Interpolation = config;
					SetTransitionViaSerializedProperty(index, transition);
					EditorUtility.SetDirty(_sheet);
				}
			}
		}

		private void DrawTransitionDiscreteValueSelectionCell(Rect rect, BindableProperty property)
		{
			if (!TryGetTransition(property, out var index, out var transition))
			{
				EditorGUI.LabelField(rect, "—", EditorStyles.miniLabel);
				return;
			}

			if (IsContinuousKind(property.Kind))
			{
				EditorGUI.LabelField(rect, "—", EditorStyles.miniLabel);
				return;
			}

			using (new EditorGUI.DisabledScope(!transition.Animate))
			{
				StylingEditorGUI.DiscreteValueSelectionModePopup(rect, transition.DiscreteValueSelection, newMode =>
				{
					Undo.RecordObject(_sheet, "Change Discrete Value Selection");
					transition.DiscreteValueSelection = newMode;
					SetTransitionViaSerializedProperty(index, transition);
					EditorUtility.SetDirty(_sheet);
				});
			}
		}
	}

	internal static class StyleSheetPropertyTableClipboard
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
