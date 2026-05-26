using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TimboJimbo.Styling.Editor
{
	/// <summary>
	/// Forces StyleSheets to serialize their baseline values by temporarily applying
	/// empty style activations during scene/prefab save, then restores the prior
	/// evaluated state immediately afterward.
	/// </summary>
	[InitializeOnLoad]
	internal static class StyleSheetSaveHandler
	{
		private static readonly string[] EmptyStyles = Array.Empty<string>();
		private static readonly List<GameObject> OverrideRoots = new List<GameObject>();
		private static readonly List<StylingOverride> SaveOverrides = new List<StylingOverride>();

		static StyleSheetSaveHandler()
		{
			EditorSceneManager.sceneSaving += OnSceneSaving;
			EditorSceneManager.sceneSaved += OnSceneSaved;
			PrefabStage.prefabSaving += OnPrefabSaving;
			PrefabStage.prefabSaved += OnPrefabSaved;
		}

		private static void OnSceneSaving(Scene scene, string path)
		{
			BeginRevert(EnumerateSceneRoots(scene));
		}

		private static void OnSceneSaved(Scene scene)
		{
			EndRevert();
		}

		private static void OnPrefabSaving(GameObject prefabRoot)
		{
			if (prefabRoot == null)
				return;

			BeginRevert(new[] { prefabRoot });
		}

		private static void OnPrefabSaved(GameObject prefabRoot)
		{
			EndRevert();
		}

		private static IEnumerable<GameObject> EnumerateSceneRoots(Scene scene)
		{
			if (!scene.IsValid())
				yield break;

			var roots = scene.GetRootGameObjects();
			for (int i = 0; i < roots.Length; i++)
				yield return roots[i];
		}

		private static void BeginRevert(IEnumerable<GameObject> roots)
		{
			EndRevert();

			foreach (var root in roots)
			{
				if (root == null)
					continue;
				if (!ContainsStyleSheet(root))
					continue;

				OverrideRoots.Add(root);
				SaveOverrides.Add(StylingSystem.StylingOverrideScope(root, EmptyStyles));
				StyleSheetEditorStylingUtility.RefreshSubtreeImmediate(root);
			}
		}

		private static void EndRevert()
		{
			if (SaveOverrides.Count == 0)
				return;

			for (int i = SaveOverrides.Count - 1; i >= 0; i--)
				SaveOverrides[i].Dispose();

			SaveOverrides.Clear();

			for (int i = 0; i < OverrideRoots.Count; i++)
			{
				var root = OverrideRoots[i];
				if (root != null)
					StyleSheetEditorStylingUtility.RefreshSubtreeImmediate(root);
			}

			OverrideRoots.Clear();
		}

		private static bool ContainsStyleSheet(GameObject root)
		{
			return root.GetComponentInChildren<StyleSheet>(includeInactive: true) != null;
		}
	}
}
