using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.Styling.Defaults
{
    internal static class StylingSettingsProvider
    {
        private class DefaultsWrapper : ScriptableObject
        {
            public StylePropertyTransitionDefaults Defaults = new StylePropertyTransitionDefaults();
        }

        [SettingsProvider]
        private static SettingsProvider CreateSettingsProvider()
        {
            DefaultsWrapper wrapper = null;
            SerializedObject serializedObject = null;

            return new SettingsProvider("Preferences/TimboJimbo/Styling", SettingsScope.User)
            {
                label = "Styling",
                keywords = new HashSet<string>(new[] { "Styling", "Transition", "Interpolation", "Ease", "Default" }),
                activateHandler = (_, _) =>
                {
                    wrapper = ScriptableObject.CreateInstance<DefaultsWrapper>();
                    wrapper.Defaults = StylingSettings.Defaults;
                    serializedObject = new SerializedObject(wrapper);
                },
                deactivateHandler = () =>
                {
                    if (wrapper != null)
                        Object.DestroyImmediate(wrapper);

                    wrapper = null;
                    serializedObject = null;
                },
                guiHandler = _ =>
                {
                    if (serializedObject == null)
                        return;

                    serializedObject.Update();

                    var previousLabelWidth = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = 250;

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(10);
                    EditorGUILayout.BeginVertical();
                    GUILayout.Space(10);

                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(DefaultsWrapper.Defaults)), true);

                    if (EditorGUI.EndChangeCheck())
                    {
                        serializedObject.ApplyModifiedProperties();
                        StylingSettings.Save();
                    }

                    EditorGUILayout.EndVertical();
                    EditorGUILayout.EndHorizontal();

                    EditorGUIUtility.labelWidth = previousLabelWidth;
                }
            };
        }
    }
}

