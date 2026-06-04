using TimboJimbo.PropertyBindings;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.Styling
{
    [CustomPropertyDrawer(typeof(ColorInterpolationMode))]
    internal sealed class ColorInterpolationModeDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(rect, label, property);

            var buttonRect = EditorGUI.PrefixLabel(rect, label);
            var currentValue = (ColorInterpolationMode)property.enumValueIndex;
            StylingEditorGUI.ColorInterpolationModePopup(buttonRect, currentValue, newValue =>
            {
                property.enumValueIndex = (int)newValue;
                property.serializedObject.ApplyModifiedProperties();
            });

            EditorGUI.EndProperty();
        }
    }
}