using System;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.Styling.Defaults
{
    [CustomPropertyDrawer(typeof(StylePropertyTransitionDefaults.OverrideDefault<>))]
    internal sealed class OverrideDefaultDrawer : PropertyDrawer
    {
        private const float IconSize = 16f;
        private const float Spacing = 4f;
        private const float ValueWidth = 160f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var typeProp = property.FindPropertyRelative(nameof(StylePropertyTransitionDefaults.OverrideDefault<int>.Type));
            var pathProp = property.FindPropertyRelative(nameof(StylePropertyTransitionDefaults.OverrideDefault<int>.PropertyPath));
            var defaultProp = property.FindPropertyRelative(nameof(StylePropertyTransitionDefaults.OverrideDefault<int>.Default));

            var type = string.IsNullOrEmpty(typeProp.stringValue) ? null : Type.GetType(typeProp.stringValue);
            var typeName = type != null ? type.Name : "<Unknown Type>";
            var propName = ObjectNames.NicifyVariableName(pathProp.stringValue);

            var iconRect = new Rect(position.x, position.y, IconSize, position.height);
            var labelRect = new Rect(iconRect.xMax + Spacing, position.y, position.width - IconSize - Spacing - ValueWidth - Spacing, position.height);
            var valueRect = new Rect(position.xMax - ValueWidth, position.y, ValueWidth, position.height);

            var icon = ResolveIcon(type);
            if (icon != null)
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);

            var fullType = type != null ? type.FullName : typeProp.stringValue;
            EditorGUI.LabelField(labelRect, new GUIContent($"{typeName} / {propName}", $"{fullType}.{pathProp.stringValue}"));

            EditorGUI.PropertyField(valueRect, defaultProp, GUIContent.none);
        }

        private static Texture ResolveIcon(Type type)
        {
            if (type == null)
                return null;

            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                var content = EditorGUIUtility.ObjectContent(null, type);
                if (content.image != null)
                    return content.image;
            }

            return AssetPreview.GetMiniTypeThumbnail(type);
        }
    }
}
