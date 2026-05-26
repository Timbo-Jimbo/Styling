using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace TimboJimbo.Styling.Editor
{
    internal static class StyleSheetEditorStylingUtility
    {
        [Obsolete("Don't need to call this")]
        public static void RefreshSubtreeImmediate(GameObject root)
        {
            // if (root == null)
            //     return;

            // using (ListPool<StyleSheet>.Get(out var sheets))
            // {
            //     root.GetComponentsInChildren(includeInactive: true, results: sheets);
            //     RefreshSheetsImmediate(sheets);
            // }
        }

        private static void RefreshSheetsImmediate(List<StyleSheet> sheets)
        {
            using (ListPool<StyleActivation>.Get(out var activations))
            using (ListPool<string>.Get(out var activeStyles))
            {
                for (int i = 0; i < sheets.Count; i++)
                {
                    var sheet = sheets[i];
                    if (sheet == null)
                        continue;

                    activations.Clear();
                    activeStyles.Clear();

                    StylingSystem.GetStyleActivations(sheet.gameObject, activations);
                    for (int j = 0; j < activations.Count; j++)
                    {
                        if (activations[j].Active)
                            activeStyles.Add(activations[j].Name);
                    }

                    sheet.OnStyleActivationsChanged(activeStyles);
                    sheet.CompleteTransitionImmediate();
                }
            }
        }
    }
}
