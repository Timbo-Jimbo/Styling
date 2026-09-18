using System.Collections.Generic;
using NUnit.Framework;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Styling;
using UnityEngine;

namespace TimboJimboTests.Styling
{
    public sealed class StyledPropertyBehaviourTests
    {
        private GameObject _root;
        private GameObject _child;
        private StyleGroup _group;
        private StyledProperty _styled;
        private BindableProperty _scale;
        private readonly List<Object> _assets = new();

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Root");
            _group = _root.AddComponent<StyleGroup>();
            _child = new GameObject("Child");
            _child.transform.SetParent(_root.transform);
            _child.transform.localScale = Vector3.one;
            _styled = _child.AddComponent<StyledProperty>();
            _scale = BindableProperty.Create(_child.transform, TransformProperties.LocalScale);
            _styled.SetProperty(_scale);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            foreach (var asset in _assets) Object.DestroyImmediate(asset);
            _assets.Clear();
        }

        [Test]
        public void SetProperty_SeedsBaselineFromLiveValue()
        {
            Assert.That(_styled.Baseline.Vector3Value, Is.EqualTo(Vector3.one));
            Assert.That(_styled.StyleValues, Is.Empty);
        }

        [Test]
        public void Baseline_IsAppliedOnEnable()
        {
            _styled.Baseline = ValueContainer.From(Vector3.one * 2f);
            AssertScale(2f);

            _styled.enabled = false;
            _child.transform.localScale = Vector3.one * 9f;
            _styled.enabled = true;

            AssertScale(2f);
        }

        [Test]
        public void Activate_WritesStyleValue_DeactivateRestoresBaseline()
        {
            SetStyleScale("Hovered", 2f);

            _group.SetActive("Hovered", true);
            AssertScale(2f);

            _group.SetActive("Hovered", false);
            AssertScale(1f);
        }

        [Test]
        public void LaterStyleValueInListOrderWins()
        {
            SetStyleScale("A", 2f);
            SetStyleScale("B", 3f);

            _group.SetActive("A", true);
            _group.SetActive("B", true);
            AssertScale(3f);

            _group.SetActive("B", false);
            AssertScale(2f);
        }

        [Test]
        public void CloserGroupOverridesAncestor_RemoveDefersBackToAncestor()
        {
            SetStyleScale("Hovered", 2f);
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
            SetStyleScale("Hovered", 2f);

            using (StylingSystem.StylingOverrideScope(_root, new[] { "Hovered" }))
                AssertScale(2f);

            AssertScale(1f);
        }

        [Test]
        public void RemoveStyleValue_FallsBackToBaseline()
        {
            SetStyleScale("Hovered", 2f);
            _group.SetActive("Hovered", true);
            AssertScale(2f);

            Assert.That(_styled.RemoveStyleValue("Hovered"), Is.True);
            AssertScale(1f);
            Assert.That(_styled.RemoveStyleValue("Hovered"), Is.False);
        }

        [Test]
        public void LinkedStyleValue_ResolvesFromTheme_UnlinkRestoresLiteral()
        {
            AddThemeSource(("Big", ValueContainer.From(Vector3.one * 5f)));
            SetStyleScale("Themed", 2f);

            using var scope = StylingSystem.StylingOverrideScope(_child, new[] { "Themed" });
            AssertScale(2f);

            _styled.SetStyleThemeKey("Themed", "Big");
            AssertScale(5f);

            _styled.SetStyleThemeKey("Themed", null);
            AssertScale(2f);
        }

        [Test]
        public void LinkedBaseline_ResolvesFromTheme()
        {
            AddThemeSource(("Big", ValueContainer.From(Vector3.one * 5f)));

            _styled.BaselineThemeKey = "Big";
            AssertScale(5f);

            _styled.BaselineThemeKey = null;
            AssertScale(1f);
        }

        [Test]
        public void MissingKeyOrKindMismatch_FallsBackToLiteral()
        {
            var theme = AddThemeSource(("Big", ValueContainer.From(Vector3.one * 5f)));
            SetStyleScale("Themed", 2f);

            using var scope = StylingSystem.StylingOverrideScope(_child, new[] { "Themed" });

            _styled.SetStyleThemeKey("Themed", "DoesNotExist");
            AssertScale(2f);

            theme.Set("Big", ValueContainer.From(Color.red));
            _styled.SetStyleThemeKey("Themed", "Big");
            AssertScale(2f);
        }

        [Test]
        public void ThemeSwap_ReStyles()
        {
            var source = _root.AddComponent<StyleThemeSource>();
            source.Theme = CreateTheme(("Big", ValueContainer.From(Vector3.one * 5f)));

            _styled.BaselineThemeKey = "Big";
            AssertScale(5f);

            source.Theme = CreateTheme(("Big", ValueContainer.From(Vector3.one * 9f)));
            AssertScale(9f);
        }

        [Test]
        public void PropertyWithNoTarget_DoesNothingAndDoesNotThrow()
        {
            var bare = new GameObject("Bare");
            bare.transform.SetParent(_root.transform);
            bare.transform.localScale = Vector3.one;
            var styled = bare.AddComponent<StyledProperty>();

            Assert.DoesNotThrow(() =>
            {
                styled.SetProperty(BindableProperty.Invalid);
                styled.SetStyleValue("Hovered", ValueContainer.From(Vector3.one * 2f));
                styled.Baseline = ValueContainer.From(Vector3.one * 3f);
                _group.SetActive("Hovered", true);
                styled.CompleteTransitionImmediate();
            });

            Assert.That(bare.transform.localScale, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void StyleNames_AreReportedAsSupported()
        {
            SetStyleScale("Hovered", 2f);

            var names = new List<string>();

            StylingSystem.GetSupportedStyleNames(_child, names);
            Assert.That(names, Contains.Item("Hovered"));

            StylingSystem.GetSupportedStyleNames(_root, names, includeChildren: true);
            Assert.That(names, Contains.Item("Hovered"));

            StylingSystem.GetSupportedStyleNames(_root, names);
            Assert.That(names, Does.Not.Contain("Hovered"));
        }

        private void SetStyleScale(string styleName, float scale)
        {
            _styled.SetStyleValue(styleName, ValueContainer.From(Vector3.one * scale));
        }

        private StyleTheme AddThemeSource(params (string key, ValueContainer value)[] entries)
        {
            var theme = CreateTheme(entries);
            var source = _root.AddComponent<StyleThemeSource>();
            source.Theme = theme;
            return theme;
        }

        private StyleTheme CreateTheme(params (string key, ValueContainer value)[] entries)
        {
            var theme = ScriptableObject.CreateInstance<StyleTheme>();
            foreach (var (key, value) in entries) theme.Set(key, value);
            _assets.Add(theme);
            return theme;
        }

        private void AssertScale(float expected)
        {
            Assert.That(_child.transform.localScale, Is.EqualTo(Vector3.one * expected));
        }
    }
}
