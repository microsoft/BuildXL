// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
    }
}
