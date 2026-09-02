using System;
using System.Collections.Generic;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Styling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TimboJimboEditor.Styling
{
    /// <summary>
    /// Detached, transactional StyleSheet authoring. No scene state changes until Commit succeeds.
    /// Disposing without Commit cancels the edit.
    /// </summary>
    public sealed class StyleSheetEditSession : IDisposable
    {
        private readonly StyleSheet _sheet;
        private readonly string _undoName;
        private readonly List<Style> _styles;
        private readonly List<BindablePropertyToValue> _baseline;
        private readonly List<StylePropertyConfig> _configs;
        private bool _committed;
        private bool _disposed;

        private StyleSheetEditSession(StyleSheet sheet, string undoName)
        {
            _sheet = sheet != null ? sheet : throw new ArgumentNullException(nameof(sheet));
            _undoName = string.IsNullOrWhiteSpace(undoName) ? "Edit Style Sheet" : undoName;

            var snapshot = sheet.CreateSnapshot();
            _styles = snapshot.CreateMutableStyles();
            _baseline = snapshot.CreateMutableBaseline();
            _configs = snapshot.CreateMutableConfigs();
        }

        public static StyleSheetEditSession Begin(StyleSheet sheet, string undoName = "Edit Style Sheet") =>
            new StyleSheetEditSession(sheet, undoName);

        public BindableProperty Bind<TTarget, TValue>(
            TTarget target,
            PropertyDescriptor<TTarget, TValue> descriptor)
            where TTarget : Object
        {
            ThrowIfClosed();
            var property = BindableProperty.Create(target, descriptor);
            EnsureBaseline(property, ReadCurrentValue(property));
            return property;
        }

        public void Bind(BindableProperty property, ValueContainer baselineValue)
        {
            ThrowIfClosed();
            ValidateValue(property, baselineValue, nameof(baselineValue));
            EnsureBaseline(property, baselineValue);
        }

        public StyleEdit UpsertStyle(string styleName)
        {
            ThrowIfClosed();
            ValidateStyleName(styleName);
            var style = FindStyle(styleName);
            if (style == null)
            {
                style = new Style { Name = styleName };
                _styles.Add(style);
            }
            return new StyleEdit(this, style);
        }

        public StyleEdit ReplaceStyle(string styleName)
        {
            ThrowIfClosed();
            ValidateStyleName(styleName);
            var style = FindStyle(styleName);
            if (style == null)
            {
                style = new Style { Name = styleName };
                _styles.Add(style);
            }
            else
            {
                style.PropertyValues.Clear();
            }
            return new StyleEdit(this, style);
        }

        public bool RemoveStyle(string styleName)
        {
            ThrowIfClosed();
            var style = FindStyle(styleName);
            return style != null && _styles.Remove(style);
        }

        public void ClearStyles(bool clearProperties = false)
        {
            ThrowIfClosed();
            _styles.Clear();
            if (clearProperties)
            {
                _baseline.Clear();
                _configs.Clear();
            }
            else
            {
                PruneConfigs();
            }
        }

        public void SetTransition(BindableProperty property, StylePropertyTransition transition)
        {
            ThrowIfClosed();
            if (!property.IsValid) throw new ArgumentException("Transition property is invalid.", nameof(property));
            if (transition.Duration < 0f) throw new ArgumentOutOfRangeException(nameof(transition));

            int index = FindConfig(property);
            var config = new StylePropertyConfig { Property = property, Transition = transition };
            if (index >= 0) _configs[index] = config;
            else _configs.Add(config);
        }

        public StyleSheetValidationReport Validate()
        {
            ThrowIfClosed();
            var issues = new List<StyleSheetValidationIssue>();
            var names = new HashSet<string>();
            var baselineProperties = new HashSet<BindableProperty>(BindablePropertyEqualityComparer.Instance);

            for (int i = 0; i < _baseline.Count; i++)
            {
                var entry = _baseline[i];
                if (!baselineProperties.Add(entry.Property))
                    issues.Add(new StyleSheetValidationIssue(StyleSheetValidationCode.DuplicateProperty,
                        $"Baseline contains duplicate property '{entry.Property.Path}'.", property: entry.Property));
                ValidateProperty(entry, null, issues);
            }

            for (int s = 0; s < _styles.Count; s++)
            {
                var style = _styles[s];
                if (style == null || string.IsNullOrWhiteSpace(style.Name))
                {
                    issues.Add(new StyleSheetValidationIssue(StyleSheetValidationCode.EmptyStyleName,
                        $"Style entry at index {s} has no name."));
                    continue;
                }
                if (!names.Add(style.Name))
                    issues.Add(new StyleSheetValidationIssue(StyleSheetValidationCode.DuplicateStyleName,
                        $"Style name '{style.Name}' is duplicated.", style.Name));

                var local = new HashSet<BindableProperty>(BindablePropertyEqualityComparer.Instance);
                for (int p = 0; p < style.PropertyValues.Count; p++)
                {
                    var entry = style.PropertyValues[p];
                    if (!local.Add(entry.Property))
                        issues.Add(new StyleSheetValidationIssue(StyleSheetValidationCode.DuplicateProperty,
                            $"Style '{style.Name}' contains duplicate property '{entry.Property.Path}'.",
                            style.Name, entry.Property));
                    if (!baselineProperties.Contains(entry.Property))
                        issues.Add(new StyleSheetValidationIssue(StyleSheetValidationCode.MissingBaseline,
                            $"Style '{style.Name}' property '{entry.Property.Path}' has no baseline.",
                            style.Name, entry.Property));
                    ValidateProperty(entry, style.Name, issues);
                }
            }

            return new StyleSheetValidationReport(issues.AsReadOnly());
        }

        public void Commit()
        {
            ThrowIfClosed();
            var validation = Validate();
            if (!validation.IsValid)
                throw new InvalidOperationException(
                    $"Cannot commit StyleSheet edit because validation found {validation.Issues.Count} issue(s): " +
                    validation.Issues[0].Message);

            PruneConfigs();
            Undo.RegisterCompleteObjectUndo(_sheet, _undoName);
            _sheet.ReplaceAuthoredState(_styles, _baseline, _configs);
            PrefabUtility.RecordPrefabInstancePropertyModifications(_sheet);
            EditorUtility.SetDirty(_sheet);
            if (_sheet.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(_sheet.gameObject.scene);
            _committed = true;
            _disposed = true;
        }

        public void Dispose()
        {
            _disposed = true;
        }

        private ValueContainer ReadCurrentValue(BindableProperty property)
        {
            using var collection = PropertyBindingCollection.Bind(_sheet.gameObject, new[] { property });
            if (!collection.TryRead(property, out var value))
                throw new InvalidOperationException(
                    $"Could not read baseline value for '{property.Target?.name}.{property.Path}'.");
            return value;
        }

        private void EnsureBaseline(BindableProperty property, ValueContainer value)
        {
            ValidateValue(property, value, nameof(value));
            int baselineIndex = FindValue(_baseline, property);
            if (baselineIndex < 0)
                _baseline.Add(new BindablePropertyToValue { Property = property, Value = value });

            if (FindConfig(property) < 0)
                _configs.Add(new StylePropertyConfig
                {
                    Property = property,
                    Transition = StylePropertyTransition.GetDefault(property)
                });
        }

        private void SetStyleValue(Style style, BindableProperty property, ValueContainer value)
        {
            ThrowIfClosed();
            ValidateValue(property, value, nameof(value));
            if (FindValue(_baseline, property) < 0)
                throw new InvalidOperationException(
                    $"Property '{property.Path}' must be bound or given a baseline before it can be styled.");

            int index = FindValue(style.PropertyValues, property);
            var entry = new BindablePropertyToValue { Property = property, Value = value };
            if (index >= 0) style.PropertyValues[index] = entry;
            else style.PropertyValues.Add(entry);
        }

        private void ValidateProperty(
            BindablePropertyToValue entry,
            string styleName,
            List<StyleSheetValidationIssue> issues)
        {
            if (!entry.Property.IsValid || entry.Value.Kind != entry.Property.Kind)
            {
                issues.Add(new StyleSheetValidationIssue(StyleSheetValidationCode.InvalidProperty,
                    $"Property '{entry.Property.Path}' or its value kind is invalid.", styleName, entry.Property));
                return;
            }

            var resolution = PropertyBindingRegistry.Diagnose(_sheet.gameObject, entry.Property);
            if (!resolution.Success)
                issues.Add(new StyleSheetValidationIssue(StyleSheetValidationCode.BindingResolutionFailed,
                    $"No binding can be constructed for '{entry.Property.Target?.name}.{entry.Property.Path}'.",
                    styleName, entry.Property, resolution));
        }

        private void PruneConfigs()
        {
            var live = new HashSet<BindableProperty>(BindablePropertyEqualityComparer.Instance);
            for (int i = 0; i < _baseline.Count; i++) live.Add(_baseline[i].Property);
            for (int s = 0; s < _styles.Count; s++)
                for (int p = 0; p < _styles[s].PropertyValues.Count; p++)
                    live.Add(_styles[s].PropertyValues[p].Property);
            for (int i = _configs.Count - 1; i >= 0; i--)
                if (!live.Contains(_configs[i].Property)) _configs.RemoveAt(i);
        }

        private Style FindStyle(string name)
        {
            for (int i = 0; i < _styles.Count; i++)
                if (_styles[i].Name == name) return _styles[i];
            return null;
        }

        private int FindConfig(BindableProperty property)
        {
            for (int i = 0; i < _configs.Count; i++)
                if (_configs[i].Property.Equals(property)) return i;
            return -1;
        }

        private static int FindValue(IReadOnlyList<BindablePropertyToValue> values, BindableProperty property)
        {
            for (int i = 0; i < values.Count; i++)
                if (values[i].Property.Equals(property)) return i;
            return -1;
        }

        private static void ValidateValue(BindableProperty property, ValueContainer value, string parameterName)
        {
            if (!property.IsValid) throw new ArgumentException("Property is invalid.", parameterName);
            if (value.Kind != property.Kind)
                throw new ArgumentException(
                    $"Value kind {value.Kind} does not match property kind {property.Kind}.", parameterName);
        }

        private static void ValidateStyleName(string styleName)
        {
            if (string.IsNullOrWhiteSpace(styleName))
                throw new ArgumentException("Style name cannot be null, empty, or whitespace.", nameof(styleName));
        }

        private void ThrowIfClosed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(StyleSheetEditSession),
                    _committed ? "The edit session has already committed." : "The edit session is disposed.");
        }

        public sealed class StyleEdit
        {
            private readonly StyleSheetEditSession _session;
            private readonly Style _style;

            internal StyleEdit(StyleSheetEditSession session, Style style)
            {
                _session = session;
                _style = style;
            }

            public StyleEdit Set(BindableProperty property, ValueContainer value)
            {
                _session.SetStyleValue(_style, property, value);
                return this;
            }

            public StyleEdit Remove(BindableProperty property)
            {
                int index = FindValue(_style.PropertyValues, property);
                if (index >= 0) _style.PropertyValues.RemoveAt(index);
                return this;
            }
        }
    }
}
