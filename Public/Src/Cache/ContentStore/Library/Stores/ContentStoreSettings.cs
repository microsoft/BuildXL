// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

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
        /// Whether the shortcuts for streaming, placing, and pinning the empty file are used.
        /// </summary>
        public bool UseEmptyFileHashShortcut { get; set; } = true;

        /// <summary>
        /// Whether to use native (unmanaged) file enumeration or not.
        /// </summary>
        public bool UseNativeBlobEnumeration { get; set; } = false;

        /// <summary>
        /// Whether to use old (original) implementation of <see cref="LegacyQuotaKeeper"/> or switch to <see cref="QuotaKeeperV2"/>.
        /// </summary>
        /// <remarks>
        /// This flag should go away after the validation of the new logic.
        /// </remarks>
        public bool UseLegacyQuotaKeeperImplementation { get; set; } = true;

        /// <summary>
        /// If true, then quota keeper will check the current content directory size and start content eviction at startup if the threshold is reached.
        /// </summary>
        public bool StartPurgingAtStartup { get; set; } = true;

        /// <summary>
        /// If true, then <see cref="FileSystemContentStoreInternal"/> will start a self-check to validate that the content in cache is valid at startup.
        /// </summary>
        /// <remarks>
        /// If the property is false, then the self check is still possible but <see cref="FileSystemContentStoreInternal.SelfCheckContentDirectoryAsync(Interfaces.Tracing.Context, System.Threading.CancellationToken)"/>
        /// method should be called manually.
        /// </remarks>
        public bool StartSelfCheckInStartup { get; set; } = false;

        /// <summary>
        /// An interval between self checks performed by a content store to make sure that all the data on disk matches it's hashes.
        /// </summary>
        public TimeSpan SelfCheckFrequency { get; set; } = TimeSpan.FromDays(1);

        /// <summary>
        /// An epoch used for reseting self check of a content directory.
        /// </summary>
        public string SelfCheckEpoch { get; set; } = "E0";

        /// <summary>
        /// An interval for tracing self check progress.
        /// </summary>
        public TimeSpan SelfCheckProgressReportingInterval { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// A number of invalid hashes that the checker will process in one attempt.
        /// </summary>
        /// <remarks>
        /// Used for testing purposes.
        /// </remarks>
        public long SelfCheckInvalidFilesLimit { get; set; } = long.MaxValue;

        /// <summary>
        /// Gets or sets whether to override Unix file access modes.
        /// </summary>
        public bool OverrideUnixFileAccessMode { get; set; } = false;

        /// <nodoc />
        public static ContentStoreSettings DefaultSettings { get; } = new ContentStoreSettings();
    }
}
