using NUnit.Framework;
using System.Collections.Generic;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Styling;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TimboJimboTests.Styling
{
    public sealed class InteractionStyleStateTests
    {
        private GameObject _root;
        private InteractionStyleState _state;
        private Selectable _selectable;
        private StyleSheet _sheet;
        private BindableProperty _scale;
        private PointerEventData _pointer;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Button");
            _root.transform.localScale = Vector3.one;
            _selectable = _root.AddComponent<Selectable>();
            _state = _root.AddComponent<InteractionStyleState>();
            _sheet = _root.AddComponent<StyleSheet>();
            _scale = BindableProperty.Create(_root.transform, TransformProperties.LocalScale);
            AddScaleStyle(InteractionStyleState.HoveredStyle, 2f);
            AddScaleStyle(InteractionStyleState.PressedStyle, 3f);
            AddScaleStyle(InteractionStyleState.DisabledStyle, 0.5f);
            _pointer = new PointerEventData(null);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        [Test]
        public void HoverAndPress_LayerInSheetOrder_ReleaseAndExitRestore()
        {
            _state.OnPointerEnter(_pointer);
            AssertScale(2f);

            _state.OnPointerDown(_pointer);
            AssertScale(3f);

            _state.OnPointerUp(_pointer);
            AssertScale(2f);

            _state.OnPointerExit(_pointer);
            AssertScale(1f);
        }

        [Test]
        public void DragOutWhilePressed_ClearsPressed()
        {
            _state.OnPointerEnter(_pointer);
            _state.OnPointerDown(_pointer);
            _state.OnPointerExit(_pointer);

            Assert.That(_state.IsPressed, Is.False);
            AssertScale(1f);
        }

        [Test]
        public void Cancel_ClearsPressedButKeepsHover()
        {
            _state.OnPointerEnter(_pointer);
            _state.OnPointerDown(_pointer);
            _state.OnCancel(new BaseEventData(null));

            Assert.That(_state.IsPressed, Is.False);
            AssertScale(2f);
        }

        [Test]
        public void NotInteractable_SuppressesHoverAndPress_AndActivatesDisabled()
        {
            _state.OnPointerEnter(_pointer);
            _selectable.interactable = false;
            StylingSystem.MarkDirty(_state);

            AssertScale(0.5f);
            _state.OnPointerDown(_pointer);
            Assert.That(_state.IsPressed, Is.False);
            AssertScale(0.5f);
        }

        [Test]
        public void ParentCanvasGroupNotInteractable_ActivatesDisabled_IgnoreParentGroupsRestores()
        {
            var parent = new GameObject("Parent");
            try
            {
                var parentGroup = parent.AddComponent<CanvasGroup>();
                _root.transform.SetParent(parent.transform);

                parentGroup.interactable = false;
                StylingSystem.MarkDirty(_state);
                Assert.That(_state.IsInteractable, Is.False);
                AssertScale(0.5f);

                var ownGroup = _root.AddComponent<CanvasGroup>();
                ownGroup.ignoreParentGroups = true;
                StylingSystem.MarkDirty(_state);
                Assert.That(_state.IsInteractable, Is.True);
                AssertScale(1f);
            }
            finally
            {
                _root.transform.SetParent(null);
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void DisableComponent_ClearsAllStates()
        {
            _state.OnPointerEnter(_pointer);
            _state.OnPointerDown(_pointer);

            _state.enabled = false;

            Assert.That(_state.IsHovered, Is.False);
            Assert.That(_state.IsPressed, Is.False);
            AssertScale(1f);
        }

        private void AddScaleStyle(string name, float scale)
        {
            _sheet.CreateStyle(name,
                new List<BindableProperty> { _scale },
                new List<BindablePropertyToValue> { new() { Property = _scale, Value = ValueContainer.From(Vector3.one) } },
                new List<BindablePropertyToValue> { new() { Property = _scale, Value = ValueContainer.From(Vector3.one * scale) } });
        }

        private void AssertScale(float expected)
        {
            Assert.That(_root.transform.localScale, Is.EqualTo(Vector3.one * expected));
        }
    }
}
