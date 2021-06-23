// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <inheritdoc/>
    public class JavaScriptProjectRegexSelector : IJavaScriptProjectRegexSelector
    {
        /// <nodoc />
        public JavaScriptProjectRegexSelector()
        { }

        /// <nodoc />
        public JavaScriptProjectRegexSelector(IJavaScriptProjectRegexSelector template)
        {
            PackageNameRegex = template.PackageNameRegex;
            CommandRegex = template.CommandRegex;
        }

        /// <inheritdoc/>
        public string PackageNameRegex { get; set; }

        /// <inheritdoc/>
        public string CommandRegex { get; set; }

        /// <inheritdoc/>
        public override string ToString() => $"Package name regex '{PackageNameRegex} and script command regex '{CommandRegex}'";
    }
}
