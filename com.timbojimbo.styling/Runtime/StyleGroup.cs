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

        public void GetStyleActivations(List<StyleActivation> activations)
        {
            activations.Clear();
            activations.AddRange(_styleActivations);
        }
    }
}
