 using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace TimboJimbo.Styling
{
    public sealed class StyleGroup : MonoBehaviour, IStyleActivationSource
    {
        [SerializeField] private List<StyleActivation> _styleActivations = new List<StyleActivation>();

        public IReadOnlyList<StyleActivation> StyleActivations => _styleActivations;

        private void OnValidate()
        {
            #if UNITY_EDITOR
            // we need to delay this as we can't poke other objects during OnValidate
            UnityEditor.EditorApplication.delayCall += () => StylingSystem.MarkDirty(this);
            #endif
        }

        private void OnEnable()
        {
            StylingSystem.MarkDirty(this);
        }

        private void OnDisable()
        {
            StylingSystem.MarkDirty(this);
        }

        private void OnTransformParentChanged()
        {
            StylingSystem.MarkDirty(this);
        }

        private void OnDidApplyAnimationProperties()
        {
            StylingSystem.MarkDirty(this);
        }

        public void GetStyleActivations(List<StyleActivation> activations)
        {
            activations.Clear();
            activations.AddRange(_styleActivations);
        }

        public bool IsActive(string styleName)
        {
            return TryIndexOf(styleName, out var index) && _styleActivations[index].Active;
        }

        /// <summary>Adds or updates the activation for <paramref name="styleName"/>. Only marks dirty when the effective state changes.</summary>
        public void SetActive(string styleName, bool active)
        {
            if (string.IsNullOrWhiteSpace(styleName))
                throw new ArgumentException("Style name cannot be null, empty, or whitespace.", nameof(styleName));

            if (TryIndexOf(styleName, out var index))
            {
                if (_styleActivations[index].Active == active)
                    return;
                _styleActivations[index] = new StyleActivation(styleName, active);
            }
            else
            {
                _styleActivations.Add(new StyleActivation(styleName, active));
            }

            StylingSystem.MarkDirty(this);
        }

        /// <summary>Removes the entry so ancestor groups decide the style's state again.</summary>
        public bool Remove(string styleName)
        {
            if (!TryIndexOf(styleName, out var index))
                return false;

            _styleActivations.RemoveAt(index);
            StylingSystem.MarkDirty(this);
            return true;
        }

        private bool TryIndexOf(string styleName, out int index)
        {
            for (index = 0; index < _styleActivations.Count; index++)
                if (_styleActivations[index].Name == styleName)
                    return true;

            index = -1;
            return false;
        }
    }
}
