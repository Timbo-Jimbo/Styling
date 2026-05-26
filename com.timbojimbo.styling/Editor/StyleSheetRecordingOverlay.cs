using System.Collections.Generic;
using TimboJimbo.PropertyBindings;
using TimboJimboEditor;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.Pool;
using Object = UnityEngine.Object;

namespace TimboJimbo.Styling.Editor
{
	/// <summary>
	/// Scene-view overlay for the <see cref="StyleSheetRecordingSession"/>.
	/// </summary>
	[Overlay(typeof(SceneView), OverlayId, "Style Sheet Recording", defaultDisplay = true)]
	[Icon("d_Animation.Record")]
	internal sealed class StyleSheetRecordingOverlay : IMGUIOverlay, ITransientOverlay
	{
		private const string OverlayId = "style-sheet-recording-overlay";
		private const float RowHeight = 20f;
		private const float MaxTreeHeight = 220f;
		private const float MinTreeHeight = 60f;

		private TreeViewState<int> _treeState;
		private ChangesTreeView _tree;
		private int _lastEditsHash;

		public bool visible => StyleSheetRecordingSession.IsRecording;

		public override void OnGUI()
		{
			if (!StyleSheetRecordingSession.IsRecording)
				return;

			using var _ = ListPool<BindablePropertyValueEdit>.Get(out var edits);
			StyleSheetRecordingSession.GetCurrentEdits(edits);

			using (new GUILayout.VerticalScope(GUILayout.MinWidth(320)))
			{
				if (StyleSheetRecordingSession.IsCreatingNew)
					DrawNameField();

				GUILayout.Space(4f);
				DrawChangesTree(edits);
				GUILayout.Space(6f);
				DrawButtons(edits.Count);
			}
		}

		private static void DrawNameField()
		{
			using (new GUILayout.HorizontalScope())
			{
				GUILayout.Label("Name:", GUILayout.Width(44f));
				StyleSheetRecordingSession.EditingStyleName = GUILayout.TextField(
					StyleSheetRecordingSession.EditingStyleName ?? string.Empty);
			}
		}

		private void DrawChangesTree(List<BindablePropertyValueEdit> edits)
		{
			var label = edits.Count > 0 ? $"Changes ({edits.Count}):" : "No changes detected.";
			GUILayout.Label(label, EditorStyles.miniLabel);

			if (edits.Count == 0)
				return;

			EnsureTree(edits);

			int rowCount = _tree.GetRowCountSafe();
			float treeHeight = Mathf.Clamp(rowCount * RowHeight + 4f, MinTreeHeight, MaxTreeHeight);
			var rect = GUILayoutUtility.GetRect(320f, treeHeight, GUILayout.ExpandWidth(true));
			_tree.OnGUI(rect);
		}

		private void EnsureTree(List<BindablePropertyValueEdit> edits)
		{
			_treeState ??= new TreeViewState<int>();

			int hash = ComputeEditsHash(edits);
			if (_tree == null)
			{
				_tree = new ChangesTreeView(_treeState, edits);
				_tree.Reload();
				_tree.ExpandAll();
				_lastEditsHash = hash;
				return;
			}

			if (hash != _lastEditsHash)
			{
				_tree.SetEdits(edits);
				_tree.Reload();
				_tree.ExpandAll();
				_lastEditsHash = hash;
			}
		}

		private static int ComputeEditsHash(List<BindablePropertyValueEdit> edits)
		{
			unchecked
			{
				int hash = 17;
				for (int i = 0; i < edits.Count; i++)
				{
					var property = edits[i].BindableProperty;
					hash = hash * 31 + (property.Target != null ? property.Target.GetInstanceID() : 0);
					hash = hash * 31 + (property.Path != null ? property.Path.GetHashCode() : 0);
				}

				return hash;
			}
		}

		private static void DrawButtons(int editCount)
		{
			var saveLabel = StyleSheetRecordingSession.IsCreatingNew ? "Create" : "Save";
			bool canSave = editCount > 0
				&& (!StyleSheetRecordingSession.IsCreatingNew
					|| !string.IsNullOrWhiteSpace(StyleSheetRecordingSession.EditingStyleName));

			using (new GUILayout.HorizontalScope())
			{
				using (new EditorGUI.DisabledScope(!canSave))
				{
					if (GUILayout.Button(saveLabel))
						StyleSheetRecordingSession.SaveAndStop();
				}

				if (GUILayout.Button("Cancel"))
					StyleSheetRecordingSession.CancelRecording();
			}
		}

		private enum NodeType { GameObject, Component, Property }

		private struct NodeData
		{
			public NodeType Type;
			public Object Target;
			public BindableProperty Property;
		}

		private sealed class ChangesTreeView : TreeView<int>
		{
			private List<BindablePropertyValueEdit> _edits;
			private readonly Dictionary<int, NodeData> _nodes = new Dictionary<int, NodeData>();

			public ChangesTreeView(TreeViewState<int> state, List<BindablePropertyValueEdit> edits)
				: base(state)
			{
				_edits = edits;
				showAlternatingRowBackgrounds = true;
				showBorder = true;
				rowHeight = RowHeight;
			}

			public void SetEdits(List<BindablePropertyValueEdit> edits) => _edits = edits;

			public int GetRowCountSafe()
			{
				var rows = GetRows();
				return rows != null ? rows.Count : 0;
			}

			protected override TreeViewItem<int> BuildRoot()
			{
				var root = new TreeViewItem<int>(-1, -1, "Root");
				var items = new List<TreeViewItem<int>>();
				_nodes.Clear();

				if (_edits == null || _edits.Count == 0)
				{
					items.Add(new TreeViewItem<int>(0, 0, "(no changes)"));
					SetupParentsAndChildrenFromDepths(root, items);
					return root;
				}

				int id = 0;
				var goOrder = new List<int>();
				var goMap = new Dictionary<int, (GameObject go, List<BindableProperty> direct, List<int> compOrder, Dictionary<int, (Component comp, List<BindableProperty> props)> comps)>();

				for (int i = 0; i < _edits.Count; i++)
				{
					var property = _edits[i].BindableProperty;
					var target = property.Target;
					var component = target as Component;
					var gameObject = component != null ? component.gameObject : target as GameObject;
					int goKey = gameObject != null ? gameObject.GetInstanceID() : 0;

					if (!goMap.TryGetValue(goKey, out var goData))
					{
						goData = (gameObject, new List<BindableProperty>(), new List<int>(), new Dictionary<int, (Component, List<BindableProperty>)>());
						goMap[goKey] = goData;
						goOrder.Add(goKey);
					}

					if (component == null)
					{
						goData.direct.Add(property);
					}
					else
					{
						int componentKey = component.GetInstanceID();
						if (!goData.comps.TryGetValue(componentKey, out var compData))
						{
							compData = (component, new List<BindableProperty>());
							goData.comps[componentKey] = compData;
							goData.compOrder.Add(componentKey);
						}

						compData.props.Add(property);
					}
				}

				for (int i = 0; i < goOrder.Count; i++)
				{
					var (go, direct, compOrder, comps) = goMap[goOrder[i]];

					int goItemId = id++;
					items.Add(new TreeViewItem<int>(goItemId, 0, go != null ? go.name : "(unknown)"));
					_nodes[goItemId] = new NodeData { Type = NodeType.GameObject, Target = go };

					for (int j = 0; j < direct.Count; j++)
					{
						var property = direct[j];
						int propertyId = id++;
						items.Add(new TreeViewItem<int>(propertyId, 1, ObjectNames.NicifyVariableName(property.Path)));
						_nodes[propertyId] = new NodeData { Type = NodeType.Property, Property = property };
					}

					for (int j = 0; j < compOrder.Count; j++)
					{
						var (component, props) = comps[compOrder[j]];
						int componentId = id++;
						var typeName = component != null ? ObjectNames.NicifyVariableName(component.GetType().Name) : "(unknown)";
						items.Add(new TreeViewItem<int>(componentId, 1, typeName));
						_nodes[componentId] = new NodeData { Type = NodeType.Component, Target = component };

						for (int k = 0; k < props.Count; k++)
						{
							var property = props[k];
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
				{
					base.RowGUI(args);
					return;
				}

				var rect = args.rowRect;
				CenterRectUsingSingleLineHeight(ref rect);
				rect.xMin += GetContentIndent(args.item);

				var icon = ResolveIcon(data);
				if (icon != null)
				{
					var iconRect = new Rect(rect.x, rect.y, 16f, rect.height);
					GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
					rect.xMin += 18f;
				}

				var style = data.Type == NodeType.GameObject ? EditorStyles.boldLabel : EditorStyles.label;
				EditorGUI.LabelField(rect, args.item.displayName, style);
			}

			protected override void SingleClickedItem(int id)
			{
				if (_nodes.TryGetValue(id, out var data) && data.Target != null)
					EditorGUIUtility.PingObject(data.Target);
			}

			protected override void DoubleClickedItem(int id)
			{
				if (_nodes.TryGetValue(id, out var data) && data.Target != null)
					Selection.activeObject = data.Target;
			}

			private static Texture ResolveIcon(NodeData data)
			{
				switch (data.Type)
				{
					case NodeType.GameObject:
						return data.Target != null ? EditorGUIUtility.ObjectContent(data.Target, typeof(GameObject)).image : null;
					case NodeType.Component:
						return data.Target != null ? EditorGUIUtility.ObjectContent(data.Target, data.Target.GetType()).image : null;
					case NodeType.Property:
						return EditorGUIUtility.IconContent("d_Settings").image;
					default:
						return null;
				}
			}
		}
	}
}
