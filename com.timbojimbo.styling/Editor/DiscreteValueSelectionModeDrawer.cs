using TimboJimbo.PropertyBindings;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.Styling
{
    [CustomPropertyDrawer(typeof(DiscreteValueSelectionMode))]
    internal sealed class DiscreteValueSelectionModeDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(rect, label, property);

            var buttonRect = EditorGUI.PrefixLabel(rect, label);
            var currentValue = (DiscreteValueSelectionMode)property.enumValueIndex;
            StylingEditorGUI.DiscreteValueSelectionModePopup(buttonRect, currentValue, newValue =>
            {
                property.enumValueIndex = (int)newValue;
                property.serializedObject.ApplyModifiedProperties();
            });

            EditorGUI.EndProperty();
        }
    }
}
