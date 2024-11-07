// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <inheritdoc/>
    public class JavaScriptProjectTimeout : IJavaScriptProjectTimeout
    {
        /// <nodoc />
        public JavaScriptProjectTimeout() { }

        /// <nodoc />
        public JavaScriptProjectTimeout(IJavaScriptProjectTimeout template, PathRemapper path) 
        {
            Timeout = template.Timeout;
            WarningTimeout = template.WarningTimeout;
            ProjectSelector = template.ProjectSelector;
        }

        /// <inheritdoc/>
        public string Timeout { get; set; }

        /// <inheritdoc/>
        public string WarningTimeout { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<DiscriminatingUnion<string, IJavaScriptProjectSimpleSelector, IJavaScriptProjectRegexSelector>> ProjectSelector { get; set; }
    }
}
