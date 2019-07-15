// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Cache.ContentStore.Interfaces.Stores
{
    /// <summary>
    ///     Session options for pinning behavior.
    /// </summary>
    [Flags]
    public enum ImplicitPin
    {
        /// <summary>
        ///     Do not ever implicitly pin.
        /// </summary>
        None = 0,

        /// <summary>
        ///     Implicitly pin on puts.
        /// </summary>
        Put = 1,

        /// <summary>
        ///     Implicitly pin on gets.
        /// </summary>
        Get = 2,

        /// <summary>
        ///     Implicitly pin on puts and gets.
        /// </summary>
        PutAndGet = 3
    }
}
