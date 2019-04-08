// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem
{
    /// <summary>
    ///     Delete behavior options
    /// </summary>
    [Flags]
    public enum DeleteOptions
    {
        /// <summary>
        ///     Convenience for specifying no options.
        /// </summary>
        None = 0,

        /// <summary>
        ///     Delete all subdirectories
        /// </summary>
        Recurse,

        /// <summary>
        ///     Delete read-only too
        /// </summary>
        ReadOnly,

        /// <summary>
        ///     Convenience option where all are enabled.
        /// </summary>
        All = Recurse | ReadOnly
    }
}
