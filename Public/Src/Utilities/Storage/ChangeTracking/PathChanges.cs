// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.Storage.ChangeTracking
{
    /// <summary>
    /// Types of detected change to a path. A path may have multiple changes reported at once (multiple flags set).
    /// </summary>
    [Flags]
    public enum PathChanges
    {
        /// <nodoc />
        None,

        /// <summary>
        /// Data or metadata has possibly changed for this path (which was previously existent and tracked).
        /// </summary>
        DataOrMetadataChanged = 1,

        /// <summary>
        /// The path possibly no longer exists (or the file at the path has been replaced with different one).
        /// </summary>
        Removed = 2,

        /// <summary>
        /// The path had previously been tracked as non-existent. Now, it may exist.
        /// </summary>
        NewlyPresent = 4,

        /// <summary>
        /// The direct membership of a directory (which had been the target of a tracked enumeration) has possibly changed.
        /// </summary>
        MembershipChanged = 8,

        /// <summary>
        /// Refinement of <see cref="NewlyPresent"/> in which the path now exists as a file.
        /// </summary>
        NewlyPresentAsFile = 16,

        /// <summary>
        /// Refinement of <see cref="NewlyPresent"/> in which the path now exists as a directory.
        /// </summary>
        NewlyPresentAsDirectory = 32,
    }

    /// <summary>
    /// Extensions for <see cref="PathChanges"/>.
    /// </summary>
    public static class PathChangesExtensions
    {
        /// <summary>
        /// Checks if path changes include newly present.
        /// </summary>
        public static bool ContainsNewlyPresent(this PathChanges pathChanges)
        {
            return (pathChanges & (PathChanges.NewlyPresent | PathChanges.NewlyPresentAsDirectory | PathChanges.NewlyPresentAsFile)) != 0;
        }

        /// <summary>
        /// Checks if path changes are excatly newly present.
        /// </summary>
        public static bool IsNewlyPresent(this PathChanges pathChanges)
        {
            return pathChanges == PathChanges.NewlyPresentAsFile || pathChanges == PathChanges.NewlyPresentAsDirectory  || pathChanges == PathChanges.NewlyPresent;
        }
    }
}
