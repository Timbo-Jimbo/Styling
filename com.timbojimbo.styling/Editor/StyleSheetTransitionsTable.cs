using TimboJimbo.Styling;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace TimboJimboEditor.Styling
{
	internal sealed class StyleSheetTransitionsTable : StyleSheetPropertyTable
	{
		private StyleSheetTransitionsTable(int signature, TreeViewState<int> state, MultiColumnHeaderState headerState, StyleSheetTreeView treeView)
			: base(signature, state, headerState, treeView)
		{
		}

		public static StyleSheetTransitionsTable Create(StyleSheet sheet, int signature)
		{
			var state = new TreeViewState<int>();

			var targets = new ColumnTarget[]
			{
				new ColumnTarget { Kind = ColumnTargetKind.Property },
				new ColumnTarget { Kind = ColumnTargetKind.TransitionDuration },
				new ColumnTarget { Kind = ColumnTargetKind.TransitionEaseType },
				new ColumnTarget { Kind = ColumnTargetKind.TransitionInterpolation },
				new ColumnTarget { Kind = ColumnTargetKind.TransitionDiscreteValueSelection },
			};

			var columns = new MultiColumnHeaderState.Column[]
			{
				new MultiColumnHeaderState.Column { headerContent = new GUIContent("Property"), width = 220f, minWidth = 120f, autoResize = false, canSort = false },
				new MultiColumnHeaderState.Column { headerContent = new GUIContent("Duration"), width = 100f, minWidth = 70f, autoResize = false, canSort = false },
				new MultiColumnHeaderState.Column { headerContent = new GUIContent("Ease"), width = 100f, minWidth = 70f, autoResize = false, canSort = false },
				new MultiColumnHeaderState.Column { headerContent = new GUIContent("Interpolation"), width = 100f, minWidth = 70f, autoResize = false, canSort = false },
				new MultiColumnHeaderState.Column { headerContent = new GUIContent("Discrete Mode"), width = 100f, minWidth = 70f, autoResize = false, canSort = false },
			};

			var headerState = new MultiColumnHeaderState(columns);
			var header = new TransitionsTableMultiColumnHeader(headerState)
			{
				canSort = false,
				height = 22f
			};
			header.ResizeToFit();

			var tree = new StyleSheetTreeView(state, header, sheet, targets);
			tree.Reload();
			tree.ExpandAll();
			return new StyleSheetTransitionsTable(signature, state, headerState, tree);
		}
	}

	internal sealed class TransitionsTableMultiColumnHeader : MultiColumnHeader
	{
		private static GUIStyle s_standardHeader;
		private static GUIStyle s_propertiesHeader;

		public TransitionsTableMultiColumnHeader(MultiColumnHeaderState state) : base(state)
		{
		}

		protected override void ColumnHeaderGUI(MultiColumnHeaderState.Column column, Rect headerRect, int columnIndex)
		{
			using (new GUILayout.AreaScope(headerRect))
			{
				if (s_standardHeader == null)
				{
					s_standardHeader = new GUIStyle(EditorStyles.label)
					{
						fontStyle = FontStyle.Normal,
						alignment = TextAnchor.MiddleCenter
					};
				}

				if (s_propertiesHeader == null)
				{
					s_propertiesHeader = new GUIStyle(EditorStyles.label)
					{
						alignment = TextAnchor.MiddleLeft,
						padding = new RectOffset(4, 0, 0, 0)
					};
				}

				using (new EditorGUILayout.HorizontalScope())
				{
					var style = columnIndex == 0 ? s_propertiesHeader : s_standardHeader;
					GUILayout.Label(column.headerContent, style, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
				}
			}
		}
	}
}
