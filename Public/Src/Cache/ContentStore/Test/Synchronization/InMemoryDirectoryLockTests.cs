// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using Xunit.Abstractions;

namespace ContentStoreTest.Synchronization
{
    public class InMemoryDirectoryLockTests : DirectoryLockTests
    {
        public InMemoryDirectoryLockTests(ITestOutputHelper output)
            : base(() => new MemoryFileSystem(TestSystemClock.Instance), output)
        {
        }
    }
}
