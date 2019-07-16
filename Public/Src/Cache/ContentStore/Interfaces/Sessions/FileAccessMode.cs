// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ContentStore.Interfaces.Sessions
{
    /// <summary>
    ///     Expected usage of file being deployed from content store.
    /// </summary>
    /// <remarks>
    ///     In some cases, a ReadOnly access level allows elision of local copies. Implementations
    ///     may set file ACLs to enforce the requested access level.
    /// </remarks>
    public enum FileAccessMode : byte
    {
        /// <summary>
        ///     Uninitialized
        /// </summary>
        None = 0,

        /// <summary>
        ///     The file will be read, but not written.
        /// </summary>
        ReadOnly,

        /// <summary>
        ///     The file may be written, so an unshared copy is required.
        /// </summary>
        Write
    }
}
