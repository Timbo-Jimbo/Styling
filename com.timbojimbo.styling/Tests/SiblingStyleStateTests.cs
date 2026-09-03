using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TimboJimbo.Styling;
using UnityEngine;

namespace TimboJimboTests.Styling
{
    public sealed class SiblingStyleStateTests
    {
        private GameObject _parent;
        private List<SiblingStyleState> _children;

        [SetUp]
        public void SetUp()
        {
            _parent = new GameObject("Parent");
            _children = new List<SiblingStyleState>();
            for (int i = 0; i < 4; i++)
            {
                var child = new GameObject($"Child {i}");
                child.transform.SetParent(_parent.transform);
                _children.Add(child.AddComponent<SiblingStyleState>());
            }
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_parent);

        [Test]
        public void EmitsFirstLastOddEvenByOneBasedPosition()
        {
            Assert.That(Active(_children[0]), Is.EquivalentTo(new[] { "First", "Odd" }));
            Assert.That(Active(_children[1]), Is.EquivalentTo(new[] { "Even" }));
            Assert.That(Active(_children[2]), Is.EquivalentTo(new[] { "Odd" }));
            Assert.That(Active(_children[3]), Is.EquivalentTo(new[] { "Last", "Even" }));
        }

        [Test]
        public void Reorder_UpdatesPositionAfterRefresh()
        {
            _children[3].transform.SetAsFirstSibling();
            foreach (var c in _children) c.Refresh();

            Assert.That(Active(_children[3]), Is.EquivalentTo(new[] { "First", "Odd" }));
            Assert.That(Active(_children[0]), Is.EquivalentTo(new[] { "Even" }));
            Assert.That(Active(_children[2]), Is.EquivalentTo(new[] { "Last", "Even" }));
        }

        [Test]
        public void InactiveSiblingsAreNotCountedByDefault_AndSiblingsRenumberImmediately()
        {
            _children[0].gameObject.SetActive(false);

            Assert.That(_children[0].Position, Is.Zero);
            Assert.That(Active(_children[0]), Is.Empty);
            Assert.That(Active(_children[1]), Is.EquivalentTo(new[] { "First", "Odd" }));
            Assert.That(_children[3].SiblingCount, Is.EqualTo(3));
        }

        [Test]
        public void SingleChild_IsBothFirstAndLast()
        {
            for (int i = 1; i < 4; i++) Object.DestroyImmediate(_children[i].gameObject);
            _children[0].Refresh();

            Assert.That(Active(_children[0]), Is.EquivalentTo(new[] { "First", "Last", "Odd" }));
        }

        private static string[] Active(SiblingStyleState state)
        {
            var list = new List<StyleActivation>();
            state.GetStyleActivations(list);
            return list.Where(a => a.Active).Select(a => a.Name).ToArray();
        }
    }
}
