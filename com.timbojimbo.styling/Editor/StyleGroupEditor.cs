using TimboJimbo.Styling;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.Styling
{
	[CustomEditor(typeof(StyleGroup))]
	public sealed class StyleGroupEditor : UnityEditor.Editor
	{
		private SerializedProperty _styleActivationsProp;
		private SerializedProperty _isToggleGroupProp;

		private void OnEnable()
		{
			_styleActivationsProp = serializedObject.FindProperty("_styleActivations");
			_isToggleGroupProp = serializedObject.FindProperty("_isToggleGroup");
		}

		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			EditorGUILayout.HelpBox(
				"Activations cascade to descendant StyleSheets. Innermost group wins per name.\n" +
				"Active=true enables, Active=false explicitly disables (shadows ancestors).",
				MessageType.None);

			EditorGUILayout.PropertyField(_isToggleGroupProp, new GUIContent(
				"As Toggle Group",
				"When enabled, only one entry can be Active at a time. Activating one deactivates the others."));

			bool isToggleGroup = _isToggleGroupProp.boolValue;
			int removeAt = -1;

			for (int i = 0; i < _styleActivationsProp.arraySize; i++)
			{
				var entry = _styleActivationsProp.GetArrayElementAtIndex(i);
				var nameProp = entry.FindPropertyRelative(nameof(StyleActivation.Name));
				var activeProp = entry.FindPropertyRelative(nameof(StyleActivation.Active));

				using (new GUILayout.HorizontalScope())
				{
					EditorGUILayout.PropertyField(nameProp, GUIContent.none);

					bool wasActive = activeProp.boolValue;
					bool nowActive = GUILayout.Toggle(wasActive, "Active", "Button", GUILayout.Width(70f));
					if (nowActive != wasActive)
					{
						activeProp.boolValue = nowActive;
						if (isToggleGroup && nowActive)
						{
							for (int j = 0; j < _styleActivationsProp.arraySize; j++)
							{
								if (j == i)
									continue;

								_styleActivationsProp.GetArrayElementAtIndex(j)
									.FindPropertyRelative(nameof(StyleActivation.Active)).boolValue = false;
							}
						}
					}

					if (GUILayout.Button("✕", GUILayout.Width(24f)))
						removeAt = i;
				}
			}

			if (removeAt >= 0)
				_styleActivationsProp.DeleteArrayElementAtIndex(removeAt);

			if (GUILayout.Button("Add Style Activation"))
			{
				_styleActivationsProp.arraySize++;
				var entry = _styleActivationsProp.GetArrayElementAtIndex(_styleActivationsProp.arraySize - 1);
				entry.FindPropertyRelative(nameof(StyleActivation.Name)).stringValue = string.Empty;
				entry.FindPropertyRelative(nameof(StyleActivation.Active)).boolValue = !isToggleGroup;
			}

			serializedObject.ApplyModifiedProperties();
		}
	}
}
