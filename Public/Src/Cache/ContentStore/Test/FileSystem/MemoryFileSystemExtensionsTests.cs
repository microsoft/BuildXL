// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;

namespace ContentStoreTest.FileSystem
{
    public sealed class MemoryFileSystemExtensionsTests : FileSystemExtensionsTests
    {
        public MemoryFileSystemExtensionsTests()
            : base(() => new MemoryFileSystem(TestSystemClock.Instance))
        {
        }
    }
}
