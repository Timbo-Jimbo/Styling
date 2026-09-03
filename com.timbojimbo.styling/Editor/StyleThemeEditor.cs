using TimboJimbo.PropertyBindings;
using TimboJimbo.Styling;
using TimboJimboEditor.PropertyBindings.Utility;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace TimboJimboEditor.Styling
{
    [CustomEditor(typeof(StyleTheme))]
    public sealed class StyleThemeEditor : Editor
    {
        // Enum/Reference/String need a target to edit meaningfully; keep themes to plain values.
        private static readonly ValueKind[] Kinds =
        {
            ValueKind.Float, ValueKind.Int, ValueKind.Bool, ValueKind.Color,
            ValueKind.Vector2, ValueKind.Vector3, ValueKind.Vector4, ValueKind.Quaternion
        };
        private static readonly string[] KindLabels = System.Array.ConvertAll(Kinds, k => k.ToString());

        private ReorderableList _list;
        private SerializedProperty _entries;

        private void OnEnable()
        {
            _entries = serializedObject.FindProperty("_entries");
            _list = new ReorderableList(serializedObject, _entries, true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Entries"),
                drawElementCallback = DrawEntry,
                elementHeight = EditorGUIUtility.singleLineHeight + 4f,
                onAddCallback = list =>
                {
                    list.serializedProperty.arraySize++;
                    var element = list.serializedProperty.GetArrayElementAtIndex(list.serializedProperty.arraySize - 1);
                    element.FindPropertyRelative("Key").stringValue = $"Key {list.serializedProperty.arraySize}";
                    element.FindPropertyRelative("Value").boxedValue = ValueContainer.FromDefault(ValueKind.Color);
                }
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            _list.DoLayoutList();
            if (serializedObject.ApplyModifiedProperties())
                MarkDependentsDirty();
        }

        private void DrawEntry(Rect rect, int index, bool isActive, bool isFocused)
        {
            var element = _entries.GetArrayElementAtIndex(index);
            var keyProp = element.FindPropertyRelative("Key");
            var valueProp = element.FindPropertyRelative("Value");
            rect.y += 2f;
            rect.height = EditorGUIUtility.singleLineHeight;

            float keyWidth = rect.width * 0.35f;
            float kindWidth = 80f;
            var keyRect = new Rect(rect.x, rect.y, keyWidth - 4f, rect.height);
            var kindRect = new Rect(keyRect.xMax + 4f, rect.y, kindWidth, rect.height);
            var valueRect = new Rect(kindRect.xMax + 4f, rect.y, rect.xMax - kindRect.xMax - 4f, rect.height);

            keyProp.stringValue = EditorGUI.TextField(keyRect, keyProp.stringValue);

            var value = (ValueContainer)valueProp.boxedValue;
            int kindIndex = System.Array.IndexOf(Kinds, value.Kind);
            int newKindIndex = EditorGUI.Popup(kindRect, Mathf.Max(0, kindIndex), KindLabels);
            if (newKindIndex != kindIndex)
                value = ValueContainer.FromDefault(Kinds[newKindIndex]);

            value = PropertyBindingsEditorGUI.ValueContainerField(valueRect, default, value);
            valueProp.boxedValue = value;
        }

        /// <summary>Live sheets cache resolved values; re-style every sheet whose theme is this asset.</summary>
        private void MarkDependentsDirty()
        {
            var theme = (StyleTheme)target;
            foreach (var source in FindObjectsByType<StyleThemeSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (source.Theme == theme)
                    StylingSystem.MarkDirty(source);
        }
    }
}
