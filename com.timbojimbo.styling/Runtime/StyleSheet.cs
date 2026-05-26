using System;
using System.Collections.Generic;
using TimboJimbo.PropertyBindings;
using UnityEngine;
using UnityEngine.Pool;

namespace TimboJimbo.Styling
{
    [Serializable]
    public struct BindablePropertyToValue
    {
        public BindableProperty Property;
        public ValueContainer Value;
    }

    [Serializable]
    public struct StylePropertyConfig
    {
        public BindableProperty Property;
        public InterpolationConfig Interpolation;
    }

    [Serializable]
    public class Style
    {
        public string Name;
        public List<BindablePropertyToValue> PropertyValues = new List<BindablePropertyToValue>();
    }

    [ExecuteAlways]
    public sealed class StyleSheet : MonoBehaviour, IStyleActivationChangeListener
    {
        [SerializeField] private List<Style> _styles = new List<Style>();
        [SerializeField] private List<BindablePropertyToValue> _baselineValues = new List<BindablePropertyToValue>();
        [SerializeField] private List<StylePropertyConfig> _propertyConfigs = new List<StylePropertyConfig>();

        [SerializeField] private bool _enableInterpolation = true;
        [SerializeField] private float _transitionTime = 0.25f;

        [NonSerialized] private CachedState _cachedState = new CachedState();

        // Cascade subscription state.
        [NonSerialized] private List<BindablePropertyToValue> _fromValues = new List<BindablePropertyToValue>();
        [NonSerialized] private List<BindablePropertyToValue> _targetValues = new List<BindablePropertyToValue>();
        [NonSerialized] private List<BindablePropertyToValue> _currentValues = new List<BindablePropertyToValue>();
        [NonSerialized] private HashSet<string> _activeStyleNames = new HashSet<string>();
        [NonSerialized] private float _t = 1f;
        [NonSerialized] private bool _isTransitioning;
        [NonSerialized] private bool _hasAppliedAnyStyling;

        public IReadOnlyList<Style> Styles => _styles;
        public IReadOnlyList<BindablePropertyToValue> BaselineValues => _baselineValues;
        public IReadOnlyList<StylePropertyConfig> PropertyConfigs => _propertyConfigs;

        public bool EnableInterpolation
        {
            get => _enableInterpolation;
            set => _enableInterpolation = value;
        }

        public float TransitionTime
        {
            get => _transitionTime;
            set => _transitionTime = value;
        }

        public bool IsTransitioning => _isTransitioning;

        private void OnEnable()
        {
            _hasAppliedAnyStyling = false;
            UpdateStylingState(UpdateType.RefreshStyleActivationsAndTargetValues);
        }

        private void OnDisable()
        {
            _cachedState.Dispose();
            _isTransitioning = false;
        }

        private void OnTransformParentChanged()
        {
            UpdateStylingState(UpdateType.RefreshStyleActivationsAndTargetValues);
        }

        private void OnValidate()
        {
            _cachedState?.Invalidate();
            UpdateStylingState(UpdateType.RefreshTargetValuesOnly);
        }

        private void Update()
        {
            // Only want to tick elements that are live
#if UNITY_EDITOR
            if (!EditorAwareUtility.IsLiveInstance(this)) return;
#endif
            if (_isTransitioning)
            {
                if (_transitionTime <= 0f)
                    _t = 1f;
                else
                    _t += Time.deltaTime / _transitionTime;

                if (_t >= 1f)
                {
                    _t = 1f;
                    _isTransitioning = false;
                    ApplyValueList(_targetValues);
                }
                else
                {
                    LerpAndApply(_t);
                }
            }
        }

        public Style GetStyle(string styleName)
        {
            if(!TryGetStyle(styleName, out var style))
                throw new ArgumentException($"No style with name {styleName} exists.");

            return style;
        }

        public bool HasStyle(string styleName)
        {
            return TryGetStyle(styleName, out _);
        }

        public bool TryGetStyle(string styleName, out Style style)
        {
            style = default;
            
            for (int i = 0; i < _styles.Count; i++)
            {
                if (_styles[i].Name == styleName)
                {
                    style = _styles[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Creates a new sparse style.
        /// </summary>
        /// <param name="styleName">Name of the new style.</param>
        /// <param name="propertiesForStyle">Properties this style opts into.</param>
        /// <param name="preEditPropertyValues">Pre-edit values used to seed baseline entries for properties newly introduced.</param>
        /// <param name="postEditPropertyValues">Post-edit values — the values this style commits to.</param>
        public Style CreateStyle(
            string styleName,
            List<BindableProperty> propertiesForStyle,
            List<BindablePropertyToValue> preEditPropertyValues,
            List<BindablePropertyToValue> postEditPropertyValues)
        {
            if (HasStyle(styleName))
                throw new ArgumentException($"A style with name {styleName} already exists.");

            var newStyle = new Style { Name = styleName };

            foreach (var property in propertiesForStyle)
            {
                if (!Util.TryFindIndexByProperty(postEditPropertyValues, property, out var postIdx))
                    throw new ArgumentException($"Post-edit property values must contain an entry for each property in the style. Missing entry for {property.Target.name}.{property.Path} of kind {property.Kind}.");

                newStyle.PropertyValues.Add(postEditPropertyValues[postIdx]);
            }

            _styles.Add(newStyle);
            EnsurePropertiesExistInBaselineAndConfig(propertiesForStyle, preEditPropertyValues);
            UpdateStylingState(UpdateType.RefreshTargetValuesOnly);
            return newStyle;
        }

        /// <summary>
        /// Adds or overwrites the given properties on an existing style. Properties not listed are untouched.
        /// </summary>
        public void EditStyle(
            string styleName,
            List<BindableProperty> propertiesForStyle,
            List<BindablePropertyToValue> preEditPropertyValues,
            List<BindablePropertyToValue> postEditPropertyValues)
        {
            var style = GetStyle(styleName);

            // pre-check to ensure all properties have post-edit values before making any changes
            // to avoid leaving the style in a broken state if the caller messes up the lists
            foreach (var property in propertiesForStyle)
            {
                if (!Util.ContainsProperty(postEditPropertyValues, property))
                    throw new ArgumentException($"Post-edit property values must contain an entry for each property in the style. Missing entry for {property.Target.name}.{property.Path} of kind {property.Kind}.");
            }

            foreach (var property in propertiesForStyle)
            {
                if (!Util.TryFindIndexByProperty(postEditPropertyValues, property, out var postIdx))
                {
                    //this can never happen because of the earlier check,
                    throw new InvalidOperationException("Unexpected error: property not found in post-edit values.");
                }

                var post = postEditPropertyValues[postIdx];

                if (!Util.TryFindIndexByProperty(style.PropertyValues, property, out var existingIdx))
                    style.PropertyValues.Add(post);
                else
                    style.PropertyValues[existingIdx] = post;
            }

            EnsurePropertiesExistInBaselineAndConfig(propertiesForStyle, preEditPropertyValues);
            UpdateStylingState(UpdateType.RefreshTargetValuesOnly);
        }

        public void RenameStyle(string oldName, string newName)
        {
            var style = GetStyle(oldName);
            if (HasStyle(newName))
                throw new ArgumentException($"A style with name {newName} already exists.");
            style.Name = newName;
            UpdateStylingState(UpdateType.RefreshTargetValuesOnly);
        }

        public void DeleteStyle(string styleName)
        {
            var style = GetStyle(styleName);
            _styles.Remove(style);
            PruneUnusedBaselineAndConfigs();
            UpdateStylingState(UpdateType.RefreshTargetValuesOnly);
        }

        public void DeletePropertyFromAllStyles(BindableProperty property)
        {
            for (int i = 0; i < _styles.Count; i++)
            {
                if (Util.TryFindIndexByProperty(_styles[i].PropertyValues, property, out var idx))
                    _styles[i].PropertyValues.RemoveAt(idx);
            }

            if (Util.TryFindIndexByProperty(_baselineValues, property, out var baselineIdx))
                _baselineValues.RemoveAt(baselineIdx);

            if (Util.TryFindIndexByProperty(_propertyConfigs, property, out var configIdx))
                _propertyConfigs.RemoveAt(configIdx);

            _cachedState.Invalidate();
            UpdateStylingState(UpdateType.RefreshTargetValuesOnly);
        }
        
        /// <summary>
        /// Updates a single property on a single style. Does not touch baseline or other styles.
        /// </summary>
        /// <returns>True if the style entry was updated; false if it was not found.</returns>
        public bool TryUpdateStyleValue(string styleName, BindableProperty property, ValueContainer value)
        {
            var style = GetStyle(styleName);
            
            if (!Util.TryFindIndexByProperty(style.PropertyValues, property, out var idx))
                return false;
            
            style.PropertyValues[idx] = new BindablePropertyToValue { Property = property, Value = value };
            UpdateStylingState(UpdateType.RefreshTargetValuesOnly);

            return true;
        }

        /// <summary>
        /// Removes <paramref name="property"/>'s entry from a single style. Does not touch baseline or other styles.
        /// </summary>
        public bool RemoveStyleValue(string styleName, BindableProperty property)
        {
            var style = GetStyle(styleName);

            if (!Util.TryFindIndexByProperty(style.PropertyValues, property, out var idx))
                return false;

            style.PropertyValues.RemoveAt(idx);
            UpdateStylingState(UpdateType.RefreshTargetValuesOnly);

            return true;
        }

        /// <summary>
        /// Overwrites the baseline value for <paramref name="property"/>.
        /// </summary>
        /// <returns>True if a baseline entry was updated; false if it was not found.</returns>
        public bool TryUpdateBaselineValue(BindableProperty property, ValueContainer value)
        {
            if (!Util.TryFindIndexByProperty(_baselineValues, property, out var idx))
                return false;

            _baselineValues[idx] = new BindablePropertyToValue { Property = property, Value = value };
            UpdateStylingState(UpdateType.RefreshTargetValuesOnly);
            
            return true;
        }

        /// <summary>
        /// Reads the current live values of every property the StyleSheet knows about.
        /// </summary>
        public void GetCurrentValues(List<BindablePropertyToValue> propertyValues)
        {
            propertyValues.Clear();
            var binding = _cachedState.GetPropertyBindingCollection(gameObject, _styles, _baselineValues, _propertyConfigs);
            var allProps = _cachedState.GetAllProperties(gameObject, _styles, _baselineValues, _propertyConfigs);

            for (int i = 0; i < allProps.Count; i++)
            {
                var property = allProps[i];
                if (binding.TryRead(property, out var value))
                {
                    propertyValues.Add(new BindablePropertyToValue { Property = property, Value = value });
                }
                else
                {
                    var fallback = ValueContainer.FromDefault(property.Kind);
                    Debug.LogWarning($"Failed to read value for property {property.Target.name}.{property.Path} of kind {property.Kind}. Defaulting to {fallback}.");
                    propertyValues.Add(new BindablePropertyToValue { Property = property, Value = fallback });
                }
            }
        }

        /// <summary>
        /// Replaces each managed baseline entry with the property's current live value.
        /// Entries that can no longer be read are left unchanged.
        /// </summary>
        public void SyncBaselineToCurrentValues()
        {
            var binding = _cachedState.GetPropertyBindingCollection(gameObject, _styles, _baselineValues, _propertyConfigs);

            for (int i = 0; i < _baselineValues.Count; i++)
            {
                var entry = _baselineValues[i];
                if (binding.TryRead(entry.Property, out var liveValue))
                {
                    _baselineValues[i] = new BindablePropertyToValue { Property = entry.Property, Value = liveValue };
                }
                else
                {
                    Debug.LogWarning($"Failed to sync baseline value for property {entry.Property.Target?.name}.{entry.Property.Path} of kind {entry.Property.Kind}; keeping the existing stored value.");
                }
            }

            _cachedState.Invalidate();
            UpdateStylingState(UpdateType.RefreshTargetValuesOnly);
        }

        public void CompleteTransitionImmediate()
        {
            if(!_hasAppliedAnyStyling)
                UpdateStylingState(UpdateType.RefreshStyleActivationsAndTargetValues);
            
            if (_isTransitioning)
            {
                _t = 1f;
                _isTransitioning = false;
                ApplyValueList(_targetValues);
            }
        }

        public void OnStyleActivationsChanged(List<string> activeStyles)
        {
            using (ListPool<string>.Get(out var oldActiveStyles))
            using (ListPool<string>.Get(out var newActiveStyles))
            {
                foreach (var style in _styles)
                {
                    if (_activeStyleNames.Contains(style.Name))
                        oldActiveStyles.Add(style.Name);
                }

                _activeStyleNames.Clear();
                for (int i = 0; i < activeStyles.Count; i++)
                    _activeStyleNames.Add(activeStyles[i]);

                foreach (var style in _styles)
                {
                    if (_activeStyleNames.Contains(style.Name))
                        newActiveStyles.Add(style.Name);
                }

                // No change in styles we actually care about!
                if (Util.ListContentsAreEqual(oldActiveStyles, newActiveStyles))
                    return;
            }

            UpdateStylingState(UpdateType.RefreshTargetValuesOnly);
        }

        private void UpdateStylingState(UpdateType updateType)
        {
            if(!_hasAppliedAnyStyling)
                updateType = UpdateType.RefreshStyleActivationsAndTargetValues;

            if (updateType == UpdateType.RefreshStyleActivationsAndTargetValues)
            {
                _activeStyleNames.Clear();

                using(ListPool<StyleActivation>.Get(out var activations))
                {
                    StylingSystem.GetStyleActivations(gameObject, activations);

                    for (int i = 0; i < activations.Count; i++)
                    {
                        if (activations[i].Active)
                            _activeStyleNames.Add(activations[i].Name);
                    }
                }
            }

            var hasNewTarget = false;
            if (updateType == UpdateType.RefreshTargetValuesOnly || updateType == UpdateType.RefreshStyleActivationsAndTargetValues)
            {
                using(ListPool<BindablePropertyToValue>.Get(out var newTarget))
                {
                    GetTargetValues(_activeStyleNames, newTarget);

                    if (!Util.ListContentsAreEqual(_targetValues, newTarget))
                    {
                        Util.Copy(source: newTarget, destination: _targetValues);
                        hasNewTarget = true;
                    }
                }
            }

            var applyInstantly =
                // if we've never been styled before or...
                !_hasAppliedAnyStyling ||
                // if interpolation is disabled or...
                !_enableInterpolation ||
                // if transition time is zero or negative or...
                _transitionTime <= 0f ||
                // if we're not a live instance (e.g. we are a prefab, inside a prefab stage, edit-time scene, etc)
                !EditorAwareUtility.IsLiveInstance(this);

            if (applyInstantly)
            {
                _t = 1f;
                _isTransitioning = false;

                Util.Copy(source: _targetValues, destination: _fromValues);
                ApplyValueList(_targetValues);
                return;
            }

            var resetAnimationState = hasNewTarget || !_hasAppliedAnyStyling;

            if(resetAnimationState)
            {
                _t = 0f;
                _isTransitioning = true;
                
                _currentValues.Clear();
                GetCurrentValues(_currentValues);

                _fromValues.Clear();
                Util.Copy(source: _currentValues, destination: _fromValues);
            }
        }

        private void GetTargetValues(HashSet<string> activeStyles, List<BindablePropertyToValue> result)
        {
            result.Clear();

            // Start from baseline.
            for (int i = 0; i < _baselineValues.Count; i++)
                result.Add(_baselineValues[i]);

            foreach(var style in _styles)
            {
                if (!activeStyles.Contains(style.Name)) continue;

                foreach (var pv in style.PropertyValues)
                {
                    // Stomp over existing value, or add if missing
                    // (Should never be missing - baseline should cover all properties - but just in case...)
                    // (Should we log a warning and tell the user they should re-sync baseline..?)
                    if (Util.TryFindIndexByProperty(result, pv.Property, out var idx))
                        result[idx] = pv;
                    else
                        result.Add(pv);
                }
            }
        }

        private void LerpAndApply(float t)
        {
            float eased = OutCubic(t);

            using (ListPool<BindablePropertyToValue>.Get(out var lerped))
            {
                for (int i = 0; i < _targetValues.Count; i++)
                {
                    var to = _targetValues[i];
                    var fromIdx = Util.FindIndexByProperty(_fromValues, to.Property);
                    var configIdx = Util.FindIndexByProperty(_propertyConfigs, to.Property);

                    var fromValue = fromIdx != -1 ? _fromValues[fromIdx].Value : to.Value;
                    var lerpConfig = configIdx != -1
                        ? new LerpConfig { Interpolation = _propertyConfigs[configIdx].Interpolation, DiscreteValueSelection = DiscreteValueSelectionMode.RightSide }
                        : new LerpConfig { DiscreteValueSelection = DiscreteValueSelectionMode.RightSide };

                    var v = ValueContainer.Lerp(fromValue, to.Value, eased, lerpConfig);
                    lerped.Add(new BindablePropertyToValue { Property = to.Property, Value = v });
                }

                ApplyValueList(lerped);
            }
        }

        private void ApplyValueList(List<BindablePropertyToValue> values)
        {
            var binding = _cachedState.GetPropertyBindingCollection(gameObject, _styles, _baselineValues, _propertyConfigs);
            using (var writer = binding.StartBulkWriteScope())
            {
                for (int i = 0; i < values.Count; i++)
                    writer.TryWrite(values[i].Property, values[i].Value);
            }

            Util.Copy(source: values, destination: _currentValues);
            _hasAppliedAnyStyling = true;
        }

        private void EnsurePropertiesExistInBaselineAndConfig(
            List<BindableProperty> propertiesForStyle,
            List<BindablePropertyToValue> preEditPropertyValues)
        {
            // for each property, ensure baseline and config values exist
            bool changed = false;
            for (int i = 0; i < propertiesForStyle.Count; i++)
            {
                var property = propertiesForStyle[i];

                if (!Util.ContainsProperty(_baselineValues, property))
                {
                    ValueContainer seed;

                    if (Util.TryFindIndexByProperty(preEditPropertyValues, property, out var seedIdx))
                    {
                        seed = preEditPropertyValues[seedIdx].Value;
                    }
                    else
                    {
                        seed = ValueContainer.FromDefault(property.Kind);
                        Debug.LogWarning($"No seed value was available for baseline property {property.Target?.name}.{property.Path} of kind {property.Kind}. Defaulting to {seed}.");
                    }

                    _baselineValues.Add(new BindablePropertyToValue { Property = property, Value = seed });
                    changed = true;
                }

                if (!Util.ContainsProperty(_propertyConfigs, property))
                {
                    _propertyConfigs.Add(new StylePropertyConfig { Property = property, Interpolation = default });
                    changed = true;
                }
            }
            
            if(changed)
                _cachedState.Invalidate();
        }

        private void PruneUnusedBaselineAndConfigs()
        {
            // Prune unused baseline and configs:
            using(HashSetPool<BindableProperty>.Get(out var liveProperties))
            {
                for (int s = 0; s < _styles.Count; s++)
                {
                    var style = _styles[s];
                    for (int p = 0; p < style.PropertyValues.Count; p++)
                        liveProperties.Add(style.PropertyValues[p].Property);
                }

                bool changed = false;
                for (int i = _baselineValues.Count - 1; i >= 0; i--)
                {
                    if (!liveProperties.Contains(_baselineValues[i].Property))
                    {
                        _baselineValues.RemoveAt(i);
                        changed = true;
                    }
                }

                for (int i = _propertyConfigs.Count - 1; i >= 0; i--)
                {
                    if (!liveProperties.Contains(_propertyConfigs[i].Property))
                    {
                        _propertyConfigs.RemoveAt(i);
                        changed = true;
                    }
                }

                if (changed)
                    _cachedState.Invalidate();
            }
        }


        private static float OutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);

        private static class Util
        {
            public static bool ContainsProperty(List<BindablePropertyToValue> list, BindableProperty property)
            {
                return TryFindIndexByProperty(list, property, out _);
            }

            public static bool ContainsProperty(List<StylePropertyConfig> list, BindableProperty property)
            {
                return TryFindIndexByProperty(list, property, out _);
            }

            public static bool TryFindIndexByProperty(List<BindablePropertyToValue> list, BindableProperty property, out int index)
            {
                index = FindIndexByProperty(list, property);
                return index != -1;
            }

            public static bool TryFindIndexByProperty(List<StylePropertyConfig> list, BindableProperty property, out int index)
            {
                index = FindIndexByProperty(list, property);
                return index != -1;
            }

            public static int FindIndexByProperty(List<BindablePropertyToValue> list, BindableProperty property)
            {
                for (int i = 0; i < list.Count; i++)
                    if (list[i].Property.Equals(property)) return i;
                return -1;
            }

            public static int FindIndexByProperty(List<StylePropertyConfig> list, BindableProperty property)
            {
                for (int i = 0; i < list.Count; i++)
                    if (list[i].Property.Equals(property)) return i;
                return -1;
            }
            
            public static void Copy(List<BindablePropertyToValue> source, List<BindablePropertyToValue> destination)
            {
                destination.Clear();
                for (int i = 0; i < source.Count; i++)
                    destination.Add(source[i]);
            }

            public static bool ListContentsAreEqual(List<BindablePropertyToValue> a, List<BindablePropertyToValue> b)
            {
                if (a.Count != b.Count)
                    return false;

                for (int i = 0; i < a.Count; i++)
                {
                    if (!a[i].Property.Equals(b[i].Property) || !a[i].Value.Equals(b[i].Value))
                        return false;
                }
                return true;
            }

            public static bool ListContentsAreEqual(List<string> a, List<string> b)
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
        }


        private class CachedState : IDisposable
        {
            private readonly List<BindableProperty> _allPropertiesList = new List<BindableProperty>();
            private readonly HashSet<BindableProperty> _allPropertiesHashSet = new HashSet<BindableProperty>(BindablePropertyEqualityComparer.Instance);
            private PropertyBindingCollection _activeBindingCollection;
            private bool _needsRebuild = true;

            public List<BindableProperty> GetAllProperties(GameObject root, List<Style> styles, List<BindablePropertyToValue> baseline, List<StylePropertyConfig> configs)
            {
                EnsureBuilt(root, styles, baseline, configs);
                return _allPropertiesList;
            }

            public PropertyBindingCollection GetPropertyBindingCollection(GameObject root, List<Style> styles, List<BindablePropertyToValue> baseline, List<StylePropertyConfig> configs)
            {
                EnsureBuilt(root, styles, baseline, configs);
                return _activeBindingCollection;
            }

            public void Invalidate()
            {
                _needsRebuild = true;
            }

            private void EnsureBuilt(GameObject root, List<Style> styles, List<BindablePropertyToValue> baseline, List<StylePropertyConfig> configs)
            {
                if (!_needsRebuild) return;
                TearDown();

                for (int i = 0; i < baseline.Count; i++)
                {
                    if (_allPropertiesHashSet.Add(baseline[i].Property))
                        _allPropertiesList.Add(baseline[i].Property);
                }

                for (int s = 0; s < styles.Count; s++)
                {
                    var style = styles[s];
                    for (int p = 0; p < style.PropertyValues.Count; p++)
                    {
                        var prop = style.PropertyValues[p].Property;
                        if (_allPropertiesHashSet.Add(prop))
                            _allPropertiesList.Add(prop);
                    }
                }

                for (int c = 0; c < configs.Count; c++)
                {
                    if (_allPropertiesHashSet.Add(configs[c].Property))
                        _allPropertiesList.Add(configs[c].Property);
                }

                _activeBindingCollection = PropertyBindingCollection.Bind(root, _allPropertiesList);
                _needsRebuild = false;
            }

            private void TearDown()
            {
                _allPropertiesList.Clear();
                _allPropertiesHashSet.Clear();
                _activeBindingCollection?.Dispose();
                _activeBindingCollection = null;
                _needsRebuild = true;
            }

            public void Dispose() => TearDown();
        }

        private enum UpdateType
        {
            RefreshStyleActivationsAndTargetValues,
            RefreshTargetValuesOnly,
        }
    }
}
