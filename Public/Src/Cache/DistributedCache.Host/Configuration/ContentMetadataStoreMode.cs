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
        WriteBothPreferRedis = WriteBoth | PreferRedis,

        /// <nodoc />
        WriteBothPreferDistributed = WriteBoth | PreferDistributed,

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
        PreferRedis = 1 << 4 | ReadBoth,

        /// <nodoc />
        PreferDistributed = 1 << 5 | ReadBoth,

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
}
