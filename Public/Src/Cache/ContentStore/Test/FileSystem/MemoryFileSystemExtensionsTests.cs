// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
