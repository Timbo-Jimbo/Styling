using System.Collections.Generic;
using TimboJimbo.PropertyBindings;

namespace TimboJimbo.Styling
{
    public enum StyleSheetValidationCode
    {
        EmptyStyleName,
        DuplicateStyleName,
        InvalidProperty,
        DuplicateProperty,
        MissingBaseline,
        OrphanedTransition,
        BindingResolutionFailed
    }

    public sealed class StyleSheetValidationIssue
    {
        public StyleSheetValidationCode Code { get; }
        public string Message { get; }
        public string StyleName { get; }
        public BindableProperty Property { get; }
        public BindingResolutionReport BindingResolution { get; }

        internal StyleSheetValidationIssue(
            StyleSheetValidationCode code,
            string message,
            string styleName = null,
            BindableProperty property = default,
            BindingResolutionReport bindingResolution = null)
        {
            Code = code;
            Message = message;
            StyleName = styleName;
            Property = property;
            BindingResolution = bindingResolution;
        }
    }

    public sealed class StyleSheetValidationReport
    {
        public IReadOnlyList<StyleSheetValidationIssue> Issues { get; }
        public bool IsValid => Issues.Count == 0;

        internal StyleSheetValidationReport(IReadOnlyList<StyleSheetValidationIssue> issues)
        {
            Issues = issues;
        }
    }
}