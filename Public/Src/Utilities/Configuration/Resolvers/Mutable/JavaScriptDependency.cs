// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities.Core;

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
            Dependencies = template.Dependencies?.Select(dep => Remap(dep, pathRemapper)).ToList();
            Dependents = template.Dependents;
        }

        /// <inheritdoc/>
        public IReadOnlyList<DiscriminatingUnion<string, IJavaScriptProjectSimpleSelector, IJavaScriptProjectRegexSelector, ILazyEval, FileArtifact, DirectoryArtifact>> Dependencies { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<DiscriminatingUnion<string, IJavaScriptProjectSimpleSelector, IJavaScriptProjectRegexSelector>> Dependents { get; set; }

        private static DiscriminatingUnion<string, IJavaScriptProjectSimpleSelector, IJavaScriptProjectRegexSelector, ILazyEval, FileArtifact, DirectoryArtifact> Remap(
            DiscriminatingUnion<string, IJavaScriptProjectSimpleSelector, IJavaScriptProjectRegexSelector, ILazyEval, FileArtifact, DirectoryArtifact> dep,
            PathRemapper pathRemapper)
        {
            if (dep == null) {
                return null;
            }

            switch (dep.GetValue()) {
                case FileArtifact fileArtifact:
                    return new DiscriminatingUnion<string, IJavaScriptProjectSimpleSelector, IJavaScriptProjectRegexSelector, ILazyEval, FileArtifact, DirectoryArtifact>(pathRemapper.Remap(fileArtifact));
                case DirectoryArtifact directoryArtifact:
                    return new DiscriminatingUnion<string, IJavaScriptProjectSimpleSelector, IJavaScriptProjectRegexSelector, ILazyEval, FileArtifact, DirectoryArtifact>(pathRemapper.Remap(directoryArtifact));
                default:
                    return dep;
            }
        }
    }
}
