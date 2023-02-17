// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Allow List entry
    /// </summary>
    public partial interface IFileAccessAllowlistEntry : ITrackedValue
    {
        /// <summary>
        /// Name of the allow exception rule.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Value allowed to have an exception.  Cannot be combined with ToolPath.
        /// </summary>
        string Value { get; }

        /// <summary>
        /// Full path or executable name to misbehaving tool allowed to have an exception.  Cannot be combined with Value.
        /// </summary>
        DiscriminatingUnion<FileArtifact, PathAtom> ToolPath { get; }

        /// <summary>
        /// Fragment of a path to match.
        /// </summary>
        string PathFragment { get; }

        /// <summary>
        /// Pattern to match against accessed paths.
        /// </summary>
        string PathRegex { get; }
    }
}
