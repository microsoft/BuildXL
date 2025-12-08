// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Core;

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
        IReadOnlyList<DiscriminatingUnion<string, IJavaScriptProjectSimpleSelector, IJavaScriptProjectRegexSelector, ILazyEval, FileArtifact, DirectoryArtifact>> Dependencies { get; }

        /// <nodoc/>
        IReadOnlyList<DiscriminatingUnion<string, IJavaScriptProjectSimpleSelector, IJavaScriptProjectRegexSelector>> Dependents { get; }
    }

    /// <nodoc/>
    public static class JavaScriptDependencyExtensions
    {
        /// <summary>
        /// Checks whether the dependency is selecting a JavaScript project (that is, a <see cref="string"/>, a <see cref="IJavaScriptProjectSimpleSelector"/> or a <see cref="IJavaScriptProjectRegexSelector"/>)
        /// </summary>
        public static bool IsProjectSelector(this DiscriminatingUnion<string, IJavaScriptProjectSimpleSelector, IJavaScriptProjectRegexSelector, ILazyEval, FileArtifact, DirectoryArtifact> dependency)
        {
            return dependency != null && 
                dependency.GetValue() is var dependencyValue &&
                dependencyValue != null &&
                dependencyValue is not ILazyEval && 
                dependencyValue is not FileArtifact && 
                dependencyValue is not DirectoryArtifact;
        }
    }
}
