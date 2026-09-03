using System.Collections.Generic;
using NUnit.Framework;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Styling;
using UnityEngine;

namespace TimboJimboTests.Styling
{
    public sealed class StyleSheetBehaviourTests
    {
        private GameObject _root;
        private GameObject _child;
        private StyleGroup _group;
        private StyleSheet _sheet;
        private BindableProperty _scale;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Root");
            _group = _root.AddComponent<StyleGroup>();
            _child = new GameObject("Child");
            _child.transform.SetParent(_root.transform);
            _child.transform.localScale = Vector3.one;
            _sheet = _child.AddComponent<StyleSheet>();
            _scale = BindableProperty.Create(_child.transform, TransformProperties.LocalScale);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        [Test]
        public void Activate_WritesStyleValue_DeactivateRestoresBaseline()
        {
            AddScaleStyle("Hovered", 2f);

            _group.SetActive("Hovered", true);
            AssertScale(2f);

            _group.SetActive("Hovered", false);
            AssertScale(1f);
        }

        [Test]
        public void LaterStyleInSheetOrderWins()
        {
            AddScaleStyle("A", 2f);
            AddScaleStyle("B", 3f);

            _group.SetActive("A", true);
            _group.SetActive("B", true);
            AssertScale(3f);

            _group.SetActive("B", false);
            AssertScale(2f);
        }

        [Test]
        public void CloserGroupOverridesAncestor_RemoveDefersBackToAncestor()
        {
            AddScaleStyle("Hovered", 2f);
            var childGroup = _child.AddComponent<StyleGroup>();
            _group.SetActive("Hovered", true);
            AssertScale(2f);

            childGroup.SetActive("Hovered", false);
            AssertScale(1f);

            childGroup.Remove("Hovered");
            AssertScale(2f);
        }

        [Test]
        public void OverrideScope_ForcesActivations_DisposeRestoresGroupState()
        {
            AddScaleStyle("Hovered", 2f);

            using (StylingSystem.StylingOverrideScope(_root, new[] { "Hovered" }))
                AssertScale(2f);

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
            Assert.That(_child.transform.localScale, Is.EqualTo(Vector3.one * expected));
        }
    }
}
