// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// A scope defined only for projects selected with a <see cref="IJavaScriptProjectRegexSelector" />
    /// </summary>
    public interface IJavaScriptScopeWithSelector
    {
        /// <nodoc/>
        DiscriminatingUnion<string, DirectoryArtifact> Scope { get; }

        /// <nodoc/>
        IReadOnlyList<DiscriminatingUnion<string, IJavaScriptProjectSimpleSelector, IJavaScriptProjectRegexSelector>> Packages { get; }
    }
}
