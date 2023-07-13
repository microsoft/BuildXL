// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Settings for Lage resolver
    /// </summary>
    public interface ILageResolverSettings : IJavaScriptResolverSettings
    {
        /// <summary>
        /// The location of NPM.  If not provided, BuildXL will try to look for it under PATH.
        /// </summary>
        /// <remarks>
        /// Npm is used to get Lage during graph construction
        /// </remarks>
        FileArtifact? NpmLocation { get; }

        /// <summary>
        /// Instructs Lage to generate a subset of the build graph that contains only the nodes that have changed since the given commit.
        /// </summary>
        /// <remarks>
        /// <see href="https://microsoft.github.io/lage/docs/Reference/cli"></see>
        /// Warning: scoping down builds with '--since' may introduce unsound incremental behavior. Lage filters out the build graph based on git changes without considering the build graph may be underspecified. 
        /// For example, if a project does not declare a dependency on another project, then the build graph will not be scoped down correctly. This could even be the case in a DFA-free build, where, for example,
        /// a project does not declare a dependency on an out-of-project *source* file (since BuildXL allows JavaScript pips to consume any source file as long as there are no races).
        /// </remarks>
        string Since { get; }
    }
}
