// --------------------------------------------------------------------
//  
// Copyright (c) Microsoft Corporation.  All rights reserved.
//  
// --------------------------------------------------------------------

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

        /// <nodoc />
        public static ContentStoreSettings DefaultSettings { get; } = new ContentStoreSettings();
    }
}
