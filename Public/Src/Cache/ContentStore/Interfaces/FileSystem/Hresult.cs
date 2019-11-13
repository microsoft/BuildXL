// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem
{
    /// <summary>
    ///     HRESULT values returned in exceptions.
    /// </summary>
    public static class Hresult
    {
        /// <summary>
        ///     E_ACCESSDENIED
        /// </summary>
        public const int AccessDenied = unchecked((int)0x80070005);

        /// <summary>
        ///     FILE_EXISTS
        /// </summary>
        public const int FileExists = unchecked((int)0x80070050);
    }
}
