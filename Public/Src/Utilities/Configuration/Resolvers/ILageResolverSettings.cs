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
        /// Either NpmLocation or LageLocation should be provided, but not both.
        /// </remarks>
        FileArtifact? NpmLocation { get; }

        /// <summary>
        /// The location of Lage.
        /// </summary>
        /// <remarks>
        /// Either NpmLocation or LageLocation should be provided, but not both.
        /// </remarks>
        FileArtifact? LageLocation { get; }

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

        /// <summary>
        /// When enabled, BuildXL assumes the name of each directory immediately under the yarn strict store (e.g. .store/@babylonjs-core@7.54.3-d93831e7ae9116fa2dd7) univocally determines the file layout and content under it.
        /// </summary>
        /// <remarks>
        /// This option is a performance optimization: when this option is enabled, BuildXL can avoid tracking all files under the yarn strict store, which can be a large number of files, and instead only track the directory names.
        /// Caution must be taken to ensure that no other process mutates the content under the yarn strict store outside of yarn strict itself before a BuildXL build begins, otherwise underbuilds may occur. E.g. this setting should not be
        /// enabled for developer builds.
        /// </remarks>
        bool? UseYarnStrictAwarenessTracking { get; }

        /// <summary>
        /// When enabled, BuildXL disallows writes under the yarn strict store.
        /// </summary>
        /// <remarks>
        /// Unless specified, this setting defaults to the value of <see cref="UseYarnStrictAwarenessTracking"/>. This reflects the fact that whenever BuildXL is aware of the existence of the yarn strict store, it will also disallow 
        /// writes under it by default.
        /// </remarks>
        bool? DisallowWritesUnderYarnStrictStore { get; }
    }
}
