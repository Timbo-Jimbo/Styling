using System.Collections.Generic;
using TimboJimbo.PropertyBindings;
using TimboJimbo.PropertyBindings.Editor.Utility;
using TimboJimboEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Pool;

namespace TimboJimbo.Styling.Editor
{
	/// <summary>
	/// Recording session for <see cref="StyleSheet"/> that commits sparse style entries:
	/// only properties the user actually edits become part of the saved style.
	/// </summary>
	public static class StyleSheetRecordingSession
	{
		private static readonly string[] EmptyStyles = System.Array.Empty<string>();

		public static bool IsRecording { get; private set; }
		public static StyleSheet Target { get; private set; }
		public static bool IsCreatingNew { get; private set; }
		public static string EditingStyleName { get; set; }

		private static PropertyBindingCollection _collection;
		private static List<BindableProperty> _allProperties;
		private static List<BindablePropertyToValue> _preEditValues;
		private static UserEditTracker _tracker;
		private static string _editingOriginalName;
		private static StylingOverride _activeOverride;

		[InitializeOnLoadMethod]
		private static void Init()
		{
			EditorApplication.playModeStateChanged += OnPlayModeChanged;
			AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
		}

		internal static void GetCurrentEdits(List<BindablePropertyValueEdit> dest)
		{
			dest.Clear();
			if (!IsRecording || _tracker == null)
				return;

			_tracker.GetEdits(dest);
		}

		private static void OnPlayModeChanged(PlayModeStateChange state)
		{
			if (state is PlayModeStateChange.ExitingEditMode or PlayModeStateChange.ExitingPlayMode)
				CancelRecording();
		}

		private static void OnBeforeAssemblyReload() => CancelRecording();

		public static void StartCreating(StyleSheet target)
		{
			if (target == null || IsRecording)
				return;

			BeginSession(target);
			IsCreatingNew = true;
			_editingOriginalName = null;
			EditingStyleName = SuggestNewStyleName(target);
			_tracker.StartDetecting(OnTrackerEdit);
		}

		public static void StartEditing(StyleSheet target, int styleIndex)
		{
			if (target == null || IsRecording)
				return;
			if (styleIndex < 0 || styleIndex >= target.Styles.Count)
				return;

			BeginSession(target);
			IsCreatingNew = false;

			var style = target.Styles[styleIndex];
			_editingOriginalName = style.Name;
			EditingStyleName = style.Name;

			ApplyOverride(target.gameObject, new[] { style.Name });

			EditorApplication.delayCall += () =>
			{
				if (IsRecording && _tracker != null)
					_tracker.StartDetecting(OnTrackerEdit);
			};
		}

		public static void SaveAndStop()
		{
			if (!IsRecording || Target == null)
				return;

			using var _ = ListPool<BindablePropertyValueEdit>.Get(out var edits);
			_tracker.GetEdits(edits);

			if (edits.Count == 0)
			{
				EditorUtility.DisplayDialog("Style Sheet", "No changes detected.", "OK");
				return;
			}

			using (ListPool<BindablePropertyToValue>.Get(out var postEditValues))
			using (ListPool<BindableProperty>.Get(out var propertiesForStyle))
			{
				SnapshotAllValues(postEditValues);

				for (int i = 0; i < edits.Count; i++)
					propertiesForStyle.Add(edits[i].BindableProperty);

				if (IsCreatingNew)
				{
					var name = (EditingStyleName ?? string.Empty).Trim();
					if (string.IsNullOrEmpty(name))
					{
						EditorUtility.DisplayDialog("Style Sheet", "Style name cannot be empty.", "OK");
						return;
					}

					if (StyleNameExists(Target, name))
					{
						EditorUtility.DisplayDialog("Style Sheet", $"A style named '{name}' already exists.", "OK");
						return;
					}

					Undo.RecordObject(Target, "Create Style");
					Target.CreateStyle(name, propertiesForStyle, _preEditValues, postEditValues);
				}
				else
				{
					Undo.RecordObject(Target, "Edit Style");
					Target.EditStyle(_editingOriginalName, propertiesForStyle, _preEditValues, postEditValues);
				}

				EditorUtility.SetDirty(Target);
			}

			EndSession(revertToSnapshot: false);
		}

		public static void CancelRecording()
		{
			if (!IsRecording)
				return;

			EndSession(revertToSnapshot: true);
		}

		private static void BeginSession(StyleSheet target)
		{
			Target = target;
			_allProperties = new List<BindableProperty>();
			_preEditValues = new List<BindablePropertyToValue>();

			BindablePropertyUtility.GetBindableProperties(target.gameObject, _allProperties, recursive: true);
			_collection = PropertyBindingCollection.Bind(target.gameObject, _allProperties);
			_tracker = new UserEditTracker(filterOut: bp => !_allProperties.Contains(bp));

			Undo.RegisterFullObjectHierarchyUndo(target.gameObject, "Begin Style Recording");

			ApplyOverride(target.gameObject, EmptyStyles);
			SnapshotAllValues(_preEditValues);

			IsRecording = true;
			SceneView.RepaintAll();
		}

		private static void EndSession(bool revertToSnapshot)
		{
			var target = Target;

			if (revertToSnapshot && _collection != null && _preEditValues != null)
			{
				using var writer = _collection.StartBulkWriteScope();
				for (int i = 0; i < _preEditValues.Count; i++)
					writer.TryWrite(_preEditValues[i].Property, _preEditValues[i].Value);
			}

			_tracker?.StopDetecting();
			_tracker = null;

			_collection?.Dispose();
			_collection = null;

			_activeOverride?.Dispose();
			_activeOverride = null;

			if (target != null)
				StyleSheetEditorStylingUtility.RefreshSubtreeImmediate(target.gameObject);

			_allProperties = null;
			_preEditValues = null;
			_editingOriginalName = null;

			IsRecording = false;
			IsCreatingNew = false;
			EditingStyleName = null;
			Target = null;

			if (target != null)
				Selection.activeGameObject = target.gameObject;

			SceneView.RepaintAll();
		}

		private static void ApplyOverride(GameObject root, IReadOnlyList<string> activeStyleNames)
		{
			_activeOverride?.Dispose();
			_activeOverride = StylingSystem.StylingOverrideScope(root, activeStyleNames);
			StyleSheetEditorStylingUtility.RefreshSubtreeImmediate(root);
		}

		private static void SnapshotAllValues(List<BindablePropertyToValue> result)
		{
			result.Clear();
			for (int i = 0; i < _allProperties.Count; i++)
			{
				var property = _allProperties[i];
				if (_collection.TryRead(property, out var value))
					result.Add(new BindablePropertyToValue { Property = property, Value = value });
			}
		}

		private static void OnTrackerEdit(EditType editType, BindablePropertyValueEdit edit)
		{
			SceneView.RepaintAll();
		}

		private static bool StyleNameExists(StyleSheet sheet, string name)
		{
			var styles = sheet.Styles;
			for (int i = 0; i < styles.Count; i++)
			{
				if (styles[i].Name == name)
					return true;
			}

			return false;
		}

		private static string SuggestNewStyleName(StyleSheet sheet)
		{
			int n = sheet.Styles.Count;
			string candidate;
			do
			{
				candidate = $"Style {++n}";
			} while (StyleNameExists(sheet, candidate));

			return candidate;
		}
	}
}
