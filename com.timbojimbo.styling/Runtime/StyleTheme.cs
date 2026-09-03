using System;
using System.Collections.Generic;
using TimboJimbo.PropertyBindings;
using UnityEngine;

namespace TimboJimbo.Styling
{
    /// <summary>
    /// A flat, named table of typed values. Style sheet cells can link to a key instead of
    /// holding a literal; the nearest <see cref="StyleThemeSource"/> up the hierarchy decides
    /// which theme resolves those keys.
    /// </summary>
    [CreateAssetMenu(menuName = "Timbo Jimbo/Styling/Style Theme", fileName = "StyleTheme")]
    public sealed class StyleTheme : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string Key;
            public ValueContainer Value;
        }

        [SerializeField] private List<Entry> _entries = new();

        public IReadOnlyList<Entry> Entries => _entries;

        public bool TryGetValue(string key, out ValueContainer value)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Key == key)
                {
                    value = _entries[i].Value;
                    return true;
                }
            }
            value = default;
            return false;
        }

        /// <summary>Adds or replaces <paramref name="key"/>. Callers are responsible for re-styling dependents.</summary>
        public void Set(string key, ValueContainer value)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Theme key cannot be null, empty, or whitespace.", nameof(key));

            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Key != key) continue;
                _entries[i] = new Entry { Key = key, Value = value };
                return;
            }
            _entries.Add(new Entry { Key = key, Value = value });
        }

        public bool Remove(string key)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Key != key) continue;
                _entries.RemoveAt(i);
                return true;
            }
            return false;
        }
    }
}
