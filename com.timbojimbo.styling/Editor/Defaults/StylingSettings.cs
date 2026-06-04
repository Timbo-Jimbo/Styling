using System.IO;
using TimboJimbo.Styling;
using UnityEditor;

namespace TimboJimboEditor.Styling.Defaults
{
    [InitializeOnLoad]
    public static class StylingSettings
    {
        private static readonly string SettingsPath =
            Path.Combine(Directory.GetCurrentDirectory(), "UserSettings", "com.timbojimbo.styling.json");

        private static StylePropertyTransitionDefaults _defaults;

        public static StylePropertyTransitionDefaults Defaults
        {
            get
            {
                if (_defaults == null)
                    Load();

                return _defaults;
            }
        }

        static StylingSettings()
        {
            Load();
        }

        private static void Load()
        {
            _defaults = new StylePropertyTransitionDefaults();

            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                if (!string.IsNullOrEmpty(json))
                    EditorJsonUtility.FromJsonOverwrite(json, _defaults);
            }

            StylePropertyTransition.SetDefaultsResolver(_defaults);
        }

        public static void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, EditorJsonUtility.ToJson(Defaults, true));
            StylePropertyTransition.SetDefaultsResolver(Defaults);
        }
    }
}
