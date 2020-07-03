// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
