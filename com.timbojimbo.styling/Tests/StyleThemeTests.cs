using System.Collections.Generic;
using NUnit.Framework;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Styling;
using UnityEngine;

namespace TimboJimboTests.Styling
{
    public sealed class StyleThemeTests
    {
        private GameObject _root;
        private GameObject _child;
        private StyleSheet _sheet;
        private StyleThemeSource _source;
        private StyleTheme _theme;
        private BindableProperty _scale;
        private readonly List<Object> _assets = new();

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Root");
            _source = _root.AddComponent<StyleThemeSource>();
            _child = new GameObject("Child");
            _child.transform.SetParent(_root.transform);
            _child.transform.localScale = Vector3.one;
            _sheet = _child.AddComponent<StyleSheet>();
            _scale = BindableProperty.Create(_child.transform, TransformProperties.LocalScale);
            _theme = CreateTheme(("Big", ValueContainer.From(Vector3.one * 5f)));
            _source.Theme = _theme;

            _sheet.CreateStyle("Themed",
                new List<BindableProperty> { _scale },
                new List<BindablePropertyToValue> { new() { Property = _scale, Value = ValueContainer.From(Vector3.one) } },
                new List<BindablePropertyToValue> { new() { Property = _scale, Value = ValueContainer.From(Vector3.one * 2f) } });
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            foreach (var asset in _assets) Object.DestroyImmediate(asset);
        }

        [Test]
        public void LinkedCell_ResolvesFromNearestSource_UnlinkRestoresLiteral()
        {
            using var scope = StylingSystem.StylingOverrideScope(_child, new[] { "Themed" });
            AssertScale(2f);

            _sheet.SetThemeKey("Themed", _scale, "Big");
            AssertScale(5f);

            _sheet.SetThemeKey("Themed", _scale, null);
            AssertScale(2f);
        }

        [Test]
        public void NearerSource_OverridesFarther_AndThemeSwapRestyles()
        {
            using var scope = StylingSystem.StylingOverrideScope(_child, new[] { "Themed" });
            _sheet.SetThemeKey("Themed", _scale, "Big");

            var nearer = _child.AddComponent<StyleThemeSource>();
            nearer.Theme = CreateTheme(("Big", ValueContainer.From(Vector3.one * 7f)));
            AssertScale(7f);

            nearer.Theme = CreateTheme(("Big", ValueContainer.From(Vector3.one * 9f)));
            AssertScale(9f);
        }

        [Test]
        public void MissingKeyOrKindMismatch_FallsBackToLiteral()
        {
            using var scope = StylingSystem.StylingOverrideScope(_child, new[] { "Themed" });

            _sheet.SetThemeKey("Themed", _scale, "DoesNotExist");
            AssertScale(2f);

            _theme.Set("Big", ValueContainer.From(Color.red));
            _sheet.SetThemeKey("Themed", _scale, "Big");
            AssertScale(2f);
        }

        [Test]
        public void EditingLiteral_KeepsLink()
        {
            _sheet.SetThemeKey("Themed", _scale, "Big");
            _sheet.SetStyleValue("Themed", _scale, ValueContainer.From(Vector3.one * 3f), ValueContainer.From(Vector3.one));

            Assert.That(_sheet.GetThemeKey("Themed", _scale), Is.EqualTo("Big"));
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
