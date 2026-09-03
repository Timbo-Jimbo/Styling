using UnityEngine;

namespace TimboJimbo.Styling
{
    /// <summary>
    /// Declares which <see cref="StyleTheme"/> resolves linked cells for this subtree.
    /// The nearest source up the hierarchy wins. Without one, linked cells use their literal value.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Timbo Jimbo/Styling/Style Theme Source")]
    public sealed class StyleThemeSource : MonoBehaviour
    {
        [SerializeField] private StyleTheme _theme;

        public StyleTheme Theme
        {
            get => _theme;
            set
            {
                if (_theme == value) return;
                _theme = value;
                StylingSystem.MarkDirty(this);
            }
        }

        /// <summary>Resolves the theme in effect for <paramref name="target"/>, or null.</summary>
        public static StyleTheme Resolve(GameObject target)
        {
            for (var t = target.transform; t != null; t = t.parent)
            {
                if (t.TryGetComponent<StyleThemeSource>(out var source) && source.enabled && source._theme != null)
                    return source._theme;
            }
            return null;
        }

        private void OnEnable() => StylingSystem.MarkDirty(this);
        private void OnDisable() => StylingSystem.MarkDirty(this);
        private void OnTransformParentChanged() => StylingSystem.MarkDirty(this);

        private void OnValidate()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () => { if (this != null) StylingSystem.MarkDirty(this); };
#endif
        }
    }
}
