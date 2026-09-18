using System.Collections;
using NUnit.Framework;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Styling;
using UnityEngine;
using UnityEngine.TestTools;

namespace TimboJimboTests.Styling.PlayMode
{
    public sealed class StyledPropertyTransitionPlayModeTests
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
            var styled = _root.AddComponent<StyledProperty>();
            var scale = BindableProperty.Create(_root.transform, TransformProperties.LocalScale);

            styled.SetProperty(scale);
            styled.SetStyleValue("Big", ValueContainer.From(Vector3.one * 3f));
            styled.Transition = new StylePropertyTransition { Duration = 0.25f };
            yield return null;

            group.SetActive("Big", true);
            yield return null;
            yield return null;

            Assert.That(styled.IsTransitioning, Is.True);
            var mid = _root.transform.localScale.x;
            Assert.That(mid, Is.GreaterThan(1f).And.LessThan(3f));

            float elapsed = 0f;
            while (styled.IsTransitioning && elapsed < 2f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.That(styled.IsTransitioning, Is.False);
            Assert.That(_root.transform.localScale, Is.EqualTo(Vector3.one * 3f));
        }

        [UnityTest]
        public IEnumerator ZeroDuration_AppliesInstantly()
        {
            _root = new GameObject("Root");
            var group = _root.AddComponent<StyleGroup>();
            var styled = _root.AddComponent<StyledProperty>();
            var scale = BindableProperty.Create(_root.transform, TransformProperties.LocalScale);

            styled.SetProperty(scale);
            styled.SetStyleValue("Big", ValueContainer.From(Vector3.one * 3f));
            styled.Transition = StylePropertyTransition.Instant;
            yield return null;

            group.SetActive("Big", true);
            yield return null;

            Assert.That(styled.IsTransitioning, Is.False);
            Assert.That(_root.transform.localScale, Is.EqualTo(Vector3.one * 3f));
        }
    }
}
