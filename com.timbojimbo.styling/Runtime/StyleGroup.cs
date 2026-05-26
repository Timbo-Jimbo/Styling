 using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace TimboJimbo.Styling
{
    public sealed class StyleGroup : MonoBehaviour, IStyleActivationSource
    {
        [SerializeField] private List<StyleActivation> _styleActivations = new List<StyleActivation>();
        [Tooltip("When enabled, at most one entry can be Active at a time. Activating an entry deactivates all others on this group.")]
        [SerializeField] private bool _isToggleGroup;

        public IReadOnlyList<StyleActivation> StyleActivations => _styleActivations;

        /// <summary>When true, this group behaves like a radio: activating one entry deactivates the others.</summary>
        public bool IsToggleGroup
        {
            get => _isToggleGroup;
            set
            {
                if (_isToggleGroup == value) return;
                _isToggleGroup = value;

                if(_isToggleGroup && EnforceToggleExclusivity())
                    StylingSystem.MarkDirty(this);
            }
        }

        /// <summary>
        /// Ensures only the entry at <paramref name="keepIndex"/> remains Active.
        /// If <paramref name="keepIndex"/> is -1, keeps the first Active entry found.
        /// Returns true if any entry was changed.
        /// </summary>
        private bool EnforceToggleExclusivity()
        {
            var foundActive = false;
            var changed = false;
            for (var i = 0; i < _styleActivations.Count; i++)
            {
                if (_styleActivations[i].Active)
                {
                    if (!foundActive)
                    {
                        foundActive = true;
                    }
                    else
                    {
                        var entry = _styleActivations[i];
                        entry.Active = false;
                        _styleActivations[i] = entry;
                        changed = true;
                    }
                }
            }

            return changed;
        }

        private void OnValidate()
        {
            // Inspector edits / animation playback go through serialization; surface them as a Changed.
            if (_isToggleGroup) EnforceToggleExclusivity();

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

        public void GetStyleActivations(List<StyleActivation> activations)
        {
            activations.Clear();
            activations.AddRange(_styleActivations);
        }
    }
}
