// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     Method for protecting content files.
    /// </summary>
    public enum DenyWriteAttributesOnContentSetting
    {
        /// <summary>
        ///     Uninitialized
        /// </summary>
        None = 0,

        /// <summary>
        ///     Content attributes to prevent writes are applied to content files.
        /// </summary>
        Enable = 1,

        /// <summary>
        ///     Content attributes to allow writes are applied to content files.
        /// </summary>
        Disable = 2
    }
}
