using TimboJimbo.Styling;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.Styling
{
	[CustomEditor(typeof(StyleGroup))]
	public sealed class StyleGroupEditor : Editor
    {
		private SerializedProperty _styleActivationsProp;

		private static bool ShowStyleActivations
		{
			get => SessionState.GetBool("StyleGroupEditor.ShowStyleActivations", true);
			set => SessionState.SetBool("StyleGroupEditor.ShowStyleActivations", value);
		} 

		private void OnEnable()
		{
			_styleActivationsProp = serializedObject.FindProperty("_styleActivations");
		}

		public override void OnInspectorGUI()
		{
			serializedObject.Update();
		
			DrawStyleActivations();

			serializedObject.ApplyModifiedProperties();
		}

		private void DrawStyleActivations()
		{
			StylingEditorGUI.DrawFoldout(
				expanded: ShowStyleActivations,
				drawContent: () =>
				{
					EditorGUILayout.LabelField("Style Activations", EditorStyles.boldLabel);
					GUILayout.FlexibleSpace();
					if(StylingEditorGUI.AddButton())
					{
						_styleActivationsProp.arraySize++;
						var entry = _styleActivationsProp.GetArrayElementAtIndex(_styleActivationsProp.arraySize - 1);
						entry.FindPropertyRelative(nameof(StyleActivation.Name)).stringValue = $"Style {_styleActivationsProp.arraySize}";
						entry.FindPropertyRelative(nameof(StyleActivation.Active)).boolValue = false;
						entry.serializedObject.ApplyModifiedProperties();
					}
				},
				onToggle: value => ShowStyleActivations = value
			);

			if (!ShowStyleActivations)
				return;


			int removeAt = -1;

			for (int i = 0; i < _styleActivationsProp.arraySize; i++)
			{
				var entry = _styleActivationsProp.GetArrayElementAtIndex(i);
				var nameProp = entry.FindPropertyRelative(nameof(StyleActivation.Name));
				var activeProp = entry.FindPropertyRelative(nameof(StyleActivation.Active));

				using (new GUILayout.HorizontalScope())
				{
					EditorGUILayout.PropertyField(nameProp, GUIContent.none);
					var active = StylingEditorGUI.ButtonGroupToggleButton(
						content: new GUIContent("Active", tooltip: "When enabled, this style is active. When disabled, this style is explicitly inactive and will override any active state from parent groups."),
						value: activeProp.boolValue,
						buttonIndex: 0,
						buttonCount: 2,
						options: GUILayout.Width(64f)
					);

					if (active != activeProp.boolValue)
						activeProp.boolValue = active;

					var removeClicked = StylingEditorGUI.ButtonGroupButton(
						content: new GUIContent("✕", tooltip: "Remove this style activation"),
						buttonIndex: 1,
						buttonCount: 2,
						options: GUILayout.Width(24f)
					);

					if (removeClicked)
						removeAt = i;
				}
			}

			if (removeAt >= 0)
				_styleActivationsProp.DeleteArrayElementAtIndex(removeAt);
		}
	}
}
