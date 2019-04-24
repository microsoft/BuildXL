// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Directory enumeration request
    /// </summary>
    public readonly struct EnumerationRequest
    {
        /// <summary>
        /// Cached directory contents to improve the performance
        /// </summary>
        public readonly ObjectCache<AbsolutePath, Lazy<DirectoryEnumerationResult>> CachedDirectoryContents;

        /// <summary>
        /// Directory path to be enumerated
        /// </summary>
        public readonly AbsolutePath DirectoryPath;

        /// <summary>
        /// Pip info
        /// </summary>
        public readonly CacheablePipInfo PipInfo;

        /// <summary>
        /// Action to handle each directory member
        /// </summary>
        public readonly Action<AbsolutePath, string> HandleEntry;

        /// <summary>
        /// Constructor
        /// </summary>
        public EnumerationRequest(
            ObjectCache<AbsolutePath, Lazy<DirectoryEnumerationResult>> cachedDirectoryContents,
            AbsolutePath directoryPath,
            CacheablePipInfo pipInfo,
            Action<AbsolutePath, string> handleEntry)
        {
            Contract.Requires(cachedDirectoryContents != null);
            Contract.Requires(handleEntry != null);
            Contract.Requires(pipInfo != null);
            Contract.Requires(directoryPath.IsValid);

            CachedDirectoryContents = cachedDirectoryContents;
            DirectoryPath = directoryPath;
            PipInfo = pipInfo;
            HandleEntry = handleEntry;
        }
    }
}
