// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
