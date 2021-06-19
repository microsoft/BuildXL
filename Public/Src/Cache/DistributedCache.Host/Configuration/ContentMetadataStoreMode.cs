// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.Host.Configuration
{
    using static ContentMetadataStoreModeFlags;

    /// <summary>
    /// Controls which implementation is used for content metadata store
    /// </summary>
    public enum ContentMetadataStoreMode
    {
        /// <nodoc />
        Redis = ReadRedis | WriteRedis,

        /// <nodoc />
        WriteBothReadRedis = WriteBoth | ReadRedis,

        /// <nodoc />
        WriteBothPreferRedis = WriteBoth | ReadBoth | PreferRedis,

        /// <nodoc />
        WriteBothPreferDistributed = WriteBoth | ReadBoth | PreferDistributed,

        /// <nodoc />
        WriteBothReadDistributed = WriteBoth | ReadDistributed,

        /// <nodoc />
        Distributed = ReadDistributed | WriteDistributed,
    }

    public enum ContentMetadataStoreModeFlags
    {
        /// <nodoc />
        ReadRedis = 1 << 0,

        /// <nodoc />
        WriteRedis = 1 << 1,

        /// <nodoc />
        ReadDistributed = 1 << 2,

        /// <nodoc />
        WriteDistributed = 1 << 3,

        /// <nodoc />
        PreferRedis = 1 << 4 | Redis,

        /// <nodoc />
        PreferDistributed = 1 << 5 | Distributed,

        /// <nodoc />
        PreferenceMask = PreferRedis | PreferDistributed,

        /// <nodoc />
        ReadBoth = ReadRedis | ReadDistributed,

        /// <nodoc />
        WriteBoth = WriteRedis | WriteDistributed,

        /// <nodoc />
        Redis = ReadRedis | WriteRedis,

        /// <nodoc />
        Distributed = ReadDistributed | WriteDistributed,

        /// <nodoc />
        All = -1
    }

    public static class ContentMetadataStoreModeExtensions
    {
        public static bool CheckFlag(this ContentMetadataStoreMode mode, ContentMetadataStoreModeFlags flags)
        {
            return mode.Flags().CheckFlag(flags);
        }

        public static bool CheckFlag(this ContentMetadataStoreModeFlags mode, ContentMetadataStoreModeFlags flags)
        {
            return (mode & flags) == flags;
        }

        public static ContentMetadataStoreMode Mask(this ContentMetadataStoreMode mode, ContentMetadataStoreModeFlags? mask)
        {
            return (ContentMetadataStoreMode)mode.MaskFlags(mask);
        }

        public static ContentMetadataStoreModeFlags MaskFlags(this ContentMetadataStoreMode mode, ContentMetadataStoreModeFlags? mask)
        {
            var maskFlags = mask ?? ContentMetadataStoreModeFlags.All;
            return mode.Flags() & maskFlags;
        }

        public static ContentMetadataStoreModeFlags Subtract(this ContentMetadataStoreModeFlags mode, ContentMetadataStoreModeFlags mask)
        {
            return mode & (~mask);
        }

        public static ContentMetadataStoreModeFlags Flags(this ContentMetadataStoreMode mode)
        {
            return (ContentMetadataStoreModeFlags)mode;
        }
    }
}
