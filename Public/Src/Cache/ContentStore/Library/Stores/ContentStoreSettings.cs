// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using BuildXL.Cache.ContentStore.Tracing.Internal;

#nullable enable

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    /// Configuration object for <see cref="FileSystemContentStore"/> and <see cref="FileSystemContentStoreInternal"/> classes.
    /// </summary>
    public sealed class ContentStoreSettings
    {
        /// <summary>
        /// Whether to check for file existence or just rely on in-memory information.
        /// This shouldn't be necessary but at some point something was messing with our
        /// files behind our back.
        /// </summary>
        public bool CheckFiles { get; set; } = true;

        /// <summary>
        /// Whether the shortcuts for redundant put files are used.
        /// </summary>
        public bool UseRedundantPutFileShortcut { get; set; } = true;

        /// <summary>
        /// Whether the shortcuts for empty files are used.
        /// </summary>
        public bool UseEmptyContentShortcut { get; set; } = true;

        /// <summary>
        /// A timeout for space reservation operation.
        /// </summary>
        public TimeSpan ReserveTimeout { get; set; } = Timeout.InfiniteTimeSpan;

        /// <summary>
        /// Gets or sets whether to override Unix file access modes.
        /// </summary>
        public bool OverrideUnixFileAccessMode { get; set; } = false;

        /// <summary>
        /// Whether to trace diagnostic-level messages emitted by <see cref="FileSystemContentStore"/> and <see cref="FileSystemContentStoreInternal"/> like hashing or placing files.
        /// </summary>
        public bool TraceFileSystemContentStoreDiagnosticMessages { get; set; } = true;

        /// <summary>
        /// If a file system operation takes longer than this threshold it will be traced regardless of other flags.
        /// </summary>
        public TimeSpan SilentOperationDurationThreshold { get; set; } = DefaultTracingConfiguration.DefaultSilentOperationDurationThreshold;

        /// <summary>
        /// Whether to skip touching the content and acquiring a hash lock when PinAsync is called by hibernated session.
        /// </summary>
        public bool SkipTouchAndLockAcquisitionWhenPinningFromHibernation { get; set; } = false;

        /// <nodoc />
        public static ContentStoreSettings DefaultSettings { get; } = new ContentStoreSettings();

        /// <nodoc />
        public SelfCheckSettings? SelfCheckSettings { get; set; }
    }

    /// <nodoc />
    public static class ContentStoreSettingsExtensions
    {
        /// <nodoc />
        public static TimeSpan GetLongOperationDurationThreshold(this ContentStoreSettings? settings)
        {
            return settings?.SilentOperationDurationThreshold ?? DefaultTracingConfiguration.DefaultSilentOperationDurationThreshold;
        }
    }
}
