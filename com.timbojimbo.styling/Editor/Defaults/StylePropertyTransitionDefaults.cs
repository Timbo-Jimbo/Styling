using System;
using System.Collections.Generic;
using TimboJimbo.Core;
using TimboJimbo.PropertyBindings;
using TimboJimbo.Styling;

namespace TimboJimboEditor.Styling.Defaults
{
    [Serializable]
    public class StylePropertyTransitionDefaults : StylePropertyTransition.IDefaultsResolver
    {
        public ColorInterpolationMode DefaultColorInterpolationMode;
        public DiscreteValueSelectionMode DefaultDiscreteValueSelectionMode;
        public VectorInterpolationMode DefaultVector2InterpolationMode;
        public VectorInterpolationMode DefaultVector3InterpolationMode;
        public RotationInterpolationMode DefaultRotationInterpolationMode;
        public EaseType DefaultEaseType;
        public List<OverrideDefault<ColorInterpolationMode>> ColorInterpolationModeOverrides = new();
        public List<OverrideDefault<DiscreteValueSelectionMode>> DiscreteValueSelectionModeOverrides = new();
        public List<OverrideDefault<VectorInterpolationMode>> Vector2InterpolationModeOverrides = new();
        public List<OverrideDefault<VectorInterpolationMode>> Vector3InterpolationModeOverrides = new();
        public List<OverrideDefault<RotationInterpolationMode>> RotationInterpolationModeOverrides = new();
        public List<OverrideDefault<EaseType>> EaseTypeOverrides = new();

        public StylePropertyTransition GetDefaultTransition(BindableProperty property, float duration = 0f)
        {
            var typeName = property.Target.GetType().AssemblyQualifiedName;
            var colorOverride = ColorInterpolationModeOverrides.Find(o => o.Type == typeName && o.PropertyPath == property.Path)?.Default ?? DefaultColorInterpolationMode;
            var discreteValueSelectionOverride = DiscreteValueSelectionModeOverrides.Find(o => o.Type == typeName && o.PropertyPath == property.Path)?.Default ?? DefaultDiscreteValueSelectionMode;
            var vector2Override = Vector2InterpolationModeOverrides.Find(o => o.Type == typeName && o.PropertyPath == property.Path)?.Default ?? DefaultVector2InterpolationMode;
            var vector3Override = Vector3InterpolationModeOverrides.Find(o => o.Type == typeName && o.PropertyPath == property.Path)?.Default ?? DefaultVector3InterpolationMode;
            var rotationOverride = RotationInterpolationModeOverrides.Find(o => o.Type == typeName && o.PropertyPath == property.Path)?.Default ?? DefaultRotationInterpolationMode;
            var easeTypeOverride = EaseTypeOverrides.Find(o => o.Type == typeName && o.PropertyPath == property.Path)?.Default ?? DefaultEaseType;

            return new StylePropertyTransition
            {
                Interpolation = new InterpolationConfig
                {
                    Rotation = rotationOverride,
                    Vector2 = vector2Override,
                    Vector3 = vector3Override,
                    Color = colorOverride
                },
                DiscreteValueSelection = discreteValueSelectionOverride,
                EaseType = easeTypeOverride,
                Duration = duration
            };
        }

        public void RemoveOverride<T>(BindableProperty property)
        {
            var typeName = property.Target.GetType().AssemblyQualifiedName;
            if (typeof(T) == typeof(ColorInterpolationMode))
                ColorInterpolationModeOverrides.RemoveAll(o => o.Type == typeName && o.PropertyPath == property.Path);
            else if (typeof(T) == typeof(DiscreteValueSelectionMode))
                DiscreteValueSelectionModeOverrides.RemoveAll(o => o.Type == typeName && o.PropertyPath == property.Path);
            else if (typeof(T) == typeof(VectorInterpolationMode))
            {
                if (property.Kind == ValueKind.Vector2)
                    Vector2InterpolationModeOverrides.RemoveAll(o => o.Type == typeName && o.PropertyPath == property.Path);
                else if (property.Kind == ValueKind.Vector3)
                    Vector3InterpolationModeOverrides.RemoveAll(o => o.Type == typeName && o.PropertyPath == property.Path);
            }
            else if (typeof(T) == typeof(RotationInterpolationMode))
                RotationInterpolationModeOverrides.RemoveAll(o => o.Type == typeName && o.PropertyPath == property.Path);
            else if (typeof(T) == typeof(EaseType))
                EaseTypeOverrides.RemoveAll(o => o.Type == typeName && o.PropertyPath == property.Path);
                
            StylingSettings.Save();
        }

        public bool GetOverride(BindableProperty property, out ColorInterpolationMode overrideDefault)
        {
            var typeName = property.Target.GetType().AssemblyQualifiedName;
            var overrideEntry = ColorInterpolationModeOverrides.Find(o => o.Type == typeName && o.PropertyPath == property.Path);
            if (overrideEntry != null)
            {
                overrideDefault = overrideEntry.Default;
                return true;
            }

            overrideDefault = DefaultColorInterpolationMode;
            return false;
        }

        public void SetOverride(BindableProperty property, ColorInterpolationMode overrideDefault)
        {
            var existing = ColorInterpolationModeOverrides.Find(o => o.Type == property.Target.GetType().AssemblyQualifiedName && o.PropertyPath == property.Path);
            
            if (existing != null)
            {
                existing.Default = overrideDefault;
                StylingSettings.Save();
                return;
            }
            
            ColorInterpolationModeOverrides.Add(new OverrideDefault<ColorInterpolationMode>
            {
                Type = property.Target.GetType().AssemblyQualifiedName,
                PropertyPath = property.Path,
                Default = overrideDefault
            });

            StylingSettings.Save();
        }

        public bool GetOverride(BindableProperty property, out DiscreteValueSelectionMode overrideDefault)
        {
            var typeName = property.Target.GetType().AssemblyQualifiedName;
            var overrideEntry = DiscreteValueSelectionModeOverrides.Find(o => o.Type == typeName && o.PropertyPath == property.Path);
            if (overrideEntry != null)
            {
                overrideDefault = overrideEntry.Default;
                return true;
            }

            overrideDefault = DefaultDiscreteValueSelectionMode;
            return false;
        }

        public void SetOverride(BindableProperty property, DiscreteValueSelectionMode overrideDefault)
        {
            var existing = DiscreteValueSelectionModeOverrides.Find(o => o.Type == property.Target.GetType().AssemblyQualifiedName && o.PropertyPath == property.Path);
            
            if (existing != null)
            {
                existing.Default = overrideDefault;
                StylingSettings.Save();
                return;
            }
            
            DiscreteValueSelectionModeOverrides.Add(new OverrideDefault<DiscreteValueSelectionMode>
            {
                Type = property.Target.GetType().AssemblyQualifiedName,
                PropertyPath = property.Path,
                Default = overrideDefault
            });

            StylingSettings.Save();
        }

        public bool GetOverride(BindableProperty property, out VectorInterpolationMode overrideDefault)
        {
            var typeName = property.Target.GetType().AssemblyQualifiedName;
            var overrideEntry = property.Kind == ValueKind.Vector3
                ? Vector3InterpolationModeOverrides.Find(o => o.Type == typeName && o.PropertyPath == property.Path)
                : Vector2InterpolationModeOverrides.Find(o => o.Type == typeName && o.PropertyPath == property.Path);

            if (overrideEntry != null)
            {
                overrideDefault = overrideEntry.Default;
                return true;
            }

            overrideDefault = property.Kind == ValueKind.Vector3 ? DefaultVector3InterpolationMode : DefaultVector2InterpolationMode;
            return false;
        }

        public void SetOverride(BindableProperty property, VectorInterpolationMode overrideDefault)
        {
            var list = property.Kind == ValueKind.Vector3 ? Vector3InterpolationModeOverrides : Vector2InterpolationModeOverrides;
            var existing = list.Find(o => o.Type == property.Target.GetType().AssemblyQualifiedName && o.PropertyPath == property.Path);
            
            if (existing != null)
            {
                existing.Default = overrideDefault;
                StylingSettings.Save();
                return;
            }
            
            list.Add(new OverrideDefault<VectorInterpolationMode>
            {
                Type = property.Target.GetType().AssemblyQualifiedName,
                PropertyPath = property.Path,
                Default = overrideDefault
            });

            StylingSettings.Save();
        }

        public bool GetOverride(BindableProperty property, out RotationInterpolationMode overrideDefault)
        {
            var typeName = property.Target.GetType().AssemblyQualifiedName;
            var overrideEntry = RotationInterpolationModeOverrides.Find(o => o.Type == typeName && o.PropertyPath == property.Path);
            if (overrideEntry != null)
            {
                overrideDefault = overrideEntry.Default;
                return true;
            }

            overrideDefault = DefaultRotationInterpolationMode;
            return false;
        }

        public void SetOverride(BindableProperty property, RotationInterpolationMode overrideDefault)
        {
            var existing = RotationInterpolationModeOverrides.Find(o => o.Type == property.Target.GetType().AssemblyQualifiedName && o.PropertyPath == property.Path);
            
            if (existing != null)
            {
                existing.Default = overrideDefault;
                StylingSettings.Save();
                return;
            }
            
            RotationInterpolationModeOverrides.Add(new OverrideDefault<RotationInterpolationMode>
            {
                Type = property.Target.GetType().AssemblyQualifiedName,
                PropertyPath = property.Path,
                Default = overrideDefault
            });

            StylingSettings.Save();
        }

        public bool GetOverride(BindableProperty property, out EaseType overrideDefault)
        {
            var typeName = property.Target.GetType().AssemblyQualifiedName;
            var overrideEntry = EaseTypeOverrides.Find(o => o.Type == typeName && o.PropertyPath == property.Path);
            if (overrideEntry != null)
            {
                overrideDefault = overrideEntry.Default;
                return true;
            }

            overrideDefault = DefaultEaseType;
            return false;
        }

        public void SetOverride(BindableProperty property, EaseType overrideDefault)
        {
            var existing = EaseTypeOverrides.Find(o => o.Type == property.Target.GetType().AssemblyQualifiedName && o.PropertyPath == property.Path);
            
            if (existing != null)
            {
                existing.Default = overrideDefault;
                StylingSettings.Save();
                return;
            }
            
            EaseTypeOverrides.Add(new OverrideDefault<EaseType>
            {
                Type = property.Target.GetType().AssemblyQualifiedName,
                PropertyPath = property.Path,
                Default = overrideDefault
            });

            StylingSettings.Save();
        }

        [Serializable]
        public class OverrideDefault<T>
        {
            public string Type;
            public string PropertyPath;
            public T Default;
        }
    }
}