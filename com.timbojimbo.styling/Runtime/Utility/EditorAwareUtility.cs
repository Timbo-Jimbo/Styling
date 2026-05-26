using UnityEngine;
using Object = UnityEngine.Object;

namespace TimboJimbo.Styling
{
    internal static class EditorAwareUtility
    {
        public static bool IsLiveInstance(Object obj)
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(obj))
                return false;

            if (IsInPreviewScene(obj))
                return false;

            if (!Application.isPlaying)
                return false;
#endif
            if (!IsInScene(obj))
                return false;

            return true;
        }

        public static bool IsAsset(Object obj)
        {
            return !IsLiveInstance(obj);
        }

        private static bool IsInScene(Object obj)
        {
            if (obj is Component c)
                return c.gameObject.scene.IsValid();
            if (obj is GameObject go)
                return go.scene.IsValid();
            return false;
        }

#if UNITY_EDITOR
        private static bool IsInPreviewScene(Object obj)
        {
            if (obj is Component c)
                return UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(c.gameObject.scene);
            if (obj is GameObject go)
                return UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(go.scene);
            return false;
        }
#endif
    }
}
