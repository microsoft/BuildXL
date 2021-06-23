// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// A JavaScriptDependency configures all dependents to depend on all specified dependencies
    /// </summary>
    public interface IJavaScriptDependency 
    {
        /// <summary>
        /// The <see cref="ILazyEval"/> component represents either a FileArtifact or a StaticDirectory (see DScript definition Public\Sdk\Public\Prelude\Prelude.Configuration.Resolvers.dsc)
        /// </summary>
        IReadOnlyList<DiscriminatingUnion<string, IJavaScriptProjectSimpleSelector, IJavaScriptProjectRegexSelector, ILazyEval>> Dependencies { get; }

        /// <nodoc/>
        IReadOnlyList<DiscriminatingUnion<string, IJavaScriptProjectSimpleSelector, IJavaScriptProjectRegexSelector>> Dependents { get; }
    }
}
