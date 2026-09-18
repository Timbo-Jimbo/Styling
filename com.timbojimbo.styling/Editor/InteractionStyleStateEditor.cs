using TimboJimbo.Styling;
using TimboJimboEditor.Core;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.Styling
{
	/// <summary>
	/// The default style names are right almost every time, so they live behind a collapsed
	/// "Style Names" foldout. Collapsed, the row shows the current names as a summary.
	/// </summary>
	[CustomEditor(typeof(InteractionStyleState))]
	[CanEditMultipleObjects]
	public sealed class InteractionStyleStateEditor : Editor
	{
		private static readonly FoldoutGUI.SessionBool StyleNamesExpanded =
			new FoldoutGUI.SessionBool("InteractionStyleStateNames", defaultExpanded: false);

		private static readonly (string field, string label, string defaultName)[] Fields =
		{
			("_hoveredStyle", "Hovered", InteractionStyleState.HoveredStyle),
			("_pressedStyle", "Pressed", InteractionStyleState.PressedStyle),
			("_selectedStyle", "Selected", InteractionStyleState.SelectedStyle),
			("_disabledStyle", "Disabled", InteractionStyleState.DisabledStyle),
		};

		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			bool expanded = StyleNamesExpanded.Get(serializedObject);

			FoldoutGUI.Draw(
				expanded: expanded,
				drawContent: () =>
				{
					FoldoutGUI.Title("Style Names");
					GUILayout.FlexibleSpace();

					if (!expanded)
						EditorGUILayout.LabelField(BuildSummary(), StylingEditorGUI.Styles.ItalicLabel, GUILayout.ExpandWidth(false));
				},
				onToggle: value => StyleNamesExpanded.Set(serializedObject, value)
			);

			if (expanded)
			{
				FoldoutGUI.BeginContent();

				foreach (var (field, label, _) in Fields)
					EditorGUILayout.PropertyField(serializedObject.FindProperty(field), new GUIContent(label));

				using (new EditorGUI.DisabledScope(AllDefault()))
				{
					if (GUILayout.Button("Reset to Defaults", EditorStyles.miniButton))
					{
						foreach (var (field, _, defaultName) in Fields)
							serializedObject.FindProperty(field).stringValue = defaultName;
					}
				}

				FoldoutGUI.EndContent();
			}

			serializedObject.ApplyModifiedProperties();
		}

		private string BuildSummary()
		{
			if (AllDefault())
				return "Defaults";

			var names = new string[Fields.Length];
			for (int i = 0; i < Fields.Length; i++)
			{
				var prop = serializedObject.FindProperty(Fields[i].field);
				names[i] = prop.hasMultipleDifferentValues ? "—" : prop.stringValue;
			}
			return string.Join(", ", names);
		}

		private bool AllDefault()
		{
			foreach (var (field, _, defaultName) in Fields)
			{
				var prop = serializedObject.FindProperty(field);
				if (prop.hasMultipleDifferentValues || prop.stringValue != defaultName)
					return false;
			}
			return true;
		}
	}
}
