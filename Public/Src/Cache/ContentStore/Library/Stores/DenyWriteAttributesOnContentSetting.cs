// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
