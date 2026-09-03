using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Styling;
using UnityEngine;
using UnityEngine.TestTools;

namespace TimboJimboTests.Styling.PlayMode
{
    public sealed class StyleSheetTransitionPlayModeTests
    {
        private GameObject _root;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(_root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Transition_InterpolatesOverFramesAndSettlesOnTarget()
        {
            _root = new GameObject("Root");
            var group = _root.AddComponent<StyleGroup>();
            var sheet = _root.AddComponent<StyleSheet>();
            var scale = BindableProperty.Create(_root.transform, TransformProperties.LocalScale);
            sheet.CreateStyle("Big",
                new List<BindableProperty> { scale },
                new List<BindablePropertyToValue> { new() { Property = scale, Value = ValueContainer.From(Vector3.one) } },
                new List<BindablePropertyToValue> { new() { Property = scale, Value = ValueContainer.From(Vector3.one * 3f) } });
            sheet.SetTransition(scale, new StylePropertyTransition { Duration = 0.25f });
            yield return null;

            group.SetActive("Big", true);
            yield return null;
            yield return null;

            Assert.That(sheet.IsTransitioning, Is.True);
            var mid = _root.transform.localScale.x;
            Assert.That(mid, Is.GreaterThan(1f).And.LessThan(3f));

            float elapsed = 0f;
            while (sheet.IsTransitioning && elapsed < 2f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.That(sheet.IsTransitioning, Is.False);
            Assert.That(_root.transform.localScale, Is.EqualTo(Vector3.one * 3f));
        }
    }
}
