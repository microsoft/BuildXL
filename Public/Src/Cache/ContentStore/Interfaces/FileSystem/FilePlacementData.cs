// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;

namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem
{
    /// <summary>
    /// Defines data for placing in VFS
    /// </summary>
    public readonly struct FilePlacementData
    {
        public readonly ContentHash Hash;
        public readonly FileRealizationMode RealizationMode;
        public readonly FileAccessMode AccessMode;

        public FilePlacementData(ContentHash hash, FileRealizationMode realizationMode, FileAccessMode accessMode)
        {
            Hash = hash;
            RealizationMode = realizationMode;
            AccessMode = accessMode;
        }
    }
}
