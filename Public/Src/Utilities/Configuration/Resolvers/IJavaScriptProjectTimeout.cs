// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// A JavaScriptDependency configures all dependents to depend on all specified dependencies
    /// </summary>
    public interface IJavaScriptProjectTimeout
    {
        /// <nodoc/>
        string Timeout { get; }

        /// <nodoc/>
        string WarningTimeout { get; }

        /// <summary>
        /// Project Selector that select the JavaScript projects to apply timeout (see DScript definition Public\Sdk\Public\Prelude\Prelude.Configuration.Resolvers.dsc)
        /// </summary>
        IReadOnlyList<DiscriminatingUnion<string, IJavaScriptProjectSimpleSelector, IJavaScriptProjectRegexSelector>> ProjectSelector { get; }
    }
}
