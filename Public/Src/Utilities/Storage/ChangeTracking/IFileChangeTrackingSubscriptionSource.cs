// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using JetBrains.Annotations;
using Microsoft.Win32.SafeHandles;

namespace BuildXL.Storage.ChangeTracking
{
    /// <summary>
    /// Accepts files to track and returns <see cref="FileChangeTrackingSubscription"/>s corresponding to them.
    /// </summary>
    /// <remarks>
    /// Many operations need to track the files that they access, but do not themselves scan for changes to files.
    /// Such operations can take a <see cref="IFileChangeTrackingSubscriptionSource"/> rather than a full-fledged <see cref="FileChangeTracker"/>,
    /// </remarks>
    public interface IFileChangeTrackingSubscriptionSource
    {
        /// <summary>
        /// Attempts to add the provided file to the change tracking set. <paramref name="maybeIdentity"/> (if provided)
        /// must correspond to the file identity of <paramref name="handle"/>.
        /// See <see cref="FileChangeTrackingSet.TryTrackChangesToFile"/>.
        /// If the file cannot be tracked, an invalid subscription is returned.
        /// </summary>
        /// <remarks>
        /// We allow providing an identity to avoid querying identity twice (files are often also added to a <see cref="FileContentTable"/>,
        /// which also needs an identity).
        /// </remarks>
        FileChangeTrackingSubscription TryTrackChangesToFile(
            [NotNull]SafeFileHandle handle,
            [NotNull]string path,
            VersionedFileIdentity? maybeIdentity = null,
            TrackingUpdateMode updateMode = TrackingUpdateMode.Preserve);

        /// <summary>
        /// Combined operation of opening and tracking a directory (or its absence), enumerating it, and then tracking changes to that enumeration result (its membership).
        /// The membership of the directory will be invalidated if a name is added or removed directly inside the directory (i.e., when <c>FindFirstFile</c>
        /// and <c>FindNextFile</c> would see a different set of names).
        /// </summary>
        Possible<FileChangeTrackingSet.EnumerationResult> TryEnumerateDirectoryAndTrackMembership(
            [NotNull]string path,
            [NotNull]Action<string, FileAttributes> handleEntry);

        /// <summary>
        /// Probes for the existence of a path, while also tracking the result (e.g. if a file does not exist and is later created, that change will be detected).
        /// If probing succeeds but tracking fails, a <see cref="PathExistence"/> is still returned (the underlying tracker should record that tracking is incomplete).
        /// </summary>
        Possible<PathExistence> TryProbeAndTrackPath([NotNull]string path, bool? isReadOnly = default);

        /// <summary>
        /// Tracks a non-existent relative path chain from a tracked parent root.
        /// If trackedParentPath = 'C:\foo' and relativeAbsentPath = 'a\b\c'
        /// Then the following paths are tracked as absent: 'C:\foo\a', 'C:\foo\a\b', 'C:\foo\a\b\c'.
        /// ADVANCED. Use with care. This should only because if the relative has been guaranteed to be non-existent
        /// because the parent path non-existent or enumerated and the child path was non-existent
        /// </summary>
        bool TrackAbsentRelativePath([NotNull]string trackedParentPath, [NotNull]string relativeAbsentPath);
    }
}
