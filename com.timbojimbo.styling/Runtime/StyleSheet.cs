using System;
using System.Collections.Generic;
using TimboJimbo.Core;
using TimboJimbo.Core.Utility;
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
        public StylePropertyTransition Transition;
    }

    [Serializable] 
    public partial struct StylePropertyTransition
    {
        public bool Animate => Duration > 0;
        public EaseType EaseType;
        public float Duration;
        public InterpolationConfig Interpolation;
        public DiscreteValueSelectionMode DiscreteValueSelection;
        public static StylePropertyTransition Instant => new StylePropertyTransition { Duration = 0 };
    }

    public partial struct StylePropertyTransition
    {
        private static IDefaultsResolver _resolver;

        public static StylePropertyTransition GetDefault(BindableProperty property, float duration = 0f)
        {
            return _resolver == null ? Instant : _resolver.GetDefaultTransition(property, duration);
        }

        internal static void SetDefaultsResolver(IDefaultsResolver resolver)
        {
            _resolver = resolver;
        }

        internal interface IDefaultsResolver
        {
            StylePropertyTransition GetDefaultTransition(BindableProperty property, float duration = 0f);
        }
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

        // Cascade subscription state.
        [NonSerialized] private List<BindablePropertyToValue> _fromValues = new List<BindablePropertyToValue>();
        [NonSerialized] private List<BindablePropertyToValue> _targetValues = new List<BindablePropertyToValue>();
        [NonSerialized] private List<BindablePropertyToValue> _currentValues = new List<BindablePropertyToValue>();
        [NonSerialized] private HashSet<string> _activeStyleNames = new HashSet<string>();
        [NonSerialized] private float _transitionTime = float.MaxValue;
        [NonSerialized] private bool _isTransitioning;
        [NonSerialized] private bool _hasAppliedAnyStyling;
        [NonSerialized] private BindingCollectionManager _bindingCollectionManager;

        public IReadOnlyList<Style> Styles => _styles;
        public IReadOnlyList<BindablePropertyToValue> BaselineValues => _baselineValues;
        public IReadOnlyList<StylePropertyConfig> PropertyConfigs => _propertyConfigs;
        public bool IsTransitioning => _isTransitioning;

        public StyleSheet() 
        { 
            _bindingCollectionManager = new BindingCollectionManager(this); 
        }

        private void OnEnable()
        {
            // Persistent - keeps the bindings alive across Update ticks while enabled.
            _bindingCollectionManager.AcquirePersistent();
            _hasAppliedAnyStyling = false;
            UpdateStylingState(UpdateType.RefreshStyleActivationsAndTargetValues);
        }

        private void OnDisable()
        {
            // Release. If nothing else currently holds a handle (typical) the
            // underling binding collection is disposed
            _bindingCollectionManager.ReleasePersistent();
            _isTransitioning = false;
        }

        private void OnTransformParentChanged()
        {
            using (_bindingCollectionManager.Acquire())
                UpdateStylingState(UpdateType.RefreshStyleActivationsAndTargetValues);
        }

        private void Update()
        {
            // Only want to tick elements that are live
#if UNITY_EDITOR
            if (!EditorAwareUtility.IsLiveInstance(this)) return;
#endif
            if (_isTransitioning)
            {
                _transitionTime += Time.smoothDeltaTime;

                var allPropertyAnimationsComplete = true;
                using (ListPool<BindablePropertyToValue>.Get(out var lerped))
                {
                    for (int i = 0; i < _targetValues.Count; i++)
                    {
                        var to = _targetValues[i];
                        var fromIdx = Util.FindIndexByProperty(_fromValues, to.Property);
                        var configIdx = Util.FindIndexByProperty(_propertyConfigs, to.Property);

                        if(fromIdx == -1 || configIdx == -1)
                        {
                            lerped.Add(to);
                            continue;
                        }

                        var from = _fromValues[fromIdx].Value;
                        var propertyAnimation = _propertyConfigs[configIdx].Transition;

                        if (!propertyAnimation.Animate || propertyAnimation.Duration <= 0)
                        {
                            lerped.Add(to);
                            continue;
                        }

                        var propertyLerpT = Mathf.Clamp01(_transitionTime / propertyAnimation.Duration);
                        allPropertyAnimationsComplete &= propertyLerpT >= 1f;

                        propertyLerpT = EaseUtility.Evaluate(propertyLerpT, propertyAnimation.EaseType);
                        var v = ValueContainer.LerpUnclamped(from, to.Value, propertyLerpT, propertyAnimation.Interpolation, propertyAnimation.DiscreteValueSelection);
                        lerped.Add(new BindablePropertyToValue { Property = to.Property, Value = v });

                    }

                    ApplyValueList(lerped);
                }

                if (allPropertyAnimationsComplete)
                {
                    _isTransitioning = false;
                    _transitionTime = float.MaxValue;
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

            using (_bindingCollectionManager.Acquire())
            {
                _styles.Add(newStyle);
                EnsurePropertiesExistInBaselineAndConfig(propertiesForStyle, preEditPropertyValues);
                UpdateStylingState(UpdateType.RefreshTargetValuesOnly);
                return newStyle;
            }
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

            using (_bindingCollectionManager.Acquire())
            {
                foreach (var property in propertiesForStyle)
                {
                    Util.TryFindIndexByProperty(postEditPropertyValues, property, out var postIdx);
                    Util.Upsert(style.PropertyValues, property, postEditPropertyValues[postIdx].Value);
                }

                EnsurePropertiesExistInBaselineAndConfig(propertiesForStyle, preEditPropertyValues);
                UpdateStylingState(UpdateType.RefreshTargetValuesOnly);
            }
        }

        /// <summary>
        /// Adds or overwrites the given properties on the baseline. Properties not listed are untouched.
        /// </summary>
        public void EditBaseline(
            List<BindableProperty> propertiesForBaseline,
            List<BindablePropertyToValue> postEditPropertyValues)
        {
            // pre-check to ensure all properties have post-edit values before making any changes
            foreach (var property in propertiesForBaseline)
            {
                if (!Util.ContainsProperty(postEditPropertyValues, property))
                    throw new ArgumentException($"Post-edit property values must contain an entry for each property in the baseline. Missing entry for {property.Target.name}.{property.Path} of kind {property.Kind}.");
            }

            using (_bindingCollectionManager.Acquire())
            {
                bool addedProperty = false;
                foreach (var property in propertiesForBaseline)
                {
                    Util.TryFindIndexByProperty(postEditPropertyValues, property, out var postIdx);
                    var post = postEditPropertyValues[postIdx];

                    if (Util.Upsert(_baselineValues, property, post.Value))
                        addedProperty = true;

                    if (!Util.ContainsProperty(_propertyConfigs, property))
                    {
                        _propertyConfigs.Add(new StylePropertyConfig { Property = property, Transition = StylePropertyTransition.GetDefault(property) });
                        addedProperty = true;
                    }
                }

                if (addedProperty)
                    _bindingCollectionManager.Invalidate();

                UpdateStylingState(UpdateType.RefreshTargetValuesOnly);
            }
        }

        public void RenameStyle(string oldName, string newName)
        {
            var style = GetStyle(oldName);
            if (HasStyle(newName))
                throw new ArgumentException($"A style with name {newName} already exists.");
            using (_bindingCollectionManager.Acquire())
            {
                style.Name = newName;
                UpdateStylingState(UpdateType.RefreshTargetValuesOnly);
            }
        }

        public void DeleteStyle(string styleName)
        {
            var style = GetStyle(styleName);
            using (_bindingCollectionManager.Acquire())
            {
                _styles.Remove(style);
                UpdateStylingState(UpdateType.RefreshTargetValuesOnly);
            }
        }

        public void RemoveProperty(BindableProperty property)
        {
            using (_bindingCollectionManager.Acquire())
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

                _bindingCollectionManager.Invalidate();
                UpdateStylingState(UpdateType.RefreshTargetValuesOnly);
            }
        }

        /// <summary>
        /// Adds or updates a single property on a single style. If baseline does not already contain an entry
        /// for this property, <paramref name="baselineSeedValue"/> is used to seed it (baseline must always
        /// contain a value for every property referenced by any style).
        /// </summary>
        public void SetStyleValue(string styleName, BindableProperty property, ValueContainer value, ValueContainer baselineSeedValue)
        {
            var style = GetStyle(styleName);

            using (_bindingCollectionManager.Acquire())
            {
                Util.Upsert(style.PropertyValues, property, value);
                using (ListPool<BindableProperty>.Get(out var propertiesForStyle))
                using (ListPool<BindablePropertyToValue>.Get(out var preEditPropertyValues))
                {
                    propertiesForStyle.Add(property);
                    preEditPropertyValues.Add(new BindablePropertyToValue { Property = property, Value = baselineSeedValue });
                    EnsurePropertiesExistInBaselineAndConfig(propertiesForStyle, preEditPropertyValues);
                }

                UpdateStylingState(UpdateType.RefreshTargetValuesOnly);
            }
        }

        /// <summary>
        /// Removes <paramref name="property"/>'s entry from a single style. Does not touch baseline or other styles.
        /// </summary>
        public bool RemoveStyleValue(string styleName, BindableProperty property)
        {
            var style = GetStyle(styleName);

            if (!Util.TryFindIndexByProperty(style.PropertyValues, property, out var idx))
                return false;

            using (_bindingCollectionManager.Acquire())
            {
                style.PropertyValues.RemoveAt(idx);
                PruneUnusedPropertyConfigs();
                UpdateStylingState(UpdateType.RefreshTargetValuesOnly);
            }

            return true;
        }

        /// <summary>
        /// Adds or updates the baseline value for <paramref name="property"/>.
        /// </summary>
        public void SetBaselineValue(BindableProperty property, ValueContainer value)
        {
            using (_bindingCollectionManager.Acquire())
            {
                var addedBaselineEntry = Util.Upsert(_baselineValues, property, value);

                if (!Util.ContainsProperty(_propertyConfigs, property))
                {
                    _propertyConfigs.Add(new StylePropertyConfig { Property = property, Transition = StylePropertyTransition.GetDefault(property) });
                    _bindingCollectionManager.Invalidate();
                }
                else if (addedBaselineEntry)
                {
                    _bindingCollectionManager.Invalidate();
                }

                UpdateStylingState(UpdateType.RefreshTargetValuesOnly);
            }
        }

        /// <summary>
        /// Reads the current live values of every property the StyleSheet knows about.
        /// </summary>
        public void GetCurrentValues(List<BindablePropertyToValue> propertyValues)
        {
            propertyValues.Clear();
            using (var handle = _bindingCollectionManager.Acquire())
            {
                var bindingCollection = handle.BindingCollection;

                foreach(var property in bindingCollection.Properties)
                {
                    var readSuccess = bindingCollection.TryRead(property, out var readValue);

                    if (readSuccess)
                    {
                        propertyValues.Add(new BindablePropertyToValue { Property = property, Value = readValue });
                    }
                    else
                    {
                        var fallback = ValueContainer.FromDefault(property.Kind);
                        Debug.LogWarning($"Failed to read value for property {property.Target.name}.{property.Path} of kind {property.Kind}. Defaulting to {fallback}.");
                        propertyValues.Add(new BindablePropertyToValue { Property = property, Value = fallback });
                    }
                }
            }
        }

        /// <summary>
        /// Replaces each managed baseline entry with the property's current live value.
        /// Entries that can no longer be read are left unchanged.
        /// </summary>
        public void PullBaselineValuesFromScene()
        {
            using (var handle = _bindingCollectionManager.Acquire())
            {
                var binding = handle.BindingCollection;

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

                _bindingCollectionManager.Invalidate();
                UpdateStylingState(UpdateType.RefreshTargetValuesOnly);
            }
        }

        public void CompleteTransitionImmediate()
        {
            using (_bindingCollectionManager.Acquire())
            {
                if(!_hasAppliedAnyStyling)
                    UpdateStylingState(UpdateType.RefreshStyleActivationsAndTargetValues);

                if (_isTransitioning)
                {
                    _transitionTime = float.MaxValue;
                    _isTransitioning = false;
                    ApplyValueList(_targetValues);
                }
            }
        }

        public void OnStyleActivationsChanged(List<string> activeStyles)
        {
            using (_bindingCollectionManager.Acquire())
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

            var _anyPropertiesWithTransitions = false;
            for (int i = 0; i < _propertyConfigs.Count; i++)
            {
                StylePropertyTransition propertyTransition = _propertyConfigs[i].Transition;

                if (propertyTransition.Animate && propertyTransition.Duration > 0)
                {
                    _anyPropertiesWithTransitions = true;
                    break;
                }
            }

            var applyInstantly =
                // if we've never been styled before or...
                !_hasAppliedAnyStyling ||
                // if no properties want to transition or...
                !_anyPropertiesWithTransitions ||
                // if we're not a live instance (e.g. we are a prefab, inside a prefab stage, edit-time scene, etc)
                !EditorAwareUtility.IsLiveInstance(this);

            if (applyInstantly)
            {
                _transitionTime = float.MaxValue;
                _isTransitioning = false;

                Util.Copy(source: _targetValues, destination: _fromValues);
                ApplyValueList(_targetValues);
                return;
            }

            var resetAnimationState = hasNewTarget || !_hasAppliedAnyStyling;

            if(resetAnimationState)
            {
                _transitionTime = 0f;
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

        private void ApplyValueList(List<BindablePropertyToValue> values)
        {
            using (var handle = _bindingCollectionManager.Acquire())
            {
                var binding = handle.BindingCollection;
                using (binding.BulkWriteScope())
                {
                    for (int i = 0; i < values.Count; i++)
                        binding.TryWrite(values[i].Property, values[i].Value);
                }
            }

            Util.Copy(source: values, destination: _currentValues);
            _hasAppliedAnyStyling = true;

            #if UNITY_EDITOR
            if (!EditorAwareUtility.IsLiveInstance(this))
                UnityEditor.SceneView.RepaintAll();
            #endif
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
                    _propertyConfigs.Add(new StylePropertyConfig { Property = property, Transition = StylePropertyTransition.GetDefault(property) });
                    changed = true;
                }
            }
            
            if(changed)
                _bindingCollectionManager.Invalidate();
        }

        private void PruneUnusedPropertyConfigs()
        {
            // Prune orphaned interpolation configs.
            using(HashSetPool<BindableProperty>.Get(out var liveProperties))
            {
                for (int s = 0; s < _styles.Count; s++)
                {
                    var style = _styles[s];
                    for (int p = 0; p < style.PropertyValues.Count; p++)
                        liveProperties.Add(style.PropertyValues[p].Property);
                }

                for (int b = 0; b < _baselineValues.Count; b++)
                    liveProperties.Add(_baselineValues[b].Property);

                bool changed = false;
                for (int i = _propertyConfigs.Count - 1; i >= 0; i--)
                {
                    if (!liveProperties.Contains(_propertyConfigs[i].Property))
                    {
                        _propertyConfigs.RemoveAt(i);
                        changed = true;
                    }
                }

                if (changed)
                    _bindingCollectionManager.Invalidate();
            }
        }

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

            public static bool Upsert(List<BindablePropertyToValue> list, BindableProperty property, ValueContainer value)
            {
                var entry = new BindablePropertyToValue { Property = property, Value = value };
                if (TryFindIndexByProperty(list, property, out var idx))
                {
                    list[idx] = entry;
                    return false; // updated existing
                }

                list.Add(entry);
                return true; // added new
            }
        }

        private class BindingCollectionManager
        {
            private readonly StyleSheet _owner;
            private PropertyBindingCollection _bindingCollection;
            private int _handleCount;
            private bool _setup = true;

            public BindingCollectionManager(StyleSheet owner) { _owner = owner; }

            public BindingCollectionHandle Acquire()
            {
                _handleCount++;
                return new BindingCollectionHandle(this);
            }

            public void AcquirePersistent() => _handleCount++;
            public void ReleasePersistent() => Release();

            public void Invalidate() => _setup = true;

            private void Release()
            {
                _handleCount--;
                if (_handleCount <= 0)
                {
                    _handleCount = 0;
                    CleanUp();
                }
            }

            private void EnsureSetup()
            {
                if (!_setup) return;
                CleanUp();

                var baseline = _owner._baselineValues;
                var styles = _owner._styles;
                var configs = _owner._propertyConfigs;

                using (HashSetPool<BindableProperty>.Get(out var _allPropertiesHashSet))
                using (ListPool<BindableProperty>.Get(out var _allPropertiesList))
                {
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

                    _bindingCollection = PropertyBindingCollection.Bind(_owner.gameObject, _allPropertiesList);
                    _setup = false;
                }
            }

            private void CleanUp()
            {
                _bindingCollection?.Dispose();
                _bindingCollection = null;
                _setup = true;
            }

            public struct BindingCollectionHandle : IDisposable
            {
                private BindingCollectionManager _lifetime;
                internal BindingCollectionHandle(BindingCollectionManager lifetime) { _lifetime = lifetime; }

                public PropertyBindingCollection BindingCollection
                {
                    get
                    {
                        ThrowIfDisposed();
                        _lifetime.EnsureSetup();
                        return _lifetime._bindingCollection;
                    }
                }

                public void Dispose()
                {
                    if (_lifetime != null)
                    {
                        _lifetime.Release();
                        _lifetime = null;
                    }
                }

                private void ThrowIfDisposed()
                {
                    if (_lifetime == null) throw new ObjectDisposedException(nameof(BindingCollectionHandle));
                }
            }
        }

        private enum UpdateType
        {
            RefreshStyleActivationsAndTargetValues,
            RefreshTargetValuesOnly,
        }
    }
}
