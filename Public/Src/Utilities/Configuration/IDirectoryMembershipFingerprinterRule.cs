// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// An exception rule for the directory membership fingerprinter
    /// </summary>
    public interface IDirectoryMembershipFingerprinterRule : ITrackedValue
    {
        /// <summary>
        /// Name of the exception
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Path the exception applies to
        /// </summary>
        AbsolutePath Root { get; }

        /// <summary>
        /// Whether to disable filesystem enumeration and force graph based enumeration
        /// </summary>
        bool DisableFilesystemEnumeration { get; }

        /// <summary>
        /// List of wildcards to see if they should be ignored
        /// </summary>
        IReadOnlyList<PathAtom> FileIgnoreWildcards { get; }

        /// <summary>
        /// Whether this rule is applied to all directories under <see cref="Root"/>.
        /// </summary>
        bool Recursive { get; }
    }
}
