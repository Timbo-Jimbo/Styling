using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TimboJimbo.PropertyBindings;

namespace TimboJimbo.Styling
{
    public sealed class StyleSnapshot
    {
        private readonly BindablePropertyToValue[] _propertyValues;
        private readonly ReadOnlyCollection<BindablePropertyToValue> _readOnlyPropertyValues;

        public string Name { get; }
        public bool IsNull { get; }
        public IReadOnlyList<BindablePropertyToValue> PropertyValues => _readOnlyPropertyValues;

        internal StyleSnapshot(Style style)
        {
            IsNull = style == null;
            Name = style?.Name;
            _propertyValues = style == null
                ? Array.Empty<BindablePropertyToValue>()
                : Copy(style.PropertyValues);
            _readOnlyPropertyValues = Array.AsReadOnly(_propertyValues);
        }

        internal Style ToMutableStyle() => IsNull
            ? null
            : new Style
            {
                Name = Name,
                PropertyValues = new List<BindablePropertyToValue>(_propertyValues)
            };

        private static BindablePropertyToValue[] Copy(IReadOnlyList<BindablePropertyToValue> source)
        {
            var result = new BindablePropertyToValue[source.Count];
            for (int i = 0; i < source.Count; i++) result[i] = source[i];
            return result;
        }
    }

    /// <summary>Immutable, detached view of a StyleSheet's authored and resolved state.</summary>
    public sealed class StyleSheetSnapshot
    {
        private readonly StyleSnapshot[] _styles;
        private readonly BindablePropertyToValue[] _baselineValues;
        private readonly StylePropertyConfig[] _propertyConfigs;
        private readonly string[] _activeStyleNames;
        private readonly BindablePropertyToValue[] _resolvedValues;
        private readonly ReadOnlyCollection<StyleSnapshot> _readOnlyStyles;
        private readonly ReadOnlyCollection<BindablePropertyToValue> _readOnlyBaselineValues;
        private readonly ReadOnlyCollection<StylePropertyConfig> _readOnlyPropertyConfigs;
        private readonly ReadOnlyCollection<string> _readOnlyActiveStyleNames;
        private readonly ReadOnlyCollection<BindablePropertyToValue> _readOnlyResolvedValues;

        public IReadOnlyList<StyleSnapshot> Styles => _readOnlyStyles;
        public IReadOnlyList<BindablePropertyToValue> BaselineValues => _readOnlyBaselineValues;
        public IReadOnlyList<StylePropertyConfig> PropertyConfigs => _readOnlyPropertyConfigs;
        public IReadOnlyList<string> ActiveStyleNames => _readOnlyActiveStyleNames;
        public IReadOnlyList<BindablePropertyToValue> ResolvedValues => _readOnlyResolvedValues;
        public StyleSheetValidationReport Validation { get; }

        internal StyleSheetSnapshot(
            IReadOnlyList<Style> styles,
            IReadOnlyList<BindablePropertyToValue> baselineValues,
            IReadOnlyList<StylePropertyConfig> propertyConfigs,
            IReadOnlyCollection<string> activeStyleNames,
            IReadOnlyList<BindablePropertyToValue> resolvedValues,
            StyleSheetValidationReport validation)
        {
            _styles = new StyleSnapshot[styles.Count];
            for (int i = 0; i < styles.Count; i++)
                _styles[i] = new StyleSnapshot(styles[i]);

            _baselineValues = Copy(baselineValues);
            _propertyConfigs = Copy(propertyConfigs);
            _activeStyleNames = new string[activeStyleNames.Count];
            int activeIndex = 0;
            foreach (var styleName in activeStyleNames) _activeStyleNames[activeIndex++] = styleName;
            _resolvedValues = Copy(resolvedValues);
            _readOnlyStyles = Array.AsReadOnly(_styles);
            _readOnlyBaselineValues = Array.AsReadOnly(_baselineValues);
            _readOnlyPropertyConfigs = Array.AsReadOnly(_propertyConfigs);
            _readOnlyActiveStyleNames = Array.AsReadOnly(_activeStyleNames);
            _readOnlyResolvedValues = Array.AsReadOnly(_resolvedValues);
            Validation = validation ?? throw new ArgumentNullException(nameof(validation));
        }

        internal List<Style> CreateMutableStyles()
        {
            var result = new List<Style>(_styles.Length);
            for (int i = 0; i < _styles.Length; i++) result.Add(_styles[i].ToMutableStyle());
            return result;
        }

        internal List<BindablePropertyToValue> CreateMutableBaseline() => new(_baselineValues);
        internal List<StylePropertyConfig> CreateMutableConfigs() => new(_propertyConfigs);

        private static BindablePropertyToValue[] Copy(IReadOnlyList<BindablePropertyToValue> source)
        {
            var result = new BindablePropertyToValue[source.Count];
            for (int i = 0; i < source.Count; i++) result[i] = source[i];
            return result;
        }

        private static StylePropertyConfig[] Copy(IReadOnlyList<StylePropertyConfig> source)
        {
            var result = new StylePropertyConfig[source.Count];
            for (int i = 0; i < source.Count; i++) result[i] = source[i];
            return result;
        }
    }
}
