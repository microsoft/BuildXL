// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <inheritdoc/>
    public class JavaScriptScopeWithSelector : IJavaScriptScopeWithSelector
    {
        /// <nodoc />
        public JavaScriptScopeWithSelector()
        {
        }

        /// <nodoc />
        public JavaScriptScopeWithSelector(IJavaScriptScopeWithSelector template, PathRemapper pathRemapper)
        {
            Scope =  template.Scope.GetValue() switch
            {
                string s => new(s),
                DirectoryArtifact d => new(DirectoryArtifact.CreateWithZeroPartialSealId(pathRemapper.Remap(d.Path))),
                _ => throw new BuildXLException($"Unexpected type in DiscriminatingUnion<string, DirectoryArtifact>: {template.Scope.GetValue().GetType()}")
            };
            Packages = template.Packages;
        }
        
        /// <inheritdoc/>
        public IReadOnlyList<DiscriminatingUnion<string, IJavaScriptProjectSimpleSelector, IJavaScriptProjectRegexSelector>> Packages { get; set; }

        /// <inheritdoc/>
        public DiscriminatingUnion<string, DirectoryArtifact> Scope { get; set; }
    }
}
