using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace TimboJimbo.Styling
{
    public class StylingOverrideScope : IDisposable
    {
        public GameObject Root => _root;

        private GameObject _root;
        private List<StyleActivation> _styleActivations;
        private bool _isDisposed;

        public StylingOverrideScope(GameObject root, List<StyleActivation> styleActivations)
        {
            _root = root;
            ListPool<StyleActivation>.Get(out _styleActivations);
            _styleActivations.AddRange(styleActivations);
        }

        public void GetStyleActivations(List<StyleActivation> result)
        {
            result.Clear();
            result.AddRange(_styleActivations);
        }

        public void Dispose()
        {
            if(_isDisposed) return;
            _isDisposed = true;

            StylingSystem.RemoveStylingOverride(this);
            ListPool<StyleActivation>.Release(_styleActivations);

            _styleActivations = null;
            _root = null;
        }
    }
}