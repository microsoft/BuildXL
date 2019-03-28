// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities
{
    /// <summary>
    /// Well-known root cause for an exception. Though a root cause is often <see cref="Unknown" />,
    /// some particular cases such as <see cref="OutOfDiskSpace" /> may be identifiable.
    /// </summary>
    /// <remarks>
    /// Think of exception root-cause as a middle ground between 'handle' and 'crash' ('crash with attribution').
    /// Though it is often preferable to log failures and gracefully continue, there exist hard-to-handle or
    /// inescapably fatal errors (such as disk or memory exhaustion) that are usefully distinguishable from general crashes.
    /// </remarks>
    public enum ExceptionRootCause
    {
        /// <summary>
        /// Something exploded.
        /// </summary>
        Unknown,

        /// <summary>
        /// I/O write failure due to lack of space (<c>ERROR_DISK_FULL</c>)
        /// </summary>
        OutOfDiskSpace,

        /// <summary>
        /// I/O failure due to data error (<c>ERROR_CRC</c>, <c>ERROR_SEEK</c>,
        /// <c>ERROR_SECTOR_NOT_FOUND</c>, <c>ERROR_WRITE_FAULT</c>,
        /// <c>ERROR_READ_FAULT</c>, <c>ERROR_GEN_FAILURE</c>)
        /// </summary>
        DataErrorDriveFailure,

        /// <summary>
        /// The process ran out of memory
        /// </summary>
        OutOfMemory,

        /// <summary>
        /// ERROR_NO_SYSTEMRESOURCES returned.
        /// </summary>
        NoSystemResources,

        /// <summary>
        /// FileLoadException for an assembly. These are usually caused by a broken deployment
        /// </summary>
        MissingRuntimeDependency,

        /// <summary>
        /// StdOut or StdErr are not working anymore. Either they were redirected to pipes and the caller died
        /// or someone killed conhost.exe
        /// </summary>
        ConsoleNotConnected,

        /// <summary>
        /// Cache is potentially corrupted.
        /// </summary>
        CorruptedCache,

        /// <summary>
        /// Application is configured to fail fast for the given exception
        /// </summary>
        FailFast,

        /// <summary>
        /// I/O failure due to a generic device access error
        /// </summary>
        DeviceAccessError,
    }
}
