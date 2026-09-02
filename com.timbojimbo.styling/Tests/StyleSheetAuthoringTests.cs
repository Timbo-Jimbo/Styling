using System.Collections.Generic;
using NUnit.Framework;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Styling;
using TimboJimboEditor.Styling;
using UnityEditor;
using UnityEngine;

namespace TimboJimboTests.Styling
{
    [TestFixture]
    public sealed class StyleSheetAuthoringTests
    {
        private GameObject _root;
        private StyleSheet _sheet;
        private BindableProperty _scale;
        private BindableProperty _position;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("StyleSheetAuthoringTests");
            _sheet = _root.AddComponent<StyleSheet>();
            _scale = BindableProperty.Create(_root.transform, TransformProperties.LocalScale);
            _position = BindableProperty.Create(_root.transform, TransformProperties.LocalPosition);
            Undo.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void UpsertStyle_IsIdempotentAndRetainsUnspecifiedValues()
        {
            var baseline = new[]
            {
                Entry(_scale, ValueContainer.From(Vector3.one)),
                Entry(_position, ValueContainer.From(Vector3.zero))
            };

            _sheet.UpsertStyle("Hovered", new[]
            {
                Entry(_scale, ValueContainer.From(Vector3.one * 1.1f)),
                Entry(_position, ValueContainer.From(Vector3.right))
            }, baseline);
            _sheet.UpsertStyle("Hovered", new[]
            {
                Entry(_scale, ValueContainer.From(Vector3.one * 1.2f))
            }, baseline);

            Assert.AreEqual(1, _sheet.Styles.Count);
            Assert.AreEqual(2, _sheet.GetStyle("Hovered").PropertyValues.Count);
            Assert.AreEqual(2, _sheet.BaselineValues.Count);
            Assert.AreEqual(Vector3.one * 1.2f, Find(_sheet.GetStyle("Hovered").PropertyValues, _scale).Vector3Value);
            Assert.AreEqual(Vector3.right, Find(_sheet.GetStyle("Hovered").PropertyValues, _position).Vector3Value);
        }

        [Test]
        public void ReplaceStyle_RemovesUnspecifiedValues()
        {
            var baseline = new[]
            {
                Entry(_scale, ValueContainer.From(Vector3.one)),
                Entry(_position, ValueContainer.From(Vector3.zero))
            };
            _sheet.UpsertStyle("Hovered", new[]
            {
                Entry(_scale, ValueContainer.From(Vector3.one * 1.1f)),
                Entry(_position, ValueContainer.From(Vector3.right))
            }, baseline);

            _sheet.ReplaceStyle("Hovered", new[]
            {
                Entry(_scale, ValueContainer.From(Vector3.one * 1.3f))
            }, baseline);

            Assert.AreEqual(1, _sheet.GetStyle("Hovered").PropertyValues.Count);
            Assert.AreEqual(_scale, _sheet.GetStyle("Hovered").PropertyValues[0].Property);
        }

        [Test]
        public void UpsertStyle_MissingBaselineSeedThrowsWithoutMutation()
        {
            Assert.Throws<System.ArgumentException>(() => _sheet.UpsertStyle("Hovered",
                new[] { Entry(_scale, ValueContainer.From(Vector3.one * 1.1f)) },
                System.Array.Empty<BindablePropertyToValue>()));

            Assert.IsFalse(_sheet.HasStyle("Hovered"));
            Assert.IsEmpty(_sheet.BaselineValues);
        }

        [Test]
        public void ReplaceAllStyles_UsesExactOrderAndPrunesRemovedProperties()
        {
            _sheet.UpsertStyle("Old",
                new[] { Entry(_position, ValueContainer.From(Vector3.right)) },
                new[] { Entry(_position, ValueContainer.From(Vector3.zero)) });

            _sheet.ReplaceAllStyles(new[]
            {
                new Style
                {
                    Name = "Standard",
                    PropertyValues = new List<BindablePropertyToValue>
                    {
                        Entry(_scale, ValueContainer.From(Vector3.one))
                    }
                },
                new Style
                {
                    Name = "Hovered",
                    PropertyValues = new List<BindablePropertyToValue>
                    {
                        Entry(_scale, ValueContainer.From(Vector3.one * 1.1f))
                    }
                }
            }, new[] { Entry(_scale, ValueContainer.From(Vector3.one)) });

            CollectionAssert.AreEqual(new[] { "Standard", "Hovered" },
                new[] { _sheet.Styles[0].Name, _sheet.Styles[1].Name });
            Assert.AreEqual(1, _sheet.BaselineValues.Count);
            Assert.AreEqual(_scale, _sheet.BaselineValues[0].Property);
            Assert.AreEqual(1, _sheet.PropertyConfigs.Count);
        }

        [Test]
        public void ClearStyles_CanRetainOrClearPropertyState()
        {
            _sheet.UpsertStyle("Hovered",
                new[] { Entry(_scale, ValueContainer.From(Vector3.one * 1.1f)) },
                new[] { Entry(_scale, ValueContainer.From(Vector3.one)) });

            _sheet.ClearStyles();
            Assert.IsEmpty(_sheet.Styles);
            Assert.AreEqual(1, _sheet.BaselineValues.Count);
            Assert.AreEqual(1, _sheet.PropertyConfigs.Count);

            _sheet.ClearStyles(clearProperties: true);
            Assert.IsEmpty(_sheet.BaselineValues);
            Assert.IsEmpty(_sheet.PropertyConfigs);
        }

        [Test]
        public void Snapshot_IsDetachedFromSubsequentMutations()
        {
            _sheet.UpsertStyle("Hovered",
                new[] { Entry(_scale, ValueContainer.From(Vector3.one * 1.1f)) },
                new[] { Entry(_scale, ValueContainer.From(Vector3.one)) });
            var snapshot = _sheet.CreateSnapshot();

            _sheet.ReplaceStyle("Hovered",
                new[] { Entry(_scale, ValueContainer.From(Vector3.one * 2f)) },
                new[] { Entry(_scale, ValueContainer.From(Vector3.one)) });

            Assert.AreEqual(Vector3.one * 1.1f, snapshot.Styles[0].PropertyValues[0].Value.Vector3Value);
            Assert.AreEqual(Vector3.one * 2f, _sheet.Styles[0].PropertyValues[0].Value.Vector3Value);
            Assert.IsTrue(snapshot.Validation.IsValid);
        }

        [Test]
        public void Snapshot_CapturesCurrentActiveAndResolvedValuesWithoutApplyingThem()
        {
            _root.transform.localScale = Vector3.one;
            _sheet.UpsertStyle("Hovered",
                new[] { Entry(_scale, ValueContainer.From(Vector3.one * 2f)) },
                new[] { Entry(_scale, ValueContainer.From(Vector3.one)) });

            using var scope = StylingSystem.StylingOverrideScope(_root, new[] { "Hovered" });
            var scaleBeforeSnapshot = _root.transform.localScale;
            var snapshot = _sheet.CreateSnapshot();

            CollectionAssert.Contains(snapshot.ActiveStyleNames, "Hovered");
            Assert.AreEqual(Vector3.one * 2f, Find(snapshot.ResolvedValues, _scale).Vector3Value);
            Assert.AreEqual(scaleBeforeSnapshot, _root.transform.localScale);
        }

        [Test]
        public void EditSession_DisposeWithoutCommitLeavesSheetUnchanged()
        {
            using (var edit = StyleSheetEditSession.Begin(_sheet))
            {
                var scale = edit.Bind(_root.transform, TransformProperties.LocalScale);
                edit.UpsertStyle("Hovered").Set(scale, ValueContainer.From(Vector3.one * 1.2f));
            }

            Assert.IsFalse(_sheet.HasStyle("Hovered"));
            Assert.IsEmpty(_sheet.BaselineValues);
        }

        [Test]
        public void EditSession_CommitAppliesDefinitionAndOneUndoRestoresPriorState()
        {
            _root.transform.localScale = Vector3.one;
            using (var edit = StyleSheetEditSession.Begin(_sheet, "Configure Hovered Style"))
            {
                var scale = edit.Bind(_root.transform, TransformProperties.LocalScale);
                edit.UpsertStyle("Hovered").Set(scale, ValueContainer.From(Vector3.one * 1.25f));
                edit.SetTransition(scale, new StylePropertyTransition { Duration = 0.25f });
                Assert.IsTrue(edit.Validate().IsValid);
                edit.Commit();
            }

            Assert.IsTrue(_sheet.HasStyle("Hovered"));
            Assert.AreEqual(1, _sheet.BaselineValues.Count);
            Assert.AreEqual(0.25f, _sheet.GetTransition(_scale).Duration);

            Undo.PerformUndo();

            Assert.IsFalse(_sheet.HasStyle("Hovered"));
            Assert.IsEmpty(_sheet.BaselineValues);
            Assert.IsEmpty(_sheet.PropertyConfigs);
        }

        [Test]
        public void EditSession_RepeatedIdenticalCommitsDoNotDuplicateAuthoredState()
        {
            for (int i = 0; i < 2; i++)
            {
                using var edit = StyleSheetEditSession.Begin(_sheet);
                var scale = edit.Bind(_root.transform, TransformProperties.LocalScale);
                edit.UpsertStyle("Hovered").Set(scale, ValueContainer.From(Vector3.one * 1.25f));
                edit.Commit();
            }

            Assert.AreEqual(1, _sheet.Styles.Count);
            Assert.AreEqual(1, _sheet.Styles[0].PropertyValues.Count);
            Assert.AreEqual(1, _sheet.BaselineValues.Count);
            Assert.AreEqual(1, _sheet.PropertyConfigs.Count);
        }

        [Test]
        public void EditSession_RejectsValueKindMismatchBeforeMutation()
        {
            using var edit = StyleSheetEditSession.Begin(_sheet);
            var scale = edit.Bind(_root.transform, TransformProperties.LocalScale);

            Assert.Throws<System.ArgumentException>(() =>
                edit.UpsertStyle("Hovered").Set(scale, ValueContainer.From(1f)));
            Assert.IsFalse(_sheet.HasStyle("Hovered"));
        }

        private static BindablePropertyToValue Entry(BindableProperty property, ValueContainer value) =>
            new BindablePropertyToValue { Property = property, Value = value };

        private static ValueContainer Find(
            IReadOnlyList<BindablePropertyToValue> values,
            BindableProperty property)
        {
            for (int i = 0; i < values.Count; i++)
                if (values[i].Property.Equals(property)) return values[i].Value;
            Assert.Fail($"Property '{property.Path}' was not found.");
            return default;
        }
    }
}
