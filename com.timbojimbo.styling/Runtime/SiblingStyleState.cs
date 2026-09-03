using System.Collections.Generic;
using UnityEngine;

namespace TimboJimbo.Styling
{
    /// <summary>
    /// Activation source driven by this object's position among its siblings. Emits
    /// <c>First</c>, <c>Last</c>, <c>Odd</c>, <c>Even</c> (1-based, like CSS) and optionally
    /// <c>Nth</c> for positions matching <c>stride * k + offset</c>. Style names are configurable.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Timbo Jimbo/Styling/Sibling Style State")]
    public sealed class SiblingStyleState : MonoBehaviour, IStyleActivationSource
    {
        public const string FirstStyle = "First";
        public const string LastStyle = "Last";
        public const string OddStyle = "Odd";
        public const string EvenStyle = "Even";
        public const string NthStyle = "Nth";

        [SerializeField] private string _firstStyle = FirstStyle;
        [SerializeField] private string _lastStyle = LastStyle;
        [SerializeField] private string _oddStyle = OddStyle;
        [SerializeField] private string _evenStyle = EvenStyle;

        [Header("Nth (0 stride disables)")]
        [SerializeField] private string _nthStyle = NthStyle;
        [SerializeField, Min(0)] private int _nthStride;
        [SerializeField, Min(0)] private int _nthOffset;

        [Tooltip("Only count siblings that are active in the hierarchy.")]
        [SerializeField] private bool _activeSiblingsOnly = true;

        private int _index = -1;
        private int _count = -1;

        /// <summary>1-based position among counted siblings, or 0 when this object is not counted.</summary>
        public int Position => _index + 1;
        public int SiblingCount => _count;

        private void OnEnable() => RefreshSiblings();
        private void OnDisable() => RefreshSiblings();
        private void OnTransformParentChanged() => RefreshSiblings();
        // Unity has no sibling-reorder callback; poll. One O(siblings) scan per frame, no allocation.
        private void Update() => Refresh();

        /// <summary>Re-evaluates sibling position; marks dirty only when it changed.</summary>
        public void Refresh()
        {
            ComputePosition(out var index, out var count);
            if (index == _index && count == _count)
                return;

            _index = index;
            _count = count;
            StylingSystem.MarkDirty(this);
        }

        /// <summary>Joining or leaving a parent changes every sibling's count, so refresh them too.</summary>
        private void RefreshSiblings()
        {
            Refresh();
            var parent = transform.parent;
            if (parent == null) return;

            using (UnityEngine.Pool.ListPool<SiblingStyleState>.Get(out var siblings))
            {
                for (int i = 0; i < parent.childCount; i++)
                {
                    parent.GetChild(i).GetComponents(siblings);
                    for (int s = 0; s < siblings.Count; s++)
                        if (siblings[s] != this) siblings[s].Refresh();
                }
            }
        }

        private void ComputePosition(out int index, out int count)
        {
            var parent = transform.parent;
            index = -1;
            count = 0;

            if (parent == null)
            {
                if (!_activeSiblingsOnly || gameObject.activeInHierarchy) { index = 0; count = 1; }
                return;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (_activeSiblingsOnly && !child.gameObject.activeInHierarchy)
                    continue;
                if (child == transform)
                    index = count;
                count++;
            }
        }

        public void GetStyleActivations(List<StyleActivation> activations)
        {
            activations.Clear();
            int position = Position;
            bool counted = position > 0;

            activations.Add(new StyleActivation(_firstStyle, counted && position == 1));
            activations.Add(new StyleActivation(_lastStyle, counted && position == _count));
            activations.Add(new StyleActivation(_oddStyle, counted && (position & 1) == 1));
            activations.Add(new StyleActivation(_evenStyle, counted && (position & 1) == 0));

            if (_nthStride > 0)
                activations.Add(new StyleActivation(_nthStyle, counted && position >= _nthOffset && (position - _nthOffset) % _nthStride == 0));
        }
    }
}
