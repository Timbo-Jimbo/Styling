using System;
using System.Collections.Generic;
using TimboJimbo.Core;
using TimboJimbo.Core.Utility;
using TimboJimbo.PropertyBindings;
using UnityEngine;
using UnityEngine.Pool;

namespace TimboJimbo.Styling
{
    /// <summary>
    /// Styles exactly one bindable property: a baseline value (literal or theme-linked), optional
    /// sparse per-style overrides, and a single transition. Use it instead of a <see cref="StyleSheet"/>
    /// when a single field needs to react to style activations.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Timbo Jimbo/Styling/Styled Property")]
    public sealed class StyledProperty : MonoBehaviour, IStyleActivationChangeListener
    {
        [Serializable]
        public struct StyleValue
        {
            public string StyleName;
            /// <summary>Literal value. Also the fallback when <see cref="ThemeKey"/> cannot be resolved.</summary>
            public ValueContainer Value;
            /// <summary>When non-empty, the value is taken from the nearest <see cref="StyleThemeSource"/>'s theme.</summary>
            public string ThemeKey;

            public bool IsThemed => !string.IsNullOrEmpty(ThemeKey);
        }

        [SerializeField] private BindableProperty _property;
        [SerializeField] private ValueContainer _baseline;
        [SerializeField] private string _baselineThemeKey = string.Empty;
        [SerializeField] private List<StyleValue> _styleValues = new List<StyleValue>();
        [SerializeField] private StylePropertyTransition _transition;

        [NonSerialized] private PropertyBindingCollection _binding;
        [NonSerialized] private BindableProperty _boundProperty;
        [NonSerialized] private BindableProperty[] _bindBuffer;
        [NonSerialized] private HashSet<string> _activeStyleNames = new HashSet<string>();
        [NonSerialized] private ValueContainer _targetValue;
        [NonSerialized] private ValueContainer _fromValue;
        [NonSerialized] private ValueContainer _currentValue;
        [NonSerialized] private float _transitionTime = float.MaxValue;
        [NonSerialized] private bool _isTransitioning;
        [NonSerialized] private bool _hasAppliedAnyStyling;
        [NonSerialized] private bool _isApplying;
        [NonSerialized] private bool _warnedKindMismatch;
        [NonSerialized] private StyleTheme _resolvedTheme;

        public BindableProperty Property => _property;
        public bool IsTransitioning => _isTransitioning;
        public IReadOnlyList<StyleValue> StyleValues => _styleValues;

        /// <summary>Literal baseline value. Also the fallback when <see cref="BaselineThemeKey"/> cannot be resolved.</summary>
        public ValueContainer Baseline
        {
            get => _baseline;
            set
            {
                _baseline = value;
                UpdateStylingState(UpdateType.RefreshTargetValueOnly);
            }
        }

        /// <summary>When non-empty, the baseline is taken from the nearest <see cref="StyleThemeSource"/>'s theme.</summary>
        public string BaselineThemeKey
        {
            get => _baselineThemeKey;
            set
            {
                _baselineThemeKey = value ?? string.Empty;
                UpdateStylingState(UpdateType.RefreshTargetValueOnly);
            }
        }

        public StylePropertyTransition Transition
        {
            get => _transition;
            set => _transition = value;
        }

        /// <summary>The theme currently resolving linked values for this component, or null.</summary>
        public StyleTheme ResolvedTheme => _resolvedTheme;

        /// <summary>
        /// Points this component at a new property: the baseline is re-seeded from the live value,
        /// style values are cleared, and the transition is re-seeded from the property's defaults.
        /// </summary>
        public void SetProperty(BindableProperty property)
        {
            ReleaseBinding();

            _property = property;
            _styleValues.Clear();
            _baselineThemeKey = string.Empty;
            _warnedKindMismatch = false;
            _hasAppliedAnyStyling = false;
            _isTransitioning = false;
            _transitionTime = float.MaxValue;
            _currentValue = default;

            if (HasBindableProperty)
            {
                _transition = StylePropertyTransition.GetDefault(property);
                _baseline = TryReadLiveValue(out var liveValue) ? liveValue : ValueContainer.FromDefault(property.Kind);
            }
            else
            {
                _transition = StylePropertyTransition.Instant;
                _baseline = default;
            }

            UpdateStylingState(UpdateType.RefreshStyleActivationsAndTargetValue);
        }

        public bool TryGetStyleValue(string styleName, out ValueContainer value)
        {
            if (TryIndexOf(styleName, out var index))
            {
                value = _styleValues[index].Value;
                return true;
            }

            value = default;
            return false;
        }

        /// <summary>Adds or updates the literal value for <paramref name="styleName"/>. Keeps any existing theme link.</summary>
        public void SetStyleValue(string styleName, ValueContainer value)
        {
            if (string.IsNullOrWhiteSpace(styleName))
                throw new ArgumentException("Style name cannot be null, empty, or whitespace.", nameof(styleName));

            if (TryIndexOf(styleName, out var index))
            {
                var existing = _styleValues[index];
                existing.Value = value;
                _styleValues[index] = existing;
            }
            else
            {
                _styleValues.Add(new StyleValue { StyleName = styleName, Value = value, ThemeKey = string.Empty });
            }

            UpdateStylingState(UpdateType.RefreshTargetValueOnly);
        }

        /// <summary>
        /// Links (non-empty key) or unlinks (null/empty) a style's value to a theme key. The literal
        /// value is kept as the fallback. Adds an entry seeded from the baseline when none exists.
        /// </summary>
        public void SetStyleThemeKey(string styleName, string themeKey)
        {
            if (string.IsNullOrWhiteSpace(styleName))
                throw new ArgumentException("Style name cannot be null, empty, or whitespace.", nameof(styleName));

            if (TryIndexOf(styleName, out var index))
            {
                var existing = _styleValues[index];
                existing.ThemeKey = themeKey ?? string.Empty;
                _styleValues[index] = existing;
            }
            else
            {
                _styleValues.Add(new StyleValue { StyleName = styleName, Value = _baseline, ThemeKey = themeKey ?? string.Empty });
            }

            UpdateStylingState(UpdateType.RefreshTargetValueOnly);
        }

        public bool RemoveStyleValue(string styleName)
        {
            if (!TryIndexOf(styleName, out var index))
                return false;

            _styleValues.RemoveAt(index);
            UpdateStylingState(UpdateType.RefreshTargetValueOnly);
            return true;
        }

        /// <summary>Replaces the baseline with the property's current live value.</summary>
        public void PullBaselineFromScene()
        {
            if (!TryReadLiveValue(out var liveValue))
            {
                Debug.LogWarning($"Failed to sync baseline value for property {_property.Target?.name}.{_property.Path} of kind {_property.Kind}; keeping the existing stored value.", this);
                return;
            }

            _baseline = liveValue;
            UpdateStylingState(UpdateType.RefreshTargetValueOnly);
        }

        public void CompleteTransitionImmediate()
        {
            if (!_hasAppliedAnyStyling)
                UpdateStylingState(UpdateType.RefreshStyleActivationsAndTargetValue);

            if (!_isTransitioning)
                return;

            _transitionTime = float.MaxValue;
            _isTransitioning = false;
            ApplyValue(_targetValue);
        }

        public void OnStyleActivationsChanged(List<string> activeStyles)
        {
            var theme = ResolveTheme();
            var themeChanged = theme != _resolvedTheme;

            using (ListPool<string>.Get(out var oldRelevant))
            using (ListPool<string>.Get(out var newRelevant))
            {
                for (int i = 0; i < _styleValues.Count; i++)
                {
                    if (_activeStyleNames.Contains(_styleValues[i].StyleName))
                        oldRelevant.Add(_styleValues[i].StyleName);
                }

                _activeStyleNames.Clear();
                for (int i = 0; i < activeStyles.Count; i++)
                    _activeStyleNames.Add(activeStyles[i]);

                for (int i = 0; i < _styleValues.Count; i++)
                {
                    if (_activeStyleNames.Contains(_styleValues[i].StyleName))
                        newRelevant.Add(_styleValues[i].StyleName);
                }

                // No change in styles we actually care about!
                if (_hasAppliedAnyStyling && !themeChanged && ListContentsAreEqual(oldRelevant, newRelevant))
                    return;
            }

            UpdateStylingState(UpdateType.RefreshTargetValueOnly);
        }

        private void Reset()
        {
            // Default the target to our own GameObject; the property picker takes it from there.
            _property = BindableProperty.CreateUnresolvedTarget(gameObject);
            _baseline = default;
            _baselineThemeKey = string.Empty;
            _styleValues.Clear();
            _transition = StylePropertyTransition.Instant;
        }

        private void OnEnable()
        {
            _hasAppliedAnyStyling = false;
            _isTransitioning = false;
            _transitionTime = float.MaxValue;
            UpdateStylingState(UpdateType.RefreshStyleActivationsAndTargetValue);
        }

        private void OnDisable()
        {
            // Same policy as StyleSheet: release the binding, but leave the last applied value in place.
            ReleaseBinding();
            _isTransitioning = false;
        }

        private void OnTransformParentChanged()
        {
            StylingSystem.MarkDirty(this);
        }

        private void OnDidApplyAnimationProperties()
        {
            // Our own writes can bounce back here through the binding's notification; ignore those.
            if (_isApplying)
                return;

            StylingSystem.MarkDirty(this);
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            // We can't poke other objects during OnValidate, so defer.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null)
                    return;

                // Inspector edits re-resolve from scratch and apply instantly.
                _hasAppliedAnyStyling = false;
                StylingSystem.MarkDirty(this);
            };
#endif
        }

        private void Update()
        {
            // Only want to tick elements that are live
#if UNITY_EDITOR
            if (!EditorAwareUtility.IsLiveInstance(this)) return;
#endif
            if (!_isTransitioning)
                return;

            if (!_transition.Animate || _transition.Duration <= 0f || _fromValue.Kind != _targetValue.Kind)
            {
                _isTransitioning = false;
                _transitionTime = float.MaxValue;
                ApplyValue(_targetValue);
                return;
            }

            _transitionTime += Time.smoothDeltaTime;

            var lerpT = Mathf.Clamp01(_transitionTime / _transition.Duration);
            var complete = lerpT >= 1f;

            lerpT = EaseUtility.Evaluate(lerpT, _transition.EaseType);
            ApplyValue(ValueContainer.LerpUnclamped(_fromValue, _targetValue, lerpT, _transition.Interpolation, _transition.DiscreteValueSelection));

            if (complete)
            {
                _isTransitioning = false;
                _transitionTime = float.MaxValue;
            }
        }

        private void UpdateStylingState(UpdateType updateType)
        {
            if (!HasBindableProperty)
                return;

            if (!_hasAppliedAnyStyling)
                updateType = UpdateType.RefreshStyleActivationsAndTargetValue;

            if (updateType == UpdateType.RefreshStyleActivationsAndTargetValue)
            {
                _activeStyleNames.Clear();

                using (ListPool<StyleActivation>.Get(out var activations))
                {
                    StylingSystem.GetStyleActivations(gameObject, activations);

                    for (int i = 0; i < activations.Count; i++)
                    {
                        if (activations[i].Active)
                            _activeStyleNames.Add(activations[i].Name);
                    }
                }
            }

            var newTarget = ResolveTargetValue();
            var hasNewTarget = !_hasAppliedAnyStyling || !newTarget.Equals(_targetValue);
            _targetValue = newTarget;

            var applyInstantly =
                // if we've never been styled before or...
                !_hasAppliedAnyStyling ||
                // if this property doesn't want to transition or...
                !_transition.Animate || _transition.Duration <= 0f ||
                // if we're not a live instance (e.g. we are a prefab, inside a prefab stage, edit-time scene, etc)
                !EditorAwareUtility.IsLiveInstance(this);

            if (applyInstantly)
            {
                _transitionTime = float.MaxValue;
                _isTransitioning = false;
                _fromValue = _targetValue;
                ApplyValue(_targetValue);
                return;
            }

            if (!hasNewTarget)
                return;

            _transitionTime = 0f;
            _isTransitioning = true;
            _fromValue = GetTransitionStartValue();
        }

        private ValueContainer ResolveTargetValue()
        {
            _resolvedTheme = ResolveTheme();

            var result = ResolveThemed(_baseline, _baselineThemeKey);

            for (int i = 0; i < _styleValues.Count; i++)
            {
                var styleValue = _styleValues[i];
                if (string.IsNullOrEmpty(styleValue.StyleName))
                    continue;
                if (!_activeStyleNames.Contains(styleValue.StyleName))
                    continue;

                // Later entries stomp over earlier ones.
                result = ResolveThemed(styleValue.Value, styleValue.ThemeKey);
            }

            return result;
        }

        private StyleTheme ResolveTheme() => StyleThemeSource.Resolve(gameObject);

        /// <summary>Substitutes the theme value when the cell is linked, the key exists, and the kind matches; otherwise the literal.</summary>
        private ValueContainer ResolveThemed(ValueContainer literal, string themeKey)
        {
            if (string.IsNullOrEmpty(themeKey) || _resolvedTheme == null)
                return literal;

            if (_resolvedTheme.TryGetValue(themeKey, out var themed) && themed.Kind == _property.Kind)
                return themed;

            return literal;
        }

        private ValueContainer GetTransitionStartValue()
        {
            if (TryReadLiveValue(out var liveValue) && liveValue.Kind == _property.Kind)
                return liveValue;

            return _currentValue.Kind == _property.Kind ? _currentValue : _targetValue;
        }

        private void ApplyValue(ValueContainer value)
        {
            if (!TryGetBinding(out var binding))
                return;

            if (value.Kind != _property.Kind)
            {
                WarnKindMismatchOnce(value);
                return;
            }

            _isApplying = true;
            try
            {
                binding.TryWrite(_property, value);
            }
            finally
            {
                _isApplying = false;
            }

            _currentValue = value;
            _hasAppliedAnyStyling = true;

#if UNITY_EDITOR
            if (!EditorAwareUtility.IsLiveInstance(this))
                UnityEditor.SceneView.RepaintAll();
#endif
        }

        private bool HasBindableProperty => _property.Target != null && _property.Kind != ValueKind.Invalid;

        private bool TryReadLiveValue(out ValueContainer value)
        {
            if (TryGetBinding(out var binding))
                return binding.TryRead(_property, out value);

            value = default;
            return false;
        }

        private bool TryGetBinding(out PropertyBindingCollection binding)
        {
            binding = null;

            if (!HasBindableProperty)
                return false;

            if (_binding == null || !_boundProperty.Equals(_property))
            {
                ReleaseBinding();
                _bindBuffer ??= new BindableProperty[1];
                _bindBuffer[0] = _property;
                _binding = PropertyBindingCollection.Bind(gameObject, _bindBuffer);
                _boundProperty = _property;
            }

            binding = _binding;
            return true;
        }

        private void ReleaseBinding()
        {
            _binding?.Dispose();
            _binding = null;
            _boundProperty = BindableProperty.Invalid;
        }

        private void WarnKindMismatchOnce(ValueContainer value)
        {
            if (_warnedKindMismatch)
                return;

            _warnedKindMismatch = true;
            Debug.LogWarning($"Styled value of kind {value.Kind} does not match property {_property.Target?.name}.{_property.Path} of kind {_property.Kind}; nothing was written.", this);
        }

        private bool TryIndexOf(string styleName, out int index)
        {
            for (index = 0; index < _styleValues.Count; index++)
            {
                if (_styleValues[index].StyleName == styleName)
                    return true;
            }

            index = -1;
            return false;
        }

        private static bool ListContentsAreEqual(List<string> a, List<string> b)
        {
            if (a.Count != b.Count)
                return false;

            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }

        private enum UpdateType
        {
            RefreshStyleActivationsAndTargetValue,
            RefreshTargetValueOnly,
        }
    }
}
