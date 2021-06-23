// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <inheritdoc/>
    public class JavaScriptDependency : IJavaScriptDependency
    {
        /// <nodoc />
        public JavaScriptDependency()
        { }
        
        /// <nodoc />
        public JavaScriptDependency(IJavaScriptDependency template, PathRemapper pathRemapper)
        {
            Dependencies = template.Dependencies;
            Dependents = template.Dependents;
        }

        /// <inheritdoc/>
        public IReadOnlyList<DiscriminatingUnion<string, IJavaScriptProjectSimpleSelector, IJavaScriptProjectRegexSelector, ILazyEval>> Dependencies { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<DiscriminatingUnion<string, IJavaScriptProjectSimpleSelector, IJavaScriptProjectRegexSelector>> Dependents { get; set; }
    }
}
