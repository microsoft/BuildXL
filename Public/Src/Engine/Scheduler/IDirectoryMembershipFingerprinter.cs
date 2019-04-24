// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Native.IO;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Computes a <see cref="DirectoryFingerprint" /> for any directory path
    /// </summary>
    public interface IDirectoryMembershipFingerprinter
    {
        /// <summary>
        /// Attempts to compute a directory fingerprint for the given path. The caller provides the implementation of
        /// directory enumeration which may or may not result in real filesystem access. If directory fingerprint computation
        /// cannot proceed (possible due to e.g. filesystem permissions), an error is logged and <c>null</c> is returned.
        /// </summary>
        DirectoryFingerprint? TryComputeDirectoryFingerprint(
            AbsolutePath directoryPath,
            CacheablePipInfo cachePipInfo,
            Func<EnumerationRequest, PathExistence?> tryEnumerateDirectory,
            bool cacheableFingerprint,
            DirectoryMembershipFingerprinterRule rule,
            DirectoryMembershipHashedEventData eventData);
    }
}
