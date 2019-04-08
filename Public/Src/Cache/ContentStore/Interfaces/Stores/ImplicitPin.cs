// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ContentStore.Interfaces.Stores
{
    /// <summary>
    ///     Session options for pinning behavior.
    /// </summary>
    public enum ImplicitPin
    {
        /// <summary>
        ///     Do not ever implicitly pin.
        /// </summary>
        None = 0,

        /// <summary>
        ///     Implicitly pin on gets and puts.
        /// </summary>
        PutAndGet
    }
}
