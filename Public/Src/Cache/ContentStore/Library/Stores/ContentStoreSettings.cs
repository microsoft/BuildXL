// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        /// Whether to use native (unmanaged) file enumeration or not.
        /// </summary>
        public bool UseNativeBlobEnumeration { get; set; } = false;

        /// <summary>
        /// Gets or sets whether to override Unix file access modes.
        /// </summary>
        public bool OverrideUnixFileAccessMode { get; set; } = false;

        /// <summary>
        /// Whether to trace diagnostic-level messages emitted by <see cref="FileSystemContentStore"/> and <see cref="FileSystemContentStoreInternal"/> like hashing or placing files.
        /// </summary>
        public bool TraceFileSystemContentStoreDiagnosticMessages { get; set; } = false;

        /// <summary>
        /// Whether to skip touching the content and acquiring a hash lock when PinAsync is called by hibernated session.
        /// </summary>
        public bool SkipTouchAndLockAcquisitionWhenPinningFromHibernation { get; set; } = false;

        /// <nodoc />
        public static ContentStoreSettings DefaultSettings { get; } = new ContentStoreSettings();

        /// <nodoc />
        public SelfCheckSettings? SelfCheckSettings { get; set; }
    }
}
