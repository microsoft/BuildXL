// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Selects a set of JavaScriptProject using a regular expression for both the package name and script command
    /// </summary>
    public interface IJavaScriptProjectRegexSelector
    {
        /// <nodoc/>
        string PackageNameRegex { get; }

        /// <nodoc/>
        string CommandRegex { get; }
    }
}
