using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TimboJimbo.Styling
{
    /// <summary>
    /// Built-in activation source for pointer/selection interaction. Emits independent
    /// <c>Hovered</c>, <c>Pressed</c>, <c>Selected</c> and <c>Disabled</c> activations so sheets can
    /// layer them. While disabled, the interactive states are reported inactive.
    /// Disabled follows <see cref="Selectable.IsInteractable"/> semantics: the sibling
    /// <see cref="Selectable.interactable"/> when present, and any parent <see cref="CanvasGroup"/>
    /// with <c>interactable = false</c> (stopping at <c>ignoreParentGroups</c>).
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Timbo Jimbo/Styling/Interaction Style State")]
    public sealed class InteractionStyleState : MonoBehaviour, IStyleActivationSource,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
        ISelectHandler, IDeselectHandler, ICancelHandler
    {
        public const string HoveredStyle = "Hovered";
        public const string PressedStyle = "Pressed";
        public const string SelectedStyle = "Selected";
        public const string DisabledStyle = "Disabled";

        [SerializeField] private string _hoveredStyle = HoveredStyle;
        [SerializeField] private string _pressedStyle = PressedStyle;
        [SerializeField] private string _selectedStyle = SelectedStyle;
        [SerializeField] private string _disabledStyle = DisabledStyle;

        private bool _hovered;
        private bool _pressed;
        private bool _selected;
        private bool _lastInteractable = true;
        private Selectable _selectable;
        private bool _selectableResolved;

        public bool IsHovered => _hovered;
        public bool IsPressed => _pressed;
        public bool IsSelected => _selected;
        public bool IsInteractable
        {
            get
            {
                if (!_selectableResolved)
                {
                    _selectable = GetComponent<Selectable>();
                    _selectableResolved = true;
                }
                // Selectable.IsInteractable() depends on a CanvasGroup cache that only fills at runtime; combine manually.
                return (_selectable == null || _selectable.interactable) && CanvasGroupsAllowInteraction();
            }
        }

        private bool CanvasGroupsAllowInteraction()
        {
            using (UnityEngine.Pool.ListPool<CanvasGroup>.Get(out var groups))
            {
                for (var t = transform; t != null; t = t.parent)
                {
                    t.GetComponents(groups);
                    for (int i = 0; i < groups.Count; i++)
                    {
                        var group = groups[i];
                        if (!group.enabled) continue;
                        if (!group.interactable) return false;
                        if (group.ignoreParentGroups) return true;
                    }
                }
            }
            return true;
        }

        private void OnEnable()
        {
            _lastInteractable = IsInteractable;
            StylingSystem.MarkDirty(this);
        }

        private void OnDisable()
        {
            _hovered = false;
            _pressed = false;
            _selected = false;
            StylingSystem.MarkDirty(this);
        }

        private void Update()
        {
            // No change callback exists for Selectable.interactable or parent CanvasGroups; poll and clear transient states on disable.
            bool interactable = IsInteractable;
            if (interactable == _lastInteractable)
                return;

            _lastInteractable = interactable;
            if (!interactable)
            {
                _hovered = false;
                _pressed = false;
            }
            StylingSystem.MarkDirty(this);
        }

        public void OnPointerEnter(PointerEventData eventData) => Set(ref _hovered, true);
        public void OnPointerExit(PointerEventData eventData)
        {
            bool changed = _hovered || _pressed;
            _hovered = false;
            _pressed = false;
            if (changed) StylingSystem.MarkDirty(this);
        }
        public void OnPointerDown(PointerEventData eventData) => Set(ref _pressed, IsInteractable);
        public void OnPointerUp(PointerEventData eventData) => Set(ref _pressed, false);
        public void OnSelect(BaseEventData eventData) => Set(ref _selected, true);
        public void OnDeselect(BaseEventData eventData) => Set(ref _selected, false);
        public void OnCancel(BaseEventData eventData) => Set(ref _pressed, false);

        public void GetStyleActivations(List<StyleActivation> activations)
        {
            activations.Clear();
            bool interactable = IsInteractable;
            activations.Add(new StyleActivation(_hoveredStyle, interactable && _hovered));
            activations.Add(new StyleActivation(_pressedStyle, interactable && _pressed));
            activations.Add(new StyleActivation(_selectedStyle, _selected));
            activations.Add(new StyleActivation(_disabledStyle, !interactable));
        }

        private void Set(ref bool field, bool value)
        {
            if (field == value) return;
            field = value;
            StylingSystem.MarkDirty(this);
        }
    }
}
