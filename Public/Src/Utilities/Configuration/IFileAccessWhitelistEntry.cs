// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// White List entry
    /// </summary>
    public partial interface IFileAccessWhitelistEntry : ITrackedValue
    {
        /// <summary>
        /// Name of the whitelist exception rule.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Value allowed to have an exception.  Cannot be combined with ToolPath.
        /// </summary>
        string Value { get; }

        /// <summary>
        /// Path to misbehaving tool allowed to have an exception.  Cannot be combined with Value.
        /// </summary>
        FileArtifact ToolPath { get; }

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
