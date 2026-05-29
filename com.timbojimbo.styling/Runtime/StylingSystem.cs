using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Pool;

namespace TimboJimbo.Styling
{
    public static class StylingSystem
    {
        private static HashSet<GameObject> _dirtyRootQueue = new();
        private static List<StylingOverrideScope> _overrides = new();

        static StylingSystem()
        {
            var pl = PlayerLoop.GetCurrentPlayerLoop();

            var systemToInsert = new PlayerLoopSystem
            {
                type = typeof(StylingSystemUpdate),
                updateDelegate = Update
            };

            // We want to process style activation changes after all Update logic has run, but before any UGUI updates happen
            if (!InsertAfter<UnityEngine.PlayerLoop.PreLateUpdate>(ref pl, systemToInsert))
            {
                Debug.LogError("Failed to insert StylingSystem into player loop. Style activation changes will not be processed.");
            }
            else
            {
                PlayerLoop.SetPlayerLoop(pl);
            }
        }

        public static void MarkDirty<TComp>(TComp component) where TComp : Component
        {
            if (component == null) return;
            MarkDirty(component.gameObject);
        }

        public static void MarkDirty(GameObject root)
        {
            if (root == null) return;

            if(EditorAwareUtility.IsLiveInstance(root))
                _dirtyRootQueue.Add(root);
            else
                ProcessSingle(root);
        }

        public static StylingOverrideScope StylingOverrideScope(GameObject root, IEnumerable<string> activeStyleNames)
        {
            using (ListPool<StyleActivation>.Get(out var activations))
            {
                foreach (var styleName in activeStyleNames)
                {
                    if (string.IsNullOrWhiteSpace(styleName))
                        continue;

                    activations.Add(new StyleActivation(styleName, active: true));
                }

                return StylingOverrideScope(root, activations);
            }
        }

        public static StylingOverrideScope StylingOverrideScope(GameObject root, List<StyleActivation> activations)
        {
            var styleOverride = new StylingOverrideScope(root, activations);
            AddStylingOverride(styleOverride);
            return styleOverride;
        }

        public static void AddStylingOverride(StylingOverrideScope styleOverride)
        {
            _overrides.Add(styleOverride);
            MarkDirty(styleOverride.Root);
        }

        public static void RemoveStylingOverride(StylingOverrideScope styleOverride)
        {
            if(_overrides.Remove(styleOverride))
                MarkDirty(styleOverride.Root);
        }
        
        public static void GetSupportedStyleNames(GameObject root, List<string> result, bool includeChildren = false)
        {
            result.Clear();
            
            using (ListPool<StyleSheet>.Get(out var styleSheets))
            using (HashSetPool<string>.Get(out var seenStyles))
            {
                if(includeChildren)
                    root.GetComponentsInChildren(true, styleSheets);
                else
                    root.GetComponents(styleSheets);

                foreach (var sheet in styleSheets)
                {
                    foreach(var style in sheet.Styles)
                    {
                        if (seenStyles.Add(style.Name))
                            result.Add(style.Name);
                    }
                }
            }
        }

        public static void GetStyleActivations(GameObject root, List<StyleActivation> result)
        {
            result.Clear();

            if (TryGetFirstOverrideInParent(root, out var styleOverride))
            {
                styleOverride.GetStyleActivations(result);
                return;
            }

            using (ListPool<IStyleActivationSource>.Get(out var sources))
            using (DictionaryPool<string, bool>.Get(out var activationMap))
            {
                root.GetComponentsInParent(true, sources);

                // Traverse from root to leaf, so that closer groups override ancestors.
                for (int i = sources.Count - 1; i >= 0; i--)
                {
                    var source = sources[i];
                    using (ListPool<StyleActivation>.Get(out var sourceActivations))
                    {
                        source.GetStyleActivations(sourceActivations);
                        
                        foreach (var style in sourceActivations)
                            activationMap[style.Name] = style.Active;
                    }
                }

                foreach (var kvp in activationMap)
                    result.Add(new StyleActivation(kvp.Key, kvp.Value));
            }
        }

        private static void Update()
        {
            if (_dirtyRootQueue.Count == 0)
                return;

            using (HashSetPool<GameObject>.Get(out var dirtyRoots))
            using (HashSetPool<IStyleActivationChangeListener>.Get(out var listenersToNotify))
            {
                // we should copy the sources to a temporary list before processing 
                // because the source->listener notifications may cause sources to be dirtied again
                foreach (var root in _dirtyRootQueue)
                {
                    if (root == null)
                        continue;
                    
                    dirtyRoots.Add(root);
                }
                _dirtyRootQueue.Clear();

                foreach (var root in dirtyRoots)
                {
                    using(ListPool<IStyleActivationChangeListener>.Get(out var childListeners))
                    {
                        root.GetComponentsInChildren(childListeners);

                        foreach (var listener in childListeners)
                            listenersToNotify.Add(listener);
                    }
                }

                foreach (var listener in listenersToNotify)
                {
                    if(listener is Behaviour listenerBehaviour && listenerBehaviour != null)
                    {
                        using(ListPool<StyleActivation>.Get(out var styleActivations))
                        using(ListPool<string>.Get(out var activeStyles))
                        {
                            GetStyleActivations(listenerBehaviour.gameObject, styleActivations);

                            foreach (var activation in styleActivations)
                            {
                                if (activation.Active)
                                    activeStyles.Add(activation.Name);
                            }

                            listener.OnStyleActivationsChanged(activeStyles);
                        }
                    }
                }
            }
        }

        private static void ProcessSingle(GameObject root)
        {
            // we should copy the sources to a temporary list before processing 
            // because the source->listener notifications may cause sources to be dirtied again
            _dirtyRootQueue.Remove(root);

            using(ListPool<IStyleActivationChangeListener>.Get(out var childListeners))
            {
                root.GetComponentsInChildren(childListeners);

                foreach (var listener in childListeners)
                {
                    var listenerBehaviour = listener as Behaviour;

                    using(ListPool<StyleActivation>.Get(out var styleActivations))
                    using(ListPool<string>.Get(out var activeStyles))
                    {
                        GetStyleActivations(listenerBehaviour.gameObject, styleActivations);

                        foreach (var activation in styleActivations)
                        {
                            if (activation.Active)
                                activeStyles.Add(activation.Name);
                        }

                        listener.OnStyleActivationsChanged(activeStyles);
                    }
                }
            }
        }

        /// <summary>
        /// Find overrides that effect this root.
        /// - Overrides that are closer to the root win over more distant ones
        /// - In the case where there are multiple overrides on the same root, new ones win over older ones.
        /// </summary>
        static bool TryGetFirstOverrideInParent(GameObject root, out StylingOverrideScope result)
        {
            using(ListPool<StylingOverrideScope>.Get(out var parentOverrides))
            {
                // important: we iterate backwards here so that newer overrides win over 
                // older ones if they happen to be on the same root
                for (int i = _overrides.Count - 1; i >= 0; i--)
                {
                    StylingOverrideScope styleOverride = _overrides[i];
                    
                    if ( root.transform.IsChildOf(styleOverride.Root.transform))
                    {
                        parentOverrides.Add(styleOverride);
                    }
                }

                if(parentOverrides.Count > 1)
                {
                    for (var current = root.transform; current != null; current = current.parent)
                    {
                        var currentGo = current.gameObject;
                        
                        foreach (var parent in parentOverrides)
                        {
                            if (parent.Root == currentGo)
                            {
                                result = parent;
                                return true;
                            }
                        }
                    }

                    result = default;
                    return false;
                }
                else if(parentOverrides.Count == 1)
                {
                    result = parentOverrides[0];
                    return true;
                }
                else
                {
                    result = default;
                    return false;
                }

            }
        }
        private static bool InsertAfter<T>(
            ref PlayerLoopSystem root,
            PlayerLoopSystem systemToInsert)
        {
            if (root.subSystemList == null)
                return false;

            for (var i = 0; i < root.subSystemList.Length; i++)
            {
                ref var subSystem = ref root.subSystemList[i];

                if (subSystem.type == typeof(T))
                {
                    var oldList = root.subSystemList;
                    var newList = new PlayerLoopSystem[oldList.Length + 1];

                    // Copy up to and including target
                    Array.Copy(oldList, 0, newList, 0, i + 1);

                    // Insert new system
                    newList[i + 1] = systemToInsert;

                    // Copy remaining
                    Array.Copy(
                        oldList,
                        i + 1,
                        newList,
                        i + 2,
                        oldList.Length - (i + 1));

                    root.subSystemList = newList;
                    return true;
                }

                if (InsertAfter<T>(ref subSystem, systemToInsert))
                    return true;
            }

            return false;
        }

        private class StylingSystemUpdate { }
    }
}