// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Utilities;

namespace BuildXL.Engine.Cache.Artifacts
{
    /// <summary>
    /// Provides existence / enumeration tracking for local file system
    /// </summary>
    public interface ILocalDiskFileSystemView : ILocalDiskFileSystemExistenceView
    {
        /// <summary>
        /// Tries to enumerate a directory and track membership.
        /// </summary>
        Possible<PathExistence> TryEnumerateDirectoryAndTrackMembership(AbsolutePath path, Action<string, FileAttributes> handleEntry);

        /// <summary>
        /// Tracks a non-existent relative path chain from a tracked parent root.
        /// If trackedParentPath = 'C:\foo' and relativeAbsentPath = 'a\b\c'
        /// Then the following paths are tracked as absent: 'C:\foo\a', 'C:\foo\a\b', 'C:\foo\a\b\c'.
        /// ADVANCED. Use with care. This should only be used when the parent path is already tracked
        /// and the relative path chain is known to be non-existent
        /// See <see cref="IFileChangeTrackingSubscriptionSource.TrackAbsentRelativePath"/>
        /// </summary>
        bool TrackAbsentPath(AbsolutePath trackedParentPath, AbsolutePath absentChildPath);
    }
}
