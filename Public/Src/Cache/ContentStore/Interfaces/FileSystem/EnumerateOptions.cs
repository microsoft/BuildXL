// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem
{
    /// <summary>
    ///     enumerate behavior options
    /// </summary>
    [Flags]
    public enum EnumerateOptions
    {
        /// <summary>
        ///     Not recursive.
        /// </summary>
        None = 0,

        /// <summary>
        ///     Include all matching paths recursively
        /// </summary>
        Recurse = 1
    }
}
