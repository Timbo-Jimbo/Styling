using System;
using TimboJimbo.Styling;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace TimboJimboEditor.Styling
{
	internal sealed class StyleSheetValuesTable : StyleSheetPropertyTable
	{
		private StyleSheetValuesTable(int signature, TreeViewState<int> state, MultiColumnHeaderState headerState, StyleSheetTreeView treeView)
			: base(signature, state, headerState, treeView)
		{
		}

		public static StyleSheetValuesTable Create(StyleSheet sheet, int signature)
		{
			var state = new TreeViewState<int>();

			var styles = sheet.Styles;
			var targets = new ColumnTarget[2 + styles.Count];
			targets[0] = new ColumnTarget { Kind = ColumnTargetKind.Property };
			targets[1] = new ColumnTarget { Kind = ColumnTargetKind.Baseline };
			for (int i = 0; i < styles.Count; i++)
				targets[2 + i] = new ColumnTarget { Kind = ColumnTargetKind.Style, StyleName = styles[i].Name };

			var columns = new MultiColumnHeaderState.Column[targets.Length];
			columns[0] = new MultiColumnHeaderState.Column { headerContent = new GUIContent("Property"), width = 220f, minWidth = 120f, autoResize = false, canSort = false };
			columns[1] = new MultiColumnHeaderState.Column { headerContent = new GUIContent("Baseline"), width = 180f, minWidth = 80f, autoResize = false, canSort = false };
			for (int i = 0; i < styles.Count; i++)
				columns[2 + i] = new MultiColumnHeaderState.Column { headerContent = new GUIContent(styles[i].Name), width = 180f, minWidth = 80f, autoResize = false, canSort = false };

			var headerState = new MultiColumnHeaderState(columns);
			var header = new ValuesTableMultiColumnHeader(headerState, baselineColumnIndex: 1, syncBaselineFromScene: () =>
			{
				Undo.RecordObject(sheet, "Sync Baseline to Active Values");
				Undo.RegisterFullObjectHierarchyUndo(sheet.gameObject, "Sync Baseline to Active Values");
				sheet.PullBaselineValuesFromScene();
				if (sheet.IsTransitioning)
					sheet.CompleteTransitionImmediate();
				EditorUtility.SetDirty(sheet);
			})
			{
				canSort = false,
				height = 22f
			};
			header.ResizeToFit();

			var tree = new StyleSheetTreeView(state, header, sheet, targets);
			tree.Reload();
			tree.ExpandAll();
			return new StyleSheetValuesTable(signature, state, headerState, tree);
		}
	}

	internal sealed class ValuesTableMultiColumnHeader : MultiColumnHeader
	{
		private static GUIStyle s_propertiesHeader;
		private static GUIStyle s_standardHeader;
		private static GUIStyle s_italicHeader;
		private readonly int _baselineColumnIndex;
		private readonly Action _syncBaselineFromScene;

		public ValuesTableMultiColumnHeader(MultiColumnHeaderState state, int baselineColumnIndex, Action syncBaselineFromScene) : base(state)
		{
			_baselineColumnIndex = baselineColumnIndex;
			_syncBaselineFromScene = syncBaselineFromScene;
		}

		protected override void ColumnHeaderGUI(MultiColumnHeaderState.Column column, Rect headerRect, int columnIndex)
		{
			using (new GUILayout.AreaScope(headerRect))
			{
				using (new EditorGUILayout.HorizontalScope())
				{
					if (columnIndex == _baselineColumnIndex)
					{
						if (s_italicHeader == null)
						{
							s_italicHeader = new GUIStyle(EditorStyles.label)
							{
								fontStyle = FontStyle.Italic,
								alignment = TextAnchor.MiddleCenter,
							};
						}

						GUILayout.Label(column.headerContent, s_italicHeader, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

						if (StylingEditorGUI.DrawImportButton(tooltip: "Sync values from scene"))
							_syncBaselineFromScene?.Invoke();
						GUILayout.Space(4f);
					}
					else
					{
						if (s_standardHeader == null)
						{
							s_standardHeader = new GUIStyle(EditorStyles.label)
							{
								fontStyle = FontStyle.Normal,
								alignment = TextAnchor.MiddleCenter
							};
						}

						if(s_propertiesHeader == null)
						{
							s_propertiesHeader = new GUIStyle(s_standardHeader)
							{
								alignment = TextAnchor.MiddleLeft,
								padding = new RectOffset(4, 0, 0, 0)
							};
						}

						var style = columnIndex == 0 ? s_propertiesHeader : s_standardHeader;

						GUILayout.Label(column.headerContent, style, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
					}
				}
			}
		}
	}
}
